// Small modal text prompt (used by Save-as naming). Returns the entered
// text, or null when the dialog is cancelled.
using Avalonia.Controls;
using Avalonia.Layout;

namespace LightingShowcase.Composer;

internal sealed class TextPromptDialog : Window
{
    /// <summary>Asks for one line of text and returns it, or null when cancelled.</summary>
    public static Task<string?> AskAsync(Window owner, string title, string label, string confirmText = "OK")
    {
        TextPromptDialog dialog = new(title, label, confirmText);
        return dialog.ShowDialog<string?>(owner);
    }

    private TextPromptDialog(string title, string label, string confirmText)
    {
        Title = title;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        TextBox input = new() { MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Stretch };
        Button confirm = new() { Content = confirmText, IsDefault = true, MinWidth = 78, HorizontalAlignment = HorizontalAlignment.Right };
        confirm.Click += (_, _) => Close(input.Text);
        Button cancel = new() { Content = "Cancel", IsCancel = true, MinWidth = 78, HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => Close(null);

        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);

        StackPanel stack = new() { Spacing = 10 };
        stack.Children.Add(new TextBlock { Text = label, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        stack.Children.Add(input);
        stack.Children.Add(buttons);

        Content = new Border { Padding = new Avalonia.Thickness(16), Child = stack };
        Opened += (_, _) => input.Focus();
    }
}
