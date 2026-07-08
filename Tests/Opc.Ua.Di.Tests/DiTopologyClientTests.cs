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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Di.Client;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Behavioural tests for <see cref="DiTopologyClient"/>. Verifies
    /// that the well-known browse helpers invoke
    /// <see cref="ISession.BrowseAsync"/> with the expected parent
    /// NodeId and project the returned references into
    /// <see cref="TopologyEntry"/> records.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("ClientHelpers")]
    public sealed class DiTopologyClientTests
    {
        [Test]
        public async Task EnumerateDevicesAsyncBrowsesDeviceSet()
        {
            var sessionMock = CreateSessionMock();
            BrowseDescription? captured = null;
            SetupBrowseReturns(sessionMock, new BrowseResult
            {
                StatusCode = StatusCodes.Good,
                References = new ReferenceDescription[]
                {
                    MakeReference("device-1", "Device 1", "DeviceType"),
                    MakeReference("device-2", "Device 2", "DeviceType")
                }
            }, d => captured = d);

            var client = new DiTopologyClient(sessionMock.Object, NullTelemetry());
            List<TopologyEntry> result = await CollectAsync(client.EnumerateDevicesAsync()).ConfigureAwait(false);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.NodeId, Is.EqualTo(client.DeviceSetId));
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].DisplayName, Is.EqualTo("Device 1"));
            Assert.That(result[1].DisplayName, Is.EqualTo("Device 2"));
        }

        [Test]
        public async Task EnumerateNetworksAsyncBrowsesNetworkSet()
        {
            var sessionMock = CreateSessionMock();
            BrowseDescription? captured = null;
            SetupBrowseReturns(sessionMock, new BrowseResult
            {
                StatusCode = StatusCodes.Good,
                References = new ReferenceDescription[]
                {
                    MakeReference("net-1", "Net 1", "NetworkType")
                }
            }, d => captured = d);

            var client = new DiTopologyClient(sessionMock.Object, NullTelemetry());
            List<TopologyEntry> result = await CollectAsync(client.EnumerateNetworksAsync()).ConfigureAwait(false);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.NodeId, Is.EqualTo(client.NetworkSetId));
            Assert.That(result, Has.Count.EqualTo(1));
        }

        [Test]
        public void EnumerateChildrenAsyncThrowsOnNullParent()
        {
            var sessionMock = CreateSessionMock();
            var client = new DiTopologyClient(sessionMock.Object, NullTelemetry());

            System.ArgumentException ex = Assert.Throws<System.ArgumentException>(
                () => client.EnumerateChildrenAsync(NodeId.Null))!;
            Assert.That(ex.ParamName, Is.EqualTo("parentNodeId"));
        }

        [Test]
        public async Task EnumerateChildrenAsyncBrowsesParent()
        {
            var sessionMock = CreateSessionMock();
            BrowseDescription? captured = null;
            SetupBrowseReturns(sessionMock, new BrowseResult
            {
                StatusCode = StatusCodes.Good,
                References = new ReferenceDescription[]
                {
                    MakeReference("child-1", "Child 1", "DeviceType")
                }
            }, d => captured = d);

            var client = new DiTopologyClient(sessionMock.Object, NullTelemetry());
            var parent = new NodeId("parent-1", 2);
            List<TopologyEntry> result = await CollectAsync(client.EnumerateChildrenAsync(parent)).ConfigureAwait(false);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.NodeId, Is.EqualTo(parent));
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].NodeId, Is.EqualTo(new NodeId("child-1", 2)));
            Assert.That(result[0].BrowseName.Name, Is.EqualTo("child-1"));
        }

        [Test]
        public async Task EnumerateReturnsEmptyListWhenBrowseStatusBad()
        {
            var sessionMock = CreateSessionMock();
            SetupBrowseReturns(sessionMock, new BrowseResult
            {
                StatusCode = StatusCodes.BadNodeIdUnknown,
                References = new ReferenceDescription[]
                {
                    MakeReference("device-1", "Device 1", "DeviceType")
                }
            });

            var client = new DiTopologyClient(sessionMock.Object, NullTelemetry());
            List<TopologyEntry> result = await CollectAsync(client.EnumerateDevicesAsync()).ConfigureAwait(false);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task EnumerateReturnsEmptyListWhenNoReferences()
        {
            var sessionMock = CreateSessionMock();
            SetupBrowseReturns(sessionMock, new BrowseResult
            {
                StatusCode = StatusCodes.Good,
                References = global::Opc.Ua.ArrayOf.Empty<ReferenceDescription>()
            });

            var client = new DiTopologyClient(sessionMock.Object, NullTelemetry());
            List<TopologyEntry> result = await CollectAsync(client.EnumerateDevicesAsync()).ConfigureAwait(false);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task EnumerateReturnsEmptyListWhenResponseHasNoResults()
        {
            var sessionMock = CreateSessionMock();
            sessionMock
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ViewDescription?>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = global::Opc.Ua.ArrayOf.Empty<BrowseResult>()
                });

            var client = new DiTopologyClient(sessionMock.Object, NullTelemetry());
            List<TopologyEntry> result = await CollectAsync(client.EnumerateDevicesAsync()).ConfigureAwait(false);

            Assert.That(result, Is.Empty);
        }

        private static async Task<List<TopologyEntry>> CollectAsync(
            IAsyncEnumerable<TopologyEntry> source)
        {
            var list = new List<TopologyEntry>();
            await foreach (TopologyEntry entry in source)
            {
                list.Add(entry);
            }
            return list;
        }

        private static ReferenceDescription MakeReference(
            string nodeIdString, string displayName, string typeName)
        {
            return new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(nodeIdString, 2),
                BrowseName = new QualifiedName(nodeIdString, 2),
                DisplayName = new LocalizedText(displayName),
                TypeDefinition = new ExpandedNodeId(typeName, 2)
            };
        }

        private static Mock<ISession> CreateSessionMock()
        {
            var mock = new Mock<ISession>();
            var nsTable = new NamespaceTable();
            nsTable.GetIndexOrAppend(global::Opc.Ua.Di.Namespaces.OpcUaDi);
            mock.SetupGet(s => s.NamespaceUris).Returns(nsTable);
            return mock;
        }

        private static void SetupBrowseReturns(
            Mock<ISession> sessionMock,
            BrowseResult result,
            System.Action<BrowseDescription>? capture = null)
        {
            sessionMock
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ViewDescription?>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader?, ViewDescription?, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    (_, _, _, nodesToBrowse, _) =>
                    {
                        if (capture != null && nodesToBrowse.Count > 0)
                        {
                            capture(nodesToBrowse[0]);
                        }
                    })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResult[] { result }
                });
        }

        private static ITelemetryContext NullTelemetry()
        {
            return new Mock<ITelemetryContext>().Object;
        }
    }
}
