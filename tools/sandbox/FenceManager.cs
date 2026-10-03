using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Text.Json;
using System.Windows.Threading;

namespace FarmFenceSandbox
{
    public class FenceStateData
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public List<string> AssignedItems { get; set; } = new List<string>();
    }

    public class FenceManager
    {
        private readonly List<FenceWindow> _fences = new List<FenceWindow>();
        private readonly Dictionary<string, string> _assignedIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer _syncTimer;
        private readonly string _stateFilePath;
        private bool _isUpdatingPositions = false;

        public event Action<string>? LogMessage;

        public FenceManager()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _stateFilePath = Path.Combine(baseDir, "fence_state.json");

            _syncTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _syncTimer.Tick += OnSyncTimerTick;
        }

        public void Initialize()
        {
            // 載入或建立兩個預設柵欄
            LoadOrCreateFences();
            SaveState();

            _syncTimer.Start();
            LogMessage?.Invoke("農場柵欄引擎已啟動，開始定時同步桌面原生圖示狀態。");
        }

        private void LoadOrCreateFences()
        {
            List<FenceStateData>? savedStates = null;
            if (File.Exists(_stateFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_stateFilePath);
                    savedStates = JsonSerializer.Deserialize<List<FenceStateData>>(json);
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"讀取歷史狀態失敗: {ex.Message}");
                }
            }

            if (savedStates != null && savedStates.Count >= 2)
            {
                // 恢復歷史柵欄
                CreateFence(savedStates[0], Color.FromRgb(0, 150, 255));
                CreateFence(savedStates[1], Color.FromRgb(40, 200, 100));

                foreach (var state in savedStates)
                {
                    foreach (var item in state.AssignedItems)
                    {
                        _assignedIcons[item] = state.Id;
                    }
                }
            }
            else
            {
                // 預設建立兩個柵欄
                var f1 = new FenceStateData
                {
                    Id = "fence_1",
                    Title = "柵欄 A (工作農場)",
                    Left = 120,
                    Top = 150,
                    Width = 320,
                    Height = 380
                };

                var f2 = new FenceStateData
                {
                    Id = "fence_2",
                    Title = "柵欄 B (暫存農場)",
                    Left = 480,
                    Top = 150,
                    Width = 320,
                    Height = 380
                };

                CreateFence(f1, Color.FromRgb(0, 150, 255));
                CreateFence(f2, Color.FromRgb(40, 200, 100));
            }
        }

        private void CreateFence(FenceStateData state, Color color)
        {
            var fence = new FenceWindow(state.Id, state.Title, state.Left, state.Top, state.Width, state.Height, color);

            fence.FenceMoved += OnFenceMoved;
            fence.FenceClosed += OnFenceClosed;

            _fences.Add(fence);
            fence.Show();
        }

        /// <summary>
        /// 當使用者拖曳柵欄標題列移動時，帶動柵欄內的原生圖示同步位移
        /// </summary>
        private void OnFenceMoved(FenceWindow fence, double deltaX, double deltaY)
        {
            if (_isUpdatingPositions) return;
            _isUpdatingPositions = true;

            try
            {
                var icons = DesktopInterop.GetAllDesktopIcons();
                foreach (var icon in icons)
                {
                    string key = !string.IsNullOrEmpty(icon.ResolvedPath) ? icon.ResolvedPath : icon.Name;
                    if (_assignedIcons.TryGetValue(key, out string? assignedFenceId) && assignedFenceId == fence.FenceId)
                    {
                        // 原生圖示同步移動
                        Point newPt = new Point(icon.ScreenPoint.X + deltaX, icon.ScreenPoint.Y + deltaY);
                        DesktopInterop.SetIconPositionByScreenPoint(icon.Index, newPt);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"移動柵欄圖示同步異常: {ex.Message}");
            }
            finally
            {
                _isUpdatingPositions = false;
                SaveState();
            }
        }

        /// <summary>
        /// 當柵欄被關閉/取消時，安全解除內部項目歸屬，圖示保留在原地，絕不刪除檔案！
        /// </summary>
        private void OnFenceClosed(FenceWindow fence)
        {
            var keysToRemove = _assignedIcons.Where(kv => kv.Value == fence.FenceId).Select(kv => kv.Key).ToList();
            foreach (var k in keysToRemove)
            {
                _assignedIcons.Remove(k);
            }

            _fences.Remove(fence);
            SaveState();
            LogMessage?.Invoke($"已取消柵欄 [{fence.FenceTitle}]，內部 {keysToRemove.Count} 個圖示安全解除歸屬，原地保留在桌面。");
        }

        /// <summary>
        /// 定時掃描桌面原生圖示座標，精確判定：
        /// 1. 手動拖入柵欄
        /// 2. 跨柵欄移動
        /// 3. 拖出回到一般桌面
        /// </summary>
        private void OnSyncTimerTick(object? sender, EventArgs e)
        {
            if (_isUpdatingPositions) return;

            try
            {
                var icons = DesktopInterop.GetAllDesktopIcons();
                var fenceCounts = _fences.ToDictionary(f => f.FenceId, f => 0);
                bool hasChanges = false;

                foreach (var icon in icons)
                {
                    string key = !string.IsNullOrEmpty(icon.ResolvedPath) ? icon.ResolvedPath : icon.Name;

                    // 取圖示中心點進行命中判定 (標準圖示約 72x72)
                    Point center = new Point(icon.ScreenPoint.X + 36, icon.ScreenPoint.Y + 36);

                    FenceWindow? targetFence = null;
                    foreach (var fence in _fences)
                    {
                        Rect bounds = fence.GetFenceBounds();
                        if (bounds.Contains(center))
                        {
                            targetFence = fence;
                            break;
                        }
                    }

                    if (targetFence != null)
                    {
                        // 圖示位於某個柵欄內部
                        if (!_assignedIcons.TryGetValue(key, out string? prevFenceId) || prevFenceId != targetFence.FenceId)
                        {
                            _assignedIcons[key] = targetFence.FenceId;
                            hasChanges = true;
                            LogMessage?.Invoke($"[分組變更] 圖示 '{icon.Name}' 歸入 [{targetFence.FenceTitle}]");
                        }
                        fenceCounts[targetFence.FenceId]++;
                    }
                    else
                    {
                        // 圖示不在任何柵欄內部 (拖出柵欄回到一般桌面)
                        if (_assignedIcons.TryGetValue(key, out string? prevFenceId))
                        {
                            _assignedIcons.Remove(key);
                            hasChanges = true;
                            LogMessage?.Invoke($"[拖出解除] 圖示 '{icon.Name}' 已拖出柵欄，回到一般桌面。");
                        }
                    }
                }

                // 更新每個柵欄標題列的計數
                foreach (var fence in _fences)
                {
                    fence.UpdateItemCount(fenceCounts[fence.FenceId]);
                }

                if (hasChanges)
                {
                    SaveState();
                }
            }
            catch (Exception ex)
            {
                // 靜默防護
                Debug.WriteLine($"同步例外: {ex.Message}");
            }
        }

        public void SaveState()
        {
            try
            {
                var list = new List<FenceStateData>();
                foreach (var f in _fences)
                {
                    var data = new FenceStateData
                    {
                        Id = f.FenceId,
                        Title = f.FenceTitle,
                        Left = f.Left,
                        Top = f.Top,
                        Width = f.ActualWidth,
                        Height = f.ActualHeight,
                        AssignedItems = _assignedIcons.Where(kv => kv.Value == f.FenceId).Select(kv => kv.Key).ToList()
                    };
                    list.Add(data);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(list, options);
                File.WriteAllText(_stateFilePath, json);
            }
            catch { }
        }

        public void Stop()
        {
            _syncTimer.Stop();
            SaveState();
            foreach (var f in _fences.ToList())
            {
                f.Close();
            }
        }
    }
}
