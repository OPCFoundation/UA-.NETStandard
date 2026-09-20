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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    public sealed class WotSelectedDependencyTests
    {
        [Test]
        public async Task SelectedResourceDoesNotAcquireOrPlanIndependentResource()
        {
            using var fixture = new SelectionFixture();
            WotResource a = await fixture.RegisterAsync("a");
            WotResource b = await fixture.RegisterAsync("b");

            WotRefreshResult result = await fixture.Coordinator.RefreshAsync(Select(a));

            Assert.Multiple(() =>
            {
                Assert.That(fixture.Reads.Select(version => version.DocumentId), Is.EqualTo(s_aDocument));
                Assert.That(fixture.Converted, Is.EqualTo(s_aResource));
                Assert.That(fixture.Prepared, Is.EqualTo(new List<string> { a.Xid }));
                Assert.That(result.Results.Single().ResourceId, Is.EqualTo("a"));
                Assert.That(result.Results.Single().LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
                Assert.That(fixture.Registry.Current.FindResource(b.GroupId, b.ResourceId)!.ActiveVersionId, Is.Null);
                Assert.That(fixture.Host.Operations.Single().SourceNames, Is.EqualTo(s_aResource));
            });
        }

        [Test]
        public async Task SelectedResourceDoesNotRetireUnselectedDisabledProjection()
        {
            using var fixture = new SelectionFixture();
            WotResource a = await fixture.RegisterAsync("a");
            WotResource b = await fixture.RegisterAsync("b");
            await fixture.Coordinator.RefreshAsync(new WotRefreshRequest());
            await fixture.Registry.SetEnabledAsync(b.GroupId, b.ResourceId, false);
            fixture.ClearObservations();

            WotRefreshRequest request = Select(a);
            request.Options.Force = true;
            await fixture.Coordinator.RefreshAsync(request);

            Assert.Multiple(() =>
            {
                Assert.That(fixture.Host.Operations.Select(operation => operation.Op), Is.EqualTo(s_shadow));
                Assert.That(fixture.Host.Operations.Single().SourceNames, Is.EqualTo(s_aResource));
                Assert.That(fixture.Deactivated, Does.Not.Contain(b.Xid));
                Assert.That(fixture.Reads.Select(version => version.DocumentId), Is.EqualTo(s_aDocument));
            });
        }

        [Test]
        public async Task UnknownSelectionDoesNotAcquireOrPublish()
        {
            using var fixture = new SelectionFixture();
            await fixture.RegisterAsync("a");
            await fixture.RegisterAsync("b");
            long registryGeneration = fixture.Registry.Current.Generation;
            uint refreshGeneration = fixture.Coordinator.Generation;

            WotRefreshResult result = await fixture.Coordinator.RefreshAsync(new WotRefreshRequest
            {
                Selection = [new WoTResourceSelectorDataType { Kind = WoTDocumentKindEnum.All, ResourceId = "absent" }]
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.Results, Is.Empty);
                Assert.That(fixture.Reads, Is.Empty);
                Assert.That(fixture.Converted, Is.Empty);
                Assert.That(fixture.Prepared, Is.Empty);
                Assert.That(fixture.Host.Operations, Is.Empty);
                Assert.That(fixture.Registry.Current.Generation, Is.EqualTo(registryGeneration));
                Assert.That(result.NewGeneration, Is.EqualTo(refreshGeneration));
            });
        }

        [TestCase(WoTDocumentKindEnum.ThingDescription)]
        [TestCase(WoTDocumentKindEnum.All)]
        public async Task SelectorKindAndExactVersionBoundInputs(WoTDocumentKindEnum kind)
        {
            using var fixture = new SelectionFixture();
            WotResource a = await fixture.RegisterAsync("a", versionId: "v1");
            await fixture.RegisterAsync("a", versionId: "v2");
            await fixture.RegisterAsync("model", WoTDocumentKindEnum.ThingModel);
            WotRefreshRequest request = Select(a, "v1");
            request.Selection[0].Kind = kind;

            WotRefreshResult result = await fixture.Coordinator.RefreshAsync(request);

            Assert.Multiple(() =>
            {
                Assert.That(fixture.Reads.Single().VersionId, Is.EqualTo("v1"));
                Assert.That(fixture.Reads.Single().Digest, Is.EqualTo(a.FindVersion("v1")!.Digest));
                Assert.That(result.Results.Single().VersionId, Is.EqualTo("v1"));
                Assert.That(result.Results.Single().Kind, Is.EqualTo(WoTDocumentKindEnum.ThingDescription));
                Assert.That(fixture.Registry.Current.FindResource(a.GroupId, a.ResourceId)!.ActiveVersionId,
                    Is.EqualTo("v1"));
                Assert.That(fixture.Registry.Current.FindResource(a.GroupId, a.ResourceId)!.DefaultVersionId,
                    Is.EqualTo("v2"));
            });
        }

        [Test]
        public async Task XidOverridesIdentityFiltersButNotKind()
        {
            using var fixture = new SelectionFixture();
            WotResource model = await fixture.RegisterAsync("model", WoTDocumentKindEnum.ThingModel);
            var selector = new WoTResourceSelectorDataType
            {
                Kind = WoTDocumentKindEnum.ThingDescription,
                Xid = model.Xid,
                GroupId = "ignored-group",
                ResourceId = "ignored-resource",
                VersionId = "ignored-version"
            };
            WotRefreshResult denied = await fixture.Coordinator.RefreshAsync(new WotRefreshRequest
            {
                Selection = [selector]
            });
            Assert.That(denied.Results, Is.Empty);
            Assert.That(fixture.Reads, Is.Empty);

            selector.Kind = WoTDocumentKindEnum.ThingModel;
            WotRefreshResult accepted = await fixture.Coordinator.RefreshAsync(new WotRefreshRequest
            {
                Selection = [selector]
            });

            Assert.That(accepted.Results.Single().ResourceId, Is.EqualTo("model"));
            Assert.That(accepted.Results.Single().Kind, Is.EqualTo(WoTDocumentKindEnum.ThingModel));
            Assert.That(fixture.Reads.Single().DocumentId, Is.EqualTo("urn:model"));
        }

        [Test]
        public async Task DisabledExactVersionIsSkippedWithoutAcquisition()
        {
            using var fixture = new SelectionFixture();
            WotResource a = await fixture.RegisterAsync("a", versionId: "v1");
            await fixture.RegisterAsync("a", versionId: "v2");
            await fixture.Registry.SetEnabledAsync(a.GroupId, a.ResourceId, false);
            string versionXid = a.Xid + "/versions/v1";

            WotRefreshResult result = await fixture.Coordinator.RefreshAsync(new WotRefreshRequest
            {
                Selection = [new WoTResourceSelectorDataType
                {
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Xid = versionXid
                }]
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.Results.Single().Xid, Is.EqualTo(versionXid));
                Assert.That(result.Results.Single().VersionId, Is.EqualTo("v1"));
                Assert.That(result.Results.Single().Outcome, Is.EqualTo(WoTOutcomeEnum.Skipped));
                Assert.That(fixture.Reads, Is.Empty);
                Assert.That(fixture.Host.Operations, Is.Empty);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task IncludeDependentsUsesOnlyIndexedReverseClosure(bool includeDependents)
        {
            using var fixture = new SelectionFixture();
            WotResource model = await fixture.RegisterAsync("model", WoTDocumentKindEnum.ThingModel);
            await fixture.RegisterAsync("a", content: TestMaterialization.Td("urn:a", extendsHrefs: "urn:model"));
            await fixture.RegisterAsync("b");
            WotRefreshRequest request = Select(model);
            request.Options.IncludeDependents = includeDependents;

            WotRefreshResult result = await fixture.Coordinator.RefreshAsync(request);
            string[] expectedDocuments = includeDependents ? ["urn:model", "urn:a"] : ["urn:model"];
            string[] expectedResources = includeDependents ? ["model", "a"] : ["model"];

            Assert.Multiple(() =>
            {
                Assert.That(fixture.Reads.Select(version => version.DocumentId), Is.EquivalentTo(expectedDocuments));
                Assert.That(fixture.Converted, Is.EquivalentTo(expectedResources));
                Assert.That(result.Results.Select(row => row.ResourceId), Is.EquivalentTo(expectedResources));
                Assert.That(result.Results.All(row => row.LoadState == WoTLoadStateEnum.Active), Is.True);
            });
        }

        [Test]
        public async Task IndependentAcquisitionFailureRetainsSuccessfulSelectedResource()
        {
            using var fixture = new SelectionFixture();
            WotResource a = await fixture.RegisterAsync("a");
            WotResource b = await fixture.RegisterAsync("b");
            fixture.FailedDocument = "urn:b";

            WotRefreshResult result = await fixture.Coordinator.RefreshAsync(new WotRefreshRequest
            {
                Selection = [new WoTResourceSelectorDataType { Kind = WoTDocumentKindEnum.ThingDescription }],
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerClosure }
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.Results.Single(row => row.ResourceId == "a").LoadState,
                    Is.EqualTo(WoTLoadStateEnum.Active));
                Assert.That(result.Results.Single(row => row.ResourceId == "b").Outcome,
                    Is.EqualTo(WoTOutcomeEnum.Failed));
                Assert.That(result.Results.Single(row => row.ResourceId == "b").Phase,
                    Is.EqualTo(WoTPhaseEnum.Fetch));
                Assert.That(fixture.Registry.Current.FindResource(a.GroupId, a.ResourceId)!.ActiveVersionId,
                    Is.EqualTo("v1"));
                Assert.That(fixture.Registry.Current.FindResource(b.GroupId, b.ResourceId)!.ActiveVersionId, Is.Null);
                Assert.That(fixture.Prepared, Is.EqualTo(new List<string> { a.Xid }));
            });
        }

        [Test]
        public async Task ExactIdentityOutranksResourceSuffix()
        {
            using var fixture = new SelectionFixture();
            await fixture.RegisterAsync("pump", WoTDocumentKindEnum.ThingModel);
            WotResource exact = await fixture.RegisterAsync(
                "actual", content: TestMaterialization.Td("https://example.test/pump"));

            Assert.That(WotDependencyGraph.Resolve(fixture.Registry.Current, "https://example.test/pump"),
                Is.SameAs(exact));
        }

        [Test]
        public async Task UnknownAbsoluteIdentityDoesNotMatchSuffix()
        {
            using var fixture = new SelectionFixture();
            await fixture.RegisterAsync("pump", WoTDocumentKindEnum.ThingModel);

            Assert.That(WotDependencyGraph.Resolve(fixture.Registry.Current, "https://unknown.test/pump"), Is.Null);
        }

        [Test]
        public async Task AmbiguousUnqualifiedIdentityDoesNotChooseAnOwner()
        {
            using var fixture = new SelectionFixture();
            await fixture.RegisterAsync("pump", groupId: "one", content: TestMaterialization.Td("urn:one"));
            await fixture.RegisterAsync("pump", groupId: "two", content: TestMaterialization.Td("urn:two"));

            Assert.That(WotDependencyGraph.Resolve(fixture.Registry.Current, "pump"), Is.Null);
        }

        [Test]
        public async Task ContextualDependencyRetainsRawReferenceAndExactTarget()
        {
            using var fixture = new SelectionFixture();
            WotResource model = await fixture.RegisterAsync(
                "model", WoTDocumentKindEnum.ThingModel,
                content: TestMaterialization.Tm("https://example.test/models/Pump"));
            WotResource source = await fixture.RegisterAsync("a", content: Encoding.UTF8.GetBytes("""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    {"model": "https://example.test/models/", "inherit": "https://www.w3.org/2019/wot/tm#extends"}
                  ],
                  "id": "urn:a",
                  "title": "A",
                  "links": [{"rel": "inherit", "href": "model:Pump"}]
                }
                """));

            ImmutableArray<WotDependencyClosure> closures = await WotDependencyGraph.BuildClosuresAsync(
                fixture.Registry.Current, [source], 64, fixture.Registry.ReadContentAsync, CancellationToken.None);

            WotDependency edge = closures.Single().Dependencies.Single();
            Assert.Multiple(() =>
            {
                Assert.That(edge.SourceXid, Is.EqualTo(source.Xid));
                Assert.That(edge.TargetHref, Is.EqualTo("model:Pump"));
                Assert.That(edge.TargetXid, Is.EqualTo(model.Xid));
                Assert.That(edge.Resolved, Is.True);
                Assert.That(closures.Single().IsProjectable, Is.True);
            });
        }

        [Test]
        public async Task ReciprocalSemanticReferencesFormAProjectableClosure()
        {
            using var fixture = new SelectionFixture();
            WotResource a = await fixture.RegisterAsync("a", content: ReferenceDocument("a", "b"));
            await fixture.RegisterAsync("b", content: ReferenceDocument("b", "a"));

            ImmutableArray<WotDependencyClosure> closures = await WotDependencyGraph.BuildClosuresAsync(
                fixture.Registry.Current, [a], 64, fixture.Registry.ReadContentAsync, CancellationToken.None);
            WotDependencyClosure closure = closures.Single();
            string[] expected = ["a", "b"];

            Assert.Multiple(() =>
            {
                Assert.That(closure.Members.Select(member => member.ResourceId), Is.EquivalentTo(expected));
                Assert.That(closure.Dependencies.Select(edge => edge.TargetHref),
                    Is.EquivalentTo(new List<string> { "urn:a", "urn:b" }));
                Assert.That(closure.IsProjectable, Is.True);
                Assert.That(closure.HasCycle, Is.False);
            });
        }

        [Test]
        public async Task InheritanceCycleRemainsAnOrderingFailure()
        {
            using var fixture = new SelectionFixture();
            WotResource a = await fixture.RegisterAsync("a", WoTDocumentKindEnum.ThingModel,
                content: TestMaterialization.Tm("urn:a", extendsHrefs: "urn:b"));
            await fixture.RegisterAsync("b", WoTDocumentKindEnum.ThingModel,
                content: TestMaterialization.Tm("urn:b", extendsHrefs: "urn:a"));

            ImmutableArray<WotDependencyClosure> closures = await WotDependencyGraph.BuildClosuresAsync(
                fixture.Registry.Current, [a], 64, fixture.Registry.ReadContentAsync, CancellationToken.None);

            Assert.That(closures.Single().HasCycle, Is.True);
            Assert.That(closures.Single().IsProjectable, Is.False);
        }

        [Test]
        public async Task RetiredDependencySuppliesDefinitionsWithoutExecutingOwnership()
        {
            using var fixture = new SelectionFixture();
            WotResource model = await fixture.RegisterAsync("model", WoTDocumentKindEnum.ThingModel);
            WotResource a = await fixture.RegisterAsync(
                "a", content: TestMaterialization.Td("urn:a", extendsHrefs: "urn:model"));
            await fixture.Registry.SetEnabledAsync(model.GroupId, model.ResourceId, false);

            WotRefreshResult result = await fixture.Coordinator.RefreshAsync(Select(a));

            Assert.Multiple(() =>
            {
                Assert.That(fixture.Reads.Select(version => version.DocumentId),
                    Is.EquivalentTo(new List<string> { "urn:a", "urn:model" }));
                Assert.That(fixture.Prepared, Is.EqualTo(new List<string> { a.Xid }));
                Assert.That(fixture.Activated, Does.Not.Contain(model.Xid));
                Assert.That(fixture.Registry.Current.FindResource(model.GroupId, model.ResourceId)!.Enabled, Is.False);
                Assert.That(fixture.Registry.Current.FindResource(model.GroupId, model.ResourceId)!.ActiveVersionId,
                    Is.Null);
                Assert.That(result.Results.Single().ResourceId, Is.EqualTo("a"));
                Assert.That(result.Results.Single().LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            });
        }

        [Test]
        public async Task CapturedExactVersionLeasePreventsRetentionEvictionAcrossReload()
        {
            using var fixture = new SelectionFixture(new WotRegistryPersistenceBounds { MaxVersionsPerResource = 2 });
            WotResource original = await fixture.RegisterAsync("a", versionId: "v1");
            await fixture.RegisterAsync("a", versionId: "v2");
            await fixture.Coordinator.RefreshAsync(new WotRefreshRequest());
            fixture.ClearObservations();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.BeforeRead = async _ =>
            {
                entered.TrySetResult(true);
                await resume.Task;
            };
            Task<WotRefreshResult> refresh = fixture.Coordinator.RefreshAsync(Select(original, "v1")).AsTask();
            try
            {
                await Task.WhenAny(entered.Task, refresh);
                Assert.That(entered.Task.IsCompleted, Is.True, "The exact selected body must be acquired.");
                WotRegistryMutationResult attempted = await fixture.Registry.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = original.GroupId,
                    ResourceId = original.ResourceId,
                    VersionId = "v3",
                    Content = ByteString.From(TestMaterialization.Td("urn:a", "v3"))
                });
                Assert.That(attempted.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected),
                    "Active v2, selected/leased v1 and incoming v3 cannot fit a two-Version bound.");
                await fixture.Registry.InitializeAsync();
                Assert.That(fixture.Registry.Current.FindResource(original.GroupId, original.ResourceId)!
                    .FindVersion("v1")!.Digest, Is.EqualTo(original.FindVersion("v1")!.Digest));
            }
            finally
            {
                resume.TrySetResult(true);
                await refresh;
            }
            Assert.That((await refresh).Results.Single().VersionId, Is.EqualTo("v1"));
            Assert.That(fixture.Reads.Single().Digest, Is.EqualTo(original.FindVersion("v1")!.Digest));
        }

        [Test]
        public async Task NamespaceRegistrationWithoutInstalledTypesIsUnavailable()
        {
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:table-only");
            var server = new Mock<IServerInternal>();
            server.SetupGet(instance => instance.NamespaceUris).Returns(namespaces);
            server.SetupGet(instance => instance.TypeTree).Returns(new TypeTable(namespaces));
            var resolver = new AddressSpaceWotNodeResolver(server.Object);

            Assert.That(await resolver.HoldsNamespaceAsync("urn:table-only"), Is.False);
        }

        [Test]
        public async Task NamespaceTableDoesNotSatisfyRequiredModel()
        {
            using var fixture = new SelectionFixture();
            await fixture.RegisterAsync("a");
            fixture.Converter.RequiredNamespace = "urn:table-only";
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:table-only");
            fixture.Coordinator.ServerNamespaceUris = namespaces;

            WotRefreshResult result = await fixture.Coordinator.RefreshAsync(new WotRefreshRequest());

            Assert.Multiple(() =>
            {
                Assert.That(result.Results.Single().Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
                Assert.That(result.Results.Single().Phase, Is.EqualTo(WoTPhaseEnum.DependencyResolution));
                Assert.That(result.Results.Single().Message, Does.Contain("urn:table-only"));
                Assert.That(fixture.Host.Operations, Is.Empty);
            });
        }

        private static WotRefreshRequest Select(WotResource resource, string versionId = "")
        {
            return new WotRefreshRequest
            {
                Selection = [new WoTResourceSelectorDataType
                {
                    Kind = resource.Kind,
                    GroupId = resource.GroupId,
                    ResourceId = resource.ResourceId,
                    VersionId = versionId
                }],
                Options = new WoTRefreshOptionsDataType
                {
                    IncludeDependents = false,
                    Atomicity = WoTAtomicityEnum.PerClosure
                }
            };
        }

        private static byte[] ReferenceDocument(string source, string target)
        {
            return Encoding.UTF8.GetBytes($$"""
                {
                  "@context": ["https://www.w3.org/2022/wot/td/v1.1", {"ua": "http://opcfoundation.org/UA/"}],
                  "id": "urn:{{source}}",
                  "title": "{{source}}",
                  "links": [{"rel": "ua:ConnectedTo", "href": "urn:{{target}}"}]
                }
                """);
        }

        private sealed class SelectionFixture : IDisposable
        {
            public SelectionFixture(WotRegistryPersistenceBounds? bounds = null)
            {
                Registry = new WotRegistryService(bounds: bounds);
                var registry = new Mock<IWotRegistryService>();
                registry.SetupGet(service => service.Current).Returns(() => Registry.Current);
                registry.SetupGet(service => service.Bounds).Returns(Registry.Bounds);
                registry.Setup(service => service.ReadContentAsync(
                    It.IsAny<WotResourceVersion>(), It.IsAny<CancellationToken>()))
                    .Returns(ReadAsync);
                registry.Setup(service => service.ApplyProjectionResultsAsync(
                    It.IsAny<IReadOnlyList<WotResourceProjection>>(), It.IsAny<CancellationToken>()))
                    .Returns((IReadOnlyList<WotResourceProjection> rows, CancellationToken token) =>
                        Registry.ApplyProjectionResultsAsync(rows, token));
                registry.As<IWotRegistryVersionLeaseProvider>().Setup(service => service.AcquireVersionLeaseAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WotResourceVersion>(),
                    It.IsAny<CancellationToken>()))
                    .Returns((string group, string resource, WotResourceVersion version, CancellationToken token) =>
                        Registry.AcquireVersionLeaseAsync(group, resource, version, token));
                var converter = new Mock<IWotDocumentConverter>();
                converter.Setup(service => service.ConvertAsync(
                    It.IsAny<WotResource>(), It.IsAny<ByteString>(), It.IsAny<WotRegistrySnapshot>(),
                    It.IsAny<IReadOnlyDictionary<string, ByteString>>(), It.IsAny<CancellationToken>()))
                    .Returns((WotResource resource, ByteString content, WotRegistrySnapshot snapshot,
                        IReadOnlyDictionary<string, ByteString> contents, CancellationToken token) =>
                    {
                        Converted.Add(resource.ResourceId);
                        return Converter.ConvertAsync(resource, content, snapshot, contents, token);
                    });
                var binders = new Mock<IWotBinderRegistry>();
                binders.SetupGet(service => service.Capabilities).Returns(NullWotBinderRegistry.Instance.Capabilities);
                binders.Setup(service => service.Prepare(It.IsAny<WotBindingPlanRequest>()))
                    .Returns((WotBindingPlanRequest request) =>
                    {
                        Prepared.Add(request.ResourceXid);
                        return NullWotBinderRegistry.Instance.Prepare(request);
                    });
                binders.Setup(service => service.ActivateAsync(
                    It.IsAny<WotBindingPlan>(), It.IsAny<CancellationToken>()))
                    .Callback((WotBindingPlan plan, CancellationToken _) => Activated.Add(plan.ResourceXid));
                binders.Setup(service => service.DeactivateAsync(
                    It.IsAny<WotBindingPlan>(), It.IsAny<CancellationToken>()))
                    .Callback((WotBindingPlan plan, CancellationToken _) => Deactivated.Add(plan.ResourceXid));
                Coordinator = new WotMaterializationCoordinator(
                    registry.Object, Host, binders.Object, documentConverter: converter.Object);
            }

            public WotRegistryService Registry { get; }
            public WotMaterializationCoordinator Coordinator { get; }
            public FakeWotProjectionHost Host { get; } = new();
            public FakeWotDocumentConverter Converter { get; } = new();
            public List<WotResourceVersion> Reads { get; } = [];
            public List<string> Converted { get; } = [];
            public List<string> Prepared { get; } = [];
            public List<string> Activated { get; } = [];
            public List<string> Deactivated { get; } = [];
            public string? FailedDocument { get; set; }
            public Func<WotResourceVersion, Task>? BeforeRead { get; set; }

            public async Task<WotResource> RegisterAsync(
                string resourceId,
                WoTDocumentKindEnum kind = WoTDocumentKindEnum.ThingDescription,
                string versionId = "v1",
                byte[]? content = null,
                string? groupId = null)
            {
                WotRegistryMutationResult result = await Registry.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = groupId ?? (kind == WoTDocumentKindEnum.ThingModel ? "models" : "things"),
                    ResourceId = resourceId,
                    VersionId = versionId,
                    Kind = kind,
                    Content = ByteString.From(content ?? (kind == WoTDocumentKindEnum.ThingModel
                        ? TestMaterialization.Tm("urn:" + resourceId, versionId)
                        : TestMaterialization.Td("urn:" + resourceId, versionId)))
                });
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success), result.Message);
                return result.Resource!;
            }

            public void ClearObservations()
            {
                Reads.Clear();
                Converted.Clear();
                Prepared.Clear();
                Activated.Clear();
                Deactivated.Clear();
                Host.Operations.Clear();
            }

            public void Dispose()
            {
                Coordinator.Dispose();
                Registry.Dispose();
            }

            private async ValueTask<ByteString> ReadAsync(WotResourceVersion version, CancellationToken token)
            {
                Reads.Add(version);
                if (BeforeRead is not null)
                {
                    await BeforeRead(version);
                }
                if (version.DocumentId == FailedDocument)
                {
                    throw new IOException("Injected independent resource acquisition failure.");
                }
                return await Registry.ReadContentAsync(version, token);
            }
        }

        private static readonly string[] s_aDocument = ["urn:a"];
        private static readonly string[] s_aResource = ["a"];
        private static readonly string[] s_shadow = ["shadow"];
    }
}
