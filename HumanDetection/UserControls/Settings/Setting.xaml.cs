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
            LoadRules();
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
        public void AddRegexRule_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RuleNameTextBox.Text) ||
                string.IsNullOrWhiteSpace(RegexPatternTextBox.Text))
            {
                MessageBox.Show("Rule Name and Regex are required");
                return;
            }

            // Optional: validate regex
            try
            {
                _ = new System.Text.RegularExpressions.Regex(RegexPatternTextBox.Text);
            }
            catch
            {
                MessageBox.Show("Invalid Regex pattern");
                return;
            }

            SettingsRepository.Insert(
                RuleNameTextBox.Text.Trim(),
                RegexPatternTextBox.Text.Trim()
            );

            RuleNameTextBox.Clear();
            RegexPatternTextBox.Clear();

            LoadRules();
        }

        public void DeleteRegexRule_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not RuleModel rule)
                return;

            var result = MessageBox.Show(
                $"Delete rule '{rule.RuleName}'?",
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            SettingsRepository.Delete(rule.Id);
            LoadRules();
        }

        private void LoadRules()
        {
            RegexRulesGrid.ItemsSource = SettingsRepository.GetAll();
        }



    }
}
