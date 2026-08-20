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

using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Aas.Server;
using Opc.Ua.Aas.Server.Materialization;
using Opc.Ua.Export;
using Opc.Ua.Server;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Aas.Tests.Server
{
    /// <summary>
    /// Tests environment projection and document-backed providers.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class AasProjectionAndProviderTests
    {
        [Test]
        public async Task EnvironmentProjectionUsesRuntimeNodeSetLifecycleAndMaterializesBrowsableNodesAsync()
        {
            var lifecycle = new RecordingLifecycle();
            var host = new LifecycleAasEnvironmentProjectionHost(lifecycle);
            AasEnvironment environment = AasServerTestData.CreateEnvironment();

            AasEnvironmentProjectionHandle handle = await host.AddAsync(
                environment,
                new DocumentAasValueProvider(),
                new DefaultAasOperationHandler()).ConfigureAwait(false);
            AasMaterializationResult materialized = AasEnvironmentMaterializer.Materialize(environment);

            Assert.Multiple(() =>
            {
                Assert.That(handle, Is.Not.Null);
                Assert.That(lifecycle.Factory, Is.TypeOf<RuntimeNodeSetNodeManagerFactory>());
                Assert.That(ContainsNamespace(lifecycle.Factory!.NamespacesUris, Opc.Ua.Aas.V3.Namespaces.AasV3), Is.True);
                Assert.That(materialized.HasErrors, Is.False);
                Assert.That(FindNode(materialized.NodeSet, "1:AASEnvironment"), Is.Not.Null);
                Assert.That(FindNode(materialized.NodeSet, "1:" + AasServerTestData.PropertyName), Is.Not.Null);
            });
        }

        [Test]
        public async Task FolderProviderReadsJsonAndXmlDocumentsAsync()
        {
            string folder = CreateFolder();
            File.WriteAllText(
                Path.Combine(folder, "environment.json"),
                "{\"submodels\":[{\"id\":\"json\",\"modelType\":\"Submodel\"}]}");
            File.WriteAllText(
                Path.Combine(folder, "environment.xml"),
                "<environment><submodels><submodel><id>xml</id><modelType>Submodel</modelType></submodel>" +
                "</submodels></environment>");

            var provider = new FolderAasEnvironmentProvider(folder);
            List<AasEnvironment> environments = await ReadAllAsync(provider).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(environments, Has.Count.EqualTo(2));
                Assert.That(environments[0].Submodels.Value[0].Id, Is.EqualTo("json"));
                Assert.That(environments[1].Submodels.Value[0].Id, Is.EqualTo("xml"));
                Assert.That(provider.Diagnostics, Is.Empty);
            });
        }

        /// <summary>
        /// A folder provider must publish its documents in a stable order. The
        /// file system's own enumeration order is unspecified and differs between
        /// platforms, so without an explicit ordering two servers reading the same
        /// folder would build different AddressSpaces from identical input.
        /// </summary>
        [Test]
        public async Task FolderProviderOrdersDocumentsByNameRegardlessOfCreationOrderAsync()
        {
            string folder = CreateFolder();

            // Written in reverse of the expected order, so a provider that simply
            // forwards the directory listing cannot pass by accident on a file
            // system that happens to return creation order.
            File.WriteAllText(
                Path.Combine(folder, "third.json"),
                "{\"submodels\":[{\"id\":\"third\",\"modelType\":\"Submodel\"}]}");
            File.WriteAllText(
                Path.Combine(folder, "second.json"),
                "{\"submodels\":[{\"id\":\"second\",\"modelType\":\"Submodel\"}]}");
            File.WriteAllText(
                Path.Combine(folder, "first.json"),
                "{\"submodels\":[{\"id\":\"first\",\"modelType\":\"Submodel\"}]}");

            var provider = new FolderAasEnvironmentProvider(folder);
            List<AasEnvironment> environments = await ReadAllAsync(provider).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(environments, Has.Count.EqualTo(3));
                Assert.That(
                    environments.Select(e => e.Submodels.Value[0].Id),
                    Is.EqualTo(s_expectedDocumentOrder));
                Assert.That(provider.Diagnostics, Is.Empty);
            });
        }

        [Test]
        public async Task FolderProviderReportsMalformedDocumentsWithoutThrowingAsync()
        {
            string folder = CreateFolder();
            File.WriteAllText(Path.Combine(folder, "bad.json"), "{");

            var provider = new FolderAasEnvironmentProvider(folder);
            List<AasEnvironment> environments = await ReadAllAsync(provider).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(environments, Is.Empty);
                Assert.That(provider.Diagnostics.Count, Is.EqualTo(1));
                Assert.That(provider.Diagnostics[0], Does.Contain("could not be read"));
            });
        }

        [Test]
        public async Task FolderProviderWithNoDocumentsReturnsNoEnvironmentsAsync()
        {
            var provider = new FolderAasEnvironmentProvider(CreateFolder());

            List<AasEnvironment> environments = await ReadAllAsync(provider).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(environments, Is.Empty);
                Assert.That(provider.Diagnostics, Is.Empty);
            });
        }

        private static UANode? FindNode(UANodeSet nodeSet, string browseName)
        {
            if (nodeSet.Items is null)
            {
                return null;
            }

            foreach (UANode node in nodeSet.Items)
            {
                if (string.Equals(node.BrowseName, browseName, StringComparison.Ordinal))
                {
                    return node;
                }
            }
            return null;
        }

        private static bool ContainsNamespace(ArrayOf<string> namespaceUris, string namespaceUri)
        {
            foreach (string uri in namespaceUris)
            {
                if (string.Equals(uri, namespaceUri, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static async Task<List<AasEnvironment>> ReadAllAsync(FolderAasEnvironmentProvider provider)
        {
            var environments = new List<AasEnvironment>();
            await foreach (AasEnvironment environment in provider.GetEnvironmentsAsync().ConfigureAwait(false))
            {
                environments.Add(environment);
            }
            return environments;
        }

        private static string CreateFolder()
        {
            string folder = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(AasProjectionAndProviderTests),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static readonly string[] s_expectedDocumentOrder = ["first", "second", "third"];

        private sealed class RecordingLifecycle : INodeManagerLifecycle
        {
            public IAsyncNodeManagerFactory? Factory { get; private set; }

            public ArrayOf<NodeManagerRegistration> Registrations => [];

            public bool IsShuttingDown => false;

            public ValueTask<NodeManagerRegistration> AddAsync(
                IAsyncNodeManagerFactory factory,
                IOperationContext? callerContext,
                CancellationToken ct = default)
            {
                Factory = factory;
                return new ValueTask<NodeManagerRegistration>(CreateRegistration(factory.NamespacesUris));
            }

            public ValueTask<NodeManagerRegistration> AddAsync(
                INodeManagerFactory factory,
                IOperationContext? callerContext,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask<NodeManagerRegistration> ReloadAsync(
                NodeManagerRegistration registration,
                IAsyncNodeManagerFactory replacement,
                IOperationContext? callerContext,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask<NodeManagerRegistration> ReloadAsync(
                NodeManagerRegistration registration,
                INodeManagerFactory replacement,
                IOperationContext? callerContext,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask<NodeManagerRegistration> ShadowReloadAsync(
                NodeManagerRegistration registration,
                IAsyncNodeManagerFactory replacement,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask<NodeManagerRegistration> ShadowReloadAsync(
                NodeManagerRegistration registration,
                INodeManagerFactory replacement,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask<NodeManagerRegistration> ImmediateReloadAsync(
                NodeManagerRegistration registration,
                IAsyncNodeManagerFactory replacement,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask<NodeManagerRegistration> ImmediateReloadAsync(
                NodeManagerRegistration registration,
                INodeManagerFactory replacement,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask RemoveAsync(
                NodeManagerRegistration registration,
                IOperationContext? callerContext,
                CancellationToken ct = default)
            {
                return new ValueTask();
            }

            private static NodeManagerRegistration CreateRegistration(ArrayOf<string> namespaceUris)
            {
                var nodeManager = new Mock<IAsyncNodeManager>(MockBehavior.Strict);
                var uris = new string[namespaceUris.Count];
                for (int i = 0; i < namespaceUris.Count; i++)
                {
                    uris[i] = namespaceUris[i];
                }
                nodeManager.Setup(n => n.NamespaceUris).Returns(uris);
                ConstructorInfo ctor = typeof(NodeManagerRegistration).GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(Guid), typeof(long), typeof(IAsyncNodeManager) },
                    modifiers: null)!;
                return (NodeManagerRegistration)ctor.Invoke(new object[] { Guid.NewGuid(), 1L, nodeManager.Object });
            }
        }
    }
}
