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
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test <see cref="MasterNodeManager"/>
    /// </summary>
    [TestFixture]
    [Category("MasterNodeManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class MasterNodeManagerTests
    {
        /// <summary>
        /// Test for registering a namespace manager for a namespace
        /// not contained in the server's namespace table
        /// </summary>
        [Test]
        public async Task RegisterNamespaceManagerNewNamespaceAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var fixture = new ServerFixture<StandardServer>();

            try
            {
                //-- Arrange
                const string ns = "http://test.org/UA/Data/";

                var nodeManager = new Mock<INodeManager>();
                nodeManager.Setup(x => x.NamespaceUris).Returns([]);

                //-- Act
                StandardServer server = await fixture.StartAsync()
                    .ConfigureAwait(false);
                var sut = new MasterNodeManager(
                    server.CurrentInstance,
                    fixture.Config,
                    null,
                    nodeManager.Object);
                sut.RegisterNamespaceManager(ns, nodeManager.Object);

                //-- Assert
                Assert.Contains(ns, server.CurrentInstance.NamespaceUris.ToArray());
                IAsyncNodeManager[] registeredManagers = [.. sut.NamespaceManagers[
                    server.CurrentInstance.NamespaceUris.GetIndex(ns)
                ]];
                Assert.AreEqual(1, registeredManagers.Length);
                Assert.Contains(nodeManager.Object, registeredManagers.Select(m => m.SyncNodeManager).ToList());
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Test for registering a namespace manager for a namespace
        /// contained in the server's namespace table
        /// </summary>
        [Test]
        public async Task RegisterNamespaceManagerExistingNamespaceAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var fixture = new ServerFixture<StandardServer>();

            try
            {
                //-- Arrange
                const string ns = "http://test.org/UA/Data/";
                var namespaceUris = new List<string> { ns };

                var originalNodeManager = new Mock<INodeManager>();
                originalNodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                var newNodeManager = new Mock<INodeManager>();
                newNodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                //-- Act
                StandardServer server = await fixture.StartAsync()
                    .ConfigureAwait(false);
                var sut = new MasterNodeManager(
                    server.CurrentInstance,
                    fixture.Config,
                    null,
                    originalNodeManager.Object);
                sut.RegisterNamespaceManager(ns, newNodeManager.Object);

                //-- Assert
                Assert.Contains(ns, server.CurrentInstance.NamespaceUris.ToArray());
                IAsyncNodeManager[] registeredManagers = [.. sut.NamespaceManagers[
                    server.CurrentInstance.NamespaceUris.GetIndex(ns)
                ]];
                Assert.AreEqual(2, registeredManagers.Length);
                Assert.Contains(originalNodeManager.Object, registeredManagers.Select(m => m.SyncNodeManager).ToList());
                Assert.Contains(newNodeManager.Object, registeredManagers.Select(m => m.SyncNodeManager).ToList());
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Test for unregistering a namespace manager which had previously
        /// been registered
        /// </summary>
        [Test]
        [TestCase(3, 0)]
        [TestCase(3, 1)]
        [TestCase(3, 2)]
        public async Task UnregisterNamespaceManagerInCollectionAsync(
            int totalManagers,
            int indexToRemove)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var fixture = new ServerFixture<StandardServer>();

            try
            {
                //-- Arrange
                const string ns = "http://test.org/UA/Data/";
                var namespaceUris = new List<string> { ns };

                var additionalManagers = new INodeManager[totalManagers];
                for (int ii = 0; ii < totalManagers; ii++)
                {
                    var nodeManager = new Mock<INodeManager>();
                    nodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                    additionalManagers[ii] = nodeManager.Object;
                }

                INodeManager nodeManagerToRemove = additionalManagers[indexToRemove];

                //-- Act
                StandardServer server = await fixture.StartAsync()
                    .ConfigureAwait(false);
                var sut = new MasterNodeManager(
                    server.CurrentInstance,
                    fixture.Config,
                    null,
                    additionalManagers);
                bool result = sut.UnregisterNamespaceManager(ns, nodeManagerToRemove);

                //-- Assert
                Assert.IsTrue(result);
                Assert.Contains(ns, server.CurrentInstance.NamespaceUris.ToArray());
                IAsyncNodeManager[] registeredManagers = [.. sut.NamespaceManagers[
                    server.CurrentInstance.NamespaceUris.GetIndex(ns)
                ]];
                Assert.AreEqual(totalManagers - 1, registeredManagers.Length);
                NUnit.Framework.Assert.That(registeredManagers.Select(m => m.SyncNodeManager).ToList(), Has.No.Member(nodeManagerToRemove));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Test for unregistering a namespace manager which had not
        /// previously been registered
        /// </summary>
        [Test]
        public async Task UnregisterNamespaceManagerNotInCollectionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var fixture = new ServerFixture<StandardServer>();

            try
            {
                //-- Arrange
                const string ns = "http://test.org/UA/Data/";
                var namespaceUris = new List<string> { ns };

                var firstNodeManager = new Mock<INodeManager>();
                firstNodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                var secondNodeManager = new Mock<INodeManager>();
                secondNodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                var thirdNodeManager = new Mock<INodeManager>();
                thirdNodeManager.Setup(x => x.NamespaceUris).Returns(namespaceUris);

                //-- Act
                StandardServer server = await fixture.StartAsync()
                    .ConfigureAwait(false);
                var sut = new MasterNodeManager(
                    server.CurrentInstance,
                    fixture.Config,
                    null,
                    firstNodeManager.Object,
                    // Do not add the secondNodeManager to additionalManagers
                    thirdNodeManager.Object);
                bool result = sut.UnregisterNamespaceManager(ns, secondNodeManager.Object);

                //-- Assert
                Assert.IsFalse(result);
                Assert.Contains(ns, server.CurrentInstance.NamespaceUris.ToArray());
                IAsyncNodeManager[] registeredManagers = [.. sut.NamespaceManagers[
                    server.CurrentInstance.NamespaceUris.GetIndex(ns)
                ]];
                Assert.AreEqual(2, registeredManagers.Length);
                Assert.Contains(firstNodeManager.Object, registeredManagers.Select(m => m.SyncNodeManager).ToList());
                Assert.Contains(thirdNodeManager.Object, registeredManagers.Select(m => m.SyncNodeManager).ToList());
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Test for unregistering a namespace manager which had not
        /// previously been registered and is for a namespace
        /// which is unknown by the server
        /// </summary>
        [Test]
        public async Task UnregisterNamespaceManagerUnknownNamespaceAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var fixture = new ServerFixture<StandardServer>();

            try
            {
                //-- Arrange
                const string originalNs = "http://test.org/UA/Data/";

                var originalNodeManager = new Mock<INodeManager>();
                originalNodeManager.Setup(x => x.NamespaceUris).Returns([originalNs]);

                const string newNs = "http://test.org/UA/Data/Instance";
                var newNodeManager = new Mock<INodeManager>();
                newNodeManager.Setup(x => x.NamespaceUris).Returns([originalNs, newNs]);

                //-- Act
                StandardServer server = await fixture.StartAsync()
                    .ConfigureAwait(false);
                var sut = new MasterNodeManager(
                    server.CurrentInstance,
                    fixture.Config,
                    null,
                    originalNodeManager.Object);
                bool result = sut.UnregisterNamespaceManager(newNs, newNodeManager.Object);

                //-- Assert
                Assert.IsFalse(result);
                NUnit.Framework.Assert
                    .That(server.CurrentInstance.NamespaceUris.ToArray(), Has.No.Member(newNs));

                Assert.Contains(originalNs, server.CurrentInstance.NamespaceUris.ToArray());
                IAsyncNodeManager[] registeredManagers = [.. sut.NamespaceManagers[
                    server.CurrentInstance.NamespaceUris.GetIndex(originalNs)
                ]];
                Assert.AreEqual(1, registeredManagers.Length);
                Assert.Contains(originalNodeManager.Object, registeredManagers.Select(m => m.SyncNodeManager).ToList());
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that Browse returns Bad_NoContinuationPoints without a
        /// continuation point when the continuation-point quota is exhausted.
        /// </summary>
        [Test]
        public async Task BrowseWhenNoContinuationPointCanBeAssignedReturnsBadNoContinuationPointsWithoutContinuationPointAsync()
        {
            var fixture = new ServerFixture<StandardServer>();

            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                MasterNodeManager sut = server.CurrentInstance.NodeManager;
                OperationContext ctx = CreateContextWithContinuationStore();
                uint originalLimit = GetMaxBrowseContinuationPointsPerBrowse(sut);

                try
                {
                    SetMaxBrowseContinuationPointsPerBrowse(sut, 0u);

                    var nodeToBrowse = new BrowseDescription
                    {
                        NodeId = ObjectIds.RootFolder,
                        BrowseDirection = BrowseDirection.Forward
                    };

                    (BrowseResultCollection results, _) = await sut.BrowseAsync(
                        ctx,
                        new ViewDescription(),
                        1u,
                        new BrowseDescriptionCollection { nodeToBrowse },
                        CancellationToken.None).ConfigureAwait(false);

                    Assert.AreEqual(1, results.Count);
                    Assert.AreEqual(StatusCodes.BadNoContinuationPoints, (uint)results[0].StatusCode);
                    NUnit.Framework.Assert.That(
                        results[0].ContinuationPoint == null || results[0].ContinuationPoint.Length == 0,
                        Is.True);
                }
                finally
                {
                    SetMaxBrowseContinuationPointsPerBrowse(sut, originalLimit);
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that BrowseNext returns Bad_NoContinuationPoints without a
        /// continuation point when the continuation-point quota is exhausted.
        /// </summary>
        [Test]
        public async Task BrowseNextWhenNoContinuationPointCanBeAssignedReturnsBadNoContinuationPointsWithoutContinuationPointAsync()
        {
            var fixture = new ServerFixture<StandardServer>();

            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                MasterNodeManager sut = server.CurrentInstance.NodeManager;
                OperationContext ctx = CreateContextWithContinuationStore();
                uint originalLimit = GetMaxBrowseContinuationPointsPerBrowse(sut);

                try
                {
                    var nodeToBrowse = new BrowseDescription
                    {
                        NodeId = ObjectIds.RootFolder,
                        BrowseDirection = BrowseDirection.Forward
                    };

                    (BrowseResultCollection firstResults, _) = await sut.BrowseAsync(
                        ctx,
                        new ViewDescription(),
                        1u,
                        new BrowseDescriptionCollection { nodeToBrowse },
                        CancellationToken.None).ConfigureAwait(false);

                    Assert.AreEqual(1, firstResults.Count);
                    Assert.AreEqual(StatusCodes.Good, (uint)firstResults[0].StatusCode);
                    NUnit.Framework.Assert.That(
                        firstResults[0].ContinuationPoint != null && firstResults[0].ContinuationPoint.Length > 0,
                        Is.True);

                    (BrowseResultCollection nextResults, _) = await sut.BrowseNextAsync(
                        ctx,
                        false,
                        new ByteStringCollection { firstResults[0].ContinuationPoint },
                        CancellationToken.None).ConfigureAwait(false);

                    Assert.AreEqual(1, nextResults.Count);
                    Assert.AreEqual(StatusCodes.Good, (uint)nextResults[0].StatusCode);
                    NUnit.Framework.Assert.That(
                        nextResults[0].ContinuationPoint != null && nextResults[0].ContinuationPoint.Length > 0,
                        Is.True);

                    SetMaxBrowseContinuationPointsPerBrowse(sut, 0u);

                    (BrowseResultCollection finalResults, _) = await sut.BrowseNextAsync(
                        ctx,
                        false,
                        new ByteStringCollection { nextResults[0].ContinuationPoint },
                        CancellationToken.None).ConfigureAwait(false);

                    Assert.AreEqual(1, finalResults.Count);
                    Assert.AreEqual(StatusCodes.BadNoContinuationPoints, (uint)finalResults[0].StatusCode);
                    NUnit.Framework.Assert.That(
                        finalResults[0].ContinuationPoint == null || finalResults[0].ContinuationPoint.Length == 0,
                        Is.True);
                }
                finally
                {
                    SetMaxBrowseContinuationPointsPerBrowse(sut, originalLimit);
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private static OperationContext CreateContextWithContinuationStore()
        {
            var continuationPoints = new Dictionary<string, ContinuationPoint>();
            var session = new Mock<ISession>();
            session.Setup(s => s.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
            session.Setup(s => s.PreferredLocales).Returns([]);
            session
                .Setup(s => s.SaveContinuationPoint(It.IsAny<ContinuationPoint>()))
                .Callback<ContinuationPoint>(cp =>
                {
                    continuationPoints[ToContinuationPointKey(cp.Id.ToByteArray())] = cp;
                });
            session
                .Setup(s => s.RestoreContinuationPoint(It.IsAny<byte[]>()))
                .Returns<byte[]>(cpBytes =>
                {
                    string key = ToContinuationPointKey(cpBytes);
                    if (continuationPoints.TryGetValue(key, out ContinuationPoint cp))
                    {
                        continuationPoints.Remove(key);
                        return cp;
                    }

                    return null;
                });

            return new OperationContext(
                new RequestHeader(), null, RequestType.Read, session.Object);
        }

        private static void SetMaxBrowseContinuationPointsPerBrowse(MasterNodeManager sut, uint value)
        {
            FieldInfo field = typeof(MasterNodeManager).GetField(
                "m_maxContinuationPointsPerBrowse",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field);
            field.SetValue(sut, value);
        }

        private static uint GetMaxBrowseContinuationPointsPerBrowse(MasterNodeManager sut)
        {
            FieldInfo field = typeof(MasterNodeManager).GetField(
                "m_maxContinuationPointsPerBrowse",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field);
            return (uint)field.GetValue(sut);
        }

        private static string ToContinuationPointKey(byte[] continuationPoint)
        {
            return Convert.ToBase64String(continuationPoint);
        }
    }
}
