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
                "FarmNoSpace" => "可用格子不足，已取消本次操作並保留原位置與歸屬。請移開柵欄或增加可用空間。",
                "FarmOverlap" => "既有柵欄重疊，已暫停排列與歸屬變更。請先手動移開重疊外框；原圖示與歸屬保留。",
                "FarmRestoreFailed" => "Explorer 拒絕位置回復，已停止儲存以保護原歸屬。請關閉自動排列／對齊格線並檢查桌面位置。",
                _ => key
            };
            return args.Length == 0 ? text : string.Format(CultureInfo.CurrentUICulture, text, args);
        }
    }
}
