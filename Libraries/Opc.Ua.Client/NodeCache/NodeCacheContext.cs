// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Node cache context provides
    /// </summary>
    public sealed class NodeCacheContext : INodeCacheContext
    {
        /// <summary>
        /// Create node cache context
        /// </summary>
        /// <param name="session"></param>
        public NodeCacheContext(ISession session)
        {
            Session = session;
        }

        /// <inheritdoc/>
        public ISession Session { get; }

        /// <inheritdoc/>
        public async ValueTask<ReferenceDescriptionCollection> FetchReferencesAsync(
            RequestHeader? requestHeader,
            NodeId nodeId,
            CancellationToken ct = default)
        {
            var browser = new Browser(Session, new BrowserOptions
            {
                RequestHeader = requestHeader,
                BrowseDirection = BrowseDirection.Both,
                ReferenceTypeId = NodeId.Null,
                IncludeSubtypes = true,
                NodeClassMask = 0
            });
            ResultSet<ReferenceDescriptionCollection> results =
                await browser.BrowseAsync([nodeId], ct).ConfigureAwait(false);
            return results.Results[0];
        }

        /// <inheritdoc/>
        public ValueTask<ResultSet<ReferenceDescriptionCollection>> FetchReferencesAsync(
            RequestHeader? requestHeader,
            IReadOnlyList<NodeId> nodeIds,
            CancellationToken ct = default)
        {
            if (nodeIds.Count == 0)
            {
                return new ValueTask<ResultSet<ReferenceDescriptionCollection>>(
                    ResultSet<ReferenceDescriptionCollection>.Empty);
            }
            var browser = new Browser(Session, new BrowserOptions
            {
                RequestHeader = requestHeader,
                BrowseDirection = BrowseDirection.Both,
                ReferenceTypeId = NodeId.Null,
                IncludeSubtypes = true,
                NodeClassMask = 0
            });
            return browser.BrowseAsync(nodeIds, ct);
        }

        /// <inheritdoc/>
        public async ValueTask<ResultSet<Node>> FetchNodesAsync(
            RequestHeader? requestHeader,
            IReadOnlyList<NodeId> nodeIds,
            bool skipOptionalAttributes = false,
            CancellationToken ct = default)
        {
            if (nodeIds.Count == 0)
            {
                return ResultSet<Node>.Empty;
            }

            var nodeCollection = new NodeCollection(nodeIds.Count);
            var itemsToRead = new ReadValueIdCollection(nodeIds.Count);

            // first read only nodeclasses for nodes from server.
            itemsToRead =
            [
                .. nodeIds.Select(nodeId => new ReadValueId {
                    NodeId = nodeId,
                    AttributeId = Attributes.NodeClass })
            ];

            ReadResponse readResponse = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                itemsToRead,
                ct)
                .ConfigureAwait(false);

            DataValueCollection nodeClassValues = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ClientBase.ValidateResponse(nodeClassValues, itemsToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            // second determine attributes to read per nodeclass
            var attributesPerNodeId = new List<IDictionary<uint, DataValue?>?>(nodeIds.Count);
            var serviceResults = new List<ServiceResult>(nodeIds.Count);
            var attributesToRead = new ReadValueIdCollection();

            CreateAttributesReadNodesRequest(
                readResponse.ResponseHeader,
                itemsToRead,
                nodeClassValues,
                diagnosticInfos,
                attributesToRead,
                attributesPerNodeId,
                nodeCollection,
                serviceResults,
                skipOptionalAttributes);

            if (attributesToRead.Count > 0)
            {
                readResponse = await Session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    attributesToRead,
                    ct)
                    .ConfigureAwait(false);

                DataValueCollection values = readResponse.Results;
                diagnosticInfos = readResponse.DiagnosticInfos;

                ClientBase.ValidateResponse(values, attributesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, attributesToRead);

                ProcessAttributesReadNodesResponse(
                    readResponse.ResponseHeader,
                    attributesToRead,
                    attributesPerNodeId,
                    values,
                    diagnosticInfos,
                    nodeCollection,
                    serviceResults);
            }

            return ResultSet.From(nodeCollection, serviceResults);
        }

        /// <inheritdoc/>
        public async ValueTask<ResultSet<Node>> FetchNodesAsync(
            RequestHeader? requestHeader,
            IReadOnlyList<NodeId> nodeIds,
            NodeClass nodeClass,
            bool skipOptionalAttributes = false,
            CancellationToken ct = default)
        {
            if (nodeIds.Count == 0)
            {
                return ResultSet<Node>.Empty;
            }

            if (nodeClass == NodeClass.Unspecified)
            {
                return await FetchNodesAsync(
                    requestHeader,
                    nodeIds,
                    skipOptionalAttributes, ct).ConfigureAwait(false);
            }

            var nodeCollection = new NodeCollection(nodeIds.Count);

            // determine attributes to read for nodeclass
            var attributesPerNodeId = new List<IDictionary<uint, DataValue?>?>(nodeIds.Count);
            var attributesToRead = new ReadValueIdCollection();

            CreateNodeClassAttributesReadNodesRequest(
                nodeIds,
                nodeClass,
                attributesToRead,
                attributesPerNodeId,
                nodeCollection,
                skipOptionalAttributes);

            ReadResponse readResponse = await Session.ReadAsync(
                requestHeader,
                0,
                TimestampsToReturn.Neither,
                attributesToRead,
                ct)
                .ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ClientBase.ValidateResponse(values, attributesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, attributesToRead);

            List<ServiceResult> serviceResults = new ServiceResult[nodeIds.Count].ToList();
            ProcessAttributesReadNodesResponse(
                readResponse.ResponseHeader,
                attributesToRead,
                attributesPerNodeId,
                values,
                diagnosticInfos,
                nodeCollection,
                serviceResults);

            return ResultSet.From(nodeCollection, serviceResults);
        }

        /// <inheritdoc/>
        public async ValueTask<Node> FetchNodeAsync(
            RequestHeader? requestHeader,
            NodeId nodeId,
            NodeClass nodeClass = NodeClass.Unspecified,
            bool skipOptionalAttributes = false,
            CancellationToken ct = default)
        {
            // build list of attributes.
            IDictionary<uint, DataValue?> attributes = CreateAttributes(
                nodeClass,
                skipOptionalAttributes);

            // build list of values to read.
            var itemsToRead = new ReadValueIdCollection();
            foreach (uint attributeId in attributes.Keys)
            {
                var itemToRead = new ReadValueId { NodeId = nodeId, AttributeId = attributeId };
                itemsToRead.Add(itemToRead);
            }

            // read from server.
            ReadResponse readResponse = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                itemsToRead,
                ct)
                .ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ClientBase.ValidateResponse(values, itemsToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            return ProcessReadResponse(
                readResponse.ResponseHeader,
                attributes,
                itemsToRead,
                values,
                diagnosticInfos);
        }

        /// <inheritdoc/>
        public async ValueTask<DataValue> FetchValueAsync(
            RequestHeader? requestHeader,
            NodeId nodeId,
            CancellationToken ct = default)
        {
            var itemToRead = new ReadValueId
            {
                NodeId = nodeId,
                AttributeId = Attributes.Value
            };
            var itemsToRead = new ReadValueIdCollection { itemToRead };

            // read from server.
            ReadResponse readResponse = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                itemsToRead,
                ct)
                .ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ClientBase.ValidateResponse(values, itemsToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            if (StatusCode.IsBad(values[0].StatusCode))
            {
                ServiceResult result = ClientBase.GetResult(
                    values[0].StatusCode,
                    0,
                    diagnosticInfos,
                    readResponse.ResponseHeader);
                throw new ServiceResultException(result);
            }

            return values[0];
        }

        /// <inheritdoc/>
        public async ValueTask<ResultSet<DataValue>> FetchValuesAsync(
            RequestHeader? requestHeader,
            IReadOnlyList<NodeId> nodeIds,
            CancellationToken ct = default)
        {
            if (nodeIds.Count == 0)
            {
                return ResultSet<DataValue>.Empty;
            }

            // read all values from server.
            var itemsToRead = new ReadValueIdCollection(
                nodeIds.Select(
                    nodeId => new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }));

            // read from server.
            var errors = new List<ServiceResult>(itemsToRead.Count);

            ReadResponse readResponse = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                itemsToRead,
                ct)
                .ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ClientBase.ValidateResponse(values, itemsToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            foreach (DataValue value in values)
            {
                ServiceResult result = ServiceResult.Good;
                if (StatusCode.IsBad(value.StatusCode))
                {
                    result = ClientBase.GetResult(
                        values[0].StatusCode,
                        0,
                        diagnosticInfos,
                        readResponse.ResponseHeader);
                }
                errors.Add(result);
            }

            return ResultSet.From(values, errors);
        }

        /// <summary>
        /// Creates a read request with attributes determined by the NodeClass.
        /// </summary>
        private static void CreateAttributesReadNodesRequest(
            ResponseHeader responseHeader,
            ReadValueIdCollection itemsToRead,
            DataValueCollection nodeClassValues,
            DiagnosticInfoCollection diagnosticInfos,
            ReadValueIdCollection attributesToRead,
            List<IDictionary<uint, DataValue?>?> attributesPerNodeId,
            NodeCollection nodeCollection,
            List<ServiceResult> errors,
            bool skipOptionalAttributes)
        {
            NodeClass? nodeClass;
            for (int ii = 0; ii < itemsToRead.Count; ii++)
            {
                var node = new Node { NodeId = itemsToRead[ii].NodeId };
                if (!DataValue.IsGood(nodeClassValues[ii]))
                {
                    nodeCollection.Add(node);
                    errors.Add(
                        new ServiceResult(
                            nodeClassValues[ii].StatusCode,
                            ii,
                            diagnosticInfos,
                            responseHeader.StringTable));
                    attributesPerNodeId.Add(null);
                    continue;
                }

                // check for valid node class.
                nodeClass = nodeClassValues[ii].Value as NodeClass?;

                if (nodeClass == null)
                {
                    if (nodeClassValues[ii].Value is int nc)
                    {
                        nodeClass = (NodeClass)nc;
                    }
                    else
                    {
                        nodeCollection.Add(node);
                        errors.Add(
                            ServiceResult.Create(
                                StatusCodes.BadUnexpectedError,
                                "Node does not have a valid value for NodeClass: {0}.",
                                nodeClassValues[ii].Value));
                        attributesPerNodeId.Add(null);
                        continue;
                    }
                }

                node.NodeClass = nodeClass.Value;

                Dictionary<uint, DataValue?> attributes = CreateAttributes(
                    node.NodeClass,
                    skipOptionalAttributes);
                foreach (uint attributeId in attributes.Keys)
                {
                    var itemToRead = new ReadValueId
                    {
                        NodeId = node.NodeId,
                        AttributeId = attributeId
                    };
                    attributesToRead.Add(itemToRead);
                }

                nodeCollection.Add(node);
                errors.Add(ServiceResult.Good);
                attributesPerNodeId.Add(attributes);
            }
        }

        /// <summary>
        /// Builds the node collection results based on the attribute values of the read response.
        /// </summary>
        /// <param name="responseHeader">The response requestHeader of the read request.</param>
        /// <param name="attributesToRead">The collection of all attributes to read passed in the read request.</param>
        /// <param name="attributesPerNodeId">The attributes requested per NodeId</param>
        /// <param name="values">The attribute values returned by the read request.</param>
        /// <param name="diagnosticInfos">The diagnostic info returned by the read request.</param>
        /// <param name="nodeCollection">The node collection which holds the results.</param>
        /// <param name="errors">The service results for each node.</param>
        private static void ProcessAttributesReadNodesResponse(
            ResponseHeader responseHeader,
            ReadValueIdCollection attributesToRead,
            List<IDictionary<uint, DataValue?>?> attributesPerNodeId,
            DataValueCollection values,
            DiagnosticInfoCollection diagnosticInfos,
            NodeCollection nodeCollection,
            List<ServiceResult> errors)
        {
            int readIndex = 0;
            for (int ii = 0; ii < nodeCollection.Count; ii++)
            {
                IDictionary<uint, DataValue?>? attributes = attributesPerNodeId[ii];
                if (attributes == null)
                {
                    continue;
                }

                int readCount = attributes.Count;
                var subRangeAttributes = new ReadValueIdCollection(
                    attributesToRead.GetRange(readIndex, readCount));
                var subRangeValues = new DataValueCollection(values.GetRange(readIndex, readCount));
                DiagnosticInfoCollection subRangeDiagnostics =
                    diagnosticInfos.Count > 0
                        ? [.. diagnosticInfos.GetRange(readIndex, readCount)]
                        : diagnosticInfos;
                try
                {
                    nodeCollection[ii] = ProcessReadResponse(
                        responseHeader,
                        attributes,
                        subRangeAttributes,
                        subRangeValues,
                        subRangeDiagnostics);
                    errors[ii] = ServiceResult.Good;
                }
                catch (ServiceResultException sre)
                {
                    errors[ii] = sre.Result;
                }
                readIndex += readCount;
            }
        }

        /// <summary>
        /// Creates a Node based on the read response.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static Node ProcessReadResponse(
            ResponseHeader responseHeader,
            IDictionary<uint, DataValue?> attributes,
            ReadValueIdCollection itemsToRead,
            DataValueCollection values,
            DiagnosticInfoCollection diagnosticInfos)
        {
            // process results.
            NodeClass? nodeClass = null;

            for (int ii = 0; ii < itemsToRead.Count; ii++)
            {
                uint attributeId = itemsToRead[ii].AttributeId;

                // the node probably does not exist if the node class is not found.
                if (attributeId == Attributes.NodeClass)
                {
                    if (!DataValue.IsGood(values[ii]))
                    {
                        throw ServiceResultException.Create(
                            values[ii].StatusCode,
                            ii,
                            diagnosticInfos,
                            responseHeader.StringTable);
                    }

                    // check for valid node class.
                    nodeClass = values[ii].Value as NodeClass?;
                    if (nodeClass == null)
                    {
                        if (values[ii].Value is int nc)
                        {
                            nodeClass = (NodeClass)nc;
                        }
                        else
                        {
                            throw ServiceResultException.Unexpected(
                                "Node does not have a valid value for NodeClass: {0}.",
                                values[ii].Value);
                        }
                    }
                }
                else if (!DataValue.IsGood(values[ii]))
                {
                    // check for unsupported attributes.
                    if (values[ii].StatusCode == StatusCodes.BadAttributeIdInvalid)
                    {
                        continue;
                    }

                    // ignore errors on optional attributes
                    if (StatusCode.IsBad(values[ii].StatusCode) &&
                        attributeId
                            is Attributes.AccessRestrictions
                                or Attributes.Description
                                or Attributes.RolePermissions
                                or Attributes.UserRolePermissions
                                or Attributes.UserWriteMask
                                or Attributes.WriteMask
                                or Attributes.AccessLevelEx
                                or Attributes.ArrayDimensions
                                or Attributes.DataTypeDefinition
                                or Attributes.InverseName
                                or Attributes.MinimumSamplingInterval)
                    {
                        continue;
                    }

                    // all supported attributes must be readable.
                    if (attributeId != Attributes.Value)
                    {
                        throw ServiceResultException.Create(
                            values[ii].StatusCode,
                            ii,
                            diagnosticInfos,
                            responseHeader.StringTable);
                    }
                }

                attributes[attributeId] = values[ii];
            }

            Node node;
            DataValue? value;
            switch (nodeClass)
            {
                case NodeClass.Object:
                    var objectNode = new ObjectNode();

                    value = attributes[Attributes.EventNotifier];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "Object does not support the EventNotifier attribute.");
                    }

                    objectNode.EventNotifier = value.GetValueOrDefault<byte>();
                    node = objectNode;
                    break;
                case NodeClass.ObjectType:
                    var objectTypeNode = new ObjectTypeNode();

                    value = attributes[Attributes.IsAbstract];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "ObjectType does not support the IsAbstract attribute.");
                    }

                    objectTypeNode.IsAbstract = value.GetValueOrDefault<bool>();
                    node = objectTypeNode;
                    break;
                case NodeClass.Variable:
                    var variableNode = new VariableNode();

                    // DataType Attribute
                    value = attributes[Attributes.DataType];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "Variable does not support the DataType attribute.");
                    }

                    variableNode.DataType = (NodeId)value.GetValue(typeof(NodeId));

                    // ValueRank Attribute
                    value = attributes[Attributes.ValueRank];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "Variable does not support the ValueRank attribute.");
                    }

                    variableNode.ValueRank = value.GetValueOrDefault<int>();

                    // ArrayDimensions Attribute
                    value = attributes[Attributes.ArrayDimensions];

                    if (value != null)
                    {
                        if (value.Value == null)
                        {
                            variableNode.ArrayDimensions = Array.Empty<uint>();
                        }
                        else
                        {
                            variableNode.ArrayDimensions = (uint[])value.GetValue(typeof(uint[]));
                        }
                    }

                    // AccessLevel Attribute
                    value = attributes[Attributes.AccessLevel];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "Variable does not support the AccessLevel attribute.");
                    }

                    variableNode.AccessLevel = value.GetValueOrDefault<byte>();

                    // UserAccessLevel Attribute
                    value = attributes[Attributes.UserAccessLevel];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "Variable does not support the UserAccessLevel attribute.");
                    }

                    variableNode.UserAccessLevel = value.GetValueOrDefault<byte>();

                    // Historizing Attribute
                    value = attributes[Attributes.Historizing];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "Variable does not support the Historizing attribute.");
                    }

                    variableNode.Historizing = value.GetValueOrDefault<bool>();

                    // MinimumSamplingInterval Attribute
                    value = attributes[Attributes.MinimumSamplingInterval];

                    if (value != null)
                    {
                        variableNode.MinimumSamplingInterval = Convert.ToDouble(
                            attributes[Attributes.MinimumSamplingInterval]?.Value,
                            CultureInfo.InvariantCulture);
                    }

                    // AccessLevelEx Attribute
                    value = attributes[Attributes.AccessLevelEx];

                    if (value != null)
                    {
                        variableNode.AccessLevelEx = value.GetValueOrDefault<uint>();
                    }

                    node = variableNode;
                    break;
                case NodeClass.VariableType:
                    var variableTypeNode = new VariableTypeNode();

                    // IsAbstract Attribute
                    value = attributes[Attributes.IsAbstract];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "VariableType does not support the IsAbstract attribute.");
                    }

                    variableTypeNode.IsAbstract = value.GetValueOrDefault<bool>();

                    // DataType Attribute
                    value = attributes[Attributes.DataType];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "VariableType does not support the DataType attribute.");
                    }

                    variableTypeNode.DataType = (NodeId)value.GetValue(typeof(NodeId));

                    // ValueRank Attribute
                    value = attributes[Attributes.ValueRank];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "VariableType does not support the ValueRank attribute.");
                    }

                    variableTypeNode.ValueRank = value.GetValueOrDefault<int>();

                    // ArrayDimensions Attribute
                    value = attributes[Attributes.ArrayDimensions];

                    if (value != null && value.Value != null)
                    {
                        variableTypeNode.ArrayDimensions = (uint[])value.GetValue(typeof(uint[]));
                    }

                    node = variableTypeNode;
                    break;
                case NodeClass.Method:
                    var methodNode = new MethodNode();

                    // Executable Attribute
                    value = attributes[Attributes.Executable];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "Method does not support the Executable attribute.");
                    }

                    methodNode.Executable = value.GetValueOrDefault<bool>();

                    // UserExecutable Attribute
                    value = attributes[Attributes.UserExecutable];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "Method does not support the UserExecutable attribute.");
                    }

                    methodNode.UserExecutable = value.GetValueOrDefault<bool>();

                    node = methodNode;
                    break;
                case NodeClass.DataType:
                    var dataTypeNode = new DataTypeNode();

                    // IsAbstract Attribute
                    value = attributes[Attributes.IsAbstract];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "DataType does not support the IsAbstract attribute.");
                    }

                    dataTypeNode.IsAbstract = value.GetValueOrDefault<bool>();

                    // DataTypeDefinition Attribute
                    value = attributes[Attributes.DataTypeDefinition];

                    if (value != null)
                    {
                        dataTypeNode.DataTypeDefinition = value.Value as ExtensionObject;
                    }

                    node = dataTypeNode;
                    break;
                case NodeClass.ReferenceType:
                    var referenceTypeNode = new ReferenceTypeNode();

                    // IsAbstract Attribute
                    value = attributes[Attributes.IsAbstract];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "ReferenceType does not support the IsAbstract attribute.");
                    }

                    referenceTypeNode.IsAbstract = value.GetValueOrDefault<bool>();

                    // Symmetric Attribute
                    value = attributes[Attributes.Symmetric];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "ReferenceType does not support the Symmetric attribute.");
                    }

                    referenceTypeNode.Symmetric = value.GetValueOrDefault<bool>();

                    // InverseName Attribute
                    value = attributes[Attributes.InverseName];

                    if (value != null && value.Value != null)
                    {
                        referenceTypeNode.InverseName = (LocalizedText)value.GetValue(
                            typeof(LocalizedText));
                    }

                    node = referenceTypeNode;
                    break;
                case NodeClass.View:
                    var viewNode = new ViewNode();

                    // EventNotifier Attribute
                    value = attributes[Attributes.EventNotifier];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "View does not support the EventNotifier attribute.");
                    }

                    viewNode.EventNotifier = value.GetValueOrDefault<byte>();

                    // ContainsNoLoops Attribute
                    value = attributes[Attributes.ContainsNoLoops];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "View does not support the ContainsNoLoops attribute.");
                    }

                    viewNode.ContainsNoLoops = value.GetValueOrDefault<bool>();

                    node = viewNode;
                    break;
                case NodeClass.Unspecified:
                    throw ServiceResultException.Unexpected(
                        "Node does not have a valid value for NodeClass: {0}.",
                        nodeClass.Value);
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected NodeClass: {nodeClass}.");
            }

            // NodeId Attribute
            value = attributes[Attributes.NodeId];

            if (value == null)
            {
                throw ServiceResultException.Unexpected(
                    "Node does not support the NodeId attribute.");
            }

            node.NodeId = (NodeId)value.GetValue(typeof(NodeId));
            node.NodeClass = nodeClass.Value;

            // BrowseName Attribute
            value = attributes[Attributes.BrowseName];

            if (value == null)
            {
                throw ServiceResultException.Unexpected(
                    "Node does not support the BrowseName attribute.");
            }

            node.BrowseName = (QualifiedName)value.GetValue(typeof(QualifiedName));

            // DisplayName Attribute
            value = attributes[Attributes.DisplayName];

            if (value == null)
            {
                throw ServiceResultException.Unexpected(
                    "Node does not support the DisplayName attribute.");
            }

            node.DisplayName = (LocalizedText)value.GetValue(typeof(LocalizedText));

            // all optional attributes follow

            // Description Attribute
            if (attributes.TryGetValue(Attributes.Description, out value) &&
                value != null &&
                value.Value != null)
            {
                node.Description = (LocalizedText)value.GetValue(typeof(LocalizedText));
            }

            // WriteMask Attribute
            if (attributes.TryGetValue(Attributes.WriteMask, out value) && value != null)
            {
                node.WriteMask = value.GetValueOrDefault<uint>();
            }

            // UserWriteMask Attribute
            if (attributes.TryGetValue(Attributes.UserWriteMask, out value) && value != null)
            {
                node.UserWriteMask = value.GetValueOrDefault<uint>();
            }

            // RolePermissions Attribute
            if (attributes.TryGetValue(Attributes.RolePermissions, out value) && value != null)
            {
                if (value.Value is ExtensionObject[] rolePermissions)
                {
                    node.RolePermissions = [];

                    foreach (ExtensionObject rolePermission in rolePermissions)
                    {
                        node.RolePermissions.Add(rolePermission.Body as RolePermissionType);
                    }
                }
            }

            // UserRolePermissions Attribute
            if (attributes.TryGetValue(Attributes.UserRolePermissions, out value) && value != null)
            {
                if (value.Value is ExtensionObject[] userRolePermissions)
                {
                    node.UserRolePermissions = [];

                    foreach (ExtensionObject rolePermission in userRolePermissions)
                    {
                        node.UserRolePermissions.Add(rolePermission.Body as RolePermissionType);
                    }
                }
            }

            // AccessRestrictions Attribute
            if (attributes.TryGetValue(Attributes.AccessRestrictions, out value) && value != null)
            {
                node.AccessRestrictions = value.GetValueOrDefault<ushort>();
            }

            return node;
        }

        /// <summary>
        /// Create a dictionary of attributes to read for a nodeclass.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static Dictionary<uint, DataValue?> CreateAttributes(
            NodeClass nodeClass,
            bool skipOptionalAttributes)
        {
            // Attributes to read for all types of nodes
            var attributes = new Dictionary<uint, DataValue?>(Attributes.MaxAttributes)
            {
                { Attributes.NodeId, null },
                { Attributes.NodeClass, null },
                { Attributes.BrowseName, null },
                { Attributes.DisplayName, null }
            };

            switch (nodeClass)
            {
                case NodeClass.Object:
                    attributes.Add(Attributes.EventNotifier, null);
                    break;
                case NodeClass.Variable:
                    attributes.Add(Attributes.DataType, null);
                    attributes.Add(Attributes.ValueRank, null);
                    attributes.Add(Attributes.ArrayDimensions, null);
                    attributes.Add(Attributes.AccessLevel, null);
                    attributes.Add(Attributes.UserAccessLevel, null);
                    attributes.Add(Attributes.Historizing, null);
                    attributes.Add(Attributes.MinimumSamplingInterval, null);
                    attributes.Add(Attributes.AccessLevelEx, null);
                    break;
                case NodeClass.Method:
                    attributes.Add(Attributes.Executable, null);
                    attributes.Add(Attributes.UserExecutable, null);
                    break;
                case NodeClass.ObjectType:
                    attributes.Add(Attributes.IsAbstract, null);
                    break;
                case NodeClass.VariableType:
                    attributes.Add(Attributes.IsAbstract, null);
                    attributes.Add(Attributes.DataType, null);
                    attributes.Add(Attributes.ValueRank, null);
                    attributes.Add(Attributes.ArrayDimensions, null);
                    break;
                case NodeClass.ReferenceType:
                    attributes.Add(Attributes.IsAbstract, null);
                    attributes.Add(Attributes.Symmetric, null);
                    attributes.Add(Attributes.InverseName, null);
                    break;
                case NodeClass.DataType:
                    attributes.Add(Attributes.IsAbstract, null);
                    attributes.Add(Attributes.DataTypeDefinition, null);
                    break;
                case NodeClass.View:
                    attributes.Add(Attributes.EventNotifier, null);
                    attributes.Add(Attributes.ContainsNoLoops, null);
                    break;
                case NodeClass.Unspecified:
                    // build complete list of attributes.
                    attributes.Add(Attributes.DataType, null);
                    attributes.Add(Attributes.ValueRank, null);
                    attributes.Add(Attributes.ArrayDimensions, null);
                    attributes.Add(Attributes.AccessLevel, null);
                    attributes.Add(Attributes.UserAccessLevel, null);
                    attributes.Add(Attributes.MinimumSamplingInterval, null);
                    attributes.Add(Attributes.Historizing, null);
                    attributes.Add(Attributes.EventNotifier, null);
                    attributes.Add(Attributes.Executable, null);
                    attributes.Add(Attributes.UserExecutable, null);
                    attributes.Add(Attributes.IsAbstract, null);
                    attributes.Add(Attributes.InverseName, null);
                    attributes.Add(Attributes.Symmetric, null);
                    attributes.Add(Attributes.ContainsNoLoops, null);
                    attributes.Add(Attributes.DataTypeDefinition, null);
                    attributes.Add(Attributes.AccessLevelEx, null);
                    break;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected NodeClass: {nodeClass}.");
            }

            if (!skipOptionalAttributes)
            {
                attributes.Add(Attributes.Description, null);
                attributes.Add(Attributes.WriteMask, null);
                attributes.Add(Attributes.UserWriteMask, null);
                attributes.Add(Attributes.RolePermissions, null);
                attributes.Add(Attributes.UserRolePermissions, null);
                attributes.Add(Attributes.AccessRestrictions, null);
            }

            return attributes;
        }

        /// <summary>
        /// Creates a read request with attributes determined by the NodeClass.
        /// </summary>
        private static void CreateNodeClassAttributesReadNodesRequest(
            IReadOnlyList<NodeId> nodeIds,
            NodeClass nodeClass,
            ReadValueIdCollection attributesToRead,
            List<IDictionary<uint, DataValue?>?> attributesPerNodeId,
            NodeCollection nodeCollection,
            bool skipOptionalAttributes)
        {
            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                var node = new Node { NodeId = nodeIds[ii], NodeClass = nodeClass };

                Dictionary<uint, DataValue?> attributes = CreateAttributes(
                    node.NodeClass,
                    skipOptionalAttributes);
                foreach (uint attributeId in attributes.Keys)
                {
                    var itemToRead = new ReadValueId
                    {
                        NodeId = node.NodeId,
                        AttributeId = attributeId
                    };
                    attributesToRead.Add(itemToRead);
                }

                nodeCollection.Add(node);
                attributesPerNodeId.Add(attributes);
            }
        }
    }
}
