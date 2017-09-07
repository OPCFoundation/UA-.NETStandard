using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using ClientAdaptor;
using Opc.Ua;
using PubSubBase.Definitions;
using PubSubConfigurationUI.Views;

namespace PubSubConfigurationUI.ViewModels
{
    public class PubSubViewModel : BaseViewModel
    {
        #region Private Member 

        private readonly IOPCUAClientAdaptor _ClientAdaptor;
        private Visibility _IsAddTargetVariablesVisible = Visibility.Collapsed;
        private Visibility _IsCancelVisible = Visibility.Collapsed;
        private Visibility _IsConnectionVisible = Visibility.Visible;
        private Visibility _IsDataSetMirrorVisible = Visibility.Collapsed;
        private Visibility _IsDataSetReaderVisible = Visibility.Collapsed;
        private Visibility _IsDataSetWriterVisible = Visibility.Collapsed;
        private Visibility _IsReaderGroupVisible = Visibility.Collapsed;
        private Visibility _IsRemoveConnectionVisible = Visibility.Collapsed;
        private Visibility _IsRemoveDataSetReaderVisible = Visibility.Collapsed;
        private Visibility _IsRemoveDataSetWriterVisible = Visibility.Collapsed;
        private Visibility _IsRemoveReaderGroupVisible = Visibility.Collapsed;
        private Visibility _IsRemoveTargetVariableVisible = Visibility.Collapsed;
        private Visibility _IsRemoveWriterGroupVisible = Visibility.Collapsed;
        private Visibility _IsUpdateVisible = Visibility.Collapsed;
        private Visibility _IsWriterGroupVisible = Visibility.Collapsed;
        private ObservableCollection< PubSubConfiguationBase > _PubSubCollectionItems;
        private PubSubConfiguationBase PubSubCollectionBase;
        private PubSubConfiguationBase PubSubConfiguationTargetvariable;

        #endregion

        #region Private Methods

