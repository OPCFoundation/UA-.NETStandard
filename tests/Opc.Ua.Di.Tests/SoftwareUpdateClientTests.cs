/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Di.Client;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Behavioural tests for
    /// <see cref="SoftwareUpdateClient.ReadSoftwareVersionAsync"/>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("ClientHelpers")]
    public sealed class SoftwareUpdateClientTests
    {
        [Test]
        public async Task ReadSoftwareVersionAsyncReturnsValueOnSuccess()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            SetupTranslateReturns(sessionMock, new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = new BrowsePathTarget[]
                {
                    new() {
                        TargetId = new ExpandedNodeId("sv-1", 2),
                        RemainingPathIndex = uint.MaxValue
                    }
                }
            });
            SetupReadReturns(sessionMock, new DataValue(new Variant("1.2.3")
            , StatusCodes.Good));

            var client = new SoftwareUpdateClient(
                sessionMock.Object, new NodeId("update-1", 2), NullTelemetry());

            string result = await client.ReadSoftwareVersionAsync().ConfigureAwait(false);
            Assert.That(result, Is.EqualTo("1.2.3"));
        }

        [Test]
        public async Task ReadSoftwareVersionAsyncReturnsEmptyWhenBrowsePathBad()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            SetupTranslateReturns(sessionMock, new BrowsePathResult
            {
                StatusCode = StatusCodes.BadNoMatch,
                Targets = global::Opc.Ua.ArrayOf.Empty<BrowsePathTarget>()
            });

            var client = new SoftwareUpdateClient(
                sessionMock.Object, new NodeId("update-1", 2), NullTelemetry());

            string result = await client.ReadSoftwareVersionAsync().ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task ReadSoftwareVersionAsyncReturnsEmptyWhenNoTargets()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            SetupTranslateReturns(sessionMock, new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = global::Opc.Ua.ArrayOf.Empty<BrowsePathTarget>()
            });

            var client = new SoftwareUpdateClient(
                sessionMock.Object, new NodeId("update-1", 2), NullTelemetry());

            string result = await client.ReadSoftwareVersionAsync().ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task ReadSoftwareVersionAsyncReturnsEmptyWhenReadStatusBad()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            SetupTranslateReturns(sessionMock, new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = new BrowsePathTarget[]
                {
                    new() {
                        TargetId = new ExpandedNodeId("sv-1", 2),
                        RemainingPathIndex = uint.MaxValue
                    }
                }
            });
            SetupReadReturns(sessionMock, new DataValue(Variant.Null
            , StatusCodes.BadAttributeIdInvalid));

            var client = new SoftwareUpdateClient(
                sessionMock.Object, new NodeId("update-1", 2), NullTelemetry());

            string result = await client.ReadSoftwareVersionAsync().ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task ReadSoftwareVersionAsyncReturnsEmptyWhenValueNotString()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            SetupTranslateReturns(sessionMock, new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = new BrowsePathTarget[]
                {
                    new() {
                        TargetId = new ExpandedNodeId("sv-1", 2),
                        RemainingPathIndex = uint.MaxValue
                    }
                }
            });
            SetupReadReturns(sessionMock, new DataValue(new Variant(42)
            , StatusCodes.Good));

            var client = new SoftwareUpdateClient(
                sessionMock.Object, new NodeId("update-1", 2), NullTelemetry());

            string result = await client.ReadSoftwareVersionAsync().ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(string.Empty));
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
            Mock<ISession> sessionMock, BrowsePathResult result)
        {
            sessionMock
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = new BrowsePathResult[] { result }
                });
        }

        private static void SetupReadReturns(
            Mock<ISession> sessionMock, DataValue result)
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
                    Results = new DataValue[] { result }
                });
        }

        private static ITelemetryContext NullTelemetry()
        {
            return new Mock<ITelemetryContext>().Object;
        }
    }
}
