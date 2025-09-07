using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using MaterialDesignThemes.Wpf; // This using statement is still required.

namespace EmuFileX
{
    public partial class App : Application
    {
        public static string AdbPath { get; private set; } = "adb.exe";
        private const string PlatformToolsFolder = "platform-tools";

        private static readonly PaletteHelper _paletteHelper = new PaletteHelper();

        // ✅ === THIS IS THE CORRECT AND FINAL THEME-SWITCHING LOGIC === ✅
        public static void SetTheme(bool isDark)
        {
            ITheme theme = _paletteHelper.GetTheme();

            // We use the static properties Theme.Dark and Theme.Light which return the required IBaseTheme object.
            theme.SetBaseTheme(isDark ? Theme.Dark : Theme.Light);

            _paletteHelper.SetTheme(theme);
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (!IsAdbInPath())
            {
                string localAdbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PlatformToolsFolder, "adb.exe");
                if (File.Exists(localAdbPath)) { AdbPath = localAdbPath; }
                else
                {
                    var result = MessageBox.Show("لم يتم العثور على أداة ADB. هل تريد تنزيلها تلقائيًا؟", "ADB غير موجود", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        var downloadSuccess = await DownloadAndUnzipAdb();
                        if (!downloadSuccess) { Current.Shutdown(); return; }
                    }
                    else { MessageBox.Show("لا يمكن تشغيل الأداة بدون ADB. سيتم إغلاق البرنامج.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error); Current.Shutdown(); return; }
                }
            }
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        #region ADB Handling
        private bool IsAdbInPath()
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo("adb", "version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var process = System.Diagnostics.Process.Start(startInfo)) { process.WaitForExit(2000); return process.ExitCode == 0; }
            }
            catch { return false; }
        }
        private async Task<bool> DownloadAndUnzipAdb()
        {
            string downloadUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";
            string zipPath = Path.Combine(Path.GetTempPath(), "platform-tools.zip");
            string extractPath = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                MessageBox.Show("بدء تنزيل Platform Tools...", "تنزيل", MessageBoxButton.OK, MessageBoxImage.Information);
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync(downloadUrl);
                    response.EnsureSuccessStatusCode();
                    using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None)) { await response.Content.CopyToAsync(fs); }
                }
                ZipFile.ExtractToDirectory(zipPath, extractPath, true);
                File.Delete(zipPath);
                string downloadedAdbPath = Path.Combine(extractPath, PlatformToolsFolder, "adb.exe");
                AdbPath = downloadedAdbPath;
                MessageBox.Show("تم تنزيل ADB بنجاح!", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
                var addToPathResult = MessageBox.Show("هل تريد إضافة مسار ADB إلى متغيرات البيئة (PATH) في النظام؟", "إعداد ADB", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (addToPathResult == MessageBoxResult.Yes) { AddAdbToPath(Path.Combine(extractPath, PlatformToolsFolder)); }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"فشل التنزيل أو فك الضغط: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        private void AddAdbToPath(string adbDirectory)
        {
            try
            {
                string existingPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine);
                if (!existingPath.Contains(adbDirectory))
                {
                    string newPath = existingPath.TrimEnd(';') + ";" + adbDirectory;
                    Environment.SetEnvironmentVariable("Path", newPath, EnvironmentVariableTarget.Machine);
                    MessageBox.Show("تمت إضافة ADB إلى PATH بنجاح. قد تحتاج إلى إعادة تشغيل الكمبيوتر.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else { MessageBox.Show("مسار ADB موجود بالفعل في PATH.", "معلومة", MessageBoxButton.OK, MessageBoxImage.Information); }
            }
            catch (System.Security.SecurityException) { MessageBox.Show("فشلت إضافة المسار. يجب تشغيل البرنامج كمسؤول.", "صلاحيات مطلوبة", MessageBoxButton.OK, MessageBoxImage.Error); }
            catch (Exception ex) { MessageBox.Show($"حدث خطأ غير متوقع: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        #endregion
    }
}