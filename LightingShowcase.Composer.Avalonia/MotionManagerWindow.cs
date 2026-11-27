// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/MotionManagerWindow.cs
// Purpose: Modeless manager for object motion tracks and their keyframes.
//
// Keyframes edited here drive the Animation Studio timeline: each key records
// an object's full transform at one global movie frame. Edits apply
// immediately without undo steps and mark the document modified.
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

    private sealed record KeyframeChoice(MotionKeyframe Key)
    {
        /// <summary>Returns the keyframe label for list display.</summary>
        public override string ToString() =>
            $"Frame {Key.Frame} @ ({Key.Position.X:0.##}, {Key.Position.Y:0.##}, {Key.Position.Z:0.##})";
    }

    private readonly ComposerSceneSession session;
    private readonly Action markModified;
    private readonly Action onClosed;

    private readonly ComboBox objectBox;
    private readonly ListBox keyList;
    private readonly TextBox frameBox;
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
        Width = 440;
        Height = 520;
        MinWidth = 360;
        MinHeight = 400;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        objectBox = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch };
        objectBox.SelectionChanged += (_, _) => RefreshKeyframeList();
        keyList = new ListBox { MinHeight = 140 };
        frameBox = new TextBox { MinWidth = 80, TextAlignment = TextAlignment.Right, Text = "0" };
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
        stack.Children.Add(keyList);

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

    /// <summary>Reloads the object picker, preserving the selection when asked.</summary>
    private void RefreshObjects(bool keepSelection)
    {
        int? keep = keepSelection ? (objectBox.SelectedItem as ObjectChoice)?.Id : null;
        ObjectChoice[] choices = session.GetObjectInfos()
            .Select(static info => new ObjectChoice(info.Id, new string(' ', info.Depth * 2) + info.Name))
            .ToArray();
        objectBox.ItemsSource = choices;
        objectBox.SelectedItem = choices.FirstOrDefault(choice => choice.Id == keep) ?? choices.FirstOrDefault();
        RefreshKeyframeList();
    }

    /// <summary>Reloads the keyframe list for the picked object.</summary>
    private void RefreshKeyframeList()
    {
        int? target = (objectBox.SelectedItem as ObjectChoice)?.Id;
        MotionTrack? track = null;
        if (target.HasValue)
        {
            StudioState state = session.GetStudioState();
            track = state.Tracks.FirstOrDefault(candidate => candidate.ObjectId == target.Value);
        }
        int keep = (keyList.SelectedItem as KeyframeChoice)?.Key.Frame ?? -1;
        KeyframeChoice[] choices = track?.Keys.Select(static key => new KeyframeChoice(key)).ToArray() ?? Array.Empty<KeyframeChoice>();
        keyList.ItemsSource = choices;
        keyList.SelectedItem = choices.FirstOrDefault(choice => choice.Key.Frame == keep);
        if (track != null && frameBox != null && string.IsNullOrWhiteSpace(frameBox.Text))
            frameBox.Text = "0";
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
        session.AddMotionKeyframe(target, new MotionKeyframe(
            frame, objectState.Position, objectState.Rotation, objectState.Scale));
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
