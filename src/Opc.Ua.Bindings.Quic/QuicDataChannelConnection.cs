// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// One native-only QUIC data context. HELLO, OPEN/ACCEPT/READY, records and revisions belong to Core.
    /// No UASC, Session, model registration or ClientServer flow is implicitly opened.
    /// </summary>
    public sealed class QuicDataChannelConnection : IDataChannelCarrier, IAsyncDisposable
    {
        /// <summary>
        /// Creates the carrier after mTLS and actual management-stream ownership have been verified.
        /// </summary>
        private QuicDataChannelConnection(
            NativeQuicConnection connection,
            NativeQuicStream management,
            Uri endpointUrl,
            QuicDataChannelOptions options,
            TimeProvider clock,
            DataChannelApplicationBudget.DataChannelCarrierReservation reservation)
        {
            NativeConnection = connection;
            EndpointUrl = endpointUrl;
            m_management = management;
            m_options = options;
            m_clock = clock;
            m_nativeCapacity = reservation;
            Identity = new DataChannelPeerIdentity(
                connection.Credentials.Credentials.ApplicationUri,
                connection.Peer.ApplicationUri,
                connection.Credentials.LocalKeyThumbprint,
                connection.Peer.SpkiSha256,
                DataChannelProtectionMode.Confidential,
                connection.Peer.IsClient);
        }

        /// <summary>
        /// Gets the common application API, ready only after mutually confirmed HELLO.
        /// Use Context.OpenAsync/AcceptAsync for configured or explicitly Session-authorized flows.
        /// </summary>
        public DataChannelContext Context => m_context ??
            throw new InvalidOperationException("The data context has not been attached.");

        /// <summary>
        /// Gets the borrowed authenticated native connection for diagnostics and native evidence.
        /// The carrier owns it; consumers must not independently receive its streams or datagrams.
        /// </summary>
        public NativeQuicConnection NativeConnection { get; }

        /// <summary>
        /// Gets the native data endpoint, not a discovery or Session endpoint.
        /// </summary>
        public Uri EndpointUrl { get; }

        /// <summary>
        /// Gets completion of native ownership and awaited ingress drainage, including host capacity release.
        /// </summary>
        public Task Completion => m_ingressDrained.Task;

        /// <inheritdoc/>
        public DataChannelCarrierKind Kind => DataChannelCarrierKind.Quic;

        /// <inheritdoc/>
        public DataChannelPeerIdentity Identity { get; }

        /// <inheritdoc/>
        public int MaxRecordSize => m_options.MaxRecordSize;

        /// <inheritdoc/>
        public int MaxDatagramSize => NativeConnection.MaxDatagramSize;

        /// <inheritdoc/>
        public ulong ManagementStreamId => m_management.StreamId;

        /// <summary>
        /// Attaches a supplied authenticated native connection to the common runtime.
        /// Ownership transfers on entry, including failure cleanup. The factory is called before ingress/HELLO.
        /// </summary>
        public static async ValueTask<QuicDataChannelConnection> EstablishAsync(
            NativeQuicConnection connection,
            IDataChannelContextFactory contextFactory,
            Uri endpointUrl,
            QuicDataChannelOptions? options = null,
            TimeProvider? timeProvider = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(contextFactory);
            ArgumentNullException.ThrowIfNull(endpointUrl);
            options ??= new QuicDataChannelOptions();
            options.Validate();
            TimeProvider clock = timeProvider ?? TimeProvider.System;
            DataChannelApplicationBudget.DataChannelCarrierReservation? reservation = null;
            try
            {
                reservation = ReserveNativeCapacity(options);
                QuicDataChannelConnection established = await EstablishReservedAsync(
                    connection, contextFactory, endpointUrl, options, clock, reservation, cancellationToken)
                    .ConfigureAwait(false);
                reservation = null;
                return established;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                reservation?.Dispose();
            }
        }

        /// <summary>
        /// Reserves fixed worst-case additional native copies/jobs before admitting a connection.
        /// Logical flow/management stream reservations remain exclusively with Core.
        /// </summary>
        internal static DataChannelApplicationBudget.DataChannelCarrierReservation ReserveNativeCapacity(
            QuicDataChannelOptions options)
        {
            DataChannelApplicationBudget budget = options.ApplicationBudget ?? new DataChannelApplicationBudget();
            long bytes = (long)options.Native.MaxPendingOperations * (4 * 65536 + 16384) +
                (long)options.Native.MaxStreams * options.Native.ReadSize + 65536;
            if (!budget.TryReserveCarrier(
                bytes, options.MaxPendingStreams, options.Native.MaxPendingOperations,
                out DataChannelApplicationBudget.DataChannelCarrierReservation? reservation))
            {
                throw new DataChannelException(DataChannelResult.WouldBlock);
            }
            return reservation;
        }

        /// <summary>
        /// Establishes a context after native host/job capacity was already reserved by its endpoint factory.
        /// </summary>
        internal static async ValueTask<QuicDataChannelConnection> EstablishReservedAsync(
            NativeQuicConnection connection,
            IDataChannelContextFactory contextFactory,
            Uri endpointUrl,
            QuicDataChannelOptions options,
            TimeProvider clock,
            DataChannelApplicationBudget.DataChannelCarrierReservation reservation,
            CancellationToken cancellationToken)
        {
            QuicDataChannelConnection? carrier = null;
            try
            {
                if (connection.Peer.ApplicationProtocol != DataChannelProfiles.QuicAlpn)
                {
                    throw new DataChannelException(DataChannelResult.SecurityFailure);
                }
                using var deadline = new CancellationTokenSource(options.EstablishmentTimeout, clock);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, deadline.Token, connection.Closed);
                NativeQuicStream management = connection.Peer.IsClient
                    ? await connection.OpenStreamAsync(cancellationToken: linked.Token).ConfigureAwait(false)
                    : await connection.AcceptStreamAsync(cancellationToken: linked.Token).ConfigureAwait(false);
                if (management.StreamId != 0 || !management.IsBidirectional ||
                    management.IsLocal != connection.Peer.IsClient)
                {
                    throw new DataChannelException(DataChannelResult.SecurityFailure);
                }
                carrier = new QuicDataChannelConnection(
                    connection, management, endpointUrl, options, clock, reservation);
                carrier.m_context = contextFactory.Create(carrier);
                carrier.StartIngress();
                await carrier.Context.StartAsync(linked.Token).ConfigureAwait(false);
                return carrier;
            }
            catch
            {
                if (carrier != null)
                {
                    await carrier.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await connection.CloseAsync((ulong)DataChannelResult.SecurityFailure).ConfigureAwait(false);
                    reservation.Dispose();
                }
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ulong> OpenStreamAsync(CancellationToken cancellationToken = default)
        {
            lock (m_gate)
            {
                if (m_usedStreams.Count >= m_options.MaxStreamHistory)
                {
                    throw new DataChannelException(DataChannelResult.WouldBlock);
                }
            }
            NativeQuicStream stream = await NativeConnection.OpenStreamAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!StartStream(stream))
            {
                await stream.CloseAsync((ulong)DataChannelResult.WouldBlock).ConfigureAwait(false);
                throw new DataChannelException(DataChannelResult.WouldBlock);
            }
            return stream.StreamId;
        }

        /// <inheritdoc/>
        public ValueTask<bool> BindStreamAsync(
            ulong streamId,
            uint channelId,
            bool locallyOpened,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (m_shutdown.IsCancellationRequested || channelId == 0 || streamId == ManagementStreamId ||
                    m_usedStreams.Count >= m_options.MaxStreamHistory || m_usedStreams.Contains(streamId) ||
                    m_channelStreams.ContainsKey(channelId) ||
                    !m_ingress.TryGetValue(streamId, out StreamIngress? ingress) ||
                    !NativeConnection.TryGetStream(streamId, out NativeQuicStream? stream) ||
                    stream == null || !ReferenceEquals(stream, ingress.Stream) ||
                    !ReferenceEquals(stream.Connection, NativeConnection) ||
                    !stream.IsBidirectional || stream.IsLocal != locallyOpened || stream.IsClosed ||
                    ingress.Deadline.IsCancellationRequested)
                {
                    return ValueTask.FromResult(false);
                }
                m_usedStreams.Add(streamId);
                m_channelStreams.Add(channelId, streamId);
                ingress.ChannelId = channelId;
                ingress.Deadline.CancelAfter(Timeout.InfiniteTimeSpan);
                m_pendingStreams--;
                return ValueTask.FromResult(true);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<DataChannelResult> SendAsync(
            DataChannelWrite write,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(write);
            if (write.Payload.Length > (write.Path == DataChannelPath.Datagram ? MaxDatagramSize : MaxRecordSize))
            {
                return DataChannelResult.TooLarge;
            }
            try
            {
                NativeQuicCompletion? sent;
                if (write.Path == DataChannelPath.Datagram)
                {
                    lock (m_gate)
                    {
                        if (!write.IsData || !m_channelStreams.TryGetValue(write.ChannelId, out ulong streamId) ||
                            streamId != write.StreamId)
                        {
                            return DataChannelResult.InvalidState;
                        }
                    }
                    sent = await NativeConnection.SendDatagramAsync(
                        write.Payload, write.TryCommit, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    NativeQuicStream? stream;
                    lock (m_gate)
                    {
                        if (write.Path == DataChannelPath.Management)
                        {
                            stream = write.StreamId == ManagementStreamId ? m_management : null;
                        }
                        else
                        {
                            stream = write.Path == DataChannelPath.Flow &&
                                m_ingress.TryGetValue(write.StreamId, out StreamIngress? ingress) &&
                                ingress.ChannelId == write.ChannelId
                                ? ingress.Stream : null;
                        }
                    }
                    if (stream == null)
                    {
                        return DataChannelResult.InvalidState;
                    }
                    sent = await stream.SendAsync(
                        write.Payload, tryCommit: write.TryCommit, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
                if (sent == null)
                {
                    return write.Result;
                }
                DataChannelResult result = MapStatus(sent.Status);
                if (sent.Status == NativeQuicStatus.Success && sent.BytesTransferred != write.Payload.Length)
                {
                    result = DataChannelResult.Closed;
                }
                if (result != DataChannelResult.Good && write.Path != DataChannelPath.Datagram)
                {
                    if (write.Path == DataChannelPath.Management)
                    {
                        NativeConnection.RequestClose((ulong)result, result == DataChannelResult.SecurityFailure);
                    }
                    else
                    {
                        await CloseFlowAsync(write.StreamId, result, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                return result;
            }
            catch (OperationCanceledException)
            {
                return DataChannelResult.Closed;
            }
            catch (ObjectDisposedException)
            {
                return DataChannelResult.Closed;
            }
            catch (NativeQuicTransportException exception)
            {
                return exception.Completion == null ? DataChannelResult.WouldBlock : MapStatus(exception.Completion.Status);
            }
        }

        /// <inheritdoc/>
        public async ValueTask CloseFlowAsync(
            ulong streamId,
            DataChannelResult result,
            CancellationToken cancellationToken = default)
        {
            if (!Enum.IsDefined(result))
            {
                throw new ArgumentOutOfRangeException(nameof(result));
            }
            if (streamId == ManagementStreamId)
            {
                return;
            }
            NativeQuicStream? stream;
            lock (m_gate)
            {
                stream = m_ingress.TryGetValue(streamId, out StreamIngress? ingress) ? ingress.Stream : null;
            }
            if (stream != null)
            {
                await stream.CloseAsync((ulong)result).ConfigureAwait(false);
                RemoveStream(streamId);
            }
        }

        /// <inheritdoc/>
        public async ValueTask CloseAsync(
            DataChannelResult result,
            CancellationToken cancellationToken = default)
        {
            if (!Enum.IsDefined(result))
            {
                throw new ArgumentOutOfRangeException(nameof(result));
            }
            if (!m_shutdown.IsCancellationRequested)
            {
                m_shutdown.Cancel();
            }
            NativeConnection.RequestClose((ulong)result, result == DataChannelResult.SecurityFailure);
            BeginIngressDrain();
            await NativeConnection.Completion.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            bool start;
            lock (m_gate)
            {
                start = !m_disposing;
                m_disposing = true;
            }
            if (start)
            {
                _ = DisposeCoreAsync();
            }
            await m_disposed.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Attaches all ingress before StartAsync can transmit HELLO.
        /// </summary>
        private void StartIngress()
        {
            lock (m_gate)
            {
                m_ingressStarted = true;
            }
            try
            {
                m_managementPump = PumpManagementAsync();
                m_managementStop = ObserveTerminationAsync(m_management, false);
                m_managementReset = ObserveTerminationAsync(m_management, true);
                m_acceptPump = PumpAcceptAsync(false);
                m_unidirectionalPump = PumpAcceptAsync(true);
                m_datagramPump = PumpDatagramsAsync();
                m_connectionMonitor = MonitorConnectionAsync();
            }
            finally
            {
                m_ingressAttached.TrySetResult();
            }
        }

        /// <summary>
        /// Adds bounded unknown-stream state and starts one serialized reader, including provisional OPEN records.
        /// </summary>
        private bool StartStream(NativeQuicStream stream)
        {
            StreamIngress ingress;
            lock (m_gate)
            {
                if (m_shutdown.IsCancellationRequested || !stream.IsBidirectional ||
                    m_pendingStreams >= m_options.MaxPendingStreams || m_ingress.ContainsKey(stream.StreamId) ||
                    m_usedStreams.Contains(stream.StreamId) || m_activePumps.Count >= m_options.Native.MaxStreams - 1)
                {
                    return false;
                }
                ingress = new StreamIngress(stream, m_options.EstablishmentTimeout, m_clock);
                m_ingress.Add(stream.StreamId, ingress);
                m_activePumps.Add(ingress);
                m_pendingStreams++;
                ingress.StopObserver = ObserveTerminationAsync(stream, false);
                ingress.ResetObserver = ObserveTerminationAsync(stream, true);
                ingress.Pump = PumpStreamAsync(ingress);
                ingress.Completion = CompleteIngressAsync(ingress);
            }
            return true;
        }

        /// <summary>
        /// Reads only actual stream handles accepted from this authenticated native connection.
        /// </summary>
        private async Task PumpAcceptAsync(bool unidirectional)
        {
            try
            {
                while (!m_shutdown.IsCancellationRequested)
                {
                    NativeQuicStream stream;
                    try
                    {
                        stream = await NativeConnection.AcceptStreamAsync(unidirectional, m_shutdown.Token)
                            .ConfigureAwait(false);
                    }
                    catch (NativeQuicTransportException exception)
                        when (exception.Completion?.Status == NativeQuicStatus.Timeout)
                    {
                        continue;
                    }
                    if (unidirectional || !StartStream(stream))
                    {
                        await stream.CloseAsync((ulong)DataChannelResult.InvalidArgument).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception) when (m_shutdown.IsCancellationRequested || NativeConnection.IsClosed)
            {
                // Connection closure owns native cleanup.
            }
            catch (Exception)
            {
                await Context.InvalidateAsync(DataChannelResult.Closed).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Processes management segments serially through the common parser and awaits its backpressure.
        /// </summary>
        private async Task PumpManagementAsync()
        {
            try
            {
                while (!m_shutdown.IsCancellationRequested)
                {
                    NativeQuicCompletion read = await m_management.ReadAsync(m_shutdown.Token).ConfigureAwait(false);
                    if (!read.Payload.IsEmpty || read.ReadOutcome == 1)
                    {
                        DataChannelResult accepted = await Context.ReceiveStreamAsync(
                            ManagementStreamId, read.Payload, read.ReadOutcome == 1, m_shutdown.Token)
                            .ConfigureAwait(false);
                        if (accepted != DataChannelResult.Good && !Context.IsReady)
                        {
                            await Context.InvalidateAsync(accepted).ConfigureAwait(false);
                            return;
                        }
                    }
                    if (read.ReadOutcome != 0 ||
                        read.Status is not (NativeQuicStatus.Success or NativeQuicStatus.Timeout))
                    {
                        await Context.InvalidateAsync(MapStatus(read.Status)).ConfigureAwait(false);
                        return;
                    }
                }
            }
            catch (Exception) when (m_shutdown.IsCancellationRequested || NativeConnection.IsClosed)
            {
                // The lifetime monitor invalidates the common context.
            }
            catch (Exception)
            {
                await Context.InvalidateAsync(DataChannelResult.Closed).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Delivers actual stream segments in order, including bounded provisional common OPEN input.
        /// </summary>
        private async Task PumpStreamAsync(StreamIngress ingress)
        {
            NativeQuicStream stream = ingress.Stream;
            bool preserveWriteHalf = false;
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    m_shutdown.Token, stream.Closed, ingress.Deadline.Token);
                while (!linked.IsCancellationRequested)
                {
                    NativeQuicCompletion read = await stream.ReadAsync(linked.Token).ConfigureAwait(false);
                    if (!read.Payload.IsEmpty || read.ReadOutcome == 1)
                    {
                        DataChannelResult accepted = await Context.ReceiveStreamAsync(
                            stream.StreamId, read.Payload, read.ReadOutcome == 1, linked.Token).ConfigureAwait(false);
                        if (accepted != DataChannelResult.Good)
                        {
                            await stream.CloseAsync((ulong)accepted).ConfigureAwait(false);
                            return;
                        }
                    }
                    if (read.ReadOutcome != 0 ||
                        read.Status is not (NativeQuicStatus.Success or NativeQuicStatus.Timeout))
                    {
                        preserveWriteHalf = read.ReadOutcome == 1 && ingress.ChannelId != 0;
                        if (read.ReadOutcome != 1)
                        {
                            await Context.ReceiveStreamResetAsync(
                                stream.StreamId, MapReset(read.ErrorCode), m_shutdown.Token).ConfigureAwait(false);
                        }
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (!m_shutdown.IsCancellationRequested && !stream.IsClosed)
                {
                    await stream.CloseAsync((ulong)DataChannelResult.Timeout).ConfigureAwait(false);
                }
            }
            catch (Exception) when (m_shutdown.IsCancellationRequested || NativeConnection.IsClosed || stream.IsClosed)
            {
                // The owning context/flow has already closed admission.
            }
            catch (Exception)
            {
                await Context.ReceiveStreamResetAsync(
                    stream.StreamId, DataChannelResult.Closed, CancellationToken.None).ConfigureAwait(false);
                await stream.CloseAsync((ulong)DataChannelResult.Closed).ConfigureAwait(false);
            }
            finally
            {
                if (!preserveWriteHalf || stream.IsClosed || NativeConnection.IsClosed)
                {
                    try
                    {
                        await stream.CloseAsync((ulong)DataChannelResult.Closed).ConfigureAwait(false);
                    }
                    finally
                    {
                        RemoveStream(stream.StreamId);
                    }
                }
            }
        }

        /// <summary>
        /// Observes native RESET/STOP even while the ordered reader awaits common application capacity.
        /// Only actual peer events enter the Core out-of-band reset seam.
        /// </summary>
        private async Task ObserveTerminationAsync(NativeQuicStream stream, bool receiveReset)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(m_shutdown.Token, stream.Closed);
            try
            {
                while (!linked.IsCancellationRequested)
                {
                    NativeQuicCompletion observed = receiveReset
                        ? await stream.WaitResetReceivedAsync(linked.Token).ConfigureAwait(false)
                        : await stream.WaitStoppedAsync(linked.Token).ConfigureAwait(false);
                    if (observed.Status == NativeQuicStatus.Timeout)
                    {
                        continue;
                    }
                    if (observed.Status == NativeQuicStatus.Success && observed.Remote)
                    {
                        DataChannelResult result = MapReset(observed.ErrorCode);
                        await Context.ReceiveStreamResetAsync(stream.StreamId, result, m_shutdown.Token)
                            .ConfigureAwait(false);
                        if (stream.StreamId != ManagementStreamId)
                        {
                            await stream.CloseAsync((ulong)result).ConfigureAwait(false);
                            RemoveStream(stream.StreamId);
                        }
                    }
                    else if (observed.Status is NativeQuicStatus.Invalid or NativeQuicStatus.Capability)
                    {
                        await Context.InvalidateAsync(DataChannelResult.Unsupported).ConfigureAwait(false);
                    }
                    return;
                }
            }
            catch (Exception) when (linked.IsCancellationRequested || NativeConnection.IsClosed || stream.IsClosed)
            {
                // Every admitted native operation has drained before the helper returns or throws.
            }
            catch (Exception)
            {
                if (stream.StreamId == ManagementStreamId)
                {
                    await Context.InvalidateAsync(DataChannelResult.Closed).ConfigureAwait(false);
                }
                else
                {
                    await Context.ReceiveStreamResetAsync(stream.StreamId, DataChannelResult.Closed, CancellationToken.None)
                        .ConfigureAwait(false);
                    await stream.CloseAsync((ulong)DataChannelResult.Closed).ConfigureAwait(false);
                    RemoveStream(stream.StreamId);
                }
            }
        }

        /// <summary>
        /// Retires bounded host ingress only after the reader and both independent native observers have completed.
        /// </summary>
        private async Task CompleteIngressAsync(StreamIngress ingress)
        {
            try
            {
                await Task.WhenAll(ingress.Pump, ingress.StopObserver, ingress.ResetObserver).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                lock (m_gate)
                {
                    m_ingressFailure ??= exception;
                }
            }
            finally
            {
                ingress.Deadline.Dispose();
                lock (m_gate)
                {
                    m_activePumps.Remove(ingress);
                }
            }
        }

        /// <summary>
        /// Retires a live association only after native release, retaining the bounded anti-reuse history.
        /// </summary>
        private void RemoveStream(ulong streamId)
        {
            lock (m_gate)
            {
                if (m_ingress.Remove(streamId, out StreamIngress? ingress))
                {
                    if (ingress.ChannelId == 0)
                    {
                        m_pendingStreams--;
                    }
                    else
                    {
                        m_channelStreams.Remove(ingress.ChannelId);
                    }
                }
            }
        }

        /// <summary>
        /// Delivers one native datagram at a time; common invalid/replayed datagram outcomes are isolated drops.
        /// </summary>
        private async Task PumpDatagramsAsync()
        {
            try
            {
                while (!m_shutdown.IsCancellationRequested)
                {
                    NativeQuicCompletion read = await NativeConnection.ReceiveDatagramAsync(m_shutdown.Token)
                        .ConfigureAwait(false);
                    if (read.Status == NativeQuicStatus.Success)
                    {
                        await Context.ReceiveDatagramAsync(read.Payload, m_shutdown.Token).ConfigureAwait(false);
                    }
                    else if (read.Status != NativeQuicStatus.Timeout)
                    {
                        await Context.InvalidateAsync(MapStatus(read.Status)).ConfigureAwait(false);
                        return;
                    }
                }
            }
            catch (Exception) when (m_shutdown.IsCancellationRequested || NativeConnection.IsClosed)
            {
                // The lifetime monitor invalidates the common context.
            }
            catch (Exception)
            {
                await Context.InvalidateAsync(DataChannelResult.Closed).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Makes actual native TLS/connection loss visible to pending application operations.
        /// </summary>
        private async Task MonitorConnectionAsync()
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, NativeConnection.Closed).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await Context.InvalidateAsync(
                    NativeConnection.SecurityInvalidated ? DataChannelResult.SecurityFailure : DataChannelResult.Closed)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Drains common runtime work and native ownership before completing disposal.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            try
            {
                m_shutdown.Cancel();
                lock (m_gate)
                {
                    if (!m_ingressStarted)
                    {
                        m_ingressAttached.TrySetResult();
                    }
                }
                if (m_context != null)
                {
                    await m_context.DisposeAsync().ConfigureAwait(false);
                }
                await NativeConnection.DisposeAsync().ConfigureAwait(false);
                BeginIngressDrain();
                await Completion.ConfigureAwait(false);
                m_shutdown.Dispose();
                m_disposed.TrySetResult();
            }
            catch (Exception exception)
            {
                m_disposed.TrySetException(exception);
            }
        }

        /// <summary>
        /// Starts host drainage without joining the ingress callback that may currently be closing the context.
        /// </summary>
        private void BeginIngressDrain()
        {
            lock (m_gate)
            {
                if (m_drainStarted)
                {
                    return;
                }
                m_drainStarted = true;
            }
            _ = DrainIngressAsync();
        }

        /// <summary>
        /// Retains additional native/host capacity until both physical jobs and borrowed ingress payloads drain.
        /// </summary>
        private async Task DrainIngressAsync()
        {
            try
            {
                await NativeConnection.Completion.ConfigureAwait(false);
                await m_ingressAttached.Task.ConfigureAwait(false);
                Task[] pumps;
                lock (m_gate)
                {
                    pumps =
                    [
                        m_managementPump, m_acceptPump, m_unidirectionalPump, m_datagramPump, m_connectionMonitor,
                        m_managementStop, m_managementReset,
                        .. m_activePumps.Select(ingress => ingress.Completion)
                    ];
                }
                try
                {
                    await Task.WhenAll(pumps).ConfigureAwait(false);
                    if (m_ingressFailure != null)
                    {
                        throw new InvalidOperationException("A native ingress operation failed during drainage.", m_ingressFailure);
                    }
                }
                finally
                {
                    m_nativeCapacity.Dispose();
                }
                m_ingressDrained.TrySetResult();
            }
            catch (Exception exception)
            {
                m_ingressDrained.TrySetException(exception);
            }
        }

        /// <summary>
        /// Maps actual backend outcomes to the generated common result vocabulary.
        /// </summary>
        private static DataChannelResult MapStatus(NativeQuicStatus status)
        {
            return status switch
            {
                NativeQuicStatus.Success => DataChannelResult.Good,
                NativeQuicStatus.Authentication => DataChannelResult.SecurityFailure,
                NativeQuicStatus.Capability => DataChannelResult.Unsupported,
                NativeQuicStatus.Invalid => DataChannelResult.InvalidArgument,
                NativeQuicStatus.TooLarge => DataChannelResult.TooLarge,
                NativeQuicStatus.Timeout => DataChannelResult.Timeout,
                NativeQuicStatus.Limit or NativeQuicStatus.Busy => DataChannelResult.WouldBlock,
                _ => DataChannelResult.Closed
            };
        }

        /// <summary>
        /// Retains a defined native flow error; an unrecognized or zero abrupt reset means Closed, never success.
        /// </summary>
        private static DataChannelResult MapReset(ulong errorCode)
        {
            if (errorCode <= int.MaxValue && Enum.IsDefined((DataChannelResult)errorCode) &&
                errorCode != (ulong)DataChannelResult.Good)
            {
                return (DataChannelResult)errorCode;
            }
            return DataChannelResult.Closed;
        }

        /// <summary>
        /// One bounded actual stream and its serialized reader, before or after common flow binding.
        /// </summary>
        private sealed class StreamIngress(
            NativeQuicStream stream,
            TimeSpan timeout,
            TimeProvider clock)
        {
            /// <summary>
            /// Gets the actual accepted/allocated native stream owner.
            /// </summary>
            internal NativeQuicStream Stream { get; } = stream;

            /// <summary>
            /// Gets the provisional-association deadline, disabled when binding succeeds.
            /// </summary>
            internal CancellationTokenSource Deadline { get; } = new(timeout, clock);

            /// <summary>
            /// Gets or sets the verified associated common ChannelId.
            /// </summary>
            internal uint ChannelId { get; set; }

            /// <summary>
            /// Gets or sets the one awaited ingress worker.
            /// </summary>
            internal Task Pump { get; set; } = Task.CompletedTask;

            /// <summary>
            /// Gets or sets the actual peer STOP_SENDING observer.
            /// </summary>
            internal Task StopObserver { get; set; } = Task.CompletedTask;

            /// <summary>
            /// Gets or sets the actual peer RESET observer, independent of application reads.
            /// </summary>
            internal Task ResetObserver { get; set; } = Task.CompletedTask;

            /// <summary>
            /// Gets or sets completion of every referencing ingress task.
            /// </summary>
            internal Task Completion { get; set; } = Task.CompletedTask;
        }

        private readonly QuicDataChannelOptions m_options;
        private readonly TimeProvider m_clock;
        private readonly NativeQuicStream m_management;
        private readonly DataChannelApplicationBudget.DataChannelCarrierReservation m_nativeCapacity;
        private readonly Lock m_gate = new();
        private readonly CancellationTokenSource m_shutdown = new();
        private readonly Dictionary<ulong, StreamIngress> m_ingress = [];
        private readonly HashSet<StreamIngress> m_activePumps = [];
        private readonly Dictionary<uint, ulong> m_channelStreams = [];
        private readonly HashSet<ulong> m_usedStreams = [];
        private readonly TaskCompletionSource m_disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource m_ingressAttached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource m_ingressDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DataChannelContext? m_context;
        private Exception? m_ingressFailure;
        private Task m_managementPump = Task.CompletedTask;
        private Task m_managementStop = Task.CompletedTask;
        private Task m_managementReset = Task.CompletedTask;
        private Task m_acceptPump = Task.CompletedTask;
        private Task m_unidirectionalPump = Task.CompletedTask;
        private Task m_datagramPump = Task.CompletedTask;
        private Task m_connectionMonitor = Task.CompletedTask;
        private int m_pendingStreams;
        private bool m_disposing;
        private bool m_ingressStarted;
        private bool m_drainStarted;
    }
}
