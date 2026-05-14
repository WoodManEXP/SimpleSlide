using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Content Dialog item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace SimpleSlide
{
    public sealed partial class MessageDialog : ContentDialog
    {
        public String MessageTitle
        {
            get { return (String)Title; } // Title defined in XAML
            set { Title = value; }
        }
        public String Message0
        {
            get { return DialogMessage0.Text; }
            set { DialogMessage0.Text = value; }
        }
        public String Message1
        {
            get { return DialogMessage1.Text; }
            set { DialogMessage1.Text = value; }
        }
        public MessageDialog()
        {
            this.InitializeComponent();
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
        }

        private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
        }
    }
}
