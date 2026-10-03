using System;
using System.Collections.Generic;
using System.IO;

namespace Desktop_Frames.Services
{
    public enum AdoptionType
    {
        /// <summary>
        /// 捷徑檔案（.lnk / .url）託管搬移至專屬儲存目錄，桌面外圍 100% 淨空
        /// </summary>
        ShortcutMove,

        /// <summary>
        /// 實體檔案或資料夾（.docx, .xlsx 等）原地設定 FileAttributes.Hidden，實體路徑不變
        /// </summary>
        InPlaceHidden
    }

    public enum AdoptionPhase
    {
        /// <summary>
        /// 登記準備接管（WAL 階段一）
        /// </summary>
        Pending,

        /// <summary>
        /// 執行接管中（搬移檔案或設定屬性中）
        /// </summary>
        Executing,

        /// <summary>
        /// 接管完成並持久化
        /// </summary>
        Committed,

        /// <summary>
        /// 失敗回滾中
        /// </summary>
        RollingBack,

        /// <summary>
        /// 程式關閉時休眠暫時歸還桌面，下次開啟時自動恢復收納至圍籬
        /// </summary>
        Suspended,

        /// <summary>
        /// 已安全釋放回桌面
        /// </summary>
        Released
    }

    public class ManagedItemRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string OriginalFullPath { get; set; } = string.Empty;
        public string ManagedStoragePath { get; set; } = string.Empty;
        public AdoptionType Type { get; set; }
        public FileAttributes OriginalAttributes { get; set; }
        public string BelongingFrameId { get; set; } = string.Empty;
        public AdoptionPhase Phase { get; set; } = AdoptionPhase.Pending;
        public DateTime RegisteredTime { get; set; } = DateTime.UtcNow;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public DateTime? TombstoneUntil { get; set; }
    }

    public class FenceInventoryState
    {
        public int Version { get; set; } = 1;
        public List<ManagedItemRecord> Items { get; set; } = new List<ManagedItemRecord>();
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
