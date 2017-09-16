using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ClientAdaptor;
using PubSubConfigurationUI.Definitions;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for FindServerDlg.xaml
    /// </summary>
    public partial class FindServerDlg : Window
    {
        #region Private Member 

        private readonly FindServerViewModel _findServerViewModel;

        #endregion

        #region Private Methods

        private void LocalNetworkView_MouseDoubleClick( object sender, MouseButtonEventArgs e )
        {
            if ( ServerTreeView.SelectedItem is ServerNode )
            {
            }
            else if ( ServerTreeView.SelectedItem is SystemNode )
            {
                _findServerViewModel.FindAvailableServers( ServerTreeView.SelectedItem as SystemNode );
            }
            else
            {
                _findServerViewModel.LocalNetworkServerNodes.Clear( );
                var t = new Task( ( ) => _findServerViewModel.FindSystemLocalNetwork( ) );
                t.Start( );
            }
        }

        private void LocalMachineView_MouseDoubleClick( object sender, MouseButtonEventArgs e )
        {
            if ( ServerTreeView.SelectedItem is ServerNode ) return;
            if ( ServerTreeView.SelectedItem is SystemNode )
                _findServerViewModel.FindAvailableServers( ServerTreeView.SelectedItem as SystemNode );
            else _findServerViewModel.FindSystemLocalMachine( );
        }

        private void Ok_Click( object sender, RoutedEventArgs e )
        {
            if ( ServerTreeView.SelectedItem is ServerNode ) _selectedServer = ServerTreeView.SelectedItem as ServerNode;
            Close( );
        }

        private void Cancel_Click( object sender, RoutedEventArgs e )
        {
            Close( );
        }

        #endregion

        #region Constructors

        public FindServerDlg( OPCUAClientAdaptor opcUaClientAdaptor )
        {
            InitializeComponent( );
            DataContext = _findServerViewModel = new FindServerViewModel( opcUaClientAdaptor );
        }

        #endregion

        public ServerNode _selectedServer;
    }
}