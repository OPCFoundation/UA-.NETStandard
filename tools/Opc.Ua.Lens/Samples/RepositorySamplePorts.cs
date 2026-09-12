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
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace UaLens.Samples
{
    internal interface IRepositorySamplePortLease : IAsyncDisposable
    {
        int Port { get; }

        ValueTask ReleaseSocketForLaunchAsync();
    }

    internal interface IRepositorySamplePortAllocator
    {
        ValueTask<IRepositorySamplePortLease> ReserveAsync(CancellationToken cancellationToken);
    }

    internal interface IRepositorySamplePortBinding : IAsyncDisposable
    {
        int Port { get; }
    }

    internal interface IRepositorySamplePortBinder
    {
        ValueTask<IRepositorySamplePortBinding> BindAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Holds an OS socket through preparation and a process-local reservation through
    /// cleanup. The samples cannot inherit listening sockets: the release/start gap
    /// is unavoidable, and readiness therefore also requires the owned certificate.
    /// </summary>
    internal sealed class RepositorySamplePortAllocator : IRepositorySamplePortAllocator
    {
        public RepositorySamplePortAllocator()
            : this(new RepositorySampleSocketBinder())
        {
        }

        public RepositorySamplePortAllocator(IRepositorySamplePortBinder binder)
        {
            m_binder = binder ?? throw new ArgumentNullException(nameof(binder));
        }

        public async ValueTask<IRepositorySamplePortLease> ReserveAsync(CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < MaximumAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IRepositorySamplePortBinding binding;
                try
                {
                    binding = await m_binder.BindAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (SocketException exception) when (
                    exception.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
                {
                    if (attempt == MaximumAttempts - 1)
                    {
                        throw new RepositorySampleException(
                            RepositorySampleFailure.ResourceUnavailable,
                            "No available sample port could be reserved within the attempt limit.",
                            exception);
                    }
                    continue;
                }
                bool transferred = false;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (binding.Port is < 1024 or > 65535)
                    {
                        throw new RepositorySampleException(
                            RepositorySampleFailure.ResourceUnavailable, "The sample port binding is invalid.");
                    }
                    if (s_reserved.TryAdd(binding.Port, 0))
                    {
                        transferred = true;
                        return new Lease(binding);
                    }
                }
                finally
                {
                    if (!transferred)
                    {
                        await binding.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            throw new RepositorySampleException(
                RepositorySampleFailure.ResourceUnavailable, "Candidate sample ports are already reserved by UaLens.");
        }

        internal const int MaximumAttempts = 16;
        private static readonly ConcurrentDictionary<int, byte> s_reserved = new();
        private readonly IRepositorySamplePortBinder m_binder;

        private sealed class Lease : IRepositorySamplePortLease
        {
            public Lease(IRepositorySamplePortBinding binding)
            {
                m_binding = binding;
                Port = binding.Port;
            }

            public int Port { get; }

            public async ValueTask ReleaseSocketForLaunchAsync()
            {
                IRepositorySamplePortBinding? binding = Interlocked.Exchange(ref m_binding, null);
                if (binding is not null)
                {
                    await binding.DisposeAsync().ConfigureAwait(false);
                }
            }

            public async ValueTask DisposeAsync()
            {
                await ReleaseSocketForLaunchAsync().ConfigureAwait(false);
                if (Interlocked.Exchange(ref m_released, 1) == 0)
                {
                    s_reserved.TryRemove(Port, out _);
                }
            }

            private IRepositorySamplePortBinding? m_binding;
            private int m_released;
        }
    }

    internal sealed class RepositorySampleSocketBinder : IRepositorySamplePortBinder
    {
        public ValueTask<IRepositorySamplePortBinding> BindAsync(CancellationToken cancellationToken)
        {
            return new ValueTask<IRepositorySamplePortBinding>(Bind(cancellationToken));
        }

        private static Binding Bind(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Socket? socket = null;
            try
            {
                socket = new Socket(
                    Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork,
                    SocketType.Stream,
                    ProtocolType.Tcp)
                {
                    ExclusiveAddressUse = true
                };
                if (Socket.OSSupportsIPv6)
                {
                    socket.DualMode = true;
                }
                socket.Bind(new IPEndPoint(Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
                int port = (socket.LocalEndPoint as IPEndPoint)?.Port ??
                    throw new IOException("The sample socket did not return an IP endpoint.");
                cancellationToken.ThrowIfCancellationRequested();
                var binding = new Binding(socket, port);
                socket = null;
                return binding;
            }
            finally
            {
                socket?.Dispose();
            }
        }

        private sealed class Binding : IRepositorySamplePortBinding
        {
            public Binding(Socket socket, int port)
            {
                m_socket = socket;
                Port = port;
            }

            public int Port { get; }

            public ValueTask DisposeAsync()
            {
                Interlocked.Exchange(ref m_socket, null)?.Dispose();
                return ValueTask.CompletedTask;
            }

            private Socket? m_socket;
        }
    }
}
