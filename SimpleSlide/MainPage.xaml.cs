using System;
using System.IO;
using System.Linq;
using Windows.Gaming.Input;
using Windows.System;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;

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

        // Play speeds
        private readonly int SlowSpeed = 15 * 1000;
        private readonly int MediumSpeed = 10 * 1000;
        private readonly int FastSpeed = 3 * 1000;

        // XBox controller
        private Gamepad? Controller { get; set; }
        private DispatcherTimer? ControllerTimer { get; set; }
        private Boolean SelectingFolder { get; set; } = false;

        public MainPage()
        {
            InitializeComponent();

            FNameProgress = new Progress<String>();
            Player = new(PickedFolderToken, FNameProgress)
            {
                ImagePane = [null, Image1, null], // The XAML image elements
                DelayBetweenImges = MediumSpeed
            };
            FNameProgress.ProgressChanged += FNameChanged;

            ControllerInit(); // Set up for XBox controller

            // Start the player
            _ = Player.Play(); // Async operation, Player running on another thread.
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
                        PauseContiue(false);
                }
                else if (reading.Buttons.HasFlag(GamepadButtons.B)) // Pause
                {
                    if (Player.Ready && Player.CurrentPlayerState != Player.PlayerState.Paused)
                        PauseContiue(true);
                }

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

            switch (ContinuePauseBtn.Content)
            {
                case "Pause":
                    PauseContiue(true);
                    break;
                default: // Continue
                    PauseContiue(false);
                    break;
            }
        }
        /// <summary>
        /// Send Pause or Continue command to Player
        /// </summary>
        /// <param name="pause"> True:pause, False:continue</param>
        private void PauseContiue(Boolean pause)
        {
            if (pause)
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

        /// <summary>
        /// A messagebox in UWP land
        /// </summary>
        private void Message()
        {
            // Create the message dialog and set its content
            var messageDialog = new MessageDialog("Button pushed.");

            // Add commands and set their callbacks; both buttons use the same callback function instead of inline event handlers
            messageDialog.Commands.Add(new UICommand(
                "Try again",
                new UICommandInvokedHandler(this.CommandInvokedHandler)));
            messageDialog.Commands.Add(new UICommand(
                "Close",
                new UICommandInvokedHandler(this.CommandInvokedHandler)));

            // Set the command that will be invoked by default
            messageDialog.DefaultCommandIndex = 0;

            // Set the command to be invoked when escape is pressed
            messageDialog.CancelCommandIndex = 1;

            // Show the message dialog
            messageDialog.ShowAsync();

        }
        private void CommandInvokedHandler(IUICommand command)
        {
            // Display message showing the label of the command that was invoked
            //rootPage.NotifyUser("The '" + command.Label + "' command has been selected.",
            //    NotifyType.StatusMessage);
        }

        private void SlowSpeed_RB_Click(object sender, RoutedEventArgs e)
        {
            // Send ChangeSpeed command to player
            Player.CommandQueue.Enqueue(new PlayerCommand()
            {
                Command = PlayerCommand.PlayerCommands.ChangeSpeed,
                Value = SlowSpeed
            });
        }

        private void MediumSpeed_RB_Click(object sender, RoutedEventArgs e)
        {
            // Send ChangeSpeed command to player
            Player.CommandQueue.Enqueue(new PlayerCommand()
            {
                Command = PlayerCommand.PlayerCommands.ChangeSpeed,
                Value = MediumSpeed
            });
        }

        private void FastSpeed_RB_Click(object sender, RoutedEventArgs e)
        {
            // Send ChangeSpeed command to player
            Player.CommandQueue.Enqueue(new PlayerCommand()
            {
                Command = PlayerCommand.PlayerCommands.ChangeSpeed,
                Value = FastSpeed
            });
        }
        /// <summary>
        /// Keyboard events
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void KeyDownEvent(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {

                case VirtualKey.P:
                    break;
                case VirtualKey.C:
                    break;


                // ???
                default:
                    base.OnKeyDown(e);
                    break;
            }

        }
    }
}