using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using ClientAdaptor;
using Opc.Ua;
using PubSubBase.Definitions;
using PubSubConfigurationUI.ViewModels;
using System.Text.RegularExpressions;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for AddDataSetReader.xaml
    /// </summary>
    public partial class AddDataSetReader : Window
    {
        #region Private Member 

        private readonly ObservableCollection<PubSubConfiguationBase> m_localPubSubCollectionItems;
        private readonly Dictionary<string, int> m_dicControlBitPositionmappping = new Dictionary<string, int>();

        private readonly Dictionary<string, int> m_dicNetworkMessageControlBitPositionmappping =
        new Dictionary<string, int>();

        private readonly Dictionary<string, int> m_uadpDataSetdicControlBitPositionmappping =
      new Dictionary<string, int>();

        private readonly Dictionary<string, int> m_jsonDataSetdicControlBitPositionmappping =
     new Dictionary<string, int>();

        private readonly Dictionary<string, int> m_jsonNetworkdicControlBitPositionmappping =
   new Dictionary<string, int>();


        private ObservableCollection<PublishedDataSetBase> m_remotePublishedDataSetBaseCollection =
        new ObservableCollection<PublishedDataSetBase>();

        private ObservableCollection<PubSubConfiguationBase> m_remotePubSubCollectionItems =
        new ObservableCollection<PubSubConfiguationBase>();

        private TreeViewNode _rootnode;

        #endregion

        #region Private Methods

        private void PubSubTreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (PubSubTreeView.SelectedItem is DataSetWriterDefinition)
            {
                var selectedDataSetWriterDefinition = PubSubTreeView.SelectedItem as DataSetWriterDefinition;

                DataSetWriterId.Text = selectedDataSetWriterDefinition.DataSetWriterId.ToString();
                PublishedDataSetDefinition publishedDataSetDefinition = null;
                if (RemoteConnection.IsChecked == false)
                    publishedDataSetDefinition = MainViewModel
                    ._PublisherDataSetView.ViewModel.PublishedDataSetCollection
                    .Where(i => (i as PublishedDataSetDefinition).PublishedDataSetNodeId ==
                                selectedDataSetWriterDefinition.PublisherDataSetNodeId)
                    .FirstOrDefault() as PublishedDataSetDefinition;
                else
                    publishedDataSetDefinition = m_remotePublishedDataSetBaseCollection
                    .Where(i => (i as PublishedDataSetDefinition).PublishedDataSetNodeId ==
                                selectedDataSetWriterDefinition.PublisherDataSetNodeId)
                    .FirstOrDefault() as PublishedDataSetDefinition;
                if (publishedDataSetDefinition != null)
                {
                    var _DataSetMetaDataDefinition =
                    publishedDataSetDefinition.Children[0] as DataSetMetaDataDefinition;
                    DataSetMetaDataType = _DataSetMetaDataDefinition.DataSetMetaDataType;
                    PublishingIterval.Text = (selectedDataSetWriterDefinition.ParentNode as DataSetWriterGroup)
                    .PublishingInterval.ToString();
                    _connectionPublisherId = ((selectedDataSetWriterDefinition.ParentNode as DataSetWriterGroup)
                        .ParentNode as Connection).PublisherId;
                    PublisherId.Text = _connectionPublisherId.ToString();
                    WriterGroupid = (selectedDataSetWriterDefinition.ParentNode as DataSetWriterGroup).WriterGroupId;
                    WriterGroupId.Text = WriterGroupid.ToString();
                    SecurityGroupid = (selectedDataSetWriterDefinition.ParentNode as DataSetWriterGroup).SecurityGroupId;
                    SecurityGroupId.Text = SecurityGroupid;
                    DataSetWriterId.Text = selectedDataSetWriterDefinition.DataSetWriterId.ToString();

                    MessageSecurityMode.SelectedIndex = (selectedDataSetWriterDefinition.ParentNode as DataSetWriterGroup).MessageSecurityMode;
                    TransportSetting = selectedDataSetWriterDefinition.TransportSetting;
                    MessageSetting = selectedDataSetWriterDefinition.MessageSetting;

                    if (TransportSetting == 1)
                    {
                        ChangeVisibilityofBrokerTransportControls();
                        TransportSelect.SelectedIndex = 1;
                        QueueName = selectedDataSetWriterDefinition.QueueName;
                        ResourceUri = selectedDataSetWriterDefinition.ResourceUri;
                        AuthenticationProfileUri = selectedDataSetWriterDefinition.AuthenticationProfileUri;
                        RequestedDeliveryGuarantee = selectedDataSetWriterDefinition.RequestedDeliveryGuarantee;
                        MetaDataQueueName = selectedDataSetWriterDefinition.MetadataQueueName;
                    }
                    if (MessageSetting == 0)
                    {
                        ChangeVisibilityofUadpMessageControls();
                        MessageSelect.SelectedIndex = 0;
                        GroupVersion = (selectedDataSetWriterDefinition.ParentNode as DataSetWriterGroup).GroupVersion;
                        Groupversion.Text = GroupVersion.ToString();
                        NetworkMessageNumber = selectedDataSetWriterDefinition.NetworkMessageNumber;
                        Networkmessagenumber.Text = NetworkMessageNumber.ToString();
                        DataSetOffset = selectedDataSetWriterDefinition.DataSetOffset;
                        datasetoffset.Text = DataSetOffset.ToString();
                        DataSetClassId = Guid.NewGuid();
                        // DatasetclassId.Text = DataSetClassId.ToString();
                        _uadpDatasetMessageContentMask = selectedDataSetWriterDefinition.UadpDataSetMessageContentMask;
                        _UadpNetworkMessageContentMask = (selectedDataSetWriterDefinition.ParentNode as DataSetWriterGroup).UadpNetworkMessageContentMask;
                    }
                    else
                    {
                        ChangeVisibilityofBrokerMessageControls();
                        MessageSelect.SelectedIndex = 1;
                        _JsonDatasetMessageContentMask = selectedDataSetWriterDefinition.JsonDataSetMessageContentMask;
                        _JsonNetworkMessageContentMask = (selectedDataSetWriterDefinition.ParentNode as DataSetWriterGroup).JsonNetworkMessageContentMask;
                    }

                    ApplyContentMask(selectedDataSetWriterDefinition);
                }
            }
        }

        private void ChangeVisibilityofBrokerMessageControls()
        {
            JsonDatasetMask.Visibility = JsonNetworkMessage.Visibility = Visibility.Visible;
        }

        private void ChangeVisibilityofUadpMessageControls()
        {
            Groupversion.Visibility = Groupversionlbl.Visibility = Visibility.Visible;
            Networkmessagenumber.Visibility = Networkmessagenumberlbl.Visibility = Visibility.Visible;
            datasetoffset.Visibility = datasetoffsetlbl.Visibility = Visibility.Visible;
            UadpDatasetMask.Visibility = UadpNetworkmask.Visibility = Visibility.Visible;
            // DatasetclassId.Visibility = DatasetclassIdlbl.Visibility = Visibility.Visible;
            Receiveoffset.Visibility = Receiveoffsetlbl.Visibility = Visibility.Visible;
            Processingoffset.Visibility = Processingoffsetlbl.Visibility = Visibility.Visible;
        }

        private void ChangeVisibilityofBrokerTransportControls()
        {
            Queuename.Visibility = Queuenamelbl.Visibility = Visibility.Visible;
            Resourceuri.Visibility = Resourceurilbl.Visibility = Visibility.Visible;
            Authenticationprofileuri.Visibility = Authenticationprofileurilbl.Visibility = Visibility.Visible;
            Requesteddeliveryguarantee.Visibility = Requesteddeliveryguaranteelbl.Visibility = Visibility.Visible;
            MetadataQueuename.Visibility = MetadataQueuenamelbl.Visibility = Visibility.Visible;

        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            //Validate here
            if (string.IsNullOrWhiteSpace(DataSetWriterId.Text) || string.IsNullOrWhiteSpace(DataSetReaderName.Text) || string.IsNullOrWhiteSpace(MessageReceiveTimeout.Text) || string.IsNullOrWhiteSpace(Processingoffset.Text) || string.IsNullOrWhiteSpace(Receiveoffset.Text) || string.IsNullOrWhiteSpace(datasetoffset.Text))
            {
                MessageBox.Show("Mandatory fields cannot be empty.", "Add DataSet Reader");
                return;
            }

            DatasetReaderName = DataSetReaderName.Text;
            _messageReceiveTimeout = Convert.ToDouble(MessageReceiveTimeout.Text);
            dataSetWriterId = Convert.ToUInt16(DataSetWriterId.Text);
            publisherId = _connectionPublisherId;
            _publishingInterval = Convert.ToInt16(PublishingIterval.Text);
            WriterGroupid = Convert.ToUInt16(WriterGroupId.Text);
            _messageSecurityMode = MessageSecurityMode.SelectedIndex;
            SecurityGroupid = SecurityGroupId.Text;

            _dataSetfieldContentMask = GetDataSetContentMask();

            if (MessageSetting == 0)
            {

                GroupVersion = Convert.ToUInt32(Groupversion.Text);
                NetworkMessageNumber = Convert.ToUInt16(Networkmessagenumber.Text);

                DataSetOffset = Convert.ToUInt16(datasetoffset.Text);

                _receiveOffset = Convert.ToInt16(Receiveoffset.Text);
                ProcessingOffset = Convert.ToInt16(Processingoffset.Text);

                _uadpDatasetMessageContentMask = GetUadpDataSetNetworkContentMask();
                _UadpNetworkMessageContentMask = GetUadpNetworkMessageContentMask();

            }
            else
            {
                _JsonDatasetMessageContentMask = GetJsonDataSetMessageContentMask();
                _JsonNetworkMessageContentMask = GetJsonNetworkMessageContentMask();
            }

            if (TransportSetting == 1)
            {
                QueueName = Queuename.Text;
                MetaDataQueueName = MetadataQueuename.Text;
                ResourceUri = Resourceuri.Text;
                AuthenticationProfileUri = Authenticationprofileuri.Text;
                RequestedDeliveryGuarantee = Requesteddeliveryguarantee.SelectedIndex;
            }

            _isApplied = true;
            Close();
        }

        private int GetJsonNetworkMessageContentMask()
        {
            var networkMessageContentMask = 0;
            foreach (var checkbox in new[]
                                      {
                                          JN_Chk_box1, JN_Chk_box2, JN_Chk_box3, JN_Chk_box4, JN_Chk_box5,
                                          JN_Chk_box6
                                      })
            {
                if (checkbox.IsChecked == true)
                {
                    var enumvalue = m_jsonNetworkdicControlBitPositionmappping[checkbox.Name];
                    networkMessageContentMask = networkMessageContentMask | enumvalue;
                }
            }
            return networkMessageContentMask;
        }

        private int GetJsonDataSetMessageContentMask()
        {
            var dataSetContentMask = 0;
            foreach (var checkbox in new[]
                                      {
                                          JNChk_box1, JNChk_box2, JNChk_box3, JNChk_box4, JNChk_box5
                                      })
            {
                if (checkbox.IsChecked == true)
                {
                    var enumValue = m_jsonDataSetdicControlBitPositionmappping[checkbox.Name];
                    dataSetContentMask = dataSetContentMask | enumValue;
                }
            }


            return dataSetContentMask;
        }

        private int GetUadpNetworkMessageContentMask()
        {
            var networkMessageContentMask = 0;
            foreach (var checkbox in new[]
                                      {
                                          NM_Chk_box1, NM_Chk_box2, NM_Chk_box3, NM_Chk_box4, NM_Chk_box5,
                                          NM_Chk_box6, NM_Chk_box7, NM_Chk_box8, NM_Chk_box9, NM_Chk_box10,
                                          NM_Chk_box11
                                      })
            {
                if (checkbox.IsChecked == true)
                {
                    var enumValue = m_dicNetworkMessageControlBitPositionmappping[checkbox.Name];
                    networkMessageContentMask = networkMessageContentMask | enumValue;
                }
            }
            return networkMessageContentMask;
        }

        public int GetDataSetContentMask()
        {
            var dataSetContentMask = 0;
            foreach (var checkbox in new[]
                                      {
                                          Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6
                                      })
            {

                if (checkbox.IsChecked == true)
                {
                    var enumvalue = m_dicControlBitPositionmappping[checkbox.Name];
                    dataSetContentMask = dataSetContentMask | enumvalue;
                }
            }


            return dataSetContentMask;
        }

        public int GetUadpDataSetNetworkContentMask()
        {
            var networkMessageContentMask = 0;
            foreach (var checkbox in new[]
                                      {
                                          UDChk_box1, UDChk_box2, UDChk_box3, UDChk_box4, UDChk_box5,
                                          UDChk_box6
                                      })
            {
                if (checkbox.IsChecked == true)
                {
                    var enumValue = m_uadpDataSetdicControlBitPositionmappping[checkbox.Name];
                    networkMessageContentMask = networkMessageContentMask | enumValue;
                }
            }
            return networkMessageContentMask;
        }

        private void ApplyContentMask(DataSetWriterDefinition selectedDataSetWriterDefinition)
        {
            _dataSetfieldContentMask = selectedDataSetWriterDefinition.DataSetContentMask;
            foreach (var checkbox in new[]
                                      {
                                          Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6
                                      })
            {

                var enumValue = m_dicControlBitPositionmappping[checkbox.Name];
                checkbox.IsChecked = (_dataSetfieldContentMask & enumValue) == enumValue ? true : false;

            }

            if (MessageSetting == 0)
            {
                _uadpDatasetMessageContentMask = selectedDataSetWriterDefinition.UadpDataSetMessageContentMask;
                foreach (var checkbox in new[]
                                    {
                                          UDChk_box1, UDChk_box2, UDChk_box3, UDChk_box4, UDChk_box5, UDChk_box6
                                      })
                {

                    var enumValue = m_uadpDataSetdicControlBitPositionmappping[checkbox.Name];
                    checkbox.IsChecked = (_uadpDatasetMessageContentMask & enumValue) == enumValue ? true : false;

                }


                _UadpNetworkMessageContentMask = (selectedDataSetWriterDefinition.ParentNode as DataSetWriterGroup).UadpNetworkMessageContentMask;
                foreach (var checkbox in new[]
                                    {
                                          NM_Chk_box1, NM_Chk_box2, NM_Chk_box3, NM_Chk_box4, NM_Chk_box5, NM_Chk_box6,NM_Chk_box7,NM_Chk_box8,NM_Chk_box9,NM_Chk_box10,NM_Chk_box11
                                      })
                {

                    var enumValue = m_dicNetworkMessageControlBitPositionmappping[checkbox.Name];
                    checkbox.IsChecked = (_UadpNetworkMessageContentMask & enumValue) == enumValue ? true : false;

                }

            }
            else
            {
                _JsonDatasetMessageContentMask = selectedDataSetWriterDefinition.JsonDataSetMessageContentMask;
                foreach (var checkbox in new[]
                                  {
                                          JNChk_box1, JNChk_box2, JNChk_box3, JNChk_box4, JNChk_box5
                                      })
                {
                    var enumValue = m_jsonDataSetdicControlBitPositionmappping[checkbox.Name];
                    checkbox.IsChecked = (_JsonDatasetMessageContentMask & enumValue) == enumValue ? true : false;

                }


                _JsonNetworkMessageContentMask = (selectedDataSetWriterDefinition.ParentNode as DataSetWriterGroup).JsonNetworkMessageContentMask;
                foreach (var checkbox in new[]
                                {
                                          JN_Chk_box1, JN_Chk_box2, JN_Chk_box3, JN_Chk_box4, JN_Chk_box5,JN_Chk_box6
                                      })
                {
                    var enumValue = m_jsonNetworkdicControlBitPositionmappping[checkbox.Name];
                    checkbox.IsChecked = (_JsonNetworkMessageContentMask & enumValue) == enumValue ? true : false;
                }
            }

        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _isApplied = false;
            Close();
        }

        private void RemoteConnectionChecked(object sender, RoutedEventArgs e)
        {
            ConnectionPanel.IsEnabled = true;
            PubSubTreeView.Items.Clear();
            foreach (var pubSubConfiguationBase in m_remotePubSubCollectionItems)
                PubSubTreeView.Items.Add(pubSubConfiguationBase);
        }

        private void RemoteConnectionUnChecked(object sender, RoutedEventArgs e)
        {
            ConnectionPanel.IsEnabled = false;
            PubSubTreeView.Items.Clear();
            foreach (var pubSubConfiguationBase in m_localPubSubCollectionItems)
                PubSubTreeView.Items.Add(pubSubConfiguationBase);
        }

        private void OnConnectClick(object sender, RoutedEventArgs e)
        {
            ClientAdaptor.Disconnect();
            Connect(TextConnectionURL.Text.Trim());
        }

        private void OnFindServerClick(object sender, RoutedEventArgs e)
        {
            var findServerDlg = new FindServerDlg(ClientAdaptor as OPCUAClientAdaptor);
            findServerDlg.Closing += _FindServerDlg_Closing;
            findServerDlg.ShowInTaskbar = false;
            findServerDlg.ShowDialog();
        }

        private void _FindServerDlg_Closing(object sender, CancelEventArgs e)
        {
            var findServerDlg = sender as FindServerDlg;
            if (findServerDlg != null && findServerDlg._selectedServer != null) TextConnectionURL.Text = findServerDlg._selectedServer.Name;
        }

        private void ViewDataSetMeta_Click(object sender, RoutedEventArgs e)
        {
            if (DataSetMetaDataType == null)
            {
                MessageBox.Show(
                    "DataSet Metadata is not configured yet. you need to select the dataset writer to view its detaset meta information",
                    "View DataSet MetaData");
                return;
            }
            var dataSetReaderDefinition = PubSubTreeView.SelectedItem as DataSetReaderDefinition;

            var dataSetMetaDataUserControl = new DataSetMetaDataUserControl();
            dataSetMetaDataUserControl.DataSetMetaDataEditViewModel.Definition.DataSetMetaDataType =
            DataSetMetaDataType;
            dataSetMetaDataUserControl.DataSetMetaDataEditViewModel.Initialize();

            var viewDataSetMetaData = new ViewDataSetMetaData();

            viewDataSetMetaData.ContentControl.Content = dataSetMetaDataUserControl;
            viewDataSetMetaData.ShowInTaskbar = false;
            viewDataSetMetaData.ShowDialog();
        }

        #endregion

        #region Constructors

        public AddDataSetReader(ObservableCollection<PubSubConfiguationBase> pubSubCollectionItems)
        {

            InitializeComponent();

            ConnectionPanel.IsEnabled = false;
            ClientAdaptor = new OPCUAClientAdaptor();

            foreach (var _PubSubConfiguationBase in pubSubCollectionItems)
                PubSubTreeView.Items.Add(_PubSubConfiguationBase);
            m_localPubSubCollectionItems = pubSubCollectionItems;

            m_dicControlBitPositionmappping["Chk_box1"] = 1;
            m_dicControlBitPositionmappping["Chk_box2"] = 2;
            m_dicControlBitPositionmappping["Chk_box3"] = 4;
            m_dicControlBitPositionmappping["Chk_box4"] = 8;
            m_dicControlBitPositionmappping["Chk_box5"] = 16;
            m_dicControlBitPositionmappping["Chk_box6"] = 32;


            m_dicNetworkMessageControlBitPositionmappping["NM_Chk_box1"] = 1;
            m_dicNetworkMessageControlBitPositionmappping["NM_Chk_box2"] = 2;
            m_dicNetworkMessageControlBitPositionmappping["NM_Chk_box3"] = 4;
            m_dicNetworkMessageControlBitPositionmappping["NM_Chk_box4"] = 8;
            m_dicNetworkMessageControlBitPositionmappping["NM_Chk_box5"] = 16;
            m_dicNetworkMessageControlBitPositionmappping["NM_Chk_box6"] = 32;
            m_dicNetworkMessageControlBitPositionmappping["NM_Chk_box7"] = 64;
            m_dicNetworkMessageControlBitPositionmappping["NM_Chk_box8"] = 128;
            m_dicNetworkMessageControlBitPositionmappping["NM_Chk_box9"] = 256;
            m_dicNetworkMessageControlBitPositionmappping["NM_Chk_box10"] = 512;
            m_dicNetworkMessageControlBitPositionmappping["NM_Chk_box11"] = 1024;

            m_uadpDataSetdicControlBitPositionmappping["UDChk_box1"] = 1;
            m_uadpDataSetdicControlBitPositionmappping["UDChk_box2"] = 2;
            m_uadpDataSetdicControlBitPositionmappping["UDChk_box3"] = 4;
            m_uadpDataSetdicControlBitPositionmappping["UDChk_box4"] = 8;
            m_uadpDataSetdicControlBitPositionmappping["UDChk_box5"] = 16;
            m_uadpDataSetdicControlBitPositionmappping["UDChk_box6"] = 32;

            m_jsonDataSetdicControlBitPositionmappping["JNChk_box1"] = 1;
            m_jsonDataSetdicControlBitPositionmappping["JNChk_box2"] = 2;
            m_jsonDataSetdicControlBitPositionmappping["JNChk_box3"] = 4;
            m_jsonDataSetdicControlBitPositionmappping["JNChk_box4"] = 8;
            m_jsonDataSetdicControlBitPositionmappping["JNChk_box5"] = 16;

            m_jsonNetworkdicControlBitPositionmappping["JN_Chk_box1"] = 1;
            m_jsonNetworkdicControlBitPositionmappping["JN_Chk_box2"] = 2;
            m_jsonNetworkdicControlBitPositionmappping["JN_Chk_box3"] = 4;
            m_jsonNetworkdicControlBitPositionmappping["JN_Chk_box4"] = 8;
            m_jsonNetworkdicControlBitPositionmappping["JN_Chk_box5"] = 16;
            m_jsonNetworkdicControlBitPositionmappping["JN_Chk_box6"] = 32;

        }

        #endregion

        #region Public Property


        public IOPCUAClientAdaptor ClientAdaptor { get; set; }

        public DataSetMetaDataType DataSetMetaDataType { get; set; }


        #endregion

        #region Public Methods

        public void Connect(string endPointUrl)
        {
            var errorMessage = string.Empty;

            var session = ClientAdaptor.Connect(endPointUrl, out errorMessage, out _rootnode);

            if (session == null)
            {
                //MessageBox.Show( (ClientAdaptor as OPCUAClientAdaptor).ServerStatus, "Add DataSet Reader" );
                MessageBox.Show("Enter the valid URL", "Add DataSet Reader");
                return;
            }
            m_remotePubSubCollectionItems = ClientAdaptor.GetPubSubConfiguation();
            PubSubTreeView.Items.Clear();
            foreach (var pubSubConfiguationBase in m_remotePubSubCollectionItems)
                PubSubTreeView.Items.Add(pubSubConfiguationBase);
            m_remotePublishedDataSetBaseCollection = ClientAdaptor.GetPublishedDataSets();
        }

        public void Disconnect()
        {
            ClientAdaptor.Disconnect();
        }

        #endregion

        public object _connectionPublisherId = "0";
        public bool _isApplied;
        public int WriterGroupid;
        public string SecurityGroupid;

        public int TransportSetting;
        public int MessageSetting;
        public int _publishingInterval;
        public object publisherId;
        public string QueueName;
        public string MetaDataQueueName;
        public string ResourceUri;
        public string AuthenticationProfileUri;
        public int RequestedDeliveryGuarantee;
        public uint NetworkMessageNumber;
        public uint GroupVersion;
        public uint DataSetOffset = 0;
        public Guid DataSetClassId;
        public int _receiveOffset = 0;
        public int ProcessingOffset = 0;
        public ushort dataSetWriterId;
        public string DatasetReaderName;
        public int _messageSecurityMode;
        public double _messageReceiveTimeout = 0;

        private void TransportSelect_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {

        }

        private void MessageSelect_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
        }
        public int _dataSetfieldContentMask;
        public int _networkMessageContentMask;
        public int _uadpDatasetMessageContentMask;
        public int _JsonDatasetMessageContentMask;
        public int _UadpNetworkMessageContentMask;
        public int _JsonNetworkMessageContentMask;

        private void Receiveoffset_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }
    }
}