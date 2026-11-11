; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
WM0001 | Handrail.Security | Error | Write tool without confirmation
WM0005 | Handrail.Security | Warning | Read-only tool that looks like a write
WM0010 | Handrail.Security | Warning | Write tool runs without confirmation
WM0020 | Handrail.Security | Warning | Tool returns user-generated content
WM0030 | Handrail.Security | Warning | Secret as tool input
