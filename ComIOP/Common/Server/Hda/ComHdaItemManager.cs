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
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using Opc.Ua.Client;
using OpcRcw.Hda;

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// Stores information about an HDA item.
    /// </summary>
    public class HdaItemHandle
    {
        /// <summary>
        /// The remote node id.
        /// </summary>
        public NodeId NodeId;

        /// <summary>
        /// The local HDA server handle.
        /// </summary>
        public int ServerHandle;

        /// <summary>
        /// The local HDA client handle.
        /// </summary>
        public int ClientHandle;

        /// <summary>
        /// Any error associated with the handle.
        /// </summary>
        public int Error;
    }

    /// <summary>
    /// A class that implements a COM DA group.
    /// </summary>
    public class ComHdaItemManager
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComHdaItemManager"/> class.
        /// </summary>
        /// <param name="mapper">The object used to map namespace indexes.</param>
        public ComHdaItemManager(ComNamespaceMapper mapper)
		{
            m_mapper = mapper;
            m_handles = new Dictionary<int, InternalHandle>();
            m_items = new Dictionary<string, Item>();
        }
        #endregion

        /// <summary>
        /// Constructs a ReadValueId for the specified UA attribute.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <param name="uaAttributeId">The ua attribute id.</param>
        /// <returns></returns>
        private ReadValueId Construct(InternalHandle handle, uint uaAttributeId)
        {
            ReadValueId readValueId = new ReadValueId();
            readValueId.NodeId = handle.NodeId;
            readValueId.AttributeId = uaAttributeId;
            readValueId.Handle = handle;
            return readValueId;
        }

        /// <summary>
        /// Constructs a ReadValueId for the specified UA attribute.
        /// </summary>
        /// <param name="nodeId">The node id.</param>
        /// <param name="hdaAttributeId">The hda attribute id.</param>
        /// <param name="uaAttributeId">The ua attribute id.</param>
        /// <returns></returns>
        private ReadValueId Construct(NodeId nodeId, uint hdaAttributeId, uint uaAttributeId)
        {
            ReadValueId readValueId = new ReadValueId();
            readValueId.NodeId = nodeId;
            readValueId.AttributeId = uaAttributeId;
            readValueId.Handle = hdaAttributeId;
            return readValueId;
        }

        /// <summary>
        /// Constructs the browse path.
        /// </summary>
        /// <param name="nodeId">The node id.</param>
        /// <param name="hdaAttributeId">The hda attribute id.</param>
        /// <param name="browsePaths">The browse paths.</param>
        /// <returns></returns>
        private BrowsePath Construct(NodeId nodeId, uint hdaAttributeId, params string[] browsePaths)
        {
            BrowsePath browsePath = new BrowsePath();
            browsePath.StartingNode = nodeId;
            browsePath.Handle = hdaAttributeId;

            for (int ii = 0; ii < browsePaths.Length; ii++)
            {
                RelativePathElement element = new RelativePathElement();

                element.ReferenceTypeId = ReferenceTypeIds.HasChild;
                element.IsInverse = false;
                element.IncludeSubtypes = true;
                element.TargetName = browsePaths[ii];

                browsePath.RelativePath.Elements.Add(element);
            }

            return browsePath;
        }

        /// <summary>
        /// Gets the type of the remote data.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns></returns>
        public TypeInfo GetRemoteDataType(HdaItemHandle handle)
        {
            InternalHandle handle2 = handle as InternalHandle;

            if (handle2 != null)
            {
                return handle2.Item.RemoteType;
            }

            return TypeInfo.Scalars.Variant;
        }

        /// <summary>
        /// Gets the available attributes for an HDA item.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="nodeId">The node id.</param>
        /// <returns></returns>
        private ReadValueIdCollection GetAvailableAttributes(Session session, NodeId nodeId)
        {
            ReadValueIdCollection supportedAttributes = new ReadValueIdCollection();

            // add mandatory HDA attributes.
            supportedAttributes.Add(Construct(nodeId, OpcRcw.Hda.Constants.OPCHDA_ITEMID, Attributes.DisplayName));
            supportedAttributes.Add(Construct(nodeId, OpcRcw.Hda.Constants.OPCHDA_DATA_TYPE, Attributes.DataType));
            supportedAttributes.Add(Construct(nodeId, ComHdaProxy.INTERNAL_ATTRIBUTE_VALUE_RANK, Attributes.ValueRank));
            supportedAttributes.Add(Construct(nodeId, OpcRcw.Hda.Constants.OPCHDA_DESCRIPTION, Attributes.Description));
            supportedAttributes.Add(Construct(nodeId, OpcRcw.Hda.Constants.OPCHDA_ARCHIVING, Attributes.Historizing));

            // check if nodes are defined for all optional HDA attributes.
            BrowsePathCollection pathsToRead = new BrowsePathCollection();

            pathsToRead.Add(Construct(nodeId, ComHdaProxy.INTERNAL_ATTRIBUTE_ANNOTATION, Opc.Ua.BrowseNames.Annotations));
            pathsToRead.Add(Construct(nodeId, OpcRcw.Hda.Constants.OPCHDA_ENG_UNITS, Opc.Ua.BrowseNames.EngineeringUnits)); ;
            pathsToRead.Add(Construct(nodeId, OpcRcw.Hda.Constants.OPCHDA_DERIVE_EQUATION, Opc.Ua.BrowseNames.Definition));
            pathsToRead.Add(Construct(nodeId, OpcRcw.Hda.Constants.OPCHDA_NORMAL_MAXIMUM, Opc.Ua.BrowseNames.EURange));
            pathsToRead.Add(Construct(nodeId, OpcRcw.Hda.Constants.OPCHDA_NORMAL_MINIMUM, Opc.Ua.BrowseNames.EURange));
            pathsToRead.Add(Construct(nodeId, OpcRcw.Hda.Constants.OPCHDA_HIGH_ENTRY_LIMIT, Opc.Ua.BrowseNames.InstrumentRange));
            pathsToRead.Add(Construct(nodeId, OpcRcw.Hda.Constants.OPCHDA_LOW_ENTRY_LIMIT, Opc.Ua.BrowseNames.InstrumentRange));
            pathsToRead.Add(Construct(nodeId, OpcRcw.Hda.Constants.OPCHDA_STEPPED, Opc.Ua.BrowseNames.HAConfiguration, Opc.Ua.BrowseNames.Stepped));
            pathsToRead.Add(Construct(nodeId, OpcRcw.Hda.Constants.OPCHDA_MAX_TIME_INT, Opc.Ua.BrowseNames.HAConfiguration, Opc.Ua.BrowseNames.MaxTimeInterval));
            pathsToRead.Add(Construct(nodeId, OpcRcw.Hda.Constants.OPCHDA_MIN_TIME_INT, Opc.Ua.BrowseNames.HAConfiguration, Opc.Ua.BrowseNames.MinTimeInterval));
            pathsToRead.Add(Construct(nodeId, OpcRcw.Hda.Constants.OPCHDA_EXCEPTION_DEV, Opc.Ua.BrowseNames.HAConfiguration, Opc.Ua.BrowseNames.ExceptionDeviation));
            pathsToRead.Add(Construct(nodeId, OpcRcw.Hda.Constants.OPCHDA_EXCEPTION_DEV_TYPE, Opc.Ua.BrowseNames.HAConfiguration, Opc.Ua.BrowseNames.ExceptionDeviationFormat));

            BrowsePathResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            session.TranslateBrowsePathsToNodeIds(
                null,
                pathsToRead,
                out results,
                out diagnosticInfos);

            Session.ValidateResponse(results, pathsToRead);
            Session.ValidateDiagnosticInfos(diagnosticInfos, pathsToRead);

            for (int ii = 0; ii < pathsToRead.Count; ii++)
            {
                uint attributeId = (uint)pathsToRead[ii].Handle;

                // path does not exist.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    continue;
                }

                // nothing found.
                if (results[ii].Targets.Count == 0)
                {
                    continue;
                }

                // choose the first valid target.
                for (int jj = 0; jj < results[ii].Targets.Count; jj++)
                {
                    BrowsePathTarget target = results[ii].Targets[jj];

                    if (target.RemainingPathIndex == UInt32.MaxValue && !NodeId.IsNull(target.TargetId) && !target.TargetId.IsAbsolute)
                    {
                        supportedAttributes.Add(Construct((NodeId)target.TargetId, attributeId, Attributes.Value));
                        break;
                    }
                }
            }

            return supportedAttributes;
        }

        /// <summary>
        /// Gets the read value id for the specified attribute.
        /// </summary>
        /// <param name="supportedAttributes">The supported attributes.</param>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns></returns>
        private ReadValueId GetReadValueId(ReadValueIdCollection supportedAttributes, uint attributeId)
        {
            for (int jj = 0; jj < supportedAttributes.Count; jj++)
            {
                ReadValueId valueToRead = supportedAttributes[jj];

                if ((uint)valueToRead.Handle == attributeId)
                {
                    return valueToRead;
                }
            }

            return null;
        }

        /// <summary>
        /// Reads the current values for the specified attributes.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="itemHandle">The item handle.</param>
        /// <param name="attributeIds">The attribute ids.</param>
        /// <returns></returns>
        public DaValue[] ReadCurrentValues(Session session, HdaItemHandle itemHandle, uint[] attributeIds)
        {
            DaValue[] results = new DaValue[attributeIds.Length];

            // check handle.
            InternalHandle handle = itemHandle as InternalHandle;

            if (handle == null)
            {
                for (int ii = 0; ii < results.Length; ii++)
                {
                    results[ii] = new DaValue();
                    results[ii].Error = ResultIds.E_INVALIDHANDLE;
                }

                return results;
            }

            // look up the supported attributes for an item.
            ReadValueIdCollection supportedAttributes = handle.Item.SupportedAttributes;

            if (supportedAttributes == null)
            {
                handle.Item.SupportedAttributes = supportedAttributes = GetAvailableAttributes(session, handle.NodeId);
            }

            // build list of values to read.
            ReadValueIdCollection valuesToRead = new ReadValueIdCollection();
            List<int> indexes = new List<int>();

            for (int ii = 0; ii < attributeIds.Length; ii++)
            {
                ReadValueId valueToRead = GetReadValueId(supportedAttributes, attributeIds[ii]);

                if (valueToRead == null)
                {
                    results[ii] = new DaValue();
                    results[ii].Error = ResultIds.E_INVALIDATTRID;
                    continue;
                }

                valuesToRead.Add(valueToRead);
                indexes.Add(ii);

                // need to fetch the value rank as well.
                if (attributeIds[ii] == Constants.OPCHDA_DATA_TYPE)
                {
                    valuesToRead.Add(GetReadValueId(supportedAttributes, ComHdaProxy.INTERNAL_ATTRIBUTE_VALUE_RANK));
                    indexes.Add(-1);
                }
            }

            // nothing to do.
            if (valuesToRead.Count == 0)
            {
                return results;
            }

            // read values from the UA server.
            DataValueCollection values = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            session.Read(
                null,
                0,
                TimestampsToReturn.Neither,
                valuesToRead,
                out values,
                out diagnosticInfos);

            // validate response from the UA server.
            ClientBase.ValidateResponse(values, valuesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToRead);

            // assign a local handle to all valid items.
            for (int ii = 0; ii < valuesToRead.Count; ii++)
            {
                int index = indexes[ii];
                uint attributeId = (uint)valuesToRead[ii].Handle;

                // check for values which are combined with other values to create the value (e.g. ValueRank).
                if (index == -1)
                {
                    continue;
                }

                results[index] = GetAttributeValue(session, attributeId, values, ii);

                // only support current value for now.
                if (results[index].Error == ResultIds.S_OK)
                {
                    results[index].Error = ResultIds.S_CURRENTVALUE;
                }
            }

            return results;
        }

        /// <summary>
        /// Reads the node ids used to fetch the history of the specified attributes.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="itemHandle">The item handle.</param>
        /// <param name="attributeIds">The attribute ids.</param>
        /// <param name="nodeIds">The node ids.</param>
        /// <returns></returns>
        public int[] GetAttributeHistoryNodeIds(Session session, HdaItemHandle itemHandle, uint[] attributeIds, out NodeId[] nodeIds)
        {
            nodeIds = new NodeId[attributeIds.Length];
            int[] results = new int[attributeIds.Length];

            // check handle.
            InternalHandle handle = itemHandle as InternalHandle;

            if (handle == null)
            {
                for (int ii = 0; ii < results.Length; ii++)
                {
                    results[ii] = ResultIds.E_INVALIDHANDLE;
                }

                return results;
            }

            // look up the supported attributes for an item.
            ReadValueIdCollection supportedAttributes = handle.Item.SupportedAttributes;

            if (supportedAttributes == null)
            {
                handle.Item.SupportedAttributes = supportedAttributes = GetAvailableAttributes(session, handle.NodeId);
            }

            // build list of values to read.
            ReadValueIdCollection valuesToRead = new ReadValueIdCollection();

            for (int ii = 0; ii < attributeIds.Length; ii++)
            {
                ReadValueId valueToRead = GetReadValueId(supportedAttributes, attributeIds[ii]);

                if (valueToRead == null)
                {
                    results[ii] = ResultIds.E_INVALIDATTRID;
                    continue;
                }

                if (valueToRead.AttributeId == Attributes.Value)
                {
                    nodeIds[ii] = valueToRead.NodeId;
                    results[ii] = ResultIds.S_OK;
                }
                else
                {
                    results[ii] = ResultIds.S_NODATA;
                }
            }

            return results;
        }

        /// <summary>
        /// Gets the annotations property node id.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="itemHandle">The item handle.</param>
        /// <returns></returns>
        public NodeId GetAnnotationsPropertyNodeId(Session session, HdaItemHandle itemHandle)
        {
            // check handle.
            InternalHandle handle = itemHandle as InternalHandle;

            if (handle == null)
            {
                return null;
            }

            // look up the supported attributes for an item.
            ReadValueIdCollection supportedAttributes = handle.Item.SupportedAttributes;

            if (supportedAttributes == null)
            {
                handle.Item.SupportedAttributes = supportedAttributes = GetAvailableAttributes(session, handle.NodeId);
            }

            // check if annotations are supported.
            ReadValueId valueToRead = GetReadValueId(supportedAttributes, ComHdaProxy.INTERNAL_ATTRIBUTE_ANNOTATION);

            if (valueToRead == null)
            {
                return null;
            }

            // return node id.
            return valueToRead.NodeId;
        }

        /// <summary>
        /// Converts a UA value to an HDA attribute value.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="values">The values.</param>
        /// <param name="index">The index.</param>
        /// <returns></returns>
        private DaValue GetAttributeValue(Session session, uint attributeId, DataValueCollection values, int index)
        {
            switch (attributeId)
            {
                case Constants.OPCHDA_DATA_TYPE:
                {
                    DaValue result = new DaValue();
                    DataValue value = values[index];

                    // check for valid node.
                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        result.Error = ResultIds.E_UNKNOWNITEMID;
                        return result;
                    }

                    // covert to var type.
                    NodeId dataTypeId = value.GetValue<NodeId>(DataTypeIds.BaseDataType);
                    int valueRank = values[index+1].GetValue<int>(ValueRanks.Scalar);

                    BuiltInType builtInType = DataTypes.GetBuiltInType(dataTypeId, session.TypeTree);
                    TypeInfo typeInfo = new TypeInfo(builtInType, valueRank);
                    short varType = (short)ComUtils.GetVarType(typeInfo);

                    result.Value = varType;
                    result.Quality = ComUtils.GetQualityCode(value.StatusCode);
                    result.Timestamp = value.ServerTimestamp;
                    result.Error = ResultIds.S_OK;

                    return result;
                }

                case Constants.OPCHDA_DESCRIPTION:
                {
                    DataValue value = values[index];

                    if (value.StatusCode == StatusCodes.BadAttributeIdInvalid)
                    {
                        DaValue result = new DaValue();
                        result.Error = ResultIds.E_INVALIDATTRID;
                        return result;
                    }

                    return m_mapper.GetLocalDataValue(value);
                }

                default:
                {
                    return ComHdaProxy.GetAttributeValue(session, m_mapper, attributeId, values[index]);
                }
            }
        }

        /// <summary>
        /// Lookups the handle.
        /// </summary>
        /// <param name="serverHandle">The server handle.</param>
        /// <returns></returns>
        public HdaItemHandle LookupHandle(int serverHandle)
        {
            lock (m_lock)
            {
                InternalHandle handle = null;

                if (!m_handles.TryGetValue(serverHandle, out handle))
                {
                    return null;
                }

                return handle;
            }
        }

        /// <summary>
        /// Releases the item handles.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="serverHandles">The server handles.</param>
        /// <returns></returns>
        public int[] ReleaseItemHandles(Session session, int[] serverHandles)
        {
            int[] errors = new int[serverHandles.Length];

            for (int ii = 0; ii < serverHandles.Length; ii++)
            {
                InternalHandle handle = null;

                lock (m_lock)
                {
                    if (!m_handles.TryGetValue(serverHandles[ii], out handle))
                    {
                        errors[ii] = ResultIds.E_INVALIDHANDLE;
                        continue;
                    }

                    Utils.Trace("Released Handle: {0} {1}", handle.ServerHandle, handle.NodeId);
                    m_handles.Remove(handle.ServerHandle);
                    handle.Item.Refs--;
                }
                
                errors[ii] = ResultIds.S_OK;
            }

            return errors;
        }

        /// <summary>
        /// Gets the item handles.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="itemIds">The item ids.</param>
        /// <param name="clientHandles">The client handles.</param>
        /// <param name="validateOnly">if set to <c>true</c> handles are not created and item ids are only validated.</param>
        /// <returns>The handles containing any error information.</returns>
        public HdaItemHandle[] GetItemHandles(Session session, string[] itemIds, int[] clientHandles, bool validateOnly)
        {
            HdaItemHandle[] handles = new HdaItemHandle[itemIds.Length];
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            for (int ii = 0; ii < itemIds.Length; ii++)
            {
                InternalHandle handle = new InternalHandle();
                handles[ii] = handle;

                if (clientHandles != null)
                {
                    handle.ClientHandle = clientHandles[ii];
                }

                string itemId = itemIds[ii];

                if (String.IsNullOrEmpty(itemId))
                {
                    handle.Error = ResultIds.E_INVALIDITEMID;
                    continue;
                }

                // check if item has already been assigned.
                Item item = null;

                if (!validateOnly)
                {
                    lock (m_lock)
                    {
                        if (m_items.TryGetValue(itemId, out item))
                        {
                            handle.NodeId = item.NodeId;
                            handle.ServerHandle = ++m_lastServerHandle;
                            handle.Item = item;
                            item.Refs++;
                            m_handles[handle.ServerHandle] = handle;
                            Utils.Trace("Created Handle: {0} {1}", handle.ServerHandle, handle.NodeId);
                            continue;
                        }
                    }
                }

                // create a new item.
                handle.Item = item = new Item();
                item.ItemId = itemId;                
                handle.Error = ResultIds.S_OK; // assume valid for no - set to an error when detected.

                handle.NodeId = item.NodeId = m_mapper.GetRemoteNodeId(itemId);

                nodesToRead.Add(Construct(handle, Attributes.UserAccessLevel));
                nodesToRead.Add(Construct(handle, Attributes.DisplayName));
                nodesToRead.Add(Construct(handle, Attributes.Description));
                nodesToRead.Add(Construct(handle, Attributes.DataType));
                nodesToRead.Add(Construct(handle, Attributes.ValueRank));
                nodesToRead.Add(Construct(handle, Attributes.Historizing));
            }

            // check if nothing to do.
            if (nodesToRead.Count == 0)
            {
                return handles;
            }

            DataValueCollection values = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            // read values from the UA server.
            session.Read(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                out values,
                out diagnosticInfos);

            // validate response from the UA server.
            ClientBase.ValidateResponse(values, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            // assign a local handle to all valid items.
            NodeIdCollection nodesToRegister = new NodeIdCollection();
            List<InternalHandle> items = new List<InternalHandle>();

            for (int ii = 0; ii < nodesToRead.Count; ii++)
            {
                InternalHandle handle = (InternalHandle)nodesToRead[ii].Handle;
                DataValue value = values[ii];
                Item item = handle.Item;
                
                // check status codes.
                if (StatusCode.IsBad(value.StatusCode))
                {
                    // description is an optional attribute.
                    if (nodesToRead[ii].AttributeId != Attributes.Description)
                    {
                        handle.Error = ResultIds.E_UNKNOWNITEMID;
                    }

                    continue;
                }

                // check access level.
                if (nodesToRead[ii].AttributeId == Attributes.UserAccessLevel)
                {
                    byte accessLevel = value.GetValue<byte>(AccessLevels.None);

                    if ((accessLevel & AccessLevels.HistoryRead) == 0)
                    {
                        handle.Error = ResultIds.E_UNKNOWNITEMID;
                        continue;
                    }
                }
                
                // save attribute.
                switch (nodesToRead[ii].AttributeId)
                {
                    case Attributes.DisplayName: { item.DisplayName = value.GetValue<LocalizedText>(null); break; }
                    case Attributes.Description: { item.Description = value.GetValue<LocalizedText>(null); break; }
                    case Attributes.DataType: { item.DataType = value.GetValue<NodeId>(null); break; }
                    case Attributes.ValueRank: { item.ValueRank = value.GetValue<int>(ValueRanks.Scalar); break; }
                    case Attributes.Historizing: { item.Historizing = value.GetValue<bool>(false); break; }
                }

                // should have all item metadata when processing the historizing attribute result.
                if (nodesToRead[ii].AttributeId == Attributes.Historizing)
                {
                    // check for a fatal error with one or more mandatory attributes.
                    if (handle.Error != ResultIds.S_OK)
                    {
                        continue;
                    }

                    BuiltInType builtInType = DataTypes.GetBuiltInType(item.DataType, session.TypeTree);
                    item.RemoteType = new TypeInfo(builtInType, item.ValueRank);

                    if (!validateOnly)
                    {
                        nodesToRegister.Add(item.NodeId);
                        items.Add(handle);

                        lock (m_lock)
                        {
                            m_items[handle.Item.ItemId] = handle.Item;
                            handle.ServerHandle = ++m_lastServerHandle;
                            handle.NodeId = handle.Item.NodeId;
                            handle.Item.Refs++;
                            m_handles[handle.ServerHandle] = handle;
                            Utils.Trace("Created Handle: {0} {1}", handle.ServerHandle, handle.NodeId);
                        }
                    }
                }
            }

            return handles;
        }
        
        private class InternalHandle : HdaItemHandle
        {
            public Item Item;
        }

        private class Item
        {
            public string ItemId;
            public NodeId NodeId;
            public LocalizedText DisplayName;
            public LocalizedText Description;
            public NodeId DataType;
            public int ValueRank;
            public bool Historizing;
            public ReadValueIdCollection SupportedAttributes;
            public int Refs;
            public TypeInfo RemoteType;
        }

        #region Private Fields
        private object m_lock = new object();
        private Dictionary<string,Item> m_items;
        private Dictionary<int,InternalHandle> m_handles;
        private ComNamespaceMapper m_mapper;
        private int m_lastServerHandle;
        #endregion
    }
}
