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

using Opc.Ua.PubSub;
using Opc.Ua.Server;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Opc.Ua.Sample.PubSub
{
   
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class PublisherNodeManager : CustomNodeManager2
    {
        X509Certificate2 certificate;
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public PublisherNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server)
        {
            SystemContext.NodeIdFactory = this;
            List<string> namespaceUris = new List<string>();
            namespaceUris.Add(Namespaces.OpcUa);
            namespaceUris.Add(Namespaces.OpcUa + "/Instance");
            NamespaceUris = namespaceUris;

            m_typeNamespaceIndex = Server.NamespaceUris.GetIndexOrAppend(namespaceUris[0]);
            m_namespaceIndex = Server.NamespaceUris.GetIndexOrAppend(namespaceUris[1]);
            m_namespaceIndex = 0;
            m_lastUsedId = 0;
            certificate = configuration.SecurityConfiguration.ApplicationCertificate.Certificate;
            m_PubSubAdaptor = new PubSubAdaptor(certificate);
            m_subscriberDelegate = new Opc.Ua.Core.SubscriberDelegate(AssignSusbribedDataValue);
        }
        #endregion

        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        /// <returns>The new NodeId.</returns>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            uint id = Utils.IncrementIdentifier(ref m_lastUsedId);
            return new NodeId(id, m_namespaceIndex);
        }
        
        #endregion

        #region INodeManager Members
        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.  
        /// </remarks>
        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                base.CreateAddressSpace(externalReferences);
                AssignHandlers();
                CreatePubSubTargetVariables();
                INodeManager nodeManager;
                NodeHandle nodehandle = Server.NodeManager.GetManagerHandle(Variables.PublishSubscribe_Status_State, out nodeManager) as NodeHandle;
                if (nodehandle != null)
                {
                    BaseDataVariableState baseDataVariableState = nodehandle.Node as BaseDataVariableState;
                    if (baseDataVariableState != null)
                    {
                        baseDataVariableState.Value = PubSubState.Operational;
                    }
                }
                
            }
        }
        #endregion

       
         
      

        /// <summary>
        /// Loads a node set from a file or resource and addes them to the set of predefined nodes.
        /// </summary>

        #region Protected Methods
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            NodeStateCollection predefinedNodes = new NodeStateCollection();
            Export.UANodeSet nodeset;
            using (FileStream fs = new FileStream("PubSub/Opc.Ua.NodeSet2.xml", FileMode.Open))
            {
                nodeset = Export.UANodeSet.Read(fs);
            }
            nodeset.Import(context, predefinedNodes);
            return predefinedNodes;
        }

        /// <summary>
        /// Replaces the generic node with a node specific to the model.
        /// </summary>
        protected override NodeState AddBehaviourToPredefinedNode(ISystemContext context, NodeState predefinedNode)
        {
            BaseObjectState passiveNode = predefinedNode as BaseObjectState;

            if (passiveNode == null)
            {
                return predefinedNode;
            }

            NodeId typeId = passiveNode.TypeDefinitionId;

            if (!IsNodeIdInNamespace(typeId) || typeId.IdType != IdType.Numeric)
            {
                return predefinedNode;
            }

            switch ((uint)typeId.Identifier)
            {
                case ObjectTypes.NamespaceMetadataType:
                    {
                        if (passiveNode is NamespaceMetadataState || passiveNode.NodeId != Opc.Ua.PubSub.ObjectIds.OPCUANamespaceMetadata)
                        {
                            break;
                        }

                        NamespaceMetadataState activeNode = new NamespaceMetadataState(passiveNode.Parent);

                        activeNode.DefaultRolePermissions = new PropertyState<RolePermissionType[]>(activeNode);
                        activeNode.DefaultUserRolePermissions = new PropertyState<RolePermissionType[]>(activeNode);
                        activeNode.DefaultAccessRestrictions = new PropertyState<ushort>(activeNode);

                        activeNode.Create(context, passiveNode);

                        activeNode.DefaultRolePermissions.NodeId = Opc.Ua.PubSub.VariableIds.OPCUANamespaceMetadata_DefaultRolePermissions;
                        activeNode.DefaultUserRolePermissions.NodeId = Opc.Ua.PubSub.VariableIds.OPCUANamespaceMetadata_DefaultUserRolePermissions;
                        activeNode.DefaultAccessRestrictions.NodeId = Opc.Ua.PubSub.VariableIds.OPCUANamespaceMetadata_DefaultAccessRestrictions;

                        activeNode.DefaultRolePermissions.OnSimpleReadValue += ReadDefaultRolePermissions;
                        activeNode.DefaultUserRolePermissions.OnSimpleReadValue += ReadDefaultUserRolePermissions;
                        activeNode.DefaultAccessRestrictions.OnSimpleReadValue += ReadDefaultAccessRestrictions;

                        // replace the node in the parent.
                        if (passiveNode.Parent != null)
                        {
                            passiveNode.Parent.ReplaceChild(context, activeNode);
                        }

                        return activeNode;
                    }

                case ObjectTypes.ServerType:
                    {
                        if (passiveNode is ServerObjectState)
                        {
                            break;
                        }

                        ServerObjectState activeNode = new ServerObjectState(passiveNode.Parent);
                        activeNode.Create(context, passiveNode);

                        // add the server object as the root notifier.
                        AddRootNotifier(activeNode);

                        // replace the node in the parent.
                        if (passiveNode.Parent != null)
                        {
                            passiveNode.Parent.ReplaceChild(context, activeNode);
                        }

                        return activeNode;
                    }


            }
            return predefinedNode;
        }

        #endregion

        #region Public Methods

        public void CreatePubSubTargetVariables()
        {
            NodeState objectstate = Server.DiagnosticsNodeManager.FindPredefinedNode(ObjectIds.ObjectsFolder, typeof(NodeState));
            FolderState PubSubTargetVariables = new FolderState(objectstate);
            PubSubTargetVariables.Create(Server.DefaultSystemContext, new NodeId("PubSubTargetVariables", 2), new QualifiedName("PubSubTargetVariables"), new LocalizedText("PubSubTargetVariables"), false);
            PubSubTargetVariables.ReferenceTypeId = ReferenceTypeIds.Organizes;
            PubSubTargetVariables.EventNotifier = EventNotifiers.None;
            PubSubTargetVariables.TypeDefinitionId = PubSubTargetVariables.GetDefaultTypeDefinitionId(Server.DefaultSystemContext);
            objectstate.AddChild(PubSubTargetVariables);

            BaseDataVariableState baseDataVariableState = CreateVaraibleState(PubSubTargetVariables, "SByte", DataTypeIds.SByte);

            PubSubTargetVariables.AddChild(baseDataVariableState);
            baseDataVariableState = CreateVaraibleState(PubSubTargetVariables, "Byte", DataTypeIds.Byte);
            PubSubTargetVariables.AddChild(baseDataVariableState);
            baseDataVariableState = CreateVaraibleState(PubSubTargetVariables, "Boolean", DataTypeIds.Boolean);
            PubSubTargetVariables.AddChild(baseDataVariableState);
            baseDataVariableState = CreateVaraibleState(PubSubTargetVariables, "Int16", DataTypeIds.Int16);
            PubSubTargetVariables.AddChild(baseDataVariableState);
            baseDataVariableState = CreateVaraibleState(PubSubTargetVariables, "UInt16", DataTypeIds.UInt16);
            PubSubTargetVariables.AddChild(baseDataVariableState);
            baseDataVariableState = CreateVaraibleState(PubSubTargetVariables, "Int32", DataTypeIds.Int32);
            PubSubTargetVariables.AddChild(baseDataVariableState);
            baseDataVariableState = CreateVaraibleState(PubSubTargetVariables, "UInt32", DataTypeIds.UInt32);
            PubSubTargetVariables.AddChild(baseDataVariableState);
            baseDataVariableState = CreateVaraibleState(PubSubTargetVariables, "Int64", DataTypeIds.Int64);
            PubSubTargetVariables.AddChild(baseDataVariableState);
            baseDataVariableState = CreateVaraibleState(PubSubTargetVariables, "UInt64", DataTypeIds.UInt64);
            PubSubTargetVariables.AddChild(baseDataVariableState);
            baseDataVariableState = CreateVaraibleState(PubSubTargetVariables, "Float", DataTypeIds.Float);
            PubSubTargetVariables.AddChild(baseDataVariableState);
            baseDataVariableState = CreateVaraibleState(PubSubTargetVariables, "Double", DataTypeIds.Double);
            PubSubTargetVariables.AddChild(baseDataVariableState);
            baseDataVariableState = CreateVaraibleState(PubSubTargetVariables, "DateTime", DataTypeIds.DateTime);
            PubSubTargetVariables.AddChild(baseDataVariableState);
            baseDataVariableState = CreateVaraibleState(PubSubTargetVariables, "Guid", DataTypeIds.Guid);
            PubSubTargetVariables.AddChild(baseDataVariableState);
            baseDataVariableState = CreateVaraibleState(PubSubTargetVariables, "String", DataTypeIds.String);
            PubSubTargetVariables.AddChild(baseDataVariableState);
            AddPredefinedNode(Server.DefaultSystemContext, PubSubTargetVariables);
            
        }
        public BaseDataVariableState CreateVaraibleState(NodeState parentstate, string name, NodeId dataType)
        {
            BaseDataVariableState baseDataVariableState = new BaseDataVariableState(parentstate);
            baseDataVariableState.Create(Server.DefaultSystemContext, new NodeId(name, 2), new QualifiedName(name), new LocalizedText(name), false);
            baseDataVariableState.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            baseDataVariableState.TypeDefinitionId = baseDataVariableState.GetDefaultTypeDefinitionId(Server.DefaultSystemContext);
            baseDataVariableState.DataType = dataType;
            baseDataVariableState.MinimumSamplingInterval = 100;
            baseDataVariableState.UserAccessLevel = baseDataVariableState.AccessLevel = AccessLevels.CurrentReadOrWrite;
            baseDataVariableState.Value = 0;
            baseDataVariableState.ValueRank = ValueRanks.Any;
            return baseDataVariableState;
        }

        public override void Browse(OperationContext context, ref ContinuationPoint continuationPoint, IList<ReferenceDescription> references)
        {
            base.Browse(context, ref continuationPoint, references);
        }

        public override void Call(OperationContext context, IList<CallMethodRequest> methodsToCall, IList<CallMethodResult> results, IList<ServiceResult> errors)
        {
            base.Call(context, methodsToCall, results, errors);
        }


        public void AssignSusbribedDataValue(NodeId targetNodeId, DataValue datavalue)
        {
            INodeManager nodeManager;
            NodeState nodehandle = Server.NodeManager.GetManagerHandle(targetNodeId, out nodeManager) as NodeState;
            if (nodehandle != null)
            {
                BaseDataVariableState baseDataVariableState = nodehandle as BaseDataVariableState;
                if (baseDataVariableState != null)
                {
                    if (baseDataVariableState.MinimumSamplingInterval <= 0)
                    {
                        baseDataVariableState.MinimumSamplingInterval = 100;
                    }
                    //if(baseDataVariableState.Value!= datavalue.Value)
                    {
                        baseDataVariableState.Value = datavalue.Value;
                        baseDataVariableState.StatusCode = datavalue.StatusCode;
                        baseDataVariableState.Timestamp = datavalue.SourceTimestamp;
                    }

                }
            }
            // if(baseDataVariableState!=null)
            {

            }

        }

        #endregion

        private void AssignHandlers()
        {
            AddConnectionMethodState addConnectionMethod = (AddConnectionMethodState)Server.DiagnosticsNodeManager.FindPredefinedNode(MethodIds.PublishSubscribe_AddConnection, typeof(AddConnectionMethodState));
            addConnectionMethod.OnCall = AddConnectionMethodStateMethodCallHandler;

            RemoveConnectionMethodState removeConnectionMethod = (RemoveConnectionMethodState)Server.DiagnosticsNodeManager.FindPredefinedNode(MethodIds.PublishSubscribe_RemoveConnection, typeof(RemoveConnectionMethodState));
            removeConnectionMethod.OnCall = RemoveConnectionMethodStateMethodCallHandler;

            DataSetFolderState dataSetFolderState = (DataSetFolderState)Server.DiagnosticsNodeManager.FindPredefinedNode(ObjectIds.PublishSubscribe_PublishedDataSets, typeof(DataSetFolderState));
             dataSetFolderState.AddPublishedDataItems.OnCall = AddPublishedDataItemsMethodStateMethodCallHandler;
            dataSetFolderState.RemovePublishedDataSet = new RemovePublishedDataSetMethodState(dataSetFolderState);
            dataSetFolderState.RemovePublishedDataSet.Create(Server.DefaultSystemContext, new NodeId(dataSetFolderState.NodeId.Identifier + ".RemovePublishedDataSet"), new QualifiedName("RemovePublishedDataSet"), new LocalizedText("RemovePublishedDataSet"), false);
            dataSetFolderState.RemovePublishedDataSet.OnCall = RemovePublishedDataSetMethodStateMethodCallHandler;

        }
        public void OnServerStarted()
        {
            string address = string.Empty;
            foreach (var endpoint in Server.EndpointAddresses)
            {
                address = endpoint.ToString();
                break;
            }
            ApplicationStartSettings settings = new ApplicationStartSettings();
            settings.EndpointUrl = address;// "opc.tcp://localhost:48011/UA/PubSubSampleServer";
            m_PubSubAdaptor.Start(settings).Wait();
        }
        #region Handlers
        #region PublishedDataSet Handlers
        ServiceResult RemovePublishedDataSetMethodStateMethodCallHandler(ISystemContext context, MethodState method, NodeId objectId, NodeId dataSetNodeId)
        {
            PublishedDataItemsState _PublishedDataItemsState = FindPredefinedNode(dataSetNodeId, typeof(NodeState)) as PublishedDataItemsState;
            m_PubSubAdaptor.RemovePublishedDataItems(_PublishedDataItemsState);
            method.Parent.RemoveChild(_PublishedDataItemsState);
            return ServiceResult.Good;
        }
        ServiceResult AddPublishedDataItemsMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        string name,
        string[] fieldNameAliases,
        UInt16[] fieldFlags,
        PublishedVariableDataType[] variablesToAdd,
        ref NodeId dataSetNodeId,
        ref ConfigurationVersionDataType configurationVersion,
        ref StatusCode[] addResults)
        {
            PublishedDataItemsState _AddPublishedDataItemsMethodState = new PublishedDataItemsState(method.Parent);
            _AddPublishedDataItemsMethodState.Create(context, new NodeId("PubSub.DataSets." + name, 2), new QualifiedName(name), new LocalizedText(name), false);

            _AddPublishedDataItemsMethodState.AddVariables = new PublishedDataItemsAddVariablesMethodState(_AddPublishedDataItemsMethodState);
            _AddPublishedDataItemsMethodState.AddVariables.Create(context, new NodeId(_AddPublishedDataItemsMethodState.NodeId.Identifier + ".AddVariables", 2), new QualifiedName("AddVariables"), new LocalizedText("AddVariables"), false);
            _AddPublishedDataItemsMethodState.AddVariables.OnCall = PublishedDataItemsAddVariablesMethodStateMethodCallHandler;

            _AddPublishedDataItemsMethodState.ConfigurationVersion = new PropertyState<ConfigurationVersionDataType>(_AddPublishedDataItemsMethodState);
            _AddPublishedDataItemsMethodState.ConfigurationVersion.Create(context, new NodeId(_AddPublishedDataItemsMethodState.NodeId.Identifier + ".ConfigurationVersion", 2), new QualifiedName("ConfigurationVersion"), new LocalizedText("ConfigurationVersion"), false);

            ConfigurationVersionDataType data = new ConfigurationVersionDataType();
            data.MajorVersion = 1;
            data.MinorVersion = 1;

            _AddPublishedDataItemsMethodState.ConfigurationVersion.Value = data;

            _AddPublishedDataItemsMethodState.DataSetMetaData = new PropertyState<DataSetMetaDataType>(_AddPublishedDataItemsMethodState);
            _AddPublishedDataItemsMethodState.DataSetMetaData.Create(context, new NodeId(_AddPublishedDataItemsMethodState.NodeId.Identifier + ".DataSetMetaData", 2), new QualifiedName("DataSetMetaData"), new LocalizedText("DataSetMetaData"), false);

            DataSetMetaDataType _DataSetMetaDataType = new DataSetMetaDataType();
            _DataSetMetaDataType.ConfigurationVersion = _AddPublishedDataItemsMethodState.ConfigurationVersion.Value;
            _DataSetMetaDataType.DataSetClassId = new Uuid();
            _DataSetMetaDataType.Name = name;
             
            _AddPublishedDataItemsMethodState.DataSetMetaData.Value = _DataSetMetaDataType;
            _DataSetMetaDataType.Fields = new FieldMetaDataCollection();
            int i = 0;
            foreach (PublishedVariableDataType PublishedVariable in variablesToAdd)
            {

                FieldMetaData metaData = new FieldMetaData();
                metaData.Name = fieldNameAliases[i];
                metaData.DataSetFieldId = new Uuid(Guid.NewGuid());
                INodeManager nodeManager;
                NodeHandle nodehandle = Server.NodeManager.GetManagerHandle(PublishedVariable.PublishedVariable, out nodeManager) as NodeHandle;
                if (nodehandle != null)
                {
                    BaseDataVariableState publishedVariableNodeState =  nodehandle.Node as BaseDataVariableState;
                    if(publishedVariableNodeState!=null)
                    {
                        metaData.DataType = publishedVariableNodeState.DataType;
                        metaData.ValueRank = publishedVariableNodeState.ValueRank;
                        //publishedVariableNodeState.ArrayDimensions.CopyTo(metaData.ArrayDimensions.ToArray(), publishedVariableNodeState.ArrayDimensions.Count);
                    }
                }
                    _DataSetMetaDataType.Fields.Add(metaData);
                i++;
            }
            _AddPublishedDataItemsMethodState.PublishedData = new PropertyState<PublishedVariableDataType[]>(_AddPublishedDataItemsMethodState);
            _AddPublishedDataItemsMethodState.PublishedData.Create(context, new NodeId(_AddPublishedDataItemsMethodState.NodeId.Identifier + ".PublishedData", 2), new QualifiedName("PublishedData"), new LocalizedText("PublishedData"), false);
            _AddPublishedDataItemsMethodState.PublishedData.Value = variablesToAdd;
            _AddPublishedDataItemsMethodState.TypeDefinitionId = _AddPublishedDataItemsMethodState.GetDefaultTypeDefinitionId(context);
            dataSetNodeId = _AddPublishedDataItemsMethodState.NodeId;
            configurationVersion = _AddPublishedDataItemsMethodState.ConfigurationVersion.Value;

            method.Parent.AddChild(_AddPublishedDataItemsMethodState);
            AddPredefinedNode(context, _AddPublishedDataItemsMethodState);
            m_PubSubAdaptor.AddPublishedDataItems(_AddPublishedDataItemsMethodState);
            return ServiceResult.Good;
        }

        #endregion
        #region Connection Handlers
        ServiceResult AddConnectionMethodStateMethodCallHandler(ISystemContext context, MethodState method, NodeId objectId, PubSubConnectionDataType configuration, ref NodeId connectionId)
        {
            PubSubConnectionState existedChild = method.Parent.FindChild(context, new QualifiedName(configuration.Name, 2)) as PubSubConnectionState;
            if (existedChild != null)
            {
                return new ServiceResult(StatusCodes.BadBrowseNameDuplicated);
            }
            else
            {
                PubSubConnectionState state = new PubSubConnectionState(method.Parent);
                state.Create(context, new NodeId("PubSub." + configuration.Name, 2), new QualifiedName(configuration.Name, 2), new LocalizedText(configuration.Name), false);

                state.AddReaderGroup.OnCall = PubSubConnectionAddReaderGroupGroupMethodStateMethodCallHandler;
                state.AddWriterGroup.OnCall = PubSubConnectionTypeAddWriterGroupMethodStateMethodCallHandler;
                state.RemoveGroup.OnCall = PubSubConnectionTypeRemoveGroupMethodStateMethodCallHandler;

                NetworkAddressUrlDataType adddressObject = configuration.Address.Body as NetworkAddressUrlDataType;
                state.Address = new NetworkAddressUrlState(state);
                state.Address.Create(context, new NodeId(state.NodeId.Identifier + ".Address", 2), new QualifiedName("Address", 2), new LocalizedText("Address"), false);
                state.Address.NetworkInterface = new BaseDataVariableState<string>(state.Address);
                state.Address.NetworkInterface.Create(context, new NodeId(state.Address.NodeId.Identifier+ ".NetworkInterface", 2), new QualifiedName("NetworkInterface", 2), new LocalizedText("NetworkInterface"), false);
                state.Address.NetworkInterface.Value = adddressObject.NetworkInterface;
                (state.Address as NetworkAddressUrlState).Url = new BaseDataVariableState<string>(state.Address);
                (state.Address as NetworkAddressUrlState).Url.Create(context, new NodeId(state.Address.NodeId.Identifier + ".Url", 2), new QualifiedName("Url", 2), new LocalizedText("Url"), false);
                (state.Address as NetworkAddressUrlState).Url.Value = adddressObject.Url;
                state.Address.TypeDefinitionId = state.Address.GetDefaultTypeDefinitionId(context);
                state.PublisherId.Create(context, new NodeId(state.NodeId.Identifier + ".PublisherId", 2), new QualifiedName("PublisherId", 2), new LocalizedText("PublisherId"), false);
                state.PublisherId.Value = configuration.PublisherId.Value;

                state.TransportSettings.NodeId = new NodeId(state.TransportSettings.NodeId.Identifier, 2);

                ExtensionObject TransportObject = configuration.TransportSettings;

                if (TransportObject.Body is BrokerConnectionTransportDataType)
                {
                    BrokerConnectionTransportDataType BrokerData = TransportObject.Body as BrokerConnectionTransportDataType;
                    BrokerConnectionTransportState brokerConnectionTransportState = new BrokerConnectionTransportState(state);
                    brokerConnectionTransportState.Create(context, new NodeId(state.NodeId.Identifier + ".TransportSettings", 2), new QualifiedName("TransportSettings"), new LocalizedText("TransportSettings"), false);

                    brokerConnectionTransportState.AuthenticationProfileUri = new PropertyState<string>(brokerConnectionTransportState);
                    brokerConnectionTransportState.AuthenticationProfileUri.Create(context, new NodeId(state.NodeId.Identifier + ".AuthenticationProfileUri", 2), new QualifiedName("AuthenticationProfileUri"), new LocalizedText("AuthenticationProfileUri"), false);
                    brokerConnectionTransportState.AuthenticationProfileUri.Value = BrokerData.AuthenticationProfileUri;
                    brokerConnectionTransportState.ResourceUri = new PropertyState<string>(brokerConnectionTransportState);
                    brokerConnectionTransportState.ResourceUri.Create(context, new NodeId(state.NodeId.Identifier + ".ResourceUri", 2), new QualifiedName("ResourceUri"), new LocalizedText("ResourceUri"), false);
                    brokerConnectionTransportState.ResourceUri.Value = BrokerData.ResourceUri;
                    state.TransportSettings = brokerConnectionTransportState;
                }
                else if (TransportObject.Body is DatagramConnectionTransportDataType)
                {
                    DatagramConnectionTransportDataType DatagramData = TransportObject.Body as DatagramConnectionTransportDataType;

                    DatagramConnectionTransportState datagramConnectionTransportState = new DatagramConnectionTransportState(state);
                    datagramConnectionTransportState.Create(context, new NodeId(state.NodeId.Identifier + ".TransportSettings", 2), new QualifiedName("TransportSettings"), new LocalizedText("TransportSettings"), false);

                    NetworkAddressUrlDataType networkaddress = DatagramData.DiscoveryAddress.Body as NetworkAddressUrlDataType;
                    datagramConnectionTransportState.DiscoveryAddress = new NetworkAddressUrlState(datagramConnectionTransportState);
                    datagramConnectionTransportState.DiscoveryAddress.Create(context, new NodeId(datagramConnectionTransportState.NodeId.Identifier + ".DiscoveryAddress", 2), new QualifiedName("DiscoveryAddress", 2), new LocalizedText("DiscoveryAddress"), false);
                    datagramConnectionTransportState.DiscoveryAddress.NetworkInterface = new BaseDataVariableState<string>(datagramConnectionTransportState.DiscoveryAddress);
                    datagramConnectionTransportState.DiscoveryAddress.NetworkInterface.Create(context, new NodeId(datagramConnectionTransportState.DiscoveryAddress.NodeId.Identifier + ".NetworkInterface", 2), new QualifiedName("NetworkInterface", 2), new LocalizedText("NetworkInterface"), false);
                    datagramConnectionTransportState.DiscoveryAddress.NetworkInterface.Value = networkaddress.NetworkInterface;
                    (datagramConnectionTransportState.DiscoveryAddress as NetworkAddressUrlState).Url = new BaseDataVariableState<string>(datagramConnectionTransportState.DiscoveryAddress);
                    (datagramConnectionTransportState.DiscoveryAddress as NetworkAddressUrlState).Url.Create(context, new NodeId(datagramConnectionTransportState.DiscoveryAddress.NodeId.Identifier + ".Url", 2), new QualifiedName("Url", 2), new LocalizedText("Url"), false);
                    (datagramConnectionTransportState.DiscoveryAddress as NetworkAddressUrlState).Url.Value = networkaddress.Url;
                    datagramConnectionTransportState.DiscoveryAddress.TypeDefinitionId = datagramConnectionTransportState.DiscoveryAddress.GetDefaultTypeDefinitionId(context);
                     
                    state.TransportSettings = datagramConnectionTransportState;

                }
                state.TransportProfileUri = new SelectionListState<string>(state);
                state.TransportProfileUri.Create(context, new NodeId(state.NodeId.Identifier + "TransportProfileUri", state.NodeId.NamespaceIndex), new QualifiedName("TransportProfileUri", state.BrowseName.NamespaceIndex), new LocalizedText("TransportProfileUri"), false);
                state.TransportProfileUri.Value =  configuration.TransportProfileUri;
                state.TransportProfileUri.TypeDefinitionId = state.TransportProfileUri.GetDefaultTypeDefinitionId(context);

                state.TypeDefinitionId = state.GetDefaultTypeDefinitionId(context);
                state.Status = new PubSubStatusState(state);
                state.Status.Create(context, new NodeId(state.NodeId.Identifier + ".Status", state.NodeId.NamespaceIndex), new QualifiedName("Status", state.BrowseName.NamespaceIndex), new LocalizedText("Status"), false);

                state.Status.State = new BaseDataVariableState<PubSubState>(state.Status);
                state.Status.State.Create(context, new NodeId(state.NodeId.Identifier + ".Status.State", state.NodeId.NamespaceIndex), new QualifiedName("State", state.BrowseName.NamespaceIndex), new LocalizedText("State"), false);
                state.Status.State.MinimumSamplingInterval = 100;
                state.Status.Enable = new MethodState(state.Status);
                state.Status.Enable.Create(context, new NodeId(state.NodeId.Identifier + ".Status" + ".Enable", state.NodeId.NamespaceIndex), new QualifiedName("Enable", state.BrowseName.NamespaceIndex), new LocalizedText("Enable"), false);
                state.Status.Enable.OnCallMethod +=  EnableMethodCalledEventHandler;
                
                state.Status.AddReference(ReferenceTypeIds.HasComponent, false, state.Status.Enable.NodeId); 
                state.Status.AddChild(state.Status.Enable);
                AddPredefinedNode(context, state.Status.Enable);
                state.Status.Disable = new MethodState(state.Status);
                state.Status.Disable.Create(context, new NodeId(state.NodeId.Identifier + ".Status" + ".Disable", state.NodeId.NamespaceIndex), new QualifiedName("Disable", state.BrowseName.NamespaceIndex), new LocalizedText("Disable"), false);
                state.Status.Disable.OnCallMethod += DisableMethodCalledEventHandler;
                state.Status.AddReference(ReferenceTypeIds.HasComponent, false, state.Status.Disable.NodeId);

                state.TypeDefinitionId = state.GetDefaultTypeDefinitionId(context);
                state.Status.AddChild(state.Status.Disable);
                AddPredefinedNode(context, state.Status.Disable);
                connectionId = state.NodeId;

                method.Parent.AddChild(state);
                AddPredefinedNode(context, state);
                
                m_PubSubAdaptor.AddConnection(state as PubSubConnectionState);

                
            }
            return ServiceResult.Good;
        }
         
        ServiceResult RemoveConnectionMethodStateMethodCallHandler(ISystemContext context, MethodState method, NodeId objectId, NodeId connectionId)
        {
            PubSubConnectionState _RemoveConnectionState = FindPredefinedNode(connectionId, typeof(NodeState)) as PubSubConnectionState;
            if (_RemoveConnectionState != null) 
            {
                m_PubSubAdaptor.RemoveConnection(_RemoveConnectionState);
                method.Parent.RemoveChild(_RemoveConnectionState); 
                return ServiceResult.Good;
            }
            return new ServiceResult(StatusCodes.BadNotFound);
        }

        #endregion
        #region Enable_Disable Handler

        
            ServiceResult Connection_EnableMethodCalledEventHandler(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            INodeManager nodeManager;
            NodeHandle nodehandle = Server.NodeManager.GetManagerHandle(ObjectIds.PublishSubscribe_Status, out nodeManager) as NodeHandle;
             if(nodehandle!=null)
            {
                BaseDataVariableState<PubSubState> baseDataVariableState=  nodehandle.Node as BaseDataVariableState<PubSubState>;
                BaseDataVariableState<PubSubState> variableState = FindPredefinedNode(new NodeId(method.NodeId.Identifier.ToString().Replace(".Enable", ".State"), method.NodeId.NamespaceIndex), typeof(NodeState)) as BaseDataVariableState<PubSubState>;
                if (baseDataVariableState.Value != PubSubState.Operational)
                {
                    
                    if (variableState != null)
                    {
                        variableState.Value = PubSubState.Paused;
                    }
                }
                else
                {
                    if (variableState.Value != PubSubState.Error)
                    {
                        variableState.Value = PubSubState.Operational;
                    }
                }
            }
            return ServiceResult.Good;
        }
            ServiceResult EnableMethodCalledEventHandler(ISystemContext context,MethodState method,IList<object> inputArguments,IList<object> outputArguments)
        {
            BaseDataVariableState<PubSubState> variableState = FindPredefinedNode(new NodeId(method.NodeId.Identifier.ToString().Replace(".Enable", ".State"), method.NodeId.NamespaceIndex), typeof(NodeState)) as BaseDataVariableState<PubSubState>;
            if (variableState != null)
            {
                variableState.Value = PubSubState.Operational;
                try
                {
                    BaseInstanceState state = ((method.Parent as BaseInstanceState).Parent as BaseInstanceState).Parent as BaseInstanceState;
                    BaseDataVariableState<PubSubState> parentVariableState = FindPredefinedNode(new NodeId(state.NodeId.Identifier.ToString()+".Status"+".State", method.NodeId.NamespaceIndex), typeof(NodeState)) as BaseDataVariableState<PubSubState>;
                    if(parentVariableState!=null && (parentVariableState.Value!= PubSubState.Operational))
                    {
                        variableState.Value = PubSubState.Disabled;
                    }
                }
                catch(Exception ex)
                {

                }
            }
            return ServiceResult.Good;
        }
        ServiceResult DisableMethodCalledEventHandler(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            NodeId id=new NodeId(method.NodeId.Identifier.ToString().Replace(".Disable", ".State"), method.NodeId.NamespaceIndex);
            BaseDataVariableState<PubSubState> variableState = FindPredefinedNode(id, typeof(NodeState)) as BaseDataVariableState<PubSubState>;
            if (variableState != null)
            {
                variableState.Value = PubSubState.Disabled;
                List<BaseInstanceState> LstChildren = new List<BaseInstanceState>();
                ((method.Parent as BaseInstanceState).Parent as BaseInstanceState).GetChildren(context, LstChildren);
                if(LstChildren.Count>0)
                {
                    foreach(BaseInstanceState instancestate in LstChildren)
                    {
                        ValidateChildStatus(context,instancestate);
                    }
                }
            }
            return ServiceResult.Good;
        }
        void ValidateChildStatus(ISystemContext context,BaseInstanceState instancestate)
        {
            BaseDataVariableState<PubSubState> variableState = instancestate as BaseDataVariableState<PubSubState>;
            if(variableState!=null)
            {
                variableState.Value = PubSubState.Paused;
                return;
            }
            if ((instancestate is PubSubConnectionState)
                        || (instancestate is ReaderGroupState)
                        || (instancestate is WriterGroupState)
                        || (instancestate is DataSetReaderState)
                        || (instancestate is DataSetWriterState)
                        || (instancestate is PubSubStatusState))
            { 

                List<BaseInstanceState> LstChildren = new List<BaseInstanceState>();
                try
                {
                    instancestate.GetChildren(context, LstChildren);
                }
                catch (Exception ex)
                {
                }
                if (LstChildren.Count > 0)
                {
                    foreach (BaseInstanceState childinstancestate in LstChildren)
                    {
                        BaseDataVariableState<PubSubState> variableState1 = childinstancestate as BaseDataVariableState<PubSubState>;
                        if (variableState1 != null)
                        {
                            variableState1.Value = PubSubState.Paused;
                             
                        }
                        if ((childinstancestate is PubSubConnectionState)
                            || (childinstancestate is ReaderGroupState)
                            || (childinstancestate is WriterGroupState)
                            || (childinstancestate is DataSetReaderState)
                            || (childinstancestate is DataSetWriterState)
                            || (childinstancestate is PubSubStatusState))
                        {
                            ValidateChildStatus(context, childinstancestate);
                        }
                    }
                }
            }
        }
        #endregion
        #region Group Handlers
        #region Reader Group Handlers
        ServiceResult PubSubConnectionAddReaderGroupGroupMethodStateMethodCallHandler(ISystemContext context, MethodState method, NodeId objectId, ReaderGroupDataType configuration, ref NodeId groupId)
        {
            ReaderGroupState existedChild = method.Parent.FindChild(context, new QualifiedName(configuration.Name, 2)) as ReaderGroupState;
            if (existedChild != null)
            {
                return new ServiceResult(StatusCodes.BadBrowseNameDuplicated);
            }
            else
            {
                ReaderGroupState _ReaderGroupState = new ReaderGroupState(method.Parent);
                _ReaderGroupState.Create(context, new NodeId(method.Parent.NodeId.Identifier + "." + configuration.Name, 2), new QualifiedName(configuration.Name, 2), new LocalizedText(configuration.Name), false);
                _ReaderGroupState.AddDataSetReader = new PubSubGroupTypeAddReaderMethodState(_ReaderGroupState);
                _ReaderGroupState.AddDataSetReader.Create(context, new NodeId(_ReaderGroupState.NodeId.Identifier + ".AddDataSetReader", 2), new QualifiedName("AddDataSetReader"), new LocalizedText("AddDataSetReader"), false);
                _ReaderGroupState.AddDataSetReader.OnCall = PubSubGroupTypeAddReaderMethodStateMethodCallHandler;

                _ReaderGroupState.RemoveDataSetReader = new PubSubGroupTypeRemoveReaderMethodState(_ReaderGroupState);
                _ReaderGroupState.RemoveDataSetReader.Create(context, new NodeId(_ReaderGroupState.NodeId.Identifier + ".RemoveDataSetReader", 2), new QualifiedName("RemoveDataSetReader"), new LocalizedText("RemoveDataSetReader"), false);
                _ReaderGroupState.RemoveDataSetReader.OnCall = PubSubGroupTypeRemoveReaderMethodStateMethodCallHandler;

                _ReaderGroupState.SecurityGroupId = new PropertyState<string>(_ReaderGroupState);
                _ReaderGroupState.SecurityGroupId.Create(context, new NodeId(_ReaderGroupState.NodeId.Identifier + ".SecurityGroupId", 2), new QualifiedName("SecurityGroupId"), new LocalizedText("SecurityGroupId"), false);
                _ReaderGroupState.SecurityGroupId.Value = configuration.SecurityGroupId;

                _ReaderGroupState.SecurityMode = new PropertyState<MessageSecurityMode>(_ReaderGroupState);
                _ReaderGroupState.SecurityMode.Create(context, new NodeId(_ReaderGroupState.NodeId.Identifier + ".SecurityMode", 2), new QualifiedName("SecurityMode"), new LocalizedText("SecurityMode"), false);
                _ReaderGroupState.SecurityMode.Value = configuration.SecurityMode;

                _ReaderGroupState.MaxNetworkMessageSize = new PropertyState<uint>(_ReaderGroupState);
                _ReaderGroupState.MaxNetworkMessageSize.Create(context, new NodeId(_ReaderGroupState.NodeId.Identifier + ".MaxNetworkMessageSize", 2), new QualifiedName("MaxNetworkMessageSize"), new LocalizedText("MaxNetworkMessageSize"), false);
                _ReaderGroupState.MaxNetworkMessageSize.Value = configuration.MaxNetworkMessageSize;

                _ReaderGroupState.SecurityKeyServices = new PropertyState<EndpointDescription[]>(_ReaderGroupState);
                _ReaderGroupState.SecurityKeyServices.Create(context, new NodeId(_ReaderGroupState.NodeId.Identifier + ".SecurityKeyServices", 2), new QualifiedName("SecurityKeyServices"), new LocalizedText("SecurityKeyServices"), false);
                EndpointDescription[] array = new EndpointDescription[configuration.SecurityKeyServices.Count];
                int i = 0;
                foreach (EndpointDescription desc in configuration.SecurityKeyServices)
                {
                    array[i] = desc;
                    i++;
                }
                _ReaderGroupState.SecurityKeyServices.Value = array;
                _ReaderGroupState.Status = new PubSubStatusState(_ReaderGroupState);
                _ReaderGroupState.Status.Create(context, new NodeId(_ReaderGroupState.NodeId.Identifier + ".Status", _ReaderGroupState.NodeId.NamespaceIndex), new QualifiedName("Status", _ReaderGroupState.BrowseName.NamespaceIndex), new LocalizedText("Status"), false);

                _ReaderGroupState.Status.State = new BaseDataVariableState<PubSubState>(_ReaderGroupState.Status);
                _ReaderGroupState.Status.State.Create(context, new NodeId(_ReaderGroupState.NodeId.Identifier + ".Status.State", _ReaderGroupState.NodeId.NamespaceIndex), new QualifiedName("State", _ReaderGroupState.BrowseName.NamespaceIndex), new LocalizedText("State"), false);
                _ReaderGroupState.Status.State.MinimumSamplingInterval = 100;
                _ReaderGroupState.Status.Enable = new MethodState(_ReaderGroupState.Status);
                _ReaderGroupState.Status.Enable.Create(context, new NodeId(_ReaderGroupState.NodeId.Identifier + ".Status" + ".Enable", 2), new QualifiedName("Enable"), new LocalizedText("Enable"), false);
                _ReaderGroupState.Status.Enable.OnCallMethod += EnableMethodCalledEventHandler;
                _ReaderGroupState.Status.AddReference(ReferenceTypeIds.HasComponent, false, _ReaderGroupState.Status.Enable.NodeId);
                _ReaderGroupState.Status.Disable = new MethodState(_ReaderGroupState.Status);
                _ReaderGroupState.Status.Disable.Create(context, new NodeId(_ReaderGroupState.NodeId.Identifier + ".Status" + ".Disable", 2), new QualifiedName("Disable"), new LocalizedText("Disable"), false);
                _ReaderGroupState.Status.Disable.OnCallMethod += DisableMethodCalledEventHandler;
                _ReaderGroupState.Status.AddReference(ReferenceTypeIds.HasComponent, false, _ReaderGroupState.Status.Disable.NodeId);
                _ReaderGroupState.TypeDefinitionId = _ReaderGroupState.GetDefaultTypeDefinitionId(context);
                groupId = _ReaderGroupState.NodeId;
                method.Parent.AddChild(_ReaderGroupState);
                AddPredefinedNode(context, _ReaderGroupState);
                
                return ServiceResult.Good;
            }
        }
        #endregion
        #region Writer Group Handlers
        ServiceResult PubSubConnectionTypeAddWriterGroupMethodStateMethodCallHandler(ISystemContext context, MethodState method, NodeId objectId, WriterGroupDataType configuration, ref NodeId groupId)
        {
            WriterGroupState existedChild = method.Parent.FindChild(context, new QualifiedName(configuration.Name, 2)) as WriterGroupState;
            if (existedChild != null)
            {
                return new ServiceResult(StatusCodes.BadBrowseNameDuplicated);
            }
            else
            {
                WriterGroupState _WriterGroupState = new WriterGroupState(method.Parent);
                _WriterGroupState.Create(context, new NodeId(method.Parent.NodeId.Identifier + "." + configuration.Name, 2), new QualifiedName(configuration.Name, 2), new LocalizedText(configuration.Name), false);
                _WriterGroupState.AddDataSetWriter = new PubSubGroupTypeAddWriterrMethodState(_WriterGroupState);
                _WriterGroupState.AddDataSetWriter.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + ".AddDataSetWriter", 2), new QualifiedName("AddDataSetWriter"), new LocalizedText("AddDataSetWriter"), false);
                _WriterGroupState.AddDataSetWriter.OnCall = PubSubGroupTypeAddWriterrMethodStateMethodCallHandler;
                _WriterGroupState.RemoveDataSetWriter = new PubSubGroupTypeRemoveWriterMethodState(_WriterGroupState);
                _WriterGroupState.RemoveDataSetWriter.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + ".RemoveDataSetWriter", 2), new QualifiedName("RemoveDataSetWriter"), new LocalizedText("RemoveDataSetWriter"), false);

                _WriterGroupState.RemoveDataSetWriter.OnCall = PubSubGroupTypeRemoveWriterMethodStateMethodCallHandler;

                _WriterGroupState.KeepAliveTime = new PropertyState<double>(_WriterGroupState);
                _WriterGroupState.KeepAliveTime.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + ".KeepAliveTime", 2), new QualifiedName("KeepAliveTime"), new LocalizedText("KeepAliveTime"), false);
                _WriterGroupState.KeepAliveTime.Value = configuration.KeepAliveTime;

                _WriterGroupState.LocaleIds = new PropertyState<string[]>(_WriterGroupState);
                _WriterGroupState.LocaleIds.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + ".LocaleIds", 2), new QualifiedName("LocaleIds"), new LocalizedText("LocaleIds"), false);
                string[] array = new string[configuration.LocaleIds.Count];
                int i = 0;
                foreach (string desc in configuration.LocaleIds)
                {
                    array[i] = desc;
                    i++;
                }
                _WriterGroupState.LocaleIds.Value = array;

                _WriterGroupState.MaxNetworkMessageSize = new PropertyState<uint>(_WriterGroupState);
                _WriterGroupState.MaxNetworkMessageSize.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + ".MaxNetworkMessageSize", 2), new QualifiedName("MaxNetworkMessageSize"), new LocalizedText("MaxNetworkMessageSize"), false);
                _WriterGroupState.MaxNetworkMessageSize.Value = configuration.MaxNetworkMessageSize;

                _WriterGroupState.Priority = new PropertyState<byte>(_WriterGroupState);
                _WriterGroupState.Priority.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + ".Priority", 2), new QualifiedName("Priority"), new LocalizedText("Priority"), false);
                _WriterGroupState.Priority.Value = configuration.Priority;

                _WriterGroupState.PublishingInterval = new PropertyState<double>(_WriterGroupState);
                _WriterGroupState.PublishingInterval.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + ".PublishingInterval", 2), new QualifiedName("PublishingInterval"), new LocalizedText("PublishingInterval"), false);
                _WriterGroupState.PublishingInterval.Value = configuration.PublishingInterval;

                _WriterGroupState.SecurityGroupId = new PropertyState<string>(_WriterGroupState);
                _WriterGroupState.SecurityGroupId.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + ".SecurityGroupId", 2), new QualifiedName("SecurityGroupId"), new LocalizedText("SecurityGroupId"), false);
                _WriterGroupState.SecurityGroupId.Value = configuration.SecurityGroupId;

                _WriterGroupState.SecurityMode = new PropertyState<MessageSecurityMode>(_WriterGroupState);
                _WriterGroupState.SecurityMode.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + ".SecurityMode", 2), new QualifiedName("SecurityMode"), new LocalizedText("SecurityMode"), false);
                _WriterGroupState.SecurityMode.Value = configuration.SecurityMode;

                _WriterGroupState.SecurityKeyServices = new PropertyState<EndpointDescription[]>(_WriterGroupState);
                _WriterGroupState.SecurityKeyServices.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + ".SecurityKeyServices", 2), new QualifiedName("SecurityKeyServices"), new LocalizedText("SecurityKeyServices"), false);
                //EndpointDescription[] keyservicearray = new EndpointDescription[configuration.SecurityKeyServices.Count];
                //int j = 0;
                //foreach (EndpointDescription keyservice in configuration.SecurityKeyServices)
                //{
                //    keyservicearray[j] = keyservice;
                //    j++;
                //}
                _WriterGroupState.SecurityKeyServices.Value = configuration.SecurityKeyServices.ToArray();

                _WriterGroupState.WriterGroupId = new PropertyState<ushort>(_WriterGroupState);
                _WriterGroupState.WriterGroupId.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + ".WriterGroupId", 2), new QualifiedName("WriterGroupId"), new LocalizedText("WriterGroupId"), false);
                _WriterGroupState.WriterGroupId.Value = configuration.WriterGroupId;

                ExtensionObject WriterTransportobject = configuration.TransportSettings;
                if (WriterTransportobject.Body is DatagramWriterGroupTransportDataType)
                {
                    DatagramWriterGroupTransportDataType DatagramWriterTransport = WriterTransportobject.Body as DatagramWriterGroupTransportDataType;
                    DatagramWriterGroupTransportState _DatagramWriterGroupTransportState = new DatagramWriterGroupTransportState(_WriterGroupState);
                    _DatagramWriterGroupTransportState.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + "TransportSettings", 2), new QualifiedName("TransportSettings"), new LocalizedText("TransportSettings"), false);

                    _DatagramWriterGroupTransportState.MessageRepeatCount = new PropertyState<byte>(_DatagramWriterGroupTransportState);
                    _DatagramWriterGroupTransportState.MessageRepeatCount.Create(context, new NodeId(_DatagramWriterGroupTransportState.NodeId.Identifier + ".MessageRepeatCount", 2), new QualifiedName("MessageRepeatCount"), new LocalizedText("MessageRepeatCount"), false);
                    _DatagramWriterGroupTransportState.MessageRepeatCount.Value = DatagramWriterTransport.MessageRepeatCount;

                    _DatagramWriterGroupTransportState.MessageRepeatDelay = new PropertyState<double>(_DatagramWriterGroupTransportState);
                    _DatagramWriterGroupTransportState.MessageRepeatDelay.Create(context, new NodeId(_DatagramWriterGroupTransportState.NodeId.Identifier + ".MessageRepeatDelay", 2), new QualifiedName("MessageRepeatDelay"), new LocalizedText("MessageRepeatDelay"), false);
                    _DatagramWriterGroupTransportState.MessageRepeatDelay.Value = DatagramWriterTransport.MessageRepeatCount;

                    _WriterGroupState.TransportSettings = _DatagramWriterGroupTransportState;

                }
                else if (WriterTransportobject.Body is BrokerWriterGroupTransportDataType)
                {
                    BrokerWriterGroupTransportDataType BrokerWriterTransport = WriterTransportobject.Body as BrokerWriterGroupTransportDataType;
                    BrokerWriterGroupTransportState _BrokerWriterGroupTransportState = new BrokerWriterGroupTransportState(_WriterGroupState);
                    _BrokerWriterGroupTransportState.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + "TransportSettings", 2), new QualifiedName("TransportSettings"), new LocalizedText("TransportSettings"), false);

                    _BrokerWriterGroupTransportState.AuthenticationProfileUri = new PropertyState<string>(_BrokerWriterGroupTransportState);
                    _BrokerWriterGroupTransportState.AuthenticationProfileUri.Create(context, new NodeId(_BrokerWriterGroupTransportState.NodeId.Identifier + ".AuthenticationProfileUri", 2), new QualifiedName("AuthenticationProfileUri"), new LocalizedText("AuthenticationProfileUri"), false);
                    _BrokerWriterGroupTransportState.AuthenticationProfileUri.Value = BrokerWriterTransport.AuthenticationProfileUri;

                    _BrokerWriterGroupTransportState.QueueName = new PropertyState<string>(_BrokerWriterGroupTransportState);
                    _BrokerWriterGroupTransportState.QueueName.Create(context, new NodeId(_BrokerWriterGroupTransportState.NodeId.Identifier + ".QueueName", 2), new QualifiedName("QueueName"), new LocalizedText("QueueName"), false);
                    _BrokerWriterGroupTransportState.QueueName.Value = BrokerWriterTransport.QueueName;

                    _BrokerWriterGroupTransportState.ResourceUri = new PropertyState<string>(_BrokerWriterGroupTransportState);
                    _BrokerWriterGroupTransportState.ResourceUri.Create(context, new NodeId(_BrokerWriterGroupTransportState.NodeId.Identifier + ".ResourceUri", 2), new QualifiedName("ResourceUri"), new LocalizedText("ResourceUri"), false);
                    _BrokerWriterGroupTransportState.ResourceUri.Value = BrokerWriterTransport.ResourceUri;

                    _BrokerWriterGroupTransportState.RequestedDeliveryGuarantee = new PropertyState<BrokerTransportQualityOfService>(_BrokerWriterGroupTransportState);
                    _BrokerWriterGroupTransportState.RequestedDeliveryGuarantee.Create(context, new NodeId(_BrokerWriterGroupTransportState.NodeId.Identifier + ".RequestedDeliveryGuarantee", 2), new QualifiedName("RequestedDeliveryGuarantee"), new LocalizedText("RequestedDeliveryGuarantee"), false);
                    _BrokerWriterGroupTransportState.RequestedDeliveryGuarantee.Value = BrokerWriterTransport.RequestedDeliveryGuarantee;

                    _WriterGroupState.TransportSettings = _BrokerWriterGroupTransportState;
                }


                ExtensionObject WriterMessageobject = configuration.MessageSettings;
                if (WriterMessageobject.Body is UadpWriterGroupMessageDataType)
                {
                    UadpWriterGroupMessageDataType _UadpWriterGroupMessageDataType = WriterMessageobject.Body as UadpWriterGroupMessageDataType;
                    UadpWriterGroupMessageState _UadpWriterGroupMessageState = new UadpWriterGroupMessageState(_WriterGroupState);
                    _UadpWriterGroupMessageState.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + "MessageSettings", 2), new QualifiedName("MessageSettings"), new LocalizedText("MessageSettings"), false);

                    _UadpWriterGroupMessageState.NetworkMessageContentMask = new PropertyState<uint>(_UadpWriterGroupMessageState);
                    _UadpWriterGroupMessageState.NetworkMessageContentMask.Create(context, new NodeId(_UadpWriterGroupMessageState.NodeId.Identifier + ".NetworkMessageContentMask", 2), new QualifiedName("NetworkMessageContentMask"), new LocalizedText("NetworkMessageContentMask"), false);
                    _UadpWriterGroupMessageState.NetworkMessageContentMask.Value = _UadpWriterGroupMessageDataType.NetworkMessageContentMask;

                    _UadpWriterGroupMessageState.GroupVersion = new PropertyState<uint>(_UadpWriterGroupMessageState);
                    _UadpWriterGroupMessageState.GroupVersion.Create(context, new NodeId(_UadpWriterGroupMessageState.NodeId.Identifier + ".GroupVersion", 2), new QualifiedName("GroupVersion"), new LocalizedText("GroupVersion"), false);
                    _UadpWriterGroupMessageState.GroupVersion.Value = _UadpWriterGroupMessageDataType.GroupVersion;

                    _UadpWriterGroupMessageState.DataSetOrdering = new PropertyState<DataSetOrderingType>(_UadpWriterGroupMessageState);
                    _UadpWriterGroupMessageState.DataSetOrdering.Create(context, new NodeId(_UadpWriterGroupMessageState.NodeId.Identifier + ".DataSetOrdering", 2), new QualifiedName("DataSetOrdering"), new LocalizedText("DataSetOrdering"), false);
                    _UadpWriterGroupMessageState.DataSetOrdering.Value = _UadpWriterGroupMessageDataType.DataSetOrdering;

                    _UadpWriterGroupMessageState.PublishingOffset = new PropertyState<double>(_UadpWriterGroupMessageState);
                    _UadpWriterGroupMessageState.PublishingOffset.Create(context, new NodeId(_UadpWriterGroupMessageState.NodeId.Identifier + ".PublishingOffset", 2), new QualifiedName("PublishingOffset"), new LocalizedText("PublishingOffset"), false);
                     _UadpWriterGroupMessageState.PublishingOffset.Value = _UadpWriterGroupMessageDataType.PublishingOffset[0];

                    _UadpWriterGroupMessageState.SamplingOffset = new PropertyState<double>(_UadpWriterGroupMessageState);
                    _UadpWriterGroupMessageState.SamplingOffset.Create(context, new NodeId(_UadpWriterGroupMessageState.NodeId.Identifier + ".SamplingOffset", 2), new QualifiedName("SamplingOffset"), new LocalizedText("SamplingOffset"), false);
                    _UadpWriterGroupMessageState.SamplingOffset.Value = _UadpWriterGroupMessageDataType.SamplingOffset;

                    _WriterGroupState.MessageSettings = _UadpWriterGroupMessageState;

                }
                else if (WriterMessageobject.Body is JsonWriterGroupMessageDataType)
                {
                    JsonWriterGroupMessageDataType _JsonWriterGroupMessageDataType = WriterMessageobject.Body as JsonWriterGroupMessageDataType;
                    JsonWriterGroupMessageState _JsonWriterGroupMessageState = new JsonWriterGroupMessageState(_WriterGroupState);
                    _JsonWriterGroupMessageState.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + "MessageSettings", 2), new QualifiedName("MessageSettings"), new LocalizedText("MessageSettings"), false);

                    _JsonWriterGroupMessageState.NetworkMessageContentMask = new PropertyState<uint>(_JsonWriterGroupMessageState);
                    _JsonWriterGroupMessageState.NetworkMessageContentMask.Create(context, new NodeId(_JsonWriterGroupMessageState.NodeId.Identifier + ".NetworkMessageContentMask", 2), new QualifiedName("NetworkMessageContentMask"), new LocalizedText("NetworkMessageContentMask"), false);
                    _JsonWriterGroupMessageState.NetworkMessageContentMask.Value = _JsonWriterGroupMessageDataType.NetworkMessageContentMask;

                    _WriterGroupState.MessageSettings = _JsonWriterGroupMessageState;
                }

                _WriterGroupState.Status = new PubSubStatusState(_WriterGroupState);
                _WriterGroupState.Status.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + ".Status", _WriterGroupState.NodeId.NamespaceIndex), new QualifiedName("Status", _WriterGroupState.BrowseName.NamespaceIndex), new LocalizedText("Status"), false);

                _WriterGroupState.Status.State = new BaseDataVariableState<PubSubState>(_WriterGroupState.Status);
                _WriterGroupState.Status.State.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + ".Status.State", _WriterGroupState.NodeId.NamespaceIndex), new QualifiedName("State", _WriterGroupState.BrowseName.NamespaceIndex), new LocalizedText("State"), false);
                _WriterGroupState.Status.State.MinimumSamplingInterval = 100;
                _WriterGroupState.Status.Enable = new MethodState(_WriterGroupState.Status);
                _WriterGroupState.Status.Enable.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + ".Status" + ".Enable", 2), new QualifiedName("Enable"), new LocalizedText("Enable"), false);
                _WriterGroupState.Status.Enable.OnCallMethod += EnableMethodCalledEventHandler;
                _WriterGroupState.Status.AddReference(ReferenceTypeIds.HasComponent, false, _WriterGroupState.Status.Enable.NodeId);
                _WriterGroupState.Status.Disable = new MethodState(_WriterGroupState.Status);
                _WriterGroupState.Status.Disable.Create(context, new NodeId(_WriterGroupState.NodeId.Identifier + ".Status" + ".Disable", 2), new QualifiedName("Disable"), new LocalizedText("Disable"), false);
                _WriterGroupState.Status.Disable.OnCallMethod += DisableMethodCalledEventHandler;
                _WriterGroupState.Status.AddReference(ReferenceTypeIds.HasComponent, false, _WriterGroupState.Status.Disable.NodeId);
                _WriterGroupState.TypeDefinitionId = _WriterGroupState.GetDefaultTypeDefinitionId(context);
                groupId = _WriterGroupState.NodeId;
                _WriterGroupState.TypeDefinitionId = _WriterGroupState.GetDefaultTypeDefinitionId(context);
                method.Parent.AddChild(_WriterGroupState);
                AddPredefinedNode(context, _WriterGroupState);
                m_PubSubAdaptor.AddWriterGroup(_WriterGroupState);
                return ServiceResult.Good;
            }
        }
        #endregion
        ServiceResult PubSubConnectionTypeRemoveGroupMethodStateMethodCallHandler(ISystemContext context, MethodState method, NodeId objectId, NodeId groupId)
        {
            
            BaseInstanceState _RemoveGroupState = FindPredefinedNode(groupId,typeof(NodeState)) as BaseInstanceState;
            if (_RemoveGroupState != null)
            {
                m_PubSubAdaptor.RemoveGroup(_RemoveGroupState);
                method.Parent.RemoveChild(_RemoveGroupState);
               
                return ServiceResult.Good;
            }
            return new ServiceResult(StatusCodes.BadNotFound);
        }

        #endregion

        #region Reader Handlers
        ServiceResult PubSubGroupTypeAddReaderMethodStateMethodCallHandler(ISystemContext context, MethodState method, NodeId objectId, DataSetReaderDataType configuration, ref NodeId dataSetReaderNodeId)
        {
            DataSetReaderState existedChild = method.Parent.FindChild(context, new QualifiedName(configuration.Name, 2)) as DataSetReaderState;
            if (existedChild != null)
            {
                return new ServiceResult(StatusCodes.BadBrowseNameDuplicated);
            }
            else
            {
                DataSetReaderState _ReaderState = new DataSetReaderState(method.Parent);
                _ReaderState.Create(context, new NodeId(method.Parent.NodeId.Identifier + "." + configuration.Name , 2), new QualifiedName(configuration.Name, 2), new LocalizedText(configuration.Name), false);
                _ReaderState.CreateTargetVariables.OnCall = DataSetReaderTypeCreateTargetVariablesMethodStateMethodCallHandler;
                _ReaderState.CreateDataSetMirror.OnCall = DataSetReaderTypeCreateDataSetMirrorMethodStateMethodCallHandler;
                
                _ReaderState.PublisherId = new PropertyState<string>(_ReaderState);
                _ReaderState.PublisherId.Create(context, new NodeId(_ReaderState.NodeId.Identifier + ".PublisherId", 2), new QualifiedName("PublisherId"), new LocalizedText("PublisherId"), false);
                _ReaderState.PublisherId.Value = configuration.PublisherId;

                _ReaderState.WriterGroupId = new PropertyState<ushort>(_ReaderState);
                _ReaderState.WriterGroupId.Create(context, new NodeId(_ReaderState.NodeId.Identifier + ".WriterGroupId", 2), new QualifiedName("WriterGroupId"), new LocalizedText("WriterGroupId"), false);
                _ReaderState.WriterGroupId.Value = configuration.WriterGroupId;

                _ReaderState.DataSetWriterId = new PropertyState<ushort>(_ReaderState);
                _ReaderState.DataSetWriterId.Create(context, new NodeId(_ReaderState.NodeId.Identifier + ".DataSetWriterId", 2), new QualifiedName("DataSetWriterId"), new LocalizedText("DataSetWriterId"), false);
                _ReaderState.DataSetWriterId.Value = configuration.DataSetWriterId;

                _ReaderState.DataSetFieldContentMask = new PropertyState<uint>(_ReaderState);
                _ReaderState.DataSetFieldContentMask.Create(context, new NodeId(_ReaderState.NodeId.Identifier + ".DataSetFieldContentMask", 2), new QualifiedName("DataSetFieldContentMask"), new LocalizedText("DataSetFieldContentMask"), false);
                _ReaderState.DataSetFieldContentMask.Value = configuration.DataSetFieldContentMask;

                _ReaderState.MessageReceiveTimeout = new PropertyState<double>(_ReaderState);
                _ReaderState.MessageReceiveTimeout.Create(context, new NodeId(_ReaderState.NodeId.Identifier + ".MessageReceiveTimeout", 2), new QualifiedName("MessageReceiveTimeout"), new LocalizedText("MessageReceiveTimeout"), false);
                _ReaderState.MessageReceiveTimeout.Value = Convert.ToUInt16(configuration.MessageReceiveTimeout);

                _ReaderState.SecurityMode = new PropertyState<MessageSecurityMode>(_ReaderState);
                _ReaderState.SecurityMode.Create(context, new NodeId(_ReaderState.NodeId.Identifier + ".SecurityMode", 2), new QualifiedName("SecurityMode"), new LocalizedText("SecurityMode"), false);
                _ReaderState.SecurityMode.Value = configuration.SecurityMode;

                _ReaderState.SecurityGroupId = new PropertyState<string>(_ReaderState);
                _ReaderState.SecurityGroupId.Create(context, new NodeId(_ReaderState.NodeId.Identifier + ".SecurityGroupId", 2), new QualifiedName("SecurityGroupId"), new LocalizedText("SecurityGroupId"), false);
                _ReaderState.SecurityGroupId.Value = configuration.SecurityGroupId;

                _ReaderState.DataSetMetaData = new PropertyState<DataSetMetaDataType>(_ReaderState);
                _ReaderState.DataSetMetaData.Create(context, new NodeId(_ReaderState.NodeId.Identifier + ".DataSetMetaData", 2), new QualifiedName("DataSetMetaData"), new LocalizedText("DataSetMetaData"), false);
                _ReaderState.DataSetMetaData.Value = configuration.DataSetMetaData;


                ExtensionObject DatasetReaderTransportobject = configuration.TransportSettings;
                if (DatasetReaderTransportobject.Body is BrokerDataSetReaderTransportDataType)
                {
                    BrokerDataSetReaderTransportDataType _BrokerDataSetReaderTransportDataType = DatasetReaderTransportobject.Body as BrokerDataSetReaderTransportDataType;
                    BrokerDataSetReaderTransportState _BrokerDataSetReaderTransportState = new BrokerDataSetReaderTransportState(_ReaderState);
                    _BrokerDataSetReaderTransportState.Create(context, new NodeId(_ReaderState.NodeId.Identifier + "TransportSettings", 2), new QualifiedName("TransportSettings"), new LocalizedText("TransportSettings"), false);

                    _BrokerDataSetReaderTransportState.QueueName = new PropertyState<string>(_BrokerDataSetReaderTransportState);
                    _BrokerDataSetReaderTransportState.QueueName.Create(context, new NodeId(_BrokerDataSetReaderTransportState.NodeId.Identifier + ".QueueName", 2), new QualifiedName("QueueName"), new LocalizedText("QueueName"), false);
                    _BrokerDataSetReaderTransportState.QueueName.Value = _BrokerDataSetReaderTransportDataType.QueueName;

                    _BrokerDataSetReaderTransportState.ResourceUri = new PropertyState<string>(_BrokerDataSetReaderTransportState);
                    _BrokerDataSetReaderTransportState.ResourceUri.Create(context, new NodeId(_BrokerDataSetReaderTransportState.NodeId.Identifier + ".ResourceUri", 2), new QualifiedName("ResourceUri"), new LocalizedText("ResourceUri"), false);
                    _BrokerDataSetReaderTransportState.ResourceUri.Value = _BrokerDataSetReaderTransportDataType.ResourceUri;

                    _BrokerDataSetReaderTransportState.AuthenticationProfileUri = new PropertyState<string>(_BrokerDataSetReaderTransportState);
                    _BrokerDataSetReaderTransportState.AuthenticationProfileUri.Create(context, new NodeId(_BrokerDataSetReaderTransportState.NodeId.Identifier + ".AuthenticationProfileUri", 2), new QualifiedName("AuthenticationProfileUri"), new LocalizedText("AuthenticationProfileUri"), false);
                    _BrokerDataSetReaderTransportState.AuthenticationProfileUri.Value = _BrokerDataSetReaderTransportDataType.QueueName;

                    _BrokerDataSetReaderTransportState.RequestedDeliveryGuarantee = new PropertyState<BrokerTransportQualityOfService>(_BrokerDataSetReaderTransportState);
                    _BrokerDataSetReaderTransportState.RequestedDeliveryGuarantee.Create(context, new NodeId(_BrokerDataSetReaderTransportState.NodeId.Identifier + ".QueueName", 2), new QualifiedName("QueueName"), new LocalizedText("QueueName"), false);
                    _BrokerDataSetReaderTransportState.RequestedDeliveryGuarantee.Value = _BrokerDataSetReaderTransportDataType.RequestedDeliveryGuarantee;

                    _BrokerDataSetReaderTransportState.MetaDataQueueName = new PropertyState<string>(_BrokerDataSetReaderTransportState);
                    _BrokerDataSetReaderTransportState.MetaDataQueueName.Create(context, new NodeId(_BrokerDataSetReaderTransportState.NodeId.Identifier + ".MetaDataQueueName", 2), new QualifiedName("MetaDataQueueName"), new LocalizedText("MetaDataQueueName"), false);
                    _BrokerDataSetReaderTransportState.MetaDataQueueName.Value = _BrokerDataSetReaderTransportDataType.MetaDataQueueName;

                    _ReaderState.TransportSettings = _BrokerDataSetReaderTransportState;

                }
                ExtensionObject DatasetReaderMessageobject = configuration.MessageSettings;
                if (DatasetReaderMessageobject.Body is UadpDataSetReaderMessageDataType)
                {
                    UadpDataSetReaderMessageDataType _UadpDataSetReaderMessageDataType = DatasetReaderMessageobject.Body as UadpDataSetReaderMessageDataType;
                    UadpDataSetReaderMessageState _UadpDataSetReaderMessageState = new UadpDataSetReaderMessageState(_ReaderState);
                    _UadpDataSetReaderMessageState.Create(context, new NodeId(_ReaderState.NodeId.Identifier + ".MessageSettings", 2), new QualifiedName("MessageSettings"), new LocalizedText("MessageSettings"), false);

                    _UadpDataSetReaderMessageState.DataSetMessageContentMask = new PropertyState<uint>(_UadpDataSetReaderMessageState);
                    _UadpDataSetReaderMessageState.DataSetMessageContentMask.Create(context, new NodeId(_UadpDataSetReaderMessageState.NodeId.Identifier + ".DataSetMessageContentMask", 2), new QualifiedName("DataSetMessageContentMask"), new LocalizedText("DataSetMessageContentMask"), false);
                    _UadpDataSetReaderMessageState.DataSetMessageContentMask.Value = _UadpDataSetReaderMessageDataType.DataSetMessageContentMask;

                    _UadpDataSetReaderMessageState.NetworkMessageContentMask = new PropertyState<uint>(_UadpDataSetReaderMessageState);
                    _UadpDataSetReaderMessageState.NetworkMessageContentMask.Create(context, new NodeId(_UadpDataSetReaderMessageState.NodeId.Identifier + ".NetworkMessageContentMask", 2), new QualifiedName("NetworkMessageContentMask"), new LocalizedText("NetworkMessageContentMask"), false);
                    _UadpDataSetReaderMessageState.NetworkMessageContentMask.Value = _UadpDataSetReaderMessageDataType.NetworkMessageContentMask;

                    _UadpDataSetReaderMessageState.GroupVersion = new PropertyState<uint>(_UadpDataSetReaderMessageState);
                    _UadpDataSetReaderMessageState.GroupVersion.Create(context, new NodeId(_UadpDataSetReaderMessageState.NodeId.Identifier + ".GroupVersion", 2), new QualifiedName("GroupVersion"), new LocalizedText("GroupVersion"), false);
                    _UadpDataSetReaderMessageState.GroupVersion.Value = _UadpDataSetReaderMessageDataType.GroupVersion;

                    _UadpDataSetReaderMessageState.DataSetClassId = new PropertyState<Guid>(_UadpDataSetReaderMessageState);
                    _UadpDataSetReaderMessageState.DataSetClassId.Create(context, new NodeId(_UadpDataSetReaderMessageState.NodeId.Identifier + ".DataSetClassId", 2), new QualifiedName("DataSetClassId"), new LocalizedText("DataSetClassId"), false);
                    _UadpDataSetReaderMessageState.DataSetClassId.Value = _UadpDataSetReaderMessageDataType.DataSetClassId;

                    _UadpDataSetReaderMessageState.DataSetOffset = new PropertyState<ushort>(_UadpDataSetReaderMessageState);
                    _UadpDataSetReaderMessageState.DataSetOffset.Create(context, new NodeId(_UadpDataSetReaderMessageState.NodeId.Identifier + ".DataSetOffset", 2), new QualifiedName("DataSetOffset"), new LocalizedText("DataSetOffset"), false);
                    _UadpDataSetReaderMessageState.DataSetOffset.Value = _UadpDataSetReaderMessageDataType.DataSetOffset;

                    _UadpDataSetReaderMessageState.ProcessingOffset = new PropertyState<double>(_UadpDataSetReaderMessageState);
                    _UadpDataSetReaderMessageState.ProcessingOffset.Create(context, new NodeId(_UadpDataSetReaderMessageState.NodeId.Identifier + ".ProcessingOffset", 2), new QualifiedName("ProcessingOffset"), new LocalizedText("ProcessingOffset"), false);
                    _UadpDataSetReaderMessageState.ProcessingOffset.Value = _UadpDataSetReaderMessageDataType.ProcessingOffset;

                    _UadpDataSetReaderMessageState.PublishingInterval = new PropertyState<double>(_UadpDataSetReaderMessageState);
                    _UadpDataSetReaderMessageState.PublishingInterval.Create(context, new NodeId(_UadpDataSetReaderMessageState.NodeId.Identifier + ".PublishingInterval", 2), new QualifiedName("PublishingInterval"), new LocalizedText("PublishingInterval"), false);
                    _UadpDataSetReaderMessageState.PublishingInterval.Value = _UadpDataSetReaderMessageDataType.PublishingInterval;

                    _UadpDataSetReaderMessageState.NetworkMessageNumber = new PropertyState<ushort>(_UadpDataSetReaderMessageState);
                    _UadpDataSetReaderMessageState.NetworkMessageNumber.Create(context, new NodeId(_UadpDataSetReaderMessageState.NodeId.Identifier + ".NetworkMessageNumber", 2), new QualifiedName("NetworkMessageNumber"), new LocalizedText("NetworkMessageNumber"), false);
                    _UadpDataSetReaderMessageState.NetworkMessageNumber.Value = _UadpDataSetReaderMessageDataType.NetworkMessageNumber;

                    _UadpDataSetReaderMessageState.ReceiveOffset = new PropertyState<double>(_UadpDataSetReaderMessageState);
                    _UadpDataSetReaderMessageState.ReceiveOffset.Create(context, new NodeId(_UadpDataSetReaderMessageState.NodeId.Identifier + ".ReceiveOffset", 2), new QualifiedName("ReceiveOffset"), new LocalizedText("ReceiveOffset"), false);
                    _UadpDataSetReaderMessageState.ReceiveOffset.Value = _UadpDataSetReaderMessageDataType.PublishingInterval;

                    _ReaderState.MessageSettings = _UadpDataSetReaderMessageState;
                }
                else if (DatasetReaderMessageobject.Body is JsonDataSetReaderMessageDataType)
                {
                    JsonDataSetReaderMessageDataType _JsonDataSetReaderMessageDataType = DatasetReaderMessageobject.Body as JsonDataSetReaderMessageDataType;
                    JsonDataSetReaderMessageState _JsonDataSetReaderMessageState = new JsonDataSetReaderMessageState(_ReaderState);
                    _JsonDataSetReaderMessageState.Create(context, new NodeId(_ReaderState.NodeId.Identifier + ".MessageSettings", 2), new QualifiedName("MessageSettings"), new LocalizedText("MessageSettings"), false);

                    _JsonDataSetReaderMessageState.DataSetMessageContentMask = new PropertyState<uint>(_JsonDataSetReaderMessageState);
                    _JsonDataSetReaderMessageState.DataSetMessageContentMask.Create(context, new NodeId(_JsonDataSetReaderMessageState.NodeId.Identifier + ".DataSetMessageContentMask", 2), new QualifiedName("DataSetMessageContentMask"), new LocalizedText("DataSetMessageContentMask"), false);
                    _JsonDataSetReaderMessageState.DataSetMessageContentMask.Value = _JsonDataSetReaderMessageDataType.DataSetMessageContentMask;

                    _JsonDataSetReaderMessageState.NetworkMessageContentMask = new PropertyState<uint>(_JsonDataSetReaderMessageState);
                    _JsonDataSetReaderMessageState.NetworkMessageContentMask.Create(context, new NodeId(_JsonDataSetReaderMessageState.NodeId.Identifier + ".NetworkMessageContentMask", 2), new QualifiedName("NetworkMessageContentMask"), new LocalizedText("NetworkMessageContentMask"), false);
                    _JsonDataSetReaderMessageState.NetworkMessageContentMask.Value = _JsonDataSetReaderMessageDataType.NetworkMessageContentMask;

                    _ReaderState.MessageSettings = _JsonDataSetReaderMessageState;
                }

                _ReaderState.Status = new PubSubStatusState(_ReaderState);
                _ReaderState.Status.Create(context, new NodeId(_ReaderState.NodeId.Identifier + ".Status", _ReaderState.NodeId.NamespaceIndex), new QualifiedName("Status", _ReaderState.BrowseName.NamespaceIndex), new LocalizedText("Status"), false);

                _ReaderState.Status.State = new BaseDataVariableState<PubSubState>(_ReaderState.Status);
                _ReaderState.Status.State.Create(context, new NodeId(_ReaderState.NodeId.Identifier + ".Status.State", _ReaderState.NodeId.NamespaceIndex), new QualifiedName("State", _ReaderState.BrowseName.NamespaceIndex), new LocalizedText("State"), false);
                _ReaderState.Status.State.MinimumSamplingInterval = 100;

                _ReaderState.Status.Enable = new MethodState(_ReaderState.Status);
                _ReaderState.Status.Enable.Create(context, new NodeId(_ReaderState.NodeId.Identifier + ".Status" + ".Enable", 2), new QualifiedName("Enable"), new LocalizedText("Enable"), false);
                _ReaderState.Status.Enable.OnCallMethod += EnableMethodCalledEventHandler;
                _ReaderState.Status.AddReference(ReferenceTypeIds.HasComponent, false, _ReaderState.Status.Enable.NodeId);

                _ReaderState.Status.Disable = new MethodState(_ReaderState.Status);
                _ReaderState.Status.Disable.Create(context, new NodeId(_ReaderState.NodeId.Identifier + ".Status" + ".Disable", 2), new QualifiedName("Disable"), new LocalizedText("Disable"), false);
                _ReaderState.Status.Disable.OnCallMethod += DisableMethodCalledEventHandler;
                _ReaderState.Status.AddReference(ReferenceTypeIds.HasComponent, false, _ReaderState.Status.Disable.NodeId);
                _ReaderState.TypeDefinitionId = _ReaderState.GetDefaultTypeDefinitionId(context);

                dataSetReaderNodeId = new NodeId(method.Parent.NodeId.Identifier + "." + configuration.Name , 2);
                method.Parent.AddChild(_ReaderState);
                AddPredefinedNode(context, _ReaderState);
                m_PubSubAdaptor.AddDataSetReader(_ReaderState, m_subscriberDelegate);
            }
            return ServiceResult.Good;
        }
        ServiceResult PubSubGroupTypeRemoveReaderMethodStateMethodCallHandler(ISystemContext context, MethodState method, NodeId objectId, NodeId dataSetReaderNodeId)
        {
            DataSetReaderState _RemoveConnectionState = FindPredefinedNode(dataSetReaderNodeId, typeof(NodeState)) as DataSetReaderState;
            if (_RemoveConnectionState != null)
            {
                
                method.Parent.RemoveChild(_RemoveConnectionState);
               
                return ServiceResult.Good;
            }
            return new ServiceResult(StatusCodes.BadNotFound);
        }


        #endregion 

        #region Writer Handlers
        ServiceResult PubSubGroupTypeAddWriterrMethodStateMethodCallHandler(ISystemContext context, MethodState method, NodeId objectId, DataSetWriterDataType configuration, ref NodeId dataSetWriterNodeId)
        {
            DataSetWriterState existedChild = method.Parent.FindChild(context, new QualifiedName(configuration.Name, 2)) as DataSetWriterState;
            if (existedChild != null)
            {
                return new ServiceResult(StatusCodes.BadBrowseNameDuplicated);
            }
            else
            {
                DataSetWriterState _WriterState = new DataSetWriterState(method.Parent);
                _WriterState.Create(context, new NodeId(method.Parent.NodeId.Identifier + "." + configuration.Name, 2), new QualifiedName(configuration.Name, 2), new LocalizedText(configuration.Name), false);

                _WriterState.DataSetFieldContentMask = new PropertyState<uint>(_WriterState);
                _WriterState.DataSetFieldContentMask.Create(context, new NodeId(_WriterState.NodeId.Identifier + ".DataSetFieldContentMask", 2), new QualifiedName("DataSetFieldContentMask"), new LocalizedText("DataSetFieldContentMask"), false);
                _WriterState.DataSetFieldContentMask.Value = configuration.DataSetFieldContentMask;

                _WriterState.KeyFrameCount = new PropertyState<uint>(_WriterState);
                _WriterState.KeyFrameCount.Create(context, new NodeId(_WriterState.NodeId.Identifier + ".KeyFrameCount", 2), new QualifiedName("KeyFrameCount"), new LocalizedText("KeyFrameCount"), false);
                _WriterState.KeyFrameCount.Value = configuration.KeyFrameCount;

                _WriterState.DataSetWriterId = new PropertyState<ushort>(_WriterState);
                _WriterState.DataSetWriterId.Create(context, new NodeId(_WriterState.NodeId.Identifier + ".DataSetWriterId", 2), new QualifiedName("DataSetWriterId"), new LocalizedText("DataSetWriterId"), false);
                _WriterState.DataSetWriterId.Value = configuration.DataSetWriterId;
                DataSetFolderState dataSetFolderState = (DataSetFolderState)Server.DiagnosticsNodeManager.FindPredefinedNode(ObjectIds.PublishSubscribe_PublishedDataSets, typeof(DataSetFolderState));
                List<BaseInstanceState> LstChildren = new List<BaseInstanceState>();
                dataSetFolderState.GetChildren(context, LstChildren);
                foreach(BaseInstanceState state in LstChildren)
                {
                    if(state.DisplayName.Text== configuration.DataSetName)
                    {
                        _WriterState.AddReference(ReferenceTypeIds.DataSetToWriter, true, state.NodeId);
                        _WriterState.Handle = state as PublishedDataItemsState;
                        break;
                    }
                    //    
                }
                
                ExtensionObject WriterTransportobject = configuration.TransportSettings;
                if (WriterTransportobject.Body is BrokerDataSetWriterTransportDataType)
                {
                    BrokerDataSetWriterTransportDataType _BrokerDataSetWriterTransportDataType = WriterTransportobject.Body as BrokerDataSetWriterTransportDataType;
                    BrokerDataSetWriterTransportState _BrokerDataSetWriterTransportState = new BrokerDataSetWriterTransportState(_WriterState);
                    _BrokerDataSetWriterTransportState.Create(context, new NodeId(_WriterState.NodeId.Identifier + "TransportSettings", 2), new QualifiedName("TransportSettings"), new LocalizedText("TransportSettings"), false);

                    _BrokerDataSetWriterTransportState.AuthenticationProfileUri = new PropertyState<string>(_BrokerDataSetWriterTransportState);
                    _BrokerDataSetWriterTransportState.AuthenticationProfileUri.Create(context, new NodeId(_BrokerDataSetWriterTransportState.NodeId.Identifier + ".AuthenticationProfileUri", 2), new QualifiedName("AuthenticationProfileUri"), new LocalizedText("AuthenticationProfileUri"), false);
                    _BrokerDataSetWriterTransportState.AuthenticationProfileUri.Value = _BrokerDataSetWriterTransportDataType.AuthenticationProfileUri;

                    _BrokerDataSetWriterTransportState.ResourceUri = new PropertyState<string>(_BrokerDataSetWriterTransportState);
                    _BrokerDataSetWriterTransportState.ResourceUri.Create(context, new NodeId(_BrokerDataSetWriterTransportState.NodeId.Identifier + ".ResourceUri", 2), new QualifiedName("ResourceUri"), new LocalizedText("ResourceUri"), false);
                    _BrokerDataSetWriterTransportState.ResourceUri.Value = _BrokerDataSetWriterTransportDataType.ResourceUri;

                    _BrokerDataSetWriterTransportState.QueueName = new PropertyState<string>(_BrokerDataSetWriterTransportState);
                    _BrokerDataSetWriterTransportState.QueueName.Create(context, new NodeId(_BrokerDataSetWriterTransportState.NodeId.Identifier + ".QueueName", 2), new QualifiedName("QueueName"), new LocalizedText("QueueName"), false);
                    _BrokerDataSetWriterTransportState.QueueName.Value = _BrokerDataSetWriterTransportDataType.QueueName;

                    _BrokerDataSetWriterTransportState.MetaDataQueueName = new PropertyState<string>(_BrokerDataSetWriterTransportState);
                    _BrokerDataSetWriterTransportState.MetaDataQueueName.Create(context, new NodeId(_BrokerDataSetWriterTransportState.NodeId.Identifier + ".MetaDataQueueName", 2), new QualifiedName("MetaDataQueueName"), new LocalizedText("MetaDataQueueName"), false);
                    _BrokerDataSetWriterTransportState.MetaDataQueueName.Value = _BrokerDataSetWriterTransportDataType.MetaDataQueueName;

                    _BrokerDataSetWriterTransportState.MetaDataUpdateTime = new PropertyState<double>(_BrokerDataSetWriterTransportState);
                    _BrokerDataSetWriterTransportState.MetaDataUpdateTime.Create(context, new NodeId(_BrokerDataSetWriterTransportState.NodeId.Identifier + ".MetaDataUpdateTime", 2), new QualifiedName("MetaDataUpdateTime"), new LocalizedText("MetaDataUpdateTime"), false);
                    _BrokerDataSetWriterTransportState.MetaDataUpdateTime.Value = _BrokerDataSetWriterTransportDataType.MetaDataUpdateTime;
                    _BrokerDataSetWriterTransportState.RequestedDeliveryGuarantee = new PropertyState<BrokerTransportQualityOfService>(_BrokerDataSetWriterTransportState);
                    _BrokerDataSetWriterTransportState.RequestedDeliveryGuarantee.Create(context, new NodeId(_BrokerDataSetWriterTransportState.NodeId.Identifier + ".RequestedDeliveryGuarantee", 2), new QualifiedName("RequestedDeliveryGuarantee"), new LocalizedText("RequestedDeliveryGuarantee"), false);
                    _BrokerDataSetWriterTransportState.RequestedDeliveryGuarantee.Value = _BrokerDataSetWriterTransportDataType.RequestedDeliveryGuarantee;

                    _WriterState.TransportSettings = _BrokerDataSetWriterTransportState;

                }

                ExtensionObject WriterMessageobject = configuration.MessageSettings;
                if (WriterMessageobject.Body is UadpDataSetWriterMessageDataType)
                {
                    UadpDataSetWriterMessageDataType _UadpDataSetWriterMessageDataType = WriterMessageobject.Body as UadpDataSetWriterMessageDataType;
                    UadpDataSetWriterMessageState _UadpDataSetWriterMessageState = new UadpDataSetWriterMessageState(_WriterState);
                    _UadpDataSetWriterMessageState.Create(context, new NodeId(_WriterState.NodeId.Identifier + ".MessageSettings", 2), new QualifiedName("MessageSettings"), new LocalizedText("MessageSettings"), false);

                    _UadpDataSetWriterMessageState.ConfiguredSize = new PropertyState<ushort>(_UadpDataSetWriterMessageState);
                    _UadpDataSetWriterMessageState.ConfiguredSize.Create(context, new NodeId(_UadpDataSetWriterMessageState.NodeId.Identifier + ".ConfiguredSize", 2), new QualifiedName("ConfiguredSize"), new LocalizedText("ConfiguredSize"), false);
                    _UadpDataSetWriterMessageState.ConfiguredSize.Value = _UadpDataSetWriterMessageDataType.ConfiguredSize;

                    _UadpDataSetWriterMessageState.DataSetOffset = new PropertyState<ushort>(_UadpDataSetWriterMessageState);
                    _UadpDataSetWriterMessageState.DataSetOffset.Create(context, new NodeId(_UadpDataSetWriterMessageState.NodeId.Identifier + ".DataSetOffset", 2), new QualifiedName("DataSetOffset"), new LocalizedText("DataSetOffset"), false);
                    _UadpDataSetWriterMessageState.DataSetOffset.Value = _UadpDataSetWriterMessageDataType.ConfiguredSize;

                    _UadpDataSetWriterMessageState.NetworkMessageNumber = new PropertyState<ushort>(_UadpDataSetWriterMessageState);
                    _UadpDataSetWriterMessageState.NetworkMessageNumber.Create(context, new NodeId(_UadpDataSetWriterMessageState.NodeId.Identifier + ".NetworkMessageNumber", 2), new QualifiedName("NetworkMessageNumber"), new LocalizedText("NetworkMessageNumber"), false);
                    _UadpDataSetWriterMessageState.NetworkMessageNumber.Value = _UadpDataSetWriterMessageDataType.NetworkMessageNumber;

                    _UadpDataSetWriterMessageState.DataSetMessageContentMask = new PropertyState<uint>(_UadpDataSetWriterMessageState);
                    _UadpDataSetWriterMessageState.DataSetMessageContentMask.Create(context, new NodeId(_UadpDataSetWriterMessageState.NodeId.Identifier + ".DataSetMessageContentMask", 2), new QualifiedName("DataSetMessageContentMask"), new LocalizedText("DataSetMessageContentMask"), false);
                    _UadpDataSetWriterMessageState.DataSetMessageContentMask.Value = _UadpDataSetWriterMessageDataType.DataSetMessageContentMask;
                    _UadpDataSetWriterMessageState.TypeDefinitionId = _UadpDataSetWriterMessageState.GetDefaultTypeDefinitionId(context);
                    _WriterState.MessageSettings = _UadpDataSetWriterMessageState;
                    _WriterState.MessageSettings.TypeDefinitionId = _UadpDataSetWriterMessageState.TypeDefinitionId;
                }
                else if (WriterMessageobject.Body is JsonDataSetWriterMessageDataType)
                {
                    JsonDataSetWriterMessageDataType _JsonDataSetWriterMessageDataType = WriterMessageobject.Body as JsonDataSetWriterMessageDataType;
                    JsonDataSetWriterMessageState _JsonDataSetWriterMessageState = new JsonDataSetWriterMessageState(_WriterState);
                    _JsonDataSetWriterMessageState.Create(context, new NodeId(_WriterState.NodeId.Identifier + ".MessageSettings", 2), new QualifiedName("MessageSettings"), new LocalizedText("MessageSettings"), false);

                    _JsonDataSetWriterMessageState.DataSetMessageContentMask = new PropertyState<uint>(_JsonDataSetWriterMessageState);
                    _JsonDataSetWriterMessageState.DataSetMessageContentMask.Create(context, new NodeId(_JsonDataSetWriterMessageState.NodeId.Identifier + ".DataSetMessageContentMask", 2), new QualifiedName("DataSetMessageContentMask"), new LocalizedText("DataSetMessageContentMask"), false);
                    _JsonDataSetWriterMessageState.DataSetMessageContentMask.Value = _JsonDataSetWriterMessageDataType.DataSetMessageContentMask;
                    _JsonDataSetWriterMessageState.TypeDefinitionId = _JsonDataSetWriterMessageState.GetDefaultTypeDefinitionId(context);
                    _WriterState.MessageSettings = _JsonDataSetWriterMessageState;
                    _WriterState.MessageSettings.TypeDefinitionId = _JsonDataSetWriterMessageState.TypeDefinitionId;
                }
                _WriterState.Status = new PubSubStatusState(_WriterState);
                _WriterState.Status.Create(context, new NodeId(_WriterState.NodeId.Identifier + ".Status", _WriterState.NodeId.NamespaceIndex), new QualifiedName("Status", _WriterState.BrowseName.NamespaceIndex), new LocalizedText("Status"), false);

                _WriterState.Status.State = new BaseDataVariableState<PubSubState>(_WriterState.Status);
                _WriterState.Status.State.Create(context, new NodeId(_WriterState.NodeId.Identifier + ".Status.State", _WriterState.NodeId.NamespaceIndex), new QualifiedName("State", _WriterState.BrowseName.NamespaceIndex), new LocalizedText("State"), false);
                _WriterState.Status.State.MinimumSamplingInterval = 100;
                _WriterState.Status.Enable = new MethodState(_WriterState.Status);
                _WriterState.Status.Enable.Create(context, new NodeId(_WriterState.NodeId.Identifier + ".Status" + ".Enable", _WriterState.NodeId.NamespaceIndex), new QualifiedName("Enable"), new LocalizedText("Enable"), false);
                _WriterState.Status.Enable.OnCallMethod += EnableMethodCalledEventHandler;
                _WriterState.Status.AddReference(ReferenceTypeIds.HasComponent, false, _WriterState.Status.Enable.NodeId);

                _WriterState.Status.Disable = new MethodState(_WriterState.Status);
                _WriterState.Status.Disable.Create(context, new NodeId(_WriterState.NodeId.Identifier + ".Status" + ".Disable", _WriterState.NodeId.NamespaceIndex), new QualifiedName("Disable"), new LocalizedText("Disable"), false);
                _WriterState.Status.Disable.OnCallMethod += DisableMethodCalledEventHandler;
                _WriterState.Status.AddReference(ReferenceTypeIds.HasComponent, false, _WriterState.Status.Disable.NodeId);
                _WriterState.TypeDefinitionId = _WriterState.GetDefaultTypeDefinitionId(context);
                dataSetWriterNodeId = _WriterState.NodeId;
                method.Parent.AddChild(_WriterState);
                AddPredefinedNode(context, _WriterState);
                m_PubSubAdaptor.AddDataSetWriter(_WriterState);
            }
            
            return ServiceResult.Good;
        }

        ServiceResult PubSubGroupTypeRemoveWriterMethodStateMethodCallHandler(ISystemContext context, MethodState method, NodeId objectId, NodeId dataSetWriterNodeId)
        {
            DataSetWriterState _DataSetWriterState = FindPredefinedNode(dataSetWriterNodeId,typeof(NodeState)) as DataSetWriterState;
            if (_DataSetWriterState != null)
            {
                m_PubSubAdaptor.RemoveDataSetWriter(_DataSetWriterState);
                method.Parent.RemoveChild(_DataSetWriterState);
                return ServiceResult.Good;
            }
            return new ServiceResult(StatusCodes.BadNotFound);
        }
        #endregion

        ServiceResult PublishedDataItemsAddVariablesMethodStateMethodCallHandler(ISystemContext context, MethodState method, NodeId objectId, ConfigurationVersionDataType configurationVersion, string[] fieldNameAliases, bool[] promotedFields, PublishedVariableDataType[] variablesToAdd, ref ConfigurationVersionDataType newConfigurationVersion, ref StatusCode[] addResults)
        {
          
            return ServiceResult.Good;
        }
        ServiceResult DataSetReaderTypeCreateTargetVariablesMethodStateMethodCallHandler(ISystemContext context, MethodState method, NodeId objectId, ConfigurationVersionDataType configurationVersion, FieldTargetDataType[] targetVariablesToAdd, ref StatusCode[] addResults)
        {
            DataSetReaderState dataSetReaderState = method.Parent as DataSetReaderState;
            if (dataSetReaderState != null)
            {
                 dataSetReaderState.SubscribedDataSet = new SubscribedDataSetState(dataSetReaderState);
                dataSetReaderState.SubscribedDataSet.Create(context, new NodeId(dataSetReaderState.NodeId.Identifier+ ".SubscribedDataSet", dataSetReaderState.NodeId.NamespaceIndex), new QualifiedName("SubscribedDataSet", dataSetReaderState.NodeId.NamespaceIndex), new LocalizedText("SubscribedDataSet"), false);
                dataSetReaderState.TypeDefinitionId = dataSetReaderState.SubscribedDataSet.GetDefaultTypeDefinitionId(context);
                TargetVariablesState targetVariablesState = new TargetVariablesState(dataSetReaderState.SubscribedDataSet);
                targetVariablesState.Create(Server.DefaultSystemContext, new NodeId(dataSetReaderState.SubscribedDataSet.NodeId.Identifier + ".TargetVariables"), new QualifiedName("TargetVariables", dataSetReaderState.SubscribedDataSet.NodeId.NamespaceIndex), new LocalizedText("TargetVariables"), false);
                targetVariablesState.TargetVariables = new PropertyState<FieldTargetDataType[]>(targetVariablesState);
                targetVariablesState.TargetVariables.Create(Server.DefaultSystemContext, new NodeId(dataSetReaderState.SubscribedDataSet.NodeId.Identifier + ".TargetVariables"), new QualifiedName("TargetVariables", dataSetReaderState.SubscribedDataSet.NodeId.NamespaceIndex), new LocalizedText("TargetVariables"), false);
                targetVariablesState.TargetVariables.Value = targetVariablesToAdd;
                targetVariablesState.AddTargetVariables = new TargetVariablesTypeAddTargetVariablesMethodState(dataSetReaderState.SubscribedDataSet);
                targetVariablesState.AddTargetVariables.Create(context, new NodeId(dataSetReaderState.SubscribedDataSet.NodeId.Identifier + ".AddTargetVariables", dataSetReaderState.SubscribedDataSet.NodeId.NamespaceIndex), new QualifiedName("AddTargetVariables", dataSetReaderState.SubscribedDataSet.NodeId.NamespaceIndex), new LocalizedText("AddTargetVariables"), false);
                // targetVariablesState.AddTargetVariables.OnCall += TargetVariablesTypeAddTargetVariablesMethodStateMethodCallHandler;
               // targetVariablesState.DataSetMetaData = dataSetReaderState.SubscribedDataSet.DataSetMetaData;
              //  targetVariablesState.MessageReceiveTimeout = dataSetReaderState.SubscribedDataSet.MessageReceiveTimeout;
                targetVariablesState.TypeDefinitionId = targetVariablesState.GetDefaultTypeDefinitionId(context);
                method.Parent.AddChild(targetVariablesState);
                AddPredefinedNode(context,targetVariablesState);
                
                ReaderGroupState readerGroupState= dataSetReaderState.Parent as ReaderGroupState;
                PubSubConnectionState pubSubConnectionState= readerGroupState.Parent as PubSubConnectionState;
                m_PubSubAdaptor.CreateTargetVariables(pubSubConnectionState.NodeId, dataSetReaderState.NodeId, targetVariablesToAdd);
            }
            return ServiceResult.Good;
        }
        //ServiceResult TargetVariablesTypeAddTargetVariablesMethodStateMethodCallHandler(
        //ISystemContext context,
        //MethodState method,
        //NodeId objectId,
        //ConfigurationVersionDataType configurationVersion,
        //FieldTargetDataType[] targetVariablesToAdd,
        //ref StatusCode[] addResults)
        //{
        //   // SubscribedDataSetState subscribedDataSet = method.Parent as SubscribedDataSetState;
        //    return ServiceResult.;
        //}
        ServiceResult DataSetReaderTypeCreateDataSetMirrorMethodStateMethodCallHandler(ISystemContext context, MethodState method, NodeId objectId, string parentNodeName, RolePermissionType[] rolePermissions, ref NodeId parentNodeId)
        {
            
            return ServiceResult.Good;
        }

        ServiceResult PubSubEnableMethodCalledEventHandler(
       ISystemContext context,
       MethodState method,
       IList<object> inputArguments,
       IList<object> outputArguments)
        {
            INodeManager nodeManager;
            NodeHandle nodehandle = Server.NodeManager.GetManagerHandle(Variables.PublishSubscribe_Status_State, out nodeManager) as NodeHandle;
            if (nodehandle != null)
            {
                BaseDataVariableState baseDataVariableState = nodehandle.Node as BaseDataVariableState;
                if (baseDataVariableState != null)
                {
                    baseDataVariableState.Value = PubSubState.Operational;
                }
            }
            return ServiceResult.Good;
        }

        #endregion



        #region Security Handlers 

        ServiceResult OnAddSecurityGroupMethodState(ISystemContext context, MethodState method, NodeId objectId, string securityGroupName, string securityGroupId, ref NodeId securityGroupNodeId)
        {
            throw new NotImplementedException();
        }
        ServiceResult OnRemoveSecurityGroupMethodState(ISystemContext context, MethodState method, NodeId objectId, NodeId securityGroupNodeId)
        {
            throw new NotImplementedException();
        }
        ServiceResult OnGetSecurityGroupMethodState(ISystemContext context, MethodState method, NodeId objectId, string securityGroupId, ref NodeId securityGroupNodeId)
        {
            throw new NotImplementedException();
        }
        ServiceResult OnGetSecurityKeysMethodState(ISystemContext context, MethodState method, NodeId objectId, string securityGroupId, uint futureKeyCount, ref string securityPolicyUri, ref uint currentTokenId, ref byte[] currentKey, ref byte[][] futureKeys, ref double timeToNextKey, ref double keyLifetime)
        {
            throw new NotImplementedException();
        }
        ServiceResult OnSetSecurityKeysMethodState(ISystemContext context, MethodState method, NodeId objectId, string securityGroupId, string securityPolicyUri, UInt32 currentTokenId, Byte[] currentKey, Byte[][] FeatureKeys, double timeToNextKey, double keyLifeTime)
        {
            throw new NotImplementedException();
        }
        
         
 
        #endregion

        #region Protected Members
        protected virtual IList<RolePermissionType> GetDefaultRolePermissions()//Thilak: Tobe verified from part 3 Spec
        {
            var rolePermissions = new RolePermissionType[]
            {
                new RolePermissionType()
                {
                    RoleId =Opc.Ua.PubSub.ObjectIds.WellKnownRole_Anonymous,
                    Permissions =(uint) (PermissionType.Browse | PermissionType.Read | PermissionType.Call | PermissionType.ReceiveEvents)
                },
                new RolePermissionType()
                {
                    RoleId = Opc.Ua.PubSub.ObjectIds.WellKnownRole_AuthenticatedUser,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Read | PermissionType.Call | PermissionType.ReceiveEvents)
                },
                  new RolePermissionType()
                {
                    RoleId = Opc.Ua.PubSub.ObjectIds.WellKnownRole_Observer,
                    Permissions =(uint) (PermissionType.Browse | PermissionType.Read |PermissionType.ReadHistory | PermissionType.Call | PermissionType.ReceiveEvents)
                },
                new RolePermissionType()
                {
                    RoleId = Opc.Ua.PubSub.ObjectIds.WellKnownRole_ConfigureAdmin,
                   // Permissions = (PermissionType.All)
                },
                new RolePermissionType()
                {
                    RoleId = Opc.Ua.PubSub.ObjectIds.WellKnownRole_SecurityAdmin,
                   // Permissions = (uint)(PermissionType.All)
                }
            };

            return rolePermissions;
        }


        /// <remarks />
        protected virtual ServiceResult ReadDefaultRolePermissions(ISystemContext context, NodeState node, ref object value)
        {
            var identity = context.UserIdentity as RoleBasedIdentity;

            if (identity != null)
            {
                foreach (var role in identity.Roles)
                {
                    if (role == Opc.Ua.PubSub.ObjectIds.WellKnownRole_SecurityAdmin || role == Opc.Ua.PubSub.ObjectIds.WellKnownRole_ConfigureAdmin)
                    {
                        value = GetDefaultRolePermissions();
                        return StatusCodes.Good;
                    }
                }
            }

            return StatusCodes.BadUserAccessDenied;
        }

        /// <remarks />
        protected virtual ServiceResult ReadDefaultUserRolePermissions(ISystemContext context, NodeState node, ref object value)
        {
            var availableRoles = new List<RolePermissionType>();

            var identity = context.UserIdentity as RoleBasedIdentity;

            if (identity != null)
            {
                IList<NodeId> roles = identity.Roles;

                var possibleRoles = GetDefaultRolePermissions();

                foreach (var possibleRole in possibleRoles)
                {
                    if (roles.Contains(possibleRole.RoleId))
                    {
                        availableRoles.Add(possibleRole);
                    }
                }
            }

            value = availableRoles.ToArray();
            return StatusCodes.Good;
        }

        /// <remarks />
        protected virtual ServiceResult ReadDefaultAccessRestrictions(ISystemContext context, NodeState node, ref object value)
        {
            value = 0;
            return StatusCodes.Good;
        }
        #endregion

        #region Private Fields
        private ushort m_namespaceIndex;
        private ushort m_typeNamespaceIndex;
        private long m_lastUsedId;
        private PubSubAdaptor m_PubSubAdaptor;
        private Opc.Ua.Core.SubscriberDelegate m_subscriberDelegate;
        #endregion

        #region Private Methods
        private bool ValidateInputArguments(VariantCollection inputArguments, out StatusCodeCollection inputArgsResult)
        {
            inputArgsResult = new StatusCodeCollection();
            bool result = true;

            if (inputArguments == null)
            {
                //Logger.Error("NodeManager::ValidateInputArguments:Unable to validate the inputArguments, inputArgument is null or empty");
                return result;
            }

            foreach (Variant variant in inputArguments)
            {
                StatusCode statusCode = StatusCodes.Good;
                if (variant.Value == null)
                {
                    statusCode = StatusCodes.BadInvalidArgument;
                    result = false;
                }

                inputArgsResult.Add(statusCode);
            }

            return result;
        }
        #endregion

        #region Private Members 
        #endregion
    }

}
