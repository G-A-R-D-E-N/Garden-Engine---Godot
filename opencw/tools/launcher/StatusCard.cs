using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace OpenCommonwealth.Launcher;

public enum CardState { Idle, Busy, Ok, Warn, Error }

public sealed class StatusCard : Border
{
    readonly TextBlock _dot;
    readonly TextBlock _title;
    readonly TextBlock _detail;
    readonly TextBlock _extra;
    readonly Button _action;
    Action? _onAction;

    public CardState State { get; private set; } = CardState.Idle;
    public string Detail => _detail.Text ?? "";

    public StatusCard(string title)
    {
        MinHeight = 78;
        Margin = new Thickness(0, 0, 0, 12);
        Background = Ui.Surface;
        BorderBrush = Ui.Border;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);

        _dot = new TextBlock
        {
            Text = "●",
            FontSize = 15,
            Foreground = Ui.Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 40,
        };

        _title = new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            Foreground = Ui.Text,
        };
        _detail = new TextBlock
        {
            Text = "Waiting…",
            FontSize = 12,
            Foreground = Ui.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        _extra = new TextBlock
        {
            FontSize = 11,
            Foreground = Ui.Muted,
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Margin = new Thickness(0, 2, 0, 0),
        };
        var textStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 10, 4, 10),
        };
        textStack.Children.Add(_title);
        textStack.Children.Add(_detail);
        textStack.Children.Add(_extra);

        _action = new Button
        {
            Content = "",
            Width = 116,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            IsVisible = false,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Margin = new Thickness(8, 0, 0, 0),
        };
        _action.Click += (_, _) => _onAction?.Invoke();

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*,Auto") };
        Grid.SetColumn(_dot, 0);
        Grid.SetColumn(textStack, 1);
        Grid.SetColumn(_action, 2);
        grid.Children.Add(_dot);
        grid.Children.Add(textStack);
        grid.Children.Add(_action);
        grid.Margin = new Thickness(4, 0, 14, 0);

        Child = grid;
    }

    public void Set(CardState state, string detail, string? actionText = null, Action? onAction = null, string? extra = null)
    {
        void Apply()
        {
            _dot.Foreground = state switch
            {
                CardState.Ok => Ui.Green,
                CardState.Warn => Ui.Amber,
                CardState.Error => Ui.Red,
                CardState.Busy => Ui.Blue,
                _ => Ui.Muted,
            };
            _dot.Text = state == CardState.Busy ? "◌" : "●";
            State = state;
            _detail.Text = detail;
            _extra.Text = extra ?? "";
            _extra.IsVisible = !string.IsNullOrEmpty(extra);
            _onAction = onAction;
            _action.Content = actionText ?? "";
            bool primary = state == CardState.Warn && actionText == "Pull latest";
            _action.Background = primary ? Ui.Accent : Ui.SurfaceAlt;
            _action.Foreground = primary ? Brushes.White : Ui.Text;
            _action.BorderBrush = primary ? Ui.Accent : Ui.Border;
            _action.BorderThickness = new Thickness(primary ? 0 : 1);
            _action.IsVisible = actionText is not null && onAction is not null;
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }
}
