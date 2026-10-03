using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
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
        public event Action? FencesChanged;

        public IReadOnlyList<FenceWindow> Fences => _fences.AsReadOnly();

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
            LoadOrCreateFences();
            SaveState();

            _syncTimer.Start();
            LogMessage?.Invoke($"農場柵欄引擎啟動完成，目前載入 {_fences.Count} 個柵欄，開始定時偵測圖示歸屬。");
        }

        /// <summary>
        /// 完整載入歷史柵欄配置 (支援 0、1、2、多個柵欄，絕不再因少於 2 個而重設)
        /// </summary>
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

            var palette = new[]
            {
                Color.FromRgb(0, 150, 255),  // 經典藍
                Color.FromRgb(40, 200, 100), // 清新綠
                Color.FromRgb(245, 158, 11), // 活力橙
                Color.FromRgb(168, 85, 247), // 典雅紫
                Color.FromRgb(236, 72, 153), // 玫瑰粉
                Color.FromRgb(14, 165, 233)  // 天空藍
            };

            if (savedStates != null)
            {
                // 完整還原歷史儲存的所有柵欄 (即使只有 0 個或 1 個)
                int cIdx = 0;
                foreach (var state in savedStates)
                {
                    CreateFence(state, palette[cIdx % palette.Length]);
                    cIdx++;

                    foreach (var item in state.AssignedItems)
                    {
                        _assignedIcons[item] = state.Id;
                    }
                }
                LogMessage?.Invoke($"已成功自設定檔還原 {savedStates.Count} 個柵欄。");
            }
            else
            {
                // 僅在設定檔完全不存在時，才建立預設 2 個柵欄
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

                CreateFence(f1, palette[0]);
                CreateFence(f2, palette[1]);
                LogMessage?.Invoke("建立預設雙柵欄 (柵欄 A 與柵欄 B)。");
            }
        }

        private void CreateFence(FenceStateData state, Color color)
        {
            var fence = new FenceWindow(state.Id, state.Title, state.Left, state.Top, state.Width, state.Height, color);

            fence.FenceMoved += OnFenceMoved;
            fence.FenceClosed += OnFenceClosed;

            _fences.Add(fence);
            fence.Show();
            FencesChanged?.Invoke();
        }

        /// <summary>
        /// 提供使用者隨時動態新增柵欄
        /// </summary>
        public void CreateNewFence(string? title = null)
        {
            int nextIdx = _fences.Count + 1;
            string id = $"fence_{Guid.NewGuid().ToString("N")[..8]}";
            string fenceTitle = !string.IsNullOrWhiteSpace(title) ? title : $"柵欄 {nextIdx}";

            double left = 140 + (_fences.Count * 40) % 400;
            double top = 160 + (_fences.Count * 30) % 300;

            var state = new FenceStateData
            {
                Id = id,
                Title = fenceTitle,
                Left = left,
                Top = top,
                Width = 320,
                Height = 360
            };

            var palette = new[]
            {
                Color.FromRgb(0, 150, 255),
                Color.FromRgb(40, 200, 100),
                Color.FromRgb(245, 158, 11),
                Color.FromRgb(168, 85, 247),
                Color.FromRgb(236, 72, 153)
            };
            Color color = palette[_fences.Count % palette.Length];

            CreateFence(state, color);
            SaveState();
            LogMessage?.Invoke($"已新增 [{fenceTitle}]，目前共有 {_fences.Count} 個柵欄。");
        }

        /// <summary>
        /// 當使用者拖曳柵欄標題列移動時，帶動柵欄內的原生圖示同步位移，並讀回校驗
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
                        Point newPt = new Point(icon.ScreenPoint.X + deltaX, icon.ScreenPoint.Y + deltaY);
                        bool ok = DesktopInterop.SetIconPositionByScreenPoint(icon.Index, newPt);
                        if (!ok)
                        {
                            LogMessage?.Invoke($"[移動警告] 圖示 '{icon.Name}' 移動未達標，可能受 Windows 自動排列圖示限制。");
                        }
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
            FencesChanged?.Invoke();
            LogMessage?.Invoke($"已取消柵欄 [{fence.FenceTitle}]，內部 {keysToRemove.Count} 個圖示安全解除歸屬，原地保留在桌面。");
        }

        /// <summary>
        /// 定時掃描桌面原生圖示座標與物理邊界命中：
        /// 1. 僅在柵欄「靜止」狀態下進行歸屬判定，避免移動外框掃過圖示時誤判為使用者手動拖入！
        /// 2. 使用 PointToScreen 換算之真實物理像素矩形判定，排除標題列誤觸。
        /// </summary>
        private void OnSyncTimerTick(object? sender, EventArgs e)
        {
            if (_isUpdatingPositions) return;

            // 關鍵防護：任何柵欄正在被使用者拖動時，不執行新圖示歸入判定
            if (_fences.Any(f => f.IsUserMoving)) return;

            try
            {
                var icons = DesktopInterop.GetAllDesktopIcons();
                var fenceCounts = _fences.ToDictionary(f => f.FenceId, f => 0);
                bool hasChanges = false;

                foreach (var icon in icons)
                {
                    string key = !string.IsNullOrEmpty(icon.ResolvedPath) ? icon.ResolvedPath : icon.Name;

                    // 取圖示中心點進行命中判定 (物理像素，圖示標準尺寸約 72x72)
                    Point center = new Point(icon.ScreenPoint.X + 36, icon.ScreenPoint.Y + 36);

                    FenceWindow? targetFence = null;
                    foreach (var fence in _fences)
                    {
                        // 取得工作區物理矩形 (扣除標題列)
                        Rect contentBounds = fence.GetPhysicalContentBounds();
                        if (contentBounds.Contains(center))
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
                            LogMessage?.Invoke($"[手動歸入] 圖示 '{icon.Name}' 進入 [{targetFence.FenceTitle}]，完成納管。");
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

                // 安全原子覆寫
                string tempPath = _stateFilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _stateFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"儲存配置失敗: {ex.Message}");
            }
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
