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

#nullable enable

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="PeerDirectionPublishStartupTask"/>: constructor guards, the
    /// no-op path when the local ServerUri is unavailable, and the publish paths that gossip the
    /// local health ServiceLevel and load weight into the shared store for peers to read.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [NonParallelizable]
    public sealed class PeerDirectionPublishStartupTaskTests
    {
        private const string LocalServerUri = "urn:server:local";

        [Test]
        public void ConstructorThrowsOnNullStore()
        {
            Assert.That(
                () => new PeerDirectionPublishStartupTask(
                    null!,
                    Mock.Of<IRecordProtector>(),
                    new LoadDirectionOptions(),
                    Mock.Of<IServiceLevelProvider>(),
                    Mock.Of<ILoadWeightProvider>(),
                    TimeProvider.System),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorThrowsOnNullProtector()
        {
            Assert.That(
                () => new PeerDirectionPublishStartupTask(
                    Mock.Of<ISharedKeyValueStore>(),
                    null!,
                    new LoadDirectionOptions(),
                    Mock.Of<IServiceLevelProvider>(),
                    Mock.Of<ILoadWeightProvider>(),
                    TimeProvider.System),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorThrowsOnNullOptions()
        {
            Assert.That(
                () => new PeerDirectionPublishStartupTask(
                    Mock.Of<ISharedKeyValueStore>(),
                    Mock.Of<IRecordProtector>(),
                    null!,
                    Mock.Of<IServiceLevelProvider>(),
                    Mock.Of<ILoadWeightProvider>(),
                    TimeProvider.System),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorThrowsOnNullServiceLevelProvider()
        {
            Assert.That(
                () => new PeerDirectionPublishStartupTask(
                    Mock.Of<ISharedKeyValueStore>(),
                    Mock.Of<IRecordProtector>(),
                    new LoadDirectionOptions(),
                    null!,
                    Mock.Of<ILoadWeightProvider>(),
                    TimeProvider.System),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorThrowsOnNullLoadWeightProvider()
        {
            Assert.That(
                () => new PeerDirectionPublishStartupTask(
                    Mock.Of<ISharedKeyValueStore>(),
                    Mock.Of<IRecordProtector>(),
                    new LoadDirectionOptions(),
                    Mock.Of<IServiceLevelProvider>(),
                    null!,
                    TimeProvider.System),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorThrowsOnNullTimeProvider()
        {
            Assert.That(
                () => new PeerDirectionPublishStartupTask(
                    Mock.Of<ISharedKeyValueStore>(),
                    Mock.Of<IRecordProtector>(),
                    new LoadDirectionOptions(),
                    Mock.Of<IServiceLevelProvider>(),
                    Mock.Of<ILoadWeightProvider>(),
                    null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void OnServerStartedThrowsOnNullServer()
        {
            using var store = new InMemorySharedKeyValueStore();
            using var task = new PeerDirectionPublishStartupTask(
                store,
                NullRecordProtector.Instance,
                new LoadDirectionOptions(),
                new TriggerableServiceLevelProvider(200),
                new TriggerableLoadWeightProvider(30),
                new FakeTimeProvider());

            Assert.That(
                async () => await task.OnServerStartedAsync(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task OnServerStartedNoOpsWhenLocalServerUriMissingAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            var options = new LoadDirectionOptions();
            var time = new FakeTimeProvider();
            using var task = new PeerDirectionPublishStartupTask(
                store,
                NullRecordProtector.Instance,
                options,
                new TriggerableServiceLevelProvider(200),
                new TriggerableLoadWeightProvider(30),
                time);

            // No ServerUris -> the task cannot address itself and must publish nothing.
            Mock<IServerInternal> server = CreateServer(hasServerUri: false, out IServiceMessageContext context);

            await task.OnServerStartedAsync(server.Object);

            var view = new SharedPeerDirectionView(store, context, NullRecordProtector.Instance, options, time);
            ArrayOf<PeerDirectionRecord> peers = await view.GetPeersAsync();
            Assert.That(peers.Count, Is.Zero, "a server without a ServerUri must not gossip direction signals");
        }

        [Test]
        public async Task OnServerStartedPublishesInitialServiceLevelAndLoadAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            var options = new LoadDirectionOptions();
            var time = new FakeTimeProvider();
            using var task = new PeerDirectionPublishStartupTask(
                store,
                NullRecordProtector.Instance,
                options,
                new TriggerableServiceLevelProvider(200),
                new TriggerableLoadWeightProvider(30),
                time);

            Mock<IServerInternal> server = CreateServer(hasServerUri: true, out IServiceMessageContext context);

            await task.OnServerStartedAsync(server.Object);

            var view = new SharedPeerDirectionView(store, context, NullRecordProtector.Instance, options, time);
            ArrayOf<PeerDirectionRecord> peers = await view.GetPeersAsync();

            Assert.That(peers.Count, Is.EqualTo(1));
            PeerDirectionRecord record = peers[0];
            Assert.That(record.ServerUri, Is.EqualTo(LocalServerUri));
            Assert.That(record.ServiceLevel, Is.EqualTo((byte)200));
            Assert.That(record.LoadKnown, Is.True);
            Assert.That(record.LoadWeight, Is.EqualTo((byte)30));
        }

        [Test]
        public async Task ServiceLevelChangeRepublishesHealthSignalAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            var options = new LoadDirectionOptions();
            var time = new FakeTimeProvider();
            var serviceLevel = new TriggerableServiceLevelProvider(200);
            using var task = new PeerDirectionPublishStartupTask(
                store,
                NullRecordProtector.Instance,
                options,
                serviceLevel,
                new TriggerableLoadWeightProvider(30),
                time);

            Mock<IServerInternal> server = CreateServer(hasServerUri: true, out IServiceMessageContext context);
            await task.OnServerStartedAsync(server.Object);

            serviceLevel.Raise(50);

            var view = new SharedPeerDirectionView(store, context, NullRecordProtector.Instance, options, time);
            PeerDirectionRecord peer = await WaitForPeerAsync(view, p => p.ServiceLevel == 50);
            Assert.That(peer.ServiceLevel, Is.EqualTo((byte)50));
        }

        [Test]
        public async Task LoadWeightChangeIsCoalescedAndPublishedOnTimerAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            var options = new LoadDirectionOptions { LoadPublishInterval = TimeSpan.FromMilliseconds(25) };
            var time = new FakeTimeProvider();
            var loadWeight = new TriggerableLoadWeightProvider(30);
            using var task = new PeerDirectionPublishStartupTask(
                store,
                NullRecordProtector.Instance,
                options,
                new TriggerableServiceLevelProvider(200),
                loadWeight,
                time);

            Mock<IServerInternal> server = CreateServer(hasServerUri: true, out IServiceMessageContext context);
            await task.OnServerStartedAsync(server.Object);

            loadWeight.Raise(99);

            var view = new SharedPeerDirectionView(store, context, NullRecordProtector.Instance, options, time);
            PeerDirectionRecord peer = await WaitForPeerAsync(view, p => p.LoadWeight == 99);
            Assert.That(peer.LoadWeight, Is.EqualTo((byte)99));
        }

        [Test]
        public async Task DisposeUnsubscribesAndStopsPublishingAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            var options = new LoadDirectionOptions { LoadPublishInterval = TimeSpan.FromMilliseconds(25) };
            var time = new FakeTimeProvider();
            var serviceLevel = new TriggerableServiceLevelProvider(200);
            var task = new PeerDirectionPublishStartupTask(
                store,
                NullRecordProtector.Instance,
                options,
                serviceLevel,
                new TriggerableLoadWeightProvider(30),
                time);

            Mock<IServerInternal> server = CreateServer(hasServerUri: true, out IServiceMessageContext context);
            await task.OnServerStartedAsync(server.Object);

            task.Dispose();

            // After disposal the provider events are detached, so a raise is a no-op.
            serviceLevel.Raise(50);
            await Task.Delay(100);

            var view = new SharedPeerDirectionView(store, context, NullRecordProtector.Instance, options, time);
            ArrayOf<PeerDirectionRecord> peers = await view.GetPeersAsync();
            Assert.That(peers.Count, Is.EqualTo(1));
            Assert.That(peers[0].ServiceLevel, Is.EqualTo((byte)200), "a disposed task must not react to further changes");
        }

        private static async Task<PeerDirectionRecord> WaitForPeerAsync(
            SharedPeerDirectionView view,
            Func<PeerDirectionRecord, bool> predicate)
        {
            for (int attempt = 0; attempt < 400; attempt++)
            {
                ArrayOf<PeerDirectionRecord> peers = await view.GetPeersAsync();
                for (int i = 0; i < peers.Count; i++)
                {
                    PeerDirectionRecord peer = peers[i];
                    if (string.Equals(peer.ServerUri, LocalServerUri, StringComparison.Ordinal) && predicate(peer))
                    {
                        return peer;
                    }
                }
                await Task.Delay(25);
            }
            Assert.Fail("the expected direction signal was not published within the timeout");
            throw new InvalidOperationException("unreachable");
        }

        private static Mock<IServerInternal> CreateServer(bool hasServerUri, out IServiceMessageContext context)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            context = ServiceMessageContext.CreateEmpty(telemetry);
            var serverUris = new StringTable();
            if (hasServerUri)
            {
                serverUris.Append(LocalServerUri);
            }

            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.ServerUris).Returns(serverUris);
            server.Setup(s => s.MessageContext).Returns(context);
            return server;
        }

        private sealed class TriggerableServiceLevelProvider : IServiceLevelProvider
        {
            public TriggerableServiceLevelProvider(byte serviceLevel)
            {
                m_serviceLevel = serviceLevel;
            }

            public event Action<byte>? ServiceLevelChanged;

            public byte GetServiceLevel()
            {
                return m_serviceLevel;
            }

            public void Raise(byte serviceLevel)
            {
                m_serviceLevel = serviceLevel;
                ServiceLevelChanged?.Invoke(serviceLevel);
            }

            private byte m_serviceLevel;
        }

        private sealed class TriggerableLoadWeightProvider : ILoadWeightProvider
        {
            public TriggerableLoadWeightProvider(byte loadWeight)
            {
                m_loadWeight = loadWeight;
            }

            public event Action<byte>? LoadWeightChanged;

            public byte GetLoadWeight()
            {
                return m_loadWeight;
            }

            public void Raise(byte loadWeight)
            {
                m_loadWeight = loadWeight;
                LoadWeightChanged?.Invoke(loadWeight);
            }

            private byte m_loadWeight;
        }
    }
}
