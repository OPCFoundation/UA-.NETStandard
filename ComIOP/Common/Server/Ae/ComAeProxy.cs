/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Opc.Ua.Client;
using OpcRcw.Ae;
using OpcRcw.Comn;

namespace Opc.Ua.Com.Server.Ae
{
    /// <summary>
    /// A base class for classes that implement an OPC COM specification.
    /// </summary>
    internal class ComAeProxy2 : ComProxy
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComDaProxy"/> class.
        /// </summary>
        public ComAeProxy2(SessionCreatedEventHandler callback, ReconnectInProgressEventHandler reconnectCallback)
        {
            m_mapper = new ComNamespaceMapper();
            m_callback = callback;
            m_reconnectCallback = reconnectCallback;
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // TBD
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Called when a new session is created.
        /// </summary>
        protected override void OnSessionCreated()
        {
            lock (Lock)
            {
                // fetch the configuration.
                m_configuration = Endpoint.ParseExtension<ComProxyConfiguration>(null);

                if (m_configuration == null)
                {
                    m_configuration = new ComProxyConfiguration();
                }

                // update the mapping and pass the new session to other objects.
                m_mapper.Initialize(Session, m_configuration);

                // save the configuration.
                Endpoint.UpdateExtension<ComProxyConfiguration>(null, m_configuration);
                SaveConfiguration();
            }

            // invoke callback.
            if (m_callback != null)
            {
                m_callback(Session);
            }
        }

        /// <summary>
        /// Called when a session is reconnected.
        /// </summary>
        protected override void OnSessionReconected()
        {
            if (m_reconnectCallback != null)
            {
                m_reconnectCallback(Session, 0);
            }
        }

        /// <summary>
        /// Called when a session reconnect is scheduled.
        /// </summary>
        protected override void OnReconnectInProgress(int secondsToReconnect)
        {
            if (m_reconnectCallback != null)
            {
                m_reconnectCallback(null, secondsToReconnect);
            }
        }
        
        /// <summary>
        /// Called when a session is removed.
        /// </summary>
        protected override void OnSessionRemoved()
        {
            // TBD
        }
        #endregion

        #region Private Fields
        private ComProxyConfiguration m_configuration;
        private ComNamespaceMapper m_mapper;
        private SessionCreatedEventHandler m_callback;
        private ReconnectInProgressEventHandler m_reconnectCallback;
        #endregion
    }

    internal delegate void SessionCreatedEventHandler(Session session);
    internal delegate void ReconnectInProgressEventHandler(Session session, int secondsToReconnect);

    /// <summary>
    /// OPC AE Event server.
    /// </summary>
    public class ComAeProxy :
        ConnectionPointContainer,
        IOPCCommon,
        IOPCEventServer,
        IOPCEventServer2
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComAeProxy"/> class.
        /// </summary>
        public ComAeProxy()
        {
            RegisterInterface(typeof(OpcRcw.Comn.IOPCShutdown).GUID);
            Global.TheGlobal.ServerListInsert(this);
        }

        /// <summary>
        /// Finalizer
        /// </summary>
        ~ComAeProxy()
        {
            UnregisterInterface(typeof(OpcRcw.Comn.IOPCShutdown).GUID);
            Global.TheGlobal.ServerListRemove(this);
        }
        #endregion

        #region IOPCWrappedServer Members
        /// <summary>
        /// Called when the object is loaded by the COM wrapper process.
        /// </summary>
        public virtual void Load(Guid clsid, ApplicationConfiguration configuration)
        {
            try
            {
                // load the configuration if this is the first time.
                lock (m_staticLock)
                {
                    if (m_configuration == null)
                    {
                        m_configuration = configuration;
                        m_endpointCache = new ConfiguredEndpointCollection(m_configuration);
                        m_verifiedEndpoints = new Dictionary<Guid, ConfiguredEndpoint>();
                        m_configuration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
                    }
                }
                
                lock (m_lock)
                {
                    m_startTime = DateTime.UtcNow;
                    m_lastUpdateTime = DateTime.MinValue;
                    m_clsid = clsid;

                    // create the proxy that manages connections with the server.
                    m_proxy = new ComAeProxy2(OnSessionCreated, OnSessionReconnect);
                    m_proxy.Load(clsid, m_configuration);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in Load().");
                throw ComUtils.CreateComException(e);
            }
        }
        
        /// <summary>
        /// Called when a session is created with a server.
        /// </summary>
        private void OnSessionReconnect(Session session, int secondsToReconnect)
        {
            lock (m_lock)
            {
                m_session = null;
                m_Subscription = null;
                m_AreaNodes.Clear();
            }
        }

        /// <summary>
        /// Called when a session is created with a server.
        /// </summary>
        private void OnSessionCreated(Session session)
        {
            string commonAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string configFileName = Utils.Format(@"{0}\OPC Foundation\ComPseudoServers\{1}.internal.xml", commonAppDataPath, m_clsid);

            lock (m_lock)
            {
                try
                {
                    m_session = null;
                    m_Subscription = null;
                    m_AreaNodes.Clear();

                    m_session = session;

                    // load the config file.
                    if (File.Exists(configFileName))
                    {
                        XmlSerializer ser = new XmlSerializer(typeof(Configuration));
                        TextReader reader = new StreamReader(configFileName);
                        m_configFile = (Configuration)ser.Deserialize(reader);
                        reader.Close();

                        // set the ActualDataType property
                        for (int ii = 0; ii < m_configFile.Attributes.Length - 1; ii++)
                        {
                            NodeId nodeid = new NodeId(m_configFile.Attributes[ii].strDataTypeNodeId);
                            m_configFile.Attributes[ii].ActualDataType = DataTypes.GetSystemType(nodeid, EncodeableFactory.GlobalFactory);
                        }
                    }
                    else
                    {
                        InitConfigInfo(configFileName);
                    }

                    // Obtain the current server table, generate index mapping tables (client->server, server->client)
                    // and update the client side namespace table if necessary due to server changes
                    GenerateNamespaceIndexMappings(m_clsid);

                    // The client side namespace table may have been updated if the server namespace table
                    // has changed therefore save the updated client table.
                    SaveConfigInfo(configFileName);
                    
                    // fetch type tree.
                    m_session.FetchTypeTree(Opc.Ua.ObjectTypeIds.BaseEventType);
                    m_session.FetchTypeTree(Opc.Ua.ReferenceTypeIds.References);
                    
                    //Create UA Event Subscription if none configured in the registry
                    m_Subscription = new Opc.Ua.Client.Subscription(m_session.DefaultSubscription);
                    m_Subscription.PublishingEnabled = true;
                    m_Subscription.PublishingInterval = m_configFile.ProxySubscriptionSettings.PublishingInterval;
                    m_Subscription.KeepAliveCount = m_configFile.ProxySubscriptionSettings.KeepAliveCount;
                    m_Subscription.LifetimeCount = m_configFile.ProxySubscriptionSettings.LifetimeCount;
                    m_Subscription.Priority = m_configFile.ProxySubscriptionSettings.Priority;
                    m_Subscription.MaxNotificationsPerPublish = m_configFile.ProxySubscriptionSettings.MaxNotificationsPerPublish;

                    m_session.AddSubscription(m_Subscription);
                    m_Subscription.Create();
                    m_KeepAliveInterval = (int)(m_Subscription.CurrentPublishingInterval * m_Subscription.CurrentKeepAliveCount);

                    // Add Server object as the only monitored item to this subscription

                    NodeId nodeId_Server = new NodeId(Opc.Ua.Objects.Server);

                    MonitoredItem monitoredItem = CreateMonitoredItemForEvents(nodeId_Server);
                    m_Subscription.AddItem(monitoredItem);
                    m_Subscription.ApplyChanges();

                    AreaNode areaNode = new AreaNode();
                    areaNode.AreaName = "/";
                    ++areaNode.RefCount;
                    areaNode.MonitoredItem = monitoredItem;
                    m_notifiers.Add(monitoredItem.ClientHandle, areaNode);
                    m_AreaNodes.Add("/", areaNode);

                    m_Subscription.Session.Call(
                        Opc.Ua.ObjectTypes.ConditionType,
                        Methods.ConditionType_ConditionRefresh,
                        m_Subscription.Id);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Initializing server after create.");
                    throw ComUtils.CreateComException(e);
                }
            }
        }

        /// <summary>
        /// Saves the proxy configuration file
        /// </summary>
        /// <param name="configFileName">Config filename</param>
        /// <returns></returns>
        private void SaveConfigInfo(string configFileName)
        {
            try
            {
                XmlSerializer ser = new XmlSerializer(typeof(Configuration));
                StreamWriter writer = new StreamWriter(configFileName);
                ser.Serialize(writer, m_configFile);
                writer.Close();
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error saving internal configuration file: {0}", configFileName);
            }
        }

       
        /// <summary>
        /// compares the new server namespace table with the (saved) table known to the client.  Provides a
        /// mapping for any namespaces which are new or removed and indices which have changed.
        /// </summary>
        /// <param name="clsid"></param>
        private void GenerateNamespaceIndexMappings(Guid clsid)
        {
            try
            {
                StringTable savedStringTable = new StringTable();
                for (int i = 0; i < m_configFile.SavedNamespaceTable.Length; i++)
                    savedStringTable.Append(m_configFile.SavedNamespaceTable[i]);

                NamespaceTable serverNamespaceTable = m_session.NamespaceUris;
                string[] serverNamespaceArray = serverNamespaceTable.ToArray();

                for (int i = 0; i < serverNamespaceArray.Length; i++)
                {
                    // Generate the serverIndex->clientIndex mapping table.  Update the client namespace
                    // table in the process if new namespaces have been added to the server namespace
                    m_serverMappingTable.Add(savedStringTable.GetIndexOrAppend(serverNamespaceArray[i]));
                }

                m_configFile.SavedNamespaceTable = savedStringTable.ToArray();

                for (int i = 0; i < m_configFile.SavedNamespaceTable.Length; i++)
                {
                    // Generate the clientIndex->serverIndex mapping table
                    m_clientmappingTable.Add(serverNamespaceTable.GetIndex(m_configFile.SavedNamespaceTable[i]));
                }

            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in InitNamespaceMappingTable");
            }
        }

        /// <summary>
        /// Called when the object is unloaded by the COM wrapper process.
        /// </summary>
        public virtual void Unload()
        {
            lock (m_lock)
            {
                Utils.SilentDispose(m_proxy);
                m_proxy = null;
                m_session = null;
                m_Subscription = null;
            }
        }
        #endregion

        #region Initialization Members
        /// <summary>
        /// Initializing the configuration data. (allUsers + @"\Application Data\OPC Foundation\COM Interop\" + remoteServerDlg.PseudoClassID)
        /// </summary>
        private void InitConfigInfo(string configFileName)
        {
            try
            {
                Browser browser = new Browser(m_session);
                ReferenceDescriptionCollection references = browser.Browse(Opc.Ua.ObjectTypes.BaseEventType);

                if (references == null)
                {
                    throw new Exception("No BaseEventType found in the type hierarchy");
                }

                foreach (ReferenceDescription reference in references)
                {
                    // check for base event types.
                    if (reference.NodeClass == NodeClass.ObjectType)
                    {
                        int cattype = OpcRcw.Ae.Constants.SIMPLE_EVENT;

                        if (reference.NodeId == Opc.Ua.ObjectTypes.ConditionType)
                        {
                            cattype = OpcRcw.Ae.Constants.CONDITION_EVENT;
                        }
                        else if (reference.NodeId == Opc.Ua.ObjectTypes.AuditEventType)
                        {
                            cattype = OpcRcw.Ae.Constants.TRACKING_EVENT;
                        }

                        ProcessNodeAsCategory((NodeId)reference.NodeId, cattype);
                    }

                    // check for properties.
                    else if (reference.NodeClass == NodeClass.Variable)
                    {
                        if (reference.TypeDefinition == Opc.Ua.VariableTypeIds.PropertyType)
                        {
                            ProcessNodeAsAttribute(Opc.Ua.ObjectTypes.BaseEventType, reference);
                        }
                    }
                }

                // Add two special attribute for compatibility with AE COM
                EventAttribute attr1 = new EventAttribute();
                attr1.AttributeID = Global.TheGlobal.StdAttrIds[1];
                attr1.strNodeId = "Areas";
                attr1.BrowseName = "AECOMAreas";
                attr1.BrowseNameNSIndex = 0;
                attr1.AttrDesc = "Areas";
                attr1.strDataTypeNodeId = DataTypes.GetDataTypeId(typeof(string[])).ToString();
                attr1.strEventNodeId = "";
                attr1.IsArray = true;
                attr1.ActualDataType = typeof(string[]);
                m_EventAttributes.Add(attr1);

                attr1 = new EventAttribute();
                attr1.AttributeID = Global.TheGlobal.StdAttrIds[0];
                attr1.strNodeId = "AckComment";
                attr1.BrowseName = "AECOMAckComment";
                attr1.BrowseNameNSIndex = 0;
                attr1.AttrDesc = "AckComment";
                attr1.strDataTypeNodeId = DataTypes.GetDataTypeId(typeof(string)).ToString();
                attr1.strEventNodeId = "";
                attr1.IsArray = false;
                attr1.ActualDataType = typeof(string);
                m_EventAttributes.Add(attr1);

                m_EventAttributes.Sort(EventAttributeNodeIdCompare);
                m_EventCategories.Sort(EventCategoryBrowseNameCompare);

                m_configFile.Categories = m_EventCategories.ToArray();
                m_configFile.Attributes = m_EventAttributes.ToArray();
                m_configFile.LastEventCategoryID = m_catID;
                m_configFile.LastEventAttributeID = m_attrID;

                m_configFile.SavedNamespaceTable = m_session.NamespaceUris.ToArray();

            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in InitConfigInfo");
            }
        }

        /// <summary>
        /// Always accept server certificates.
        /// </summary>
        void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            e.Accept = true;
        }

