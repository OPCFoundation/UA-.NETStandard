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
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Reserves physical UASC connections independently of channel registration and handoff.
    /// The configured limiter remains owned by the host.
    /// </summary>
    internal sealed class UaScConnectionAdmission
    {
        public UaScConnectionAdmission(int maxChannelCount, IConnectionRateLimiter? limiter)
        {
            m_maxChannelCount = maxChannelCount;
            m_limiter = limiter;
        }

        public bool TryAcquire(
            EndPoint? remoteEndpoint,
            [NotNullWhen(true)] out Lease? lease)
        {
            lease = null;
            lock (m_lock)
            {
                if (m_stopped)
                {
                    return false;
                }
            }
            if (m_limiter != null && !m_limiter.TryAdmitConnection(remoteEndpoint, out _))
            {
                return false;
            }

            lock (m_lock)
            {
                if (m_stopped || (m_maxChannelCount > 0 && m_leases.Count >= m_maxChannelCount))
                {
                    return false;
                }
                lease = new Lease(this);
                m_leases.Add(lease);
                return true;
            }
        }

        public void Stop()
        {
            Lease[] leases;
            lock (m_lock)
            {
                m_stopped = true;
                leases = [.. m_leases];
            }

            List<Exception>? errors = null;
            foreach (Lease lease in leases)
            {
                try
                {
                    lease.Dispose();
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }
            }
            if (errors != null)
            {
                throw new AggregateException("Failed to close admitted UASC connections.", errors);
            }
        }

        public void Start()
        {
            lock (m_lock)
            {
                m_stopped = false;
            }
        }

        private void Release(Lease lease)
        {
            lock (m_lock)
            {
                m_leases.Remove(lease);
            }
        }

        /// <summary>
        /// Follows a transport through reverse-connect handoff. Only physical close releases capacity.
        /// </summary>
        internal sealed class Lease : IUaSCByteTransport, IUaSCByteTransportLimits, IDisposable
        {
            internal Lease(UaScConnectionAdmission owner)
            {
                m_owner = owner;
            }

            public EndPoint? LocalEndpoint => Transport.LocalEndpoint;
            public EndPoint? RemoteEndpoint => Transport.RemoteEndpoint;
            public TransportChannelFeatures Features => Transport.Features;
            public string Implementation => Transport.Implementation;

            private IUaSCByteTransport Transport => m_transport ??
                throw new InvalidOperationException("The admission lease has no transport.");

            public void SetAbortAction(Action abort)
            {
                if (abort == null)
                {
                    throw new ArgumentNullException(nameof(abort));
                }
                lock (m_lock)
                {
                    if (!m_closed)
                    {
                        m_abort = abort;
                        return;
                    }
                }
                abort();
            }

            public void Attach(IUaSCByteTransport transport)
            {
                if (transport == null)
                {
                    throw new ArgumentNullException(nameof(transport));
                }
                lock (m_lock)
                {
                    if (!m_closed)
                    {
                        if (m_transport != null)
                        {
                            throw new InvalidOperationException("The admission lease already has a transport.");
                        }
                        m_transport = transport;
                        return;
                    }
                }
                transport.Close();
                throw new ObjectDisposedException(nameof(Lease));
            }

            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                return Transport.ConnectAsync(url, ct);
            }

            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                return Transport.SendChunkAsync(chunk, ct);
            }

            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                return Transport.SendChunkAsync(buffers, ct);
            }

            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                return Transport.ReceiveChunkAsync(ct);
            }

            public async Task WaitForCloseAsync(CancellationToken ct)
            {
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (ct.Register(
                    static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancelled))
                {
                    await Task.WhenAny(m_completion.Task, cancelled.Task).ConfigureAwait(false);
                }
            }

            public void Close()
            {
                IUaSCByteTransport? transport;
                Action? abort;
                lock (m_lock)
                {
                    if (m_closed)
                    {
                        return;
                    }
                    m_closed = true;
                    transport = m_transport;
                    abort = m_abort;
                }
                try
                {
                    // Before attachment the lease aborts; afterward the transport owns physical close.
                    if (transport == null)
                    {
                        abort?.Invoke();
                    }
                    else
                    {
                        try
                        {
                            transport.Close();
                        }
                        catch
                        {
                            abort?.Invoke();
                            throw;
                        }
                    }
                }
                finally
                {
                    m_owner.Release(this);
                    m_completion.TrySetResult(true);
                }
            }

            public void Dispose()
            {
                Close();
            }

            void IUaSCByteTransportLimits.SetReceiveBufferSize(int receiveBufferSize)
            {
                if (Transport is IUaSCByteTransportLimits limits)
                {
                    limits.SetReceiveBufferSize(receiveBufferSize);
                }
            }

            private readonly UaScConnectionAdmission m_owner;
            private readonly Lock m_lock = new();
            private readonly TaskCompletionSource<bool> m_completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private IUaSCByteTransport? m_transport;
            private Action? m_abort;
            private bool m_closed;
        }

        private readonly Lock m_lock = new();
        private readonly HashSet<Lease> m_leases = [];
        private readonly int m_maxChannelCount;
        private readonly IConnectionRateLimiter? m_limiter;
        private bool m_stopped;
    }
}
