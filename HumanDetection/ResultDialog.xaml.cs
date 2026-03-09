using Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

namespace HumanDetection
{
    /// <summary>
    /// Interaction logic for ResultDialog.xaml
    /// </summary>
    public partial class ResultDialog : UserControl
    {
        public event EventHandler CloseClicked;
        public event EventHandler RestartProcessClicked;

        public ObservableCollection<ResutlModel> ResultDataList { get; set; }
        public ResultDialog()
        {
            InitializeComponent();
        }
        public void UpdateResults(ObservableCollection<ResutlModel> results)
        {
            
            ResultDataList = results;
            this.DataContext = null;
            this.DataContext = this; // refresh binding
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseClicked?.Invoke(this, EventArgs.Empty);
        }

        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            RestartProcessClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
