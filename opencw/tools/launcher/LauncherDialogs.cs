using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace OpenCommonwealth.Launcher;

internal static class LauncherDialogs
{
    public static async Task<bool> ShowMessageAsync(Window owner, string message, string title)
    {
        var dlg = SmallDialog(title, 420, 160, out var body);
        body.Children.Add(new TextBlock { Text = message, Foreground = Ui.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        var ok = Ui.PillButton("OK", true, 88, 34);
        ok.Margin = new Thickness(0);
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        ok.Click += (_, _) => dlg.Close();
        body.Children.Add(ok);
        await dlg.ShowDialog(owner);
        return true;
    }

    public static async Task<string?> PromptTextAsync(Window owner, string message, string title)
    {
        string? result = null;
        var dlg = SmallDialog(title, 460, 220, out var body);
        body.Children.Add(new TextBlock { Text = message, Foreground = Ui.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        var box = new TextBox { PasswordChar = '●', Background = Ui.Surface, Foreground = Ui.Text, BorderBrush = Ui.Border, Margin = new Thickness(0, 0, 0, 16) };
        body.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Ui.PillButton("Cancel", false, 80, 34);
        var save = Ui.PillButton("Save", true, 80, 34);
        cancel.Click += (_, _) => dlg.Close();
        save.Click += (_, _) => { result = box.Text; dlg.Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        body.Children.Add(buttons);
        await dlg.ShowDialog(owner);
        return result;
    }

    public static async Task<int> ChooseAsync(Window owner, string message, string title, string opt1, string opt2, string opt3)
    {
        int result = 0;
        var dlg = SmallDialog(title, 480, 280, out var body);
        body.Children.Add(new TextBlock { Text = message, Foreground = Ui.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        void AddOpt(string text, int val)
        {
            var b = new Button
            {
                Content = text, HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center,
                Background = Ui.Surface, Foreground = Ui.Text, BorderBrush = Ui.Border, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12), Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            b.Click += (_, _) => { result = val; dlg.Close(); };
            body.Children.Add(b);
        }
        AddOpt(opt1, 1);
        AddOpt(opt2, 2);
        AddOpt(opt3, 3);
        await dlg.ShowDialog(owner);
        return result;
    }

    public static async Task<string?> PromptOpenFileAsync(Window owner, string title, string[] patterns)
    {
        var top = TopLevel.GetTopLevel(owner);
        if (top?.StorageProvider is not { } sp) return null;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(title) { Patterns = patterns } },
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> PromptFolderAsync(Window owner, string desc)
    {
        var top = TopLevel.GetTopLevel(owner);
        if (top?.StorageProvider is not { } sp) return null;
        var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = desc, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    static Window SmallDialog(string title, double width, double height, out StackPanel body)
    {
        var dlg = new Window
        {
            Title = title, Width = width, Height = height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Ui.Bg,
        };
        Ui.ApplyChrome(dlg);
        body = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(20) };
        dlg.Content = body;
        return dlg;
    }
}
