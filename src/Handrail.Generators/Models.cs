using Microsoft.CodeAnalysis.Text;

namespace Handrail.Generators;

internal sealed record DiagInfo(string Id, string Message, string FilePath, TextSpan Span, LinePositionSpan LineSpan);

internal sealed record AuthInfo(string? Policy, string? Roles, string? Schemes);

internal sealed record ParamModel(
    string JsonName,
    string CsName,
    string TypeFqn,
    bool IsCancellationToken,
    bool Required,
    string? DefaultExpr,
    bool HasRange,
    double RangeMin,
    double RangeMax,
    int MinLength,
    int MaxLength);

internal sealed record ToolModel(
    string TypeFqn,
    string MethodName,
    bool IsStatic,
    string Name,
    string Title,
    string Description,
    bool ReadOnly,
    bool Consequential,
    bool ReturnsUntrusted,
    int Confirm,
    bool AllowUnconfirmedWrite,
    int Scope,
    string InputSchema,
    string? OutputSchema,
    EquatableArray<ParamModel> Params,
    bool ReturnsValue,
    bool IsAwaitable,
    EquatableArray<AuthInfo> Auth,
    bool AllowAnonymous,
    EquatableArray<DiagInfo> Diagnostics);
