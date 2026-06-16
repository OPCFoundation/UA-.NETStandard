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
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
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
                    PubSubNetworkMessage? message;
                    try
                    {
                        message = await decoder.TryDecodeAsync(frame.Payload, context, cancellationToken)
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
            ReadOnlyMemory<byte> payload = await encoder.EncodeAsync(
                networkMessage,
                context,
                cancellationToken).ConfigureAwait(false);
            await transport.SendAsync(payload, topic: null, cancellationToken)
                .ConfigureAwait(false);
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
        }
    }
}
