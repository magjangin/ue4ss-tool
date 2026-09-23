using ue4ss_tool;

namespace Ue4ssTool.Tests;

/// <summary>zip 분석·설치·업데이트·제거·켜기/끄기. 실제 파일을 임시 폴더에 쓰고 확인한다.</summary>
public class InstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ue4ss-tool-tests", Guid.NewGuid().ToString("N"));
    private readonly string _exeDir;
    private readonly string _zips;

    public InstallerTests()
    {
        _exeDir = Path.Combine(_root, "Game", "Game", "Binaries", "Win64");
        _zips = Path.Combine(_root, "zips");
        Directory.CreateDirectory(_exeDir);
        File.WriteAllText(Path.Combine(_exeDir, "Game-Win64-Shipping.exe"), "game");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>사용자가 고친 설정 파일. 콘솔은 꺼 두고 GraphicsAPI 를 바꿨다.</summary>
    private const string MySettings = "; my-settings\r\n[Debug]\r\nConsoleEnabled = 0\r\nGraphicsAPI = dx11\r\n";

    private string Read(string rel) => File.ReadAllText(Path.Combine(_exeDir, rel));
    private bool Exists(string rel) => File.Exists(Path.Combine(_exeDir, rel)) || Directory.Exists(Path.Combine(_exeDir, rel));

    /// <summary>테스트는 휴지통 대신 바로 지운다.</summary>
    private static void Delete(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else File.Delete(path);
    }

    // ── zip 분석 ────────────────────────────────────────────────

    [Fact]
    public void 실험판_zip은_하위폴더_배치로_읽는다()
    {
        var info = Ue4ssInstaller.Analyze(FakeZip.Subfolder(_zips));
        Assert.Equal(Ue4ssLayout.Subfolder, info.Layout);
        Assert.Equal("", info.BasePrefix);
        Assert.Equal("ue4ss/", info.WorkingPrefix);
        Assert.Equal("dwmapi.dll", info.ProxyName);
    }

    [Fact]
    public void 안정판_zip은_exe옆_배치로_읽는다()
    {
        var info = Ue4ssInstaller.Analyze(FakeZip.Flat(_zips));
        Assert.Equal(Ue4ssLayout.Flat, info.Layout);
        Assert.Equal("", info.WorkingPrefix);
    }

    [Fact]
    public void 최상위_폴더로_한번_감싼_zip과_역슬래시_경로도_읽는다()
    {
        var zip = FakeZip.Create(_zips, "wrapped.zip",
            ("UE4SS_v3.0.1\\dwmapi.dll", "p"),
            ("UE4SS_v3.0.1\\ue4ss\\UE4SS.dll", "c"));

        var info = Ue4ssInstaller.Analyze(zip);
        Assert.Equal("UE4SS_v3.0.1/", info.BasePrefix);
        Assert.Equal(Ue4ssLayout.Subfolder, info.Layout);

        Ue4ssInstaller.Install(zip, _exeDir);
        Assert.Equal("c", Read(@"ue4ss\UE4SS.dll"));
        Assert.False(Exists("UE4SS_v3.0.1"));
    }

    [Fact]
    public void UE4SS_zip이_아니면_거부한다()
    {
        var noProxy = FakeZip.Create(_zips, "a.zip", ("UE4SS.dll", "c"));
        var noCore = FakeZip.Create(_zips, "b.zip", ("dwmapi.dll", "p"), ("readme.txt", "r"));

        Assert.Throws<InvalidDataException>(() => Ue4ssInstaller.Analyze(noProxy));
        Assert.Throws<InvalidDataException>(() => Ue4ssInstaller.Analyze(noCore));
    }

    // ── 설치·업데이트 ───────────────────────────────────────────

    [Fact]
    public void 빈_폴더에_설치하면_zip_배치대로_풀린다()
    {
        var report = Ue4ssInstaller.Install(FakeZip.Subfolder(_zips), _exeDir);

        Assert.False(report.WasUpdate);
        Assert.Equal(5, report.Written);
        Assert.Equal("proxy-new", Read("dwmapi.dll"));
        Assert.Equal("core-new", Read(@"ue4ss\UE4SS.dll"));
        Assert.True(Directory.Exists(Path.Combine(_exeDir, "ue4ss", "Mods", "shared")));
        Assert.Equal("game", Read("Game-Win64-Shipping.exe"));   // 게임 파일은 그대로

        var state = Ue4ssDetector.Detect(_exeDir);
        Assert.Equal(Ue4ssLayout.Subfolder, state.Layout);
        Assert.Equal(1, state.ModFolderCount);   // BPModLoaderMod (shared 는 세지 않는다)
    }

    [Fact]
    public void 업데이트는_DLL과_기본모드를_바꾸고_사용자_설정과_mods_txt는_유지한다()
    {
        Ue4ssInstaller.Install(FakeZip.Subfolder(_zips, "old"), _exeDir);
        File.WriteAllText(Path.Combine(_exeDir, "ue4ss", "UE4SS-settings.ini"), MySettings);
        File.WriteAllText(Path.Combine(_exeDir, "ue4ss", "Mods", "mods.txt"), "my-mods");
        FakeLibrary.Create(_exeDir, "ue4ss/Mods/MyMod/Scripts/main.lua");

        var report = Ue4ssInstaller.Install(FakeZip.Subfolder(_zips, "new"), _exeDir);

        Assert.True(report.WasUpdate);
        Assert.Equal(["ue4ss/Mods/mods.txt", "ue4ss/UE4SS-settings.ini"], report.Preserved.OrderBy(x => x));
        Assert.Equal("core-new", Read(@"ue4ss\UE4SS.dll"));
        Assert.Equal("lua-new", Read(@"ue4ss\Mods\BPModLoaderMod\Scripts\main.lua"));
        var settings = Read(@"ue4ss\UE4SS-settings.ini");
        Assert.StartsWith("; my-settings", settings);
        Assert.Contains("GraphicsAPI = dx11", settings);   // 사용자 값은 그대로
        Assert.Contains("ConsoleEnabled = 1", settings);    // 콘솔 창만 켠다
        Assert.Equal("my-mods", Read(@"ue4ss\Mods\mods.txt"));
        Assert.True(Exists(@"ue4ss\Mods\MyMod\Scripts\main.lua"));
    }

    [Fact]
    public void 유지를_끄면_설정도_패키지_기본값으로_덮는다()
    {
        Ue4ssInstaller.Install(FakeZip.Subfolder(_zips, "old"), _exeDir);
        File.WriteAllText(Path.Combine(_exeDir, "ue4ss", "UE4SS-settings.ini"), MySettings);

        var report = Ue4ssInstaller.Install(FakeZip.Subfolder(_zips, "new"), _exeDir, keepUserFiles: false);

        Assert.Empty(report.Preserved);
        Assert.StartsWith("; settings-default", Read(@"ue4ss\UE4SS-settings.ini"));
    }

    [Fact]
    public void 안정판을_안정판으로_업데이트해도_설정을_유지한다()
    {
        Ue4ssInstaller.Install(FakeZip.Flat(_zips, "300"), _exeDir);
        File.WriteAllText(Path.Combine(_exeDir, "UE4SS-settings.ini"), MySettings);

        Ue4ssInstaller.Install(FakeZip.Flat(_zips, "301"), _exeDir);

        Assert.Equal("core-301", Read("UE4SS.dll"));
        Assert.StartsWith("; my-settings", Read("UE4SS-settings.ini"));
        Assert.Contains("GraphicsAPI = dx11", Read("UE4SS-settings.ini"));
        Assert.Equal(Ue4ssLayout.Flat, Ue4ssDetector.Detect(_exeDir).Layout);
    }

    [Fact]
    public void 배치가_다른_기존_설치_위에는_설치하지_않는다()
    {
        Ue4ssInstaller.Install(FakeZip.Flat(_zips), _exeDir);
        var subZip = FakeZip.Subfolder(_zips);

        var ex = Assert.Throws<InstallBlockedException>(() => Ue4ssInstaller.Install(subZip, _exeDir));
        Assert.Contains("제거", ex.Message);
        Assert.Throws<InstallBlockedException>(() => Ue4ssInstaller.Preflight(subZip, _exeDir));
        Assert.False(Exists("ue4ss"));   // 아무것도 쓰지 않았다
    }

    [Fact]
    public void 다른_모드의_dwmapi가_있으면_설치하지_않는다()
    {
        File.WriteAllText(Path.Combine(_exeDir, "dwmapi.dll"), "someone-else");

        Assert.Throws<InstallBlockedException>(() => Ue4ssInstaller.Install(FakeZip.Subfolder(_zips), _exeDir));
        Assert.Equal("someone-else", Read("dwmapi.dll"));
    }

    [Fact]
    public void 구버전_xinput_설치_위에는_설치하지_않는다()
    {
        FakeLibrary.Create(_exeDir, "xinput1_3.dll", "UE4SS.dll");

        Assert.Throws<InstallBlockedException>(() => Ue4ssInstaller.Install(FakeZip.Flat(_zips), _exeDir));
        Assert.False(Exists("dwmapi.dll"));
    }

    [Fact]
    public void 게임폴더_밖을_가리키는_zip_항목이_있으면_하나도_쓰지_않는다()
    {
        var zip = FakeZip.Create(_zips, "evil.zip",
            ("dwmapi.dll", "p"),
            ("ue4ss/UE4SS.dll", "c"),
            ("../../evil.txt", "x"));

        Assert.Throws<InvalidDataException>(() => Ue4ssInstaller.Install(zip, _exeDir));
        Assert.False(Exists("dwmapi.dll"));
        Assert.False(File.Exists(Path.Combine(_root, "Game", "Game", "evil.txt")));
    }

    [Fact]
    public void 꺼둔_상태에서_업데이트하면_새_프록시로_켜진다()
    {
        Ue4ssInstaller.Install(FakeZip.Subfolder(_zips, "old"), _exeDir);
        Ue4ssInstaller.SetEnabled(_exeDir, enabled: false);

        var report = Ue4ssInstaller.Install(FakeZip.Subfolder(_zips, "new"), _exeDir);

        Assert.False(Exists("dwmapi.dll.disabled"));
        Assert.Equal("proxy-new", Read("dwmapi.dll"));
        Assert.False(Ue4ssDetector.Detect(_exeDir).IsDisabled);
        Assert.Contains(report.Notes, n => n.Contains("프록시"));
    }

    // ── 콘솔 창 ─────────────────────────────────────────────────

    [Theory]
    [InlineData(true, @"ue4ss\UE4SS-settings.ini")]
    [InlineData(false, "UE4SS-settings.ini")]
    public void 설치하면_콘솔과_GUI_창_설정을_1로_고정한다(bool subfolder, string settingsRel)
    {
        var zip = subfolder ? FakeZip.Subfolder(_zips) : FakeZip.Flat(_zips);

        var report = Ue4ssInstaller.Install(zip, _exeDir);

        var settings = Read(settingsRel);
        Assert.Contains("ConsoleEnabled = 1\r\n", settings);
        Assert.Contains("GuiConsoleEnabled = 1\r\n", settings);
        Assert.Contains("GuiConsoleVisible = 1\r\n", settings);
        Assert.DoesNotContain("= 0", settings);
        Assert.Contains("GraphicsAPI = opengl", settings);   // 다른 값은 그대로
        Assert.Contains(report.Notes, n => n.Contains("콘솔"));
    }

    [Fact]
    public void 이미_켜져_있으면_설정_파일을_다시_쓰지_않는다()
    {
        Ue4ssInstaller.Install(FakeZip.Subfolder(_zips, "old"), _exeDir);
        var path = Path.Combine(_exeDir, "ue4ss", "UE4SS-settings.ini");
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);

        var report = Ue4ssInstaller.Install(FakeZip.Subfolder(_zips, "new"), _exeDir);

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
        Assert.DoesNotContain(report.Notes, n => n.Contains("콘솔"));
    }

    // ── 켜기/끄기 ───────────────────────────────────────────────

    [Fact]
    public void 끄기와_켜기는_프록시_이름만_바꾼다()
    {
        Ue4ssInstaller.Install(FakeZip.Subfolder(_zips), _exeDir);

        Ue4ssInstaller.SetEnabled(_exeDir, enabled: false);
        Assert.True(Exists("dwmapi.dll.disabled"));
        Assert.False(Exists("dwmapi.dll"));
        Assert.True(Ue4ssDetector.Detect(_exeDir).IsDisabled);
        Assert.True(Exists(@"ue4ss\UE4SS.dll"));

        Ue4ssInstaller.SetEnabled(_exeDir, enabled: true);
        Assert.Equal("proxy-new", Read("dwmapi.dll"));
        Assert.False(Ue4ssDetector.Detect(_exeDir).IsDisabled);
    }

    [Fact]
    public void 설치가_없으면_켜기끄기를_거부한다()
    {
        Assert.Throws<InstallBlockedException>(() => Ue4ssInstaller.SetEnabled(_exeDir, enabled: false));
    }

    // ── 제거 ────────────────────────────────────────────────────

    [Fact]
    public void 하위폴더_배치_제거는_프록시와_ue4ss_폴더만_치운다()
    {
        Ue4ssInstaller.Install(FakeZip.Subfolder(_zips), _exeDir);
        FakeLibrary.Create(_exeDir, "Game.pdb", "D3D12/D3D12Core.dll");

        var plan = Ue4ssInstaller.PlanUninstall(_exeDir);
        Assert.Equal(
            [Path.Combine(_exeDir, "dwmapi.dll"), Path.Combine(_exeDir, "ue4ss")],
            plan);

        Ue4ssInstaller.Uninstall(_exeDir, Delete);

        Assert.Equal(Ue4ssLayout.None, Ue4ssDetector.Detect(_exeDir).Layout);
        Assert.True(Exists("Game-Win64-Shipping.exe"));
        Assert.True(Exists("Game.pdb"));
        Assert.True(Exists(@"D3D12\D3D12Core.dll"));
    }

    [Fact]
    public void 꺼진_프록시도_제거한다()
    {
        Ue4ssInstaller.Install(FakeZip.Subfolder(_zips), _exeDir);
        Ue4ssInstaller.SetEnabled(_exeDir, enabled: false);

        Ue4ssInstaller.Uninstall(_exeDir, Delete);

        Assert.False(Exists("dwmapi.dll.disabled"));
        Assert.Equal(Ue4ssLayout.None, Ue4ssDetector.Detect(_exeDir).Layout);
    }

    [Fact]
    public void exe옆_배치_제거는_UE4SS_이름의_항목만_치운다()
    {
        Ue4ssInstaller.Install(FakeZip.Flat(_zips), _exeDir);
        FakeLibrary.Create(_exeDir, "UE4SS.log", "steam_api64.dll", "Game.pdb");

        Ue4ssInstaller.Uninstall(_exeDir, Delete);

        Assert.False(Exists("dwmapi.dll"));
        Assert.False(Exists("UE4SS.dll"));
        Assert.False(Exists("UE4SS-settings.ini"));
        Assert.False(Exists("UE4SS.log"));
        Assert.False(Exists("Mods"));
        Assert.True(Exists("Game-Win64-Shipping.exe"));
        Assert.True(Exists("steam_api64.dll"));
        Assert.True(Exists("Game.pdb"));
    }

    [Fact]
    public void exe옆_배치의_README와_Changelog는_UE4SS_표식이_있을_때만_치운다()
    {
        Ue4ssInstaller.Install(FakeZip.Flat(_zips), _exeDir);
        File.WriteAllText(Path.Combine(_exeDir, "README.md"), "# Unreal Engine 4/5 Scripting System\n... UE4SS docs ...");
        File.WriteAllText(Path.Combine(_exeDir, "Changelog.md"), "Game patch notes 1.0.2");

        var plan = Ue4ssInstaller.PlanUninstall(_exeDir);

        Assert.Contains(Path.Combine(_exeDir, "README.md"), plan);
        Assert.DoesNotContain(Path.Combine(_exeDir, "Changelog.md"), plan);
    }

    [Fact]
    public void 제거할_것이_없으면_거부하고_다른_모드의_dwmapi는_건드리지_않는다()
    {
        Assert.Throws<InstallBlockedException>(() => Ue4ssInstaller.Uninstall(_exeDir, Delete));

        File.WriteAllText(Path.Combine(_exeDir, "dwmapi.dll"), "someone-else");
        Assert.Empty(Ue4ssInstaller.PlanUninstall(_exeDir));
        Assert.Throws<InstallBlockedException>(() => Ue4ssInstaller.Uninstall(_exeDir, Delete));
        Assert.True(Exists("dwmapi.dll"));
    }
}
