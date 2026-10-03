using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Desktop_Frames.Services
{
    public class DesktopReconciler : IDisposable
    {
        private static readonly Lazy<DesktopReconciler> _instance =
            new Lazy<DesktopReconciler>(() => new DesktopReconciler());

        public static DesktopReconciler Instance => _instance.Value;

        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private readonly ConcurrentDictionary<string, DateTime> _tombstones =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly Timer _debounceTimer;
        private readonly ConcurrentQueue<string> _pendingEvents = new ConcurrentQueue<string>();
        private readonly object _lock = new object();
        private bool _isDisposed = false;

        public DesktopReconciler()
        {
            // 防抖定時器：600ms 檢查一次
            _debounceTimer = new Timer(ProcessDebouncedEvents, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start()
        {
            lock (_lock)
            {
                Stop();

                try
                {
                    string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    if (Directory.Exists(userDesktop))
                    {
                        RegisterWatcher(userDesktop);
                    }

                    string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                    if (Directory.Exists(commonDesktop))
                    {
                        RegisterWatcher(commonDesktop);
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                        $"Failed to start DesktopReconciler watchers: {ex.Message}");
                }
            }
        }

        private void RegisterWatcher(string directoryPath)
        {
            try
            {
                var watcher = new FileSystemWatcher(directoryPath)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                   NotifyFilters.Attributes | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false,
                    InternalBufferSize = 65536 // 64KB 緩衝區避免高頻溢位
                };

                watcher.Deleted += OnFileSystemDeleted;
                watcher.Renamed += OnFileSystemRenamed;
                watcher.Changed += OnFileSystemChanged;
                watcher.Error += OnWatcherError;

                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"Registered desktop watcher on: {directoryPath}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"Could not register watcher on '{directoryPath}': {ex.Message}");
            }
        }

        private void OnFileSystemDeleted(object sender, FileSystemEventArgs e)
        {
            // 收到 Deleted 事件時，不直接銷毀庫存記錄！
            // 很多編輯器（如 Word、Excel、VS Code）的原子存檔機制是：
            // 寫入臨時檔 -> 刪除原檔 -> 將臨時檔改名為原檔。
            // 立即刪除會導致假警報，因此標記 Tombstone 並進入 3 秒寬限期。
            string fullPath = e.FullPath;
            var record = FenceInventoryManager.Instance.FindRecord(fullPath);
            if (record != null)
            {
                _tombstones[fullPath] = DateTime.UtcNow.AddSeconds(3);
                ScheduleDebounce(fullPath);
            }
        }

        private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
        {
            // 若原檔被改名，更新庫存記錄中的路徑
            var record = FenceInventoryManager.Instance.FindRecord(e.OldFullPath);
            if (record != null)
            {
                record.OriginalFullPath = e.FullPath;
                record.DisplayName = Path.GetFileNameWithoutExtension(e.FullPath);
                if (record.Type == AdoptionType.InPlaceHidden)
                {
                    record.ManagedStoragePath = e.FullPath;
                }
            }
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            ScheduleDebounce(e.FullPath);
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                $"Desktop watcher buffer overflow or error: {e.GetException()?.Message}. Scheduling full reconciliation.");

            // 發生緩衝區溢位時，執行全量對帳
            Task.Delay(1000).ContinueWith(_ =>
            {
                try
                {
                    FenceInventoryManager.Instance.ReconcileOnStartup();
                }
                catch { }
            });
        }

        private void ScheduleDebounce(string path)
        {
            _pendingEvents.Enqueue(path);
            _debounceTimer.Change(600, Timeout.Infinite);
        }

        private void ProcessDebouncedEvents(object state)
        {
            try
            {
                var now = DateTime.UtcNow;
                var expiredTombstones = new List<string>();

                foreach (var kvp in _tombstones)
                {
                    if (now >= kvp.Value)
                    {
                        expiredTombstones.Add(kvp.Key);
                    }
                }

                foreach (var path in expiredTombstones)
                {
                    _tombstones.TryRemove(path, out _);

                    // 寬限期過後，再次確認檔案是否真的不存在
                    bool exists = File.Exists(path) || Directory.Exists(path);
                    if (!exists)
                    {
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                            $"Item truly confirmed deleted from desktop after grace period: {path}");
                        // 檔案已被外部真正刪除，保留日誌，並可安全釋放或通知面板
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"Error in ProcessDebouncedEvents: {ex.Message}");
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                foreach (var watcher in _watchers)
                {
                    try
                    {
                        watcher.EnableRaisingEvents = false;
                        watcher.Dispose();
                    }
                    catch { }
                }
                _watchers.Clear();
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
            _debounceTimer.Dispose();
        }
    }
}
