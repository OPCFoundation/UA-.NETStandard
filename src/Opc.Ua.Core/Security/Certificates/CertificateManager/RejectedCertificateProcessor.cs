/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Processes rejected certificate chains asynchronously via a
    /// bounded channel, writing them to the rejected certificate store.
    /// </summary>
    internal sealed class RejectedCertificateProcessor : IAsyncDisposable
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="RejectedCertificateProcessor"/> class.
        /// </summary>
        public RejectedCertificateProcessor(
            ICertificateTrustListManager trustListManager,
            int maxRejectedCertificates,
            ITelemetryContext telemetry)
        {
            m_trustListManager = trustListManager;
            m_maxRejectedCertificates = maxRejectedCertificates;
            m_logger = telemetry.CreateLogger<RejectedCertificateProcessor>();
            m_channel = Channel.CreateBounded<WriteRequest>(
                new BoundedChannelOptions(100)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true
                },
                itemDropped: dropped =>
                {
                    // The dropped chain is owned by us — dispose it and
                    // signal completion so any awaiter is unblocked.
                    try
                    {
                        dropped.Chain?.Dispose();
                    }
                    finally
                    {
                        dropped.Completion.TrySetResult(false);
                    }
                });
            m_processingTask = ProcessAsync();
        }

        /// <summary>
        /// Updates the maximum rejected-certificate cap. Subsequent writes
        /// honour the new value.
        /// </summary>
        public void SetMaxRejectedCertificates(int maxRejectedCertificates)
        {
            Volatile.Write(ref m_maxRejectedCertificates, maxRejectedCertificates);
        }

        /// <summary>
        /// Enqueues a rejected certificate chain for background processing.
        /// </summary>
        public ValueTask EnqueueAsync(
            CertificateCollection chain,
            CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            // Track the most recently enqueued request so WaitForDrainAsync
            // can return when *that* request has been processed. Because the
            // channel is processed in order, this also implies all earlier
            // requests have completed.
            Interlocked.Exchange(ref m_drainTcs, tcs);
            return WriteRequestAsync(new WriteRequest(chain.AddRef(), tcs), ct);
        }

        /// <summary>
        /// Enqueues a trim-only signal that re-applies the current
        /// <c>MaxRejectedCertificates</c> cap to the existing rejected
        /// store contents. Use this to actively shrink the store after
        /// the cap has been lowered.
        /// </summary>
        public ValueTask EnqueueTrimAsync(CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Exchange(ref m_drainTcs, tcs);
            return WriteRequestAsync(new WriteRequest(Chain: null, tcs), ct);
        }

        private ValueTask WriteRequestAsync(WriteRequest request, CancellationToken ct)
        {
            return m_channel.Writer.TryWrite(request)
                ? default
                : WriteRequestSlowAsync(request, ct);
        }

        /// <summary>
        /// Fallback for a refused <c>TryWrite</c>. The channel is bounded with
        /// <see cref="BoundedChannelFullMode.DropOldest"/>, so a refused
        /// TryWrite means the writer has been completed by
        /// <see cref="DisposeAsync"/> and the awaited write below fails rather
        /// than blocks. The request owns its chain (the per-cert AddRef taken
        /// on enqueue): once the write cannot commit the request to the
        /// channel, nothing else references it, so the chain must be disposed
        /// here — an abandoned request keeps one leaked reference per
        /// certificate for the life of the process (this was the intermittent
        /// one-certificate leak reported by the test assemblies' teardown leak
        /// detector when a late validation raced manager disposal). The
        /// completion is resolved as "not processed" so a WaitForDrainAsync
        /// caller is not left waiting on the abandoned request. An enqueue
        /// after shutdown is a benign teardown race and the store write is
        /// best-effort, so <see cref="ChannelClosedException"/> is swallowed;
        /// cancellation still propagates.
        /// </summary>
        private async ValueTask WriteRequestSlowAsync(
            WriteRequest request,
            CancellationToken ct)
        {
            try
            {
                await m_channel.Writer.WriteAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try
                {
                    request.Chain?.Dispose();
                }
                finally
                {
                    request.Completion.TrySetResult(false);
                }
                if (ex is not ChannelClosedException)
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Returns a task that completes when the most recently enqueued
        /// chain has been processed (or immediately if the queue is idle).
        /// </summary>
        public Task WaitForDrainAsync()
        {
            return Volatile.Read(ref m_drainTcs).Task;
        }

        /// <summary>
        /// Completes the channel and waits for all queued items to be
        /// processed.
        /// </summary>
        public async Task DrainAsync(CancellationToken ct = default)
        {
            m_channel.Writer.Complete();
            await m_processingTask.WaitAsync(ct).ConfigureAwait(false);
        }

        private async Task ProcessAsync()
        {
            await foreach (WriteRequest request in m_channel.Reader.ReadAllAsync()
                .ConfigureAwait(false))
            {
                bool ok = false;
                try
                {
                    if (!m_trustListManager.TrustLists
                        .Contains(TrustListIdentifier.Rejected))
                    {
                        continue;
                    }

                    using ICertificateStore store = m_trustListManager
                        .OpenTrustedStore(TrustListIdentifier.Rejected);
                    int max = Volatile.Read(ref m_maxRejectedCertificates);
                    if (request.Chain == null)
                    {
                        // Trim-only: pass an empty collection so the store
                        // re-applies the cap to existing entries without
                        // adding any new ones.
                        using var empty = new CertificateCollection();
                        await store.AddRejectedAsync(empty, max)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await store.AddRejectedAsync(request.Chain, max)
                            .ConfigureAwait(false);
                    }

                    ok = true;
                }
                catch (Exception ex)
                {
                    m_logger.RejectedCertificateProcessorLogMessage0(ex);
                }
                finally
                {
                    // Dispose the chain we own so the per-cert AddRef from
                    // CertificateCollection.Add is balanced. Without this,
                    // certs added to a chain enqueued here would leak.
                    request.Chain?.Dispose();
                    request.Completion.TrySetResult(ok);
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_channel.Writer.TryComplete();
            await m_processingTask.ConfigureAwait(false);

            // Defensive drain: if the processing task exited early (e.g. due
            // to an unhandled exception), dispose any remaining chains that
            // we own.
            while (m_channel.Reader.TryRead(out WriteRequest leftover))
            {
                leftover.Chain?.Dispose();
                leftover.Completion.TrySetResult(false);
            }
            GC.SuppressFinalize(this);
        }

        private static TaskCompletionSource<bool> CreateCompletedTcs()
        {
            var tcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            tcs.SetResult(true);
            return tcs;
        }

        private readonly record struct WriteRequest(
            CertificateCollection? Chain,
            TaskCompletionSource<bool> Completion);

        private readonly Channel<WriteRequest> m_channel;
        private readonly Task m_processingTask;
        private readonly ICertificateTrustListManager m_trustListManager;
        private int m_maxRejectedCertificates;
        private TaskCompletionSource<bool> m_drainTcs = CreateCompletedTcs();
        private readonly ILogger m_logger;
    }

    /// <summary>
    /// Source-generated log messages for RejectedCertificateProcessor.
    /// </summary>
    internal static partial class RejectedCertificateProcessorLog
    {
        [LoggerMessage(EventId = CoreEventIds.RejectedCertificateProcessor + 0, Level = LogLevel.Debug,
            Message = "Could not write rejected certificate to store.")]
        public static partial void RejectedCertificateProcessorLogMessage0(
            this ILogger logger,
            global::System.Exception? exception);
    }

}
