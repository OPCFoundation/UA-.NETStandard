// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings
{
    public sealed partial class NativeQuicUascConnection
    {
        /// <inheritdoc/>
        public bool IsAvailable => Volatile.Read(ref m_disposed) == 0 &&
            !m_connection.IsClosed && m_stream.CanRead && m_stream.CanWrite;

        /// <inheritdoc/>
        public string Capability => DataChannelProfiles.QuicDirectCapability;

        /// <inheritdoc/>
        public string Authority => EndpointUrl.Authority;

        /// <inheritdoc/>
        public string Path => EndpointUrl.PathAndQuery;

        /// <inheritdoc/>
        public bool AreDirectStreamsAvailable => IsAvailable;

        /// <inheritdoc/>
        public int MaxDirectDatagramSize => IsAvailable ? m_connection.MaxDatagramSize : 0;

        /// <inheritdoc/>
        public async ValueTask<ByteString> ExportKeyingMaterialAsync(
            ByteString transcriptDigest,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (!IsAvailable)
            {
                throw new DataChannelException(DataChannelResult.Closed);
            }
            NativeQuicCompletion exported = await m_connection.ExportKeyingMaterialAsync(
                transcriptDigest, cancellationToken).ConfigureAwait(false);
            if (exported.Status != NativeQuicStatus.Success)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new DataChannelException(MapDirectStatus(exported.Status));
            }
            if (exported.Payload.Length != 32)
            {
                throw new DataChannelException(DataChannelResult.SecurityFailure);
            }
            return exported.Payload;
        }

        /// <inheritdoc/>
        public async ValueTask<Stream> OpenDirectStreamAsync(
            Guid associationToken,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                ReserveDirectTokenLocked(associationToken);
            }
            using var deadline = new CancellationTokenSource(m_options.EstablishmentTimeout, m_clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, deadline.Token, m_connection.Closed);
            NativeQuicStream native;
            try
            {
                native = await m_connection.OpenStreamAsync(cancellationToken: linked.Token).ConfigureAwait(false);
            }
            catch (NativeQuicTransportException exception)
            {
                linked.Token.ThrowIfCancellationRequested();
                throw new DataChannelException(exception.Completion == null
                    ? DataChannelResult.WouldBlock : MapDirectStatus(exception.Completion.Status));
            }
            var stream = new NativeQuicByteStream(native);
            try
            {
                await stream.WriteAsync(associationToken.ToByteArray(), linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                return stream;
            }
            catch
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<Stream> AcceptDirectStreamAsync(
            Guid associationToken,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            cancellationToken.ThrowIfCancellationRequested();
            TaskCompletionSource<Stream> admission;
            lock (m_gate)
            {
                if (m_directAdmissions.Count >= m_options.MaxPendingStreams)
                {
                    throw new DataChannelException(DataChannelResult.WouldBlock);
                }
                ReserveDirectTokenLocked(associationToken);
                admission = new(TaskCreationOptions.RunContinuationsAsynchronously);
                m_directAdmissions.Add(associationToken, admission);
            }
            using var deadline = new CancellationTokenSource(m_options.EstablishmentTimeout, m_clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, deadline.Token, m_connection.Closed);
            try
            {
                Stream stream = await admission.Task.WaitAsync(linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                return stream;
            }
            catch (Exception exception)
            {
                Task<Stream>? abandoned = null;
                lock (m_gate)
                {
                    m_directAdmissions.Remove(associationToken);
                    if (admission.Task.IsCompletedSuccessfully)
                    {
                        abandoned = admission.Task;
                    }
                }
                if (abandoned != null)
                {
                    Stream stream = await abandoned.ConfigureAwait(false);
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
                if (exception is OperationCanceledException && deadline.IsCancellationRequested &&
                    !cancellationToken.IsCancellationRequested && !m_connection.Closed.IsCancellationRequested)
                {
                    throw new DataChannelException(DataChannelResult.Timeout);
                }
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<DataChannelResult> SendDirectDatagramAsync(
            DataChannelWrite write,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(write);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (write.Path != DataChannelPath.Datagram || !write.IsData)
            {
                return DataChannelResult.InvalidArgument;
            }
            if (MaxDirectDatagramSize == 0)
            {
                return DataChannelResult.Unsupported;
            }
            if (write.Payload.Length > MaxDirectDatagramSize)
            {
                return DataChannelResult.TooLarge;
            }
            try
            {
                NativeQuicCompletion? sent = await m_connection.SendDatagramAsync(
                    write.Payload, write.TryCommit, cancellationToken).ConfigureAwait(false);
                return sent == null ? write.Result : MapDirectStatus(sent.Status);
            }
            catch (NativeQuicTransportException exception) when (exception.Completion == null)
            {
                return DataChannelResult.WouldBlock;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ByteString> ReceiveDirectDatagramAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (MaxDirectDatagramSize == 0)
            {
                throw new DataChannelException(DataChannelResult.Unsupported);
            }
            if (Interlocked.CompareExchange(ref m_directDatagramReading, 1, 0) != 0)
            {
                throw new DataChannelException(DataChannelResult.InvalidState);
            }
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, m_connection.Closed);
                while (true)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    NativeQuicCompletion read = await m_connection.ReceiveDatagramAsync(linked.Token)
                        .ConfigureAwait(false);
                    if (read.Status == NativeQuicStatus.Timeout)
                    {
                        continue;
                    }
                    linked.Token.ThrowIfCancellationRequested();
                    if (read.Status != NativeQuicStatus.Success)
                    {
                        throw new DataChannelException(MapDirectStatus(read.Status));
                    }
                    return read.Payload;
                }
            }
            finally
            {
                Volatile.Write(ref m_directDatagramReading, 0);
            }
        }

        /// <summary>
        /// Retains bounded, application-accounted token tombstones until this actual connection closes.
        /// </summary>
        private void ReserveDirectTokenLocked(Guid token)
        {
            if (!IsAvailable)
            {
                throw new DataChannelException(DataChannelResult.Closed);
            }
            if (token == Guid.Empty)
            {
                throw new DataChannelException(DataChannelResult.InvalidArgument);
            }
            if (m_directTokens.Contains(token))
            {
                throw new DataChannelException(DataChannelResult.InvalidState);
            }
            if (m_directTokens.Count >= m_options.MaxStreamHistory)
            {
                throw new DataChannelException(DataChannelResult.WouldBlock);
            }
            if (m_directHistoryReservation == null)
            {
                DataChannelApplicationBudget budget = m_options.ApplicationBudget ??
                    throw new InvalidOperationException("The native baseline requires an application budget.");
                if (!budget.TryReserveCarrier(
                    (long)m_options.MaxStreamHistory * 256, 0, 0, out m_directHistoryReservation))
                {
                    throw new DataChannelException(DataChannelResult.WouldBlock);
                }
            }
            m_directTokens.Add(token);
        }

        /// <summary>
        /// Routes bounded actual streams by their complete Guid preface rather than QUIC stream identifiers.
        /// One provisional stream per native stream kind is read at a time, with a finite preface deadline.
        /// </summary>
        private async Task AcceptDirectStreamsAsync(bool unidirectional)
        {
            try
            {
                while (!m_connection.Closed.IsCancellationRequested)
                {
                    NativeQuicStream native;
                    try
                    {
                        await m_connection.WaitForStreamAllocationCapacityAsync(m_connection.Closed)
                            .ConfigureAwait(false);
                        native = await m_connection.AcceptStreamAsync(
                            unidirectional: unidirectional,
                            cancellationToken: m_connection.Closed).ConfigureAwait(false);
                    }
                    catch (NativeQuicTransportException exception)
                        when (exception.Completion?.Status == NativeQuicStatus.Timeout)
                    {
                        continue;
                    }
                    catch (NativeQuicTransportException exception) when (exception.Completion == null)
                    {
                        continue;
                    }
                    catch (NativeQuicTransportException exception)
                        when (exception.Completion?.Status is NativeQuicStatus.Limit or NativeQuicStatus.Busy)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(5), m_clock, m_connection.Closed)
                            .ConfigureAwait(false);
                        continue;
                    }
                    NativeQuicByteStream? stream = new(native);
                    bool ownsPreface = false;
                    try
                    {
                        DataChannelResult refusal;
                        lock (m_gate)
                        {
                            refusal = m_directAdmissions.Count == 0
                                ? DataChannelResult.InvalidState : DataChannelResult.WouldBlock;
                            if (m_directAdmissions.Count != 0 && m_directPrefaces < m_options.MaxPendingStreams)
                            {
                                m_directPrefaces++;
                                ownsPreface = true;
                            }
                        }
                        if (!ownsPreface)
                        {
                            await native.CloseAsync((ulong)refusal).ConfigureAwait(false);
                            continue;
                        }
                        using var deadline = new CancellationTokenSource(m_options.EstablishmentTimeout, m_clock);
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                            deadline.Token, m_connection.Closed);
                        byte[] preface = new byte[16];
                        await stream.ReadExactlyAsync(preface, linked.Token).ConfigureAwait(false);
                        Guid token = new(preface);
                        lock (m_gate)
                        {
                            if (IsAvailable &&
                                m_directAdmissions.Remove(token, out TaskCompletionSource<Stream>? target) &&
                                target.TrySetResult(stream))
                            {
                                m_directPrefaces--;
                                ownsPreface = false;
                                stream = null;
                            }
                        }
                        if (stream != null)
                        {
                            await native.CloseAsync((ulong)DataChannelResult.InvalidState).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (!m_connection.IsClosed)
                    {
                        await native.CloseAsync((ulong)DataChannelResult.Timeout).ConfigureAwait(false);
                    }
                    catch (EndOfStreamException)
                    {
                        await native.CloseAsync((ulong)DataChannelResult.InvalidArgument).ConfigureAwait(false);
                    }
                    catch (NativeQuicTransportException) when (!m_connection.IsClosed)
                    {
                        await native.CloseAsync((ulong)DataChannelResult.Closed).ConfigureAwait(false);
                    }
                    finally
                    {
                        try
                        {
                            if (stream != null)
                            {
                                await stream.DisposeAsync().ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            if (ownsPreface)
                            {
                                lock (m_gate)
                                {
                                    m_directPrefaces--;
                                }
                            }
                        }
                    }
                }
            }
            catch (NativeQuicTransportException exception)
                when (exception.Completion?.Status == NativeQuicStatus.Closed)
            {
                m_connection.RequestClose(exception.Completion.ErrorCode, false);
            }
            catch (Exception) when (m_connection.IsClosed)
            {
                // The connection owner joins native cancellation and handle release.
            }
            catch (Exception exception)
            {
                lock (m_gate)
                {
                    foreach (TaskCompletionSource<Stream> admission in m_directAdmissions.Values)
                    {
                        admission.TrySetException(exception);
                    }
                }
                m_connection.RequestClose(0x100, false);
                throw;
            }
        }

        /// <summary>
        /// Preserves explicit capacity and capability refusals without treating them as baseline failure.
        /// </summary>
        private static DataChannelResult MapDirectStatus(NativeQuicStatus status) => status switch
        {
            NativeQuicStatus.Success => DataChannelResult.Good,
            NativeQuicStatus.Capability => DataChannelResult.Unsupported,
            NativeQuicStatus.TooLarge => DataChannelResult.TooLarge,
            NativeQuicStatus.Busy or NativeQuicStatus.Limit => DataChannelResult.WouldBlock,
            NativeQuicStatus.Timeout => DataChannelResult.Timeout,
            NativeQuicStatus.Authentication => DataChannelResult.SecurityFailure,
            NativeQuicStatus.Invalid => DataChannelResult.InvalidArgument,
            _ => DataChannelResult.Closed
        };

        /// <summary>The bounded set of expected actual-stream tokens.</summary>
        private readonly Dictionary<Guid, TaskCompletionSource<Stream>> m_directAdmissions = [];

        /// <summary>Lifetime token nonreuse history; never a source of payload authorization.</summary>
        private readonly HashSet<Guid> m_directTokens = [];

        /// <summary>The actual-stream acceptance workers, joined before releasing carrier capacity.</summary>
        private readonly Task m_directAcceptTask;

        /// <summary>The fixed maximum retained token/admission storage reservation.</summary>
        private DataChannelApplicationBudget.DataChannelCarrierReservation? m_directHistoryReservation;

        /// <summary>The single pending native datagram receiver guard.</summary>
        private int m_directDatagramReading;

        /// <summary>The shared pending-preface bound across both native stream kinds.</summary>
        private int m_directPrefaces;
    }
}