        /// <summary>
        /// If this node is not abstract, add it as a category to the EventCategories list
        /// and process all nodes beneath it as categories.
        /// Also, add all of its atrributes to the EventAttributes list
        /// </summary>
        /// <param name="nodeId">The node being processed</param>
        /// <param name="CategoryType">The category type id this node will be added as </param>
        private void ProcessNodeAsCategory(NodeId nodeId, int CategoryType)
        {
            try
            {
                Node node = m_session.ReadNode(nodeId);
                EventCategory cat = new EventCategory();
                DataValue value = new DataValue();

                cat.CategoryID = m_catID++;
                cat.strNodeId = node.NodeId.ToString();
                cat.BrowseName = node.BrowseName.ToString();
                cat.EventDesc = node.DisplayName.Text;
                cat.EventType = CategoryType;
                m_EventCategories.Add(cat);

                Browser browse = new Browser(m_session);
                ReferenceDescriptionCollection references = null;

                references = browse.Browse(nodeId);

                foreach (ReferenceDescription reference in references)
                {
                    if (reference.NodeClass == NodeClass.ObjectType)
                    {
                        ProcessNodeAsCategory((NodeId)reference.NodeId, CategoryType);
                    }
                    else if ((reference.NodeClass == NodeClass.Variable) && (reference.ReferenceTypeId == Opc.Ua.ReferenceTypes.HasProperty))
                    {
                        ProcessNodeAsAttribute(nodeId, reference);
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in ProcessNodesAsCategory");
            }
        }

        /// <summary>
        /// Adds a node as an attribute to the EventCategories list
        /// </summary>
        /// <param name="EventTypeNodeId">The event type node</param>
        /// <param name="AttrRef">The reference node being added as an attribute</param>
        private void ProcessNodeAsAttribute(NodeId EventTypeNodeId, ReferenceDescription AttrRef)
        {
            try
            {
                EventAttribute attr = new EventAttribute();
                DataValue value = new DataValue();

                attr.AttributeID = m_attrID++;
                attr.strNodeId = AttrRef.NodeId.ToString();
                attr.BrowseName = AttrRef.BrowseName.Name;
                attr.BrowseNameNSIndex = AttrRef.BrowseName.NamespaceIndex;
                attr.AttrDesc = AttrRef.DisplayName.Text;

                Node node = m_session.ReadNode((NodeId)AttrRef.NodeId);
                node.Read(null, Attributes.DataType, value);
                NodeId typenode = (NodeId)value.Value;
                attr.strDataTypeNodeId = typenode.ToString();
                attr.ActualDataType = DataTypes.GetSystemType(typenode, EncodeableFactory.GlobalFactory);
                attr.strEventNodeId = EventTypeNodeId.ToString();

                node.Read(null, Attributes.ValueRank, value);
                //TODO: Used to be ArraySize. Does ValueRank have the same definition?
                if ((int)value.Value >= 0) //TODO: is there a constant that can be used
                    attr.IsArray = true;
                else
                    attr.IsArray = false;

                m_EventAttributes.Add(attr);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in ProcessNodesAsAttribute");
            }
        }

        /// <summary>
        /// Compare method based off the Attribute NodeId
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        private static int EventAttributeNodeIdCompare(EventAttribute x, EventAttribute y)
        {
            return x.strNodeId.CompareTo(y.strNodeId);
        }

        /// <summary>
        /// Compare method based off the BrowseName
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        private static int EventCategoryBrowseNameCompare(EventCategory x, EventCategory y)
        //TODO: should this be a case insensitive compare?
        {
            return x.BrowseName.ToLower().CompareTo(y.BrowseName.ToLower());
        }

        
        #endregion

        #region MonitoredItem Helper Members
        /// <summary>
        /// Creates a new MonitoredItem for events.  Applies the standard event filters for condition events
        /// and adds the filters necessary to obtain event attributes -- properties of event types/subtypes
        /// </summary>
        /// <param name="nodeId">The node id.</param>
        /// <returns></returns>
        public MonitoredItem CreateMonitoredItemForEvents(NodeId nodeId)
        {
            MonitoredItem monitoredItem = new MonitoredItem(m_Subscription.DefaultItem);
            monitoredItem.AttributeId = Attributes.EventNotifier;
            monitoredItem.DisplayName = m_Subscription.Session.NodeCache.GetDisplayText((ExpandedNodeId)nodeId);
            monitoredItem.StartNodeId = nodeId;
            monitoredItem.NodeClass = NodeClass.Object;

            //get all attribute values from all events
            EventFilter filter = new EventFilter();

            foreach (EventAttribute attr in m_configFile.Attributes)
            {
                if (attr.strEventNodeId != "")
                {
                    filter.AddSelectClause(new NodeId(attr.strEventNodeId), attr.BrowseName);
                    // Utils.Trace("AddSelectClause ( {0}: {1}, {2} )", i++, attr.strEventNodeId, attr.BrowseName);
                }
            }
            
            // Add Condition-related attributes to the filter so that we have enough info to determine
            // Enabled/Disabled, Active/Inactive including time of active transition, Acked/Unacked and active subcondition
            
            filter.AddSelectClause(Opc.Ua.ObjectTypes.ConditionType, "/EnabledState/Id", Attributes.Value);
            filter.AddSelectClause(Opc.Ua.ObjectTypes.ConditionType, "/Quality", Attributes.Value);

            filter.AddSelectClause(Opc.Ua.ObjectTypes.AcknowledgeableConditionType, "/AckedState/Id", Attributes.Value);
            filter.AddSelectClause(Opc.Ua.ObjectTypes.AcknowledgeableConditionType, "", Attributes.NodeId);

            filter.AddSelectClause(Opc.Ua.ObjectTypes.AlarmConditionType, "/ActiveState/Id", Attributes.Value);
            filter.AddSelectClause(Opc.Ua.ObjectTypes.AlarmConditionType, "/ActiveState/TransitionTime", Attributes.Value);
            filter.AddSelectClause(Opc.Ua.ObjectTypes.AlarmConditionType, "/ActiveState/EffectiveDisplayName", Attributes.Value);

            monitoredItem.Filter = filter;

            monitoredItem.Notification += new MonitoredItemNotificationEventHandler(MonitoredItem_Notification);

            return monitoredItem;

        }
        #endregion

        #region Subscription Helper methods

        /// <summary>
        /// Calls the TraslateBrowsePathsToNodeIds service to get the nodeIds for each of the Relative Paths in the list
        /// </summary>
        /// <param name="RelativePaths">The List of Relative Paths</param>
        public BrowsePathResultCollection GetBrowseTargets(List<String> RelativePaths)
        {
            BrowsePathCollection browsePaths = new BrowsePathCollection();
            NamespaceTable clientUris = null;
            try
            {
                clientUris = new NamespaceTable(m_configFile.SavedNamespaceTable);
            }
            catch (Exception)
            {
                return null;
            }

            foreach (string relativePath in RelativePaths)
            {
                BrowsePath browsePath = new BrowsePath();
                browsePath.RelativePath = RelativePath.Parse(relativePath, m_session.TypeTree, clientUris, m_session.NamespaceUris);
                browsePath.StartingNode = Objects.Server;
                browsePaths.Add(browsePath);
            }

            BrowsePathResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = m_session.TranslateBrowsePathsToNodeIds(
                null,
                browsePaths,
                out results,
                out diagnosticInfos);

            // ensure that the server returned valid results.
            Session.ValidateResponse(results, browsePaths);
            Session.ValidateDiagnosticInfos(diagnosticInfos, browsePaths);

            return results;
        }


        /// <summary>
        /// Area filter strings which contain wildcard characters require more processing
        /// </summary>
        public List<string> ProcessWildCardAreaName(string areaName, int wildcardLocation)
        {                
            List<string> areas = new List<string>();
            try
            {
                // Retrieve substring from the area name up to the wildcard character.  
                string areaSubstring = areaName.Substring(0, wildcardLocation);
                //Scan this string for path elements that are completely specified
                BrowsePathTarget target = null;
                string path = "";
                int location = areaSubstring.LastIndexOf('/');
                while (location != -1)
                {
                    if (location == 0)  // Starts with a slash
                        break;
                    // Make sure the '\' is not being treated as a reserved character that is escaped using '&'.
                    if (String.Compare(areaSubstring.Substring(location - 1, 1), "&") != 0)
                        break;
                    // '\'is treated as a special character. Get the next occurance of this character
                    areaSubstring = areaName.Substring(0, location);
                    location = areaSubstring.LastIndexOf('/');
                }
                if (location != -1)
                   path = areaName.Substring(0, location);
                
                if (path.Length != 0)
                {
                    // Get NodeId for this path
                    List<String> paths = new List<string>();
                    paths.Add(path);
                    BrowsePathResultCollection results = GetBrowseTargets(paths);
                    if (!StatusCode.IsBad(results[0].StatusCode))
                    {
                        target = results[0].Targets[0];
                    }
                }
               
                ExpandedNodeId node = null;
                if (location == -1 || target == null)
                    node = Objects.Server;
                else
                    node = target.TargetId;

                // Browse node for all notifiers below it
                IList<INode> children = m_session.NodeCache.Find(node, ReferenceTypes.HasEventSource, false, true);
                foreach (INode child in children)
                {
                    // match the browsename against the client supplied relative path containing the wildcard character.
                    // If browsename matches the pattern, add that to the list of areas to be monitored
                    string pattern = areaName.Substring(location + 1);
                    if (ComUtils.Match(child.BrowseName.ToString(), pattern, false))
                    {
                        string notifierName = path + '/' + child.BrowseName.ToString();
                        areas.Add(notifierName);
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "Unexpected error in ProcessWildCardAreaName");
            }
            return areas;
        }

        /// <summary>
        /// create monitored items for each notifier node identified by the area string(s) added using SetFilter.   
        /// </summary>
        /// <param name="AreaNames">The List of Area names</param>
        public void AddMonitoredItems(List<String> AreaNames)
        {
            try
            {
                List<string> szAreas = new List<string>();
                foreach (string areaName in AreaNames)
                {
                    int wildCardLocation = 0;
                    if ((wildCardLocation = areaName.IndexOfAny(new char[] { '*', '?', '#', '[', ']', '!', '-' })) != -1)
                    {
                        // The string contains wildcards
                        List<string> items = ProcessWildCardAreaName(areaName, wildCardLocation);
                        if (items.Count == 0)
                        {
                            throw ComUtils.CreateComException("AddMonitoredItems", ResultIds.E_INVALIDARG);
                        }
                        foreach (string item in items)
                        {
                            AreaNode areaNode;
                            // Check to see if this area was already added to the subscription as a monitored item
                            if (!m_AreaNodes.TryGetValue(item, out areaNode))
                                szAreas.Add(item);
                            else  // increment reference count for this area
                                ++areaNode.RefCount;
                        }
                    }
                    else
                    {
                        // Check to see if this area was already added to the subscription as a monitored item
                        AreaNode areaNode;
                        if (!m_AreaNodes.TryGetValue(areaName, out areaNode))
                            szAreas.Add(areaName);
                        else  // increment reference count for this area
                            ++areaNode.RefCount;
                    }
                }
                if (szAreas.Count == 0)
                    return;

                // Translate the fully qualified area names to NodeIds
                BrowsePathResultCollection results = GetBrowseTargets(szAreas);
                for (int ii = 0; ii < results.Count; ii++)
                {
                    if (StatusCode.IsBad(results[ii].StatusCode))
                    {
                        throw ComUtils.CreateComException("AddMonitoredItems", ResultIds.E_INVALIDARG);
                    }
                    // Add monitored item to the subscription
                    BrowsePathTarget target = results[ii].Targets[0];
                    NodeId node;
                    node = ExpandedNodeId.ToNodeId(target.TargetId, m_session.NamespaceUris);
                    MonitoredItem monitoredItem = CreateMonitoredItemForEvents(node);
                    m_Subscription.AddItem(monitoredItem);
                    m_Subscription.ApplyChanges();
                    // Add this area to the AreaNode and then add the AreaNode to the two dictionary elements
                    AreaNode areaNode = new AreaNode();
                    areaNode.AreaName = szAreas[ii];
                    ++areaNode.RefCount;
                    areaNode.MonitoredItem = monitoredItem;
                    m_notifiers.Add(monitoredItem.ClientHandle, areaNode);
                    m_AreaNodes.Add(szAreas[ii], areaNode);
                }
            }
            catch (COMException ex)
            {
                throw ComUtils.CreateComException(ex);
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "Unexpected error in AddMonitoredItems");
                throw ComUtils.CreateComException(ex);
            }
        }

        /// <summary>
        /// Remove monitored items from the subscription.   
        /// </summary>
        /// <param name="AreaNames">The List of fully qulaified area names of nodes to be removed from the subscription</param>
        public void RemoveMonitoredItems(List<String> AreaNames)
        {
            try
            {
                List<MonitoredItem> removeNodes = new List<MonitoredItem>();
                foreach (string areaName in AreaNames)
                {
                    AreaNode areaNode;
                    if (m_AreaNodes.TryGetValue(areaName, out areaNode))
                    {
                        // decrement reference count
                        --areaNode.RefCount;
                        if (areaNode.RefCount <= 0)
                        {
                            // Add item to list of monitored items to be deleted
                            removeNodes.Add(areaNode.MonitoredItem);
                            m_AreaNodes.Remove(areaNode.AreaName);
                            m_notifiers.Remove(areaNode.MonitoredItem.ClientHandle);
                        }
                    }
                }
                if (removeNodes.Count > 0)  // remove the monitored items from the subscription
                {
                    m_Subscription.RemoveItems(removeNodes);
                    m_Subscription.ApplyChanges();
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in RemoveMonitoredItems");
            }
        }

        #endregion


        #region IOPCCommon Members

        /// <summary>
        /// Sets the Locale ID for the server.
        /// </summary>
        public void SetLocaleID(int dwLcid)
        {
            lock (m_lock)
            {
                if (m_session == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                try
                {
                    bool localeSupported = false;

                    foreach (int locale in m_localeIds)
                    {
                        if (dwLcid == locale)
                        {
                            if (locale != m_lcid)
                            {
                                UpdateLocale(m_session, ComUtils.GetLocale(locale));
                            }

                            // save the passed locale value.
                            m_lcid = dwLcid;
                            localeSupported = true;
                            break;
                        }
                    }

                    if (!localeSupported) throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                }
                catch (COMException e)
                {
                    throw ComUtils.CreateComException(e);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error in SetLocaleID");
                    throw ComUtils.CreateComException(e);
                }
            }
        }

        /// <summary>
        /// Update locale ID for the session.
        /// </summary>
        private void UpdateLocale(Session session, string locale)
        {
            if (session == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

            try
            {
                StringCollection preferredLocales = new StringCollection();
                preferredLocales.Add(locale);
                session.ChangePreferredLocales(preferredLocales);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in UpdateLocale");
                throw ComUtils.CreateComException(e);
            }
        }

        /// <summary>
        /// Get the local IDs from the server.
        /// </summary>
        internal List<int> GetLocaleIDs()
        {
            lock (m_lock)
            {
                string[] locales = null;
                List<int> localeList = new List<int>();
                DataValueCollection values = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                try
                {
                    ReadValueIdCollection nodesToRead = new ReadValueIdCollection();
                    ReadValueId valueId = new ReadValueId();
                    valueId.NodeId = new NodeId(Opc.Ua.Variables.Server_ServerCapabilities_LocaleIdArray);
                    valueId.AttributeId = Attributes.Value;
                    nodesToRead.Add(valueId);

                    // read values from the UA server.
                    ResponseHeader responseHeader = m_session.Read(
                        null,
                        0,
                        TimestampsToReturn.Neither,
                        nodesToRead,
                        out values,
                        out diagnosticInfos);

                    // validate response from the UA server.
                    ClientBase.ValidateResponse(values, nodesToRead);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

                    locales = ((string[])(values[0].Value));

                    // add the default locale
                    localeList.Add(ComUtils.LOCALE_SYSTEM_DEFAULT);
                    localeList.Add(ComUtils.LOCALE_USER_DEFAULT);

                    if (locales != null)
                    {
                        foreach (string locale in locales)
                        {
                            // cache the supported locales.
                            localeList.Add(ComUtils.GetLocale(locale));
                        }
                    }
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error in GetLocaleIds");
                    throw ComUtils.CreateComException(e);
                }

                return localeList;
            }
        }

        /// <summary>
        /// Gets the available Locale IDs from the server.
        /// </summary>
        public void QueryAvailableLocaleIDs(out int pdwCount, out System.IntPtr pdwLcid)
        {
            if (m_session == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

            lock (m_lock)
            {
                try
                {
                    pdwCount = 0;
                    pdwLcid = IntPtr.Zero;

                    // marshal parameters.
                    if (m_localeIds != null && m_localeIds.Count > 0)
                    {
                        pdwLcid = Marshal.AllocCoTaskMem(m_localeIds.Count * Marshal.SizeOf(typeof(int)));

                        int[] lcids = new int[m_localeIds.Count];

                        for (int ii = 0; ii < m_localeIds.Count; ii++)
                        {
                            lcids[ii] = m_localeIds[ii];
                        }

                        Marshal.Copy(lcids, 0, pdwLcid, m_localeIds.Count);
                        pdwCount = m_localeIds.Count;
                    }
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error in QueryAvailableLocaleIDs");
                    throw ComUtils.CreateComException(e);
                }
            }
        }

        /// <summary>
        /// Gets the current Locale ID from the server.
        /// </summary>
        public void GetLocaleID(out int pdwLcid)
        {
            lock (m_lock)
            {
                try
                {
                    pdwLcid = m_lcid;
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error in GetLocaleID");
                    throw ComUtils.CreateComException(e);
                }
            }
        }

        /// <summary>
        /// Gets the error string from the server.
        /// </summary>
        void OpcRcw.Comn.IOPCCommon.GetErrorString(int dwError, out string ppString)
        {
            lock (m_lock)
            {
                try
                {
                    // look up COM errors locally.
                    ppString = ComUtils.GetSystemMessage(dwError, m_lcid);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error in GetErrorString");
                    throw ComUtils.CreateComException(e);
                }
            }
        }

        /// <summary>
        /// Sets the name of the client.
        /// </summary>
        public void SetClientName(string szName)
        {
            lock (m_lock)
            {
                m_clientName = szName;
            }
        }

        #endregion

        #region IOPCEventServer Members


        /// <summary>
        /// Returns the current status information for the Event server.
        /// </summary>
        /// <param name="ppEventServerStatus">The structure returning the status infromation.  The server allocates the structure.</param>
        public void GetStatus(out IntPtr ppEventServerStatus)
        {
            lock (m_lock)
            {
                try
                {
                    OpcRcw.Ae.OPCEVENTSERVERSTATUS status = new OPCEVENTSERVERSTATUS();
                    
                    Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

                    status.dwServerState = OPCEVENTSERVERSTATE.OPCAE_STATUS_COMM_FAULT;
                    status.ftStartTime   = ComUtils.GetFILETIME(m_startTime);
                    status.ftLastUpdateTime = ComUtils.GetFILETIME(m_lastUpdateTime);
                    status.ftCurrentTime = ComUtils.GetFILETIME(DateTime.UtcNow);
                    status.wMajorVersion = (short)version.Major;
                    status.wMinorVersion = (short)version.Minor;
                    status.wBuildNumber  = (short)version.Build;
                    status.szVendorInfo  = "OPC UA COM AE Proxy Server";

                    if (m_session != null)
                    {
                        status.dwServerState = OPCEVENTSERVERSTATE.OPCAE_STATUS_RUNNING;
                        status.szVendorInfo = m_session.ConfiguredEndpoint.ToString();
                    }

                    ppEventServerStatus = Marshal.AllocCoTaskMem(Marshal.SizeOf(status.GetType()));
                    Marshal.StructureToPtr(status, ppEventServerStatus, false);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error in GetStatus");
                    throw ComUtils.CreateComException(e);
                }
            }
        }
        
        /// <summary>
        /// Add an Event Subscription object to an Event Server
        /// </summary>
        /// <param name="bActive">FALSE if the Event Subscription is to be created inactive and TRUE if it is to be created as active.</param>
        /// <param name="dwBufferTime">The requested buffer time. The buffer time is in milliseconds and tells the server how often to send event notifications. A value of 0 for dwBufferTime means that the server should send event notifications as soon as it gets them.</param>
        /// <param name="dwMaxSize">The requested maximum number of events that will be sent in a single IOPCEventSink::OnEvent callback. A value of 0 means that there is no limit to the number of events that will be sent in a single callback.</param>
        /// <param name="hClientSubscription">Client provided handle for this event subscription.</param>
        /// <param name="riid">The type of interface desired</param>
        /// <param name="ppUnk">Where to store the returned interface pointer.</param>
        /// <param name="pdwRevisedBufferTime">The buffer time that the server is actually providing, which may differ from dwBufferTime.</param>
        /// <param name="pdwRevisedMaxSize">The maximum number of events that the server will actually be sending in a single IOPCEventSink::OnEvent callback, which may differ from dwMaxSize.</param>
        public void CreateEventSubscription(int bActive, int dwBufferTime, int dwMaxSize, int hClientSubscription, ref Guid riid, out object ppUnk, out int pdwRevisedBufferTime, out int pdwRevisedMaxSize)
        {
            ppUnk = IntPtr.Zero;
            pdwRevisedBufferTime = 0;
            pdwRevisedMaxSize = 0;
            IntPtr pbActive = IntPtr.Zero;
            IntPtr pdwBufferTime = IntPtr.Zero;
            IntPtr pdwMaxSize = IntPtr.Zero;
            Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");

            try
            {
                Subscription subscription = new Subscription(this);
                if (subscription == null)
                    throw ComUtils.CreateComException("E_OUTOFMEMORY", ResultIds.E_OUTOFMEMORY);

                SubscriptionListInsert(subscription);

                pbActive = Marshal.AllocHGlobal(Marshal.SizeOf(bActive.GetType()));
                pdwBufferTime = Marshal.AllocHGlobal(Marshal.SizeOf(dwBufferTime.GetType()));
                pdwMaxSize = Marshal.AllocHGlobal(Marshal.SizeOf(dwMaxSize.GetType()));

                Marshal.WriteInt32(pbActive, bActive);
                Marshal.WriteInt32(pdwBufferTime, dwBufferTime);
                Marshal.WriteInt32(pdwMaxSize, dwMaxSize);
                subscription.SetState(pbActive, pdwBufferTime, pdwMaxSize, hClientSubscription, out pdwRevisedBufferTime, out pdwRevisedMaxSize);
                subscription.UaSubscription = m_Subscription;
                ppUnk = subscription;
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in CreateEventSubscription");
                throw ComUtils.CreateComException(e);
            }
            finally
            {
                if (pbActive != IntPtr.Zero)
                    Marshal.FreeHGlobal(pbActive);

                if (pdwBufferTime != IntPtr.Zero)
                    Marshal.FreeHGlobal(pdwBufferTime);

                if (pdwMaxSize != IntPtr.Zero)
                    Marshal.FreeHGlobal(pdwMaxSize);
            }

        }

        /// <summary>
        /// Gives clients a means of finding out exactly which filter criteria are supported by a given event server.
        /// </summary>
        /// <param name="pdwFilterMask">A bit mask which indicates which types of filtering are supported by the server.</param>
        public void QueryAvailableFilters(out int pdwFilterMask)
        {
            try
            {
                pdwFilterMask = OpcRcw.Ae.Constants.FILTER_BY_EVENT |
                                OpcRcw.Ae.Constants.FILTER_BY_CATEGORY |
                                OpcRcw.Ae.Constants.FILTER_BY_SEVERITY |
                                OpcRcw.Ae.Constants.FILTER_BY_AREA |
                                OpcRcw.Ae.Constants.FILTER_BY_SOURCE;
            }

            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in QueryAvailableFilters");
                throw ComUtils.CreateComException(e);
            }
        }

        /// <summary>
        /// Returns the specific categories of events supported by the server.
        /// </summary>
        /// <param name="dwEventType">Bit mask specifying which event types are of interest.</param>
        /// <param name="pdwCount">The number of event categories returned.  (This is the size of the EventCategoryID and EventCategoryDesc arrays.)</param>
        /// <param name="ppdwEventCategories">Array of DWORD codes for the vendor specific event categories implemented by the server.</param>
        /// <param name="ppszEventCategoryDescs">Array of strings for the text names or descriptions for each of the event category IDs.</param>
        public void QueryEventCategories(int dwEventType, out int pdwCount, out IntPtr ppdwEventCategories, out IntPtr ppszEventCategoryDescs)
        {
            lock (m_lock)
            {
                pdwCount = 0;
                ppdwEventCategories = IntPtr.Zero;
                ppszEventCategoryDescs = IntPtr.Zero;
                List<EventCategory> Cats = new List<EventCategory>();

                try
                {
                    if (dwEventType == 0 || dwEventType > OpcRcw.Ae.Constants.ALL_EVENTS)
                    {
                        throw ComUtils.CreateComException("QueryEventCategories", ResultIds.E_INVALIDARG);
                    }

                    foreach (EventCategory cat in m_configFile.Categories)
                    {
                        if ((System.Convert.ToBoolean(dwEventType & OpcRcw.Ae.Constants.SIMPLE_EVENT) && (cat.EventType == OpcRcw.Ae.Constants.SIMPLE_EVENT)) ||
                            (System.Convert.ToBoolean(dwEventType & OpcRcw.Ae.Constants.CONDITION_EVENT) && (cat.EventType == OpcRcw.Ae.Constants.CONDITION_EVENT)) ||
                            (System.Convert.ToBoolean(dwEventType & OpcRcw.Ae.Constants.TRACKING_EVENT) && (cat.EventType == OpcRcw.Ae.Constants.TRACKING_EVENT)))
                        {
                            Cats.Add(cat);
                        }
                    }

                    Cats.Sort(EventCategoryDescriptionCompare);
                    pdwCount = Cats.Count;
                    int[] CatIDs = new int[pdwCount];
                    string[] CatDescs = new string[pdwCount];

                    for (int i = 0; i < pdwCount; i++)
                    {
                        CatIDs[i] = Cats[i].CategoryID;
                        CatDescs[i] = Cats[i].EventDesc;
                    }

                    ppdwEventCategories = ComUtils.GetInt32s(CatIDs);
                    ppszEventCategoryDescs = ComUtils.GetUnicodeStrings(CatDescs);
                }
                catch (COMException e)
                {
                    throw ComUtils.CreateComException(e);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error in QueryEventCategories");
                    throw ComUtils.CreateComException(e);
                }

            }
        }

        /// <summary>
        /// Gives clients a means of finding out the specific condition names which the event server supports for the specified event category.
        /// </summary>
        /// <param name="dwEventCategory">A DWORD event category code, as returned by the QueryEventCategories method. Only the names of conditions within this event category are returned.</param>
        /// <param name="pdwCount">The number of condition names being returned.</param>
        /// <param name="ppszConditionNames">Array of strings containing the condition names for the specified event category.</param>
        public int QueryConditionNames(int dwEventCategory, out int pdwCount, out IntPtr ppszConditionNames)
        {
            pdwCount = 0;
            ppszConditionNames = IntPtr.Zero;
            return ResultIds.E_NOTIMPL;
        }

        /// <summary>
        /// Gives clients a means of finding out the specific sub-condition names which are associated with the specified condition name.
        /// </summary>
        /// <param name="szConditionName">A condition name, as returned by the QueryConditionNames method. Only the names of sub-conditions associated with this condition are returned.</param>
        /// <param name="pdwCount">The number of sub-condition names being returned.</param>
        /// <param name="ppszSubConditionNames">Array of strings containing the sub-condition names associated with the specified condition.</param>
        public void QuerySubConditionNames(string szConditionName, out int pdwCount, out IntPtr ppszSubConditionNames)
        {
            pdwCount = 0;
            ppszSubConditionNames = IntPtr.Zero;

            throw ComUtils.CreateComException("QuerySubConditionNames", ResultIds.E_NOTIMPL);
        }

        /// <summary>
        /// Gives clients a means of finding out the specific condition names associated with the specified source.
        /// </summary>
        /// <param name="szSource">A source name, as returned by the IOPCEventAreaBrower::GetQualifiedSourceName method. Only the names of conditions associated with this source are returned.</param>
        /// <param name="pdwCount">The number of condition names being returned.</param>
        /// <param name="ppszConditionNames">Array of strings containing the condition names for the specified source.</param>
        public void QuerySourceConditions(string szSource, out int pdwCount, out IntPtr ppszConditionNames)
        {
            if (szSource == string.Empty)
                throw ComUtils.CreateComException("QuerySourceConditions", ResultIds.E_INVALIDARG);

            BrowsePathTarget target = null;
            List<string> conditionNames = new List<string>();
            List<String> paths = new List<string>();

            paths.Add(szSource);
            BrowsePathResultCollection results = GetBrowseTargets(paths);

            if (StatusCode.IsBad(results[0].StatusCode))
                throw ComUtils.CreateComException("QuerySourceConditions", ResultIds.E_INVALIDARG);
            else
                target = results[0].Targets[0];

            ExpandedNodeId node = null;
            if (target == null)
                throw ComUtils.CreateComException("QuerySourceConditions", ResultIds.E_INVALIDARG);
            else
                node = target.TargetId;

            IList<INode> children = m_session.NodeCache.Find(node, ReferenceTypes.HasComponent, false, true);
            foreach (INode child in children)
            {
                if (m_session.TypeTree.IsTypeOf(child.TypeDefinitionId, Opc.Ua.ObjectTypes.ConditionType))
                {
                    conditionNames.Add(child.DisplayName.ToString());
                }

            }

            pdwCount = conditionNames.Count;
            ppszConditionNames = ComUtils.GetUnicodeStrings(conditionNames.ToArray());

        }

        /// <summary>
        /// Using the EventCategories returned by the QueryEventCategories method, client application can invoke the QueryEventAttributes method to get information about the vendor-specific attributes the server can provide as part of an event notification for an event within the specified event category. Simple servers may not support any vendor-specific attributes for some or even all EventCategories.
        /// </summary>
        /// <param name="dwEventCategory">One of the Event Category codes returned from the QueryEventCategories function.</param>
        /// <param name="pdwCount">The number of event attributes (size of the AttrID, and AttrDescs, and AttrTypes arrays) returned by the function.</param>
        /// <param name="ppdwAttrIDs">Array of DWORD codes for vendor-specific event attributes associated with the event category and available from the server.</param>
        /// <param name="ppszAttrDescs">Array of strings for the text names or descriptions for each of the event attribute IDs. This array corresponds to the AttrIDs array.</param>
        /// <param name="ppvtAttrTypes">Array of VARTYPES identifying the data type of each of the event attributes. This array corresponds to the AttrIDs array.</param>
        public void QueryEventAttributes(int dwEventCategory, out int pdwCount, out IntPtr ppdwAttrIDs, out IntPtr ppszAttrDescs, out IntPtr ppvtAttrTypes)
        {
            pdwCount = 0;
            ppdwAttrIDs = IntPtr.Zero;
            ppszAttrDescs = IntPtr.Zero;
            ppvtAttrTypes = IntPtr.Zero;

            try
            {
                // Make sure we are passed a valid dwEventCategory
                NodeId catNodeId = FindEventCatNodeId(dwEventCategory);
                if (catNodeId == null)
                    throw ComUtils.CreateComException("QueryEventAttributes", ResultIds.E_INVALIDARG);
                List<EventAttribute> Attrs = GetEventAttributes(dwEventCategory);
                Attrs.Sort(EventAttributeDescCompare);
                pdwCount = Attrs.Count;
                int[] AttrIDs = new int[pdwCount];
                string[] AttrDescs = new string[pdwCount];
                short[] AttrTypes = new short[pdwCount];

                for (int i = 0; i < pdwCount; i++)
                {
                    AttrIDs[i] = Attrs[i].AttributeID;
                    AttrDescs[i] = Attrs[i].AttrDesc;
                    AttrTypes[i] = (short)ComUtils.GetVarType(Attrs[i].ActualDataType);
                    if ((Attrs[i].ActualDataType != null) & (short)AttrTypes[i] == 0)
                    {
                        // any UA types that do not have a corresponding COM type will be set to string 
                        AttrTypes[i] = (short)VarEnum.VT_BSTR;
                    }

                    if (Attrs[i].IsArray)
                        AttrTypes[i] = (short)((short)VarEnum.VT_ARRAY | AttrTypes[i]);
                }
                ppdwAttrIDs = ComUtils.GetInt32s(AttrIDs);
                ppszAttrDescs = ComUtils.GetUnicodeStrings(AttrDescs);
                ppvtAttrTypes = ComUtils.GetInt16s(AttrTypes);

            }
            catch (COMException e)
            {
                throw ComUtils.CreateComException(e);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in QueryEventAttributes");
                throw ComUtils.CreateComException(e);
            }
        }


        /// <summary>
        /// Given an event source, and an array of associated attribute ID codes, return an array of the item ID strings corresponding to each attribute ID.
        /// </summary>
        /// <param name="szSource">An event source for which to return the item IDs corresponding to each of an array of attribute IDs if they exist.</param>
        /// <param name="dwEventCategory">DWORD event category code indicating the category of events for which item IDs are to be returned.</param>
        /// <param name="szConditionName">The name of a condition within the event category for which item IDs are to be returned.</param>
        /// <param name="szSubconditionName">The name of a sub-condition within a multi-state condition. This should be a NULL string for a single state condition.</param>
        /// <param name="dwCount">The number of event attribute IDs (size of the AssocAttrIDs array) passed into the function.</param>
        /// <param name="pdwAssocAttrIDs">Array of DWORD IDs of vendor-specific event attributes associated with the generator ID and available from the server for which to return ItemIDs.</param>
        /// <param name="ppszAttrItemIDs">Array of item ID strings corresponding to each event attribute ID associated with the generator ID. This array is the same length as the AssocAttrIDs array passed into the function. A Null string is returned if no item ID is available for this attribute.</param>
        /// <param name="ppszNodeNames">Array of network node names of the associated OPC Data Access Servers. A Null string is returned if the OPC Data Access Server is running on the local node.</param>
        /// <param name="ppCLSIDs">Array of class IDs for the associated OPC Data Access Servers.</param>
        public void TranslateToItemIDs(string szSource, int dwEventCategory, string szConditionName, string szSubconditionName, int dwCount, int[] pdwAssocAttrIDs, out IntPtr ppszAttrItemIDs, out IntPtr ppszNodeNames, out IntPtr ppCLSIDs)
        {
            ppszAttrItemIDs = IntPtr.Zero;
            ppszNodeNames = IntPtr.Zero;
            ppCLSIDs = IntPtr.Zero;

            throw ComUtils.CreateComException("TranslateToItemIDs", ResultIds.E_NOTIMPL);

        }

        /// <summary>
        /// Returns the current state information for the condition instance corresponding to the szSource and szConditionName.
        /// </summary>
        /// <param name="szSource">A source name, as returned by the IOPCEventAreaBrower::GetQualifiedSourceName method. The state of the condition instance associated with this source is returned.</param>
        /// <param name="szConditionName">A condition name, as returned by the QueryConditionNames method. The state of this condition is returned.</param>
        /// <param name="dwNumEventAttrs">The requested number of event attributes to be returned in the OPCCONDITIONSTATE structure. Can be zero if no attributes are desired</param>
        /// <param name="pdwAttributeIDs">The array of Attribute IDs indicating which event attributes should be returned in the OPCCONDITIONSTATE structure.</param>
        /// <param name="ppConditionState">Pointer to where the OPCCONDITIONSTATE structure pointer should be returned. The server allocates the structure.</param>
        public void GetConditionState(string szSource, string szConditionName, int dwNumEventAttrs, int[] pdwAttributeIDs, out IntPtr ppConditionState)
        {
            ppConditionState = IntPtr.Zero;

            throw ComUtils.CreateComException("GetConditionState", ResultIds.E_NOTIMPL);
        }

        /// <summary>
        /// Places the specified process areas into the enabled state. Therefore, the server will now generate condition-related events for these conditions as long as the source itself is enabled and no containing area in its hierarchy is disabled.
        /// </summary>
        /// <param name="dwNumAreas">The number of process areas for which conditions are to be enabled.</param>
        /// <param name="pszAreas">An array of area names, as returned by IOPCEventAreaBrowser::GetQualifiedAreaName.</param>
        public void EnableConditionByArea(int dwNumAreas, string[] pszAreas)
        {
            throw ComUtils.CreateComException("EnableConditionByArea", ResultIds.E_NOTIMPL);
        }

        /// <summary>
        /// Places all conditions for the specified event sources into the enabled state. Therefore, the server will now generate condition-related events for these conditions.
        /// </summary>
        /// <param name="dwNumSources">The number of event sources for which conditions are to be enabled.</param>
        /// <param name="pszSources">An array of source names, as returned by IOPCEventAreaBrowser::GetQualifiedSourceName</param>
        public void EnableConditionBySource(int dwNumSources, string[] pszSources)
        {
            throw ComUtils.CreateComException("EnableConditionBySource", ResultIds.E_NOTIMPL);
        }

        /// <summary>
        /// Places the specified process areas into the disabled state. Therefore, the server will now cease generating condition-related events for these conditions.
        /// </summary>
        /// <param name="dwNumAreas">The number of process areas for which conditions are to be disabled.</param>
        /// <param name="pszAreas">An array of area names, as returned by IOPCEventAreaBrowser::GetQualifiedAreaName</param>
        public void DisableConditionByArea(int dwNumAreas, string[] pszAreas)
        {
            throw ComUtils.CreateComException("DisableConditionByArea", ResultIds.E_NOTIMPL);
        }

        /// <summary>
        /// Places all conditions for the specified event sources into the disabled state. Therefore, the server will no longer generate condition-related events for these conditions.
        /// </summary>
        /// <param name="dwNumSources">The number of event sources for which conditions are to be disabled.</param>
        /// <param name="pszSources">An array of source names, as returned by IOPCEventAreaBrowser::GetQualifiedSourceName</param>
        public void DisableConditionBySource(int dwNumSources, string[] pszSources)
        {
            throw ComUtils.CreateComException("DisableConditionBySource", ResultIds.E_NOTIMPL);
        }

        /// <summary>
        /// Used to acknowledge one or more conditions in the Event Server.
        /// </summary>
        /// <param name="dwCount">The number of acknowledgments passed with this function.</param>
        /// <param name="szAcknowledgerID">A string passed in by the client, identifying who is acknowledging the conditions.</param>
        /// <param name="szComment">Comment string passed in by the client associated with acknowledging the conditions. A NULL string indicating no comment is allowed.</param>
        /// <param name="pszSource">Array of event source strings identifying the source (or owner) of each condition that is being acknowledged.</param>
        /// <param name="pszConditionName">Array of Condition Name strings identifying each condition that is being acknowledged. Condition Names are unique within the scope of the event server.</param>
        /// <param name="pftActiveTime">Array of active times corresponding to each Source and ConditionName pair.</param>
        /// <param name="pdwCookie">Array of server supplied �cookies� corresponding to each Source and Condition Name pair, that in addition to the Active Time, uniquely identifies a specific event notification.</param>
        /// <param name="ppErrors">Array of HRESULTS indicating the success of the individual acknowledgments.</param>
        public void AckCondition(int dwCount, string szAcknowledgerID, string szComment, string[] pszSource, string[] pszConditionName, System.Runtime.InteropServices.ComTypes.FILETIME[] pftActiveTime, int[] pdwCookie, out IntPtr ppErrors)
        {
            if ((dwCount <= 0) || (szAcknowledgerID == string.Empty))
                throw ComUtils.CreateComException("AckCondition", ResultIds.E_INVALIDARG);

            ppErrors = IntPtr.Zero;
            int[] errors = new int[dwCount];

            try
            {
                // Utils.Trace("AckCondition (count: {0}) => BEGIN", dwCount);
                for (int i = 0; i < dwCount; i++)
                {
                    // Utils.Trace("\tCall AckCondition ... (AcknowledgerID:{0}, Source: {1}, condition: {2}, ActiveTime: {3}, Cookie: {4})",
                    //    szAcknowledgerID, pszSource[i], pszConditionName[i], GetDateTime(pftActiveTime[i]), pdwCookie[i]);
                    
                    errors[i] = AckCondition(szAcknowledgerID, szComment, pszSource[i], pszConditionName[i], pftActiveTime[i], pdwCookie[i]);
                    
                    // Utils.Trace("\tAckCondition return: {0}", errors[i]);
                }

                ppErrors = ComUtils.GetInt32s(errors);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in AckCondition");
                throw ComUtils.CreateComException(e);
            }

           //  Utils.Trace("AckCondition (count: {0}) => END", dwCount);
        }

        /// <summary>
        /// Create an OPCEventAreaBrowser object on behalf of this client and return the interface to the Client.
        /// </summary>
        /// <param name="riid">The type of interface desired (e.g. IID_IOPCEventAreaBrowser)</param>
        /// <param name="ppUnk">Where to store the returned interface pointer.</param>
        public void CreateAreaBrowser(ref Guid riid, out object ppUnk)
        {
            Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");

            ppUnk = IntPtr.Zero;

            try
            {
                AreaBrowser brower = new AreaBrowser(this, m_session);
                if (brower == null)
                    throw ComUtils.CreateComException("CreateAreaBrowser", ResultIds.E_OUTOFMEMORY);

                ppUnk = brower;
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in CreateAreaBrowser");
                throw ComUtils.CreateComException(e);
            }
        }

        #endregion

        #region IOPCEventServer2 Members

        /// <summary>
        /// Places the specified process areas into the enabled state. Therefore, the server will now generate condition-related events for the sources in these areas as long as the source itself is enabled and no containing area in its hierarchy is disabled.
        /// </summary>
        /// <param name="dwNumAreas">The number of process areas for which conditions are to be enabled.</param>
        /// <param name="pszAreas">An array of area names, as returned by IOPCEventAreaBrowser::GetQualifiedAreaName.</param>
        /// <param name="ppErrors">Array of HRESULTS indicating the success of placing all conditions for all sources within a specified process area into the enabled state. The errors correspond to the areas passed into the method.</param>
        public void EnableConditionByArea2(int dwNumAreas, string[] pszAreas, out IntPtr ppErrors)
        {
            ppErrors = IntPtr.Zero;

            throw ComUtils.CreateComException("EnableConditionByArea2", ResultIds.E_NOTIMPL);
        }

        /// <summary>
        /// Places all conditions for the specified event sources into the enabled state. Therefore, the server will now generate condition-related events for these sources.
        /// </summary>
        /// <param name="dwNumSources">The number of event sources for which conditions are to be enabled.</param>
        /// <param name="pszSources">An array of source names, as returned by IOPCEventAreaBrowser::GetQualifiedSourceName</param>
        /// <param name="ppErrors">Array of HRESULTS indicating the success of placing all conditions for the specified event source into the enabled state. The errors correspond to the sources passed into the method.</param>
        public void EnableConditionBySource2(int dwNumSources, string[] pszSources, out IntPtr ppErrors)
        {
            ppErrors = IntPtr.Zero;

            throw ComUtils.CreateComException("EnableConditionBySource2", ResultIds.E_NOTIMPL);
        }

        /// <summary>
        /// Places the specified process areas into the disabled state. Therefore, the server will now cease generating condition-related events for these conditions.
        /// </summary>
        /// <param name="dwNumAreas">The number of process areas for which conditions are to be disabled.</param>
        /// <param name="pszAreas">An array of area names, as returned by IOPCEventAreaBrowser::GetQualifiedAreaName</param>
        /// <param name="ppErrors">Array of HRESULTS indicating the success of placing all conditions for all sources within a specified process area into the disabled state. The errors correspond to the areas passed into the method.</param>
        public void DisableConditionByArea2(int dwNumAreas, string[] pszAreas, out IntPtr ppErrors)
        {
            ppErrors = IntPtr.Zero;

            throw ComUtils.CreateComException("DisableConditionByArea2", ResultIds.E_NOTIMPL);
        }

        /// <summary>
        /// Places all conditions for the specified event sources into the disabled state. Therefore, the server will no longer generate condition-related events for these sources.
        /// </summary>
        /// <param name="dwNumSources">The number of event sources for which conditions are to be disabled.</param>
        /// <param name="pszSources">An array of source names, as returned by IOPCEventAreaBrowser::GetQualifiedSourceName</param>
        /// <param name="ppErrors">Array of HRESULTS indicating the success of placing all conditions for the specified event source into the disabled state. The errors correspond to the sources passed into the method.</param>
        public void DisableConditionBySource2(int dwNumSources, string[] pszSources, out IntPtr ppErrors)
        {
            ppErrors = IntPtr.Zero;

            throw ComUtils.CreateComException("DisableConditionBySource2", ResultIds.E_NOTIMPL);
        }

        /// <summary>
        /// Returns the current enable state and the effective enable state for each area specified in pszAreas.
        /// </summary>
        /// <param name="dwNumAreas">The number of areas for which the enable state is to be queried.</param>
        /// <param name="pszAreas">An array of area names, as returned by IOPCEventAreaBrowser::GetQualifiedAreaName</param>
        /// <param name="pbEnabled">Array of BOOL indicating the current enable state of the corresponding area. TRUE if the area is enabled, FALSE if it is disabled.</param>
        /// <param name="pbEffectivelyEnabled">Array of BOOL indicating the effective enable state of the corresponding area. TRUE if the area is enabled and all areas within the hierarchy of its containing areas are enabled. FALSE if the area is disabled or any area within the hierarchy of its containing areas is disabled.</param>
        /// <param name="ppErrors">Array of HRESULTS indicating the success of retrieving the enable state of the area. The errors correspond to the areas passed into the method.</param>
        public void GetEnableStateByArea(int dwNumAreas, string[] pszAreas, out IntPtr pbEnabled, out IntPtr pbEffectivelyEnabled, out IntPtr ppErrors)
        {
            pbEnabled = IntPtr.Zero;
            pbEffectivelyEnabled = IntPtr.Zero;
            ppErrors = IntPtr.Zero;

            throw ComUtils.CreateComException("GetEnableStateByArea", ResultIds.E_NOTIMPL);
        }

        /// <summary>
        /// Returns the current enable state and the effective enable state for each source specified in pszSources.
        /// </summary>
        /// <param name="dwNumSources">The number of event sources for which the enable state is to be queried.</param>
        /// <param name="pszSources">An array of source names, as returned by IOPCEventAreaBrowser::GetQualifiedSourceName</param>
        /// <param name="pbEnabled">Array of BOOL indicating the current enable state of the corresponding source. TRUE if the source is enabled, FALSE if it is disabled.</param>
        /// <param name="pbEffectivelyEnabled">Array of BOOL indicating the effective enable state of the corresponding source. TRUE if the source is enabled and all areas within the hierarchy of its containing areas are enabled. FALSE if the source is disabled or any area within the hierarchy of its containing areas is disabled.</param>
        /// <param name="ppErrors">Array of HRESULTS indicating the success of retrieving the enable state of the source. The errors correspond to the sources passed into the method.</param>
        public void GetEnableStateBySource(int dwNumSources, string[] pszSources, out IntPtr pbEnabled, out IntPtr pbEffectivelyEnabled, out IntPtr ppErrors)
        {
            pbEnabled = IntPtr.Zero;
            pbEffectivelyEnabled = IntPtr.Zero;
            ppErrors = IntPtr.Zero;

            throw ComUtils.CreateComException("GetEnableStateBySource", ResultIds.E_NOTIMPL);
        }

        #endregion

        #region Query Support Members
        /// <summary>
        /// Returns the list of attribute data of the event category specified
        /// </summary>
        /// <param name="dwEventCategory"></param>
        public List<EventAttribute> GetEventAttributes(int dwEventCategory)
        {
            List<EventAttribute> Attrs = new List<EventAttribute>();
            try
            {
                Boolean BaseObjectTypeProcessed = false;
                Browser browseEventTypes = new Browser(m_session);
                browseEventTypes.BrowseDirection = BrowseDirection.Inverse;

                Browser browseVars = new Browser(m_session);
                browseVars.NodeClassMask = (int)NodeClass.Variable;
                browseVars.ReferenceTypeId = Opc.Ua.ReferenceTypes.HasProperty;

                ReferenceDescriptionCollection references = null;
                NodeId typeNodeId = FindEventCatNodeId(dwEventCategory);

                while (!BaseObjectTypeProcessed)
                {
                    references = browseVars.Browse(typeNodeId);
                    foreach (ReferenceDescription reference in references)
                    {
                        EventAttribute attribute = FindEventAttrInfo(reference.NodeId.ToString());
                        if (attribute != null)
                            Attrs.Add(attribute);
                    }

                    if (typeNodeId.Equals(Opc.Ua.ObjectTypes.BaseObjectType))
                    {
                        BaseObjectTypeProcessed = true;
                        break;
                    }

                    if (typeNodeId.Equals(Opc.Ua.ObjectTypes.BaseEventType))
                    {
                        BaseObjectTypeProcessed = true;
                        Attrs.Add(FindEventAttrInfo("Areas"));
                    }
                    else
                    {
                        if (typeNodeId.Equals(Opc.Ua.ObjectTypes.ConditionType))
                            Attrs.Add(FindEventAttrInfo("AckComment"));

                        references = browseEventTypes.Browse(typeNodeId);
                        foreach (ReferenceDescription refDesc in references)
                        {
                            if (refDesc.ReferenceTypeId == ReferenceTypes.HasSubtype)
                            {
                                typeNodeId = (NodeId)refDesc.NodeId; //TODO: Can a type have only one parent?
                                break;
                            }
                        }
                    }

                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in GetEventAttributes");
            }
            return Attrs;
        }

        /// <summary>
        /// Returns the event attribute data based off the attribute node ID
        /// </summary>
        /// <param name="AttrNodeId">String version of the Attribute NodeId being searched for</param>
        private EventAttribute FindEventAttrInfo(string AttrNodeId)
        {
            try
            {
                //The Attributes array is sorted off the strNodeId so we can do a binary search.
                int first = 0;
                int last = m_configFile.Attributes.Length - 1;
                int mid;
                while (first <= last)
                {
                    mid = (first + last) / 2;
                    int compareValue = m_configFile.Attributes[mid].strNodeId.CompareTo(AttrNodeId);
                    if (compareValue == 0)
                        return m_configFile.Attributes[mid];
                    else if (compareValue > 0)
                        last = mid - 1;
                    else
                        first = mid + 1;
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in FindEventAttrInfo");
            }
            return null;
        }

        /// <summary>
        /// Returns the event category NodeId based off the event category ID
        /// </summary>
        /// <param name="EventCategoryID">The event category ID</param>
        public NodeId FindEventCatNodeId(int EventCategoryID)
        {
            NodeId nodeId = null;
            try
            {
                foreach (EventCategory cat in m_configFile.Categories)
                    if (cat.CategoryID == EventCategoryID)
                    {
                        nodeId = new NodeId(cat.strNodeId);
                        break;
                    }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in FindEventCatNodeId");
            }
            return nodeId;
        }

        /// <summary>
        /// Compare method based off the Attribute Description
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        private static int EventAttributeDescCompare(EventAttribute x, EventAttribute y)
        {
            int c = x.AttrDesc.ToLower().CompareTo(y.AttrDesc.ToLower());
            if (c != 0)
                return c;
            else
            {
                if ((x.ActualDataType != null) && (y.ActualDataType != null))
                    return x.ActualDataType.Name.CompareTo(y.ActualDataType.Name);
                else
                    return 0;
            }
        }

        /// <summary>
        /// Compare method based off the BrowseName
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        private static int EventCategoryDescriptionCompare(EventCategory x, EventCategory y)
        //TODO: should this be a case insensitive compare?
        {
            return x.EventDesc.ToLower().CompareTo(y.EventDesc.ToLower());
        }
        #endregion

        #region Notification Processing Members
        /// <summary>
        /// Processes a Publish response from the UA server.
        /// </summary>
        /// <param name="monitoredItem"></param>
        /// <param name="e"></param>
        void MonitoredItem_Notification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            try
            {
                EventFieldList eventFields = e.NotificationValue as EventFieldList;

                if (eventFields == null)
                {
                    return;
                }
                if (monitoredItem != null)
                {
                    if (monitoredItem.ClientHandle != eventFields.ClientHandle)
                    {
                        return;
                    }
                }
                INode eventUA = monitoredItem.GetEventType(eventFields);
                EventCategory cat = FindEventCatInfo(eventUA.BrowseName.ToString());

                if (cat == null) return; // The event is not of a category that we recognize.
           
                if (cat.EventType == OpcRcw.Ae.Constants.CONDITION_EVENT)
                {
                    NodeId branchId = monitoredItem.GetFieldValue(eventFields, Opc.Ua.ObjectTypes.ConditionType, Opc.Ua.BrowseNames.BranchId) as NodeId;
                    if (!NodeId.IsNull(branchId)) return; // We don't support condition branches in the COM Proxy
                }

                EventNotification ev = new EventNotification();

                ev.EventId = monitoredItem.GetFieldValue(eventFields, Opc.Ua.ObjectTypes.BaseEventType, new QualifiedName(Opc.Ua.BrowseNames.EventId)) as byte[];
                ev.SourceID = System.Convert.ToString(monitoredItem.GetFieldValue(eventFields, Opc.Ua.ObjectTypes.BaseEventType, new QualifiedName(Opc.Ua.BrowseNames.SourceName)));
                ev.Time = System.Convert.ToDateTime(monitoredItem.GetFieldValue(eventFields, Opc.Ua.ObjectTypes.BaseEventType, new QualifiedName(Opc.Ua.BrowseNames.Time)));
                ev.Message = System.Convert.ToString(monitoredItem.GetFieldValue(eventFields, Opc.Ua.ObjectTypes.BaseEventType, new QualifiedName(Opc.Ua.BrowseNames.Message)));
                ev.EventType = cat.EventType;
                ev.EventCategory = cat.CategoryID;
                ev.Severity = System.Convert.ToInt32(monitoredItem.GetFieldValue(eventFields, Opc.Ua.ObjectTypes.BaseEventType, new QualifiedName(Opc.Ua.BrowseNames.Severity)));

                List<EventAttribute> Attrs = GetEventAttributes(cat.CategoryID);
                UniqueList<string> strEventNodeIds = new UniqueList<string>();
                foreach (EventAttribute attr in Attrs)
                    if (attr.strEventNodeId != "")
                        strEventNodeIds.AddUnique(attr.strEventNodeId);

                ev.EventAttributes = new Dictionary<int, object>();
                foreach (EventAttribute attr in m_configFile.Attributes)
                {
                    foreach (string strEventNodeId in strEventNodeIds)
                    {
                        if (attr.strEventNodeId == strEventNodeId)
                        {
                            object value = monitoredItem.GetFieldValue(eventFields, (NodeId)attr.strEventNodeId, new QualifiedName(attr.BrowseName, attr.BrowseNameNSIndex));
                            if (value == null)
                            {
                                ev.EventAttributes.Add(attr.AttributeID, "");
                            }
                            else if ((value.GetType() != null) & (short)ComUtils.GetVarType(value) != 0)
                            {
                                ev.EventAttributes.Add(attr.AttributeID, value);
                            }
                            else
                            {
                                // any value with a UA type that does not have a corresponding COM type will be returned as a string 
                                ev.EventAttributes.Add(attr.AttributeID, value.ToString());
                            }
                        }
                    }
                }

                //Condition-Related Event properties
                ev.ConditionName = "";
                ev.SubConditionName = "";
                ev.ChangeMask = 0;
                ev.NewState = OpcRcw.Ae.Constants.CONDITION_ENABLED | OpcRcw.Ae.Constants.CONDITION_ACKED;
                ev.Quality = OpcRcw.Da.Qualities.OPC_QUALITY_GOOD;
                ev.AckRequired = false;
                ev.ActiveTime = DateTime.Now;
                ev.Cookie = 0;

                if (ev.EventType == OpcRcw.Ae.Constants.CONDITION_EVENT)
                    SetConditionEventFields(monitoredItem, eventFields, ev, cat);

                //Tracking Events and for Condition-Related Events which are acknowledgment notifications
                if (cat.EventType == OpcRcw.Ae.Constants.TRACKING_EVENT)
                    ev.ActorID = System.Convert.ToString(monitoredItem.GetFieldValue(eventFields, (NodeId)eventUA.NodeId, new QualifiedName(Opc.Ua.BrowseNames.ClientUserId)));

                IncomingEventHandler eventHandler = new IncomingEventHandler();
                
                //extract the area associated with this event.
                AreaNode areaNode;
                string[] areas = null;
                if (m_notifiers.TryGetValue(monitoredItem.ClientHandle, out areaNode))
                {
                    areas = new string[] { areaNode.AreaName };
                }
                eventHandler.ProcessEventNotificationList(ev, areas);
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "Unexpected error in MonitoredItem_Notification");
            }
        }

        /// <summary>
        /// Additional new event processing when the received event maps to a (COM AE) condition event type.  We need to extract
        /// the condition name, subcondition name, changeMask, newState, Quality, AckRequired, ActiveTime and cookie.
        /// </summary>
        /// <param name="monitoredItem"></param>
        /// <param name="eventFields"></param>
        /// <param name="ev"></param>
        /// <param name="cat"></param>
        void SetConditionEventFields(MonitoredItem monitoredItem, EventFieldList eventFields, EventNotification ev, EventCategory cat)
        {
            LocalizedText localText;
            String ConditionName;
            StatusCode? Status;
            DateTime? TimeOfLastTransition;

            try
            {
                NodeId eventType = monitoredItem.GetFieldValue(eventFields, Opc.Ua.ObjectTypes.BaseEventType, Opc.Ua.BrowseNames.EventType) as NodeId;

                // UA events are categorized into three subsets.  The first of these subsets consists of types and subtypes of ConditionType
                // which yields the event condition name, quality and enable/disable status.
                if (m_session.TypeTree.IsTypeOf(eventType, Opc.Ua.ObjectTypes.ConditionType))
                {
                    ConditionName = monitoredItem.GetFieldValue(eventFields, Opc.Ua.ObjectTypes.ConditionType, Opc.Ua.BrowseNames.ConditionName) as String;
                    if (ConditionName != null)
                        ev.ConditionName = ConditionName;
                    else
                        ev.ConditionName = cat.BrowseName;

                    // Set the subcondition name as conditionname for now.  If the event of of type AlarmconditionType and a subcondition (UA substate)
                    // exists then this field will be set accordingly.
                    ev.SubConditionName = ev.ConditionName;

                    bool? enabled = monitoredItem.GetFieldValue(eventFields, Opc.Ua.ObjectTypes.ConditionType,
                        "/EnabledState/Id", Attributes.Value) as bool?;

                    Status = monitoredItem.GetFieldValue(eventFields, Opc.Ua.ObjectTypes.ConditionType,
                        "/Quality", Attributes.Value) as StatusCode?;

                    ev.Quality = MapStatusToQuality(Status);

                    if (enabled == true)
                        ev.NewState |= OpcRcw.Ae.Constants.CONDITION_ENABLED;
                    else
                        ev.NewState &= ~OpcRcw.Ae.Constants.CONDITION_ENABLED;
                }

                // The second of the three UA event subsets consists of types and subtypes of AcknowledgeableconditionType.
                // This categorization yields events which support acknowledgement in addition to enable/disable state
                if (m_session.TypeTree.IsTypeOf(eventType, Opc.Ua.ObjectTypes.AcknowledgeableConditionType))
                {
                    bool? acked = monitoredItem.GetFieldValue(eventFields, Opc.Ua.ObjectTypes.AcknowledgeableConditionType,
                        "/AckedState/Id", Attributes.Value) as bool?;

                    // Extract the "ConditionId" (nodeId of the condition instance)
                    ev.ConditionId = monitoredItem.GetFieldValue(eventFields, Opc.Ua.ObjectTypes.AcknowledgeableConditionType,
                        "", Attributes.NodeId) as NodeId;

                    ev.AcknowledgeMethod = Opc.Ua.Methods.AcknowledgeableConditionType_Acknowledge;
                    
                    if (acked == true)
                    {
                        ev.NewState |= OpcRcw.Ae.Constants.CONDITION_ACKED;
                        ev.AckRequired = false;
                    }
                    else
                    {
                        ev.NewState &= ~OpcRcw.Ae.Constants.CONDITION_ACKED;
                        ev.AckRequired = true;
                    }

                }

                // the third of the three UA event subsets consists of types and subtypes of AlarmConditionType.  This
                // categorization yields events which support the notion of Active/Inactive and also may support substates
                // (subconditions).
                if (m_session.TypeTree.IsTypeOf(eventType, Opc.Ua.ObjectTypes.AlarmConditionType))
                {
                    bool? active = monitoredItem.GetFieldValue(eventFields, Opc.Ua.ObjectTypes.AlarmConditionType,
                        "/ActiveState/Id", Attributes.Value) as bool?;

                    TimeOfLastTransition = monitoredItem.GetFieldValue(eventFields, Opc.Ua.ObjectTypes.AlarmConditionType,
                        "/ActiveState/TransitionTime", Attributes.Value) as DateTime?;

                    if (active == true)
                    {
                        ev.NewState |= OpcRcw.Ae.Constants.CONDITION_ACTIVE;
                        ev.ActiveTime = TimeOfLastTransition ?? DateTime.MinValue;
                    }

                    // Active subconditon. 
                    localText = monitoredItem.GetFieldValue(eventFields, Opc.Ua.ObjectTypes.AlarmConditionType,
                        "/ActiveState/EffectiveDisplayName", Attributes.Value) as LocalizedText;
                    if (localText != null && localText.ToString() != "")
                        ev.SubConditionName = localText.ToString();
                }
                else // If this is not an AlarmConditionType (thus no UA active/inactive states apply) default to Active
                    ev.NewState |= OpcRcw.Ae.Constants.CONDITION_ACTIVE;
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in SetConditionEventFields");
            }
        }

        /// <summary>
        /// Maps status code returned from the server into corresponding OPC COM Quality.  Not all status codes have 
        /// one-for-one mapping to COM quality therefore as a default, just map qualities 'Bad', 'Uncertain' and 'Good'
        /// </summary>
        /// <param name="Status">Status code associated with the event notification</param>
        /// <returns>Closest match COM quality</returns>
        short MapStatusToQuality(StatusCode? Status)
        {
            short mappedQuality = OpcRcw.Da.Qualities.OPC_QUALITY_BAD;

            if (Status != null)
            {
                switch (((StatusCode)Status).Code)
                {
                    // Bad status codes
                    case StatusCodes.BadConfigurationError: mappedQuality = OpcRcw.Da.Qualities.OPC_QUALITY_CONFIG_ERROR; break;
                    case StatusCodes.BadNotConnected: mappedQuality = OpcRcw.Da.Qualities.OPC_QUALITY_NOT_CONNECTED; break;
                    case StatusCodes.BadDeviceFailure: mappedQuality = OpcRcw.Da.Qualities.OPC_QUALITY_DEVICE_FAILURE; break;
                    case StatusCodes.BadSensorFailure: mappedQuality = OpcRcw.Da.Qualities.OPC_QUALITY_SENSOR_FAILURE; break;
                    case StatusCodes.BadNoCommunication: mappedQuality = OpcRcw.Da.Qualities.OPC_QUALITY_COMM_FAILURE; break;
                    case StatusCodes.BadOutOfService: mappedQuality = OpcRcw.Da.Qualities.OPC_QUALITY_OUT_OF_SERVICE; break;

                    // Uncertain status codes
                    case StatusCodes.UncertainNoCommunicationLastUsableValue: mappedQuality = OpcRcw.Da.Qualities.OPC_QUALITY_LAST_USABLE; break;
                    case StatusCodes.UncertainLastUsableValue: mappedQuality = OpcRcw.Da.Qualities.OPC_QUALITY_LAST_USABLE; break;
                    case StatusCodes.UncertainSensorNotAccurate: mappedQuality = OpcRcw.Da.Qualities.OPC_QUALITY_SENSOR_CAL; break;
                    case StatusCodes.UncertainEngineeringUnitsExceeded: mappedQuality = OpcRcw.Da.Qualities.OPC_QUALITY_EGU_EXCEEDED; break;
                    case StatusCodes.UncertainSubNormal: mappedQuality = OpcRcw.Da.Qualities.OPC_QUALITY_SUB_NORMAL; break;

                    // Good status codes
                    case StatusCodes.GoodLocalOverride: mappedQuality = OpcRcw.Da.Qualities.OPC_QUALITY_LOCAL_OVERRIDE; break;

                    // Defaults
                    default:
                        if (StatusCode.IsBad((StatusCode)Status))
                            mappedQuality = OpcRcw.Da.Qualities.OPC_QUALITY_BAD;
                        else if (StatusCode.IsUncertain((StatusCode)Status))
                            mappedQuality = OpcRcw.Da.Qualities.OPC_QUALITY_UNCERTAIN;
                        else if (StatusCode.IsGood((StatusCode)Status))
                            mappedQuality = OpcRcw.Da.Qualities.OPC_QUALITY_GOOD;

                        break;
                }
            }

            return mappedQuality;

        }

        /// <summary>
        /// Somewhat simplistic logic to extract the containing area associated with a received event.  Just parse the
        /// source path and assume the final token is the unqualified source while the remainder of the string is the area
        /// path.
        /// </summary>
        /// <param name="ev"></param>
        /// <returns></returns>
        string[] FindEventAreas(EventNotification ev)
        {
            try
            {
                QualifiedNameCollection browseNames = SimpleAttributeOperand.Parse(ev.SourceID);
                String areaString = "/";

                if (browseNames.Count > 0)
                {
                    for (int i = 0; i < browseNames.Count - 1; i++)
                    {
                        areaString += browseNames[i];
                        areaString += "/";
                    }
                }
                return new string[] { areaString };
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in FindEventAreas");
                return null;
            }
        }

        /// <summary>
        /// Processes a Publish response from the UA server for AddressSpace changes.
        /// </summary>
        /// <param name="monitoredItem"></param>
        /// <param name="e"></param>
        void AddressSpaceChange_Notification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            try
            {
                EventFieldList eventFields = e.NotificationValue as EventFieldList;

                if (eventFields == null)
                {
                    return;
                }
                if (monitoredItem != null)
                {
                    if (monitoredItem.ClientHandle != eventFields.ClientHandle)
                    {
                        return;
                    }
                }
                INode eventUA = monitoredItem.GetEventType(eventFields);
                if (eventUA.NodeId == new NodeId(Opc.Ua.ObjectTypes.BaseModelChangeEventType))
                {
                    //TODO:if we get this event we know a change was made, but we do not know what so we will beed to get all EventTypes and compare and update our config data
                }
                else if (eventUA.NodeId != new NodeId(Opc.Ua.ObjectTypes.GeneralModelChangeEventType))
                {
                    //We are not interested in any other event, so we will return.
                    //If we can set the where clause on the filter for this item, this else clause can be removed.
                    return;
                }
                else
                {
                    object v = monitoredItem.GetFieldValue(eventFields, Opc.Ua.ObjectTypes.GeneralModelChangeEventType, new QualifiedName(Opc.Ua.BrowseNames.Changes));

                    //ChangeStructureDataTypeCollection changes = (ChangeStructureDataTypeCollection) monitoredItem.GetFieldValue(eventFields, ObjectTypes.GeneralModelChangeEventType, new QualifiedName(GeneralModelChangeEvent.Names.Changes));



                }

            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "Unexpected error in AddressSpaceChange_Notification");
            }
        }

        /// <summary>
        /// Returns the event category data based off the event category browse name
        /// </summary>
        /// <param name="EventName">The event category browse name.</param>
        public EventCategory FindEventCatInfo(string EventName)
        {
            try
            {
                //The Categories array is sorted off the BrowseName so we can do a binary search.
                int first = 0;
                int last = m_configFile.Categories.Length - 1;
                int mid;
                while (first <= last)
                {
                    mid = (first + last) / 2;
                    int compareValue = m_configFile.Categories[mid].BrowseName.CompareTo(EventName);
                    if (compareValue == 0)
                        return m_configFile.Categories[mid];
                    else if (compareValue > 0)
                        last = mid - 1;
                    else
                        first = mid + 1;
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in FindEventCatInfo");
            }
            return null;
        }

        /// <summary>
        /// Give each subscription the oppurtunity to examine the new event and
        /// notify clients if appropriate (e.g., filter)
        /// </summary>
        /// <param name="OEClass"></param>
        public void ProcessNewEvent(OnEventClass OEClass)
        {
            lock (m_lock)
            {
                foreach (Subscription s in m_EvSubMgtSet)
                {
                    s.ProcessNewEvent(OEClass);
                }
            }
        }
        #endregion
         
        #region Condition Acknowledgement Support members
        /// <summary>
        /// AckCondition
        /// </summary>
        /// <param name="szAcknowledgerID"></param>
        /// <param name="szComment"></param>
        /// <param name="szSource"></param>
        /// <param name="szConditionName"></param>
        /// <param name="ftActiveTime"></param>
        /// <param name="dwCookie"></param>
        /// <returns></returns>
        public int AckCondition(string szAcknowledgerID, string szComment, string szSource, string szConditionName, System.Runtime.InteropServices.ComTypes.FILETIME ftActiveTime, int dwCookie)
        {
            OPCCondition cond = FindCondition(szSource, szConditionName);
            if (cond == null)
                return ResultIds.E_INVALIDARG;

            if (cond.ActiveTime != GetDateTime(ftActiveTime))
                return ResultIds.E_INVALIDTIME;

            if (cond.Cookie != dwCookie)
                return ResultIds.E_INVALIDARG;

            if (cond.IsAckedOrWaiting(m_Subscription.CurrentPublishingInterval * 2))
                return ResultIds.S_ALREADYACKED;
            try
            {
                IList<object> args = m_session.Call(cond.ConditionId, cond.AcknowledgeMethod, cond.EventId, new LocalizedText(szComment));
            }

            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in AckCondition");
                return ResultIds.E_FAIL;
            }

            return ResultIds.S_OK;
        }

        /// <summary>
        /// Unmarshals a WIN32 FILETIME.
        /// </summary>
        public static DateTime GetDateTime(System.Runtime.InteropServices.ComTypes.FILETIME input)
        {
            System.Runtime.InteropServices.ComTypes.FILETIME output = new System.Runtime.InteropServices.ComTypes.FILETIME();

            output.dwLowDateTime = input.dwLowDateTime;
            output.dwHighDateTime = input.dwHighDateTime;

            return ComUtils.GetDateTime(output);
        }

        /// <summary>
        /// FindCondition
        /// </summary>
        /// <param name="szSource"></param>
        /// <param name="szCondition"></param>
        /// <returns></returns>
        OPCCondition FindCondition(string szSource, string szCondition)
        {
            SourceMap sourceMap = SourceMap.TheSourceMap;
            lock (sourceMap)
            {
                OPCCondition cond;
                ConditionMap conditionMap;
                if (sourceMap.TryGetValue(szSource, out conditionMap) == false)
                    return null;

                if (conditionMap.TryGetValue(szCondition, out cond) == false)
                    return null;
                else
                    return cond;
            }

        }
        #endregion

        #region Subscription Management Members
        /// <summary>
        /// Adds subscription reference to the list
        /// </summary>
        /// <param name="s"></param>
        public void SubscriptionListInsert(Subscription s)
        {
            m_EvSubMgtSet.Add(s);
        }

        /// <summary>
        /// Removes subscription reference from the list
        /// </summary>
        /// <param name="s"></param>
        public void SubscriptionListRemove(Subscription s)
        {
            m_EvSubMgtSet.Remove(s);
        }

        /// <summary>
        /// Time of last subscription callback for this server instance
        /// </summary>
        public DateTime LastUpdateTime
        {
            get { return m_lastUpdateTime; }
            set { m_lastUpdateTime = value; }
        }
        #endregion

        /// <summary>
        /// Accessor used when negotiating subscription keepalive time.  Client cannot request a keepalive
        /// time faster than that of the UA master subscription
        /// </summary>
        public int KeepAliveInterval
        {
            get { return m_KeepAliveInterval; }
        }

        /// <summary>
        /// Accessor for use by internal subscription to add/remove monitored items.  MasterSubscription 
        /// is the single UA subscription maintained by the proxy
        /// </summary>
        public Opc.Ua.Client.Subscription MasterSubscription
        {
            get { return m_Subscription; }
        }

        /// <summary>
        /// Accessor used to obtain the Server namespace mapping table (server namespace index to client namespace index)
        /// </summary>
        public List<int> ServerMappingTable
        {
            get { return m_serverMappingTable; }
        }

        /// <summary>
        /// Accessor used to obtain the Client namespace mapping table (client namespace index to server namespace index)
        /// Client indices which cannot be mapped to a server index (i.e., namespace is removed from server) map to -1
        /// </summary>
        public List<int> ClientMappingTable
        {
            get { return m_clientmappingTable; }
        }

        #region Private Fields
        private ComAeProxy2 m_proxy;
        private Guid m_clsid;
        private int m_KeepAliveInterval = 1000;
        private object m_lock = new object();
        private DateTime m_startTime = DateTime.MinValue;
        private DateTime m_lastUpdateTime = DateTime.MinValue;
        private static object m_staticLock = new object();
        private static ApplicationConfiguration m_configuration;
        private static ConfiguredEndpointCollection m_endpointCache;
        private static Dictionary<Guid, ConfiguredEndpoint> m_verifiedEndpoints;
        private Session m_session;
        private string m_clientName;
        private List<int> m_localeIds = null;
        private int m_lcid = ComUtils.LOCALE_SYSTEM_DEFAULT;
        private List<Subscription> m_EvSubMgtSet = new List<Subscription>();
        private Opc.Ua.Client.Subscription m_Subscription = null;
        private List<int> m_serverMappingTable = new List<int>();
        private List<int> m_clientmappingTable = new List<int>();

        private Configuration m_configFile = new Configuration();
        private List<EventCategory> m_EventCategories = new List<EventCategory>();
        private List<EventAttribute> m_EventAttributes = new List<EventAttribute>();


        private int m_catID = 1;
        private int m_attrID = 2;   //starts with 2 as 0 and 1 are used for the standard attributes 'areas' and 'ackcomment'
        private const string AreasAttributeName = "Areas";
        private const string AckCommentAttributeName = "AckComment";

        /// <summary>
        /// The DefaultServerUrl 
        /// </summary>
        private const string DefaultServerUrl = "http://localhost:51211/UA/SampleServer";

        //Dictionary of notifier nodes
        private Dictionary<uint, AreaNode> m_notifiers = new Dictionary<uint, AreaNode>();
        private Dictionary<string, AreaNode> m_AreaNodes = new Dictionary<string, AreaNode>();
        #endregion
    }

    #region class to store information about COM areas
    class AreaNode
    {
        public AreaNode()
        {
            m_name = "";
            m_count = 0;
            m_monitoredItem = null;
        }
        public string AreaName
        {
            get { return m_name; }
            set { m_name = value; }
        }

        public int RefCount
        {
            get { return m_count; }
            set { m_count = value; }
        }

        public MonitoredItem MonitoredItem
        {
            get { return m_monitoredItem; }
            set { m_monitoredItem = value; }
        }

        private string m_name; // Fully qualified area name
        private int m_count;   // Count of subscriptions subscribing to this area
        private MonitoredItem m_monitoredItem; // monitored item that represents the area node
    };
    #endregion

    #region Configuration File Classes

    /// <summary>
    /// Configuration data for the event categories and attributess 
    /// </summary>
    public class Configuration
    {
        /// <summary>
        /// SubscriptionSettings accessor
        /// </summary>
        public SubscriptionSettings ProxySubscriptionSettings
        {
            get { return m_proxySubscriptionSettings; }
            set { m_proxySubscriptionSettings = value; }
        }

        /// <summary>
        /// SavedNamespaceTable accessor
        /// </summary>
        public string[] SavedNamespaceTable
        {
            get { return m_savedNamespaceTable; }
            set { m_savedNamespaceTable = value; }
        }

        /// <summary>
        /// Categories accessor
        /// </summary>
        public EventCategory[] Categories
        {
            get { return m_Categories; }
            set { m_Categories = value; }
        }

        /// <summary>
        /// Attributes accessor
        /// </summary>
        public EventAttribute[] Attributes
        {
            get { return m_Attributes; }
            set { m_Attributes = value; }
        }

        /// <summary>
        /// LastEventCategoryID accessor
        /// </summary>
        public int LastEventCategoryID
        {
            get { return m_LastEventCategoryID; }
            set { m_LastEventCategoryID = value; }
        }

        /// <summary>
        /// LastEventAttributeID accessor
        /// </summary>
        public int LastEventAttributeID
        {
            get { return m_LastEventAttributeID; }
            set { m_LastEventAttributeID = value; }
        }

        #region Private Fields
        private string[] m_savedNamespaceTable;
        private EventCategory[] m_Categories;
        private EventAttribute[] m_Attributes;
        private int m_LastEventCategoryID;
        private int m_LastEventAttributeID;
        private SubscriptionSettings m_proxySubscriptionSettings = new SubscriptionSettings();
        #endregion

    }

    /// <summary>
    /// UA Subscription configuration settings.  
    /// </summary>
    public class SubscriptionSettings
    {
        /// <summary>
        /// MaxNotificationsPerPublish accessor
        /// </summary>
        public uint MaxNotificationsPerPublish
        {
            get { return m_maxNotificationsPerPublish; }
            set { m_maxNotificationsPerPublish = value; }
        }

        /// <summary>
        /// KeepAliveCount accessor
        /// </summary>
        public uint KeepAliveCount
        {
            get { return m_keepAliveCount; }
            set { m_keepAliveCount = value; }
        }

        /// <summary>
        /// LifetimeCount accessor
        /// </summary>
        public uint LifetimeCount
        {
            get { return m_lifetimeCount; }
            set { m_lifetimeCount = value; }
        }

        /// <summary>
        /// PublishingInterval accessor
        /// </summary>
        public int PublishingInterval
        {
            get { return m_publishingInterval; }
            set { m_publishingInterval = value; }
        }

        /// <summary>
        /// Priority accessor
        /// </summary>
        public byte Priority
        {
            get { return m_priority; }
            set { m_priority = value; }
        }

        #region Private Fields
        private uint m_maxNotificationsPerPublish = 0;
        private uint m_keepAliveCount = 10;
        private uint m_lifetimeCount = 1000;
        private int m_publishingInterval = 0;
        private byte m_priority = 0;
        #endregion
    }

  
    /// <summary>
    /// Event category data
    /// </summary>
    public class EventCategory
    {
        /// <summary>
        /// CategoryID accessor
        /// </summary>
        [XmlAttribute]
        public int CategoryID
        {
            get { return m_CategoryID; }
            set { m_CategoryID = value; }
        }

        /// <summary>
        /// strNodeId accessor
        /// </summary>
        [XmlAttribute]
        public string strNodeId
        {
            get { return m_strNodeId; }
            set { m_strNodeId = value; }
        }

        /// <summary>
        /// BrowseName accessor
        /// </summary>
        [XmlAttribute]
        public string BrowseName
        {
            get { return m_BrowseName; }
            set { m_BrowseName = value; }
        }

        /// <summary>
        /// EventDesc accessor
        /// </summary>
        [XmlAttribute]
        public string EventDesc
        {
            get { return m_EventDesc; }
            set { m_EventDesc = value; }
        }

        /// <summary>
        /// EventType accessor
        /// </summary>
        [XmlAttribute]
        public int EventType
        {
            get { return m_EventType; }
            set { m_EventType = value; }
        }

        #region Private Fields
        private int m_CategoryID;  //Event Category ID number
        private string m_strNodeId;   //NodeId of the event type
        private string m_BrowseName;
        private string m_EventDesc;
        private int m_EventType;
        #endregion
    }
    /// <summary>
    /// Event attribute data
    /// </summary>
    public class EventAttribute
    {
        /// <summary>
        /// AttributeID accessor
        /// </summary>
        [XmlAttribute]
        public int AttributeID
        {
            get { return m_AttributeID; }
            set { m_AttributeID = value; }
        }

        /// <summary>
        /// strNodeId accessor
        /// </summary>
        [XmlAttribute]
        public string strNodeId
        {
            get { return m_strNodeId; }
            set { m_strNodeId = value; }
        }

        /// <summary>
        /// BrowseName accessor
        /// </summary>
        [XmlAttribute]
        public string BrowseName
        {
            get { return m_BrowseName; }
            set { m_BrowseName = value; }
        }

        /// <summary>
        /// BrowseNameNSIndex accessor
        /// </summary>
        [XmlAttribute]
        public ushort BrowseNameNSIndex
        {
            get { return m_BrowseNameNSIndex; }
            set { m_BrowseNameNSIndex = value; }
        }

        /// <summary>
        /// AttrDesc accessor
        /// </summary>
        [XmlAttribute]
        public string AttrDesc
        {
            get { return m_AttrDesc; }
            set { m_AttrDesc = value; }
        }

        /// <summary>
        /// strEventNodeId accessor
        /// </summary>
        [XmlAttribute]
        public string strEventNodeId
        {
            get { return m_strEventNodeId; }
            set { m_strEventNodeId = value; }
        }

        /// <summary>
        /// strDataTypeNodeId accessor
        /// </summary>
        [XmlAttribute]
        public string strDataTypeNodeId
        {
            get { return m_strDataTypeNodeId; }
            set { m_strDataTypeNodeId = value; }
        }

        /// <summary>
        /// IsArray accessor
        /// </summary>
        [XmlAttribute]
        public bool IsArray
        {
            get { return m_IsArray; }
            set { m_IsArray = value; }
        }

        /// <summary>
        /// ActualDataType accessor
        /// </summary>
        [XmlIgnoreAttribute]
        public Type ActualDataType
        {
            get { return m_ActualDataType; }
            set { m_ActualDataType = value; }
        }

        

        #region Private Fields
        private int m_AttributeID;  //Attribute ID number
        private string m_strNodeId;   //NodeId of the attribute
        private string m_BrowseName;
        private ushort m_BrowseNameNSIndex; // namespace index
        private string m_AttrDesc;
        private string m_strEventNodeId; // NodeId of the Event Type where this attribute is defined
        private string m_strDataTypeNodeId; // NodeId of the DataType
        private bool m_IsArray;
        private Type m_ActualDataType;
        #endregion

    }
    #endregion
}
