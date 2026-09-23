using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ue4ss_tool;

/// <summary>XAML에서 x:Static 으로 참조하는 값 변환기 모음.</summary>
public static class Converters
{
    /// <summary>배지 종류 → 배경색.</summary>
    public static readonly IValueConverter TagBrush =
        new FuncValueConverter<TagKind, IBrush>(kind => kind switch
        {
            TagKind.UE5         => new SolidColorBrush(Color.Parse("#6E4FE0")), // 보라
            TagKind.UE4         => new SolidColorBrush(Color.Parse("#2563EB")), // 파랑
            TagKind.UE3         => new SolidColorBrush(Color.Parse("#6B7280")), // 짙은 회색
            TagKind.Ue4ss       => new SolidColorBrush(Color.Parse("#2E7D32")), // 초록
            TagKind.Ue4ssOff    => new SolidColorBrush(Color.Parse("#B7791F")), // 황토
            TagKind.Ue4ssBroken => new SolidColorBrush(Color.Parse("#C2410C")), // 주황
            TagKind.AntiCheat   => new SolidColorBrush(Color.Parse("#C62828")), // 빨강
            _                   => new SolidColorBrush(Color.Parse("#9AA0A6")), // 회색
        });
}
