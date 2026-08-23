using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Microsoft.Win32;
using ACE.DatLoader;

using ACViewer.Services;

namespace ACViewer.View
{
    public sealed class DatWorkspaceWindow : Window
    {
        private readonly DatWorkspaceService _workspace = new DatWorkspaceService(MainWindow.StatusSink);
        private readonly TextBox _datPath = new TextBox();
        private readonly TextBlock _datStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBox _textureId = new TextBox();
        private readonly TextBox _imagePath = new TextBox();
        private readonly CheckBox _addNewTexture = new CheckBox { Content = "Add as a new RenderSurface ID" };
        private readonly CheckBox _resizeTexture = new CheckBox { Content = "Resize replacement to existing texture dimensions", IsChecked = true };
        private readonly TextBox _modelId = new TextBox();
        private readonly CheckBox _allowPackageOverwrite = new CheckBox { Content = "Allow package import to overwrite existing portal.dat IDs" };
        private readonly ListBox _activity = new ListBox { MinHeight = 130 };

        public DatWorkspaceWindow()
        {
            Title = "DAT Workspace";
            Width = 880;
            Height = 680;
            MinWidth = 760;
            MinHeight = 560;
            Owner = MainWindow.Instance;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Background = Brush("#252525");
            Foreground = Brush("#e7e7e7");

            var defaultPath = @"C:\Turbine\Asheron's Call";
            _datPath.Text = Directory.Exists(defaultPath) ? defaultPath : string.Empty;
            SeedIdsFromSelection();

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Content = root;

            AddHeader(root);
            AddDatSection(root);
            AddTextureSection(root);
            AddModelSection(root);
            AddActivitySection(root);

            if (!string.IsNullOrWhiteSpace(_datPath.Text))
                InspectDatFolder();
        }

        private void AddHeader(Grid root)
        {
            var title = new TextBlock
            {
                Text = "DerpACE DAT Workspace",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);
        }

        private void AddDatSection(Grid root)
        {
            var panel = Card();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddSectionTitle(panel, "Workspace DATs", 0);

            var pathRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _datPath.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(_datPath, 0);
            pathRow.Children.Add(_datPath);

            var browse = Button("Browse DAT...");
            browse.Margin = new Thickness(0, 0, 8, 0);
            browse.Click += (_, __) => BrowseDatFolder();
            Grid.SetColumn(browse, 1);
            pathRow.Children.Add(browse);

            var inspect = Button("Validate / Load");
            inspect.Click += (_, __) =>
            {
                InspectDatFolder();
                MainMenu.Instance?.LoadDATs(_datPath.Text);
            };
            Grid.SetColumn(inspect, 2);
            pathRow.Children.Add(inspect);

            Grid.SetRow(pathRow, 1);
            panel.Children.Add(pathRow);

            _datStatus.Foreground = Brush("#b9c7d8");
            _datStatus.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetRow(_datStatus, 2);
            panel.Children.Add(_datStatus);

            Grid.SetRow(panel, 1);
            root.Children.Add(panel);
        }

        private void AddTextureSection(Grid root)
        {
            var panel = Card();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddSectionTitle(panel, "Textures", 0);

            var idRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            idRow.Children.Add(Label("RenderSurface ID"));
            _textureId.Width = 135;
            _textureId.Margin = new Thickness(8, 0, 14, 0);
            idRow.Children.Add(_textureId);

            var export = Button("Export PNG");
            export.Click += (_, __) => ExportTexture();
            idRow.Children.Add(export);

            var selected = Button("Use Selected DID");
            selected.Margin = new Thickness(8, 0, 0, 0);
            selected.Click += (_, __) => UseSelectedDid(_textureId);
            idRow.Children.Add(selected);
            Grid.SetRow(idRow, 1);
            panel.Children.Add(idRow);

            var imageRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            imageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            imageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            imageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            imageRow.Children.Add(Label("Replacement image"));
            _imagePath.Margin = new Thickness(8, 0, 8, 0);
            Grid.SetColumn(_imagePath, 1);
            imageRow.Children.Add(_imagePath);
            var imageBrowse = Button("Choose...");
            imageBrowse.Click += (_, __) => BrowseImage();
            Grid.SetColumn(imageBrowse, 2);
            imageRow.Children.Add(imageBrowse);
            Grid.SetRow(imageRow, 2);
            panel.Children.Add(imageRow);

            var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            options.Children.Add(_addNewTexture);
            _resizeTexture.Margin = new Thickness(16, 0, 0, 0);
            options.Children.Add(_resizeTexture);
            var apply = Button("Apply Staged Texture Write");
            apply.Margin = new Thickness(18, 0, 0, 0);
            apply.Click += (_, __) => ApplyTexture();
            options.Children.Add(apply);
            Grid.SetRow(options, 3);
            panel.Children.Add(options);

            Grid.SetRow(panel, 2);
            root.Children.Add(panel);
        }

