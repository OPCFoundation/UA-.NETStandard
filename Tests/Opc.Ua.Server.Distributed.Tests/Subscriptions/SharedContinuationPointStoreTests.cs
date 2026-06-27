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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Distributed;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Distributed
{
    /// <summary>
    /// Tests best-effort continuation point envelope mirroring.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Category("ContinuationPoints")]
    public class SharedContinuationPointStoreTests
    {
        [Test]
        public async Task BrowseContinuationPointEnvelopeReplicatesAcrossStoresAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            await using var primary = CreateStore(kv);
            await using var backup = CreateStore(kv);
            NodeId sessionId = new NodeId(Guid.NewGuid(), 1);
            Guid continuationPointId = Guid.NewGuid();

            primary.StoreContinuationPoint(CreateBrowseEnvelope(sessionId, continuationPointId));
            await primary.FlushAsync();

            ArrayOf<ContinuationPointEnvelope> envelopes =
                await backup.LoadContinuationPointsAsync(sessionId);

            Assert.That(envelopes, Has.Count.EqualTo(1));
            ContinuationPointEnvelope envelope = envelopes[0];
            Assert.That(envelope.Id, Is.EqualTo(continuationPointId));
            Assert.That(envelope.OwnerSessionId, Is.EqualTo(sessionId));
            Assert.That(envelope.Kind, Is.EqualTo(ContinuationPointKind.Browse));
            Assert.That(envelope.BrowseNodeId, Is.EqualTo(new NodeId("Demo", 2)));
            Assert.That(envelope.MaxResultsToReturn, Is.EqualTo(10));
            Assert.That(envelope.ResultMask, Is.EqualTo(BrowseResultMask.All));
        }

        [Test]
        public async Task RemovedContinuationPointEnvelopeDoesNotLoadOnBackupAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            await using var primary = CreateStore(kv);
            await using var backup = CreateStore(kv);
            NodeId sessionId = new NodeId(Guid.NewGuid(), 1);
            Guid continuationPointId = Guid.NewGuid();

            primary.StoreContinuationPoint(CreateBrowseEnvelope(sessionId, continuationPointId));
            await primary.FlushAsync();
            primary.RemoveContinuationPoint(sessionId, ContinuationPointKind.Browse, continuationPointId);
            await primary.FlushAsync();

            ArrayOf<ContinuationPointEnvelope> envelopes =
                await backup.LoadContinuationPointsAsync(sessionId);

            Assert.That(envelopes, Is.Empty);
        }

        [Test]
        public async Task BrowseNextForMirroredOpaqueContinuationPointFailsGracefullyAsync()
        {
            MasterNodeManager manager = CreateMasterNodeManager();
            var session = new Mock<ISession>();
            ByteString continuationPoint = Guid.NewGuid().ToByteArray().ToByteString();
            session
                .Setup(s => s.RestoreContinuationPoint(continuationPoint))
                .Returns((ContinuationPoint?)null);
            var context = new OperationContext(session.Object, DiagnosticsMasks.None);

            (ArrayOf<BrowseResult> results, _) = await manager.BrowseNextAsync(
                context,
                false,
                [continuationPoint]);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
        }

        [Test]
        public async Task LoadUnknownSessionReturnsNoEnvelopesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            await using var store = CreateStore(kv);

            ArrayOf<ContinuationPointEnvelope> envelopes =
                await store.LoadContinuationPointsAsync(new NodeId(Guid.NewGuid(), 1));

            Assert.That(envelopes, Is.Empty);
        }

        [Test]
        public async Task SecondBrowseContinuationPointEnvelopeReplicatesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            await using var primary = CreateStore(kv);
            await using var backup = CreateStore(kv);
            NodeId sessionId = new NodeId(Guid.NewGuid(), 1);
            Guid continuationPointId = Guid.NewGuid();

            primary.StoreContinuationPoint(new ContinuationPointEnvelope
            {
                Id = continuationPointId,
                OwnerSessionId = sessionId,
                Kind = ContinuationPointKind.Browse,
                Index = 5
            });
            await primary.FlushAsync();

            ArrayOf<ContinuationPointEnvelope> envelopes =
                await backup.LoadContinuationPointsAsync(sessionId);

            Assert.That(envelopes, Has.Count.EqualTo(1));
            Assert.That(envelopes[0].Kind, Is.EqualTo(ContinuationPointKind.Browse));
            Assert.That(envelopes[0].Index, Is.EqualTo(5));
        }

        [Test]
        public async Task HistoryContinuationPointEnvelopeReplicatesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            await using var primary = CreateStore(kv);
            await using var backup = CreateStore(kv);
            NodeId sessionId = new NodeId(Guid.NewGuid(), 1);
            Guid continuationPointId = Guid.NewGuid();

            primary.StoreContinuationPoint(new ContinuationPointEnvelope
            {
                Id = continuationPointId,
                OwnerSessionId = sessionId,
                Kind = ContinuationPointKind.History,
                BrowseNodeId = new NodeId("HistoryVar", 2)
            });
            await primary.FlushAsync();

            ArrayOf<ContinuationPointEnvelope> envelopes =
                await backup.LoadContinuationPointsAsync(sessionId);

            Assert.That(envelopes, Has.Count.EqualTo(1));
            Assert.That(envelopes[0].Kind, Is.EqualTo(ContinuationPointKind.History));
            Assert.That(envelopes[0].BrowseNodeId, Is.EqualTo(new NodeId("HistoryVar", 2)));
        }

        [Test]
        public async Task MultipleContinuationPointsForSameSessionReplicateAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            await using var primary = CreateStore(kv);
            await using var backup = CreateStore(kv);
            NodeId sessionId = new NodeId(Guid.NewGuid(), 1);

            primary.StoreContinuationPoint(CreateBrowseEnvelope(sessionId, Guid.NewGuid()));
            primary.StoreContinuationPoint(new ContinuationPointEnvelope
            {
                Id = Guid.NewGuid(),
                OwnerSessionId = sessionId,
                Kind = ContinuationPointKind.History
            });
            await primary.FlushAsync();

            ArrayOf<ContinuationPointEnvelope> envelopes =
                await backup.LoadContinuationPointsAsync(sessionId);

            Assert.That(envelopes, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task ContinuationPointForDifferentSessionDoesNotLoadAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            await using var primary = CreateStore(kv);
            await using var backup = CreateStore(kv);
            NodeId sessionA = new NodeId(Guid.NewGuid(), 1);
            NodeId sessionB = new NodeId(Guid.NewGuid(), 1);

            primary.StoreContinuationPoint(CreateBrowseEnvelope(sessionA, Guid.NewGuid()));
            await primary.FlushAsync();

            ArrayOf<ContinuationPointEnvelope> envelopes =
                await backup.LoadContinuationPointsAsync(sessionB);

            Assert.That(envelopes, Is.Empty);
        }

        [Test]
        public async Task FlushBeforeStoreCausesNoReplicationAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            await using var primary = CreateStore(kv);
            await using var backup = CreateStore(kv);
            NodeId sessionId = new NodeId(Guid.NewGuid(), 1);

            await primary.FlushAsync();
            primary.StoreContinuationPoint(CreateBrowseEnvelope(sessionId, Guid.NewGuid()));

            ArrayOf<ContinuationPointEnvelope> envelopes =
                await backup.LoadContinuationPointsAsync(sessionId);

            Assert.That(envelopes, Is.Empty);
        }

        private static SharedKeyValueSubscriptionStore CreateStore(InMemorySharedKeyValueStore kv)
        {
            return new SharedKeyValueSubscriptionStore(
                kv,
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
        }

        private static ContinuationPointEnvelope CreateBrowseEnvelope(NodeId sessionId, Guid id)
        {
            return new ContinuationPointEnvelope
            {
                Id = id,
                OwnerSessionId = sessionId,
                Kind = ContinuationPointKind.Browse,
                BrowseNodeId = new NodeId("Demo", 2),
                View = new ViewDescription { ViewId = ObjectIds.ViewsFolder },
                MaxResultsToReturn = 10,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (uint)NodeClass.Variable,
                ResultMask = BrowseResultMask.All,
                Index = 3
            };
        }

        private static MasterNodeManager CreateMasterNodeManager()
        {
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            var nodeManagerFactory = new Mock<IMainNodeManagerFactory>();
            var configurationNodeManager = new Mock<IConfigurationNodeManager>();
            configurationNodeManager.Setup(n => n.NamespaceUris).Returns(Array.Empty<string>());
            var coreNodeManager = new Mock<ICoreNodeManager>();
            nodeManagerFactory
                .Setup(f => f.CreateConfigurationNodeManager())
                .Returns(configurationNodeManager.Object);
            nodeManagerFactory
                .Setup(f => f.CreateCoreNodeManager(It.IsAny<ushort>()))
                .Returns(coreNodeManager.Object);
            server.Setup(s => s.MainNodeManagerFactory).Returns(nodeManagerFactory.Object);
            return new MasterNodeManager(
                server.Object,
                new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MaxBrowseContinuationPoints = 10
                    }
                },
                null,
                Array.Empty<IAsyncNodeManager>());
        }
    }
}
