using Basler.Pylon;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace HumanDetection
{
    public partial class LivePreview : UserControl
    {
        public event EventHandler CloseClicked;

        public LivePreview()
        {
            InitializeComponent();
            //LoadBaslerCameras();
        }

        private void LoadBaslerCameras()
        {
            try
            {
                // Enumerate all Basler cameras
                var allCameras = CameraFinder.Enumerate();

                if (allCameras.Count == 0)
                {
                    CameraComboBox.Items.Add("No Cameras Found");
                    return;
                }

                // Add each camera to ComboBox
                foreach (var camera in allCameras)
                {
                    CameraComboBox.Items.Add($"{camera[CameraInfoKey.FriendlyName]}  ({camera[CameraInfoKey.SerialNumber]})");
                }

                // Optional: select first camera
                CameraComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading cameras: " + ex.Message);
            }
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            string selectedCamera = CameraComboBox.SelectedItem?.ToString();
            MessageBox.Show("Selected Camera: " + selectedCamera);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
