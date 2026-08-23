using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ACViewer.CustomPalettes;
using ACViewer.Utilities;

namespace ACViewer.View
{
    public sealed class CustomPaletteEntryWindow : Window
    {
        private readonly TextBox _paletteTemplate;
        private readonly TextBox _iconId;
        private readonly TextBox _paletteId;
        private readonly TextBox _shade;
        private readonly StackPanel _rangeRows;
        private readonly List<RangeEditorRow> _rangeEditors = new();

        public uint PaletteTemplate { get; private set; }
        public uint IconId { get; private set; }
        public uint PaletteId { get; private set; }
        public List<RangeDef> Ranges { get; private set; } = new();
        public float Shade { get; private set; }

        public CustomPaletteEntryWindow(string defaultTemplate, string defaultIconId, string defaultPaletteId, string defaultRanges, float defaultShade)
        {
            Title = "Add Palette Template";
            Width = 560;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var root = new Grid { Margin = new Thickness(12) };
            for (var i = 0; i < 7; i++)
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition());

            AddLabel(root, "Pal #:", 0);
            _paletteTemplate = AddTextBox(root, defaultTemplate, 0, "Palette template / sub-palette effect number. Example: 13. This becomes ClothingSubPalEffects['13'].");

            AddLabel(root, "Icon:", 1);
            _iconId = AddTextBox(root, defaultIconId, 1, "0 or texture icon id. Example: 0x060017E4.");

            AddLabel(root, "Palette ID:", 2);
            _paletteId = AddTextBox(root, defaultPaletteId, 2, "Palette or palette-set id. Example: 0x0400007E or 0x0F00001D.");

            AddLabel(root, "Ranges:", 3);
            _rangeRows = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            Grid.SetRow(_rangeRows, 3);
            Grid.SetColumn(_rangeRows, 1);
            root.Children.Add(_rangeRows);

            var parsedDefaults = RangeParser.ParseRanges(defaultRanges ?? string.Empty, true, out _);
            if (parsedDefaults.Count == 0)
                parsedDefaults.Add(new RangeDef { Offset = 0, Length = 1 });
            foreach (var range in parsedDefaults)
                AddRangeRow(range.Offset, range.Length);

            AddLabel(root, "Shade:", 4);
            _shade = AddTextBox(root, defaultShade.ToString("0.###", CultureInfo.InvariantCulture), 4, "0 to 1. Used when previewing palette sets.");

            var help = new TextBlock
            {
                Text = "This creates/replaces a full ClothingSubPalEffects entry. Ranges are offset + length in 8-color groups; exported JSON uses raw Offset/NumColors values.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 10),
                Opacity = 0.8
            };
            Grid.SetRow(help, 5);
            Grid.SetColumnSpan(help, 2);
            root.Children.Add(help);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var addRange = new Button { Content = "+ Range", MinWidth = 78, Margin = new Thickness(0, 0, 18, 0) };
            addRange.Click += (_, __) => AddRangeRow(0, 1);
            var ok = new Button { Content = "Add Pal #", MinWidth = 92, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
            ok.Click += (_, __) => Accept();
            var cancel = new Button { Content = "Cancel", MinWidth = 72, IsCancel = true };
            buttons.Children.Add(addRange);
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 6);
            Grid.SetColumnSpan(buttons, 2);
            root.Children.Add(buttons);

            Content = root;
        }

        private void AddRangeRow(uint offset, uint length)
        {
            var row = new RangeEditorRow(offset, length);
            row.RemoveRequested += (_, __) =>
            {
                if (_rangeEditors.Count <= 1)
                {
                    row.OffsetText.Text = "0";
                    row.LengthText.Text = "1";
                    return;
                }
                _rangeEditors.Remove(row);
                _rangeRows.Children.Remove(row.Root);
            };
            _rangeEditors.Add(row);
            _rangeRows.Children.Add(row.Root);
        }

        private static void AddLabel(Grid root, string text, int row)
        {
            var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) };
            Grid.SetRow(label, row);
            root.Children.Add(label);
        }

        private static TextBox AddTextBox(Grid root, string text, int row, string toolTip)
        {
            var box = new TextBox { Text = text ?? string.Empty, MinWidth = 320, Margin = new Thickness(0, 0, 0, 8), ToolTip = toolTip };
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            root.Children.Add(box);
            return box;
        }

        private void Accept()
        {
            try
            {
                PaletteTemplate = HexId.Parse(_paletteTemplate.Text);
                if (PaletteTemplate == 0)
                    throw new FormatException("Pal # / PaletteTemplate 0 is reserved. Use a number like 13.");

                IconId = string.IsNullOrWhiteSpace(_iconId.Text) ? 0 : HexId.Parse(_iconId.Text);
                if (IconId != 0 && (IconId >> 24) != 0x06)
                    throw new FormatException("Icon must be 0 or a 0x06 texture icon ID, like 0x060017E4.");

                PaletteId = HexId.Parse(_paletteId.Text);
                Ranges = _rangeEditors.Select(row => row.ToRange()).ToList();
                if (Ranges.Count == 0)
                    throw new FormatException("Add at least one range, like 240:10 for raw 0x780/0x50.");

                Shade = float.TryParse(_shade.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var shade)
                    ? Math.Clamp(shade, 0f, 1f)
                    : 0f;

                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Invalid palette template", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private sealed class RangeEditorRow
        {
            public Grid Root { get; }
            public TextBox OffsetText { get; }
            public TextBox LengthText { get; }
            public event EventHandler RemoveRequested;

            public RangeEditorRow(uint offset, uint length)
            {
                Root = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                Root.Children.Add(new TextBlock { Text = "Offset", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Foreground = Brushes.Gray });

                OffsetText = new TextBox { Text = offset.ToString(CultureInfo.InvariantCulture), MinWidth = 70, Margin = new Thickness(0, 0, 10, 0), ToolTip = "Logical group offset. Raw JSON offset is this value * 8." };
                Grid.SetColumn(OffsetText, 1);
                Root.Children.Add(OffsetText);

                var lengthLabel = new TextBlock { Text = "Length", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Foreground = Brushes.Gray };
                Grid.SetColumn(lengthLabel, 2);
                Root.Children.Add(lengthLabel);

                LengthText = new TextBox { Text = length.ToString(CultureInfo.InvariantCulture), MinWidth = 70, Margin = new Thickness(0, 0, 10, 0), ToolTip = "Logical group length. Raw JSON NumColors is this value * 8." };
                Grid.SetColumn(LengthText, 3);
                Root.Children.Add(LengthText);

                var remove = new Button { Content = "Remove", MinWidth = 68 };
                remove.Click += (_, __) => RemoveRequested?.Invoke(this, EventArgs.Empty);
                Grid.SetColumn(remove, 4);
                Root.Children.Add(remove);
            }

            public RangeDef ToRange()
            {
                var offsetText = OffsetText.Text?.Trim();
                var lengthText = LengthText.Text?.Trim();
                if (!uint.TryParse(offsetText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset))
                    throw new FormatException($"Invalid range offset '{offsetText}'.");
                if (!uint.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length))
                    throw new FormatException($"Invalid range length '{lengthText}'.");
                if (length == 0)
                    throw new FormatException("Range length must be at least 1.");
                if (offset >= RangeParser.MaxLogicalGroups || length > RangeParser.MaxLogicalGroups || length > RangeParser.MaxLogicalGroups - offset)
                    throw new FormatException($"Range {offset}:{length} exceeds supported palette bounds.");
                return new RangeDef { Offset = offset, Length = length };
            }
        }
    }
}