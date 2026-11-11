using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Handrail.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class WebMcpToolGenerator : IIncrementalGenerator
{
    private const string AttributeName = "Handrail.WebMcpToolAttribute";

    private static readonly SymbolDisplayFormat Fqn = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var tools = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeName,
                static (node, _) => node is MethodDeclarationSyntax,
                static (ctx, ct) => Read(ctx, ct))
            .WithTrackingName("tools")
            .Collect();

        var assembly = context.CompilationProvider.Select(static (c, _) => c.AssemblyName);

        context.RegisterSourceOutput(tools.Combine(assembly), static (spc, pair) => Emit(spc, pair.Left, pair.Right));
    }

    private static ToolModel Read(GeneratorAttributeSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        var method = (IMethodSymbol)ctx.TargetSymbol;
        var syntax = (MethodDeclarationSyntax)ctx.TargetNode;
        var attribute = ctx.Attributes[0];
        var diags = new List<DiagInfo>();
        var location = syntax.Identifier.GetLocation();

        void Report(string id, string message, Location? at = null)
        {
            var loc = at ?? location;
            var line = loc.GetLineSpan();
            diags.Add(new DiagInfo(id, message, line.Path, loc.SourceSpan, line.Span));
        }

        string? ctorName = attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;
        T Named<T>(string key, T fallback) =>
            attribute.NamedArguments.Where(n => n.Key == key).Select(n => n.Value.Value).OfType<T>().DefaultIfEmpty(fallback).First();

        var name = ctorName ?? Names.SnakeCase(method.Name);
        if (!Names.IsValidToolName(name))
        {
            Report("WM0002", $"Tool name '{name}' must be 1-128 characters of A-Z a-z 0-9 _ . -.");
        }

        if (method.IsGenericMethod || method.ContainingType.IsGenericType || !IsAccessible(method))
        {
            Report("WM0007", $"Method '{method.Name}' must be public or internal, non-generic, in a non-generic type that is not nested in a private type.");
        }

        var description = Named<string>("Description", null!) ?? XmlSummary(syntax);
        if (string.IsNullOrWhiteSpace(description))
        {
            description = string.Empty;
            Report("WM0006", $"Tool '{name}' has no description. Agents pick tools by description: add an XML summary or Description = \"...\".");
        }

        var title = Named<string>("Title", null!) ?? Names.Humanize(name);

        // Parameters become the input schema; the token for cancellation is passed through, not exposed.
        var parameters = new List<ParamModel>();
        var properties = new List<string>();
        var required = new List<string>();
        foreach (var p in method.Parameters)
        {
            if (p.Type.ToDisplayString() == "System.Threading.CancellationToken")
            {
                parameters.Add(new ParamModel(string.Empty, p.Name, string.Empty, true, false, null, false, 0, 0, 0, 0));
                continue;
            }

            var builder = new SchemaBuilder(forOutput: false);
            var schema = builder.Build(p.Type, $"Parameter '{p.Name}' of '{method.Name}'");
            foreach (var problem in builder.Problems)
            {
                Report("WM0004", problem, p.Locations.FirstOrDefault());
            }

            if (schema is null)
            {
                continue;
            }

            SchemaBuilder.ApplyConstraints(p, schema);
            var hasDefault = p.HasExplicitDefaultValue;
            var defaultExpr = hasDefault ? DefaultLiteral(p) : null;
            if (hasDefault && DefaultJson(p) is { } json)
            {
                schema.Add(new KeyValuePair<string, string>("default", json));
            }

            var jsonName = Names.CamelCase(p.Name);
            var isRequired = !hasDefault && !SchemaBuilder.IsNullable(p.Type) ||
                p.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.ComponentModel.DataAnnotations.RequiredAttribute");
            properties.Add(Names.Json(jsonName) + ":" + SchemaBuilder.Render(schema));
            if (isRequired)
            {
                required.Add(Names.Json(jsonName));
            }

            var (hasRange, rangeMin, rangeMax) = Range(p, schema);
            var (minLen, maxLen) = Lengths(p, schema);
            parameters.Add(new ParamModel(jsonName, p.Name, p.Type.ToDisplayString(Fqn), false, isRequired, defaultExpr,
                hasRange, rangeMin, rangeMax, minLen, maxLen));
        }

        var inputSchema = "{\"type\":\"object\",\"properties\":{" + string.Join(",", properties) + "}"
            + (required.Count > 0 ? ",\"required\":[" + string.Join(",", required) + "]" : string.Empty)
            + ",\"additionalProperties\":false}";

        // Return type: unwrap Task/ValueTask; only object results get an outputSchema.
        var returnType = method.ReturnType;
        var returnsValue = returnType.SpecialType != SpecialType.System_Void;
        var isAwaitable = false;
        if (returnType is INamedTypeSymbol rt)
        {
            var def = rt.OriginalDefinition.ToDisplayString();
            if (def is "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask<TResult>")
            {
                returnType = rt.TypeArguments[0];
                isAwaitable = true;
            }
            else if (def is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask")
            {
                returnsValue = false;
                isAwaitable = true;
            }
        }

        string? outputSchema = null;
        if (returnsValue)
        {
            var outBuilder = new SchemaBuilder(forOutput: true);
            var outSchema = outBuilder.Build(returnType, "return");
            if (outSchema is not null && outSchema.Any(m => m.Key == "type" && m.Value == "\"object\"") && outSchema.Any(m => m.Key == "properties"))
            {
                outputSchema = SchemaBuilder.Render(outSchema);
            }
        }

        var auth = new List<AuthInfo>();
        foreach (var source in method.GetAttributes().Concat(method.ContainingType.GetAttributes()))
        {
            if (source.AttributeClass?.ToDisplayString() == "Microsoft.AspNetCore.Authorization.AuthorizeAttribute")
            {
                string? Arg(string key) => source.NamedArguments.Where(n => n.Key == key).Select(n => n.Value.Value as string).FirstOrDefault();
                var policy = source.ConstructorArguments.Length > 0 ? source.ConstructorArguments[0].Value as string : null;
                auth.Add(new AuthInfo(policy ?? Arg("Policy"), Arg("Roles"), Arg("AuthenticationSchemes")));
            }
        }

        var allowAnonymous = method.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute");

        return new ToolModel(
            method.ContainingType.ToDisplayString(Fqn),
            method.Name,
            method.IsStatic,
            name,
            title,
            description!,
            Named("ReadOnly", false),
            Named("Consequential", false),
            Named("ReturnsUntrusted", false),
            Named("Confirm", 0),
            Named("AllowUnconfirmedWrite", false),
            Named("Scope", 0),
            inputSchema,
            outputSchema,
            new EquatableArray<ParamModel>(parameters),
            returnsValue,
            isAwaitable,
            new EquatableArray<AuthInfo>(auth),
            allowAnonymous,
            new EquatableArray<DiagInfo>(diags));
    }

    private static bool IsAccessible(IMethodSymbol method)
    {
        for (ISymbol? s = method; s is not null && s is not INamespaceSymbol; s = s.ContainingSymbol)
        {
            if (s.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
            {
                return false;
            }
        }

        return true;
    }

    private static (bool, double, double) Range(IParameterSymbol p, List<KeyValuePair<string, string>> schema)
    {
        var min = schema.FirstOrDefault(m => m.Key == "minimum").Value;
        var max = schema.FirstOrDefault(m => m.Key == "maximum").Value;
        if (min is not null && max is not null
            && double.TryParse(min, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lo)
            && double.TryParse(max, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hi))
        {
            return (true, lo, hi);
        }

        return (false, 0, 0);
    }

    private static (int, int) Lengths(IParameterSymbol p, List<KeyValuePair<string, string>> schema)
    {
        int Get(string key, int fallback) =>
            int.TryParse(schema.FirstOrDefault(m => m.Key == key).Value, out var v) ? v : fallback;

        return p.Type.SpecialType == SpecialType.System_String ? (Get("minLength", 0), Get("maxLength", int.MaxValue)) : (0, int.MaxValue);
    }

    private static string? DefaultLiteral(IParameterSymbol p)
    {
        var value = p.ExplicitDefaultValue;
        var type = p.Type is INamedTypeSymbol n && n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? n.TypeArguments[0] : p.Type;
        if (value is null)
        {
            return "default";
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return $"({type.ToDisplayString(Fqn)}){SymbolDisplay.FormatPrimitive(value, false, false)}";
        }

        return SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false);
    }

    private static string? DefaultJson(IParameterSymbol p)
    {
        var value = p.ExplicitDefaultValue;
        var type = p.Type is INamedTypeSymbol n && n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? n.TypeArguments[0] : p.Type;
        return value switch
        {
            null => null,
            bool b => b ? "true" : "false",
            string s => Names.Json(s),
            _ when type.TypeKind == TypeKind.Enum => SchemaBuilder.EnumName(type, value) is { } e ? Names.Json(e) : null,
            IConvertible c when type.SpecialType is >= SpecialType.System_SByte and <= SpecialType.System_Double
                => SchemaBuilder.Number(c.ToDouble(System.Globalization.CultureInfo.InvariantCulture)),
            _ => null,
        };
    }

    /// <summary>Reads the summary from the raw /// comment text, so it works whether or not documentation parsing is on.</summary>
    private static string XmlSummary(MethodDeclarationSyntax syntax)
    {
        var lines = syntax.GetLeadingTrivia()
            .Where(t => t.Kind() is SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia)
            .SelectMany(t => t.ToFullString().Split('\n'))
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("///", StringComparison.Ordinal))
            .Select(l => l.Substring(3));

        var text = string.Join(" ", lines);
        var match = Regex.Match(text, "<summary>(.*?)</summary>", RegexOptions.Singleline);
        if (!match.Success)
        {
            return string.Empty;
        }

        var inner = match.Groups[1].Value;
        inner = Regex.Replace(inner, "<(?:see|seealso)\\s+cref=\"([^\"]+)\"\\s*/>", m => m.Groups[1].Value.Split('.').Last().TrimStart('!', ':'));
        inner = Regex.Replace(inner, "<paramref\\s+name=\"([^\"]+)\"\\s*/>", "$1");
        inner = Regex.Replace(inner, "<[^>]+>", string.Empty);
        inner = System.Net.WebUtility.HtmlDecode(inner);
        return Regex.Replace(inner, "\\s+", " ").Trim();
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<ToolModel> models, string? assemblyName)
    {
        var ordered = models.OrderBy(m => m.TypeFqn, StringComparer.Ordinal).ThenBy(m => m.MethodName, StringComparer.Ordinal).ToList();
        var valid = new List<ToolModel>();
        var seen = new Dictionary<string, ToolModel>(StringComparer.Ordinal);

        foreach (var model in ordered)
        {
            var failed = false;
            foreach (var d in model.Diagnostics)
            {
                var descriptor = GeneratorDiagnostics.ById(d.Id);
                spc.ReportDiagnostic(Diagnostic.Create(descriptor, Location.Create(d.FilePath, d.Span, d.LineSpan), d.Message));
                failed |= descriptor.DefaultSeverity == DiagnosticSeverity.Error;
            }

            if (!failed && seen.TryGetValue(model.Name, out var first))
            {
                spc.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.DuplicateName, Location.None,
                    $"Tool name '{model.Name}' is used by '{first.TypeFqn}.{first.MethodName}' and '{model.TypeFqn}.{model.MethodName}'."));
                failed = true;
            }

            if (!failed)
            {
                seen[model.Name] = model;
                valid.Add(model);
            }
        }

        if (valid.Count == 0)
        {
            return;
        }

        var ns = "Handrail.Generated." + Names.Sanitize(assemblyName);
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS1998, CS0618, CS8600, CS8601, CS8602, CS8604"); // the invoker is async in every shape, and not every shape awaits
        sb.AppendLine($"[assembly: global::Handrail.WebMcpToolSet(typeof(global::{ns}.WebMcpToolSet))]");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("[global::System.CodeDom.Compiler.GeneratedCode(\"Handrail.Generators\", \"0.1.0\")]");
        sb.AppendLine("internal sealed class WebMcpToolSet : global::Handrail.IWebMcpToolSet");
        sb.AppendLine("{");
        sb.AppendLine("    public global::System.Collections.Generic.IReadOnlyList<global::Handrail.WebMcpToolDefinition> Tools { get; } = new global::Handrail.WebMcpToolDefinition[]");
        sb.AppendLine("    {");
        foreach (var tool in valid)
        {
            EmitTool(sb, tool);
        }

        sb.AppendLine("    };");
        sb.AppendLine("}");
        spc.AddSource("Handrail.WebMcpToolSet.g.cs", sb.ToString());
    }

    private static string Lit(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    private static string B(bool value) => value ? "true" : "false";

    private static void EmitTool(StringBuilder sb, ToolModel t)
    {
        sb.AppendLine("        new()");
        sb.AppendLine("        {");
        sb.AppendLine($"            Name = {Lit(t.Name)},");
        sb.AppendLine($"            Title = {Lit(t.Title)},");
        sb.AppendLine($"            Description = {Lit(t.Description)},");
        sb.AppendLine($"            InputSchema = {Lit(t.InputSchema)},");
        if (t.OutputSchema is not null)
        {
            sb.AppendLine($"            OutputSchema = {Lit(t.OutputSchema)},");
        }

        sb.AppendLine($"            ReadOnly = {B(t.ReadOnly)},");
        sb.AppendLine($"            Consequential = {B(t.Consequential)},");
        sb.AppendLine($"            ReturnsUntrusted = {B(t.ReturnsUntrusted)},");
        sb.AppendLine($"            Confirm = (global::Handrail.Confirm){t.Confirm},");
        sb.AppendLine($"            AllowUnconfirmedWrite = {B(t.AllowUnconfirmedWrite)},");
        sb.AppendLine($"            Scope = (global::Handrail.WebMcpScope){t.Scope},");
        sb.AppendLine($"            DeclaringType = typeof({t.TypeFqn}),");
        sb.AppendLine($"            AllowAnonymous = {B(t.AllowAnonymous)},");
        if (t.Auth.Any())
        {
            sb.AppendLine("            Authorize = new global::Handrail.WebMcpAuthorizeRule[]");
            sb.AppendLine("            {");
            foreach (var a in t.Auth)
            {
                string Opt(string? s) => s is null ? "null" : Lit(s);
                sb.AppendLine($"                new global::Handrail.WebMcpAuthorizeRule({Opt(a.Policy)}, {Opt(a.Roles)}, {Opt(a.Schemes)}),");
            }

            sb.AppendLine("            },");
        }

        sb.AppendLine("            Invoke = static async (services, args, cancellationToken) =>");
        sb.AppendLine("            {");
        var names = string.Join(", ", t.Params.Where(p => !p.IsCancellationToken).Select(p => Lit(p.JsonName)));
        sb.AppendLine($"                global::Handrail.WebMcpInput.RejectUnknown(args{(names.Length > 0 ? ", " + names : string.Empty)});");
        foreach (var p in t.Params.Where(p => !p.IsCancellationToken))
        {
            var read = p.Required
                ? $"global::Handrail.WebMcpInput.Required<{p.TypeFqn}>(args, {Lit(p.JsonName)})"
                : $"global::Handrail.WebMcpInput.Optional<{p.TypeFqn}>(args, {Lit(p.JsonName)}, {p.DefaultExpr ?? "default"})";
            sb.AppendLine($"                var {Local(p)} = {read};");
            if (p.HasRange)
            {
                sb.AppendLine($"                global::Handrail.WebMcpInput.CheckRange({Lit(p.JsonName)}, {Local(p)}, {SchemaBuilder.Number(p.RangeMin)}D, {SchemaBuilder.Number(p.RangeMax)}D);");
            }

            if (p.MinLength > 0 || p.MaxLength != int.MaxValue)
            {
                sb.AppendLine($"                global::Handrail.WebMcpInput.CheckLength({Lit(p.JsonName)}, {Local(p)}, {p.MinLength}, {p.MaxLength});");
            }
        }

        var target = t.IsStatic
            ? t.TypeFqn
            : "((" + t.TypeFqn + ")(services.GetService(typeof(" + t.TypeFqn + ")) ?? throw new global::System.InvalidOperationException(\"Tool type '" + t.TypeFqn.Replace("global::", string.Empty) + "' is not registered in the service provider.\")))";
        var call = target + "." + t.MethodName + "(" + string.Join(", ", t.Params.Select(p => p.IsCancellationToken ? "cancellationToken" : Local(p))) + ")";
        var await = t.IsAwaitable ? "await " : string.Empty;
        if (t.ReturnsValue)
        {
            sb.AppendLine($"                var result = {await}{call};");
            sb.AppendLine("                return global::Handrail.WebMcpJson.Serialize(result);");
        }
        else
        {
            sb.AppendLine($"                {await}{call};");
            sb.AppendLine("                return global::Handrail.WebMcpJson.Ok;");
        }

        sb.AppendLine("            },");
        sb.AppendLine("        },");
    }

    private static string Local(ParamModel p) => "@" + p.CsName;
}