        private void _AddTargetVariables_Closing( object sender, CancelEventArgs e )
        {
            var _AddTargetVariables = sender as AddTargetVariables;
            if ( _AddTargetVariables._isApplied )
            {
                var errMsg = string.Empty;
                if ( PubSubConfiguationTargetvariable is DataSetReaderDefinition )
                {
                    var DataSetReaderNodeId = (PubSubConfiguationTargetvariable as DataSetReaderDefinition)
                    .DataSetReaderNodeId;
                    errMsg = _ClientAdaptor.AddTargetVariables(
                        (PubSubConfiguationTargetvariable as DataSetReaderDefinition).DataSetReaderNodeId,
                        Convert.ToUInt16( _AddTargetVariables.MinorVersion.Text ),
                        Convert.ToUInt16( _AddTargetVariables.MajorVersion.Text ),
                        _AddTargetVariables._targetVariablesViewModel.VariableListDefinitionCollection );
                    PubSubConfiguationTargetvariable.Children.Clear( );
                    // if(DataSetReaderDefinition!=null)
                    {
                        var _SubscribedDataSetDefinition = new SubscribedDataSetDefinition( );
                        _SubscribedDataSetDefinition.Name = "SubscribedDataSet";
                        _SubscribedDataSetDefinition.ParentNode = PubSubConfiguationTargetvariable;
                        _SubscribedDataSetDefinition.ConfigurationVersionDataType =
                        new ConfigurationVersionDataType
                        {
                            MinorVersion =
                            Convert.ToUInt16( _AddTargetVariables.MinorVersion.Text ),
                            MajorVersion =
                            Convert.ToUInt16( _AddTargetVariables.MajorVersion.Text )
                        };

                        _SubscribedDataSetDefinition.FieldTargetVariableDefinitionCollection = _AddTargetVariables
                        ._targetVariablesViewModel.VariableListDefinitionCollection;
                        foreach ( var _FieldTargetVariableDefinition in _AddTargetVariables
                        ._targetVariablesViewModel.VariableListDefinitionCollection )
                        {
                            _FieldTargetVariableDefinition.ParentNode = _SubscribedDataSetDefinition;
                            _SubscribedDataSetDefinition.Children.Add( _FieldTargetVariableDefinition );
                        }
                        PubSubConfiguationTargetvariable.Children.Add( _SubscribedDataSetDefinition );
                    }
                }
                else
                {
                    var _SubscribedDataSetDefinition = PubSubConfiguationTargetvariable as SubscribedDataSetDefinition;
                    _SubscribedDataSetDefinition.ConfigurationVersionDataType =
                    new ConfigurationVersionDataType
                    {
                        MinorVersion =
                        Convert.ToUInt16( _AddTargetVariables.MinorVersion.Text ),
                        MajorVersion =
                        Convert.ToUInt16( _AddTargetVariables.MajorVersion.Text )
                    };
                    errMsg = _ClientAdaptor.AddAdditionalTargetVariables(
                        new NodeId(
                            (_SubscribedDataSetDefinition.ParentNode as DataSetReaderDefinition)
                            .DataSetReaderNodeId.Identifier + ".SubscribedDataSet", 1 ),
                        Convert.ToUInt16( _AddTargetVariables.MinorVersion.Text ),
                        Convert.ToUInt16( _AddTargetVariables.MajorVersion.Text ),
                        _AddTargetVariables._targetVariablesViewModel.VariableListDefinitionCollection );
                    if ( string.IsNullOrWhiteSpace( errMsg ) )
                    {
                        foreach ( var _FieldTargetVariableDefinition in _AddTargetVariables
                        ._targetVariablesViewModel.VariableListDefinitionCollection )
                        {
                            _FieldTargetVariableDefinition.ParentNode = _SubscribedDataSetDefinition;
                            _SubscribedDataSetDefinition.Children.Add( _FieldTargetVariableDefinition );
                        }
                        (PubSubConfiguationTargetvariable.ParentNode as DataSetReaderDefinition)
                        .DataSetMetaDataType.ConfigurationVersion.MinorVersion =
                        Convert.ToUInt16( _AddTargetVariables.MinorVersion.Text );
                        (PubSubConfiguationTargetvariable.ParentNode as DataSetReaderDefinition)
                        .DataSetMetaDataType.ConfigurationVersion.MajorVersion =
                        Convert.ToUInt16( _AddTargetVariables.MajorVersion.Text );
                    }
                    else
                    {
                        MessageBox.Show( errMsg, "Add Target Variables" );
                        _AddTargetVariables._isApplied = false;
                        e.Cancel = true;
                        return;
                    }
                }

                if ( !string.IsNullOrWhiteSpace( errMsg ) )
                {
                    MessageBox.Show( errMsg, "Add Variables" );
                    _AddTargetVariables._isApplied = false;
                    e.Cancel = true;
                }
            }
        }

        #endregion

        #region Constructors

        public PubSubViewModel( IOPCUAClientAdaptor OPCUAClientAdaptor )
        {
            _ClientAdaptor = OPCUAClientAdaptor;
            PubSubCollectionItems = new ObservableCollection< PubSubConfiguationBase >( );
        }

        #endregion

        #region Public Property

        public Visibility IsConnectionVisible
        {
            get { return _IsConnectionVisible; }
            set
            {
                _IsConnectionVisible = value;
                OnPropertyChanged( "IsConnectionVisible" );
            }
        }

        public Visibility IsAddTargetVariablesVisible
        {
            get { return _IsAddTargetVariablesVisible; }
            set
            {
                _IsAddTargetVariablesVisible = value;
                OnPropertyChanged( "IsAddTargetVariablesVisible" );
            }
        }

        public Visibility IsDataSetMirrorVisible
        {
            get { return _IsDataSetMirrorVisible; }
            set
            {
                _IsDataSetMirrorVisible = value;
                OnPropertyChanged( "IsDataSetMirrorVisible" );
            }
        }

        public Visibility IsRemoveTargetVariableVisible
        {
            get { return _IsRemoveTargetVariableVisible; }
            set
            {
                _IsRemoveTargetVariableVisible = value;
                OnPropertyChanged( "IsRemoveTargetVariableVisible" );
            }
        }

