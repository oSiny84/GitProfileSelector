using GitOwnerSelector.Core;

var root = Path.GetFullPath(Path.Combine(".test-work", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var global = Path.Combine(root, "global.gitconfig");
await File.WriteAllTextAsync(global, "[user]\n name = Original\n email = original@example.com\n[alias]\n st = status\n");
var environment = new Dictionary<string, string?>
{
    ["GIT_CONFIG_GLOBAL"] = global, ["GIT_CONFIG_NOSYSTEM"] = "1", ["GIT_CONFIG_COUNT"] = "0",
    ["GIT_DIR"] = null, ["GIT_WORK_TREE"] = null, ["GIT_COMMON_DIR"] = null,
    ["GIT_CONFIG"] = null, ["GIT_CONFIG_PARAMETERS"] = null,
    ["HOME"] = root, ["XDG_CONFIG_HOME"] = Path.Combine(root, "xdg")
};
var git = new Git(environment);
var service = new ConfigurationService(git, Path.Combine(root, "앱 설정"));
var settings = new Settings();
var work = new Account(Guid.NewGuid().ToString("N"), "회사", "company-user", "회사 개발자", "company@example.com");
var personal = new Account(Guid.NewGuid().ToString("N"), "개인", "personal-user", "개인 \"이름\"", "personal@example.com");
settings.Accounts.AddRange([work, personal]);
var parent = Path.Combine(root, "회사 프로젝트");
var repo = Path.Combine(parent, "repo");
var exception = Path.Combine(parent, "개인", "repo");
var outside = Path.Combine(root, "outside");
var sibling = Path.Combine(root, "회사 프로젝트-other", "repo");
foreach (var folder in new[] { repo, exception, outside, sibling })
{
    Directory.CreateDirectory(folder);
    Git.Require(await git.Run("init", folder));
}
var assertions = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    assertions++;
    Console.WriteLine("PASS: " + label);
}
async Task<string> Get(string folder, string key) => Git.Require(await git.Run("-C", folder, "config", "--get", key));
async Task<string> Credential(string folder) => Git.Require(await git.Run("-C", folder, "config", "--get-urlmatch", "credential.username", "https://github.com/owner/repo.git"));

settings.Rules.Add(await service.CreateRule(parent, true, work.Id));
settings.Rules.Add(await service.CreateRule(Path.Combine(parent, "개인"), true, personal.Id));
await service.Save(settings);
Check(await Get(repo, "user.name") == work.Name, "Korean path and commit author");
Check(await Credential(repo) == work.Username, "Company GitHub credential default");
Check(await Get(exception, "user.name") == personal.Name, "Nested rule priority and quoted name");
Check(await Credential(exception) == personal.Username, "Personal credential in nested folder");
Check(await Get(outside, "user.name") == "Original", "Unrelated repository unchanged");
Check(await Get(sibling, "user.name") == "Original", "Similar folder prefix does not match");
Check(await Get(repo, "alias.st") == "status", "Existing global configuration preserved");
Check(await service.IsEnabled(), "Managed include enabled");
await service.Save(settings);
var includes = Git.Require(await git.Run("config", "--global", "--no-includes", "--get-all", "include.path"));
Check(includes.Split('\n').Length == 1, "Repeated apply does not duplicate global include");
var exact = await service.CreateRule(repo, false, personal.Id);
settings.Rules.Add(exact);
await service.Save(settings);
Check(await Credential(repo) == personal.Username, "Exact repository rule overrides parent folder");
var child = Path.Combine(repo, "child");
Directory.CreateDirectory(child);
Git.Require(await git.Run("init", child));
Check(await Credential(child) == work.Username, "Exact rule does not affect nested repository");
Git.Require(await git.Run("-C", repo, "config", "user.email", "local@example.com"));
Git.Require(await git.Run("-C", repo, "remote", "add", "origin", "git@github.com:owner/repo.git"));
var inspection = await service.Inspect(repo);
Check(inspection.Contains("local@example.com") && inspection.Contains("저장소 자체"), "Local override surfaced");
Check(inspection.Contains("HTTPS github.com 주소가 아닙니다"), "SSH unsupported warning");
Git.Require(await git.Run("-C", repo, "remote", "set-url", "origin", "https://other-user:secret@github.com/owner/repo.git"));
inspection = await service.Inspect(repo);
Check(inspection.Contains("remote URL에 인증 정보") && !inspection.Contains("secret"), "URL identity conflict detected without exposing secret");
Git.Require(await git.Run("-C", repo, "remote", "set-url", "origin", "https://github.com/owner/repo.git"));
Git.Require(await git.Run("-C", repo, "config", "credential.https://github.com/owner/repo.git.username", "per-url-user"));
inspection = await service.Inspect(repo);
Check(inspection.Contains("주소별 기본 계정: per-url-user"), "More specific credential override surfaced");
settings.Rules.Remove(exact);
await service.Save(settings);
Check(await Get(repo, "user.name") == work.Name, "Rule removal restores parent identity");
await service.Disable();
Check(!await service.IsEnabled(), "Managed include removed");
Check(await Get(repo, "user.name") == "Original", "Disable restores original default");
Check(await Get(repo, "user.email") == "local@example.com", "Disable preserves repository-local configuration");
await service.Save(settings, enable: false);
Check(!await service.IsEnabled(), "Profile-only save does not re-enable disabled rules");
Check(service.Load().Rules.Count == 2, "Rules retained after disabling");
Check(Directory.GetFiles(Path.Combine(service.DataDirectory, "backups")).Length > 0, "Backups created");
bool invalid = false;
try { ConfigurationService.ValidateAccount(work with { Username = "bad\n[credential]" }); }
catch (InvalidOperationException) { invalid = true; }
Check(invalid, "Config injection rejected");
invalid = false;
try { await service.CreateRule(parent, false, work.Id); }
catch (InvalidOperationException) { invalid = true; }
Check(invalid, "Exact mode rejects non-repository folders");
var oldSettings = File.ReadAllText(Path.Combine(service.DataDirectory, "settings.json"));
await File.WriteAllTextAsync(global + ".lock", "test lock");
try
{
    settings.Rules.Clear();
    settings.Rules.Add(await service.CreateRule(parent, true, personal.Id));
    bool failed = false;
    try { await service.Save(settings); } catch (InvalidOperationException) { failed = true; }
    Check(failed, "Locked global configuration reports failure");
    Check(File.ReadAllText(Path.Combine(service.DataDirectory, "settings.json")) == oldSettings, "Failed apply rolls back application settings");
}
finally { File.Delete(global + ".lock"); }
Console.WriteLine($"\n{assertions} checks passed. Isolated artifacts: {root}");
