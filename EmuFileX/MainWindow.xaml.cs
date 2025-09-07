using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace EmuFileX
{
    public partial class MainWindow : Window
    {
        public class FileSystemItem
        {
            public string Name { get; set; }
            public string FullPath { get; set; }
            public string Type { get; set; }
            public string Size { get; set; }
            public string LastModified { get; set; }
            public string IconPath => Type == "Directory" ? "pack://application:,,,/Icons/folder.png" : "pack://application:,,,/Icons/file.png";
        }

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadEmulators();
            InitializeFileExplorer();
        }

        private void UpdateStatus(string message, bool isError = false)
        {
            var messageQueue = MainSnackbar.MessageQueue;
            messageQueue.Enqueue(message);
            if (isError) { MessageBox.Show(message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string url)
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
        }

        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggleButton && toggleButton.IsChecked.HasValue)
            {
                App.SetTheme(toggleButton.IsChecked.Value);
            }
        }

        #region Core Functions
        private string ExecuteAdbCommand(string command)
        {
            try
            {
                ProcessStartInfo procStartInfo = new ProcessStartInfo(App.AdbPath, command) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
                using (Process process = new Process { StartInfo = procStartInfo })
                {
                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (!string.IsNullOrEmpty(error) && !error.Contains("total"))
                    {
                        if (error.ToLower().Contains("error") || error.ToLower().Contains("failed") || error.ToLower().Contains("denied")) return $"Error: {error}";
                    }
                    return output;
                }
            }
            catch (Exception ex) { return $"Exception: {ex.Message}"; }
        }

        private void LoadEmulators()
        {
            EmulatorsComboBox.Items.Clear();
            string result = ExecuteAdbCommand("devices");
            var devices = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Skip(1);
            foreach (var device in devices) { if (!string.IsNullOrWhiteSpace(device) && device.Contains("device")) { EmulatorsComboBox.Items.Add(device.Split('\t')[0]); } }
            if (EmulatorsComboBox.Items.Count > 0) EmulatorsComboBox.SelectedIndex = 0;
            else UpdateStatus("لم يتم العثور على أي أجهزة متصلة.", isError: true);
        }
        #endregion

        #region File Explorer
        private void InitializeFileExplorer()
        {
            DirectoryTreeView.Items.Clear();
            var rootItem = new TreeViewItem() { Header = "/sdcard/", Tag = "/sdcard/" };
            rootItem.Items.Add(null);
            rootItem.Expanded += Directory_Expanded;
            DirectoryTreeView.Items.Add(rootItem);
            rootItem.IsSelected = true;
        }
        void Directory_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem item && item.Items.Count == 1 && item.Items[0] == null)
            {
                item.Items.Clear();
                string path = (string)item.Tag;
                var subDirs = GetDirectoryContents(path).Where(i => i.Type == "Directory");
                foreach (var dir in subDirs)
                {
                    var subItem = new TreeViewItem() { Header = dir.Name, Tag = dir.FullPath };
                    subItem.Items.Add(null);
                    subItem.Expanded += Directory_Expanded;
                    item.Items.Add(subItem);
                }
            }
        }
        private void DirectoryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) { RefreshFileExplorerView(); }
        private List<FileSystemItem> GetDirectoryContents(string path)
        {
            var items = new List<FileSystemItem>();
            string deviceId = EmulatorsComboBox.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(deviceId)) return items;
            string command = $"-s {deviceId} shell ls -l \"{path}\"";
            string output = ExecuteAdbCommand(command);
            var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0 && (lines[0].StartsWith("total") || lines[0].Contains("No such file or directory"))) { lines = lines.Skip(1).ToArray(); }
            foreach (var line in lines)
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 6) continue;
                string permissions = parts[0]; bool isDirectory = permissions.StartsWith("d"); string size = isDirectory ? "" : parts[4];
                string date = parts[5]; string time = parts[6]; string name = string.Join(" ", parts.Skip(7));
                items.Add(new FileSystemItem { Name = name, FullPath = $"{path.TrimEnd('/')}/{name}", Type = isDirectory ? "Directory" : "File", Size = size, LastModified = $"{date} {time}" });
            }
            return items;
        }
        #endregion

        #region Event Handlers
        private void BrowseApkButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "APK Files (*.apk)|*.apk" };
            if (openFileDialog.ShowDialog() == true) ApkPathTextBox.Text = openFileDialog.FileName;
        }
        private void InstallApkButton_Click(object sender, RoutedEventArgs e)
        {
            string deviceId = EmulatorsComboBox.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(deviceId) || !File.Exists(ApkPathTextBox.Text)) { UpdateStatus("الرجاء اختيار محاكي وملف APK صالح.", isError: true); return; }
            UpdateStatus("جاري تثبيت التطبيق...");
            string command = $"-s {deviceId} install -r \"{ApkPathTextBox.Text}\"";
            string result = ExecuteAdbCommand(command);
            if (result.ToLower().Contains("success")) { UpdateStatus("تم تثبيت التطبيق بنجاح."); } else { UpdateStatus($"فشل التثبيت: {result}", isError: true); }
        }
        private void BrowsePushFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog { Title = "اختر ملفًا لنقله" };
            if (openFileDialog.ShowDialog() == true) PushSourcePath.Text = openFileDialog.FileName;
        }
        private void PushButton_Click(object sender, RoutedEventArgs e)
        {
            string deviceId = EmulatorsComboBox.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(deviceId)) return;
            string sourceFilePath = PushSourcePath.Text;
            if (!File.Exists(sourceFilePath)) { UpdateStatus("الملف المصدر غير موجود على الكمبيوتر.", isError: true); return; }
            if (Path.GetExtension(sourceFilePath).Equals(".apk", StringComparison.OrdinalIgnoreCase))
            {
                var choice = MessageBox.Show("هذا ملف APK. هل تريد تثبيته؟\n- نعم: لتثبيت التطبيق.\n- لا: لنسخ ملف APK.", "تم اكتشاف ملف APK", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (choice == MessageBoxResult.Yes) { InstallApkButton_Click(sender, e); return; }
                if (choice == MessageBoxResult.Cancel) return;
            }
            UpdateStatus($"جاري نقل الملف: {Path.GetFileName(sourceFilePath)}...");
            string destinationPath = PushDestinationPath.Text;
            string pushCmd = $"-s {deviceId} push \"{sourceFilePath}\" \"{destinationPath}\"";
            string result = ExecuteAdbCommand(pushCmd);
            if (result.Contains("pushed")) { UpdateStatus($"تم نقل الملف بنجاح."); } else { UpdateStatus($"فشل نقل الملف: {result}", isError: true); }
            RefreshFileExplorerView();
        }
        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (FilesListView.SelectedItem is FileSystemItem selectedItem)
            {
                string deviceId = EmulatorsComboBox.SelectedItem?.ToString(); if (string.IsNullOrEmpty(deviceId)) return;
                if (MessageBox.Show($"هل أنت متأكد من حذف {selectedItem.Name}؟", "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    UpdateStatus($"جاري حذف {selectedItem.Name}...");
                    string command = $"-s {deviceId} shell rm {(selectedItem.Type == "Directory" ? "-r " : "")}\"{selectedItem.FullPath}\"";
                    string result = ExecuteAdbCommand(command);
                    if (string.IsNullOrWhiteSpace(result) || !result.Contains("Error")) { UpdateStatus($"تم حذف '{selectedItem.Name}' بنجاح."); }
                    else { UpdateStatus($"فشل الحذف: {result}", isError: true); }
                    RefreshFileExplorerView();
                }
            }
        }
        private void PullMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (FilesListView.SelectedItem is FileSystemItem selectedItem && selectedItem.Type == "File")
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog { FileName = selectedItem.Name };
                if (saveFileDialog.ShowDialog() == true)
                {
                    string deviceId = EmulatorsComboBox.SelectedItem?.ToString(); if (string.IsNullOrEmpty(deviceId)) return;
                    UpdateStatus($"جاري سحب الملف {selectedItem.Name}...");
                    string command = $"-s {deviceId} pull \"{selectedItem.FullPath}\" \"{saveFileDialog.FileName}\"";
                    string result = ExecuteAdbCommand(command);
                    if (result.Contains("pulled")) { UpdateStatus("تم سحب الملف بنجاح إلى الكمبيوتر."); }
                    else { UpdateStatus($"فشل سحب الملف: {result}", isError: true); }
                }
            }
            else { MessageBox.Show("يمكن سحب الملفات فقط.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information); }
        }
        private void RefreshFileExplorerView()
        {
            if (DirectoryTreeView.SelectedItem is TreeViewItem selectedItem)
            {
                string path = (string)selectedItem.Tag;
                FilesListView.Items.Clear();
                var items = GetDirectoryContents(path);
                foreach (var item in items) { FilesListView.Items.Add(item); }
            }
        }
        #endregion

        private void Transitioner_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}