using System.Windows;

namespace YourApp
{
    public partial class ThemeNameDialog : Window
    {
        public string ThemeName { get; private set; } = string.Empty;

        public ThemeNameDialog()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtThemeName.Text))
            {
                MessageBox.Show("Please enter a theme name.", "Missing Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ThemeName = TxtThemeName.Text.Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
