using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ue4ss_tool;

/// <summary>설치할 수 있는 UE4SS 패키지(릴리스의 zip 파일 하나).</summary>
public sealed record Ue4ssPackage(
    string Tag,
    bool IsExperimental,
    bool IsDev,
    string AssetName,
    long Size,
    DateTimeOffset Updated,
    string DownloadUrl)
{
    /// <summary><c>UE4SS_v3.0.1-1136-g35d1795d.zip</c> → <c>v3.0.1-1136-g35d1795d</c>.</summary>
    public string VersionText
    {
        get
        {
            var name = Path.GetFileNameWithoutExtension(AssetName);
            var i = name.IndexOf("_v", StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? name[(i + 1)..] : name;
        }
    }

    public string Label => $"{(IsExperimental ? "실험판" : "안정판")}{(IsDev ? " 개발자용(zDEV)" : "")} {VersionText}";

    public string Details =>
        $"{Updated.ToLocalTime():yyyy-MM-dd} · {Size / 1024d / 1024d:0.0}MB · " +
        (IsExperimental ? "UE 4.7~5.8 · ue4ss 하위 폴더 배치" : "UE 4.11~5.3 · exe 옆 배치") +
        (IsDev ? " · 콘솔 표시·디버그 파일 포함" : "");

    /// <summary>
    /// 받기 전에 짐작하는 zip 배치. 실험판은 2025 년부터 ue4ss 하위 폴더, 안정판 v3.0.x 는 exe 옆이다.
    /// 모르는 버전이면 None — 실제 배치는 받은 뒤 <see cref="Ue4ssInstaller.Analyze(string)"/> 가 정한다.
    /// </summary>
    public Ue4ssLayout ExpectedLayout =>
        IsExperimental ? Ue4ssLayout.Subfolder
        : VersionText.StartsWith("v3.0", StringComparison.OrdinalIgnoreCase) ? Ue4ssLayout.Flat
        : Ue4ssLayout.None;

    public override string ToString() => Label;
}

/// <summary>
/// RE-UE4SS 의 GitHub 릴리스에서 설치 패키지를 찾고 받아 둔다.
/// 네트워크는 사용자가 목록을 불러오거나 설치할 때만 쓴다.
/// </summary>
public static class Ue4ssReleases
{
    public const string Repo = "UE4SS-RE/RE-UE4SS";
    public const string ExperimentalTag = "experimental-latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ue4ss-tool/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>받은 zip 을 두는 곳. 같은 파일을 다시 받지 않는다.</summary>
    public static string CacheDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ue4ss tool", "packages");

    /// <summary>실험판 최신과 안정판 최신의 패키지(기본·zDEV)를 가져온다. 실험판이 앞에 온다.</summary>
    public static async Task<List<Ue4ssPackage>> FetchAsync(CancellationToken ct = default)
    {
        var packages = new List<Ue4ssPackage>();
        var errors = new List<string>();

        foreach (var url in new[]
        {
            $"https://api.github.com/repos/{Repo}/releases/tags/{ExperimentalTag}",
            $"https://api.github.com/repos/{Repo}/releases/latest",
        })
        {
            try
            {
                var json = await Http.GetStringAsync(url, ct);
                packages.AddRange(ParseRelease(json));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(ex.Message);
            }
        }

        if (packages.Count == 0 && errors.Count > 0)
            throw new HttpRequestException("UE4SS 릴리스 목록을 가져오지 못했습니다: " + string.Join(" / ", errors));
        return packages;
    }

    /// <summary>
    /// GitHub 릴리스 JSON 한 개에서 UE4SS 본체 zip 만 고른다.
    /// zCustomGameConfigs·zMapGenBP 같은 부속 zip 은 뺀다. 같은 종류가 여럿이면 가장 최근 것 하나만 남긴다.
    /// </summary>
    internal static List<Ue4ssPackage> ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var experimental = root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean();

        var list = new List<Ue4ssPackage>();
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

            bool isDev = name.StartsWith("zDEV-UE4SS", StringComparison.OrdinalIgnoreCase);
            if (!isDev && !name.StartsWith("UE4SS_", StringComparison.OrdinalIgnoreCase)) continue;

            var updated = asset.TryGetProperty("updated_at", out var u) && u.TryGetDateTimeOffset(out var dt)
                ? dt : DateTimeOffset.MinValue;
            list.Add(new Ue4ssPackage(
                tag, experimental, isDev, name,
                asset.GetProperty("size").GetInt64(), updated,
                asset.GetProperty("browser_download_url").GetString() ?? ""));
        }

        return list
            .GroupBy(p => p.IsDev)
            .Select(g => g.OrderByDescending(p => p.Updated).First())
            .OrderBy(p => p.IsDev)
            .ToList();
    }

    /// <summary>
    /// 전에 받아 둔 패키지. GitHub 에 닿지 못할 때 목록 대신 쓴다.
    /// <see cref="DownloadAsync"/> 는 크기가 맞는 캐시 파일을 그대로 돌려주므로 네트워크 없이 설치된다.
    /// </summary>
    public static List<Ue4ssPackage> FromCache()
    {
        var list = new List<Ue4ssPackage>();
        try
        {
            if (!Directory.Exists(CacheDir)) return list;
            foreach (var tagDir in Directory.EnumerateDirectories(CacheDir))
            {
                var tag = Path.GetFileName(tagDir);
                foreach (var zip in Directory.EnumerateFiles(tagDir, "*.zip"))
                {
                    var name = Path.GetFileName(zip);
                    bool isDev = name.StartsWith("zDEV-UE4SS", StringComparison.OrdinalIgnoreCase);
                    if (!isDev && !name.StartsWith("UE4SS_", StringComparison.OrdinalIgnoreCase)) continue;
                    var info = new FileInfo(zip);
                    list.Add(new Ue4ssPackage(tag, tag == ExperimentalTag, isDev, name, info.Length,
                        info.LastWriteTimeUtc, DownloadUrl: ""));
                }
            }
        }
        catch { }
        return list
            .OrderByDescending(p => p.IsExperimental)
            .ThenBy(p => p.IsDev)
            .ThenByDescending(p => p.Updated)
            .ToList();
    }

    public static string CachePathFor(Ue4ssPackage package) =>
        Path.Combine(CacheDir, package.Tag, package.AssetName);

    /// <summary>패키지를 받아 로컬 경로를 돌려준다. 이미 받아 둔 같은 크기의 파일이 있으면 그것을 쓴다.</summary>
    public static async Task<string> DownloadAsync(
        Ue4ssPackage package, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var path = CachePathFor(package);
        if (File.Exists(path) && new FileInfo(path).Length == package.Size && IsZip(path))
        {
            progress?.Report(1);
            return path;
        }
        if (string.IsNullOrEmpty(package.DownloadUrl))
            throw new FileNotFoundException("받아 둔 패키지 파일이 없고 내려받을 주소도 없습니다.", path);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var part = path + ".part";

        using (var response = await Http.GetAsync(package.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? package.Size;

            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        if (!IsZip(part))
        {
            File.Delete(part);
            throw new InvalidDataException("받은 파일이 zip 이 아닙니다.");
        }
        File.Move(part, path, overwrite: true);
        return path;
    }

    private static bool IsZip(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.Entries.Count > 0;
        }
        catch { return false; }
    }
}
