using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Threading;
using Desktop_Frames.Notes.Models;
using Newtonsoft.Json;

namespace Desktop_Frames.Notes.Services
{
    /// <summary>
    /// Handles persistent storage of notes with debouncing and atomic file replacement.
    /// Strictly respects Portable mode by only saving inside the active profile directory.
    /// </summary>
    public class NoteStorageService
    {
        private const string NOTES_FILE_NAME = "notes.json";
        private readonly DispatcherTimer _debounceTimer;
        private List<NoteItem>? _pendingNotesToSave;
        private readonly object _lock = new object();

        public NoteStorageService()
        {
            _debounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _debounceTimer.Tick += OnDebounceTimerTick;
        }

        public string? GetNotesFilePath()
        {
            try
            {
                string profileDir = ProfileManager.CurrentProfileDir;
                if (!string.IsNullOrEmpty(profileDir))
                {
                    if (!Directory.Exists(profileDir))
                    {
                        Directory.CreateDirectory(profileDir);
                    }
                    return Path.Combine(profileDir, NOTES_FILE_NAME);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"NoteStorageService: Failed to resolve profile directory: {ex.Message}");
            }

            return null;
        }

        public List<NoteItem> LoadNotes()
        {
            lock (_lock)
            {
            string? filePath = GetNotesFilePath();
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    return new List<NoteItem>();
                }

                try
                {
                    string json = File.ReadAllText(filePath, Encoding.UTF8);
                    var notes = JsonConvert.DeserializeObject<List<NoteItem>>(json);
                    return notes ?? new List<NoteItem>();
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                        $"NoteStorageService: Error loading notes from {filePath}: {ex.Message}");
                    return new List<NoteItem>();
                }
            }
        }

        public void RequestSave(List<NoteItem> notes)
        {
            lock (_lock)
            {
                _pendingNotesToSave = new List<NoteItem>(notes);
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }

        private void OnDebounceTimerTick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();
            Flush();
        }

        public void Flush()
        {
            lock (_lock)
            {
                _debounceTimer.Stop();
                if (_pendingNotesToSave == null) return;

                SaveImmediateInternal(_pendingNotesToSave);
                _pendingNotesToSave = null;
            }
        }

        public void SaveImmediate(List<NoteItem> notes)
        {
            lock (_lock)
            {
                _debounceTimer.Stop();
                _pendingNotesToSave = null;
                SaveImmediateInternal(notes);
            }
        }

        private void SaveImmediateInternal(List<NoteItem> notes)
        {
            string? targetPath = GetNotesFilePath();
            if (string.IsNullOrEmpty(targetPath))
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    "NoteStorageService: Cannot save notes because target file path is invalid.");
                return;
            }

            string tempPath = targetPath + ".tmp";

            try
            {
                string json = JsonConvert.SerializeObject(notes, Formatting.Indented);
                File.WriteAllText(tempPath, json, Encoding.UTF8);

                if (File.Exists(targetPath))
                {
                    try
                    {
                        File.Replace(tempPath, targetPath, null);
                    }
                    catch
                    {
                        // 同磁碟直接覆寫搬移；避免先刪除正式檔後搬移失敗而遺失資料。
                        File.Move(tempPath, targetPath, true);
                    }
                }
                else
                {
                    File.Move(tempPath, targetPath);
                }

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General,
                    $"NoteStorageService: Saved {notes.Count} notes to {targetPath}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"NoteStorageService: Failed to save notes to {targetPath}: {ex.Message}");
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }
    }
}
