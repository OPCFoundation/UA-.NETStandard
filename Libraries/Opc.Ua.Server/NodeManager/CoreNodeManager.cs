/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The default node manager for the server.
    /// </summary>
    /// <remarks>
    /// Every Server has one instance of this NodeManager.
    /// It stores objects that implement ILocalNode and indexes them by NodeId.
    /// </remarks>
    public class CoreNodeManager : INodeManager, IDisposable
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public CoreNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ushort dynamicNamespaceIndex)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            Server = server ?? throw new ArgumentNullException(nameof(server));
            m_nodes = new NodeTable(server.NamespaceUris, server.ServerUris, server.TypeTree);
            m_monitoredItems = [];
            m_defaultMinimumSamplingInterval = 1000;
            m_namespaceUris = [];
            m_dynamicNamespaceIndex = dynamicNamespaceIndex;

            // use namespace 1 if out of range.
            if (m_dynamicNamespaceIndex == 0 ||
                m_dynamicNamespaceIndex >= server.NamespaceUris.Count)
            {
                m_dynamicNamespaceIndex = 1;
            }

            m_samplingGroupManager = new SamplingGroupManager(
                server,
                this,
                (uint)configuration.ServerConfiguration.MaxNotificationQueueSize,
                (uint)configuration.ServerConfiguration.MaxDurableNotificationQueueSize,
                configuration.ServerConfiguration.AvailableSamplingRates);
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                List<INode> nodes = null;

                lock (DataLock)
                {
                    nodes = [.. m_nodes];
                    m_nodes.Clear();

                    m_monitoredItems.Clear();
                }

                foreach (INode node in nodes)
                {
                    Utils.SilentDispose(node);
                }

                Utils.SilentDispose(m_samplingGroupManager);
            }
        }

        /// <summary>
        /// Acquires the lock on the node manager.
        /// </summary>
        public object DataLock { get; } = new object();

        /// <summary>
        /// Imports the nodes from a dictionary of NodeState objects.
        /// </summary>
        public void ImportNodes(ISystemContext context, IEnumerable<NodeState> predefinedNodes)
        {
            ImportNodes(context, predefinedNodes, false);
        }

        /// <summary>
        /// Imports the nodes from a dictionary of NodeState objects.
        /// </summary>
        internal void ImportNodes(
            ISystemContext context,
            IEnumerable<NodeState> predefinedNodes,
            bool isInternal)
        {
            var nodesToExport = new NodeTable(
                Server.NamespaceUris,
                Server.ServerUris,
                Server.TypeTree);

            foreach (NodeState node in predefinedNodes)
            {
                node.Export(context, nodesToExport);
            }

            lock (Server.CoreNodeManager.DataLock)
            {
                foreach (ILocalNode nodeToExport in nodesToExport.OfType<ILocalNode>())
                {
                    Server.CoreNodeManager.AttachNode(nodeToExport, isInternal);
                }
            }
        }

        /// <inheritdoc/>
        public IEnumerable<string> NamespaceUris => m_namespaceUris;

        /// <inheritdoc/>
        /// <remarks>
        /// Populates the NodeManager by loading the standard nodes from an XML file stored as an embedded resource.
        /// </remarks>
        public void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            // TBD
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Disposes all of the nodes.
        /// </remarks>
        public void DeleteAddressSpace()
        {
            var nodesToDispose = new List<IDisposable>();

            lock (DataLock)
            {
                // collect nodes to dispose.
                foreach (INode node in m_nodes)
                {
                    if (node is IDisposable disposable)
                    {
                        nodesToDispose.Add(disposable);
                    }
                }

                m_nodes.Clear();
            }

            // dispose of the nodes.
            foreach (IDisposable disposable in nodesToDispose)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception e)
                {
                    Utils.LogError(e, "Unexpected error disposing a Node object.");
                }
            }
        }

        /// <inheritdoc/>
        public object GetManagerHandle(NodeId nodeId)
        {
            lock (DataLock)
            {
                if (NodeId.IsNull(nodeId))
                {
                    return null;
                }

                return GetLocalNode(nodeId);
            }
        }

        /// <inheritdoc/>
        public void TranslateBrowsePath(
            OperationContext context,
            object sourceHandle,
            RelativePathElement relativePath,
            IList<ExpandedNodeId> targetIds,
            IList<NodeId> unresolvedTargetIds)
        {
            if (sourceHandle == null)
            {
                throw new ArgumentNullException(nameof(sourceHandle));
            }

            if (relativePath == null)
            {
                throw new ArgumentNullException(nameof(relativePath));
            }

            if (targetIds == null)
            {
                throw new ArgumentNullException(nameof(targetIds));
            }

            if (unresolvedTargetIds == null)
            {
                throw new ArgumentNullException(nameof(unresolvedTargetIds));
            }

            // check for valid handle.
            if (sourceHandle is not ILocalNode source)
            {
                return;
            }

            lock (DataLock)
            {
                // find the references that meet the filter criteria.
                IList<IReference> references = source.References.Find(
                    relativePath.ReferenceTypeId,
                    relativePath.IsInverse,
                    relativePath.IncludeSubtypes,
                    Server.TypeTree);

                // nothing more to do.
                if (references == null || references.Count == 0)
                {
                    return;
                }

                // find targets with matching browse names.
                foreach (IReference reference in references)
                {
                    INode target = GetLocalNode(reference.TargetId);

                    // target is not known to the node manager.
                    if (target == null)
                    {
                        // ignore unknown external references.
                        if (reference.TargetId.IsAbsolute)
                        {
                            continue;
                        }

                        // caller must check the browse name.
                        unresolvedTargetIds.Add((NodeId)reference.TargetId);
                        continue;
                    }

                    // check browse name.
                    if (target.BrowseName == relativePath.TargetName)
                    {
                        targetIds.Add(reference.TargetId);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void Browse(
            OperationContext context,
            ref ContinuationPoint continuationPoint,
            IList<ReferenceDescription> references)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (continuationPoint == null)
            {
                throw new ArgumentNullException(nameof(continuationPoint));
            }

            if (references == null)
            {
                throw new ArgumentNullException(nameof(references));
            }

            // check for valid handle.
            if (continuationPoint.NodeToBrowse is not ILocalNode source)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }

            // check for view.
            if (!ViewDescription.IsDefault(continuationPoint.View))
            {
                throw new ServiceResultException(StatusCodes.BadViewIdUnknown);
            }

            lock (DataLock)
            {
                // construct list of references.
                uint maxResultsToReturn = continuationPoint.MaxResultsToReturn;

                // get previous enumerator.

                // fetch a snapshot all references for node.
                if (continuationPoint.Data is not IEnumerator<IReference> enumerator)
                {
                    enumerator = GetEnumerator(source.References);
                    enumerator.MoveNext();
                }

                static IEnumerator<IReference> GetEnumerator(IReferenceCollection references)
                {
                    var copy = new List<IReference>(references);
                    return copy.GetEnumerator();
                }

                do
                {
                    IReference reference = enumerator.Current;

                    // silently ignore bad values.
                    if (reference == null ||
                        NodeId.IsNull(reference.ReferenceTypeId) ||
                        NodeId.IsNull(reference.TargetId))
                    {
                        continue;
                    }

                    // apply browse filters.
                    bool include = ApplyBrowseFilters(
                        reference,
                        continuationPoint.BrowseDirection,
                        continuationPoint.ReferenceTypeId,
                        continuationPoint.IncludeSubtypes);

                    if (include)
                    {
                        var description = new ReferenceDescription { NodeId = reference.TargetId };
                        description.SetReferenceType(
                            continuationPoint.ResultMask,
                            reference.ReferenceTypeId,
                            !reference.IsInverse);

                        // only fetch the metadata if it is requested.
                        if (continuationPoint.TargetAttributesRequired)
                        {
                            // get the metadata for the node.
                            NodeMetadata metadata = GetNodeMetadata(
                                context,
                                GetManagerHandle(reference.TargetId),
                                continuationPoint.ResultMask);

                            // update description with local node metadata.
                            if (metadata != null)
                            {
                                description.SetTargetAttributes(
                                    continuationPoint.ResultMask,
                                    metadata.NodeClass,
                                    metadata.BrowseName,
                                    metadata.DisplayName,
                                    metadata.TypeDefinition);

                                // check node class mask.
                                if (!CheckNodeClassMask(
                                    continuationPoint.NodeClassMask,
                                    description.NodeClass))
                                {
                                    continue;
                                }
                            }
                            // any target that is not remote must be owned by another node manager.
                            else if (!reference.TargetId.IsAbsolute)
                            {
                                description.Unfiltered = true;
                            }
                        }

                        // add reference to list.
                        references.Add(description);

                        // construct continuation point if max results reached.
                        if (maxResultsToReturn > 0 && references.Count >= maxResultsToReturn)
                        {
                            continuationPoint.Index = 0;
                            continuationPoint.Data = enumerator;
                            enumerator.MoveNext();
                            return;
                        }
                    }
                } while (enumerator.MoveNext());

                // nothing more to browse if it exits from the loop normally.
                continuationPoint.Dispose();
                continuationPoint = null;
            }
        }

        /// <summary>
        /// Returns true if the target meets the filter criteria.
        /// </summary>
        private bool ApplyBrowseFilters(
            IReference reference,
            BrowseDirection browseDirection,
            NodeId referenceTypeId,
            bool includeSubtypes)
        {
            // check browse direction.
            if (reference.IsInverse)
            {
                if (browseDirection == BrowseDirection.Forward)
                {
                    return false;
                }
            }
            else if (browseDirection == BrowseDirection.Inverse)
            {
                return false;
            }

            // check reference type filter.
            if (!NodeId.IsNull(referenceTypeId) && reference.ReferenceTypeId != referenceTypeId)
            {
                return includeSubtypes &&
                    Server.TypeTree.IsTypeOf(reference.ReferenceTypeId, referenceTypeId);
            }

            // include reference for now.
            return true;
        }

        /// <inheritdoc/>
        public NodeMetadata GetNodeMetadata(
            OperationContext context,
            object targetHandle,
            BrowseResultMask resultMask)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // find target.
            if (targetHandle is not ILocalNode target)
            {
                return null;
            }

            lock (DataLock)
            {
                // copy the default metadata.
                var metadata = new NodeMetadata(target, target.NodeId);

                // copy target attributes.
                if (((int)resultMask & (int)BrowseResultMask.NodeClass) != 0)
                {
                    metadata.NodeClass = target.NodeClass;
                }

                if (((int)resultMask & (int)BrowseResultMask.BrowseName) != 0)
                {
                    metadata.BrowseName = target.BrowseName;
                }

                if (((int)resultMask & (int)BrowseResultMask.DisplayName) != 0)
                {
                    metadata.DisplayName = target.DisplayName;

                    // check if the display name can be localized.
                    if (!string.IsNullOrEmpty(metadata.DisplayName.Key))
                    {
                        metadata.DisplayName = Server.ResourceManager.Translate(
                            context.PreferredLocales,
                            metadata.DisplayName);
                    }
                }

                metadata.WriteMask = target.WriteMask;

                if (metadata.WriteMask != AttributeWriteMask.None)
                {
                    var value = new DataValue((uint)(int)target.UserWriteMask);
                    ServiceResult result = target.Read(context, Attributes.UserWriteMask, value);

                    if (ServiceResult.IsBad(result))
                    {
                        metadata.WriteMask = AttributeWriteMask.None;
                    }
                    else
                    {
                        metadata.WriteMask = (AttributeWriteMask)
                            (int)((uint)(int)metadata.WriteMask & (uint)value.Value);
                    }
                }

                metadata.EventNotifier = EventNotifiers.None;
                metadata.AccessLevel = AccessLevels.None;
                metadata.Executable = false;

                switch (target.NodeClass)
                {
                    case NodeClass.Object:
                        metadata.EventNotifier = ((IObject)target).EventNotifier;
                        break;
                    case NodeClass.View:
                        metadata.EventNotifier = ((IView)target).EventNotifier;
                        break;
                    case NodeClass.Variable:
                    {
                        var variable = (IVariable)target;
                        metadata.DataType = variable.DataType;
                        metadata.ValueRank = variable.ValueRank;
                        metadata.ArrayDimensions = variable.ArrayDimensions;
                        metadata.AccessLevel = variable.AccessLevel;

                        var value = new DataValue(variable.UserAccessLevel);
                        ServiceResult result = variable.Read(
                            context,
                            Attributes.UserAccessLevel,
                            value);

                        if (ServiceResult.IsBad(result))
                        {
                            metadata.AccessLevel = 0;
                            break;
                        }

                        metadata.AccessLevel = (byte)(metadata.AccessLevel & (byte)value.Value);
                        break;
                    }
                    case NodeClass.Method:
                    {
                        var method = (IMethod)target;
                        metadata.Executable = method.Executable;

                        if (metadata.Executable)
                        {
                            var value = new DataValue(method.UserExecutable);
                            ServiceResult result = method.Read(
                                context,
                                Attributes.UserExecutable,
                                value);

                            if (ServiceResult.IsBad(result))
                            {
                                metadata.Executable = false;
                                break;
                            }

                            metadata.Executable = (bool)value.Value;
                        }

                        break;
                    }
                }

                // look up type definition.
                if (((int)resultMask & (int)BrowseResultMask.TypeDefinition) != 0 &&
                    target.NodeClass is NodeClass.Variable or NodeClass.Object)
                {
                    metadata.TypeDefinition = target.TypeDefinitionId;
                }

                // Set AccessRestrictions and RolePermissions
                var node = (Node)target;
                metadata.AccessRestrictions = (AccessRestrictionType)node.AccessRestrictions;
                metadata.RolePermissions = node.RolePermissions;
                metadata.UserRolePermissions = node.UserRolePermissions;

                // check if NamespaceMetadata is defined for NamespaceUri
                string namespaceUri = Server.NamespaceUris.GetString(target.NodeId.NamespaceIndex);
                NamespaceMetadataState namespaceMetadataState =
                    Server.NodeManager.ConfigurationNodeManager
                        .GetNamespaceMetadataState(namespaceUri);
                if (namespaceMetadataState != null)
                {
                    metadata.DefaultAccessRestrictions = (AccessRestrictionType)
                        namespaceMetadataState.DefaultAccessRestrictions.Value;
                    metadata.DefaultRolePermissions = namespaceMetadataState.DefaultRolePermissions
                        .Value;
                    metadata.DefaultUserRolePermissions = namespaceMetadataState
                        .DefaultUserRolePermissions
                        .Value;
                }

                // return metadata.
                return metadata;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// This method must not be called without first acquiring
        /// </remarks>
        public void AddReferences(IDictionary<NodeId, IList<IReference>> references)
        {
            if (references == null)
            {
                throw new ArgumentNullException(nameof(references));
            }

            lock (DataLock)
            {
                IEnumerator<KeyValuePair<NodeId, IList<IReference>>> enumerator = references
                    .GetEnumerator();

                while (enumerator.MoveNext())
                {
                    ILocalNode actualNode = GetLocalNode(enumerator.Current.Key);

                    if (actualNode != null)
                    {
                        foreach (IReference reference in enumerator.Current.Value)
                        {
                            AddReference(
                                actualNode,
                                reference.ReferenceTypeId,
                                reference.IsInverse,
                                reference.TargetId);
                        }
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void Read(
            OperationContext context,
            double maxAge,
            IList<ReadValueId> nodesToRead,
            IList<DataValue> values,
            IList<ServiceResult> errors)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (nodesToRead == null)
            {
                throw new ArgumentNullException(nameof(nodesToRead));
            }

            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            lock (DataLock)
            {
                for (int ii = 0; ii < nodesToRead.Count; ii++)
                {
                    ReadValueId nodeToRead = nodesToRead[ii];

                    // skip items that have already been processed.
                    if (nodeToRead.Processed)
                    {
                        continue;
                    }

                    // look up the node.
                    ILocalNode node = GetLocalNode(nodeToRead.NodeId);

                    if (node == null)
                    {
                        continue;
                    }

                    DataValue value = values[ii] = new DataValue();

                    value.Value = null;
                    value.ServerTimestamp = DateTime.UtcNow;
                    value.SourceTimestamp = DateTime.MinValue;
                    value.StatusCode = StatusCodes.BadAttributeIdInvalid;

                    // owned by this node manager.
                    nodeToRead.Processed = true;

                    // read the default value (also verifies that the attribute id is valid for the node).
                    ServiceResult error = node.Read(context, nodeToRead.AttributeId, value);

                    if (ServiceResult.IsBad(error))
                    {
                        errors[ii] = error;
                        continue;
                    }

                    // always use default value for base attributes.
                    bool useDefault = false;

                    switch (nodeToRead.AttributeId)
                    {
                        case Attributes.NodeId:
                        case Attributes.NodeClass:
                        case Attributes.BrowseName:
                            useDefault = true;
                            break;
                    }

                    if (useDefault)
                    {
                        errors[ii] = error;
                        continue;
                    }

                    // apply index range to value attributes.
                    if (nodeToRead.AttributeId == Attributes.Value)
                    {
                        object defaultValue = value.Value;

                        error = nodeToRead.ParsedIndexRange.ApplyRange(ref defaultValue);

                        if (ServiceResult.IsBad(error))
                        {
                            value.Value = null;
                            errors[ii] = error;
                            continue;
                        }

                        // apply data encoding.
                        if (!QualifiedName.IsNull(nodeToRead.DataEncoding))
                        {
                            error = EncodeableObject.ApplyDataEncoding(
                                Server.MessageContext,
                                nodeToRead.DataEncoding,
                                ref defaultValue);

                            if (ServiceResult.IsBad(error))
                            {
                                value.Value = null;
                                errors[ii] = error;
                                continue;
                            }
                        }

                        value.Value = defaultValue;

                        // don't replace timestamp if it was set in the NodeSource
                        if (value.SourceTimestamp == DateTime.MinValue)
                        {
                            value.SourceTimestamp = DateTime.UtcNow;
                        }
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void HistoryRead(
            OperationContext context,
            HistoryReadDetails details,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            IList<HistoryReadValueId> nodesToRead,
            IList<HistoryReadResult> results,
            IList<ServiceResult> errors)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (details == null)
            {
                throw new ArgumentNullException(nameof(details));
            }

            if (nodesToRead == null)
            {
                throw new ArgumentNullException(nameof(nodesToRead));
            }

            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            lock (DataLock)
            {
                for (int ii = 0; ii < nodesToRead.Count; ii++)
                {
                    HistoryReadValueId nodeToRead = nodesToRead[ii];

                    // skip items that have already been processed.
                    if (nodeToRead.Processed)
                    {
                        continue;
                    }

                    // look up the node.
                    ILocalNode node = GetLocalNode(nodeToRead.NodeId);

                    if (node == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    nodeToRead.Processed = true;

                    errors[ii] = StatusCodes.BadNotReadable;
                }
            }
        }

        /// <inheritdoc/>
        public void Write(
            OperationContext context,
            IList<WriteValue> nodesToWrite,
            IList<ServiceResult> errors)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (nodesToWrite == null)
            {
                throw new ArgumentNullException(nameof(nodesToWrite));
            }

            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            lock (DataLock)
            {
                for (int ii = 0; ii < nodesToWrite.Count; ii++)
                {
                    WriteValue nodeToWrite = nodesToWrite[ii];

                    // skip items that have already been processed.
                    if (nodeToWrite.Processed)
                    {
                        continue;
                    }

                    // look up the node.
                    ILocalNode node = GetLocalNode(nodeToWrite.NodeId);

                    if (node == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    nodeToWrite.Processed = true;

                    if (!node.SupportsAttribute(nodeToWrite.AttributeId))
                    {
                        errors[ii] = StatusCodes.BadAttributeIdInvalid;
                        continue;
                    }

                    // fetch the node metadata.
                    NodeMetadata metadata = GetNodeMetadata(context, node, BrowseResultMask.All);

                    // check access.
                    bool writeable = true;
                    ServiceResult error = null;

                    // determine access rights.
                    switch (nodeToWrite.AttributeId)
                    {
                        case Attributes.NodeId:
                        case Attributes.NodeClass:
                        case Attributes.AccessLevel:
                        case Attributes.UserAccessLevel:
                        case Attributes.Executable:
                        case Attributes.UserExecutable:
                        case Attributes.EventNotifier:
                            writeable = false;
                            break;
                        case Attributes.Value:
                            writeable = (metadata.AccessLevel & AccessLevels.CurrentWrite) != 0;
                            break;
                        default:
                            writeable = (metadata.WriteMask &
                                Attributes.GetMask(nodeToWrite.AttributeId)) != 0;
                            break;
                    }

                    // error if not writeable.
                    if (!writeable)
                    {
                        errors[ii] = StatusCodes.BadNotWritable;
                        continue;
                    }

                    // determine expected datatype and value rank.
                    NodeId expectedDatatypeId = metadata.DataType;
                    int expectedValueRank = metadata.ValueRank;

                    if (nodeToWrite.AttributeId != Attributes.Value)
                    {
                        expectedDatatypeId = Attributes.GetDataTypeId(nodeToWrite.AttributeId);

                        DataValue value = nodeToWrite.Value;

                        if (value.StatusCode != StatusCodes.Good ||
                            value.ServerTimestamp != DateTime.MinValue ||
                            value.SourceTimestamp != DateTime.MinValue)
                        {
                            errors[ii] = StatusCodes.BadWriteNotSupported;
                            continue;
                        }

                        expectedValueRank = ValueRanks.Scalar;

                        if (nodeToWrite.AttributeId == Attributes.ArrayDimensions)
                        {
                            expectedValueRank = ValueRanks.OneDimension;
                        }
                    }

                    // check whether value being written is an instance of the expected data type.
                    object valueToWrite = nodeToWrite.Value.Value;

                    var typeInfo = TypeInfo.IsInstanceOfDataType(
                        valueToWrite,
                        expectedDatatypeId,
                        expectedValueRank,
                        Server.NamespaceUris,
                        Server.TypeTree);

                    if (typeInfo == null)
                    {
                        errors[ii] = StatusCodes.BadTypeMismatch;
                        continue;
                    }

                    // check index range.
                    if (nodeToWrite.ParsedIndexRange.Count > 0)
                    {
                        // check index range for scalars.
                        if (typeInfo.ValueRank < 0)
                        {
                            errors[ii] = StatusCodes.BadIndexRangeInvalid;
                            continue;
                        }
                        var array = (Array)valueToWrite;

                        if (nodeToWrite.ParsedIndexRange.Count != array.Length)
                        {
                            errors[ii] = StatusCodes.BadIndexRangeInvalid;
                            continue;
                        }
                    }

                    // write the default value.
                    error = node.Write(nodeToWrite.AttributeId, nodeToWrite.Value);

                    if (ServiceResult.IsBad(error))
                    {
                        errors[ii] = error;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void HistoryUpdate(
            OperationContext context,
            Type detailsType,
            IList<HistoryUpdateDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (nodesToUpdate == null)
            {
                throw new ArgumentNullException(nameof(nodesToUpdate));
            }

            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            lock (DataLock)
            {
                for (int ii = 0; ii < nodesToUpdate.Count; ii++)
                {
                    HistoryUpdateDetails nodeToUpdate = nodesToUpdate[ii];

                    // skip items that have already been processed.
                    if (nodeToUpdate.Processed)
                    {
                        continue;
                    }

                    // look up the node.
                    ILocalNode node = GetLocalNode(nodeToUpdate.NodeId);

                    if (node == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    nodeToUpdate.Processed = true;

                    errors[ii] = StatusCodes.BadNotWritable;
                }
            }
        }

        /// <inheritdoc/>
        public void Call(
            OperationContext context,
            IList<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> results,
            IList<ServiceResult> errors)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (methodsToCall == null)
            {
                throw new ArgumentNullException(nameof(methodsToCall));
            }

            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            lock (DataLock)
            {
                for (int ii = 0; ii < methodsToCall.Count; ii++)
                {
                    CallMethodRequest methodToCall = methodsToCall[ii];

                    // skip items that have already been processed.
                    if (methodToCall.Processed)
                    {
                        continue;
                    }

                    // look up the node.
                    ILocalNode node = GetLocalNode(methodToCall.ObjectId);

                    if (node == null)
                    {
                        continue;
                    }

                    methodToCall.Processed = true;

                    // look up the method.
                    ILocalNode method = GetLocalNode(methodToCall.MethodId);

                    if (method == null)
                    {
                        errors[ii] = ServiceResult.Create(
                            StatusCodes.BadMethodInvalid,
                            "Method is not in the address space.");
                        continue;
                    }

                    // check that the method is defined for the object.
                    if (!node.References.Exists(
                            ReferenceTypeIds.HasComponent,
                            false,
                            methodToCall.MethodId,
                            true,
                            Server.TypeTree))
                    {
                        errors[ii] = ServiceResult.Create(
                            StatusCodes.BadMethodInvalid,
                            "Method is not a component of the Object.");
                        continue;
                    }

                    errors[ii] = StatusCodes.BadNotImplemented;
                }
            }
        }

        /// <inheritdoc/>
        public ServiceResult SubscribeToEvents(
            OperationContext context,
            object sourceId,
            uint subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (sourceId == null)
            {
                throw new ArgumentNullException(nameof(sourceId));
            }

            if (monitoredItem == null)
            {
                throw new ArgumentNullException(nameof(monitoredItem));
            }

            lock (DataLock)
            {
                // validate the node.
                NodeMetadata metadata = GetNodeMetadata(
                    context,
                    sourceId,
                    BrowseResultMask.NodeClass);

                if (metadata == null)
                {
                    return StatusCodes.BadNodeIdUnknown;
                }

                // validate the node class.
                if ((((int)metadata.NodeClass & (int)NodeClass.Object) | (int)NodeClass.View) == 0)
                {
                    return StatusCodes.BadNotSupported;
                }

                // check that it supports events.
                if ((metadata.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
                {
                    return StatusCodes.BadNotSupported;
                }

                return ServiceResult.Good;
            }
        }

        /// <inheritdoc/>
        public ServiceResult SubscribeToAllEvents(
            OperationContext context,
            uint subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (monitoredItem == null)
            {
                throw new ArgumentNullException(nameof(monitoredItem));
            }

            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public ServiceResult ConditionRefresh(
            OperationContext context,
            IList<IEventMonitoredItem> monitoredItems)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Creates a set of monitored items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public void CreateMonitoredItems(
            OperationContext context,
            uint subscriptionId,
            double publishingInterval,
            TimestampsToReturn timestampsToReturn,
            IList<MonitoredItemCreateRequest> itemsToCreate,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterErrors,
            IList<IMonitoredItem> monitoredItems,
            bool createDurable,
            MonitoredItemIdFactory monitoredItemIdFactory)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (itemsToCreate == null)
            {
                throw new ArgumentNullException(nameof(itemsToCreate));
            }

            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            if (monitoredItems == null)
            {
                throw new ArgumentNullException(nameof(monitoredItems));
            }

            lock (DataLock)
            {
                for (int ii = 0; ii < errors.Count; ii++)
                {
                    MonitoredItemCreateRequest itemToCreate = itemsToCreate[ii];

                    // skip items that have already been processed.
                    if (itemToCreate.Processed)
                    {
                        continue;
                    }

                    // look up the node.
                    ILocalNode node = GetLocalNode(itemToCreate.ItemToMonitor.NodeId);

                    if (node == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    itemToCreate.Processed = true;

                    if (!node.SupportsAttribute(itemToCreate.ItemToMonitor.AttributeId))
                    {
                        errors[ii] = StatusCodes.BadAttributeIdInvalid;
                        continue;
                    }

                    // fetch the metadata for the node.
                    NodeMetadata metadata = GetNodeMetadata(context, node, BrowseResultMask.All);

                    if (itemToCreate.ItemToMonitor.AttributeId == Attributes.Value &&
                        (metadata.AccessLevel & AccessLevels.CurrentRead) == 0)
                    {
                        errors[ii] = StatusCodes.BadNotReadable;
                        continue;
                    }

                    // check value rank against index range.
                    if (itemToCreate.ItemToMonitor.ParsedIndexRange != NumericRange.Empty)
                    {
                        int valueRank = metadata.ValueRank;

                        if (itemToCreate.ItemToMonitor.AttributeId != Attributes.Value)
                        {
                            valueRank = Attributes.GetValueRank(
                                itemToCreate.ItemToMonitor.AttributeId);
                        }

                        if (valueRank == ValueRanks.Scalar)
                        {
                            errors[ii] = StatusCodes.BadIndexRangeInvalid;
                            continue;
                        }
                    }

                    // validate the filter against the node/attribute being monitored.
                    errors[ii] = ValidateFilter(
                        metadata,
                        itemToCreate.ItemToMonitor.AttributeId,
                        itemToCreate.RequestedParameters.Filter,
                        out bool rangeRequired);

                    if (ServiceResult.IsBad(errors[ii]))
                    {
                        continue;
                    }

                    // lookup EU range if required.
                    Range range = null;

                    if (rangeRequired)
                    {
                        errors[ii] = ReadEURange(context, node, out range);

                        if (ServiceResult.IsBad(errors[ii]))
                        {
                            continue;
                        }
                    }

                    // limit the sampling rate for non-value attributes.
                    double minimumSamplingInterval = m_defaultMinimumSamplingInterval;

                    if (itemToCreate.ItemToMonitor.AttributeId == Attributes.Value)
                    {
                        // use the MinimumSamplingInterval attribute to limit the sampling rate for value attributes.

                        if (node is IVariable variableNode)
                        {
                            minimumSamplingInterval = variableNode.MinimumSamplingInterval;

                            // use the default if the node does not specify one.
                            if (minimumSamplingInterval < 0)
                            {
                                minimumSamplingInterval = m_defaultMinimumSamplingInterval;
                            }
                        }
                    }

                    // create monitored item.
                    ISampledDataChangeMonitoredItem monitoredItem = m_samplingGroupManager
                        .CreateMonitoredItem(
                            context,
                            subscriptionId,
                            publishingInterval,
                            timestampsToReturn,
                            monitoredItemIdFactory.GetNextId(),
                            node,
                            itemToCreate,
                            range,
                            minimumSamplingInterval,
                            createDurable);

                    // final check for initial value
                    ServiceResult error = ReadInitialValue(context, node, monitoredItem);
                    if (ServiceResult.IsBad(error) &&
                        error.StatusCode.Code
                            is StatusCodes.BadAttributeIdInvalid
                                or StatusCodes.BadDataEncodingInvalid
                                or StatusCodes.BadDataEncodingUnsupported)
                    {
                        errors[ii] = error;
                        continue;
                    }

                    // save monitored item.
                    m_monitoredItems.Add(monitoredItem.Id, monitoredItem);

                    // update monitored item list.
                    monitoredItems[ii] = monitoredItem;

                    // errors updating the monitoring groups will be reported in notifications.
                    errors[ii] = StatusCodes.Good;
                }
            }

            // update all groups with any new items.
            m_samplingGroupManager.ApplyChanges();
        }

        /// <summary>
        /// Restore a set of monitored items after a restart.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="itemsToRestore"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException"></exception>
        public void RestoreMonitoredItems(
            IList<IStoredMonitoredItem> itemsToRestore,
            IList<IMonitoredItem> monitoredItems,
            IUserIdentity savedOwnerIdentity)
        {
            if (itemsToRestore == null)
            {
                throw new ArgumentNullException(nameof(itemsToRestore));
            }

            if (monitoredItems == null)
            {
                throw new ArgumentNullException(nameof(monitoredItems));
            }

            if (Server.IsRunning)
            {
                throw new InvalidOperationException(
                    "Subscription restore can only occur on startup");
            }

            lock (DataLock)
            {
                for (int ii = 0; ii < itemsToRestore.Count; ii++)
                {
                    IStoredMonitoredItem item = itemsToRestore[ii];

                    // skip items that have already been processed.
                    if (item.IsRestored)
                    {
                        continue;
                    }

                    // look up the node.
                    ILocalNode node = GetLocalNode(item.NodeId);

                    if (node == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    item.IsRestored = true;

                    // create monitored item.
                    ISampledDataChangeMonitoredItem monitoredItem = m_samplingGroupManager
                        .RestoreMonitoredItem(
                            node,
                            item,
                            savedOwnerIdentity);

                    // save monitored item.
                    m_monitoredItems.Add(monitoredItem.Id, monitoredItem);

                    // update monitored item list.
                    monitoredItems[ii] = monitoredItem;
                }
            }

            // update all groups with any new items.
            m_samplingGroupManager.ApplyChanges();
        }

        /// <summary>
        /// Reads the initial value for a monitored item.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node to read.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        protected virtual ServiceResult ReadInitialValue(
            OperationContext context,
            ILocalNode node,
            IDataChangeMonitoredItem2 monitoredItem)
        {
            var initialValue = new DataValue
            {
                Value = null,
                ServerTimestamp = DateTime.UtcNow,
                SourceTimestamp = DateTime.MinValue,
                StatusCode = StatusCodes.BadWaitingForInitialData
            };

            ServiceResult error = node.Read(context, monitoredItem.AttributeId, initialValue);

            if (ServiceResult.IsBad(error))
            {
                initialValue.Value = null;
                initialValue.StatusCode = error.StatusCode;
            }

            monitoredItem.QueueValue(initialValue, error, true);

            return error;
        }

        /// <summary>
        /// Modifies a set of monitored items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public void ModifyMonitoredItems(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            IList<IMonitoredItem> monitoredItems,
            IList<MonitoredItemModifyRequest> itemsToModify,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterErrors)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (monitoredItems == null)
            {
                throw new ArgumentNullException(nameof(monitoredItems));
            }

            if (itemsToModify == null)
            {
                throw new ArgumentNullException(nameof(itemsToModify));
            }

            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            lock (DataLock)
            {
                for (int ii = 0; ii < errors.Count; ii++)
                {
                    MonitoredItemModifyRequest itemToModify = itemsToModify[ii];

                    // skip items that have already been processed.
                    if (itemToModify.Processed || monitoredItems[ii] == null)
                    {
                        continue;
                    }

                    // check if the node manager created the item.
                    if (!ReferenceEquals(this, monitoredItems[ii].NodeManager))
                    {
                        continue;
                    }

                    // owned by this node manager.
                    itemToModify.Processed = true;

                    // validate monitored item.

                    if (!m_monitoredItems.TryGetValue(
                            monitoredItems[ii].Id,
                            out ISampledDataChangeMonitoredItem monitoredItem))
                    {
                        errors[ii] = StatusCodes.BadMonitoredItemIdInvalid;
                        continue;
                    }

                    if (!ReferenceEquals(monitoredItem, monitoredItems[ii]))
                    {
                        errors[ii] = StatusCodes.BadMonitoredItemIdInvalid;
                        continue;
                    }

                    // find the node being monitored.

                    if (monitoredItem.ManagerHandle is not ILocalNode node)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;
                        continue;
                    }

                    // fetch the metadata for the node.
                    NodeMetadata metadata = GetNodeMetadata(
                        context,
                        monitoredItem.ManagerHandle,
                        BrowseResultMask.All);

                    // validate the filter against the node/attribute being monitored.
                    errors[ii] = ValidateFilter(
                        metadata,
                        monitoredItem.AttributeId,
                        itemToModify.RequestedParameters.Filter,
                        out bool rangeRequired);

                    if (ServiceResult.IsBad(errors[ii]))
                    {
                        continue;
                    }

                    // lookup EU range if required.
                    Range range = null;

                    if (rangeRequired)
                    {
                        // look up EU range.
                        errors[ii] = ReadEURange(context, node, out range);

                        if (ServiceResult.IsBad(errors[ii]))
                        {
                            continue;
                        }
                    }

                    // update sampling.
                    errors[ii] = m_samplingGroupManager.ModifyMonitoredItem(
                        context,
                        timestampsToReturn,
                        monitoredItem,
                        itemToModify,
                        range);

                    // state of item did not change if an error returned here.
                    if (ServiceResult.IsBad(errors[ii]))
                    {
                        continue;
                    }

                    // item has been modified successfully.
                    // errors updating the sampling groups will be reported in notifications.
                    errors[ii] = StatusCodes.Good;
                }
            }

            // update all sampling groups.
            m_samplingGroupManager.ApplyChanges();
        }

        /// <summary>
        /// Deletes a set of monitored items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public void DeleteMonitoredItems(
            OperationContext context,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (monitoredItems == null)
            {
                throw new ArgumentNullException(nameof(monitoredItems));
            }

            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            lock (DataLock)
            {
                for (int ii = 0; ii < errors.Count; ii++)
                {
                    // skip items that have already been processed.
                    if (processedItems[ii] || monitoredItems[ii] == null)
                    {
                        continue;
                    }

                    // check if the node manager created the item.
                    if (!ReferenceEquals(this, monitoredItems[ii].NodeManager))
                    {
                        continue;
                    }

                    // owned by this node manager.
                    processedItems[ii] = true;

                    // validate monitored item.

                    if (!m_monitoredItems.TryGetValue(
                            monitoredItems[ii].Id,
                            out ISampledDataChangeMonitoredItem monitoredItem))
                    {
                        errors[ii] = StatusCodes.BadMonitoredItemIdInvalid;
                        continue;
                    }

                    if (!ReferenceEquals(monitoredItem, monitoredItems[ii]))
                    {
                        errors[ii] = StatusCodes.BadMonitoredItemIdInvalid;
                        continue;
                    }

                    // remove item.
                    m_samplingGroupManager.StopMonitoring(monitoredItem);

                    // remove association with the group.
                    m_monitoredItems.Remove(monitoredItem.Id);

                    // delete successful.
                    errors[ii] = StatusCodes.Good;
                }
            }

            // remove all items from groups.
            m_samplingGroupManager.ApplyChanges();
        }

        /// <summary>
        /// Transfers a set of monitored items.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="sendInitialValues">Whether the subscription should send initial values after transfer.</param>
        /// <param name="monitoredItems">The set of monitoring items to update.</param>
        /// <param name="processedItems">The set of processed items.</param>
        /// <param name="errors">Any errors.</param>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public virtual void TransferMonitoredItems(
            OperationContext context,
            bool sendInitialValues,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (monitoredItems == null)
            {
                throw new ArgumentNullException(nameof(monitoredItems));
            }

            if (processedItems == null)
            {
                throw new ArgumentNullException(nameof(processedItems));
            }

            lock (DataLock)
            {
                for (int ii = 0; ii < monitoredItems.Count; ii++)
                {
                    // skip items that have already been processed.
                    if (processedItems[ii] || monitoredItems[ii] == null)
                    {
                        continue;
                    }

                    // check if the node manager created the item.
                    if (!ReferenceEquals(this, monitoredItems[ii].NodeManager))
                    {
                        continue;
                    }

                    // owned by this node manager.
                    processedItems[ii] = true;

                    // validate monitored item.
                    IMonitoredItem monitoredItem = monitoredItems[ii];

                    // find the node being monitored.
                    if (monitoredItem.ManagerHandle is not ILocalNode node)
                    {
                        continue;
                    }

                    if (sendInitialValues)
                    {
                        monitoredItem.SetupResendDataTrigger();
                    }

                    errors[ii] = StatusCodes.Good;
                }
            }
        }

        /// <summary>
        /// Changes the monitoring mode for a set of monitored items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public void SetMonitoringMode(
            OperationContext context,
            MonitoringMode monitoringMode,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (monitoredItems == null)
            {
                throw new ArgumentNullException(nameof(monitoredItems));
            }

            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            lock (DataLock)
            {
                for (int ii = 0; ii < errors.Count; ii++)
                {
                    // skip items that have already been processed.
                    if (processedItems[ii] || monitoredItems[ii] == null)
                    {
                        continue;
                    }

                    // check if the node manager created the item.
                    if (!ReferenceEquals(this, monitoredItems[ii].NodeManager))
                    {
                        continue;
                    }

                    // owned by this node manager.
                    processedItems[ii] = true;

                    // validate monitored item.

                    if (!m_monitoredItems.TryGetValue(
                            monitoredItems[ii].Id,
                            out ISampledDataChangeMonitoredItem monitoredItem))
                    {
                        errors[ii] = StatusCodes.BadMonitoredItemIdInvalid;
                        continue;
                    }

                    if (!ReferenceEquals(monitoredItem, monitoredItems[ii]))
                    {
                        errors[ii] = StatusCodes.BadMonitoredItemIdInvalid;
                        continue;
                    }

                    // update monitoring mode.
                    MonitoringMode previousMode = monitoredItem.SetMonitoringMode(monitoringMode);

                    // need to provide an immediate update after enabling.
                    if (previousMode == MonitoringMode.Disabled &&
                        monitoringMode != MonitoringMode.Disabled)
                    {
                        var initialValue = new DataValue
                        {
                            ServerTimestamp = DateTime.UtcNow,
                            StatusCode = StatusCodes.BadWaitingForInitialData
                        };

                        // read the initial value.

                        if (monitoredItem.ManagerHandle is Node node)
                        {
                            ServiceResult error = node.Read(
                                context,
                                monitoredItem.AttributeId,
                                initialValue);

                            if (ServiceResult.IsBad(error))
                            {
                                initialValue.Value = null;
                                initialValue.StatusCode = error.StatusCode;
                            }
                        }

                        monitoredItem.QueueValue(initialValue, null);
                    }

                    // modify the item attributes.
                    m_samplingGroupManager.ModifyMonitoring(context, monitoredItem);

                    // item has been modified successfully.
                    // errors updating the sampling groups will be reported in notifications.
                    errors[ii] = StatusCodes.Good;
                }
            }

            // update all sampling groups.
            m_samplingGroupManager.ApplyChanges();
        }

        /// <summary>
        /// Returns true if the node class matches the node class mask.
        /// </summary>
        public static bool CheckNodeClassMask(uint nodeClassMask, NodeClass nodeClass)
        {
            if (nodeClassMask != 0)
            {
                return ((uint)nodeClass & nodeClassMask) != 0;
            }

            return true;
        }

        /// <summary>
        /// The server that the node manager belongs to.
        /// </summary>
        protected IServerInternal Server { get; }

        /// <summary>
        /// Returns an index for the NamespaceURI (Adds it to the server namespace table if it does not already exist).
        /// </summary>
        /// <remarks>
        /// Returns the server's default index (1) if the namespaceUri is empty or null.
        /// </remarks>
        public ushort GetNamespaceIndex(string namespaceUri)
        {
            int namespaceIndex = 1;

            if (!string.IsNullOrEmpty(namespaceUri))
            {
                namespaceIndex = Server.NamespaceUris.GetIndex(namespaceUri);

                if (namespaceIndex == -1)
                {
                    namespaceIndex = Server.NamespaceUris.Append(namespaceUri);
                }
            }

            return (ushort)namespaceIndex;
        }

        /// <summary>
        /// Returns all targets of the specified reference.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="sourceId"/> is <c>null</c>.</exception>
        public NodeIdCollection FindLocalNodes(
            NodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse)
        {
            if (sourceId == null)
            {
                throw new ArgumentNullException(nameof(sourceId));
            }

            if (referenceTypeId == null)
            {
                throw new ArgumentNullException(nameof(referenceTypeId));
            }

            lock (DataLock)
            {
                if (GetManagerHandle(sourceId) is not ILocalNode source)
                {
                    return null;
                }

                var targets = new NodeIdCollection();

                foreach (IReference reference in source.References)
                {
                    if (reference.IsInverse != isInverse ||
                        !Server.TypeTree.IsTypeOf(reference.ReferenceTypeId, referenceTypeId))
                    {
                        continue;
                    }

                    ExpandedNodeId targetId = reference.TargetId;

                    if (targetId.IsAbsolute)
                    {
                        continue;
                    }

                    targets.Add((NodeId)targetId);
                }

                return targets;
            }
        }

        /// <summary>
        /// Returns the id the first node with the specified browse name if it exists. null otherwise
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="sourceId"/> is <c>null</c>.</exception>
        public NodeId FindTargetId(
            NodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            QualifiedName browseName)
        {
            if (sourceId == null)
            {
                throw new ArgumentNullException(nameof(sourceId));
            }

            if (referenceTypeId == null)
            {
                throw new ArgumentNullException(nameof(referenceTypeId));
            }

            lock (DataLock)
            {
                if (GetManagerHandle(sourceId) is not ILocalNode source)
                {
                    return null;
                }

                foreach (ReferenceNode reference in source.References.OfType<ReferenceNode>())
                {
                    if (reference.IsInverse != isInverse ||
                        !Server.TypeTree.IsTypeOf(reference.ReferenceTypeId, referenceTypeId))
                    {
                        continue;
                    }

                    ExpandedNodeId targetId = reference.TargetId;

                    if (targetId.IsAbsolute)
                    {
                        continue;
                    }

                    if (GetManagerHandle((NodeId)targetId) is not ILocalNode target)
                    {
                        continue;
                    }

                    if (QualifiedName.IsNull(browseName) || target.BrowseName == browseName)
                    {
                        return (NodeId)targetId;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Returns the first target that matches the browse path.
        /// </summary>
        public NodeId Find(NodeId sourceId, string browsePath)
        {
            IList<NodeId> targets = TranslateBrowsePath(sourceId, browsePath);

            if (targets.Count > 0)
            {
                return targets[0];
            }

            return null;
        }

        /// <summary>
        /// Returns a list of targets the match the browse path.
        /// </summary>
        public IList<NodeId> TranslateBrowsePath(
            OperationContext context,
            NodeId sourceId,
            string browsePath)
        {
            return TranslateBrowsePath(
                context,
                sourceId,
                RelativePath.Parse(browsePath, Server.TypeTree));
        }

        /// <summary>
        /// Returns a list of targets the match the browse path.
        /// </summary>
        public IList<NodeId> TranslateBrowsePath(NodeId sourceId, string browsePath)
        {
            return TranslateBrowsePath(
                null,
                sourceId,
                RelativePath.Parse(browsePath, Server.TypeTree));
        }

        /// <summary>
        /// Returns a list of targets the match the browse path.
        /// </summary>
        public IList<NodeId> TranslateBrowsePath(NodeId sourceId, RelativePath relativePath)
        {
            return TranslateBrowsePath(null, sourceId, relativePath);
        }

        /// <summary>
        /// Returns a list of targets the match the browse path.
        /// </summary>
        public IList<NodeId> TranslateBrowsePath(
            OperationContext context,
            NodeId sourceId,
            RelativePath relativePath)
        {
            var targets = new List<NodeId>();

            if (relativePath == null || relativePath.Elements.Count == 0)
            {
                targets.Add(sourceId);
                return targets;
            }

            lock (DataLock)
            {
                // look up source in this node manager.
                ILocalNode source = GetLocalNode(sourceId);
                if (source == null)
                {
                    return targets;
                }
            }

            // return the set of matching targets.
            return targets;
        }

        /// <summary>
        /// Registers a source for a node.
        /// </summary>
        /// <remarks>
        /// The source could be one or more of IDataSource, IEventSource, ICallable, IHistorian or IViewManager
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="nodeId"/> is <c>null</c>.</exception>
        public void RegisterSource(NodeId nodeId, object source, object handle, bool isEventSource)
        {
            if (nodeId == null)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
        }

        /// <summary>
        /// Called when the source is no longer used.
        /// </summary>
        /// <remarks>
        /// When a source disappears it must either delete all of its nodes from the address space
        /// or unregister itself their source by calling RegisterSource with source == null.
        /// After doing that the source must call this method.
        /// </remarks>
        public void UnregisterSource(object source)
        {
        }

        /// <summary>
        /// Applys the modelling rules to any existing instance.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="instance"/> is <c>null</c>.</exception>
        public void ApplyModellingRules(
            ILocalNode instance,
            ILocalNode typeDefinition,
            ILocalNode templateDeclaration,
            ushort namespaceIndex)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            if (typeDefinition == null)
            {
                throw new ArgumentNullException(nameof(typeDefinition));
            }

            // check existing type definition.
            UpdateTypeDefinition(instance, typeDefinition.NodeId);

            // create list of declarations for the type definition (recursively collects definitions from supertypes).
            var declarations = new List<DeclarationNode>();
            BuildDeclarationList(typeDefinition, declarations);

            // add instance declaration if provided.
            if (templateDeclaration != null)
            {
                var declaration = new DeclarationNode
                {
                    Node = templateDeclaration,
                    BrowsePath = string.Empty
                };

                declarations.Add(declaration);

                BuildDeclarationList(templateDeclaration, declarations);
            }

            // build list of instances to create.
            var typeDefinitions = new List<ILocalNode>();
            var instanceDeclarations = new SortedDictionary<string, ILocalNode>();
            var possibleTargets = new SortedDictionary<NodeId, ILocalNode>();

            // create instances from declarations.
            // subtypes appear in list last so traversing the list backwards find the overridden nodes first.
            for (int ii = declarations.Count - 1; ii >= 0; ii--)
            {
                DeclarationNode declaration = declarations[ii];

                // update type definition list.
                if (string.IsNullOrEmpty(declaration.BrowsePath))
                {
                    typeDefinitions.Add(declaration.Node);
                    continue;
                }

                // skip declaration if instance already exists.
                // (i.e. the declaration was overridden).
                if (instanceDeclarations.ContainsKey(declaration.BrowsePath))
                {
                    continue;
                }

                // update instance declaration list.
                instanceDeclarations[declaration.BrowsePath] = declaration.Node;

                // save the node as a possible target of references.
                possibleTargets[declaration.Node.NodeId] = declaration.Node;
            }

            // build list of instances that already exist.
            var existingInstances = new SortedDictionary<string, ILocalNode>();
            BuildInstanceList(instance, string.Empty, existingInstances);

            // maps the instance declaration onto an instance node.
            var instancesToCreate = new Dictionary<NodeId, ILocalNode>();

            // apply modelling rules to instance declarations.
            foreach (KeyValuePair<string, ILocalNode> current in instanceDeclarations)
            {
                string browsePath = current.Key;
                ILocalNode instanceDeclaration = current.Value;

                // check if the same instance has multiple browse paths to it.
                if (instancesToCreate.TryGetValue(instanceDeclaration.NodeId, out _))
                {
                    continue;
                }

                // check for an existing instance.
                if (existingInstances.TryGetValue(browsePath, out ILocalNode newInstance))
                {
                    continue;
                }

                // apply modelling rule to determine whether to create a new instance.
                NodeId modellingRule = instanceDeclaration.ModellingRule;

                // always create a new instance if one does not already exist.
                if (modellingRule == Objects.ModellingRule_Mandatory)
                {
                    if (newInstance == null)
                    {
                        newInstance = instanceDeclaration.CreateCopy(CreateUniqueNodeId());
                        AddNode(newInstance);
                    }
                }
                // ignore optional instances unless one has been specified in the existing tree.
                else if (modellingRule == Objects.ModellingRule_Optional)
                {
                    if (newInstance == null)
                    {
                        continue;
                    }
                }
                // ignore any unknown modelling rules.
                else
                {
                    continue;
                }

                // save the mapping between the instance declaration and the new instance.
                instancesToCreate[instanceDeclaration.NodeId] = newInstance;
            }

            // add references from type definitions to top level.
            foreach (ILocalNode type in typeDefinitions)
            {
                foreach (IReference reference in type.References)
                {
                    // ignore external references from type.
                    if (reference.TargetId.IsAbsolute)
                    {
                        continue;
                    }

                    // ignore subtype references.
                    if (m_nodes.TypeTree
                        .IsTypeOf(reference.ReferenceTypeId, ReferenceTypeIds.HasSubtype))
                    {
                        continue;
                    }

                    // ignore targets that are not in the instance tree.
                    if (!instancesToCreate.TryGetValue(
                        (NodeId)reference.TargetId,
                        out ILocalNode target))
                    {
                        continue;
                    }

                    // add forward and backward reference.
                    AddReference(
                        instance,
                        reference.ReferenceTypeId,
                        reference.IsInverse,
                        target,
                        true);
                }
            }

            // add references between instance declarations.
            foreach (ILocalNode instanceDeclaration in instanceDeclarations.Values)
            {
                // find the source for the references.

                if (!instancesToCreate.TryGetValue(
                    instanceDeclaration.NodeId,
                    out ILocalNode source))
                {
                    continue;
                }

                // check if the source is a shared node.
                bool sharedNode = ReferenceEquals(instanceDeclaration, source);

                foreach (IReference reference in instanceDeclaration.References)
                {
                    // add external reference.
                    if (reference.TargetId.IsAbsolute)
                    {
                        if (!sharedNode)
                        {
                            AddReference(
                                source,
                                reference.ReferenceTypeId,
                                reference.IsInverse,
                                reference.TargetId);
                        }

                        continue;
                    }

                    // check for modelling rule.
                    if (reference.ReferenceTypeId == ReferenceTypeIds.HasModellingRule)
                    {
                        if (!source.References.Exists(
                                ReferenceTypeIds.HasModellingRule,
                                false,
                                reference.TargetId,
                                false,
                                null))
                        {
                            AddReference(
                                source,
                                reference.ReferenceTypeId,
                                false,
                                reference.TargetId);
                        }

                        continue;
                    }

                    // check for type definition.
                    if (reference.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition)
                    {
                        if (!sharedNode)
                        {
                            UpdateTypeDefinition(source, instanceDeclaration.TypeDefinitionId);
                        }

                        continue;
                    }

                    // add targets that are not in the instance tree.
                    if (!instancesToCreate.TryGetValue(
                        (NodeId)reference.TargetId,
                        out ILocalNode target))
                    {
                        // don't update shared nodes because the reference should already exist.
                        if (sharedNode)
                        {
                            continue;
                        }

                        // top level references to the type definition node were already added.
                        if (reference.TargetId == typeDefinition.NodeId)
                        {
                            continue;
                        }

                        // see if a reference is allowed.
                        if (!IsExternalReferenceAllowed(reference.ReferenceTypeId))
                        {
                            continue;
                        }

                        // add one way reference.
                        source.References.Add(
                            reference.ReferenceTypeId,
                            reference.IsInverse,
                            reference.TargetId);
                        continue;
                    }

                    // add forward and backward reference.
                    AddReference(
                        source,
                        reference.ReferenceTypeId,
                        reference.IsInverse,
                        target,
                        true);
                }
            }
        }

        /// <summary>
        /// Returns true if a one-way reference to external nodes is permitted.
        /// </summary>
        private bool IsExternalReferenceAllowed(NodeId referenceTypeId)
        {
            // always exclude hierarchial references.
            if (m_nodes.TypeTree.IsTypeOf(referenceTypeId, ReferenceTypeIds.HierarchicalReferences))
            {
                return false;
            }

            // allow one way reference to event.
            if (m_nodes.TypeTree.IsTypeOf(referenceTypeId, ReferenceTypes.GeneratesEvent))
            {
                return true;
            }

            // all other references not permitted.
            return false;
        }

        /// <summary>
        /// Updates the type definition for a node.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void UpdateTypeDefinition(ILocalNode instance, ExpandedNodeId typeDefinitionId)
        {
            // check existing type definition.
            ExpandedNodeId existingTypeId = instance.TypeDefinitionId;

            if (existingTypeId == typeDefinitionId)
            {
                return;
            }

            if (!NodeId.IsNull(existingTypeId))
            {
                if (m_nodes.TypeTree.IsTypeOf(existingTypeId, typeDefinitionId))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTypeDefinitionInvalid,
                        "Type definition {0} is not a subtype of the existing type definition {1}.",
                        existingTypeId,
                        typeDefinitionId);
                }

                DeleteReference(
                    instance,
                    ReferenceTypeIds.HasTypeDefinition,
                    false,
                    existingTypeId,
                    false);
            }

            AddReference(instance, ReferenceTypeIds.HasTypeDefinition, false, typeDefinitionId);
        }

        /// <summary>
        /// A node in the type system that is used to instantiate objects or variables.
        /// </summary>
        private class DeclarationNode
        {
            public ILocalNode Node;
            public string BrowsePath;
        }

        /// <summary>
        /// Builds the list of declaration nodes for a type definition.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="typeDefinition"/> is <c>null</c>.</exception>
        private void BuildDeclarationList(
            ILocalNode typeDefinition,
            List<DeclarationNode> declarations)
        {
            if (typeDefinition == null)
            {
                throw new ArgumentNullException(nameof(typeDefinition));
            }

            if (declarations == null)
            {
                throw new ArgumentNullException(nameof(declarations));
            }

            // guard against loops (i.e. common grandparents).
            for (int ii = 0; ii < declarations.Count; ii++)
            {
                if (ReferenceEquals(declarations[ii].Node, typeDefinition))
                {
                    return;
                }
            }

            // create the root declaration for the type.
            var declaration = new DeclarationNode
            {
                Node = typeDefinition,
                BrowsePath = string.Empty
            };

            declarations.Add(declaration);

            // follow references to supertypes first.
            foreach (
                IReference reference in typeDefinition.References
                    .Find(ReferenceTypeIds.HasSubtype, true, false, null))
            {
                ILocalNode supertype = GetLocalNode(reference.TargetId);

                if (supertype == null)
                {
                    continue;
                }

                BuildDeclarationList(supertype, declarations);
            }

            // add children of type.
            BuildDeclarationList(declaration, declarations);
        }

        /// <summary>
        /// Builds a list of declarations from the nodes aggregated by a parent.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="parent"/> is <c>null</c>.</exception>
        private void BuildDeclarationList(
            DeclarationNode parent,
            List<DeclarationNode> declarations)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (declarations == null)
            {
                throw new ArgumentNullException(nameof(declarations));
            }

            // get list of children.
            foreach (
                IReference reference in parent.Node.References.Find(
                    ReferenceTypeIds.HierarchicalReferences,
                    false,
                    true,
                    m_nodes.TypeTree))
            {
                // do not follow sub-type references.
                if (m_nodes.TypeTree
                    .IsTypeOf(reference.ReferenceTypeId, ReferenceTypeIds.HasSubtype))
                {
                    continue;
                }

                // find child (ignore children that are not in the node table).
                ILocalNode child = GetLocalNode(reference.TargetId);

                if (child == null)
                {
                    continue;
                }

                // create the declartion node.
                var declaration = new DeclarationNode
                {
                    Node = child,
                    BrowsePath = Utils.Format("{0}.{1}", parent.BrowsePath, child.BrowseName)
                };

                declarations.Add(declaration);

                // recursively include aggregated children.
                NodeId modellingRule = child.ModellingRule;

                if (modellingRule == ObjectIds.ModellingRule_Mandatory ||
                    modellingRule == ObjectIds.ModellingRule_Optional)
                {
                    BuildDeclarationList(declaration, declarations);
                }
            }
        }

        /// <summary>
        /// Builds a table of instances indexed by browse path from the nodes aggregated by a parent
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="instances"/> is <c>null</c>.</exception>
        private void BuildInstanceList(
            ILocalNode parent,
            string browsePath,
            IDictionary<string, ILocalNode> instances)
        {
            if (instances == null)
            {
                throw new ArgumentNullException(nameof(instances));
            }

            // guard against loops.
            if (instances.ContainsKey(browsePath))
            {
                return;
            }

            // index parent by browse path.
            instances[browsePath] = parent ?? throw new ArgumentNullException(nameof(parent));

            // get list of children.
            foreach (
                IReference reference in parent.References.Find(
                    ReferenceTypeIds.HierarchicalReferences,
                    false,
                    true,
                    m_nodes.TypeTree))
            {
                // find child (ignore children that are not in the node table).
                ILocalNode child = GetLocalNode(reference.TargetId);

                if (child == null)
                {
                    continue;
                }

                // recursively include aggregated children.
                BuildInstanceList(
                    child,
                    Utils.Format("{0}.{1}", browsePath, child.BrowseName),
                    instances);
            }
        }

        /// <summary>
        /// Exports a node to a nodeset.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void ExportNode(NodeId nodeId, NodeSet nodeSet)
        {
            lock (DataLock)
            {
                ILocalNode node =
                    GetLocalNode(nodeId)
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdUnknown,
                        "NodeId ({0}) does not exist.",
                        nodeId);

                ExportNode(
                    node,
                    nodeSet,
                    ((int)node.NodeClass & ((int)NodeClass.Object | (int)NodeClass.Variable)) != 0);
            }
        }

        /// <summary>
        /// Exports a node to a nodeset.
        /// </summary>
        public void ExportNode(ILocalNode node, NodeSet nodeSet, bool instance)
        {
            lock (DataLock)
            {
                // check if the node has already been added.
                NodeId exportedId = nodeSet.Export(node.NodeId, m_nodes.NamespaceUris);

                if (nodeSet.Contains(exportedId))
                {
                    return;
                }

                // add to nodeset.
                Node nodeToExport = nodeSet.Add(node, m_nodes.NamespaceUris, m_nodes.ServerUris);

                // follow children.
                foreach (ReferenceNode reference in node.References.OfType<ReferenceNode>())
                {
                    // export all references.
                    bool export = true;

                    // unless it is a subtype reference.
                    if (Server.TypeTree
                        .IsTypeOf(reference.ReferenceTypeId, ReferenceTypeIds.HasSubtype))
                    {
                        export = false;
                    }

                    if (export)
                    {
                        nodeSet.AddReference(
                            nodeToExport,
                            reference,
                            m_nodes.NamespaceUris,
                            m_nodes.ServerUris);
                    }

                    if (reference.IsInverse ||
                        Server.TypeTree
                            .IsTypeOf(reference.ReferenceTypeId, ReferenceTypeIds.HasSubtype))
                    {
                        nodeSet.AddReference(
                            nodeToExport,
                            reference,
                            m_nodes.NamespaceUris,
                            m_nodes.ServerUris);
                    }

                    if (Server.TypeTree
                        .IsTypeOf(reference.ReferenceTypeId, ReferenceTypeIds.Aggregates))
                    {
                        if (reference.IsInverse)
                        {
                            continue;
                        }

                        ILocalNode child = GetLocalNode(reference.TargetId);

                        if (child != null)
                        {
                            if (instance)
                            {
                                NodeId modellingRule = child.ModellingRule;

                                if (modellingRule != Objects.ModellingRule_Mandatory)
                                {
                                    continue;
                                }
                            }

                            ExportNode(child, nodeSet, instance);
                        }
                    }
                }
            }
        }

#if XXX
        /// <summary>
        /// Changes the type definition for an instance.
        /// </summary>
        public void ChangeTypeDefinition(NodeId instanceId, NodeId typeDefinitionId)
        {
            try
            {
                m_lock.Enter();

                // find the instance.
                ILocalNode instance = GetLocalNode(instanceId) as ILocalNode;

                if (instance == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdUnknown,
                        "NodeId ({0}) does not exist.",
                        instanceId);
                }

                // check node class.
                if (instance.NodeClass != NodeClass.Object && instance.NodeClass != NodeClass.Variable)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeClassInvalid,
                        "Node (NodeClass={0}) cannot have a type definition.",
                        instance.NodeClass);
                }

                // get current type definition.
                ExpandedNodeId existingTypeId = instance.TypeDefinitionId;

                if (existingTypeId == typeDefinitionId)
                {
                    return;
                }

                // can only change to a subtype of the existing type definition.
                if (!m_server.TypeTree.IsTypeOf(typeDefinitionId, existingTypeId))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTypeDefinitionInvalid,
                        "Type definition ({0}) must be a must subtype of the existing type definition ({1}).",
                        typeDefinitionId,
                        existingTypeId);
                }

                // find the type definition node.
                ILocalNode typeDefinition = GetLocalNode(typeDefinitionId) as ILocalNode;

                if (typeDefinition == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTypeDefinitionInvalid,
                        "TypeDefinitionId ({0}) does not exist.",
                        typeDefinitionId);
                }

                // apply modelling rules.
                NodeFactory factory = new NodeFactory(m_nodes);
                IList<ILocalNode> nodesToAdd = factory.ApplyModellingRules(
                    instance,
                    typeDefinition.NodeId,
                    ref m_lastId,
                    1);

                // add the nodes.
                foreach (Node nodeToAdd in nodesToAdd)
                {
                    AddNode(nodeToAdd);
                }
            }
            finally
            {
                m_lock.Exit();
            }
        }
#endif

        /// <summary>
        /// Deletes a node from the address sapce.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="nodeId"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public void DeleteNode(NodeId nodeId, bool deleteChildren, bool silent)
        {
            if (nodeId == null)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }

            // find the node to delete.

            if (GetManagerHandle(nodeId) is not ILocalNode node)
            {
                if (!silent)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSourceNodeIdInvalid,
                        "Node '{0}' does not exist.",
                        nodeId);
                }

                return;
            }

            bool instance = ((int)node.NodeClass &
                ((int)NodeClass.Object | (int)NodeClass.Variable)) != 0;

            var referencesToDelete = new Dictionary<NodeId, IList<IReference>>();

            if (silent)
            {
                try
                {
                    DeleteNode(node, deleteChildren, instance, referencesToDelete);
                }
                catch (Exception e)
                {
                    Utils.LogError(e, "Error deleting node: {0}", nodeId);
                }
            }
            else
            {
                DeleteNode(node, deleteChildren, instance, referencesToDelete);
            }

            if (referencesToDelete.Count > 0)
            {
                Task.Run(() => OnDeleteReferences(referencesToDelete));
            }
        }

        /// <summary>
        /// Deletes a node from the address sapce.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is <c>null</c>.</exception>
        private void DeleteNode(
            ILocalNode node,
            bool deleteChildren,
            bool instance,
            Dictionary<NodeId, IList<IReference>> referencesToDelete)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            var nodesToDelete = new List<ILocalNode>();
            var referencesForNode = new List<IReference>();

            lock (DataLock)
            {
                // remove the node.
                m_nodes.Remove(node.NodeId);

                // check need to connect subtypes to the supertype if they are being deleted.
                ExpandedNodeId supertypeId = Server.TypeTree.FindSuperType(node.NodeId);

                if (!NodeId.IsNull(supertypeId))
                {
                    Server.TypeTree.Remove(node.NodeId);
                }

                // remove any references to the node.
                foreach (IReference reference in node.References)
                {
                    // ignore remote references.
                    if (reference.TargetId.IsAbsolute)
                    {
                        continue;
                    }

                    // find the target.

                    if (GetManagerHandle(reference.TargetId) is not ILocalNode target)
                    {
                        referencesForNode.Add(reference);
                        continue;
                    }

                    // delete the backward reference.
                    target.References
                        .Remove(reference.ReferenceTypeId, !reference.IsInverse, node.NodeId);

                    // check for children that need to be deleted.
                    if (deleteChildren &&
                        Server.TypeTree
                            .IsTypeOf(reference.ReferenceTypeId, ReferenceTypeIds.Aggregates) &&
                        !reference.IsInverse)
                    {
                        nodesToDelete.Add(target);
                    }
                }

                if (referencesForNode.Count > 0)
                {
                    referencesToDelete[node.NodeId] = referencesForNode;
                }
            }

            // delete the child nodes.
            foreach (ILocalNode nodeToDelete in nodesToDelete)
            {
                DeleteNode(nodeToDelete, deleteChildren, instance, referencesToDelete);
            }
        }

        /// <summary>
        /// Deletes the external references to a node in a background thread.
        /// </summary>
        private void OnDeleteReferences(object state)
        {
            var referencesToDelete = state as Dictionary<NodeId, IList<IReference>>;

            if (state == null)
            {
                return;
            }

            foreach (KeyValuePair<NodeId, IList<IReference>> current in referencesToDelete)
            {
                try
                {
                    Server.NodeManager.DeleteReferences(current.Key, current.Value);
                }
                catch (Exception e)
                {
                    Utils.LogError(e, "Error deleting references for node: {0}", current.Key);
                }
            }
        }

        /// <summary>
        /// Verifies that the source and the target meet the restrictions imposed by the reference type.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void ValidateReference(
            ILocalNode source,
            NodeId referenceTypeId,
            bool isInverse,
            NodeClass targetNodeClass)
        {
            // find reference type.
            if (GetLocalNode(referenceTypeId) is not IReferenceType)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadReferenceTypeIdInvalid,
                    "Reference type '{0}' does not exist.",
                    referenceTypeId);
            }

            // swap the source and target for inverse references.
            NodeClass sourceNodeClass = source.NodeClass;

            if (isInverse)
            {
                sourceNodeClass = targetNodeClass;
                targetNodeClass = source.NodeClass;
            }

            // check HasComponent references.
            if (Server.TypeTree.IsTypeOf(referenceTypeId, ReferenceTypeIds.HasComponent))
            {
                if ((
                        (int)sourceNodeClass &
                        (
                            (int)NodeClass.Object |
                            (int)NodeClass.Variable |
                            (int)NodeClass.ObjectType |
                            (int)NodeClass.VariableType)
                    ) == 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadReferenceNotAllowed,
                        "Source node cannot be used with HasComponent references.");
                }

                if (((int)targetNodeClass &
                    ((int)NodeClass.Object |
                        (int)NodeClass.Variable |
                        (int)NodeClass.Method)) ==
                    0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadReferenceNotAllowed,
                        "Target node cannot be used with HasComponent references.");
                }

                if (targetNodeClass == NodeClass.Variable &&
                    ((int)targetNodeClass &
                        ((int)NodeClass.Variable | (int)NodeClass.VariableType)) == 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadReferenceNotAllowed,
                        "A Variable must be a component of an Variable or VariableType.");
                }

                if (targetNodeClass == NodeClass.Method &&
                    ((int)sourceNodeClass &
                        ((int)NodeClass.Object | (int)NodeClass.ObjectType)) == 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadReferenceNotAllowed,
                        "A Method must be a component of an Object or ObjectType.");
                }
            }

            // check HasProperty references.
            if (Server.TypeTree.IsTypeOf(referenceTypeId, ReferenceTypes.HasProperty) &&
                targetNodeClass != NodeClass.Variable)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadReferenceNotAllowed,
                    "Targets of HasProperty references must be Variables.");
            }

            // check HasSubtype references.
            if (Server.TypeTree.IsTypeOf(referenceTypeId, ReferenceTypeIds.HasSubtype))
            {
                if ((
                        (int)sourceNodeClass &
                        (
                            (int)NodeClass.DataType |
                            (int)NodeClass.ReferenceType |
                            (int)NodeClass.ObjectType |
                            (int)NodeClass.VariableType)
                    ) == 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadReferenceNotAllowed,
                        "Source node cannot be used with HasSubtype references.");
                }

                if (targetNodeClass != sourceNodeClass)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadReferenceNotAllowed,
                        "The source and target cannot be connected by a HasSubtype reference.");
                }
            }

            // TBD - check rules for other reference types.
        }

        /// <summary>
        /// Adds a reference between two existing nodes.
        /// </summary>
        public ServiceResult AddReference(
            NodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            NodeId targetId,
            bool bidirectional)
        {
            lock (DataLock)
            {
                // find source.
                if (GetManagerHandle(sourceId) is not ILocalNode source)
                {
                    return StatusCodes.BadParentNodeIdInvalid;
                }

                // add reference from target to source.
                if (bidirectional)
                {
                    // find target.
                    if (GetManagerHandle(targetId) is not ILocalNode target)
                    {
                        return StatusCodes.BadNodeIdUnknown;
                    }

                    // ensure the reference is valid.
                    ValidateReference(source, referenceTypeId, isInverse, target.NodeClass);

                    // add reference from target to source.
                    AddReferenceToLocalNode(target, referenceTypeId, !isInverse, sourceId, false);
                }

                // add reference from source to target.
                AddReferenceToLocalNode(source, referenceTypeId, isInverse, targetId, false);

                return null;
            }
        }

        /// <summary>
        /// Ensures any changes to built-in nodes are reflected in the diagnostics node manager.
        /// </summary>
        private void AddReferenceToLocalNode(
            ILocalNode source,
            NodeId referenceTypeId,
            bool isInverse,
            ExpandedNodeId targetId,
            bool isInternal)
        {
            source.References.Add(referenceTypeId, isInverse, targetId);

            if (!isInternal && source.NodeId.NamespaceIndex == 0)
            {
                lock (Server.DiagnosticsNodeManager.Lock)
                {
                    NodeState state = Server.DiagnosticsNodeManager
                        .FindPredefinedNode(source.NodeId, null);

                    if (state != null)
                    {
                        INodeBrowser browser = state.CreateBrowser(
                            Server.DefaultSystemContext,
                            null,
                            referenceTypeId,
                            true,
                            isInverse ? BrowseDirection.Inverse : BrowseDirection.Forward,
                            null,
                            null,
                            true);

                        bool found = false;

                        for (IReference reference = browser.Next();
                            reference != null;
                            reference = browser.Next())
                        {
                            if (reference.TargetId == targetId)
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            state.AddReference(referenceTypeId, isInverse, targetId);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Adds a reference between two existing nodes.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void CreateReference(
            NodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            NodeId targetId,
            bool bidirectional)
        {
            lock (DataLock)
            {
                ServiceResult result = AddReference(
                    sourceId,
                    referenceTypeId,
                    isInverse,
                    targetId,
                    bidirectional);

                if (ServiceResult.IsBad(result))
                {
                    throw new ServiceResultException(result);
                }
            }
        }

        /// <summary>
        /// Adds a reference to the address space.
        /// </summary>
        private void AddReference(
            ILocalNode source,
            NodeId referenceTypeId,
            bool isInverse,
            ILocalNode target,
            bool bidirectional)
        {
            AddReferenceToLocalNode(source, referenceTypeId, isInverse, target.NodeId, false);

            if (bidirectional)
            {
                AddReferenceToLocalNode(target, referenceTypeId, !isInverse, source.NodeId, false);
            }
        }

        /// <summary>
        /// Adds a reference to the address space.
        /// </summary>
        private void AddReference(
            ILocalNode source,
            NodeId referenceTypeId,
            bool isInverse,
            ExpandedNodeId targetId)
        {
            AddReferenceToLocalNode(source, referenceTypeId, isInverse, targetId, false);
        }

        /// <summary>
        /// Deletes a reference.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="sourceHandle"/> is <c>null</c>.</exception>
        public ServiceResult DeleteReference(
            object sourceHandle,
            NodeId referenceTypeId,
            bool isInverse,
            ExpandedNodeId targetId,
            bool deleteBidirectional)
        {
            if (sourceHandle == null)
            {
                throw new ArgumentNullException(nameof(sourceHandle));
            }

            if (referenceTypeId == null)
            {
                throw new ArgumentNullException(nameof(referenceTypeId));
            }

            if (targetId == null)
            {
                throw new ArgumentNullException(nameof(targetId));
            }

            lock (DataLock)
            {
                if (sourceHandle is not ILocalNode source)
                {
                    return StatusCodes.BadSourceNodeIdInvalid;
                }

                source.References.Remove(referenceTypeId, isInverse, targetId);

                if (deleteBidirectional)
                {
                    var target = GetManagerHandle(targetId) as ILocalNode;

                    target?.References.Remove(referenceTypeId, !isInverse, source.NodeId);
                }

                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Deletes a reference.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void DeleteReference(
            NodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            ExpandedNodeId targetId,
            bool deleteBidirectional)
        {
            ServiceResult result = DeleteReference(
                GetManagerHandle(sourceId) as ILocalNode,
                referenceTypeId,
                isInverse,
                targetId,
                deleteBidirectional);

            if (ServiceResult.IsBad(result))
            {
                throw new ServiceResultException(result);
            }
        }

        /// <summary>
        /// Adds a node to the address space.
        /// </summary>
        private void AddNode(ILocalNode node)
        {
            m_nodes.Attach(node);
        }

        /// <summary>
        /// Returns a node managed by the manager with the specified node id.
        /// </summary>
        public ILocalNode GetLocalNode(ExpandedNodeId nodeId)
        {
            if (nodeId == null)
            {
                return null;
            }

            // check for absolute declarations of local nodes.
            if (nodeId.IsAbsolute)
            {
                if (nodeId.ServerIndex != 0)
                {
                    return null;
                }

                int namespaceIndex = Server.NamespaceUris.GetIndex(nodeId.NamespaceUri);

                if (namespaceIndex < 0 || nodeId.NamespaceIndex >= Server.NamespaceUris.Count)
                {
                    return null;
                }

                return GetLocalNode(new NodeId(nodeId.Identifier, (ushort)namespaceIndex));
            }

            return GetLocalNode((NodeId)nodeId);
        }

        /// <summary>
        /// Returns a node managed by the manager with the specified node id.
        /// </summary>
        public ILocalNode GetLocalNode(
            NodeId nodeId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            QualifiedName browseName)
        {
            lock (DataLock)
            {
                return m_nodes.Find(
                    nodeId,
                    referenceTypeId,
                    isInverse,
                    includeSubtypes,
                    browseName) as ILocalNode;
            }
        }

        /// <summary>
        /// Returns a node managed by the manager with the specified node id.
        /// </summary>
        public ILocalNode GetLocalNode(NodeId nodeId)
        {
            lock (DataLock)
            {
                return m_nodes.Find(nodeId) as ILocalNode;
            }
        }

        /// <summary>
        /// Returns a list of nodes which are targets of the specified references.
        /// </summary>
        public IList<ILocalNode> GetLocalNodes(
            NodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes)
        {
            lock (DataLock)
            {
                var targets = new List<ILocalNode>();

                ILocalNode source = GetLocalNode(sourceId);

                if (source == null)
                {
                    return targets;
                }

                foreach (
                    IReference reference in source.References
                        .Find(referenceTypeId, isInverse, true, m_nodes.TypeTree))
                {
                    ILocalNode target = GetLocalNode(reference.TargetId);

                    if (target != null)
                    {
                        targets.Add(target);
                    }
                }

                return targets;
            }
        }

        /// <summary>
        /// Returns a node managed by the manager that has the specified browse name.
        /// </summary>
        public ILocalNode GetTargetNode(
            NodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            QualifiedName browseName)
        {
            lock (DataLock)
            {
                ILocalNode source = GetLocalNode(sourceId);

                if (source == null)
                {
                    return null;
                }

                return GetTargetNode(
                    source,
                    referenceTypeId,
                    isInverse,
                    includeSubtypes,
                    browseName);
            }
        }

        /// <summary>
        /// Returns a node managed by the manager that has the specified browse name.
        /// </summary>
        private ILocalNode GetTargetNode(
            ILocalNode source,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            QualifiedName browseName)
        {
            foreach (
                IReference reference in source.References.Find(
                    referenceTypeId,
                    isInverse,
                    includeSubtypes,
                    Server.TypeTree))
            {
                ILocalNode target = GetLocalNode(reference.TargetId);

                if (target == null)
                {
                    continue;
                }

                if (QualifiedName.IsNull(browseName) || browseName == target.BrowseName)
                {
                    return target;
                }
            }

            return null;
        }

        /// <summary>
        /// Attaches a node to the address space.
        /// </summary>
        public void AttachNode(ILocalNode node)
        {
            AttachNode(node, false);
        }

        /// <summary>
        /// Attaches a node to the address space.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        private void AttachNode(ILocalNode node, bool isInternal)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            lock (DataLock)
            {
                // check if node exists.
                if (m_nodes.Exists(node.NodeId))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdExists,
                        "A node with the same node id already exists: {0}",
                        node.NodeId);
                }

                // ensure reverse references exist.
                foreach (IReference reference in node.References)
                {
                    // ignore references that are always one way.
                    if (reference.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition ||
                        reference.ReferenceTypeId == ReferenceTypeIds.HasModellingRule)
                    {
                        continue;
                    }

                    // find target.
                    ILocalNode target = GetLocalNode(reference.TargetId);

                    if (target != null)
                    {
                        AddReferenceToLocalNode(
                            target,
                            reference.ReferenceTypeId,
                            !reference.IsInverse,
                            node.NodeId,
                            isInternal);
                    }
                }

                // must generate a model change event.
                AddNode(node);
            }
        }

        /// <summary>
        /// Creates a unique node identifier.
        /// </summary>
        public NodeId CreateUniqueNodeId()
        {
            return CreateUniqueNodeId(m_dynamicNamespaceIndex);
        }

        /// <inheritdoc/>
        private object GetManagerHandle(ExpandedNodeId nodeId)
        {
            lock (DataLock)
            {
                if (nodeId == null || nodeId.IsAbsolute)
                {
                    return null;
                }

                return GetLocalNode(nodeId);
            }
        }

        /// <summary>
        /// Reads the EU Range for a variable.
        /// </summary>
        private ServiceResult ReadEURange(
            OperationContext context,
            ILocalNode node,
            out Range range)
        {
            range = null;

            if (GetTargetNode(node, ReferenceTypes.HasProperty, false, true, BrowseNames.EURange)
                is not IVariable target)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            range = target.Value as Range;

            if (range == null)
            {
                return StatusCodes.BadTypeMismatch;
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Validates a filter for a monitored item.
        /// </summary>
        private ServiceResult ValidateFilter(
            NodeMetadata metadata,
            uint attributeId,
            ExtensionObject filter,
            out bool rangeRequired)
        {
            rangeRequired = false;

            // check filter.
            DataChangeFilter datachangeFilter = null;

            if (filter != null)
            {
                datachangeFilter = filter.Body as DataChangeFilter;
            }

            if (datachangeFilter != null)
            {
                // get the datatype of the node.
                NodeId datatypeId = metadata.DataType;

                // check that filter is valid.
                ServiceResult error = datachangeFilter.Validate();

                if (ServiceResult.IsBad(error))
                {
                    return error;
                }

                // check datatype of the variable.
                if (!Server.TypeTree.IsTypeOf(datatypeId, DataTypes.Number))
                {
                    return StatusCodes.BadDeadbandFilterInvalid;
                }

                // percent deadbands only allowed for analog data items.
                if (datachangeFilter.DeadbandType == (int)DeadbandType.Percent)
                {
                    ExpandedNodeId typeDefinitionId = metadata.TypeDefinition;

                    if (typeDefinitionId == null)
                    {
                        return StatusCodes.BadDeadbandFilterInvalid;
                    }

                    // percent deadbands only allowed for analog data items.
                    if (!Server.TypeTree.IsTypeOf(typeDefinitionId, VariableTypes.AnalogItemType))
                    {
                        return StatusCodes.BadDeadbandFilterInvalid;
                    }

                    // the EURange property is required to use the filter.
                    rangeRequired = true;
                }
            }

            // filter is valid
            return ServiceResult.Good;
        }

        /// <summary>
        /// Creates a new unique identifier for a node.
        /// </summary>
        private NodeId CreateUniqueNodeId(ushort namespaceIndex)
        {
            return new NodeId(Utils.IncrementIdentifier(ref m_lastId), namespaceIndex);
        }

        private readonly NodeTable m_nodes;
        private uint m_lastId;
        private readonly SamplingGroupManager m_samplingGroupManager;
        private readonly Dictionary<uint, ISampledDataChangeMonitoredItem> m_monitoredItems;
        private readonly double m_defaultMinimumSamplingInterval;
        private readonly List<string> m_namespaceUris;
        private readonly ushort m_dynamicNamespaceIndex;
    }
}
