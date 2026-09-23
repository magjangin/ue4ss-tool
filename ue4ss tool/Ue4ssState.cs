using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ue4ss_tool;

/// <summary>게임 exe 폴더에 놓인 UE4SS 파일의 배치 방식.</summary>
public enum Ue4ssLayout
{
    /// <summary>UE4SS 흔적 없음.</summary>
    None = 0,
    /// <summary><c>dwmapi.dll</c> + <c>ue4ss\UE4SS.dll</c> (실험판 기본 배치).</summary>
    Subfolder,
    /// <summary><c>dwmapi.dll</c> + <c>UE4SS.dll</c> 가 exe 옆에 (안정판 v3.0.x 배치).</summary>
    Flat,
    /// <summary><c>xinput1_3.dll</c> + <c>UE4SS.dll</c> (v2.x 배치).</summary>
    LegacyXinput,
    /// <summary>
    /// 일부만 있다. 프록시 없는 UE4SS.dll(수동 주입용이거나 깨진 설치),
    /// 또는 UE4SS.dll 없는 UE4SS 프록시(override.txt 로 다른 곳을 가리키거나 깨진 설치).
    /// </summary>
    Partial,
    /// <summary>UE4SS.dll 없이 dwmapi.dll 만 있다. 다른 모드가 쓰는 프록시일 수 있다.</summary>
    ForeignProxy,
}

/// <summary>한 exe 폴더의 UE4SS 설치 상태.</summary>
public sealed record Ue4ssState
{
    public static Ue4ssState None { get; } = new();

    public Ue4ssLayout Layout { get; init; }

    /// <summary>프록시 DLL 경로(꺼져 있으면 <c>.disabled</c> 가 붙은 경로).</summary>
    public string? ProxyPath { get; init; }

    /// <summary>UE4SS.dll 경로.</summary>
    public string? CoreDllPath { get; init; }

    /// <summary>설정 파일과 Mods 폴더가 있는 작업 폴더(= UE4SS.dll 이 있는 폴더).</summary>
    public string? WorkingDir { get; init; }

    /// <summary>프록시가 <c>.disabled</c> 로 이름이 바뀌어 게임이 UE4SS 를 불러오지 않는다.</summary>
    public bool IsDisabled { get; init; }

    public string? Version { get; init; }

    /// <summary>Mods 폴더 아래의 모드 폴더 수(shared 제외).</summary>
    public int ModFolderCount { get; init; }

    /// <summary>override.txt 로 UE4SS.dll 위치를 따로 지정했다.</summary>
    public bool HasOverride { get; init; }

    /// <summary>exe 폴더에 있는 다른 DLL 로더(ReShade 의 dxgi.dll 등). 충돌 진단용 정보.</summary>
    public IReadOnlyList<string> OtherLoaders { get; init; } = Array.Empty<string>();

    public bool IsInstalled => Layout is Ue4ssLayout.Subfolder or Ue4ssLayout.Flat or Ue4ssLayout.LegacyXinput;

    /// <summary>UE4SS.dll 이 어디에 있는가만 본다(프록시와 무관). Subfolder / Flat / None.</summary>
    public Ue4ssLayout CoreLayout =>
        CoreDllPath is null ? Ue4ssLayout.None
        : string.Equals(Path.GetFileName(Path.GetDirectoryName(CoreDllPath)), Ue4ssDetector.SubfolderName, StringComparison.OrdinalIgnoreCase)
            ? Ue4ssLayout.Subfolder
            : Ue4ssLayout.Flat;

    public string SettingsPath => WorkingDir is null ? "" : Path.Combine(WorkingDir, Ue4ssDetector.SettingsFile);
    public string LogPath => WorkingDir is null ? "" : Path.Combine(WorkingDir, "UE4SS.log");
    public string ModsDir => WorkingDir is null ? "" : Path.Combine(WorkingDir, "Mods");

