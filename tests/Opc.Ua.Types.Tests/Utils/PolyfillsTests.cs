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

#if NETFRAMEWORK
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Utils
{
    [TestFixture]
    public class PolyfillsTests
    {
        [Test]
        public void ConnectAsyncWithNullSocketThrowsArgumentNullException()
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, 1);

            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await System.Net.Sockets.Polyfills.ConnectAsync(
                    null!,
                    endpoint,
                    CancellationToken.None).ConfigureAwait(false))!;

            Assert.That(exception.ParamName, Is.EqualTo("socket"));
        }

        [Test]
        public void ConnectAsyncWithNullEndpointThrowsArgumentNullException()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await System.Net.Sockets.Polyfills.ConnectAsync(
                    socket,
                    null!,
                    CancellationToken.None).ConfigureAwait(false))!;

            Assert.That(exception.ParamName, Is.EqualTo("remoteEP"));
        }

        [Test]
        public void ConnectAsyncWithPreCancelledTokenThrowsOperationCanceledException()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            using var source = new CancellationTokenSource();
            source.Cancel();

            OperationCanceledException exception = Assert.CatchAsync<OperationCanceledException>(
                async () => await System.Net.Sockets.Polyfills.ConnectAsync(
                    socket,
                    new IPEndPoint(IPAddress.Loopback, 1),
                    source.Token).ConfigureAwait(false))!;

            Assert.That(exception.CancellationToken, Is.EqualTo(source.Token));
        }

        [Test]
        public async Task ConnectAsyncWithLoopbackListenerEstablishesConnectionAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                var endpoint = (IPEndPoint)listener.LocalEndpoint;

                await System.Net.Sockets.Polyfills.ConnectAsync(socket, endpoint, CancellationToken.None)
                    .ConfigureAwait(false);
                using Socket accepted = await listener.AcceptSocketAsync().ConfigureAwait(false);

                Assert.That(socket.Connected, Is.True);
                Assert.That(socket.RemoteEndPoint, Is.EqualTo(endpoint));
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void ConnectAsyncWithRefusedConnectionPreservesSocketException()
        {
            using var reserved = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            reserved.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            Assert.ThrowsAsync<SocketException>(
                async () => await System.Net.Sockets.Polyfills.ConnectAsync(
                    socket,
                    reserved.LocalEndPoint!,
                    CancellationToken.None).ConfigureAwait(false));
        }
    }
}
#endif
