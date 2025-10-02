using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ACViewer.CustomPalettes
{
    public class PresetPickerWindow : Window
    {
        private readonly ListBox _list;
        private readonly List<CustomPaletteDefinition> _defs;
        public CustomPaletteDefinition Selected { get; private set; }

        public PresetPickerWindow(List<CustomPaletteDefinition> defs)
        {
            Title = "Select Preset";
            Width = 300;
            Height = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _defs = defs.OrderBy(d => d.Name).ToList();

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _list = new ListBox();
            foreach (var d in _defs)
                _list.Items.Add(d.Name);
            grid.Children.Add(_list);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(5) };
            var ok = new Button { Content = "OK", Width = 70, Margin = new Thickness(0, 0, 5, 0), IsDefault = true };
            ok.Click += (s, e) => { if (_list.SelectedIndex >= 0) { Selected = _defs[_list.SelectedIndex]; DialogResult = true; } };
            var cancel = new Button { Content = "Cancel", Width = 70, IsCancel = true };
            btnPanel.Children.Add(ok);
            btnPanel.Children.Add(cancel);
            Grid.SetRow(btnPanel, 1);
            grid.Children.Add(btnPanel);

            Content = grid;
        }
    }
}
