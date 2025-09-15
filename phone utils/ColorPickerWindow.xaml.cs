using System.Windows;
using System.Windows.Media;

namespace phone_utils
{
    public partial class ColorPickerWindow : Window
    {
        public Color SelectedColor { get; private set; }

        public ColorPickerWindow(Color initial)
        {
            InitializeComponent();

            SliderR.Value = initial.R;
            SliderG.Value = initial.G;
            SliderB.Value = initial.B;
            UpdatePreview();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            byte r = (byte)SliderR.Value;
            byte g = (byte)SliderG.Value;
            byte b = (byte)SliderB.Value;

            TxtR.Text = r.ToString();
            TxtG.Text = g.ToString();
            TxtB.Text = b.ToString();

            SelectedColor = Color.FromRgb(r, g, b);
            ColorPreview.Background = new SolidColorBrush(SelectedColor);
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
