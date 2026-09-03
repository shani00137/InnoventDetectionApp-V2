
using ACGPUIO;
using Basler.Pylon;
using Dynamsoft.Core;
using Dynamsoft.CVR;
using Dynamsoft.License;
using Dynamsoft.Utility;
using HumanDetection.Model;
using HumanDetection.Utilites.Animation;
using HumanDetection.Utilites.Audio;
using MaterialDesignThemes.Wpf;
using Microsoft.ML.OnnxRuntime;
using Microsoft.VisualBasic.ApplicationServices;
using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;







namespace HumanDetection
{
    public partial class MainWindow : System.Windows.Window
    {

        System.Windows.Media.Color NavForegroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#787AFF");
        System.Windows.Media.Color NavBackgroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#111134");

        private Uri _pendingNavUri;
        private Action _pendingNavAction;

        public MainWindow()
        {
            InitializeComponent();
            MaximizeRestoreButton_Click(null, null);
            var homePage = new Uri("UserControls/Home/Home.xaml", UriKind.Relative);

            MainFrame.Navigate(homePage);


            //  LoadCamera();

        }

        /// <summary>
        /// Checks the current (Home) page for a running process. If one is running,
        /// opens a confirmation dialog; navigation only happens after the operator
        /// confirms. Returns true when it is safe/OK to navigate now.
        /// </summary>
        private bool TryNavigate(Uri uri, Action navigateAction)
        {
            // Only Home runs background processes that need stopping.
            if (MainFrame.Content is Home home && home.IsProcessRunning)
            {
                _pendingNavUri = uri;
                _pendingNavAction = navigateAction;
                NavConfirmDialogHost.IsOpen = true;
                return false;
            }

            navigateAction();
            return true;
        }

        private void NavConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            NavConfirmDialogHost.IsOpen = false;

            var navigateAction = _pendingNavAction;
            if (navigateAction == null) return;

            // Stop the pending action reference, then (if leaving Home) stop processes.
            _pendingNavAction = null;
            _pendingNavUri = null;

            if (MainFrame.Content is Home home)
            {
                _ = StopAndNavigateAsync(home, navigateAction);
            }
            else
            {
                navigateAction();
            }
        }

        private void NavConfirmNo_Click(object sender, RoutedEventArgs e)
        {
            NavConfirmDialogHost.IsOpen = false;
            _pendingNavUri = null;
            _pendingNavAction = null;
        }

        private async Task StopAndNavigateAsync(Home home, Action navigateAction)
        {
            try
            {
                await home.StopAllProcessesAsync();
            }
            catch
            {
            }
            navigateAction();
        }

        #region Window UI Controls
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                this.MaxHeight = double.PositiveInfinity; // Reset max height
            }
            else
            {
                var desktopWorkingArea = SystemParameters.WorkArea; // Get area excluding taskbar
                this.MaxHeight = desktopWorkingArea.Height + 10;
                this.MaxWidth = desktopWorkingArea.Width + 12;


                this.WindowState = WindowState.Maximized;

            }
        }
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ResetNavButtons()
        {
            HomeNavBtn.Foreground = new SolidColorBrush(Colors.White);
            HomeNavBtn.Background = new SolidColorBrush(Colors.Transparent);
            SettingNavBtn.Foreground = new SolidColorBrush(Colors.White);
            SettingNavBtn.Background = new SolidColorBrush(Colors.Transparent);
            ReportsNavBtn.Foreground = new SolidColorBrush(Colors.White);
            ReportsNavBtn.Background = new SolidColorBrush(Colors.Transparent);
            TestingNavBtn.Foreground = new SolidColorBrush(Colors.White);
            TestingNavBtn.Background = new SolidColorBrush(Colors.Transparent);
            MoxaStatusNavBtn.Foreground = new SolidColorBrush(Colors.White);
            MoxaStatusNavBtn.Background = new SolidColorBrush(Colors.Transparent);
        }

        private void NavToHomePage_Click(object sender, RoutedEventArgs e)
        {
            TryNavigate(
                new Uri("UserControls/Home/Home.xaml", UriKind.Relative),
                () =>
                {
                    MainFrame.Navigate(new Uri("UserControls/Home/Home.xaml", UriKind.Relative));
                    ResetNavButtons();
                    HomeNavBtn.Foreground = new SolidColorBrush(NavForegroundColor);
                    HomeNavBtn.Background = new SolidColorBrush(NavBackgroundColor);
                });
        }
        private void NavToSettingPage_Click(object sender, RoutedEventArgs e)
        {
            TryNavigate(
                new Uri("UserControls/Settings/Setting.xaml", UriKind.Relative),
                () =>
                {
                    MainFrame.Navigate(new Uri("UserControls/Settings/Setting.xaml", UriKind.Relative));
                    ResetNavButtons();
                    SettingNavBtn.Foreground = new SolidColorBrush(NavForegroundColor);
                    SettingNavBtn.Background = new SolidColorBrush(NavBackgroundColor);
                });
        }
        private void NavToReportsPage_Click(object sender, RoutedEventArgs e)
        {
            TryNavigate(
                new Uri("UserControls/Reports/ReportPage.xaml", UriKind.Relative),
                () =>
                {
                    MainFrame.Navigate(new Uri("UserControls/Reports/ReportPage.xaml", UriKind.Relative));
                    ResetNavButtons();
                    ReportsNavBtn.Foreground = new SolidColorBrush(NavForegroundColor);
                    ReportsNavBtn.Background = new SolidColorBrush(NavBackgroundColor);
                });
        }
        private void NavToLivePreviewPage_Click(object sender, RoutedEventArgs e)
        {
            TryNavigate(
                new Uri("UserControls/LivePreview/LivePreview.xaml", UriKind.Relative),
                () =>
                {
                    MainFrame.Navigate(new Uri("UserControls/LivePreview/LivePreview.xaml", UriKind.Relative));
                    ResetNavButtons();
                });
        }
        private void NavToTestingPage_Click(object sender, RoutedEventArgs e)
        {
            TryNavigate(
                new Uri("UserControls/Testing/Testing.xaml", UriKind.Relative),
                () =>
                {
                    MainFrame.Navigate(new Uri("UserControls/Testing/Testing.xaml", UriKind.Relative));
                    ResetNavButtons();
                    TestingNavBtn.Foreground = new SolidColorBrush(NavForegroundColor);
                    TestingNavBtn.Background = new SolidColorBrush(NavBackgroundColor);
                });
        }
        private void NavToMoxaStatusPage_Click(object sender, RoutedEventArgs e)
        {
            TryNavigate(
                new Uri("UserControls/MoxaStatus/MoxaStatus.xaml", UriKind.Relative),
                () =>
                {
                    MainFrame.Navigate(new Uri("UserControls/MoxaStatus/MoxaStatus.xaml", UriKind.Relative));
                    ResetNavButtons();
                    MoxaStatusNavBtn.Foreground = new SolidColorBrush(NavForegroundColor);
                    MoxaStatusNavBtn.Background = new SolidColorBrush(NavBackgroundColor);
                });
        }

        #endregion


    }
}
