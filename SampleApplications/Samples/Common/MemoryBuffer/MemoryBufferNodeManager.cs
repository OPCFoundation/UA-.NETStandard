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
using Opc.Ua.Sample;
using System.Reflection;

namespace MemoryBuffer
{
    /// <summary>
    /// A node manager for a variety of test data.
    /// </summary>
    public class MemoryBufferNodeManager : SampleNodeManager
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public MemoryBufferNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server)
        {
            List<string> namespaceUris = new List<string>();
         
            namespaceUris.Add(Namespaces.MemoryBuffer);
            namespaceUris.Add(Namespaces.MemoryBuffer + "/Instance");
            
            NamespaceUris = namespaceUris;
            
            // get the configuration for the node manager.
            m_configuration = configuration.ParseExtension<MemoryBufferConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new MemoryBufferConfiguration();
            }

            m_buffers = new Dictionary<string, MemoryBufferState>();
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
                
                // create the nodes from configuration.
                ushort namespaceIndex = Server.NamespaceUris.GetIndexOrAppend(Namespaces.MemoryBuffer);

                BaseInstanceState root = (BaseInstanceState)FindPredefinedNode(
                    new NodeId(Objects.MemoryBuffers, namespaceIndex), 
                    typeof(BaseInstanceState));
                        
                // create the nodes from configuration.
                namespaceIndex = Server.NamespaceUris.GetIndexOrAppend(Namespaces.MemoryBuffer + "/Instance");

                if (m_configuration != null && m_configuration.Buffers != null)
                {
                    for (int ii = 0; ii < m_configuration.Buffers.Count; ii++)
                    {
                        MemoryBufferInstance instance = m_configuration.Buffers[ii];

                        // create a new buffer.
                        MemoryBufferState bufferNode = new MemoryBufferState(SystemContext, instance);

                        // assign node ids.
                        bufferNode.Create(
                            SystemContext, 
                            new NodeId(bufferNode.SymbolicName, namespaceIndex), 
                            new QualifiedName(bufferNode.SymbolicName, namespaceIndex),
                            null, 
                            true);
                        
                        bufferNode.CreateBuffer(instance.DataType, instance.TagCount);
                        bufferNode.InitializeMonitoring(Server, this);

                        // save the buffers for easy look up later.
                        m_buffers[bufferNode.SymbolicName] = bufferNode;

                        // link to root.
                        root.AddChild(bufferNode);
                    }
                }
            }
        }

        /// <summary>
        /// Loads a node set from a file or resource and addes them to the set of predefined nodes.
        /// </summary>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            NodeStateCollection predefinedNodes = new NodeStateCollection();
            predefinedNodes.LoadFromBinaryResource(context, "Opc.Ua.Sample.MemoryBuffer.MemoryBuffer.PredefinedNodes.uanodes", this.GetType().GetTypeInfo().Assembly, true);
            return predefinedNodes;
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
                base.DeleteAddressSpace();
            }
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        /// <remarks>
        /// This must efficiently determine whether the node belongs to the node manager. If it does belong to 
        /// NodeManager it should return a handle that does not require the NodeId to be validated again when
        /// the handle is passed into other methods such as 'Read' or 'Write'.
        /// </remarks>
        protected override object GetManagerHandle(ISystemContext context, NodeId nodeId, IDictionary<NodeId,NodeState> cache)
        {
            lock (Lock)
            {
                if (!IsNodeIdInNamespace(nodeId))
                {
                    return null;
                }

                string id = nodeId.Identifier as string;

                if (id != null)
                {
                    // check for a reference to the buffer.
                    MemoryBufferState buffer = null;
 
                    if (m_buffers.TryGetValue(id, out buffer))
                    {
                        return buffer;
                    }

                    // tag ids have the syntax <bufferName>[<address>]
                    if (id[id.Length-1] != ']')
                    {
                        return null;
                    }

                    int index = id.IndexOf('[');

                    if (index == -1)
                    {
                        return null;
                    }

                    string bufferName = id.Substring(0, index);

                    // verify the buffer.
                    if (!m_buffers.TryGetValue(bufferName, out buffer))
                    {
                        return null;
                    }

                    // validate the address.
                    string offsetText = id.Substring(index+1, id.Length-index-2);

                    for (int ii = 0; ii < offsetText.Length; ii++)
                    {
                        if (!Char.IsDigit(offsetText[ii]))
                        {
                            return null;
                        }
                    }

                    // check range on offset.
                    uint offset = Convert.ToUInt32(offsetText);

                    if (offset >= buffer.SizeInBytes.Value)
                    {
                        return null;
                    }
                    
                    // the tags contain all of the metadata required to support the UA
                    // operations and pointers to functions in the buffer object that
                    // allow the value to be accessed. These tags are ephemeral and are
                    // discarded after the operation completes. This design pattern allows
                    // the server to expose potentially millions of UA nodes without 
                    // creating millions of objects that reside in memory.
                    return new MemoryTagState(buffer, offset);
                }

                return base.GetManagerHandle(context, nodeId, cache);
            }
        }

        /// <summary>
        /// Creates a new set of monitored items for a set of variables.
        /// </summary>
        /// <remarks>
        /// This method only handles data change subscriptions. Event subscriptions are created by the SDK.
        /// </remarks>
        protected override ServiceResult CreateMonitoredItem(
            ISystemContext context,
            NodeState source,
            uint subscriptionId,
            double publishingInterval,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequest itemToCreate,
            ref long globalIdCounter,
            out MonitoringFilterResult filterError,
            out IMonitoredItem monitoredItem)
        {
            filterError = null;
            monitoredItem = null;

            MemoryTagState tag = source as MemoryTagState;

            // use default behavoir for non-tag sources.
            if (tag == null)
            {
                return base.CreateMonitoredItem(
                    context,
                    source,
                    subscriptionId,
                    publishingInterval,
                    diagnosticsMasks,
                    timestampsToReturn,
                    itemToCreate,
                    ref globalIdCounter,
                    out filterError,
                    out monitoredItem);
            }

            // validate parameters.
            MonitoringParameters parameters = itemToCreate.RequestedParameters;

            // no filters supported at this time.
            MonitoringFilter filter = (MonitoringFilter)ExtensionObject.ToEncodeable(parameters.Filter);

            if (filter != null)
            {
                return StatusCodes.BadFilterNotAllowed;
            }
            
            // index range not supported.
            if (itemToCreate.ItemToMonitor.ParsedIndexRange != NumericRange.Empty)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            // data encoding not supported.
            if (!QualifiedName.IsNull(itemToCreate.ItemToMonitor.DataEncoding))
            {
                return StatusCodes.BadDataEncodingInvalid;
            }

            // read initial value.
            DataValue initialValue = new DataValue();

            initialValue.Value = null;
            initialValue.ServerTimestamp = DateTime.UtcNow;
            initialValue.SourceTimestamp = DateTime.MinValue;
            initialValue.StatusCode = StatusCodes.Good;

            ServiceResult error = source.ReadAttribute(
                context,
                itemToCreate.ItemToMonitor.AttributeId,
                itemToCreate.ItemToMonitor.ParsedIndexRange,
                itemToCreate.ItemToMonitor.DataEncoding,
                initialValue);

            if (ServiceResult.IsBad(error))
            {
                return error;
            }

            // get the monitored node for the containing buffer.
            MemoryBufferState buffer = tag.Parent as MemoryBufferState;

            if (buffer == null)
            {
                return StatusCodes.BadInternalError;
            }

            // create a globally unique identifier.
            uint monitoredItemId = Utils.IncrementIdentifier(ref globalIdCounter);

            // determine the sampling interval.
            double samplingInterval = itemToCreate.RequestedParameters.SamplingInterval;

            if (samplingInterval < 0)
            {
                samplingInterval = publishingInterval;
            }
            
            // create the item.
            MemoryBufferMonitoredItem datachangeItem = buffer.CreateDataChangeItem(
                context as ServerSystemContext,
                tag,
                subscriptionId,
                monitoredItemId,
                itemToCreate.ItemToMonitor,
                diagnosticsMasks,
                timestampsToReturn,
                itemToCreate.MonitoringMode,
                itemToCreate.RequestedParameters.ClientHandle,
                samplingInterval);

            /*
            // create the item.
            MemoryBufferMonitoredItem datachangeItem = buffer.CreateDataChangeItem(
                context,
                tag,
                monitoredItemId,
                itemToCreate.ItemToMonitor.AttributeId,
                diagnosticsMasks,
                timestampsToReturn,
                itemToCreate.MonitoringMode,
                itemToCreate.RequestedParameters.ClientHandle,
                samplingInterval);
            */

            // report the initial value.
            datachangeItem.QueueValue(initialValue, null);

            // update monitored item list.
            monitoredItem = datachangeItem;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Modifies the parameters for a monitored item.
        /// </summary>
        protected override ServiceResult ModifyMonitoredItem(
            ISystemContext context,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            IMonitoredItem monitoredItem,
            MonitoredItemModifyRequest itemToModify,
            out MonitoringFilterResult filterError)
        {
            filterError = null;

            // check for valid handle.
            MemoryBufferState buffer = monitoredItem.ManagerHandle as MemoryBufferState;

            if (buffer == null)
            {
                return base.ModifyMonitoredItem(
                    context,
                    diagnosticsMasks,
                    timestampsToReturn,
                    monitoredItem,
                    itemToModify,
                    out filterError);
            }

            // owned by this node manager.
            itemToModify.Processed = true;

            // get the monitored item.
            MemoryBufferMonitoredItem datachangeItem = monitoredItem as MemoryBufferMonitoredItem;

            if (datachangeItem == null)
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            // validate parameters.
            MonitoringParameters parameters = itemToModify.RequestedParameters;

            // no filters supported at this time.
            MonitoringFilter filter = (MonitoringFilter)ExtensionObject.ToEncodeable(parameters.Filter);

            if (filter != null)
            {
                return StatusCodes.BadFilterNotAllowed;
            }

            // modify the monitored item parameters.
            ServiceResult error = datachangeItem.Modify(
                diagnosticsMasks,
                timestampsToReturn,
                itemToModify.RequestedParameters.ClientHandle,
                itemToModify.RequestedParameters.SamplingInterval);

            return ServiceResult.Good;
        }       

        /// <summary>
        /// Deletes a monitored item.
        /// </summary>
        protected override ServiceResult DeleteMonitoredItem(
            ISystemContext context,
            IMonitoredItem monitoredItem,
            out bool processed)
        {
            processed = false;

            // check for valid handle.
            MemoryBufferState buffer = monitoredItem.ManagerHandle as MemoryBufferState;

            if (buffer == null)
            {
                return base.DeleteMonitoredItem(
                    context,
                    monitoredItem,
                    out processed);
            }

            // owned by this node manager.
            processed = true;

            // get the monitored item.
            MemoryBufferMonitoredItem datachangeItem = monitoredItem as MemoryBufferMonitoredItem;

            if (datachangeItem == null)
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            // delete the item.
            buffer.DeleteItem(datachangeItem);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Changes the monitoring mode for an item.
        /// </summary>
        protected override ServiceResult SetMonitoringMode(
            ISystemContext context,
            IMonitoredItem monitoredItem,
            MonitoringMode monitoringMode, 
            out bool processed)
        {
            processed = false;

            // check for valid handle.
            MemoryBufferState buffer = monitoredItem.ManagerHandle as MemoryBufferState;

            if (buffer == null)
            {
                return base.SetMonitoringMode(
                    context,
                    monitoredItem,
                    monitoringMode,
                    out processed);
            }

            // owned by this node manager.
            processed = true;

            // get the monitored item.
            MemoryBufferMonitoredItem datachangeItem = monitoredItem as MemoryBufferMonitoredItem;

            if (datachangeItem == null)
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            // delete the item.
            MonitoringMode previousMode = datachangeItem.SetMonitoringMode(monitoringMode);
            
            // need to provide an immediate update after enabling.
            if (previousMode == MonitoringMode.Disabled && monitoringMode != MonitoringMode.Disabled)
            {
                DataValue initialValue = new DataValue();

                initialValue.Value = null;
                initialValue.ServerTimestamp = DateTime.UtcNow;
                initialValue.SourceTimestamp = DateTime.MinValue;
                initialValue.StatusCode = StatusCodes.Good;
                
                MemoryTagState tag = new MemoryTagState(buffer, datachangeItem.Offset);

                ServiceResult error = tag.ReadAttribute(
                    context,
                    datachangeItem.AttributeId,
                    NumericRange.Empty,
                    null,
                    initialValue);

                datachangeItem.QueueValue(initialValue, error);
            }
            
            return ServiceResult.Good;
        }
        #endregion

        #region Private Fields
        private MemoryBufferConfiguration m_configuration;
        private Dictionary<string, MemoryBufferState> m_buffers;
        #endregion
    }
}
