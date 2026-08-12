/*
 * This UI code turns editor state into controls and converts user edits back into validated domain operations.
 * Dialog/window state is intentionally temporary: values should only become authoritative scene changes through
 * the session/controller path, which preserves cancel, undo, and renderer invalidation behavior.
 */
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

// ExportFormatDialog owns temporary Avalonia presentation/edit state. Values become durable only when accepted and
// routed through the relevant session/controller operation, preserving validation and cancellation semantics.
/// <summary>Explicit format selection shown before choosing an export directory.</summary>
internal sealed class ExportFormatDialog : Window
{
    private readonly ComboBox formatBox;

    public ExportFormatDialog()
    {
        Title = "Export package";
        Width = 460;
        Height = 230;
        MinWidth = 420;
        MinHeight = 210;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        formatBox = new ComboBox
        {
            ItemsSource = SceneExportFormats.All,
            SelectedItem = SceneExportFormats.Find("gltf"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        Button exportButton = new()
        {
            Content = "Choose destination…",
            MinWidth = 150,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Button cancelButton = new()
        {
            Content = "Cancel",
            MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        exportButton.Click += (_, _) =>
            Close(formatBox.SelectedItem as SceneExportFormat);
        cancelButton.Click += (_, _) => Close(null);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(exportButton);

        StackPanel panel = new() { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "Choose the model format for this export package.",
            FontWeight = FontWeight.SemiBold
        });
        panel.Children.Add(formatBox);
        panel.Children.Add(new TextBlock
        {
            Text = "Optimized glTF/GLB is recommended for ungrouped or chunked scenes. The program creates a new directory; related resources are external and named res_0001.ext, res_0002.ext, and so on.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        });
        panel.Children.Add(buttons);

        Content = new Border
        {
            Padding = new Avalonia.Thickness(18),
            Child = panel
        };
    }
}
