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

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Redundancy.Tests
{
    /// <summary>
    /// Unit tests for the client replica coordinator and builder seams that do not
    /// require a live server. Full lifecycle (token reuse, failover, subscription
    /// transfer) is exercised by the integration tests.
    /// </summary>
    [TestFixture]
    [Category("ClientRedundancy")]
    public sealed class ClientReplicaCoordinatorTests
    {
        private ITelemetryContext m_telemetry = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void FailClosedRejectsNetworkedStoreWithoutProtector()
        {
            var options = new ClientReplicaOptions { CreateSessionAsync = _ => default };
            var election = new StaticLeaderElection(false);
            var store = new FakeNetworkedStore();
            Assert.That(
                () => new ClientReplicaCoordinator(
                    options, election, store, NullRecordProtector.Instance, m_telemetry),
                Throws.InvalidOperationException);
        }

        [Test]
        public void InMemoryStoreWithoutProtectorIsAllowed()
        {
            var options = new ClientReplicaOptions { CreateSessionAsync = _ => default };
            var election = new StaticLeaderElection(false);
            using var store = new InMemorySharedKeyValueStore();
            Assert.That(
                () => new ClientReplicaCoordinator(
                    options, election, store, NullRecordProtector.Instance, m_telemetry),
                Throws.Nothing);
        }

        [Test]
        public void MissingSessionFactoryThrows()
        {
            var election = new StaticLeaderElection(false);
            using var store = new InMemorySharedKeyValueStore();
            Assert.That(
                () => new ClientReplicaCoordinator(
                    new ClientReplicaOptions(), election, store, NullRecordProtector.Instance, m_telemetry),
                Throws.ArgumentException);
        }

        [Test]
        public async Task ColdStandbyDoesNotConnectBeforeLeadershipAsync()
        {
            int created = 0;
            var options = new ClientReplicaOptions
            {
                Mode = ClientStandbyMode.Cold,
                CreateSessionAsync = _ => { created++; return default; }
            };
            var election = new StaticLeaderElection(false);
            using var store = new InMemorySharedKeyValueStore();
            await using var coordinator = new ClientReplicaCoordinator(
                options, election, store, NullRecordProtector.Instance, m_telemetry);
            await coordinator.StartAsync().ConfigureAwait(false);
            Assert.That(created, Is.Zero);
            Assert.That(coordinator.CurrentSession, Is.Null);
            Assert.That(coordinator.IsLeader, Is.False);
        }

        [Test]
        public void BuilderRequiresRedundancySeams()
        {
            ClientReplicaSetBuilder builder = new ClientReplicaSetBuilder(m_telemetry)
                .WithNodeId("a")
                .WithStandbyMode(ClientStandbyMode.Hot)
                .UseSession(_ => default);
            Assert.That(() => builder.Build(), Throws.InvalidOperationException);
        }

        private sealed class FakeNetworkedStore : ISharedKeyValueStore
        {
            public ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default)
                => new((false, default));

            public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default) => default;

            public ValueTask<bool> CompareAndSwapAsync(
                string key, ByteString expected, ByteString value, CancellationToken ct = default) => new(true);

            public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default) => new(true);

            public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix, [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }

            public async IAsyncEnumerable<KeyValueChange> WatchAsync(
                string keyPrefix, [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }
        }
    }
}
