/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Xml;
using System.IO;
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;
using System.Reflection;

namespace TestData
{
    /// <summary>
    /// A node manager for a variety of test data.
    /// </summary>
    public class TestDataNodeManager : CustomNodeManager2, ITestDataSystemCallback
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public TestDataNodeManager(Opc.Ua.Server.IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration)
        {
            // update the namespaces.
            List<string> namespaceUris = new List<string>();

            namespaceUris.Add(Namespaces.TestData);
            namespaceUris.Add(Namespaces.TestData + "/Instance");

            NamespaceUris = namespaceUris;

            // get the configuration for the node manager.
            m_configuration = configuration.ParseExtension<TestDataNodeManagerConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new TestDataNodeManagerConfiguration();
            }

            m_lastUsedId = m_configuration.NextUnusedId-1;

            // create the object used to access the test system.
            m_system = new TestDataSystem(this, server.NamespaceUris, server.ServerUris);

            // update the default context.
            SystemContext.SystemHandle = m_system;
        }
        #endregion

        #region ITestDataSystemCallback Members
        /// <summary>
        /// Updates the variable after receiving a notification that it has changed in the underlying system.
        /// </summary>
        public void OnDataChange(BaseVariableState variable, object value, StatusCode statusCode, DateTime timestamp)
        {
            lock (Lock)
            {
                variable.Value = value;
                variable.StatusCode = statusCode;
                variable.Timestamp = timestamp;

                // notifies any monitored items that the value has changed.
                variable.ClearChangeMasks(SystemContext, false);
            }
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
                // ensure the namespace used by the node manager is in the server's namespace table.
                m_typeNamespaceIndex = Server.NamespaceUris.GetIndexOrAppend(Namespaces.TestData);
                m_namespaceIndex = Server.NamespaceUris.GetIndexOrAppend(Namespaces.TestData + "/Instance");

                base.CreateAddressSpace(externalReferences);
                                
                // start monitoring the system status.
                m_systemStatusCondition = (TestSystemConditionState)FindPredefinedNode(
                    new NodeId(Objects.Data_Conditions_SystemStatus, m_typeNamespaceIndex), 
                    typeof(TestSystemConditionState));

                if (m_systemStatusCondition != null)
                {
                    m_systemStatusTimer = new Timer(OnCheckSystemStatus, null, 5000, 5000);
                    m_systemStatusCondition.Retain.Value = true;
                }
                
                // link all conditions to the conditions folder.
                NodeState conditionsFolder = (NodeState)FindPredefinedNode(
                    new NodeId(Objects.Data_Conditions, m_typeNamespaceIndex), 
                    typeof(NodeState));

                foreach (NodeState node in PredefinedNodes.Values)
                {
                    ConditionState condition = node as ConditionState;

                    if (condition != null && !Object.ReferenceEquals(condition.Parent, conditionsFolder))
                    {
                        condition.AddNotifier(SystemContext, null, true, conditionsFolder);
                        conditionsFolder.AddNotifier(SystemContext, null, false, condition);
                    }
                }
                
                // enable history for all numeric scalar values.
                ScalarValueObjectState scalarValues = (ScalarValueObjectState)FindPredefinedNode(
                    new NodeId(Objects.Data_Dynamic_Scalar, m_typeNamespaceIndex), 
                    typeof(ScalarValueObjectState));

                scalarValues.Int32Value.Historizing = true;
                scalarValues.Int32Value.AccessLevel = (byte)(scalarValues.Int32Value.AccessLevel | AccessLevels.HistoryRead);

                m_system.EnableHistoryArchiving(scalarValues.Int32Value);
            }
        }

        /// <summary>
        /// Loads a node set from a file or resource and addes them to the set of predefined nodes.
        /// </summary>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            NodeStateCollection predefinedNodes = new NodeStateCollection();
            predefinedNodes.LoadFromBinaryResource(context, "Opc.Ua.Sample.TestData.TestData.PredefinedNodes.uanodes", this.GetType().GetTypeInfo().Assembly, true);
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
                case ObjectTypes.TestSystemConditionType:
                {
                    if (passiveNode is TestSystemConditionState)
                    {
                        break;
                    }

                    TestSystemConditionState activeNode = new TestSystemConditionState(passiveNode.Parent);
                    activeNode.Create(context, passiveNode);

                    if (passiveNode.Parent != null)
                    {
                        passiveNode.Parent.ReplaceChild(context, activeNode);
                    }

                    return activeNode;
                }

                case ObjectTypes.ScalarValueObjectType:
                {
                    if (passiveNode is ScalarValueObjectState)
                    {
                        break;
                    }

                    ScalarValueObjectState activeNode = new ScalarValueObjectState(passiveNode.Parent);
                    activeNode.Create(context, passiveNode);

                    if (passiveNode.Parent != null)
                    {
                        passiveNode.Parent.ReplaceChild(context, activeNode);
                    }

                    return activeNode;
                }

                case ObjectTypes.AnalogScalarValueObjectType:
                {
                    if (passiveNode is AnalogScalarValueObjectState)
                    {
                        break;
                    }

                    AnalogScalarValueObjectState activeNode = new AnalogScalarValueObjectState(passiveNode.Parent);
                    activeNode.Create(context, passiveNode);

                    if (passiveNode.Parent != null)
                    {
                        passiveNode.Parent.ReplaceChild(context, activeNode);
                    }

                    return activeNode;
                }

                case ObjectTypes.ArrayValueObjectType:
                {
                    if (passiveNode is ArrayValueObjectState)
                    {
                        break;
                    }

                    ArrayValueObjectState activeNode = new ArrayValueObjectState(passiveNode.Parent);
                    activeNode.Create(context, passiveNode);

                    if (passiveNode.Parent != null)
                    {
                        passiveNode.Parent.ReplaceChild(context, activeNode);
                    }

                    return activeNode;
                }

                case ObjectTypes.AnalogArrayValueObjectType:
                {
                    if (passiveNode is AnalogArrayValueObjectState)
                    {
                        break;
                    }

                    AnalogArrayValueObjectState activeNode = new AnalogArrayValueObjectState(passiveNode.Parent);
                    activeNode.Create(context, passiveNode);

                    if (passiveNode.Parent != null)
                    {
                        passiveNode.Parent.ReplaceChild(context, activeNode);
                    }

                    return activeNode;
                }

                case ObjectTypes.UserScalarValueObjectType:
                {
                    if (passiveNode is UserScalarValueObjectState)
                    {
                        break;
                    }

                    UserScalarValueObjectState activeNode = new UserScalarValueObjectState(passiveNode.Parent);
                    activeNode.Create(context, passiveNode);

                    if (passiveNode.Parent != null)
                    {
                        passiveNode.Parent.ReplaceChild(context, activeNode);
                    }

                    return activeNode;
                }

                case ObjectTypes.UserArrayValueObjectType:
                {
                    if (passiveNode is UserArrayValueObjectState)
                    {
                        break;
                    }

                    UserArrayValueObjectState activeNode = new UserArrayValueObjectState(passiveNode.Parent);
                    activeNode.Create(context, passiveNode);

                    if (passiveNode.Parent != null)
                    {
                        passiveNode.Parent.ReplaceChild(context, activeNode);
                    }

                    return activeNode;
                }

                case ObjectTypes.MethodTestType:
                {
                    if (passiveNode is MethodTestState)
                    {
                        break;
                    }

                    MethodTestState activeNode = new MethodTestState(passiveNode.Parent);
                    activeNode.Create(context, passiveNode);

                    if (passiveNode.Parent != null)
                    {
                        passiveNode.Parent.ReplaceChild(context, activeNode);
                    }

                    return activeNode;
                }
            }

            return predefinedNode;
        }

        /// <summary>
        /// Restores a previously cached history reader.
        /// </summary>
        protected virtual HistoryDataReader RestoreDataReader(Opc.Ua.Server.ServerSystemContext context, byte[] continuationPoint)
        {
            if (context == null || context.OperationContext == null || context.OperationContext.Session == null)
            {
                return null;
            }

            HistoryDataReader reader = context.OperationContext.Session.RestoreHistoryContinuationPoint(continuationPoint) as HistoryDataReader;

            if (reader == null)
            {
                return null;
            }

            return reader;
        }

        /// <summary>
        /// Saves a history data reader.
        /// </summary>
        protected virtual void SaveDataReader(Opc.Ua.Server.ServerSystemContext context, HistoryDataReader reader)
        {
            if (context == null || context.OperationContext == null || context.OperationContext.Session == null)
            {
                return;
            }

            context.OperationContext.Session.SaveHistoryContinuationPoint(reader.Id, reader);
        }

        /// <summary>
        /// Returns the history data source for a node.
        /// </summary>
        protected virtual ServiceResult GetHistoryDataSource(
            Opc.Ua.Server.ServerSystemContext context,
            BaseVariableState variable,
            out IHistoryDataSource datasource)
        {
            datasource = m_system.GetHistoryFile(variable);

            if (datasource == null)
            {
                return StatusCodes.BadNotReadable;
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Reads the raw data for a variable
        /// </summary>
        protected ServiceResult HistoryReadRaw(
            ISystemContext context, 
            BaseVariableState source, 
            ReadRawModifiedDetails details, 
            TimestampsToReturn timestampsToReturn, 
            bool releaseContinuationPoints, 
            HistoryReadValueId nodeToRead, 
            HistoryReadResult result)
        {
            ServerSystemContext serverContext = context as ServerSystemContext;

            HistoryDataReader reader = null;
            HistoryData data = new HistoryData();

            if (nodeToRead.ContinuationPoint != null && nodeToRead.ContinuationPoint.Length > 0)
            {
                // restore the continuation point.
                reader = RestoreDataReader(serverContext, nodeToRead.ContinuationPoint);

                if (reader == null)
                {
                    return StatusCodes.BadContinuationPointInvalid;
                }

                // node id must match previous node id.
                if (reader.VariableId != nodeToRead.NodeId)
                {
                    Utils.SilentDispose(reader);
                    return StatusCodes.BadContinuationPointInvalid;
                }

                // check if releasing continuation points.
                if (releaseContinuationPoints)
                {
                    Utils.SilentDispose(reader);
                    return ServiceResult.Good;
                }
            }
            else
            {
                // get the source for the variable.
                IHistoryDataSource datasource = null;
                ServiceResult error = GetHistoryDataSource(serverContext, source, out datasource);

                if (ServiceResult.IsBad(error))
                {
                    return error;
                }

                // create a reader.
                reader = new HistoryDataReader(nodeToRead.NodeId, datasource);
                
                // start reading.
                reader.BeginReadRaw(
                    serverContext, 
                    details, 
                    timestampsToReturn, 
                    nodeToRead.ParsedIndexRange,
                    nodeToRead.DataEncoding,
                    data.DataValues);
            }
     
            // continue reading data until done or max values reached.
            bool complete = reader.NextReadRaw(
                serverContext, 
                timestampsToReturn, 
                nodeToRead.ParsedIndexRange,
                nodeToRead.DataEncoding,
                data.DataValues);

            // save continuation point.
            if (!complete)
            {
                SaveDataReader(serverContext, reader);
                result.StatusCode = StatusCodes.GoodMoreData;
            }

            // return the dat.
            result.HistoryData = new ExtensionObject(data);
            
            return result.StatusCode;
        }

        /// <summary>
        /// Returns true if the system must be scanning to provide updates for the monitored item.
        /// </summary>
        private bool SystemScanRequired(MonitoredNode2 monitoredNode, IDataChangeMonitoredItem2 monitoredItem)
        {
            // ingore other types of monitored items.
            if (monitoredItem == null)
            {
                return false;
            }

            // only care about variables.
            BaseDataVariableState source = monitoredNode.Node as BaseDataVariableState;

            if (source == null)
            {
                return false;
            }

            // check for variables that need to be scanned.
            if (monitoredItem.AttributeId == Attributes.Value)
            {
                TestDataObjectState test = source.Parent as TestDataObjectState;
                
                if (test != null && test.SimulationActive.Value)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Called after creating a MonitoredItem.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle for the node.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        protected override void OnMonitoredItemCreated(
            ServerSystemContext context,
            NodeHandle handle,
            MonitoredItem monitoredItem)
        {
            if (SystemScanRequired(handle.MonitoredNode, monitoredItem))
            {
                if (monitoredItem.MonitoringMode != MonitoringMode.Disabled)
                {
                    m_system.StartMonitoringValue(
                        monitoredItem.Id, 
                        monitoredItem.SamplingInterval, 
                        handle.Node as BaseVariableState);
                }
            }    
        }

        /// <summary>
        /// Called after modifying a MonitoredItem.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle for the node.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        protected override void OnMonitoredItemModified(
            ServerSystemContext context,
            NodeHandle handle,
            MonitoredItem monitoredItem)
        {
            if (SystemScanRequired(handle.MonitoredNode, monitoredItem))
            {
                if (monitoredItem.MonitoringMode != MonitoringMode.Disabled)
                {
                    BaseVariableState source = handle.Node as BaseVariableState;
                    m_system.StopMonitoringValue(monitoredItem.Id);
                    m_system.StartMonitoringValue(monitoredItem.Id, monitoredItem.SamplingInterval, source);
                }
            }
        }

        /// <summary>
        /// Called after deleting a MonitoredItem.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle for the node.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        protected override void OnMonitoredItemDeleted(
            ServerSystemContext context,
            NodeHandle handle,
            MonitoredItem monitoredItem)
        {
            // check for variables that need to be scanned.
            if (SystemScanRequired(handle.MonitoredNode, monitoredItem))
            {
                m_system.StopMonitoringValue(monitoredItem.Id);
            }
        }

        /// <summary>
        /// Called after changing the MonitoringMode for a MonitoredItem.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle for the node.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        /// <param name="previousMode">The previous monitoring mode.</param>
        /// <param name="monitoringMode">The current monitoring mode.</param>
        protected override void OnMonitoringModeChanged(
            ServerSystemContext context,
            NodeHandle handle,
            MonitoredItem monitoredItem,
            MonitoringMode previousMode,
            MonitoringMode monitoringMode)
        {
            if (SystemScanRequired(handle.MonitoredNode, monitoredItem))
            {
                BaseVariableState source = handle.Node as BaseVariableState;

                if (previousMode != MonitoringMode.Disabled && monitoredItem.MonitoringMode == MonitoringMode.Disabled)
                {
                    m_system.StopMonitoringValue(monitoredItem.Id);
                }

                if (previousMode == MonitoringMode.Disabled && monitoredItem.MonitoringMode != MonitoringMode.Disabled)
                {
                    m_system.StartMonitoringValue(monitoredItem.Id, monitoredItem.SamplingInterval, source);
                }
            }
        }
        #endregion

        /// <summary>
        /// Peridically checks the system state.
        /// </summary>
        private void OnCheckSystemStatus(object state)
        {
            #if CONDITION_SAMPLES
            lock (Lock)
            {
                try
                {  
                    // create the dialog.
                    if (m_dialog == null)
                    {
                        m_dialog = new DialogConditionState(null);

                        CreateNode(
                            SystemContext,
                            ExpandedNodeId.ToNodeId(ObjectIds.Data_Conditions, SystemContext.NamespaceUris),
                            ReferenceTypeIds.HasComponent,
                            new QualifiedName("ResetSystemDialog", m_namespaceIndex),
                            m_dialog);

                        m_dialog.OnAfterResponse = OnDialogComplete;
                    }
        
                    StatusCode systemStatus = m_system.SystemStatus;
                    m_systemStatusCondition.UpdateStatus(systemStatus);

                    // cycle through different status codes in order to simulate a real system.
                    if (StatusCode.IsGood(systemStatus))
                    {
                        m_systemStatusCondition.UpdateSeverity((ushort)EventSeverity.Low);
                        m_system.SystemStatus = StatusCodes.Uncertain;
                    }
                    else if (StatusCode.IsUncertain(systemStatus))
                    {
                        m_systemStatusCondition.UpdateSeverity((ushort)EventSeverity.Medium);
                        m_system.SystemStatus = StatusCodes.Bad;
                    }
                    else
                    {
                        m_systemStatusCondition.UpdateSeverity((ushort)EventSeverity.High);
                        m_system.SystemStatus = StatusCodes.Good;
                    }

                    // request a reset if status is bad.
                    if (StatusCode.IsBad(systemStatus))
                    {
                        m_dialog.RequestResponse(
                            SystemContext, 
                            "Reset the test system?", 
                            (uint)(int)(DialogConditionChoice.Ok | DialogConditionChoice.Cancel),
                            (ushort)EventSeverity.MediumHigh);
                    }
                                        
                    // report the event.
                    TranslationInfo info = new TranslationInfo(
                        "TestSystemStatusChange",
                        "en-US",
                        "The TestSystem status is now {0}.",
                        systemStatus);

                    m_systemStatusCondition.ReportConditionChange(
                        SystemContext,
                        null,
                        new LocalizedText(info),
                        false);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error monitoring system status.");
                }
            }
            #endif
        }    
        
        #if CONDITION_SAMPLES
        /// <summary>
        /// Handles a user response to a dialog.
        /// </summary>
        private ServiceResult OnDialogComplete(
            ISystemContext context, 
            DialogConditionState dialog, 
            DialogConditionChoice response)
        {
            if (m_dialog != null)
            {
                DeleteNode(SystemContext, m_dialog.NodeId);
                m_dialog = null;
            }

            return ServiceResult.Good;
        }
        #endif

        #region Private Fields
        private TestDataNodeManagerConfiguration m_configuration;
        private ushort m_namespaceIndex;
        private ushort m_typeNamespaceIndex;
        private TestDataSystem m_system;
        private long m_lastUsedId;
        private Timer m_systemStatusTimer;
        private TestSystemConditionState m_systemStatusCondition;
        
        #if CONDITION_SAMPLES
        private DialogConditionState m_dialog;
        #endif
        #endregion
    }
}
