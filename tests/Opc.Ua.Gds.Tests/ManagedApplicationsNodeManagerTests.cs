/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Tests for <see cref="DefaultManagedApplicationsNodeManager"/>.
    /// </summary>
    [TestFixture]
    [Category("ManagedApplications")]
    [NonParallelizable]
    public sealed class ManagedApplicationsNodeManagerTests : GdsTestFixture
    {
        /// <summary>
        /// OPC 10000-12 §7.10.14: ApplicationUri and ApplicationType are
        /// mandatory children of ApplicationConfigurationType; OPC 10000-5
        /// §6.3.1: namespace 0 is reserved for the OPC UA namespace.
        /// </summary>
        [Test]
        public async Task ApplicationNodesHavePropertiesAndOwnNamespaceAsync()
        {
            var store = new InMemoryConfigurationDataStore();
            store.AddApplication(new ManagedApplicationInfo
            {
                ApplicationUri = "urn:a/b",
                ProductUri = "urn:product",
                ApplicationType = ApplicationType.Server
            });
            store.AddApplication(new ManagedApplicationInfo
            {
                ApplicationUri = "urn:a:b",
                ApplicationType = ApplicationType.Client,
                Enabled = false
            });

            using var nodeManager = new DefaultManagedApplicationsNodeManager(
                ReferenceServer.CurrentInstance,
                ServerFixture.Config,
                store);
            await nodeManager
                .CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                .ConfigureAwait(false);

            Assert.That(nodeManager.ApplicationNodes, Has.Count.EqualTo(2));
            ApplicationConfigurationState server = nodeManager.ApplicationNodes["urn:a/b"];
            ApplicationConfigurationState client = nodeManager.ApplicationNodes["urn:a:b"];
            Assert.That(server.NodeId, Is.Not.EqualTo(client.NodeId));
            Assert.That(server.NodeId.NamespaceIndex, Is.Not.Zero);
            Assert.That(server.ApplicationUri, Is.Not.Null);
            Assert.That(server.ApplicationUri!.Value, Is.EqualTo("urn:a/b"));
            Assert.That(server.ProductUri!.Value, Is.EqualTo("urn:product"));
            Assert.That(server.ApplicationType!.Value, Is.EqualTo(ApplicationType.Server));
            Assert.That(client.Enabled!.Value, Is.False);
        }
    }
}
