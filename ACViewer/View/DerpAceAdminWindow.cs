using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using ACE.DatLoader.FileTypes;

using ACViewer.CustomTextures;
using ACViewer.Services;

namespace ACViewer.View
{
    public sealed class DerpAceAdminWindow : Window
    {
        private readonly TextBox _url = new TextBox();
        private readonly PasswordBox _token = new PasswordBox();
        private readonly CheckBox _allowInsecure = new CheckBox { Content = "Allow insecure HTTP to a non-localhost server" };
        private readonly TextBox _id = new TextBox { Text = "0x10000000" };
        private readonly ListBox _documents = new ListBox { MinHeight = 190 };
        private readonly CheckBox _force = new CheckBox { Content = "Force overwrite (bypass conflict protection)" };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray };
        private readonly Button _pull = new Button { Content = "Pull into Editor", IsEnabled = false, MinWidth = 115 };
        private readonly Button _publish = new Button { Content = "Publish Current", IsEnabled = false, MinWidth = 115 };
        private readonly Button _reload = new Button { Content = "Reload Server", IsEnabled = false, MinWidth = 105 };
        private readonly DerpAceAdminClient _client = DerpAceAdminClient.Instance;

        private readonly Button _portal = new Button { Content = "Open Admin Portal", IsEnabled = false, MinWidth = 125 };
        public DerpAceAdminWindow()
        {
            Title = "DerpACE Clothing Admin";
            Width = 680;
            Height = 590;
            MinWidth = 560;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = MainWindow.Instance;

            var profile = _client.LoadProfile();
            _url.Text = profile.BaseUrl;
            _allowInsecure.IsChecked = profile.AllowInsecureRemoteHttp;

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            var intro = new TextBlock
            {
                Text = "Connect to DerpACE's authenticated admin service to pull, publish, and hot-reload CustomClothingBase files. The token stays in memory and is never saved.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(intro, 0);
            root.Children.Add(intro);

            var connection = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            connection.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            connection.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            connection.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            connection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            connection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            connection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddLabel(connection, "Server URL", 0);
            Grid.SetRow(_url, 0); Grid.SetColumn(_url, 1); _url.Margin = new Thickness(8, 2, 8, 2); connection.Children.Add(_url);
            var connect = new Button { Content = "Connect", MinWidth = 90, Margin = new Thickness(0, 2, 0, 2) };
            connect.Click += async (_, __) => await ConnectAsync();
            Grid.SetRow(connect, 0); Grid.SetColumn(connect, 2); connection.Children.Add(connect);
            AddLabel(connection, "Admin token", 1);
            Grid.SetRow(_token, 1); Grid.SetColumn(_token, 1); Grid.SetColumnSpan(_token, 2); _token.Margin = new Thickness(8, 2, 0, 2); connection.Children.Add(_token);
            Grid.SetRow(_allowInsecure, 2); Grid.SetColumn(_allowInsecure, 1); Grid.SetColumnSpan(_allowInsecure, 2); _allowInsecure.Margin = new Thickness(8, 5, 0, 0); connection.Children.Add(_allowInsecure);
            Grid.SetRow(connection, 1);
            root.Children.Add(connection);

            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var idRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            idRow.Children.Add(new TextBlock { Text = "ClothingBase ID", VerticalAlignment = VerticalAlignment.Center });
            _id.Width = 130; _id.Margin = new Thickness(8, 0, 0, 0); idRow.Children.Add(_id);
            idRow.Children.Add(new TextBlock { Text = "Select a server file below, or enter any portal.dat ID.", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Foreground = Brushes.DimGray });
            Grid.SetRow(idRow, 0); content.Children.Add(idRow);
            _documents.DisplayMemberPath = nameof(DerpAceClothingDocument.DisplayName);
            _documents.SelectionChanged += (_, __) => { if (_documents.SelectedItem is DerpAceClothingDocument item) _id.Text = item.IdHex; };
            Grid.SetRow(_documents, 1); content.Children.Add(_documents);
            _force.Margin = new Thickness(0, 7, 0, 0); Grid.SetRow(_force, 2); content.Children.Add(_force);
            Grid.SetRow(content, 2); root.Children.Add(content);

            var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            _pull.Margin = new Thickness(4, 0, 0, 0); _pull.Click += async (_, __) => await PullAsync(); buttons.Children.Add(_pull);
            _publish.Margin = new Thickness(4, 0, 0, 0); _publish.Click += async (_, __) => await PublishAsync(); buttons.Children.Add(_publish);
            _reload.Margin = new Thickness(4, 0, 0, 0); _reload.Click += async (_, __) => await ReloadAsync(); buttons.Children.Add(_reload);
            _portal.Margin = new Thickness(4, 0, 0, 0); _portal.Click += (_, __) => OpenAdminPortal(); buttons.Children.Add(_portal);
            Grid.SetColumn(_status, 0); footer.Children.Add(_status);
            Grid.SetColumn(buttons, 1); footer.Children.Add(buttons);
            Grid.SetRow(footer, 3); root.Children.Add(footer);
        }

        private static void AddLabel(Grid grid, string text, int row)
        {
            var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, row); Grid.SetColumn(label, 0); grid.Children.Add(label);
        }

