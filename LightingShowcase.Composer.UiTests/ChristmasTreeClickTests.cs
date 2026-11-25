// Christmas tree drawn with simulated mouse clicks through the real Composer UI.
// Mirrors docs/DRAW_CHRISTMAS_TREE.md step for step: Add-button clicks,
// Parameters fields, Inspector transform boxes, Material editor, Lighting
// editor, then viewport screenshots. Menu leaf items are invoked via routed
// Click events (popup placement belongs to Avalonia); everything else is a
// real MouseDown/Move/Up at control bounds. File-save dialogs are excluded
// (native OS); persistence is asserted through the session API instead.
// Requires one networked restore for the Avalonia.Headless packages, then:
// dotnet test LightingShowcase.Composer.UiTests.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using LightingShowcase.CameraSystem;
using LightingShowcase.Composer;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.UiTests;

public sealed class ChristmasTreeClickTests
{
    private static T Find<T>(Control root, Func<T, bool> match) where T : Control
    {
        if (root is T self && match(self))
            return self;
        foreach (T control in root.GetVisualDescendants().OfType<T>())
        {
            if (match(control))
                return control;
        }
        throw new InvalidOperationException("Control not found: " + typeof(T).Name);
    }

    private static T FindByName<T>(Control root, string name) where T : Control =>
        Find<T>(root, c => c.Name == name);

    private static Button FindButton(Control root, string content) =>
        Find<Button>(root, b => Equals(b.Content?.ToString(), content));

    private static ComboBox PrimitivePicker(ComposerWindow window) =>
        FindByName<ComboBox>(window, "PrimitivePicker");

    private static Point CenterOf(Control control, Visual relativeTo)
    {
        if (control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
            throw new InvalidOperationException("Control has empty bounds (theme/layout missing?): " + control.GetType().Name);
        Point? point = control.TranslatePoint(new Point(control.Bounds.Width / 2.0, control.Bounds.Height / 2.0), relativeTo);
        if (point == null)
            throw new InvalidOperationException("Cannot map control center: " + control.GetType().Name);
        return point.Value;
    }

    private static void Click(Window window, Control control)
    {
        Point point = CenterOf(control, window);
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

    private static void SetText(Window window, TextBox box, string text)
    {
        Click(window, box);
        box.Text = text;
    }

    private static void InvokeMenuItem(MenuItem item) =>
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private static async Task WaitForAsync(Func<bool> condition, string what, int timeoutMs = 30000)
    {
        DateTime start = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                throw new TimeoutException("Timed out waiting for: " + what);
            await Task.Delay(100);
        }
    }

    private static async Task<Window> WaitForDialogAsync(ComposerWindow owner, string titlePrefix)
    {
        Window? found = null;
        await WaitForAsync(() =>
        {
            Window? dialog = owner.Dialogs.ActiveDialog();
            if (dialog != null && dialog.Title != null && dialog.Title.StartsWith(titlePrefix, StringComparison.Ordinal))
            {
                found = dialog;
                return true;
            }
            return false;
        }, "dialog: " + titlePrefix);
        return found!;
    }

    private static string FindRepoDir(string name)
    {
        string? directory = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && directory != null; i++)
        {
            string candidate = Path.Combine(directory, name);
            if (Directory.Exists(candidate))
                return candidate;
            directory = Path.GetDirectoryName(directory);
        }
        return Path.Combine(AppContext.BaseDirectory, name);
    }

    private static void CloseDialog(Window dialog) => dialog.Close();

    private static int PrimitiveIndex(ComboBox picker, string name)
    {
        for (int i = 0; i < picker.Items.Count; i++)
        {
            if (Equals(picker.Items[i]?.ToString(), name))
                return i;
        }
        throw new InvalidOperationException("Primitive not offered: " + name);
    }

    private async Task SetBaseColorAsync(ComposerWindow window, int id, string hex)
    {
        window.Session.SetSelectedObject(id);
        Click(window, FindButton(window, "Material…"));
        Window dialog = await WaitForDialogAsync(window, "Material");
        SetText(dialog, FindByName<TextBox>(dialog, "MaterialHex"), hex);
        Click(dialog, FindButton(dialog, "Apply color"));
        dialog.Close();
        await WaitForAsync(() => window.Dialogs.ActiveDialog() == null, "material close");
    }

