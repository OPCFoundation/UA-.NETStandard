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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for the GetEndpoints load-direction peer signal publish/view
    /// (<see cref="SharedPeerDirectionPublisher"/> / <see cref="SharedPeerDirectionView"/>).
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class PeerDirectionViewTests
    {
        [Test]
        public async Task PublishAndViewRoundTripReturnsFreshPeerAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions();
            var time = new FakeTimeProvider();

            var publisher = new SharedPeerDirectionPublisher(
                store, context, NullRecordProtector.Instance, options, time, "urn:server:a");
            await publisher.PublishServiceLevelAsync(200);
            await publisher.PublishLoadWeightAsync(30);

            var view = new SharedPeerDirectionView(
                store, context, NullRecordProtector.Instance, options, time);
            PeerDirectionRecord[] peers = (await view.GetPeersAsync()).ToArray();

            Assert.That(peers, Has.Length.EqualTo(1));
            Assert.That(peers[0].ServerUri, Is.EqualTo("urn:server:a"));
            Assert.That(peers[0].ServiceLevel, Is.EqualTo((byte)200));
            Assert.That(peers[0].LoadKnown, Is.True);
            Assert.That(peers[0].LoadWeight, Is.EqualTo((byte)30));
        }

        [Test]
        public async Task ViewExcludesStaleHealthRecordAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { StalenessWindow = TimeSpan.FromSeconds(15) };
            var time = new FakeTimeProvider();

            var publisher = new SharedPeerDirectionPublisher(
                store, context, NullRecordProtector.Instance, options, time, "urn:server:a");
            await publisher.PublishServiceLevelAsync(200);
            await publisher.PublishLoadWeightAsync(30);

            time.Advance(TimeSpan.FromSeconds(20));

            var view = new SharedPeerDirectionView(
                store, context, NullRecordProtector.Instance, options, time);
            PeerDirectionRecord[] peers = (await view.GetPeersAsync()).ToArray();

            Assert.That(peers, Is.Empty, "a peer whose health record is stale must be aged out");
        }

        [Test]
        public async Task ViewReportsLoadUnknownWhenLoadStaleButHealthFreshAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { StalenessWindow = TimeSpan.FromSeconds(15) };
            var time = new FakeTimeProvider();

            var publisher = new SharedPeerDirectionPublisher(
                store, context, NullRecordProtector.Instance, options, time, "urn:server:a");
            await publisher.PublishLoadWeightAsync(30);
            time.Advance(TimeSpan.FromSeconds(20));
            await publisher.PublishServiceLevelAsync(200);

            var view = new SharedPeerDirectionView(
                store, context, NullRecordProtector.Instance, options, time);
            PeerDirectionRecord[] peers = (await view.GetPeersAsync()).ToArray();

            Assert.That(peers, Has.Length.EqualTo(1));
            Assert.That(peers[0].ServiceLevel, Is.EqualTo((byte)200));
            Assert.That(peers[0].LoadKnown, Is.False, "a stale load record must be reported as unknown");
            Assert.That(peers[0].LoadWeight, Is.Zero);
        }

        [Test]
        public async Task ViewDropsMalformedHealthRecordAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions();
            var time = new FakeTimeProvider();

            await store.SetAsync("svc/urn:server:x", ByteString.From(new byte[] { 9, 9, 9 }));

            var view = new SharedPeerDirectionView(
                store, context, NullRecordProtector.Instance, options, time);
            PeerDirectionRecord[] peers = (await view.GetPeersAsync()).ToArray();

            Assert.That(peers, Is.Empty, "an undecodable record must be dropped (fail-closed)");
        }

        [Test]
        public async Task ViewReturnsAllHealthyPeersAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions();
            var time = new FakeTimeProvider();

            var a = new SharedPeerDirectionPublisher(
                store, context, NullRecordProtector.Instance, options, time, "urn:server:a");
            var b = new SharedPeerDirectionPublisher(
                store, context, NullRecordProtector.Instance, options, time, "urn:server:b");
            await a.PublishServiceLevelAsync(200);
            await b.PublishServiceLevelAsync(210);

            var view = new SharedPeerDirectionView(
                store, context, NullRecordProtector.Instance, options, time);
            PeerDirectionRecord[] peers = (await view.GetPeersAsync()).ToArray();

            Assert.That(peers.Select(p => p.ServerUri), Is.EquivalentTo(new[] { "urn:server:a", "urn:server:b" }));
            Assert.That(peers.Single(p => p.ServerUri == "urn:server:b").ServiceLevel, Is.EqualTo((byte)210));
        }

        [Test]
        public async Task RepublishOverwritesPreviousSignalAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions();
            var time = new FakeTimeProvider();

            var publisher = new SharedPeerDirectionPublisher(
                store, context, NullRecordProtector.Instance, options, time, "urn:server:a");
            await publisher.PublishServiceLevelAsync(200);
            time.Advance(TimeSpan.FromSeconds(1));
            await publisher.PublishServiceLevelAsync(120);

            var view = new SharedPeerDirectionView(
                store, context, NullRecordProtector.Instance, options, time);
            PeerDirectionRecord[] peers = (await view.GetPeersAsync()).ToArray();

            Assert.That(peers, Has.Length.EqualTo(1));
            Assert.That(peers[0].ServiceLevel, Is.EqualTo((byte)120), "the latest published health value must win");
        }

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
        }
    }
}
