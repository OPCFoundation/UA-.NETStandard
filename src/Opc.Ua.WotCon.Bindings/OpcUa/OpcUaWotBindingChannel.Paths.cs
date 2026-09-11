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
 *
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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings.OpcUa
{
    internal sealed partial class OpcUaWotBindingChannel
    {
        private async ValueTask<ResolvedPathTarget> ResolveTargetAsync(
            NodeClass expectedClass, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Form.Addressing.BrowsePathTarget is not { } path)
            {
                if (!TryResolveNodeId(m_nodeId, out NodeId explicitTarget))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, $"'{m_nodeId}' is not a valid NodeId.");
                }
                return new ResolvedPathTarget(explicitTarget, null);
            }
            if (path.Path.Length > m_context.Bounds.MaxUriLength ||
                path.Elements.Count > m_context.Bounds.MaxBrowsePathElements)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "The browse path exceeds the executor's addressing bounds.");
            }
            OpcUaWotBindingExecutor.EnforceSessionSecurity(m_session, Form);
            PathSessionState sessionState = CapturePathSessionState();
            if (!TryResolveNodeId(path.AnchorId, out NodeId anchor, sessionState.NamespaceUris))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid,
                    "The browse-path anchor is not present in this Session's namespaces.");
            }
            var steps = new RelativePathElement[path.Elements.Count];
            for (int index = 0; index < steps.Length; index++)
            {
                WotBrowsePathStep step = path.Elements[index];
                NodeId referenceId = step.ReferenceKind switch
                {
                    RelativePathFormatter.ElementType.AnyHierarchical => Types.ReferenceTypeIds.HierarchicalReferences,
                    RelativePathFormatter.ElementType.AnyComponent => Types.ReferenceTypeIds.Aggregates,
                    _ => m_session.TypeTree.FindReferenceType(
                        ResolvePathName(step.ReferenceName, sessionState.NamespaceUris))
                };
                if (referenceId.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadReferenceTypeIdInvalid,
                        "The browse-path reference type is unknown to the Session.");
                }
                steps[index] = new RelativePathElement
                {
                    ReferenceTypeId = referenceId,
                    IsInverse = step.ReferenceKind == RelativePathFormatter.ElementType.InverseReference,
                    IncludeSubtypes = step.IncludeSubtypes,
                    TargetName = ResolvePathName(step.TargetName, sessionState.NamespaceUris)
                };
            }
            ArrayOf<BrowsePath> requests =
            [
                new BrowsePath { StartingNode = anchor, RelativePath = new RelativePath { Elements = steps } }
            ];
            TranslateBrowsePathsToNodeIdsResponse response = await m_session.TranslateBrowsePathsToNodeIdsAsync(
                null, requests, cancellationToken).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, requests);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
            BrowsePathResult result = response.Results[0] ??
                throw new ServiceResultException(StatusCodes.BadUnknownResponse, "The browse-path result is missing.");
            if (!StatusCode.IsGood(result.StatusCode))
            {
                throw new ServiceResultException(
                    result.StatusCode, "The Server could not resolve the browse-path target.");
            }
            NodeId resolved = default;
            for (int index = 0; index < result.Targets.Count; index++)
            {
                BrowsePathTarget candidate = result.Targets[index] ??
                    throw new ServiceResultException(
                        StatusCodes.BadUnknownResponse, "A browse-path target is missing.");
                if (candidate.RemainingPathIndex != uint.MaxValue || candidate.TargetId.ServerIndex != 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid,
                        "A browse-path target must be complete and belong to this Server.");
                }
                NodeId target = ExpandedNodeId.ToNodeId(candidate.TargetId, sessionState.NamespaceUris);
                if (target.IsNull ||
                    target.NamespaceIndex >= sessionState.NamespaceUris.Count ||
                    (!resolved.IsNull && resolved != target))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid,
                        "The browse path does not resolve to one unambiguous local Node.");
                }
                resolved = target;
            }
            if (resolved.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNoMatch, "The browse path did not resolve to a target.");
            }
            if (!string.IsNullOrEmpty(m_nodeId) &&
                (!TryResolveNodeId(m_nodeId, out NodeId explicitId, sessionState.NamespaceUris) ||
                    explicitId != resolved))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, "The explicit NodeId and browse path identify different Nodes.");
            }
            if (Form.Addressing.Metadata.TryGetValue("pathHrefNodeId", out string? hrefId) &&
                (!TryResolveNodeId(hrefId, out NodeId hrefTarget, sessionState.NamespaceUris) ||
                    hrefTarget != resolved))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, "The href NodeId and browse path identify different Nodes.");
            }
            ArrayOf<ReadValueId> attributes =
            [
                new ReadValueId { NodeId = resolved, AttributeId = Attributes.NodeClass }
            ];
            ReadResponse node = await m_session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, attributes, cancellationToken)
                .ConfigureAwait(false);
            ClientBase.ValidateResponse(node.Results, attributes);
            ClientBase.ValidateDiagnosticInfos(node.DiagnosticInfos, attributes);
            DataValue value = node.Results[0];
            if (!StatusCode.IsGood(value.StatusCode))
            {
                throw new ServiceResultException(value.StatusCode, "The resolved target NodeClass could not be read.");
            }
            if (!value.WrappedValue.TryGetValue(out int nodeClass) ||
                nodeClass is not ((int)NodeClass.Variable or (int)NodeClass.Method or (int)NodeClass.Object or
                    (int)NodeClass.View) ||
                (nodeClass & (int)expectedClass) == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeClassInvalid,
                    "The browse path resolves to the wrong NodeClass for this operation.");
            }
            ValidatePathSessionState(sessionState);
            return new ResolvedPathTarget(resolved, sessionState);
        }

        private PathSessionState CapturePathSessionState()
        {
            long revision = Volatile.Read(ref m_pathConfigurationRevision);
            ServiceMessageContext context = CreateSourceContext();
            NodeId sessionId = m_session.SessionId;
            if (revision != Volatile.Read(ref m_pathConfigurationRevision))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "The Session configuration changed while capturing the source context.");
            }
            return new PathSessionState(revision, sessionId, context);
        }

        private void ValidatePathSessionState(PathSessionState? state)
        {
            if (state is null)
            {
                return;
            }
            OpcUaWotBindingExecutor.EnforceSessionSecurity(m_session, Form);
            if (!state.Matches(CapturePathSessionState()) ||
                state.Revision != Volatile.Read(ref m_pathConfigurationRevision))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The admitted source context is no longer current.");
            }
        }

        private bool TryUsePathSessionState(PathSessionState state, Action use)
        {
            PathSessionState current;
            try
            {
                current = CapturePathSessionState();
            }
            catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadInvalidState)
            {
                return false;
            }
            lock (m_pathPublicationGate)
            {
                if (state.Revision != m_pathConfigurationRevision || !state.Matches(current))
                {
                    return false;
                }
                use();
                return true;
            }
        }

        private void OnPathConfigurationChanged(object? sender, EventArgs arguments)
        {
            lock (m_pathPublicationGate)
            {
                Interlocked.Increment(ref m_pathConfigurationRevision);
            }
        }

        private void PublishPathStatus(Action publish)
        {
            lock (m_pathPublicationGate)
            {
                publish();
            }
        }

        private static QualifiedName ResolvePathName(WotBrowsePathElement name, NamespaceTable namespaces)
        {
            int index = namespaces.GetIndex(name.NamespaceUri ?? Namespaces.OpcUa);
            if (index is < 0 or > ushort.MaxValue)
            {
                throw new ServiceResultException(
                    StatusCodes.BadBrowseNameInvalid, "A browse-path namespace is not present in the current Session.");
            }
            return new QualifiedName(name.Name, (ushort)index);
        }

        private sealed record ResolvedPathTarget(NodeId NodeId, PathSessionState? State);

        private sealed class PathSessionState(long revision, NodeId sessionId, ServiceMessageContext context)
        {
            public long Revision { get; } = revision;

            public NamespaceTable NamespaceUris => m_context.NamespaceUris;

            public bool Matches(PathSessionState other)
            {
                return Revision == other.Revision &&
                    m_sessionId == other.m_sessionId &&
                    ReferenceEquals(m_context.Factory, other.m_context.Factory) &&
                    m_context.NamespaceUris.ToArray().AsSpan().SequenceEqual(other.m_context.NamespaceUris.ToArray()) &&
                    m_context.ServerUris.ToArray().AsSpan().SequenceEqual(other.m_context.ServerUris.ToArray());
            }

            public ServiceMessageContext CreateContext()
            {
                return new ServiceMessageContext(m_context, m_context.Telemetry)
                {
                    NamespaceUris = new NamespaceTable(m_context.NamespaceUris),
                    ServerUris = new StringTable(m_context.ServerUris)
                };
            }

            private readonly NodeId m_sessionId = sessionId;
            private readonly ServiceMessageContext m_context = context;
        }

        private readonly Lock m_pathPublicationGate = new();
        private long m_pathConfigurationRevision;
    }
}
