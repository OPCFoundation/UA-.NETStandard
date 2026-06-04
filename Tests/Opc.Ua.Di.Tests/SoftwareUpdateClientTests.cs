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
            var sessionMock = CreateSessionMock();
            SetupTranslateReturns(sessionMock, new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = new BrowsePathTarget[]
                {
                    new BrowsePathTarget
                    {
                        TargetId = new ExpandedNodeId("sv-1", 2),
                        RemainingPathIndex = uint.MaxValue
                    }
                }
            });
            SetupReadReturns(sessionMock, new DataValue(new Variant("1.2.3")
            , StatusCodes.Good));

            var client = new SoftwareUpdateClient(
                sessionMock.Object, new NodeId("update-1", 2), NullTelemetry());

            string result = await client.ReadSoftwareVersionAsync();
            Assert.That(result, Is.EqualTo("1.2.3"));
        }

        [Test]
        public async Task ReadSoftwareVersionAsyncReturnsEmptyWhenBrowsePathBad()
        {
            var sessionMock = CreateSessionMock();
            SetupTranslateReturns(sessionMock, new BrowsePathResult
            {
                StatusCode = StatusCodes.BadNoMatch,
                Targets = global::Opc.Ua.ArrayOf.Empty<BrowsePathTarget>()
            });

            var client = new SoftwareUpdateClient(
                sessionMock.Object, new NodeId("update-1", 2), NullTelemetry());

            string result = await client.ReadSoftwareVersionAsync();
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task ReadSoftwareVersionAsyncReturnsEmptyWhenNoTargets()
        {
            var sessionMock = CreateSessionMock();
            SetupTranslateReturns(sessionMock, new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = global::Opc.Ua.ArrayOf.Empty<BrowsePathTarget>()
            });

            var client = new SoftwareUpdateClient(
                sessionMock.Object, new NodeId("update-1", 2), NullTelemetry());

            string result = await client.ReadSoftwareVersionAsync();
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task ReadSoftwareVersionAsyncReturnsEmptyWhenReadStatusBad()
        {
            var sessionMock = CreateSessionMock();
            SetupTranslateReturns(sessionMock, new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = new BrowsePathTarget[]
                {
                    new BrowsePathTarget
                    {
                        TargetId = new ExpandedNodeId("sv-1", 2),
                        RemainingPathIndex = uint.MaxValue
                    }
                }
            });
            SetupReadReturns(sessionMock, new DataValue(Variant.Null
            , StatusCodes.BadAttributeIdInvalid));

            var client = new SoftwareUpdateClient(
                sessionMock.Object, new NodeId("update-1", 2), NullTelemetry());

            string result = await client.ReadSoftwareVersionAsync();
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task ReadSoftwareVersionAsyncReturnsEmptyWhenValueNotString()
        {
            var sessionMock = CreateSessionMock();
            SetupTranslateReturns(sessionMock, new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = new BrowsePathTarget[]
                {
                    new BrowsePathTarget
                    {
                        TargetId = new ExpandedNodeId("sv-1", 2),
                        RemainingPathIndex = uint.MaxValue
                    }
                }
            });
            SetupReadReturns(sessionMock, new DataValue(new Variant((int)42)
            , StatusCodes.Good));

            var client = new SoftwareUpdateClient(
                sessionMock.Object, new NodeId("update-1", 2), NullTelemetry());

            string result = await client.ReadSoftwareVersionAsync();
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
