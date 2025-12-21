using SQLite;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace UserControls.Settings
{
    /// <summary>
    /// Interaction logic for Setting.xaml
    /// </summary>
    public partial class Setting : Page
    {
       
        private AppSettings _settings;

        public Setting()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            _settings = SettingsRepository.GetSettings();

            if (_settings == null) return;

            TunnelID.Text = _settings.ComPort;
            SecurityLock.Text = _settings.MoxIP;
            DBConnectionURL.Text = _settings.DatabaseURL;
            BackOfficeURL.Text = _settings.BackOfficeURL;

            if (int.TryParse(_settings.ConfidenceLevel, out int val))
            {
                MinSlider.Value = val;
                PowerValueText.Text = val.ToString();
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            var data = new AppSettings
            {
                ComPort = TunnelID.Text,
                MoxIP = SecurityLock.Text,
                DatabaseURL = DBConnectionURL.Text,
                BackOfficeURL = BackOfficeURL.Text,
                ConfidenceLevel = ((int)MinSlider.Value).ToString()
            };

            if (_settings == null)
            {
                SettingsRepository.InsertSettings(data);
            }
            else
            {
                data.Id = _settings.Id;
                SettingsRepository.UpdateSettings(data);
            }

            MessageBox.Show("Settings saved successfully", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);

            LoadSettings();
        }

        private void MinSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PowerValueText != null)
                PowerValueText.Text = ((int)e.NewValue).ToString();
        }

    }
}
