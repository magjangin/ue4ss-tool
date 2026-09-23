using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ue4ss_tool;

/// <summary>예/아니오 확인 창. Avalonia 에는 기본 메시지 상자가 없어 직접 만든다.</summary>
public sealed class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string okText, string? cancelText)
    {
        Title = title;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 700;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var ok = new Button { Content = okText, Classes = { "accent" }, Padding = new Thickness(16, 6), IsDefault = true };
        ok.Click += (_, _) => Close(true);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { ok },
        };
        if (cancelText is not null)
        {
            var cancel = new Button { Content = cancelText, Padding = new Thickness(16, 6), IsCancel = true };
            cancel.Click += (_, _) => Close(false);
            buttons.Children.Add(cancel);
        }

        Content = new DockPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                buttons.With(b => DockPanel.SetDock(b, Dock.Bottom)),
                new ScrollViewer
                {
                    Margin = new Thickness(0, 0, 0, 16),
                    MaxHeight = 560,
                    Content = new SelectableTextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                },
            },
        };
    }

    /// <summary>확인을 누르면 true.</summary>
    public static Task<bool> AskAsync(Window owner, string title, string message, string okText = "확인", string cancelText = "취소") =>
        new ConfirmDialog(title, message, okText, cancelText).ShowDialog<bool>(owner);

    public static Task ShowAsync(Window owner, string title, string message) =>
        new ConfirmDialog(title, message, "닫기", null).ShowDialog<bool>(owner);
}

internal static class ControlExtensions
{
    public static T With<T>(this T control, System.Action<T> apply)
    {
        apply(control);
        return control;
    }
}
