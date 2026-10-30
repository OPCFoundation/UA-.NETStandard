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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// Preserves physical-channel registration and disposal races independently of flow authorization.
    /// </summary>
    [TestFixture]
    [Category("DataChannels")]
    public sealed class UascListenerAttachmentTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task ListenerAttachmentRegistersChannelAndDisposalUnbindsAsync(bool useSocket)
        {
            using TcpListenerChannel channel = CreateListenerChannel();
            Mock<IUaSCByteTransport> transport = CreateIdleBoundTransport();
            channel.TransportDecorator = _ => transport.Object;
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            if (useSocket)
            {
                channel.Attach(1, socket);
            }
            else
            {
                channel.Attach(1, transport.Object);
            }

            Assert.That(UaSCSecureChannelRegistry.TryGet(channel.GlobalChannelId, out UaSCUaBinaryChannel? registered),
                Is.True);
            Assert.That(registered, Is.SameAs(channel));
            transport.As<IUaSCSecureChannelBoundTransport>()
                .Verify(bound => bound.OnSecureChannelAttached(channel.GlobalChannelId), Times.Once);
            Assert.That(channel.DataChannels, Is.Null, "Physical attachment is not authenticated flow establishment.");

            IUaSCByteTransport? detached = await channel.DetachTransportAsync().ConfigureAwait(false);
            Assert.That(detached, Is.SameAs(transport.Object));
            detached?.Close();
            channel.Dispose();
            Assert.That(UaSCSecureChannelRegistry.TryGet(channel.GlobalChannelId, out _), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ListenerDisposedDuringAttachmentIsNotRegistered(bool useSocket)
        {
            using TcpListenerChannel channel = CreateListenerChannel();
            Mock<IUaSCByteTransport> transport = CreateIdleBoundTransport();
            transport.As<IUaSCSecureChannelBoundTransport>()
                .Setup(bound => bound.OnSecureChannelAttached(It.IsAny<string>()))
                .Callback<string>(_ => channel.Dispose());
            channel.TransportDecorator = _ => transport.Object;
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            Assert.That(() =>
            {
                if (useSocket)
                {
                    channel.Attach(1, socket);
                }
                else
                {
                    channel.Attach(1, transport.Object);
                }
            }, Throws.TypeOf<ObjectDisposedException>());

            Assert.That(UaSCSecureChannelRegistry.TryGet(channel.GlobalChannelId, out _), Is.False);
            transport.Verify(value => value.ReceiveChunkAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        private static TcpListenerChannel CreateListenerChannel()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new TcpListenerChannel(
                Guid.NewGuid().ToString(),
                Mock.Of<ITcpChannelListener>(),
                new BufferManager("listener-binding", 8192, telemetry),
                new ChannelQuotas(ServiceMessageContext.CreateEmpty(telemetry)),
                serverCertificates: null!,
                endpoints: [],
                telemetry);
        }

        private static Mock<IUaSCByteTransport> CreateIdleBoundTransport()
        {
            var pending = new TaskCompletionSource<ArraySegment<byte>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = new Mock<IUaSCByteTransport>();
            transport.As<IUaSCSecureChannelBoundTransport>();
            transport.Setup(value => value.ReceiveChunkAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken ct) =>
                    new ValueTask<ArraySegment<byte>>(pending.Task.WaitAsync(ct)));
            return transport;
        }
    }
}
