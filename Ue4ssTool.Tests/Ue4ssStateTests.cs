using ue4ss_tool;

namespace Ue4ssTool.Tests;

/// <summary>exe 폴더의 파일만 보고 UE4SS 설치 상태를 읽는 부분.</summary>
public class Ue4ssStateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ue4ss-tool-tests", Guid.NewGuid().ToString("N"));

    public Ue4ssStateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private Ue4ssState Detect(params string[] entries)
    {
        FakeLibrary.Create(_dir, entries);
        return Ue4ssDetector.Detect(_dir);
    }

    [Fact]
    public void 아무것도_없으면_미설치()
    {
        var state = Detect("Game-Win64-Shipping.exe");
        Assert.Equal(Ue4ssLayout.None, state.Layout);
        Assert.False(state.IsInstalled);
    }

    [Fact]
    public void 실험판_하위폴더_배치()
    {
        var state = Detect("dwmapi.dll", "ue4ss/UE4SS.dll", "ue4ss/UE4SS-settings.ini",
            "ue4ss/Mods/BPModLoaderMod/", "ue4ss/Mods/Keybinds/", "ue4ss/Mods/shared/");

        Assert.Equal(Ue4ssLayout.Subfolder, state.Layout);
        Assert.Equal(Path.Combine(_dir, "ue4ss"), state.WorkingDir);
        Assert.Equal(Path.Combine(_dir, "ue4ss", "UE4SS-settings.ini"), state.SettingsPath);
        Assert.Equal(2, state.ModFolderCount);   // shared 는 모드가 아니다
        Assert.False(state.IsDisabled);
    }

    [Fact]
    public void 안정판_exe옆_배치()
    {
        var state = Detect("dwmapi.dll", "UE4SS.dll", "UE4SS-settings.ini", "Mods/Keybinds/");
        Assert.Equal(Ue4ssLayout.Flat, state.Layout);
        Assert.Equal(_dir, state.WorkingDir);
    }

    [Fact]
    public void 구버전_xinput_배치()
    {
        var state = Detect("xinput1_3.dll", "UE4SS.dll");
        Assert.Equal(Ue4ssLayout.LegacyXinput, state.Layout);
        Assert.Empty(state.OtherLoaders);   // UE4SS 가 쓰는 xinput1_3 은 "다른 로더"가 아니다
    }

    [Fact]
    public void 프록시에_disabled가_붙으면_꺼진_상태()
    {
        var state = Detect("dwmapi.dll.disabled", "ue4ss/UE4SS.dll");
        Assert.Equal(Ue4ssLayout.Subfolder, state.Layout);
        Assert.True(state.IsDisabled);
        Assert.True(state.IsInstalled);
    }

    [Fact]
    public void UE4SS_없이_dwmapi만_있으면_다른_모드의_프록시()
    {
        var state = Detect("dwmapi.dll");
        Assert.Equal(Ue4ssLayout.ForeignProxy, state.Layout);
        Assert.False(state.IsInstalled);
    }

    [Fact]
    public void 프록시_없이_UE4SS_dll만_있으면_불완전()
    {
        Assert.Equal(Ue4ssLayout.Partial, Detect("ue4ss/UE4SS.dll").Layout);
    }

    [Fact]
    public void 다른_DLL_로더를_알려준다()
    {
        var state = Detect("dwmapi.dll", "ue4ss/UE4SS.dll", "dxgi.dll", "xinput1_3.dll");
        Assert.Equal(["dxgi.dll", "xinput1_3.dll"], state.OtherLoaders.OrderBy(x => x));
    }

    [Theory]
    [InlineData("[12:00:00] UE4SS - v3.0.1 Beta #0 - Git SHA #35d1795d", "v3.0.1")]
    [InlineData("Unreal Engine modding tool 'UE4SS' - v3.0.1-1136-g35d1795d", "v3.0.1-1136-g35d1795d")]
    [InlineData("[12:00:00] Game version 1.2.3", null)]
    public void 로그_첫머리에서_버전을_읽는다(string line, string? expected)
        => Assert.Equal(expected, Ue4ssDetector.VersionFromLogLine(line));

    [Fact]
    public void DLL에_버전이_없으면_로그에서_읽는다()
    {
        var state = Detect("dwmapi.dll", "ue4ss/UE4SS.dll");
        Assert.Null(state.Version);

        File.WriteAllLines(Path.Combine(_dir, "ue4ss", "UE4SS.log"),
            ["[00:00:01] UE4SS - v3.0.1-1136-g35d1795d - Git SHA #35d1795d"]);
        Assert.Equal("v3.0.1-1136-g35d1795d", Ue4ssDetector.Detect(_dir).Version);
    }
}
