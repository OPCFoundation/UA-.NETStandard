using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClientAdaptor;
using Opc.Ua;
using PubSubBase.Definitions;
using PubSubConfigurationUI.UserControls;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for PubSubConfigurationView.xaml
    /// </summary>
    public partial class PubSubConfigurationView
    {
        #region Private Member 

        private readonly ConnectionUserControl _connectionUserControl;
        private readonly DataSetReaderUserControl _dataSetReaderUserControl;
        private readonly DataSetWriterGroupUserControl _dataSetWriterGroupUserControl;
        private readonly DataSetWriterUserControl _dataSetWriterUserControl;
        private readonly FieldTargetVariableUserControl _fieldTargetVariableUserControl;
        private readonly ReaderGroupUserControl _readerGroupUserControl;
        private PubSubViewModel _viewModel;

        #endregion

        #region Private Methods

        private void UIElement_OnMouseRightButtonDown( object sender, MouseButtonEventArgs e )
        {
            if ( e.ChangedButton == MouseButton.Right && !(e.OriginalSource is Image) &&
                 !(e.OriginalSource is TextBlock) && !(e.OriginalSource is Border) )
            {
                ViewModel.IsConnectionVisible = Visibility.Visible;
                ViewModel.IsDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveConnectionVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsRemoveReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsAddTargetVariablesVisible = Visibility.Collapsed;
                ViewModel.IsDataSetMirrorVisible = Visibility.Collapsed;
                ViewModel.IsRemoveTargetVariableVisible = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Collapsed;
                ViewModel.IsCancelVisible = Visibility.Collapsed;
                return;
            }
            UpdateMenuandButtonVisibility( );
        }

        private void UpdateMenuandButtonVisibility( )
        {
            if ( PubSubTreeView.ContextMenu != null )
                PubSubTreeView.ContextMenu.Visibility = Visibility.Visible;
            if ( PubSubTreeView.SelectedItem is Connection )
            {
                ViewModel.IsConnectionVisible = Visibility.Collapsed;
                ViewModel.IsDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsReaderGroupVisible = Visibility.Visible;
                ViewModel.IsRemoveConnectionVisible = Visibility.Visible;
                ViewModel.IsRemoveDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsRemoveReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsWriterGroupVisible = Visibility.Visible;
                ViewModel.IsAddTargetVariablesVisible = Visibility.Collapsed;
                ViewModel.IsDataSetMirrorVisible = Visibility.Collapsed;
                ViewModel.IsRemoveTargetVariableVisible = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Visible;
                ViewModel.IsCancelVisible = Visibility.Visible;
            }
            else if ( PubSubTreeView.SelectedItem is DataSetWriterGroup )
            {
                ViewModel.IsConnectionVisible = Visibility.Collapsed;
                ViewModel.IsDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsDataSetWriterVisible = Visibility.Visible;
                ViewModel.IsReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveConnectionVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsRemoveReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveWriterGroupVisible = Visibility.Visible;
                ViewModel.IsWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsAddTargetVariablesVisible = Visibility.Collapsed;
                ViewModel.IsDataSetMirrorVisible = Visibility.Collapsed;
                ViewModel.IsRemoveTargetVariableVisible = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Visible;
                ViewModel.IsCancelVisible = Visibility.Visible;
            }
            else if ( PubSubTreeView.SelectedItem is DataSetWriterDefinition )
            {
                ViewModel.IsConnectionVisible = Visibility.Collapsed;
                ViewModel.IsDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveConnectionVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetWriterVisible = Visibility.Visible;
                ViewModel.IsRemoveReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsAddTargetVariablesVisible = Visibility.Collapsed;
                ViewModel.IsDataSetMirrorVisible = Visibility.Collapsed;
                ViewModel.IsRemoveTargetVariableVisible = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Visible;
                ViewModel.IsCancelVisible = Visibility.Visible;
            }
            else if ( PubSubTreeView.SelectedItem is ReaderGroupDefinition )
            {
                ViewModel.IsConnectionVisible = Visibility.Collapsed;
                ViewModel.IsDataSetReaderVisible = Visibility.Visible;
                ViewModel.IsDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveConnectionVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsRemoveReaderGroupVisible = Visibility.Visible;
                ViewModel.IsRemoveWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsAddTargetVariablesVisible = Visibility.Collapsed;
                ViewModel.IsDataSetMirrorVisible = Visibility.Collapsed;
                ViewModel.IsRemoveTargetVariableVisible = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Visible;
                ViewModel.IsCancelVisible = Visibility.Visible;
            }
            else if ( PubSubTreeView.SelectedItem is DataSetReaderDefinition )
            {
                ViewModel.IsConnectionVisible = Visibility.Collapsed;
                ViewModel.IsDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveConnectionVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetReaderVisible = Visibility.Visible;
                ViewModel.IsRemoveDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsRemoveReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsAddTargetVariablesVisible = Visibility.Visible;
                ViewModel.IsDataSetMirrorVisible = Visibility.Visible;
                ViewModel.IsRemoveTargetVariableVisible = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Visible;
                ViewModel.IsCancelVisible = Visibility.Visible;
            }
            else if ( PubSubTreeView.SelectedItem is SubscribedDataSetDefinition )
            {
                ViewModel.IsConnectionVisible = Visibility.Collapsed;
                ViewModel.IsDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveConnectionVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsRemoveReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsAddTargetVariablesVisible = Visibility.Visible;
                ViewModel.IsDataSetMirrorVisible = Visibility.Visible;
                ViewModel.IsRemoveTargetVariableVisible = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Collapsed;
                ViewModel.IsCancelVisible = Visibility.Collapsed;
            }
            else if ( PubSubTreeView.SelectedItem is FieldTargetVariableDefinition )
            {
                ViewModel.IsConnectionVisible = Visibility.Collapsed;
                ViewModel.IsDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveConnectionVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsRemoveReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsAddTargetVariablesVisible = Visibility.Collapsed;
                ViewModel.IsDataSetMirrorVisible = Visibility.Collapsed;
                ViewModel.IsRemoveTargetVariableVisible = Visibility.Visible;
                ViewModel.IsUpdateVisible = Visibility.Collapsed;
                ViewModel.IsCancelVisible = Visibility.Collapsed;
            }
            else if ( PubSubTreeView.SelectedItem is MirrorSubscribedDataSetDefinition )
            {
                ViewModel.IsConnectionVisible = Visibility.Collapsed;
                ViewModel.IsDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveConnectionVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsRemoveReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsAddTargetVariablesVisible = Visibility.Collapsed;
                ViewModel.IsDataSetMirrorVisible = Visibility.Collapsed;
                ViewModel.IsRemoveTargetVariableVisible = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Collapsed;
                ViewModel.IsCancelVisible = Visibility.Collapsed;
            }
            else if ( PubSubTreeView.SelectedItem is MirrorVariableDefinition )
            {
                ViewModel.IsConnectionVisible = Visibility.Collapsed;
                ViewModel.IsDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveConnectionVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsRemoveReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsAddTargetVariablesVisible = Visibility.Collapsed;
                ViewModel.IsDataSetMirrorVisible = Visibility.Collapsed;
                ViewModel.IsRemoveTargetVariableVisible = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Collapsed;
                ViewModel.IsCancelVisible = Visibility.Collapsed;
            }
            else
            {
                ViewModel.IsConnectionVisible = Visibility.Visible;
                ViewModel.IsDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveConnectionVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetReaderVisible = Visibility.Collapsed;
                ViewModel.IsRemoveDataSetWriterVisible = Visibility.Collapsed;
                ViewModel.IsRemoveReaderGroupVisible = Visibility.Collapsed;
                ViewModel.IsRemoveWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsWriterGroupVisible = Visibility.Collapsed;
                ViewModel.IsAddTargetVariablesVisible = Visibility.Collapsed;
                ViewModel.IsDataSetMirrorVisible = Visibility.Collapsed;
                ViewModel.IsRemoveTargetVariableVisible = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Visible;
                ViewModel.IsCancelVisible = Visibility.Visible;
            }
        }

        private void PubSubTreeView_OnSelectedItemChanged( object sender, RoutedPropertyChangedEventArgs< object > e )
        {
            UpdateMenuandButtonVisibility( );

            if ( PubSubTreeView.SelectedItem is Connection )
            {
                _connectionUserControl.ConnectionEditViewModel.Connection = PubSubTreeView.SelectedItem as Connection;
                _connectionUserControl.PublisherDataType.SelectedIndex = _connectionUserControl
                .ConnectionEditViewModel.Connection.PublisherDataType;
                _connectionUserControl.ConnectionEditViewModel.Initialize( );
                if ( _connectionUserControl.ConnectionEditViewModel.Connection.ConnectionType == 0 )
                    _connectionUserControl.ConnectHeaderText.Text = "UADP Connection";
                else if ( _connectionUserControl.ConnectionEditViewModel.Connection.ConnectionType == 1 )
                    _connectionUserControl.ConnectHeaderText.Text = "Broker Connection";
                PubSubContentControl.Content = _connectionUserControl;
            }
            else if ( PubSubTreeView.SelectedItem is DataSetWriterGroup )
            {
                Connection connection = null;
                _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.DataSetWriterGroup =
                PubSubTreeView.SelectedItem as DataSetWriterGroup;
                connection = _dataSetWriterGroupUserControl
                .DataSetWriterGroupEditViewModel.DataSetWriterGroup.ParentNode as Connection;
                if ( connection != null && connection.ConnectionType == 1 )
                {
                    _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.IsAMQP = Visibility.Visible;
                    _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.IsUADP = Visibility.Collapsed;
                }
                if ( connection != null && connection.ConnectionType == 0 )
                {
                    _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.IsAMQP = Visibility.Collapsed;
                    _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.IsUADP = Visibility.Visible;
                }
                _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.Initialize( );
                _dataSetWriterGroupUserControl.InitializeContentmask( );
                PubSubContentControl.Content = _dataSetWriterGroupUserControl;
            }
            else if ( PubSubTreeView.SelectedItem is DataSetWriterDefinition )
            {
                Connection connection = null;
                DataSetWriterGroup dataSetWriterGroup = null;
                _dataSetWriterUserControl.DataSetWriterEditViewModel.DataSetWriterDefinition =
                PubSubTreeView.SelectedItem as DataSetWriterDefinition;
                dataSetWriterGroup = _dataSetWriterUserControl
                .DataSetWriterEditViewModel.DataSetWriterDefinition.ParentNode as DataSetWriterGroup;
                if ( dataSetWriterGroup != null ) connection = dataSetWriterGroup.ParentNode as Connection;
                if ( connection != null && connection.ConnectionType == 1 )
                {
                    _dataSetWriterUserControl.DataSetWriterEditViewModel.IsAMQP = Visibility.Visible;
                    _dataSetWriterUserControl.DataSetWriterEditViewModel.IsUADP = Visibility.Collapsed;
                }
                if ( connection != null && connection.ConnectionType == 0 )
                {
                    _dataSetWriterUserControl.DataSetWriterEditViewModel.IsAMQP = Visibility.Collapsed;
                    _dataSetWriterUserControl.DataSetWriterEditViewModel.IsUADP = Visibility.Visible;
                }

                _dataSetWriterUserControl.DataSetWriterEditViewModel.Initialize( );
                _dataSetWriterUserControl.InitializeContentmask( );
                PubSubContentControl.Content = _dataSetWriterUserControl;
            }
            else if ( PubSubTreeView.SelectedItem is ReaderGroupDefinition )
            {
                _readerGroupUserControl.ReaderGroupEditViewModel.ReaderGroupDefinition =
                PubSubTreeView.SelectedItem as ReaderGroupDefinition;
                _readerGroupUserControl.ReaderGroupEditViewModel.Initialize( );
                PubSubContentControl.Content = _readerGroupUserControl;
            }
            else if ( PubSubTreeView.SelectedItem is DataSetReaderDefinition )
            {
                _dataSetReaderUserControl.DataSetReaderEditViewModel.ReaderDefinition =
                PubSubTreeView.SelectedItem as DataSetReaderDefinition;
                _dataSetReaderUserControl.DataSetReaderEditViewModel.Initialize( );
                _dataSetReaderUserControl.InitializeContentmask( );
                PubSubContentControl.Content = _dataSetReaderUserControl;
            }
            else if ( PubSubTreeView.SelectedItem is FieldTargetVariableDefinition )
            {
                _fieldTargetVariableUserControl.FieldTargetVariableEditViewModel.Definition =
                PubSubTreeView.SelectedItem as FieldTargetVariableDefinition;
                _fieldTargetVariableUserControl.FieldTargetVariableEditViewModel.Initialize( );
                _fieldTargetVariableUserControl.FieldTargetVariableEditViewModel.DataSetMetaDataType =
                ((PubSubTreeView.SelectedItem as FieldTargetVariableDefinition)
                    .ParentNode.ParentNode as DataSetReaderDefinition).DataSetMetaDataType;
                _fieldTargetVariableUserControl.RecieverIndextxt.IsEnabled = false;
                _fieldTargetVariableUserControl.writerIndextxt.IsEnabled = false;
                _fieldTargetVariableUserControl.OverrideValueTxt.IsEnabled = false;
                _fieldTargetVariableUserControl.OverrideValueHandlingTxt.IsEditable = false;

                PubSubContentControl.Content = _fieldTargetVariableUserControl;
            }
            else
            {
                PubSubContentControl.Content = null;
            }
        }

        private void OnAddConnectionClick( object sender, RoutedEventArgs e )
        {
            var addConnectionView = new AddConnectionView( );
            addConnectionView.Closing += AddConnectionView_Closing;
            addConnectionView.ShowInTaskbar = false;
            addConnectionView.ShowDialog( );
        }

        private void AddConnectionView_Closing( object sender, CancelEventArgs e )
        {
            var addConnectionView = sender as AddConnectionView;
            var connection = new Connection( );
            connection.Name = addConnectionView.ConnectionName;
            connection.Address = addConnectionView.Address;
            connection.PublisherId = addConnectionView.PublisherId;
            connection.ConnectionType = addConnectionView.ConnectionType;
            connection.PublisherDataType = addConnectionView.PublisherDataType.SelectedIndex;
            if ( addConnectionView.IsApplied )
                if ( connection.ConnectionType == 0 ) ViewModel.AddUADPConnection( connection );
                else ViewModel.AddBrokerConnection( connection );
        }

        private void OnRemoveConnectionClick( object sender, RoutedEventArgs e )
        {
            if ( PubSubTreeView.SelectedItem is Connection )
                ViewModel.RemoveConnection( PubSubTreeView.SelectedItem as Connection );
        }

        private void OnRemoveWriterGroupClick( object sender, RoutedEventArgs e )
        {
            if ( PubSubTreeView.SelectedItem is DataSetWriterGroup )
                ViewModel.RemoveWriterGroup( PubSubTreeView.SelectedItem as DataSetWriterGroup );
        }

        private void OnDataSetWriterClick( object sender, RoutedEventArgs e )
        {
            var addDataSetWriter = new AddDataSetWriter( );

            var dataSetWriterGroup = PubSubTreeView.SelectedItem as DataSetWriterGroup;
            if ( dataSetWriterGroup != null )
            {
                var connection = dataSetWriterGroup.ParentNode as Connection;
                if ( connection != null && connection.ConnectionType == 1 )
                {
                    addDataSetWriter._dataSetWriterViewModel.IsAMQP = Visibility.Visible;
                    addDataSetWriter._dataSetWriterViewModel.IsUADP = Visibility.Collapsed;
                    addDataSetWriter.Height = 310;
                }
                if ( connection != null && connection.ConnectionType == 0 )
                {
                    addDataSetWriter._dataSetWriterViewModel.IsAMQP = Visibility.Collapsed;
                    addDataSetWriter._dataSetWriterViewModel.IsUADP = Visibility.Visible;
                    addDataSetWriter.Height = 340;
                }
            }

            addDataSetWriter.Closing += AddDataSetWriterOnClosing;
            addDataSetWriter.ShowInTaskbar = false;
            addDataSetWriter.ShowDialog( );
        }

        private void OnRemoveDataSetWriterClick( object sender, RoutedEventArgs e )
        {
            if ( PubSubTreeView.SelectedItem is DataSetWriterDefinition )
                ViewModel.RemoveDataSetWriter( PubSubTreeView.SelectedItem as DataSetWriterDefinition );
        }

        private void AddDataSetWriterOnClosing( object sender, CancelEventArgs cancelEventArgs )
        {
            var addDataSetWriter = sender as AddDataSetWriter;
            if ( addDataSetWriter != null && addDataSetWriter._isApplied )
            {
                var dataSetWriterDefinition = new DataSetWriterDefinition( );
                dataSetWriterDefinition.ParentNode = PubSubTreeView.SelectedItem as DataSetWriterGroup;
                dataSetWriterDefinition.DataSetWriterName =
                addDataSetWriter._dataSetWriterViewModel.DataSetWriterName;
                dataSetWriterDefinition.PublisherDataSetId =
                addDataSetWriter._dataSetWriterViewModel.PublisherDataSetId;
                dataSetWriterDefinition.PublisherDataSetNodeId =
                addDataSetWriter._dataSetWriterViewModel.PublisherDataSetNodeId;
                dataSetWriterDefinition.DataSetWriterId = addDataSetWriter._dataSetWriterViewModel.DataSetWriterId;
                dataSetWriterDefinition.KeyFrameCount = addDataSetWriter._dataSetWriterViewModel.KeyFrameCount;
                dataSetWriterDefinition.DataSetContentMask = addDataSetWriter._dataSetContentMask;
                dataSetWriterDefinition.QueueName = addDataSetWriter._dataSetWriterViewModel.QueueName;
                dataSetWriterDefinition.MetadataQueueName =
                addDataSetWriter._dataSetWriterViewModel.MetadataQueueName;
                dataSetWriterDefinition.MetadataUpdataTime =
                addDataSetWriter._dataSetWriterViewModel.MetadataUpdataTime;
                dataSetWriterDefinition.MaxMessageSize = addDataSetWriter._dataSetWriterViewModel.MaxMessageSize;
                var isSuccess = ViewModel.AddDataSetWriter( PubSubTreeView.SelectedItem as DataSetWriterGroup,
                                                            dataSetWriterDefinition );
                if ( !isSuccess )
                {
                    addDataSetWriter._isApplied = false;
                    cancelEventArgs.Cancel = true;
                }
            }
        }

        private void OnWriterGroupClick( object sender, RoutedEventArgs e )
        {
            var addDataSetWriterGroup = new AddDataSetWriterGroup( );
            var connection = PubSubTreeView.SelectedItem as Connection;
            if ( connection != null && connection.ConnectionType == 1 )
            {
                addDataSetWriterGroup._dataSetGroupViewModel.IsAMQP = Visibility.Visible;
                addDataSetWriterGroup._dataSetGroupViewModel.IsUADP = Visibility.Collapsed;
                addDataSetWriterGroup.Height = 380;
            }
            if ( connection != null && connection.ConnectionType == 0 )
            {
                addDataSetWriterGroup._dataSetGroupViewModel.IsAMQP = Visibility.Collapsed;
                addDataSetWriterGroup._dataSetGroupViewModel.IsUADP = Visibility.Visible;
                addDataSetWriterGroup.Height = 570;
            }
            addDataSetWriterGroup.Closing += AddDataSetWriterGroupOnClosing;
            addDataSetWriterGroup.ShowInTaskbar = false;
            addDataSetWriterGroup.ShowDialog( );
        }

        private void AddDataSetWriterGroupOnClosing( object sender, CancelEventArgs cancelEventArgs )
        {
            var addDataSetWriterGroup = sender as AddDataSetWriterGroup;

            if ( addDataSetWriterGroup != null && addDataSetWriterGroup._isApplied )
            {
                var dataSetWriterGroup = new DataSetWriterGroup( );
                dataSetWriterGroup.ParentNode = PubSubTreeView.SelectedItem as Connection;
                dataSetWriterGroup.GroupName = addDataSetWriterGroup._dataSetGroupViewModel.GroupName;
                dataSetWriterGroup.KeepAliveTime = addDataSetWriterGroup._dataSetGroupViewModel.KeepAliveTime;
                dataSetWriterGroup.PublishingInterval = addDataSetWriterGroup
                ._dataSetGroupViewModel.PublishingInterval;
                dataSetWriterGroup.PublishingOffset = addDataSetWriterGroup._dataSetGroupViewModel.PublishingOffset;
                dataSetWriterGroup.Priority = ( byte ) addDataSetWriterGroup._dataSetGroupViewModel.Priority;
                dataSetWriterGroup.WriterGroupId = addDataSetWriterGroup._dataSetGroupViewModel.WriterGroupId;
                dataSetWriterGroup.SecurityGroupId = addDataSetWriterGroup._dataSetGroupViewModel.SecurityGroupId;
                dataSetWriterGroup.MaxNetworkMessageSize =
                addDataSetWriterGroup._dataSetGroupViewModel.MaxNetworkMessageSize;
                dataSetWriterGroup.MessageSecurityMode = addDataSetWriterGroup
                ._dataSetGroupViewModel.MessageSecurityMode;
                dataSetWriterGroup.NetworkMessageContentMask = addDataSetWriterGroup._networkMessageContentMask;
                dataSetWriterGroup.QueueName = addDataSetWriterGroup._dataSetGroupViewModel.QueueName;
                dataSetWriterGroup.EncodingMimeType = addDataSetWriterGroup._dataSetGroupViewModel.EncodingMimeType;
                var isSucess =
                ViewModel.AddWriterGroup( PubSubTreeView.SelectedItem as Connection, dataSetWriterGroup );
                if ( !isSucess )
                {
                    addDataSetWriterGroup._isApplied = false;
                    cancelEventArgs.Cancel = true;
                }
            }
        }

        private void OnReaderGroupClick( object sender, RoutedEventArgs e )
        {
            var addReaderGroup = new AddReaderGroup( );
            addReaderGroup.Closing += AddReaderGroupOnClosing;
            addReaderGroup.ShowInTaskbar = false;
            addReaderGroup.ShowDialog( );
        }

        private void AddReaderGroupOnClosing( object sender, CancelEventArgs cancelEventArgs )
        {
            var addReaderGroup = sender as AddReaderGroup;
            if ( addReaderGroup != null && addReaderGroup._isApplied )
            {
                var readerGroupDefinition = new ReaderGroupDefinition( );
                readerGroupDefinition.ParentNode = PubSubTreeView.SelectedItem as Connection;
                readerGroupDefinition.GroupName = addReaderGroup._readerGroupViewModel.GroupName;
                readerGroupDefinition.SecurityGroupId = addReaderGroup._readerGroupViewModel.SecurityGroupId;
                readerGroupDefinition.QueueName = addReaderGroup._readerGroupViewModel.QueueName;
                readerGroupDefinition.MaxNetworkMessageSize =
                addReaderGroup._readerGroupViewModel.MaxNetworkMessageSize;
                readerGroupDefinition.SecurityMode = addReaderGroup._readerGroupViewModel.SecurityMode;
                var isSuccess =
                ViewModel.AddReaderGroup( PubSubTreeView.SelectedItem as Connection, readerGroupDefinition );
                if ( !isSuccess )
                {
                    addReaderGroup._isApplied = false;
                    cancelEventArgs.Cancel = true;
                }
            }
        }

        private void OnDataSetReaderClick( object sender, RoutedEventArgs e )
        {
            if ( PubSubTreeView.SelectedItem is ReaderGroupDefinition )
            {
                //ReaderGroupDefinition _ReaderGroupDefinition= PubSubTreeView.SelectedItem as ReaderGroupDefinition;
                //Connection _Connection = _ReaderGroupDefinition.ParentNode as Connection;

                var addDataSetReader = new AddDataSetReader( ViewModel.PubSubCollectionItems );
                addDataSetReader.Closing += _AddDataSetReader_Closing;
                //addDataSetReader.ShowInTaskbar = false;
                addDataSetReader.ShowDialog( );
            }
        }

        private void _AddDataSetReader_Closing( object sender, CancelEventArgs e )
        {
            var addDataSetReader = sender as AddDataSetReader;
            if ( addDataSetReader != null && !addDataSetReader._isApplied )
            {
                addDataSetReader.Disconnect( );
                return;
            }
            var dataSetReaderDefinition = new DataSetReaderDefinition( );
            dataSetReaderDefinition.ParentNode = PubSubTreeView.SelectedItem as ReaderGroupDefinition;
            if ( addDataSetReader != null )
            {
                dataSetReaderDefinition.DataSetReaderName = addDataSetReader.DataSetReaderName.Text;
                dataSetReaderDefinition.PublisherId = addDataSetReader._connectionPublisherId;
                dataSetReaderDefinition.DataSetWriterId = Convert.ToInt16( addDataSetReader.DataSetWriterId.Text );
                dataSetReaderDefinition.DataSetMetaDataType = addDataSetReader.DataSetMetaDataType;
                dataSetReaderDefinition.MessageReceiveTimeOut =
                Convert.ToDouble( addDataSetReader.MessageReceiveTimeout.Text );
                dataSetReaderDefinition.PublishingInterval = Convert.ToDouble( addDataSetReader.PublishingIterval.Text );
                dataSetReaderDefinition.DataSetContentMask = addDataSetReader._dataSetContentMask;
                dataSetReaderDefinition.NetworkMessageContentMask = addDataSetReader._networkMessageContentMask;
                var isSuccess = ViewModel.AddDataSetReader( PubSubTreeView.SelectedItem as ReaderGroupDefinition,
                                                            dataSetReaderDefinition );
                if ( !isSuccess )
                {
                    addDataSetReader._isApplied = false;
                    e.Cancel = true;
                }
                addDataSetReader.Disconnect( );
            }
        }

        private void OnRemoveReaderGroupClick( object sender, RoutedEventArgs e )
        {
            if ( PubSubTreeView.SelectedItem is ReaderGroupDefinition )
                ViewModel.RemoveReaderGroup( PubSubTreeView.SelectedItem as ReaderGroupDefinition );
        }

        private void OnRemoveDataSetReaderClick( object sender, RoutedEventArgs e )
        {
            if ( PubSubTreeView.SelectedItem is DataSetReaderDefinition )
                ViewModel.RemoveDataSetReader( PubSubTreeView.SelectedItem as DataSetReaderDefinition );
        }

        private void OnRefreshClick( object sender, RoutedEventArgs e )
        {
            ViewModel.Initialize( );
        }

        private void OnAddVariablesClick( object sender, RoutedEventArgs e )
        {
            ViewModel.AddTargetVariables( PubSubTreeView.SelectedItem as PubSubConfiguationBase );
        }

        private void OnCreateDataSetMirrorClick( object sender, RoutedEventArgs e )
        {
            var addDataSetMirror = new AddDataSetMirror( );
            addDataSetMirror.Closing += _AddDataSetMirror_Closing;
            addDataSetMirror.ShowInTaskbar = false;
            addDataSetMirror.ShowDialog( );
        }

        private void _AddDataSetMirror_Closing( object sender, CancelEventArgs e )
        {
            var addDataSetMirror = sender as AddDataSetMirror;
            if ( addDataSetMirror != null && !addDataSetMirror.IsApplied ) return;
            var isSuccess = addDataSetMirror != null && ViewModel.AddDataSetMirror( PubSubTreeView.SelectedItem as DataSetReaderDefinition,
                                                                                    addDataSetMirror.ParentNodeName.Text );
            if ( !isSuccess )
            {
                if ( addDataSetMirror != null ) addDataSetMirror.IsApplied = false;
                e.Cancel = true;
            }
        }

        private void OnRemoveTargetVariableClick( object sender, RoutedEventArgs e )
        {
            ViewModel.RemoveTargetVariable( PubSubTreeView.SelectedItem as FieldTargetVariableDefinition );
        }

        private void UpdateConnectionSettings( )
        {
            var connection = new Connection( );
            connection.Name = _connectionUserControl.ConnectionEditViewModel.ConnectionName;
            connection.Address = _connectionUserControl.ConnectionEditViewModel.Address;
            connection.PublisherId = _connectionUserControl.ConnectionEditViewModel.PublisherId;
            connection.ConnectionType = _connectionUserControl.ConnectionEditViewModel.ConnectionType;
            connection.PublisherDataType = _connectionUserControl.PublisherDataType.SelectedIndex;
            connection.ConnectionNodeId = (PubSubTreeView.SelectedItem as Connection).ConnectionNodeId;
            ViewModel.UpdateConnection( connection );
            _connectionUserControl.ConnectionEditViewModel.Connection = connection;
            _connectionUserControl.ConnectionEditViewModel.Initialize( );
            var oldconfig = PubSubTreeView.SelectedItem as Connection;
            if ( oldconfig != null )
            {
                oldconfig.Address = connection.Address;
                oldconfig.PublisherId = connection.PublisherId;
                oldconfig.PublisherDataType = connection.PublisherDataType;
            }
        }

        private void UpdateDataSetWriterGroupSettings( )
        {
            var oldConfiguration = PubSubTreeView.SelectedItem as DataSetWriterGroup;

            if ( oldConfiguration != null )
            {
                var referenceDescriptionCollection = ViewModel.Browse( oldConfiguration.GroupId );

                if ( referenceDescriptionCollection.Count > 0 )
                {
                    var writeValueCollection = new WriteValueCollection( );

                    foreach ( var referenceDescription in referenceDescriptionCollection )
                        if ( referenceDescription.BrowseName.Name == "PublishingInterval" )
                        {
                            if ( _dataSetWriterGroupUserControl
                                 .DataSetWriterGroupEditViewModel.DataSetWriterGroup.PublishingInterval !=
                                 _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.PublishingInterval )
                            {
                                var writeValue =
                                CreateOPCWriteValue( ( NodeId ) referenceDescription.NodeId,
                                                     new DataValue(
                                                         new Variant(
                                                             Convert.ToDouble(
                                                                 _dataSetWriterGroupUserControl
                                                                 .DataSetWriterGroupEditViewModel
                                                                 .PublishingInterval ) ) ) );

                                writeValueCollection.Add( writeValue );
                            }
                        }
                        else if ( referenceDescription.BrowseName.Name == "PublishingOffset" )
                        {
                            if ( _dataSetWriterGroupUserControl
                                 .DataSetWriterGroupEditViewModel.DataSetWriterGroup.PublishingOffset !=
                                 _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.PublishingOffset )
                            {
                                var writeValue =
                                CreateOPCWriteValue( ( NodeId ) referenceDescription.NodeId,
                                                     new DataValue(
                                                         new Variant(
                                                             Convert.ToDouble(
                                                                 _dataSetWriterGroupUserControl
                                                                 .DataSetWriterGroupEditViewModel.PublishingOffset ) ) ) );
                                writeValueCollection.Add( writeValue );
                            }
                        }
                        else if ( referenceDescription.BrowseName.Name == "KeepAliveTime" )
                        {
                            if ( _dataSetWriterGroupUserControl
                                 .DataSetWriterGroupEditViewModel.DataSetWriterGroup.KeepAliveTime !=
                                 _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.KeepAliveTime )
                            {
                                var writeValue =
                                CreateOPCWriteValue( ( NodeId ) referenceDescription.NodeId,
                                                     new DataValue(
                                                         new Variant(
                                                             Convert.ToDouble(
                                                                 _dataSetWriterGroupUserControl
                                                                 .DataSetWriterGroupEditViewModel.KeepAliveTime ) ) ) );
                                writeValueCollection.Add( writeValue );
                            }
                        }
                        else if ( referenceDescription.BrowseName.Name == "SecurityGroupId" )
                        {
                            if ( _dataSetWriterGroupUserControl
                                 .DataSetWriterGroupEditViewModel.DataSetWriterGroup.SecurityGroupId !=
                                 _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.SecurityGroupId )
                            {
                                var writeValue =
                                CreateOPCWriteValue( ( NodeId ) referenceDescription.NodeId,
                                                     new DataValue( _dataSetWriterGroupUserControl
                                                                    .DataSetWriterGroupEditViewModel.SecurityGroupId ) );
                                writeValueCollection.Add( writeValue );
                            }
                        }
                        else if ( referenceDescription.BrowseName.Name == "WriterGroupId" )
                        {
                            if ( _dataSetWriterGroupUserControl
                                 .DataSetWriterGroupEditViewModel.DataSetWriterGroup.WriterGroupId !=
                                 _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.WriterGroupId )
                            {
                                var writeValue =
                                CreateOPCWriteValue( ( NodeId ) referenceDescription.NodeId,
                                                     new DataValue(
                                                         new Variant(
                                                             Convert.ToUInt16(
                                                                 _dataSetWriterGroupUserControl
                                                                 .DataSetWriterGroupEditViewModel.WriterGroupId ) ) ) );
                                writeValueCollection.Add( writeValue );
                            }
                        }
                        else if ( referenceDescription.BrowseName.Name == "MaxNetworkMessageSize" )
                        {
                            if ( _dataSetWriterGroupUserControl
                                 .DataSetWriterGroupEditViewModel.DataSetWriterGroup.MaxNetworkMessageSize !=
                                 _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.MaxNetworkMessageSize )
                            {
                                var writeValue =
                                CreateOPCWriteValue( ( NodeId ) referenceDescription.NodeId,
                                                     new DataValue(
                                                         new Variant(
                                                             Convert.ToUInt16(
                                                                 _dataSetWriterGroupUserControl
                                                                 .DataSetWriterGroupEditViewModel
                                                                 .MaxNetworkMessageSize ) ) ) );
                                writeValueCollection.Add( writeValue );
                            }
                        }
                        else if ( referenceDescription.BrowseName.Name == "Priority" )
                        {
                            if ( _dataSetWriterGroupUserControl
                                 .DataSetWriterGroupEditViewModel.DataSetWriterGroup.Priority !=
                                 _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.Priority )
                            {
                                var writeValue =
                                CreateOPCWriteValue( ( NodeId ) referenceDescription.NodeId,
                                                     new DataValue(
                                                         new Variant(
                                                             Convert.ToByte(
                                                                 _dataSetWriterGroupUserControl
                                                                 .DataSetWriterGroupEditViewModel.Priority ) ) ) );
                                writeValueCollection.Add( writeValue );
                            }
                        }

                        else if ( referenceDescription.BrowseName.Name == "SecurityMode" )
                        {
                            if ( _dataSetWriterGroupUserControl
                                 .DataSetWriterGroupEditViewModel.DataSetWriterGroup.MessageSecurityMode !=
                                 _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.MessageSecurityMode )
                            {
                                var writeValue =
                                CreateOPCWriteValue( ( NodeId ) referenceDescription.NodeId,
                                                     new DataValue( _dataSetWriterGroupUserControl
                                                                    .DataSetWriterGroupEditViewModel
                                                                    .MessageSecurityMode ) );
                                writeValueCollection.Add( writeValue );
                            }
                        }
                        else if ( referenceDescription.BrowseName.Name == "NetworkMessageContentMask" )
                        {
                            var networkMessageContentMask = _dataSetWriterGroupUserControl.GetNetworkContentMask( );

                            if ( _dataSetWriterGroupUserControl
                                 .DataSetWriterGroupEditViewModel.DataSetWriterGroup.NetworkMessageContentMask !=
                                 networkMessageContentMask )
                            {
                                var writeValue =
                                CreateOPCWriteValue( ( NodeId ) referenceDescription.NodeId,
                                                     new DataValue(
                                                         new Variant( Convert.ToUInt32( networkMessageContentMask ) ) ) );
                                writeValueCollection.Add( writeValue );
                            }
                        }
                    var statusCollection = ViewModel.WriteValue( writeValueCollection );

                    foreach ( var code in statusCollection )
                        if ( !StatusCode.IsGood( code ) )
                        {
                            MessageBox.Show( "One or more parameter(s) are failed to write values to the server",
                                             "DataSet Writer Group View" );
                            break;
                        }

                    var refeDescriptionCollection = ViewModel.Browse( oldConfiguration.GroupId );

                    if ( referenceDescriptionCollection.Count > 0 )
                        foreach ( var referenceDescription in referenceDescriptionCollection )
                        {
                            if ( referenceDescription.BrowseName.Name == "PublishingInterval" )
                                oldConfiguration.PublishingInterval =
                                Convert.ToDouble( ViewModel.ReadValue( ( NodeId ) referenceDescription.NodeId ) );
                            else if ( referenceDescription.BrowseName.Name == "PublishingOffset" )
                                oldConfiguration.PublishingOffset =
                                Convert.ToDouble( ViewModel.ReadValue( ( NodeId ) referenceDescription.NodeId ) );
                            else if ( referenceDescription.BrowseName.Name == "KeepAliveTime" )
                                oldConfiguration.KeepAliveTime =
                                Convert.ToDouble( ViewModel.ReadValue( ( NodeId ) referenceDescription.NodeId ) );
                            if ( referenceDescription.BrowseName.Name == "SecurityGroupId" )
                                oldConfiguration.SecurityGroupId =
                                ViewModel.ReadValue( ( NodeId ) referenceDescription.NodeId ).ToString( );
                            else if ( referenceDescription.BrowseName.Name == "WriterGroupId" )
                                oldConfiguration.WriterGroupId =
                                Convert.ToInt32( ViewModel.ReadValue( ( NodeId ) referenceDescription.NodeId ) );
                            else if ( referenceDescription.BrowseName.Name == "MaxNetworkMessageSize" )
                                oldConfiguration.MaxNetworkMessageSize =
                                Convert.ToInt32( ViewModel.ReadValue( ( NodeId ) referenceDescription.NodeId ) );
                            else if ( referenceDescription.BrowseName.Name == "Priority" )
                                oldConfiguration.Priority =
                                Convert.ToInt32( ViewModel.ReadValue( ( NodeId ) referenceDescription.NodeId ) );
                            else if ( referenceDescription.BrowseName.Name == "SecurityMode" )
                                oldConfiguration.MessageSecurityMode =
                                Convert.ToInt32( ViewModel.ReadValue( ( NodeId ) referenceDescription.NodeId ) );
                            else if ( referenceDescription.BrowseName.Name == "NetworkMessageContentMask" )
                                oldConfiguration.NetworkMessageContentMask =
                                Convert.ToInt32( ViewModel.ReadValue( ( NodeId ) referenceDescription.NodeId ) );
                            else if ( referenceDescription.BrowseName.Name == "EncodingMimeType" )
                                oldConfiguration.EncodingMimeType =
                                ViewModel.ReadValue( ( NodeId ) referenceDescription.NodeId ).ToString( );
                            else if ( referenceDescription.BrowseName.Name == "QueueName" )
                                oldConfiguration.QueueName = ViewModel
                                .ReadValue( ( NodeId ) referenceDescription.NodeId ).ToString( );
                        }
                    _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.DataSetWriterGroup = oldConfiguration;
                    _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.Initialize( );
                }
            }
        }

        private void UpdateDataSetWriterSettings( )
        {
            var oldConfiguration = PubSubTreeView.SelectedItem as DataSetWriterDefinition;

            if ( oldConfiguration != null )
            {
                var referenceDescriptionCollection = ViewModel.Browse( oldConfiguration.WriterNodeId );

                if ( referenceDescriptionCollection.Count > 0 )
                {
                    var writeValueCollection = new WriteValueCollection( );

                    foreach ( var referenceDescription in referenceDescriptionCollection )
                    {
                        if ( referenceDescription.BrowseName.Name == "DataSetMessageContentMask" )
                        {
                            var dataSetContentMask = _dataSetWriterUserControl.GetDataSetContentMask( );
                            if ( _dataSetWriterUserControl
                                 .DataSetWriterEditViewModel.DataSetWriterDefinition.DataSetContentMask !=
                                 dataSetContentMask )
                            {
                                var writeValue =
                                CreateOPCWriteValue( ( NodeId ) referenceDescription.NodeId,
                                                     new DataValue(
                                                         new Variant( Convert.ToUInt32( dataSetContentMask ) ) ) );

                                writeValueCollection.Add( writeValue );
                            }
                        }
                        if ( referenceDescription.TypeDefinition == Constants.TransPortSettingsTypeDefinition )
                        {
                            var referenceDescriptionTransPortCollection =
                            ViewModel.Browse( ( NodeId ) referenceDescription.NodeId );
                            foreach ( var refDescription in referenceDescriptionTransPortCollection )
                                if ( refDescription.TypeDefinition == Constants.PropertyNodeId )
                                    if ( refDescription.BrowseName.Name == "DataSetWriterId" )
                                    {
                                        if ( _dataSetWriterUserControl
                                             .DataSetWriterEditViewModel.DataSetWriterDefinition.DataSetWriterId !=
                                             _dataSetWriterUserControl.DataSetWriterEditViewModel.DataSetWriterId )
                                        {
                                            var writeValue =
                                            CreateOPCWriteValue( ( NodeId ) refDescription.NodeId,
                                                                 new DataValue(
                                                                     new Variant(
                                                                         Convert.ToUInt16(
                                                                             _dataSetWriterUserControl
                                                                             .DataSetWriterEditViewModel
                                                                             .DataSetWriterId ) ) ) );

                                            writeValueCollection.Add( writeValue );
                                        }
                                    }
                                    else if ( refDescription.BrowseName.Name == "KeyFrameCount" )
                                    {
                                        if ( _dataSetWriterUserControl
                                             .DataSetWriterEditViewModel.DataSetWriterDefinition.KeyFrameCount !=
                                             _dataSetWriterUserControl.DataSetWriterEditViewModel.KeyFrameCount )
                                        {
                                            var writeValue =
                                            CreateOPCWriteValue( ( NodeId ) refDescription.NodeId,
                                                                 new DataValue(
                                                                     new Variant(
                                                                         Convert.ToUInt32(
                                                                             _dataSetWriterUserControl
                                                                             .DataSetWriterEditViewModel
                                                                             .KeyFrameCount ) ) ) );

                                            writeValueCollection.Add( writeValue );
                                        }
                                    }
                                    else if ( refDescription.BrowseName.Name == "QueueName" )
                                    {
                                        if ( _dataSetWriterUserControl
                                             .DataSetWriterEditViewModel.DataSetWriterDefinition.QueueName !=
                                             _dataSetWriterUserControl.DataSetWriterEditViewModel.QueueName )
                                        {
                                            var writeValue =
                                            CreateOPCWriteValue( ( NodeId ) refDescription.NodeId,
                                                                 new DataValue(
                                                                     _dataSetWriterUserControl
                                                                     .DataSetWriterEditViewModel.QueueName ) );

                                            writeValueCollection.Add( writeValue );
                                        }
                                    }
                                    else if ( refDescription.BrowseName.Name == "MetadataQueueName" )
                                    {
                                        if ( _dataSetWriterUserControl
                                             .DataSetWriterEditViewModel.DataSetWriterDefinition.MetadataQueueName !=
                                             _dataSetWriterUserControl.DataSetWriterEditViewModel.MetadataQueueName )
                                        {
                                            var writeValue =
                                            CreateOPCWriteValue( ( NodeId ) refDescription.NodeId,
                                                                 new DataValue(
                                                                     _dataSetWriterUserControl
                                                                     .DataSetWriterEditViewModel.MetadataQueueName ) );

                                            writeValueCollection.Add( writeValue );
                                        }
                                    }
                                    else if ( refDescription.BrowseName.Name == "MetadataUpdataTime" )
                                    {
                                        if ( _dataSetWriterUserControl
                                             .DataSetWriterEditViewModel.DataSetWriterDefinition.MetadataUpdataTime !=
                                             _dataSetWriterUserControl.DataSetWriterEditViewModel.MetadataUpdataTime )
                                        {
                                            var writeValue =
                                            CreateOPCWriteValue( ( NodeId ) refDescription.NodeId,
                                                                 new DataValue(
                                                                     new Variant(
                                                                         Convert.ToInt32(
                                                                             _dataSetWriterUserControl
                                                                             .DataSetWriterEditViewModel
                                                                             .MetadataUpdataTime ) ) ) );

                                            writeValueCollection.Add( writeValue );
                                        }
                                    }
                                    else if ( refDescription.BrowseName.Name == "MaxMessageSize" )
                                    {
                                        if ( _dataSetWriterUserControl
                                             .DataSetWriterEditViewModel.DataSetWriterDefinition.MaxMessageSize !=
                                             _dataSetWriterUserControl.DataSetWriterEditViewModel.MaxMessageSize )
                                        {
                                            var writeValue =
                                            CreateOPCWriteValue( ( NodeId ) refDescription.NodeId,
                                                                 new DataValue(
                                                                     new Variant(
                                                                         Convert.ToUInt16(
                                                                             _dataSetWriterUserControl
                                                                             .DataSetWriterEditViewModel
                                                                             .MaxMessageSize ) ) ) );

                                            writeValueCollection.Add( writeValue );
                                        }
                                    }
                        }
                    }
                    var statusCollection = ViewModel.WriteValue( writeValueCollection );

                    foreach ( var code in statusCollection )
                        if ( !StatusCode.IsGood( code ) )
                        {
                            MessageBox.Show( "One or more parameter(s) are failed to write values to the server",
                                             "DataSet Writer Group View" );
                            break;
                        }

                    var refeDescriptionCollection = ViewModel.Browse( oldConfiguration.WriterNodeId );

                    if ( referenceDescriptionCollection.Count > 0 )
                        foreach ( var referenceDescription in referenceDescriptionCollection )
                        {
                            if ( referenceDescription.BrowseName.Name == "DataSetMessageContentMask" )
                                oldConfiguration.DataSetContentMask =
                                Convert.ToInt32( ViewModel.ReadValue( ( NodeId ) referenceDescription.NodeId ) );
                            if ( referenceDescription.TypeDefinition == Constants.TransPortSettingsTypeDefinition )
                            {
                                var refDescWriterPropertiesCollection =
                                ViewModel.Browse( ( NodeId ) referenceDescription.NodeId );
                                foreach ( var refDescription in refDescWriterPropertiesCollection )
                                    try
                                    {
                                        if ( refDescription.TypeDefinition == Constants.PropertyNodeId )
                                            if ( refDescription.BrowseName.Name == "DataSetWriterId" )
                                                oldConfiguration.DataSetWriterId =
                                                Convert.ToInt32( ViewModel.ReadValue( ( NodeId ) refDescription.NodeId ) );
                                            else if ( refDescription.BrowseName.Name == "KeyFrameCount" )
                                                oldConfiguration.KeyFrameCount =
                                                Convert.ToInt32( ViewModel.ReadValue( ( NodeId ) refDescription.NodeId ) );
                                            else if ( refDescription.BrowseName.Name == "QueueName" )
                                                oldConfiguration.QueueName =
                                                ViewModel.ReadValue( ( NodeId ) refDescription.NodeId ).ToString( );
                                            else if ( refDescription.BrowseName.Name == "MetadataQueueName" )
                                                oldConfiguration.MetadataQueueName =
                                                ViewModel.ReadValue( ( NodeId ) refDescription.NodeId ).ToString( );
                                            else if ( refDescription.BrowseName.Name == "MetadataUpdataTime" )
                                                oldConfiguration.MetadataUpdataTime =
                                                Convert.ToInt32( ViewModel.ReadValue( ( NodeId ) refDescription.NodeId ) );
                                            else if ( refDescription.BrowseName.Name == "MaxMessageSize" )
                                                oldConfiguration.MaxMessageSize =
                                                Convert.ToInt32( ViewModel.ReadValue( ( NodeId ) refDescription.NodeId ) );
                                    }
                                    catch ( Exception ex )
                                    {
                                        Utils.Trace(ex, "PubSubConfigurationView:UpdateDataSetWriterSettings", ex );
                                    }
                            }
                        }

                    _dataSetWriterUserControl.DataSetWriterEditViewModel.DataSetWriterDefinition = oldConfiguration;
                    _dataSetWriterUserControl.DataSetWriterEditViewModel.Initialize( );
                }
            }
        }

        private void UpdateReaderGroupSettings( )
        {
            var oldConfiguration = PubSubTreeView.SelectedItem as ReaderGroupDefinition;

            if ( oldConfiguration != null )
            {
                var referenceDescriptionCollection = ViewModel.Browse( oldConfiguration.GroupId );

                if ( referenceDescriptionCollection.Count > 0 )
                {
                    var writeValueCollection = new WriteValueCollection( );

                    foreach ( var referenceDescription in referenceDescriptionCollection )
                        if ( referenceDescription.BrowseName.Name == "SecurityGroupId" )
                        {
                            if ( _readerGroupUserControl.ReaderGroupEditViewModel.ReaderGroupDefinition.SecurityGroupId !=
                                 _readerGroupUserControl.ReaderGroupEditViewModel.SecurityGroupId )
                            {
                                var writeValue =
                                CreateOPCWriteValue( ( NodeId ) referenceDescription.NodeId,
                                                     new DataValue( _readerGroupUserControl
                                                                    .ReaderGroupEditViewModel.SecurityGroupId ) );
                                writeValueCollection.Add( writeValue );
                            }
                        }
                        else if ( referenceDescription.BrowseName.Name == "SecurityMode" )
                        {
                            if ( _readerGroupUserControl.ReaderGroupEditViewModel.ReaderGroupDefinition.SecurityMode !=
                                 _readerGroupUserControl.ReaderGroupEditViewModel.SecurityMode )
                            {
                                var writeValue =
                                CreateOPCWriteValue( ( NodeId ) referenceDescription.NodeId,
                                                     new DataValue( _readerGroupUserControl
                                                                    .ReaderGroupEditViewModel.SecurityMode ) );
                                writeValueCollection.Add( writeValue );
                            }
                        }
                        else if ( referenceDescription.BrowseName.Name == "MaxNetworkMessageSize" )
                        {
                            if ( _readerGroupUserControl
                                 .ReaderGroupEditViewModel.ReaderGroupDefinition.MaxNetworkMessageSize !=
                                 _readerGroupUserControl.ReaderGroupEditViewModel.MaxNetworkMessageSize )
                            {
                                var writeValue =
                                CreateOPCWriteValue( ( NodeId ) referenceDescription.NodeId,
                                                     new DataValue(
                                                         new Variant(
                                                             Convert.ToUInt16(
                                                                 _readerGroupUserControl
                                                                 .ReaderGroupEditViewModel.MaxNetworkMessageSize ) ) ) );
                                writeValueCollection.Add( writeValue );
                            }
                        }
                        else if ( referenceDescription.BrowseName.Name == "QueueName" )
                        {
                            if ( _readerGroupUserControl.ReaderGroupEditViewModel.ReaderGroupDefinition.QueueName !=
                                 _readerGroupUserControl.ReaderGroupEditViewModel.QueueName )
                            {
                                var writeValue =
                                CreateOPCWriteValue( ( NodeId ) referenceDescription.NodeId,
                                                     new DataValue( _readerGroupUserControl
                                                                    .ReaderGroupEditViewModel.QueueName ) );
                                writeValueCollection.Add( writeValue );
                            }
                        }

                    var statusCollection = ViewModel.WriteValue( writeValueCollection );

                    foreach ( var code in statusCollection )
                        if ( !StatusCode.IsGood( code ) )
                        {
                            MessageBox.Show( "One or more parameter(s) are failed to write values to the server",
                                             "DataSet Writer Group View" );
                            break;
                        }

                    var refeDescriptionCollection = ViewModel.Browse( oldConfiguration.GroupId );

                    if ( referenceDescriptionCollection.Count > 0 )
                        foreach ( var referenceDescription in referenceDescriptionCollection )
                            if ( referenceDescription.BrowseName.Name == "SecurityGroupId" )
                                oldConfiguration.SecurityGroupId =
                                ViewModel.ReadValue( ( NodeId ) referenceDescription.NodeId ).ToString( );
                            else if ( referenceDescription.BrowseName.Name == "SecurityMode" )
                                oldConfiguration.SecurityMode =
                                Convert.ToInt32( ViewModel.ReadValue( ( NodeId ) referenceDescription.NodeId ) );
                            else if ( referenceDescription.BrowseName.Name == "MaxNetworkMessageSize" )
                                oldConfiguration.MaxNetworkMessageSize =
                                Convert.ToInt32( ViewModel.ReadValue( ( NodeId ) referenceDescription.NodeId ) );
                            else if ( referenceDescription.BrowseName.Name == "QueueName" )
                                oldConfiguration.QueueName = ViewModel
                                .ReadValue( ( NodeId ) referenceDescription.NodeId ).ToString( );

                    _readerGroupUserControl.ReaderGroupEditViewModel.ReaderGroupDefinition = oldConfiguration;
                    _readerGroupUserControl.ReaderGroupEditViewModel.Initialize( );
                }
            }
        }

        private void UpdateDataSetReaderSettings( )
        {
            var oldConfiguration = PubSubTreeView.SelectedItem as DataSetReaderDefinition;

            object publisherId = null;
            if ( oldConfiguration != null )
            {
                var type = oldConfiguration.PublisherId.GetType( );
                switch ( type.Name.ToLower( ) )
                {
                    case "string" :
                        publisherId = Convert.ToString( _dataSetReaderUserControl
                                                        .DataSetReaderEditViewModel.ConnectionPublisherId );
                        break;
                    case "byte" :
                        publisherId = Convert.ToByte( _dataSetReaderUserControl
                                                      .DataSetReaderEditViewModel.ConnectionPublisherId );
                        break;
                    case "uint16" :
                        publisherId = Convert.ToUInt16( _dataSetReaderUserControl
                                                        .DataSetReaderEditViewModel.ConnectionPublisherId );
                        break;
                    case "uint32" :
                        publisherId = Convert.ToUInt32( _dataSetReaderUserControl
                                                        .DataSetReaderEditViewModel.ConnectionPublisherId );
                        break;
                    case "uint64" :
                        publisherId = Convert.ToUInt64( _dataSetReaderUserControl
                                                        .DataSetReaderEditViewModel.ConnectionPublisherId );
                        break;
                    case "guid" :
                        var guid = new Guid( );
                        if ( Guid.TryParse( _dataSetReaderUserControl.DataSetReaderEditViewModel.ConnectionPublisherId,
                                            out guid ) ) publisherId = guid;
                        else return;
                        break;
                }
            }
            if ( oldConfiguration != null )
            {
                var referenceDescriptionCollection = ViewModel.Browse( oldConfiguration.DataSetReaderNodeId );

                if ( referenceDescriptionCollection.Count > 0 )
                {
                    var writeValueCollection = new WriteValueCollection( );

                    foreach ( var referenceDescription in referenceDescriptionCollection )
                    {
                        if ( referenceDescription.BrowseName.Name == "TransportSettings" )
                        {
                            var refDescriptionDataSetReaderGroupCollection =
                            ViewModel.Browse( ( NodeId ) referenceDescription.NodeId );
                            {
                                foreach ( var refDsDesc in refDescriptionDataSetReaderGroupCollection )
                                    if ( refDsDesc.BrowseName.Name == "DataSetWriterId" )
                                    {
                                        if ( _dataSetReaderUserControl
                                             .DataSetReaderEditViewModel.ReaderDefinition.DataSetWriterId !=
                                             _dataSetReaderUserControl.DataSetReaderEditViewModel.DataSetWriterId )
                                        {
                                            var writeValue =
                                            CreateOPCWriteValue( ( NodeId ) refDsDesc.NodeId,
                                                                 new DataValue(
                                                                     new Variant(
                                                                         Convert.ToUInt16(
                                                                             _dataSetReaderUserControl
                                                                             .DataSetReaderEditViewModel
                                                                             .DataSetWriterId ) ) ) );
                                            writeValueCollection.Add( writeValue );
                                        }
                                    }
                                    else if ( refDsDesc.BrowseName.Name == "PublisherId" )
                                    {
                                        if ( _dataSetReaderUserControl
                                             .DataSetReaderEditViewModel.ReaderDefinition.PublisherId != publisherId )
                                        {
                                            var writeValue =
                                            CreateOPCWriteValue( ( NodeId ) refDsDesc.NodeId,
                                                                 new DataValue( new Variant( publisherId ) ) );
                                            writeValueCollection.Add( writeValue );
                                        }
                                    }
                                    else if ( refDsDesc.BrowseName.Name == "NetworkMessageContentMask" )
                                    {
                                        var netWorkContextMask = _dataSetReaderUserControl.GetNetworkContentMask( );

                                        if ( _dataSetReaderUserControl
                                             .DataSetReaderEditViewModel.ReaderDefinition.NetworkMessageContentMask !=
                                             netWorkContextMask )
                                        {
                                            var writeValue =
                                            CreateOPCWriteValue( ( NodeId ) refDsDesc.NodeId,
                                                                 new DataValue(
                                                                     new Variant(
                                                                         Convert.ToUInt32( netWorkContextMask ) ) ) );
                                            writeValueCollection.Add( writeValue );
                                        }
                                    }
                                    else if ( refDsDesc.BrowseName.Name == "DataSetMessageContentMask" )
                                    {
                                        var dataSetContextMask = _dataSetReaderUserControl.GetDataSetContentMask( );
                                        if ( _dataSetReaderUserControl
                                             .DataSetReaderEditViewModel.ReaderDefinition.DataSetContentMask !=
                                             dataSetContextMask )
                                        {
                                            var writeValue =
                                            CreateOPCWriteValue( ( NodeId ) refDsDesc.NodeId,
                                                                 new DataValue(
                                                                     new Variant(
                                                                         Convert.ToUInt32( dataSetContextMask ) ) ) );
                                            writeValueCollection.Add( writeValue );
                                        }
                                    }
                                    else if ( refDsDesc.BrowseName.Name == "PublishingInterval" )
                                    {
                                        if ( _dataSetReaderUserControl
                                             .DataSetReaderEditViewModel.ReaderDefinition.PublishingInterval !=
                                             _dataSetReaderUserControl.DataSetReaderEditViewModel.PublishingInterval )
                                        {
                                            var writeValue =
                                            CreateOPCWriteValue( ( NodeId ) refDsDesc.NodeId,
                                                                 new DataValue(
                                                                     new Variant(
                                                                         Convert.ToDouble(
                                                                             _dataSetReaderUserControl
                                                                             .DataSetReaderEditViewModel
                                                                             .PublishingInterval ) ) ) );
                                            writeValueCollection.Add( writeValue );
                                        }
                                    }
                            }
                        }
                        if ( referenceDescription.BrowseName.Name == "SubscribedDataSet" )
                        {
                            var refDescriptionDataSetReaderGroupCollection =
                            ViewModel.Browse( ( NodeId ) referenceDescription.NodeId );

                            foreach ( var refDsDesc in refDescriptionDataSetReaderGroupCollection )
                                if ( refDsDesc.BrowseName.Name == "MessageReceiveTimeout" )
                                    if ( _dataSetReaderUserControl
                                         .DataSetReaderEditViewModel.ReaderDefinition.MessageReceiveTimeOut !=
                                         _dataSetReaderUserControl.DataSetReaderEditViewModel.MessageReceiveTimeOut )
                                    {
                                        var writeValue =
                                        CreateOPCWriteValue( ( NodeId ) refDsDesc.NodeId,
                                                             new DataValue(
                                                                 new Variant(
                                                                     Convert.ToDouble(
                                                                         _dataSetReaderUserControl
                                                                         .DataSetReaderEditViewModel
                                                                         .MessageReceiveTimeOut ) ) ) );
                                        writeValueCollection.Add( writeValue );
                                    }
                        }
                    }

                    var statusCollection = ViewModel.WriteValue( writeValueCollection );

                    foreach ( var code in statusCollection )
                        if ( !StatusCode.IsGood( code ) )
                        {
                            MessageBox.Show( "One or more parameter(s) are failed to write values to the server",
                                             "DataSet Writer Group View" );
                            break;
                        }

                    var refeDescriptionCollection = ViewModel.Browse( oldConfiguration.DataSetReaderNodeId );

                    if ( referenceDescriptionCollection.Count > 0 )
                        foreach ( var referenceDescription1 in referenceDescriptionCollection )
                        {
                            if ( referenceDescription1.BrowseName.Name == "TransportSettings" )
                            {
                                var refDescriptionDataSetReaderGroupCollection =
                                ViewModel.Browse( ( NodeId ) referenceDescription1.NodeId );
                                {
                                    foreach ( var refDsDesc in refDescriptionDataSetReaderGroupCollection )
                                        if ( refDsDesc.BrowseName.Name == "DataSetWriterId" )
                                            oldConfiguration.DataSetWriterId =
                                            Convert.ToInt32( ViewModel.ReadValue( ( NodeId ) refDsDesc.NodeId ) );
                                        else if ( refDsDesc.BrowseName.Name == "PublisherId" )
                                            oldConfiguration.PublisherId =
                                            ViewModel.ReadValue( ( NodeId ) refDsDesc.NodeId ).ToString( );
                                        else if ( refDsDesc.BrowseName.Name == "NetworkMessageContentMask" )
                                            oldConfiguration.NetworkMessageContentMask =
                                            Convert.ToInt32( ViewModel.ReadValue( ( NodeId ) refDsDesc.NodeId ) );
                                        else if ( refDsDesc.BrowseName.Name == "DataSetMessageContentMask" )
                                            oldConfiguration.DataSetContentMask =
                                            Convert.ToInt32( ViewModel.ReadValue( ( NodeId ) refDsDesc.NodeId ) );

                                        else if ( refDsDesc.BrowseName.Name == "PublishingInterval" )
                                            oldConfiguration.PublishingInterval =
                                            Convert.ToDouble( ViewModel.ReadValue( ( NodeId ) refDsDesc.NodeId ) );
                                }
                            }
                            if ( referenceDescription1.BrowseName.Name == "SubscribedDataSet" )
                            {
                                var refDescriptionDataSetReaderGroupCollection =
                                ViewModel.Browse( ( NodeId ) referenceDescription1.NodeId );

                                foreach ( var refDsDesc in refDescriptionDataSetReaderGroupCollection )
                                    if ( refDsDesc.BrowseName.Name == "MessageReceiveTimeout" )
                                        oldConfiguration.MessageReceiveTimeOut =
                                        Convert.ToDouble( ViewModel.ReadValue( ( NodeId ) refDsDesc.NodeId ) );
                            }
                        }
                }
            }

            _dataSetReaderUserControl.DataSetReaderEditViewModel.ReaderDefinition = oldConfiguration;
            _dataSetReaderUserControl.DataSetReaderEditViewModel.Initialize( );
        }

        private void Update_Click( object sender, RoutedEventArgs e )
        {
            if ( PubSubTreeView.SelectedItem is Connection ) UpdateConnectionSettings( );
            else if ( PubSubTreeView.SelectedItem is DataSetWriterGroup ) UpdateDataSetWriterGroupSettings( );
            else if ( PubSubTreeView.SelectedItem is DataSetWriterDefinition ) UpdateDataSetWriterSettings( );

            else if ( PubSubTreeView.SelectedItem is ReaderGroupDefinition ) UpdateReaderGroupSettings( );
            else if ( PubSubTreeView.SelectedItem is DataSetReaderDefinition ) UpdateDataSetReaderSettings( );
        }

        private WriteValue CreateOPCWriteValue( NodeId nodeId, DataValue dataValue )
        {
            var writeValue = new WriteValue( );
            writeValue.NodeId = nodeId;
            writeValue.AttributeId = Attributes.Value;
            writeValue.Value = dataValue;
            return writeValue;
        }

        private void OnCancel_Click( object sender, RoutedEventArgs e )
        {
            if ( PubSubTreeView.SelectedItem is Connection )
            {
                _connectionUserControl.ConnectionEditViewModel.Connection = PubSubTreeView.SelectedItem as Connection;

                _connectionUserControl.ConnectionEditViewModel.Initialize( );
            }
            else if ( PubSubTreeView.SelectedItem is DataSetWriterGroup )
            {
                _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.DataSetWriterGroup =
                PubSubTreeView.SelectedItem as DataSetWriterGroup;
                _dataSetWriterGroupUserControl.DataSetWriterGroupEditViewModel.Initialize( );
            }
            else if ( PubSubTreeView.SelectedItem is DataSetWriterDefinition )
            {
                _dataSetWriterUserControl.DataSetWriterEditViewModel.DataSetWriterDefinition =
                PubSubTreeView.SelectedItem as DataSetWriterDefinition;
                _dataSetWriterUserControl.DataSetWriterEditViewModel.Initialize( );
            }
            else if ( PubSubTreeView.SelectedItem is ReaderGroupDefinition )
            {
                _readerGroupUserControl.ReaderGroupEditViewModel.ReaderGroupDefinition =
                PubSubTreeView.SelectedItem as ReaderGroupDefinition;
                _readerGroupUserControl.ReaderGroupEditViewModel.Initialize( );
            }
            else if ( PubSubTreeView.SelectedItem is DataSetReaderDefinition )
            {
                _dataSetReaderUserControl.DataSetReaderEditViewModel.ReaderDefinition =
                PubSubTreeView.SelectedItem as DataSetReaderDefinition;
                _dataSetReaderUserControl.DataSetReaderEditViewModel.Initialize( );
            }
        }

        #endregion

        #region Constructors

        public PubSubConfigurationView( OPCUAClientAdaptor opcuaClientAdaptor )
        {
            InitializeComponent( );
            ViewModel = new PubSubViewModel( opcuaClientAdaptor );
            DataContext = ViewModel;
            _connectionUserControl = new ConnectionUserControl( );
            _dataSetWriterGroupUserControl = new DataSetWriterGroupUserControl( );
            _dataSetWriterUserControl = new DataSetWriterUserControl( );
            _readerGroupUserControl = new ReaderGroupUserControl( );
            _dataSetReaderUserControl = new DataSetReaderUserControl( );
            _fieldTargetVariableUserControl = new FieldTargetVariableUserControl( );
            UpdateMenuandButtonVisibility( );
        }

        #endregion

        #region Public Property

        public PubSubViewModel ViewModel
        {
            get { return _viewModel; }
            set { _viewModel = value; }
        }

        #endregion
    }
}