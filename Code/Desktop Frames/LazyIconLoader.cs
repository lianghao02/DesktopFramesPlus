using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Desktop_Frames
{
    public class IconLoadRequest
    {
        public string FilePath { get; set; }
        public string TargetPath { get; set; }
        public bool IsFolder { get; set; }
        public bool IsLink { get; set; }
        public bool IsShortcut { get; set; }
        public System.Collections.Generic.IDictionary<string, object> IconDict { get; set; }
        public Image TargetImage { get; set; }
        public Action OnLoaded { get; set; }
    }

    public static class LazyIconLoader
    {
        private static readonly ConcurrentQueue<IconLoadRequest> _loadQueue = new ConcurrentQueue<IconLoadRequest>();
        private static readonly object _lifecycleLock = new object();
        private static CancellationTokenSource _cts;
        private static Task _loaderTask;
        private static bool _isRunning = false;

        public static void Start()
        {
            lock (_lifecycleLock)
            {
                if (_isRunning) return;
                _isRunning = true;
                _cts = new CancellationTokenSource();
                _loaderTask = Task.Run(() => ProcessLoadQueue(_cts.Token));
            }
        }

        public static void Stop()
        {
            lock (_lifecycleLock)
            {
                if (!_isRunning) return;
                _isRunning = false;
                try { _cts?.Cancel(); } catch { }
                try { _cts?.Dispose(); } catch { }
                _cts = null;
                _loaderTask = null;
            }
        }

        public static void RequestIcon(IconLoadRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.FilePath) || request.TargetImage == null) return;
            _loadQueue.Enqueue(request);
        }

        private static async Task ProcessLoadQueue(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    int processed = 0;
                    // Batch size of 15 prevents stuttering
                    while (processed < 15 && _loadQueue.TryDequeue(out var request))
                    {
                        if (token.IsCancellationRequested) break;

                        try
                        {
                            ImageSource icon = null;
                            lock (IconManager.IconCache)
                            {
                                if (IconManager.IconCache.TryGetValue(request.FilePath, out var cached))
                                    icon = cached;
                            }

                            if (icon == null)
                            {
                                icon = IconManager.GetIconForFile(request.TargetPath, request.FilePath, request.IsFolder, request.IsLink, request.IsShortcut, request.IconDict);
                                if (icon != null && icon.CanFreeze && !icon.IsFrozen) icon.Freeze();
                            }

                            if (icon != null)
                            {
                                var dispatcher = Application.Current?.Dispatcher;
                                if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                                {
                                    return;
                                }

                                await dispatcher.InvokeAsync(() =>
                                {
                                    request.TargetImage.Source = icon;
                                    request.OnLoaded?.Invoke();
                                }, System.Windows.Threading.DispatcherPriority.Background);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (Exception itemEx)
                        {
                            LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.IconHandling,
                                $"LazyIconLoader: 略過圖示載入失敗項目 '{request?.FilePath}': {itemEx.Message}");
                        }

                        processed++;
                    }

                    await Task.Delay(processed > 0 ? 30 : 100, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception loopEx)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.IconHandling,
                        $"LazyIconLoader 迴圈異常: {loopEx.Message}");
                    try { await Task.Delay(200, token); } catch { break; }
                }
            }
        }
    }
}