        public Visibility IsWriterGroupVisible
        {
            get { return _IsWriterGroupVisible; }
            set
            {
                _IsWriterGroupVisible = value;
                OnPropertyChanged( "IsWriterGroupVisible" );
            }
        }

        public Visibility IsDataSetWriterVisible
        {
            get { return _IsDataSetWriterVisible; }
            set
            {
                _IsDataSetWriterVisible = value;
                OnPropertyChanged( "IsDataSetWriterVisible" );
            }
        }

        public Visibility IsReaderGroupVisible
        {
            get { return _IsReaderGroupVisible; }
            set
            {
                _IsReaderGroupVisible = value;
                OnPropertyChanged( "IsReaderGroupVisible" );
            }
        }

        public Visibility IsDataSetReaderVisible
        {
            get { return _IsDataSetReaderVisible; }
            set
            {
                _IsDataSetReaderVisible = value;
                OnPropertyChanged( "IsDataSetReaderVisible" );
            }
        }

        public Visibility IsRemoveWriterGroupVisible
        {
            get { return _IsRemoveWriterGroupVisible; }
            set
            {
                _IsRemoveWriterGroupVisible = value;
                OnPropertyChanged( "IsRemoveWriterGroupVisible" );
            }
        }

        public Visibility IsRemoveDataSetWriterVisible
        {
            get { return _IsRemoveDataSetWriterVisible; }
            set
            {
                _IsRemoveDataSetWriterVisible = value;
                OnPropertyChanged( "IsRemoveDataSetWriterVisible" );
            }
        }

        public Visibility IsRemoveReaderGroupVisible
        {
            get { return _IsRemoveReaderGroupVisible; }
            set
            {
                _IsRemoveReaderGroupVisible = value;
                OnPropertyChanged( "IsRemoveReaderGroupVisible" );
            }
        }

        public Visibility IsRemoveDataSetReaderVisible
        {
            get { return _IsRemoveDataSetReaderVisible; }
            set
            {
                _IsRemoveDataSetReaderVisible = value;
                OnPropertyChanged( "IsRemoveDataSetReaderVisible" );
            }
        }

        public Visibility IsRemoveConnectionVisible
        {
            get { return _IsRemoveConnectionVisible; }
            set
            {
                _IsRemoveConnectionVisible = value;
                OnPropertyChanged( "IsRemoveConnectionVisible" );
            }
        }

        public Visibility IsUpdateVisible
        {
            get { return _IsUpdateVisible; }
            set
            {
                _IsUpdateVisible = value;
                OnPropertyChanged( "IsUpdateVisible" );
            }
        }

        public Visibility IsCancelVisible
        {
            get { return _IsCancelVisible; }
            set
            {
                _IsCancelVisible = value;
                OnPropertyChanged( "IsCancelVisible" );
            }
        }

        public ObservableCollection< PubSubConfiguationBase > PubSubCollectionItems
        {
            get { return _PubSubCollectionItems; }
            set
            {
                _PubSubCollectionItems = value;
                OnPropertyChanged( "PubSubCollectionItems" );
            }
        }

        #endregion

        #region Public Methods

        public void AddBrokerConnection( Connection connection )
        {
            NodeId connectionId;
            var errorMessage = _ClientAdaptor.AddAMQPConnection( connection, out connectionId );
            if ( string.IsNullOrWhiteSpace( errorMessage ) )
            {
                connection.ConnectionNodeId = connectionId;
                PubSubCollectionItems.Add( connection );
            }
            else
            {
                MessageBox.Show( errorMessage );
            }
        }

