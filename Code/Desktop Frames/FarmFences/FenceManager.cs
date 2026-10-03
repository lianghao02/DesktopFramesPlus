using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Desktop_Frames.FarmFences
{
    public sealed class FenceStateData
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public List<string> AssignedItems { get; set; } = new();
        public Dictionary<string, ItemPlacement> ItemPositions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ItemPlacement
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public sealed class FenceManager
    {
        private readonly List<FenceWindow> _fences = new();
        private readonly Dictionary<string, string> _assigned = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Point> _positions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly DispatcherTimer _refresh;
        private readonly string _stateFilePath;
        private readonly CancellationTokenSource _lifetime = new();
        private DesktopDragMonitor? _input;
        private Task<List<DesktopIconItem>>? _pressedSnapshot;
        private Point _pressedPoint;
        private bool _loadFailed, _stopped, _refreshing, _dragBusy, _moving;
        private Task? _initialization;
        private readonly Dictionary<FenceWindow, Rect> _moveOrigins = new();
        private readonly Dictionary<FenceWindow, Rect> _committedMoveBounds = new();
        private readonly Dictionary<FenceWindow, Task<List<DesktopIconItem>>> _moveSnapshots = new();
        private readonly HashSet<FenceWindow> _failedMoves = new();
        private readonly Dictionary<FenceWindow, Point> _pendingMoves = new();
        public event Action<string>? LogMessage;
        public event Action? FencesChanged;
        public event Action<string>? Warning;
        public IReadOnlyList<FenceWindow> Fences => _fences.AsReadOnly();

        public FenceManager(string? statePath = null)
        {
            _stateFilePath = statePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fence_state.json");
            _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _refresh.Tick += async (_, _) => await RefreshAsync();
        }

        public Task Ready => _initialization ??= InitializeAsync();
        public void Initialize() => _ = Ready;

        private async Task InitializeAsync()
        {
            try
            {
                if (File.Exists(_stateFilePath))
                {
                    var states = JsonSerializer.Deserialize<List<FenceStateData>>(File.ReadAllText(_stateFilePath, Encoding.UTF8))
                        ?? throw new InvalidDataException("設定必須是陣列");
                    ValidateStates(states);
                    foreach (var state in states)
                    {
                        CreateFence(state);
                        foreach (var item in state.AssignedItems)
                        {
                            if (!_assigned.TryAdd(item, state.Id)) throw new InvalidDataException("同一 Shell 項目不能同時屬於兩個柵欄");
                            if (state.ItemPositions.TryGetValue(item, out var position)) _positions[item] = new Point(position.X, position.Y);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _loadFailed = true;
                foreach (var window in _fences) window.Close();
                _fences.Clear();
                _assigned.Clear();
                _positions.Clear();
                Fail("FarmLoadFailed", ex);
            }
            if (_stopped) return;
            try
            {
                _input = new DesktopDragMonitor();
                _input.Diagnostic += message => _dispatcher.BeginInvoke(new Action(() => LogMessage?.Invoke(message)));
                // Hook callback 只排入 UI 佇列，不在低階 Hook 中做 COM 或磁碟作業。
                _input.Pressed += point => _dispatcher.BeginInvoke(new Action(() => BeginDrag(point)));
                _input.Released += (point, accepted) => _dispatcher.BeginInvoke(new Action(async () => await EndDragAsync(point, accepted)));
                await RefreshAsync();
                // 只恢復已保存的明確歸屬，不依柵欄範圍收集桌面圖示。
                var restore = _positions.ToArray();
                await Task.Run(() =>
                {
                    foreach (var position in restore)
                    {
                        _lifetime.Token.ThrowIfCancellationRequested();
                        if (!DesktopInterop.MoveShellItem(position.Key, position.Value))
                            throw new InvalidOperationException("保存的圖示位置無法恢復");
                    }
                });
                LogMessage?.Invoke($"桌面監聽已啟動，HWND={_input.DesktopWindow}，柵欄={_fences.Count}");
            }
            catch (Exception ex) { Fail("FarmOperationFailed", ex); }
            finally { if (!_stopped && _input != null) _refresh.Start(); }
        }

        public static void ValidateStates(List<FenceStateData> states)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in states)
            {
                if (s == null || string.IsNullOrWhiteSpace(s.Id) || !ids.Add(s.Id) ||
                    string.IsNullOrWhiteSpace(s.Title) || !double.IsFinite(s.Left) || !double.IsFinite(s.Top) ||
                    !double.IsFinite(s.Width) || !double.IsFinite(s.Height) || s.Width < 160 || s.Height < 120 ||
                    s.AssignedItems == null || s.AssignedItems.Any(k => string.IsNullOrWhiteSpace(k) || !items.Add(k)) ||
                    s.ItemPositions == null || s.ItemPositions.Any(p => p.Value == null || !s.AssignedItems.Contains(p.Key, StringComparer.OrdinalIgnoreCase) ||
                        !double.IsFinite(p.Value.X) || !double.IsFinite(p.Value.Y)))
                    throw new InvalidDataException("農場柵欄設定含無效值或重複歸屬");
            }
        }

        private void CreateFence(FenceStateData s)
        {
            var window = new FenceWindow(s.Id, s.Title, s.Left, s.Top, s.Width, s.Height, Color.FromRgb(75, 165, 175));
            window.FenceClosed += CancelFence;
            window.TitleChanged += _ => SaveState();
            window.MoveStarted += f =>
            {
                _moveOrigins[f] = f.GetPhysicalBounds();
                _committedMoveBounds[f] = _moveOrigins[f];
                var members = _assigned.Where(p => p.Value == f.FenceId).Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                _moveSnapshots[f] = Task.Run(() => DesktopInterop.GetAllDesktopIcons().Where(i => members.Contains(i.ResolvedPath)).ToList());
                _pressedSnapshot = null;
            };
            window.FenceMoved += (f, x, y) =>
            {
                if (_failedMoves.Contains(f) || !_moveOrigins.TryGetValue(f, out var origin)) return;
                var bounds = f.GetPhysicalBounds();
                _pendingMoves[f] = new Point(bounds.X - origin.X, bounds.Y - origin.Y);
                if (!_moving) _ = DrainMovesAsync();
            };
            window.MoveCompleted += async f =>
            {
                while (_moving && !_stopped) await Task.Delay(20);
                if (!_stopped)
                {
                    if (_failedMoves.Remove(f) && _moveOrigins.TryGetValue(f, out var origin)) f.SetPhysicalBounds(origin);
                    _moveOrigins.Remove(f);
                    _committedMoveBounds.Remove(f);
                    _moveSnapshots.Remove(f);
                    SaveState();
                }
            };
            _fences.Add(window);
            window.Show();
            FencesChanged?.Invoke();
        }

        public void CreateNewFence(string? title = null)
        {
            if (_loadFailed || _stopped) return;
            var area = SystemParameters.WorkArea;
            CreateFence(new FenceStateData
            {
                Id = Guid.NewGuid().ToString("N"), Title = string.IsNullOrWhiteSpace(title) ? FenceText.Get("FarmName", _fences.Count + 1) : title.Trim(),
                Left = area.Left + 240 + (_fences.Count % 5) * 30, Top = area.Top + 120 + (_fences.Count % 5) * 30,
                Width = 320, Height = 280
            });
            SaveState();
        }

        private void CancelFence(FenceWindow window)
        {
            _pendingMoves.Remove(window);
            _moveOrigins.Remove(window);
            _committedMoveBounds.Remove(window);
            _moveSnapshots.Remove(window);
            _failedMoves.Remove(window);
            foreach (var key in _assigned.Where(p => p.Value == window.FenceId).Select(p => p.Key).ToArray()) { _assigned.Remove(key); _positions.Remove(key); }
            _fences.Remove(window);
            SaveState();
            FencesChanged?.Invoke();
        }

        private void BeginDrag(Point point)
        {
            if (_stopped || _loadFailed || _moving || _dragBusy || _fences.Any(f => f.IsUserMoving)) return;
            _pressedPoint = point;
            _pressedSnapshot = Task.Run(DesktopInterop.GetAllDesktopIcons);
        }

        private async Task EndDragAsync(Point point, bool accepted)
        {
            var beforeTask = _pressedSnapshot;
            var pressedPoint = _pressedPoint;
            _pressedSnapshot = null;
            if (!accepted || beforeTask == null || _stopped || _moving || _dragBusy) return;
            _dragBusy = true;
            try
            {
                var before = await beforeTask;
                LogMessage?.Invoke($"拖曳來源快照：{before.Count} 項，按下={pressedPoint}");
                var source = before.SingleOrDefault(i => !i.BoundsOnScreen.IsEmpty && i.BoundsOnScreen.Contains(pressedPoint));
                if (source == null) { LogMessage?.Invoke("來源未命中原生圖示範圍，不變更歸屬。"); return; }
                // 等 Explorer 完成原生 Drop 並讀回；兩次結果一致才提交。
                await Task.Delay(180);
                var after = await Task.Run(DesktopInterop.GetAllDesktopIcons);
                await Task.Delay(100);
                var confirmed = await Task.Run(DesktopInterop.GetAllDesktopIcons);
                if (_stopped || _moving || _fences.Any(f => f.IsUserMoving)) return;
                var sourceAfter = after.SingleOrDefault(i => Same(i.ResolvedPath, source.ResolvedPath));
                var sourceConfirm = confirmed.SingleOrDefault(i => Same(i.ResolvedPath, source.ResolvedPath));
                LogMessage?.Invoke($"來源={source.Name}，原座標={source.ScreenPoint}，新座標={sourceAfter?.ScreenPoint}，selected={sourceAfter?.IsSelected}");
                if (sourceAfter == null || sourceConfirm == null || sourceAfter.ScreenPoint != sourceConfirm.ScreenPoint ||
                    sourceAfter.ScreenPoint == source.ScreenPoint || !sourceAfter.IsSelected) return;
                Point delta = new Point(sourceAfter.ScreenPoint.X - source.ScreenPoint.X, sourceAfter.ScreenPoint.Y - source.ScreenPoint.Y);
                bool changed = false;
                foreach (var item in after.Where(i => i.IsSelected && !i.BoundsOnScreen.IsEmpty))
                {
                    var old = before.SingleOrDefault(i => Same(i.ResolvedPath, item.ResolvedPath));
                    var verify = confirmed.SingleOrDefault(i => Same(i.ResolvedPath, item.ResolvedPath));
                    if (old == null || verify == null || verify.ScreenPoint != item.ScreenPoint ||
                        Math.Abs(item.ScreenPoint.X - old.ScreenPoint.X - delta.X) > 2 ||
                        Math.Abs(item.ScreenPoint.Y - old.ScreenPoint.Y - delta.Y) > 2) continue;
                    var center = new Point(item.BoundsOnScreen.X + item.BoundsOnScreen.Width / 2, item.BoundsOnScreen.Y + item.BoundsOnScreen.Height / 2);
                    // 重疊時最小工作區優先；同面積以固定 Id 決定，與清單插入順序無關。
                    var target = _fences.Where(f => f.GetPhysicalContentBounds().Contains(center))
                        .OrderBy(f => f.GetPhysicalContentBounds().Width * f.GetPhysicalContentBounds().Height)
                        .ThenBy(f => f.FenceId, StringComparer.Ordinal).FirstOrDefault();
                    if (target == null) { changed |= _assigned.Remove(item.ResolvedPath); _positions.Remove(item.ResolvedPath); }
                    else if (!_assigned.TryGetValue(item.ResolvedPath, out var id) || id != target.FenceId)
                    {
                        _assigned[item.ResolvedPath] = target.FenceId;
                        changed = true;
                    }
                    if (target != null) { _positions[item.ResolvedPath] = item.ScreenPoint; changed = true; }
                }
                if (changed) { SaveState(); LogMessage?.Invoke("原生桌面拖曳完成，已提交單一歸屬。"); }
            }
            catch (Exception ex) { Fail("FarmOperationFailed", ex); }
            finally { _dragBusy = false; }
        }

        private async Task DrainMovesAsync()
        {
            _moving = true;
            try
            {
                while (_pendingMoves.Count > 0 && !_stopped)
                {
                    var pair = _pendingMoves.First();
                    _pendingMoves.Remove(pair.Key);
                    if (!_moveSnapshots.TryGetValue(pair.Key, out var snapshot)) continue;
                    var baseline = await snapshot;
                    bool success = await Task.Run(() =>
                    {
                        bool moved = true;
                        try
                        {
                        foreach (var icon in baseline)
                        {
                            _lifetime.Token.ThrowIfCancellationRequested();
                            var target = new Point(icon.ScreenPoint.X + pair.Value.X, icon.ScreenPoint.Y + pair.Value.Y);
                            if (!DesktopInterop.MoveShellItem(icon.ResolvedPath, target))
                            {
                                moved = false;
                                break;
                            }
                        }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception) { moved = false; }
                        if (!moved)
                            foreach (var original in baseline)
                                // 每筆回復都獨立嘗試；整體失敗由 UI 警示，不更新歸屬。
                                try { DesktopInterop.MoveShellItem(original.ResolvedPath, original.ScreenPoint); }
                                catch (Exception) { }
                        return moved;
                    });
                    if (!success && !_stopped)
                    {
                        _failedMoves.Add(pair.Key);
                        _pendingMoves.Remove(pair.Key);
                        // 回到本次開始位置；所有原圖示仍可在原生桌面操作。
                        if (_moveOrigins.TryGetValue(pair.Key, out var origin)) pair.Key.SetPhysicalBounds(origin);
                        Fail("FarmOperationFailed", new InvalidOperationException("Shell 位置讀回不符，可能啟用自動排列或對齊格線"));
                    }
                    if (!_stopped)
                    {
                        foreach (var original in baseline)
                            _positions[original.ResolvedPath] = success ? new Point(original.ScreenPoint.X + pair.Value.X, original.ScreenPoint.Y + pair.Value.Y) : original.ScreenPoint;
                        if (_moveOrigins.TryGetValue(pair.Key, out var start))
                            _committedMoveBounds[pair.Key] = success ? new Rect(start.X + pair.Value.X, start.Y + pair.Value.Y, start.Width, start.Height) : start;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!_stopped) Fail("FarmOperationFailed", ex); }
            finally { _moving = false; }
        }

        private async Task RefreshAsync()
        {
            if (_refreshing || _moving || _dragBusy || _stopped) return;
            _refreshing = true;
            try
            {
                var icons = await Task.Run(DesktopInterop.GetAllDesktopIcons);
                var hwnd = await Task.Run(DesktopInterop.GetDesktopListViewHwnd);
                if (_stopped) return;
                if (_input != null) _input.DesktopWindow = hwnd;
                var keys = icons.Select(i => i.ResolvedPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                bool changed = false;
                // 改名／搬離／刪除會解除失效身分；不以顯示名稱猜測替代項目。
                foreach (var key in _assigned.Keys.Where(k => !keys.Contains(k)).ToArray()) { changed |= _assigned.Remove(key); _positions.Remove(key); }
                if (changed) SaveState();
            }
            catch (Exception ex) { LogMessage?.Invoke($"Explorer 尚未就緒，保留歸屬並稍後重試：{ex.Message}"); }
            finally { _refreshing = false; }
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        private void Fail(string key, Exception ex) { LogMessage?.Invoke(ex.ToString()); Warning?.Invoke(FenceText.Get(key)); }

        public void SaveState()
        {
            if (_loadFailed) return;
            try
            {
                var list = _fences.Select(f =>
                {
                    // 移動尚未完成時，只保存最近已讀回成功的外框與圖示位置。
                    var r = _committedMoveBounds.TryGetValue(f, out var committed) ? committed : f.GetPhysicalBounds();
                    return new FenceStateData { Id = f.FenceId, Title = f.FenceTitle, Left = r.X, Top = r.Y, Width = r.Width, Height = r.Height,
                        AssignedItems = _assigned.Where(p => p.Value == f.FenceId).Select(p => p.Key).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(),
                        ItemPositions = _positions.Where(p => _assigned.TryGetValue(p.Key, out var id) && id == f.FenceId)
                            .ToDictionary(p => p.Key, p => new ItemPlacement { X = p.Value.X, Y = p.Value.Y }, StringComparer.OrdinalIgnoreCase) };
                }).ToList();
                ValidateStates(list);
                Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath)!);
                string temporary = _stateFilePath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                File.Move(temporary, _stateFilePath, true);
            }
            catch (Exception ex) { Fail("FarmSaveFailed", ex); }
        }

        public void Stop()
        {
            if (_stopped) return;
            _stopped = true;
            _lifetime.Cancel();
            _refresh.Stop();
            _input?.Dispose();
            SaveState();
            foreach (var window in _fences) window.Close();
            _fences.Clear();
        }
    }
}
