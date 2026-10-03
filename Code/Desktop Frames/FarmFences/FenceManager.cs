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
        private readonly bool _arrangeDesktop;
        private readonly Dictionary<FenceWindow, Rect> _committedMoveBounds = new();
        private readonly Dictionary<FenceWindow, Rect> _lastAllowed = new();
        private readonly Dictionary<FenceWindow, Task<List<DesktopIconItem>>> _moveSnapshots = new();
        private DesktopDragMonitor? _input;
        private Task<List<DesktopIconItem>>? _pressedSnapshot;
        private Point _pressedPoint;
        private bool _loadFailed, _stopped, _refreshing, _dragBusy, _moving, _overlapBlocked;
        private Task? _initialization;
        private Task<(bool Success, bool Restored)>? _nativeTransaction;
        private Dictionary<string, Point>? _transactionOriginals;
        private Task<List<DesktopIconItem>>? _dropOriginalSnapshot;
        public event Action<string>? LogMessage;
        public event Action? FencesChanged;
        public event Action<string>? Warning;
        public event Action<string>? FenceCancelled;
        public event Action<IReadOnlyList<FenceStateData>>? StateSaved;
        public IReadOnlyList<FenceWindow> Fences => _fences.AsReadOnly();

        public FenceManager(string? statePath = null, bool arrangeDesktop = true)
        {
            _stateFilePath = statePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fence_state.json");
            _arrangeDesktop = arrangeDesktop;
            _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _refresh.Tick += async (_, _) => await RefreshAsync();
        }

        public Task Ready => _initialization ??= InitializeAsync();
        public void Initialize() => _ = Ready;
        private bool Busy => _moving || _dragBusy || _fences.Any(f => f.IsUserMoving);
        private List<LayoutFence> LayoutFences() => _fences.Select(f =>
        {
            var bounds = f.GetPhysicalBounds();
            var content = f.GetPhysicalContentBounds();
            return new LayoutFence(f.FenceId, bounds, new Thickness(content.Left - bounds.Left,
                content.Top - bounds.Top, bounds.Right - content.Right, bounds.Bottom - content.Bottom));
        }).ToList();

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
                            _assigned.Add(item, state.Id);
                            if (state.ItemPositions.TryGetValue(item, out var position)) _positions[item] = new Point(position.X, position.Y);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _loadFailed = true;
                foreach (var window in _fences) window.Close();
                _fences.Clear(); _assigned.Clear(); _positions.Clear();
                Fail("FarmLoadFailed", ex);
            }
            if (_stopped || _loadFailed) return;
            await _dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            if (_stopped) return;
            // 既有重疊先提示；不回復圖示、不排列、不清除或交換歸屬。
            _overlapBlocked = FenceLayout.HasOverlap(LayoutFences());
            if (_overlapBlocked) Warn("FarmOverlap");
            if (!_arrangeDesktop) return; // 設定回歸只操作測試視窗與測試設定檔。
            try
            {
                _input = new DesktopDragMonitor();
                _input.Diagnostic += message => _dispatcher.BeginInvoke(new Action(() => LogMessage?.Invoke(message)));
                _input.Pressed += point => _dispatcher.BeginInvoke(new Action(() => BeginDrag(point)));
                _input.Released += (point, accepted) => _dispatcher.BeginInvoke(new Action(async () => await EndDragAsync(point, accepted)));
                await RefreshAsync();
                if (_stopped) return;
                if (!_overlapBlocked && _fences.Count > 0)
                {
                    _moving = true;
                    try
                    {
                        var actual = await Task.Run(DesktopInterop.GetAllDesktopIcons);
                        var ordered = actual.Select(i =>
                        {
                            var point = _positions.TryGetValue(i.ResolvedPath, out var saved) ? saved : i.ScreenPoint;
                            var bounds = i.BoundsOnScreen;
                            if (!bounds.IsEmpty) bounds.Offset(point.X - i.ScreenPoint.X, point.Y - i.ScreenPoint.Y);
                            return new DesktopIconItem { ResolvedPath = i.ResolvedPath, ScreenPoint = point, BoundsOnScreen = bounds, Spacing = i.Spacing };
                        }).ToList();
                        await ArrangeAsync(ordered, new Dictionary<string, string>(_assigned, StringComparer.OrdinalIgnoreCase), actual);
                    }
                    finally { _moving = false; }
                }
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


        private void CreateFence(FenceStateData state)
        {
            var window = new FenceWindow(state.Id, state.Title, state.Left, state.Top, state.Width, state.Height, Color.FromRgb(75, 165, 175));
            window.CanManipulate = () => !_stopped && !_loadFailed && !_moving && !_dragBusy && !_fences.Any(f => f.IsUserMoving);
            window.FenceClosed += CancelFence;
            window.TitleChanged += _ => SaveState();
            window.ConstrainBounds = (f, requested) =>
            {
                var current = _lastAllowed.TryGetValue(f, out var previous) ? previous : f.GetPhysicalBounds();
                var obstacles = _fences.Where(g => g != f).Select(g => g.GetPhysicalBounds()).ToArray();
                var areas = DesktopInterop.GetWorkAreas();
                Rect allowed;
                if (_overlapBlocked)
                {
                    // 明確手動移開舊重疊外框：只能減少既有重疊，不能侵入新的柵欄。
                    double Intersection(Rect a, Rect b) { a.Intersect(b); return a.IsEmpty ? 0 : a.Width * a.Height; }
                    allowed = areas.Any(a => a.Contains(requested)) && obstacles.All(o => Intersection(requested, o) <= Intersection(current, o)) ? requested : current;
                }
                else allowed = FenceLayout.Constrain(current, requested, obstacles, areas);
                _lastAllowed[f] = allowed;
                return allowed;
            };
            window.MoveStarted += f =>
            {
                _committedMoveBounds[f] = f.GetPhysicalBounds();
                _lastAllowed[f] = f.GetPhysicalBounds();
                _moveSnapshots[f] = Task.Run(DesktopInterop.GetAllDesktopIcons);
                _pressedSnapshot = null;
            };
            window.MoveCompleted += async f => await CompleteGeometryAsync(f);
            _fences.Add(window);
            window.Show();
            FencesChanged?.Invoke();
        }

        public void CreateNewFence(string? title = null) => _ = CreateNewFenceAsync(title);
        public async Task<bool> BindPanelAsync(string panelId, string title, Rect bounds)
        {
            await Ready;
            if (_stopped || _loadFailed || Busy || _overlapBlocked || _fences.Any(f => f.FenceId == panelId)) return false;
            if (_fences.Any(f => FenceLayout.Overlaps(f.GetPhysicalBounds(), bounds)) || !DesktopInterop.GetWorkAreas().Any(a => a.Contains(bounds)))
            { Warn("FarmNoSpace"); return false; }
            _moving = true;
            FenceWindow? added = null;
            try
            {
                var before = _arrangeDesktop ? await Task.Run(DesktopInterop.GetAllDesktopIcons) : new List<DesktopIconItem>();
                if (_stopped) return false;
                CreateFence(new FenceStateData { Id = panelId, Title = title, Left = bounds.X, Top = bounds.Y,
                    Width = Math.Max(160, bounds.Width), Height = Math.Max(120, bounds.Height) });
                added = _fences.Last();
                await _dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                if (_stopped) return false;
                if (_arrangeDesktop && !await ArrangeAsync(before, new Dictionary<string, string>(_assigned, StringComparer.OrdinalIgnoreCase), before, new HashSet<string> { panelId }))
                { _fences.Remove(added); added.Close(); return false; }
                SaveState(); return true;
            }
            catch (Exception ex)
            { if (added != null) { _fences.Remove(added); added.Close(); } Fail("FarmOperationFailed", ex); return false; }
            finally { _moving = false; }
        }
        public async Task CreateNewFenceAsync(string? title = null, string? id = null)
        {
            await Ready;
            if (_loadFailed || _stopped || Busy) return;
            if (id != null && _fences.Any(f => f.FenceId == id)) return;
            if (_overlapBlocked) { Warn("FarmOverlap"); return; }
            var areas = DesktopInterop.GetWorkAreas();
            Rect? available = null;
            foreach (var area in areas.OrderBy(a => a.Left).ThenBy(a => a.Top))
            {
                for (double y = area.Top; y + 280 <= area.Bottom && available == null; y += 16)
                    for (double x = area.Left; x + 320 <= area.Right; x += 16)
                    {
                        var candidate = new Rect(x, y, 320, 280);
                        if (_fences.Any(f => FenceLayout.Overlaps(candidate, f.GetPhysicalBounds()))) continue;
                        available = candidate; break;
                    }
                if (available != null) break;
            }
            if (available == null) { Warn("FarmNoSpace"); return; }
            _moving = true;
            FenceWindow? added = null;
            try
            {
                var before = _arrangeDesktop ? await Task.Run(DesktopInterop.GetAllDesktopIcons) : new List<DesktopIconItem>();
                if (_stopped) return;
                var r = available.Value;
                CreateFence(new FenceStateData { Id = id ?? Guid.NewGuid().ToString("N"), Title = string.IsNullOrWhiteSpace(title) ? FenceText.Get("FarmName", _fences.Count + 1) : title.Trim(),
                    Left = r.Left, Top = r.Top, Width = r.Width, Height = r.Height });
                added = _fences.Last();
                await _dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                if (_stopped) return;
                if (_arrangeDesktop && !await ArrangeAsync(before, new Dictionary<string, string>(_assigned, StringComparer.OrdinalIgnoreCase), before, new HashSet<string> { added.FenceId }))
                { _fences.Remove(added); added.Close(); FencesChanged?.Invoke(); }
                if (!_stopped) SaveState();
            }
            catch (Exception ex)
            {
                if (added != null) { _fences.Remove(added); added.Close(); FencesChanged?.Invoke(); }
                Fail("FarmOperationFailed", ex);
            }
            finally { _moving = false; }
        }

        internal void CancelFence(FenceWindow window)
        {
            foreach (var key in _assigned.Where(p => p.Value == window.FenceId).Select(p => p.Key).ToArray()) { _assigned.Remove(key); _positions.Remove(key); }
            _fences.Remove(window);
            _committedMoveBounds.Remove(window); _lastAllowed.Remove(window); _moveSnapshots.Remove(window);
            _overlapBlocked = FenceLayout.HasOverlap(LayoutFences());
            SaveState();
            FencesChanged?.Invoke();
            // 取消只解除歸屬，不移動原生圖示；下一次排列將它們視為框外項目。
            FenceCancelled?.Invoke(window.FenceId);
        }

        private async Task CompleteGeometryAsync(FenceWindow window)
        {
            if (_stopped || !_moveSnapshots.Remove(window, out var snapshot)) return;
            _moving = true;
            bool success = false;
            try
            {
                _overlapBlocked = FenceLayout.HasOverlap(LayoutFences());
                if (_overlapBlocked) { Warn("FarmOverlap"); success = true; return; }
                var before = await snapshot;
                if (_stopped) return;
                success = await ArrangeAsync(before, new Dictionary<string, string>(_assigned, StringComparer.OrdinalIgnoreCase), before, new HashSet<string> { window.FenceId });
            }
            catch (Exception ex) { Fail("FarmOperationFailed", ex); }
            finally
            {
                if (!_stopped && !success && _committedMoveBounds.TryGetValue(window, out var original)) window.SetPhysicalBounds(original);
                _committedMoveBounds.Remove(window); _lastAllowed.Remove(window);
                _moving = false;
                if (!_stopped) SaveState();
            }
        }

        private void BeginDrag(Point point)
        {
            if (_stopped || _loadFailed || Busy || _overlapBlocked || _fences.Count == 0) return;
            _pressedPoint = point;
            _pressedSnapshot = Task.Run(DesktopInterop.GetAllDesktopIcons);
        }

        private async Task EndDragAsync(Point point, bool accepted)
        {
            var beforeTask = _pressedSnapshot;
            var pressedPoint = _pressedPoint;
            _pressedSnapshot = null;
            if (!accepted || beforeTask == null || _stopped || Busy) return;
            _dragBusy = true;
            _dropOriginalSnapshot = beforeTask;
            try
            {
                var before = await beforeTask;
                var source = before.SingleOrDefault(i => !i.BoundsOnScreen.IsEmpty && i.BoundsOnScreen.Contains(pressedPoint));
                if (source == null) return;
                await Task.Delay(180);
                var after = await Task.Run(DesktopInterop.GetAllDesktopIcons);
                await Task.Delay(100);
                var confirmed = await Task.Run(DesktopInterop.GetAllDesktopIcons);
                if (_stopped || _fences.Any(f => f.IsUserMoving)) return;
                var sourceAfter = after.SingleOrDefault(i => Same(i.ResolvedPath, source.ResolvedPath));
                var sourceConfirm = confirmed.SingleOrDefault(i => Same(i.ResolvedPath, source.ResolvedPath));
                if (sourceAfter == null || sourceConfirm == null || sourceAfter.ScreenPoint != sourceConfirm.ScreenPoint ||
                    sourceAfter.ScreenPoint == source.ScreenPoint || !sourceAfter.IsSelected) return;
                var delta = sourceAfter.ScreenPoint - source.ScreenPoint;
                var proposed = new Dictionary<string, string>(_assigned, StringComparer.OrdinalIgnoreCase);
                var dragged = new List<DesktopIconItem>();
                foreach (var item in after.Where(i => i.IsSelected && !i.BoundsOnScreen.IsEmpty))
                {
                    var old = before.SingleOrDefault(i => Same(i.ResolvedPath, item.ResolvedPath));
                    var verify = confirmed.SingleOrDefault(i => Same(i.ResolvedPath, item.ResolvedPath));
                    if (old == null || verify == null || verify.ScreenPoint != item.ScreenPoint ||
                        Math.Abs(item.ScreenPoint.X - old.ScreenPoint.X - delta.X) > 2 ||
                        Math.Abs(item.ScreenPoint.Y - old.ScreenPoint.Y - delta.Y) > 2) continue;
                    dragged.Add(item);
                    proposed.TryGetValue(item.ResolvedPath, out var previous);
                    var owner = FenceLayout.DropOwner(item.BoundsOnScreen, LayoutFences(), previous);
                    if (owner != null) proposed[item.ResolvedPath] = owner;
                    else proposed.Remove(item.ResolvedPath);
                }
                if (dragged.Count == 0) return;
                // 只回復本次真正移動的選取項目；其他桌面項目以目前讀回位置為回復基準。
                var originals = after.Select(i => dragged.Any(d => Same(d.ResolvedPath, i.ResolvedPath)) ? before.Single(b => Same(b.ResolvedPath, i.ResolvedPath)) : i).ToList();
                var targets = dragged.Where(i => proposed.ContainsKey(i.ResolvedPath)).Select(i => proposed[i.ResolvedPath]).ToHashSet(StringComparer.Ordinal);
                if (await ArrangeAsync(after, proposed, originals, targets)) { SaveState(); LogMessage?.Invoke("原生拖曳與分區排列已完成，歸屬已提交。"); }
            }
            catch (Exception ex) { Fail("FarmOperationFailed", ex); }
            finally { _dragBusy = false; _dropOriginalSnapshot = null; }
        }

        private async Task<bool> ArrangeAsync(List<DesktopIconItem> icons, Dictionary<string, string> proposed, List<DesktopIconItem> originals, IReadOnlySet<string>? growable = null)
        {
            if (_stopped) return false;
            LayoutPlan plan;
            var originalPositions = originals.ToDictionary(i => i.ResolvedPath, i => i.ScreenPoint, StringComparer.OrdinalIgnoreCase);
            try { plan = FenceLayout.Plan(LayoutFences(), icons, proposed, DesktopInterop.GetWorkAreas(), growable); }
            catch (InvalidOperationException ex)
            {
                // 容量預檢失敗時尚未執行排列；仍須回復 Explorer 已完成的圖示 Drop。
                await MovePositionsAsync(originalPositions, originalPositions, true);
                if (!_stopped) Warn(ex.Message);
                return false;
            }
            var plannedFences = LayoutFences().Select(f => f with { Bounds = plan.Fences[f.Id] }).ToArray();
            bool success = await MovePositionsAsync(plan.Positions, originalPositions, verify: () =>
            {
                // Explorer 對齊／自動排列可能延後拉回；整批完成後再驗證位置及完整範圍。
                Thread.Sleep(100);
                var actual = DesktopInterop.GetAllDesktopIcons();
                if (actual.Count != plan.Positions.Count) return false;
                foreach (var item in actual)
                {
                    if (!plan.Positions.TryGetValue(item.ResolvedPath, out var expected) ||
                        Math.Abs(item.ScreenPoint.X - expected.X) > 2 || Math.Abs(item.ScreenPoint.Y - expected.Y) > 2 || item.BoundsOnScreen.IsEmpty) return false;
                    if (proposed.TryGetValue(item.ResolvedPath, out var owner))
                    {
                        if (!plannedFences.Single(f => f.Id == owner).Content.Contains(item.BoundsOnScreen)) return false;
                    }
                    else if (plannedFences.Any(f => FenceLayout.Overlaps(f.Bounds, item.BoundsOnScreen))) return false;
                }
                return actual.SelectMany((i, index) => actual.Skip(index + 1).Select(j => FenceLayout.Overlaps(i.BoundsOnScreen, j.BoundsOnScreen))).All(b => !b);
            });
            if (!success || _stopped) return false;
            foreach (var f in _fences) f.SetPhysicalBounds(plan.Fences[f.FenceId]);
            _assigned.Clear();
            foreach (var pair in proposed) _assigned.Add(pair.Key, pair.Value);
            _positions.Clear();
            foreach (var pair in plan.Positions.Where(p => proposed.ContainsKey(p.Key))) _positions.Add(pair.Key, pair.Value);
            return true;
        }

        private async Task<bool> MovePositionsAsync(Dictionary<string, Point> requested, Dictionary<string, Point> originals, bool force = false, Func<bool>? verify = null)
        {
            _transactionOriginals = originals;
            _nativeTransaction = Task.Run(() =>
            {
                bool success = LayoutTransaction.Apply(requested, originals,
                    DesktopInterop.MoveShellItem, _lifetime.Token, out bool restored, force, verify);
                return (success, restored);
            });
            var result = await _nativeTransaction;
            if (_stopped) return false;
            _transactionOriginals = null;
            if (!result.Restored) { _loadFailed = true; if (!_stopped) Warn("FarmRestoreFailed"); }
            else if (!result.Success && !_stopped) Warn("FarmOperationFailed");
            return result.Success;
        }

        private async Task RefreshAsync()
        {
            if (_refreshing || Busy || _stopped || _overlapBlocked) return;
            _refreshing = true;
            try
            {
                var icons = await Task.Run(DesktopInterop.GetAllDesktopIcons);
                var hwnd = await Task.Run(DesktopInterop.GetDesktopListViewHwnd);
                if (_stopped || Busy) return;
                if (_input != null) _input.DesktopWindow = hwnd;
                var keys = icons.Select(i => i.ResolvedPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                bool changed = false;
                foreach (var key in _assigned.Keys.Where(k => !keys.Contains(k)).ToArray()) { changed |= _assigned.Remove(key); _positions.Remove(key); }
                if (changed) SaveState();
            }
            catch (Exception ex) { LogMessage?.Invoke($"Explorer 尚未就緒，保留歸屬並稍後重試：{ex.Message}"); }
            finally { _refreshing = false; }
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        private void Warn(string key) { LogMessage?.Invoke(key); Warning?.Invoke(FenceText.Get(key)); }
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
                StateSaved?.Invoke(list);
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
            // 背景位置交易不呼叫 Dispatcher；可等它回復完畢，不造成 UI 互鎖。
            var result = _nativeTransaction?.GetAwaiter().GetResult();
            if (result is { Restored: false }) _loadFailed = true;
            if (_transactionOriginals != null)
            {
                var originals = _transactionOriginals;
                bool restored = Task.Run(() => LayoutTransaction.Apply(originals, originals, DesktopInterop.MoveShellItem,
                    CancellationToken.None, out _, true)).GetAwaiter().GetResult();
                if (!restored) _loadFailed = true;
                _transactionOriginals = null;
            }
            else if (_dragBusy && _dropOriginalSnapshot != null)
            {
                // 在等待 Explorer Drop 穩定時退出，仍回復按下滑鼠前的原生位置。
                try
                {
                    var originals = _dropOriginalSnapshot.GetAwaiter().GetResult().ToDictionary(i => i.ResolvedPath, i => i.ScreenPoint, StringComparer.OrdinalIgnoreCase);
                    bool restored = Task.Run(() => LayoutTransaction.Apply(originals, originals, DesktopInterop.MoveShellItem,
                        CancellationToken.None, out _, true)).GetAwaiter().GetResult();
                    if (!restored) _loadFailed = true;
                }
                catch (Exception ex) { _loadFailed = true; Fail("FarmRestoreFailed", ex); }
            }
            SaveState();
            foreach (var window in _fences) window.Close();
            _fences.Clear();
        }
    }
}
