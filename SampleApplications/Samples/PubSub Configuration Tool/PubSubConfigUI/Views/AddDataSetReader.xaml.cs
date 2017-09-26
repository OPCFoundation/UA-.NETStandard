using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using ClientAdaptor;
using Opc.Ua;
using PubSubBase.Definitions;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for AddDataSetReader.xaml
    /// </summary>
    public partial class AddDataSetReader : Window
    {
        #region Private Member 

        private readonly ObservableCollection< PubSubConfiguationBase > m_localPubSubCollectionItems;
        private readonly Dictionary< string, int > m_dicControlBitPositionmappping = new Dictionary< string, int >( );

        private readonly Dictionary< string, int > m_dicNetworkMessageControlBitPositionmappping =
        new Dictionary< string, int >( );

        private ObservableCollection< PublishedDataSetBase > m_remotePublishedDataSetBaseCollection =
        new ObservableCollection< PublishedDataSetBase >( );

        private ObservableCollection< PubSubConfiguationBase > m_remotePubSubCollectionItems =
        new ObservableCollection< PubSubConfiguationBase >( );

        private TreeViewNode _rootnode;

        #endregion

        #region Private Methods

        private void PubSubTreeView_OnSelectedItemChanged( object sender, RoutedPropertyChangedEventArgs< object > e )
        {
            if ( PubSubTreeView.SelectedItem is DataSetWriterDefinition )
            {
                var selectedDataSetWriterDefinition = PubSubTreeView.SelectedItem as DataSetWriterDefinition;
                DataSetWriterId.Text = selectedDataSetWriterDefinition.DataSetWriterId.ToString( );
                PublishedDataSetDefinition publishedDataSetDefinition = null;
                if ( RemoteConnection.IsChecked == false )
                    publishedDataSetDefinition = MainViewModel
                    ._PublisherDataSetView.ViewModel.PublishedDataSetCollection
                    .Where( i => (i as PublishedDataSetDefinition).PublishedDataSetNodeId ==
                                 selectedDataSetWriterDefinition.PublisherDataSetNodeId )
                    .FirstOrDefault( ) as PublishedDataSetDefinition;
                else
                    publishedDataSetDefinition = m_remotePublishedDataSetBaseCollection
                    .Where( i => (i as PublishedDataSetDefinition).PublishedDataSetNodeId ==
                                 selectedDataSetWriterDefinition.PublisherDataSetNodeId )
                    .FirstOrDefault( ) as PublishedDataSetDefinition;
                if ( publishedDataSetDefinition != null )
                {
                    var _DataSetMetaDataDefinition =
                    publishedDataSetDefinition.Children[ 0 ] as DataSetMetaDataDefinition;
                    DataSetMetaDataType = _DataSetMetaDataDefinition.DataSetMetaDataType;
                    PublishingIterval.Text = (selectedDataSetWriterDefinition.ParentNode as DataSetWriterGroup)
                    .PublishingInterval.ToString( );
                    _connectionPublisherId = ((selectedDataSetWriterDefinition.ParentNode as DataSetWriterGroup)
                        .ParentNode as Connection).PublisherId;
                    PublisherId.Text = _connectionPublisherId.ToString( );
                    ApplyContentMask( selectedDataSetWriterDefinition );
                }
            }
        }

        private void Apply_Click( object sender, RoutedEventArgs e )
        {
            //Validate here
            if ( string.IsNullOrWhiteSpace( DataSetWriterId.Text ) )
            {
                MessageBox.Show( "DataSetWriter Id cannot be empty", "Add DataSet Reader" );
                return;
            }
            if ( string.IsNullOrWhiteSpace( DataSetReaderName.Text ) )
            {
                MessageBox.Show( "DataSetReader Name cannot be empty", "Add DataSet Reader" );
                return;
            }

            _dataSetContentMask = GetDataSetContentMask( );
            _networkMessageContentMask = GetNetworkContentMask( );
            //ApplyContentMask();

            _isApplied = true;
            Close( );
        }

        private void ApplyContentMask( DataSetWriterDefinition selectedDataSetWriterDefinition )
        {
            //foreach (CheckBox checkbox in new CheckBox[] { Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6, Chk_box7, Chk_box8, Chk_box9, Chk_box10, Chk_box11, Chk_box12 })
            //{
            //    int shiftNumber = 1;
            //    if (checkbox.IsChecked == true)
            //    {
            //        int bitposition = DicControlBitPositionmappping[checkbox.Name];
            //        shiftNumber = 1 << bitposition;
            //        DataSetContentMask = DataSetContentMask | shiftNumber;
            //    }
            //}
            //foreach (CheckBox checkbox in new CheckBox[] { NM_Chk_box1, NM_Chk_box2, NM_Chk_box3, NM_Chk_box4, NM_Chk_box5, NM_Chk_box6, NM_Chk_box7, NM_Chk_box8, NM_Chk_box9, NM_Chk_box10, NM_Chk_box11 })
            //{
            //    int shiftNumber = 1;
            //    if (checkbox.IsChecked == true)
            //    {
            //        int bitposition = DicNetworkMessageControlBitPositionmappping[checkbox.Name];
            //        shiftNumber = 1 << bitposition;
            //        NetworkMessageContentMask = NetworkMessageContentMask | shiftNumber;
            //    }
            //}
            _dataSetContentMask = selectedDataSetWriterDefinition.DataSetContentMask;
            _networkMessageContentMask = (selectedDataSetWriterDefinition.ParentNode as DataSetWriterGroup)
            .NetworkMessageContentMask;
            foreach ( var checkbox in new[ ]
                                      {
                                          Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6, Chk_box7,
                                          Chk_box8, Chk_box9, Chk_box10, Chk_box11, Chk_box12
                                      } )
            {
                var shiftNumber = 1;
                var bitposition = m_dicControlBitPositionmappping[ checkbox.Name ];
                shiftNumber = 1 << bitposition;
                checkbox.IsChecked = (_dataSetContentMask & shiftNumber) == shiftNumber ? true : false;

                // checkbox.IsEnabled = false;
            }
            foreach ( var checkbox in new[ ]
                                      {
                                          NM_Chk_box1, NM_Chk_box2, NM_Chk_box3, NM_Chk_box4, NM_Chk_box5,
                                          NM_Chk_box6, NM_Chk_box7, NM_Chk_box8, NM_Chk_box9, NM_Chk_box10,
                                          NM_Chk_box11
                                      } )
            {
                var shiftNumber = 1;
                var bitposition = m_dicNetworkMessageControlBitPositionmappping[ checkbox.Name ];
                shiftNumber = 1 << bitposition;
                checkbox.IsChecked = (_networkMessageContentMask & shiftNumber) == shiftNumber ? true : false;

                // checkbox.IsEnabled = false;
            }
        }

        private void Cancel_Click( object sender, RoutedEventArgs e )
        {
            _isApplied = false;
            Close( );
        }

        private void RemoteConnectionChecked( object sender, RoutedEventArgs e )
        {
            ConnectionPanel.IsEnabled = true;
            PubSubTreeView.Items.Clear( );
            foreach ( var pubSubConfiguationBase in m_remotePubSubCollectionItems )
                PubSubTreeView.Items.Add( pubSubConfiguationBase );
        }

        private void RemoteConnectionUnChecked( object sender, RoutedEventArgs e )
        {
            ConnectionPanel.IsEnabled = false;
            PubSubTreeView.Items.Clear( );
            foreach ( var pubSubConfiguationBase in m_localPubSubCollectionItems )
                PubSubTreeView.Items.Add( pubSubConfiguationBase );
        }

        private void OnConnectClick( object sender, RoutedEventArgs e )
        {
            ClientAdaptor.Disconnect( );
            Connect( TextConnectionURL.Text.Trim( ) );
        }

        private void OnFindServerClick( object sender, RoutedEventArgs e )
        {
            var findServerDlg = new FindServerDlg( ClientAdaptor as OPCUAClientAdaptor );
            findServerDlg.Closing += _FindServerDlg_Closing;
            findServerDlg.ShowInTaskbar = false;
            findServerDlg.ShowDialog( );
        }

        private void _FindServerDlg_Closing( object sender, CancelEventArgs e )
        {
            var findServerDlg = sender as FindServerDlg;
            if ( findServerDlg != null && findServerDlg._selectedServer != null ) TextConnectionURL.Text = findServerDlg._selectedServer.Name;
        }

        private void ViewDataSetMeta_Click( object sender, RoutedEventArgs e )
        {
            if ( DataSetMetaDataType == null )
            {
                MessageBox.Show(
                    "DataSet Metadata is not configured yet. you need to select the dataset writer to view its detaset meta information",
                    "View DataSet MetaData" );
                return;
            }
            var dataSetReaderDefinition = PubSubTreeView.SelectedItem as DataSetReaderDefinition;

            var dataSetMetaDataUserControl = new DataSetMetaDataUserControl( );
            dataSetMetaDataUserControl.DataSetMetaDataEditViewModel.Definition.DataSetMetaDataType =
            DataSetMetaDataType;
            dataSetMetaDataUserControl.DataSetMetaDataEditViewModel.Initialize( );

            var viewDataSetMetaData = new ViewDataSetMetaData( );

            viewDataSetMetaData.ContentControl.Content = dataSetMetaDataUserControl;
            viewDataSetMetaData.ShowInTaskbar = false;
            viewDataSetMetaData.ShowDialog( );
        }

        #endregion

        #region Constructors

        public AddDataSetReader( ObservableCollection< PubSubConfiguationBase > pubSubCollectionItems )
        {
            InitializeComponent( );
            ConnectionPanel.IsEnabled = false;
            ClientAdaptor = new OPCUAClientAdaptor( );
            foreach ( var _PubSubConfiguationBase in pubSubCollectionItems )
                PubSubTreeView.Items.Add( _PubSubConfiguationBase );
            m_localPubSubCollectionItems = pubSubCollectionItems;
            m_dicControlBitPositionmappping[ "Chk_box1" ] = 0;
            m_dicControlBitPositionmappping[ "Chk_box2" ] = 1;
            m_dicControlBitPositionmappping[ "Chk_box3" ] = 2;
            m_dicControlBitPositionmappping[ "Chk_box4" ] = 3;
            m_dicControlBitPositionmappping[ "Chk_box5" ] = 4;
            m_dicControlBitPositionmappping[ "Chk_box6" ] = 5;
            m_dicControlBitPositionmappping[ "Chk_box7" ] = 16;
            m_dicControlBitPositionmappping[ "Chk_box8" ] = 17;
            m_dicControlBitPositionmappping[ "Chk_box9" ] = 18;
            m_dicControlBitPositionmappping[ "Chk_box10" ] = 19;
            m_dicControlBitPositionmappping[ "Chk_box11" ] = 20;
            m_dicControlBitPositionmappping[ "Chk_box12" ] = 21;

            m_dicNetworkMessageControlBitPositionmappping[ "NM_Chk_box1" ] = 0;
            m_dicNetworkMessageControlBitPositionmappping[ "NM_Chk_box2" ] = 1;
            m_dicNetworkMessageControlBitPositionmappping[ "NM_Chk_box3" ] = 2;
            m_dicNetworkMessageControlBitPositionmappping[ "NM_Chk_box4" ] = 3;
            m_dicNetworkMessageControlBitPositionmappping[ "NM_Chk_box5" ] = 4;
            m_dicNetworkMessageControlBitPositionmappping[ "NM_Chk_box6" ] = 5;
            m_dicNetworkMessageControlBitPositionmappping[ "NM_Chk_box7" ] = 6;
            m_dicNetworkMessageControlBitPositionmappping[ "NM_Chk_box8" ] = 7;
            m_dicNetworkMessageControlBitPositionmappping[ "NM_Chk_box9" ] = 8;
            m_dicNetworkMessageControlBitPositionmappping[ "NM_Chk_box10" ] = 9;
            m_dicNetworkMessageControlBitPositionmappping[ "NM_Chk_box11" ] = 10;
        }

        #endregion

        #region Public Property

        public IOPCUAClientAdaptor ClientAdaptor { get; set; }

        public DataSetMetaDataType DataSetMetaDataType { get; set; }

        #endregion

        #region Public Methods

        public void Connect( string endPointUrl )
        {
            var errorMessage = string.Empty;

            var session = ClientAdaptor.Connect( endPointUrl, out errorMessage, out _rootnode );

            if ( session == null )
            {
                //MessageBox.Show( (ClientAdaptor as OPCUAClientAdaptor).ServerStatus, "Add DataSet Reader" );
                MessageBox.Show("Enter the valid URL" ,"Add DataSet Reader");
                return;
            }
            m_remotePubSubCollectionItems = ClientAdaptor.GetPubSubConfiguation( );
            PubSubTreeView.Items.Clear( );
            foreach ( var pubSubConfiguationBase in m_remotePubSubCollectionItems )
                PubSubTreeView.Items.Add( pubSubConfiguationBase );
            m_remotePublishedDataSetBaseCollection = ClientAdaptor.GetPublishedDataSets( );
        }

        public void Disconnect( )
        {
            ClientAdaptor.Disconnect( );
        }

        public int GetDataSetContentMask( )
        {
            var dataSetContentMask = 0;
            foreach ( var checkbox in new[ ]
                                      {
                                          Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6, Chk_box7,
                                          Chk_box8, Chk_box9, Chk_box10, Chk_box11, Chk_box12
                                      } )
            {
                var shiftNumber = 1;
                if ( checkbox.IsChecked == true )
                {
                    var bitposition = m_dicControlBitPositionmappping[ checkbox.Name ];
                    shiftNumber = 1 << bitposition;
                    dataSetContentMask = dataSetContentMask | shiftNumber;
                }
            }
            return dataSetContentMask;
        }

        public int GetNetworkContentMask( )
        {
            var networkMessageContentMask = 0;
            foreach ( var checkbox in new[ ]
                                      {
                                          NM_Chk_box1, NM_Chk_box2, NM_Chk_box3, NM_Chk_box4, NM_Chk_box5,
                                          NM_Chk_box6, NM_Chk_box7, NM_Chk_box8, NM_Chk_box9, NM_Chk_box10,
                                          NM_Chk_box11
                                      } )
            {
                var shiftNumber = 1;
                if ( checkbox.IsChecked == true )
                {
                    var bitposition = m_dicNetworkMessageControlBitPositionmappping[ checkbox.Name ];
                    shiftNumber = 1 << bitposition;
                    networkMessageContentMask = networkMessageContentMask | shiftNumber;
                }
            }
            return networkMessageContentMask;
        }

        #endregion

        public object _connectionPublisherId = "0";
        public int _dataSetContentMask;
        public int _networkMessageContentMask;
        public bool _isApplied;
    }
}