    public string Describe() => Layout switch
    {
        Ue4ssLayout.None => HasOverride ? "미설치 (override.txt 있음)" : "미설치",
        Ue4ssLayout.ForeignProxy => "UE4SS 파일 없이 dwmapi.dll 만 있음 (다른 모드의 파일일 수 있음)",
        Ue4ssLayout.Partial when CoreDllPath is null =>
            "UE4SS 프록시(dwmapi.dll)만 있고 UE4SS.dll 이 없음" + (HasOverride ? " — override.txt 가 다른 위치를 가리킴" : " (불완전한 설치)"),
        Ue4ssLayout.Partial => "UE4SS.dll 은 있지만 프록시 DLL 이 없음 (불완전한 설치)",
        _ => $"설치됨{(Version is null ? "" : $" {Version}")} · {LayoutName(Layout)}" +
             (IsDisabled ? " · 꺼짐" : "") +
             $" · 모드 폴더 {ModFolderCount}개",
    };

    public static string LayoutName(Ue4ssLayout layout) => layout switch
    {
        Ue4ssLayout.Subfolder => "ue4ss 하위 폴더 배치",
        Ue4ssLayout.Flat => "exe 옆 배치(v3.0.x)",
        Ue4ssLayout.LegacyXinput => "구버전 xinput1_3 배치(v2.x)",
        Ue4ssLayout.Partial => "불완전",
        Ue4ssLayout.ForeignProxy => "다른 dwmapi.dll",
        _ => "없음",
    };
}

/// <summary>게임 exe 폴더에서 UE4SS 설치 상태를 읽는다. 파일을 바꾸지 않는다.</summary>
public static class Ue4ssDetector
{
    public const string CoreDll = "UE4SS.dll";
    public const string SettingsFile = "UE4SS-settings.ini";
    public const string SubfolderName = "ue4ss";
    public const string ProxyDwmapi = "dwmapi.dll";
    public const string ProxyXinput = "xinput1_3.dll";
    public const string DisabledSuffix = ".disabled";

    /// <summary>
    /// 흔한 DLL 프록시 이름. exe 폴더에 있으면 다른 모드 로더(ReShade, 해상도 패치 등)가 깔려 있다는 뜻이다.
    /// </summary>
    private static readonly string[] KnownLoaderNames =
    {
        "dxgi.dll", "d3d9.dll", "d3d11.dll", "dinput8.dll", "version.dll", "winmm.dll",
        "winhttp.dll", "dsound.dll", "xinput1_4.dll", "xinput9_1_0.dll",
    };