        public bool AddDataSetMirror( DataSetReaderDefinition _DataSetReaderDefinition, string parentName )
        {
            var errMsg = _ClientAdaptor.AddDataSetMirror( _DataSetReaderDefinition, parentName );
            if ( string.IsNullOrWhiteSpace( errMsg ) )
            {
                var _ReferenceDescriptionCollection =
                _ClientAdaptor.Browse( _DataSetReaderDefinition.DataSetReaderNodeId );
                foreach ( var _ReferenceDescription in _ReferenceDescriptionCollection )
                    if ( _ReferenceDescription.BrowseName.Name == "SubscribedDataSet" )
                    {
                        var _RefDescriptionCollection =
                        _ClientAdaptor.Browse( ( NodeId ) _ReferenceDescription.NodeId );
                        foreach ( var _RefDescription in _RefDescriptionCollection )
                            if ( _RefDescription.TypeDefinition == Constants.BaseDataVariableType )
                            {
                                _DataSetReaderDefinition.Children.Clear( );
                                var _MirrorSubscribedDataSetDefinition = new MirrorSubscribedDataSetDefinition( );
                                _MirrorSubscribedDataSetDefinition.Name = "SubscribedDataSet";
                                _MirrorSubscribedDataSetDefinition.ParentNode = _DataSetReaderDefinition;
                                var _RefDesCollection = _ClientAdaptor.Browse( ( NodeId ) _RefDescription.NodeId );
                                foreach ( var _RefDesc in _RefDesCollection )
                                {
                                    var _MirrorVariableDefinition = new MirrorVariableDefinition( );
                                    _MirrorVariableDefinition.Name = _RefDesc.DisplayName.Text;
                                    _MirrorVariableDefinition.ParentNode = _MirrorSubscribedDataSetDefinition;
                                    _MirrorSubscribedDataSetDefinition.Children.Add( _MirrorVariableDefinition );
                                }

                                _DataSetReaderDefinition.Children.Add( _MirrorSubscribedDataSetDefinition );
                                break;
                            }
                    }
            }
            else
            {
                MessageBox.Show( errMsg, "Add DataSet Mirror" );
                return false;
            }
            return true;
        }

        public bool AddDataSetReader(
            ReaderGroupDefinition _ReaderGroupDefinition, DataSetReaderDefinition _DataSetReaderDefinition )
        {
            _DataSetReaderDefinition.ParentNode = _ReaderGroupDefinition;
            var errorMessage =
            _ClientAdaptor.AddDataSetReader( _ReaderGroupDefinition.GroupId, _DataSetReaderDefinition );
            if ( !string.IsNullOrWhiteSpace( errorMessage ) )
            {
                MessageBox.Show( errorMessage, "Add DataSet Reader" );
                return false;
            }
            _ReaderGroupDefinition.Children.Add( _DataSetReaderDefinition );
            return true;
        }

        public bool AddDataSetWriter(
            DataSetWriterGroup _DataSetWriterGroup, DataSetWriterDefinition _DataSetWriterDefinition )
        {
            NodeId writerNodeId = null;
            int revisedKeyFrameCount;
            int revisedMaxMessageSize;
            var errorMessage = string.Empty;
            var _Connection = _DataSetWriterGroup.ParentNode as Connection;

            if ( _Connection.ConnectionType == 0 )
            {
                errorMessage =
                _ClientAdaptor.AddUADPDataSetWriter( _DataSetWriterGroup.GroupId, _DataSetWriterDefinition,
                                                     out writerNodeId, out revisedKeyFrameCount );

                _DataSetWriterDefinition.WriterNodeId = writerNodeId;
                _DataSetWriterDefinition.RevisedKeyFrameCount = revisedKeyFrameCount;
            }
            if ( _Connection.ConnectionType == 1 )
            {
                errorMessage =
                _ClientAdaptor.AddAMQPDataSetWriter( _DataSetWriterGroup.GroupId, _DataSetWriterDefinition,
                                                     out writerNodeId, out revisedMaxMessageSize );

                _DataSetWriterDefinition.WriterNodeId = writerNodeId;
                _DataSetWriterDefinition.RevisedMaxMessageSize = revisedMaxMessageSize;
            }
            if ( !string.IsNullOrWhiteSpace( errorMessage ) )
            {
                MessageBox.Show( errorMessage, "Add DataSet Writer" );
                return false;
            }

            _DataSetWriterGroup.Children.Add( _DataSetWriterDefinition );
            return true;
        }

