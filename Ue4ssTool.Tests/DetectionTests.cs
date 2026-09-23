using ue4ss_tool;

namespace Ue4ssTool.Tests;

/// <summary>게임 폴더 구조만 보고 언리얼 여부·게임 exe·설치 위치를 고르는 부분.</summary>
public class DetectionTests
{
    [Fact]
    public void 표준_UE5_구조에서_Shipping_exe와_설치위치를_찾는다()
    {
        using var lib = new FakeLibrary();
        var exeDir = lib.AddUnrealGame("Exit8", "Exit8",
            "Exit8/Content/Paks/Exit8-Windows.utoc",
            "Exit8/Content/Paks/Exit8-Windows.ucas");

        var game = lib.ScanSingle("Exit8");

        Assert.Equal(exeDir, game.ExeDir);
        Assert.Equal(Path.Combine(exeDir, "Exit8-Win64-Shipping.exe"), game.GameExe);
        Assert.Equal("Win64", game.Platform);
        Assert.Equal("Exit8", game.ProjectName);
        Assert.True(game.IsShippingExe);
        Assert.True(game.UsesIoStore);
        // 빈 exe 라 버전 리소스가 없다 → 세대를 확정하지 않는다.
        Assert.Null(game.Version);
        Assert.Equal(UnrealGeneration.Unknown, game.Generation);
        Assert.Equal(Ue4ssSupport.Unknown, game.Support);
    }

    [Fact]
    public void 설치폴더_바로_아래의_런처_exe는_게임_exe로_고르지_않는다()
    {
        using var lib = new FakeLibrary();
        lib.AddUnrealGame("ABZU", "AbzuGame");

        var game = lib.ScanSingle("ABZU");

        Assert.EndsWith(Path.Combine("AbzuGame", "Binaries", "Win64", "AbzuGame-Win64-Shipping.exe"), game.GameExe);
    }

    [Fact]
    public void Shipping_exe_이름이_프로젝트폴더와_달라도_찾는다()
    {
        // 실제 사례: Platform8\Platform8\Binaries\Win64\Exit8-Win64-Shipping.exe
        using var lib = new FakeLibrary();
        lib.AddGame("Platform8",
            "Platform8.exe",
            "Engine/",
            "Platform8/Binaries/Win64/Exit8-Win64-Shipping.exe",
            "Platform8/Content/Paks/Platform8-Windows.pak");

        var game = lib.ScanSingle("Platform8");

        Assert.EndsWith("Exit8-Win64-Shipping.exe", game.GameExe);
        Assert.True(game.IsShippingExe);
    }

    [Fact]
    public void Shipping이_아닌_빌드는_프로젝트이름_exe를_고르고_표시한다()
    {
        // 실제 사례: Pet Lands\PetLands\Binaries\Win64\PetLands.exe (Development/Test 빌드)
        using var lib = new FakeLibrary();
        lib.AddGame("Pet Lands",
            "PetLands.exe",
            "Engine/",
            "PetLands/Binaries/Win64/PetLands.exe",
            "PetLands/Binaries/Win64/CrashReportClient.exe",
            "PetLands/Content/Paks/PetLands-Windows.pak");

        var game = lib.ScanSingle("Pet Lands");

        Assert.EndsWith(Path.Combine("Win64", "PetLands.exe"), game.GameExe);
        Assert.False(game.IsShippingExe);
    }

    [Fact]
    public void 보조_실행파일만_있는_플랫폼폴더는_게임으로_보지_않는다()
    {
        using var lib = new FakeLibrary();
        lib.AddGame("Broken",
            "Engine/",
            "Broken/Binaries/Win64/CrashReportClient.exe",
            "Broken/Binaries/Win64/UnrealCEFSubProcess.exe",
            "Broken/Content/Paks/Broken-Windows.pak");

        Assert.Empty(lib.Scan().Games);
    }

    [Fact]
    public void Engine_폴더의_exe는_후보가_아니다()
    {
        using var lib = new FakeLibrary();
        lib.AddUnrealGame("Game", "MyGame",
            "Engine/Binaries/Win64/CrashReportClient.exe",
            "Engine/Binaries/Win64/Big.exe",
            "Engine/Content/Paks/engine.pak");

        var game = lib.ScanSingle("Game");

        Assert.Equal("MyGame", game.ProjectName);
    }

    [Fact]
    public void 한_단계_더_깊은_프로젝트도_찾는다()
    {
        using var lib = new FakeLibrary();
        lib.AddGame("Deep",
            "Windows/Engine/",
            "Windows/DeepGame/Binaries/Win64/DeepGame-Win64-Shipping.exe",
            "Windows/DeepGame/Content/Paks/DeepGame-Windows.pak");

        var game = lib.ScanSingle("Deep");

        Assert.Equal("DeepGame", game.ProjectName);
        Assert.EndsWith(Path.Combine("Windows", "DeepGame", "Binaries", "Win64"), game.ExeDir);
    }

