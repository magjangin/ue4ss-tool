using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.VisualBasic.FileIO;

namespace ue4ss_tool;

/// <summary>설치를 진행하면 기존 파일이 섞이거나 망가지는 경우. 메시지는 그대로 사용자에게 보여 준다.</summary>
public sealed class InstallBlockedException(string message) : Exception(message);

/// <summary>zip 안의 UE4SS 배치.</summary>
/// <param name="BasePrefix">zip 안에서 게임 exe 폴더에 해당하는 경로(<c>""</c> 또는 <c>"UE4SS_v3/"</c>).</param>
/// <param name="WorkingPrefix">BasePrefix 기준 작업 폴더(<c>"ue4ss/"</c> 또는 <c>""</c>).</param>
public sealed record PackageInfo(string BasePrefix, Ue4ssLayout Layout, string ProxyName, string WorkingPrefix);

public sealed record InstallReport(
    Ue4ssLayout Layout, bool WasUpdate, int Written, IReadOnlyList<string> Preserved, IReadOnlyList<string> Notes);

/// <summary>
/// UE4SS zip 을 게임 exe 폴더에 풀고, 걷어 내고, 켜고 끈다.
/// <para>
/// zip 의 배치를 그대로 따른다. 실험판은 <c>dwmapi.dll</c> + <c>ue4ss\</c>, 안정판 v3.0.x 는 exe 옆에 모든 파일을 둔다.
/// 이미 다른 배치로 설치돼 있으면 섞이지 않도록 설치를 막는다.
/// </para>
/// </summary>
public static class Ue4ssInstaller
{
    /// <summary>업데이트할 때 덮어쓰지 않는 사용자 파일(작업 폴더 기준).</summary>
    internal static readonly string[] UserFiles =
    {
        Ue4ssDetector.SettingsFile,
        "Mods/mods.txt",
        "Mods/mods.json",
    };

    /// <summary>
    /// exe 옆 배치(v3.0.x·v2.x)에서 이름만으로 UE4SS 것이라고 볼 수 있는 항목.
    /// 이 배치는 게임 파일과 한 폴더에 섞여 있으므로 확인된 것만 걷어 낸다.
    /// (Mods 는 흔한 이름이지만, UE4SS.dll 이 exe 옆에 있을 때는 UE4SS 의 작업 폴더다.)
    /// </summary>
    internal static readonly string[] FlatItems =
    {
        "UE4SS.dll", "UE4SS.pdb", Ue4ssDetector.SettingsFile, "UE4SS.log", "Mods",
        "UE4SS_Signatures", "MemberVariableLayout.ini", "VTableLayout.ini", "UE4SS_ObjectDump.txt",
    };

    /// <summary>
    /// 이름이 흔해서 내용 앞부분에 "UE4SS" 가 있을 때만 걷어 내는 항목.
    /// v3.0.1 안정판 zip 에는 README.md·Changelog.md 가 들어 있다(실제 설치본에서 확인).
    /// </summary>
    internal static readonly string[] MarkedFlatItems = { "README.md", "Changelog.md", "API.txt", "LICENSE" };

    // ── zip 분석 ────────────────────────────────────────────────

    public static PackageInfo Analyze(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return Analyze(zip);
    }

