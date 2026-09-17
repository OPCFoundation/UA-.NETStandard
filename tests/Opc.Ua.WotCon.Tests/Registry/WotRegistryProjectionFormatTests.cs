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
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Materialization;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Tests.Registry
{
    [TestFixture]
    [Category("WoT")]
    public sealed class WotRegistryProjectionFormatTests
    {
        [TestCase("WoT-TD/1.1", "application/td+json")]
        [TestCase("WoT-TM/1.1", "application/tm+json")]
        [TestCase(WotProjection.Format, "application/td+json")]
        [TestCase("WoT-TD/1.1", WotProjection.ContentType)]
        [TestCase("WoT-Projection/9.9", WotProjection.ContentType)]
        [TestCase("", WotProjection.ContentType)]
        [TestCase(WotProjection.Format, "")]
        public async Task PlansRequireBothExplicitProjectionFormatFields(string format, string contentType)
        {
            using var service = new WotRegistryService();
            WotUpsertResourceRequest request = Request(WoTDocumentKindEnum.ThingDescription);
            request.Format = format;
            request.ContentType = contentType;
            int changes = 0;
            service.Changed += (_, _) => changes++;

            WotRegistryMutationResult result = await service.UpsertResourceAsync(request).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(result.Resource, Is.Null);
            Assert.That(result.Diagnostics, Is.Not.Empty);
            Assert.That(service.Current.Generation, Is.Zero);
            Assert.That(service.Current.FindGroup("plans"), Is.Null);
            Assert.That(changes, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExplicitFormatDetectionPreservesTheDeclaredKindGuard(bool wrongKind)
        {
            using var service = new WotRegistryService();
            WotUpsertResourceRequest request = Request(WoTDocumentKindEnum.ThingModel);
            request.Format = "WoT-TD/1.1";
            request.ContentType = "application/td+json";
            request.DetectProjectionFormat = true;
            if (wrongKind)
            {
                request.Kind = WoTDocumentKindEnum.ThingDescription;
            }

            WotRegistryMutationResult result = await service.UpsertResourceAsync(request).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(wrongKind ? WoTOutcomeEnum.Rejected : WoTOutcomeEnum.Success));
            if (wrongKind)
            {
                Assert.That(service.Current.Generation, Is.Zero);
                Assert.That(result.Diagnostics, Is.Not.Empty);
            }
            else
            {
                Assert.That(result.Resource!.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingModel));
                Assert.That(result.Resource.DefaultVersion!.Format, Is.EqualTo(WotProjection.Format));
                Assert.That(result.Resource.DefaultVersion.ContentType, Is.EqualTo(WotProjection.ContentType));
            }
        }

        [TestCase(WoTDocumentKindEnum.ThingDescription)]
        [TestCase(WoTDocumentKindEnum.ThingModel)]
        public async Task ProjectionKindControlsTheStoredSubtypeWithoutRewritingBytes(WoTDocumentKindEnum kind)
        {
            using var service = new WotRegistryService();
            WotUpsertResourceRequest request = Request(kind);

            WotRegistryMutationResult result = await service.UpsertResourceAsync(request).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(result.Resource!.Kind, Is.EqualTo(kind));
            WotResourceVersion version = result.Resource.DefaultVersion!;
            Assert.That(version.Format, Is.EqualTo(WotProjection.Format));
            Assert.That(version.ContentType, Is.EqualTo(WotProjection.ContentType));
            ByteString actual = await service.ReadContentAsync(version).ConfigureAwait(false);
            Assert.That(actual, Is.EqualTo(request.Content));
            WoTValidationOutcomeDataType validation = await service.ValidateVersionAsync(
                "plans", "view", version.VersionId).ConfigureAwait(false);
            Assert.That(validation.FormatValidated, Is.True);
            Assert.That(validation.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(validation.CompatibilityValidated, Is.False);
        }

        [TestCase(WoTDocumentKindEnum.ThingDescription, WoTDocumentKindEnum.ThingModel)]
        [TestCase(WoTDocumentKindEnum.ThingModel, WoTDocumentKindEnum.ThingDescription)]
        public async Task ProjectionResultKindCannotDisagreeWithTheResource(
            WoTDocumentKindEnum declared, WoTDocumentKindEnum requested)
        {
            using var service = new WotRegistryService();
            WotUpsertResourceRequest request = Request(declared);
            request.Kind = requested;

            WotRegistryMutationResult result = await service.UpsertResourceAsync(request).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(service.Current.Generation, Is.Zero);
            Assert.That(result.Diagnostics, Is.Not.Empty);
        }

        [TestCase("Thing")]
        [TestCase("tm:ThingModel")]
        public async Task ProjectionFormatDoesNotAdmitAnOrdinaryDocument(string type)
        {
            using var service = new WotRegistryService();
            JsonObject document = Plan(WoTDocumentKindEnum.ThingDescription);
            document["@type"] = type;
            document.Remove("uav:projectionKind");
            document.Remove("uav:projects");
            WotUpsertResourceRequest request = Request(WoTDocumentKindEnum.ThingDescription);
            request.Content = ByteString.From(Encoding.UTF8.GetBytes(document.ToJsonString()));

            WotRegistryMutationResult result = await service.UpsertResourceAsync(request).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(service.Current.Generation, Is.Zero);
        }

        [TestCase("uav:projectionKind")]
        [TestCase("uav:scenario")]
        [TestCase("uav:projects")]
        public async Task CurrentPlanAdmissionRequiresItsCompletePlanHeader(string missing)
        {
            using var service = new WotRegistryService();
            JsonObject document = Plan(WoTDocumentKindEnum.ThingDescription);
            document.Remove(missing);
            WotUpsertResourceRequest request = Request(WoTDocumentKindEnum.ThingDescription);
            request.Content = ByteString.From(Encoding.UTF8.GetBytes(document.ToJsonString()));

            WotRegistryMutationResult result = await service.UpsertResourceAsync(request).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(service.Current.Generation, Is.Zero);
            Assert.That(result.Diagnostics, Is.Not.Empty);
        }

        [TestCase(WoTDocumentKindEnum.ThingDescription)]
        [TestCase(WoTDocumentKindEnum.ThingModel)]
        public async Task LegacyRegistryAdmissionRequiresExplicitCompatibilityAndPreservesBytes(WoTDocumentKindEnum kind)
        {
            JsonObject document = Plan(kind);
            document.Remove("uav:projectionKind");
            ((JsonArray)document["@type"]!).Add(kind == WoTDocumentKindEnum.ThingModel ? "tm:ThingModel" : "Thing");
            WotUpsertResourceRequest request = Request(kind);
            request.Content = ByteString.From(Encoding.UTF8.GetBytes(document.ToJsonString()));
            using var strict = new WotRegistryService();
            using var compatible = new WotRegistryService(null, null, WotProjectionCompatibilityMode.DraftProjection11);

            WotRegistryMutationResult rejected = await strict.UpsertResourceAsync(request).ConfigureAwait(false);
            WotRegistryMutationResult accepted = await compatible.UpsertResourceAsync(request).ConfigureAwait(false);

            Assert.That(rejected.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(strict.Current.Generation, Is.Zero);
            Assert.That(accepted.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(accepted.Resource!.Kind, Is.EqualTo(kind));
            ByteString stored = await compatible.ReadContentAsync(accepted.Resource.DefaultVersion!).ConfigureAwait(false);
            Assert.That(stored, Is.EqualTo(request.Content));
            WoTValidationOutcomeDataType validation = await compatible.ValidateResourceAsync("plans", "view")
                .ConfigureAwait(false);
            Assert.That(validation.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Success));
        }

        [TestCase(-1)]
        [TestCase(2)]
        [TestCase(int.MaxValue)]
        public void UndefinedRegistryCompatibilityModesAreRejected(int value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new WotRegistryService(null, null, (WotProjectionCompatibilityMode)value));
        }

        [TestCase(WoTDocumentKindEnum.ThingDescription, WoTDocumentKindEnum.ThingModel)]
        [TestCase(WoTDocumentKindEnum.ThingModel, WoTDocumentKindEnum.ThingDescription)]
        public async Task ProjectionCannotChangeTheKindOfAnExistingGroup(
            WoTDocumentKindEnum existing, WoTDocumentKindEnum incoming)
        {
            using var service = new WotRegistryService();
            await service.GetOrCreateGroupAsync("plans", existing).ConfigureAwait(false);
            long generation = service.Current.Generation;

            WotRegistryMutationResult result = await service.UpsertResourceAsync(Request(incoming)).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(service.Current.Generation, Is.EqualTo(generation));
            Assert.That(service.Current.FindGroup("plans")!.Kind, Is.EqualTo(existing));
            Assert.That(service.Current.FindResource("plans", "view"), Is.Null);
        }

        [TestCase("WoT-TD/1.1", "application/td+json", WoTDocumentKindEnum.ThingDescription)]
        [TestCase(WotProjection.Format, "application/td+json", WoTDocumentKindEnum.ThingDescription)]
        [TestCase(WotProjection.Format, WotProjection.ContentType, WoTDocumentKindEnum.ThingModel)]
        public async Task RestoredPlanAdmissionRunsBeforeAnyRuntimePublication(
            string format, string contentType, WoTDocumentKindEnum storedKind)
        {
            var store = new InMemoryWotRegistryStore();
            using var service = new WotRegistryService(store);
            await service.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "plans",
                ResourceId = "source",
                Content = ByteString.From(TestMaterialization.Td("urn:registry:projection:source"))
            }).ConfigureAwait(false);
            WotRegistryMutationResult accepted = await service.UpsertResourceAsync(
                Request(WoTDocumentKindEnum.ThingDescription)).ConfigureAwait(false);
            WotResource resource = accepted.Resource!;
            WotResourceVersion original = resource.DefaultVersion!;
            var replacedVersion = new WotResourceVersion(
                original.VersionId, original.Digest, original.ContentLength, contentType, format,
                original.CreatedAt, original.ModifiedAt)
            {
                DocumentId = original.DocumentId,
                Title = original.Title
            };
            var replaced = new WotResource(
                resource.GroupId, resource.ResourceId, storedKind, [replacedVersion],
                defaultVersionId: replacedVersion.VersionId, thingId: resource.ThingId, title: resource.Title);
            WotResourceGroup group = service.Current.FindGroup("plans")!;
            WotRegistrySnapshot restored = service.Current.WithGroup(
                group.WithResources(group.Resources.SetItem(resource.ResourceId, replaced), group.Epoch),
                service.Current.Generation + 1);
            await store.CommitAsync(restored).ConfigureAwait(false);
            await service.InitializeAsync().ConfigureAwait(false);

            WoTValidationOutcomeDataType validation = await service.ValidateResourceAsync("plans", "view")
                .ConfigureAwait(false);
            Assert.That(validation.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Failed));
            var host = new FakeWotProjectionHost();
            var viewHost = new InMemoryWotViewProjectionHost();
            using var coordinator = new WotMaterializationCoordinator(
                service, host, documentConverter: new FakeWotDocumentConverter(), viewProjectionHost: viewHost);

            WotRefreshResult refresh = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.That(host.Operations, Is.Empty, "An invalid stored plan cannot publish its sources first.");
            Assert.That(viewHost.Applied, Is.Empty);
            WoTResourceLoadResultDataType result = refresh.Results.Single(value => value.ResourceId == "view");
            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
            Assert.That(result.Phase, Is.EqualTo(WoTPhaseEnum.FormatValidation));
            Assert.That(result.LoadState, Is.EqualTo(WoTLoadStateEnum.Failed));
        }

        [Test]
        public async Task ChangingContentTypeForIdenticalBytesInvalidatesValidationAndRuntimeAdmission()
        {
            using var service = new WotRegistryService();
            var request = new WotUpsertResourceRequest
            {
                GroupId = "ordinary",
                ResourceId = "source",
                Content = ByteString.From(TestMaterialization.Td("urn:source"))
            };
            WotRegistryMutationResult created = await service.UpsertResourceAsync(request).ConfigureAwait(false);
            WotResourceVersion original = created.Resource!.DefaultVersion!;
            var host = new FakeWotProjectionHost();
            using var coordinator = new WotMaterializationCoordinator(
                service, host, documentConverter: new FakeWotDocumentConverter());
            await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            WoTValidationOutcomeDataType validation = await service.ValidateResourceAsync("ordinary", "source")
                .ConfigureAwait(false);
            Assert.That(validation.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Success));

            request.ContentType = "application/td+json; charset=utf-8";
            WotRegistryMutationResult changed = await service.UpsertResourceAsync(request).ConfigureAwait(false);

            Assert.That(changed.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            WotResource resource = changed.Resource!;
            Assert.That(resource.DefaultVersion!.Digest, Is.EqualTo(original.Digest));
            Assert.That(resource.DefaultVersion.VersionId, Is.EqualTo(original.VersionId));
            Assert.That(resource.DefaultVersion.Validation, Is.Null, "Format validation also depends on ContentType.");
            Assert.That(resource.Validation, Is.Null);
            Assert.That(resource.LoadState, Is.EqualTo(WoTLoadStateEnum.Unloaded));
            WotRefreshResult refreshed = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            Assert.That(refreshed.Results.Single().Outcome, Is.Not.EqualTo(WoTOutcomeEnum.Unchanged));
            Assert.That(host.ShadowCount, Is.EqualTo(1));
        }

        [TestCase("unchanged")]
        [TestCase("contentType")]
        [TestCase("incarnation")]
        public async Task ValidationPublicationRemainsBoundToTheReadVersion(string mutation)
        {
            var persisted = new InMemoryWotRegistryStore();
            var blobStore = new Mock<IXRegistryResourceStore>(MockBehavior.Strict);
            blobStore.Setup(value => value.WriteAsync(
                    It.IsAny<string>(), It.IsAny<long>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((string key, long offset, ByteString data, CancellationToken ct) =>
                    persisted.ResourceStore.WriteAsync(key, offset, data, ct));
            var store = new ValidationStore(persisted, blobStore.Object);
            using var service = new WotRegistryService(store);
            var request = new WotUpsertResourceRequest
            {
                GroupId = "ordinary",
                ResourceId = "source",
                VersionId = "v1",
                Content = ByteString.From(TestMaterialization.Td("urn:source"))
            };
            WotRegistryMutationResult created = await service.UpsertResourceAsync(request).ConfigureAwait(false);
            WotResourceVersion original = created.Resource!.DefaultVersion!;
            var readStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var readResult = new TaskCompletionSource<ByteString>(TaskCreationOptions.RunContinuationsAsynchronously);
            blobStore.Setup(value => value.ReadAsync(
                    original.DigestHex, 0, request.Content.Length, It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    readStarted.SetResult(true);
                    return new ValueTask<ByteString>(readResult.Task);
                });

            Task<WoTValidationOutcomeDataType> validating =
                service.ValidateVersionAsync("ordinary", "source", "v1").AsTask();
            try
            {
                Assert.That(readStarted.Task.IsCompleted, Is.True);
                if (mutation == "contentType")
                {
                    request.ContentType = "application/td+json; charset=utf-8";
                    await service.UpsertResourceAsync(request).ConfigureAwait(false);
                }
                else if (mutation == "incarnation")
                {
                    await service.DeleteResourceAsync("ordinary", "source").ConfigureAwait(false);
                    await service.UpsertResourceAsync(request).ConfigureAwait(false);
                }
            }
            finally
            {
                readResult.TrySetResult(request.Content);
            }

            if (mutation == "unchanged")
            {
                WoTValidationOutcomeDataType result = await validating.ConfigureAwait(false);
                Assert.That(result.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(service.Current.FindResource("ordinary", "source")!.DefaultVersion!.Validation,
                    Is.Not.Null);
            }
            else
            {
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await validating.ConfigureAwait(false))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(service.Current.FindResource("ordinary", "source")!.DefaultVersion!.Validation, Is.Null);
            }
        }

        private static WotUpsertResourceRequest Request(WoTDocumentKindEnum kind)
        {
            return new WotUpsertResourceRequest
            {
                GroupId = "plans",
                ResourceId = "view",
                Kind = kind,
                Format = WotProjection.Format,
                ContentType = WotProjection.ContentType,
                Content = ByteString.From(Encoding.UTF8.GetBytes(Plan(kind).ToJsonString()))
            };
        }

        private static JsonObject Plan(WoTDocumentKindEnum kind)
        {
            return new JsonObject
            {
                ["@context"] = "https://www.w3.org/2022/wot/td/v1.1",
                ["@type"] = new JsonArray("uav:projection"),
                ["id"] = "urn:registry:projection:view",
                ["title"] = "Projection view",
                ["uav:projectionKind"] = kind == WoTDocumentKindEnum.ThingModel ? "ThingModel" : "ThingDescription",
                ["uav:scenario"] = "urn:scenario:projection",
                ["uav:projects"] = new JsonArray(new JsonObject
                {
                    ["uav:sourceName"] = "source",
                    ["href"] = "urn:registry:projection:source",
                    ["type"] = "application/td+json",
                    ["uav:selectAll"] = true
                })
            };
        }

        private sealed class ValidationStore(
            IWotRegistryStore inner,
            IXRegistryResourceStore resourceStore) : IWotRegistryStore, IWotRegistryResourceStoreProvider
        {
            public IXRegistryResourceStore ResourceStore { get; } = resourceStore;

            public ValueTask<WotRegistrySnapshot> LoadAsync(CancellationToken cancellationToken = default)
            {
                return inner.LoadAsync(cancellationToken);
            }

            public ValueTask CommitAsync(
                WotRegistrySnapshot snapshot, CancellationToken cancellationToken = default)
            {
                return inner.CommitAsync(snapshot, cancellationToken);
            }
        }
    }
}