    [Fact]
    public void GamePass_WinGDK_폴더도_찾는다()
    {
        using var lib = new FakeLibrary();
        lib.AddGame("Gdk",
            "Engine/",
            "Gdk/Binaries/WinGDK/Gdk-WinGDK-Shipping.exe",
            "Gdk/Content/Paks/Gdk-WinGDK.pak");

        var game = lib.ScanSingle("Gdk");

        Assert.Equal("WinGDK", game.Platform);
        Assert.True(game.IsShippingExe);
    }

    [Fact]
    public void pak_없이_풀어둔_게임도_Engine_폴더가_있으면_찾는다()
    {
        using var lib = new FakeLibrary();
        lib.AddGame("Loose",
            "Engine/Content/",
            "Loose/Binaries/Win64/Loose-Win64-Shipping.exe",
            "Loose/Content/Maps/Main.umap");

        Assert.Equal("Loose", lib.ScanSingle("Loose").ProjectName);
    }

    [Fact]
    public void Binaries와_Content만_있고_언리얼_흔적이_없으면_제외한다()
    {
        using var lib = new FakeLibrary();
        lib.AddGame("OtherEngine",
            "OtherEngine/Binaries/Win64/OtherEngine.exe",
            "OtherEngine/Content/data.bin");

        Assert.Empty(lib.Scan().Games);
    }

    [Fact]
    public void Unity_게임은_목록에_없다()
    {
        using var lib = new FakeLibrary();
        lib.AddGame("UnityGame",
            "UnityGame.exe",
            "UnityPlayer.dll",
            "UnityGame_Data/Managed/Assembly-CSharp.dll",
            "MonoBleedingEdge/");
        lib.AddUnrealGame("Real", "Real");

        var result = lib.Scan();

        Assert.Equal(2, result.ScannedFolderCount);
        Assert.Equal("Real", Assert.Single(result.Games).Name);
    }

    [Fact]
    public void UE3_구조는_UE3로_표시하고_미지원으로_둔다()
    {
        // 실제 사례: HatinTime\Binaries\Win64\HatinTimeGame.exe + HatinTime\HatinTimeGame\CookedPC
        using var lib = new FakeLibrary();
        lib.AddGame("HatinTime",
            "Binaries/Win32/",
            "Binaries/Win64/HatinTimeGame.exe",
            "Binaries/Win64/UE3ShaderCompileWorker.exe",
            "Engine/Config/",
            "HatinTimeGame/CookedPC/Maps/");

        var game = lib.ScanSingle("HatinTime");

        Assert.Equal(UnrealGeneration.UE3, game.Generation);
        Assert.EndsWith("HatinTimeGame.exe", game.GameExe);
        Assert.Equal(Ue4ssSupport.Unsupported, game.Support);
    }

    [Fact]
    public void 안티치트_폴더를_감지한다()
    {
        using var lib = new FakeLibrary();
        lib.AddUnrealGame("Eac", "Eac", "EasyAntiCheat/", "start_protected_game.exe");
        lib.AddUnrealGame("Be", "Be", "Be/Binaries/Win64/BattlEye/");
        lib.AddUnrealGame("Clean", "Clean");

        var games = lib.Scan().Games.ToDictionary(g => g.Name);

        Assert.Equal(AntiCheat.EasyAntiCheat, games["Eac"].AntiCheat);
        Assert.Equal(AntiCheat.BattlEye, games["Be"].AntiCheat);
        Assert.Equal(AntiCheat.None, games["Clean"].AntiCheat);
    }

    [Fact]
    public void appmanifest가_있으면_스토어_이름을_붙인다()
    {
        using var lib = new FakeLibrary();
        lib.AddUnrealGame("Tetris Effect Connected", "TetrisEffect");
        lib.AddManifest(1003590, "Tetris® Effect: Connected", "Tetris Effect Connected");

        var game = lib.ScanSingle("Tetris Effect Connected");

        Assert.Equal(1003590u, game.AppId);
        Assert.Equal("Tetris® Effect: Connected", game.DisplayName);
    }

    [Fact]
    public void 게임_폴더_하나를_직접_지정해도_스캔한다()
    {
        using var lib = new FakeLibrary();
        lib.AddUnrealGame("Solo", "Solo");

        var result = UnrealScanner.Scan([Path.Combine(lib.CommonDir, "Solo")]);

        Assert.Equal("Solo", Assert.Single(result.Games).Name);
    }

    [Fact]
    public void 스캔_결과에_기존_UE4SS_상태가_들어간다()
    {
        using var lib = new FakeLibrary();
        var exeDir = lib.AddUnrealGame("Modded", "Modded");
        FakeLibrary.Create(exeDir, "dwmapi.dll", "ue4ss/UE4SS.dll");

        var result = lib.Scan();

        Assert.Equal(Ue4ssLayout.Subfolder, result.Games.Single().Ue4ss.Layout);
        Assert.Equal(1, result.InstalledCount);
        Assert.Single(UnrealScanner.Filter(result, GameView.Ue4ssInstalled));
        Assert.Empty(UnrealScanner.Filter(result, GameView.Ue4ssNotInstalled));
    }
}
