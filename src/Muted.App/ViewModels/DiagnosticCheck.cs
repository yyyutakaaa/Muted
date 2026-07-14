namespace Muted.App.ViewModels;

internal enum DiagnosticSeverity
{
    Passed,
    Warning,
    Failed
}

internal sealed record DiagnosticCheck(
    string Title,
    string Detail,
    DiagnosticSeverity Severity);
