using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ue4ss_tool;

/// <summary>
/// 스캔할 폴더를 찾는 쪽. 레지스트리와 libraryfolders.vdf 만 읽고,
/// 게임 폴더 안을 들여다보는 일은 <see cref="UnrealInspector"/> 가 맡는다.
/// </summary>
public static class SteamLibraries
{
    /// <summary>레지스트리/기본 경로에서 Steam 설치 폴더를 찾는다.</summary>
    public static string? FindSteamPath()
    {
        foreach (var (hive, sub, val) in new[]
        {
            (Registry.CurrentUser,  @"Software\Valve\Steam",             "SteamPath"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            (Registry.LocalMachine, @"SOFTWARE\Valve\Steam",             "InstallPath"),
        })
        {
            try
            {
                using var key = hive.OpenSubKey(sub);
                if (key?.GetValue(val) is string p && Directory.Exists(p))
                    return Path.GetFullPath(p);
            }
            catch { /* 무시하고 다음 후보 */ }
        }
        return null;
    }

    /// <summary>
    /// 폴더가 하위 게임들을 담는 컨테이너(common)가 아니라 그 자체로 언리얼 게임 설치 폴더인지 판별한다.
    /// 드라이브 루트에 따로 보존해 둔 게임이나, 사용자가 게임 폴더 하나를 직접 지정한 경우에 쓴다.
    /// </summary>
    public static bool IsGameDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return false;
        var dirName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(dirName, "common", StringComparison.OrdinalIgnoreCase))
            return false;

        return UnrealInspector.LooksLikeGameRoot(dir);
    }

    /// <summary>드라이브 루트에 별도로 보존해 둔 독립 언리얼 게임 폴더를 찾는다.</summary>
    public static List<string> FindPreservedGameFolders()
    {
        var preserved = new List<string>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                string[] rootDirs;
                try { rootDirs = Directory.GetDirectories(drive.RootDirectory.FullName); }
                catch { continue; }

                foreach (var dir in rootDirs)
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith('$') ||
                        name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("ProgramData", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Program Files", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Users", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("source", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("steam", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("SteamLibrary", StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        if (IsGameDirectory(dir)) preserved.Add(dir);
                    }
                    catch { /* 권한 없는 폴더 등은 건너뛴다 */ }
                }
            }
        }
        catch { }
        return preserved;
    }

    /// <summary>
    /// 스캔할 steamapps\common 폴더들과 Steam 외부 보존본 게임 폴더들을 찾는다.
    /// libraryfolders.vdf 의 모든 라이브러리 + 흔한 위치 + 드라이브 루트의 언리얼 보존본을 후보로 모은다.
    /// </summary>
    public static List<string> FindCommonFolders()
    {
        var libs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddLib(string? libRoot)
        {
            if (string.IsNullOrWhiteSpace(libRoot)) return;
            try
            {
                var full = NormalizeDrive(Path.GetFullPath(libRoot));
                if (Directory.Exists(full))
                    libs.Add(full);
            }
            catch { }
        }

        // 1. 레지스트리 경로
        AddLib(FindSteamPath());

        // 2. 각 드라이브의 기본 Steam 설치 위치 및 흔한 라이브러리 위치
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                var root = drive.RootDirectory.FullName;
                AddLib(Path.Combine(root, "Program Files (x86)", "Steam"));
                AddLib(Path.Combine(root, "Program Files", "Steam"));
                AddLib(Path.Combine(root, "Steam"));
                AddLib(Path.Combine(root, "SteamLibrary"));
                AddLib(Path.Combine(root, "Games", "Steam"));
            }
        }
        catch { }

        // 3. libraryfolders.vdf 를 따라가며 등록된 모든 라이브러리 경로 수집
        var checkedVdfs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(libs);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var vdf in new[]
            {
                Path.Combine(cur, "steamapps", "libraryfolders.vdf"),
                Path.Combine(cur, "config", "libraryfolders.vdf"),
            })
            {
                if (!File.Exists(vdf) || !checkedVdfs.Add(vdf)) continue;
                try
                {
                    var text = File.ReadAllText(vdf);
                    foreach (Match m in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\""))
                    {
                        var parsed = NormalizeDrive(m.Groups[1].Value.Replace("\\\\", "\\"));
                        if (Directory.Exists(parsed) && libs.Add(parsed))
                            queue.Enqueue(parsed);
                    }
                }
                catch { }
            }
        }

        // 4. 각 라이브러리에서 실제로 존재하는 common 폴더만 반환
        var results = new List<string>();
        foreach (var lib in libs)
        {
            var common = Path.Combine(lib, "steamapps", "common");
            if (Directory.Exists(common) && !results.Any(x => string.Equals(x, common, StringComparison.OrdinalIgnoreCase)))
                results.Add(common);
        }

        // 5. Steam 외부에 보존된 독립 언리얼 게임 폴더들도 함께 반환
        foreach (var preserved in FindPreservedGameFolders())
        {
            if (!results.Any(x => string.Equals(x, preserved, StringComparison.OrdinalIgnoreCase)))
                results.Add(preserved);
        }

        return results;
    }

    /// <summary>레지스트리의 SteamPath 는 <c>h:/steam</c> 처럼 소문자 드라이브라, 화면 표시를 위해 대문자로 맞춘다.</summary>
    internal static string NormalizeDrive(string path) =>
        path.Length >= 2 && path[1] == ':' && char.IsLower(path[0])
            ? char.ToUpperInvariant(path[0]) + path[1..]
            : path;

    /// <summary>
    /// steamapps\common 경로에서 라이브러리 루트를 되짚는다.
    /// (…\SteamLibrary\steamapps\common → …\SteamLibrary)
    /// 구조가 다르면 null.
    /// </summary>
    internal static string? LibraryRootOf(string commonDir)
    {
        try
        {
            var steamapps = Directory.GetParent(commonDir);
            if (steamapps is null) return null;
            if (!steamapps.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase)) return null;
            return steamapps.Parent?.FullName;
        }
        catch { return null; }
    }
}
