using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ue4ss_tool;

/// <summary>목록 보기 필터.</summary>
public enum GameView
{
    AllUnreal,
    UE5,
    UE4,
    UnknownVersion,
    UE3,
    Ue4ssInstalled,
    Ue4ssNotInstalled,
    AntiCheat,
}

public sealed class ScanResult
{
    /// <summary>언리얼로 판별된 게임만 담는다.</summary>
    public List<UnrealGame> Games { get; } = new();
    public List<string> Roots { get; } = new();

    /// <summary>검사한 게임 폴더 수(언리얼이 아닌 것 포함).</summary>
    public int ScannedFolderCount { get; set; }

    /// <summary>스캔 중 삼킨 오류들. 비어 있지 않으면 결과가 불완전할 수 있다.</summary>
    public List<string> Warnings { get; } = new();

    public int CountOf(UnrealGeneration generation) => Games.Count(g => g.Generation == generation);
    public int InstalledCount => Games.Count(g => g.Ue4ss.IsInstalled);
}

/// <summary>
/// 스캔의 조립 지점. 폴더 찾기(<see cref="SteamLibraries"/>), 앱 이름(<see cref="SteamCatalog"/>),
/// 폴더 내부 검사(<see cref="UnrealInspector"/>, <see cref="Ue4ssDetector"/>)를 엮는다.
/// </summary>
public static class UnrealScanner
{
    public static List<string> FindCommonFolders() => SteamLibraries.FindCommonFolders();

    /// <summary>지정한 common 폴더(또는 단일 게임 폴더)들을 스캔한다. roots 가 비면 자동 감지.</summary>
    public static ScanResult Scan(
        IEnumerable<string>? roots = null,
        Action<string>? progress = null,
        SteamCatalog? catalog = null)
    {
        var result = new ScanResult();
        var rootList = (roots ?? FindCommonFolders()).Where(Directory.Exists).ToList();
        if (rootList.Count == 0)
        {
            progress?.Invoke("스캔할 steamapps\\common 폴더를 찾지 못했습니다.");
            return result;
        }

        if (catalog is null)
        {
            progress?.Invoke("Steam 앱 목록을 읽는 중...");
            catalog = SteamCatalog.Build(
                rootList.Select(SteamLibraries.LibraryRootOf).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase),
                w => result.Warnings.Add(w));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in rootList)
        {
            result.Roots.Add(root);

            string[] dirs;
            if (SteamLibraries.IsGameDirectory(root))
            {
                dirs = new[] { root };
            }
            else
            {
                try { dirs = Directory.GetDirectories(root); }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{root}: {ex.Message}");
                    continue;
                }
            }
            dirs = dirs.Where(d => seen.Add(Path.GetFullPath(d))).ToArray();
            result.ScannedFolderCount += dirs.Length;

            var gate = new object();
            Parallel.ForEach(dirs, dir =>
            {
                var name = Path.GetFileName(dir);
                progress?.Invoke($"검사 중: {name}");

                var inspection = UnrealInspector.Inspect(dir);
                if (inspection.Error is not null)
                {
                    lock (gate) result.Warnings.Add($"{name}: {inspection.Error}");
                }
                if (!inspection.IsUnreal) return;

                var game = new UnrealGame
                {
                    Name = name,
                    InstallDir = dir,
                    Generation = inspection.Generation,
                    Version = inspection.Version,
                    ProjectName = inspection.ProjectName,
                    ProjectDir = inspection.ProjectDir,
                    ExeDir = inspection.ExeDir,
                    GameExe = inspection.GameExe,
                    Platform = inspection.Platform,
                    IsShippingExe = inspection.IsShippingExe,
                    UsesIoStore = inspection.UsesIoStore,
                    AntiCheat = inspection.AntiCheat,
                    Ue4ss = Ue4ssDetector.Detect(inspection.ExeDir),
                };
                if (catalog.Find(name) is { } entry)
                {
                    game.AppId = entry.AppId;
                    game.StoreName = entry.Name;
                }

                lock (gate) result.Games.Add(game);
            });
        }

        result.Games.Sort((a, b) => string.Compare(a.NameKey, b.NameKey, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    public static IEnumerable<UnrealGame> Filter(ScanResult result, GameView view) => view switch
    {
        GameView.UE5 => result.Games.Where(g => g.Generation == UnrealGeneration.UE5),
        GameView.UE4 => result.Games.Where(g => g.Generation == UnrealGeneration.UE4),
        GameView.UnknownVersion => result.Games.Where(g => g.Generation == UnrealGeneration.Unknown),
        GameView.UE3 => result.Games.Where(g => g.Generation == UnrealGeneration.UE3),
        GameView.Ue4ssInstalled => result.Games.Where(g => g.Ue4ss.Layout != Ue4ssLayout.None),
        GameView.Ue4ssNotInstalled => result.Games.Where(g => g.Ue4ss.Layout == Ue4ssLayout.None && g.Support != Ue4ssSupport.Unsupported),
        GameView.AntiCheat => result.Games.Where(g => g.AntiCheat != AntiCheat.None),
        _ => result.Games,
    };
}