    private async Task PlaceAsync(ComposerWindow window, int id, Vec3 center)
    {
        Aabb? bounds = window.Session.GetTransformTargetBounds(id);
        Assert.NotNull(bounds);
        Vec3 offset = center - (bounds!.Value.Min + bounds!.Value.Max) * 0.5;
        window.Session.SetSelectedObject(id);
        SetText(window, FindByName<TextBox>(window, "PositionX"), offset.X.ToString("0.###"));
        SetText(window, FindByName<TextBox>(window, "PositionY"), offset.Y.ToString("0.###"));
        SetText(window, FindByName<TextBox>(window, "PositionZ"), offset.Z.ToString("0.###"));
        Click(window, FindButton(window, "Apply transform"));
        await Task.Delay(500);
    }

    private static TextBox LabeledField(Window dialog, string label)
    {
        TextBlock? text = dialog.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == label)
            ?? throw new InvalidOperationException("Label not found: " + label);
        Control? parent = text.Parent as Control;
        TextBox? box = parent?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()
            ?? throw new InvalidOperationException("Field not found for label: " + label);
        return box;
    }

    private static IReadOnlyList<TextBox> VectorRowAfter(Window dialog, string heading)
    {
        TextBlock? text = dialog.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == heading)
            ?? throw new InvalidOperationException("Heading not found: " + heading);
        Panel? panel = text.Parent as Panel
            ?? throw new InvalidOperationException("Heading has no panel parent: " + heading);
        int index = panel.Children.IndexOf(text);
        Grid? row = panel.Children.OfType<Grid>().FirstOrDefault(g => panel.Children.IndexOf(g) > index)
            ?? throw new InvalidOperationException("Vector row not found after: " + heading);
        return row.Children.OfType<TextBox>().ToArray();
    }

    private async Task AddLightAsync(
        ComposerWindow window, string addButton, Vec3 position, Vec3? direction,
        string colorHex, string intensity)
    {
        MenuItem renderMenu = Find<MenuItem>(window, m => Equals(m.Header?.ToString(), "_Render"));
        MenuItem? lightItem = renderMenu.GetLogicalDescendants().OfType<MenuItem>()
            .FirstOrDefault(m => Equals(m.Header?.ToString(), "_Lighting…"))
            ?? throw new InvalidOperationException("Lighting menu item not found");
        InvokeMenuItem(lightItem);
        Window dialog = await WaitForDialogAsync(window, "Lighting");
        Click(dialog, FindButton(dialog, addButton));
        IReadOnlyList<TextBox> pos = VectorRowAfter(dialog, "Position (m)");
        SetText(dialog, pos[0], position.X.ToString("0.###"));
        SetText(dialog, pos[1], position.Y.ToString("0.###"));
        SetText(dialog, pos[2], position.Z.ToString("0.###"));
        if (direction != null)
        {
            IReadOnlyList<TextBox> dir = VectorRowAfter(dialog, "Direction");
            SetText(dialog, dir[0], direction.Value.X.ToString("0.###"));
            SetText(dialog, dir[1], direction.Value.Y.ToString("0.###"));
            SetText(dialog, dir[2], direction.Value.Z.ToString("0.###"));
        }
        SetText(dialog, LabeledField(dialog, "Color"), colorHex);
        SetText(dialog, LabeledField(dialog, "Intensity"), intensity);
        Click(dialog, FindButton(dialog, "Apply"));
        dialog.Close();
        await WaitForAsync(() => window.Dialogs.ActiveDialog() == null, "light editor close");
    }

    private static Dictionary<string, string> Params(params (string Key, string Value)[] values)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in values)
            result[key] = value;
        return result;
    }

    [AvaloniaTest]
    public async Task DrawsChristmasTreeWithSimulatedClicks()
    {
        if (Application.Current != null)
        {
            Application.Current.Styles.Add(new FluentTheme());
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
        }

        ComposerWindow window = new(Array.Empty<string>()) { Width = 1280, Height = 800 };
        window.Show();
        int lightsBefore = window.Session.LightCount;

        int snow = await AddClickedPrimitive(window, "Plane", Params(("width", "14"), ("depth", "14")));
        await PlaceAsync(window, snow, new Vec3(0, 0, 0));
        await SetBaseColorAsync(window, snow, "#E6EBF2");

        int trunk = await AddClickedPrimitive(window, "Cylinder",
            Params(("radius", "0.18"), ("height", "1.0"), ("sides", "12"), ("capFill", "1")));
        await PlaceAsync(window, trunk, new Vec3(0, 0.5, 0));
        await SetBaseColorAsync(window, trunk, "#59381F");

        (double Y, double R, double H)[] cones = [(1.9, 2.0, 1.8), (3.0, 1.45, 1.5), (3.95, 0.9, 1.2)];
        foreach ((double y, double r, double h) in cones)
        {
            int cone = await AddClickedPrimitive(window, "Cone", Params(
                ("radius1", r.ToString("0.###")), ("radius2", "0"), ("height", h.ToString("0.###")),
                ("sides", "24"), ("capFill", "1")));
            await PlaceAsync(window, cone, new Vec3(0, y, 0));
            await SetBaseColorAsync(window, cone, "#296B21");
        }

        int topper = await AddClickedPrimitive(window, "UV Sphere",
            Params(("radius", "0.16"), ("longitudeSegments", "16"), ("latitudeSegments", "12")));
        await PlaceAsync(window, topper, new Vec3(0, 4.75, 0));
        await SetBaseColorAsync(window, topper, "#D9A633");

        (double X, double Y, double Z, string Color)[] ornaments =
        [
            (1.33, 1.6, 0, "#B3140F"), (-1.33, 1.6, 0, "#D9A633"), (0, 1.6, 1.33, "#1A40B3"),
            (0, 1.6, -1.33, "#B3140F"), (0.72, 3.0, 0, "#D9A633"), (-0.72, 3.0, 0, "#1A40B3"),
            (0.49, 3.9, 0, "#B3140F")
        ];
        foreach ((double x, double y, double z, string color) in ornaments)
        {
            int ornament = await AddClickedPrimitive(window, "UV Sphere",
                Params(("radius", "0.14"), ("longitudeSegments", "12"), ("latitudeSegments", "8")));
            await PlaceAsync(window, ornament, new Vec3(x, y, z));
            await SetBaseColorAsync(window, ornament, color);
        }

        (double X, double Y, double Z, double S, string Color)[] presents =
        [
            (1.9, 0.35, 1.2, 0.7, "#B3140F"), (-1.7, 0.275, 0.8, 0.55, "#296B21"), (1.4, 0.225, -1.6, 0.45, "#1A40B3")
        ];
        foreach ((double x, double y, double z, double s, string color) in presents)
        {
            int present = await AddClickedPrimitive(window, "Cube", Params(
                ("width", s.ToString("0.###")), ("height", s.ToString("0.###")), ("depth", s.ToString("0.###"))));
            await PlaceAsync(window, present, new Vec3(x, y, z));
            await SetBaseColorAsync(window, present, color);
        }

        Assert.Equal(16, window.Session.ObjectCount);

        await AddLightAsync(window, "+ Point", new Vec3(3.0, 5.0, -3.0), null, "#FFD999", "6");
        await AddLightAsync(window, "+ Directional", new Vec3(0, 5, 0), new Vec3(-0.4, -1.0, 0.3), "#B3CCFF", "1.5");
        Assert.Equal(lightsBefore + 2, window.Session.LightCount);

        window.Session.SetSelectedObject(null);
        CameraDefinition camera = new() { Position = new Vec3(2.17, 3.51, -6.52), Target = new Vec3(0, 2.2, 0) };
        string screenshots = FindRepoDir("screenshots");
        (ComposerRendererKind Kind, string File)[] backends =
        [
            (ComposerRendererKind.Raster, "ChristmasTree-click-raster.png"),
            (ComposerRendererKind.VulkanRaster, "ChristmasTree-click-vraster.png"),
            (ComposerRendererKind.VulkanCompute, "ChristmasTree-click-vulkan.png"),
            (ComposerRendererKind.Cpu, "ChristmasTree-click-cpu.png")
        ];
        foreach ((ComposerRendererKind kind, string file) in backends)
        {
            ComposerFrame frame = window.Session.Render(kind, camera, 480, 360, interactive: false, CancellationToken.None);
            string output = Path.Combine(screenshots, file);
            PngWriter.Write(output, frame.Image);
            Assert.True(new FileInfo(output).Length > 4096, file);
        }

        window.Close();
    }

    private async Task<int> AddClickedPrimitive(
        ComposerWindow window, string kind, IReadOnlyDictionary<string, string> parameters)
    {
        ComboBox picker = FindByName<ComboBox>(window, "PrimitivePicker");
        picker.SelectedIndex = PrimitiveIndex(picker, kind);
        int before = window.Session.ObjectCount;
        Click(window, FindButton(window, "Add primitive"));
        await WaitForAsync(() => window.Session.ObjectCount == before + 1, "insert " + kind);
        int id = window.Session.SelectedObjectId ?? throw new InvalidOperationException("Insert did not select: " + kind);
        Window dialog = await WaitForDialogAsync(window, "Parameters");
        foreach ((string key, string value) in parameters)
            SetText(dialog, FindByName<TextBox>(dialog, "Param_" + key), value);
        Click(dialog, FindButton(dialog, "Apply"));
        dialog.Close();
        await WaitForAsync(() => window.Dialogs.ActiveDialog() == null, "parameters close");
        return id;
    }
}
