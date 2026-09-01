using SQLite;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace UserControls.Reports
{
    public partial class ReportPage : Page
    {
        private List<DetectionResultModel> _allResults = new();

        public ReportPage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            DateFrom.SelectedDate = DateTime.Today.AddDays(-30);
            DateTo.SelectedDate = DateTime.Today;
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                _allResults = DetectionResultRepository.GetAll();
                ApplyFilters();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load reports: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilters()
        {
            if (DateFrom == null || DateTo == null || StatusFilter == null || _allResults == null)
                return;

            string selectedStatus = (StatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
            DateTime? fromDate = DateFrom.SelectedDate;
            DateTime? toDate = DateTo.SelectedDate?.AddDays(1).AddTicks(-1);

            var filtered = _allResults;

            if (selectedStatus != "All")
                filtered = filtered.FindAll(r => r.Status == selectedStatus);

            if (fromDate.HasValue)
                filtered = filtered.FindAll(r => r.ScanDate >= fromDate.Value);

            if (toDate.HasValue)
                filtered = filtered.FindAll(r => r.ScanDate <= toDate.Value);

            ReportsGrid.ItemsSource = filtered;

            int success = filtered.FindAll(r => r.Status == "Success").Count;
            int failed = filtered.FindAll(r => r.Status == "Failed").Count;
            int lessScore = filtered.FindAll(r => r.Status == "LessScore").Count;

            SuccessCount.Text = success.ToString();
            FailedCount.Text = failed.ToString();
            LessScoreCount.Text = lessScore.ToString();
            TotalCount.Text = filtered.Count.ToString();

            string dateInfo = "";
            if (filtered.Count > 0)
            {
                var earliest = filtered.Min(r => r.ScanDate);
                var latest = filtered.Max(r => r.ScanDate);
                dateInfo = $"{earliest:dd/MM/yy} - {latest:dd/MM/yy}";
            }

            SuccessDateRange.Text = dateInfo;
            FailedDateRange.Text = dateInfo;
            LessScoreDateRange.Text = dateInfo;
            TotalDateRange.Text = dateInfo;
        }

        private void StatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_allResults != null)
                ApplyFilters();
        }

        private void DateFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_allResults != null)
                ApplyFilters();
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            LoadData();
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DetectionResultModel result)
            {
                var confirm = MessageBox.Show(
                    $"Delete report #{result.Id} ({result.ScanDate:dd/MM/yyyy HH:mm})?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm == MessageBoxResult.Yes)
                {
                    try
                    {
                        DetectionResultRepository.Delete(result.Id);
                        LoadData();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Delete failed: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void ReportsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ReportsGrid.SelectedItem is DetectionResultModel result)
            {
                string detail =
                    $"Report #{result.Id}\n" +
                    $"Date: {result.ScanDate:yyyy-MM-dd HH:mm:ss}\n" +
                    $"Status: {result.Status}\n" +
                    $"Score: {result.Score:P0}\n" +
                    $"Total Boxes: {result.TotalBoxes}\n" +
                    $"Pallet Height: {result.PalletHeight:F2}m\n" +
                    $"Weight: {result.Weight}\n" +
                    $"Human Detected: {result.HumanDetected}\n" +
                    $"Barcodes: {result.BarcodeCount} ({result.BarcodeList})\n" +
                    $"Labels: {result.DateCount} ({result.DateList})\n" +
                    $"Attempts: {result.Attempts}\n" +
                    $"Task1 (Capture): {result.Task1StartTime} → {result.Task1EndTime}\n" +
                    $"Task2 (AI+OCR): {result.Task2StartTime} → {result.Task2EndTime}\n" +
                    $"Entry: {result.EntryTime}\n" +
                    $"Exit: {result.ExitTime}\n" +
                    $"Images: {result.ImagesPath}\n" +
                    $"Annotated: {result.AnnotatedPath}\n" +
                    $"OCR Result:\n{result.OCRResult}";

                MessageBox.Show(detail, $"Report #{result.Id} Details",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
