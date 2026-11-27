// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/MotionManagerWindow.cs
// Purpose: Modeless manager for object motion tracks and their keyframes.
//
// Keyframes edited here drive the Animation Studio timeline: the first key
// stores an absolute pose, later keys store deltas from the previous key, so
// shifting an early key ripples the motion after it. Edits apply immediately
// without undo steps and mark the document modified.
// -----------------------------------------------------------------------------
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed class MotionManagerWindow : Window
{
    private sealed record ObjectChoice(int Id, string Label)
    {
        /// <summary>Returns the object label for list display.</summary>
        public override string ToString() => Label;
    }

    private sealed record KeyframeChoice(MotionKeyframe Key, Vec3 Absolute, bool IsFirst)
    {
        private const double RadiansToDegrees = 180.0 / Math.PI;

        /// <summary>Returns the keyframe delta and absolute position for list display.</summary>
        public override string ToString() => IsFirst
            ? $"F{Key.Frame} [abs] p({V(Key.Position)}) r({D(Key.Rotation)})°"
            : $"F{Key.Frame} Δp({V(Key.Position)}) Δr({D(Key.Rotation)})° → p({V(Absolute)})";

        private static string V(Vec3 v) => $"{v.X:0.#}, {v.Y:0.#}, {v.Z:0.#}";

        private static string D(Vec3 v) =>
            $"{v.X * RadiansToDegrees:0.#}, {v.Y * RadiansToDegrees:0.#}, {v.Z * RadiansToDegrees:0.#}";
    }

    private readonly ComposerSceneSession session;
    private readonly Action markModified;
    private readonly Action onClosed;

    private readonly ComboBox objectBox;
    private readonly ListBox keyList;
    private readonly TextBox frameBox;
    private readonly TextBox deltaPositionXBox;
    private readonly TextBox deltaPositionYBox;
    private readonly TextBox deltaPositionZBox;
    private readonly TextBox deltaRotationXBox;
    private readonly TextBox deltaRotationYBox;
    private readonly TextBox deltaRotationZBox;
    private readonly TextBox deltaScaleXBox;
    private readonly TextBox deltaScaleYBox;
    private readonly TextBox deltaScaleZBox;
    private readonly TextBlock keyAbsoluteLabel;
    private readonly TextBlock statusText;

    /// <summary>Creates a modeless motion manager bound to the session.</summary>
    public MotionManagerWindow(
        ComposerSceneSession session,
        Action markModified,
        Action onClosed)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.markModified = markModified ?? (() => { });
        this.onClosed = onClosed ?? (() => { });

        Title = "Motion";
        Width = 470;
        Height = 660;
        MinWidth = 380;
        MinHeight = 540;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        objectBox = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch };
        objectBox.SelectionChanged += (_, _) => RefreshKeyframeList();
        keyList = new ListBox { MinHeight = 140 };
        keyList.SelectionChanged += (_, _) => LoadSelectedKey();
        frameBox = new TextBox { MinWidth = 80, TextAlignment = TextAlignment.Right, Text = "0" };
        deltaPositionXBox = SmallNumberBox();
        deltaPositionYBox = SmallNumberBox();
        deltaPositionZBox = SmallNumberBox();
        deltaRotationXBox = SmallNumberBox();
        deltaRotationYBox = SmallNumberBox();
        deltaRotationZBox = SmallNumberBox();
        deltaScaleXBox = SmallNumberBox();
        deltaScaleYBox = SmallNumberBox();
        deltaScaleZBox = SmallNumberBox();
        keyAbsoluteLabel = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.72, FontSize = 12 };
        statusText = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.76, FontSize = 12 };

        Content = BuildContent();
        RefreshObjects(keepSelection: false);
        Activated += (_, _) => RefreshObjects(keepSelection: true);
        Closed += (_, _) => onClosed();
    }

    /// <summary>Builds the manager content with its object picker and keyframe list.</summary>
    private Control BuildContent()
    {
        StackPanel stack = new() { Spacing = 12 };
        stack.Children.Add(new TextBlock
        {
            Text = "Motion",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Keyframe each object's transform on global movie frames. The Animation Studio timeline plays these tracks back; pose the object in the main window, then capture.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        });
        stack.Children.Add(new TextBlock { Text = "Object", FontWeight = FontWeight.SemiBold });
        stack.Children.Add(objectBox);
        stack.Children.Add(new TextBlock
        {
            Text = "Keyframes (first is absolute, rest are deltas from the previous key)",
            FontWeight = FontWeight.SemiBold
        });
        stack.Children.Add(keyList);
        stack.Children.Add(new TextBlock
        {
            Text = "Selected key: Δposition, Δrotation (degrees), Δscale",
            FontWeight = FontWeight.SemiBold
        });
        Grid deltaEditor = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnSpacing = 6,
            RowSpacing = 6
        };
        AddDeltaRow(deltaEditor, 0, "Position", deltaPositionXBox, deltaPositionYBox, deltaPositionZBox);
        AddDeltaRow(deltaEditor, 1, "Rotation°", deltaRotationXBox, deltaRotationYBox, deltaRotationZBox);
        AddDeltaRow(deltaEditor, 2, "Scale", deltaScaleXBox, deltaScaleYBox, deltaScaleZBox);
        stack.Children.Add(deltaEditor);
        Button applyDelta = Tip(NewButton("Apply"), "Writes the entered deltas into the selected keyframe.");
        applyDelta.Click += (_, _) => ApplyDelta();
        Grid applyRow = new() { ColumnDefinitions = new ColumnDefinitions("Auto"), ColumnSpacing = 8 };
        applyRow.Children.Add(applyDelta);
        stack.Children.Add(applyRow);
        stack.Children.Add(keyAbsoluteLabel);

        Grid captureRow = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        TextBlock frameLabel = new() { Text = "Frame:", VerticalAlignment = VerticalAlignment.Center };
        captureRow.Children.Add(frameLabel);
        captureRow.Children.Add(frameBox);
        Grid.SetColumn(frameBox, 1);
        Button captureKey = Tip(NewButton("Capture"), "Records the picked object's current transform at the entered frame.");
        captureKey.Click += (_, _) => CaptureKeyframe();
        captureRow.Children.Add(captureKey);
        Grid.SetColumn(captureKey, 2);
        stack.Children.Add(captureRow);

        Grid keyRow = new() { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 8 };
        Button deleteKey = Tip(NewButton("Delete key"), "Deletes the selected keyframe.");
        deleteKey.Click += (_, _) => DeleteKeyframe();
        keyRow.Children.Add(deleteKey);
        Button deleteTrack = Tip(NewButton("Delete track"), "Deletes the whole motion track for the picked object.");
        deleteTrack.Click += (_, _) => DeleteTrack();
        keyRow.Children.Add(deleteTrack);
        Grid.SetColumn(deleteTrack, 1);
        stack.Children.Add(keyRow);
        stack.Children.Add(statusText);

        return new Border
        {
            Padding = new Thickness(16),
            Child = new ScrollViewer
            {
                Content = stack,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            }
        };
    }

    /// <summary>Creates a button with the given text.</summary>
    private static Button NewButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(10, 6),
        MinWidth = 78
    };

    /// <summary>Sets a tooltip on the given control and returns it for inline use.</summary>
    private static T Tip<T>(T control, string text) where T : Control
    {
        ToolTip.SetTip(control, text);
        return control;
    }

    /// <summary>Creates a compact numeric entry box.</summary>
    private static TextBox SmallNumberBox() =>
        new() { MinWidth = 64, TextAlignment = TextAlignment.Right };

    /// <summary>Adds one labeled XYZ row to the delta editor grid.</summary>
    private static void AddDeltaRow(Grid grid, int row, string label, TextBox x, TextBox y, TextBox z)
    {
        TextBlock caption = new() { Text = label, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(caption);
        Grid.SetColumn(caption, 0);
        Grid.SetRow(caption, row);
        grid.Children.Add(x);
        Grid.SetColumn(x, 1);
        Grid.SetRow(x, row);
        grid.Children.Add(y);
        Grid.SetColumn(y, 2);
        Grid.SetRow(y, row);
        grid.Children.Add(z);
        Grid.SetColumn(z, 3);
        Grid.SetRow(z, row);
    }

    /// <summary>Reloads the object picker, preferring tracked objects on first open.</summary>
    private void RefreshObjects(bool keepSelection)
    {
        int? keep = keepSelection ? (objectBox.SelectedItem as ObjectChoice)?.Id : null;
        ObjectChoice[] choices = session.GetObjectInfos()
            .Select(static info => new ObjectChoice(info.Id, new string(' ', info.Depth * 2) + info.Name))
            .ToArray();
        objectBox.ItemsSource = choices;
        ObjectChoice? selected = choices.FirstOrDefault(choice => choice.Id == keep);
        if (selected == null && !keepSelection)
        {
            HashSet<int> trackedIds = session.GetStudioState().Tracks.Select(static track => track.ObjectId).ToHashSet();
            selected = choices.FirstOrDefault(choice => trackedIds.Contains(choice.Id));
        }
        objectBox.SelectedItem = selected ?? choices.FirstOrDefault();
        RefreshKeyframeList();
    }

    /// <summary>Reloads the keyframe list for the picked object.</summary>
    private void RefreshKeyframeList()
    {
        int? target = (objectBox.SelectedItem as ObjectChoice)?.Id;
        StudioState state = session.GetStudioState();
        MotionTrack? track = target.HasValue
            ? state.Tracks.FirstOrDefault(candidate => candidate.ObjectId == target.Value)
            : null;
        int keep = (keyList.SelectedItem as KeyframeChoice)?.Key.Frame ?? -1;
        List<(int Frame, MotionPose Pose)> absolute = track?.AbsolutePoses() ?? new List<(int Frame, MotionPose Pose)>();
        KeyframeChoice[] choices = new KeyframeChoice[absolute.Count];
        for (int i = 0; i < absolute.Count; i++)
            choices[i] = new KeyframeChoice(track!.Keys[i], absolute[i].Pose.Position, i == 0);
        keyList.ItemsSource = choices;
        keyList.SelectedItem = choices.FirstOrDefault(choice => choice.Key.Frame == keep);
        LoadSelectedKey();
        if (track != null && frameBox != null && string.IsNullOrWhiteSpace(frameBox.Text))
            frameBox.Text = "0";
        if (track == null && state.Tracks.Count > 0)
        {
            string[] names = state.Tracks
                .Select(candidate => session.GetObjectState(candidate.ObjectId)?.Name ?? $"#{candidate.ObjectId}")
                .ToArray();
            statusText.Text = $"Motion tracks exist for: {string.Join(", ", names)}. Pick one above.";
        }
    }

    /// <summary>Records the picked object's transform at the entered frame.</summary>
    private void CaptureKeyframe()
    {
        if ((objectBox.SelectedItem as ObjectChoice)?.Id is not int target)
        {
            statusText.Text = "Pick an object first.";
            return;
        }
        if (!int.TryParse(frameBox.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int frame) || frame < 0)
        {
            statusText.Text = "Frame must be zero or greater.";
            return;
        }
        ComposerObjectState? objectState = session.GetObjectState(target);
        if (objectState == null)
        {
            statusText.Text = "That object no longer exists.";
            RefreshObjects(keepSelection: false);
            return;
        }
        if (!session.CaptureMotionKeyframe(target, frame, new MotionPose(
            objectState.Position, objectState.Rotation, objectState.Scale)))
        {
            statusText.Text = "That object no longer exists.";
            RefreshObjects(keepSelection: false);
            return;
        }
        markModified();
        RefreshKeyframeList();
        statusText.Text = $"Keyframe captured for '{objectState.Name}' at frame {frame + 1}.";
    }

    /// <summary>Deletes the selected keyframe.</summary>
    private void DeleteKeyframe()
    {
        if ((objectBox.SelectedItem as ObjectChoice)?.Id is not int target ||
            keyList.SelectedItem is not KeyframeChoice choice)
        {
            statusText.Text = "Select a keyframe first.";
            return;
        }
        session.RemoveMotionKeyframe(target, choice.Key.Frame);
        markModified();
        RefreshKeyframeList();
    }

    /// <summary>Loads the stored deltas of the selected key into the editor.</summary>
    private void LoadSelectedKey()
    {
        if (keyList.SelectedItem is not KeyframeChoice choice)
        {
            keyAbsoluteLabel.Text = string.Empty;
            return;
        }
        MotionKeyframe key = choice.Key;
        deltaPositionXBox.Text = Format(key.Position.X);
        deltaPositionYBox.Text = Format(key.Position.Y);
        deltaPositionZBox.Text = Format(key.Position.Z);
        deltaRotationXBox.Text = Format(key.Rotation.X * 180.0 / Math.PI);
        deltaRotationYBox.Text = Format(key.Rotation.Y * 180.0 / Math.PI);
        deltaRotationZBox.Text = Format(key.Rotation.Z * 180.0 / Math.PI);
        deltaScaleXBox.Text = Format(key.Scale.X);
        deltaScaleYBox.Text = Format(key.Scale.Y);
        deltaScaleZBox.Text = Format(key.Scale.Z);
        int? target = (objectBox.SelectedItem as ObjectChoice)?.Id;
        MotionTrack? track = target.HasValue
            ? session.GetStudioState().Tracks.FirstOrDefault(candidate => candidate.ObjectId == target.Value)
            : null;
        MotionPose? absolute = track?.AbsolutePoses().FirstOrDefault(entry => entry.Frame == key.Frame).Pose;
        keyAbsoluteLabel.Text = absolute == null
            ? string.Empty
            : choice.IsFirst
                ? $"First key: stored values are the absolute pose p({Format3(absolute.Position)}) r({FormatDegrees(absolute.Rotation)})° s({Format3(absolute.Scale)})."
                : $"Results in absolute p({Format3(absolute.Position)}) r({FormatDegrees(absolute.Rotation)})° s({Format3(absolute.Scale)}); editing ripples later keys.";
    }

    /// <summary>Writes the entered deltas into the selected keyframe.</summary>
    private void ApplyDelta()
    {
        if ((objectBox.SelectedItem as ObjectChoice)?.Id is not int target ||
            keyList.SelectedItem is not KeyframeChoice choice)
        {
            statusText.Text = "Select a keyframe first.";
            return;
        }
        if (!TryDelta(out Vec3 position, out Vec3 rotation, out Vec3 scale))
        {
            statusText.Text = "Deltas must be numbers (rotation in degrees).";
            return;
        }
        session.AddMotionKeyframe(target, choice.Key with
        {
            Position = position,
            Rotation = rotation,
            Scale = scale
        });
        markModified();
        RefreshKeyframeList();
        statusText.Text = choice.IsFirst
            ? "Absolute pose updated."
            : "Delta updated; later keys ripple to the same motion offset.";
    }

    /// <summary>Parses the nine delta boxes, converting rotation from degrees.</summary>
    private bool TryDelta(out Vec3 position, out Vec3 rotation, out Vec3 scale)
    {
        position = Vec3.Zero;
        rotation = Vec3.Zero;
        scale = Vec3.Zero;
        if (!TryDouble(deltaPositionXBox.Text, out double px) ||
            !TryDouble(deltaPositionYBox.Text, out double py) ||
            !TryDouble(deltaPositionZBox.Text, out double pz) ||
            !TryDouble(deltaRotationXBox.Text, out double rx) ||
            !TryDouble(deltaRotationYBox.Text, out double ry) ||
            !TryDouble(deltaRotationZBox.Text, out double rz) ||
            !TryDouble(deltaScaleXBox.Text, out double sx) ||
            !TryDouble(deltaScaleYBox.Text, out double sy) ||
            !TryDouble(deltaScaleZBox.Text, out double sz))
            return false;
        position = new Vec3(px, py, pz);
        rotation = new Vec3(rx * Math.PI / 180.0, ry * Math.PI / 180.0, rz * Math.PI / 180.0);
        scale = new Vec3(sx, sy, sz);
        return true;
    }

    private static bool TryDouble(string? text, out double value) =>
        double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Format3(Vec3 v) => $"{Format(v.X)}, {Format(v.Y)}, {Format(v.Z)}";

    private static string FormatDegrees(Vec3 v) =>
        $"{Format(v.X * 180.0 / Math.PI)}, {Format(v.Y * 180.0 / Math.PI)}, {Format(v.Z * 180.0 / Math.PI)}";

    /// <summary>Deletes the whole motion track for the picked object.</summary>
    private void DeleteTrack()
    {
        if ((objectBox.SelectedItem as ObjectChoice)?.Id is not int target)
        {
            statusText.Text = "Pick an object first.";
            return;
        }
        session.RemoveMotionTrack(target);
        markModified();
        RefreshKeyframeList();
        statusText.Text = "Motion track deleted.";
    }
}
