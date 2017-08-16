using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using ClientAdaptor;
using NLogManager;
using Opc.Ua;
using Opc.Ua.Client;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    public class PubSubStatusViewModel : BaseViewModel
    {
        #region Private Member 

        private Visibility _DisableBtnVisibility = Visibility.Collapsed;
        private Visibility _EnableBtnVisibility = Visibility.Collapsed;
        private readonly IOPCUAClientAdaptor _OPCUAClientAdaptor;
        private ObservableCollection< PubSubState > _PubSubStateCollection = new ObservableCollection< PubSubState >( );
        private PubSubState _PubSubStateItem;
        private ObservableCollection< TreeViewNode > _serverItems = new ObservableCollection< TreeViewNode >( );
        private ObservableCollection< MonitorNode > _StatusMonitoredItems = new ObservableCollection< MonitorNode >( );

        private readonly Dictionary< string, MonitoredItem > MonitorItemsDic =
        new Dictionary< string, MonitoredItem >( );

        private Subscription subscription;

        #endregion

        #region Private Methods

        private MonitoredItem CreateMonitoredItem( MonitorNode node, Subscription subscription, string logicalTreeName )
        {
            var monitoredItem = new MonitoredItem( );
            monitoredItem.DisplayName = logicalTreeName;
            monitoredItem.StartNodeId = node.Id;
            monitoredItem.AttributeId = Attributes.Value;
            monitoredItem.SamplingInterval = -1;

            monitoredItem.Notification += OnMonitoredItemNotification;

            subscription.AddItem( monitoredItem );

            subscription.ApplyChanges( );
            MonitorItemsDic[ monitoredItem.StartNodeId.Identifier.ToString( ) ] = monitoredItem;

            var existingNode = StatusMonitoredItems.Where( i => i.Id == node.Id ).FirstOrDefault( );
            if ( existingNode == null ) StatusMonitoredItems.Add( node );

            return monitoredItem;
        }

        private void BrowseChildNodes( ReferenceDescription _CurrentReferenceDescription )
        {
            var _ReferenceDescriptionCollection =
            _OPCUAClientAdaptor.Browse( ( NodeId ) _CurrentReferenceDescription.NodeId );
            if ( _ReferenceDescriptionCollection != null )
                foreach ( var _ReferenceDescription in _ReferenceDescriptionCollection )
                {
                    if ( _ReferenceDescription.TypeDefinition == Constants.PubSubStateTypeId )
                    {
                        var node = new MonitorNode( );
                        node.ParantNodeId = ( NodeId ) _ReferenceDescription.NodeId;
                        var _RefDescriptionCollection =
                        _OPCUAClientAdaptor.Browse( ( NodeId ) _ReferenceDescription.NodeId );
                        foreach ( var _RefDescription in _RefDescriptionCollection )
                        {
                            if ( _RefDescription.BrowseName.Name == "State" )
                            {
                                node.Id = _RefDescription.NodeId.ToString( );
                                node.DisplayName = _RefDescription.NodeId.Identifier.ToString( );
                                CreateMonitoredItem( node, subscription, node.DisplayName );
                            }
                            if ( _RefDescription.BrowseName.Name == "Enable" )
                                node.EnableNodeId = ( NodeId ) _RefDescription.NodeId;
                            if ( _RefDescription.BrowseName.Name == "Disable" )
                                node.DisableNodeId = ( NodeId ) _RefDescription.NodeId;
                        }
                    }
                    BrowseChildNodes( _ReferenceDescription );
                }
        }

        private void OnMonitoredItemNotification( MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e )
        {
            var notification = e.NotificationValue as MonitoredItemNotification;
            if ( monitoredItem != null )
            {
                var item = monitoredItem;
                if ( item != null )
                {
                    var node = StatusMonitoredItems.Where( i => i.Id == monitoredItem.StartNodeId ).FirstOrDefault( );
                    node.Value = notification.Value.WrappedValue.ToString( );
                    node.Value = GetEnumString( node );
                }
            }
        }

        private string GetEnumString( MonitorNode node )
        {
            var CurrentValue = node.Value;
            switch ( CurrentValue )
            {
                case "0" :
                    CurrentValue = "Disabled";

                    break;
                case "1" :
                    CurrentValue = "Paused";

                    break;
                case "2" :
                    CurrentValue = "Operational";

                    break;
                case "3" :
                    CurrentValue = "Error";

                    break;
            }
            return CurrentValue;
        }

        #endregion

        #region Constructors

        public PubSubStatusViewModel( IOPCUAClientAdaptor opcUAClientAdaptor )
        {
            _OPCUAClientAdaptor = opcUAClientAdaptor;
            PubSubStateItem = new PubSubState
                              {
                                  Name = PubSubStateEnum.Operational.ToString( ),
                                  Value = "2",
                                  DisplayName = "Operational"
                              };
            PubSubStateCollection.Add( new PubSubState
                                       {
                                           Name = PubSubStateEnum.Disabled.ToString( ),
                                           Value = "0",
                                           DisplayName = "Disabled"
                                       } );
            PubSubStateCollection.Add( new PubSubState
                                       {
                                           Name = PubSubStateEnum.Error.ToString( ),
                                           Value = "1",
                                           DisplayName = "Error"
                                       } );
            PubSubStateCollection.Add( PubSubStateItem );
            PubSubStateCollection.Add( new PubSubState
                                       {
                                           Name = PubSubStateEnum.Paused.ToString( ),
                                           Value = "3",
                                           DisplayName = "Paused"
                                       } );
        }

        #endregion

        #region Public Property

        public ObservableCollection< PubSubState > PubSubStateCollection
        {
            get { return _PubSubStateCollection; }
            set
            {
                _PubSubStateCollection = value;
                OnPropertyChanged( "PubSubStateCollection" );
            }
        }

        public ObservableCollection< TreeViewNode > ServerItems
        {
            get { return _serverItems; }
            set
            {
                _serverItems = value;
                OnPropertyChanged( "ServerItems" );
            }
        }

        public ObservableCollection< MonitorNode > StatusMonitoredItems
        {
            get { return _StatusMonitoredItems; }
            set
            {
                _StatusMonitoredItems = value;
                OnPropertyChanged( "StatusMonitoredItems" );
            }
        }

        public PubSubState PubSubStateItem
        {
            get { return _PubSubStateItem; }
            set
            {
                _PubSubStateItem = value;
                OnPropertyChanged( "PubSubStateItem" );
            }
        }

        public Visibility EnableBtnVisibility
        {
            get { return _EnableBtnVisibility; }
            set
            {
                _EnableBtnVisibility = value;
                OnPropertyChanged( "EnableBtnVisibility" );
            }
        }

        public Visibility DisableBtnVisibility
        {
            get { return _DisableBtnVisibility; }
            set
            {
                _DisableBtnVisibility = value;
                OnPropertyChanged( "DisableBtnVisibility" );
            }
        }

        #endregion

        #region Public Methods

        public void AddorUpdateStatusToSubscription( )
        {
            StatusMonitoredItems.Clear( );
           
            if ( subscription != null ) subscription.DeleteItems( );

            var _ReferenceDescriptionCollection = _OPCUAClientAdaptor.Browse( Constants.PublishSubscribeObjectId );
            if ( _ReferenceDescriptionCollection != null )
                foreach ( var _ReferenceDescription in _ReferenceDescriptionCollection )
                {
                    if ( _ReferenceDescription.TypeDefinition == Constants.PubSubStateTypeId )
                    {
                        var node = new MonitorNode( );
                        node.ParantNodeId = ( NodeId ) _ReferenceDescription.NodeId;
                        var _RefDescriptionCollection =
                        _OPCUAClientAdaptor.Browse( ( NodeId ) _ReferenceDescription.NodeId );
                        foreach ( var _RefDescription in _RefDescriptionCollection )
                        {
                            if ( _RefDescription.BrowseName.Name == "State" )
                            {
                                node.Id = _RefDescription.NodeId.ToString( );
                                node.DisplayName = _RefDescription.NodeId.Identifier.ToString( );
                                CreateMonitoredItem( node, subscription, node.DisplayName );
                            }
                            if ( _RefDescription.BrowseName.Name == "Enable" )
                                node.EnableNodeId = ( NodeId ) _RefDescription.NodeId;
                            if ( _RefDescription.BrowseName.Name == "Disable" )
                                node.DisableNodeId = ( NodeId ) _RefDescription.NodeId;
                        }
                    }
                    BrowseChildNodes( _ReferenceDescription );
                }
        }

        public void DisablePubSubState( MonitorNode _MonitorNode )
        {
            var errmsg = _OPCUAClientAdaptor.DisablePubSubState( _MonitorNode );
            if ( !string.IsNullOrWhiteSpace( errmsg ) ) MessageBox.Show( errmsg, "Disable State" );
        }

        public void EnablePubSubState( MonitorNode _MonitorNode )
        {
            var errmsg = _OPCUAClientAdaptor.EnablePubSubState( _MonitorNode );
            if ( !string.IsNullOrWhiteSpace( errmsg ) ) MessageBox.Show( errmsg, "Enable State" );
        }

        public void Initialize( )
        {
            try
            {
                subscription = _OPCUAClientAdaptor.GetPubSubStateSubscription( "PubSubStatus_Subscription" );
                if ( subscription == null )
                    subscription = _OPCUAClientAdaptor.CreateSubscription( "PubSubStatus_Subscription", 1 * 1000 );
                AddorUpdateStatusToSubscription( );
            }
            catch ( Exception ex )
            {
                Log.ErrorException( "PubSubStatusViewModel:Initialize", ex );
            }
        }

        internal void OnItemDropInDataGrid( TreeViewNode treeViewNode )
        {
            var reference = treeViewNode.Reference;
            if ( reference.NodeClass != NodeClass.Variable.ToString( ) ||
                 treeViewNode.Reference.DisplayName.ToLower( ) != "state" ) return;
            if ( reference != null || reference.NodeClass == NodeClass.Variable.ToString( ) )
            {
                var _ReferenceDescriptionCollection = _OPCUAClientAdaptor.Browse( treeViewNode.ParentId );
                if ( _ReferenceDescriptionCollection != null )
                    foreach ( var _ReferenceDescription in _ReferenceDescriptionCollection )
                        if ( _ReferenceDescription.BrowseName.Name.Contains( "Enable" ) )
                            treeViewNode.IsMethodEnable = Visibility.Visible;
                        else if ( _ReferenceDescription.BrowseName.Name.Contains( "Disable" ) )
                            treeViewNode.IsMethodDisable = Visibility.Visible;
            }
            var logicalTreeName = treeViewNode.Header;
            //MonitoredItem monitoredItem = CreateMonitoredItem(treeViewNode, subscription, logicalTreeName);
        }

        internal void Rebrowse( ref TreeViewNode node )
        {
            _OPCUAClientAdaptor.Rebrowse( ref node );
        }

        #endregion
    }

    public class PubSubState
    {
        #region Public Property

        public string Name { get; set; }

        public string DisplayName { get; set; }

        public string Value { get; set; }

        #endregion
    }

    public enum PubSubStateEnum
    {
        Disabled = 0,
        Paused = 1,
        Operational = 2,
        Error = 3
    }
}