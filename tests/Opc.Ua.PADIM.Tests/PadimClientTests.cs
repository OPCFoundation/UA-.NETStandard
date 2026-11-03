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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.PADIM.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.PADIM.Tests
{
    /// <summary>
    /// Exercises bounded client traversal and typed reads without a listening server.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class PadimClientTests
    {
        /// <summary>
        /// Registers PAT decoding on the borrowed session and permits another client to reuse it.
        /// </summary>
        [Test]
        public void ConstructionRegistersPatTypesOnBorrowedSession()
        {
            Mock<ISession> session = CreateSession();
            IEncodeableFactory factory = session.Object.MessageContext.Factory;
            Assert.That(factory.ContainsEncodeableType(DataTypeIds.ChemicalSubstance), Is.False);
            var client = new PadimClient(session.Object, NUnitTelemetryContext.Create());
            Assert.That(factory.ContainsEncodeableType(DataTypeIds.ChemicalSubstance), Is.True);
            var secondClient = new PadimClient(session.Object, NUnitTelemetryContext.Create());
            Assert.That(client.Session, Is.SameAs(secondClient.Session));
            using var encoder = new BinaryEncoder(session.Object.MessageContext);
            encoder.WriteVariant(null, Variant.From(new ExtensionObject(new ChemicalSubstance
            {
                PatDictionary = PatDictionaryEnum.CAS,
                Label = new LocalizedText("borrowed-session substance"),
                Id = new LocalizedText("PAT-001")
            })));
            using var decoder = new BinaryDecoder(
                encoder.CloseAndReturnBuffer() ?? throw new InvalidOperationException("No encoded PAT sample."),
                session.Object.MessageContext);
            Variant decoded = decoder.ReadVariant(null);
            Assert.That(decoded.TryGetStructure<ChemicalSubstance>(out ChemicalSubstance? substance), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(substance!.PatDictionary, Is.EqualTo(PatDictionaryEnum.CAS));
                Assert.That(substance.Label.Text, Is.EqualTo("borrowed-session substance"));
                Assert.That(substance.Id.Text, Is.EqualTo("PAT-001"));
            });
            session.Verify(s => s.Dispose(), Times.Never);
        }

        /// <summary>
        /// Rejects node limits outside the documented inclusive interval.
        /// </summary>
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(65537)]
        public void InvalidDiscoveryLimitIsRejected(int limit)
        {
            Mock<ISession> session = CreateSession();
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PadimClient(session.Object, NUnitTelemetryContext.Create(), limit));
        }

        /// <summary>
        /// Allows the two endpoints of the supported discovery interval.
        /// </summary>
        [TestCase(1)]
        [TestCase(65536)]
        public void InclusiveDiscoveryLimitsAreAccepted(int limit)
        {
            Mock<ISession> session = CreateSession();
            var client = new PadimClient(session.Object, NUnitTelemetryContext.Create(), limit);
            Assert.That(client.IsSupported, Is.True);
            Assert.That(client.Session, Is.SameAs(session.Object));
        }

        /// <summary>
        /// A raw session cannot accidentally create an unmanaged observation pipeline.
        /// </summary>
        [Test]
        public void ObservationRequiresManagedSessionAndDoesNotOwnTheBorrowedSession()
        {
            Mock<ISession> session = CreateSession();
            var client = new PadimClient(session.Object, NUnitTelemetryContext.Create());
            Assert.Throws<NotSupportedException>(() => client.ObserveValueAsync(new NodeId(10)));
            session.Verify(s => s.Dispose(), Times.Never);
        }

        /// <summary>
        /// Rejects an absent identifier before constructing a generated proxy.
        /// </summary>
        [Test]
        public void ProxiesRejectNullNodeIdentifiers()
        {
            Mock<ISession> session = CreateSession();
            var client = new PadimClient(session.Object, NUnitTelemetryContext.Create());
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentException>(() => client.Device(NodeId.Null));
                Assert.Throws<ArgumentException>(() => client.AnalogSignal(NodeId.Null));
                Assert.Throws<ArgumentException>(() => client.ControlSignal(NodeId.Null));
                Assert.Throws<ArgumentException>(() => client.ProcessValue(NodeId.Null));
            });
        }

        /// <summary>
        /// Discovers vendor subtypes once despite cyclic and duplicate hierarchical references.
        /// </summary>
        [Test]
        public async Task DiscoveryRecognizesVendorSubtypesAndStopsAtVisitedNodes()
        {
            Mock<ISession> session = CreateSession();
            var root = new NodeId("device", 1);
            var signal = new NodeId("vendor-signal", 1);
            var vendorType = new NodeId("vendor-type", 1);
            NodeId required = ExpandedNodeId.ToNodeId(ObjectTypeIds.SignalType, session.Object.NamespaceUris);
            var cache = new Mock<INodeCache>();
            cache.Setup(c => c.IsTypeOfAsync(vendorType, required, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            session.SetupGet(s => s.NodeCache).Returns(cache.Object);
            BrowseWith(session, node => node == root
                ? [Reference(signal, vendorType), Reference(signal, vendorType)]
                : [Reference(root, ObjectTypeIds.PADIMType)]);
            var client = new PadimClient(session.Object, NUnitTelemetryContext.Create(), 2);

            ArrayOf<PadimEntry> discovered = await client.DiscoverSignalsAsync(root);

            Assert.Multiple(() =>
            {
                Assert.That(discovered.Count, Is.EqualTo(1));
                Assert.That(discovered[0].NodeId, Is.EqualTo(signal));
                Assert.That(discovered[0].TypeDefinitionId, Is.EqualTo(vendorType));
            });
            session.Verify(s => s.BrowseAsync(null, null, 0,
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            cache.Verify(c => c.IsTypeOfAsync(vendorType, required, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// The first unseen node over the limit produces an error rather than a truncated device list.
        /// </summary>
        [Test]
        public async Task DiscoveryRejectsTheFirstNodeOverItsLimit()
        {
            Mock<ISession> session = CreateSession();
            BrowseWith(session, _ => [Reference(new NodeId("child", 1), ObjectTypeIds.PADIMType)]);
            var client = new PadimClient(session.Object, NUnitTelemetryContext.Create(), 1);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.DiscoverDevicesAsync());
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
        }

        /// <summary>
        /// A service response containing too many immediate children also respects the bound.
        /// </summary>
        [Test]
        public async Task BrowseRejectsAResponseOverItsLimit()
        {
            Mock<ISession> session = CreateSession();
            BrowseWith(session, _ =>
                [Reference(new NodeId("first", 1)), Reference(new NodeId("second", 1))]);
            var client = new PadimClient(session.Object, NUnitTelemetryContext.Create(), 1);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.BrowseChildrenAsync(new NodeId("root", 1)));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
        }

        /// <summary>
        /// A dictionary identifier remains URI-qualified when namespace indexes differ.
        /// </summary>
        [Test]
        public async Task DictionaryReadsPreserveNamespaceUriIdentity()
        {
            Mock<ISession> session = CreateSession();
            const string dictionaryUri = "urn:padim:test:dictionary";
            session.Object.NamespaceUris.GetIndexOrAppend(dictionaryUri);
            BrowseWith(session, _ =>
                [Reference(new ExpandedNodeId("semantic-identifier", dictionaryUri))]);
            var client = new PadimClient(session.Object, NUnitTelemetryContext.Create());

            ArrayOf<ExpandedNodeId> entries = await client.ReadDictionaryEntriesAsync(new NodeId("value", 1));

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0], Is.EqualTo(new ExpandedNodeId("semantic-identifier", dictionaryUri)));
        }

        /// <summary>
        /// Remote dictionary targets are never treated as local numeric node identifiers.
        /// </summary>
        [Test]
        public async Task RemoteDictionaryReferencesAreRejected()
        {
            Mock<ISession> session = CreateSession();
            BrowseWith(session, _ => [Reference(new ExpandedNodeId(10, serverIndex: 2))]);
            var client = new PadimClient(session.Object, NUnitTelemetryContext.Create());
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadDictionaryEntriesAsync(new NodeId("value", 1)));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        /// <summary>
        /// A group with no variables does not issue a zero-operation Read request.
        /// </summary>
        [Test]
        public async Task EmptyReadGroupReturnsAnEmptyCollectionWithoutAReadRequest()
        {
            Mock<ISession> session = CreateSession();
            BrowseWith(session, _ => ArrayOf<ReferenceDescription>.Empty);
            var client = new PadimClient(session.Object, NUnitTelemetryContext.Create());

            ArrayOf<PadimValue> values = await client.ReadValuesAsync(new NodeId("empty", 1));

            Assert.That(values.IsNull, Is.False);
            Assert.That(values.Count, Is.Zero);
            session.Verify(s => s.ReadAsync(
                It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Typed Float samples preserve uncertain quality and both source and server timestamps.
        /// </summary>
        [Test]
        public async Task ReadsKeepTheOriginalDataValueAndQualifiedBrowseName()
        {
            Mock<ISession> session = CreateSession();
            var variable = new NodeId("measurement", 1);
            ReferenceDescription child = Reference(variable);
            child.BrowseName = new QualifiedName("Measurement", 1);
            BrowseWith(session, _ => [child]);
            var expected = new DataValue(Variant.From(12.5f), StatusCodes.UncertainSensorNotAccurate,
                new DateTimeUtc(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
                new DateTimeUtc(new DateTime(2026, 1, 2, 0, 0, 1, DateTimeKind.Utc)));
            session.Setup(s => s.ReadAsync(null, 0, TimestampsToReturn.Both,
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse { Results = [expected] });
            var client = new PadimClient(session.Object, NUnitTelemetryContext.Create());

            ArrayOf<PadimValue> values = await client.ReadValuesAsync(new NodeId("group", 1));

            Assert.Multiple(() =>
            {
                Assert.That(values.Count, Is.EqualTo(1));
                Assert.That(values[0].NodeId, Is.EqualTo(variable));
                Assert.That(values[0].BrowseName, Is.EqualTo(child.BrowseName));
                Assert.That(values[0].Sample, Is.EqualTo(expected));
            });
        }

        /// <summary>
        /// A missing per-variable result is reported instead of fabricating a sample.
        /// </summary>
        [Test]
        public async Task ReadRejectsAMismatchedServiceResultCount()
        {
            Mock<ISession> session = CreateSession();
            BrowseWith(session, _ => [Reference(new NodeId("measurement", 1))]);
            session.Setup(s => s.ReadAsync(null, 0, TimestampsToReturn.Both,
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse { Results = ArrayOf<DataValue>.Empty });
            var client = new PadimClient(session.Object, NUnitTelemetryContext.Create());
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadValuesAsync(new NodeId("group", 1)));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        /// <summary>
        /// An already cancelled traversal does not issue a browse or return an empty successful result.
        /// </summary>
        [Test]
        public async Task CancelledDiscoveryDoesNotIssueBrowseRequests()
        {
            Mock<ISession> session = CreateSession();
            var client = new PadimClient(session.Object, NUnitTelemetryContext.Create());
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await Assert.CatchAsync<OperationCanceledException>(
                async () => await client.DiscoverDevicesAsync(cancellation.Token));

            session.Verify(s => s.BrowseAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Creates a mock with the same namespace-aware browsing context as a connected session.
        /// </summary>
        private static Mock<ISession> CreateSession()
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.NamespaceUris.GetIndexOrAppend(Namespaces.PADIM);
            context.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi);
            var session = new Mock<ISession>();
            session.SetupGet(s => s.MessageContext).Returns(context);
            session.SetupGet(s => s.NamespaceUris).Returns(context.NamespaceUris);
            session.SetupGet(s => s.OperationLimits).Returns(new OperationLimits());
            session.SetupGet(s => s.ServerCapabilities).Returns(new ServerCapabilities());
            return session;
        }

        /// <summary>
        /// Returns one complete browse page for each visited node.
        /// </summary>
        private static void BrowseWith(
            Mock<ISession> session,
            Func<NodeId, ArrayOf<ReferenceDescription>> children)
        {
            session.Setup(s => s.BrowseAsync(null, null, 0,
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader?, ViewDescription?, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    (_, _, _, nodes, _) => new ValueTask<BrowseResponse>(new BrowseResponse
                    {
                        Results = [new BrowseResult { References = children(nodes[0].NodeId) }]
                    }));
        }

        /// <summary>
        /// Creates a local forward reference with the supplied generated or vendor type.
        /// </summary>
        private static ReferenceDescription Reference(ExpandedNodeId nodeId, ExpandedNodeId type = default)
        {
            return new ReferenceDescription { NodeId = nodeId, TypeDefinition = type };
        }
    }
}
