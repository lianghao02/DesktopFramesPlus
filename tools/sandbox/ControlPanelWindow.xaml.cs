using System;
using System.Windows;

namespace FarmFenceSandbox
{
    public partial class ControlPanelWindow : Window
    {
        private readonly FenceManager _fenceManager;

        public ControlPanelWindow(FenceManager fenceManager)
        {
            InitializeComponent();
            _fenceManager = fenceManager;

            _fenceManager.LogMessage += OnLogMessage;
        }

        private void OnLogMessage(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                string line = $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
                TxtLogs.AppendText(line);
                TxtLogs.ScrollToEnd();
            });
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLogs.Clear();
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            _fenceManager.Stop();
            Application.Current.Shutdown();
        }

        protected override void OnClosed(EventArgs e)
        {
            _fenceManager.Stop();
            Application.Current.Shutdown();
            base.OnClosed(e);
        }
    }
}
