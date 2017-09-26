using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for HelpInformation.xaml
    /// </summary>
    public partial class AboutInformationDlg : Window
    {
        #region Private Methods

        private void Button_Click( object sender, RoutedEventArgs e )
        {
            Close( );
        }

        private void Hyperlink_RequestNavigate( object sender, RequestNavigateEventArgs e )
        {
            Process.Start( new ProcessStartInfo( e.Uri.AbsoluteUri ) );
            e.Handled = true;
        }

        #endregion

        #region Constructors

        public AboutInformationDlg( )
        {
            InitializeComponent( );
        }

        #endregion
    }
}