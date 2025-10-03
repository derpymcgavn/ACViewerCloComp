using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ACViewer.Config;
using AvalonDock.Layout.Serialization; // Dirkster serializer

namespace ACViewer.View
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static MainWindow Instance { get; set; }

        private static Config.Config Config => ConfigManager.Config;

        private const string DockLayoutFile = "DockLayout.config";

        public MainWindow()
        {
            InitializeComponent();
            Instance = this;
            DataContext = this;
            LoadConfig();
            TryLoadDockLayout();
        }

        private static void LoadConfig()
        {
            ConfigManager.LoadConfig();

            if (Config.AutomaticallyLoadDATsOnStartup)
                MainMenu.Instance?.LoadDATs(Config.ACFolder);

            if (ConfigManager.HasDBInfo)
                Server.TryPrimeDatabase();

            var t = ConfigManager.Config.Toggles;
            if (t.UseMipMaps) MainMenu.ToggleMipMaps(false);
            if (t.ShowHUD) MainMenu.ToggleHUD(false);
            if (t.ShowParticles) MainMenu.ToggleParticles(false);
            if (t.LoadInstances) MainMenu.ToggleInstances(false);
            if (t.LoadEncounters) MainMenu.ToggleEncounters(false);
            if (ConfigManager.Config.Theme != null)
                ThemeManager.SetTheme(ConfigManager.Config.Theme);
        }

        private DateTime lastUpdateTime { get; set; }
        private static readonly TimeSpan maxUpdateInterval = TimeSpan.FromMilliseconds(1000);
        private readonly List<string> statusLines = new();
        private static readonly int maxLines = 100;
        private bool pendingUpdate { get; set; }
        public bool SuppressStatusText { get; set; }

        public async void AddStatusText(string line)
        {
            if (SuppressStatusText) return;
            statusLines.Add(line);
            var timeSince = DateTime.Now - lastUpdateTime;
            if (timeSince < maxUpdateInterval)
            {
                if (pendingUpdate) return;
                pendingUpdate = true;
                await Task.Delay((int)maxUpdateInterval.TotalMilliseconds);
                pendingUpdate = false;
            }
            if (statusLines.Count > maxLines)
                statusLines.RemoveRange(0, statusLines.Count - maxLines);
            Status.Text = string.Join('\n', statusLines);
            Status.ScrollToEnd();
            lastUpdateTime = DateTime.Now;
        }

        public ICommand FindCommand { get; } = new ActionCommand(() => new Finder().ShowDialog());
        public ICommand TeleportCommand { get; } = new ActionCommand(() => new Teleport().ShowDialog());
        public ICommand HistoryCommand { get; } = new ActionCommand(() => { var prev = FileExplorer.Instance.History.Pop(); if (prev != null) Finder.Navigate(prev.Value.ToString("X8")); });
        public static bool DebugMode { get; set; }
        public ICommand DebugCommand { get; } = new ActionCommand(() => { DebugMode = !DebugMode; Console.WriteLine($"Debug mode {(DebugMode ? "enabled" : "disabled")}"); });

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

        private void TryLoadDockLayout()
        {
            try
            {
                if (!File.Exists(DockLayoutFile)) return;
                var serializer = new XmlLayoutSerializer(DockManager);
                serializer.Deserialize(DockLayoutFile);
            }
            catch { }
        }

        private void SaveDockLayout()
        {
            try
            {
                var serializer = new XmlLayoutSerializer(DockManager);
                serializer.Serialize(DockLayoutFile);
            }
            catch { }
        }
    }
}
