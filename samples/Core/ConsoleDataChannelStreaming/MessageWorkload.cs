/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.DataChannels;

namespace ConsoleDataChannelStreaming
{
    /// <summary>
    /// The same complete-message producer/consumer for every carrier and both sample applications.
    /// </summary>
    internal static class MessageWorkload
    {
        /// <summary>
        /// Sends finite owned messages, validates every received byte, and consumes directional EOF.
        /// The optional receiver borrows a payload only until its asynchronous callback completes.
        /// </summary>
        public static async Task<SampleResult> RunAsync(
            StreamingHarness harness,
            SampleOptions options,
            Func<int, ByteString> messageFactory,
            Func<int, ByteString, CancellationToken, ValueTask>? receive,
            CancellationToken cancellationToken)
        {
            using var requestedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (options.CancelAfterMilliseconds > 0)
            {
                requestedCancellation.CancelAfter(options.CancelAfterMilliseconds);
            }
            using var failure = CancellationTokenSource.CreateLinkedTokenSource(requestedCancellation.Token);
            var midpoint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int readBefore = harness.Services?.Reads ?? 0;
            int publishBefore = harness.Services?.Publishes ?? 0;
            int notificationsBefore = harness.Services?.Notifications ?? 0;
            var watch = Stopwatch.StartNew();
            Task<(int Count, long Bytes, string Hash)> consumer = ReceiveAsync();
            Task<string> producer = SendAsync();
            Task first = await Task.WhenAny(consumer, producer).ConfigureAwait(false);
            if (!first.IsCompletedSuccessfully)
            {
                failure.Cancel();
            }
            try
            {
                await Task.WhenAll(consumer, producer).ConfigureAwait(false);
            }
            catch (DataChannelException exception) when (
                exception.Result == DataChannelResult.Closed && requestedCancellation.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "The requested cancellation closed the pending complete-message operation.",
                    exception, requestedCancellation.Token);
            }
            watch.Stop();
            (int count, long bytes, string receivedHash) = await consumer.ConfigureAwait(false);
            string sourceHash = await producer.ConfigureAwait(false);
            if (count != options.Count || bytes != (long)options.Count * options.Size || sourceHash != receivedHash)
            {
                throw new InvalidOperationException("The completed flow did not deliver the exact finite workload.");
            }
            (int reads, int publishes, int calls) = harness.ServiceCounters();
            return new SampleResult
            {
                Carrier = harness.Source.CarrierKind.ToString(),
                Authorization = options.SessionAuthorization ? "Session" : "Configured",
                Publisher = options.ClientPublishes ? "Client" : "Server",
                PublisherIsTransportInitiator = options.ClientPublishes,
                SourceIsOpener = harness.Source.IsLocalOpener,
                ContextId = harness.ContextId,
                ChannelId = harness.Source.ChannelId,
                Port = harness.Port,
                Messages = count,
                Bytes = bytes,
                SourceSha256 = sourceHash,
                ReceivedSha256 = receivedHash,
                Revision = harness.Source.Revision,
                SecureChannelId = harness.SecureChannelId,
                ServiceSecureChannelId = harness.ServiceSecureChannelId,
                SameServiceTransport = harness.SameServiceTransport,
                Sessions = harness.Sessions,
                ModelEnabled = harness.ModelEnabled,
                Reads = reads,
                Publishes = publishes,
                Calls = calls,
                ReadResponsesDuringStreaming = (harness.Services?.Reads ?? 0) - readBefore,
                PublishResponsesDuringStreaming = (harness.Services?.Publishes ?? 0) - publishBefore,
                NotificationsDuringStreaming = (harness.Services?.Notifications ?? 0) - notificationsBefore,
                RevisedPublishingInterval = harness.Services?.RevisedPublishingInterval ?? 0,
                ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds,
                MegabitsPerSecond = bytes * 8 / 1_000_000.0 / Math.Max(watch.Elapsed.TotalSeconds, double.Epsilon)
            };

            async Task<string> SendAsync()
            {
                using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                for (int index = 0; index < options.Count; index++)
                {
                    if (options.Revise && index == options.Count / 2)
                    {
                        await midpoint.Task.WaitAsync(failure.Token).ConfigureAwait(false);
                        await harness.ReviseAsync(failure.Token).ConfigureAwait(false);
                    }
                    ByteString message = messageFactory(index);
                    if (message.Length != options.Size)
                    {
                        throw new InvalidOperationException(
                            "The source emitted an unexpected complete-message length.");
                    }
                    DataChannelResult result;
                    do
                    {
                        result = await harness.Source.SendAsync(message, cancellationToken: failure.Token)
                            .ConfigureAwait(false);
                        if (result == DataChannelResult.WouldBlock)
                        {
                            await Task.Delay(1, failure.Token).ConfigureAwait(false);
                        }
                    }
                    while (result == DataChannelResult.WouldBlock);
                    StreamingHarness.RequireGood(result);
                    hash.AppendData(message.Span);
                    if (options.IntervalMilliseconds > 0)
                    {
                        await Task.Delay(options.IntervalMilliseconds, failure.Token).ConfigureAwait(false);
                    }
                }
                DataChannelResult ended;
                do
                {
                    ended = await harness.Source.EndAsync(failure.Token).ConfigureAwait(false);
                    if (ended == DataChannelResult.WouldBlock)
                    {
                        await Task.Delay(1, failure.Token).ConfigureAwait(false);
                    }
                }
                while (ended == DataChannelResult.WouldBlock);
                StreamingHarness.RequireGood(ended);
                return Convert.ToHexString(hash.GetHashAndReset());
            }

            async Task<(int Count, long Bytes, string Hash)> ReceiveAsync()
            {
                int count = 0;
                long bytes = 0;
                using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                while (true)
                {
                    using DataChannelMessage? message = await harness.Sink.ReadAsync(failure.Token)
                        .ConfigureAwait(false);
                    if (message == null)
                    {
                        break;
                    }
                    if (count >= options.Count || !message.Payload.Span.SequenceEqual(messageFactory(count).Span))
                    {
                        throw new InvalidOperationException("The receiver observed corrupt, reordered or extra bytes.");
                    }
                    hash.AppendData(message.Payload.Span);
                    bytes += message.Payload.Length;
                    if (receive != null)
                    {
                        await receive(count, message.Payload, failure.Token).ConfigureAwait(false);
                    }
                    count++;
                    if (count == options.Count / 2)
                    {
                        midpoint.TrySetResult();
                    }
                }
                return (count, bytes, Convert.ToHexString(hash.GetHashAndReset()));
            }
        }

        /// <summary>
        /// Creates opaque sample bytes with an independently decodable little-endian counter.
        /// This application fixture is not a payload protocol or media packetization specification.
        /// </summary>
        public static ByteString SyntheticMessage(int sequence, int size)
        {
            byte[] bytes = new byte[size];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, sequence);
            for (int index = sizeof(int); index < bytes.Length; index++)
            {
                bytes[index] = unchecked((byte)(sequence + index));
            }
            return new ByteString(bytes);
        }

        /// <summary>
        /// Decodes the received marker independently from the complete byte identity check.
        /// </summary>
        public static ValueTask DecodeSyntheticAsync(
            int sequence, ByteString payload, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (BinaryPrimitives.ReadInt32LittleEndian(payload.Span) != sequence)
            {
                throw new InvalidOperationException("The received counter is not the expected ordered value.");
            }
            return ValueTask.CompletedTask;
        }
    }
}
