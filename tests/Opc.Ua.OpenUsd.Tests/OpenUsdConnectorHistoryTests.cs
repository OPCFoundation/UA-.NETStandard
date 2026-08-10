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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Drives §5.6 history replay of <see cref="OpenUsdConnector"/> against an
    /// in-memory address space and a scripted HistoryRead service.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdConnectorHistoryTests
    {
        private FakeAddressSpace m_space = null!;
        private Mock<ISession> m_session = null!;
        private MockUsdSink m_sink = null!;
        private NodeId m_binding = NodeId.Null;
        private NodeId m_signal = NodeId.Null;
        private readonly Queue<HistoryReadResponse> m_responses = new();
        private readonly List<bool> m_releaseFlags = [];

        [SetUp]
        public void SetUp()
        {
            m_space = new FakeAddressSpace();
            m_session = FakeSession.Create(m_space);
            m_sink = new MockUsdSink();
            m_responses.Clear();
            m_releaseFlags.Clear();

            NodeId facility = m_space.AddObject(Opc.Ua.ObjectIds.Server, "OpenUSD",
                browseNameNamespace: m_space.OpenUsdNamespaceIndex);
            NodeId registry = m_space.AddObject(facility, "Representations");
            NodeId machine = m_space.AddObject(Opc.Ua.ObjectIds.Server, "Machine");
            m_signal = m_space.AddVariable(machine, "Speed", new Variant(0.0));
            NodeId rep = m_space.AddObject(registry, "Robot",
                new NodeId(OpenUsdModel.RepresentationTypeId, m_space.OpenUsdNamespaceIndex));
            m_space.AddVariable(rep, "PrimPath", new Variant("/World/Robot"));
            m_binding = m_space.AddObject(rep, "SpeedHistory",
                new NodeId(OpenUsdModel.HistoryBindingTypeId, m_space.OpenUsdNamespaceIndex));
            m_space.AddVariable(m_binding, "SourceNodeId", new Variant(m_signal));
            m_space.AddVariable(m_binding, "TargetPropertyName", new Variant("speed"));
            m_space.AddVariable(m_binding, "RenderTargetKind",
                new Variant((int)OpenUsdRenderTargetKind.Custom));
            m_space.AddVariable(m_binding, "TimeSampled", new Variant(true));

            m_session
                .Setup(s => s.HistoryReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ExtensionObject>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ExtensionObject _, TimestampsToReturn _,
                    bool release, ArrayOf<HistoryReadValueId> _, CancellationToken _) =>
                {
                    m_releaseFlags.Add(release);
                    return new ValueTask<HistoryReadResponse>(m_responses.Count > 0
                        ? m_responses.Dequeue()
                        : EmptyResponse());
                });
        }

        private OpenUsdConnector Connector()
        {
            return new OpenUsdConnector(m_session.Object, m_sink);
        }

        private static HistoryReadResponse EmptyResponse()
        {
            return new HistoryReadResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [],
                DiagnosticInfos = []
            };
        }

        private static HistoryReadResponse Response(
            StatusCode statusCode, ByteString continuationPoint, params DataValue[] values)
        {
            return new HistoryReadResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results =
                [
                    new HistoryReadResult
                    {
                        StatusCode = statusCode,
                        ContinuationPoint = continuationPoint,
                        HistoryData = new ExtensionObject(new HistoryData
                        {
                            DataValues = values.ToArrayOf()
                        })
                    }
                ],
                DiagnosticInfos = []
            };
        }

        private static DataValue Sample(double value, int secondsAgo)
        {
            return new DataValue(
                new Variant(value),
                StatusCodes.Good,
                DateTimeUtc.From(DateTime.UtcNow.AddSeconds(-secondsAgo)));
        }

        private Task<int> ReplayAsync(OpenUsdConnector connector)
        {
            return connector.ReplayHistoryAsync(
                DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, CancellationToken.None);
        }

        [Test]
        public async Task ReplayHistoryAuthorsOneTimeSamplePerGoodValueAsync()
        {
            m_responses.Enqueue(Response(StatusCodes.Good, default,
                Sample(1.0, 3), Sample(2.0, 2), Sample(3.0, 1)));
            OpenUsdConnector connector = Connector();

            int authored = await ReplayAsync(connector);

            Assert.That(authored, Is.EqualTo(3));
            Assert.That(m_sink.TimeSampleWrites, Is.EqualTo(3));
        }

        [Test]
        public async Task ReplayHistorySkipsValuesWithABadStatusCodeAsync()
        {
            var bad = new DataValue(
                new Variant(9.0), StatusCodes.BadOutOfService, DateTimeUtc.Now);
            m_responses.Enqueue(Response(StatusCodes.Good, default, Sample(1.0, 2), bad));
            OpenUsdConnector connector = Connector();

            int authored = await ReplayAsync(connector);

            Assert.That(authored, Is.EqualTo(1));
        }

        [Test]
        public async Task ReplayHistorySkipsValuesThatCannotBeConvertedAsync()
        {
            var unconvertible = new DataValue(
                new Variant("fast"), StatusCodes.Good, DateTimeUtc.Now);
            m_responses.Enqueue(Response(StatusCodes.Good, default, unconvertible));
            OpenUsdConnector connector = Connector();

            int authored = await ReplayAsync(connector);

            Assert.That(authored, Is.Zero);
            Assert.That(m_sink.TimeSampleWrites, Is.Zero);
        }

        [Test]
        public async Task ReplayHistoryFollowsContinuationPointsAsync()
        {
            m_responses.Enqueue(Response(StatusCodes.Good,
                new ByteString(new byte[] { 7 }), Sample(1.0, 3)));
            m_responses.Enqueue(Response(StatusCodes.Good, default, Sample(2.0, 2)));
            OpenUsdConnector connector = Connector();

            int authored = await ReplayAsync(connector);

            Assert.That(authored, Is.EqualTo(2));
            Assert.That(m_releaseFlags, Has.Count.EqualTo(2));
            Assert.That(m_releaseFlags, Has.None.True);
        }

        [Test]
        public async Task ReplayHistoryReleasesTheContinuationPointWhenTheServerFailsAsync()
        {
            m_responses.Enqueue(Response(StatusCodes.Good,
                new ByteString(new byte[] { 7 }), Sample(1.0, 3)));
            m_responses.Enqueue(Response(StatusCodes.BadTooManyOperations,
                new ByteString(new byte[] { 7 })));
            OpenUsdConnector connector = Connector();

            int authored = await ReplayAsync(connector);

            Assert.That(authored, Is.EqualTo(1));
            Assert.That(m_releaseFlags, Has.Some.True);
        }

        [Test]
        public async Task ReplayHistoryStopsWhenTheServerReturnsNoResultsAsync()
        {
            m_responses.Enqueue(EmptyResponse());
            OpenUsdConnector connector = Connector();

            int authored = await ReplayAsync(connector);

            Assert.That(authored, Is.Zero);
        }

        [Test]
        public async Task ReplayHistoryDegradesWhenTheSourceDoesNotHistorizeAsync()
        {
            m_session
                .Setup(s => s.HistoryReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ExtensionObject>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadHistoryOperationUnsupported));
            OpenUsdConnector connector = Connector();

            int authored = await ReplayAsync(connector);

            Assert.That(authored, Is.Zero);
        }

        [Test]
        public async Task ReplayHistoryIgnoresAFailedContinuationReleaseAsync()
        {
            int calls = 0;
            m_session
                .Setup(s => s.HistoryReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ExtensionObject>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ExtensionObject _, TimestampsToReturn _,
                    bool release, ArrayOf<HistoryReadValueId> _, CancellationToken _) =>
                {
                    calls++;
                    if (release)
                    {
                        throw new ServiceResultException(StatusCodes.BadContinuationPointInvalid);
                    }
                    return new ValueTask<HistoryReadResponse>(calls == 1
                        ? Response(StatusCodes.Good, new ByteString(new byte[] { 9 }), Sample(1.0, 1))
                        : Response(StatusCodes.BadTooManyOperations, new ByteString(new byte[] { 9 })));
                });
            OpenUsdConnector connector = Connector();

            int authored = await ReplayAsync(connector);

            Assert.That(authored, Is.EqualTo(1));
            Assert.That(calls, Is.EqualTo(3));
        }

        [Test]
        public async Task ReplayHistorySkipsABindingThatIsNotTimeSampledAsync()
        {
            m_space.SetValue(ChildOfBinding("TimeSampled"), new DataValue(new Variant(false)));
            m_responses.Enqueue(Response(StatusCodes.Good, default, Sample(1.0, 1)));
            OpenUsdConnector connector = Connector();

            int authored = await ReplayAsync(connector);

            Assert.That(authored, Is.Zero);
            Assert.That(m_releaseFlags, Is.Empty);
        }

        [Test]
        public async Task ReplayHistorySkipsABindingSuppressedByItsEnabledTombstoneAsync()
        {
            m_space.AddVariable(m_binding, "Enabled", new Variant(false));
            m_responses.Enqueue(Response(StatusCodes.Good, default, Sample(1.0, 1)));
            OpenUsdConnector connector = Connector();

            int authored = await ReplayAsync(connector);

            Assert.That(authored, Is.Zero);
            Assert.That(m_releaseFlags, Is.Empty);
        }

        [Test]
        public async Task ReplayHistorySkipsABindingWithAnUnresolvedSourceAsync()
        {
            m_space.SetValue(ChildOfBinding("SourceNodeId"), new DataValue(new Variant(NodeId.Null)));
            m_responses.Enqueue(Response(StatusCodes.Good, default, Sample(1.0, 1)));
            OpenUsdConnector connector = Connector();

            int authored = await ReplayAsync(connector);

            Assert.That(authored, Is.Zero);
            Assert.That(m_releaseFlags, Is.Empty);
        }

        private NodeId ChildOfBinding(string browseName)
        {
            BrowseResponse response = m_space.Browse(
            [
                new BrowseDescription
                {
                    NodeId = m_binding,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true
                }
            ]);
            ArrayOf<ReferenceDescription> refs = response.Results[0].References;
            for (int i = 0; i < refs.Count; i++)
            {
                if (refs[i].BrowseName.Name == browseName)
                {
                    return ExpandedNodeId.ToNodeId(refs[i].NodeId, m_space.NamespaceUris);
                }
            }
            return NodeId.Null;
        }
    }
}
