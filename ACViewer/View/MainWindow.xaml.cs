using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AvalonDock.Layout.Serialization;
using ACViewer.Config;
using ACViewer.CustomTextures;
using ACE.DatLoader.FileTypes;

namespace ACViewer.View
{
    /// <summary>
    /// Main application window with docking + JSON tooling.
    /// </summary>
    public partial class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; }
        private static Config.Config Config => ConfigManager.Config;
        private const string DockLayoutFile = "DockLayout.config";

        // JSON editor state
        private string _lastJsonSnapshot = string.Empty;
        private bool _autoApply;
        private bool _suppressCaretUpdate;
        private Regex _lastSearchRegex;

        // Status batching
        private readonly List<string> _statusLines = new();
        private DateTime _lastStatusFlush;
        private bool _pendingStatusFlush;
        private const int MaxStatusLines = 200;
        private static readonly TimeSpan StatusMinInterval = TimeSpan.FromMilliseconds(800);
        public bool SuppressStatusText { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            Instance = this;
            DataContext = this;
            // Defer heavy init so docked controls (MainMenu, etc.) are created
            Loaded += (_, __) => SafeInit();
        }

        private void SafeInit()
        {
            try
            {
                LoadConfig();
                TryLoadDockLayout();
                // Ensure Custom Palette / Texture panels are present at startup
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { ClothingTableList.Instance?.OpenCustomDialog(); } catch { }
                }));
            }
            catch (Exception ex)
            {
                AddStatusText($"Startup init failed: {ex.Message}");
            }
        }

        #region Config / Layout persistence
        private static void LoadConfig()
        {
            ConfigManager.LoadConfig();

            if (Config.AutomaticallyLoadDATsOnStartup)
            {
                if (MainMenu.Instance != null)
                    MainMenu.Instance.LoadDATs(Config.ACFolder);
                else
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => MainMenu.Instance?.LoadDATs(Config.ACFolder)));
            }

            if (ConfigManager.HasDBInfo)
            {
                try { Server.TryPrimeDatabase(); } catch { }
            }

            var t = Config.Toggles;
            if (t.UseMipMaps) MainMenu.ToggleMipMaps(false);
            if (t.ShowHUD) MainMenu.ToggleHUD(false);
            if (t.ShowParticles) MainMenu.ToggleParticles(false);
            if (t.LoadInstances) MainMenu.ToggleInstances(false);
            if (t.LoadEncounters) MainMenu.ToggleEncounters(false);
            if (Config.Theme != null) ThemeManager.SetTheme(Config.Theme);
        }

        private void TryLoadDockLayout()
        {
            try
            {
                if (!File.Exists(DockLayoutFile)) return;
                var serializer = new XmlLayoutSerializer(DockManager);
                serializer.Deserialize(DockLayoutFile);
            }
            catch { /* ignore layout load errors */ }
        }

        private void SaveDockLayout()
        {
            try
            {
                var serializer = new XmlLayoutSerializer(DockManager);
                serializer.Serialize(DockLayoutFile);
            }
            catch { /* ignore layout save errors */ }
        }

        public void SaveLayoutAsDefault()
        {
            try
            {
                var serializer = new XmlLayoutSerializer(DockManager);
                serializer.Serialize(DockLayoutFile);
                AddStatusText("Layout saved as default.");
            }
            catch (Exception ex)
            {
                AddStatusText($"Failed to save layout: {ex.Message}");
            }
        }
        private void SaveLayoutAsDefault_Click(object sender, RoutedEventArgs e) => SaveLayoutAsDefault();
        #endregion

        #region Window events
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveDockLayout();
            var pos = Config.WindowPos;
            pos.X = (int)Left; pos.Y = (int)Top; pos.Width = (int)Width; pos.Height = (int)Height; pos.IsMaximized = WindowState == WindowState.Maximized;
            ConfigManager.SaveConfig();
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            var pos = Config.WindowPos;
            if (pos.X == int.MinValue) return;
            Left = pos.X; Top = pos.Y; Width = pos.Width; Height = pos.Height;
            if (pos.IsMaximized) WindowState = WindowState.Maximized;
        }
        #endregion

        #region Status output
        public async void AddStatusText(string line)
        {
            if (SuppressStatusText) return;
            _statusLines.Add(line);
            if (_statusLines.Count > MaxStatusLines)
                _statusLines.RemoveRange(0, _statusLines.Count - MaxStatusLines);

            var elapsed = DateTime.Now - _lastStatusFlush;
            if (elapsed < StatusMinInterval)
            {
                if (_pendingStatusFlush) return;
                _pendingStatusFlush = true;
                await Task.Delay(StatusMinInterval);
                _pendingStatusFlush = false;
            }
            if (Status != null)
            {
                Status.Text = string.Join('\n', _statusLines);
                Status.ScrollToEnd();
            }
            _lastStatusFlush = DateTime.Now;
        }
        #endregion

        #region Commands
        public ICommand FindCommand { get; } = new ActionCommand(() => new Finder().ShowDialog());
        public ICommand TeleportCommand { get; } = new ActionCommand(() => new Teleport().ShowDialog());
        public ICommand HistoryCommand { get; } = new ActionCommand(() => { var prev = FileExplorer.Instance.History.Pop(); if (prev != null) Finder.Navigate(prev.Value.ToString("X8")); });
        public static bool DebugMode { get; set; }
        public ICommand DebugCommand { get; } = new ActionCommand(() => { DebugMode = !DebugMode; Console.WriteLine($"Debug mode {(DebugMode ? "enabled" : "disabled")}"); });
        #endregion

        #region JSON Refresh / Apply
        private void RefreshJson_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var clothing = ClothingTableList.CurrentClothingItem;
                if (clothing == null) { AddStatusText("No clothing selected for JSON export"); return; }
                var temp = Path.GetTempFileName();
                CustomTextureStore.ExportClothingTable(clothing, temp);
                JsonEditorText.Text = File.ReadAllText(temp);
                File.Delete(temp);
                _lastJsonSnapshot = JsonEditorText.Text;
                UpdateJsonMetrics();
                UpdateDiffStat();
                AddStatusText("JSON refreshed from current clothing table");
            }
            catch (Exception ex) { AddStatusText("Refresh failed: " + ex.Message); }
        }

        private void ApplyJson_Click(object sender, RoutedEventArgs e) => ApplyJsonInternal();
        private void ApplyJsonInternal()
        {
            try
            {
                var raw = JsonEditorText.Text;
                using (var reader = new JsonTextReader(new StringReader(raw))) { while (reader.Read()) { } } // syntax validation
                var temp = Path.GetTempFileName(); File.WriteAllText(temp, raw);
                var imported = CustomTextureStore.ImportClothingTable(temp); File.Delete(temp);
                if (imported == null) { AddStatusText("Apply failed: parsed null"); return; }
                ClothingTableList.Instance?.OnClickClothingBase(imported, imported.Id, null, null);
                _lastJsonSnapshot = raw;
                UpdateJsonMetrics();
                UpdateDiffStat();
                AddStatusText("Applied JSON to clothing table.");
            }
            catch (Exception ex) { AddStatusText("Apply failed: " + ex.Message); JsonValidationStatus.Text = "Invalid"; }
        }
        #endregion

        #region Formatting / Validation / Diff
        private void FormatJson_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(JsonEditorText.Text)) return;
                var token = JToken.Parse(JsonEditorText.Text);
                JsonEditorText.Text = token.ToString(Formatting.Indented);
                AddStatusText("Formatted JSON");
            }
            catch (Exception ex) { AddStatusText("Format failed: " + ex.Message); }
        }

        private void MinifyJson_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(JsonEditorText.Text)) return;
                var token = JToken.Parse(JsonEditorText.Text);
                JsonEditorText.Text = token.ToString(Formatting.None);
                AddStatusText("Minified JSON");
            }
            catch (Exception ex) { AddStatusText("Minify failed: " + ex.Message); }
        }

        private void ValidateJson_Click(object sender, RoutedEventArgs e) => ValidateJson();
        private void ValidateJson()
        {
            try
            {
                using var reader = new JsonTextReader(new StringReader(JsonEditorText.Text)); while (reader.Read()) { }
                JsonValidationStatus.Text = "Valid";
            }
            catch (Exception ex)
            {
                JsonValidationStatus.Text = "Error";
                AddStatusText("Validation error: " + ex.Message);
            }
        }

        private void DiffJson_Click(object sender, RoutedEventArgs e) => UpdateDiffStat();
        private void UpdateDiffStat()
        {
            try
            {
                var current = JsonEditorText.Text ?? string.Empty;
                var diff = ComputeDiffMagnitude(_lastJsonSnapshot, current);
                JsonDiffStatus.Text = $"Diff: {diff}";
            }
            catch { JsonDiffStatus.Text = "Diff: ?"; }
        }

        private static int ComputeDiffMagnitude(string a, string b)
        {
            var al = a.Split('\n');
            var bl = b.Split('\n');
            int max = Math.Max(al.Length, bl.Length);
            int changes = 0;
            for (int i = 0; i < max; i++)
            {
                var l1 = i < al.Length ? al[i] : string.Empty;
                var l2 = i < bl.Length ? bl[i] : string.Empty;
                if (!l1.Equals(l2, StringComparison.Ordinal)) changes++;
            }
            return changes;
        }
        #endregion

        #region Search / Replace
        private void JsonSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = JsonSearchBox.Text;
            if (string.IsNullOrEmpty(text)) { _lastSearchRegex = null; return; }
            try { _lastSearchRegex = new Regex(Regex.Escape(text), RegexOptions.IgnoreCase | RegexOptions.Compiled); }
            catch { _lastSearchRegex = null; }
        }

        private void FindNextJson_Click(object sender, RoutedEventArgs e)
        {
            if (_lastSearchRegex == null || string.IsNullOrEmpty(JsonEditorText.Text)) return;
            int start = JsonEditorText.CaretIndex;
            var match = _lastSearchRegex.Match(JsonEditorText.Text, start);
            if (!match.Success) match = _lastSearchRegex.Match(JsonEditorText.Text, 0);
            if (!match.Success) return;
            JsonEditorText.Focus();
            JsonEditorText.Select(match.Index, match.Length);
            UpdateCaretStatus();
        }

        private void ReplaceJson_Click(object sender, RoutedEventArgs e)
        {
            if (JsonEditorText.SelectedText.Length > 0 && !string.IsNullOrEmpty(JsonSearchBox.Text) &&
                JsonEditorText.SelectedText.Equals(JsonSearchBox.Text, StringComparison.OrdinalIgnoreCase))
            {
                JsonEditorText.SelectedText = JsonReplaceBox.Text;
            }
            FindNextJson_Click(sender, e);
        }

        private void ReplaceAllJson_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(JsonSearchBox.Text)) return;
            int total = 0;
            var pattern = Regex.Escape(JsonSearchBox.Text);
            JsonEditorText.Text = Regex.Replace(JsonEditorText.Text, pattern, _ => { total++; return JsonReplaceBox.Text; }, RegexOptions.IgnoreCase);
            AddStatusText($"Replaced {total} occurrence(s).");
            UpdateJsonMetrics();
        }
        #endregion

        #region Metrics / caret
        private void JsonEditorText_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateJsonMetrics();
            if (LiveValidateJson?.IsChecked == true) ValidateJson();
            UpdateDiffStat();
            if (_autoApply && AutoApplyJson?.IsChecked == true) ApplyJsonInternal();
        }

        private void UpdateJsonMetrics()
        {
            JsonLengthStatus.Text = $"Len {JsonEditorText.Text.Length}";
            UpdateCaretStatus();
        }

        private void UpdateCaretStatus()
        {
            if (_suppressCaretUpdate) return;
            try
            {
                int idx = JsonEditorText.CaretIndex;
                var text = JsonEditorText.Text;
                int line = 1, col = 1;
                for (int i = 0; i < idx && i < text.Length; i++)
                {
                    if (text[i] == '\n') { line++; col = 1; } else col++;
                }
                JsonCaretStatus.Text = $"Ln {line}, Col {col}";
            }
            catch { JsonCaretStatus.Text = "Ln ?, Col ?"; }
        }
        #endregion

        #region Toggles
        private void ToggleAutoApply_Checked(object sender, RoutedEventArgs e) => _autoApply = AutoApplyJson.IsChecked == true;
        private void WrapJson_Checked(object sender, RoutedEventArgs e)
        {
            if (JsonEditorText == null) return; // not yet created
            JsonEditorText.TextWrapping = WrapJson.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
        }
        #endregion
    }
}
