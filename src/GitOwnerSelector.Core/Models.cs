namespace GitOwnerSelector.Core;

public sealed record Account(string Id, string Label, string Username, string Name, string Email)
{
    public override string ToString() => $"{Label} · @{Username}";
}
public sealed record FolderRule(string Id, string Folder, bool Recursive, string AccountId, string GitDirectory = "");
public sealed class Settings
{
    public List<Account> Accounts { get; set; } = [];
    public List<FolderRule> Rules { get; set; } = [];
}
public sealed record GitResult(int ExitCode, string Output, string Error);
