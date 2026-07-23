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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Di.Client;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Behavioural tests for <see cref="DiDeviceClient"/>. Covers
    /// constructor validation, <see cref="DiDeviceClient.ForDeviceAsync"/>
    /// existence check, and the full
    /// <see cref="DiDeviceClient.ReadIdentificationAsync"/> translate +
    /// batch read flow.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("ClientHelpers")]
    public sealed class DiDeviceClientTests
    {
        private const int IdentificationCount = 8;

        [Test]
        public void ConstructorThrowsOnNullSession()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new DiDeviceClient(
                    null!, new NodeId("dev-1", 2), NullTelemetry()))!;
            Assert.That(ex.ParamName, Is.EqualTo("session"));
        }

        [Test]
        public void ConstructorThrowsOnNullDeviceNodeId()
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => new DiDeviceClient(
                    CreateSessionMock().Object, NodeId.Null, NullTelemetry()))!;
            Assert.That(ex.ParamName, Is.EqualTo("deviceNodeId"));
        }

        [Test]
        public void ConstructorThrowsOnNullTelemetry()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new DiDeviceClient(
                    CreateSessionMock().Object, new NodeId("dev-1", 2), null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("telemetry"));
        }

        [Test]
        public void ConstructorExposesArgumentsAndCreatesProxy()
        {
            ISession session = CreateSessionMock().Object;
            var nodeId = new NodeId("dev-1", 2);
            ITelemetryContext telemetry = NullTelemetry();
            var client = new DiDeviceClient(session, nodeId, telemetry);

            Assert.That(client.Session, Is.SameAs(session));
            Assert.That(client.DeviceNodeId, Is.EqualTo(nodeId));
            Assert.That(client.Telemetry, Is.SameAs(telemetry));
            Assert.That(client.Proxy, Is.Not.Null);
        }

        [Test]
        public async Task ReadIdentificationAsyncBuildsEightBrowsePathsAndPopulatesValues()
        {
            Mock<ISession> sessionMock = CreateSessionMock();

            ArrayOf<BrowsePath> capturedPaths = default;
            sessionMock
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader?, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, paths, _) => capturedPaths = paths)
                .ReturnsAsync(() => new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = BuildAllGoodBrowsePathResults(IdentificationCount)
                });

            SetupReadReturns(sessionMock, BuildGoodValues(IdentificationCount));

            var client = new DiDeviceClient(
                sessionMock.Object, new NodeId("dev-1", 2), NullTelemetry());

            DeviceIdentification id = await client.ReadIdentificationAsync().ConfigureAwait(false);

            Assert.That(capturedPaths.Count, Is.EqualTo(IdentificationCount));
            Assert.That(id.Manufacturer, Is.EqualTo("v0"));
            Assert.That(id.Model, Is.EqualTo("v1"));
            Assert.That(id.SerialNumber, Is.EqualTo("v2"));
            Assert.That(id.HardwareRevision, Is.EqualTo("v3"));
            Assert.That(id.SoftwareRevision, Is.EqualTo("v4"));
            Assert.That(id.DeviceRevision, Is.EqualTo("v5"));
            Assert.That(id.DeviceClass, Is.EqualTo("v6"));
            Assert.That(id.ProductInstanceUri, Is.EqualTo("v7"));

            sessionMock.Verify(
                s => s.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ReadIdentificationAsyncBadValueReadIsNull()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            SetupTranslateReturns(
                sessionMock, BuildAllGoodBrowsePathResults(IdentificationCount));

            // Read 3rd value with bad status — SerialNumber should be null.
            DataValue[] values = BuildGoodValues(IdentificationCount);
            values[2] = new DataValue(Variant.Null
            , StatusCodes.BadAttributeIdInvalid);
            SetupReadReturns(sessionMock, values);

            var client = new DiDeviceClient(
                sessionMock.Object, new NodeId("dev-1", 2), NullTelemetry());

            DeviceIdentification id = await client.ReadIdentificationAsync().ConfigureAwait(false);
            Assert.That(id.SerialNumber, Is.Null);
            Assert.That(id.Manufacturer, Is.EqualTo("v0"));
            Assert.That(id.Model, Is.EqualTo("v1"));
        }

        [Test]
        public async Task ReadIdentificationAsyncReturnsAllNullsWhenAllBrowsePathsBad()
        {
            Mock<ISession> sessionMock = CreateSessionMock();

            var badResults = new BrowsePathResult[IdentificationCount];
            for (int i = 0; i < IdentificationCount; i++)
            {
                badResults[i] = new BrowsePathResult
                {
                    StatusCode = StatusCodes.BadNoMatch,
                    Targets = global::Opc.Ua.ArrayOf.Empty<BrowsePathTarget>()
                };
            }
            SetupTranslateReturns(sessionMock, badResults);

            // ReadAsync should not be called when no paths resolved; set up
            // a strict expectation to verify that.
            var client = new DiDeviceClient(
                sessionMock.Object, new NodeId("dev-1", 2), NullTelemetry());

            DeviceIdentification id = await client.ReadIdentificationAsync().ConfigureAwait(false);

            Assert.That(id.Manufacturer, Is.Null);
            Assert.That(id.Model, Is.Null);
            Assert.That(id.SerialNumber, Is.Null);
            Assert.That(id.HardwareRevision, Is.Null);
            Assert.That(id.SoftwareRevision, Is.Null);
            Assert.That(id.DeviceRevision, Is.Null);
            Assert.That(id.DeviceClass, Is.Null);
            Assert.That(id.ProductInstanceUri, Is.Null);

            sessionMock.Verify(
                s => s.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public void ForDeviceAsyncThrowsBadNodeIdUnknownWhenReadStatusBad()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            SetupReadReturns(sessionMock,
            [
                new DataValue(Variant.Null
                , StatusCodes.BadNodeIdUnknown)
            ]);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await DiDeviceClient.ForDeviceAsync(
                    sessionMock.Object,
                    new NodeId("dev-1", 2),
                    NullTelemetry()).ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task ForDeviceAsyncReturnsClientWhenReadSucceeds()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            SetupReadReturns(sessionMock,
            [
                new DataValue(new Variant((int)NodeClass.Object)
                , StatusCodes.Good)
            ]);

            DiDeviceClient client = await DiDeviceClient.ForDeviceAsync(
                sessionMock.Object,
                new NodeId("dev-1", 2),
                NullTelemetry()).ConfigureAwait(false);

            Assert.That(client, Is.Not.Null);
            Assert.That(client.DeviceNodeId, Is.EqualTo(new NodeId("dev-1", 2)));
        }

        [Test]
        public async Task BrowseFunctionalGroupsAsyncReturnsSubtypeInstancesAsync()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            var nodeCacheMock = new Mock<INodeCache>(MockBehavior.Strict);
            sessionMock.SetupGet(s => s.NodeCache).Returns(nodeCacheMock.Object);

            ExpandedNodeId functionalGroupSubtype = new("VendorFunctionalGroupType", 2);
            ExpandedNodeId unrelatedType = new("FolderType", 2);
            ReferenceDescription subtypeGroup = new()
            {
                NodeId = new ExpandedNodeId(new NodeId("group-1", 2)),
                DisplayName = new LocalizedText("VendorGroup"),
                TypeDefinition = functionalGroupSubtype,
                NodeClass = NodeClass.Object
            };
            ReferenceDescription folder = new()
            {
                NodeId = new ExpandedNodeId(new NodeId("folder-1", 2)),
                DisplayName = new LocalizedText("Folder"),
                TypeDefinition = unrelatedType,
                NodeClass = NodeClass.Object
            };

            sessionMock
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ViewDescription?>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            References = [subtypeGroup, folder]
                        }
                    ]
                });

            nodeCacheMock
                .Setup(c => c.IsTypeOfAsync(
                    functionalGroupSubtype,
                    Opc.Ua.Di.ObjectTypeIds.FunctionalGroupType,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(true));
            nodeCacheMock
                .Setup(c => c.IsTypeOfAsync(
                    unrelatedType,
                    Opc.Ua.Di.ObjectTypeIds.FunctionalGroupType,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(false));

            var client = new DiDeviceClient(
                sessionMock.Object, new NodeId("dev-1", 2), NullTelemetry());

            var groups = new System.Collections.Generic.List<FunctionalGroupEntry>();
            await foreach (FunctionalGroupEntry group in client.BrowseFunctionalGroupsAsync())
            {
                groups.Add(group);
            }

            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].NodeId, Is.EqualTo(new NodeId("group-1", 2)));
            Assert.That(groups[0].DisplayName, Is.EqualTo("VendorGroup"));
            nodeCacheMock.VerifyAll();
        }

        [Test]
        public void ForDeviceAsyncThrowsOnNullSession()
        {
            ArgumentNullException ex = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await DiDeviceClient.ForDeviceAsync(
                    null!, new NodeId("dev-1", 2), NullTelemetry()).ConfigureAwait(false))!;
            Assert.That(ex.ParamName, Is.EqualTo("session"));
        }

        [Test]
        public void ForDeviceAsyncThrowsOnNullDeviceNodeId()
        {
            ISession session = CreateSessionMock().Object;
            ArgumentException ex = Assert.ThrowsAsync<ArgumentException>(
                async () => await DiDeviceClient.ForDeviceAsync(
                    session, NodeId.Null, NullTelemetry()).ConfigureAwait(false))!;
            Assert.That(ex.ParamName, Is.EqualTo("deviceNodeId"));
        }

        private static BrowsePathResult[] BuildAllGoodBrowsePathResults(int count)
        {
            var results = new BrowsePathResult[count];
            for (int i = 0; i < count; i++)
            {
                results[i] = new BrowsePathResult
                {
                    StatusCode = StatusCodes.Good,
                    Targets = new BrowsePathTarget[]
                    {
                        new() {
                            TargetId = new ExpandedNodeId("prop-" + i, 2),
                            RemainingPathIndex = uint.MaxValue
                        }
                    }
                };
            }
            return results;
        }

        private static DataValue[] BuildGoodValues(int count)
        {
            var values = new DataValue[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = new DataValue(new Variant("v" + i)
                , StatusCodes.Good);
            }
            return values;
        }

        private static Mock<ISession> CreateSessionMock()
        {
            var mock = new Mock<ISession>();
            var nsTable = new NamespaceTable();
            nsTable.GetIndexOrAppend(global::Opc.Ua.Di.Namespaces.OpcUaDi);
            mock.SetupGet(s => s.NamespaceUris).Returns(nsTable);
            return mock;
        }

        private static void SetupTranslateReturns(
            Mock<ISession> sessionMock, BrowsePathResult[] results)
        {
            sessionMock
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = results
                });
        }

        private static void SetupReadReturns(
            Mock<ISession> sessionMock, DataValue[] results)
        {
            sessionMock
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = results
                });
        }

        private static ITelemetryContext NullTelemetry()
        {
            return new Mock<ITelemetryContext>().Object;
        }
    }
}
