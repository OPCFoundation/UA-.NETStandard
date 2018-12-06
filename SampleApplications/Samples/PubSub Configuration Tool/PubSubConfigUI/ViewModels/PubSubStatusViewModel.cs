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
using System.Linq;
using System.Windows;
using ClientAdaptor;
using Opc.Ua;
using Opc.Ua.Client;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    /// <summary>
    /// view model for Pub sub ststus view
    /// </summary>
    public class PubSubStatusViewModel : BaseViewModel
    {
        #region Private Fields 

        private readonly IOPCUAClientAdaptor m_OPCUAClientAdaptor;
        private ObservableCollection< PubSubState > m_pubSubStateCollection = new ObservableCollection< PubSubState >( );
        private PubSubState _PubSubStateItem;
        private ObservableCollection< TreeViewNode > m_serverItems = new ObservableCollection< TreeViewNode >( );
        private ObservableCollection< MonitorNode > m_statusMonitoredItems = new ObservableCollection< MonitorNode >( );

        private readonly Dictionary< string, MonitoredItem > m_monitorItemsDic =
        new Dictionary< string, MonitoredItem >( );

        private Subscription m_subscription;

        #endregion

        #region Private Methods
        /// <summary>
        /// Method to create monitored items to monitor
        /// </summary>
        /// <param name="node"></param>
        /// <param name="subscription"></param>
        /// <param name="logicalTreeName"></param>
        /// <returns></returns>
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
            m_monitorItemsDic[ monitoredItem.StartNodeId.Identifier.ToString( ) ] = monitoredItem;

            var existingNode = StatusMonitoredItems.Where( i => i.Id == node.Id ).FirstOrDefault( );
            if ( existingNode == null ) StatusMonitoredItems.Add( node );

            return monitoredItem;
        }

        /// <summary>
        /// Method to browe child nodes for selected refernce node
        /// </summary>
        /// <param name="_CurrentReferenceDescription"></param>
        private void BrowseChildNodes( ReferenceDescription _CurrentReferenceDescription )
        {
            try
            {
                var referenceDescriptionCollection =
                m_OPCUAClientAdaptor.Browse((NodeId)_CurrentReferenceDescription.NodeId);
                if (referenceDescriptionCollection != null)
                    foreach (var _ReferenceDescription in referenceDescriptionCollection)
                    {
                        try
                        {
                            if (_ReferenceDescription.TypeDefinition == Constants.PubSubStateTypeId)
                            {
                                var node = new MonitorNode();
                                node.ParentNodeId = (NodeId)_ReferenceDescription.NodeId;
                                var refDescriptionCollection =
                                m_OPCUAClientAdaptor.Browse((NodeId)_ReferenceDescription.NodeId);
                                foreach (var _RefDescription in refDescriptionCollection)
                                {
                                    if (_RefDescription.BrowseName.Name == "State")
                                    {
                                        node.Id = _RefDescription.NodeId.ToString();
                                        node.DisplayName = _RefDescription.NodeId.Identifier.ToString();
                                        CreateMonitoredItem(node, m_subscription, node.DisplayName);
                                    }
                                    if (_RefDescription.BrowseName.Name == "Enable")
                                        node.EnableNodeId = (NodeId)_RefDescription.NodeId;
                                    if (_RefDescription.BrowseName.Name == "Disable")
                                        node.DisableNodeId = (NodeId)_RefDescription.NodeId;
                                }
                            }
                            BrowseChildNodes(_ReferenceDescription);
                        }
                        catch (Exception ex)
                        {

                        }
                    }
            }
            catch(Exception ex)
            {

            }
        }

        /// <summary>
        /// event to monitor for monitored items.
        /// </summary>
        /// <param name="monitoredItem"></param>
        /// <param name="e"></param>
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

        /// <summary>
        /// Method get EnumStrings based on node value
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
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
            m_OPCUAClientAdaptor = opcUAClientAdaptor;
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

        #region Public Properties
        /// <summary>
        /// defines collection of pubsub state
        /// </summary>
        public ObservableCollection< PubSubState > PubSubStateCollection
        {
            get { return m_pubSubStateCollection; }
            set
            {
                m_pubSubStateCollection = value;
                OnPropertyChanged( "PubSubStateCollection" );
            }
        }

        /// <summary>
        /// defines collection of monitor node
        /// </summary>
        public ObservableCollection< MonitorNode > StatusMonitoredItems
        {
            get { return m_statusMonitoredItems; }
            set
            {
                m_statusMonitoredItems = value;
                OnPropertyChanged( "StatusMonitoredItems" );
            }
        }

        /// <summary>
        /// defines pub sub status item
        /// </summary>
        public PubSubState PubSubStateItem
        {
            get { return _PubSubStateItem; }
            set
            {
                _PubSubStateItem = value;
                OnPropertyChanged( "PubSubStateItem" );
            }
        }

        
        #endregion

        #region Public Methods
        /// <summary>
        /// Method to add or update current status of subscription.
        /// </summary>
        public void AddorUpdateStatusToSubscription( )
        {
            StatusMonitoredItems.Clear( );
           
            if ( m_subscription != null ) m_subscription.DeleteItems( );

            var referenceDescriptionCollection = m_OPCUAClientAdaptor.Browse( Constants.PublishSubscribeObjectId );
            if ( referenceDescriptionCollection != null )
                foreach ( var referenceDescription in referenceDescriptionCollection )
                {
                    try
                    {
                        if (referenceDescription.TypeDefinition == Constants.PubSubStateTypeId)
                        {
                            var node = new MonitorNode();
                            node.ParentNodeId = (NodeId)referenceDescription.NodeId;
                            var _RefDescriptionCollection =
                            m_OPCUAClientAdaptor.Browse((NodeId)referenceDescription.NodeId);
                            foreach (var _RefDescription in _RefDescriptionCollection)
                            {
                                if (_RefDescription.BrowseName.Name == "State")
                                {
                                    node.Id = _RefDescription.NodeId.ToString();
                                    node.DisplayName = _RefDescription.NodeId.Identifier.ToString();
                                    CreateMonitoredItem(node, m_subscription, node.DisplayName);
                                }
                                if (_RefDescription.BrowseName.Name == "Enable")
                                    node.EnableNodeId = (NodeId)_RefDescription.NodeId;
                                if (_RefDescription.BrowseName.Name == "Disable")
                                    node.DisableNodeId = (NodeId)_RefDescription.NodeId;
                            }
                        }
                        BrowseChildNodes(referenceDescription);
                    }
                    catch(Exception ex)
                    {

                    }
                }
        }

        /// <summary>
        /// Method to Disable PubSub State for the selected node.
        /// </summary>
        /// <param name="_MonitorNode"></param>
        public void DisablePubSubState( MonitorNode _MonitorNode )
        {
            var errmsg = m_OPCUAClientAdaptor.DisablePubSubState( _MonitorNode );
            if ( !string.IsNullOrWhiteSpace( errmsg ) ) MessageBox.Show( errmsg, "Disable State" );
        }

        /// <summary>
        /// Method to Enable PubSub State for the selected node.
        /// </summary>
        /// <param name="_MonitorNode"></param>
        public void EnablePubSubState( MonitorNode _MonitorNode )
        {
            var errmsg = m_OPCUAClientAdaptor.EnablePubSubState( _MonitorNode );
            if ( !string.IsNullOrWhiteSpace( errmsg ) ) MessageBox.Show( errmsg, "Enable State" );
        }

        /// <summary>
        /// Initialiser method for PubSubStatus
        /// </summary>
        public void Initialize( )
        {
            try
            {
                m_subscription = m_OPCUAClientAdaptor.GetPubSubStateSubscription( "PubSubStatus_Subscription" );
                if ( m_subscription == null )
                    m_subscription = m_OPCUAClientAdaptor.CreateSubscription( "PubSubStatus_Subscription", 1 * 1000 );
                AddorUpdateStatusToSubscription( );
            }
            catch ( Exception ex )
            {
                Utils.Trace(ex, "PubSubStatusViewModel:Initialize", ex );
            }
        }

        

        #endregion
    }

    /// <summary>
    /// defines state
    /// </summary>
    public class PubSubState
    {
        #region Public Property
        /// <summary>
        /// defines name of PubSub state
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// defines display name of PubSub state
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// defines value of PubSub state
        /// </summary>
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