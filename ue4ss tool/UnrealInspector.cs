using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ue4ss_tool;

/// <summary><see cref="UnrealInspector.Inspect"/> 의 결과.</summary>
public sealed class UnrealInspection
{
    public bool IsUnreal => Generation != UnrealGeneration.Unknown || ProjectDir is not null;
    public UnrealGeneration Generation { get; set; }
    public EngineVersion? Version { get; set; }
    public string? ProjectName { get; set; }
    public string? ProjectDir { get; set; }
    public string? ExeDir { get; set; }
    public string? GameExe { get; set; }
    public string? Platform { get; set; }
    public bool IsShippingExe { get; set; }
    public bool UsesIoStore { get; set; }
    public AntiCheat AntiCheat { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// 게임 설치 폴더 하나를 들여다보고 언리얼 엔진 게임인지, 실제 게임 exe 가 어디 있는지, 엔진 버전이 무엇인지 판별한다.
/// 파일을 읽기만 한다.
/// <para>
/// UE4/5 패키징 구조:
/// <c>&lt;설치폴더&gt;\&lt;프로젝트&gt;\Binaries\Win64\&lt;프로젝트&gt;-Win64-Shipping.exe</c> 와
/// <c>&lt;설치폴더&gt;\&lt;프로젝트&gt;\Content\Paks</c>. 설치 폴더 바로 아래의 <c>&lt;프로젝트&gt;.exe</c> 는
/// 실제 게임을 띄우기만 하는 작은 런처다.
/// </para>
/// </summary>
public static class UnrealInspector
{
    /// <summary>우선순위 순. Game Pass 판은 WinGDK 를 쓴다.</summary>
    private static readonly string[] Platforms = { "Win64", "WinGDK", "Win32" };

    /// <summary>프로젝트 폴더를 찾을 때 내려가지 않는 폴더.</summary>
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Engine", "_CommonRedist", "__Installer", "CommonRedist", "Redist", "Redistributables",
        "DirectX", "EasyAntiCheat", "BattlEye", "ue4ss", "Mods", "Saved", ".git",
    };

    /// <summary>게임 exe 후보에서 빼는 보조 실행 파일.</summary>
    private static readonly Regex HelperExe = new(
        @"^(CrashReportClient|UnrealCEFSubProcess|EpicWebHelper|UE4PrereqSetup.*|UEPrereqSetup.*|start_protected_game|EasyAntiCheat.*|BEService.*|.*_BE|.*Setup.*|.*Installer.*|.*Uninst.*|UE3ShaderCompileWorker|ShaderCompileWorker|curl|7za?)\.exe$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ShippingExe = new(
        @"-(Win64|WinGDK|Win32)-Shipping\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly EnumerationOptions TopOnly = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.System,
    };

    private const int MaxProjectDepth = 3;

    public static UnrealInspection Inspect(string installDir)
    {
        var result = new UnrealInspection();
        try
        {
            var project = FindProjects(installDir, MaxProjectDepth)
                .OrderByDescending(p => p.Score)
                .ThenBy(p => p.Dir.Length)
                .FirstOrDefault();

            if (project is not null)
            {
                result.ProjectDir = project.Dir;
                result.ProjectName = Path.GetFileName(project.Dir);
                result.ExeDir = project.ExeDir;
                result.GameExe = project.Exe;
                result.Platform = project.Platform;
                result.IsShippingExe = project.IsShipping;
                result.UsesIoStore = HasFiles(Path.Combine(project.Dir, "Content", "Paks"), "*.utoc");
                result.Version = project.Exe is null ? null : ReadEngineVersion(project.Exe);
                result.Generation = result.Version?.Generation ?? UnrealGeneration.Unknown;
            }
            else if (FindUe3(installDir) is { } ue3)
            {
                result.Generation = UnrealGeneration.UE3;
                result.ExeDir = ue3.ExeDir;
                result.GameExe = ue3.Exe;
                result.Platform = Path.GetFileName(ue3.ExeDir);
                result.ProjectDir = ue3.GameDir;
                result.ProjectName = Path.GetFileName(ue3.GameDir);
            }

            if (result.IsUnreal)
                result.AntiCheat = DetectAntiCheat(installDir, result.ExeDir);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }
        return result;
    }

    /// <summary>
    /// 폴더 자체가 언리얼 게임 설치 폴더처럼 보이는가. 드라이브 루트의 보존본을 찾을 때 쓰므로 가볍게 본다.
    /// </summary>
    public static bool LooksLikeGameRoot(string dir) =>
        FindProjects(dir, maxDepth: 1).Any() || FindUe3(dir) is not null;

    internal sealed record ProjectCandidate(string Dir, string ExeDir, string Platform, string? Exe, bool IsShipping, int Depth)
    {
        public int Score => (IsShipping ? 100 : 0) + (Exe is not null ? 20 : 0) - Depth;
    }

    /// <summary>
    /// <c>Binaries\&lt;플랫폼&gt;\*.exe</c> 와 <c>Content</c> 를 함께 가진 폴더를 프로젝트 후보로 모은다.
    /// 설치 폴더 자신(깊이 0)부터 <paramref name="maxDepth"/> 단계까지 내려간다.
    /// </summary>
    internal static IEnumerable<ProjectCandidate> FindProjects(string root, int maxDepth)
    {
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();

            var candidate = AsProject(dir, depth);
            if (candidate is not null)
            {
                yield return candidate;
                continue;   // 프로젝트 안쪽(Binaries/Content/Plugins…)은 더 볼 필요가 없다.
            }

            if (depth >= maxDepth) continue;
            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(dir, "*", TopOnly).ToList(); }
            catch { continue; }

            foreach (var child in children)
            {
                if (!SkipDirs.Contains(Path.GetFileName(child)))
                    queue.Enqueue((child, depth + 1));
            }
        }
    }

    private static ProjectCandidate? AsProject(string dir, int depth)
    {
        var binaries = Path.Combine(dir, "Binaries");
        var content = Path.Combine(dir, "Content");
        if (!Directory.Exists(binaries) || !Directory.Exists(content))
            return null;
        // UE3 도 Binaries 를 쓰지만 Content 대신 *Game\Cooked* 구조다. 여기서는 UE4/5 만 본다.

        // Binaries+Content 는 다른 엔진에도 있을 수 있는 이름이라, 언리얼 패키징 흔적을 하나 더 요구한다.
        // pak 로 묶인 게임은 Content\Paks, 묶지 않은(loose) 게임도 프로젝트 옆에 Engine 폴더가 있다.
        var parent = Path.GetDirectoryName(dir);
        if (!Directory.Exists(Path.Combine(content, "Paks")) &&
            !(parent is not null && Directory.Exists(Path.Combine(parent, "Engine"))))
            return null;

        foreach (var platform in Platforms)
        {
            var exeDir = Path.Combine(binaries, platform);
            if (!Directory.Exists(exeDir)) continue;

            var exe = PickGameExe(exeDir, Path.GetFileName(dir));
            if (exe is null) continue;
            return new ProjectCandidate(dir, exeDir, platform, exe, ShippingExe.IsMatch(Path.GetFileName(exe)), depth);
        }
        return null;
    }

    /// <summary>
    /// 폴더에서 실제 게임 exe 를 고른다.
    /// 1) <c>*-Win64-Shipping.exe</c>(프로젝트 이름으로 시작하는 것 우선) 2) <c>&lt;프로젝트&gt;.exe</c> 3) 가장 큰 exe.
    /// Shipping exe 이름이 프로젝트 폴더와 다른 게임도 있다(Platform8 → Exit8-Win64-Shipping.exe).
    /// </summary>
    internal static string? PickGameExe(string exeDir, string? projectName)
    {
        List<FileInfo> exes;
        try
        {
            exes = new DirectoryInfo(exeDir).EnumerateFiles("*.exe", TopOnly)
                .Where(f => !HelperExe.IsMatch(f.Name))
                .ToList();
        }
        catch { return null; }
        if (exes.Count == 0) return null;

        var shipping = exes.Where(f => ShippingExe.IsMatch(f.Name)).ToList();
        if (shipping.Count > 0)
        {
            return (shipping.FirstOrDefault(f => projectName is not null &&
                        f.Name.StartsWith(projectName + "-", StringComparison.OrdinalIgnoreCase))
                    ?? shipping.OrderByDescending(f => f.Length).First()).FullName;
        }

        if (projectName is not null &&
            exes.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f.Name).Equals(projectName, StringComparison.OrdinalIgnoreCase)) is { } named)
            return named.FullName;

        return exes.OrderByDescending(f => f.Length).First().FullName;
    }

    internal sealed record Ue3Layout(string ExeDir, string? Exe, string GameDir);

    /// <summary>
    /// UE3: <c>Binaries\Win64|Win32\*.exe</c> 와 <c>&lt;이름&gt;Game\Cooked*</c> 폴더가 설치 폴더(또는 한 단계 아래)에 함께 있다.
    /// UE4SS 는 UE3 를 지원하지 않으므로 표시만 한다.
    /// </summary>
    internal static Ue3Layout? FindUe3(string root)
    {
        foreach (var dir in new[] { root }.Concat(SafeDirs(root)))
        {
            // Binaries 가 없는 폴더는 볼 필요가 없다. 깊은 폴더 트리를 훑지 않도록 먼저 거른다.
            if (!Directory.Exists(Path.Combine(dir, "Binaries"))) continue;

            string? cooked = null;
            foreach (var child in SafeDirs(dir))
            {
                if (SafeDirs(child).Any(d => Path.GetFileName(d).StartsWith("Cooked", StringComparison.OrdinalIgnoreCase)))
                {
                    cooked = child;
                    break;
                }
            }
            if (cooked is null) continue;

            foreach (var platform in new[] { "Win64", "Win32" })
            {
                var exeDir = Path.Combine(dir, "Binaries", platform);
                if (!Directory.Exists(exeDir)) continue;
                var exe = PickGameExe(exeDir, Path.GetFileName(cooked));
                if (exe is not null) return new Ue3Layout(exeDir, exe, cooked);
            }
        }
        return null;
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir, "*", TopOnly).ToList(); }
        catch { return Array.Empty<string>(); }
    }

    private static bool HasFiles(string dir, string pattern)
    {
        try { return Directory.Exists(dir) && Directory.EnumerateFiles(dir, pattern, TopOnly).Any(); }
        catch { return false; }
    }

    private static AntiCheat DetectAntiCheat(string installDir, string? exeDir)
    {
        var found = AntiCheat.None;
        foreach (var dir in new[] { installDir, exeDir }.OfType<string>())
        {
            if (Directory.Exists(Path.Combine(dir, "EasyAntiCheat")) ||
                File.Exists(Path.Combine(dir, "start_protected_game.exe")))
                found |= AntiCheat.EasyAntiCheat;
            if (Directory.Exists(Path.Combine(dir, "BattlEye")) || HasFiles(dir, "*_BE.exe"))
                found |= AntiCheat.BattlEye;
        }
        return found;
    }

    // ── 엔진 버전 ───────────────────────────────────────────────

    /// <summary>
    /// 게임 exe 에서 엔진 버전을 읽는다.
    /// 1) 버전 리소스: UE 는 기본으로 FILEVERSION 에 엔진 버전(예: 5.3.2)을 넣는다. 그럴듯한 값일 때만 믿는다.
    /// 2) 브랜치 문자열: exe 에 박힌 <c>++UE5+Release-5.2</c> 를 찾는다(UE 4.2x 이후). 파일 전체를 읽으므로 느리다.
    /// </summary>
    public static EngineVersion? ReadEngineVersion(string exePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            if (BranchVersion(info.FileVersion) is { } branch)
            {
                var patch = info.FileMajorPart == branch.Major && info.FileMinorPart == branch.Minor ? info.FileBuildPart : -1;
                return new EngineVersion(branch.Major, branch.Minor, patch, "exe 버전 정보");
            }
            if (EngineVersion.IsPlausible(info.FileMajorPart, info.FileMinorPart))
                return new EngineVersion(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, "exe 버전 정보");
        }
        catch { }

        try
        {
            if (ScanBranchString(exePath) is { } found)
                return new EngineVersion(found.Major, found.Minor, -1, "exe 안의 엔진 브랜치 문자열");
        }
        catch { }
        return null;
    }

    private static readonly Regex BranchRegex = new(@"\+UE([45])\+Release-(\d)\.(\d{1,2})", RegexOptions.CultureInvariant);

    /// <summary><c>++UE5+Release-5.5-CL-…</c> 에서 (5, 5) 를 뽑는다.</summary>
    internal static (int Major, int Minor)? BranchVersion(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var m = BranchRegex.Match(text);
        if (!m.Success) return null;
        int major = int.Parse(m.Groups[2].Value), minor = int.Parse(m.Groups[3].Value);
        return EngineVersion.IsPlausible(major, minor) ? (major, minor) : null;
    }

    private static readonly byte[][] BranchNeedles =
    {
        Encoding.Unicode.GetBytes("+UE5+Release-"),
        Encoding.Unicode.GetBytes("+UE4+Release-"),
        Encoding.ASCII.GetBytes("+UE5+Release-"),
        Encoding.ASCII.GetBytes("+UE4+Release-"),
    };

    /// <summary>exe 바이트에서 UTF-16/ASCII 브랜치 문자열을 찾는다. 청크 경계에 걸친 문자열도 놓치지 않도록 겹쳐 읽는다.</summary>
    internal static (int Major, int Minor)? ScanBranchString(string path)
    {
        const int ChunkSize = 4 << 20;
        const int Overlap = 64;
        var buffer = new byte[ChunkSize + Overlap];

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
        int carried = 0;
        while (true)
        {
            int read = fs.Read(buffer, carried, ChunkSize);
            if (read <= 0) break;
            int length = carried + read;
            var span = buffer.AsSpan(0, length);

            foreach (var needle in BranchNeedles)
            {
                int start = 0;
                while (start < span.Length)
                {
                    int at = span[start..].IndexOf(needle);
                    if (at < 0) break;
                    at += start;
                    bool wide = needle.Length > 1 && needle[1] == 0;
                    var tail = DecodeTail(span[at..], wide);
                    if (BranchVersion(tail) is { } v) return v;
                    start = at + needle.Length;
                }
            }

            // 다음 청크 앞에 끝부분을 붙여 경계에 걸친 문자열을 잇는다.
            carried = Math.Min(Overlap, length);
            span[(length - carried)..].CopyTo(buffer);
        }
        return null;
    }

    private static string DecodeTail(ReadOnlySpan<byte> bytes, bool wide)
    {
        int max = Math.Min(bytes.Length, wide ? 48 : 24);
        if (wide) max &= ~1;
        return (wide ? Encoding.Unicode : Encoding.ASCII).GetString(bytes[..max]);
    }
}
