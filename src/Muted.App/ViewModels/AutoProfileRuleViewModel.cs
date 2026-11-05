using Muted.Core.Settings;

namespace Muted.App.ViewModels;

internal sealed record AutoProfileRuleViewModel(string ProcessName, string ProfileId, string ProfileName)
{
    public string Description => $"{ProcessName}  →  {ProfileName}";

    public AutoProfileRule ToRule() => new() { ProcessName = ProcessName, ProfileId = ProfileId };
}
