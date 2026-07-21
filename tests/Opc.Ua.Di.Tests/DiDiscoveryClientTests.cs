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
using Opc.Ua.Di.Client;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Behavioural tests for <see cref="DiDiscoveryClient"/>. Verifies
    /// recursive browse of the Objects folder for instances whose
    /// TypeDefinition is <c>DeviceType</c>, projection into
    /// <see cref="DeviceEntry"/> records, and depth bounding.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("ClientHelpers")]
    public sealed class DiDiscoveryClientTests
    {
        [Test]
        public void EnumerateDevicesAsyncThrowsOnNullSession()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => DiDiscoveryClient.EnumerateDevicesAsync(
                    null!, NullTelemetry()))!;
            Assert.That(ex.ParamName, Is.EqualTo("session"));
        }

        [Test]
        public void EnumerateDevicesAsyncThrowsOnNullTelemetry()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => DiDiscoveryClient.EnumerateDevicesAsync(
                    sessionMock.Object, null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("telemetry"));
        }

        [Test]
        public async Task EnumerateDevicesAsyncBrowsesObjectsFolder()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            BrowseDescription? captured = null;
            SetupBrowseEmpty(sessionMock, d => captured = d);

            List<DeviceEntry> result = await ToListAsync(
                DiDiscoveryClient.EnumerateDevicesAsync(
                    sessionMock.Object, NullTelemetry())).ConfigureAwait(false);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.NodeId, Is.EqualTo(Opc.Ua.ObjectIds.ObjectsFolder));
            Assert.That(captured.BrowseDirection, Is.EqualTo(BrowseDirection.Forward));
            Assert.That(captured.ReferenceTypeId,
                Is.EqualTo(Opc.Ua.ReferenceTypeIds.HierarchicalReferences));
            Assert.That(captured.IncludeSubtypes, Is.True);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task EnumerateDevicesAsyncReturnsEmptyWhenNoReferences()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            SetupBrowseEmpty(sessionMock);

            List<DeviceEntry> result = await ToListAsync(
                DiDiscoveryClient.EnumerateDevicesAsync(
                    sessionMock.Object, NullTelemetry())).ConfigureAwait(false);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task EnumerateDevicesAsyncReturnsDeviceEntryForMatchingTypeDefinition()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            var nodeCacheMock = new Mock<INodeCache>(MockBehavior.Strict);
            sessionMock.SetupGet(s => s.NodeCache).Returns(nodeCacheMock.Object);
            ExpandedNodeId deviceTypeId = global::Opc.Ua.Di.ObjectTypeIds.DeviceType;
            var deviceNodeId = new NodeId("device-1", 2);

            ReferenceDescription deviceRef = MakeReference(
                deviceNodeId, "Device 1", deviceTypeId);
            ReferenceDescription nonDeviceRef = MakeReference(
                new NodeId("other-1", 2), "Other 1",
                new ExpandedNodeId("OtherType", 2));

            // First browse (Objects folder): returns both the device and
            // a non-device. Subsequent browses (recursion into the
            // non-device) return empty.
            SetupBrowseSequential(sessionMock,
                first: [deviceRef, nonDeviceRef],
                rest: []);
            nodeCacheMock
                .Setup(c => c.IsTypeOfAsync(
                    deviceTypeId,
                    deviceTypeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(true));
            nodeCacheMock
                .Setup(c => c.IsTypeOfAsync(
                    new ExpandedNodeId("OtherType", 2),
                    deviceTypeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(false));

            // Stub the DeviceClass property lookup so it returns
            // empty (no targets → empty deviceClass).
            SetupTranslateBrowsePathsEmpty(sessionMock);

            List<DeviceEntry> result = await ToListAsync(
                DiDiscoveryClient.EnumerateDevicesAsync(
                    sessionMock.Object, NullTelemetry())).ConfigureAwait(false);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].DeviceId, Is.EqualTo(deviceNodeId));
            Assert.That(result[0].DisplayName, Is.EqualTo("Device 1"));
            Assert.That(result[0].DeviceClass, Is.EqualTo(string.Empty));
            nodeCacheMock.VerifyAll();
        }

        [Test]
        public async Task EnumerateDevicesAsyncReturnsDeviceEntryForSubtypeTypeDefinition()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            var nodeCacheMock = new Mock<INodeCache>(MockBehavior.Strict);
            sessionMock.SetupGet(s => s.NodeCache).Returns(nodeCacheMock.Object);
            ExpandedNodeId deviceTypeId = global::Opc.Ua.Di.ObjectTypeIds.DeviceType;
            ExpandedNodeId vendorDeviceType = new("VendorDeviceType", 2);
            var deviceNodeId = new NodeId("device-subtype-1", 2);

            ReferenceDescription deviceRef = MakeReference(
                deviceNodeId, "Vendor Device", vendorDeviceType);
            ReferenceDescription nonDeviceRef = MakeReference(
                new NodeId("other-1", 2), "Other 1",
                new ExpandedNodeId("OtherType", 2));

            SetupBrowseSequential(sessionMock,
                first: [deviceRef, nonDeviceRef],
                rest: []);
            nodeCacheMock
                .Setup(c => c.IsTypeOfAsync(
                    vendorDeviceType,
                    deviceTypeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(true));
            nodeCacheMock
                .Setup(c => c.IsTypeOfAsync(
                    new ExpandedNodeId("OtherType", 2),
                    deviceTypeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(false));

            SetupTranslateBrowsePathsEmpty(sessionMock);

            List<DeviceEntry> result = await ToListAsync(
                DiDiscoveryClient.EnumerateDevicesAsync(
                    sessionMock.Object, NullTelemetry())).ConfigureAwait(false);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].DeviceId, Is.EqualTo(deviceNodeId));
            Assert.That(result[0].DisplayName, Is.EqualTo("Vendor Device"));
            Assert.That(result[0].DeviceClass, Is.EqualTo(string.Empty));
            nodeCacheMock.VerifyAll();
        }

        [Test]
        public async Task EnumerateDevicesAsyncFiltersOutNonDeviceTypes()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            var nodeCacheMock = new Mock<INodeCache>(MockBehavior.Strict);
            sessionMock.SetupGet(s => s.NodeCache).Returns(nodeCacheMock.Object);
            ReferenceDescription nonDeviceRef = MakeReference(
                new NodeId("folder-1", 2), "Folder 1",
                new ExpandedNodeId("FolderType", 2));

            SetupBrowseSequential(sessionMock,
                first: [nonDeviceRef],
                rest: []);
            nodeCacheMock
                .Setup(c => c.IsTypeOfAsync(
                    new ExpandedNodeId("FolderType", 2),
                    global::Opc.Ua.Di.ObjectTypeIds.DeviceType,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(false));

            List<DeviceEntry> result = await ToListAsync(
                DiDiscoveryClient.EnumerateDevicesAsync(
                    sessionMock.Object, NullTelemetry())).ConfigureAwait(false);

            Assert.That(result, Is.Empty);
            nodeCacheMock.VerifyAll();
        }

        [Test]
        public async Task EnumerateDevicesAsyncStopsRecursionAtMaxDepth()
        {
            // Production constant is maxDepth = 3. Each non-device
            // reference triggers recursion. We return one non-device
            // ref at every level; the helper should stop after a
            // bounded number of browse calls (depth 0..3 inclusive
            // → 4 calls before depth > maxDepth aborts).
            Mock<ISession> sessionMock = CreateSessionMock();
            var nodeCacheMock = new Mock<INodeCache>(MockBehavior.Strict);
            sessionMock.SetupGet(s => s.NodeCache).Returns(nodeCacheMock.Object);
            ReferenceDescription nonDeviceRef = MakeReference(
                new NodeId("nested", 2), "Nested",
                new ExpandedNodeId("FolderType", 2));

            int browseCalls = 0;
            sessionMock
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ViewDescription?>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => browseCalls++)
                .ReturnsAsync(() => new BrowseResponse
                {
                    Results = new BrowseResult[]
                    {
                        new() {
                            StatusCode = StatusCodes.Good,
                            References = new[] { nonDeviceRef }.ToArrayOf()
                        }
                    }.ToArrayOf()
                });
            nodeCacheMock
                .Setup(c => c.IsTypeOfAsync(
                    new ExpandedNodeId("FolderType", 2),
                    global::Opc.Ua.Di.ObjectTypeIds.DeviceType,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(false));

            List<DeviceEntry> result = await ToListAsync(
                DiDiscoveryClient.EnumerateDevicesAsync(
                    sessionMock.Object, NullTelemetry())).ConfigureAwait(false);

            Assert.That(result, Is.Empty);
            // Depth 0, 1, 2, 3 each invoke BrowseAsync once before the
            // depth > 3 guard stops further recursion.
            Assert.That(browseCalls, Is.EqualTo(4),
                "Recursion must stop at maxDepth=3 (4 invocations).");
            nodeCacheMock.Verify(
                c => c.IsTypeOfAsync(
                    new ExpandedNodeId("FolderType", 2),
                    global::Opc.Ua.Di.ObjectTypeIds.DeviceType,
                    It.IsAny<CancellationToken>()),
                Times.Exactly(4));
        }

        private static async Task<List<DeviceEntry>> ToListAsync(
            IAsyncEnumerable<DeviceEntry> source)
        {
            var list = new List<DeviceEntry>();
            await foreach (DeviceEntry entry in source)
            {
                list.Add(entry);
            }
            return list;
        }

        private static ReferenceDescription MakeReference(
            NodeId nodeId, string displayName, ExpandedNodeId typeDefinition)
        {
            return new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(nodeId),
                BrowseName = new QualifiedName(nodeId.ToString(), 2),
                DisplayName = new LocalizedText(displayName),
                TypeDefinition = typeDefinition,
                NodeClass = NodeClass.Object
            };
        }

        private static Mock<ISession> CreateSessionMock()
        {
            var mock = new Mock<ISession>();
            var nsTable = new NamespaceTable();
            nsTable.GetIndexOrAppend(global::Opc.Ua.Di.Namespaces.OpcUaDi);
            mock.SetupGet(s => s.NamespaceUris).Returns(nsTable);
            mock.SetupGet(s => s.NodeCache)
                .Returns(new Mock<INodeCache>(MockBehavior.Loose).Object);
            return mock;
        }

        private static void SetupBrowseEmpty(
            Mock<ISession> sessionMock,
            Action<BrowseDescription>? capture = null)
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
                    Results = new BrowseResult[]
                    {
                        new() {
                            StatusCode = StatusCodes.Good,
                            References = global::Opc.Ua.ArrayOf.Empty<ReferenceDescription>()
                        }
                    }.ToArrayOf()
                });
        }

        private static void SetupBrowseSequential(
            Mock<ISession> sessionMock,
            ReferenceDescription[] first,
            ReferenceDescription[] rest)
        {
            int callCount = 0;
            sessionMock
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ViewDescription?>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    ReferenceDescription[] refs =
                        callCount == 0 ? first : rest;
                    callCount++;
                    return new BrowseResponse
                    {
                        Results = new BrowseResult[]
                        {
                            new() {
                                StatusCode = StatusCodes.Good,
                                References = refs.ToArrayOf()
                            }
                        }.ToArrayOf()
                    };
                });
        }

        private static void SetupTranslateBrowsePathsEmpty(
            Mock<ISession> sessionMock)
        {
            sessionMock
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = global::Opc.Ua.ArrayOf.Empty<BrowsePathResult>()
                });
        }

        private static ITelemetryContext NullTelemetry()
        {
            return new Mock<ITelemetryContext>().Object;
        }
    }
}
