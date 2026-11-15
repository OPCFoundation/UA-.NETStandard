/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * Licensed under the OPC Foundation MIT License 1.00.
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Holds one test-owned contributor getter while another thread changes its ownership.
        /// </summary>
        private sealed class OneShotConformanceReadGate : IDisposable
        {
            /// <summary>
            /// Gets the signal raised after the armed getter has entered.
            /// </summary>
            public Task Entered => m_entered.Task;

            /// <summary>
            /// Gets the signal raised when the gated getter leaves the gate.
            /// The caller must also join its aggregate operation before disposing this gate.
            /// </summary>
            public Task Exited => m_exited.Task;

            /// <summary>
            /// Gets the aggregate worker whose completion protects the gate's storage.
            /// </summary>
            public Task Aggregate => m_aggregate ??
                throw new InvalidOperationException("The read gate has no aggregate worker.");

            /// <summary>
            /// Gets whether the gated getter is currently waiting for release.
            /// </summary>
            public bool IsReadPending => Volatile.Read(ref m_pending) != 0;

            /// <summary>
            /// Arms exactly one subsequent getter invocation.
            /// </summary>
            public void Arm()
            {
                if (Interlocked.CompareExchange(ref m_armed, 1, 0) != 0)
                {
                    throw new InvalidOperationException("The read gate can only be armed once.");
                }
            }

            /// <summary>
            /// Observes a getter synchronously, blocking only the one armed invocation.
            /// Invoke the aggregate on a dedicated test worker, not the test continuation.
            /// </summary>
            public void ObserveRead()
            {
                if (Interlocked.CompareExchange(ref m_armed, 2, 1) != 1)
                {
                    return;
                }

                Volatile.Write(ref m_pending, 1);
                m_entered.TrySetResult(true);
                try
                {
                    if (!m_release.Wait(TimeSpan.FromMinutes(2)))
                    {
                        throw new TimeoutException("The test did not release its conformance getter.");
                    }
                }
                finally
                {
                    Volatile.Write(ref m_pending, 0);
                    m_exited.TrySetResult(true);
                }
            }

            /// <summary>
            /// Releases the getter, including when a test assertion fails.
            /// </summary>
            public void Release()
            {
                m_release.Set();
            }

            /// <summary>
            /// Associates the gate with the aggregate that can enter its getter.
            /// </summary>
            public void TrackAggregate(Task aggregate)
            {
                if (Interlocked.CompareExchange(
                    ref m_aggregate,
                    aggregate ?? throw new ArgumentNullException(nameof(aggregate)),
                    null) is not null)
                {
                    throw new InvalidOperationException("The read gate already has an aggregate worker.");
                }
            }

            /// <summary>
            /// Releases gate storage only after the aggregate worker and gated getter have exited.
            /// </summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return;
                }
                // Disarm a getter that never entered; otherwise join the getter that claimed the gate.
                int previous = Interlocked.CompareExchange(ref m_armed, 2, 1);
                if (previous == 2 && !m_exited.Task.IsCompleted)
                {
                    _ = m_exited.Task.ContinueWith(
                        static (_, state) => ((OneShotConformanceReadGate)state!).DisposeAfterAggregate(),
                        this,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    return;
                }
                DisposeAfterAggregate();
            }

            /// <summary>
            /// Releases storage after any tracked aggregate has finished using the gate.
            /// </summary>
            private void DisposeAfterAggregate()
            {
                if (m_aggregate is { IsCompleted: false } aggregate)
                {
                    _ = aggregate.ContinueWith(
                        static (_, state) => ((ManualResetEventSlim)state!).Dispose(),
                        m_release,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    return;
                }
                m_release.Dispose();
            }

            /// <summary>The entered-getter signal.</summary>
            private readonly TaskCompletionSource<bool> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>The exiting-getter signal.</summary>
            private readonly TaskCompletionSource<bool> m_exited =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>The synchronous getter release signal.</summary>
            private readonly ManualResetEventSlim m_release = new();

            /// <summary>The aggregate that owns the gated getter.</summary>
            private Task? m_aggregate;

            /// <summary>The unarmed, armed, or consumed state.</summary>
            private int m_armed;

            /// <summary>Whether gate disposal has been requested.</summary>
            private int m_disposed;

            /// <summary>Whether the gated read is pending.</summary>
            private int m_pending;
        }
    }
}
