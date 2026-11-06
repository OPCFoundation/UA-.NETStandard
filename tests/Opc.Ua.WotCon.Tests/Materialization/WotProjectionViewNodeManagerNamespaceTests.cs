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
using Opc.Ua.Server;
using Opc.Ua.Tests;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Tests the namespace registration of <see cref="WotProjectionViewNodeManager"/>
    /// when a View is authored in a namespace the manager does not own yet.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotProjectionViewNodeManagerNamespaceTests
    {
        /// <summary>
        /// Verifies a cancelled namespace registration leaves the manager's namespace list
        /// unchanged, so the next attempt registers the namespace with the master again.
        /// </summary>
        [Test]
        public async Task CancelledNamespaceRegistrationIsRetriedByTheNextApplyAsync()
        {
            const string viewNamespaceUri = "urn:opcfoundation:test:wot-view";
            var namespaces = new NamespaceTable();
            namespaces.Append(Namespaces.WotCon);
            ushort viewNamespaceIndex = namespaces.GetIndexOrAppend(viewNamespaceUri);
            var master = new Mock<IMasterNodeManager>();
            master.Setup(value => value.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<(object? handle, IAsyncNodeManager? nodeManager)>((null, null)));
            master.Setup(value => value.RegisterNamespaceManager(viewNamespaceUri, It.IsAny<IAsyncNodeManager>()))
                .Throws(new InvalidOperationException("Registration reached the master."));
            WotProjectionViewNodeManager manager = CreateManager(namespaces, master.Object);
            var request = new WotViewProjectionRequest(
                "closure",
                "resource-xid",
                Ua.ObjectIds.Server,
                new NodeId("view", viewNamespaceIndex),
                new WotViewProjectionPlan(
                    "http://example.com/scenario/Test",
                    WotDocumentKind.ThingDescription,
                    ArrayOf<NodeId>.Empty,
                    ArrayOf<WotOrganizationalGroup>.Empty,
                    1u,
                    []));
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync().ConfigureAwait(false);

            Assert.That(
                async () => await manager.ApplyViewAsync(request, cancelled.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(manager.NamespaceUris, Has.No.Member(viewNamespaceUri));

            Assert.That(
                async () => await manager.ApplyViewAsync(request).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.EqualTo("Registration reached the master."));
            master.Verify(
                value => value.RegisterNamespaceManager(viewNamespaceUri, manager),
                Times.Once);
            Assert.That(manager.NamespaceUris, Has.No.Member(viewNamespaceUri));
        }

        private static WotProjectionViewNodeManager CreateManager(
            NamespaceTable namespaces,
            IMasterNodeManager master)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
            server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.SetupGet(value => value.NodeManager).Returns(master);
            server.SetupGet(value => value.MonitoredItemQueueFactory)
                .Returns(new MonitoredItemQueueFactory(telemetry));
            server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            return new WotProjectionViewNodeManager(
                server.Object,
                new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                telemetry.CreateLogger<WotProjectionViewNodeManagerNamespaceTests>());
        }
    }
}
