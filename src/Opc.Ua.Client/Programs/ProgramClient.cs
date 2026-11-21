// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Programs
{
    /// <summary>
    /// Session-bound Part 10 discovery, lifecycle and metadata operations.
    /// Use <see cref="GetProgram"/> for the generated standard control methods and inherited
    /// state-machine helpers. This client neither owns the session nor replays failed controls.
    /// </summary>
    public sealed partial class ProgramClient
    {
        /// <summary>
        /// Creates a client over a caller-owned session.
        /// </summary>
        public ProgramClient(ISessionClient session, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// The session used for all operations.
        /// </summary>
        public ISessionClient Session { get; }

        /// <summary>
        /// The telemetry context supplied to generated proxies.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Binds the standard generated Program proxy to an invocation. Generated Start, Suspend,
        /// Resume, Halt and Reset methods remain the typed control surface.
        /// </summary>
        public ProgramStateMachineTypeClient GetProgram(NodeId invocationId)
        {
            ValidateNodeId(invocationId, nameof(invocationId));
            return new ProgramStateMachineTypeClient(Session, invocationId, Telemetry);
        }

        /// <summary>
        /// Calls a published vendor method once and preserves all argument diagnostics and output
        /// values. In particular, vendor Create output positions are never inferred by this client.
        /// Prefer a vendor's generated proxy when its signature is known.
        /// </summary>
        public ValueTask<CallMethodResult> CallWithResultAsync(
            NodeId objectId,
            ExpandedNodeId methodId,
            ArrayOf<Variant> arguments,
            CancellationToken ct = default)
        {
            return GetProgram(objectId).CallWithResultAsync(methodId, arguments, ct);
        }

        /// <summary>
        /// Requests invocation deletion through DeleteNodes. The server authorizes and validates
        /// Deletable and the current state; no read-before-delete race is treated as authorization.
        /// Bad service and operation results are propagated.
        /// </summary>
        public async ValueTask DeleteAsync(NodeId invocationId, CancellationToken ct = default)
        {
            ValidateNodeId(invocationId, nameof(invocationId));
            ArrayOf<DeleteNodesItem> requests =
            [
                new DeleteNodesItem { NodeId = invocationId, DeleteTargetReferences = true }
            ];
            DeleteNodesResponse response = await Session.DeleteNodesAsync(null, requests, ct).ConfigureAwait(false);
            ValidateService(response.ResponseHeader);
            ClientBase.ValidateResponse(response.Results, requests);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
            if (StatusCode.IsBad(response.Results[0]))
            {
                throw ServiceResultException.Create(response.Results[0], 0,
                    response.DiagnosticInfos, response.ResponseHeader.StringTable);
            }
        }

        /// <summary>
        /// Invokes the SDK model's generated, type-level Create method using its declared signature.
        /// This overload does not apply that signature to arbitrary vendor factories.
        /// </summary>
        public ValueTask<NodeId> CreateAsync(
            NodeId typeId,
            NodeId parentId,
            QualifiedName browseName,
            CancellationToken ct = default)
        {
            ValidateNodeId(typeId, nameof(typeId));
            ValidateNodeId(parentId, nameof(parentId));
            if (browseName.IsNull)
            {
                throw new ArgumentException("An invocation BrowseName is required.", nameof(browseName));
            }
            return new Ua.Programs.ManagedProgramTypeClient(Session, typeId, Telemetry)
                .CreateAsync(parentId, browseName, ct);
        }

        /// <summary>
        /// Validates a service-level result without interpreting bad per-value quality as missing nodes.
        /// </summary>
        private static void ValidateService(ResponseHeader header)
        {
            if (StatusCode.IsBad(header.ServiceResult))
            {
                throw new ServiceResultException(header.ServiceResult);
            }
        }

        /// <summary>
        /// Rejects an unspecified required node.
        /// </summary>
        private static void ValidateNodeId(NodeId nodeId, string parameter)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException("A non-null NodeId is required.", parameter);
            }
        }

        /// <summary>
        /// Fails a walk over server-supplied references that visited more nodes than
        /// <paramref name="limit"/>. The visited set and the results are kept in memory and the
        /// server decides how many nodes there are, so a walk has to end.
        /// </summary>
        private static void EnsureWalkBudget(int visited, int limit)
        {
            if (visited > limit)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The server returned more nodes than the Program client follows in one operation.");
            }
        }

        /// <summary>
        /// Nodes one type or invocation discovery visits.
        /// </summary>
        internal const int MaxDiscoveryNodes = 100_000;

        /// <summary>
        /// Nodes read beneath one FinalResultData object.
        /// </summary>
        internal const int MaxFinalResultNodes = 10_000;

        /// <summary>
        /// Levels followed beneath one FinalResultData object. Every result keeps its browse
        /// path, so the depth bounds the memory per result.
        /// </summary>
        internal const int MaxFinalResultDepth = 32;

        /// <summary>
        /// Resolves only local references using the current session's namespace mapping.
        /// </summary>
        private NodeId LocalNodeId(ExpandedNodeId id)
        {
            NodeId local = ExpandedNodeId.ToNodeId(id, Session.MessageContext.NamespaceUris);
            if (id.ServerIndex != 0 || local.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The browse target is not addressable in the current session.");
            }
            return local;
        }

        /// <summary>
        /// Browses one node completely, consuming and releasing continuation points.
        /// </summary>
        private async ValueTask<ArrayOf<ReferenceDescription>> BrowseAllAsync(
            NodeId nodeId,
            NodeId referenceType,
            BrowseDirection direction,
            NodeClass nodeClass,
            CancellationToken ct)
        {
            ArrayOf<BrowseDescription> requests =
            [
                new BrowseDescription
                {
                    NodeId = nodeId,
                    ReferenceTypeId = referenceType,
                    BrowseDirection = direction,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)nodeClass,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ];
            ByteString continuationPoint = default;
            var references = new List<ReferenceDescription>();
            // The server decides how many pages follow; the pages are kept in
            // memory, so the chain is followed within the browser's default limits.
            BrowsePagingLimits limits = BrowsePagingLimits.StartDefault();
            var pages = new BrowsePageCounter();
            bool failed = false;
            try
            {
                BrowseResponse response = await Session.BrowseAsync(null, null, 0, requests, ct).ConfigureAwait(false);
                ValidateService(response.ResponseHeader);
                ClientBase.ValidateResponse(response.Results, requests);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
                BrowseResult result = response.Results[0];
                continuationPoint = result.ContinuationPoint;
                while (true)
                {
                    if (StatusCode.IsBad(result.StatusCode))
                    {
                        throw new ServiceResultException(result.StatusCode);
                    }
                    foreach (ReferenceDescription reference in result.References)
                    {
                        references.Add(reference);
                    }
                    bool more = !continuationPoint.IsNull && continuationPoint.Length > 0;
                    ServiceResult? stop = pages.Record(in limits, result.References.Count, more);
                    if (!more)
                    {
                        return ArrayOf.Wrapped(references.ToArray());
                    }
                    if (stop != null)
                    {
                        // the continuation point is released in the finally block.
                        throw new ServiceResultException(stop);
                    }
                    ArrayOf<ByteString> points = [continuationPoint];
                    BrowseNextResponse next = await Session.BrowseNextAsync(null, false, points, ct)
                        .ConfigureAwait(false);
                    ValidateService(next.ResponseHeader);
                    ClientBase.ValidateResponse(next.Results, points);
                    ClientBase.ValidateDiagnosticInfos(next.DiagnosticInfos, points);
                    result = next.Results[0];
                    continuationPoint = result.ContinuationPoint;
                }
            }
            catch
            {
                failed = true;
                throw;
            }
            finally
            {
                if (!continuationPoint.IsNull && continuationPoint.Length > 0)
                {
                    try
                    {
                        await Session.BrowseNextAsync(null, true, [continuationPoint], CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch when (failed)
                    {
                        // Preserve the original browse/cancellation failure if releasing the server resource fails.
                    }
                }
            }
        }
    }
}
