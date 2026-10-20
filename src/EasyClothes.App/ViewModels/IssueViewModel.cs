using EasyClothes.Core.Validation;

namespace EasyClothes.App.ViewModels;

public sealed record IssueViewModel(ValidationIssue Issue)
{
    public string Message => Issue.Message;
    public bool IsError => Issue.Severity == IssueSeverity.Error;
    public string Icon => IsError ? "✕" : "!";
}
