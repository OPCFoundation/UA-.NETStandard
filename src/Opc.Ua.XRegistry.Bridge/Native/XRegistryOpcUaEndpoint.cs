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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.XRegistry.Bridge.Model;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    /// <summary>
    /// Endpoint over a host-owned ManagedSession. The session's authenticated identity,
    /// not caller-supplied envelope metadata, is authoritative on the remote server.
    /// </summary>
    public sealed class XRegistryOpcUaEndpoint : IXRegistryOperationJournalEndpoint, IXRegistryPreparedEndpoint
    {
        /// <summary>
        /// Binds an endpoint to a host-owned authenticated session and an explicitly selected registry root.
        /// </summary>
        public XRegistryOpcUaEndpoint(
            ManagedSession session, NodeId root, XRegistryBridgeNativeOptions options, ITelemetryContext telemetry)
            : this((ISession)session, root, options, telemetry)
        {
        }

        internal XRegistryOpcUaEndpoint(
            ISession session, NodeId root, XRegistryBridgeNativeOptions options, ITelemetryContext telemetry)
        {
            session.ThrowIfNull(nameof(session));
            options.ThrowIfNull(nameof(options));
            telemetry.ThrowIfNull(nameof(telemetry));
            options.Validate();
            if (root.IsNull)
            {
                throw new ArgumentException("A registry root is required.", nameof(root));
            }
            m_session = session;
            m_root = NodeId.ToExpandedNodeId(root, session.NamespaceUris);
            m_options = options;
            m_telemetry = telemetry;
            m_codec = new XRegistryProtocolCodec(options.MaxMessageBytes);
            m_logger = telemetry.CreateLogger<XRegistryOpcUaEndpoint>();
        }

        /// <inheritdoc/>
        public async ValueTask<XRegistryEndpointDescription> InspectAsync(
            XRegistryCallContext context, CancellationToken cancellationToken = default)
        {
            context.ThrowIfNull(nameof(context));
            RegistryBridgeTypeClient? bridge = await DiscoverBridgeAsync(cancellationToken).ConfigureAwait(false);
            if (bridge is null)
            {
                return await BaseEndpoint().InspectAsync(context, cancellationToken).ConfigureAwait(false);
            }
            (NodeId file, uint handle) = await bridge.InspectRegistryAsync(cancellationToken).ConfigureAwait(false);
            ByteString bytes = await ReadTransferAsync(bridge, file, handle, cancellationToken).ConfigureAwait(false);
            XRegistryEndpointDescription description = m_codec.DecodeDescription(bytes);
            return description with
            {
                SupportsPreparedMutations = description.SupportsPreparedMutations &&
                    await DiscoverBridgeAsync(cancellationToken,
                        requirePrepared: true).ConfigureAwait(false) is not null
            };
        }

        /// <inheritdoc/>
        public async ValueTask<IXRegistryPreparedOperation> PrepareAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default)
        {
            request.ThrowIfNull(nameof(request));
            if (request.IsMutation && m_session.Endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityModeInsufficient);
            }
            RegistryBridgeTypeClient bridge = await DiscoverBridgeAsync(cancellationToken, requirePrepared: true)
                .ConfigureAwait(false) ??
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The native endpoint does not expose prepared mutation methods.");
            ByteString encoded = m_codec.EncodeRequest(request);
            (NodeId upload, uint handle) = await bridge.BeginRequestAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var file = new FileTypeClient(m_session, upload, m_telemetry);
                int chunkSize = ChunkSize(m_session, m_options);
                for (int offset = 0; offset < encoded.Length; offset += chunkSize)
                {
                    await file.WriteAsync(handle,
                        new ByteString(encoded.Slice(offset, Math.Min(chunkSize, encoded.Length - offset))),
                        cancellationToken).ConfigureAwait(false);
                }
                await file.CloseAsync(handle, cancellationToken).ConfigureAwait(false);
                (NodeId preview, uint previewHandle) = await bridge.PrepareRequestAsync(
                    upload, request.OperationId ?? string.Empty, m_codec.ComputeRequestDigest(request),
                        cancellationToken)
                    .ConfigureAwait(false);
                XRegistryResponse response = m_codec.DecodeResponse(await ReadTransferAsync(
                    bridge, preview, previewHandle, cancellationToken).ConfigureAwait(false));
                return new RemotePreparedOperation(this, bridge, upload, response);
            }
            catch (Exception exception)
            {
                await ReleaseTransferAsync(bridge, upload, exception).ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<XRegistryResponse> ExecuteAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default)
        {
            request.ThrowIfNull(nameof(request));
            RegistryBridgeTypeClient? bridge = await DiscoverBridgeAsync(cancellationToken).ConfigureAwait(false);
            if (bridge is null)
            {
                return await BaseEndpoint().ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            }
            if (request.IsMutation && m_session.Endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityModeInsufficient);
            }
            ByteString encoded = m_codec.EncodeRequest(request);
            string digest = m_codec.ComputeRequestDigest(request);
            (NodeId upload, uint handle) = await bridge.BeginRequestAsync(cancellationToken).ConfigureAwait(false);
            Exception? failure = null;
            try
            {
                var file = new FileTypeClient(m_session, upload, m_telemetry);
                int chunkSize = ChunkSize(m_session, m_options);
                for (int offset = 0; offset < encoded.Length; offset += chunkSize)
                {
                    await file.WriteAsync(handle,
                        new ByteString(encoded.Slice(offset, Math.Min(chunkSize, encoded.Length - offset))),
                        cancellationToken).ConfigureAwait(false);
                }
                await file.CloseAsync(handle, cancellationToken).ConfigureAwait(false);
                (NodeId responseFile, uint responseHandle) = await bridge.CommitRequestAsync(
                    upload, request.OperationId ?? string.Empty, digest, cancellationToken).ConfigureAwait(false);
                return m_codec.DecodeResponse(await ReadTransferAsync(
                    bridge, responseFile, responseHandle, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                failure = exception;
                throw;
            }
            finally
            {
                await ReleaseTransferAsync(bridge, upload, failure).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<XRegistryOperationOutcome> GetOperationOutcomeAsync(
            string operationId, XRegistryCallContext context, CancellationToken cancellationToken = default)
        {
            context.ThrowIfNull(nameof(context));
            if (string.IsNullOrWhiteSpace(operationId))
            {
                throw new ArgumentException("An operation identity is required.", nameof(operationId));
            }
            RegistryBridgeTypeClient? bridge = await DiscoverBridgeAsync(cancellationToken).ConfigureAwait(false) ??
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The base binding has no outcome journal.");
            (uint state, NodeId file, uint handle) = await bridge.GetOperationOutcomeAsync(
                operationId, cancellationToken).ConfigureAwait(false);
            if (state > (uint)XRegistryOperationState.Rejected ||
                (state == (uint)XRegistryOperationState.Unknown && (!file.IsNull || handle != 0)) ||
                (state != (uint)XRegistryOperationState.Unknown && (file.IsNull || handle == 0)))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "The operation outcome shape is invalid.");
            }
            XRegistryResponse? response = file.IsNull ? null : m_codec.DecodeResponse(
                await ReadTransferAsync(bridge, file, handle, cancellationToken).ConfigureAwait(false));
            return new XRegistryOperationOutcome((XRegistryOperationState)state, response);
        }

        internal static async ValueTask<ArrayOf<ReferenceDescription>> BrowseAsync(
            ISession session, NodeId parent, XRegistryBridgeNativeOptions options, CancellationToken ct)
        {
            var descriptions = new List<ReferenceDescription>();
            ByteString continuation = default;
            try
            {
                BrowseResponse response = await session.BrowseAsync(null, null, 0,
                    [new BrowseDescription
                    {
                        NodeId = parent,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable | NodeClass.Method),
                        ResultMask = (uint)BrowseResultMask.All
                    }], ct).ConfigureAwait(false);
                if (response.Results.Count != 1)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError, "A Browse result is missing.");
                }
                BrowseResult result = response.Results[0];
                for (int page = 0; page < options.MaxBrowsePages; page++)
                {
                    continuation = result.ContinuationPoint;
                    if (StatusCode.IsBad(result.StatusCode))
                    {
                        throw new ServiceResultException(result.StatusCode);
                    }
                    foreach (ReferenceDescription reference in result.References)
                    {
                        if (reference.NodeId.ServerIndex != 0)
                        {
                            throw new ServiceResultException(StatusCodes.BadNotSupported,
                                "External server references require an explicit domain mapping.");
                        }
                        if (ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris).IsNull)
                        {
                            throw new ServiceResultException(StatusCodes.BadNodeIdUnknown,
                                "The reference namespace URI could not be resolved in the current session.");
                        }
                        descriptions.Add(reference);
                        if (descriptions.Count > options.MaxEntities * 32L)
                        {
                            throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                        }
                    }
                    if (continuation.IsNull || continuation.Length == 0)
                    {
                        return [.. descriptions];
                    }
                    BrowseNextResponse next = await session.BrowseNextAsync(null, false, [continuation], ct)
                        .ConfigureAwait(false);
                    if (next.Results.Count != 1)
                    {
                        throw new ServiceResultException(StatusCodes.BadDecodingError);
                    }
                    result = next.Results[0];
                }
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The native Browse page budget was reached; the inventory is incomplete.");
            }
            finally
            {
                if (!continuation.IsNull && continuation.Length != 0)
                {
                    _ = await session.BrowseNextAsync(null, true, [continuation], CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
        }

        internal static async ValueTask<ByteString> ReadFileAsync(
            FileTypeClient file, uint handle, int chunkSize, int maximumBytes, CancellationToken ct)
        {
            using var bytes = new MemoryStream();
            while (true)
            {
                ByteString chunk = await file.ReadAsync(handle, chunkSize, ct).ConfigureAwait(false);
                if (chunk.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError,
                        "A null file chunk is not an empty document.");
                }
                if (chunk.Length > chunkSize || bytes.Length + chunk.Length > maximumBytes)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
                if (chunk.Length == 0)
                {
                    return ByteString.From(bytes.ToArray());
                }
                XRegistryNativeBuffers.Write(bytes, chunk);
            }
        }

        internal static int ChunkSize(ISession session, XRegistryBridgeNativeOptions options)
        {
            int serverLimit = session.MessageContext.MaxByteStringLength;
            return serverLimit > 0 ? Math.Min(options.ChunkSize, serverLimit) : options.ChunkSize;
        }

        private async ValueTask<RegistryBridgeTypeClient?> DiscoverBridgeAsync(
            CancellationToken ct, bool requirePrepared = false)
        {
            var root = ExpandedNodeId.ToNodeId(m_root, m_session.NamespaceUris);
            ArrayOf<ReferenceDescription> children = await BrowseAsync(m_session, root, m_options, ct)
                .ConfigureAwait(false);
            ReferenceDescription[] candidates = [.. (children.ToArray() ?? []).Where(reference =>
                reference.BrowseName.Name == "Bridge" &&
                m_session.NamespaceUris.GetString(reference.BrowseName.NamespaceIndex) ==
                    XRegistryBridgeNativeOptions.ExperimentalNamespaceUri)];
            if (candidates.Length == 0)
            {
                return null;
            }
            if (candidates.Length != 1 ||
                ExpandedNodeId.ToNodeId(candidates[0].TypeDefinition, m_session.NamespaceUris) !=
                ExpandedNodeId.ToNodeId(Model.ObjectTypeIds.RegistryBridgeType, m_session.NamespaceUris))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "The experimental bridge type is invalid.");
            }
            var id = ExpandedNodeId.ToNodeId(candidates[0].NodeId, m_session.NamespaceUris);
            ArrayOf<ReferenceDescription> bridgeChildren = await BrowseAsync(m_session, id, m_options, ct)
                .ConfigureAwait(false);
            ReferenceDescription? protocol = (bridgeChildren.ToArray() ?? []).SingleOrDefault(reference =>
                reference.BrowseName.Name == "ProtocolVersion" &&
                m_session.NamespaceUris.GetString(reference.BrowseName.NamespaceIndex) ==
                    XRegistryBridgeNativeOptions.ExperimentalNamespaceUri) ??
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The experimental protocol is unversioned.");
            DataValue value = await m_session.ReadValueAsync(
                ExpandedNodeId.ToNodeId(protocol.NodeId, m_session.NamespaceUris), ct).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                throw new ServiceResultException(value.StatusCode);
            }
            if (!value.WrappedValue.TryGetValue(out uint version) || version is not (1 or 2))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Unsupported experimental protocol version.");
            }
            if (requirePrepared &&
                (version < 2 ||
                    !HasMethod(bridgeChildren, "PrepareRequest") ||
                    !HasMethod(bridgeChildren, "CommitPreparedRequest")))
            {
                return null;
            }
            return new RegistryBridgeTypeClient(m_session, id, m_telemetry);
        }

        private bool HasMethod(ArrayOf<ReferenceDescription> children, string name)
        {
            int count = 0;
            foreach (ReferenceDescription reference in children)
            {
                if (reference.NodeClass == NodeClass.Method &&
                    reference.BrowseName.Name == name &&
                    m_session.NamespaceUris.GetString(reference.BrowseName.NamespaceIndex) ==
                        XRegistryBridgeNativeOptions.ExperimentalNamespaceUri)
                {
                    count++;
                }
            }
            return count == 1;
        }

        private XRegistryBaseOpcUaEndpoint BaseEndpoint()
        {
            return new XRegistryBaseOpcUaEndpoint(m_session,
                ExpandedNodeId.ToNodeId(m_root, m_session.NamespaceUris), m_options, m_telemetry);
        }

        private async ValueTask<ByteString> ReadTransferAsync(
            RegistryBridgeTypeClient bridge, NodeId id, uint handle, CancellationToken ct)
        {
            if (id.IsNull || handle == 0)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "A response transfer is missing.");
            }
            var file = new FileTypeClient(m_session, id, m_telemetry);
            Exception? failure = null;
            try
            {
                ByteString result = await ReadFileAsync(
                    file, handle, ChunkSize(m_session, m_options), m_options.MaxMessageBytes, ct)
                    .ConfigureAwait(false);
                await file.CloseAsync(handle, ct).ConfigureAwait(false);
                return result;
            }
            catch (Exception exception)
            {
                failure = exception;
                throw;
            }
            finally
            {
                await ReleaseTransferAsync(bridge, id, failure).ConfigureAwait(false);
            }
        }

        private async ValueTask ReleaseTransferAsync(
            RegistryBridgeTypeClient bridge, NodeId id, Exception? primaryFailure)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await bridge.AbortRequestAsync(id, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (primaryFailure is not null &&
                exception is ServiceResultException or OperationCanceledException or IOException)
            {
                m_logger.TransferCleanupFailed(exception);
            }
        }

        private sealed class RemotePreparedOperation(
            XRegistryOpcUaEndpoint endpoint, RegistryBridgeTypeClient bridge, NodeId ticket, XRegistryResponse response)
            : IXRegistryPreparedOperation
        {
            public XRegistryResponse Response { get; } = response;

            public async ValueTask<XRegistryResponse> CommitAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Interlocked.CompareExchange(ref m_state, 1, 0) != 0)
                {
                    throw new InvalidOperationException("The prepared operation was already committed or aborted.");
                }
                Exception? failure = null;
                try
                {
                    (NodeId rejection, uint handle) = await bridge.CommitPreparedRequestAsync(ticket, cancellationToken)
                        .ConfigureAwait(false);
                    if (rejection.IsNull && handle == 0)
                    {
                        return Response;
                    }
                    XRegistryResponse rejected = endpoint.m_codec.DecodeResponse(
                        await endpoint.ReadTransferAsync(bridge, rejection, handle,
                            cancellationToken).ConfigureAwait(false));
                    if (rejected.IsSuccess)
                    {
                        throw new ServiceResultException(StatusCodes.BadDecodingError,
                            "A prepared commit must return either the original preview or a rejection.");
                    }
                    return rejected;
                }
                catch (Exception exception)
                {
                    failure = exception;
                    throw;
                }
                finally
                {
                    try
                    {
                        await endpoint.ReleaseTransferAsync(bridge, ticket, failure).ConfigureAwait(false);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref m_state, 2);
                    }
                }
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.CompareExchange(ref m_state, 2, 0) == 0)
                {
                    await endpoint.ReleaseTransferAsync(bridge, ticket, null).ConfigureAwait(false);
                }
            }

            private int m_state;
        }

        private readonly ISession m_session;
        private readonly ExpandedNodeId m_root;
        private readonly XRegistryBridgeNativeOptions m_options;
        private readonly ITelemetryContext m_telemetry;
        private readonly XRegistryProtocolCodec m_codec;
        private readonly ILogger m_logger;
    }

    internal static partial class XRegistryOpcUaEndpointLog
    {
        [LoggerMessage(EventId = XRegistryBridgeNativeEventIds.TransferCleanupFailed, Level = LogLevel.Warning,
            Message = "Native transfer cleanup failed after an earlier operation failure.")]
        public static partial void TransferCleanupFailed(this ILogger logger, Exception exception);
    }

    internal static class XRegistryBridgeNativeEventIds
    {
        public const int TransferCleanupFailed = 2000;
    }
}
