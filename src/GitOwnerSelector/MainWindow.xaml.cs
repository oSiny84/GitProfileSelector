using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using GitOwnerSelector.Core;

namespace GitOwnerSelector;

public partial class MainWindow : Window
{
    private readonly Git git = new();
    private readonly ConfigurationService service;
    private Settings settings = new();
    private bool busy;
    private sealed record RuleRow(string Id, string Folder, string Account, string Scope);

    public MainWindow()
    {
        InitializeComponent();
        service = new(git);
        if (Environment.GetCommandLineArgs().Contains("--render-preview"))
        {
            Status.Text = "화면 미리보기 · Git 설정 변경 없음";
            return;
        }
        Loaded += async (_, _) => await Work(async () =>
        {
            var version = Git.Require(await git.Run("--version"));
            Git.Require(await git.Run("credential-manager", "--version"));
            settings = service.Load();
            Refresh();
            Output.Text = $"{version}\nGit Credential Manager 사용 가능\n\n계정 프로필과 폴더를 연결하면 설정이 적용됩니다. 로그인은 계정별로 한 번씩 진행하세요.";
        });
    }

    private async Task Work(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        Workspace.IsEnabled = false;
        DisableButton.IsEnabled = false;
        Status.Text = "작업 중…";
        try
        {
            await action();
            Status.Text = await service.IsEnabled() ? "● 폴더별 계정 설정 활성화" : "○ 폴더별 계정 설정 비활성화";
        }
        catch (Exception ex)
        {
            Status.Text = "작업을 완료하지 못했습니다";
            Output.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "확인해 주세요", MessageBoxButton.OK, MessageBoxImage.Warning);
            try { settings = service.Load(); Refresh(); } catch { /* Keep the original failure visible. */ }
        }
        finally { busy = false; Workspace.IsEnabled = true; DisableButton.IsEnabled = true; }
    }

    private void Refresh(string? accountId = null)
    {
        accountId ??= (Profiles.SelectedItem as Account)?.Id;
        Profiles.ItemsSource = null;
        Profiles.ItemsSource = settings.Accounts;
        RuleAccount.ItemsSource = null;
        RuleAccount.ItemsSource = settings.Accounts;
        var account = settings.Accounts.Find(a => a.Id == accountId) ?? settings.Accounts.FirstOrDefault();
        Profiles.SelectedItem = account;
        RuleAccount.SelectedItem = account;
        Rules.ItemsSource = settings.Rules.Select(r => new RuleRow(r.Id, r.Folder,
            settings.Accounts.Find(a => a.Id == r.AccountId)?.Label ?? "?", r.Recursive ? "하위 포함" : "이 저장소")).ToList();
    }

    private void ProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Profiles.SelectedItem is not Account a) return;
        ProfileLabel.Text = a.Label; Username.Text = a.Username; CommitName.Text = a.Name; CommitEmail.Text = a.Email;
        RuleAccount.SelectedItem = a;
    }

    private void NewProfile(object sender, RoutedEventArgs e)
    {
        Profiles.SelectedItem = null;
        ProfileLabel.Clear(); Username.Clear(); CommitName.Clear(); CommitEmail.Clear(); ProfileLabel.Focus();
    }

    private async void SaveProfile(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        var previous = Profiles.SelectedItem as Account;
        var account = new Account(previous?.Id ?? Guid.NewGuid().ToString("N"), ProfileLabel.Text.Trim(), Username.Text.Trim(), CommitName.Text.Trim(), CommitEmail.Text.Trim());
        ConfigurationService.ValidateAccount(account);
        settings.Accounts.RemoveAll(a => a.Id == account.Id);
        settings.Accounts.Add(account);
        await service.Save(settings, enable: false);
        Refresh(account.Id);
        Output.Text = "프로필을 저장했습니다. ‘GitHub 로그인’으로 이 계정의 인증을 등록하세요.";
    });

    private async void DeleteProfile(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        var a = Profiles.SelectedItem as Account ?? throw new InvalidOperationException("삭제할 계정을 선택해 주세요.");
        if (settings.Rules.Any(r => r.AccountId == a.Id)) throw new InvalidOperationException("먼저 이 계정과 연결된 폴더를 해제해 주세요.");
        settings.Accounts.Remove(a);
        await service.Save(settings, enable: false); Refresh();
        if (settings.Accounts.Count == 0) NewProfile(sender, e);
        Output.Text = "프로필을 삭제했습니다. GitHub 로그인 정보는 Credential Manager에 유지됩니다.";
    });

    private async void Login(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        var a = Profiles.SelectedItem as Account ?? throw new InvalidOperationException("계정 프로필을 먼저 저장하고 선택해 주세요.");
        Output.Text = $"@{a.Username} 계정으로 브라우저에서 로그인해 주세요. 최대 5분간 기다립니다.";
        Git.Require(await git.RunWithTimeout(TimeSpan.FromMinutes(5), "credential-manager", "github", "login", "--username", a.Username, "--browser"));
        Output.Text = $"@{a.Username} 로그인 절차가 완료되었습니다. ‘로그인된 계정 확인’으로 등록 상태를 확인할 수 있습니다.";
    });

    private async void ListAccounts(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        var accounts = Git.Require(await git.Run("credential-manager", "github", "list"));
        Output.Text = string.IsNullOrWhiteSpace(accounts) ? "등록된 GitHub 인증 계정이 없습니다." : "Git Credential Manager에 등록된 계정\n\n" + accounts;
    });

    private void Browse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "계정을 연결할 폴더 선택" };
        if (dialog.ShowDialog(this) == true) Folder.Text = dialog.FolderName;
    }

    private async void ApplyRule(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        var account = RuleAccount.SelectedItem as Account ?? throw new InvalidOperationException("연결할 계정을 선택해 주세요.");
        var rule = await service.CreateRule(Folder.Text, Recursive.IsChecked == true, account.Id);
        settings.Rules.RemoveAll(r => string.Equals(r.Folder, rule.Folder, StringComparison.OrdinalIgnoreCase) && r.Recursive == rule.Recursive);
        settings.Rules.Add(rule);
        await service.Save(settings); Refresh(account.Id);
        Output.Text = $"{rule.Folder}\n→ @{account.Username} 적용 완료\n\n앱을 종료해도 설정이 유지됩니다. 더 구체적인 하위 폴더 규칙이 우선합니다.\n‘설정 검사’로 저장소별 설정과 SSH 주소 여부를 확인하세요.";
    });

    private void RuleSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Rules.SelectedItem is not RuleRow row) return;
        var rule = settings.Rules.First(r => r.Id == row.Id);
        Folder.Text = rule.Folder; Recursive.IsChecked = rule.Recursive;
        RuleAccount.SelectedItem = settings.Accounts.Find(a => a.Id == rule.AccountId);
    }

    private async void RemoveRule(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        var row = Rules.SelectedItem as RuleRow ?? throw new InvalidOperationException("연결을 해제할 폴더를 목록에서 선택해 주세요.");
        settings.Rules.RemoveAll(r => r.Id == row.Id);
        await service.Save(settings); Refresh();
        Output.Text = "폴더 연결을 해제했습니다. 기존 Git 설정 또는 상위 폴더 규칙이 다시 적용됩니다.";
    });

    private async Task<string?> InspectionFolder()
    {
        if (!string.IsNullOrWhiteSpace(Folder.Text))
        {
            var result = await git.Run("-C", Folder.Text, "rev-parse", "--git-dir");
            if (result.ExitCode == 0) return Folder.Text;
        }
        var dialog = new OpenFolderDialog { Title = "검사할 실제 Git 저장소를 선택하세요" };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    private async void Inspect(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        var folder = await InspectionFolder();
        if (folder is not null) Output.Text = await service.Inspect(folder);
    });

    private async void TestAccess(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        var folder = await InspectionFolder();
        if (folder is not null) Output.Text = await service.TestAccess(folder);
    });

    private async void Disable(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        await service.Disable();
        Output.Text = "앱이 추가한 전역 include 연결을 제거했습니다. 기존 Git 설정이 다시 사용됩니다.\n프로필과 폴더 목록은 보관됩니다. 다시 적용하려면 폴더에 계정 적용을 누르세요.";
    });

    private void OpenData(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(service.DataDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { service.DataDirectory }, UseShellExecute = false });
    }
}