    public static Ue4ssState Detect(string? exeDir)
    {
        if (string.IsNullOrEmpty(exeDir) || !Directory.Exists(exeDir)) return Ue4ssState.None;

        var subCore = Path.Combine(exeDir, SubfolderName, CoreDll);
        var flatCore = Path.Combine(exeDir, CoreDll);
        var dwm = FindProxy(exeDir, ProxyDwmapi);
        var xin = FindProxy(exeDir, ProxyXinput);
        var hasOverride = File.Exists(Path.Combine(exeDir, "override.txt"));

        Ue4ssLayout layout;
        string? core = null;
        (string Path, bool Disabled)? proxy = null;

        if (File.Exists(subCore))
        {
            core = subCore;
            proxy = dwm ?? xin;
            layout = proxy is null ? Ue4ssLayout.Partial : Ue4ssLayout.Subfolder;
        }
        else if (File.Exists(flatCore))
        {
            core = flatCore;
            if (dwm is not null) { proxy = dwm; layout = Ue4ssLayout.Flat; }
            else if (xin is not null) { proxy = xin; layout = Ue4ssLayout.LegacyXinput; }
            else layout = Ue4ssLayout.Partial;
        }
        else if (dwm is not null)
        {
            // UE4SS 프록시는 버전 리소스의 제품명이 "UE4SS Injection Proxy" 다.
            // 그렇다면 UE4SS.dll 만 빠진(또는 override.txt 로 다른 곳을 가리키는) 설치이고, 아니면 다른 모드의 파일이다.
            proxy = dwm;
            layout = IsUe4ssProxy(dwm.Value.Path) ? Ue4ssLayout.Partial : Ue4ssLayout.ForeignProxy;
        }
        else
        {
            layout = Ue4ssLayout.None;
        }

        // xinput1_3.dll 이 UE4SS 프록시가 아니라면 다른 로더 목록에 넣는다.
        var others = KnownLoaderNames
            .Append(ProxyXinput)
            .Where(n => File.Exists(Path.Combine(exeDir, n)))
            .Where(n => !(proxy is { } p && string.Equals(Path.GetFileName(p.Path), n, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var workingDir = core is null ? null : Path.GetDirectoryName(core);
        return new Ue4ssState
        {
            Layout = layout,
            ProxyPath = proxy?.Path,
            IsDisabled = proxy?.Disabled ?? false,
            CoreDllPath = core,
            WorkingDir = workingDir,
            Version = core is null ? null : ReadVersion(core),
            ModFolderCount = workingDir is null ? 0 : CountMods(Path.Combine(workingDir, "Mods")),
            HasOverride = hasOverride,
            OtherLoaders = others,
        };
    }

    /// <summary>프록시 DLL 을 찾는다. 켜진 것을 우선하고, 없으면 <c>.disabled</c> 로 꺼 둔 것.</summary>
    private static (string Path, bool Disabled)? FindProxy(string exeDir, string name)
    {
        var on = Path.Combine(exeDir, name);
        if (File.Exists(on)) return (on, false);
        var off = on + DisabledSuffix;
        if (File.Exists(off)) return (off, true);
        return null;
    }

    /// <summary>프록시 DLL 의 버전 리소스(제품명·설명)에 UE4SS 가 적혀 있는가.</summary>
    internal static bool IsUe4ssProxy(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return (info.ProductName?.Contains("UE4SS", StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (info.FileDescription?.Contains("UE4SS", StringComparison.OrdinalIgnoreCase) ?? false);
        }
        catch { return false; }
    }

    private static int CountMods(string modsDir)
    {
        try
        {
            if (!Directory.Exists(modsDir)) return 0;
            return Directory.EnumerateDirectories(modsDir)
                .Count(d => !Path.GetFileName(d).Equals("shared", StringComparison.OrdinalIgnoreCase));
        }
        catch { return 0; }
    }

    /// <summary>
    /// UE4SS 버전. DLL 버전 리소스가 있으면 그것을, 없으면 게임을 한 번 실행한 뒤 생기는 UE4SS.log 의 첫머리를 읽는다.
    /// </summary>
    internal static string? ReadVersion(string coreDll)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(coreDll);
            var text = FirstNonEmpty(info.ProductVersion, info.FileVersion);
            if (text is not null) return text.StartsWith('v') ? text : "v" + text;
            if (info.FileMajorPart > 0 || info.FileMinorPart > 0)
                return $"v{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
        }
        catch { }

        try
        {
            var log = Path.Combine(Path.GetDirectoryName(coreDll)!, "UE4SS.log");
            if (!File.Exists(log)) return null;
            using var reader = new StreamReader(new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            for (int i = 0; i < 20 && reader.ReadLine() is { } line; i++)
            {
                if (VersionFromLogLine(line) is { } v) return v;
            }
        }
        catch { }
        return null;
    }

    /// <summary>로그 한 줄에서 <c>UE4SS … v3.0.1-…</c> 형태의 버전을 뽑는다.</summary>
    internal static string? VersionFromLogLine(string line)
    {
        if (line.IndexOf("UE4SS", StringComparison.OrdinalIgnoreCase) < 0) return null;
        var m = Regex.Match(line, @"\bv(\d+\.\d+\.\d+(?:[-+][0-9A-Za-z][0-9A-Za-z.\-]*)?)");
        return m.Success ? "v" + m.Groups[1].Value : null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v) && v != "0.0.0.0");
}
