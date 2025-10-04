using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Linq; // ensure Linq for later if needed
using AvalonDock.Layout;
using System.Windows.Input; // added

using Microsoft.Win32;

using ACE.DatLoader;
using ACE.DatLoader.Entity;
using ACE.DatLoader.FileTypes;

using ACViewer.Config;
using ACViewer.Data;
using ACViewer.Enum;
using ACViewer.Render;
using ACViewer.CustomTextures; // added
using ACViewer.FileTypes; // added
using System.Threading;
using System.Threading.Tasks;
using System.IO; // added

namespace ACViewer.View
{
    /// <summary>
    /// Interaction logic for MainMenu.xaml
    /// </summary>
    public partial class MainMenu : UserControl
    {
        public static MainWindow MainWindow => MainWindow.Instance;

        public static MainMenu Instance { get; set; }

        public static GameView GameView => GameView.Instance;

        public static Options Options { get; set; }

        public static bool ShowHUD { get; set; }

        public static bool ShowParticles { get; set; }

        public static bool UseMipMaps
        {
            get => TextureCache.UseMipMaps;
            set => TextureCache.UseMipMaps = value;
        }

        public static bool LoadInstances { get; set; }

        public static bool LoadEncounters { get; set; }

        private CancellationTokenSource _datCts;

        public MainMenu()
        {
            InitializeComponent();
            Instance = this;

            // subscribe to live clothing JSON update events for real-time model refresh
            CustomTextureStore.ClothingJsonUpdated += updated =>
            {
                // ensure UI thread
                Dispatcher.Invoke(() =>
                {
                    if (updated == null) return;
                    if (ClothingTableList.Instance == null) return;

                    // Preserve current selection context if possible
                    var currentSetupIndex = ClothingTableList.Instance.SetupIds?.SelectedIndex ?? -1;
                    uint? currentPaletteTemplate = ClothingTableList.PaletteTemplate;
                    float? currentShade = ClothingTableList.Shade;

                    ClothingTableList.Instance.OnClickClothingBase(updated, updated.Id, currentPaletteTemplate, currentShade);

                    MainWindow?.AddStatusText($"Reloaded clothing JSON: 0x{updated.Id:X8}");
                });
            };
        }

        private static void ClearTransientMenuFocus()
        {
            if (GameView.Instance != null)
                Keyboard.Focus(GameView.Instance);
            else
                Keyboard.ClearFocus();
        }