        private async Task ConnectAsync()
        {
            await RunAsync(async () =>
            {
                _client.Configure(_url.Text, _token.Password, _allowInsecure.IsChecked == true);
                var entries = await _client.ListAsync();
                _documents.ItemsSource = entries;
                SetConnected(true);
                SetStatus($"Connected. {entries.Count} custom ClothingBase file(s) available.", false);
            });
        }

        private async Task PullAsync()
        {
            await RunAsync(async () =>
            {
                var id = ParseId(_id.Text);
                var document = await _client.GetAsync(id);
                var temp = Path.GetTempFileName();
                try
                {
                    await File.WriteAllTextAsync(temp, document.Json);
                    var imported = CustomTextureStore.ImportClothingTable(temp);
                    ClothingTableList.Instance?.OnClickClothingBase(imported, imported.Id, null, null);
                    ClothingTableList.Instance?.ForceOpenPaletteEditorAfterImport();
                }
                finally { try { File.Delete(temp); } catch { } }
                SetStatus($"Pulled {document.IdHex} from {(document.IsCustom ? document.FileName : "portal.dat")}.", false);
                MainWindow.Instance?.AddStatusText($"DerpACE: pulled {document.IdHex} into the clothing editor.");
            });
        }

        private async Task PublishAsync()
        {
            await RunAsync(async () =>
            {
                var clothing = ClothingTableList.CurrentClothingItem;
                if (clothing == null) throw new InvalidOperationException("Select or create a ClothingBase in the editor first.");
                var temp = Path.GetTempFileName();
                try
                {
                    if (CustomPaletteDialog.ActiveInstance != null)
                        CustomPaletteDialog.ActiveInstance.ExportClothingMod(temp);
                    else
                        CustomTextureStore.ExportClothingTable(clothing, temp);
                    var json = await File.ReadAllTextAsync(temp);
                    var saved = await _client.PublishAsync(clothing.Id, json, _force.IsChecked == true);
                    SetStatus($"Published {saved.IdHex} as {saved.FileName}; DerpACE reloaded it immediately.", false);
                    MainWindow.Instance?.AddStatusText($"DerpACE: published and reloaded {saved.IdHex}.");
                    _documents.ItemsSource = await _client.ListAsync();
                }
                finally { try { File.Delete(temp); } catch { } }
            });
        }

        private async Task ReloadAsync()
        {
            await RunAsync(async () => SetStatus(await _client.ReloadAsync(), false));
        }

        private async Task RunAsync(Func<Task> action)
        {
            IsEnabled = false;
            SetStatus("Working...", false);
            try { await action(); }
            catch (Exception ex) { SetStatus(ex.Message, true); }
            finally { IsEnabled = true; }
        }

        private void SetConnected(bool connected)
        {
            _pull.IsEnabled = connected;
            _publish.IsEnabled = connected;
            _reload.IsEnabled = connected;
            _portal.IsEnabled = connected;
        }

        private void SetStatus(string message, bool error)
        {
            _status.Text = message;
            _status.Foreground = error ? Brushes.Firebrick : Brushes.DimGray;
        }

        private void OpenAdminPortal()
        {
            try
            {
                if (_client.BaseUri == null) throw new InvalidOperationException("Connect to DerpACE first.");
                Process.Start(new ProcessStartInfo(_client.BaseUri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex) { SetStatus(ex.Message, true); }
        }

        private static uint ParseId(string value)
        {
            value = value?.Trim() ?? string.Empty;
            var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || value.Length == 8;
            if (hex) value = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.Substring(2) : value;
            if (!uint.TryParse(value, hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id == 0)
                throw new InvalidOperationException("Enter a valid non-zero ClothingBase ID, such as 0x100001A1.");
            return id;
        }
    }
}
