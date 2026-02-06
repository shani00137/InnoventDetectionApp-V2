
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
        public MainWindow()
        {
            InitializeComponent();
            MaximizeRestoreButton_Click(null, null);
            var homePage = new Uri("UserControls/Home/Home.xaml", UriKind.Relative);

            MainFrame.Navigate(homePage);


            //  LoadCamera();

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

        private void NavToHomePage_Click(object sender, RoutedEventArgs e)
        {
            var homePage = new Uri("UserControls/Home/Home.xaml", UriKind.Relative);
           
            MainFrame.Navigate(homePage);
            HomeNavBtn.Foreground = new SolidColorBrush(NavForegroundColor);
            HomeNavBtn.Background = new SolidColorBrush(NavBackgroundColor);

          

            SettingNavBtn.Foreground = new SolidColorBrush(Colors.White);
            SettingNavBtn.Background = new SolidColorBrush(Colors.Transparent);

        }
        private void NavToSettingPage_Click(object sender, RoutedEventArgs e)
        {
            var homePage = new Uri("UserControls/Settings/Setting.xaml", UriKind.Relative);
       
            MainFrame.Navigate(homePage);

            SettingNavBtn.Foreground = new SolidColorBrush(NavForegroundColor);
            SettingNavBtn.Background = new SolidColorBrush(NavBackgroundColor);

            

            HomeNavBtn.Foreground = new SolidColorBrush(Colors.White);
            HomeNavBtn.Background = new SolidColorBrush(Colors.Transparent);
        }
        private void NavToLivePreviewPage_Click(object sender, RoutedEventArgs e)
        {
            var homePage = new Uri("UserControls/LivePreview/LivePreview.xaml", UriKind.Relative);
          
            MainFrame.Navigate(homePage);

            SettingNavBtn.Foreground = new SolidColorBrush(Colors.White);
            SettingNavBtn.Background = new SolidColorBrush(Colors.Transparent);

            HomeNavBtn.Foreground = new SolidColorBrush(Colors.White);
            HomeNavBtn.Background = new SolidColorBrush(Colors.Transparent);
        }

        #endregion


    }
}