        private void AfterMenuAction()
        {
            Dispatcher.BeginInvoke(new System.Action(ClearTransientMenuFocus), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "DAT files (*.dat)|*.dat|All files (*.*)|*.*";

            var success = openFileDialog.ShowDialog();

            if (success != true) { AfterMenuAction(); return; }

            var filenames = openFileDialog.FileNames;
            if (filenames.Length < 1) { AfterMenuAction(); return; }
            LoadDATs(filenames[0]);
            AfterMenuAction();
        }

        public void LoadDATs(string filename)
        {
            if (!File.Exists(filename) && !Directory.Exists(filename)) return;
            MainWindow.StatusSink?.Post("Starting DAT initialization...");
            _datCts?.Cancel();
            _datCts = new CancellationTokenSource();
            var path = File.Exists(filename) ? new System.IO.FileInfo(filename).Directory!.FullName : filename;
            _ = InitializeDatsAsync(path, _datCts.Token);
        }

        private async Task InitializeDatsAsync(string path, CancellationToken ct)
        {
            try
            {
                if (MainWindow.DatInitService.IsInitializing)
                {
                    MainWindow.StatusSink?.Post("DAT init already in progress", Services.StatusSeverity.Warning);
                    return;
                }
                var ok = await MainWindow.DatInitService.InitializeAsync(path, loadCellDat: true, ct);
                if (ok)
                {
                    MainWindow.StatusSink?.Post("DAT initialization complete", Services.StatusSeverity.Success);
                    if (DatManager.CellDat != null && DatManager.PortalDat != null)
                        GameView.PostInit();
                }
                else
                {
                    MainWindow.StatusSink?.Post("DAT initialization failed or canceled", Services.StatusSeverity.Error);
                }
            }
            catch (System.OperationCanceledException)
            {
                MainWindow.StatusSink?.Post("DAT initialization canceled", Services.StatusSeverity.Warning);
            }
            catch (System.Exception ex)
            {
                MainWindow.StatusSink?.Post($"DAT init exception: {ex.Message}", Services.StatusSeverity.Error);
            }
        }

        public static void ReadDATFile(string filename)
        {
            // legacy fallback retained for any old calls
            var fi = new System.IO.FileInfo(filename);
            var di = fi.Attributes.HasFlag(FileAttributes.Directory) ? new DirectoryInfo(filename) : fi.Directory;
            var loadCell = true;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            DatManager.Initialize(di.FullName, true, loadCell);
        }

        private void Options_Click(object sender, RoutedEventArgs e)
        {
            Options = new Options();
            Options.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Options.ShowDialog();
            AfterMenuAction();
        }

        private void WorldMap_Click(object sender, RoutedEventArgs e)
        {
            if (DatManager.CellDat == null || DatManager.PortalDat == null) { AfterMenuAction(); return; }
            MapViewer.Instance.Init();
            AfterMenuAction();
        }

        private void ShowHUD_Click(object sender, RoutedEventArgs e)
        {
            ToggleHUD();
            AfterMenuAction();
        }

        private void ShowParticles_Click(object sender, RoutedEventArgs e)
        {
            ToggleParticles();
            AfterMenuAction();
        }

        private void UseMipMaps_Click(object sender, RoutedEventArgs e)
        {
            ToggleMipMaps();
            AfterMenuAction();
        }

        public static bool ToggleHUD(bool updateConfig = true)
        {
            ShowHUD = !ShowHUD;
            Instance.optionShowHUD.IsChecked = ShowHUD;
            if (updateConfig)
            {
                ConfigManager.Config.Toggles.ShowHUD = ShowHUD;
                ConfigManager.SaveConfig();
            }
            return ShowHUD;
        }

        public static bool ToggleParticles(bool updateConfig = true)
        {
            ShowParticles = !ShowParticles;
            Instance.optionShowParticles.IsChecked = ShowParticles;
            if (updateConfig)
            {
                ConfigManager.Config.Toggles.ShowParticles = ShowParticles;
                ConfigManager.SaveConfig();
            }
            if (GameView.ViewMode == ViewMode.World)
            {
                if (ShowParticles && !GameView.Render.ParticlesInitted)
                    GameView.Render.InitEmitters();
                if (!ShowParticles && GameView.Render.ParticlesInitted)
                    GameView.Render.DestroyEmitters();
            }
            return ShowHUD;
        }

        public static bool ToggleMipMaps(bool updateConfig = true)
        {
            UseMipMaps = !UseMipMaps;
            Instance.optionUseMipMaps.IsChecked = UseMipMaps;
            if (updateConfig)
            {
                ConfigManager.Config.Toggles.UseMipMaps = UseMipMaps;
                ConfigManager.SaveConfig();
            }
            return UseMipMaps;
        }

        private void ShowLocation_Click(object sender, RoutedEventArgs e)
        {
            if (WorldViewer.Instance != null)
                WorldViewer.Instance.ShowLocation();
            AfterMenuAction();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var about = new About();
            about.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            about.ShowDialog();
            AfterMenuAction();
        }

        private void Guide_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("cmd", @"/c docs\index.html");
            AfterMenuAction();
        }

        private void FindDID_Click(object sender, RoutedEventArgs e)
        {
            var findDID = new Finder();
            findDID.ShowDialog();
            AfterMenuAction();
        }

        private void Teleport_Click(object sender, RoutedEventArgs e)
        {
            var teleport = new Teleport();
            teleport.ShowDialog();
            AfterMenuAction();
        }

        private void LoadInstances_Click(object sender, RoutedEventArgs e)
        {
            ToggleInstances();
            AfterMenuAction();
        }

        public static void ToggleInstances(bool updateConfig = true)
        {
            if (Server.Initting) return;
            LoadInstances = !LoadInstances;
            Instance.optionLoadInstances.IsChecked = LoadInstances;
            if (updateConfig)
            {
                ConfigManager.Config.Toggles.LoadInstances = LoadInstances;
                ConfigManager.SaveConfig();
            }
            if (GameView.ViewMode != ViewMode.World) return;
            Server.ClearInstances();
            if (!LoadInstances)
            {
                if (ShowParticles)
                {
                    GameView.Instance.Render.DestroyEmitters();
                    GameView.Instance.Render.InitEmitters();
                }
                return;
            }
            var worker = new BackgroundWorker();
            worker.DoWork += (sender, doWorkEventArgs) => Server.LoadInstances();
            worker.RunWorkerCompleted += (sender, runWorkerCompletedEventArgs) => Server.LoadInstances_Finalize();
            worker.RunWorkerAsync();
        }

