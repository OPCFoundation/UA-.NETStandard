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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Pcap.Tests
{
    /// <summary>
    /// Integration tests for the in-process PubSub capture pipeline: frames
    /// pushed through the <see cref="IPubSubCaptureRegistry"/> seam are
    /// buffered by <see cref="InProcessPubSubCaptureSource"/> and replayed.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    public sealed class InProcessPubSubCaptureSourceTests
    {
        [Test]
        public async Task CapturedFrameIsReplayedAsync()
        {
            var registry = new PubSubCaptureRegistry();
            await using var source = new InProcessPubSubCaptureSource(registry);
            await source.StartAsync().ConfigureAwait(false);

            Assert.That(registry.CurrentObserver, Is.SameAs(source));

            byte[] payload = [0xB1, 0x00, 0xDE, 0xAD, 0xBE, 0xEF];
            EmitFrame(registry, PubSubCaptureDirection.Outbound, payload, "239.0.0.1:4840");

            await source.StopAsync().ConfigureAwait(false);

            List<PubSubCaptureFrame> frames = await ReadAllAsync(source).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(frames, Has.Count.EqualTo(1));
                Assert.That(frames[0].Data.ToArray(), Is.EqualTo(payload));
                Assert.That(frames[0].Direction, Is.EqualTo(PubSubCaptureDirection.Outbound));
                Assert.That(frames[0].Endpoint, Is.EqualTo("239.0.0.1:4840"));
                Assert.That(source.FrameCount, Is.EqualTo(1));
                Assert.That(source.ByteCount, Is.EqualTo(payload.Length));
            });
        }

        [Test]
        public async Task StopRemovesObserverFromRegistryAsync()
        {
            var registry = new PubSubCaptureRegistry();
            await using var source = new InProcessPubSubCaptureSource(registry);
            await source.StartAsync().ConfigureAwait(false);
            await source.StopAsync().ConfigureAwait(false);

            Assert.That(registry.CurrentObserver, Is.Null);
        }

        [Test]
        public async Task FramesAfterStopAreIgnoredAsync()
        {
            var registry = new PubSubCaptureRegistry();
            await using var source = new InProcessPubSubCaptureSource(registry);
            await source.StartAsync().ConfigureAwait(false);
            IPubSubCaptureObserver observer = registry.CurrentObserver!;
            await source.StopAsync().ConfigureAwait(false);

            // Observer reference held after stop must no-op.
            var context = new PubSubCaptureContext(
                PubSubCaptureDirection.Inbound,
                "urn:test",
                new DateTimeUtc(DateTime.UtcNow));
            observer.OnFrameCaptured(in context, [1, 2, 3]);

            List<PubSubCaptureFrame> frames = await ReadAllAsync(source).ConfigureAwait(false);
            Assert.That(frames, Is.Empty);
        }

        [Test]
        public async Task StartTwiceThrowsAsync()
        {
            var registry = new PubSubCaptureRegistry();
            await using var source = new InProcessPubSubCaptureSource(registry);
            await source.StartAsync().ConfigureAwait(false);

            Assert.That(
                async () => await source.StartAsync().ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task SessionManagerStartsAndStopsCaptureAsync()
        {
            var registry = new PubSubCaptureRegistry();
            await using var manager = new PubSubCaptureSessionManager(registry);

            IPubSubCaptureSource source = await manager.StartAsync().ConfigureAwait(false);
            Assert.That(manager.ActiveSource, Is.SameAs(source));
            Assert.That(registry.CurrentObserver, Is.Not.Null);

            byte[] payload = [0x01, 0x02];
            EmitFrame(registry, PubSubCaptureDirection.Inbound, payload, null);

            IPubSubCaptureSource? stopped = await manager.StopAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(stopped, Is.SameAs(source));
                Assert.That(manager.ActiveSource, Is.Null);
                Assert.That(registry.CurrentObserver, Is.Null);
            });

            List<PubSubCaptureFrame> frames = await ReadAllAsync(source).ConfigureAwait(false);
            Assert.That(frames, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task SessionManagerRejectsSecondConcurrentSessionAsync()
        {
            var registry = new PubSubCaptureRegistry();
            await using var manager = new PubSubCaptureSessionManager(registry);
            await manager.StartAsync().ConfigureAwait(false);

            Assert.That(
                async () => await manager.StartAsync().ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        private static void EmitFrame(
            PubSubCaptureRegistry registry,
            PubSubCaptureDirection direction,
            byte[] payload,
            string? endpoint)
        {
            var context = new PubSubCaptureContext(
                direction,
                "urn:test:transport",
                new DateTimeUtc(DateTime.UtcNow),
                endpoint);
            registry.CurrentObserver!.OnFrameCaptured(in context, payload);
        }

        private static async Task<List<PubSubCaptureFrame>> ReadAllAsync(IPubSubCaptureSource source)
        {
            var frames = new List<PubSubCaptureFrame>();
            await foreach (PubSubCaptureFrame frame in source.ReadCapturedFramesAsync(
                maxFrames: null, CancellationToken.None))
            {
                frames.Add(frame);
            }
            return frames;
        }
    }
}
