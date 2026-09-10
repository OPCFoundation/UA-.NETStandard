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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NodeSetImportModel;
using NUnit.Framework;
using Opc.Ua.Server.Nodes;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// End-to-end tests for a source-generated node manager which overlays a
    /// NodeSet2 document on its own model through
    /// <c>INodeManagerBuilder.Import</c>.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    [Category("NodeSetImport")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class NodeSetImportIntegrationTests
    {
        private string m_pkiRoot;
        private ServerFixture<NodeSetImportServer> m_fixture;
        private NodeSetImportServer m_server;
        private NodeSetImportOverlayNodeManager m_manager;
        private ushort m_namespaceIndex;
        private RequestHeader m_requestHeader;
        private SecureChannelContext m_secureChannelContext;
        private ILogger m_logger;

        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(NodeSetImportIntegrationTests),
                Guid.NewGuid().ToString("N"));

            m_fixture = new ServerFixture<NodeSetImportServer>(
                telemetry => new NodeSetImportServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
            m_manager = m_server.NodeManagerFactory.Manager;
            m_namespaceIndex = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(
                NodeSetImportTestModel.NamespaceUri);
            m_logger = NUnitTelemetryContext.Create()
                .CreateLogger<NodeSetImportIntegrationTests>();
            (m_requestHeader, m_secureChannelContext) = await m_server
                .CreateAndActivateSessionAsync(nameof(NodeSetImportIntegrationTests))
                .ConfigureAwait(false);
            m_requestHeader.Timestamp = DateTimeUtc.Now;
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            if (m_requestHeader is not null)
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                await m_server
                    .CloseSessionAsync(
                        m_secureChannelContext,
                        m_requestHeader,
                        true,
                        RequestLifetime.None)
                    .ConfigureAwait(false);
            }

            m_server?.Dispose();
            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        [Test]
        public void GeneratedManagerSuppliesTypedStatesForImportedNodes()
        {
            NodeState device = m_manager.Find(
                Id(NodeSetImportTestModel.OverlayDeviceIdentifier));
            NodeState calibrate = m_manager.Find(
                Id(NodeSetImportTestModel.OverlayDeviceCalibrateIdentifier));
            NodeState value = m_manager.Find(
                Id(NodeSetImportTestModel.ReplacedDeviceValueIdentifier));

            Assert.Multiple(() =>
            {
                Assert.That(device, Is.TypeOf<DeviceState>());
                Assert.That(calibrate, Is.TypeOf<CalibrateMethodState>());
                Assert.That(value, Is.TypeOf<CustomValueState>());
                Assert.That(device, Is.SameAs(m_manager.OverlayDevice));
                Assert.That(value, Is.SameAs(m_manager.ReplacedDeviceValue));
            });
        }

        [Test]
        public void ImportedNodesOfOneBatchLinkAcrossDocuments()
        {
            var device = (DeviceState)m_manager.Find(
                Id(NodeSetImportTestModel.OverlayDeviceIdentifier));
            NodeState calibrate = m_manager.Find(
                Id(NodeSetImportTestModel.OverlayDeviceCalibrateIdentifier));
            var children = new List<BaseInstanceState>();
            device.GetChildren(m_manager.SystemContext, children);

            Assert.Multiple(() =>
            {
                // The method comes from the second document, its parent from
                // the first one.
                Assert.That(((BaseInstanceState)calibrate).Parent, Is.SameAs(device));
                Assert.That(children, Does.Contain(calibrate));
                Assert.That(device.Calibrate, Is.SameAs(calibrate));
            });
        }

        [Test]
        public void ImportedChildReplacesTheGeneratedPlaceholder()
        {
            var device = (DeviceState)m_manager.Find(
                Id(NodeSetImportTestModel.GeneratedDeviceIdentifier));
            NodeState imported = m_manager.Find(
                Id(NodeSetImportTestModel.ReplacedDeviceValueIdentifier));
            var children = new List<BaseInstanceState>();
            device.GetChildren(m_manager.SystemContext, children);

            Assert.Multiple(() =>
            {
                Assert.That(device.Value, Is.SameAs(imported));
                Assert.That(
                    children.Count(child => child.BrowseName.Name == "Value"),
                    Is.EqualTo(1));
                // The displaced generated child is gone from the address space.
                Assert.That(
                    m_manager.Find(m_manager.DisplacedDeviceValueId),
                    Is.Null);
            });
        }

        [Test]
        public async Task ImportedNodesAreBrowsableThroughTheServerAsync()
        {
            BrowseResponse objectsFolder = await BrowseAsync(ObjectIds.ObjectsFolder)
                .ConfigureAwait(false);
            BrowseResponse overlayDevice = await BrowseAsync(
                Id(NodeSetImportTestModel.OverlayDeviceIdentifier)).ConfigureAwait(false);
            BrowseResponse generatedDevice = await BrowseAsync(
                Id(NodeSetImportTestModel.GeneratedDeviceIdentifier)).ConfigureAwait(false);

            NodeId overlayDeviceId = Id(NodeSetImportTestModel.OverlayDeviceIdentifier);
            NodeId replacedValueId = Id(NodeSetImportTestModel.ReplacedDeviceValueIdentifier);

            Assert.Multiple(() =>
            {
                // The imported root hangs off a folder another node manager
                // owns, so its reference had to be published externally.
                Assert.That(
                    Targets(objectsFolder).Any(target => target == overlayDeviceId),
                    Is.True);
                // An imported instance carries exactly the children its
                // document declares: the typed state is created empty, so the
                // model's mandatory children are not materialized behind the
                // document's back.
                Assert.That(
                    BrowseNames(overlayDevice),
                    Is.EquivalentTo(s_overlayDeviceBrowseNames));
                // The generated Device exposes the imported replacement, and
                // exposes it exactly once.
                Assert.That(
                    Targets(generatedDevice).Count(target => target == replacedValueId),
                    Is.EqualTo(1));
                Assert.That(
                    Targets(generatedDevice).Any(
                        target => target == m_manager.DisplacedDeviceValueId),
                    Is.False);
            });
        }

        [Test]
        public void GeneratedProviderRegistersTypedFactoriesWithoutDuplicates()
        {
            ArrayOf<INodeSetImportFactory> factories =
                ((INodeSetImportFactoryProvider)m_manager).GetNodeSetImportFactories();

            Assert.Multiple(() =>
            {
                Assert.That(factories.Count, Is.GreaterThan(0));
                Assert.That(
                    factories.ToArray()
                        .Select(factory =>
                            $"{factory.NodeClass}|{factory.Discriminator}|{factory.DiscriminatorId}")
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    Is.EqualTo(factories.Count));
            });

            INodeSetImportFactory deviceFactory = FindFactory(
                factories,
                NodeClass.Object,
                NodeSetImportDiscriminator.TypeDefinition,
                1000u);
            NodeState first = deviceFactory.CreateEmptyState();
            NodeState second = deviceFactory.CreateEmptyState();
            var children = new List<BaseInstanceState>();
            first.GetChildren(m_manager.SystemContext, children);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.TypeOf<DeviceState>());
                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(first.IsCreated, Is.False);
                Assert.That(first.NodeId.IsNull, Is.True);
                // Import factories create empty states: the document supplies
                // the children as flat nodes.
                Assert.That(children, Is.Empty);
            });
        }

        private INodeSetImportFactory FindFactory(
            ArrayOf<INodeSetImportFactory> factories,
            NodeClass nodeClass,
            NodeSetImportDiscriminator discriminator,
            uint identifier)
        {
            var expected = new ExpandedNodeId(identifier, NodeSetImportTestModel.NamespaceUri);
            INodeSetImportFactory match = factories.ToArray().FirstOrDefault(factory =>
                factory.NodeClass == nodeClass &&
                factory.Discriminator == discriminator &&
                factory.DiscriminatorId == expected);
            Assert.That(
                match,
                Is.Not.Null,
                $"No generated import factory for {nodeClass} {discriminator} {expected}.");
            return match;
        }

        private NodeId Id(uint identifier)
        {
            return new NodeId(identifier, m_namespaceIndex);
        }

        private static List<ReferenceDescription> References(BrowseResponse response)
        {
            var references = new List<ReferenceDescription>();
            for (int ii = 0; ii < response.Results.Count; ii++)
            {
                ArrayOf<ReferenceDescription> result = response.Results[ii].References;
                for (int jj = 0; jj < result.Count; jj++)
                {
                    references.Add(result[jj]);
                }
            }
            return references;
        }

        private static IEnumerable<NodeId> Targets(BrowseResponse response)
        {
            return References(response)
                .Select(reference => (NodeId)reference.NodeId);
        }

        private static IEnumerable<string> BrowseNames(BrowseResponse response)
        {
            return References(response)
                .Select(reference => reference.BrowseName.Name);
        }

        private async Task<BrowseResponse> BrowseAsync(NodeId nodeId)
        {
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            var template = new BrowseDescription
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };
            ArrayOf<BrowseDescription> nodesToBrowse =
                ServerFixtureUtils.CreateBrowseDescriptionCollectionFromNodeId([nodeId], template);

            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            BrowseResponse response = await services
                .BrowseAsync(requestHeader, view: null, requestedMaxReferencesPerNode: 0, nodesToBrowse)
                .ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(response.ResponseHeader, response.Results, nodesToBrowse);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                response.DiagnosticInfos,
                nodesToBrowse,
                response.ResponseHeader.StringTable,
                m_logger);

            return response;
        }

        private static readonly string[] s_overlayDeviceBrowseNames = ["Calibrate"];
    }
}
