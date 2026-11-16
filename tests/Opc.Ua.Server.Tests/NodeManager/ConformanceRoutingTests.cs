/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Exercises conformance publication against routing images without starting a server.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class ConformanceRoutingTests
    {
        /// <summary>
        /// Hidden managers are not read or advertised until they become visible.
        /// </summary>
        [Test]
        public async Task PublicationReadsOnlyVisibleManagersAsync()
        {
            var routing = new NodeManagerRoutingTable();
            var visible = new Mock<IAsyncNodeManager>();
            Mock<IConformanceContributor> visibleContributor = visible.As<IConformanceContributor>();
            visibleContributor.SetupGet(value => value.ConformanceUnits).Returns([new("Visible Unit")]);
            visibleContributor.SetupGet(value => value.ServerProfiles).Returns(["urn:profile:visible"]);
            var hidden = new Mock<IAsyncNodeManager>();
            Mock<IConformanceContributor> hiddenContributor = hidden.As<IConformanceContributor>();
            int hiddenUnitReads = 0;
            int hiddenProfileReads = 0;
            hiddenContributor.SetupGet(value => value.ConformanceUnits).Returns(() =>
            {
                hiddenUnitReads++;
                return [new QualifiedName("Hidden Unit")];
            });
            hiddenContributor.SetupGet(value => value.ServerProfiles).Returns(() =>
            {
                hiddenProfileReads++;
                return ["urn:profile:hidden"];
            });
            routing.Add(visible.Object, [1]);
            routing.Add(hidden.Object, [2], visible: false);
            Assert.That(routing.Revision.GetGeneration(hidden.Object).Gate.State,
                Is.EqualTo(NodeManagerGenerationState.Prepared));

            var diagnostics = new Mock<IDiagnosticsNodeManager>();
            ArrayOf<string> publishedProfiles = default;
            diagnostics.Setup(value => value.PublishConformanceUnitsAsync(
                    It.IsAny<ArrayOf<QualifiedName>>(), It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<QualifiedName>, ArrayOf<string>, CancellationToken>(
                    (_, profiles, _) => publishedProfiles = profiles)
                .Returns(default(ValueTask));
            Mock<IServerInternal> server = CreateServer(routing, diagnostics.Object);
            using var catalog = new ConformanceUnitsManager(server.Object);

            await catalog.PublishAsync().ConfigureAwait(false);
            Assert.That(catalog.IsSupported(new QualifiedName("Visible Unit")), Is.True);
            Assert.That(catalog.IsSupported(new QualifiedName("Hidden Unit")), Is.False);
            Assert.That(publishedProfiles.ToArray(), Is.EqualTo(s_visibleProfiles));
            Assert.That(hiddenUnitReads, Is.Zero);
            Assert.That(hiddenProfileReads, Is.Zero);

            routing.SetVisible(hidden.Object, visible: true);
            await catalog.PublishAsync().ConfigureAwait(false);
            Assert.That(catalog.IsSupported(new QualifiedName("Hidden Unit")), Is.True);
            Assert.That(publishedProfiles.ToArray(),
                Is.EqualTo(s_visibleAndHiddenProfiles));
            Assert.That(hiddenUnitReads, Is.EqualTo(1));
            Assert.That(hiddenProfileReads, Is.EqualTo(1));

            routing.SetVisible(hidden.Object, visible: false);
            await catalog.PublishAsync().ConfigureAwait(false);
            Assert.That(catalog.IsSupported(new QualifiedName("Hidden Unit")), Is.False);
            Assert.That(publishedProfiles.ToArray(), Is.EqualTo(s_visibleProfiles));
            Assert.That(hiddenUnitReads, Is.EqualTo(1));
            Assert.That(hiddenProfileReads, Is.EqualTo(1));
        }

        /// <summary>
        /// A routed facade supplies its own contributions while its generation is admitted.
        /// </summary>
        [Test]
        public async Task PublicationReadsRoutedFacadeInsteadOfPriorResourceOwnerAsync()
        {
            var routing = new NodeManagerRoutingTable();
            var resource = new Mock<IAsyncNodeManager>();
            Mock<IConformanceContributor> resourceContributor = resource.As<IConformanceContributor>();
            resourceContributor.SetupGet(value => value.ConformanceUnits).Returns([new("Resource Unit")]);
            resourceContributor.SetupGet(value => value.ServerProfiles).Returns(["urn:profile:resource"]);
            var sync = new SyncNodeManagerAdapter(resource.Object);
            resource.SetupGet(value => value.SyncNodeManager).Returns(sync);
            _ = NodeManagerGenerationRegistry.GetOrCreate(resource.Object);

            var facade = new Mock<IAsyncNodeManager>();
            Mock<IConformanceContributor> facadeContributor = facade.As<IConformanceContributor>();
            long unitsActiveCount = -1;
            long profilesActiveCount = -1;
            NodeManagerGeneration? routedGeneration = null;
            facadeContributor.SetupGet(value => value.ConformanceUnits).Returns(() =>
            {
                unitsActiveCount = routedGeneration!.Gate.ActiveCount;
                return [new QualifiedName("Facade Unit")];
            });
            facadeContributor.SetupGet(value => value.ServerProfiles).Returns(() =>
            {
                profilesActiveCount = routedGeneration!.Gate.ActiveCount;
                return ["urn:profile:facade"];
            });
            facade.SetupGet(value => value.SyncNodeManager).Returns(sync);
            routing.Add(facade.Object, [1]);
            routedGeneration = routing.Revision.GetGeneration(facade.Object);

            var diagnostics = new Mock<IDiagnosticsNodeManager>();
            ArrayOf<string> publishedProfiles = default;
            diagnostics.Setup(value => value.PublishConformanceUnitsAsync(
                    It.IsAny<ArrayOf<QualifiedName>>(), It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<QualifiedName>, ArrayOf<string>, CancellationToken>(
                    (_, profiles, _) => publishedProfiles = profiles)
                .Returns(default(ValueTask));
            Mock<IServerInternal> server = CreateServer(routing, diagnostics.Object);
            using var catalog = new ConformanceUnitsManager(server.Object);

            await catalog.PublishAsync().ConfigureAwait(false);

            Assert.That(catalog.IsSupported(new QualifiedName("Facade Unit")), Is.True);
            Assert.That(catalog.IsSupported(new QualifiedName("Resource Unit")), Is.False);
            Assert.That(publishedProfiles.ToArray(), Is.EqualTo(s_facadeProfiles));
            Assert.That(unitsActiveCount, Is.GreaterThan(0));
            Assert.That(profilesActiveCount, Is.GreaterThan(0));
            Assert.That(routedGeneration.Gate.ActiveCount, Is.Zero);
            resourceContributor.VerifyGet(value => value.ConformanceUnits, Times.Never);
            resourceContributor.VerifyGet(value => value.ServerProfiles, Times.Never);
        }

        /// <summary>
        /// A routed wrapper can expose its underlying contributor through the stack's synchronous adapter.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PublicationFindsContributorBehindSynchronousAdapterAsync(bool standardAlias)
        {
            var routing = new NodeManagerRoutingTable();
            var resource = new Mock<IAsyncNodeManager>();
            Mock<IConformanceContributor> contributor = resource.As<IConformanceContributor>();
            contributor.SetupGet(value => value.ConformanceUnits).Returns([new("Resource Unit")]);
            contributor.SetupGet(value => value.ServerProfiles).Returns(["urn:profile:resource"]);
            var synchronous = new SyncNodeManagerAdapter(resource.Object);
            resource.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
            IAsyncNodeManager routed;
            if (standardAlias)
            {
                routed = new AsyncNodeManagerAdapter(synchronous);
            }
            else
            {
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.SyncNodeManager).Returns(synchronous);
                routed = facade.Object;
            }
            routing.Add(routed, [1]);

            var diagnostics = new Mock<IDiagnosticsNodeManager>();
            ArrayOf<string> publishedProfiles = default;
            diagnostics.Setup(value => value.PublishConformanceUnitsAsync(
                    It.IsAny<ArrayOf<QualifiedName>>(), It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<QualifiedName>, ArrayOf<string>, CancellationToken>(
                    (_, profiles, _) => publishedProfiles = profiles)
                .Returns(default(ValueTask));
            Mock<IServerInternal> server = CreateServer(routing, diagnostics.Object);
            using var catalog = new ConformanceUnitsManager(server.Object);

            await catalog.PublishAsync().ConfigureAwait(false);

            Assert.That(catalog.IsSupported(new QualifiedName("Resource Unit")), Is.True);
            Assert.That(publishedProfiles.ToArray(), Is.EqualTo(s_resourceProfiles));
            contributor.VerifyGet(value => value.ConformanceUnits, Times.Once);
            contributor.VerifyGet(value => value.ServerProfiles, Times.Once);
            Assert.That(routing.Revision.GetGeneration(routed).Gate.ActiveCount, Is.Zero);
        }

        /// <summary>
        /// Creates a server whose batch host exposes the supplied routing table.
        /// </summary>
        private static Mock<IServerInternal> CreateServer(
            NodeManagerRoutingTable routing,
            IDiagnosticsNodeManager diagnostics)
        {
            var master = new Mock<IMasterNodeManager>();
            master.As<IDynamicNodeManagerBatchHost>()
                .SetupGet(value => value.RoutingRevision).Returns(() => routing.Revision);
            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.NodeManager).Returns(master.Object);
            server.SetupGet(value => value.DiagnosticsNodeManager).Returns(diagnostics);
            return server;
        }

        /// <summary>The profile exposed by the visible routed contributor.</summary>
        private static readonly string[] s_visibleProfiles = ["urn:profile:visible"];

        /// <summary>The profiles exposed while both routed contributors are visible.</summary>
        private static readonly string[] s_visibleAndHiddenProfiles = ["urn:profile:visible", "urn:profile:hidden"];

        /// <summary>The profile exposed by the routing facade.</summary>
        private static readonly string[] s_facadeProfiles = ["urn:profile:facade"];

        /// <summary>The profile exposed by the wrapped resource.</summary>
        private static readonly string[] s_resourceProfiles = ["urn:profile:resource"];
    }
}
