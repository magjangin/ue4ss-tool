using System.IO.Compression;
using ue4ss_tool;

namespace Ue4ssTool.Tests;

/// <summary>
/// 임시 폴더에 가짜 steamapps\common 을 만들어 주는 테스트 픽스처.
/// 실제 파일시스템을 쓰므로 스캐너·설치기를 손대지 않고 그대로 검증할 수 있다.
/// </summary>
public sealed class FakeLibrary : IDisposable
{
    public string Root { get; }

    /// <summary>UnrealScanner.Scan 에 넘길 common 폴더 경로.</summary>
    public string CommonDir { get; }

    public FakeLibrary()
    {
        Root = Path.Combine(Path.GetTempPath(), "ue4ss-tool-tests", Guid.NewGuid().ToString("N"));
        CommonDir = Path.Combine(Root, "steamapps", "common");
        Directory.CreateDirectory(CommonDir);
    }

    /// <summary>
    /// 게임 폴더를 만들고 그 아래에 항목들을 생성한다.
    /// '/' 로 끝나는 항목은 폴더, 나머지는 빈 파일로 만든다.
    /// </summary>
    public string AddGame(string name, params string[] entries)
    {
        var gameDir = Path.Combine(CommonDir, name);
        Directory.CreateDirectory(gameDir);
        Create(gameDir, entries);
        return gameDir;
    }

    public static void Create(string baseDir, params string[] entries)
    {
        foreach (var entry in entries)
        {
            var isDirectory = entry.EndsWith('/');
            var full = Path.Combine(baseDir, entry.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
            if (isDirectory)
            {
                Directory.CreateDirectory(full);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllBytes(full, []);
            }
        }
    }

    /// <summary>표준 UE4/5 패키징 구조의 게임을 만든다. exe 폴더 경로를 돌려준다.</summary>
    public string AddUnrealGame(string name, string project, params string[] extra)
    {
        var dir = AddGame(name,
            $"{project}.exe",
            "Engine/Binaries/ThirdParty/",
            $"{project}/Binaries/Win64/{project}-Win64-Shipping.exe",
            $"{project}/Content/Paks/{project}-Windows.pak");
        Create(dir, extra);
        return Path.Combine(dir, project, "Binaries", "Win64");
    }

    /// <summary>appmanifest 를 써서 스토어 이름을 붙인다.</summary>
    public void AddManifest(uint appId, string storeName, string installDir)
    {
        var path = Path.Combine(Root, "steamapps", $"appmanifest_{appId}.acf");
        File.WriteAllText(path, $$"""
            "AppState"
            {
                "appid"		"{{appId}}"
                "name"		"{{storeName}}"
                "installdir"		"{{installDir}}"
            }
            """);
    }

    public ScanResult Scan() => UnrealScanner.Scan([CommonDir]);

    public UnrealGame ScanSingle(string name) => Scan().Games.Single(g => g.Name == name);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch { /* 임시 폴더 정리 실패는 테스트 결과와 무관 */ }
    }
}

/// <summary>테스트용 UE4SS zip 을 만든다.</summary>
public static class FakeZip
{
    /// <summary>항목 이름 → 내용. 내용이 null 이면 폴더 항목.</summary>
    public static string Create(string dir, string fileName, params (string Name, string? Content)[] entries)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = zip.CreateEntry(name);
            if (content is null) continue;
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
        return path;
    }

    /// <summary>기본 패키지의 설정 파일처럼 콘솔 창이 모두 꺼져 있다. 첫 줄로 패키지 기본값인지 알아본다.</summary>
    public const string DefaultSettings =
        "; settings-default\r\n[General]\r\nUseCache = 1\r\n\r\n[Debug]\r\n" +
        "ConsoleEnabled = 0\r\nGuiConsoleEnabled = 0\r\nGuiConsoleVisible = 0\r\nGraphicsAPI = opengl\r\n";

    /// <summary>실험판 배치: dwmapi.dll + ue4ss\…</summary>
    public static string Subfolder(string dir, string version = "new") => Create(dir, $"UE4SS_sub_{version}.zip",
        ("dwmapi.dll", "proxy-" + version),
        ("ue4ss/UE4SS.dll", "core-" + version),
        ("ue4ss/UE4SS-settings.ini", DefaultSettings),
        ("ue4ss/Mods/mods.txt", "mods-default"),
        ("ue4ss/Mods/BPModLoaderMod/Scripts/main.lua", "lua-" + version),
        ("ue4ss/Mods/shared/", null));

    /// <summary>안정판 v3.0.x 배치: 모두 exe 옆에.</summary>
    public static string Flat(string dir, string version = "301") => Create(dir, $"UE4SS_flat_{version}.zip",
        ("dwmapi.dll", "proxy-" + version),
        ("UE4SS.dll", "core-" + version),
        ("UE4SS-settings.ini", DefaultSettings),
        ("Mods/mods.txt", "mods-default"),
        ("Mods/Keybinds/Scripts/main.lua", "lua-" + version));
}
