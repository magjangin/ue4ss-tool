using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace ue4ss_tool;

/// <summary>언리얼 엔진 세대. 버전을 못 읽으면 구조만으로는 UE4/UE5 를 가르지 못해 Unknown 이다.</summary>
public enum UnrealGeneration
{
    Unknown = 0,
    UE3,
    UE4,
    UE5,
}

/// <summary>게임이 쓰는 안티치트. UE4SS 같은 DLL 주입은 온라인 게임에서 제재 사유가 될 수 있다.</summary>
[Flags]
public enum AntiCheat
{
    None = 0,
    EasyAntiCheat = 1,
    BattlEye = 2,
}

/// <summary>
/// 엔진 버전. <see cref="Patch"/> 가 -1 이면 major.minor 까지만 안다(바이너리 브랜치 문자열에서 읽은 경우).
/// </summary>
public sealed record EngineVersion(int Major, int Minor, int Patch, string Source)
{
    public UnrealGeneration Generation => Major switch
    {
        4 => UnrealGeneration.UE4,
        5 => UnrealGeneration.UE5,
        _ => UnrealGeneration.Unknown,
    };

    /// <summary>
    /// exe 의 숫자 버전이 엔진 버전으로 볼 만한가.
    /// UE 는 기본적으로 FILEVERSION 에 엔진 버전을 넣지만, 개발사가 게임 버전(1.0.8767 등)으로 바꾸기도 한다.
    /// </summary>
    public static bool IsPlausible(int major, int minor) =>
        (major == 4 && minor is >= 0 and <= 27) ||
        (major == 5 && minor is >= 0 and <= 20);

    public override string ToString() => Patch >= 0 ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}";
}

/// <summary>이 게임에 UE4SS 를 쓸 수 있는가.</summary>
public enum Ue4ssSupport
{
    /// <summary>버전을 몰라 판단하지 못함. 설치는 할 수 있다.</summary>
    Unknown = 0,
    /// <summary>안정판·실험판 모두 지원(UE 4.11~5.3, Shipping 빌드).</summary>
    Supported,
    /// <summary>실험판만 지원(UE 4.7~4.10, 5.4~5.8, Development/Test 빌드).</summary>
    ExperimentalOnly,
    /// <summary>지원하지 않음(UE3, UE 4.6 이하).</summary>
    Unsupported,
}

/// <summary>배지 종류. 색은 <see cref="Converters.TagBrush"/> 가 정한다.</summary>
public enum TagKind { UE5, UE4, UE3, UEUnknown, Ue4ss, Ue4ssOff, Ue4ssBroken, AntiCheat }

public sealed record GameTag(string Text, TagKind Kind);

/// <summary>언리얼로 판별된 게임 폴더 하나.</summary>
public sealed class UnrealGame : INotifyPropertyChanged
{
    /// <summary>디스크의 폴더 이름. 스토어 표기명과 다를 수 있다.</summary>
    public required string Name { get; init; }
    public required string InstallDir { get; init; }

    public uint? AppId { get; set; }
    public string? StoreName { get; set; }
    public string DisplayName => StoreName ?? Name;

    private string? _nameKey;
    public string NameKey => _nameKey ??= GameNames.Normalize(DisplayName);

    public UnrealGeneration Generation { get; set; }
    public EngineVersion? Version { get; set; }

    /// <summary>프로젝트 폴더 이름(<c>&lt;설치폴더&gt;\&lt;프로젝트&gt;\Binaries</c> 의 프로젝트).</summary>
    public string? ProjectName { get; set; }
    public string? ProjectDir { get; set; }

    /// <summary>실제 게임 exe 가 있는 폴더 = UE4SS 설치 위치. 찾지 못하면 null.</summary>
    public string? ExeDir { get; set; }
    public string? GameExe { get; set; }

    /// <summary>Binaries 아래 플랫폼 폴더 이름(Win64 / WinGDK / Win32).</summary>
    public string? Platform { get; set; }

    /// <summary>게임 exe 이름이 <c>-Shipping.exe</c> 로 끝나는가. 아니면 Development/Test 빌드일 수 있다.</summary>
    public bool IsShippingExe { get; set; }