        private void AddModelSection(Grid root)
        {
            var panel = Card();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddSectionTitle(panel, "Models", 0);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(Label("Setup/GfxObj ID"));
            _modelId.Width = 135;
            _modelId.Margin = new Thickness(8, 0, 14, 0);
            row.Children.Add(_modelId);

            var useSelected = Button("Use Selected DID");
            useSelected.Click += (_, __) => UseSelectedDid(_modelId);
            row.Children.Add(useSelected);

            var export = Button("Export Model...");
            export.Margin = new Thickness(8, 0, 0, 0);
            export.Click += (_, __) => ExportModel();
            row.Children.Add(export);

            var exportPackage = Button("Export DAT Package...");
            exportPackage.Margin = new Thickness(8, 0, 0, 0);
            exportPackage.Click += (_, __) => ExportDatPackage();
            row.Children.Add(exportPackage);

            var importPackage = Button("Import DAT Package...");
            importPackage.Margin = new Thickness(8, 0, 0, 0);
            importPackage.Click += (_, __) => ImportDatPackage();
            row.Children.Add(importPackage);
            Grid.SetRow(row, 1);
            panel.Children.Add(row);

            _allowPackageOverwrite.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetRow(_allowPackageOverwrite, 2);
            panel.Children.Add(_allowPackageOverwrite);

            Grid.SetRow(panel, 3);
            root.Children.Add(panel);
        }

        private void AddActivitySection(Grid root)
        {
            var panel = Card();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            AddSectionTitle(panel, "Activity", 0);
            _activity.Margin = new Thickness(0, 8, 0, 0);
            _activity.Background = Brush("#1e1e1e");
            _activity.Foreground = Brush("#e7e7e7");
            Grid.SetRow(_activity, 1);
            panel.Children.Add(_activity);
            Grid.SetRow(panel, 4);
            root.Children.Add(panel);
        }

        private void InspectDatFolder()
        {
            Run(() =>
            {
                var info = _workspace.Inspect(_datPath.Text);
                _datPath.Text = info.DirectoryPath;
                _datStatus.Text = info.Summary;
                Log($"Validated {info.DirectoryPath}");
            });
        }

        private void ExportTexture()
        {
            Run(() =>
            {
                var id = ParseId(_textureId.Text, "texture");
                var save = new SaveFileDialog
                {
                    Filter = "PNG image (*.png)|*.png",
                    FileName = $"{id:X8}.png",
                    Title = "Export RenderSurface"
                };
                if (save.ShowDialog(this) != true) return;

                if ((id >> 24) == 0x06)
                {
                    _workspace.ExportRenderSurface(_datPath.Text, id, save.FileName);
                }
                else
                {
                    if (DatManager.PortalDat == null)
                        throw new InvalidOperationException("Load the DATs first, then export SurfaceTexture/Surface IDs through the existing viewer pipeline.");
                    if (!FileExport.ExportImage(id, save.FileName))
                        throw new InvalidOperationException($"Could not export 0x{id:X8} as an image.");
                }
                Log($"Exported texture 0x{id:X8} -> {save.FileName}");
            });
        }

        private void ApplyTexture()
        {
            Run(() =>
            {
                var id = ParseId(_textureId.Text, "texture");
                if ((id >> 24) != 0x06)
                    throw new InvalidOperationException("DAT writes currently target RenderSurface IDs in the 0x06 range. Export SurfaceTexture 0x05 IDs first, then write the backing 0x06 RenderSurface.");

                var receipt = _workspace.ApplyRenderSurfaceImage(_datPath.Text, id, _imagePath.Text, _addNewTexture.IsChecked == true, _resizeTexture.IsChecked == true);
                Log(receipt.Message);
                Log($"Manifest: {receipt.ManifestPath}");
            });
        }

        private void ExportModel()
        {
            Run(() =>
            {
                var id = ParseId(_modelId.Text, "model");
                if ((id >> 24) != 0x01 && (id >> 24) != 0x02)
                    throw new InvalidOperationException("Model export expects a GfxObj 0x01 or Setup 0x02 ID.");
                if (DatManager.PortalDat == null)
                    throw new InvalidOperationException("Load the DATs first so the existing model exporter can resolve references.");

                var save = new SaveFileDialog
                {
                    Filter = "OBJ files (*.obj)|*.obj|FBX files (*.fbx)|*.fbx|DAE files (*.dae)|*.dae|RAW files (*.raw)|*.raw",
                    FileName = $"{id:X8}.obj",
                    Title = "Export Model"
                };
                if (save.ShowDialog(this) != true) return;

                if (save.FilterIndex == 1)
                    FileExport.ExportModel(id, save.FileName);
                else if (save.FilterIndex < 4)
                    FileExport.ExportModel_Assimp(id, null, save.FileName);
                else
                    FileExport.ExportRaw(Enum.DatType.Portal, id, save.FileName);

                Log($"Exported model 0x{id:X8} -> {save.FileName}");
            });
        }

