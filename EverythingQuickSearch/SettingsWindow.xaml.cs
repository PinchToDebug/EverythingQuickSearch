using System.Windows;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;

namespace EverythingQuickSearch
{
    public partial class SettingsWindow : FluentWindow
    {
        MainWindow _window;
        public SettingsWindow(MainWindow window)
        {
            InitializeComponent();
            _window = window;
            BackgroundDropDownButton.Content = _window.Settings.TransparentBackground ? "Transparent" : "Default";
            LoadBackgroundContextMenu();
        }

        private void LoadBackgroundContextMenu()
        {
            HashSet<String> sortValues = new HashSet<string> {
                "Default",
                "Transparent"
            };

            int id = 0;
            int fontSize = 13;
            int height = 31;
            Thickness padding = new Thickness(0, 0, 8, 0);

            foreach (var item in sortValues)
            {
                MenuItem menuitem = new MenuItem
                {
                    Header = new TextBlock
                    {
                        Text = item,
                        FontSize = fontSize,
                        Padding = padding
                    },
                    Height = height,
                    Tag = id,
                };

                menuitem.Click += (_, _) =>
                {
                    _window.Settings.TransparentBackground = ((int)menuitem.Tag == 1);
                    BackgroundDropDownButton.Content = ((TextBlock)menuitem.Header).Text;
                };
                id++;
                BackgroundContextMenu.Items.Add(menuitem);
            }
        }


    }
}