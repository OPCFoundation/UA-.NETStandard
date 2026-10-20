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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// A NodeManager published while the calling flow still holds a captured routing image, as a method
        /// callback does inside a dispatched Call, must be visible to the live existence query. The captured
        /// image keeps the generation the service call started with.
        /// </summary>
        [Test]
        public async Task IsNodeMaterializedSeesManagerPublishedInsideCapturedFlowAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            NodeManagerRoutingTable table = GetRoutingTable(master);

            NodeManagerRegistration? registration = null;
            try
            {
                using (table.Capture())
                {
                    NodeManagerRoutingTable.RoutingSnapshot captured = table.CapturedRevision;
                    registration = await m_server.NodeManagerLifecycle
                        .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1), null, timeout.Token)
                        .ConfigureAwait(false);

                    ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
                    var valueNodeId = new NodeId(kValueNodeId, ns);
                    bool materialized = await master.IsNodeMaterializedAsync(valueNodeId, timeout.Token)
                        .ConfigureAwait(false);

                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(materialized, Is.True);
                        Assert.That(table.CapturedRevision, Is.SameAs(captured));
                        Assert.That(table.Revision, Is.Not.SameAs(captured));
                    }
                }
            }
            finally
            {
                if (registration != null)
                {
                    await m_server.NodeManagerLifecycle.RemoveAsync(registration, null).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// The existence query must not leave its live routing scope on the calling flow, and must report
        /// an unknown node as not materialized.
        /// </summary>
        [Test]
        public async Task IsNodeMaterializedRestoresCapturedImageAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var master = (MasterNodeManager)m_server.CurrentInstance.NodeManager;
            NodeManagerRoutingTable table = GetRoutingTable(master);

            using (table.Capture())
            {
                NodeManagerRoutingTable.RoutingSnapshot captured = table.CapturedRevision;
                bool known = await master.IsNodeMaterializedAsync(ObjectIds.ObjectsFolder, timeout.Token)
                    .ConfigureAwait(false);
                bool unknown = await master.IsNodeMaterializedAsync(
                    new NodeId("NoSuchNodeForLiveLookup", 1), timeout.Token).ConfigureAwait(false);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(known, Is.True);
                    Assert.That(unknown, Is.False);
                    Assert.That(table.CapturedRevision, Is.SameAs(captured));
                }
            }
        }

        /// <summary>
        /// The existence query resolves through the public virtual GetManagerHandleAsync exactly once, so
        /// subclass overrides are honored.
        /// </summary>
        [Test]
        public async Task IsNodeMaterializedHonorsVirtualHandleLookupOverrideAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var master = new OverridingMasterNodeManager(m_server.CurrentInstance, m_fixture.Config);

            bool materialized = await master.IsNodeMaterializedAsync(new NodeId("Overridden", 1), timeout.Token)
                .ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(materialized, Is.True);
                Assert.That(master.LookupCount, Is.EqualTo(1));
            }
        }

        private static NodeManagerRoutingTable GetRoutingTable(MasterNodeManager master)
        {
            FieldInfo field = typeof(MasterNodeManager).GetField(
                "m_nodeManagers", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (NodeManagerRoutingTable)field.GetValue(master)!;
        }

        private sealed class OverridingMasterNodeManager : MasterNodeManager
        {
            public OverridingMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration, null, System.Array.Empty<INodeManager>())
            {
            }

            public int LookupCount { get; private set; }

            public override ValueTask<(object? handle, IAsyncNodeManager? nodeManager)> GetManagerHandleAsync(
                NodeId nodeId,
                CancellationToken cancellationToken = default)
            {
                LookupCount++;
                return new ValueTask<(object? handle, IAsyncNodeManager? nodeManager)>((new object(), null));
            }
        }
    }
}
