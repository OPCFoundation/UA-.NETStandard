using System.ComponentModel;
using System.Windows;
using NLogManager;
using PubSubConfigurationUI.ViewModels;
using PubSubConfigurationUI.Views;

namespace PubSubConfigurationUI
{
    /// <summary>
    ///   Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Private Member 

        private readonly MainViewModel _MainViewModel;

        #endregion

        #region Private Methods

        private void MainWindow_Loaded( object sender, RoutedEventArgs e )
        {
            PubSubTabItems.SelectedIndex = 0;
        }

        private void MainWindow_Closing( object sender, CancelEventArgs e )
        {
            ShowDown( );
        }

        private void OnConnectClick( object sender, RoutedEventArgs e )
        {
            var isConnected = _MainViewModel.Connect( cmbBox.Text );
            if ( isConnected )
            {
                cmbBox.Text = _MainViewModel.SelectedEndPoint;
                //  PubSubTabItems.IsEnabled = true;
                _MainViewModel.IsConnectButtonVisible = false;
                _MainViewModel.IsDisConnectButtonVisible = true;
                Log.Info( "Server connected Successfully " + cmbBox.Text );
            }
        }

        private void OnDisConnectClick( object sender, RoutedEventArgs e )
        {
            var isDisConnected = _MainViewModel.DisConnect( );
            if ( isDisConnected )
            {
                //   PubSubTabItems.IsEnabled = false;
                _MainViewModel.IsConnectButtonVisible = true;
                _MainViewModel.IsDisConnectButtonVisible = false;

                _MainViewModel.ServerStatus = "Disconnected";
                Log.Info( "Server disconnected Successfully " );
            }
        }

        private void OnFindServerClick( object sender, RoutedEventArgs e )
        {
            var _FindServerDlg = new FindServerDlg( _MainViewModel.OPCUAClientAdaptor );
            _FindServerDlg.Closing += _FindServerDlg_Closing;
            _FindServerDlg.ShowDialog( );
        }

        private void _FindServerDlg_Closing( object sender, CancelEventArgs e )
        {
            var _FindServerDlg = sender as FindServerDlg;
            if ( _FindServerDlg._selectedServer != null )
            {
                _MainViewModel.SelectedServers.Insert( 0, _FindServerDlg._selectedServer );
                cmbBox.SelectedIndex = 0;
            }
        }

        private void Exit_Click( object sender, RoutedEventArgs e )
        {
            Close( );
        }

        private void ShowDown( )
        {
            _MainViewModel.OPCUAClientAdaptor.Disconnect( );
        }

        private void OnAboutClick( object sender, RoutedEventArgs e )
        {
            var helpInformation = new AboutInformationDlg( );
            helpInformation.ShowDialog( );
        }

        #endregion

        #region Constructors

        public MainWindow( )
        {
            InitializeComponent( );
            Closing += MainWindow_Closing;
            DataContext = _MainViewModel = new MainViewModel( );
            Loaded += MainWindow_Loaded;
        }

        #endregion
    }
}