        public bool AddReaderGroup( Connection _Connection, ReaderGroupDefinition _ReaderGroupDefinition )
        {
            NodeId groupId;
            _ReaderGroupDefinition.ParentNode = _Connection;
            var errorMessage = _ClientAdaptor.AddReaderGroup( _ReaderGroupDefinition, out groupId );

            if ( !string.IsNullOrWhiteSpace( errorMessage ) )
            {
                MessageBox.Show( errorMessage, "Add Reader Group" );
                return false;
            }

            _ReaderGroupDefinition.GroupId = groupId;
            _Connection.Children.Add( _ReaderGroupDefinition );
            return true;
        }

        public void AddTargetVariables( PubSubConfiguationBase ParentNode )
        {
            AddTargetVariables _AddTargetVariables = null;
            if ( ParentNode is DataSetReaderDefinition )
                _AddTargetVariables =
                new AddTargetVariables( _ClientAdaptor, MainViewModel.Rootnode,
                                        (ParentNode as DataSetReaderDefinition).DataSetMetaDataType );
            else
                _AddTargetVariables =
                new AddTargetVariables( _ClientAdaptor, MainViewModel.Rootnode,
                                        (ParentNode.ParentNode as DataSetReaderDefinition).DataSetMetaDataType );

            _AddTargetVariables.Closing += _AddTargetVariables_Closing;
            PubSubConfiguationTargetvariable = ParentNode;
            _AddTargetVariables.TargetVariableUserControl.TargetVariableTxt.Visibility = Visibility.Collapsed;
            _AddTargetVariables.ShowInTaskbar = false;
            _AddTargetVariables.ShowDialog( );
        }

        public void AddUADPConnection( Connection connection )
        {
            NodeId connectionId;
            var errorMessage = _ClientAdaptor.AddUADPConnection( connection, out connectionId );
            if ( string.IsNullOrWhiteSpace( errorMessage ) )
            {
                connection.ConnectionNodeId = connectionId;
                PubSubCollectionItems.Add( connection );
            }
            else
            {
                MessageBox.Show( errorMessage );
            }
        }

        public bool AddWriterGroup( Connection _Connection, DataSetWriterGroup _DataSetWriterGroup )
        {
            NodeId groupId;
            _DataSetWriterGroup.ParentNode = _Connection;
            var errorMessage = _ClientAdaptor.AddWriterGroup( _DataSetWriterGroup, out groupId );
            if ( !string.IsNullOrWhiteSpace( errorMessage ) )
            {
                MessageBox.Show( errorMessage, "Add Writer Group" );
                return false;
            }

            _DataSetWriterGroup.GroupId = groupId;
            _Connection.Children.Add( _DataSetWriterGroup );
            return true;
        }

        public ReferenceDescriptionCollection Browse( NodeId NodeId )
        {
            return _ClientAdaptor.Browse( NodeId );
        }

        public void Initialize( )
        {
            PubSubCollectionItems = _ClientAdaptor.GetPubSubConfiguation( );
            PubSubCollectionBase = new PubSubConfiguationBase( );
        }

        public object ReadValue( NodeId nodeId )
        {
            return _ClientAdaptor.ReadValue( nodeId );
        }

        public void RemoveConnection( Connection connection )
        {
            var errorMessage = _ClientAdaptor.RemoveConnection( connection.ConnectionNodeId );

            if ( string.IsNullOrWhiteSpace( errorMessage ) ) PubSubCollectionItems.Remove( connection );
            else MessageBox.Show( errorMessage );
        }

        public void RemoveDataSetReader( DataSetReaderDefinition _DataSetReaderDefinition )
        {
            var _ReaderGroupDefinition = _DataSetReaderDefinition.ParentNode as ReaderGroupDefinition;
            var errorMessage =
            _ClientAdaptor.RemoveDataSetReader( _ReaderGroupDefinition, _DataSetReaderDefinition.DataSetReaderNodeId );
            if ( string.IsNullOrWhiteSpace( errorMessage ) )
                _ReaderGroupDefinition.Children.Remove( _DataSetReaderDefinition );
            else MessageBox.Show( errorMessage, "Remove DataSet Reader" );
        }

