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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Aas.Server.Registry;
using Opc.Ua.Aas.Tests.Server;
using Opc.Ua.Server;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.Aas.Tests.Registry
{
    /// <summary>
    /// Tests the stable AAS registry NodeManager: the AddressSpace it publishes, the registry
    /// lifecycle it drives and the security-relevant behaviour of the Methods it binds.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class AasRegistryNodeManagerTests
    {
        /// <summary>
        /// The registry root has to come from the compiled model so it carries the declared
        /// Methods; a bare Object would leave the discovery surface unbound.
        /// </summary>
        [Test]
        public async Task CreateAddressSpacePublishesTheWellKnownRegistryRootAsync()
        {
            using var fixture = await Fixture.CreateAsync().ConfigureAwait(false);

            var registry = (AASRegistryState?)fixture.NodeManager.Find(fixture.RegistryNodeId);

            Assert.That(registry, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(registry!.EventNotifier, Is.EqualTo(EventNotifiers.SubscribeToEvents));
                Assert.That(registry.LookupShellsByAssetLink, Is.Not.Null);
                Assert.That(registry.GetSubmodel, Is.Not.Null);
                Assert.That(registry.Materialize, Is.Not.Null);
                Assert.That(registry.GetSubmodel!.InputArguments, Is.Not.Null,
                    "The Method arguments have to be linked or a Call cannot be validated.");
                Assert.That(registry.GetSubmodel.OutputArguments, Is.Not.Null);
            });
        }

        /// <summary>
        /// The NodeManager owns the registry lifecycle: nothing loads the persisted snapshot but
        /// its own <c>InitializeAsync</c> call, so a projected group proves the load happened
        /// before the projection was attached.
        /// </summary>
        [Test]
        public async Task CreateAddressSpaceLoadsThePersistedRegistryBeforeProjectingItAsync()
        {
            var store = new InMemoryAasRegistryStore();
            await SeedAsync(store, "urn:shell", "urn:submodel", AasRegistryEntityKind.Shell).ConfigureAwait(false);
            var service = new AasRegistryService(store);

            Assert.That(service.Current.GroupsById, Is.Empty,
                "A freshly constructed service starts from the empty snapshot.");

            using var fixture = await Fixture.CreateAsync(service).ConfigureAwait(false);

            AASShellGroupState group = fixture.Children<AASShellGroupState>(fixture.RegistryNode).Single();
            AASSubmodelFileState resource = fixture.Children<AASSubmodelFileState>(group).Single();
            Assert.Multiple(() =>
            {
                Assert.That(group.AasIdentifier!.Value, Is.EqualTo("urn:shell"));
                Assert.That(resource.SubmodelIdentifier!.Value, Is.EqualTo("urn:submodel"));
                Assert.That(fixture.NodeManager.Find(group.NodeId), Is.SameAs(group),
                    "The projected group has to be published through the NodeManager.");
                Assert.That(fixture.NodeManager.Find(resource.NodeId), Is.SameAs(resource));
            });
        }

        /// <summary>
        /// The NodeManager reconciles on every committed snapshot switch, so it must hold exactly
        /// one subscription while the AddressSpace is up.
        /// </summary>
        [Test]
        public async Task CreateAddressSpaceSubscribesToCommittedSnapshotSwitchesAsync()
        {
            var service = new ObservableRegistryService(new AasRegistryService());

            Assert.That(service.ChangedSubscriberCount, Is.Zero);

            using var fixture = await Fixture.CreateAsync(service).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(service.InitializeCalls, Is.EqualTo(1));
                Assert.That(service.ChangedSubscriberCount, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Tearing the AddressSpace down has to release the subscription, otherwise a later commit
        /// reconciles into a disposed projection.
        /// </summary>
        [Test]
        public async Task DeleteAddressSpaceReleasesTheSnapshotSubscriptionAsync()
        {
            var service = new ObservableRegistryService(new AasRegistryService());
            using var fixture = await Fixture.CreateAsync(service).ConfigureAwait(false);

            await fixture.NodeManager.DeleteAddressSpaceAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(service.ChangedSubscriberCount, Is.Zero);
        }

        /// <summary>
        /// An authorized caller receives the document together with the metadata needed to parse it.
        /// </summary>
        [Test]
        public async Task GetSubmodelReturnsTheDocumentWithItsParseMetadataAsync()
        {
            using var fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            await fixture.UpsertAsync("urn:shell", "urn:submodel", "the-document").ConfigureAwait(false);

            MethodResult result = await fixture.CallAsync(
                fixture.RegistryNode.GetSubmodel!,
                new Variant("urn:submodel")).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.OutputArguments, Has.Count.EqualTo(3));
            Assert.Multiple(() =>
            {
                Assert.That(result.OutputArguments[0].TryGetValue(out ByteString document), Is.True);
                Assert.That(Encoding.UTF8.GetString(document.ToArray()), Is.EqualTo("the-document"));
                Assert.That(result.OutputArguments[1].TryGetValue(out string? format), Is.True);
                Assert.That(format, Is.EqualTo("aas/3.0+json"));
                Assert.That(result.OutputArguments[2].TryGetValue(out string? contentType), Is.True);
                Assert.That(contentType, Is.EqualTo("application/aas+json"));
            });
        }

        /// <summary>
        /// A concealed target must be indistinguishable from an absent one: the same status code
        /// and, critically, no output argument that would confirm the target exists.
        /// </summary>
        [Test]
        public async Task GetSubmodelConcealsControlledTargetsFromUnauthorizedCallersAsync()
        {
            using var fixture = await Fixture.CreateAsync(DenyingService()).ConfigureAwait(false);
            await fixture.UpsertAsync(
                "urn:shell", "urn:submodel", "secret", conceal: true).ConfigureAwait(false);

            MethodResult concealed = await fixture.CallAsync(
                fixture.RegistryNode.GetSubmodel!, new Variant("urn:submodel")).ConfigureAwait(false);
            MethodResult missing = await fixture.CallAsync(
                fixture.RegistryNode.GetSubmodel!, new Variant("urn:absent")).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(concealed.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(missing.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(concealed.OutputArguments, Is.Empty,
                    "Returning any output argument would confirm the concealed target exists.");
                Assert.That(missing.OutputArguments, Is.Empty);
            });
        }

        /// <summary>
        /// A target that is merely unauthorized, not concealed, keeps the distinct access-denied
        /// status but still returns no bytes.
        /// </summary>
        [Test]
        public async Task GetSubmodelDeniesUnauthorizedCallersWithoutReturningBytesAsync()
        {
            using var fixture = await Fixture.CreateAsync(DenyingService()).ConfigureAwait(false);
            await fixture.UpsertAsync(
                "urn:shell", "urn:submodel", "secret", conceal: false).ConfigureAwait(false);

            MethodResult result = await fixture.CallAsync(
                fixture.RegistryNode.GetSubmodel!, new Variant("urn:submodel")).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(result.OutputArguments, Is.Empty);
            });
        }

        /// <summary>
        /// An empty identifier can never resolve, so it is rejected as an invalid argument and no
        /// document is handed out.
        /// </summary>
        [Test]
        public async Task GetSubmodelRejectsAnEmptyIdentifierAsInvalidAsync()
        {
            using var fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            await fixture.UpsertAsync("urn:shell", "urn:submodel", "the-document").ConfigureAwait(false);

            MethodResult result = await fixture.CallAsync(
                fixture.RegistryNode.GetSubmodel!, new Variant(string.Empty)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(result.OutputArguments, Is.Empty);
            });
        }

        /// <summary>
        /// The Method signature is enforced before the handler runs, so a Call without the
        /// identifier never reaches the registry.
        /// </summary>
        [Test]
        public async Task GetSubmodelRejectsACallWithoutTheIdentifierArgumentAsync()
        {
            using var fixture = await Fixture.CreateAsync().ConfigureAwait(false);

            MethodResult result = await fixture.CallAsync(fixture.RegistryNode.GetSubmodel!)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadArgumentsMissing));
                Assert.That(result.OutputArguments, Is.Empty);
            });
        }

        /// <summary>
        /// Discovery answers with the shell identifiers that carry the asset link, and with an
        /// empty list - not an error - when nothing matches.
        /// </summary>
        [Test]
        public async Task LookupShellsByAssetLinkReturnsMatchesAndAnEmptyListForAMissAsync()
        {
            using var fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            await fixture.UpsertAsync(
                "urn:shell",
                "urn:submodel",
                "the-document",
                assetLinks: s_serialAssetLink)
                .ConfigureAwait(false);

            MethodResult hit = await fixture.CallAsync(
                fixture.RegistryNode.LookupShellsByAssetLink!,
                new Variant("serial"),
                new Variant("42")).ConfigureAwait(false);
            MethodResult miss = await fixture.CallAsync(
                fixture.RegistryNode.LookupShellsByAssetLink!,
                new Variant("serial"),
                new Variant("99")).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(hit.ServiceResult), Is.True);
                Assert.That(hit.OutputArguments[0].TryGetValue(out ArrayOf<string> hits), Is.True);
                Assert.That(hits, Is.EqualTo(s_shellIdentifiers));
                Assert.That(ServiceResult.IsGood(miss.ServiceResult), Is.True);
                Assert.That(miss.OutputArguments[0].TryGetValue(out ArrayOf<string> misses), Is.True);
                Assert.That(misses, Is.Empty);
            });
        }

        /// <summary>
        /// This NodeManager is the read-only registry half; Materialize belongs to the updateable
        /// profile and must fail closed rather than silently do nothing.
        /// </summary>
        [Test]
        public async Task MaterializeIsRejectedBecauseTheUpdateableProfileIsNotEnabledAsync()
        {
            using var fixture = await Fixture.CreateAsync().ConfigureAwait(false);

            MethodResult result = await fixture.CallAsync(
                fixture.RegistryNode.Materialize!,
                new Variant(s_materializeTargets),
                new Variant(true)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(result.ServiceResult.LocalizedText.Text,
                    Does.Contain("updateable-registry profile"));
                Assert.That(result.OutputArguments, Is.Empty);
            });
        }

        /// <summary>
        /// Without a package group the Server cannot serve packages, so claiming either package
        /// unit would be a false conformance claim.
        /// </summary>
        [Test]
        public async Task ConformanceUnitsOmitThePackageUnitsWithoutAPackageStoreAsync()
        {
            using var fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            await fixture.UpsertAsync("urn:shell", "urn:submodel", "the-document").ConfigureAwait(false);

            IEnumerable<string> units = Names(fixture.NodeManager.ConformanceUnits);

            Assert.Multiple(() =>
            {
                Assert.That(units, Is.EquivalentTo(s_registryUnits));
                Assert.That(fixture.NodeManager.ServerProfiles, Is.Empty,
                    "Clause 10 assigns no server profile URI to the registry half.");
            });
        }

        /// <summary>
        /// Clause 10 requires AAS-PackageIntegrity to accompany AAS-Packages, so the two appear
        /// together as soon as a package store group exists.
        /// </summary>
        [Test]
        public async Task ConformanceUnitsAddBothPackageUnitsWhenAPackageStoreExistsAsync()
        {
            using var fixture = await Fixture.CreateAsync().ConfigureAwait(false);
            await fixture.UpsertAsync(
                "urn:store",
                "urn:package",
                "the-package",
                groupKind: AasRegistryEntityKind.PackageStore,
                resourceKind: AasRegistryEntityKind.Package).ConfigureAwait(false);

            IEnumerable<string> units = Names(fixture.NodeManager.ConformanceUnits);

            Assert.That(units, Is.EquivalentTo(s_registryUnits.Concat(s_packageUnits)));
        }

        /// <summary>
        /// A NodeManager without a registry cannot answer anything, so the dependency is required.
        /// </summary>
        [Test]
        public void ConstructorRejectsAMissingRegistryService()
        {
            Mock<IServerInternal> server = AasServerTestHarness.CreateServer(
                Opc.Ua.Aas.V3.Namespaces.AasV3, XRegistryWellKnown.XRegistryNamespaceUri);

            Assert.That(
                () => new AasRegistryNodeManager(server.Object, null!, null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("registry"));
        }

        private static List<string> Names(ArrayOf<QualifiedName> units)
        {
            var names = new List<string>(units.Count);
            for (int i = 0; i < units.Count; i++)
            {
                names.Add(units[i].Name ?? string.Empty);
            }
            return names;
        }

        private static AasRegistryService DenyingService()
        {
            var evaluator = new Mock<IAasRegistryAuthorizationEvaluator>(MockBehavior.Strict);
            evaluator.Setup(e => e.IsAuthenticated(It.IsAny<ISystemContext?>())).Returns(true);
            evaluator
                .Setup(e => e.CanReadSubmodel(It.IsAny<ISystemContext?>(), It.IsAny<AasRegistryResource>()))
                .Returns(false);
            return new AasRegistryService(authorizationEvaluator: evaluator.Object);
        }

        private static async Task SeedAsync(
            IAasRegistryStore store,
            string groupIdentity,
            string resourceIdentity,
            AasRegistryEntityKind groupKind)
        {
            var seed = new AasRegistryService(store);
            await seed.InitializeAsync().ConfigureAwait(false);
            await seed.UpsertResourceAsync(new AasUpsertResourceRequest
            {
                GroupSourceIdentity = groupIdentity,
                ResourceSourceIdentity = resourceIdentity,
                GroupKind = groupKind,
                ResourceKind = AasRegistryEntityKind.Submodel,
                Content = ByteString.From(Encoding.UTF8.GetBytes("seeded")),
                ContentType = "application/aas+json",
                Format = "aas/3.0+json"
            }).ConfigureAwait(false);
        }

        private static readonly string[] s_registryUnits =
        [
            "AAS-Registry",
            "AAS-RegistryIdentity",
            "AAS-RegistryVersioning",
            "AAS-Discovery",
            "AAS-DisclosureTiers",
            "AAS-UpdateableRegistry",
            "AAS-EnvironmentExport"
        ];

        private static readonly string[] s_packageUnits = ["AAS-Packages", "AAS-PackageIntegrity"];

        private static readonly AasRegistryAssetLink[] s_serialAssetLink =
        [
            new AasRegistryAssetLink { Name = "serial", Value = "42" }
        ];

        private static readonly string[] s_shellIdentifiers = ["urn:shell"];

        private static readonly string[] s_materializeTargets = ["urn:shell"];

        /// <summary>
        /// The outcome of one Method Call on the registry root.
        /// </summary>
        private sealed record MethodResult(ServiceResult ServiceResult, List<Variant> OutputArguments);

        /// <summary>
        /// A registry NodeManager over a mocked server with its AddressSpace already created.
        /// </summary>
        private sealed class Fixture : IDisposable
        {
            private Fixture(AasRegistryNodeManager nodeManager, IAasRegistryService registry, NodeId registryNodeId)
            {
                NodeManager = nodeManager;
                Registry = registry;
                RegistryNodeId = registryNodeId;
                RegistryNode = (AASRegistryState)nodeManager.Find(registryNodeId)!;
            }

            public AasRegistryNodeManager NodeManager { get; }

            public IAasRegistryService Registry { get; }

            public NodeId RegistryNodeId { get; }

            public AASRegistryState RegistryNode { get; }

            public static async Task<Fixture> CreateAsync(IAasRegistryService? registry = null)
            {
                Mock<IServerInternal> server = AasServerTestHarness.CreateServer(
                    Opc.Ua.Aas.V3.Namespaces.AasV3, XRegistryWellKnown.XRegistryNamespaceUri);
                IAasRegistryService service = registry ?? new AasRegistryService();
                var nodeManager = new AasRegistryNodeManager(server.Object, null!, service);
                await nodeManager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                return new Fixture(
                    nodeManager,
                    service,
                    ExpandedNodeId.ToNodeId(Opc.Ua.Aas.V3.ObjectIds.AASRegistry, server.Object.NamespaceUris));
            }

            public ValueTask<AasRegistryMutationResult> UpsertAsync(
                string groupIdentity,
                string resourceIdentity,
                string document,
                bool conceal = false,
                AasRegistryEntityKind groupKind = AasRegistryEntityKind.Shell,
                AasRegistryEntityKind resourceKind = AasRegistryEntityKind.Submodel,
                AasRegistryAssetLink[]? assetLinks = null)
            {
                var request = new AasUpsertResourceRequest
                {
                    GroupSourceIdentity = groupIdentity,
                    ResourceSourceIdentity = resourceIdentity,
                    GroupKind = groupKind,
                    ResourceKind = resourceKind,
                    Content = ByteString.From(Encoding.UTF8.GetBytes(document)),
                    ContentType = "application/aas+json",
                    Format = "aas/3.0+json"
                };
                if (conceal)
                {
                    request.DisclosureTier = AASDisclosureTierDataType.Controlled;
                    request.ConcealFromUnauthorized = true;
                }
                if (assetLinks is not null)
                {
                    request.SpecificAssetIds = new ArrayOf<AasRegistryAssetLink>(assetLinks);
                }
                return Registry.UpsertResourceAsync(request);
            }

            public async Task<MethodResult> CallAsync(MethodState method, params Variant[] inputArguments)
            {
                var argumentErrors = new List<ServiceResult>();
                var outputArguments = new List<Variant>();
                ServiceResult result = await method.CallAsync(
                    NodeManager.SystemContext,
                    RegistryNodeId,
                    new ArrayOf<Variant>(inputArguments),
                    argumentErrors,
                    outputArguments,
                    CancellationToken.None).ConfigureAwait(false);
                return new MethodResult(result, outputArguments);
            }

            public List<T> Children<T>(NodeState parent) where T : NodeState
            {
                var children = new List<BaseInstanceState>();
                parent.GetChildren(NodeManager.SystemContext, children);
                return [.. children.OfType<T>()];
            }

            public void Dispose()
            {
                NodeManager.Dispose();
            }
        }

        /// <summary>
        /// Delegating registry service that makes the NodeManager's own lifecycle calls and event
        /// subscriptions observable.
        /// </summary>
        private sealed class ObservableRegistryService : IAasRegistryService
        {
            public ObservableRegistryService(AasRegistryService inner)
            {
                m_inner = inner;
                m_inner.Changed += (sender, args) => Changed?.Invoke(this, args);
            }

            public event EventHandler<AasRegistryChangedEventArgs>? Changed;

            public int InitializeCalls { get; private set; }

            public int ChangedSubscriberCount => Changed?.GetInvocationList().Length ?? 0;

            public AasRegistrySnapshot Current => m_inner.Current;

            public AasRegistryPersistenceBounds Bounds => m_inner.Bounds;

            public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
            {
                InitializeCalls++;
                return m_inner.InitializeAsync(cancellationToken);
            }

            public ValueTask<AasRegistryGroup> GetOrCreateGroupAsync(
                string sourceIdentity,
                AasRegistryEntityKind kind,
                CancellationToken cancellationToken = default)
            {
                return m_inner.GetOrCreateGroupAsync(sourceIdentity, kind, cancellationToken);
            }

            public ValueTask<AasRegistryMutationResult> UpsertResourceAsync(
                AasUpsertResourceRequest request,
                CancellationToken cancellationToken = default)
            {
                return m_inner.UpsertResourceAsync(request, cancellationToken);
            }

            public ValueTask<ByteString> ReadContentAsync(
                AasRegistryResourceVersion version,
                CancellationToken cancellationToken = default)
            {
                return m_inner.ReadContentAsync(version, cancellationToken);
            }

            public ValueTask<ByteString> ReadContentChunkAsync(
                string digestHex,
                long offset,
                int count,
                CancellationToken cancellationToken = default)
            {
                return m_inner.ReadContentChunkAsync(digestHex, offset, count, cancellationToken);
            }

            public ArrayOf<string> LookupShellsByAssetLink(string name, string value, ISystemContext? context = null)
            {
                return m_inner.LookupShellsByAssetLink(name, value, context);
            }

            public ValueTask<AasGetSubmodelResult> GetSubmodelAsync(
                string submodelIdentifier,
                ISystemContext? context = null,
                CancellationToken cancellationToken = default)
            {
                return m_inner.GetSubmodelAsync(submodelIdentifier, context, cancellationToken);
            }

            private readonly AasRegistryService m_inner;
        }
    }
}
