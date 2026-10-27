// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings
{
    public sealed partial class WebTransportDataChannelConnection
    {
        /// <summary>
        /// Commits at the ordered writer handoff, then completes every segment without caller-token retraction.
        /// </summary>
        private async ValueTask<DataChannelResult> SendStreamRecordAsync(
            StreamState stream,
            DataChannelWrite write,
            CancellationToken cancellationToken)
        {
            using var admission = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime);
            await stream.PrefixComplete.Task.WaitAsync(admission.Token).ConfigureAwait(false);
            await stream.Writer.WaitAsync(admission.Token).ConfigureAwait(false);
            try
            {
                lock (m_gate)
                {
                    admission.Token.ThrowIfCancellationRequested();
                    if (m_closing || stream.Closing)
                    {
                        return DataChannelResult.Closed;
                    }
                    if (!write.TryCommit())
                    {
                        return write.Result;
                    }
                }
                int offset = 0;
                while (offset < write.Payload.Length)
                {
                    int length = Math.Min(
                        Math.Min(65536, m_options.Http3OutputBytes - 128), write.Payload.Length - offset);
                    while (true)
                    {
                        Task? changed = null;
                        PendingSend? pending = null;
                        lock (m_gate)
                        {
                            if (m_closing || stream.Closing)
                            {
                                return DataChannelResult.Closed;
                            }
                            pending = new PendingSend();
                            stream.PendingSend = pending;
                            if (!TryCommandLocked(13, stream.Stream.StreamId, 0,
                                write.EndsDirection ? 1UL : 0UL,
                                write.Payload.Slice(offset, length), out Http3Error error))
                            {
                                stream.PendingSend = null;
                                pending = null;
                                if (!error.Retryable)
                                {
                                    throw new WebTransportException(error);
                                }
                                if (length > 1)
                                {
                                    length = Math.Max(1, length / 2);
                                }
                                else
                                {
                                    changed = m_changed.Task;
                                }
                            }
                        }
                        if (pending != null)
                        {
                            DataChannelResult result = await pending.Completion.Task.ConfigureAwait(false);
                            lock (m_gate)
                            {
                                if (ReferenceEquals(stream.PendingSend, pending))
                                {
                                    stream.PendingSend = null;
                                }
                            }
                            if (result != DataChannelResult.Good)
                            {
                                return result;
                            }
                            offset += length;
                            break;
                        }
                        if (changed != null)
                        {
                            await changed.WaitAsync(m_lifetime).ConfigureAwait(false);
                        }
                    }
                }
                return DataChannelResult.Good;
            }
            finally
            {
                stream.Writer.Release();
            }
        }

        /// <summary>
        /// Gives H3 the unchanged ChannelId+record once; native output already includes HTTP datagram framing.
        /// </summary>
        private async ValueTask<DataChannelResult> SendDatagramRecordAsync(
            DataChannelWrite write,
            CancellationToken cancellationToken)
        {
            using var admission = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime);
            await m_datagramWriter.WaitAsync(admission.Token).ConfigureAwait(false);
            try
            {
                lock (m_gate)
                {
                    admission.Token.ThrowIfCancellationRequested();
                    if (m_closing)
                    {
                        return DataChannelResult.Closed;
                    }
                    if (!write.TryCommit())
                    {
                        return write.Result;
                    }
                    m_datagramSend = new PendingSend();
                }
                await RetryCommandAsync(14, 0, 0, write.Payload, m_lifetime).ConfigureAwait(false);
                return await m_datagramSend.Completion.Task.ConfigureAwait(false);
            }
            finally
            {
                lock (m_gate)
                {
                    m_datagramSend = null;
                }
                m_datagramWriter.Release();
            }
        }

        /// <summary>
        /// Performs actual native I/O outside H3 synchronization and acknowledges exact cumulative per-token bytes.
        /// The copied payload stays owned until the native operation and receipt have completed.
        /// </summary>
        private async ValueTask TransmitLeaseAsync(SendLease lease, StreamState? stream)
        {
            Http3Event metadata = lease.Metadata;
            bool datagram = metadata.Kind == 2;
            bool prefix = (metadata.Flags & 2) != 0;
            bool retainWrites;
            lock (m_gate)
            {
                retainWrites = stream?.RetainWrites == true;
            }
            bool fin = (metadata.Flags & 1) != 0;
            bool critical = !datagram && (stream!.Stream.StreamId == 0 ||
                !stream.Stream.IsBidirectional || ReferenceEquals(stream, m_management));
            SemaphoreSlim sendGate = critical ? m_criticalSends : m_streamSends;
            DataChannelResult outcome = DataChannelResult.Closed;
            bool success = false;
            bool admittedGate = false;
            bool definitive = true;
            try
            {
                if (m_closing)
                {
                    return;
                }
                await sendGate.WaitAsync(m_lifetime).ConfigureAwait(false);
                admittedGate = true;
                if (!prefix && stream is { Closing: true })
                {
                    return;
                }
                int offset = 0;
                while (true)
                {
                    int length = Math.Min(65536, lease.Payload.Length - offset);
                    uint flags = prefix || retainWrites ? 2u : 0u;
                    if (fin && offset + length == lease.Payload.Length)
                    {
                        flags |= 1;
                    }
                    NativeQuicCompletion? actual;
                    using var admissionDeadline = new CancellationTokenSource(
                        m_options.Native.OperationTimeout, m_clock);
                    using var retryToken = CancellationTokenSource.CreateLinkedTokenSource(
                        m_lifetime, admissionDeadline.Token);
                    while (true)
                    {
                        actual = datagram
                            ? await NativeConnection.SendDatagramAsync(
                                lease.Payload, static () => true, retryToken.Token).ConfigureAwait(false)
                            : await stream!.Stream.SendAsync(
                                lease.Payload.Slice(offset, length), flags, static () => true, retryToken.Token)
                                .ConfigureAwait(false);
                        if (actual == null)
                        {
                            throw new WebTransportException("The native lease was refused after host commitment.");
                        }
                        if (actual.NativeStatus is not (NativeQuicStatus.Limit or NativeQuicStatus.Busy))
                        {
                            break;
                        }
                        if (actual.BytesTransferred != 0)
                        {
                            throw new WebTransportException("A nonconsuming native refusal accepted bytes.");
                        }
                        await Task.Delay(TimeSpan.FromMilliseconds(10), m_clock, retryToken.Token)
                            .ConfigureAwait(false);
                    }
                    if (actual.BytesTransferred > length)
                    {
                        throw new WebTransportException("The native write exceeded its leased segment.");
                    }
                    lease.Accepted += actual.BytesTransferred;
                    bool full = actual.NativeStatus == NativeQuicStatus.Success &&
                        actual.BytesTransferred == length;
                    if (!full)
                    {
                        outcome = MapNative(actual.Status);
                        if (outcome == DataChannelResult.Good)
                        {
                            outcome = DataChannelResult.Closed;
                        }
                        break;
                    }
                    offset += length;
                    if (offset == lease.Payload.Length)
                    {
                        success = true;
                        outcome = DataChannelResult.Good;
                        break;
                    }
                    if (prefix || datagram)
                    {
                        throw new WebTransportException("An atomic prefix or datagram was segmented.");
                    }
                    lock (m_gate)
                    {
                        CommandLocked(20, metadata.Arg, lease.Accepted, 0);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // A wrapper throws cancellation only before native admission; admitted jobs return their actual result.
                outcome = DataChannelResult.Closed;
            }
            catch (InvalidOperationException) when (!prefix && stream is { Closing: true })
            {
                // Reset may win the native wrapper's admission gate before this leased body is submitted.
                outcome = DataChannelResult.Closed;
            }
            catch (NativeQuicTransportException exception) when (exception.Completion != null)
            {
                lease.Accepted += exception.Completion.BytesTransferred;
                outcome = MapNative(exception.Completion.Status);
            }
            catch (Exception exception)
            {
                definitive = false;
                RequestClose(DataChannelResult.Closed, exception);
            }
            finally
            {
                try
                {
                    lock (m_gate)
                    {
                        if (stream != null && ReferenceEquals(stream.ActiveLease, lease))
                        {
                            stream.ActiveLease = null;
                        }
                        if (definitive)
                        {
                            CommandLocked(20, metadata.Arg, lease.Accepted, success ? fin ? 3UL : 1UL : 5UL);
                        }
                        else
                        {
                            // No receipt is fabricated for an unreported native outcome. Connection teardown owns it.
                            RequestClose(DataChannelResult.Closed);
                        }
                        if (prefix)
                        {
                            if (success)
                            {
                                stream!.PrefixComplete.TrySetResult();
                            }
                            else
                            {
                                RequestClose(DataChannelResult.Closed);
                            }
                        }
                        if (!success && ((datagram && outcome != DataChannelResult.TooLarge) ||
                            critical || (stream != null && !stream.Closing)))
                        {
                            RequestClose(outcome);
                        }
                    }
                }
                finally
                {
                    lock (m_gate)
                    {
                        m_leases.Remove(metadata.Arg);
                        if (stream != null && ReferenceEquals(stream.ActiveLease, lease))
                        {
                            stream.ActiveLease = null;
                        }
                        lease.Pending?.Completion.TrySetResult(outcome);
                        lease.Completion.TrySetResult(outcome);
                        PulseLocked();
                    }
                    if (admittedGate)
                    {
                        sendGate.Release();
                    }
                }
            }
        }

        /// <summary>
        /// Interrupts a leased body using the exact mapped wire error, then reports actual locally sent reset sizes.
        /// No writer gate is held while waiting for the original send or packetizer evidence.
        /// </summary>
        private async ValueTask ResetNativeSendAsync(StreamState stream, Http3Event metadata)
        {
            ValueTask<NativeQuicCompletion> resetting = stream.Stream.ResetSendAsync(metadata.Arg);
            stream.ResetSignalled.TrySetResult();
            NativeQuicCompletion reset = await resetting.ConfigureAwait(false);
            if (reset.NativeStatus is not (NativeQuicStatus.Success or NativeQuicStatus.Closed))
            {
                throw new NativeQuicTransportException(reset);
            }
            while (true)
            {
                Task? pending;
                lock (m_gate)
                {
                    pending = stream.ActiveLease?.Completion.Task;
                }
                if (pending == null)
                {
                    break;
                }
                await pending.ConfigureAwait(false);
            }
            NativeQuicCompletion observed = await stream.Stream.WaitResetSentAsync(m_lifetime)
                .ConfigureAwait(false);
            if (observed.NativeStatus != NativeQuicStatus.Success || observed.Remote ||
                observed.ReliableSize < metadata.Aux || observed.FinalSize < observed.ReliableSize ||
                (observed.ReliableSize != 0 && observed.ResetFrameType != 0x24) ||
                (observed.ReliableSize == 0 && observed.ResetFrameType is not (0x04 or 0x24)))
            {
                throw new WebTransportException(
                    $"Invalid local reset evidence: status={observed.NativeStatus}, remote={observed.Remote}, " +
                    $"final={observed.FinalSize}, reliable={observed.ReliableSize}, " +
                    $"minimum={metadata.Aux}, frame={observed.ResetFrameType}.");
            }
            if (observed.ErrorCode != metadata.Arg)
            {
                NativeQuicCompletion stopped = await stream.PeerStopped.Task
                    .WaitAsync(m_options.Native.OperationTimeout, m_clock, m_lifetime).ConfigureAwait(false);
                if (!stopped.Remote || stopped.ErrorCode != observed.ErrorCode)
                {
                    throw new WebTransportException(
                        $"Local reset error {observed.ErrorCode} matches neither requested {metadata.Arg} " +
                        $"nor observed peer STOP {stopped.ErrorCode}.");
                }
            }
            lock (m_gate)
            {
                CommandLocked(22, stream.Stream.StreamId, observed.FinalSize, observed.ReliableSize);
                // A crossing peer STOP can win native first-code ownership. Preserve its actual wire error;
                // never claim that the later local request replaced it or issue a differently coded STOP.
                stream.ResetError = observed.ErrorCode;
                stream.ResetComplete.TrySetResult();
            }
        }

        /// <summary>
        /// Sends an actual STOP_SENDING for only the indicated receive direction.
        /// </summary>
        private async ValueTask StopNativeReceiveAsync(StreamState stream, ulong errorCode)
        {
            Task signalled;
            lock (m_gate)
            {
                signalled = stream.ResetRequested ? stream.ResetSignalled.Task : Task.CompletedTask;
            }
            // Publish the local mapped cancellation before STOP can provoke a racing peer response.
            // This waits for signalling only, never for the blocked body or reset packetizer result.
            await signalled.WaitAsync(m_lifetime).ConfigureAwait(false);
            NativeQuicCompletion stopped = await stream.Stream.StopReceiveAsync(errorCode, m_lifetime)
                .ConfigureAwait(false);
            if (stopped.NativeStatus is not (NativeQuicStatus.Success or NativeQuicStatus.Closed))
            {
                throw new NativeQuicTransportException(stopped);
            }
        }

        /// <summary>
        /// Maps actual native outcomes, never inferring success from missing evidence.
        /// </summary>
        private static DataChannelResult MapNative(NativeQuicStatus status)
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
    }
}
