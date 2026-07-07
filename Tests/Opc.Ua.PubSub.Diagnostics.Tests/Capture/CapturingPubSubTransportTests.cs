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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Pcap.Tests
{
    /// <summary>
    /// Tests for the capturing transport decorator: it taps outbound /
    /// inbound frames to the registered observer and delegates everything
    /// else to the wrapped transport.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    public sealed class CapturingPubSubTransportTests
    {
        [Test]
        public async Task SendTapsOutboundPayloadAndDelegatesAsync()
        {
            var inner = new FakeTransport();
            var registry = new PubSubCaptureRegistry();
            var observer = new RecordingObserver();
            registry.SetObserver(observer);
            await using var decorator = new CapturingPubSubTransport(inner, registry);

            byte[] payload = [0xB1, 0x01, 0x02, 0x03];
            await decorator.SendAsync(payload, topic: "t/1");

            Assert.Multiple(() =>
            {
                Assert.That(inner.Sent, Has.Count.EqualTo(1));
                Assert.That(observer.Captured, Has.Count.EqualTo(1));
                Assert.That(observer.Captured[0].Direction, Is.EqualTo(PubSubCaptureDirection.Outbound));
                Assert.That(observer.Captured[0].Payload, Is.EqualTo(payload));
                Assert.That(observer.Captured[0].Topic, Is.EqualTo("t/1"));
            });
        }

        [Test]
        public async Task ReceiveTapsInboundFramesAndDelegatesAsync()
        {
            var inner = new FakeTransport();
            inner.Inbound.Add(new PubSubTransportFrame(
                new byte[] { 0xAA, 0xBB }, "topic", new DateTimeUtc(DateTime.UtcNow)));
            var registry = new PubSubCaptureRegistry();
            var observer = new RecordingObserver();
            registry.SetObserver(observer);
            await using var decorator = new CapturingPubSubTransport(inner, registry);

            var received = new List<PubSubTransportFrame>();
            await foreach (PubSubTransportFrame frame in decorator.ReceiveAsync(CancellationToken.None))
            {
                received.Add(frame);
            }

            Assert.Multiple(() =>
            {
                Assert.That(received, Has.Count.EqualTo(1));
                Assert.That(observer.Captured, Has.Count.EqualTo(1));
                Assert.That(observer.Captured[0].Direction, Is.EqualTo(PubSubCaptureDirection.Inbound));
                Assert.That(observer.Captured[0].Payload, Is.EqualTo(new byte[] { 0xAA, 0xBB }));
            });
        }

        [Test]
        public async Task NoObserverMeansNoCaptureButStillDelegatesAsync()
        {
            var inner = new FakeTransport();
            var registry = new PubSubCaptureRegistry();
            await using var decorator = new CapturingPubSubTransport(inner, registry);

            await decorator.SendAsync(new byte[] { 1 });

            Assert.That(inner.Sent, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task OpenCloseAndPropertiesDelegateAsync()
        {
            var inner = new FakeTransport();
            var registry = new PubSubCaptureRegistry();
            await using var decorator = new CapturingPubSubTransport(inner, registry);

            await decorator.OpenAsync();
            await decorator.CloseAsync();

            Assert.Multiple(() =>
            {
                Assert.That(inner.Opened, Is.True);
                Assert.That(inner.Closed, Is.True);
                Assert.That(decorator.TransportProfileUri, Is.EqualTo(inner.TransportProfileUri));
                Assert.That(decorator.Direction, Is.EqualTo(inner.Direction));
            });
        }

        [Test]
        public void FactoryWrapsCreatedTransport()
        {
            var innerFactory = new FakeFactory();
            var registry = new PubSubCaptureRegistry();
            var factory = new CapturingPubSubTransportFactory(innerFactory, registry);

            IPubSubTransport transport = factory.Create(
                new PubSubConnectionDataType(),
                TestTelemetryContext.Instance,
                TimeProvider.System);

            Assert.Multiple(() =>
            {
                Assert.That(factory.TransportProfileUri, Is.EqualTo(innerFactory.TransportProfileUri));
                Assert.That(transport, Is.TypeOf<CapturingPubSubTransport>());
            });
        }

        private sealed class RecordingObserver : IPubSubCaptureObserver
        {
            public List<(PubSubCaptureDirection Direction, byte[] Payload, string? Topic)> Captured { get; } = [];

            public void OnFrameCaptured(in PubSubCaptureContext context, ReadOnlySpan<byte> payload)
            {
                Captured.Add((context.Direction, payload.ToArray(), context.Topic));
            }
        }

        private sealed class FakeTransport : IPubSubTransport
        {
            public List<byte[]> Sent { get; } = [];
            public List<PubSubTransportFrame> Inbound { get; } = [];
            public bool Opened { get; private set; }
            public bool Closed { get; private set; }

            public string TransportProfileUri => "urn:test:transport";
            public PubSubTransportDirection Direction => PubSubTransportDirection.SendReceive;
            public bool IsConnected => Opened && !Closed;
#pragma warning disable CS0067
            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged;
#pragma warning restore CS0067

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                Opened = true;
                return ValueTask.CompletedTask;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                Closed = true;
                return ValueTask.CompletedTask;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                Sent.Add(payload.ToArray());
                return ValueTask.CompletedTask;
            }

            public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                foreach (PubSubTransportFrame frame in Inbound)
                {
                    yield return frame;
                }
                await Task.CompletedTask.ConfigureAwait(false);
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }

        private sealed class FakeFactory : IPubSubTransportFactory
        {
            public string TransportProfileUri => "urn:test:transport";

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                return new FakeTransport();
            }
        }

        private sealed class TestTelemetryContext : TelemetryContextBase
        {
            private TestTelemetryContext()
                : base(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            {
            }

            public static TestTelemetryContext Instance { get; } = new();
        }
    }
}
