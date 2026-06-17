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
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.StateMachine;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Connections
{
    /// <summary>
    /// Default sealed <see cref="IPubSubConnection"/> implementation.
    /// Owns the transport binding, the encoder / decoder lookup, and
    /// the writer and reader groups attached to the connection.
    /// </summary>
    /// <remarks>
    /// Implements the PubSubConnection contract from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.7">
    /// Part 14 §6.2.7 PubSubConnection</see>.
    /// </remarks>
    public sealed class PubSubConnection : IPubSubConnection, IAsyncDisposable
    {
        private readonly IPubSubTransportFactory m_transportFactory;
        private readonly IReadOnlyDictionary<string, INetworkMessageEncoder> m_encoders;
        private readonly IReadOnlyDictionary<string, INetworkMessageDecoder> m_decoders;
        private readonly IReadOnlyList<WriterGroup> m_writerGroups;
        private readonly IReadOnlyList<ReaderGroup> m_readerGroups;
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_timeProvider;
        private readonly IDataSetMetaDataRegistry m_metaDataRegistry;
        private readonly IPubSubDiagnostics m_diagnostics;
        private readonly UadpSecurityWrapper? m_securityWrapper;
        private readonly UadpSecurityWrapOptions m_securityWrapOptions;
        private readonly MessageSecurityMode m_requiredSecurityMode;
        private readonly int m_maxNetworkMessageSize;
        private readonly UadpReassembler m_reassembler;
        private int m_chunkSequenceNumber;
        private readonly ILogger<PubSubConnection> m_logger;
        private readonly System.Threading.Lock m_gate = new();
        private IPubSubTransport? m_transport;
        private CancellationTokenSource? m_receiveCts;
        private Task? m_receiveLoop;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="PubSubConnection"/>.
        /// </summary>
        /// <param name="configuration">Connection configuration.</param>
        /// <param name="transportFactory">Factory used to materialise the transport.</param>
        /// <param name="encoders">Encoders keyed by transport profile URI.</param>
        /// <param name="decoders">Decoders keyed by transport profile URI.</param>
        /// <param name="writerGroups">Writer groups owned by the connection.</param>
        /// <param name="readerGroups">Reader groups owned by the connection.</param>
        /// <param name="metaDataRegistry">Shared metadata registry.</param>
        /// <param name="diagnostics">Diagnostics sink.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Clock.</param>
        public PubSubConnection(
            PubSubConnectionDataType configuration,
            IPubSubTransportFactory transportFactory,
            IReadOnlyDictionary<string, INetworkMessageEncoder> encoders,
            IReadOnlyDictionary<string, INetworkMessageDecoder> decoders,
            IReadOnlyList<WriterGroup> writerGroups,
            IReadOnlyList<ReaderGroup> readerGroups,
            IDataSetMetaDataRegistry metaDataRegistry,
            IPubSubDiagnostics diagnostics,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
            : this(configuration, transportFactory, encoders, decoders,
                  writerGroups, readerGroups, metaDataRegistry, diagnostics,
                  telemetry, timeProvider,
                  securityWrapper: null,
                  securityWrapOptions: UadpSecurityWrapOptions.SignAndEncrypt,
                  maxNetworkMessageSize: 0,
                  requiredSecurityMode: MessageSecurityMode.None)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="PubSubConnection"/> with an
        /// optional UADP security wrapper. When supplied the wrapper is
        /// invoked on every outbound UADP NetworkMessage and on every
        /// inbound UADP frame whose
        /// <c>ExtendedFlags1.SecurityEnabled</c> bit is set.
        /// </summary>
        /// <param name="configuration">Connection configuration.</param>
        /// <param name="transportFactory">Factory used to materialise the transport.</param>
        /// <param name="encoders">Encoders keyed by transport profile URI.</param>
        /// <param name="decoders">Decoders keyed by transport profile URI.</param>
        /// <param name="writerGroups">Writer groups owned by the connection.</param>
        /// <param name="readerGroups">Reader groups owned by the connection.</param>
        /// <param name="metaDataRegistry">Shared metadata registry.</param>
        /// <param name="diagnostics">Diagnostics sink.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Clock.</param>
        /// <param name="securityWrapper">
        /// Optional UADP security wrapper resolved from the connection's
        /// SecurityKeyServices configuration.
        /// </param>
        /// <param name="securityWrapOptions">
        /// Sign/encrypt selection passed to
        /// <see cref="UadpSecurityWrapper.WrapAsync"/>.
        /// </param>
        /// <param name="maxNetworkMessageSize">
        /// Maximum size in bytes of a single outbound UADP NetworkMessage
        /// before chunking. <c>0</c> disables chunking.
        /// </param>
        /// <param name="requiredSecurityMode">
        /// Strictest <see cref="MessageSecurityMode"/> requested by any
        /// reader group on this connection. When
        /// <see cref="MessageSecurityMode.Sign"/> or
        /// <see cref="MessageSecurityMode.SignAndEncrypt"/> the receive
        /// path rejects any inbound frame that is not secured to at
        /// least that level (fail-closed).
        /// </param>
        public PubSubConnection(
            PubSubConnectionDataType configuration,
            IPubSubTransportFactory transportFactory,
            IReadOnlyDictionary<string, INetworkMessageEncoder> encoders,
            IReadOnlyDictionary<string, INetworkMessageDecoder> decoders,
            IReadOnlyList<WriterGroup> writerGroups,
            IReadOnlyList<ReaderGroup> readerGroups,
            IDataSetMetaDataRegistry metaDataRegistry,
            IPubSubDiagnostics diagnostics,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            UadpSecurityWrapper? securityWrapper,
            UadpSecurityWrapOptions securityWrapOptions,
            int maxNetworkMessageSize = 0,
            MessageSecurityMode requiredSecurityMode = MessageSecurityMode.None)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (transportFactory is null)
            {
                throw new ArgumentNullException(nameof(transportFactory));
            }
            if (encoders is null)
            {
                throw new ArgumentNullException(nameof(encoders));
            }
            if (decoders is null)
            {
                throw new ArgumentNullException(nameof(decoders));
            }
            if (writerGroups is null)
            {
                throw new ArgumentNullException(nameof(writerGroups));
            }
            if (readerGroups is null)
            {
                throw new ArgumentNullException(nameof(readerGroups));
            }
            if (metaDataRegistry is null)
            {
                throw new ArgumentNullException(nameof(metaDataRegistry));
            }
            if (diagnostics is null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            Configuration = configuration;
            m_transportFactory = transportFactory;
            m_encoders = encoders;
            m_decoders = decoders;
            m_writerGroups = writerGroups;
            m_readerGroups = readerGroups;
            m_metaDataRegistry = metaDataRegistry;
            m_diagnostics = diagnostics;
            m_telemetry = telemetry;
            m_timeProvider = timeProvider;
            m_securityWrapper = securityWrapper;
            m_securityWrapOptions = securityWrapOptions;
            m_requiredSecurityMode = requiredSecurityMode;
            m_maxNetworkMessageSize = maxNetworkMessageSize;
            m_reassembler = new UadpReassembler(timeProvider);
            Name = configuration.Name ?? string.Empty;
            TransportProfileUri = configuration.TransportProfileUri ?? string.Empty;
            PublisherId = configuration.PublisherId.IsNull
                ? PubSub.Encoding.PublisherId.Null
                : PubSub.Encoding.PublisherId.From(configuration.PublisherId);
            m_logger = telemetry.CreateLogger<PubSubConnection>();
            State = new PubSubStateMachine(
                string.IsNullOrEmpty(Name) ? "connection" : Name,
                PubSubComponentKind.Connection,
                m_logger);
            foreach (WriterGroup wg in m_writerGroups)
            {
                State.AttachChild(wg.State);
                wg.EncodingProfileOverride = ResolveEncoderProfile();
                wg.PubSubAddressing = new WriterGroup.PublisherIdHolder
                {
                    PublisherId = PublisherId
                };
                wg.PublishSink = SendNetworkMessageAsync;
            }
            foreach (ReaderGroup rg in m_readerGroups)
            {
                State.AttachChild(rg.State);
            }
        }

        /// <inheritdoc/>
        public string Name { get; }

        /// <inheritdoc/>
        public PublisherId PublisherId { get; }

        /// <inheritdoc/>
        public string TransportProfileUri { get; }

        /// <inheritdoc/>
        public IReadOnlyList<IWriterGroup> WriterGroups => m_writerGroups;

        /// <inheritdoc/>
        public IReadOnlyList<IReaderGroup> ReaderGroups => m_readerGroups;

        /// <inheritdoc/>
        public PubSubConnectionDataType Configuration { get; }

        /// <inheritdoc/>
        public PubSubStateMachine State { get; }

        private bool RequiresInboundSecurity =>
            m_requiredSecurityMode is MessageSecurityMode.Sign
                or MessageSecurityMode.SignAndEncrypt;

        /// <summary>
        /// Currently bound transport, or <see langword="null"/> when
        /// the connection has not yet been enabled. Exposed only to
        /// the application-internal metadata publisher so it can
        /// emit retained-metadata frames per
        /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.8">
        /// Part 14 §7.3.4.8</see> /
        /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.6.4">
        /// §7.2.4.6.4</see> without re-implementing transport ownership.
        /// </summary>
        internal IPubSubTransport? CurrentTransport
        {
            get
            {
                lock (m_gate)
                {
                    return m_transport;
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask EnableAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!State.TryEnable())
            {
                return;
            }
            IPubSubTransport transport;
            try
            {
                transport = m_transportFactory.Create(
                    Configuration,
                    m_telemetry,
                    m_timeProvider);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex,
                    "Failed to create transport for {Conn}.", Name);
                _ = State.TryFault(StatusCodes.BadResourceUnavailable);
                throw;
            }

            try
            {
                await transport.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transport.DisposeAsync().ConfigureAwait(false);
                _ = State.TryFault(StatusCodes.BadCommunicationError);
                throw;
            }

            lock (m_gate)
            {
                m_transport = transport;
            }

            _ = State.TryMarkOperational();

            // Start receive pump.
            if (m_readerGroups.Count > 0)
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                lock (m_gate)
                {
                    m_receiveCts = cts;
                }
                m_receiveLoop = Task.Run(() => ReceiveLoopAsync(cts.Token), cts.Token);
            }

            foreach (ReaderGroup rg in m_readerGroups)
            {
                await rg.EnableAsync(cancellationToken).ConfigureAwait(false);
            }
            foreach (WriterGroup wg in m_writerGroups)
            {
                await wg.EnableAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisableAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (WriterGroup wg in m_writerGroups)
            {
                await wg.DisableAsync(cancellationToken).ConfigureAwait(false);
            }
            foreach (ReaderGroup rg in m_readerGroups)
            {
                await rg.DisableAsync(cancellationToken).ConfigureAwait(false);
            }

            CancellationTokenSource? cts;
            Task? receiveLoop;
            IPubSubTransport? transport;
            lock (m_gate)
            {
                cts = m_receiveCts;
                m_receiveCts = null;
                receiveLoop = m_receiveLoop;
                m_receiveLoop = null;
                transport = m_transport;
                m_transport = null;
            }
            if (cts is not null)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
            if (receiveLoop is not null)
            {
                try
                {
                    await receiveLoop.ConfigureAwait(false);
                }
                catch
                {
                }
            }
            cts?.Dispose();
            if (transport is not null)
            {
                try
                {
                    await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex, "Transport close failed.");
                }
                await transport.DisposeAsync().ConfigureAwait(false);
            }
            _ = State.TryDisable();
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            IPubSubTransport? transport;
            lock (m_gate)
            {
                transport = m_transport;
            }
            if (transport is null)
            {
                return;
            }
            INetworkMessageDecoder? decoder = ResolveDecoder();
            if (decoder is null)
            {
                m_logger.LogWarning(
                    "No decoder registered for {Profile}; receive disabled.",
                    TransportProfileUri);
                return;
            }
            var context = new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(m_telemetry),
                m_metaDataRegistry,
                m_diagnostics,
                m_timeProvider);
            try
            {
                await foreach (PubSubTransportFrame frame
                    in transport.ReceiveAsync(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ReadOnlyMemory<byte> framePayload = frame.Payload;

                    if (UadpDecoder.TryReadOuterPrefix(framePayload,
                        out int prefixLength,
                        out bool securityEnabled,
                        out bool chunkMessage,
                        out PublisherId framePublisherId,
                        out ushort frameWriterGroupId))
                    {
                        if (chunkMessage)
                        {
                            ReadOnlyMemory<byte>? reassembled;
                            try
                            {
                                reassembled = TryReassembleChunk(
                                    framePayload, prefixLength,
                                    framePublisherId, frameWriterGroupId);
                            }
                            catch (Exception ex)
                            {
                                // Fail-soft: a malformed or hostile chunk
                                // must not terminate the receive loop.
                                m_diagnostics.Increment(
                                    PubSubDiagnosticsCounterKind.ChunksDiscarded);
                                m_logger.LogWarning(ex,
                                    "Inbound UADP chunk reassembly threw; dropping frame.");
                                continue;
                            }
                            if (reassembled is null)
                            {
                                continue;
                            }
                            framePayload = reassembled.Value;
                        }
                        else if (RequiresInboundSecurity)
                        {
                            // Fail-closed: a secured reader never accepts
                            // an unsecured frame and never trusts the
                            // wire's securityEnabled bit to opt out.
                            if (m_securityWrapper is null || !securityEnabled)
                            {
                                RecordSecurityFailure(
                                    StatusCodes.BadSecurityModeRejected,
                                    "Inbound frame is not secured to the reader's "
                                    + "configured SecurityMode.");
                                m_logger.LogWarning(
                                    "Dropping unsecured inbound frame on connection "
                                    + "'{Connection}' requiring {Mode}.",
                                    Name,
                                    m_requiredSecurityMode);
                                continue;
                            }
                            ReadOnlyMemory<byte>? unwrapped = await TryUnwrapInboundAsync(
                                framePayload, prefixLength,
                                m_requiredSecurityMode, cancellationToken)
                                .ConfigureAwait(false);
                            if (unwrapped is null)
                            {
                                continue;
                            }
                            framePayload = unwrapped.Value;
                        }
                        else if (m_securityWrapper is not null && securityEnabled)
                        {
                            ReadOnlyMemory<byte>? unwrapped = await TryUnwrapInboundAsync(
                                framePayload, prefixLength,
                                MessageSecurityMode.None, cancellationToken)
                                .ConfigureAwait(false);
                            if (unwrapped is null)
                            {
                                continue;
                            }
                            framePayload = unwrapped.Value;
                        }
                    }

                    PubSubNetworkMessage? message;
                    try
                    {
                        message = await decoder.TryDecodeAsync(framePayload, context, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(ex,
                            "Decoder threw on inbound frame.");
                        continue;
                    }
                    if (message is null)
                    {
                        continue;
                    }
                    if (TryRouteInboundMetaData(message))
                    {
                        continue;
                    }
                    foreach (ReaderGroup rg in m_readerGroups)
                    {
                        try
                        {
                            await rg.DispatchAsync(message, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogError(ex,
                                "Reader group {Group} dispatch threw.", rg.Name);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Receive loop terminated.");
            }
        }

        /// <summary>
        /// Routes an inbound MetaData NetworkMessage
        /// (<c>JsonMetaDataMessage</c> or
        /// <c>UadpDiscoveryResponseMessage</c> with
        /// <c>DiscoveryType = DataSetMetaData</c>) into the connection
        /// scoped <see cref="IDataSetMetaDataRegistry"/>, ensuring the
        /// <c>MetaDataChanged</c> event fires per
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/6.2.9.4">
        /// Part 14 §6.2.9.4</see> and
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.3.4.8">
        /// §7.3.4.8</see>.
        /// </summary>
        /// <param name="message">Decoded inbound NetworkMessage.</param>
        /// <returns><see langword="true"/> when the message was a
        /// metadata frame and was registered (so callers should skip
        /// the data-side dispatch).</returns>
        internal bool TryRouteInboundMetaData(PubSubNetworkMessage message)
        {
            return TryRouteInboundMetaData(m_metaDataRegistry, message, m_logger);
        }

        /// <summary>
        /// Static counterpart of <see cref="TryRouteInboundMetaData(PubSubNetworkMessage)"/>
        /// used by tests and by the receive loop. Dispatches the
        /// JSON / UADP metadata variants into the supplied registry.
        /// </summary>
        /// <param name="registry">Target registry.</param>
        /// <param name="message">Decoded NetworkMessage.</param>
        /// <param name="logger">Logger for diagnostic events.</param>
        /// <returns>Whether the message was recognised as metadata.</returns>
        internal static bool TryRouteInboundMetaData(
            IDataSetMetaDataRegistry registry,
            PubSubNetworkMessage message,
            ILogger logger)
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            DataSetMetaDataType? meta = null;
            PublisherId publisherId = message.PublisherId;
            ushort writerId = 0;
            Uuid classId = default;

            switch (message)
            {
                case Opc.Ua.PubSub.Encoding.Json.JsonMetaDataMessage json:
                    meta = json.MetaDataPayload ?? json.MetaData;
                    writerId = json.DataSetWriterId;
                    classId = json.DataSetClassId;
                    break;
                case UadpDiscoveryResponseMessage uadp
                    when uadp.DiscoveryType == UadpDiscoveryType.DataSetMetaData
                        && uadp.DataSetMetaData is not null:
                    meta = uadp.DataSetMetaData;
                    writerId = uadp.DataSetWriterId;
                    classId = uadp.DataSetClassId;
                    break;
                default:
                    return false;
            }

            if (meta is null)
            {
                return true;
            }

            var key = new DataSetMetaDataKey(
                publisherId,
                0,
                writerId,
                classId,
                meta.ConfigurationVersion?.MajorVersion ?? 0);

            MetaDataMatchResult existing = registry.TryGet(in key, out DataSetMetaDataType? current);
            if (existing == MetaDataMatchResult.MajorVersionMismatch
                && current?.ConfigurationVersion is { } currentVersion
                && currentVersion.MajorVersion > key.MajorVersion)
            {
                logger?.LogWarning(
                    "Discarding stale inbound metadata for writer {WriterId}: incoming major {Incoming} < registered major {Existing}.",
                    writerId, key.MajorVersion, currentVersion.MajorVersion);
                return true;
            }

            try
            {
                registry.Register(in key, meta);
                logger?.LogDebug(
                    "Registered inbound metadata for writer {WriterId} (major {Major}).",
                    writerId, key.MajorVersion);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex,
                    "Inbound metadata registration failed for writer {WriterId}.",
                    writerId);
            }
            return true;
        }


        private async ValueTask SendNetworkMessageAsync(
            PubSubNetworkMessage networkMessage,
            CancellationToken cancellationToken)
        {
            IPubSubTransport? transport;
            lock (m_gate)
            {
                transport = m_transport;
            }
            if (transport is null)
            {
                return;
            }
            INetworkMessageEncoder? encoder = ResolveEncoder();
            if (encoder is null)
            {
                m_logger.LogWarning(
                    "No encoder registered for {Profile}; publish skipped.",
                    TransportProfileUri);
                return;
            }
            var context = new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(m_telemetry),
                m_metaDataRegistry,
                m_diagnostics,
                m_timeProvider);

            ReadOnlyMemory<byte> payload;
            if (m_securityWrapper is not null
                && networkMessage is Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage uadp)
            {
                payload = await EncodeAndWrapUadpAsync(uadp, context, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (RequiresInboundSecurity || m_securityWrapper is not null
                && m_requiredSecurityMode is MessageSecurityMode.Sign
                    or MessageSecurityMode.SignAndEncrypt)
            {
                // Fail-closed: never emit plaintext for a secured group.
                // This path is only reachable for non-UADP messages, which
                // the UADP security wrapper cannot protect.
                m_diagnostics.Increment(PubSubDiagnosticsCounterKind.EncryptionErrors);
                m_diagnostics.RecordError(
                    StatusCodes.BadSecurityModeRejected,
                    "Refusing to publish an unsecured NetworkMessage on a connection "
                    + "configured for message security.");
                m_logger.LogError(
                    "Dropping outbound message on connection '{Connection}': "
                    + "configured SecurityMode {Mode} cannot be applied to this message.",
                    Name,
                    m_requiredSecurityMode);
                return;
            }
            else
            {
                payload = await encoder.EncodeAsync(
                    networkMessage,
                    context,
                    cancellationToken).ConfigureAwait(false);
            }

            if (m_maxNetworkMessageSize > 0
                && payload.Length > m_maxNetworkMessageSize
                && networkMessage is Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage uadpForChunk)
            {
                await SendChunkedAsync(
                    transport, payload, uadpForChunk, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await transport.SendAsync(payload, topic: null, cancellationToken)
                .ConfigureAwait(false);
        }

        private async ValueTask SendChunkedAsync(
            IPubSubTransport transport,
            ReadOnlyMemory<byte> encoded,
            Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage message,
            CancellationToken cancellationToken)
        {
            ushort sequenceNumber = unchecked(
                (ushort)Interlocked.Increment(ref m_chunkSequenceNumber));
            var chunker = new UadpChunker();
            IReadOnlyList<byte[]> chunkFrames;
            try
            {
                chunkFrames = chunker.Split(
                    encoded, sequenceNumber, m_maxNetworkMessageSize);
            }
            catch (Exception ex)
            {
                m_diagnostics.Increment(PubSubDiagnosticsCounterKind.ChunksDiscarded);
                m_diagnostics.RecordError(
                    StatusCodes.BadEncodingLimitsExceeded,
                    $"UADP chunking failed: {ex.Message}");
                throw;
            }
            foreach (byte[] chunk in chunkFrames)
            {
                ReadOnlyMemory<byte> envelope = UadpEncoder.WriteChunkEnvelope(
                    chunk, message.PublisherId, message.WriterGroupId);
                await transport.SendAsync(envelope, topic: null, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private async ValueTask<ReadOnlyMemory<byte>> EncodeAndWrapUadpAsync(
            Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage message,
            PubSubNetworkMessageContext context,
            CancellationToken cancellationToken)
        {
            try
            {
                ReadOnlyMemory<byte> encoded = UadpEncoder.EncodeWithSecurityBoundary(
                    message, context, out int payloadOffset);
                ReadOnlyMemory<byte> prefix = encoded.Slice(0, payloadOffset);
                ReadOnlyMemory<byte> inner = encoded.Slice(payloadOffset);
                ReadOnlyMemory<byte> wrapped = await m_securityWrapper!
                    .WrapAsync(prefix, inner, m_securityWrapOptions, cancellationToken)
                    .ConfigureAwait(false);
                return wrapped;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_diagnostics.Increment(PubSubDiagnosticsCounterKind.EncryptionErrors);
                m_diagnostics.RecordError(
                    StatusCodes.BadSecurityChecksFailed,
                    $"UADP security wrap failed: {ex.Message}");
                m_logger.LogError(ex, "UADP security wrap failed; dropping message.");
                throw;
            }
        }

        private INetworkMessageEncoder? ResolveEncoder()
        {
            if (m_encoders.TryGetValue(TransportProfileUri, out INetworkMessageEncoder? exact))
            {
                return exact;
            }
            // Fallback: pick by encoding family.
            string family = TransportProfileFamily(TransportProfileUri);
            foreach (KeyValuePair<string, INetworkMessageEncoder> entry in m_encoders)
            {
                if (TransportProfileFamily(entry.Key) == family)
                {
                    return entry.Value;
                }
            }
            return null;
        }

        private INetworkMessageDecoder? ResolveDecoder()
        {
            if (m_decoders.TryGetValue(TransportProfileUri, out INetworkMessageDecoder? exact))
            {
                return exact;
            }
            string family = TransportProfileFamily(TransportProfileUri);
            foreach (KeyValuePair<string, INetworkMessageDecoder> entry in m_decoders)
            {
                if (TransportProfileFamily(entry.Key) == family)
                {
                    return entry.Value;
                }
            }
            return null;
        }

        private string ResolveEncoderProfile()
        {
            // Map a transport profile to the encoding family used to
            // populate the WriterGroup's PubSubNetworkMessage subtype.
            return TransportProfileFamily(TransportProfileUri) switch
            {
                "Json" => Profiles.PubSubMqttJsonTransport,
                _ => Profiles.PubSubUdpUadpTransport
            };
        }

        private static string TransportProfileFamily(string profile)
        {
            return profile?.IndexOf("Json", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Json"
                : "Uadp";
        }

        private ReadOnlyMemory<byte>? TryReassembleChunk(
            ReadOnlyMemory<byte> frame,
            int prefixLength,
            PublisherId publisherId,
            ushort writerGroupId)
        {
            m_diagnostics.Increment(PubSubDiagnosticsCounterKind.ChunksReceived);
            ReadOnlyMemory<byte> inner = frame.Slice(prefixLength);
            if (!UadpChunker.TryParseChunk(inner,
                out _, out _, out _, out _))
            {
                m_diagnostics.Increment(PubSubDiagnosticsCounterKind.ChunksDiscarded);
                m_diagnostics.RecordError(
                    StatusCodes.BadDecodingError,
                    "Inbound UADP chunk frame header malformed.");
                return null;
            }
            int pendingBefore = m_reassembler.PendingCount;
            if (!m_reassembler.TryAddChunk(
                publisherId, writerGroupId, inner,
                out ReadOnlyMemory<byte>? reassembled))
            {
                int pendingAfter = m_reassembler.PendingCount;
                if (pendingAfter < pendingBefore)
                {
                    m_diagnostics.Increment(
                        PubSubDiagnosticsCounterKind.ChunkTimeouts);
                }
                return null;
            }
            m_diagnostics.Increment(PubSubDiagnosticsCounterKind.ChunksReassembled);
            return reassembled;
        }

        private async ValueTask<ReadOnlyMemory<byte>?> TryUnwrapInboundAsync(
            ReadOnlyMemory<byte> frame,
            int prefixLength,
            MessageSecurityMode requiredMode,
            CancellationToken cancellationToken)
        {
            try
            {
                ReadOnlyMemory<byte> prefix = frame.Slice(0, prefixLength);
                ReadOnlyMemory<byte> securityAndPayload = frame.Slice(prefixLength);

                UadpSecurityWrapper.UnwrapResult result = await m_securityWrapper!
                    .TryUnwrapAsync(prefix, securityAndPayload, cancellationToken)
                    .ConfigureAwait(false);
                if (!result.IsSuccess || result.InnerPayload is null)
                {
                    RecordSecurityFailure(result.Status, result.Reason ?? "Unwrap failed");
                    return null;
                }

                if (!SatisfiesRequiredSecurity(requiredMode, result.Header))
                {
                    RecordSecurityFailure(
                        StatusCodes.BadSecurityModeRejected,
                        "Inbound frame security level is lower than the reader's "
                        + "configured SecurityMode.");
                    m_logger.LogWarning(
                        "Dropping inbound frame on connection '{Connection}': "
                        + "security level below required {Mode}.",
                        Name,
                        requiredMode);
                    return null;
                }

                ReadOnlyMemory<byte> cleartext = result.InnerPayload.Value;
                int totalLength = prefix.Length + cleartext.Length;
                var combined = new byte[totalLength];
                prefix.Span.CopyTo(combined);
                cleartext.Span.CopyTo(combined.AsSpan(prefix.Length));
                return combined;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                RecordSecurityFailure(StatusCodes.BadSecurityChecksFailed, ex.Message);
                m_logger.LogError(ex, "UADP unwrap threw on inbound frame.");
                return null;
            }
        }

        private static bool SatisfiesRequiredSecurity(
            MessageSecurityMode requiredMode,
            UadpSecurityHeader? header)
        {
            if (requiredMode is not (MessageSecurityMode.Sign
                or MessageSecurityMode.SignAndEncrypt))
            {
                return true;
            }
            if (header is null)
            {
                return false;
            }
            var flags = (UadpSecurityFlagsEncodingMask)header.Value.SecurityFlags;
            bool signed = (flags & UadpSecurityFlagsEncodingMask.NetworkMessageSigned) != 0;
            bool encrypted =
                (flags & UadpSecurityFlagsEncodingMask.NetworkMessageEncrypted) != 0;
            if (requiredMode == MessageSecurityMode.SignAndEncrypt)
            {
                return signed && encrypted;
            }
            return signed;
        }

        private void RecordSecurityFailure(StatusCode status, string message)
        {
            PubSubDiagnosticsCounterKind kind;
            uint statusCode = status.Code;
            if (statusCode == StatusCodes.BadSecurityChecksFailed)
            {
                kind = PubSubDiagnosticsCounterKind.SignatureErrors;
            }
            else if (statusCode == StatusCodes.BadDecodingError)
            {
                kind = PubSubDiagnosticsCounterKind.EncryptionErrors;
            }
            else
            {
                kind = PubSubDiagnosticsCounterKind.SecurityTokenErrors;
            }
            m_diagnostics.Increment(kind);
            if (message.Contains("Replay", StringComparison.OrdinalIgnoreCase)
                || message.Contains("nonce", StringComparison.OrdinalIgnoreCase))
            {
                m_diagnostics.Increment(PubSubDiagnosticsCounterKind.ReplayErrors);
            }
            m_diagnostics.RecordError(status, message);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            try
            {
                await DisableAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
            m_reassembler.Dispose();
        }
    }
}
