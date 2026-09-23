using System.Text;
using ue4ss_tool;

namespace Ue4ssTool.Tests;

/// <summary>엔진 버전 읽기와 UE4SS 지원 범위 판정.</summary>
public class EngineVersionTests
{
    [Theory]
    [InlineData(4, 12, true)]    // ABZU
    [InlineData(4, 27, true)]
    [InlineData(5, 5, true)]
    [InlineData(5, 20, true)]
    [InlineData(4, 28, false)]
    [InlineData(1, 0, false)]    // A Hat in Time 의 게임 버전 1.0.8767
    [InlineData(0, 0, false)]    // 버전 리소스 없음
    [InlineData(6, 0, false)]
    public void exe_숫자버전이_엔진버전으로_그럴듯한지_판단한다(int major, int minor, bool expected)
        => Assert.Equal(expected, EngineVersion.IsPlausible(major, minor));

    [Theory]
    [InlineData("++UE5+Release-5.5-CL-40574608", 5, 5)]
    [InlineData("++UE4+Release-4.27", 4, 27)]
    [InlineData("4.26.2-0+++UE4+Release-4.26", 4, 26)]
    public void 브랜치_문자열에서_버전을_뽑는다(string text, int major, int minor)
        => Assert.Equal((major, minor), UnrealInspector.BranchVersion(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1, 0, 8767, 0")]
    [InlineData("++Game+Main")]
    public void 브랜치_문자열이_아니면_null(string? text)
        => Assert.Null(UnrealInspector.BranchVersion(text));

    [Fact]
    public void exe_바이트에서_UTF16_브랜치_문자열을_찾는다_청크_경계에_걸쳐도()
    {
        var path = Path.GetTempFileName();
        try
        {
            // 4MB 청크 경계에 문자열이 걸치도록 배치한다.
            var marker = Encoding.Unicode.GetBytes("++UE5+Release-5.2");
            var data = new byte[(4 << 20) + 4096];
            new Random(1).NextBytes(data);
            Array.Clear(data, 0, 64);   // 우연한 일치 방지용이 아니라 가독성용
            marker.CopyTo(data, (4 << 20) - 10);
            File.WriteAllBytes(path, data);

            Assert.Equal((5, 2), UnrealInspector.ScanBranchString(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void exe_바이트에서_ASCII_브랜치_문자열도_찾는다()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [.. new byte[1000], .. Encoding.ASCII.GetBytes("xx++UE4+Release-4.26\0"), .. new byte[100]]);
            Assert.Equal((4, 26), UnrealInspector.ScanBranchString(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 브랜치_문자열이_없으면_null()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[10_000]);
            Assert.Null(UnrealInspector.ScanBranchString(path));
            Assert.Null(UnrealInspector.ReadEngineVersion(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(4, 6, Ue4ssSupport.Unsupported)]
    [InlineData(4, 7, Ue4ssSupport.ExperimentalOnly)]
    [InlineData(4, 10, Ue4ssSupport.ExperimentalOnly)]
    [InlineData(4, 11, Ue4ssSupport.Supported)]
    [InlineData(4, 27, Ue4ssSupport.Supported)]
    [InlineData(5, 3, Ue4ssSupport.Supported)]
    [InlineData(5, 4, Ue4ssSupport.ExperimentalOnly)]
    [InlineData(5, 8, Ue4ssSupport.ExperimentalOnly)]
    [InlineData(5, 9, Ue4ssSupport.Unknown)]
    public void UE4SS_지원_범위(int major, int minor, Ue4ssSupport expected)
        => Assert.Equal(expected, Ue4ssCompatibility.For(major, minor));

    [Fact]
    public void Shipping이_아닌_빌드는_지원_버전이어도_실험판만()
    {
        var game = new UnrealGame
        {
            Name = "PetLands",
            InstallDir = @"C:\x",
            ExeDir = @"C:\x\PetLands\Binaries\Win64",
            Version = new EngineVersion(5, 3, 2, "test"),
            Generation = UnrealGeneration.UE5,
            IsShippingExe = false,
        };
        Assert.Equal(Ue4ssSupport.ExperimentalOnly, game.Support);

        game.IsShippingExe = true;
        Assert.Equal(Ue4ssSupport.Supported, game.Support);
    }

    [Fact]
    public void 배지에_엔진버전과_UE4SS_상태가_나온다()
    {
        var game = new UnrealGame
        {
            Name = "G",
            InstallDir = @"C:\g",
            ExeDir = @"C:\g\G\Binaries\Win64",
            Version = new EngineVersion(5, 4, 4, "test"),
            Generation = UnrealGeneration.UE5,
            AntiCheat = AntiCheat.EasyAntiCheat,
            Ue4ss = new Ue4ssState { Layout = Ue4ssLayout.Subfolder, IsDisabled = true },
        };

        Assert.Equal(
            ["UE5.4", "UE4SS 꺼짐", "안티치트"],
            game.Tags.Select(t => t.Text));
    }
}