        public void RemoveDataSetWriter( DataSetWriterDefinition _DataSetWriterDefinition )
        {
            var _DataSetWriterGroup = _DataSetWriterDefinition.ParentNode as DataSetWriterGroup;
            var errorMessage =
            _ClientAdaptor.RemoveDataSetWriter( _DataSetWriterGroup, _DataSetWriterDefinition.WriterNodeId );
            if ( string.IsNullOrWhiteSpace( errorMessage ) )
                _DataSetWriterGroup.Children.Remove( _DataSetWriterDefinition );
            else MessageBox.Show( errorMessage, "Remove DataSet Reader" );
        }

        public void RemoveReaderGroup( ReaderGroupDefinition _ReaderGroupDefinition )
        {
            var Connection = _ReaderGroupDefinition.ParentNode as Connection;
            var errorMessage = _ClientAdaptor.RemoveGroup( Connection, _ReaderGroupDefinition.GroupId );

            if ( string.IsNullOrWhiteSpace( errorMessage ) ) Connection.Children.Remove( _ReaderGroupDefinition );
            else MessageBox.Show( errorMessage );
        }

        public void RemoveTargetVariable( FieldTargetVariableDefinition _FieldTargetVariableDefinition )
        {
            var _DataSetReaderDefinition =
            _FieldTargetVariableDefinition.ParentNode.ParentNode as DataSetReaderDefinition;
            var _SubscribedDataSetDefinition = _FieldTargetVariableDefinition.ParentNode as SubscribedDataSetDefinition;
            uint index = 0;
            var TargetToRemove = new List< uint >( );
            foreach ( var _PubSubConfiguationBase in _FieldTargetVariableDefinition.ParentNode.Children )
            {
                if ( _PubSubConfiguationBase.Name == _FieldTargetVariableDefinition.Name )
                {
                    TargetToRemove.Add( index );

                    break;
                }
                index++;
            }

            var errMsg =
            _ClientAdaptor.RemoveTargetVariable(
                new NodeId( _DataSetReaderDefinition.DataSetReaderNodeId.Identifier + ".SubscribedDataSet", 1 ),
                _DataSetReaderDefinition.DataSetMetaDataType.ConfigurationVersion, TargetToRemove );
            if ( string.IsNullOrWhiteSpace( errMsg ) )
                _FieldTargetVariableDefinition.ParentNode.Children.RemoveAt( ( int ) TargetToRemove[ 0 ] );
        }

        public void RemoveWriterGroup( DataSetWriterGroup _DataSetWriterGroup )
        {
            var Connection = _DataSetWriterGroup.ParentNode as Connection;
            var errorMessage = _ClientAdaptor.RemoveGroup( Connection, _DataSetWriterGroup.GroupId );

            if ( string.IsNullOrWhiteSpace( errorMessage ) ) Connection.Children.Remove( _DataSetWriterGroup );
            else MessageBox.Show( errorMessage );
        }

