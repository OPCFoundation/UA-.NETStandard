/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Sockets
{
    /// <summary>
    /// Polyfills for <see cref="Socket"/> methods that are unavailable on legacy target frameworks.
    /// </summary>
    public static class Polyfills
    {
#if NETSTANDARD2_0 || NETFRAMEWORK
        /// <summary>
        /// Asynchronously establishes a connection to a remote endpoint and observes cancellation.
        /// </summary>
        /// <param name="socket">
        /// The socket to connect.
        /// </param>
        /// <param name="remoteEP">
        /// The remote endpoint.
        /// </param>
        /// <param name="cancellationToken">
        /// The token to monitor for cancellation.
        /// </param>
        /// <returns>
        /// A task that represents the asynchronous connection operation.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="socket"/> or <paramref name="remoteEP"/> is <see langword="null"/>.
        /// </exception>
        public static async Task ConnectAsync(
            this Socket socket,
            EndPoint remoteEP,
            CancellationToken cancellationToken)
        {
            if (socket is null)
            {
                throw new ArgumentNullException(nameof(socket));
            }

            if (remoteEP is null)
            {
                throw new ArgumentNullException(nameof(remoteEP));
            }

            cancellationToken.ThrowIfCancellationRequested();
            using CancellationTokenRegistration registration = cancellationToken.Register(
                static state => ((Socket)state).Dispose(),
                socket,
                useSynchronizationContext: false);
            try
            {
                await socket.ConnectAsync(remoteEP).ConfigureAwait(false);
                registration.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "Connection attempt was cancelled.",
                    cancellationToken);
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "Connection attempt was cancelled.",
                    cancellationToken);
            }
        }
#endif
    }
}
