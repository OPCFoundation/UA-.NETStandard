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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Drives <see cref="OpenUsdConnector"/> discovery and §5.7 binding-source
    /// resolution against an in-memory address space.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdConnectorDiscoveryTests
    {
        private FakeAddressSpace m_space = null!;
        private Mock<ISession> m_session = null!;
        private MockUsdSink m_sink = null!;

        [SetUp]
        public void SetUp()
        {
            m_space = new FakeAddressSpace();
            m_session = FakeSession.Create(m_space);
            m_sink = new MockUsdSink();
        }

        private OpenUsdConnector Connector()
        {
            return new OpenUsdConnector(m_session.Object, m_sink);
        }

        private NodeId AddFacility()
        {
            return m_space.AddObject(Opc.Ua.ObjectIds.Server, "OpenUSD",
                browseNameNamespace: m_space.OpenUsdNamespaceIndex);
        }

        private NodeId AddRegistry()
        {
            return m_space.AddObject(AddFacility(), "Representations");
        }

        private NodeId RepresentationTypeId
            => new(OpenUsdModel.RepresentationTypeId, m_space.OpenUsdNamespaceIndex);

        private NodeId ValueChangeBindingTypeId
            => new(OpenUsdModel.ValueChangeBindingTypeId, m_space.OpenUsdNamespaceIndex);

        private NodeId ComponentBindingTypeId
            => new(OpenUsdModel.ComponentBindingTypeId, m_space.OpenUsdNamespaceIndex);

        [Test]
        public async Task DiscoverRepresentationReturnsNullWhenTheFacilityHasNoRegistryAsync()
        {
            AddFacility();
            OpenUsdConnector connector = Connector();

            OpenUsdConnector.RepresentationInfo? info =
                await connector.DiscoverRepresentationAsync(CancellationToken.None);

            Assert.That(info, Is.Null);
        }

        [Test]
        public async Task DiscoverAllRepresentationsReturnsEmptyWhenTheFacilityHasNoRegistryAsync()
        {
            AddFacility();
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

            Assert.That(reps, Is.Empty);
        }

        [Test]
        public async Task DiscoverRepresentationFallsBackToTheConventionalRootWhenNotOrganizedAsync()
        {
            OpenUsdConnector connector = Connector();

            OpenUsdConnector.RepresentationInfo? info =
                await connector.DiscoverRepresentationAsync(CancellationToken.None);

            Assert.That(info, Is.Null);
            Assert.That(m_space.BrowseCount, Is.GreaterThan(0));
        }

        [Test]
        public async Task DiscoverRepresentationIgnoresRegistryChildrenOfAForeignTypeAsync()
        {
            NodeId registry = AddRegistry();
            m_space.AddObject(registry, "NotARepresentation", new NodeId(9999));
            OpenUsdConnector connector = Connector();

            OpenUsdConnector.RepresentationInfo? info =
                await connector.DiscoverRepresentationAsync(CancellationToken.None);

            Assert.That(info, Is.Null);
        }

        [Test]
        public async Task DiscoverAllRepresentationsReadsStageMetadataAsync()
        {
            NodeId registry = AddRegistry();
            NodeId rep = m_space.AddObject(registry, "Robot", RepresentationTypeId);
            NodeId stage = m_space.AddObject(rep, "Stage-Object");
            m_space.AddVariable(rep, "PrimPath", new Variant("/World/Robot"));
            m_space.AddVariable(rep, "Stage", new Variant(stage));
            m_space.AddVariable(stage, "RootLayerIdentifier", new Variant("robot.usda"));
            m_space.AddVariable(stage, "RootLayerDigest", new Variant(new ByteString(new byte[] { 1, 2, 3 })));
            m_space.AddVariable(stage, "RootLayerDigestAlgorithm", new Variant(1));
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

            Assert.That(reps, Has.Count.EqualTo(1));
            Assert.That(reps[0].PrimPath, Is.EqualTo("/World/Robot"));
            Assert.That(reps[0].StageNodeId, Is.EqualTo(stage));
            Assert.That(reps[0].RootLayerIdentifier, Is.EqualTo("robot.usda"));
            Assert.That(reps[0].RootLayerDigest.Length, Is.EqualTo(3));
            Assert.That(reps[0].DigestAlgorithm, Is.EqualTo(OpenUsdDigestAlgorithm.Sha256));
        }

        [Test]
        public async Task DiscoverAllRepresentationsReadsComponentBindingsAsync()
        {
            NodeId registry = AddRegistry();
            NodeId rep = m_space.AddObject(registry, "Line", RepresentationTypeId);
            m_space.AddVariable(rep, "PrimPath", new Variant("/World/Line"));
            NodeId component = m_space.AddObject(rep, "Cells", ComponentBindingTypeId);
            m_space.AddVariable(component, "Cardinality", new Variant(1));
            m_space.AddVariable(component, "CompositionArc", new Variant(2));
            m_space.AddVariable(component, "TargetPrimPath", new Variant("/World/Line/Cells"));
            m_space.AddVariable(component, "ComponentAssetReference", new Variant("cell.usda"));
            m_space.AddVariable(component, "Dynamic", new Variant(true));
            m_space.AddVariable(component, "Enabled", new Variant(false));
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

            Assert.That(reps, Has.Count.EqualTo(1));
            Assert.That(reps[0].Components, Has.Count.EqualTo(1));
            OpenUsdConnector.ComponentInfo c = reps[0].Components[0];
            Assert.That(c.Cardinality, Is.EqualTo(OpenUsdCardinality.Many));
            Assert.That(c.Arc, Is.EqualTo(OpenUsdCompositionArc.Payload));
            Assert.That(c.TargetPrimPath, Is.EqualTo("/World/Line/Cells"));
            Assert.That(c.ComponentAssetReference, Is.EqualTo("cell.usda"));
            Assert.That(c.Dynamic, Is.True);
            Assert.That(c.Enabled, Is.False);
        }

        [Test]
        public async Task DiscoverAllRepresentationsResolvesTheBindingSourceBySemanticIdAsync()
        {
            NodeId machine = m_space.AddObject(Opc.Ua.ObjectIds.Server, "Machine");
            NodeId speed = m_space.AddVariable(machine, "Speed", new Variant(1.0));
            m_space.AddReference(speed, ReferenceTypeIds.HasDictionaryEntry, m_space.NewNodeId(), "SEM-SPEED");
            NodeId rep = AddRepresentationWithBinding(machine, out NodeId binding);
            m_space.AddVariable(binding, "SourceSemanticId", new Variant("SEM-SPEED"));
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

            Assert.That(rep.IsNull, Is.False);
            Assert.That(reps, Has.Count.EqualTo(1));
            Assert.That(reps[0].Bindings, Has.Count.EqualTo(1));
            Assert.That(reps[0].Bindings[0].SourceNodeId, Is.EqualTo(speed));
        }

        [Test]
        public async Task DiscoverAllRepresentationsLeavesTheBindingUnresolvedWhenNoSemanticIdMatchesAsync()
        {
            NodeId machine = m_space.AddObject(Opc.Ua.ObjectIds.Server, "Machine");
            NodeId speed = m_space.AddVariable(machine, "Speed", new Variant(1.0));
            m_space.AddReference(speed, ReferenceTypeIds.HasDictionaryEntry, m_space.NewNodeId(), "SEM-OTHER");
            AddRepresentationWithBinding(machine, out NodeId binding);
            m_space.AddVariable(binding, "SourceSemanticId", new Variant("SEM-SPEED"));
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

            Assert.That(reps[0].Bindings[0].SourceNodeId.IsNull, Is.True);
        }

        [Test]
        public void DiscoverAllRepresentationsThrowsWhenTheSemanticIdMatchesMoreThanOneVariable()
        {
            NodeId machine = m_space.AddObject(Opc.Ua.ObjectIds.Server, "Machine");
            NodeId first = m_space.AddVariable(machine, "SpeedA", new Variant(1.0));
            NodeId second = m_space.AddVariable(machine, "SpeedB", new Variant(2.0));
            m_space.AddReference(first, ReferenceTypeIds.HasDictionaryEntry, m_space.NewNodeId(), "SEM-SPEED");
            m_space.AddReference(second, ReferenceTypeIds.HasDictionaryEntry, m_space.NewNodeId(), "SEM-SPEED");
            AddRepresentationWithBinding(machine, out NodeId binding);
            m_space.AddVariable(binding, "SourceSemanticId", new Variant("SEM-SPEED"));
            OpenUsdConnector connector = Connector();

            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(
                () => connector.DiscoverAllRepresentationsAsync(CancellationToken.None));

            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadTooManyMatches));
        }

        [Test]
        public async Task DiscoverAllRepresentationsSkipsResolutionWhenTheRepresentationHasNoAggregatingParentAsync()
        {
            NodeId registry = AddRegistry();
            NodeId rep = m_space.AddObject(registry, "Robot", RepresentationTypeId);
            m_space.AddVariable(rep, "PrimPath", new Variant("/World/Robot"));
            NodeId binding = m_space.AddObject(rep, "Speed", ValueChangeBindingTypeId);
            m_space.AddVariable(binding, "SourceSemanticId", new Variant("SEM-SPEED"));
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

            Assert.That(reps[0].Bindings[0].SourceNodeId.IsNull, Is.True);
        }

        [Test]
        public async Task DiscoverAllRepresentationsResolvesTheBindingSourceByBrowsePathAsync()
        {
            NodeId machine = m_space.AddObject(Opc.Ua.ObjectIds.Server, "Machine");
            NodeId speed = m_space.AddVariable(machine, "Speed", new Variant(1.0));
            AddRepresentationWithBinding(machine, out NodeId binding);
            AddSourceBrowsePath(binding);
            SetupTranslate(StatusCodes.Good, new BrowsePathTarget
            {
                TargetId = new ExpandedNodeId(speed),
                RemainingPathIndex = uint.MaxValue
            });
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

            Assert.That(reps[0].Bindings[0].SourceNodeId, Is.EqualTo(speed));
        }

        [Test]
        public async Task DiscoverAllRepresentationsIgnoresBrowsePathTargetsWithARemainingPathIndexAsync()
        {
            NodeId machine = m_space.AddObject(Opc.Ua.ObjectIds.Server, "Machine");
            NodeId speed = m_space.AddVariable(machine, "Speed", new Variant(1.0));
            AddRepresentationWithBinding(machine, out NodeId binding);
            AddSourceBrowsePath(binding);
            SetupTranslate(StatusCodes.Good, new BrowsePathTarget
            {
                TargetId = new ExpandedNodeId(speed),
                RemainingPathIndex = 0
            });
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

            Assert.That(reps[0].Bindings[0].SourceNodeId.IsNull, Is.True);
        }

        [Test]
        public async Task DiscoverAllRepresentationsIgnoresBrowsePathTargetsThatAreNotVariablesAsync()
        {
            NodeId machine = m_space.AddObject(Opc.Ua.ObjectIds.Server, "Machine");
            NodeId folder = m_space.AddObject(machine, "Folder");
            AddRepresentationWithBinding(machine, out NodeId binding);
            AddSourceBrowsePath(binding);
            SetupTranslate(StatusCodes.Good, new BrowsePathTarget
            {
                TargetId = new ExpandedNodeId(folder),
                RemainingPathIndex = uint.MaxValue
            });
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

            Assert.That(reps[0].Bindings[0].SourceNodeId.IsNull, Is.True);
        }

        [Test]
        public async Task DiscoverAllRepresentationsIgnoresATargetThatIsUnknownToTheServerAsync()
        {
            NodeId machine = m_space.AddObject(Opc.Ua.ObjectIds.Server, "Machine");
            AddRepresentationWithBinding(machine, out NodeId binding);
            AddSourceBrowsePath(binding);
            SetupTranslate(StatusCodes.Good, new BrowsePathTarget
            {
                TargetId = new ExpandedNodeId(new NodeId(424242u)),
                RemainingPathIndex = uint.MaxValue
            });
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

            Assert.That(reps[0].Bindings[0].SourceNodeId.IsNull, Is.True);
        }

        [Test]
        public async Task DiscoverAllRepresentationsIgnoresABadBrowsePathTranslationAsync()
        {
            NodeId machine = m_space.AddObject(Opc.Ua.ObjectIds.Server, "Machine");
            AddRepresentationWithBinding(machine, out NodeId binding);
            AddSourceBrowsePath(binding);
            SetupTranslate(StatusCodes.BadNoMatch);
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

            Assert.That(reps[0].Bindings[0].SourceNodeId.IsNull, Is.True);
        }

        [Test]
        public async Task DiscoverAllRepresentationsIgnoresAFailedBrowsePathTranslationAsync()
        {
            NodeId machine = m_space.AddObject(Opc.Ua.ObjectIds.Server, "Machine");
            AddRepresentationWithBinding(machine, out NodeId binding);
            AddSourceBrowsePath(binding);
            m_session
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadServiceUnsupported));
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

            Assert.That(reps[0].Bindings[0].SourceNodeId.IsNull, Is.True);
        }

        [Test]
        public void StartThrowsWhenNoRepresentationIsDiscovered()
        {
            OpenUsdConnector connector = Connector();

            Assert.ThrowsAsync<System.InvalidOperationException>(
                () => connector.StartAsync(CancellationToken.None));
        }

        private NodeId AddRepresentationWithBinding(NodeId representedObject, out NodeId binding)
        {
            NodeId registry = AddRegistry();
            NodeId rep = m_space.AddObject(registry, "Robot", RepresentationTypeId);
            m_space.AddObject(representedObject, "Robot", RepresentationTypeId,
                ReferenceTypeIds.HasComponent, 0, rep);
            m_space.AddVariable(rep, "PrimPath", new Variant("/World/Robot"));
            binding = m_space.AddObject(rep, "SpeedBinding", ValueChangeBindingTypeId);
            m_space.AddVariable(binding, "TargetPropertyName", new Variant("speed"));
            return rep;
        }

        private void AddSourceBrowsePath(NodeId binding)
        {
            var path = new RelativePath
            {
                Elements =
                [
                    new RelativePathElement
                    {
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName("Speed")
                    }
                ]
            };
            m_space.AddVariable(binding, "SourceBrowsePath", new Variant(new ExtensionObject(path)));
        }

        private void SetupTranslate(StatusCode statusCode, params BrowsePathTarget[] targets)
        {
            m_session
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    FakeSession.TranslateResponse(statusCode, targets)));
        }
    }
}
