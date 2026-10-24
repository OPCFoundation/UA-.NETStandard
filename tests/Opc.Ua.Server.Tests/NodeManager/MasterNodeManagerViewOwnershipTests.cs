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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies canonical and secondary type table and factory publication ownership of
    /// <see cref="MasterNodeManager"/> on a live server.
    /// </summary>
    [TestFixture]
    [Category("MasterNodeManager")]
    [NonParallelizable]
    public sealed class MasterNodeManagerViewOwnershipTests
    {
        /// <summary>
        /// Verifies that a secondary manager on a running server does not own the shared tables and that
        /// its disposal leaves the canonical publication intact.
        /// </summary>
        [Test]
        public async Task SecondaryManagerDisposeKeepsCanonicalPublicationAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                TypeTable types = server.CurrentInstance.TypeTree;
                var factory = (EncodeableFactory)server.CurrentInstance.Factory;
                Assert.That(GetRoutingImage(server.CurrentInstance.NodeManager), Is.SameAs(types));

                using (var secondary = new MasterNodeManager(
                    server.CurrentInstance, fixture.Config, null, Array.Empty<INodeManager>()))
                {
                    Assert.That(GetRoutingImage(secondary), Is.SameAs(types));
                    Assert.That(server.CurrentInstance.Factory, Is.SameAs(factory));
                }

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(server.CurrentInstance.TypeTree, Is.SameAs(types));
                    Assert.That(server.CurrentInstance.Factory, Is.SameAs(factory));
                    Assert.That(types.FindSuperType(ObjectTypeIds.FolderType),
                        Is.EqualTo(ObjectTypeIds.BaseObjectType));
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that a secondary manager cannot publish or select another preparation's private metadata.
        /// </summary>
        [Test]
        public async Task SecondaryManagerDoesNotSelectPrivateStagingMetadataAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                ushort ns = server.CurrentInstance.NamespaceUris.GetIndexOrAppend(
                    "urn:opcfoundation.org:Tests:MasterNodeManagerViewOwnership");
                var privateType = new NodeId(987654, ns);
                TypeTable canonicalImage = GetRoutingImage(server.CurrentInstance.NodeManager);
                using (var secondary = new MasterNodeManager(
                    server.CurrentInstance, fixture.Config, null, Array.Empty<INodeManager>()))
                {
                    var preparation = new ChangePreparer(server.CurrentInstance);
                    using ChangePreparer.StagingServer staging = preparation.CreateServer();
                    TypeTable image = staging.TypeTree;
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(image, Is.Not.SameAs(canonicalImage));
                        Assert.That(image, Is.Not.SameAs(server.CurrentInstance.TypeTree));
                        Assert.That(staging.DefaultSystemContext.Server, Is.SameAs(staging));
                        Assert.That(staging.DefaultSystemContext.Copy().TypeTable, Is.SameAs(image));
                    }
                    image.AddSubtype(privateType, ObjectTypeIds.BaseObjectType);
                    Assert.That(image.IsKnown(privateType), Is.True);
                    Assert.That(GetRoutingImage(secondary).IsKnown(privateType), Is.False);
                }
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(canonicalImage.IsKnown(privateType), Is.False);
                    Assert.That(server.CurrentInstance.TypeTree.IsKnown(privateType), Is.False);
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that server shutdown does not replace canonical metadata handles.
        /// </summary>
        [Test]
        public async Task CanonicalDisposePreservesMetadataHandleIdentityAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            bool stopped = false;
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                TypeTable types = server.CurrentInstance.TypeTree;
                var factory = (EncodeableFactory)server.CurrentInstance.Factory;
                TypeTable servingImage = GetRoutingImage(server.CurrentInstance.NodeManager);
                NodeId[] probes =
                [
                    ObjectTypeIds.FolderType,
                    ObjectTypeIds.BaseObjectType,
                    VariableTypeIds.BaseDataVariableType,
                    DataTypeIds.Int32
                ];
                await fixture.StopAsync().ConfigureAwait(false);
                stopped = true;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(factory.Fork(), Is.Not.SameAs(factory));
                    foreach (NodeId probe in probes)
                    {
                        Assert.That(types.FindSuperType(probe), Is.EqualTo(servingImage.FindSuperType(probe)));
                        Assert.That(types.IsKnown(probe), Is.EqualTo(servingImage.IsKnown(probe)));
                    }
                }
                NodeId added = new(987655, 1);
                types.AddSubtype(added, NodeId.Null);
                Assert.That(servingImage.IsKnown(added), Is.True);
            }
            finally
            {
                if (!stopped)
                {
                    await fixture.StopAsync().ConfigureAwait(false);
                }
            }
        }

        private static TypeTable GetRoutingImage(IMasterNodeManager manager)
        {
            FieldInfo field = typeof(MasterNodeManager).GetField(
                "m_nodeManagers", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var table = (NodeManagerRoutingTable)field.GetValue(manager)!;
            return table.Revision.TypeTree!;
        }
    }
}
