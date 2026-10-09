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
