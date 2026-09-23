using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ue4ss_tool;

/// <summary>설치된 앱 하나에 대한 Steam 쪽 정보.</summary>
public sealed class SteamAppEntry
{
    public required uint AppId { get; init; }
    /// <summary>Steam 스토어 표기 이름.</summary>
    public required string Name { get; init; }
    /// <summary>steamapps\common 아래의 폴더 이름.</summary>
    public required string InstallDir { get; init; }
}

/// <summary>
/// 각 라이브러리의 <c>steamapps\appmanifest_*.acf</c> 에서 읽은 설치 앱 목록.
/// 목록에 폴더명 대신 스토어 이름을 보여 주기 위해서만 쓴다. 읽지 못해도 스캔은 동작한다.
/// </summary>
public sealed class SteamCatalog
{
    private readonly Dictionary<string, SteamAppEntry> _byInstallDir =
        new(StringComparer.OrdinalIgnoreCase);

    public static SteamCatalog Empty { get; } = new();

    /// <summary>appmanifest 를 하나라도 읽어냈는가.</summary>
    public bool HasApps => _byInstallDir.Count > 0;

    /// <summary>설치 폴더 이름으로 앱을 찾는다. 없으면 null.</summary>
    public SteamAppEntry? Find(string installDirName) =>
        _byInstallDir.TryGetValue(installDirName, out var e) ? e : null;

    /// <param name="libraryRoots">라이브러리 루트(=steamapps 의 부모) 경로들.</param>
    /// <param name="onWarning">읽기 실패를 알리는 콜백.</param>
    public static SteamCatalog Build(IEnumerable<string> libraryRoots, Action<string>? onWarning = null)
    {
        var catalog = new SteamCatalog();

        foreach (var lib in libraryRoots)
        {
            var steamapps = Path.Combine(lib, "steamapps");
            string[] manifests;
            try { manifests = Directory.GetFiles(steamapps, "appmanifest_*.acf"); }
            catch (Exception ex) { onWarning?.Invoke($"{steamapps}: {ex.Message}"); continue; }

            foreach (var file in manifests)
            {
                try
                {
                    var text = File.ReadAllText(file);
                    var idText = AcfValue(text, "appid");
                    var name = AcfValue(text, "name");
                    var dir = AcfValue(text, "installdir");
                    if (idText is null || dir is null) continue;
                    if (!uint.TryParse(idText, out var appid)) continue;

                    // 사운드트랙 DLC 처럼 여러 앱이 한 폴더를 공유하면 먼저 읽은 것을 유지한다.
                    catalog._byInstallDir.TryAdd(dir, new SteamAppEntry
                    {
                        AppId = appid,
                        Name = name ?? dir,
                        InstallDir = dir,
                    });
                }
                catch (Exception ex) { onWarning?.Invoke($"{Path.GetFileName(file)}: {ex.Message}"); }
            }
        }

        return catalog;
    }

    /// <summary>ACF(텍스트 VDF)에서 최상위 키의 값을 뽑는다. 중첩 블록은 다루지 않는다.</summary>
    internal static string? AcfValue(string text, string key)
    {
        var needle = '"' + key + '"';
        var i = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        i = text.IndexOf('"', i + needle.Length);
        if (i < 0) return null;
        var j = text.IndexOf('"', i + 1);
        return j < 0 ? null : text.Substring(i + 1, j - i - 1);
    }
}

public static class GameNames
{
    /// <summary>정렬용 키. 공백·NFC·아포스트로피를 정규화한다.</summary>
    public static string Normalize(string name) => Regex.Replace(
        name.Normalize(NormalizationForm.FormC).Replace('‘', '\'').Replace('’', '\''),
        @"\s+", " ").Trim();
}
