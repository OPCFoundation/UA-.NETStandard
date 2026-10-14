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
    public class MasterNodeManagerViewOwnershipTests
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
                Assert.That(types.HasViewOwner, Is.True);
                Assert.That(factory.HasViewOwner, Is.True);

                using (var secondary = new MasterNodeManager(
                    server.CurrentInstance, fixture.Config, null, Array.Empty<INodeManager>()))
                {
                    Assert.That(types.HasViewOwner, Is.True);
                    Assert.That(factory.HasViewOwner, Is.True);
                }

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(types.HasViewOwner, Is.True);
                    Assert.That(factory.HasViewOwner, Is.True);
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
        /// Verifies that a private type added to a secondary manager image never reaches the shared table.
        /// </summary>
        [Test]
        public async Task SecondaryManagerPrivateImageDoesNotPublishAsync()
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
                    TypeTable image = GetRoutingImage(secondary);
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(image, Is.Not.SameAs(canonicalImage));
                        Assert.That(image, Is.Not.SameAs(server.CurrentInstance.TypeTree));
                    }
                    image.AddSubtype(privateType, ObjectTypeIds.BaseObjectType);
                    Assert.That(image.IsKnown(privateType), Is.True);
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
        /// Verifies that the canonical manager releases both owners on server stop, that the released table
        /// equals the final serving image after node manager teardown, and that a later manager can acquire
        /// the owners again.
        /// </summary>
        [Test]
        public async Task CanonicalDisposeReleasesOwnersAndAllowsReacquireAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            StandardServer? server = null;
            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);
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
                server = null;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(types.HasViewOwner, Is.False);
                    Assert.That(factory.HasViewOwner, Is.False);
                    // Node manager teardown removes its own types from the serving image before the owner is
                    // released, so the released table must equal that final image, whatever it contains.
                    foreach (NodeId probe in probes)
                    {
                        Assert.That(types.FindSuperType(probe), Is.EqualTo(servingImage.FindSuperType(probe)));
                        Assert.That(types.IsKnown(probe), Is.EqualTo(servingImage.IsKnown(probe)));
                    }
                }
                TypeTable.ViewOwner? again = types.TryAcquireViewOwner(() => null);
                Assert.That(again, Is.Not.Null);
                again!.Release(types);
            }
            finally
            {
                if (server != null)
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