        private void ExportDatPackage()
        {
            Run(() =>
            {
                var id = ParseId(_modelId.Text, "asset");
                if (DatManager.PortalDat == null)
                    throw new InvalidOperationException("Load the DATs first so the package exporter can read raw records and dependencies.");

                var save = new SaveFileDialog
                {
                    Filter = "DerpACE DAT asset package (*.datasset.zip)|*.datasset.zip|Zip package (*.zip)|*.zip",
                    FileName = $"{id:X8}.datasset.zip",
                    Title = "Export DAT Asset Package"
                };
                if (save.ShowDialog(this) != true) return;

                _workspace.ExportPortalAssetPackage(_datPath.Text, id, save.FileName);
                Log($"Exported DAT package 0x{id:X8} -> {save.FileName}");
            });
        }

        private void ImportDatPackage()
        {
            Run(() =>
            {
                var open = new OpenFileDialog
                {
                    Filter = "DerpACE DAT asset package (*.datasset.zip;*.zip)|*.datasset.zip;*.zip|All files (*.*)|*.*",
                    Title = "Import DAT Asset Package"
                };
                if (open.ShowDialog(this) != true) return;

                var receipt = _workspace.ImportPortalAssetPackage(_datPath.Text, open.FileName, _allowPackageOverwrite.IsChecked == true);
                Log(receipt.Message);
                Log($"Manifest: {receipt.ManifestPath}");
            });
        }
        private void BrowseDatFolder()
        {
            var open = new OpenFileDialog
            {
                Filter = "DAT files (*.dat)|*.dat|All files (*.*)|*.*",
                Title = "Choose any DAT in the workspace folder"
            };
            if (Directory.Exists(_datPath.Text))
                open.InitialDirectory = _datPath.Text;
            if (open.ShowDialog(this) == true)
            {
                _datPath.Text = Path.GetDirectoryName(open.FileName) ?? _datPath.Text;
                InspectDatFolder();
            }
        }

        private void BrowseImage()
        {
            var open = new OpenFileDialog
            {
                Filter = "Images (*.png;*.bmp;*.gif;*.jpg;*.jpeg)|*.png;*.bmp;*.gif;*.jpg;*.jpeg|All files (*.*)|*.*",
                Title = "Choose replacement texture image"
            };
            if (open.ShowDialog(this) == true)
                _imagePath.Text = open.FileName;
        }

        private void UseSelectedDid(TextBox target)
        {
            var selected = FileExplorer.Instance?.Selected_FileID ?? 0;
            if (selected == 0)
            {
                Log("No DID selected in the explorer.");
                return;
            }
            target.Text = $"0x{selected:X8}";
            Log($"Using selected DID 0x{selected:X8}");
        }

        private void SeedIdsFromSelection()
        {
            var selected = FileExplorer.Instance?.Selected_FileID ?? 0;
            _textureId.Text = selected != 0 && ((selected >> 24) == 0x05 || (selected >> 24) == 0x06 || (selected >> 24) == 0x08)
                ? $"0x{selected:X8}"
                : "0x06000000";
            _modelId.Text = selected != 0 && ((selected >> 24) == 0x01 || (selected >> 24) == 0x02)
                ? $"0x{selected:X8}"
                : "0x02000000";
        }

        private uint ParseId(string text, string label)
        {
            if (!DatWorkspaceService.TryParseHexId(text, out var id))
                throw new InvalidOperationException($"Enter a valid {label} ID, such as 0x06000069.");
            return id;
        }

        private void Run(Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                Log(ex.Message);
                MessageBox.Show(this, ex.Message, "DAT Workspace", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Log(string message)
        {
            _activity.Items.Insert(0, $"{DateTime.Now:t}  {message}");
            MainWindow.Instance?.AddStatusText(message);
        }

        private static Grid Card()
        {
            var grid = new Grid
            {
                Background = Brush("#303030"),
                Margin = new Thickness(0, 12, 0, 0),
                MinHeight = 72
            };
            return grid;
        }

        private static void AddSectionTitle(Grid grid, string text, int row)
        {
            var title = new TextBlock
            {
                Text = text,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(12, 10, 12, 0)
            };
            Grid.SetRow(title, row);
            grid.Children.Add(title);
        }

        private static TextBlock Label(string text) => new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };

        private static Button Button(string text) => new Button { Content = text, MinWidth = 96, Padding = new Thickness(9, 4, 9, 4) };

        private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
    }
}
