using System;
using System.Globalization;

namespace Desktop_Frames.FarmFences
{
    public static class FenceText
    {
        public static Func<string, string>? Resource { get; set; }
        public static string Get(string key, params object[] args)
        {
            string text = Resource?.Invoke(key) ?? key switch
            {
                "FarmNew" => "新增農場柵欄",
                "FarmName" => "農場柵欄 {0}",
                "FarmRename" => "重新命名",
                "FarmCancel" => "取消柵欄（圖示保留在桌面）",
                "FarmNameLabel" => "柵欄名稱",
                "FarmSave" => "儲存",
                "FarmLoadFailed" => "農場柵欄設定讀取失敗，已保護原設定不被覆寫。",
                "FarmOperationFailed" => "農場柵欄操作未完成；原圖示仍留在桌面。請查看診斷記錄。",
                "FarmSaveFailed" => "農場柵欄設定無法儲存。請檢查資料夾寫入權限。",
                _ => key
            };
            return args.Length == 0 ? text : string.Format(CultureInfo.CurrentUICulture, text, args);
        }
    }
}
