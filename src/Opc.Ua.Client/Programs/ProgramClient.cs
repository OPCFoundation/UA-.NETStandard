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
                    if (continuationPoint.IsNull || continuationPoint.Length == 0)
                    {
                        return ArrayOf.Wrapped(references.ToArray());
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
