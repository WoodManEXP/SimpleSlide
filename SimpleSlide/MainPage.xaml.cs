using System;
using Windows.UI.Popups;
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
        private readonly String PauseTT = "Pause slide show";
        private readonly String ContiueStr = "Continue";
        private readonly String ContinueTT = "Continue slide show";

        private readonly String? PickedFolderToken = "PickedFolderToken";

        public Progress<String> FNameProgress;
        public Progress<Boolean> ProgressBarProgress; 
        private Player Player;

        // Play speeds
        private readonly int SlowSpeed = 15 * 1000;
        private readonly int MediumSpeed = 10 * 1000;
        private readonly int FastSpeed = 3 * 1000;

        public MainPage()
        {
            InitializeComponent();

            FNameProgress = new Progress<String>();
            ProgressBarProgress = new Progress<Boolean>();
            Player = new(PickedFolderToken, FNameProgress, ProgressBarProgress)
            {
                ImagePane = [null, Image1, null], // The XAML image elements
                DelayBetweenImges = MediumSpeed
            };
            FNameProgress.ProgressChanged += FNameChanged;
            ProgressBarProgress.ProgressChanged += ActivateProgressBar;

            // Start the player
            _ = Player.Play(); // Async operation, Player running on another thread.
        }

        /// <summary>
        /// Receives message to set text string in the FNameTextBlock
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="fileName"></param>
        private void FNameChanged(object? sender, string fileName)
        {
            FNameTextBlock.Text = fileName;
        }

        /// <summary>
        /// Receives message to set Visability of ProgressBar
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="activate"></param>        
        private void ActivateProgressBar(object? sender, Boolean activate)
        {
            ProgressBar.Visibility = activate ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void SelectFolderBtn_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            //Message();

            // Disable Select folders btn
            ((Button)sender).IsEnabled = false;

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
                FNameTextBlock.Text = folder.Path;

                // Change to Pause
                ContinuePauseBtn.Content = PauseStr;
                ContinuePauseBtn.IsEnabled = true;
                SetToolTip(ContinuePauseBtn, PauseTT);

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
            ((Button)sender).IsEnabled = true;
        }
        private void ContinuePauseBtn_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            var CommandQueue = Player.CommandQueue;

            switch (ContinuePauseBtn.Content)
            {
                case "Pause":
                    ContinuePauseBtn.Content = ContiueStr;
                    SetToolTip(ContinuePauseBtn, ContinueTT);

                    // Send Pause command to player
                    Player.CommandQueue.Enqueue(new PlayerCommand()
                    {
                        Command = PlayerCommand.PlayerCommands.Pause
                    });
                    break;
                default: // Continue
                    ContinuePauseBtn.Content = PauseStr;
                    SetToolTip(ContinuePauseBtn, PauseTT);

                    // Send Cotinue command to player
                    Player.CommandQueue.Enqueue(new PlayerCommand()
                    {
                        Command = PlayerCommand.PlayerCommands.Continue
                    });
                    break;
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
    }
}
