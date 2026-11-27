// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/AnimationStudioWindow.cs
// Purpose: Modeless animation studio: cameras, shots, motion tracks, movie rendering.
//
// Edits scene studio state through the session (no undo steps; changes mark the
// document modified). Scrubbing and playback pose the live scene transiently and
// restore it on close; movie renders pose per frame and always restore after.
// -----------------------------------------------------------------------------
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LightingShowcase.CameraSystem;
using LightingShowcase.CommandLine;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed class AnimationStudioWindow : Window
{
    private sealed record BackendChoice(RenderBackend Backend)
    {
        /// <summary>Returns the command-line backend name for display.</summary>
        public override string ToString() => RenderRequest.BackendName(Backend);
    }

    private sealed record KeyframeChoice(MotionKeyframe Key)
    {
        /// <summary>Returns the keyframe label for list display.</summary>
        public override string ToString() => $"Frame {Key.Frame}";
    }

    private readonly ComposerSceneSession session;
    private readonly Action markModified;
    private readonly Action requestPreview;
    private readonly Action<bool> setBusy;
    private readonly Func<ComposerRenderOptions> currentOptions;
    private readonly Action openCameras;
    private readonly Action openMotion;
    private readonly Action onClosed;

    private readonly ListBox shotList;
    private readonly TextBox shotNameBox;
    private readonly ComboBox shotCameraBox;
    private readonly TextBox shotFramesBox;
    private readonly ComboBox shotMoveBox;
    private readonly TextBox shotAmountBox;
    private readonly Slider scrubSlider;
    private readonly TextBlock frameLabel;
    private readonly Button playButton;
    private readonly TextBox fpsBox;
    private readonly TextBox widthBox;
    private readonly TextBox heightBox;
    private readonly ComboBox backendBox;
    private readonly TextBox samplesBox;
    private readonly TextBox bouncesBox;
    private readonly TextBox exposureBox;
    private readonly TextBox outputNameBox;
    private readonly TextBlock outputDirLabel;
    private readonly Button renderButton;
    private readonly Button cancelButton;
    private readonly ProgressBar progressBar;
    private readonly TextBlock statusText;
    private readonly StackPanel mainPanel;

    private StudioState state;
    private int scrubFrame;
    private bool syncingScrub;
    private bool settingsLoaded;
    private readonly DispatcherTimer playTimer;
    private CancellationTokenSource? renderCts;
    private bool rendering;
    private bool closed;
    private string? outputDirectory;
    private Dictionary<int, MotionPose>? poseSnapshot;

    /// <summary>Creates a modeless animation studio window bound to the session.</summary>
    public AnimationStudioWindow(
        ComposerSceneSession session,
        Action markModified,
        Action requestPreview,
        Action<bool> setBusy,
        Func<ComposerRenderOptions> currentOptions,
        Action openCameras,
        Action openMotion,
        Action onClosed)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.markModified = markModified ?? (() => { });
        this.requestPreview = requestPreview ?? (() => { });
        this.setBusy = setBusy ?? (_ => { });
        this.currentOptions = currentOptions ?? throw new ArgumentNullException(nameof(currentOptions));
        this.openCameras = openCameras ?? (() => { });
        this.openMotion = openMotion ?? (() => { });
        this.onClosed = onClosed ?? (() => { });

        Title = "Animation Studio";
        Width = 540;
        Height = 780;
        MinWidth = 440;
        MinHeight = 560;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        shotList = new ListBox { MinHeight = 90, MaxHeight = 140 };
        shotNameBox = new TextBox { MinWidth = 140 };
        shotCameraBox = new ComboBox { MinWidth = 160 };
        shotFramesBox = new TextBox { MinWidth = 64, TextAlignment = TextAlignment.Right, Text = "12" };
        shotMoveBox = new ComboBox { ItemsSource = Enum.GetValues<StudioShotMove>(), SelectedIndex = 0, MinWidth = 110 };
        shotAmountBox = new TextBox { MinWidth = 80, TextAlignment = TextAlignment.Right, Text = "0" };
        scrubSlider = new Slider { Minimum = 0, Maximum = 0, MinWidth = 200 };
        frameLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 };
        playButton = NewButton("Play");
        fpsBox = SmallNumberBox();
        widthBox = SmallNumberBox();
        heightBox = SmallNumberBox();
        backendBox = new ComboBox
        {
            ItemsSource = new[]
            {
                new BackendChoice(RenderBackend.ShadowRasterPreview),
                new BackendChoice(RenderBackend.VulkanRasterPreview),
                new BackendChoice(RenderBackend.VulkanGpu),
                new BackendChoice(RenderBackend.Cpu),
            },
            SelectedIndex = 0,
            MinWidth = 150
        };
        samplesBox = SmallNumberBox();
        bouncesBox = SmallNumberBox();
        exposureBox = SmallNumberBox();
        outputNameBox = new TextBox { MinWidth = 140 };
        outputDirLabel = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.76, FontSize = 12 };
        renderButton = NewButton("Render movie");
        cancelButton = NewButton("Cancel");
        cancelButton.IsEnabled = false;
        progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 14, HorizontalAlignment = HorizontalAlignment.Stretch };
        statusText = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.76, FontSize = 12 };
        mainPanel = new StackPanel { Spacing = 12 };

        playTimer = new DispatcherTimer();
        playTimer.Tick += (_, _) => AdvancePlayback();

        Content = BuildContent();
        state = session.GetStudioState();
        RefreshAll();
        Activated += (_, _) => { if (!rendering) RefreshAll(); };
        Closing += (_, args) =>
        {
            if (rendering)
            {
                args.Cancel = true;
                statusText.Text = "Rendering… press Cancel first.";
                return;
            }
            closed = true;
            playTimer.Stop();
            RestorePoseSnapshot();
            setBusy(false);
            onClosed();
        };
    }

    /// <summary>Builds the studio window content with its camera, shot, track, and render sections.</summary>
    private Control BuildContent()
    {
        mainPanel.Children.Add(new TextBlock
        {
            Text = "Animation Studio",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold
        });
        mainPanel.Children.Add(new TextBlock
        {
            Text = "Set up named cameras, arrange them into shots, keyframe object motion on the global timeline, and render the movie.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        });

        mainPanel.Children.Add(Heading("Shots"));
        Grid cameraRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        cameraRow.Children.Add(new TextBlock
        {
            Text = "Shots use the scene cameras.",
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.72,
            FontSize = 12
        });
        Button manageCameras = Tip(NewButton("Manage cameras…"), "Opens the camera manager.");
        manageCameras.Click += (_, _) => openCameras();
        cameraRow.Children.Add(manageCameras);
        Grid.SetColumn(manageCameras, 1);
        mainPanel.Children.Add(cameraRow);
        mainPanel.Children.Add(shotList);
        shotList.SelectionChanged += (_, _) => LoadSelectedShot();
        Grid shotFields = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"), ColumnSpacing = 8, RowSpacing = 6 };
        AddField(shotFields, "Name", shotNameBox, 0, 0);
        AddField(shotFields, "Camera", shotCameraBox, 0, 2);
        AddField(shotFields, "Frames", shotFramesBox, 1, 0);
        AddField(shotFields, "Move", shotMoveBox, 1, 2);
        AddField(shotFields, "Amount", shotAmountBox, 2, 0);
        mainPanel.Children.Add(shotFields);
        mainPanel.Children.Add(new TextBlock
        {
            Text = "Amount is total orbit degrees, or the end-radius fraction for a push (0.6 pushes to 60%).",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68,
            FontSize = 12
        });
        Grid shotRow = ButtonRow();
        Button addShot = Tip(NewButton("Add"), "Appends the shot described above.");
        addShot.Click += (_, _) => AddShot();
        Button updateShot = Tip(NewButton("Update"), "Writes the fields into the selected shot.");
        updateShot.Click += (_, _) => UpdateShot();
        Button deleteShot = Tip(NewButton("Delete"), "Deletes the selected shot.");
        deleteShot.Click += (_, _) => DeleteShot();
        Button shotUp = Tip(NewButton("Up"), "Moves the selected shot earlier.");
        shotUp.Click += (_, _) => MoveShot(-1);
        Button shotDown = Tip(NewButton("Down"), "Moves the selected shot later.");
        shotDown.Click += (_, _) => MoveShot(1);
        AddRowButtons(shotRow, addShot, updateShot, deleteShot, shotUp, shotDown);
        mainPanel.Children.Add(shotRow);

        mainPanel.Children.Add(Heading("Timeline"));
        Grid scrubRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        scrubSlider.ValueChanged += (_, _) => OnScrubChanged();
        scrubRow.Children.Add(scrubSlider);
        playButton.Click += (_, _) => TogglePlayback();
        scrubRow.Children.Add(playButton);
        Grid.SetColumn(playButton, 1);
        Button resetPose = Tip(NewButton("Reset pose"), "Restores the pre-scrub object transforms.");
        resetPose.Click += (_, _) => { RestorePoseSnapshot(); requestPreview(); };
        scrubRow.Children.Add(resetPose);
        Grid.SetColumn(resetPose, 2);
        mainPanel.Children.Add(frameLabel);
        mainPanel.Children.Add(scrubRow);
        mainPanel.Children.Add(new TextBlock
        {
            Text = "Scrubbing jumps the viewport to each frame's shot camera and poses the live scene; closing restores. Playing previews the movie in the viewport.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68,
            FontSize = 12
        });

        mainPanel.Children.Add(Heading("Object motion"));
        Grid motionRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        motionRow.Children.Add(new TextBlock
        {
            Text = "Motion is keyframed per object across the whole timeline.",
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.72,
            FontSize = 12
        });
        Button manageMotion = Tip(NewButton("Manage motion…"), "Opens the motion manager.");
        manageMotion.Click += (_, _) => openMotion();
        motionRow.Children.Add(manageMotion);
        Grid.SetColumn(manageMotion, 1);
        mainPanel.Children.Add(motionRow);

        mainPanel.Children.Add(Heading("Movie settings"));
        Grid settings = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"), ColumnSpacing = 8, RowSpacing = 6 };
        AddField(settings, "FPS", fpsBox, 0, 0);
        AddField(settings, "Output", outputNameBox, 0, 2);
        AddField(settings, "Width", widthBox, 1, 0);
        AddField(settings, "Height", heightBox, 1, 2);
        AddField(settings, "Backend", backendBox, 2, 0);
        AddField(settings, "Samples", samplesBox, 2, 2);
        AddField(settings, "Bounces", bouncesBox, 3, 0);
        AddField(settings, "Exposure", exposureBox, 3, 2);
        mainPanel.Children.Add(settings);
        Grid dirRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        dirRow.Children.Add(outputDirLabel);
        Button changeDir = Tip(NewButton("Change…"), "Chooses the output folder for PNGs and the GIF.");
        changeDir.Click += async (_, _) => await ChangeOutputDirectoryAsync();
        dirRow.Children.Add(changeDir);
        Grid.SetColumn(changeDir, 1);
        mainPanel.Children.Add(dirRow);

        StackPanel footer = new() { Spacing = 6 };
        Grid renderRow = new() { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 8 };
        renderButton.Click += async (_, _) => await RenderMovieAsync();
        renderRow.Children.Add(renderButton);
        cancelButton.Click += (_, _) => renderCts?.Cancel();
        renderRow.Children.Add(cancelButton);
        Grid.SetColumn(cancelButton, 1);
        renderRow.Children.Add(progressBar);
        Grid.SetColumn(progressBar, 2);
        footer.Children.Add(renderRow);
        footer.Children.Add(statusText);

        return new Border
        {
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new ScrollViewer
                    {
                        Content = mainPanel,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                    },
                    footer
                }
            }
        };
    }

    /// <summary>Creates a right-aligned numeric text box.</summary>
    private static TextBox SmallNumberBox() => new() { MinWidth = 70, TextAlignment = TextAlignment.Right };

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

    /// <summary>Creates a section heading with the given text.</summary>
    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 6, 0, 0)
    };

    /// <summary>Creates a wrapping row panel for action buttons.</summary>
    private static Grid ButtonRow() => new() { ColumnSpacing = 8, RowSpacing = 6 };

    /// <summary>Adds buttons as auto-sized columns of the given row grid.</summary>
    private static void AddRowButtons(Grid row, params Button[] buttons)
    {
        row.ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("Auto", buttons.Length)));
        for (int i = 0; i < buttons.Length; i++)
        {
            row.Children.Add(buttons[i]);
            Grid.SetColumn(buttons[i], i);
        }
    }

    /// <summary>Adds a label and editor pair to the given grid at the given cell.</summary>
    private static void AddField(Grid grid, string label, Control editor, int row, int column)
    {
        int rows = row + 1;
        while (grid.RowDefinitions.Count < rows)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        TextBlock text = new() { Text = label, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(text);
        Grid.SetRow(text, row);
        Grid.SetColumn(text, column);
        grid.Children.Add(editor);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, column + 1);
    }

    /// <summary>Reloads every list from the session, preserving selections where possible.</summary>
    private void RefreshAll()
    {
        state = session.GetStudioState();
        RefreshShotCameraPicker();
        RefreshShotList();
        RefreshScrubRange();
        RefreshOutputLabel();
        if (!settingsLoaded)
        {
            LoadSettingsEditors();
            settingsLoaded = true;
        }
    }

    /// <summary>Reloads the shot camera picker.</summary>
    private void RefreshShotCameraPicker()
    {
        int? keep = (shotCameraBox.SelectedItem as SceneCamera)?.Id;
        shotCameraBox.ItemsSource = state.Cameras.ToArray();
        shotCameraBox.SelectedItem = state.Cameras.FirstOrDefault(camera => camera.Id == keep)
            ?? state.Cameras.FirstOrDefault();
    }

    /// <summary>Reloads the shot list and the duration label.</summary>
    private void RefreshShotList()
    {
        int keep = shotList.SelectedIndex;
        shotList.ItemsSource = state.Shots.ToArray();
        shotList.SelectedIndex = state.Shots.Count == 0 ? -1 : Math.Clamp(keep, -1, state.Shots.Count - 1);
        if (shotList.SelectedIndex < 0 && state.Shots.Count > 0)
            shotList.SelectedIndex = 0;
    }

    /// <summary>Updates the scrub range and frame label.</summary>
    private void RefreshScrubRange()
    {
        int total = state.Shots.Sum(static shot => Math.Max(0, shot.FrameCount));
        scrubFrame = Math.Clamp(scrubFrame, 0, Math.Max(0, total - 1));
        syncingScrub = true;
        scrubSlider.Maximum = Math.Max(0, total - 1);
        scrubSlider.Value = scrubFrame;
        syncingScrub = false;
        double seconds = state.Settings.Fps > 0 ? (double)total / state.Settings.Fps : 0.0;
        frameLabel.Text = total == 0
            ? "No frames — add a shot."
            : $"Frame {scrubFrame + 1}/{total} ({seconds:0.0}s at {state.Settings.Fps} fps)";
    }

    /// <summary>Updates the output folder label.</summary>
    private void RefreshOutputLabel()
    {
        outputDirLabel.Text = "Folder: " + (outputDirectory ?? DefaultOutputDirectory());
    }

    /// <summary>Loads the settings editors from the scene settings.</summary>
    private void LoadSettingsEditors()
    {
        fpsBox.Text = state.Settings.Fps.ToString(CultureInfo.InvariantCulture);
        widthBox.Text = state.Settings.Width.ToString(CultureInfo.InvariantCulture);
        heightBox.Text = state.Settings.Height.ToString(CultureInfo.InvariantCulture);
        samplesBox.Text = state.Settings.Samples.ToString(CultureInfo.InvariantCulture);
        bouncesBox.Text = state.Settings.Bounces.ToString(CultureInfo.InvariantCulture);
        exposureBox.Text = state.Settings.Exposure.ToString("0.######", CultureInfo.InvariantCulture);
        outputNameBox.Text = state.Settings.OutputName;
        if (backendBox.ItemsSource is System.Collections.IEnumerable items)
        {
            foreach (object? item in items)
            {
                if (item is BackendChoice choice && choice.Backend == state.Settings.Backend)
                {
                    backendBox.SelectedItem = item;
                    break;
                }
            }
        }
    }

    /// <summary>Loads the selected shot into the editor fields.</summary>
    private void LoadSelectedShot()
    {
        if (shotList.SelectedItem is not StudioShot shot)
            return;
        shotNameBox.Text = shot.Name;
        if (shotCameraBox.ItemsSource is System.Collections.IEnumerable items)
        {
            foreach (object? item in items)
            {
                if (item is SceneCamera camera && camera.Id == shot.CameraId)
                {
                    shotCameraBox.SelectedItem = item;
                    break;
                }
            }
        }
        shotFramesBox.Text = shot.FrameCount.ToString(CultureInfo.InvariantCulture);
        shotMoveBox.SelectedItem = shot.Move;
        shotAmountBox.Text = shot.MoveAmount.ToString("0.######", CultureInfo.InvariantCulture);
    }

    /// <summary>Reads and validates the shot editor fields.</summary>
    private bool TryReadShot(out string name, out int cameraId, out int frames, out StudioShotMove move, out double amount)
    {
        name = (shotNameBox.Text ?? string.Empty).Trim();
        cameraId = (shotCameraBox.SelectedItem as SceneCamera)?.Id ?? -1;
        frames = 0;
        move = shotMoveBox.SelectedItem as StudioShotMove? ?? StudioShotMove.Static;
        amount = 0.0;
        if (string.IsNullOrWhiteSpace(name))
        {
            statusText.Text = "Give the shot a name.";
            return false;
        }
        if (cameraId < 0)
        {
            statusText.Text = "Add a camera first.";
            return false;
        }
        if (!TryInt(shotFramesBox.Text, 1, StudioTimeline.MaxTotalFrames, out frames))
        {
            statusText.Text = $"Shot frames must be between 1 and {StudioTimeline.MaxTotalFrames}.";
            return false;
        }
        if (!TryDouble(shotAmountBox.Text, out amount) || (move == StudioShotMove.Push && amount <= 0.0))
        {
            statusText.Text = move == StudioShotMove.Push
                ? "Push amount must be a number greater than zero."
                : "Amount must be a number.";
            return false;
        }
        return true;
    }

    /// <summary>Appends the described shot.</summary>
    private void AddShot()
    {
        if (!TryReadShot(out string name, out int cameraId, out int frames, out StudioShotMove move, out double amount))
            return;
        int index = session.AddStudioShot(name, cameraId, frames, move, amount);
        markModified();
        RefreshAll();
        shotList.SelectedIndex = index;
    }

    /// <summary>Writes the fields into the selected shot.</summary>
    private void UpdateShot()
    {
        if (shotList.SelectedIndex < 0)
        {
            statusText.Text = "Select a shot first.";
            return;
        }
        if (!TryReadShot(out string name, out int cameraId, out int frames, out StudioShotMove move, out double amount))
            return;
        session.UpdateStudioShot(shotList.SelectedIndex, name, cameraId, frames, move, amount);
        markModified();
        RefreshAll();
    }

    /// <summary>Deletes the selected shot.</summary>
    private void DeleteShot()
    {
        if (shotList.SelectedIndex < 0)
        {
            statusText.Text = "Select a shot first.";
            return;
        }
        session.RemoveStudioShot(shotList.SelectedIndex);
        markModified();
        RefreshAll();
    }

    /// <summary>Moves the selected shot by the signed offset.</summary>
    private void MoveShot(int delta)
    {
        if (shotList.SelectedIndex < 0)
        {
            statusText.Text = "Select a shot first.";
            return;
        }
        int index = shotList.SelectedIndex;
        session.MoveStudioShot(index, delta);
        markModified();
        RefreshAll();
        shotList.SelectedIndex = Math.Clamp(index + delta, 0, state.Shots.Count - 1);
    }

    /// <summary>Handles scrub slider movement.</summary>
    private void OnScrubChanged()
    {
        if (syncingScrub || rendering)
            return;
        scrubFrame = (int)Math.Round(scrubSlider.Value);
        PoseScrubFrame();
        RefreshScrubRange();
    }

    /// <summary>Applies the scrub frame camera and poses, then requests a preview.</summary>
    private void PoseScrubFrame()
    {
        poseSnapshot ??= session.CaptureStudioPoses();
        state = session.GetStudioState();
        try
        {
            IReadOnlyList<StudioFrame> frames = StudioTimeline.ResolveFrames(state.Cameras, state.Shots);
            StudioFrame? frame = frames.FirstOrDefault(candidate => candidate.GlobalFrame == scrubFrame);
            if (frame != null)
                session.Camera.ApplyView(frame.Camera);
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        Dictionary<int, MotionPose> effective = new(poseSnapshot);
        foreach ((int id, MotionPose pose) in StudioTimeline.SamplePoses(state.Tracks, scrubFrame))
            effective[id] = pose;
        session.ApplyStudioPoses(effective);
        requestPreview();
    }

    /// <summary>Restores pre-scrub transforms when posed.</summary>
    private void RestorePoseSnapshot()
    {
        if (poseSnapshot == null)
            return;
        session.ApplyStudioPoses(poseSnapshot);
        poseSnapshot = null;
    }

    /// <summary>Drops the scrub pose snapshot so a stale pose is never restored.</summary>
    public void DropPoseSnapshot()
    {
        poseSnapshot = null;
    }

    /// <summary>Toggles timeline playback.</summary>
    private void TogglePlayback()
    {
        if (playTimer.IsEnabled)
        {
            StopPlayback();
            return;
        }
        int total = state.Shots.Sum(static shot => Math.Max(0, shot.FrameCount));
        if (total == 0)
        {
            statusText.Text = "Add a shot first.";
            return;
        }
        if (scrubFrame >= total - 1)
            scrubFrame = 0;
        playTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(120, 1000 / Math.Max(1, state.Settings.Fps)));
        playTimer.Start();
        playButton.Content = "Stop";
    }

    /// <summary>Stops timeline playback.</summary>
    private void StopPlayback()
    {
        playTimer.Stop();
        playButton.Content = "Play";
    }

    /// <summary>Advances one playback frame.</summary>
    private void AdvancePlayback()
    {
        int total = state.Shots.Sum(static shot => Math.Max(0, shot.FrameCount));
        if (total == 0 || scrubFrame >= total - 1)
        {
            StopPlayback();
            return;
        }
        scrubFrame++;
        PoseScrubFrame();
        RefreshScrubRange();
    }

    /// <summary>Reads and validates the movie settings editors.</summary>
    private bool TryReadSettings(out MovieSettings settings)
    {
        settings = new MovieSettings();
        if (backendBox.SelectedItem is BackendChoice backend)
            settings.Backend = backend.Backend;
        if (!TryInt(fpsBox.Text, 1, 50, out int fps)
            || !TryInt(widthBox.Text, 1, 32768, out int width)
            || !TryInt(heightBox.Text, 1, 32768, out int height)
            || !TryInt(samplesBox.Text, 1, 4096, out int samples)
            || !TryInt(bouncesBox.Text, 0, 8, out int bounces)
            || !TryDouble(exposureBox.Text, out double exposure))
        {
            statusText.Text = "Settings must be valid numbers within their ranges.";
            return false;
        }
        settings.Fps = fps;
        settings.Width = width;
        settings.Height = height;
        settings.Samples = samples;
        settings.Bounces = bounces;
        settings.Exposure = exposure;
        settings.OutputName = (outputNameBox.Text ?? string.Empty).Trim();
        try
        {
            settings.Validate();
            return true;
        }
        catch (ArgumentException ex)
        {
            statusText.Text = ex.Message;
            return false;
        }
    }

    /// <summary>Returns the output folder, defaulting to the scene folder or videos.</summary>
    private string DefaultOutputDirectory()
    {
        string? scenePath = session.ScenePath;
        if (!string.IsNullOrWhiteSpace(scenePath))
        {
            string? sceneDirectory = Path.GetDirectoryName(Path.GetFullPath(scenePath));
            if (!string.IsNullOrWhiteSpace(sceneDirectory))
                return sceneDirectory;
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    }

    /// <summary>Lets the user pick the output folder.</summary>
    private async Task ChangeOutputDirectoryAsync()
    {
        if (!StorageProvider.CanOpen)
        {
            statusText.Text = "The desktop folder picker is unavailable.";
            return;
        }
        IReadOnlyList<IStorageFolder> folders;
        try
        {
            folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose the movie output folder",
                AllowMultiple = false
            });
        }
        catch (Exception ex)
        {
            statusText.Text = $"Folder picker failed: {ex.Message}";
            return;
        }
        string? path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (string.IsNullOrWhiteSpace(path))
            return;
        outputDirectory = path;
        RefreshOutputLabel();
    }

    /// <summary>Renders the timeline to a PNG sequence plus a GIF.</summary>
    private async Task RenderMovieAsync()
    {
        if (rendering)
            return;
        if (!TryReadSettings(out MovieSettings? settings) || settings == null)
            return;
        session.UpdateMovieSettings(settings);
        markModified();
        RefreshAll();
        IReadOnlyList<StudioFrame> frames;
        try
        {
            frames = StudioTimeline.ResolveFrames(state.Cameras, state.Shots);
        }
        catch (Exception ex)
        {
            statusText.Text = ex.Message;
            return;
        }
        if (frames.Count == 0)
        {
            statusText.Text = "Add a shot to the timeline first.";
            return;
        }

        StopPlayback();
        rendering = true;
        mainPanel.IsEnabled = false;
        renderButton.IsEnabled = false;
        cancelButton.IsEnabled = true;
        setBusy(true);
        renderCts = new CancellationTokenSource();
        progressBar.Minimum = 0;
        progressBar.Maximum = frames.Count;
        progressBar.Value = 0;
        statusText.Text = $"Rendering {frames.Count} frames…";
        string directory = outputDirectory ?? DefaultOutputDirectory();
        ComposerRenderOptions options = currentOptions();
        RenderRequest request = new()
        {
            Width = settings.Width,
            Height = settings.Height,
            Samples = settings.Samples,
            Bounces = settings.Bounces,
            Exposure = settings.Exposure,
            Backend = settings.Backend,
            AmbientStrength = options.AmbientStrength,
            BackgroundTop = ToHexColor(options.BackgroundTop),
            BackgroundBottom = ToHexColor(options.BackgroundBottom),
            UseShadows = options.UseShadows
        };
        try
        {
            CancellationToken token = renderCts.Token;
            RenderSettings renderSettings = request.ToRenderSettings();
            MovieRenderResult result = await Task.Run(() => session.WithScene(scene =>
                MovieRenderer.RenderTimeline(
                    scene,
                    frames,
                    state.Tracks,
                    settings,
                    directory,
                    view => MovieFrameRenderer.RenderStill(
                        scene,
                        view,
                        WithFieldOfView(request, view.FieldOfViewDegrees),
                        renderSettings,
                        token,
                        out _),
                    (done, total, _) =>
                    {
                        if (!closed)
                            Dispatcher.UIThread.Post(() => UpdateRenderProgress(done, total));
                    },
                    token), token), token);
            statusText.Text = $"Movie saved: {result.GifPath} ({result.FrameCount} frames). {result.Details}";
        }
        catch (OperationCanceledException)
        {
            statusText.Text = "Render cancelled.";
        }
        catch (Exception ex)
        {
            statusText.Text = $"Render failed: {ex.Message}";
        }
        finally
        {
            rendering = false;
            mainPanel.IsEnabled = true;
            renderButton.IsEnabled = true;
            cancelButton.IsEnabled = false;
            setBusy(false);
            requestPreview();
        }
    }

    /// <summary>Returns the request with the field of view the CPU backend reads from it.</summary>
    private static RenderRequest WithFieldOfView(RenderRequest request, double fieldOfViewDegrees)
    {
        request.FieldOfViewDegrees = fieldOfViewDegrees;
        return request;
    }

    /// <summary>Updates the render progress bar.</summary>
    private void UpdateRenderProgress(int done, int total)
    {
        progressBar.Maximum = total;
        progressBar.Value = done;
        statusText.Text = $"Rendering frame {done}/{total}…";
    }

    /// <summary>Parses an integer within the given range.</summary>
    private static bool TryInt(string? text, int min, int max, out int value) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    /// <summary>Parses a finite number.</summary>
    private static bool TryDouble(string? text, out double value)
    {
        string trimmed = text?.Trim() ?? string.Empty;
        bool ok = double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                  double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return ok && double.IsFinite(value);
    }

    /// <summary>Formats the given color as a hex string.</summary>
    private static string ToHexColor(Vec3 color)
    {
        static byte ToByte(double channel) => (byte)Math.Clamp((int)Math.Round(channel * 255.0), 0, 255);
        return $"#{ToByte(color.X):X2}{ToByte(color.Y):X2}{ToByte(color.Z):X2}";
    }
}