    /// <summary>Content\Paks 에 .utoc/.ucas(IoStore)가 있다.</summary>
    public bool UsesIoStore { get; set; }

    public AntiCheat AntiCheat { get; set; }

    private Ue4ssState _ue4ss = Ue4ssState.None;

    /// <summary>현재 UE4SS 설치 상태. 설치·제거 후 다시 읽어 바꾸면 목록 배지가 갱신된다.</summary>
    public Ue4ssState Ue4ss
    {
        get => _ue4ss;
        set
        {
            _ue4ss = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Ue4ss)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tags)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>화면에 보여 줄 엔진 표기. 버전을 모르면 세대만.</summary>
    public string EngineText => Version is not null
        ? $"UE {Version}"
        : Generation switch
        {
            UnrealGeneration.UE3 => "UE3",
            UnrealGeneration.UE4 => "UE4",
            UnrealGeneration.UE5 => "UE5",
            _ => "UE4/5 (버전 미확인)",
        };

    public Ue4ssSupport Support => Ue4ssCompatibility.For(this);

    /// <summary>화면 낭독기 등 UI 자동화가 읽는 이름.</summary>
    public override string ToString() => DisplayName;

    public IEnumerable<GameTag> Tags
    {
        get
        {
            yield return Generation switch
            {
                UnrealGeneration.UE5 => new GameTag(Version is null ? "UE5" : $"UE{Version.Major}.{Version.Minor}", TagKind.UE5),
                UnrealGeneration.UE4 => new GameTag(Version is null ? "UE4" : $"UE{Version.Major}.{Version.Minor}", TagKind.UE4),
                UnrealGeneration.UE3 => new GameTag("UE3", TagKind.UE3),
                _ => new GameTag("UE?", TagKind.UEUnknown),
            };

            switch (Ue4ss.Layout)
            {
                case Ue4ssLayout.None:
                    break;
                case Ue4ssLayout.Partial:
                case Ue4ssLayout.ForeignProxy:
                    yield return new GameTag("UE4SS 불완전", TagKind.Ue4ssBroken);
                    break;
                default:
                    yield return Ue4ss.IsDisabled
                        ? new GameTag("UE4SS 꺼짐", TagKind.Ue4ssOff)
                        : new GameTag("UE4SS", TagKind.Ue4ss);
                    break;
            }

            if (AntiCheat != AntiCheat.None)
                yield return new GameTag("안티치트", TagKind.AntiCheat);
        }
    }
}

/// <summary>
/// UE4SS 지원 범위. RE-UE4SS 릴리스 노트 기준(2026-09 확인):
/// 안정판 v3.0.x 는 UE 4.11~5.3, 실험판은 여기에 4.7~4.10, 5.4~5.8, Development/Test 빌드를 더한다.
/// </summary>
public static class Ue4ssCompatibility
{
    public static Ue4ssSupport For(UnrealGame game)
    {
        if (game.Generation == UnrealGeneration.UE3) return Ue4ssSupport.Unsupported;
        if (game.ExeDir is null) return Ue4ssSupport.Unsupported;

        var v = game.Version;
        if (v is null) return Ue4ssSupport.Unknown;

        var support = For(v.Major, v.Minor);
        // Development/Test 빌드는 실험판에서만 지원한다.
        if (support == Ue4ssSupport.Supported && !game.IsShippingExe)
            return Ue4ssSupport.ExperimentalOnly;
        return support;
    }

    public static Ue4ssSupport For(int major, int minor) => (major, minor) switch
    {
        (4, <= 6) => Ue4ssSupport.Unsupported,
        (4, <= 10) => Ue4ssSupport.ExperimentalOnly,
        (4, _) => Ue4ssSupport.Supported,
        (5, <= 3) => Ue4ssSupport.Supported,
        (5, <= 8) => Ue4ssSupport.ExperimentalOnly,
        _ => Ue4ssSupport.Unknown,
    };

    public static string Describe(Ue4ssSupport support) => support switch
    {
        Ue4ssSupport.Supported => "안정판·실험판 모두 지원",
        Ue4ssSupport.ExperimentalOnly => "실험판만 지원",
        Ue4ssSupport.Unsupported => "UE4SS 미지원",
        _ => "지원 여부 미확인 (실험판 권장)",
    };
}
