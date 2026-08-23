using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ACViewer.Config;
using ACViewer.CustomTextures;

namespace ACViewer.View
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Any(arg => string.Equals(arg, "--self-test-clothingmod", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    ClothingModService.RunExportSelfTest();
                    Console.WriteLine("ClothingMod export self-test passed.");
                    Shutdown(0);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("ClothingMod export self-test failed: " + ex);
                    Shutdown(1);
                }
                return;
            }

            ConfigManager.LoadConfig();
            ConfigManager.Config.Theme = string.IsNullOrWhiteSpace(ConfigManager.Config.Theme) ? ThemeManager.DefaultTheme : ConfigManager.Config.Theme;
            ThemeManager.SetTheme(ConfigManager.Config.Theme);
            string datPath = null;
            for (var i = 0; i < e.Args.Length; i++)
            {
                if (string.Equals(e.Args[i], "--dat-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
                {
                    datPath = e.Args[++i];
                    continue;
                }

                if (!e.Args[i].StartsWith("--", StringComparison.Ordinal) &&
                    (File.Exists(e.Args[i]) || Directory.Exists(e.Args[i])))
                    datPath ??= e.Args[i];
            }

            base.OnStartup(e);
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            if (!string.IsNullOrWhiteSpace(datPath))
                mainWindow.Loaded += (_, __) => MainMenu.Instance?.LoadDATs(datPath);
            mainWindow.Show();
        }
    }
}
