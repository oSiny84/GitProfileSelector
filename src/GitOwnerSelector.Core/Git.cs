using System.Diagnostics;

namespace GitOwnerSelector.Core;

public sealed class Git(IReadOnlyDictionary<string, string?>? environment = null)
{
    public string? EnvironmentValue(string key) => environment is not null && environment.TryGetValue(key, out var value)
        ? value : Environment.GetEnvironmentVariable(key);

    public async Task<GitResult> Run(params string[] args) => await RunWithTimeout(TimeSpan.FromSeconds(20), args);

    public async Task<GitResult> RunWithTimeout(TimeSpan timeout, params string[] args)
    {
        var info = new ProcessStartInfo("git")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (environment is not null)
            foreach (var (key, value) in environment) info.Environment[key] = value;
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Git을 실행할 수 없습니다.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var cancel = new CancellationTokenSource(timeout);
        try { await process.WaitForExitAsync(cancel.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new InvalidOperationException("Git 작업 시간이 초과되었습니다. 로그인 상태나 네트워크를 확인해 주세요.");
        }
        return new(process.ExitCode, (await output).TrimEnd(), (await error).TrimEnd());
    }

    public static string Require(GitResult result)
    {
        if (result.ExitCode != 0) throw new InvalidOperationException(result.Error.Length > 0 ? result.Error : "Git 명령을 실행하지 못했습니다.");
        return result.Output;
    }
}
