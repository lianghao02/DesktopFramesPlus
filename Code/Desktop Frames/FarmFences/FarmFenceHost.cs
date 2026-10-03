using System;
using System.IO;
using Desktop_Frames.Localization;

namespace Desktop_Frames.FarmFences
{
    internal static class FarmFenceHost
    {
        private static FenceManager? _manager;
        private static DateTime _lastWarning = DateTime.MinValue;

        public static void Start()
        {
            if (_manager != null) return;
            FenceText.Resource = key => Strings.Get(key);
            _manager = new FenceManager(Path.Combine(ProfileManager.CurrentProfileDir, "farm-fences.json"));
            _manager.LogMessage += message => LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, message);
            _manager.Warning += message =>
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, message);
                if ((DateTime.UtcNow - _lastWarning).TotalSeconds < 30) return;
                _lastWarning = DateTime.UtcNow;
                TrayManager.Instance?.ShowFarmFenceWarning(message);
            };
            _manager.Initialize();
        }

        public static void Create()
        {
            Start();
            _manager?.CreateNewFence();
        }

        public static void Stop()
        {
            _manager?.Stop();
            _manager = null;
        }
    }
}
