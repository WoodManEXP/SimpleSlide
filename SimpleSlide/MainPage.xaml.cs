using System;
using System.Diagnostics;
using Windows.ApplicationModel;
using Windows.Gaming.Input;
using Windows.Storage;
using Windows.System;
using Windows.System.Threading;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace SimpleSlide
{
    public static class AppDataKeys
    {
        public const string CurrSpeedIndex = "001";
    }

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a <see cref="Frame">.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private String PickedFolderTokenName { get; } = "PickedFolderToken";
        public Progress<String> FNameProgress { get; set; }
        private Player Player { get; set; }

        // XBox controller
        private Gamepad? Controller { get; set; }
        private DispatcherTimer? ControllerTimer { get; set; }
        private Boolean SelectingFolder { get; set; } = false;

        #region PlaySpeedControl
        private enum ChangeSpeed
        { Faster, Slower }
        private static readonly int[] PlaySpeeds = [2 * 1000, 5 * 1000, 10 * 1000, 20 * 1000, 30 * 1000, 60 * 1000];
        private static readonly int MaxSpeed = PlaySpeeds.Length - 1;
        private static int CurrSpeedIndex = 2;
        private static int PSInterval { get; } = 250;
        private static int PCInterval { get; } = 4 * 250; // Pause/Continue
        private ThreadPoolTimer? PlaySpeedBarTimer { get; set; } = null;
        private readonly TimeSpan PlaySpeedBarDelay = TimeSpan.FromMilliseconds(2 * 1000);
        private DateTime LastSpeedChangeDT = DateTime.Now;
        #endregion
        private enum NextOrPrevious
        { Next, Previous }
        private DateTime LastNextOrPreviousDT = DateTime.Now;
        private enum PauseOrContinue
        { Pause, Continue }
        private DateTime LastPauseOrContinueDT = DateTime.Now;
        private Boolean OnXBox { get; set; }

        // For saving app settings
        private ApplicationDataContainer ApplicationDataContainer = Windows.Storage.ApplicationData.Current.LocalSettings;

        public MainPage()
        {
            InitializeComponent();

            OnXBox = Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox";

            // Get app revision info into window/page title
            Package package = Package.Current;
            PackageId packageId = package.Id;
            PackageVersion version = packageId.Version;
            String vStr = version.Major.ToString() + "." + version.Minor.ToString() + "."
                          + version.Revision.ToString() + "." + version.Build.ToString();
            ApplicationView.GetForCurrentView().Title = vStr;   // In tite bar for non-XBox
            VersionXBox.Text = vStr;                            // Shows up this way on XBox

            // Subscribe to the UnhandledException event
            //this.DispatcherUnhandledException += App_UnhandledException;

            FNameProgress = new Progress<String>();

            // If a DelayBetweenImges setting was saved, go with that.
            int oInt;
            if (ApplicationDataContainer.Values.TryGetValue(AppDataKeys.CurrSpeedIndex, out Object? oValue))
                if (int.TryParse(oValue as String, out oInt))
                    CurrSpeedIndex = oInt;

            Player = new(PickedFolderTokenName, FNameProgress)
            {
                ImagePane = [Image0, Image1], // The XAML image elements
                ImageFadeStoryBoard = [Image0FadeStoryboard, Image1FadeStoryboard],
                ImageFadeAnimation = [Image0Animation, Image1Animation],
                DelayBetweenImges = PlaySpeeds[CurrSpeedIndex],
                WorkingThing = WorkingThing // ProgressRing
            };

            FNameProgress.ProgressChanged += FNameChanged;
            FNameChanged(null, SimpleSlide.Strings.SelectFolder); // Set initial value

            ControllerInit(); // Set up for XBox controller

            // Speed control setup
            PlaySpeedBar.Maximum = MaxSpeed;
            PlaySpeedBar.Value = 0D;

            // Start the player
            _ = Player.Play(); // Player runs on another thread.
        }

        #region XBoxController
        private void ControllerInit()
        {
            ControllerTimer = new DispatcherTimer();
            // DispatcherTimer.Inerval defaults to {00:00:00}. Perhaps it should be set to something else ??
            ControllerTimer.Tick += ControllerTick;

            Gamepad.GamepadAdded += ControllerAdded;
            Gamepad.GamepadRemoved += ControllerRemoved;
        }
        /// <summary>
        /// Controller added - hide screen controlsv
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void ControllerAdded(object? sender, Gamepad e)
        {
            // The following activities must take place on the UI thread, so use the Dispatcher to toss them over,
            // via a lambda expression, to that thread.
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal,
                    async () =>
                    {
                        OnScreenControls.Visibility = Visibility.Collapsed; // Hide on-screen controls
                        ControllerControls.Visibility = Visibility.Visible; // Show controller cotrols
                        ControllerTimer?.Start(); // XBox controller attached, start ControllerTimer
                    }
                );
        }
        /// <summary>
        /// Controller removed, show screen controls
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void ControllerRemoved(object? sender, Gamepad e)
        {
            // The following activities must take place on the UI thread, so use the Dispatcher to toss them over,
            // via a lambda expression, to that thread.
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal,
                    async () =>
                    {
                        OnScreenControls.Visibility = Visibility.Visible; // Bring the XAML controls back
                        ControllerControls.Visibility = Visibility.Collapsed; // Hide controller cotrols
                        ControllerTimer?.Stop(); // No contoller, no need
                    }
                );
        }
        /// <summary>
        /// Called periodically to monitor Xox controller
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <remarks>
        /// XBox controller state comes in fast, from another thread (eg. a button being held down)
        /// </remarks>
        private void ControllerTick(object? sender, object e)
        {
            if (Gamepad.Gamepads.Count > 0)
            {
                Controller = Gamepad.Gamepads[0];

                var reading = Controller.GetCurrentReading();

                double rtValue = reading.RightTrigger;

                if (reading.Buttons.HasFlag(GamepadButtons.Menu))
                {
                    if (!SelectingFolder)
                        SelectFolder();
                }
                else if (reading.Buttons.HasFlag(GamepadButtons.A)) // Continue/Pause
                {
                    if (Player.PlayerState.Playing == Player.CurrentPlayerState)
                        PauseOrContiue(PauseOrContinue.Pause);
                    else if (Player.PlayerState.Paused == Player.CurrentPlayerState)
                        PauseOrContiue(PauseOrContinue.Continue);
                }
                //                else if (reading.Buttons.HasFlag(GamepadButtons.X)) // Pause
                //                    PauseOrContiue(PauseOrContinue.Pause);
                else if (reading.Buttons.HasFlag(GamepadButtons.DPadUp)) // Speed up
                    ChangePlaySpeed(ChangeSpeed.Faster);
                else if (reading.Buttons.HasFlag(GamepadButtons.DPadDown)) // Slow down
                    ChangePlaySpeed(ChangeSpeed.Slower);
                else if (reading.Buttons.HasFlag(GamepadButtons.DPadRight)) // Next image
                    ControllerNextOrPrevious(NextOrPrevious.Next);
                else if (reading.Buttons.HasFlag(GamepadButtons.DPadLeft)) // Previous image
                    ControllerNextOrPrevious(NextOrPrevious.Previous);
                else if (reading.Buttons.HasFlag(GamepadButtons.Y)) // Continue/Pause
                    HelpXBox();

                //pbLeftThumbstickX.Value = reading.LeftThumbstickX;
                //pbLeftThumbstickY.Value = reading.LeftThumbstickY;

                //pbRightThumbstickX.Value = reading.RightThumbstickX;
                //pbRightThumbstickY.Value = reading.RightThumbstickY;

                //pbRightThumbstickY.Value = reading.RightThumbstickY;

                //pbLeftTrigger.Value = reading.LeftTrigger;
                //pbRightTrigger.Value = reading.RightTrigger;

                //https://msdn.microsoft.com/en-us/library/windows/apps/windows.gaming.input.gamepadbuttons.aspx
                //ChangeVisibility(reading.Buttons.HasFlag(GamepadButtons.A), lblA);
                //ChangeVisibility(reading.Buttons.HasFlag(GamepadButtons.B), lblB);

                //ChangeVisibility(reading.Buttons.HasFlag(GamepadButtons.Menu), lblMenu);
                //ChangeVisibility(reading.Buttons.HasFlag(GamepadButtons.DPadLeft), lblDPadLeft);
                //ChangeVisibility(reading.Buttons.HasFlag(GamepadButtons.DPadRight), lblDPadRight);
                //ChangeVisibility(reading.Buttons.HasFlag(GamepadButtons.DPadUp), lblDPadUp);
                //ChangeVisibility(reading.Buttons.HasFlag(GamepadButtons.DPadDown), lblDPadDown);
            }
        }

        /// <summary>
        /// Display help whe o XBox
        /// </summary>
        private Boolean HelpOpen = false;
        private async void HelpXBox()
        {
            if (HelpOpen)
                return;
            HelpOpen = true;
            var xBoxHelp = new XBoxHelp();
            PauseOrContiue(PauseOrContinue.Pause); // Pause slides
            await xBoxHelp.ShowAsync();
            HelpOpen = false;
            PauseOrContiue(PauseOrContinue.Continue); // Contiue slides
        }
        #endregion XBoxController

        private async void Help()
        {
            if (HelpOpen)
                return;
            HelpOpen = true;
            var help = new Help();
            PauseOrContiue(PauseOrContinue.Pause); // Pause slides
            await help.ShowAsync();
            HelpOpen = false;
            PauseOrContiue(PauseOrContinue.Continue); // Contiue slides
        }

        /// <summary>
        /// Receives message to set text string in the FNameTextBlock
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="aStr"></param>
        private void FNameChanged(object? sender, string aStr)
        {
            StatusTextBlock.Text = aStr;

            // Kludgey yes: When Player starts it will sometimes go diretly into Playing state.
            // If the Pause/Contiue button is not set correctly for the current PLayer state, correct it
            // here.
            // This is called frequetly by Player, so as good a place as any to make the check...
            if (Player.PlayerState.Playing == Player.CurrentPlayerState)
                if ((String)ContinuePauseBtn.Content != SimpleSlide.Strings.PauseStr)
                    ContinuePauseBtn.Content = SimpleSlide.Strings.PauseStr;
        }
        private async void SelectFolderBtn_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            SelectFolder();
        }
        private async void SelectFolder()
        {
            SelectingFolder = true;
            // Send Stop command to player
            Player.CommandQueue.Enqueue(new PlayerCommand()
            {
                Command = PlayerCommand.PlayerCommands.Stop
            });

            // Let user select folder
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");

            Windows.Storage.StorageFolder folder = await folderPicker.PickSingleFolderAsync();
            if (null != folder)
            {
                // Application now has read/write access to all contents in the picked folder
                // (including other sub-folder contents)
                Windows.Storage.AccessCache.StorageApplicationPermissions.
                FutureAccessList.AddOrReplace(PickedFolderTokenName, folder);
                StatusTextBlock.Text = folder.Path;

                // Change to Pause
                ContinuePauseBtn.Content = SimpleSlide.Strings.PauseStr;
                ContinuePauseBtn.IsEnabled = true;
                SetToolTip(ContinuePauseBtn, SimpleSlide.Strings.PauseTT);

                if (OnXBox)
                    FNameChanged(null, SimpleSlide.Strings.StartingXBox);
                else
                    FNameChanged(null, SimpleSlide.Strings.Starting);

                // Send Play command to player
                Player.CommandQueue.Enqueue(new PlayerCommand()
                {
                    Command = PlayerCommand.PlayerCommands.NewFolderStart,
                });
            }
            else
            {
                // Send Continue command to player
                Player.CommandQueue.Enqueue(new PlayerCommand()
                {
                    Command = PlayerCommand.PlayerCommands.Continue
                });
            }
            SelectingFolder = false;
        }
        private void ContinuePauseBtn_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if (Player.MediaListLoaded)
            {
                if (0 == String.Compare(SimpleSlide.Strings.PauseStr, (String)ContinuePauseBtn.Content))
                    PauseOrContiue(PauseOrContinue.Pause);
                else
                    PauseOrContiue(PauseOrContinue.Continue);
            }
        }

        /// <summary>
        /// Send Pause or Continue command to Player
        /// </summary>
        /// <param name="pause"> True:pause, False:continue</param>
        private void PauseOrContiue(PauseOrContinue what)
        {
            // Limit how often these are processed
            DateTime now = DateTime.Now;
            TimeSpan interval = now - LastPauseOrContinueDT;
            if (interval.TotalMilliseconds < PCInterval)
                return;

            LastPauseOrContinueDT = now;

            //   Debug.WriteLine("PauseOrContiue " + what.ToString());

            if (PauseOrContinue.Pause == what)
            {
                ContinuePauseBtn.Content = SimpleSlide.Strings.ContiueStr; // ContiueStr;
                SetToolTip(ContinuePauseBtn, SimpleSlide.Strings.ContinueTT);

                // Send Pause command to player
                Player.CommandQueue.Enqueue(new PlayerCommand()
                {
                    Command = PlayerCommand.PlayerCommands.Pause
                });
                StatusTextBlock.Text = SimpleSlide.Strings.Paused;
            }
            else // Continue
            {
                ContinuePauseBtn.Content = SimpleSlide.Strings.Paused;
                SetToolTip(ContinuePauseBtn, SimpleSlide.Strings.PauseTT);

                // Send Cotinue command to player
                Player.CommandQueue.Enqueue(new PlayerCommand()
                {
                    Command = PlayerCommand.PlayerCommands.Continue
                });
                StatusTextBlock.Text = SimpleSlide.Strings.Continuing;
            }
        }
        private void SetToolTip(DependencyObject element, String value)
        {
            var tt = new ToolTip()
            {
                Content = value
            };
            ToolTipService.SetToolTip(element, tt);
        }
        private void ControllerNextOrPrevious(NextOrPrevious nextOrPrevious)
        {
            // Limit how often these are processed
            DateTime now = DateTime.Now;
            TimeSpan interval = now - LastNextOrPreviousDT;
            if (interval.TotalMilliseconds < PSInterval)
                return;

            LastNextOrPreviousDT = now;

            switch (nextOrPrevious)
            {
                case NextOrPrevious.Previous:
                    // Send Previous image command to Player
                    Player.CommandQueue.Enqueue(new PlayerCommand()
                    {
                        Command = PlayerCommand.PlayerCommands.Previous
                    });
                    break;
                default:
                    // Send Next image command to Player
                    Player.CommandQueue.Enqueue(new PlayerCommand()
                    {
                        Command = PlayerCommand.PlayerCommands.Next
                    });
                    break;
            }
        }
        #region SpeedControl
        private void ChangePlaySpeed(ChangeSpeed change)
        {
            // Limit how often these are processed
            DateTime now = DateTime.Now;
            TimeSpan interval = now - LastSpeedChangeDT;
            if (interval.TotalMilliseconds < PSInterval)
                return;

            LastSpeedChangeDT = now;

            // Show the PlaySpeedBar controls
            PlaySpeedStackPanel.Visibility = Visibility.Visible;

            // Stop timer, if there is one
            if (null != PlaySpeedBarTimer)
                PlaySpeedBarTimer.Cancel();

            // (Re)Start ThreadPoolTimer
            PlaySpeedBarTimer = ThreadPoolTimer.CreateTimer(PlaySpeedBarTimerHandler,
                                        TimeSpan.FromMilliseconds(1000D));

            if (ChangeSpeed.Faster == change)
                CurrSpeedIndex = Math.Max(0, CurrSpeedIndex - 1);
            else
                CurrSpeedIndex = Math.Min(MaxSpeed, CurrSpeedIndex + 1);

            PlaySpeedBar.Value = (Double)CurrSpeedIndex; // Adjust the PlaySpeedBar value

            int currSpeedMS = PlaySpeeds[CurrSpeedIndex];
            PlaySpeedTextBlock.Text = SimpleSlide.Strings.Delay + " " + (currSpeedMS / 1000).ToString() + " seconds";

            // Send ChangeSpeed command to player
            Player.CommandQueue.Enqueue(new PlayerCommand()
            {
                Command = PlayerCommand.PlayerCommands.ChangeSpeed,
                Value = currSpeedMS
            });

            // Save selected speed to ApplicationDataContainer
            ApplicationDataContainer.Values[AppDataKeys.CurrSpeedIndex] = CurrSpeedIndex.ToString();
        }

        /// <summary>
        /// Called when Play Speed timer fires. Hides the control.
        /// </summary>
        /// <param name="timer"></param>
        public async void PlaySpeedBarTimerHandler(ThreadPoolTimer timer)
        {
            // The following activities must take place on the UI thread, so use the Dispatcher to toss them over,
            // via a lambda expression, to that thread.
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal,
                    async () =>
                    {
                        // Hide the PlaySpeedBar controls
                        PlaySpeedStackPanel.Visibility = Visibility.Collapsed;
                    }
                );
        }
        #endregion

        /// <summary>
        /// Keyboard events
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// https://learn.microsoft.com/en-us/uwp/api/windows.system.virtualkey?view=winrt-26100
        private void KeyDownEvent(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.Up:
                    ChangePlaySpeed(ChangeSpeed.Faster);
                    break;
                case VirtualKey.Down:
                    ChangePlaySpeed(ChangeSpeed.Slower);
                    break;
                case VirtualKey.Right:
                    // Send Next image command to Player
                    ControllerNextOrPrevious(NextOrPrevious.Next);
                    break;
                case VirtualKey.Left:
                    // Send Previous image command to Player
                    ControllerNextOrPrevious(NextOrPrevious.Previous);
                    break;
                default: // ???
                    base.OnKeyDown(e);
                    break;
            }
        }

        /// <summary>
        /// Not used, at this time...
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Image1Opened(object sender, RoutedEventArgs e)
        {

        }

        private void DoubleAnimation_Completed(object sender, object e)
        {

        }

        private void HelpBtn_Click(object sender, RoutedEventArgs e)
        {
            Help();
        }
    }
}