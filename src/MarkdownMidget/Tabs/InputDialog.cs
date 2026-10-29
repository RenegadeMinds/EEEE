using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MarkdownMidget.Tabs;

/// <summary>A small "type a name" box (WPF has no InputBox). Built in code; uses the app's chrome colours.</summary>
public static class InputDialog
{
    /// <summary>Show the box with <paramref name="text"/> filled in and the caret at its end. Null on Cancel.</summary>
    public static string? Ask(Window owner, string title, string prompt, string text, string? hint = null)
    {
        var box = new TextBox { Text = text, MinWidth = 380, Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 6, 0, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        if (hint is not null)
        {
            var h = new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            h.SetResourceReference(TextBlock.ForegroundProperty, "Chrome.Text.Secondary");
            panel.Children.Add(h);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        var win = new Window
        {
            Title = title, Owner = owner, Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };
        win.SetResourceReference(Control.BackgroundProperty, "Chrome.Window.Background");
        win.SetResourceReference(Control.ForegroundProperty, "Chrome.Text");
        ok.Click += (_, _) => win.DialogResult = true;
        win.Loaded += (_, _) => { box.Focus(); box.CaretIndex = box.Text.Length; Keyboard.Focus(box); };
        return win.ShowDialog() == true ? box.Text : null;
    }
}
