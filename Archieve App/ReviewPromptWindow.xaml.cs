using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;

namespace Archieve_App
{
    public partial class ReviewPromptWindow : FluentWindow
    {
        private const string StoreProtocolUri = "ms-windows-store://review/?ProductId=9n4hcx0krfnp";
        private const string StoreWebUri = "https://apps.microsoft.com/detail/9n4hcx0krfnp?hl=en-US&gl=US";
        private const string SourceForgeUri = "https://sourceforge.net/projects/zenarchieve/reviews/";

        public ReviewPromptWindow()
        {
            InitializeComponent();

            try
            {
                var iconUri = new Uri("pack://application:,,,/assets/app.ico", UriKind.Absolute);
                Icon = BitmapFrame.Create(iconUri);
            }
            catch { }

            ChkDontShowAgain.IsChecked = AppSettings.Instance.DontShowReviewDialog;
        }

        private void BtnReviewStore_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Attempt to open native Microsoft Store review sheet
                Process.Start(new ProcessStartInfo(StoreProtocolUri) { UseShellExecute = true });
            }
            catch
            {
                // Fallback to web store page
                try
                {
                    Process.Start(new ProcessStartInfo(StoreWebUri) { UseShellExecute = true });
                }
                catch { }
            }

            AppSettings.Instance.SetDontShowAgain();
            Close();
        }

        private void BtnReviewSourceForge_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(SourceForgeUri) { UseShellExecute = true });
            }
            catch { }

            AppSettings.Instance.SetDontShowAgain();
            Close();
        }

        private void ChkDontShowAgain_Checked(object sender, RoutedEventArgs e)
        {
            AppSettings.Instance.SetDontShowAgain();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (ChkDontShowAgain.IsChecked == true)
            {
                AppSettings.Instance.SetDontShowAgain();
            }

            Close();
        }
    }
}
