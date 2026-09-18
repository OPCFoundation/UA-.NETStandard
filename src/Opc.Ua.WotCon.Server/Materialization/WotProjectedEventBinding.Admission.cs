/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    internal sealed partial class WotProjectedEventBinding
    {
        public async ValueTask PrepareTransparentAsync(INodeManagerBuilder builder, CancellationToken cancellationToken)
        {
            ValidateIdentityAdmission();
            WotEventSource source = await Source.CaptureAsync(cancellationToken).ConfigureAwait(false);
            source.Validate();
            if (!source.IsAuthenticated)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "Transparent activation requires authenticated source identity.");
            }
            if (Source.Form.Payload.Schema is not null ||
                (Source.Form.EventSelection ?? WotEventSelection.Default).Clauses
                    .Contains(clause => clause.PayloadSchema is not null))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Transparent activation cannot prove a custom payload schema.");
            }
            await ValidateEventTypeAsync(builder, source, cancellationToken).ConfigureAwait(false);
            source.Validate();
            ValidateIdentityAdmission();
            cancellationToken.ThrowIfCancellationRequested();
            m_routeRegistry.PrepareTransparentSource(this, source);
            m_preparedSource = source;
        }

        public void ValidatePreparedSource()
        {
            if (m_preparedSource is not null)
            {
                m_preparedSource.Validate();
                ValidateIdentityAdmission();
            }
        }

        private void ValidateIdentityAdmission()
        {
            if (m_eventManager?.SupportsEventIdentityAdmission != true)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Transparent activation requires server-wide event identity admission.");
            }
            StatusCode status = m_eventManager.EventIdentityAdmissionStatus;
            if (!StatusCode.IsGood(status))
            {
                throw new ServiceResultException(
                    status, "The host cannot establish continuous server-wide event identity ownership.");
            }
        }

        private async ValueTask ValidateEventTypeAsync(
            INodeManagerBuilder builder, WotEventSource source, CancellationToken cancellationToken)
        {
            HashSet<NodeId> declarationOwners = FindDeclarationOwners(builder, cancellationToken);
            NodeId localId = EventTypeId;
            var visited = new HashSet<NodeId>();
            while (true)
            {
                if (!visited.Add(localId) || visited.Count > 64 || localId.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTypeDefinitionInvalid, "The event type lineage is cyclic, missing or too deep.");
                }
                BaseObjectTypeState? local = localId.NamespaceIndex == 0
                    ? null : builder.Node<BaseObjectTypeState>(localId).Node;
                NodeId parent = local?.SuperTypeId ?? m_context.TypeTable.FindSuperType(localId);
                if (local is not null)
                {
                    var children = new List<BaseInstanceState>();
                    local.GetChildren(m_context, children);
                    var references = new List<IReference>();
                    local.GetReferences(m_context, references);
                    if (children.Count != 0 || declarationOwners.Contains(localId) ||
                        references.Exists(reference => !reference.IsInverse &&
                        m_context.TypeTable.IsTypeOf(reference.ReferenceTypeId, Ua.ReferenceTypeIds.Aggregates)))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNotSupported,
                            "Transparent activation supports Core event types without custom instance declarations.");
                    }
                }
                ExpandedNodeId identity = NodeId.ToExpandedNodeId(localId, m_context.NamespaceUris);
                NodeId remoteId = ExpandedNodeId.ToNodeId(identity, source.Context.NamespaceUris);
                if (remoteId.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTypeDefinitionInvalid, "The source does not define the projected type namespace.");
                }
                ArrayOf<ReadValueId> requests =
                [
                    new ReadValueId { NodeId = remoteId, AttributeId = Attributes.NodeClass },
                    new ReadValueId { NodeId = remoteId, AttributeId = Attributes.IsAbstract },
                    new ReadValueId { NodeId = remoteId, AttributeId = Attributes.BrowseName }
                ];
                ReadResponse response = await source.Client.ReadAsync(
                    null, 0, TimestampsToReturn.Neither, requests, cancellationToken).ConfigureAwait(false);
                ClientBase.ValidateResponse(response.Results, requests);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
                foreach (DataValue value in response.Results)
                {
                    if (!StatusCode.IsGood(value.StatusCode))
                    {
                        throw new ServiceResultException(value.StatusCode, "The source event type is unavailable.");
                    }
                }
                if (!response.Results[0].WrappedValue.TryGetValue(out int nodeClass) ||
                    nodeClass != (int)NodeClass.ObjectType ||
                    !response.Results[1].WrappedValue.TryGetValue(out bool isAbstract) ||
                    !response.Results[2].WrappedValue.TryGetValue(out QualifiedName browseName) ||
                    (local is not null && (isAbstract != local.IsAbstract ||
                        WotBindingValueMapper.Translate(new Variant(browseName), source.Context, m_valueContext) !=
                            new Variant(local.BrowseName))))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTypeDefinitionInvalid, "The source event type differs from its projected type.");
                }
                await ValidateTypeReferencesAsync(source, remoteId, parent, local is not null, cancellationToken)
                    .ConfigureAwait(false);
                if (localId == Ua.ObjectTypeIds.BaseEventType)
                {
                    break;
                }
                localId = parent;
            }
            if (IsCondition != m_context.TypeTable.IsTypeOf(EventTypeId, Ua.ObjectTypeIds.ConditionType))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeDefinitionInvalid, "The declared Condition facet differs from its event type.");
            }
        }

        private HashSet<NodeId> FindDeclarationOwners(INodeManagerBuilder builder, CancellationToken cancellationToken)
        {
            if (builder.NodeManager is not ILocalAddressSpaceSource addressSpace)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The projection cannot inspect its prepared event declarations.");
            }
            var owners = new HashSet<NodeId>();
            var visited = new HashSet<NodeId>();
            var pending = new Stack<NodeState>(addressSpace.CreateLocalAddressSpace().Nodes);
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NodeState node = pending.Pop();
                if (!visited.Add(node.NodeId))
                {
                    continue;
                }
                var children = new List<BaseInstanceState>();
                node.GetChildren(m_context, children);
                foreach (BaseInstanceState child in children)
                {
                    pending.Push(child);
                }
                var references = new List<IReference>();
                node.GetReferences(m_context, references);
                foreach (IReference reference in references)
                {
                    if (reference.IsInverse &&
                        m_context.TypeTable.IsTypeOf(reference.ReferenceTypeId, Ua.ReferenceTypeIds.Aggregates))
                    {
                        // RuntimeNodeSet has not completed its reverse-reference pass yet.
                        owners.Add(ExpandedNodeId.ToNodeId(reference.TargetId, m_context.NamespaceUris));
                    }
                }
            }
            return owners;
        }

        private async ValueTask ValidateTypeReferencesAsync(
            WotEventSource source, NodeId remoteId, NodeId parent, bool customType, CancellationToken cancellationToken)
        {
            ArrayOf<BrowseDescription> requests =
            [
                new BrowseDescription
                {
                    NodeId = remoteId,
                    BrowseDirection = BrowseDirection.Inverse,
                    ReferenceTypeId = Ua.ReferenceTypeIds.HasSubtype,
                    ResultMask = (uint)BrowseResultMask.All
                },
                new BrowseDescription
                {
                    NodeId = remoteId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = Ua.ReferenceTypeIds.Aggregates,
                    IncludeSubtypes = true,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ];
            if (!customType)
            {
                requests = [requests[0]];
            }
            BrowseResponse response = await source.Client.BrowseAsync(
                null, null, 2, requests, cancellationToken).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, requests);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
            try
            {
                foreach (BrowseResult result in response.Results)
                {
                    if (!StatusCode.IsGood(result.StatusCode))
                    {
                        throw new ServiceResultException(result.StatusCode, "The source type cannot be inspected.");
                    }
                    if (!result.ContinuationPoint.IsEmpty)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNotSupported, "The source type has unsupported or ambiguous declarations.");
                    }
                }
                ArrayOf<ReferenceDescription> parents = response.Results[0].References;
                if (parents.Count != 1 ||
                    parents[0].NodeId.ServerIndex != 0 ||
                    NodeId.ToExpandedNodeId(
                        ExpandedNodeId.ToNodeId(parents[0].NodeId, source.Context.NamespaceUris),
                        source.Context.NamespaceUris) != NodeId.ToExpandedNodeId(parent, m_context.NamespaceUris))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTypeDefinitionInvalid, "The source and projected type lineages differ.");
                }
                if (customType && !response.Results[1].References.IsEmpty)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported, "Custom source declarations require unsupported schema admission.");
                }
            }
            finally
            {
                var continuations = new List<ByteString>();
                foreach (BrowseResult result in response.Results)
                {
                    if (!result.ContinuationPoint.IsEmpty)
                    {
                        continuations.Add(result.ContinuationPoint);
                    }
                }
                if (continuations.Count != 0)
                {
                    _ = await source.Client.BrowseNextAsync(null, true, continuations.ToArrayOf(), CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            source.Validate();
        }

        private WotEventSource? m_preparedSource;
    }
}
