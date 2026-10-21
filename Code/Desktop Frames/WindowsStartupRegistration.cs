using System;
using System.IO;
using Microsoft.Win32;

namespace Desktop_Frames
{
    /// <summary>使用者登入啟動註冊；只有成功寫入並讀回一致後才視為啟用。</summary>
    public static class WindowsStartupRegistration
    {
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        // 工作管理員／設定「啟動應用程式」的開關；首位元組為奇數代表使用者停用，Run 項目仍在但 Windows 不會執行。
        public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        public const string ValueName = "Desktop Frames +";
        public static string Executable => Environment.ProcessPath ?? throw new InvalidOperationException("無法取得程式執行路徑。");
        public static string Command(string executable) => (char)34 + Path.GetFullPath(executable) + (char)34;

        public static bool IsBlockedByWindows(string valueName = ValueName, string approvedKeyPath = ApprovedKey)
        {
            using var key = Registry.CurrentUser.OpenSubKey(approvedKeyPath, false);
            return key?.GetValue(valueName) is byte[] state && state.Length > 0 && (state[0] & 1) == 1;
        }

        public static bool IsEnabled(string? executable = null, string keyPath = RunKey, string valueName = ValueName, string approvedKeyPath = ApprovedKey)
        {
            string path = Path.GetFullPath(executable ?? Executable);
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, false);
            return File.Exists(path) && string.Equals(key?.GetValue(valueName) as string, Command(path), StringComparison.OrdinalIgnoreCase)
                && !IsBlockedByWindows(valueName, approvedKeyPath);
        }

        public static void SetEnabled(bool enabled, string? executable = null, string keyPath = RunKey, string valueName = ValueName, string approvedKeyPath = ApprovedKey)
        {
            string path = Path.GetFullPath(executable ?? Executable);
            if (enabled && !File.Exists(path)) throw new FileNotFoundException("啟動程式不存在，無法登錄隨 Windows 啟動。", path);
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath, true)
                ?? throw new InvalidOperationException("無法建立 Windows 登入啟動設定，請檢查使用者權限。"))
            {
                if (enabled) key.SetValue(valueName, Command(path), RegistryValueKind.String);
                else key.DeleteValue(valueName, false);
            }
            // 使用者在本程式明確啟用時，一併解除 Windows 啟動應用程式的停用狀態；停用時清掉殘留狀態。
            using (var approved = Registry.CurrentUser.OpenSubKey(approvedKeyPath, true))
            {
                if (enabled && IsBlockedByWindows(valueName, approvedKeyPath)) approved?.SetValue(valueName, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
                else if (!enabled) approved?.DeleteValue(valueName, false);
            }
            using var verified = Registry.CurrentUser.OpenSubKey(keyPath, false);
            if (enabled ? !IsEnabled(path, keyPath, valueName, approvedKeyPath) : verified?.GetValue(valueName) != null)
                throw new IOException("Windows 登入啟動設定未成功儲存，請重新設定。");
        }
    }
}
