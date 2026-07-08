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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Bindings;

namespace Opc.Ua.Pcap.Tests.Bindings
{
    [TestFixture]
    public sealed class CapturingByteTransportFactoryTests
    {
        [Test]
        public void ConstructorRejectsNulls()
        {
            var registry = new ChannelCaptureRegistry();
            Assert.That(
                () => new CapturingByteTransportFactory(null!, registry),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => new CapturingByteTransportFactory(new RecordingFactory(), null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void CreateWrapsInnerTransportInCapturingDecorator()
        {
            var registry = new ChannelCaptureRegistry();
            var inner = new RecordingFactory();
            var factory = new CapturingByteTransportFactory(inner, registry);
            ITelemetryContext telemetry = Ua.Tests.NUnitTelemetryContext.Create();
            var buffers = new BufferManager("test", 8192, telemetry);

            IUaSCByteTransport transport = factory.Create(buffers, 8192, telemetry);
            try
            {
                Assert.That(transport, Is.InstanceOf<CapturingByteTransport>());
                Assert.That(inner.CreateCount, Is.EqualTo(1));
            }
            finally
            {
                (transport as IDisposable)?.Dispose();
            }
        }

        [Test]
        public void ImplementationStringIncludesPcapSuffix()
        {
            var factory = new CapturingByteTransportFactory(
                new RecordingFactory(),
                new ChannelCaptureRegistry());
            Assert.That(factory.Implementation, Is.EqualTo("UA-TEST+pcap"));
        }

        private sealed class RecordingFactory : IUaSCByteTransportFactory
        {
            public int CreateCount { get; private set; }

            public string Implementation => "UA-TEST";

            public IUaSCByteTransport Create(
                BufferManager bufferManager,
                int receiveBufferSize,
                ITelemetryContext telemetry)
            {
                CreateCount++;
                return new NoopTransport();
            }
        }

        private sealed class NoopTransport : IUaSCByteTransport, IDisposable
        {
            public string Implementation => "UA-NOOP";
            public TransportChannelFeatures Features => TransportChannelFeatures.None;
            public EndPoint? LocalEndpoint => null;
            public EndPoint? RemoteEndpoint => null;
            public ValueTask ConnectAsync(Uri url, CancellationToken ct) => default;
            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct) => default;
            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct) => default;

            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
                => new(new ArraySegment<byte>([]));

            public void Close()
            {
            }
            public void Dispose()
            {
            }
        }
    }
}
