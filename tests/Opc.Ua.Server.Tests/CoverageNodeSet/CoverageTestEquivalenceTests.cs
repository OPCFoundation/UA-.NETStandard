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
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.CoverageNodeSet
{
    /// <summary>
    /// Cross-pipeline equivalence: the source-generation address space and the
    /// runtime-import address space must be structurally identical. Both
    /// servers load the same NodeSet bytes through two independent code paths,
    /// and this fixture walks the authored catalogue asserting node identity,
    /// NodeClass, key attributes and reference edges agree.
    /// </summary>
    [TestFixture]
    [Category("CoverageNodeSet")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class CoverageTestEquivalenceTests
    {
        private ServerFixture<CoverageTestSourceGenServer> m_sourceGenFixture;
        private ServerFixture<CoverageTestRuntimeServer> m_runtimeFixture;
        private ServerFixture<CoverageTestFluentGenServer> m_fluentGenFixture;
        private CoverageTestSourceGenServer m_sourceGen;
        private CoverageTestRuntimeServer m_runtime;
        private CoverageTestFluentGenServer m_fluentGen;
        private ushort m_sourceGenNs;
        private ushort m_runtimeNs;
        private ushort m_fluentGenNs;
        private string m_sourceGenPkiRoot;
        private string m_runtimePkiRoot;
        private string m_fluentGenPkiRoot;

        /// <summary>
        /// Starts both coverage servers.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_sourceGenPkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                Guid.NewGuid().ToString("N").Substring(0, 8));
            m_runtimePkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                Guid.NewGuid().ToString("N").Substring(0, 8));
            m_fluentGenPkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                Guid.NewGuid().ToString("N").Substring(0, 8));

            m_sourceGenFixture = new ServerFixture<CoverageTestSourceGenServer>(
                t => new CoverageTestSourceGenServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            m_runtimeFixture = new ServerFixture<CoverageTestRuntimeServer>(
                t => new CoverageTestRuntimeServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            m_fluentGenFixture = new ServerFixture<CoverageTestFluentGenServer>(
                t => new CoverageTestFluentGenServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            m_sourceGen = await m_sourceGenFixture
                .StartAsync(m_sourceGenPkiRoot).ConfigureAwait(false);
            m_runtime = await m_runtimeFixture
                .StartAsync(m_runtimePkiRoot).ConfigureAwait(false);
            m_fluentGen = await m_fluentGenFixture
                .StartAsync(m_fluentGenPkiRoot).ConfigureAwait(false);

            m_sourceGenNs = (ushort)m_sourceGen.CurrentInstance.NamespaceUris
                .GetIndex(CoverageTestCatalogue.NamespaceUri);
            m_runtimeNs = (ushort)m_runtime.CurrentInstance.NamespaceUris
                .GetIndex(CoverageTestCatalogue.NamespaceUri);
            m_fluentGenNs = (ushort)m_fluentGen.CurrentInstance.NamespaceUris
                .GetIndex(CoverageTestCatalogue.NamespaceUri);
        }

        /// <summary>
        /// Stops both servers and cleans up PKI artefacts.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            m_sourceGen?.Dispose();
            m_runtime?.Dispose();
            m_fluentGen?.Dispose();

            if (m_sourceGenFixture != null)
            {
                await m_sourceGenFixture.StopAsync().ConfigureAwait(false);
            }

            if (m_runtimeFixture != null)
            {
                await m_runtimeFixture.StopAsync().ConfigureAwait(false);
            }

            if (m_fluentGenFixture != null)
            {
                await m_fluentGenFixture.StopAsync().ConfigureAwait(false);
            }

            foreach (string root in new[] { m_sourceGenPkiRoot, m_runtimePkiRoot, m_fluentGenPkiRoot })
            {
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        /// <summary>
        /// Every catalogue node exists in both address spaces with identical
        /// BrowseName and NodeClass.
        /// </summary>
        [Test]
        [Order(100)]
        [TestCaseSource(typeof(CoverageTestCatalogue), nameof(CoverageTestCatalogue.Nodes))]
        public async Task NodeIdentityMatchesAcrossPipelinesAsync(CoverageTestCatalogue.ExpectedNode expected)
        {
            NodeState sourceGen = await FindAsync(m_sourceGen, m_sourceGenNs, expected.Id).ConfigureAwait(false);
            NodeState runtime = await FindAsync(m_runtime, m_runtimeNs, expected.Id).ConfigureAwait(false);
            NodeState fluentGen = await FindAsync(m_fluentGen, m_fluentGenNs, expected.Id).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(sourceGen, Is.Not.Null, $"source-gen missing {expected.BrowseName}");
                Assert.That(runtime, Is.Not.Null, $"runtime missing {expected.BrowseName}");
                Assert.That(fluentGen, Is.Not.Null, $"fluent-gen missing {expected.BrowseName}");
                Assert.That(sourceGen.BrowseName, Is.EqualTo(runtime.BrowseName));
                Assert.That(fluentGen.BrowseName, Is.EqualTo(runtime.BrowseName));
                Assert.That(sourceGen.NodeClass, Is.EqualTo(runtime.NodeClass));
                Assert.That(fluentGen.NodeClass, Is.EqualTo(runtime.NodeClass));
                Assert.That(sourceGen.DisplayName.Text, Is.EqualTo(runtime.DisplayName.Text));
                Assert.That(fluentGen.DisplayName.Text, Is.EqualTo(runtime.DisplayName.Text));
            });
        }

        /// <summary>
        /// Every catalogue reference edge exists in both address spaces.
        /// </summary>
        [Test]
        [Order(200)]
        [TestCaseSource(typeof(CoverageTestCatalogue), nameof(CoverageTestCatalogue.References))]
        public async Task ReferenceEdgeMatchesAcrossPipelinesAsync(CoverageTestCatalogue.ExpectedReference expected)
        {
            bool inSourceGen = await ReferenceExistsAsync(m_sourceGen, m_sourceGenNs, expected).ConfigureAwait(false);
            bool inRuntime = await ReferenceExistsAsync(m_runtime, m_runtimeNs, expected).ConfigureAwait(false);
            bool inFluentGen = await ReferenceExistsAsync(m_fluentGen, m_fluentGenNs, expected).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(inSourceGen, Is.True, "source-gen missing reference edge");
                Assert.That(inRuntime, Is.True, "runtime missing reference edge");
                Assert.That(inFluentGen, Is.True, "fluent-gen missing reference edge");
            });
        }

        private static async Task<bool> ReferenceExistsAsync(
            global::Quickstarts.ReferenceServer.ReferenceServer server,
            ushort ns,
            CoverageTestCatalogue.ExpectedReference expected)
        {
            NodeState source = await FindAsync(server, ns, expected.Source).ConfigureAwait(false);
            if (source == null)
            {
                return false;
            }

            NodeId referenceTypeId = expected.ReferenceType is >= 5001 and <= 5004
                ? new NodeId(expected.ReferenceType, ns)
                : new NodeId(expected.ReferenceType, 0);
            NodeId targetId = expected.TargetIsOwned
                ? new NodeId(expected.Target, ns)
                : new NodeId(expected.Target, 0);

            var context = server.CurrentInstance.DefaultSystemContext;
            using INodeBrowser browser = source.CreateBrowser(
                context, null, NodeId.Null, false, BrowseDirection.Both, QualifiedName.Null, null, true);

            for (IReference reference = browser.Next(); reference != null; reference = browser.Next())
            {
                if (reference.ReferenceTypeId != referenceTypeId ||
                    reference.IsInverse == expected.IsForward)
                {
                    continue;
                }

                NodeId target = ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris);
                if (target == targetId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Every secondary-namespace node exists in all three address spaces
        /// with identical BrowseName and NodeClass.
        /// </summary>
        [Test]
        [Order(150)]
        [TestCaseSource(typeof(CoverageTestCatalogue), nameof(CoverageTestCatalogue.SecondaryNodes))]
        public async Task SecondaryNodeIdentityMatchesAcrossPipelinesAsync(CoverageTestCatalogue.ExpectedNode expected)
        {
            NodeState sourceGen = await FindAsync(m_sourceGen, SecondaryNs(m_sourceGen), expected.Id).ConfigureAwait(false);
            NodeState runtime = await FindAsync(m_runtime, SecondaryNs(m_runtime), expected.Id).ConfigureAwait(false);
            NodeState fluentGen = await FindAsync(m_fluentGen, SecondaryNs(m_fluentGen), expected.Id).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(sourceGen, Is.Not.Null, $"source-gen missing secondary {expected.BrowseName}");
                Assert.That(runtime, Is.Not.Null, $"runtime missing secondary {expected.BrowseName}");
                Assert.That(fluentGen, Is.Not.Null, $"fluent-gen missing secondary {expected.BrowseName}");
                Assert.That(sourceGen.BrowseName.Name, Is.EqualTo(expected.BrowseName));
                Assert.That(runtime.BrowseName.Name, Is.EqualTo(expected.BrowseName));
                Assert.That(fluentGen.BrowseName.Name, Is.EqualTo(expected.BrowseName));
                Assert.That(sourceGen.NodeClass, Is.EqualTo(expected.NodeClass));
                Assert.That(runtime.NodeClass, Is.EqualTo(expected.NodeClass));
                Assert.That(fluentGen.NodeClass, Is.EqualTo(expected.NodeClass));
            });
        }

        private static ushort SecondaryNs(global::Quickstarts.ReferenceServer.ReferenceServer server)
        {
            return (ushort)server.CurrentInstance.NamespaceUris
                .GetIndex(CoverageTestCatalogue.SecondaryNamespaceUri);
        }

        private static ValueTask<NodeState> FindAsync(
            global::Quickstarts.ReferenceServer.ReferenceServer server,
            ushort ns,
            uint id)
        {
            return server.CurrentInstance.NodeManager
                .FindNodeInAddressSpaceAsync(new NodeId(id, ns));
        }
    }
}
