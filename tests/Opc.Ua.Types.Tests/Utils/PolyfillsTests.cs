/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
        public async Task ConnectAsyncWithNullSocketThrowsArgumentNullException()
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, 1);

            ArgumentNullException exception = (await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await System.Net.Sockets.Polyfills.ConnectAsync(
                    null!,
                    endpoint,
                    CancellationToken.None).ConfigureAwait(false)))!;

            Assert.That(exception.ParamName, Is.EqualTo("socket"));
        }

        [Test]
        public async Task ConnectAsyncWithNullEndpointThrowsArgumentNullException()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            ArgumentNullException exception = (await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await System.Net.Sockets.Polyfills.ConnectAsync(
                    socket,
                    null!,
                    CancellationToken.None).ConfigureAwait(false)))!;

            Assert.That(exception.ParamName, Is.EqualTo("remoteEP"));
        }

        [Test]
        public async Task ConnectAsyncWithPreCancelledTokenThrowsOperationCanceledException()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            using var source = new CancellationTokenSource();
            source.Cancel();

            OperationCanceledException exception = (await Assert.CatchAsync<OperationCanceledException>(
                async () => await System.Net.Sockets.Polyfills.ConnectAsync(
                    socket,
                    new IPEndPoint(IPAddress.Loopback, 1),
                    source.Token).ConfigureAwait(false)))!;

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
        public async Task ConnectAsyncWithRefusedConnectionPreservesSocketException()
        {
            using var reserved = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            reserved.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            await Assert.ThrowsAsync<SocketException>(
                async () => await System.Net.Sockets.Polyfills.ConnectAsync(
                    socket,
                    reserved.LocalEndPoint!,
                    CancellationToken.None).ConfigureAwait(false));
        }
    }
}
#endif
