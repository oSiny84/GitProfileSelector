using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GitOwnerSelector.Core;

public sealed class ConfigurationService
{
    private readonly Git git;
    public string DataDirectory { get; }
    public string ManagedFile => Path.Combine(DataDirectory, "folders.gitconfig").Replace('\\', '/');
    private string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public ConfigurationService(Git git, string? dataDirectory = null)
    {
        this.git = git;
        DataDirectory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitOwnerSelector");
    }

    public Settings Load() => File.Exists(SettingsFile)
        ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile)) ?? throw new InvalidOperationException("설정 파일을 읽을 수 없습니다.")
        : new();

    public static void ValidateAccount(Account account)
    {
        if (!Regex.IsMatch(account.Username, @"^[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,37}[a-zA-Z0-9])?$"))
            throw new InvalidOperationException("GitHub 아이디를 입력해 주세요. 이메일 주소가 아닌 @ 뒤의 아이디입니다.");
        foreach (var value in new[] { account.Label, account.Name, account.Email })
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
                throw new InvalidOperationException("계정 별칭, 커밋 이름, 이메일을 모두 입력해 주세요.");
        if (!account.Email.Contains('@') || account.Email.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("올바른 커밋 이메일 주소를 입력해 주세요.");
    }

    public async Task<FolderRule> CreateRule(string folder, bool recursive, string accountId)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Trim()));
        if (!Directory.Exists(folder)) throw new InvalidOperationException("존재하는 폴더를 선택해 주세요.");
        if (folder.IndexOfAny(['*', '?', '[', ']', '\n', '\r']) >= 0)
            throw new InvalidOperationException("경로에 [, ], *, ? 또는 줄바꿈이 있는 폴더는 지원하지 않습니다.");
        string gitDirectory = "";
        if (!recursive)
        {
            var top = Git.Require(await git.Run("-C", folder, "rev-parse", "--show-toplevel"));
            if (!SamePath(top, folder)) throw new InvalidOperationException("해당 저장소만 적용하려면 저장소의 최상위 폴더를 선택해 주세요.");
            gitDirectory = Git.Require(await git.Run("-C", folder, "rev-parse", "--absolute-git-dir")).Replace('\\', '/');
            if (gitDirectory.IndexOfAny(['*', '?', '[', ']']) >= 0) throw new InvalidOperationException("Git 경로에 지원하지 않는 패턴 문자가 있습니다.");
        }
        return new(Guid.NewGuid().ToString("N"), folder, recursive, accountId, gitDirectory);
    }

    public async Task<bool> IsEnabled()
    {
        var result = await git.Run("config", "--global", "--no-includes", "--fixed-value", "--get-all", "include.path", ManagedFile);
        if (result.ExitCode is not (0 or 1)) Git.Require(result);
        return result.ExitCode == 0;
    }

    public async Task Save(Settings settings, bool enable = true)
    {
        foreach (var account in settings.Accounts) ValidateAccount(account);
        if (settings.Rules.Any(r => settings.Accounts.All(a => a.Id != r.AccountId)))
            throw new InvalidOperationException("폴더 규칙에 연결된 계정을 찾을 수 없습니다.");
        Directory.CreateDirectory(DataDirectory);
        // Immutable profiles and an atomic include-file swap keep rules consistent for Git readers.
        var builder = new StringBuilder("# Managed by GitOwnerSelector. Edit rules in the app.\n");
        var generation = Guid.NewGuid().ToString("N");
        var profiles = Path.Combine(DataDirectory, "profiles", generation);
        Directory.CreateDirectory(profiles);
        foreach (var account in settings.Accounts)
        {
            if (!Guid.TryParseExact(account.Id, "N", out _)) throw new InvalidOperationException("잘못된 계정 ID입니다.");
            var content = $"[user]\n\tname = {Quote(account.Name)}\n\temail = {Quote(account.Email)}\n" +
                $"[credential \"https://github.com\"]\n\tusername = {Quote(account.Username)}\n\thelper =\n\thelper = manager\n";
            AtomicWrite(Path.Combine(profiles, account.Id + ".gitconfig"), content);
        }
        foreach (var rule in settings.Rules.OrderBy(r => r.Folder.Length).ThenBy(r => r.Recursive ? 0 : 1))
        {
            var pattern = rule.Recursive ? rule.Folder.Replace('\\', '/').TrimEnd('/') + "/" : rule.GitDirectory;
            if (string.IsNullOrWhiteSpace(pattern)) throw new InvalidOperationException("폴더 규칙의 Git 경로가 없습니다.");
            var profile = Path.Combine(profiles, rule.AccountId + ".gitconfig").Replace('\\', '/');
            builder.Append($"[includeIf {Quote("gitdir/i:" + pattern)}]\n\tpath = {Quote(profile)}\n");
        }
        Backup(ManagedFile);
        Backup(SettingsFile);
        var oldManaged = File.Exists(ManagedFile) ? File.ReadAllText(ManagedFile) : null;
        var oldSettings = File.Exists(SettingsFile) ? File.ReadAllText(SettingsFile) : null;
        try
        {
            AtomicWrite(ManagedFile, builder.ToString());
            AtomicWrite(SettingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            if (enable && settings.Rules.Count > 0 && !await IsEnabled())
            {
                Backup(GlobalTarget());
                Git.Require(await git.Run("config", "--global", "--add", "include.path", ManagedFile));
            }
        }
        catch
        {
            Restore(ManagedFile, oldManaged);
            Restore(SettingsFile, oldSettings);
            throw;
        }
    }

    public async Task Disable()
    {
        if (!await IsEnabled()) return;
        Backup(GlobalTarget());
        Git.Require(await git.Run("config", "--global", "--fixed-value", "--unset-all", "include.path", ManagedFile));
    }

    public async Task<string> Inspect(string folder)
    {
        Git.Require(await git.Run("-C", folder, "rev-parse", "--git-dir"));
        var lines = new List<string> { $"검사 폴더: {folder}", "" };
        foreach (var key in new[] { "user.name", "user.email" })
        {
            var result = await git.Run("-C", folder, "config", "--show-origin", "--get", key);
            if (result.ExitCode is not (0 or 1)) Git.Require(result);
            lines.Add($"{key}: {(result.ExitCode == 0 ? result.Output : "미설정")}");
        }
        var configured = await git.Run("-C", folder, "config", "--get-urlmatch", "credential.username", "https://github.com");
        if (configured.ExitCode is not (0 or 1)) Git.Require(configured);
        lines.Add($"GitHub 기본 인증 계정: {(configured.ExitCode == 0 ? configured.Output : "미설정")}");
        var helpers = await git.Run("-C", folder, "config", "--get-urlmatch", "credential.helper", "https://github.com");
        lines.Add($"인증 도우미: {helpers.Output.Trim()}");
        var local = await git.Run("-C", folder, "config", "--local", "--get-regexp", @"^(user\.|credential\.|url\.)");
        if (local.ExitCode == 0) lines.Add("\n주의: 저장소 자체에 사용자/인증/URL 설정이 있습니다. 폴더의 기본값보다 우선할 수 있습니다.");
        var remotes = Git.Require(await git.Run("-C", folder, "remote"));
        foreach (var remote in remotes.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var urls = Git.Require(await git.Run("-C", folder, "remote", "get-url", "--push", "--all", remote.Trim()));
            foreach (var url in urls.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com")
                    lines.Add($"\n{remote}: HTTPS github.com 주소가 아닙니다. 이 앱의 인증 계정 지정이 적용되지 않습니다.");
                else
                {
                    lines.Add($"\n{remote}: https://github.com{uri.AbsolutePath}");
                    var actual = await git.Run("-C", folder, "config", "--get-urlmatch", "credential.username", url);
                    lines.Add($"  주소별 기본 계정: {(actual.ExitCode == 0 ? actual.Output : "미설정")}");
                    if (uri.UserInfo.Length > 0) lines.Add("  주의: remote URL에 인증 정보가 있어 폴더 기본 계정보다 우선합니다.");
                }
            }
        }
        lines.Add("\n이 검사는 로컬 설정을 확인합니다. 실제 GitHub 접근 권한은 ‘접근 테스트’로 확인하세요.");
        return string.Join("\n", lines);
    }

    public async Task<string> TestAccess(string folder)
    {
        var url = Git.Require(await git.Run("-C", folder, "remote", "get-url", "--push", "origin"));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || uri.UserInfo.Length != 0)
            throw new InvalidOperationException("접근 테스트는 사용자 정보가 없는 https://github.com/... origin 주소에서 지원합니다.");
        // Avoid interactive prompts and never push or print credentials.
        var result = await git.RunWithTimeout(TimeSpan.FromSeconds(45), "-C", folder, "-c", "credential.interactive=false", "-c", "core.askPass=", "ls-remote", url, "HEAD");
        if (result.ExitCode != 0) throw new InvalidOperationException("원격 읽기 실패. 계정 로그인, 저장소 권한, 네트워크를 확인해 주세요.");
        return "원격 읽기 성공. 공개 저장소는 로그인 없이도 성공할 수 있으며, 쓰기 권한이나 실제 push 계정까지 증명하지는 않습니다.";
    }

    private string GlobalTarget()
    {
        var explicitPath = git.EnvironmentValue("GIT_CONFIG_GLOBAL");
        if (!string.IsNullOrEmpty(explicitPath)) return explicitPath;
        var home = git.EnvironmentValue("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var main = Path.Combine(home, ".gitconfig");
        var xdg = Path.Combine(git.EnvironmentValue("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config"), "git", "config");
        return File.Exists(main) || !File.Exists(xdg) ? main : xdg;
    }

    private void Backup(string file)
    {
        if (!File.Exists(file)) return;
        var directory = Path.Combine(DataDirectory, "backups");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8] + "-" + Path.GetFileName(file));
        File.Copy(file, destination);
    }

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left).TrimEnd('\\', '/'), Path.GetFullPath(right).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    private static void AtomicWrite(string path, string content)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, content, new UTF8Encoding(false)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static void Restore(string path, string? content)
    {
        if (content is null) File.Delete(path);
        else AtomicWrite(path, content);
    }
}