    internal static PackageInfo Analyze(ZipArchive zip)
    {
        var files = zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .Select(e => Norm(e.FullName))
            .ToList();

        string? Shortest(string fileName) => files
            .Where(f => NameOf(f).Equals(fileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Length)
            .FirstOrDefault();

        var proxy = Shortest(Ue4ssDetector.ProxyDwmapi) ?? Shortest(Ue4ssDetector.ProxyXinput)
            ?? throw new InvalidDataException("zip 안에 프록시 DLL(dwmapi.dll)이 없습니다. UE4SS 설치 패키지가 아닌 것 같습니다.");

        var basePrefix = proxy[..(proxy.LastIndexOf('/') + 1)];
        var proxyName = NameOf(proxy);
        bool Has(string rel) => files.Any(f => f.Equals(basePrefix + rel, StringComparison.OrdinalIgnoreCase));

        if (Has($"{Ue4ssDetector.SubfolderName}/{Ue4ssDetector.CoreDll}"))
            return new PackageInfo(basePrefix, Ue4ssLayout.Subfolder, proxyName, Ue4ssDetector.SubfolderName + "/");
        if (Has(Ue4ssDetector.CoreDll))
        {
            var layout = proxyName.Equals(Ue4ssDetector.ProxyXinput, StringComparison.OrdinalIgnoreCase)
                ? Ue4ssLayout.LegacyXinput : Ue4ssLayout.Flat;
            return new PackageInfo(basePrefix, layout, proxyName, "");
        }
        throw new InvalidDataException("zip 안에 UE4SS.dll 이 없습니다. UE4SS 설치 패키지가 아닌 것 같습니다.");
    }

    // ── 설치 ────────────────────────────────────────────────────

    /// <summary>
    /// 파일을 쓰지 않고 zip 구조와 기존 설치와의 충돌만 확인한다. 막히면 <see cref="InstallBlockedException"/>.
    /// 사용자에게 확인을 받기 전에 부른다.
    /// </summary>
    public static PackageInfo Preflight(string zipPath, string exeDir)
    {
        var package = Analyze(zipPath);
        CheckConflicts(Ue4ssDetector.Detect(exeDir), package);
        return package;
    }

    /// <summary>zip 을 풀고, <c>UE4SS-settings.ini</c> 의 콘솔 창 설정을 켠다(<see cref="Ue4ssSettings"/>).</summary>
    /// <param name="keepUserFiles">
    /// 이미 있는 <c>UE4SS-settings.ini</c>, <c>Mods\mods.txt</c>, <c>Mods\mods.json</c> 을 덮어쓰지 않는다.
    /// 유지한 설정 파일도 콘솔 창 설정만은 켠다.
    /// </param>
    public static InstallReport Install(string zipPath, string exeDir, bool keepUserFiles = true)
    {
        if (!Directory.Exists(exeDir))
            throw new DirectoryNotFoundException($"게임 exe 폴더가 없습니다: {exeDir}");

        var exeRoot = Path.GetFullPath(exeDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        var package = Analyze(zip);
        var current = Ue4ssDetector.Detect(exeDir);
        CheckConflicts(current, package);

        // 쓰기 전에 모든 경로를 먼저 검증한다. 중간에 멈춰 반쯤 풀린 상태가 되지 않게 한다.
        var directories = new List<string>();
        var files = new List<(ZipArchiveEntry Entry, string Rel, string Target)>();
        foreach (var entry in zip.Entries)
        {
            var full = Norm(entry.FullName);
            if (!full.StartsWith(package.BasePrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rel = full[package.BasePrefix.Length..].TrimStart('/');
            if (rel.Length == 0) continue;

            var target = Path.GetFullPath(Path.Combine(exeRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(exeRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"zip 항목이 게임 폴더 밖을 가리킵니다: {entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name)) directories.Add(target);
            else files.Add((entry, rel, target));
        }

        var preserved = new List<string>();
        var notes = new List<string>();
        int written = 0;
        try
        {
            foreach (var dir in directories) Directory.CreateDirectory(dir);
            foreach (var (entry, rel, target) in files)
            {
                if (keepUserFiles && IsUserFile(rel, package.WorkingPrefix) && File.Exists(target))
                {
                    preserved.Add(rel);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
                written++;
            }

            // 기본 패키지는 콘솔 창이 모두 꺼져 있어 게임을 켜도 아무 창이 안 뜬다. 유지한 기존 설정 파일에도 적용한다.
            var settings = Path.Combine(exeRoot, package.WorkingPrefix.Replace('/', Path.DirectorySeparatorChar), Ue4ssDetector.SettingsFile);
            if (File.Exists(settings) && Ue4ssSettings.ForceConsoleOn(settings))
                notes.Add($"{Ue4ssDetector.SettingsFile} 에서 콘솔·GUI 창을 켰습니다({string.Join("·", Ue4ssSettings.ConsoleKeys)} = 1).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InstallBlockedException(
                $"파일을 쓰지 못했습니다({written}개까지 씀). 게임이 실행 중이면 종료하고, 폴더 권한을 확인한 뒤 다시 시도하세요.\n{ex.Message}");
        }

        // 꺼 두었던(.disabled) 이전 프록시는 방금 쓴 새 프록시로 대체됐다. 남겨 두면 켜기/끄기가 꼬인다.
        var newProxy = Path.Combine(exeDir, package.ProxyName);
        if (current.IsDisabled && current.ProxyPath is { } oldProxy && File.Exists(oldProxy) && File.Exists(newProxy) &&
            string.Equals(oldProxy, newProxy + Ue4ssDetector.DisabledSuffix, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(oldProxy);
            notes.Add("꺼 두었던 이전 프록시를 새 프록시로 바꿔서 UE4SS 가 다시 켜졌습니다.");
        }
        if (current.HasOverride)
            notes.Add("override.txt 가 있습니다. UE4SS 는 방금 설치한 파일보다 override.txt 가 가리키는 UE4SS.dll 을 먼저 불러옵니다.");

        return new InstallReport(package.Layout, current.Layout != Ue4ssLayout.None, written, preserved, notes);
    }

    private static void CheckConflicts(Ue4ssState current, PackageInfo package)
    {
        switch (current.Layout)
        {
            case Ue4ssLayout.None:
                return;

            case Ue4ssLayout.ForeignProxy:
                throw new InstallBlockedException(
                    "이 폴더에 UE4SS 가 아닌 dwmapi.dll 이 이미 있습니다. 다른 모드가 쓰는 파일일 수 있으니 " +
                    "직접 확인해 옮긴 뒤 다시 시도하세요.");

            case Ue4ssLayout.LegacyXinput when package.Layout != Ue4ssLayout.LegacyXinput:
                throw new InstallBlockedException(
                    "구버전(v2.x, xinput1_3.dll) UE4SS 가 설치되어 있습니다. 새 버전과 함께 두면 두 번 불러오게 되므로 " +
                    "「제거」로 먼저 치운 뒤 설치하세요. 제거한 파일은 휴지통으로 가므로 Mods 폴더의 모드는 되살릴 수 있습니다.");
        }

        var existing = current.CoreLayout;
        var incoming = package.Layout == Ue4ssLayout.Subfolder ? Ue4ssLayout.Subfolder : Ue4ssLayout.Flat;
        if (existing != Ue4ssLayout.None && existing != incoming)
        {
            throw new InstallBlockedException(
                $"이미 설치된 UE4SS 는 「{Ue4ssState.LayoutName(existing)}」이고, 고른 패키지는 「{Ue4ssState.LayoutName(incoming)}」입니다. " +
                "그대로 덮으면 두 벌이 섞여 설정과 모드가 엇갈립니다. 「제거」 후 설치하세요. " +
                "제거한 파일은 휴지통으로 가므로 Mods 폴더의 모드는 되살려 옮길 수 있습니다.");
        }
    }

    private static bool IsUserFile(string rel, string workingPrefix)
    {
        if (!rel.StartsWith(workingPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        var inWorking = rel[workingPrefix.Length..];
        return UserFiles.Any(u => u.Equals(inWorking, StringComparison.OrdinalIgnoreCase));
    }

    // ── 제거 ────────────────────────────────────────────────────

    /// <summary>제거할 항목(파일·폴더 전체 경로). 실제로 존재하는 것만.</summary>
    public static List<string> PlanUninstall(string exeDir)
    {
        var current = Ue4ssDetector.Detect(exeDir);
        if (current.Layout is Ue4ssLayout.None or Ue4ssLayout.ForeignProxy)
            return new List<string>();

        var items = new List<string>();
        // 프록시는 켜진 것·꺼진 것 모두.
        var proxyName = current.ProxyPath is null ? null : Path.GetFileName(current.ProxyPath);
        if (proxyName is not null)
        {
            var baseName = proxyName.EndsWith(Ue4ssDetector.DisabledSuffix, StringComparison.OrdinalIgnoreCase)
                ? proxyName[..^Ue4ssDetector.DisabledSuffix.Length] : proxyName;
            items.Add(Path.Combine(exeDir, baseName));
            items.Add(Path.Combine(exeDir, baseName + Ue4ssDetector.DisabledSuffix));
        }

        if (current.CoreLayout == Ue4ssLayout.Subfolder)
        {
            items.Add(Path.Combine(exeDir, Ue4ssDetector.SubfolderName));
        }
        else
        {
            items.AddRange(FlatItems.Select(n => Path.Combine(exeDir, n)));
            items.AddRange(MarkedFlatItems.Select(n => Path.Combine(exeDir, n)).Where(MentionsUe4ss));
        }

        return items.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
    }

    /// <summary>파일 앞부분(8KB)에 "UE4SS" 가 적혀 있는가.</summary>
    private static bool MentionsUe4ss(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var reader = new StreamReader(path);
            var buffer = new char[8192];
            var n = reader.ReadBlock(buffer, 0, buffer.Length);
            return new string(buffer, 0, n).Contains("UE4SS", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// UE4SS 를 걷어 낸다. 기본으로 휴지통에 보내므로 되살릴 수 있다.
    /// </summary>
    /// <param name="remove">항목 하나를 지우는 방법. null 이면 휴지통으로 보낸다(테스트는 다른 방법을 넘긴다).</param>
    public static List<string> Uninstall(string exeDir, Action<string>? remove = null)
    {
        var items = PlanUninstall(exeDir);
        if (items.Count == 0)
            throw new InstallBlockedException("제거할 UE4SS 파일이 없습니다.");

        remove ??= RecycleBin.Send;
        var removed = new List<string>();
        foreach (var item in items)
        {
            try
            {
                remove(item);
                removed.Add(item);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                throw new InstallBlockedException(
                    $"'{Path.GetFileName(item)}' 을(를) 치우지 못했습니다({removed.Count}개 처리함). 게임이 실행 중이면 종료하고 다시 시도하세요.\n{ex.Message}");
            }
        }
        return removed;
    }

    // ── 켜기/끄기 ───────────────────────────────────────────────

    /// <summary>프록시 DLL 이름에 <c>.disabled</c> 를 붙이거나 떼서 UE4SS 로드를 끄고 켠다. 다른 파일은 건드리지 않는다.</summary>
    public static void SetEnabled(string exeDir, bool enabled)
    {
        var current = Ue4ssDetector.Detect(exeDir);
        if (!current.IsInstalled || current.ProxyPath is null)
            throw new InstallBlockedException("켜거나 끌 UE4SS 설치가 없습니다.");
        if (current.IsDisabled != enabled) return;   // 이미 원하는 상태

        var proxy = current.ProxyPath;
        var target = enabled
            ? proxy[..^Ue4ssDetector.DisabledSuffix.Length]
            : proxy + Ue4ssDetector.DisabledSuffix;
        if (File.Exists(target))
            throw new InstallBlockedException($"'{Path.GetFileName(target)}' 이(가) 이미 있어 이름을 바꿀 수 없습니다.");
        try { File.Move(proxy, target); }
        catch (IOException ex)
        {
            throw new InstallBlockedException($"프록시 이름을 바꾸지 못했습니다. 게임이 실행 중이면 종료하세요.\n{ex.Message}");
        }
    }

    // ── 보조 ────────────────────────────────────────────────────

    private static string Norm(string zipPath) => zipPath.Replace('\\', '/');
    private static string NameOf(string normPath) => normPath[(normPath.LastIndexOf('/') + 1)..];
}

/// <summary>파일·폴더를 휴지통으로 보낸다.</summary>
public static class RecycleBin
{
    public static void Send(string path)
    {
        if (Directory.Exists(path))
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else if (File.Exists(path))
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }
}

public static class GameProcess
{
    /// <summary>이 exe 로 실행 중인 프로세스가 있는가. 경로를 읽을 수 없으면 같은 이름이면 실행 중으로 본다.</summary>
    public static bool IsRunning(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return false;
        var running = false;
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath)))
        {
            using (p)
            {
                try
                {
                    if (string.Equals(p.MainModule?.FileName, exePath, StringComparison.OrdinalIgnoreCase))
                        running = true;
                }
                catch { running = true; }
            }
        }
        return running;
    }
}
