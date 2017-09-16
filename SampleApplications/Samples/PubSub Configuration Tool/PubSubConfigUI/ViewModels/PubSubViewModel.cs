/* Copyright (c) 1996-2017, OPC Foundation. All rights reserved.

   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else

   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/

   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2

   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
    /// <summary>
    /// view model for pub sub view
    /// </summary>
    public class PubSubViewModel : BaseViewModel
    {
        #region Private Fields 

        private readonly IOPCUAClientAdaptor m_clientAdaptor;
        private Visibility m_isAddTargetVariablesVisible = Visibility.Collapsed;
        private Visibility m_isCancelVisible = Visibility.Collapsed;
        private Visibility m_isConnectionVisible = Visibility.Visible;
        private Visibility m_isDataSetMirrorVisible = Visibility.Collapsed;
        private Visibility m_isDataSetReaderVisible = Visibility.Collapsed;
        private Visibility m_isDataSetWriterVisible = Visibility.Collapsed;
        private Visibility m_isReaderGroupVisible = Visibility.Collapsed;
        private Visibility m_isRemoveConnectionVisible = Visibility.Collapsed;
        private Visibility m_isRemoveDataSetReaderVisible = Visibility.Collapsed;
        private Visibility m_isRemoveDataSetWriterVisible = Visibility.Collapsed;
        private Visibility m_isRemoveReaderGroupVisible = Visibility.Collapsed;
        private Visibility m_isRemoveTargetVariableVisible = Visibility.Collapsed;
        private Visibility m_isRemoveWriterGroupVisible = Visibility.Collapsed;
        private Visibility m_isUpdateVisible = Visibility.Collapsed;
        private Visibility m_isWriterGroupVisible = Visibility.Collapsed;
        private ObservableCollection< PubSubConfiguationBase > m_pubSubCollectionItems;
        private PubSubConfiguationBase m_pubSubCollectionBase;
        private PubSubConfiguationBase m_pubSubConfiguationTargetvariable;

        #endregion

        #region Private Methods

        /// <summary>
        /// Method to perform action before closing TargetVariable
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AddTargetVariables_Closing( object sender, CancelEventArgs e )
        {
            var addTargetVariables = sender as AddTargetVariables;
            if ( addTargetVariables._isApplied )
            {
                var errMsg = string.Empty;
                if ( m_pubSubConfiguationTargetvariable is DataSetReaderDefinition )
                {
                    var dataSetReaderNodeId = (m_pubSubConfiguationTargetvariable as DataSetReaderDefinition)
                    .DataSetReaderNodeId;
                    errMsg = m_clientAdaptor.AddTargetVariables(
                        (m_pubSubConfiguationTargetvariable as DataSetReaderDefinition).DataSetReaderNodeId,
                        Convert.ToUInt16( addTargetVariables.MinorVersion.Text ),
                        Convert.ToUInt16( addTargetVariables.MajorVersion.Text ),
                        addTargetVariables._targetVariablesViewModel.VariableListDefinitionCollection );
                    m_pubSubConfiguationTargetvariable.Children.Clear( );
                    // if(DataSetReaderDefinition!=null)
                    {
                        var subscribedDataSetDefinition = new SubscribedDataSetDefinition( );
                        subscribedDataSetDefinition.Name = "SubscribedDataSet";
                        subscribedDataSetDefinition.ParentNode = m_pubSubConfiguationTargetvariable;
                        subscribedDataSetDefinition.ConfigurationVersionDataType =
                        new ConfigurationVersionDataType
                        {
                            MinorVersion =
                            Convert.ToUInt16( addTargetVariables.MinorVersion.Text ),
                            MajorVersion =
                            Convert.ToUInt16( addTargetVariables.MajorVersion.Text )
                        };

                        subscribedDataSetDefinition.FieldTargetVariableDefinitionCollection = addTargetVariables
                        ._targetVariablesViewModel.VariableListDefinitionCollection;
                        foreach ( var _FieldTargetVariableDefinition in addTargetVariables
                        ._targetVariablesViewModel.VariableListDefinitionCollection )
                        {
                            _FieldTargetVariableDefinition.ParentNode = subscribedDataSetDefinition;
                            subscribedDataSetDefinition.Children.Add( _FieldTargetVariableDefinition );
                        }
                        m_pubSubConfiguationTargetvariable.Children.Add( subscribedDataSetDefinition );
                    }
                }
                else
                {
                    var subscribedDataSetDefinition = m_pubSubConfiguationTargetvariable as SubscribedDataSetDefinition;
                    subscribedDataSetDefinition.ConfigurationVersionDataType =
                    new ConfigurationVersionDataType
                    {
                        MinorVersion =
                        Convert.ToUInt16( addTargetVariables.MinorVersion.Text ),
                        MajorVersion =
                        Convert.ToUInt16( addTargetVariables.MajorVersion.Text )
                    };
                    errMsg = m_clientAdaptor.AddAdditionalTargetVariables(
                        new NodeId(
                            (subscribedDataSetDefinition.ParentNode as DataSetReaderDefinition)
                            .DataSetReaderNodeId.Identifier + ".SubscribedDataSet", 1 ),
                        Convert.ToUInt16( addTargetVariables.MinorVersion.Text ),
                        Convert.ToUInt16( addTargetVariables.MajorVersion.Text ),
                        addTargetVariables._targetVariablesViewModel.VariableListDefinitionCollection );
                    if ( string.IsNullOrWhiteSpace( errMsg ) )
                    {
                        foreach ( var _FieldTargetVariableDefinition in addTargetVariables
                        ._targetVariablesViewModel.VariableListDefinitionCollection )
                        {
                            _FieldTargetVariableDefinition.ParentNode = subscribedDataSetDefinition;
                            subscribedDataSetDefinition.Children.Add( _FieldTargetVariableDefinition );
                        }
                        (m_pubSubConfiguationTargetvariable.ParentNode as DataSetReaderDefinition)
                        .DataSetMetaDataType.ConfigurationVersion.MinorVersion =
                        Convert.ToUInt16( addTargetVariables.MinorVersion.Text );
                        (m_pubSubConfiguationTargetvariable.ParentNode as DataSetReaderDefinition)
                        .DataSetMetaDataType.ConfigurationVersion.MajorVersion =
                        Convert.ToUInt16( addTargetVariables.MajorVersion.Text );
                    }
                    else
                    {
                        MessageBox.Show( errMsg, "Add Target Variables" );
                        addTargetVariables._isApplied = false;
                        e.Cancel = true;
                        return;
                    }
                }

                if ( !string.IsNullOrWhiteSpace( errMsg ) )
                {
                    MessageBox.Show( errMsg, "Add Variables" );
                    addTargetVariables._isApplied = false;
                    e.Cancel = true;
                }
            }
        }

        #endregion

        #region Constructors

        public PubSubViewModel( IOPCUAClientAdaptor OPCUAClientAdaptor )
        {
            m_clientAdaptor = OPCUAClientAdaptor;
            PubSubCollectionItems = new ObservableCollection< PubSubConfiguationBase >( );
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsConnectionVisible
        {
            get { return m_isConnectionVisible; }
            set
            {
                m_isConnectionVisible = value;
                OnPropertyChanged( "IsConnectionVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsAddTargetVariablesVisible
        {
            get { return m_isAddTargetVariablesVisible; }
            set
            {
                m_isAddTargetVariablesVisible = value;
                OnPropertyChanged( "IsAddTargetVariablesVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsDataSetMirrorVisible
        {
            get { return m_isDataSetMirrorVisible; }
            set
            {
                m_isDataSetMirrorVisible = value;
                OnPropertyChanged( "IsDataSetMirrorVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsRemoveTargetVariableVisible
        {
            get { return m_isRemoveTargetVariableVisible; }
            set
            {
                m_isRemoveTargetVariableVisible = value;
                OnPropertyChanged( "IsRemoveTargetVariableVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsWriterGroupVisible
        {
            get { return m_isWriterGroupVisible; }
            set
            {
                m_isWriterGroupVisible = value;
                OnPropertyChanged( "IsWriterGroupVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsDataSetWriterVisible
        {
            get { return m_isDataSetWriterVisible; }
            set
            {
                m_isDataSetWriterVisible = value;
                OnPropertyChanged( "IsDataSetWriterVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsReaderGroupVisible
        {
            get { return m_isReaderGroupVisible; }
            set
            {
                m_isReaderGroupVisible = value;
                OnPropertyChanged( "IsReaderGroupVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsDataSetReaderVisible
        {
            get { return m_isDataSetReaderVisible; }
            set
            {
                m_isDataSetReaderVisible = value;
                OnPropertyChanged( "IsDataSetReaderVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsRemoveWriterGroupVisible
        {
            get { return m_isRemoveWriterGroupVisible; }
            set
            {
                m_isRemoveWriterGroupVisible = value;
                OnPropertyChanged( "IsRemoveWriterGroupVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsRemoveDataSetWriterVisible
        {
            get { return m_isRemoveDataSetWriterVisible; }
            set
            {
                m_isRemoveDataSetWriterVisible = value;
                OnPropertyChanged( "IsRemoveDataSetWriterVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsRemoveReaderGroupVisible
        {
            get { return m_isRemoveReaderGroupVisible; }
            set
            {
                m_isRemoveReaderGroupVisible = value;
                OnPropertyChanged( "IsRemoveReaderGroupVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsRemoveDataSetReaderVisible
        {
            get { return m_isRemoveDataSetReaderVisible; }
            set
            {
                m_isRemoveDataSetReaderVisible = value;
                OnPropertyChanged( "IsRemoveDataSetReaderVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsRemoveConnectionVisible
        {
            get { return m_isRemoveConnectionVisible; }
            set
            {
                m_isRemoveConnectionVisible = value;
                OnPropertyChanged( "IsRemoveConnectionVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsUpdateVisible
        {
            get { return m_isUpdateVisible; }
            set
            {
                m_isUpdateVisible = value;
                OnPropertyChanged( "IsUpdateVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsCancelVisible
        {
            get { return m_isCancelVisible; }
            set
            {
                m_isCancelVisible = value;
                OnPropertyChanged( "IsCancelVisible" );
            }
        }

        /// <summary>
        /// defines collection of pub sub configuration base
        /// </summary>
        public ObservableCollection< PubSubConfiguationBase > PubSubCollectionItems
        {
            get { return m_pubSubCollectionItems; }
            set
            {
                m_pubSubCollectionItems = value;
                OnPropertyChanged( "PubSubCollectionItems" );
            }
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// method to add new broker connection
        /// </summary>
        /// <param name="connection"></param>
        public void AddBrokerConnection( Connection connection )
        {
            NodeId connectionId;
            var errorMessage = m_clientAdaptor.AddAMQPConnection( connection, out connectionId );
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

        /// <summary>
        /// Method to add new DataSetMirror
        /// </summary>
        /// <param name="_DataSetReaderDefinition"></param>
        /// <param name="parentName"></param>
        /// <returns></returns>
        public bool AddDataSetMirror( DataSetReaderDefinition _DataSetReaderDefinition, string parentName )
        {
            var errMsg = m_clientAdaptor.AddDataSetMirror( _DataSetReaderDefinition, parentName );
            if ( string.IsNullOrWhiteSpace( errMsg ) )
            {
                var _ReferenceDescriptionCollection =
                m_clientAdaptor.Browse( _DataSetReaderDefinition.DataSetReaderNodeId );
                foreach ( var _ReferenceDescription in _ReferenceDescriptionCollection )
                    if ( _ReferenceDescription.BrowseName.Name == "SubscribedDataSet" )
                    {
                        var _RefDescriptionCollection =
                        m_clientAdaptor.Browse( ( NodeId ) _ReferenceDescription.NodeId );
                        foreach ( var _RefDescription in _RefDescriptionCollection )
                            if ( _RefDescription.TypeDefinition == Constants.BaseDataVariableType )
                            {
                                _DataSetReaderDefinition.Children.Clear( );
                                var _MirrorSubscribedDataSetDefinition = new MirrorSubscribedDataSetDefinition( );
                                _MirrorSubscribedDataSetDefinition.Name = "SubscribedDataSet";
                                _MirrorSubscribedDataSetDefinition.ParentNode = _DataSetReaderDefinition;
                                var _RefDesCollection = m_clientAdaptor.Browse( ( NodeId ) _RefDescription.NodeId );
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

        /// <summary>
        /// Method to add new DataSetReader.
        /// </summary>
        /// <param name="_ReaderGroupDefinition"></param>
        /// <param name="_DataSetReaderDefinition"></param>
        /// <returns></returns>
        public bool AddDataSetReader(ReaderGroupDefinition _ReaderGroupDefinition, DataSetReaderDefinition _DataSetReaderDefinition )
        {
            _DataSetReaderDefinition.ParentNode = _ReaderGroupDefinition;
            var errorMessage =
            m_clientAdaptor.AddDataSetReader( _ReaderGroupDefinition.GroupId, _DataSetReaderDefinition );
            if ( !string.IsNullOrWhiteSpace( errorMessage ) )
            {
                MessageBox.Show( errorMessage, "Add DataSet Reader" );
                return false;
            }
            _ReaderGroupDefinition.Children.Add( _DataSetReaderDefinition );
            return true;
        }

        /// <summary>
        /// Method to add new datasetwriter
        /// </summary>
        /// <param name="_DataSetWriterGroup"></param>
        /// <param name="_DataSetWriterDefinition"></param>
        /// <returns></returns>
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
                m_clientAdaptor.AddUADPDataSetWriter( _DataSetWriterGroup.GroupId, _DataSetWriterDefinition,
                                                     out writerNodeId, out revisedKeyFrameCount );

                _DataSetWriterDefinition.WriterNodeId = writerNodeId;
                _DataSetWriterDefinition.RevisedKeyFrameCount = revisedKeyFrameCount;
            }
            if ( _Connection.ConnectionType == 1 )
            {
                errorMessage =
                m_clientAdaptor.AddAMQPDataSetWriter( _DataSetWriterGroup.GroupId, _DataSetWriterDefinition,
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

        /// <summary>
        /// Method to add new DataReaderGroup
        /// </summary>
        /// <param name="_Connection"></param>
        /// <param name="_ReaderGroupDefinition"></param>
        /// <returns></returns>
        public bool AddReaderGroup( Connection _Connection, ReaderGroupDefinition _ReaderGroupDefinition )
        {
            NodeId groupId;
            _ReaderGroupDefinition.ParentNode = _Connection;
            var errorMessage = m_clientAdaptor.AddReaderGroup( _ReaderGroupDefinition, out groupId );

            if ( !string.IsNullOrWhiteSpace( errorMessage ) )
            {
                MessageBox.Show( errorMessage, "Add Reader Group" );
                return false;
            }

            _ReaderGroupDefinition.GroupId = groupId;
            _Connection.Children.Add( _ReaderGroupDefinition );
            return true;
        }

        /// <summary>
        /// Method to add New Targetvariables.
        /// </summary>
        /// <param name="ParentNode"></param>
        public void AddTargetVariables( PubSubConfiguationBase ParentNode )
        {
            AddTargetVariables _AddTargetVariables = null;
            if ( ParentNode is DataSetReaderDefinition )
                _AddTargetVariables =
                new AddTargetVariables( m_clientAdaptor, MainViewModel.Rootnode,
                                        (ParentNode as DataSetReaderDefinition).DataSetMetaDataType );
            else
                _AddTargetVariables =
                new AddTargetVariables( m_clientAdaptor, MainViewModel.Rootnode,
                                        (ParentNode.ParentNode as DataSetReaderDefinition).DataSetMetaDataType );

            _AddTargetVariables.Closing += AddTargetVariables_Closing;
            m_pubSubConfiguationTargetvariable = ParentNode;
            _AddTargetVariables.TargetVariableUserControl.TargetVariableTxt.Visibility = Visibility.Collapsed;
            _AddTargetVariables.ShowInTaskbar = false;
            _AddTargetVariables.ShowDialog( );
        }

        /// <summary>
        /// Method to add new UADP Connection
        /// </summary>
        /// <param name="connection"></param>
        public void AddUADPConnection( Connection connection )
        {
            NodeId connectionId;
            var errorMessage = m_clientAdaptor.AddUADPConnection( connection, out connectionId );
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

        /// <summary>
        /// Method to add new DataWritergroup
        /// </summary>
        /// <param name="_Connection"></param>
        /// <param name="_DataSetWriterGroup"></param>
        /// <returns></returns>
        public bool AddWriterGroup( Connection _Connection, DataSetWriterGroup _DataSetWriterGroup )
        {
            NodeId groupId;
            _DataSetWriterGroup.ParentNode = _Connection;
            var errorMessage = m_clientAdaptor.AddWriterGroup( _DataSetWriterGroup, out groupId );
            if ( !string.IsNullOrWhiteSpace( errorMessage ) )
            {
                MessageBox.Show( errorMessage, "Add Writer Group" );
                return false;
            }

            _DataSetWriterGroup.GroupId = groupId;
            _Connection.Children.Add( _DataSetWriterGroup );
            return true;
        }

        /// <summary>
        /// Method to browse the selected node
        /// </summary>
        /// <param name="NodeId"></param>
        /// <returns></returns>
        public ReferenceDescriptionCollection Browse( NodeId NodeId )
        {
            return m_clientAdaptor.Browse( NodeId );
        }

        /// <summary>
        /// Initialiser method for PubSub
        /// </summary>
        public void Initialize( )
        {
            PubSubCollectionItems = m_clientAdaptor.GetPubSubConfiguation( );
            m_pubSubCollectionBase = new PubSubConfiguationBase( );
        }

        /// <summary>
        /// method to read value for selected node.
        /// </summary>
        /// <param name="nodeId"></param>
        /// <returns></returns>
        public object ReadValue( NodeId nodeId )
        {
            return m_clientAdaptor.ReadValue( nodeId );
        }

        /// <summary>
        /// Method to remove connection.
        /// </summary>
        /// <param name="connection"></param>
        public void RemoveConnection( Connection connection )
        {
            var errorMessage = m_clientAdaptor.RemoveConnection( connection.ConnectionNodeId );

            if ( string.IsNullOrWhiteSpace( errorMessage ) ) PubSubCollectionItems.Remove( connection );
            else MessageBox.Show( errorMessage );
        }

        /// <summary>
        /// Method to remove selected DataSetReader
        /// </summary>
        /// <param name="_DataSetReaderDefinition"></param>
        public void RemoveDataSetReader( DataSetReaderDefinition _DataSetReaderDefinition )
        {
            var _ReaderGroupDefinition = _DataSetReaderDefinition.ParentNode as ReaderGroupDefinition;
            var errorMessage =
            m_clientAdaptor.RemoveDataSetReader( _ReaderGroupDefinition, _DataSetReaderDefinition.DataSetReaderNodeId );
            if ( string.IsNullOrWhiteSpace( errorMessage ) )
                _ReaderGroupDefinition.Children.Remove( _DataSetReaderDefinition );
            else MessageBox.Show( errorMessage, "Remove DataSet Reader" );
        }

        /// <summary>
        /// Method to remove selected DataSetWriter.
        /// </summary>
        /// <param name="_DataSetWriterDefinition"></param>
        public void RemoveDataSetWriter( DataSetWriterDefinition _DataSetWriterDefinition )
        {
            var _DataSetWriterGroup = _DataSetWriterDefinition.ParentNode as DataSetWriterGroup;
            var errorMessage =
            m_clientAdaptor.RemoveDataSetWriter( _DataSetWriterGroup, _DataSetWriterDefinition.WriterNodeId );
            if ( string.IsNullOrWhiteSpace( errorMessage ) )
                _DataSetWriterGroup.Children.Remove( _DataSetWriterDefinition );
            else MessageBox.Show( errorMessage, "Remove DataSet Reader" );
        }

        /// <summary>
        /// Method to Reader selected ReaderGroup
        /// </summary>
        /// <param name="_ReaderGroupDefinition"></param>
        public void RemoveReaderGroup( ReaderGroupDefinition _ReaderGroupDefinition )
        {
            var Connection = _ReaderGroupDefinition.ParentNode as Connection;
            var errorMessage = m_clientAdaptor.RemoveGroup( Connection, _ReaderGroupDefinition.GroupId );

            if ( string.IsNullOrWhiteSpace( errorMessage ) ) Connection.Children.Remove( _ReaderGroupDefinition );
            else MessageBox.Show( errorMessage );
        }

        /// <summary>
        /// Method to remove selected TargetVariables.
        /// </summary>
        /// <param name="_FieldTargetVariableDefinition"></param>
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
            m_clientAdaptor.RemoveTargetVariable(
                new NodeId( _DataSetReaderDefinition.DataSetReaderNodeId.Identifier + ".SubscribedDataSet", 1 ),
                _DataSetReaderDefinition.DataSetMetaDataType.ConfigurationVersion, TargetToRemove );
            if ( string.IsNullOrWhiteSpace( errMsg ) )
                _FieldTargetVariableDefinition.ParentNode.Children.RemoveAt( ( int ) TargetToRemove[ 0 ] );
        }

        /// <summary>
        /// Method to remove selected writerGroup.
        /// </summary>
        /// <param name="_DataSetWriterGroup"></param>
        public void RemoveWriterGroup( DataSetWriterGroup _DataSetWriterGroup )
        {
            var Connection = _DataSetWriterGroup.ParentNode as Connection;
            var errorMessage = m_clientAdaptor.RemoveGroup( Connection, _DataSetWriterGroup.GroupId );

            if ( string.IsNullOrWhiteSpace( errorMessage ) ) Connection.Children.Remove( _DataSetWriterGroup );
            else MessageBox.Show( errorMessage );
        }

        /// <summary>
        /// Method to update existing connection
        /// </summary>
        /// <param name="connection"></param>
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

            var _ReferenceDescriptionCollection = m_clientAdaptor.Browse( connection.ConnectionNodeId );

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
                var _StatusCollection = m_clientAdaptor.WriteValue( _WriteValueCollection );

                foreach ( var code in _StatusCollection )
                    if ( !StatusCode.IsGood( code ) )
                    {
                        MessageBox.Show( "One or more parameter(s) are failed to write values to the server",
                                         "Add Connection" );
                        break;
                    }
                var _RefeDescriptionCollection = m_clientAdaptor.Browse( connection.ConnectionNodeId );

                if ( _ReferenceDescriptionCollection.Count > 0 )
                    foreach ( var _ReferenceDescription in _ReferenceDescriptionCollection )
                        if ( _ReferenceDescription.BrowseName.Name == "Address" )
                            connection.Address = m_clientAdaptor
                            .ReadValue( ( NodeId ) _ReferenceDescription.NodeId ).ToString( );
                        else if ( _ReferenceDescription.BrowseName.Name == "PublisherId" )
                            connection.PublisherId =
                            m_clientAdaptor.ReadValue( ( NodeId ) _ReferenceDescription.NodeId );
            }
        }

        /// <summary>
        /// Method to write values for selected node
        /// </summary>
        /// <param name="writeValueCollection"></param>
        /// <returns></returns>
        public StatusCodeCollection WriteValue( WriteValueCollection writeValueCollection )
        {
            return m_clientAdaptor.WriteValue( writeValueCollection );
        }

        #endregion
    }
}