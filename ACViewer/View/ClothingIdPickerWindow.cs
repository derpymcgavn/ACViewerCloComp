using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ACViewer.ViewModels;

namespace ACViewer.View
{
    public sealed class ClothingIdPickerWindow : Window
    {
        private readonly ClothingEditingSession _session;
        private readonly uint _currentId;
        private readonly TextBox _id;
        private readonly TextBlock _status;
        private readonly CheckBox _allowOverride;
        private readonly Button _ok;

        public uint SelectedId { get; private set; }
        public bool AllowPortalOverride => _allowOverride.IsChecked == true;

        public ClothingIdPickerWindow(ClothingEditingSession session, uint currentId)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _currentId = currentId;
            Title = "Assign Clothing ID";
            Width = 540;
            Height = 440;
            MinWidth = 480;
            MinHeight = 390;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            var explanation = new TextBlock
            {
                Text = $"Current ID: 0x{currentId:X8}\nAssign a free 0x10 ID for a new ClothingMod entry, or explicitly allow an existing portal.dat ID when creating an intentional override.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(explanation);

            var input = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            input.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            input.ColumnDefinitions.Add(new ColumnDefinition());
            input.Children.Add(new TextBlock { Text = "New clothing ID:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _id = new TextBox { Text = $"0x{currentId:X8}", FontFamily = new FontFamily("Consolas"), FontSize = 14 };
            _id.TextChanged += (_, __) => UpdateValidation();
            Grid.SetColumn(_id, 1);
            input.Children.Add(_id);
            Grid.SetRow(input, 1);
            root.Children.Add(input);

            root.Children.Add(new TextBlock { Text = "Suggested free IDs", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 4) });
            Grid.SetRow(root.Children[^1], 2);

            var suggestions = new ListBox
            {
                ItemsSource = session.SuggestClothingIds(currentId),
                DisplayMemberPath = nameof(ClothingIdSuggestion.DisplayName),
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            suggestions.SelectionChanged += (_, __) =>
            {
                if (suggestions.SelectedItem is ClothingIdSuggestion suggestion)
                    _id.Text = $"0x{suggestion.Id:X8}";
            };
            Grid.SetRow(suggestions, 3);
            root.Children.Add(suggestions);

            var options = new StackPanel();
            _allowOverride = new CheckBox
            {
                Content = "Allow an existing portal.dat clothing ID (intentional override)",
                Margin = new Thickness(0, 0, 0, 5)
            };
            _allowOverride.Checked += (_, __) => UpdateValidation();
            _allowOverride.Unchecked += (_, __) => UpdateValidation();
            options.Children.Add(_allowOverride);
            _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
            options.Children.Add(_status);
            Grid.SetRow(options, 4);
            root.Children.Add(options);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            _ok = new Button { Content = "Assign ID", Width = 92, IsDefault = true, Margin = new Thickness(0, 0, 7, 0) };
            _ok.Click += (_, __) =>
            {
                if (!TryParseId(_id.Text, out var parsed)) return;
                SelectedId = parsed;
                DialogResult = true;
            };
            var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
            buttons.Children.Add(_ok);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 5);
            root.Children.Add(buttons);

            Loaded += (_, __) =>
            {
                if (suggestions.Items.Count > 0) suggestions.SelectedIndex = 0;
                _id.Focus();
                _id.SelectAll();
                UpdateValidation();
            };
        }

        private void UpdateValidation()
        {
            var valid = TryParseId(_id.Text, out var id);
            var message = valid ? "ID is available." : "Enter a decimal ID or hexadecimal ID such as 0x10FF0000.";
            var warning = false;
            if (valid && (id >> 24) != 0x10)
            {
                valid = false;
                message = "Clothing IDs must be in 0x10000000-0x10FFFFFF.";
            }
            if (valid && id != _currentId && _session.ClothingItems.Any(item => item.Id == id))
            {
                valid = false;
                message = "That ID is already used by another working mod.";
            }
            if (valid && id != _currentId && _session.IsPortalClothingId(id))
            {
                warning = true;
                valid = AllowPortalOverride;
                message = valid
                    ? "Existing DAT ID: export will intentionally override this clothing table."
                    : "That ID exists in portal.dat. Enable intentional override to use it.";
            }
            if (valid && id == _currentId) message = "This is already the current clothing ID.";
            _ok.IsEnabled = valid;
            _status.Text = message;
            _status.Foreground = warning ? Brushes.DarkOrange : valid ? Brushes.SeaGreen : Brushes.IndianRed;
        }

        private static bool TryParseId(string text, out uint value)
        {
            value = 0;
            text = text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
