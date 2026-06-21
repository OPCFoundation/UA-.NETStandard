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
using NUnit.Framework;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Tests.Transports
{
    /// <summary>
    /// Unit tests for <see cref="PubSubCaptureRegistry"/> and the capture
    /// seam contract (Part 14 §8.3 diagnostics tap).
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    public sealed class PubSubCaptureRegistryTests
    {
        [Test]
        public void CurrentObserverIsNullByDefault()
        {
            var registry = new PubSubCaptureRegistry();
            Assert.That(registry.CurrentObserver, Is.Null);
        }

        [Test]
        public void SetObserverPublishesObserver()
        {
            var registry = new PubSubCaptureRegistry();
            var observer = new RecordingObserver();

            registry.SetObserver(observer);

            Assert.That(registry.CurrentObserver, Is.SameAs(observer));
        }

        [Test]
        public void SetObserverReplacesPrevious()
        {
            var registry = new PubSubCaptureRegistry();
            var first = new RecordingObserver();
            var second = new RecordingObserver();

            registry.SetObserver(first);
            registry.SetObserver(second);

            Assert.That(registry.CurrentObserver, Is.SameAs(second));
        }

        [Test]
        public void SetObserverNullThrows()
        {
            var registry = new PubSubCaptureRegistry();
            Assert.That(
                () => registry.SetObserver(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void TryClearObserverClearsMatchingObserver()
        {
            var registry = new PubSubCaptureRegistry();
            var observer = new RecordingObserver();
            registry.SetObserver(observer);

            bool cleared = registry.TryClearObserver(observer);

            Assert.Multiple(() =>
            {
                Assert.That(cleared, Is.True);
                Assert.That(registry.CurrentObserver, Is.Null);
            });
        }

        [Test]
        public void TryClearObserverIgnoresNonMatchingObserver()
        {
            var registry = new PubSubCaptureRegistry();
            var active = new RecordingObserver();
            var other = new RecordingObserver();
            registry.SetObserver(active);

            bool cleared = registry.TryClearObserver(other);

            Assert.Multiple(() =>
            {
                Assert.That(cleared, Is.False);
                Assert.That(registry.CurrentObserver, Is.SameAs(active));
            });
        }

        [Test]
        public void ObserverReceivesContextAndPayload()
        {
            var observer = new RecordingObserver();
            var timestamp = new DateTimeUtc(
                new DateTime(2026, 6, 21, 8, 0, 0, DateTimeKind.Utc));
            var context = new PubSubCaptureContext(
                PubSubCaptureDirection.Outbound,
                "urn:test:transport",
                timestamp,
                endpoint: "239.0.0.1:4840",
                topic: null);
            byte[] payload = [0x01, 0x02, 0x03];

            observer.OnFrameCaptured(in context, payload);

            Assert.That(observer.Captured, Has.Count.EqualTo(1));
            (PubSubCaptureContext ctx, byte[] bytes) = observer.Captured[0];
            Assert.Multiple(() =>
            {
                Assert.That(ctx.Direction, Is.EqualTo(PubSubCaptureDirection.Outbound));
                Assert.That(ctx.TransportProfileUri, Is.EqualTo("urn:test:transport"));
                Assert.That(ctx.Endpoint, Is.EqualTo("239.0.0.1:4840"));
                Assert.That(ctx.Topic, Is.Null);
                Assert.That(ctx.Timestamp, Is.EqualTo(timestamp));
                Assert.That(bytes, Is.EqualTo(payload));
            });
        }

        private sealed class RecordingObserver : IPubSubCaptureObserver
        {
            public List<(PubSubCaptureContext Context, byte[] Payload)> Captured { get; } = [];

            public void OnFrameCaptured(in PubSubCaptureContext context, ReadOnlySpan<byte> payload)
            {
                Captured.Add((context, payload.ToArray()));
            }
        }
    }
}
