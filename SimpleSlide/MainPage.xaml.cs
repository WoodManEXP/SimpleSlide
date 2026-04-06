using System;
using Windows.Gaming.Input;
using Windows.System;
using Windows.System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace SimpleSlide
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a <see cref="Frame">.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private readonly String PauseStr = "Pause";
        private readonly String PauseTT = "Pause slide show (Ctrl-P)";
        private readonly String ContiueStr = "Continue";
        private readonly String ContinueTT = "Continue slide show  (Ctrl-P)";

        private readonly String? PickedFolderToken = "PickedFolderToken";

        public Progress<String> FNameProgress;
        private Player Player;

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
        private static readonly int PSInterval = 250;
        private ThreadPoolTimer? PlaySpeedBarTimer { get; set; } = null;
        private readonly TimeSpan PlaySpeedBarDelay = TimeSpan.FromMilliseconds(2 * 1000);
        private DateTime LastSpeedChangeDT = DateTime.Now;
        #endregion

        public MainPage()
        {
            InitializeComponent();

            FNameProgress = new Progress<String>();
            Player = new(PickedFolderToken, FNameProgress)
            {
                ImagePane = [null, Image1, null], // The XAML image elements
                DelayBetweenImges = PlaySpeeds[CurrSpeedIndex]
            };
            FNameProgress.ProgressChanged += FNameChanged;

            ControllerInit(); // Set up for XBox controller

            // Speed control setup
            PlaySpeedBar.Maximum = MaxSpeed;
            PlaySpeedBar.Value = 0D;

            // Start the player
            _ = Player.Play(); // Player is running on another thread.
        }

        #region XBoxController
        private void ControllerInit()
        {
            ControllerTimer = new DispatcherTimer();
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
        /// XBox controller state comes in fast, from another thread (like a button being held down)
        /// And it comes w/out respect to state of the system.
        /// So proessing is requests/commands looks at state of the system before 
        /// passing through commands.
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
                else if (reading.Buttons.HasFlag(GamepadButtons.A)) // Continue
                {
                    if (Player.Ready && Player.CurrentPlayerState != Player.PlayerState.Playing)
                        PauseContiue(PauseOrContinue.Continue);
                }
                else if (reading.Buttons.HasFlag(GamepadButtons.B)) // Pause
                {
                    if (Player.Ready && Player.CurrentPlayerState != Player.PlayerState.Paused)
                        PauseContiue(PauseOrContinue.Pause);
                }
                else if (reading.Buttons.HasFlag(GamepadButtons.DPadUp)) // Speed up
                    ChangePlaySpeed(ChangeSpeed.Faster);
                else if (reading.Buttons.HasFlag(GamepadButtons.DPadDown)) // Slow down
                    ChangePlaySpeed(ChangeSpeed.Slower);


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
        #endregion

        /// <summary>
        /// Receives message to set text string in the FNameTextBlock
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="aStr"></param>
        private void FNameChanged(object? sender, string aStr)
        {
            StatusTextBlock.Text = aStr;
        }
        private async void SelectFolderBtn_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            SelectFolder();
        }
        private async void SelectFolder()
        {
            SelectingFolder = true;
            //Message();

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
                FutureAccessList.AddOrReplace(PickedFolderToken, folder);
                StatusTextBlock.Text = folder.Path;

                // Change to Pause
                ContinuePauseBtn.Content = PauseStr;
                ContinuePauseBtn.IsEnabled = true;
                SetToolTip(ContinuePauseBtn, PauseTT);

                FNameChanged(null, "Starting...");

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
            var CommandQueue = Player.CommandQueue;

            if (Player.Ready)
                switch (ContinuePauseBtn.Content)
                {
                    case "Pause":
                        PauseContiue(PauseOrContinue.Pause);
                        break;
                    default: // Continue
                        PauseContiue(PauseOrContinue.Continue);
                        break;
                }
        }

        private enum PauseOrContinue
        { Pause, Continue }

        /// <summary>
        /// Send Pause or Continue command to Player
        /// </summary>
        /// <param name="pause"> True:pause, False:continue</param>
        private void PauseContiue(PauseOrContinue what)
        {
            if (PauseOrContinue.Pause == what)
            {
                ContinuePauseBtn.Content = ContiueStr;
                SetToolTip(ContinuePauseBtn, ContinueTT);

                // Send Pause command to player
                Player.CommandQueue.Enqueue(new PlayerCommand()
                {
                    Command = PlayerCommand.PlayerCommands.Pause
                });
                StatusTextBlock.Text = "Paused...";
            }
            else // Continue
            {
                ContinuePauseBtn.Content = PauseStr;
                SetToolTip(ContinuePauseBtn, PauseTT);

                // Send Cotinue command to player
                Player.CommandQueue.Enqueue(new PlayerCommand()
                {
                    Command = PlayerCommand.PlayerCommands.Continue
                });
                StatusTextBlock.Text = "Continuing...";
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

            if (ChangeSpeed.Slower == change)
                CurrSpeedIndex = Math.Max(0, CurrSpeedIndex - 1);
            else
                CurrSpeedIndex = Math.Min(MaxSpeed, CurrSpeedIndex + 1);

            PlaySpeedBar.Value = (Double)CurrSpeedIndex; // Adjust the PlaySpeedBar value

            int currSpeedMS = PlaySpeeds[CurrSpeedIndex];
            PlaySpeedTextBlock.Text = "Delay " + (currSpeedMS / 1000).ToString() + " seconds";

            // Send speed change to Player
            // Send ChangeSpeed command to player
            Player.CommandQueue.Enqueue(new PlayerCommand()
            {
                Command = PlayerCommand.PlayerCommands.ChangeSpeed,
                Value = currSpeedMS
            });
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

                // ???
                default:
                    base.OnKeyDown(e);
                    break;
            }
        }
    }
}