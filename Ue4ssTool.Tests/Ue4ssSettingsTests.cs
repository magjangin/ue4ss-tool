using System.Text;
using ue4ss_tool;

namespace Ue4ssTool.Tests;

/// <summary>UE4SS-settings.ini 의 콘솔 창 설정 고정.</summary>
public class Ue4ssSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ue4ss-tool-tests", Guid.NewGuid().ToString("N"));

    public Ue4ssSettingsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>실험판 기본 패키지(UE4SS_v3.0.1-1136)의 [Debug] 앞부분. 줄바꿈은 LF 다.</summary>
    private const string RealDebug =
        "[General]\n" +
        "UseCache = 1\n" +
        "\n" +
        "[Debug]\n" +
        "; Whether to enable the external UE4SS debug console.\n" +
        "ConsoleEnabled = 0\n" +
        "GuiConsoleEnabled = 0\n" +
        "GuiConsoleVisible = 0\n" +
        "\n" +
        "; Default: opengl\n" +
        "GraphicsAPI = opengl\n" +
        "\n" +
        "[Threads]\n" +
        "SigScannerNumThreads = 8\n";

    [Fact]
    public void 기본_패키지의_꺼진_값을_1로_바꾸고_다른_줄은_그대로_둔다()
    {
        var result = Ue4ssSettings.ForceConsoleOn(RealDebug, out var changed);

        Assert.True(changed);
        Assert.Equal(
            RealDebug
                .Replace("ConsoleEnabled = 0", "ConsoleEnabled = 1")
                .Replace("GuiConsoleVisible = 0", "GuiConsoleVisible = 1"),
            result);
    }

    [Fact]
    public void CRLF와_들여쓰기와_키_대소문자를_지킨다()
    {
        var text = "[debug]\r\n  consoleenabled=0\r\nGuiConsoleEnabled = 1\r\nGuiConsoleVisible = 0";

        var result = Ue4ssSettings.ForceConsoleOn(text, out _);

        Assert.Equal("[debug]\r\n  consoleenabled = 1\r\nGuiConsoleEnabled = 1\r\nGuiConsoleVisible = 1", result);
    }

    [Fact]
    public void 이미_모두_켜져_있으면_바꾸지_않는다()
    {
        var on = Ue4ssSettings.ForceConsoleOn(RealDebug, out _);

        var again = Ue4ssSettings.ForceConsoleOn(on, out var changed);

        Assert.False(changed);
        Assert.Equal(on, again);
    }

    [Fact]
    public void 빠진_키는_Debug_바로_아래에_넣는다()
    {
        var text = "[Debug]\nConsoleEnabled = 0\nGraphicsAPI = dx11\n[Threads]\nX = 1\n";

        var result = Ue4ssSettings.ForceConsoleOn(text, out var changed);

        Assert.True(changed);
        Assert.Equal(
            "[Debug]\nGuiConsoleEnabled = 1\nGuiConsoleVisible = 1\nConsoleEnabled = 1\nGraphicsAPI = dx11\n[Threads]\nX = 1\n",
            result);
    }

    [Fact]
    public void Debug_섹션이_없으면_끝에_붙인다()
    {
        var result = Ue4ssSettings.ForceConsoleOn("[General]\nUseCache = 1\n", out var changed);

        Assert.True(changed);
        Assert.Equal(
            "[General]\nUseCache = 1\n\n[Debug]\nConsoleEnabled = 1\nGuiConsoleEnabled = 1\nGuiConsoleVisible = 1\n",
            result);
    }

    [Fact]
    public void Debug_밖의_같은_이름과_주석은_건드리지_않는다()
    {
        var text = "[Other]\nConsoleEnabled = 0\n[Debug]\n; ConsoleEnabled = 0\nConsoleEnabled = 0\nGuiConsoleEnabled = 0\nGuiConsoleVisible = 0\n";

        var result = Ue4ssSettings.ForceConsoleOn(text, out _);

        Assert.Equal(
            "[Other]\nConsoleEnabled = 0\n[Debug]\n; ConsoleEnabled = 0\nConsoleEnabled = 1\nGuiConsoleEnabled = 1\nGuiConsoleVisible = 1\n",
            result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 파일은_BOM_유무를_그대로_두고_바뀐_게_없으면_쓰지_않는다(bool bom)
    {
        var path = Path.Combine(_root, "UE4SS-settings.ini");
        File.WriteAllText(path, RealDebug, new UTF8Encoding(bom));

        Assert.True(Ue4ssSettings.ForceConsoleOn(path));

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(bom, bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.Contains("GuiConsoleVisible = 1\n", File.ReadAllText(path));

        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);
        Assert.False(Ue4ssSettings.ForceConsoleOn(path));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
    }
}