        private void LoadEncounters_Click(object sender, RoutedEventArgs e)
        {
            ToggleEncounters();
            AfterMenuAction();
        }

        public static void ToggleEncounters(bool updateConfig = true)
        {
            if (Server.Initting) return;
            LoadEncounters = !LoadEncounters;
            Instance.optionLoadEncounters.IsChecked = LoadEncounters;
            if (updateConfig)
            {
                ConfigManager.Config.Toggles.LoadEncounters = LoadEncounters;
                ConfigManager.SaveConfig();
            }
            if (GameView.ViewMode != ViewMode.World) return;
            Server.ClearEncounters();
            if (!LoadEncounters)
            {
                if (ShowParticles)
                {
                    GameView.Instance.Render.DestroyEmitters();
                    GameView.Instance.Render.InitEmitters();
                }
                return;
            }
            var worker = new BackgroundWorker();
            worker.DoWork += (sender, doWorkEventArgs) => Server.LoadEncounters();
            worker.RunWorkerCompleted += (sender, runWorkerCompletedEventArgs) => Server.LoadEncounters_Finalize();
            worker.RunWorkerAsync();
        }

        private void miVirindiColorTool_Click(object sender, RoutedEventArgs e)
        {
            var vct = new VirindiColorTool();
            vct.ShowDialog();
            AfterMenuAction();
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            var armorWindow = new ArmorList();
            armorWindow.ShowDialog();
            AfterMenuAction();
        }

        private void ImportClothingJson_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Clothing JSON (*.json)|*.json", Title = "Import Clothing JSON" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var imported = CustomTextureStore.ImportClothingTable(dlg.FileName);
                    if (imported == null)
                    {
                        MainWindow.Instance.AddStatusText("Import failed: empty file");
                    }
                    else
                    {
                        ClothingTableList.Instance?.OnClickClothingBase(imported, imported.Id, null, null);
                        ClothingTableList.Instance?.ForceOpenPaletteEditorAfterImport();
                        MainWindow.Instance.AddStatusText($"Imported clothing JSON: {System.IO.Path.GetFileName(dlg.FileName)}");
                        CustomTextureStore.WatchClothingJson(dlg.FileName);
                    }
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show($"Import failed: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            AfterMenuAction();
        }

        private void ExportClothingJson_Click(object sender, RoutedEventArgs e)
        {
            var clothing = ClothingTableList.CurrentClothingItem; // this is ACE.DatLoader.FileTypes.ClothingTable
            if (clothing == null)
            {
                MainWindow.Instance.AddStatusText("No clothing table selected for export");
                AfterMenuAction();
                return;
            }
            var save = new SaveFileDialog { Filter = "Clothing JSON (*.json)|*.json", FileName = $"{clothing.Id:X8}.json", Title = "Export Clothing JSON" };
            if (save.ShowDialog() == true)
            {
                try
                {
                    CustomTextureStore.ExportClothingTable(clothing, save.FileName);
                    MainWindow.Instance.AddStatusText($"Exported clothing JSON: {Path.GetFileName(save.FileName)}");
                    CustomTextureStore.WatchClothingJson(save.FileName);
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            AfterMenuAction();
        }

        private void Menu_OpenPaletteEditor(object sender, RoutedEventArgs e)
        {
            ClothingTableList.Instance?.OpenPaletteAndTextureEditors();
            AfterMenuAction();
        }

        private void BuildDockVisibilityMenu()
        {
            var viewMenu = (this.Content as Grid)?.Children.OfType<Menu>().FirstOrDefault()?.Items.OfType<MenuItem>().FirstOrDefault(mi => (string)mi.Header == "_View");
            if (viewMenu == null) return;
            var existing = viewMenu.Items.OfType<Separator>().FirstOrDefault(s => (s.Tag as string) == "DockWindowsSeparator");
            if (existing != null)
            {
                int idx = viewMenu.Items.IndexOf(existing);
                while (viewMenu.Items.Count > idx + 1 && viewMenu.Items[idx + 1] is MenuItem m && (m.Tag as string) == "DockWindowItem")
                    viewMenu.Items.RemoveAt(idx + 1);
                viewMenu.Items.Remove(existing);
            }
            var mw = MainWindow.Instance;
            if (mw?.DockManager?.Layout == null) return;
            var docks = mw.DockManager.Layout.Descendents().OfType<LayoutAnchorable>().ToList();
            if (docks.Count == 0) return;
            var sep = new Separator { Tag = "DockWindowsSeparator" };
            viewMenu.Items.Add(sep);
            foreach (var a in docks)
            {
                var mi = new MenuItem { Header = a.Title, IsCheckable = true, IsChecked = a.IsVisible, Tag = "DockWindowItem" };
                mi.Click += (_, __) => { if (mi.IsChecked) a.Show(); else a.Hide(); };
                viewMenu.Items.Add(mi);
            }
        }

        private void ViewMenu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            BuildDockVisibilityMenu();
        }

        private void SaveLayoutAsDefault_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance.SaveLayoutAsDefault();
            AfterMenuAction();
        }

