using System.Windows;
using System.Windows.Controls;
using YTDLApp; // make sure namespace matches your YTDLControl

namespace phone_utils
{
    public partial class Intent_Sender : UserControl
    {
        private MainWindow _main;
        private string _currentDevice;

        public Intent_Sender(MainWindow main, string device)
        {
            InitializeComponent();
            _main = main;
            _currentDevice = device;
        }

        private void BtnYTDL_Click(object sender, RoutedEventArgs e)
        {
            if (ContentArea.Content is YTDLControl)
            {
                // If already open, close it
                ContentArea.Content = null;
            }
            else
            {
                // Otherwise, load YTDLControl
                ContentArea.Content = new YTDLControl(_main, _currentDevice);
            }
        }
        private void BtnInstaller_Click(object sender, RoutedEventArgs e)
        {
            if (ContentArea.Content is AppManagerControl)
            {
                ContentArea.Content = null;
            }
            else
            {
                ContentArea.Content = new AppManagerControl(_currentDevice);
            }
        }

        private void BtnSchedule_Click(object sender, RoutedEventArgs e)
        {
            if (ContentArea.Content is WakeTaskControl)
            {
                ContentArea.Content = null;
            }
            else
            {
                ContentArea.Content = new WakeTaskControl();
            }
        }

    }
}
