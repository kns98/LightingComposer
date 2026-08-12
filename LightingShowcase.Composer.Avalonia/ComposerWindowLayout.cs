/*
 * This UI code turns editor state into controls and converts user edits back into validated domain operations.
 * Dialog/window state is intentionally temporary: values should only become authoritative scene changes through
 * the session/controller path, which preserves cancel, undo, and renderer invalidation behavior.
 */
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LightingShowcase.Composer;

/// <summary>
/// Builds the Composer window's three-panel visual shell. This class owns only
/// layout/composition; behavior remains in the controllers wired by ComposerWindow.
/// </summary>
internal static class ComposerWindowLayout
{
    internal sealed record Controls(
        Menu MenuBar,
        Border Viewport,
        ScrollViewer ObjectTree,
        Button DuplicateButton,
        Button GroupButton,
        Button UngroupButton,
        Button DeleteButton,
        TextBox NameBox,
        CheckBox VisibleBox,
        Button ParametersButton,
        Button MaterialButton,
        TextBox PositionX,
        TextBox PositionY,
        TextBox PositionZ,
        TextBox RotationX,
        TextBox RotationY,
        TextBox RotationZ,
        TextBox ScaleX,
        TextBox ScaleY,
        TextBox ScaleZ,
        Button ApplyButton,
        Button FrameButton,
        Button ResetTransformButton,
        TextBlock PathText,
        TextBlock StatusText,
        TextBlock DetailsText);

    public static Control Build(Controls ui)
    {
        ArgumentNullException.ThrowIfNull(ui);

        Grid root = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };

        root.Children.Add(ui.MenuBar);

        Grid content = new()
        {
            ColumnDefinitions = new ColumnDefinitions("280,5,*,5,310"),
            Margin = new Thickness(10, 0, 10, 8)
        };

        Control scenePanel = BuildScenePanel(ui);
        content.Children.Add(scenePanel);
        Grid.SetColumn(scenePanel, 0);

        GridSplitter leftSplitter = new() { Width = 5, ResizeDirection = GridResizeDirection.Columns };
        content.Children.Add(leftSplitter);
        Grid.SetColumn(leftSplitter, 1);

        content.Children.Add(ui.Viewport);
        Grid.SetColumn(ui.Viewport, 2);

        GridSplitter rightSplitter = new() { Width = 5, ResizeDirection = GridResizeDirection.Columns };
        content.Children.Add(rightSplitter);
        Grid.SetColumn(rightSplitter, 3);

        Control inspector = BuildInspectorPanel(ui);
        content.Children.Add(inspector);
        Grid.SetColumn(inspector, 4);

        root.Children.Add(content);
        Grid.SetRow(content, 1);

        Border statusBar = new()
        {
            Padding = new Thickness(10, 6),
            Child = new StackPanel
            {
                Spacing = 2,
                Children = { ui.PathText, ui.StatusText, ui.DetailsText }
            }
        };
        root.Children.Add(statusBar);
        Grid.SetRow(statusBar, 2);
        return root;
    }

    private static Control BuildScenePanel(Controls ui)
    {
        Grid panel = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8
        };

        panel.Children.Add(Heading("Scene objects"));
        panel.Children.Add(ui.ObjectTree);
        Grid.SetRow(ui.ObjectTree, 1);

        Grid objectButtons = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 8,
            RowSpacing = 6
        };
        objectButtons.Children.Add(ui.DuplicateButton);
        objectButtons.Children.Add(ui.GroupButton);
        Grid.SetColumn(ui.GroupButton, 1);
        objectButtons.Children.Add(ui.UngroupButton);
        Grid.SetColumn(ui.UngroupButton, 2);
        objectButtons.Children.Add(ui.DeleteButton);
        Grid.SetRow(ui.DeleteButton, 1);
        Grid.SetColumnSpan(ui.DeleteButton, 3);
        panel.Children.Add(objectButtons);
        Grid.SetRow(objectButtons, 2);

        return panel;
    }

    private static Control BuildInspectorPanel(Controls ui)
    {
        StackPanel stack = new() { Spacing = 9, Margin = new Thickness(8, 0, 0, 0) };
        stack.Children.Add(Heading("Inspector"));
        stack.Children.Add(LabeledControl("Name", ui.NameBox));
        stack.Children.Add(ui.VisibleBox);

        Grid editButtons = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8
        };
        editButtons.Children.Add(ui.ParametersButton);
        editButtons.Children.Add(ui.MaterialButton);
        Grid.SetColumn(ui.MaterialButton, 1);
        stack.Children.Add(editButtons);

        stack.Children.Add(new TextBlock
        {
            Text = "Scene length unit: meter (m). Primitive dimensions and object positions use meters.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            FontSize = 12
        });
        stack.Children.Add(Heading("Position (m)"));
        stack.Children.Add(VectorRow(ui.PositionX, ui.PositionY, ui.PositionZ));
        stack.Children.Add(Heading("Rotation (degrees)"));
        stack.Children.Add(VectorRow(ui.RotationX, ui.RotationY, ui.RotationZ));
        stack.Children.Add(Heading("Scale"));
        stack.Children.Add(VectorRow(ui.ScaleX, ui.ScaleY, ui.ScaleZ));
        stack.Children.Add(ui.ApplyButton);
        stack.Children.Add(ui.FrameButton);
        stack.Children.Add(ui.ResetTransformButton);
        stack.Children.Add(new TextBlock
        {
            Text = "Hierarchy: ▸/▾ expands groups and … show faces reveals logical polygon faces (a Cube has six). Ctrl-click objects to multi-select; Group/Ctrl+G wraps sibling objects and Ctrl+Shift+G ungroups. Standard primitives: Plane, Cube, Circle, UV Sphere, Icosphere, Cylinder, Cone, Torus, and Grid. Use Parameters… for real dimensions in meters and Material… for PBR/color/textures. Face mode (3): right-click a polygon for Extrude or Inset; Extrude uses signed distance (+ outward, - inward), while inset depth uses + inward / - outward and offers Square or Sloped (Blender-style) depth profiles. Object/Vertex/Edge/Face modes use 4/1/2/3. Gizmos: G move, R rotate, S scale; Shift is precision and Ctrl snaps. Viewport: right drag orbits, middle drag pans, and mouse wheel zooms. On Windows Precision Touchpads, two-finger translation orbits, pinch/spread zooms, and two-finger twist turns the scene around its center. Use Render > Settings… for renderer-specific controls. Unsupported settings stay visible but disabled. Vulkan compute supports resolution, samples, bounces, field of view, exposure, ambient strength, shadows, and background colors; CPU supports resolution, samples, bounces, field of view, and exposure.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68,
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 0)
        });

        return new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = Avalonia.Media.FontWeight.SemiBold,
        FontSize = 15
    };

    private static Control LabeledControl(string label, Control control)
    {
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8
        };
        TextBlock text = new() { Text = label, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(text);
        grid.Children.Add(control);
        Grid.SetColumn(control, 1);
        return grid;
    }

    private static Control VectorRow(TextBox x, TextBox y, TextBox z)
    {
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto,*"),
            ColumnSpacing = 5
        };
        Add("X", x, 0);
        Add("Y", y, 2);
        Add("Z", z, 4);
        return grid;

        void Add(string label, TextBox box, int column)
        {
            TextBlock text = new() { Text = label, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(text);
            Grid.SetColumn(text, column);
            grid.Children.Add(box);
            Grid.SetColumn(box, column + 1);
        }
    }
}
