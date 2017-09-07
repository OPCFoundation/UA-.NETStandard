using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Controls;
using ClientAdaptor;
using Opc.Ua.Client;
using PubSubBase.Definitions;
using PubSubConfigurationUI.Definitions;
using PubSubConfigurationUI.Views;

namespace PubSubConfigurationUI.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        #region Private Member 

        private Session _Session;

        #endregion

        #region Private Methods

        private void OPCUAClientAdaptor_PropertyChanged( object sender, PropertyChangedEventArgs e )
        {
            if ( e.PropertyName == "ServerStatus" )
            {
                ServerStatus = OPCUAClientAdaptor.ServerStatus;
            }
            else if ( e.PropertyName == "RefreshOnReconnection" )
            {
                if ( OPCUAClientAdaptor.RefreshOnReconnection ) InitializeViews( );
            }
            else if ( e.PropertyName == "ActivateTabsonReConnection" )
            {
                TabItemEnabled = OPCUAClientAdaptor.ActivateTabsonReConnection;
            }
        }

        private void InitializeViews( )
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var item in TabItems)
                {
                    if (item.Content is SecurityGroupConfigurationView)
                        (item.Content as SecurityGroupConfigurationView).ViewModel.Initialize();
                    if (item.Content is PubSubStatusView) (item.Content as PubSubStatusView).ViewModel.Initialize();

                    if (item.Content is PublishedDataSetView)
                        (item.Content as PublishedDataSetView).ViewModel.Initialize(Rootnode);

                    if (item.Content is PubSubConfigurationView)
                        (item.Content as PubSubConfigurationView).ViewModel.Initialize();

                     
                }
            });
        }

        #endregion

        #region Constructors

        public MainViewModel( )
        {
            OPCUAClientAdaptor = new OPCUAClientAdaptor( );
            OPCUAClientAdaptor.PropertyChanged += OPCUAClientAdaptor_PropertyChanged;
            _SecurityGroupConfigurationView = new SecurityGroupConfigurationView( OPCUAClientAdaptor );
            _PubSubConfigurationView = new PubSubConfigurationView( OPCUAClientAdaptor );
            _PublisherDataSetView = new PublishedDataSetView( OPCUAClientAdaptor );
            _PubSubStatusView = new PubSubStatusView( OPCUAClientAdaptor );
            TabItems.Add( new TabItem
                          {
                              Header = "Security Group Configuration",
                              Content = _SecurityGroupConfigurationView
                          } );
            TabItems.Add( new TabItem { Header = "PubSub Configuration", Content = _PubSubConfigurationView } );
            TabItems.Add( new TabItem { Header = "Publisher DataSet Configuration", Content = _PublisherDataSetView } );
            TabItems.Add( new TabItem { Header = "PubSub Status", Content = _PubSubStatusView } );
        }

        #endregion

        #region Public Methods

        public bool Connect( string endPointURL )
        {
            var errorMessage = string.Empty;

            _Session = OPCUAClientAdaptor.Connect( endPointURL, out errorMessage, out Rootnode );

            if ( _Session == null )
            {
                ServerStatus = errorMessage;
                return false;
            }
            ServerStatus = "Running";
            InitializeViews( );

            SelectedEndPoint = OPCUAClientAdaptor.SelectedEndpoint;
            TabItemEnabled = true;
            return true;
        }

        public bool DisConnect( )
        {
            TabItemEnabled = false;
            return OPCUAClientAdaptor.Disconnect( );
        }

        #endregion

        public OPCUAClientAdaptor OPCUAClientAdaptor;
        public static TreeViewNode Rootnode;
        public static SecurityGroupConfigurationView _SecurityGroupConfigurationView;
        public static PubSubConfigurationView _PubSubConfigurationView;
        public static PublishedDataSetView _PublisherDataSetView;
        public static PubSubStatusView _PubSubStatusView;

        #region Public Properties

        private ObservableCollection< ServerNode > _selectedServers = new ObservableCollection< ServerNode >( );

        public ObservableCollection< ServerNode > SelectedServers
        {
            get { return _selectedServers; }
            set
            {
                _selectedServers = value;
                OnPropertyChanged( "SelectedServers" );
            }
        }

        public string _ServerStatusColor = Brushes.Orange.ToString( );

        public string ServerStatusColor
        {
            get { return _ServerStatusColor; }
            set
            {
                _ServerStatusColor = value;
                OnPropertyChanged( "ServerStatusColor" );
            }
        }

        private string _ServerStatus = "Not Connected";

        public string ServerStatus
        {
            get { return _ServerStatus; }
            set
            {
                _ServerStatus = value;

                OnPropertyChanged( "ServerStatus" );
            }
        }

        public bool _IsConnectButtonVisible = true;

        public bool IsConnectButtonVisible
        {
            get { return _IsConnectButtonVisible; }
            set
            {
                _IsConnectButtonVisible = value;
                OnPropertyChanged( "IsConnectButtonVisible" );
            }
        }

        public bool _IsDisConnectButtonVisible;

        public bool IsDisConnectButtonVisible
        {
            get { return _IsDisConnectButtonVisible; }
            set
            {
                _IsDisConnectButtonVisible = value;
                OnPropertyChanged( "IsDisConnectButtonVisible" );
            }
        }

        private string _SelectedEndPoint;

        public string SelectedEndPoint
        {
            get { return _SelectedEndPoint; }
            set { _SelectedEndPoint = value; }
        }

        private ObservableCollection< TabItem > _TabItems = new ObservableCollection< TabItem >( );

        public ObservableCollection< TabItem > TabItems
        {
            get { return _TabItems; }
            set
            {
                _TabItems = value;
                OnPropertyChanged( "TabItems" );
            }
        }

        private bool _TabItemEnabled;

        public bool TabItemEnabled
        {
            get { return _TabItemEnabled; }
            set
            {
                _TabItemEnabled = value;
                OnPropertyChanged( "TabItemEnabled" );
            }
        }

        #endregion
    }
}