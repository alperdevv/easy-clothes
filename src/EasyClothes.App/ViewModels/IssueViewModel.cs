using EasyClothes.Core.Validation;

namespace EasyClothes.App.ViewModels;

public sealed record IssueViewModel(ValidationIssue Issue)
{
    public string Message => Issue.Message;
    public bool IsError => Issue.Severity == IssueSeverity.Error;
    public bool IsWarning => Issue.Severity == IssueSeverity.Warning;
    public bool IsInfo => Issue.Severity == IssueSeverity.Info;
    public bool HasFix => Issue.Fix is not null;
    public string FixLabel => Issue.Fix?.Label ?? "";

    public string Icon => Issue.Severity switch
    {
        IssueSeverity.Error => "Icon.CircleX",
        IssueSeverity.Warning => "Icon.TriangleAlert",
        _ => "Icon.Info",
    };
}
