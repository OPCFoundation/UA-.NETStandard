// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Native.QuicGo
{
    /// <summary>
    /// One-shot cancellation for a native operation whose application error is learned after submission.
    /// Keep this control alive until the operation has drained. Signalling never joins the native operation.
    /// </summary>
    public sealed class NativeQuicCancellation : IDisposable
    {
        /// <summary>
        /// Creates an uncancelled control. Use a separate control for each independently resettable operation.
        /// </summary>
        public NativeQuicCancellation()
        {
            Token = m_source.Token;
        }

        /// <summary>
        /// Gets the cancellation token. Passing it separately to the operation is optional.
        /// </summary>
        public CancellationToken Token { get; }

        /// <summary>
        /// Gets whether an application error has been requested.
        /// </summary>
        public bool IsCancellationRequested => Volatile.Read(ref m_errorCode) >= 0;

        /// <summary>
        /// Gets the first requested application error, or zero before cancellation is requested.
        /// This is the requested code, not a substitute for actual native reset evidence.
        /// </summary>
        public ulong ErrorCode
        {
            get
            {
                long code = Volatile.Read(ref m_errorCode);
                return code < 0 ? 0 : (ulong)code;
            }
        }

        /// <summary>
        /// Publishes an error code before signalling cancellation callbacks. The first call wins;
        /// subsequent calls return the same signalling task without changing the code.
        /// Awaiting this task waits only for cancellation callbacks, never the native send or its body lease.
        /// Native prefix critical sections, racing success and result ownership remain unchanged.
        /// </summary>
        public Task CancelAsync(ulong errorCode)
        {
            if (errorCode >= (1UL << 62))
            {
                throw new ArgumentOutOfRangeException(nameof(errorCode));
            }
            lock (m_gate)
            {
                ThrowIfDisposed();
                if (m_cancellation is not null)
                {
                    return m_cancellation;
                }
                Volatile.Write(ref m_errorCode, (long)errorCode);
                m_cancellation = m_source.CancelAsync();
                return m_cancellation;
            }
        }

        /// <summary>
        /// Releases managed cancellation resources without cancelling an operation or changing its error code.
        /// The caller must first await every operation using this control.
        /// </summary>
        public void Dispose()
        {
            lock (m_gate)
            {
                if (m_disposed == 0)
                {
                    Volatile.Write(ref m_disposed, 1);
                    m_source.Dispose();
                }
            }
        }

        /// <summary>
        /// Prevents attaching a disposed control to a new native operation.
        /// </summary>
        internal void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
        }

        private readonly Lock m_gate = new();
        private readonly CancellationTokenSource m_source = new();
        private Task? m_cancellation;
        private long m_errorCode = -1;
        private int m_disposed;
    }
}