        public void UpdateConnection( Connection connection )
        {
            if ( connection.PublisherId == null )
            {
                MessageBox.Show( "Publisher Id value Cannot be empty", "Add Connection");
                return;
            }
            if ( string.IsNullOrWhiteSpace( connection.Address ) )
            {
                MessageBox.Show( "Publisher Id value Cannot be empty", "Add Connection");
                return;
            }
            object PublisherId = null;
            if ( connection.PublisherDataType == 0 )
            {
                PublisherId = connection.PublisherId;
            }
            else if ( connection.PublisherDataType == 1 )
            {
                byte PublisherIdbyteType;
                if ( !byte.TryParse( connection.PublisherId.ToString( ), out PublisherIdbyteType ) )
                {
                    MessageBox.Show( "Publisher Id value doesn't match for the selected DataType","Add Connection");
                    connection.PublisherId = null;
                    return;
                }
                PublisherId = PublisherIdbyteType;
            }
            else if ( connection.PublisherDataType == 2 )
            {
                ushort PublisherIdType;
                if ( !ushort.TryParse( connection.PublisherId.ToString( ), out PublisherIdType ) )
                {
                    MessageBox.Show( "Publisher Id value doesn't match for the selected DataType", "Add Connection");
                    connection.PublisherId = null;
                    return;
                }
                PublisherId = PublisherIdType;
            }
            else if ( connection.PublisherDataType == 3 )
            {
                uint PublisherIdType;
                if ( !uint.TryParse( connection.PublisherId.ToString( ), out PublisherIdType ) )
                {
                    MessageBox.Show( "Publisher Id value doesn't match for the selected DataType", "Add Connection");
                    connection.PublisherId = null;
                    return;
                }
                PublisherId = PublisherIdType;
            }
            else if ( connection.PublisherDataType == 4 )
            {
                ulong PublisherIdType;
                if ( !ulong.TryParse( connection.PublisherId.ToString( ), out PublisherIdType ) )
                {
                    MessageBox.Show( "Publisher Id value doesn't match for the selected DataType", "Add Connection");
                    connection.PublisherId = null;
                    return;
                }
                PublisherId = PublisherIdType;
            }
            else if ( connection.PublisherDataType == 5 )
            {
                Guid PublisherIdType;

                if ( !Guid.TryParse( connection.PublisherId.ToString( ), out PublisherIdType ) )
                {
                    MessageBox.Show( "Publisher Id value doesn't match for the selected DataType", "Add Connection");
                    connection.PublisherId = null;
                    return;
                }
                PublisherId = PublisherIdType;
            }

            var _ReferenceDescriptionCollection = _ClientAdaptor.Browse( connection.ConnectionNodeId );

            if ( _ReferenceDescriptionCollection.Count > 0 )
            {
                var _WriteValueCollection = new WriteValueCollection( );

                foreach ( var _ReferenceDescription in _ReferenceDescriptionCollection )
                    if ( _ReferenceDescription.BrowseName.Name == "Address" )
                    {
                        var _WriteValue = new WriteValue( );
                        _WriteValue.NodeId = ( NodeId ) _ReferenceDescription.NodeId;
                        _WriteValue.AttributeId = Attributes.Value;
                        _WriteValue.Value = new DataValue( connection.Address );

                        _WriteValueCollection.Add( _WriteValue );
                    }
                    else if ( _ReferenceDescription.BrowseName.Name == "PublisherId" )
                    {
                        var _WriteValue = new WriteValue( );
                        _WriteValue.NodeId = ( NodeId ) _ReferenceDescription.NodeId;
                        _WriteValue.AttributeId = Attributes.Value;
                        _WriteValue.Value = new DataValue( new Variant( PublisherId ) );
                        _WriteValueCollection.Add( _WriteValue );
                    }
                var _StatusCollection = _ClientAdaptor.WriteValue( _WriteValueCollection );

                foreach ( var code in _StatusCollection )
                    if ( !StatusCode.IsGood( code ) )
                    {
                        MessageBox.Show( "One or more parameter(s) are failed to write values to the server",
                                         "Add Connection" );
                        break;
                    }
                var _RefeDescriptionCollection = _ClientAdaptor.Browse( connection.ConnectionNodeId );

                if ( _ReferenceDescriptionCollection.Count > 0 )
                    foreach ( var _ReferenceDescription in _ReferenceDescriptionCollection )
                        if ( _ReferenceDescription.BrowseName.Name == "Address" )
                            connection.Address = _ClientAdaptor
                            .ReadValue( ( NodeId ) _ReferenceDescription.NodeId ).ToString( );
                        else if ( _ReferenceDescription.BrowseName.Name == "PublisherId" )
                            connection.PublisherId =
                            _ClientAdaptor.ReadValue( ( NodeId ) _ReferenceDescription.NodeId );
            }
        }

        public StatusCodeCollection WriteValue( WriteValueCollection writeValueCollection )
        {
            return _ClientAdaptor.WriteValue( writeValueCollection );
        }

        #endregion
    }
}