        private void Menu_OpenTextureGallery(object sender, RoutedEventArgs e)
        {
            if (DatManager.PortalDat == null) { AfterMenuAction(); return; }
            GameView.ViewMode = ACViewer.Enum.ViewMode.TextureGallery;
            AfterMenuAction();
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var selectedFileID = FileExplorer.Instance.Selected_FileID;
            if (selectedFileID == 0)
            {
                MainWindow.Instance.AddStatusText("You must first select a file to export");
                AfterMenuAction();
                return;
            }
            var saveFileDialog = new SaveFileDialog();
            var fileType = selectedFileID >> 24;
            var isModel = fileType == 0x1 || fileType == 0x2;
            var isImage = fileType == 0x5 || fileType == 0x6 || fileType == 0x8;
            var isSound = fileType == 0xA;
            if (isModel)
            {
                saveFileDialog.Filter = "OBJ files (*.obj)|*.obj|FBX files (*.fbx)|*.fbx|DAE files (*.dae)|*.dae|RAW files (*.raw)|*.raw";
                saveFileDialog.FileName = $"{selectedFileID:X8}.obj";
            }
            else if (isImage)
            {
                saveFileDialog.Filter = "PNG files (*.png)|*.png|RAW files (*.raw)|*.raw";
                saveFileDialog.FileName = $"{selectedFileID:X8}.png";
            }
            else if (isSound)
            {
                var sound = DatManager.PortalDat.ReadFromDat<Wave>(selectedFileID);
                if (sound.Header[0] == 0x55)
                {
                    saveFileDialog.Filter = "MP3 files (*.mp3)|*.mp3|RAW files (*.raw)|*.raw";
                    saveFileDialog.FileName = $"{selectedFileID:X8}.mp3";
                }
                else
                {
                    saveFileDialog.Filter = "WAV files (*.wav)|*.wav|RAW files (*.raw)|*.raw";
                    saveFileDialog.FileName = $"{selectedFileID:X8}.wav";
                }
            }
            else
            {
                saveFileDialog.Filter = "RAW files (*.raw)|*.raw";
                saveFileDialog.FileName = $"{selectedFileID:X8}.raw";
            }
            var success = saveFileDialog.ShowDialog();
            if (success == true)
            {
                var path = saveFileDialog.FileName;
                if (isModel && saveFileDialog.FilterIndex == 1)
                    FileExport.ExportModel(selectedFileID, path);
                else if (isModel && saveFileDialog.FilterIndex > 1)
                {
                    var rawState = ModelViewer.Instance?.ViewObject?.PhysicsObj?.MovementManager?.MotionInterpreter?.RawState;
                    MotionData motionData = null;
                    if (rawState != null)
                    {
                        var didTable = DIDTables.Get(selectedFileID);
                        if (didTable != null)
                        {
                            motionData = ACE.Server.Physics.Animation.MotionTable.GetMotionData(didTable.MotionTableID, rawState.ForwardCommand, rawState.CurrentStyle) ??
                                         ACE.Server.Physics.Animation.MotionTable.GetLinkData(didTable.MotionTableID, rawState.ForwardCommand, rawState.CurrentStyle);
                        }
                    }
                    FileExport.ExportModel_Assimp(selectedFileID, motionData, path);
                }
                else if (isImage && saveFileDialog.FilterIndex == 1)
                    FileExport.ExportImage(selectedFileID, path);
                else if (isSound && saveFileDialog.FilterIndex == 1)
                    FileExport.ExportSound(selectedFileID, path);
                else
                    FileExport.ExportRaw(DatType.Portal, selectedFileID, path);
            }
            AfterMenuAction();
        }
    }
}
