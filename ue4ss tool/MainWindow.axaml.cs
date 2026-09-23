using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace ue4ss_tool;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<UnrealGame> _view = new();
    private ScanResult? _last;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        GameList.ItemsSource = _view;
        Opened += async (_, _) => await LoadPackagesAsync();
    }

    /// <summary>선택된 보기. 값은 ComboBoxItem 의 Tag 에서 읽는다.</summary>
    private GameView SelectedView =>
        (FilterCombo.SelectedItem as ComboBoxItem)?.Tag is GameView v ? v : GameView.AllUnreal;

    private UnrealGame? SelectedGame => GameList.SelectedItem as UnrealGame;

    private Ue4ssPackage? SelectedPackage => (PackageCombo.SelectedItem as ComboBoxItem)?.Tag as Ue4ssPackage;

    // ── 스캔 ────────────────────────────────────────────────────

    private async void OnScanClick(object? sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        Spinner.IsVisible = true;
        SummaryText.Text = "스캔 중...";
        StatusText.Text = "";
        _view.Clear();

        // 입력된 폴더가 있으면 그것들(세미콜론 등으로 구분), 없으면 자동 감지.
        var typed = RootBox.Text?.Trim();
        List<string>? roots = null;
        if (!string.IsNullOrEmpty(typed))
        {
            roots = typed.Split(new[] { ';', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Where(Directory.Exists)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .ToList();
            if (roots.Count == 0) roots = null;
        }

        try
        {
            var watch = Stopwatch.StartNew();
            var result = await Task.Run(() => UnrealScanner.Scan(roots, msg =>
                Dispatcher.UIThread.Post(() => StatusText.Text = msg)));
            _last = result;
            ApplyFilter();

            if (result.Roots.Count == 0)
            {
                SummaryText.Text = "스캔할 폴더를 찾지 못했습니다.";
                StatusText.Text = "스캔 폴더에 steamapps\\common 또는 게임 폴더 경로를 지정해 보세요.";
            }
            else
            {
                SummaryText.Text = BuildSummary(result);
                StatusText.Text = BuildStatus(result, watch.Elapsed);
            }
        }
        catch (Exception ex)
        {
            SummaryText.Text = "스캔 중 오류가 발생했습니다.";
            StatusText.Text = ex.Message;
        }
        finally
        {
            Spinner.IsVisible = false;
            ScanButton.IsEnabled = true;
        }
    }

    private static string BuildSummary(ScanResult result) =>
        $"언리얼 게임 {result.Games.Count}개 — " +
        $"UE5 {result.CountOf(UnrealGeneration.UE5)} · " +
        $"UE4 {result.CountOf(UnrealGeneration.UE4)} · " +
        $"버전 미확인 {result.CountOf(UnrealGeneration.Unknown)} · " +
        $"UE3 {result.CountOf(UnrealGeneration.UE3)} · " +
        $"UE4SS 설치됨 {result.InstalledCount}";

    private static string BuildStatus(ScanResult result, TimeSpan elapsed)
    {
        var parts = new List<string>
        {
            $"폴더 {result.ScannedFolderCount}개 검사 ({elapsed.TotalSeconds:0.0}초)",
            "스캔 폴더: " + string.Join("  |  ", result.Roots),
        };
        if (result.Warnings.Count > 0)
            parts.Add($"⚠ 읽지 못한 항목 {result.Warnings.Count}개 — 결과가 일부 누락될 수 있습니다");
        return string.Join("   ·   ", parts);
    }

    private void OnFilterChanged(object? sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (_last is null) return;
        var keep = SelectedGame;
        _view.Clear();
        foreach (var g in UnrealScanner.Filter(_last, SelectedView))
            _view.Add(g);
        if (keep is not null && _view.Contains(keep)) GameList.SelectedItem = keep;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        // async void 이므로 예외가 새어 나가면 프로세스가 죽는다. 반드시 여기서 잡는다.
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "steamapps\\common 폴더 또는 게임 폴더 선택",
                AllowMultiple = false,
            });
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) RootBox.Text = path;
        }
        catch (Exception ex)
        {
            StatusText.Text = "폴더 선택 실패: " + ex.Message;
        }
    }

    private async void OnAddFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "추가할 폴더(라이브러리 또는 게임 폴더) 선택",
                AllowMultiple = true,
            });
            var paths = folders.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
            if (paths.Count == 0) return;

            var existing = (RootBox.Text ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            foreach (var p in paths)
            {
                if (!existing.Contains(p, StringComparer.OrdinalIgnoreCase)) existing.Add(p);
            }
            RootBox.Text = string.Join("; ", existing);
        }
        catch (Exception ex)
        {
            StatusText.Text = "폴더 추가 실패: " + ex.Message;
        }
    }

    private void OnAutoClick(object? sender, RoutedEventArgs e)
    {
        RootBox.Text = "";
        var found = UnrealScanner.FindCommonFolders();
        StatusText.Text = found.Count > 0
            ? "자동 감지된 폴더: " + string.Join("  |  ", found)
            : "자동 감지 실패 — 폴더를 직접 지정하세요.";
    }

    // ── 상세 ────────────────────────────────────────────────────

    private void OnGameSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedGame is { } game) SelectMatchingPackage(game);
        ShowDetail(SelectedGame);
    }

    /// <summary>
    /// 이미 설치된 UE4SS 와 같은 배치의 패키지를 고른다. 배치가 다르면 업데이트가 막히기 때문이다.
    /// 설치가 없으면 목록 첫 항목(실험판)으로 둔다.
    /// </summary>
    private void SelectMatchingPackage(UnrealGame game)
    {
        var items = PackageCombo.Items.OfType<ComboBoxItem>().ToList();
        if (items.Count == 0) return;

        var want = game.Ue4ss.CoreLayout;
        var match = want == Ue4ssLayout.None
            ? items[0]
            : items.FirstOrDefault(i => i.Tag is Ue4ssPackage p && !p.IsDev && p.ExpectedLayout == want);
        if (match is not null) PackageCombo.SelectedItem = match;
    }

    /// <summary>받기 전에 알 수 있는 배치 충돌. 실제 판정은 받은 뒤 Preflight 가 한다.</summary>
    private static bool LayoutConflicts(UnrealGame game, Ue4ssPackage? package) =>
        package is not null &&
        package.ExpectedLayout != Ue4ssLayout.None &&
        game.Ue4ss.CoreLayout != Ue4ssLayout.None &&
        game.Ue4ss.CoreLayout != package.ExpectedLayout;

    private void OnPackageChanged(object? sender, SelectionChangedEventArgs e) => ShowDetail(SelectedGame);

    private void ShowDetail(UnrealGame? game)
    {
        if (game is null)
        {
            DetailTitle.Text = "왼쪽 목록에서 게임을 고르세요.";
            DetailPanel.IsVisible = false;
            return;
        }

        DetailPanel.IsVisible = true;
        DetailTitle.Text = game.AppId is { } id ? $"{game.DisplayName}  (AppID {id})" : game.DisplayName;

        var engine = new StringBuilder(game.EngineText);
        if (game.Version is not null) engine.Append($"  ·  출처: {game.Version.Source}");
        if (game.UsesIoStore) engine.Append("  ·  IoStore(.utoc/.ucas)");
        EngineText.Text = engine.ToString();

        ExeText.Text = game.GameExe is null
            ? "(찾지 못함)"
            : Path.GetRelativePath(game.InstallDir, game.GameExe) +
              (game.IsShippingExe || game.Generation == UnrealGeneration.UE3 ? "" : "  (Shipping 이 아님 — Development/Test 빌드일 수 있음)");
        TargetText.Text = game.ExeDir ?? "(찾지 못함)";
        SupportText.Text = Ue4ssCompatibility.Describe(game.Support);
        Ue4ssText.Text = game.Ue4ss.Describe();

        var extra = new List<string>();
        if (game.Ue4ss.OtherLoaders.Count > 0)
            extra.Add("다른 DLL 로더: " + string.Join(", ", game.Ue4ss.OtherLoaders) + " (ReShade 등. 충돌하면 하나씩 꺼 보세요)");
        if (game.AntiCheat != AntiCheat.None)
            extra.Add("안티치트: " + game.AntiCheat.ToString().Replace(", ", " + "));
        ExtraText.Text = extra.Count > 0 ? string.Join("\n", extra) : "—";

        var warnings = Warnings(game, SelectedPackage);
        WarningBox.IsVisible = warnings.Count > 0;
        WarningText.Text = string.Join("\n\n", warnings);
        PackageInfoText.Text = SelectedPackage?.Details ?? "";

        UpdateButtons(game);
    }

    /// <summary>설치 전에 알려야 할 주의 사항. 확인 창에도 같은 내용을 쓴다.</summary>
    private static List<string> Warnings(UnrealGame game, Ue4ssPackage? package)
    {
        var list = new List<string>();
        if (game.Generation == UnrealGeneration.UE3)
            list.Add("UE3 게임입니다. UE4SS 는 UE4/UE5 전용이라 설치할 수 없습니다.");
        else if (game.ExeDir is null)
            list.Add("실제 게임 exe 를 찾지 못해 설치 위치를 정할 수 없습니다. 「로컬 ZIP」 대신 직접 설치하세요.");
        else if (game.Support == Ue4ssSupport.Unsupported)
            list.Add($"UE {game.Version} 은(는) UE4SS 가 지원하지 않는 버전입니다.");

        if (game.AntiCheat != AntiCheat.None)
            list.Add($"{game.AntiCheat.ToString().Replace(", ", "·")} 안티치트가 있습니다. 온라인 모드에서 DLL 주입은 계정 제재로 이어질 수 있고, " +
                     "안티치트가 켜진 상태로는 UE4SS 가 불러와지지 않을 수 있습니다. 오프라인 전용으로만 쓰세요.");

        if (package is not null && !package.IsExperimental && game.Support == Ue4ssSupport.ExperimentalOnly)
            list.Add(game.IsShippingExe || game.Version is null
                ? $"UE {game.Version} 게임은 실험판만 지원합니다. 안정판(v3.0.x)은 UE 5.3 까지만 지원합니다."
                : "Shipping 빌드가 아니어서 실험판만 지원합니다.");
        else if (game.Support == Ue4ssSupport.Unknown && game.Generation != UnrealGeneration.UE3 && game.ExeDir is not null)
            list.Add("엔진 버전을 확인하지 못했습니다. 지원 범위가 넓은 실험판을 권장합니다.");

        if (LayoutConflicts(game, package))
            list.Add($"지금 설치된 UE4SS 는 「{Ue4ssState.LayoutName(game.Ue4ss.CoreLayout)}」이고 고른 패키지는 " +
                     $"「{Ue4ssState.LayoutName(package!.ExpectedLayout)}」라서 그대로 덮을 수 없습니다. " +
                     "이 패키지로 바꾸려면 먼저 「제거」하세요. 제거한 파일은 휴지통으로 가므로 직접 넣은 모드는 되살려 옮길 수 있습니다.");

        switch (game.Ue4ss.Layout)
        {
            case Ue4ssLayout.ForeignProxy:
                list.Add("UE4SS 가 아닌 dwmapi.dll 이 이미 있습니다. 다른 모드의 파일일 수 있어 설치를 막습니다.");
                break;
            case Ue4ssLayout.LegacyXinput:
                list.Add("구버전(v2.x) UE4SS 입니다. 새 버전으로 바꾸려면 먼저 「제거」하세요.");
                break;
            case Ue4ssLayout.Partial:
                list.Add(game.Ue4ss.Describe() + ". 다시 설치하면 빠진 파일이 채워집니다.");
                break;
        }
        return list;
    }

    private void UpdateButtons(UnrealGame game)
    {
        bool canInstall = !_busy && game.ExeDir is not null && game.Support != Ue4ssSupport.Unsupported &&
                          game.Ue4ss.Layout != Ue4ssLayout.ForeignProxy;
        InstallButton.IsEnabled = canInstall && SelectedPackage is not null && !LayoutConflicts(game, SelectedPackage);
        InstallButton.Content = game.Ue4ss.Layout is Ue4ssLayout.None ? "설치" : "업데이트 (다시 설치)";
        InstallLocalButton.IsEnabled = canInstall;

        ToggleButton.IsEnabled = !_busy && game.Ue4ss.IsInstalled;
        ToggleButton.Content = game.Ue4ss.IsDisabled ? "켜기" : "끄기";
        UninstallButton.IsEnabled = !_busy && game.Ue4ss.Layout is not (Ue4ssLayout.None or Ue4ssLayout.ForeignProxy);

        OpenExeDirButton.IsEnabled = game.ExeDir is not null;
        OpenSettingsButton.IsEnabled = game.Ue4ss.WorkingDir is not null && File.Exists(game.Ue4ss.SettingsPath);
        OpenModsButton.IsEnabled = game.Ue4ss.WorkingDir is not null && Directory.Exists(game.Ue4ss.ModsDir);
        OpenLogButton.IsEnabled = game.Ue4ss.WorkingDir is not null && File.Exists(game.Ue4ss.LogPath);
    }

    private void Refresh(UnrealGame game)
    {
        game.Ue4ss = Ue4ssDetector.Detect(game.ExeDir);
        // 제거한 뒤에는 다시 기본(실험판)으로 돌려 둔다. 안정판→실험판으로 옮길 때 바로 설치할 수 있다.
        if (game.Ue4ss.CoreLayout == Ue4ssLayout.None && SelectedGame == game) SelectMatchingPackage(game);
        if (_last is not null) SummaryText.Text = BuildSummary(_last);
        if (SelectedGame == game) ShowDetail(game);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ScanButton.IsEnabled = !busy;
        if (SelectedGame is { } g) UpdateButtons(g);
    }

    // ── 패키지 목록 ─────────────────────────────────────────────

    private async void OnRefreshPackagesClick(object? sender, RoutedEventArgs e) => await LoadPackagesAsync();

    private async Task LoadPackagesAsync()
    {
        PackageCombo.Items.Clear();
        PackageCombo.PlaceholderText = "(릴리스 목록을 불러오는 중…)";
        List<Ue4ssPackage> packages;
        string? note = null;
        try
        {
            packages = await Ue4ssReleases.FetchAsync();
        }
        catch (Exception ex)
        {
            packages = Ue4ssReleases.FromCache();
            note = packages.Count > 0
                ? $"GitHub 에 연결하지 못해 전에 받아 둔 패키지만 보여 줍니다. ({ex.Message})"
                : $"UE4SS 릴리스 목록을 가져오지 못했습니다. 「새로고침」하거나 「로컬 ZIP으로 설치」를 쓰세요. ({ex.Message})";
        }

        foreach (var p in packages)
            PackageCombo.Items.Add(new ComboBoxItem { Content = p.Label, Tag = p });
        PackageCombo.PlaceholderText = packages.Count > 0 ? "" : "(패키지 없음)";
        if (packages.Count > 0) PackageCombo.SelectedIndex = 0;   // 실험판 기본. 지원 범위가 가장 넓다.
        if (SelectedGame is { } game) SelectMatchingPackage(game);

        if (note is not null) ActionLog.Text = note;
        ShowDetail(SelectedGame);
    }

    // ── 설치 ────────────────────────────────────────────────────

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedGame is not { } game || SelectedPackage is not { } package) return;
        await InstallAsync(game, package.Label, package,
            async (progress, ct) => await Ue4ssReleases.DownloadAsync(package, progress, ct));
    }

    private async void OnInstallLocalClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedGame is not { } game) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "UE4SS zip 선택",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("zip 파일") { Patterns = new[] { "*.zip" } } },
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;
            await InstallAsync(game, "로컬 파일 " + Path.GetFileName(path), null, (_, _) => Task.FromResult(path));
        }
        catch (Exception ex)
        {
            ActionLog.Text = "파일 선택 실패: " + ex.Message;
        }
    }

    /// <summary>받기 → 구조·충돌 확인 → 사용자 확인 → 풀기 → 상태 다시 읽기.</summary>
    private async Task InstallAsync(
        UnrealGame game, string packageLabel, Ue4ssPackage? package,
        Func<IProgress<double>, CancellationToken, Task<string>> getZip)
    {
        if (game.ExeDir is not { } exeDir) return;
        if (GameProcess.IsRunning(game.GameExe))
        {
            await ConfirmDialog.ShowAsync(this, "게임 실행 중", "게임이 실행 중입니다. 종료한 뒤 다시 시도하세요.");
            return;
        }

        SetBusy(true);
        DownloadBar.IsVisible = true;
        DownloadBar.Value = 0;
        try
        {
            ActionLog.Text = $"{packageLabel} 준비 중…";
            var progress = new Progress<double>(v => DownloadBar.Value = v);
            var zip = await getZip(progress, CancellationToken.None);

            var info = await Task.Run(() => Ue4ssInstaller.Preflight(zip, exeDir));
            DownloadBar.IsVisible = false;

            var keep = KeepUserFilesCheck.IsChecked == true;
            var isUpdate = game.Ue4ss.Layout != Ue4ssLayout.None;
            var message = new StringBuilder()
                .AppendLine($"「{game.DisplayName}」에 UE4SS 를 {(isUpdate ? "업데이트" : "설치")}합니다.")
                .AppendLine()
                .AppendLine($"• 패키지: {packageLabel}")
                .AppendLine($"• 설치 위치: {exeDir}")
                .AppendLine($"• 배치: {Ue4ssState.LayoutName(info.Layout)} (프록시 {info.ProxyName})");
            if (isUpdate)
                message.AppendLine($"• 기존 설정·mods.txt: {(keep ? "유지" : "패키지 기본값으로 덮어씀")}");
            message.AppendLine("• 콘솔·GUI 창: 켬으로 고정 (UE4SS-settings.ini [Debug] 의 세 값을 1로)");

            var warnings = Warnings(game, package);
            if (warnings.Count > 0)
            {
                message.AppendLine();
                foreach (var w in warnings) message.AppendLine("⚠ " + w);
            }
            message.AppendLine()
                .Append("게임 파일은 바꾸지 않고 위 폴더에 UE4SS 파일만 추가합니다. 나중에 「제거」로 휴지통에 보낼 수 있습니다.");

            if (!await ConfirmDialog.AskAsync(this, "UE4SS 설치", message.ToString(), isUpdate ? "업데이트" : "설치"))
            {
                ActionLog.Text = "취소했습니다.";
                return;
            }

            var report = await Task.Run(() => Ue4ssInstaller.Install(zip, exeDir, keep));
            Refresh(game);

            var log = new StringBuilder($"{(report.WasUpdate ? "업데이트" : "설치")} 완료 — 파일 {report.Written}개");
            if (report.Preserved.Count > 0)
                log.Append($", 유지 {report.Preserved.Count}개 ({string.Join(", ", report.Preserved)})");
            log.AppendLine(".");
            foreach (var note in report.Notes) log.AppendLine(note);
            log.Append("게임을 실행하면 UE4SS 콘솔 창과 GUI 창이 함께 뜹니다(GUI 는 기본 Ctrl+O 로 숨기기/보이기). " +
                       "안 뜨면 「로그 열기」로 UE4SS.log 를 확인하세요.");
            ActionLog.Text = log.ToString();
        }
        catch (InstallBlockedException ex)
        {
            ActionLog.Text = "설치하지 않았습니다.";
            await ConfirmDialog.ShowAsync(this, "설치할 수 없음", ex.Message);
        }
        catch (Exception ex)
        {
            ActionLog.Text = "설치 실패: " + ex.Message;
        }
        finally
        {
            DownloadBar.IsVisible = false;
            SetBusy(false);
        }
    }

    // ── 제거·켜기/끄기 ──────────────────────────────────────────

    private async void OnUninstallClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedGame is not { ExeDir: { } exeDir } game) return;
        try
        {
            if (GameProcess.IsRunning(game.GameExe))
            {
                await ConfirmDialog.ShowAsync(this, "게임 실행 중", "게임이 실행 중입니다. 종료한 뒤 다시 시도하세요.");
                return;
            }

            var items = Ue4ssInstaller.PlanUninstall(exeDir);
            if (items.Count == 0)
            {
                ActionLog.Text = "제거할 UE4SS 파일이 없습니다.";
                return;
            }

            var message = new StringBuilder()
                .AppendLine($"「{game.DisplayName}」에서 아래 항목을 휴지통으로 보냅니다.")
                .AppendLine();
            foreach (var item in items)
                message.AppendLine("• " + Path.GetRelativePath(exeDir, item) + (Directory.Exists(item) ? "\\" : ""));
            if (game.Ue4ss.ModFolderCount > 0)
                message.AppendLine().AppendLine($"Mods 폴더의 모드 {game.Ue4ss.ModFolderCount}개도 함께 휴지통으로 갑니다. 필요하면 휴지통에서 되살릴 수 있습니다.");

            if (!await ConfirmDialog.AskAsync(this, "UE4SS 제거", message.ToString(), "휴지통으로 보내기"))
                return;

            SetBusy(true);
            var removed = await Task.Run(() => Ue4ssInstaller.Uninstall(exeDir));
            Refresh(game);
            ActionLog.Text = $"제거 완료 — {removed.Count}개 항목을 휴지통으로 보냈습니다.";
        }
        catch (InstallBlockedException ex)
        {
            Refresh(game);
            await ConfirmDialog.ShowAsync(this, "제거하지 못함", ex.Message);
        }
        catch (Exception ex)
        {
            Refresh(game);
            ActionLog.Text = "제거 실패: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedGame is not { ExeDir: { } exeDir } game) return;
        try
        {
            if (GameProcess.IsRunning(game.GameExe))
            {
                await ConfirmDialog.ShowAsync(this, "게임 실행 중", "게임이 실행 중입니다. 종료한 뒤 다시 시도하세요.");
                return;
            }
            var enable = game.Ue4ss.IsDisabled;
            Ue4ssInstaller.SetEnabled(exeDir, enable);
            Refresh(game);
            ActionLog.Text = enable
                ? "UE4SS 를 켰습니다. 다음 실행부터 불러옵니다."
                : "UE4SS 를 껐습니다(프록시 DLL 이름에 .disabled 를 붙임). 모드와 설정은 그대로 남아 있습니다.";
        }
        catch (InstallBlockedException ex)
        {
            Refresh(game);
            await ConfirmDialog.ShowAsync(this, "바꾸지 못함", ex.Message);
        }
        catch (Exception ex)
        {
            Refresh(game);
            ActionLog.Text = "실패: " + ex.Message;
        }
    }

    // ── 열기 ────────────────────────────────────────────────────

    private void OnOpenInstallDirClick(object? sender, RoutedEventArgs e) => Open(SelectedGame?.InstallDir);
    private void OnOpenExeDirClick(object? sender, RoutedEventArgs e) => Open(SelectedGame?.ExeDir);
    private void OnOpenSettingsClick(object? sender, RoutedEventArgs e) => OpenText(SelectedGame?.Ue4ss.SettingsPath);
    private void OnOpenModsClick(object? sender, RoutedEventArgs e) => Open(SelectedGame?.Ue4ss.ModsDir);
    private void OnOpenLogClick(object? sender, RoutedEventArgs e) => OpenText(SelectedGame?.Ue4ss.LogPath);

    private void Open(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { ActionLog.Text = "열지 못했습니다: " + ex.Message; }
    }

    /// <summary>.ini/.log 는 연결 프로그램이 없을 수 있어 메모장으로 한 번 더 시도한다.</summary>
    private void OpenText(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch
        {
            try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true }); }
            catch (Exception ex) { ActionLog.Text = "열지 못했습니다: " + ex.Message; }
        }
    }
}
