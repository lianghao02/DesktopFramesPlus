using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Desktop_Frames.Notes.Models;
using Desktop_Frames.Notes.Views;
using MessageBox = System.Windows.MessageBox;

namespace Desktop_Frames.Notes.Services
{
    /// <summary>
    /// Manages sticky notes lifecycle, windows, and global actions.
    /// </summary>
    public class NoteManager
    {
        public static NoteManager Instance { get; private set; } = null!;
        public static bool IsApplicationExiting { get; set; } = false;

        private readonly NoteStorageService _storageService = new NoteStorageService();
        private readonly List<NoteItem> _notes = new List<NoteItem>();
        private readonly Dictionary<string, NoteWindow> _windows = new Dictionary<string, NoteWindow>();
        private readonly object _lock = new object();

        public static void Initialize()
        {
            if (Instance == null)
            {
                Instance = new NoteManager();
                Instance.InitInternal();
            }
        }

        private void InitInternal()
        {
            lock (_lock)
            {
                var loadedNotes = _storageService.LoadNotes();
                _notes.Clear();
                _windows.Clear();

                foreach (var note in loadedNotes)
                {
                    _notes.Add(note);
                    EnsureWithinVisibleScreen(note);

                    var window = new NoteWindow(note);
                    window.DataChanged += OnNoteDataChanged;
                    window.RequestDelete += OnNoteRequestDelete;

                    _windows[note.Id] = window;

                    if (note.IsVisible)
                    {
                        window.Show();
                    }
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"NoteManager: Initialized with {_notes.Count} notes ({_windows.Count} windows created).");
            }
        }

        private void EnsureWithinVisibleScreen(NoteItem note)
        {
            try
            {
                bool isVisibleOnAnyScreen = false;
                foreach (var screen in Screen.AllScreens)
                {
                    Rectangle wa = screen.WorkingArea;
                    if (note.X + note.Width > wa.Left + 20 && note.X < wa.Right - 20 &&
                        note.Y + note.Height > wa.Top + 20 && note.Y < wa.Bottom - 20)
                    {
                        isVisibleOnAnyScreen = true;
                        break;
                    }
                }

                if (!isVisibleOnAnyScreen && Screen.PrimaryScreen != null)
                {
                    Rectangle primary = Screen.PrimaryScreen.WorkingArea;
                    note.X = primary.Left + 80;
                    note.Y = primary.Top + 80;
                    note.Width = Math.Min(note.Width, primary.Width - 100);
                    note.Height = Math.Min(note.Height, primary.Height - 100);

                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                        $"NoteManager: Note {note.Id} was off-screen; relocated to primary screen ({note.X}, {note.Y}).");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"NoteManager: Screen bounds check error: {ex.Message}");
            }
        }

        public NoteWindow CreateNewNote()
        {
            lock (_lock)
            {
                Rectangle primary = Screen.PrimaryScreen != null ? Screen.PrimaryScreen.WorkingArea : new Rectangle(0, 0, 1920, 1080);
                double defaultX = primary.Left + (primary.Width - 300) / 2.0;
                double defaultY = primary.Top + (primary.Height - 240) / 2.0;

                // Cascade slightly if multiple notes exist
                int offset = (_notes.Count % 10) * 20;

                var newNote = new NoteItem
                {
                    X = defaultX + offset,
                    Y = defaultY + offset,
                    Width = 300,
                    Height = 240,
                    Color = !string.IsNullOrWhiteSpace(SettingsManager.NoteDefaultColor) ? SettingsManager.NoteDefaultColor : "yellow",
                    FontSize = SettingsManager.NoteDefaultFontSize > 0 ? SettingsManager.NoteDefaultFontSize : 14.0,
                    // 空字串是「系統預設」的有效值；只有 null 才使用舊版 fallback。
                    FontFamily = SettingsManager.NoteDefaultFontFamily ?? "Microsoft JhengHei",
                    AlwaysOnTop = false,
                    IsLocked = false,
                    IsVisible = true,
                    Content = ""
                };

                _notes.Add(newNote);

                var window = new NoteWindow(newNote);
                window.DataChanged += OnNoteDataChanged;
                window.RequestDelete += OnNoteRequestDelete;

                _windows[newNote.Id] = window;
                window.Show();
                window.Activate();

                _storageService.RequestSave(_notes);

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"NoteManager: Created new note {newNote.Id}.");

                return window;
            }
        }

        public void DeleteNote(NoteItem note)
        {
            lock (_lock)
            {
                if (note == null) return;

                if (_windows.TryGetValue(note.Id, out var win))
                {
                    win.DataChanged -= OnNoteDataChanged;
                    win.RequestDelete -= OnNoteRequestDelete;
                    win.CloseForDeletion();
                    _windows.Remove(note.Id);
                }

                _notes.Remove(note);
                _storageService.SaveImmediate(_notes);

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"NoteManager: Permanently deleted note {note.Id}.");
            }
        }

        private void OnNoteRequestDelete(NoteItem note)
        {
            var result = MessageBox.Show(
                "確定要刪除這張便箋嗎？\n刪除後將無法還原。",
                "刪除便箋",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                DeleteNote(note);
            }
        }

        private void OnNoteDataChanged(NoteItem note)
        {
            lock (_lock)
            {
                _storageService.RequestSave(_notes);
            }
        }

        public void ShowAllNotes()
        {
            lock (_lock)
            {
                foreach (var win in _windows.Values)
                {
                    win.ShowNote();
                }
                _storageService.RequestSave(_notes);
            }
        }

        public void HideAllNotes()
        {
            lock (_lock)
            {
                foreach (var win in _windows.Values)
                {
                    win.HideNote();
                }
                _storageService.RequestSave(_notes);
            }
        }

        public void FlushAndCloseAll()
        {
            lock (_lock)
            {
                // 1. Flush any pending changes to disk atomically
                _storageService.Flush();

                // 2. Mark app exiting flag so windows don't alter IsVisible on closing
                IsApplicationExiting = true;

                // 3. Close all open windows
                foreach (var win in _windows.Values)
                {
                    try
                    {
                        win.Close();
                    }
                    catch { }
                }

                _windows.Clear();
                _notes.Clear();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    "NoteManager: FlushAndCloseAll completed successfully.");
            }
        }
    }
}
