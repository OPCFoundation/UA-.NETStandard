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
#if NETSTANDARD2_0 || NETSTANDARD2_1 || NETFRAMEWORK
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
