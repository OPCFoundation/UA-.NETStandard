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
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Tests.Registry
{
    [TestFixture]
    public sealed class WotRegistryIdentityTests
    {
        [Test]
        public async Task ExactCatalogueHasIndependentTdAndTmAllocationsBeforeContent()
        {
            var bytes = new Mock<IXRegistryResourceStore>(MockBehavior.Strict);
            var store = new ObservedStore(bytes.Object);
            using var service = new WotRegistryService(store);
            const string catalogue = "HTTPS://Contoso.org/Plant%2FOne/?q=A#F";

            WotDocumentGroupResult td = await service.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, catalogue).ConfigureAwait(false);
            WotDocumentGroupResult tm = await service.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingModel, catalogue).ConfigureAwait(false);
            WotDocumentGroupResult repeated = await service.GetOrCreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, catalogue).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(td.Group.GroupId, Does.StartWith("td."));
                Assert.That(tm.Group.GroupId, Does.StartWith("tm."));
                Assert.That(td.Group.GroupId, Is.Not.EqualTo(tm.Group.GroupId).IgnoreCase);
                Assert.That(td.Group.CatalogUri, Is.EqualTo(catalogue));
                Assert.That(tm.Group.CatalogUri, Is.EqualTo(catalogue));
                Assert.That(td.Group.Name, Is.EqualTo(catalogue));
                Assert.That(tm.Group.Name, Is.EqualTo(catalogue));
                Assert.That(td.Created && tm.Created, Is.True);
                Assert.That(repeated.Created, Is.False);
                Assert.That(repeated.Group, Is.SameAs(td.Group));
                Assert.That(store.CommitCount, Is.EqualTo(2));
                Assert.That(bytes.Invocations, Is.Empty);
            });
        }

        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, false, true)]
        [TestCase(true, true, true)]
        public async Task LegacyGroupCreationRejectsCaseCollidingTypedAssignment(
            bool fileBacked,
            bool tryCreate,
            bool lowercaseAssignment)
        {
            string root = Path.Combine(Path.GetTempPath(), "wric-id-" + Guid.NewGuid().ToString("N"));
            try
            {
                var blobs = new Mock<IXRegistryResourceStore>(MockBehavior.Strict);
                using FileWotRegistryStore? fileStore = fileBacked
                    ? new FileWotRegistryStore(root, blobs.Object) : null;
                IWotRegistryStore? persistent = fileStore;
                IWotRegistryStore inner = persistent ?? new InMemoryWotRegistryStore();
                var store = new Mock<IWotRegistryStore>(MockBehavior.Strict);
                store.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>()))
                    .Returns((CancellationToken ct) => inner.LoadAsync(ct));
                store.Setup(value => value.CommitAsync(
                    It.IsAny<WotRegistrySnapshot>(), It.IsAny<CancellationToken>()))
                    .Returns((WotRegistrySnapshot snapshot, CancellationToken ct) => inner.CommitAsync(snapshot, ct));
                using var service = new WotRegistryService(store.Object);
                await service.InitializeAsync().ConfigureAwait(false);
                int changes = 0;
                service.Changed += (_, _) => Interlocked.Increment(ref changes);
                string catalogue = lowercaseAssignment ? "urn:review:case" : "urn:Review:Case";
                WotDocumentGroupResult typed = await service.CreateDocumentGroupAsync(
                    WoTDocumentKindEnum.ThingDescription, catalogue).ConfigureAwait(false);
                WotRegistrySnapshot before = service.Current;
                string manifestPath = Path.Combine(root, "manifest.json");
                byte[] manifest = fileBacked ? File.ReadAllBytes(manifestPath) : [];
                WotResourceGroup exact = await service.GetOrCreateGroupAsync(typed.Group.GroupId, typed.Group.Kind)
                    .ConfigureAwait(false);
                WotResourceGroup? duplicate = await service.TryCreateGroupAsync(typed.Group.GroupId, typed.Group.Kind)
                    .ConfigureAwait(false);
                string collision = lowercaseAssignment
                    ? typed.Group.GroupId.ToUpperInvariant() : typed.Group.GroupId.ToLowerInvariant();
                Assert.Multiple(() =>
                {
                    Assert.That(typed.Group.GroupId,
                        Is.EqualTo(lowercaseAssignment ? "td.urn.review.case" : "td.urn.Review.Case"));
                    Assert.That(collision,
                        Is.EqualTo(lowercaseAssignment ? "TD.URN.REVIEW.CASE" : "td.urn.review.case"));
                    Assert.That(exact, Is.SameAs(typed.Group));
                    Assert.That(duplicate, Is.Null);
                    Assert.That(service.Current, Is.SameAs(before));
                });

                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    if (tryCreate)
                    {
                        await service.TryCreateGroupAsync(collision, typed.Group.Kind).ConfigureAwait(false);
                    }
                    else
                    {
                        await service.GetOrCreateGroupAsync(collision, typed.Group.Kind).ConfigureAwait(false);
                    }
                })!;

                using var restarted = new WotRegistryService(inner);
                await restarted.InitializeAsync().ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
                    Assert.That(service.Current, Is.SameAs(before));
                    Assert.That(changes, Is.EqualTo(1));
                    Assert.That(restarted.Current.Generation, Is.EqualTo(before.Generation));
                    Assert.That(restarted.Current.Groups, Has.Count.EqualTo(1));
                    Assert.That(restarted.Current.FindGroup(collision), Is.Null);
                    Assert.That(restarted.Current.FindGroup(typed.Group.GroupId)!.CatalogUri,
                        Is.EqualTo(catalogue));
                    Assert.That(blobs.Invocations, Is.Empty);
                    if (fileBacked)
                    {
                        Assert.That(File.ReadAllBytes(manifestPath), Is.EqualTo(manifest));
                    }
                });
                store.Verify(value => value.CommitAsync(
                    It.IsAny<WotRegistrySnapshot>(), It.IsAny<CancellationToken>()), Times.Once);
                string otherCatalogue = lowercaseAssignment ? "urn:Review:Case" : "urn:review:case";
                WotDocumentGroupResult different = await service.GetOrCreateDocumentGroupAsync(
                    typed.Group.Kind, otherCatalogue).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(different.Created, Is.True);
                    Assert.That(different.Group.GroupId, Is.Not.EqualTo(typed.Group.GroupId).IgnoreCase);
                    Assert.That(different.Group.CatalogUri, Is.EqualTo(otherCatalogue));
                    Assert.That(service.Current.FindGroup(typed.Group.GroupId)!.CatalogUri,
                        Is.EqualTo(catalogue));
                });
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [TestCase("")]
        [TestCase("relative/catalogue")]
        [TestCase("urn-a-b")]
        [TestCase(" https://contoso.org/a")]
        [TestCase("https://contoso.org/a ")]
        [TestCase("https://contoso.org/a b")]
        [TestCase("https://contoso.org/%zz")]
        [TestCase("https://contoso.org\\a")]
        public void InvalidCatalogueRejectsBeforeStoreOrContentIo(string catalogue)
        {
            var bytes = new Mock<IXRegistryResourceStore>(MockBehavior.Strict);
            var store = new ObservedStore(bytes.Object);
            using var service = new WotRegistryService(store);
            WotRegistrySnapshot before = service.Current;

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await service.GetOrCreateDocumentGroupAsync(WoTDocumentKindEnum.ThingDescription, catalogue)
                    .ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(service.Current, Is.SameAs(before));
                Assert.That(store.CommitCount, Is.Zero);
                Assert.That(bytes.Invocations, Is.Empty);
            });
        }

        [TestCase(-1)]
        [TestCase(2)]
        [TestCase(3)]
        public void SelectorAndUndefinedKindsCannotAllocate(int kind)
        {
            var store = new ObservedStore();
            using var service = new WotRegistryService(store);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await service.CreateDocumentGroupAsync((WoTDocumentKindEnum)kind, "urn:catalogue")
                    .ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(service.Current.Groups, Is.Empty);
                Assert.That(store.CommitCount, Is.Zero);
            });
        }

        [TestCase("https://contoso.org/A", "https://contoso.org/a")]
        [TestCase("http://contoso.org/a", "https://contoso.org/a")]
        [TestCase("https://contoso.org/a", "https://contoso.org/a/")]
        [TestCase("https://contoso.org?x=1", "https://contoso.org?x=2")]
        [TestCase("https://contoso.org#A", "https://contoso.org#a")]
        [TestCase("https://contoso.org/%41", "https://contoso.org/A")]
        [TestCase("https://contoso.org/a%2Fb", "https://contoso.org/a%2fb")]
        [TestCase("urn:a:b", "urn:a.b")]
        public async Task ExactAuthorityPartitionsAllocateDistinctResourcesAndReuseTheirMappings(
            string firstSource,
            string secondSource)
        {
            using var service = new WotRegistryService();
            WotResourceGroup group = (await service.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:catalogue").ConfigureAwait(false)).Group;
            WotDocumentResourceResult first = await service.GetOrCreateDocumentResourceAsync(
                group.GroupId, group.Kind, firstSource).ConfigureAwait(false);
            WotDocumentResourceResult second = await service.GetOrCreateDocumentResourceAsync(
                group.GroupId, group.Kind, secondSource).ConfigureAwait(false);
            long generation = service.Current.Generation;
            WotDocumentResourceResult repeated = await service.GetOrCreateDocumentResourceAsync(
                group.GroupId, group.Kind, firstSource).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first.Resource.ResourceId, Is.Not.EqualTo(second.Resource.ResourceId).IgnoreCase);
                Assert.That(first.Resource.SourceId, Is.EqualTo(firstSource));
                Assert.That(second.Resource.SourceId, Is.EqualTo(secondSource));
                Assert.That(first.Resource.Name, Is.EqualTo(firstSource));
                Assert.That(second.Resource.Name, Is.EqualTo(secondSource));
                Assert.That(first.Version.DocumentId, Is.EqualTo(firstSource));
                Assert.That(first.Version.HasContent, Is.False);
                Assert.That(second.Version.HasContent, Is.False);
                Assert.That(repeated.Resource.ResourceId, Is.EqualTo(first.Resource.ResourceId));
                Assert.That(repeated.CreatedResource || repeated.CreatedVersion, Is.False);
                Assert.That(service.Current.Generation, Is.EqualTo(generation));
            });
        }

        [Test]
        public async Task ConcurrentRequestsAndRestartReuseOneCommittedResourceAllocation()
        {
            var store = new ObservedStore();
            using var service = new WotRegistryService(store);
            WotResourceGroup group = (await service.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingModel, "urn:models").ConfigureAwait(false)).Group;
            store.PauseNextCommit();
            Task<WotDocumentResourceResult> first = service.GetOrCreateDocumentResourceAsync(
                group.GroupId, group.Kind, "urn:Model:A", "v1").AsTask();
            await store.CommitEntered.ConfigureAwait(false);
            Task<WotDocumentResourceResult>[] followers = [.. Enumerable.Range(0, 12).Select(_ =>
                service.GetOrCreateDocumentResourceAsync(group.GroupId, group.Kind, "urn:Model:A", "v1").AsTask())];
            Assert.That(followers.All(task => !task.IsCompleted), Is.True);
            store.ReleaseCommit();
            WotDocumentResourceResult[] results = await Task.WhenAll(followers.Prepend(first)).ConfigureAwait(false);
            using var restarted = new WotRegistryService(store);
            await restarted.InitializeAsync().ConfigureAwait(false);
            WotDocumentResourceResult restored = await restarted.GetOrCreateDocumentResourceAsync(
                group.GroupId, group.Kind, "urn:Model:A", "v1").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(results.Count(result => result.CreatedResource), Is.EqualTo(1));
                Assert.That(results.Count(result => result.CreatedVersion), Is.EqualTo(1));
                Assert.That(results.Select(result => result.Resource.ResourceId).Distinct().ToArray(),
                    Has.Length.EqualTo(1));
                Assert.That(restored.Resource.ResourceId, Is.EqualTo(results[0].Resource.ResourceId));
                Assert.That(restored.Resource.SourceId, Is.EqualTo("urn:Model:A"));
                Assert.That(restored.CreatedResource || restored.CreatedVersion, Is.False);
                Assert.That(store.CommitCount, Is.EqualTo(2));
            });
        }

        [Test]
        public async Task CreateAndGetOrCreateUseExactVersionsAndIndependentCreationFlags()
        {
            using var service = new WotRegistryService();
            WotResourceGroup group = (await service.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:catalogue").ConfigureAwait(false)).Group;
            WotDocumentResourceResult first = await service.CreateDocumentResourceAsync(
                group.GroupId, group.Kind, "urn:thing").ConfigureAwait(false);
            WotDocumentResourceResult next = await service.CreateDocumentResourceAsync(
                group.GroupId, group.Kind, "urn:thing").ConfigureAwait(false);
            WotDocumentResourceResult selected = await service.GetOrCreateDocumentResourceAsync(
                group.GroupId, group.Kind, "urn:thing").ConfigureAwait(false);
            WotDocumentResourceResult explicitVersion = await service.GetOrCreateDocumentResourceAsync(
                group.GroupId, group.Kind, "urn:thing", "V3").ConfigureAwait(false);
            WotDocumentResourceResult repeated = await service.GetOrCreateDocumentResourceAsync(
                group.GroupId, group.Kind, "urn:thing", "V3").ConfigureAwait(false);
            WotRegistrySnapshot beforeDuplicate = service.Current;
            ServiceResultException duplicate = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await service.CreateDocumentResourceAsync(group.GroupId, group.Kind, "urn:thing", "V3")
                    .ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(first.CreatedResource && first.CreatedVersion, Is.True);
                Assert.That(next.CreatedResource, Is.False);
                Assert.That(next.CreatedVersion, Is.True);
                Assert.That(next.Version.VersionId, Is.Not.EqualTo(first.Version.VersionId));
                Assert.That(next.Resource.ResourceId, Is.EqualTo(first.Resource.ResourceId));
                Assert.That(selected.Version.VersionId, Is.EqualTo(first.Version.VersionId));
                Assert.That(selected.CreatedResource || selected.CreatedVersion, Is.False);
                Assert.That(explicitVersion.CreatedResource, Is.False);
                Assert.That(explicitVersion.CreatedVersion, Is.True);
                Assert.That(explicitVersion.Version.VersionId, Is.EqualTo("V3"));
                Assert.That(repeated.CreatedResource || repeated.CreatedVersion, Is.False);
                Assert.That(repeated.Resource.DefaultVersionId, Is.EqualTo(first.Version.VersionId));
                Assert.That(duplicate.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
                Assert.That(service.Current, Is.SameAs(beforeDuplicate));
            });
        }

        [TestCase("")]
        [TestCase("relative")]
        [TestCase("urn-a-b")]
        [TestCase("https://contoso.org/%zz")]
        [TestCase("https://contoso.org/a ")]
        public async Task InvalidResourceAuthorityRejectsBeforeAllocation(string source)
        {
            var bytes = new Mock<IXRegistryResourceStore>(MockBehavior.Strict);
            var store = new ObservedStore(bytes.Object);
            using var service = new WotRegistryService(store);
            WotResourceGroup group = (await service.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingModel, "urn:models").ConfigureAwait(false)).Group;
            WotRegistrySnapshot before = service.Current;

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await service.GetOrCreateDocumentResourceAsync(group.GroupId, group.Kind, source)
                    .ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(service.Current, Is.SameAs(before));
                Assert.That(store.CommitCount, Is.EqualTo(1));
                Assert.That(bytes.Invocations, Is.Empty);
            });
        }

        [Test]
        public async Task ConcurrentGroupsCommitOneAllocationPerKindAndRestoreCreationFlags()
        {
            var store = new ObservedStore();
            using var service = new WotRegistryService(store);
            store.PauseNextCommit();
            Task<WotDocumentGroupResult> first = service.GetOrCreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:shared:catalogue").AsTask();
            await store.CommitEntered.ConfigureAwait(false);
            Task<WotDocumentGroupResult>[] followers = [.. Enumerable.Range(0, 12).Select(index =>
                service.GetOrCreateDocumentGroupAsync(
                    (WoTDocumentKindEnum)(index % 2), "urn:shared:catalogue").AsTask())];
            Assert.That(followers.All(task => !task.IsCompleted), Is.True);
            store.ReleaseCommit();
            WotDocumentGroupResult[] results = await Task.WhenAll(followers.Prepend(first)).ConfigureAwait(false);
            using var restarted = new WotRegistryService(store);
            await restarted.InitializeAsync().ConfigureAwait(false);

            foreach (WoTDocumentKindEnum kind in new[]
                { WoTDocumentKindEnum.ThingDescription, WoTDocumentKindEnum.ThingModel })
            {
                WotDocumentGroupResult restored = await restarted.GetOrCreateDocumentGroupAsync(
                    kind, "urn:shared:catalogue").ConfigureAwait(false);
                Assert.That(restored.Created, Is.False);
                Assert.That(restored.Group.GroupId, Is.EqualTo(results.First(result => result.Group.Kind == kind)
                    .Group.GroupId));
            }
            Assert.Multiple(() =>
            {
                Assert.That(results.Count(result => result.Created), Is.EqualTo(2));
                Assert.That(restarted.Current.Groups, Has.Count.EqualTo(2));
                Assert.That(store.CommitCount, Is.EqualTo(2));
            });
        }

        [TestCase(123)]
        [TestCase(124)]
        [TestCase(125)]
        public async Task PublicAllocationHonorsBeforeAtAndAfter128Characters(int pathLength)
        {
            using var service = new WotRegistryService();
            WotResourceGroup group = (await service.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:catalogue").ConfigureAwait(false)).Group;
            string source = "urn:" + new string('x', pathLength);
            WotDocumentResourceResult result = await service.CreateDocumentResourceAsync(
                group.GroupId, group.Kind, source).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Resource.ResourceId, Has.Length.LessThanOrEqualTo(128));
                Assert.That(result.Resource.SourceId, Is.EqualTo(source));
                Assert.That(result.Resource.Name, Is.EqualTo(source));
                Assert.That(result.Version.DocumentId, Is.EqualTo(source));
                Assert.That(result.Version.HasContent, Is.False);
            });
            if (pathLength <= 124)
            {
                Assert.That(result.Resource.ResourceId, Is.EqualTo("urn." + new string('x', pathLength)));
            }
            else
            {
                Assert.That(result.Resource.ResourceId, Has.Length.EqualTo(128));
                Assert.That(result.Resource.ResourceId, Does.StartWith("urn." + new string('x', 115) + "."));
            }
        }

        [TestCase(WoTDocumentKindEnum.ThingDescription)]
        [TestCase(WoTDocumentKindEnum.ThingModel)]
        public async Task TypedAndLegacyCreationRejectWrongGroupKindBeforeCommit(WoTDocumentKindEnum kind)
        {
            var store = new ObservedStore();
            using var service = new WotRegistryService(store);
            WotResourceGroup group = (await service.CreateDocumentGroupAsync(kind, "urn:catalogue")
                .ConfigureAwait(false)).Group;
            WoTDocumentKindEnum other = kind == WoTDocumentKindEnum.ThingDescription
                ? WoTDocumentKindEnum.ThingModel : WoTDocumentKindEnum.ThingDescription;
            WotRegistrySnapshot before = service.Current;

            ServiceResultException typed = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await service.CreateDocumentResourceAsync(group.GroupId, other, "urn:wrong").ConfigureAwait(false))!;
            ServiceResultException legacy = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await service.GetOrCreateGroupAsync(group.GroupId, other).ConfigureAwait(false))!;
            ServiceResultException version = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await service.GetOrCreateVersionAsync(group.GroupId, "wrong", "v1", other).ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(typed.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(legacy.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(version.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(service.Current, Is.SameAs(before));
                Assert.That(store.CommitCount, Is.EqualTo(1));
            });
        }

        [TestCase("different")]
        [TestCase("missing")]
        [TestCase("relative")]
        [TestCase("duplicate")]
        [TestCase("oppositeKind")]
        [TestCase("malformed")]
        public async Task InvalidUploadAuthorityCannotReplaceEstablishedBytes(string failure)
        {
            var store = new ObservedStore();
            using var service = new WotRegistryService(store);
            WotResourceGroup group = (await service.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:catalogue").ConfigureAwait(false)).Group;
            WotDocumentResourceResult created = await service.CreateDocumentResourceAsync(
                group.GroupId, group.Kind, "urn:thing", "v1").ConfigureAwait(false);
            WotUpsertResourceRequest original = Request(group, created.Resource.ResourceId, "urn:thing", "v1");
            Assert.That((await service.UpsertResourceAsync(original)
                .ConfigureAwait(false)).Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            WotRegistrySnapshot before = service.Current;
            int commits = store.CommitCount;
            JsonObject document = JsonNode.Parse(Encoding.UTF8.GetString(original.Content.Span.ToArray()))!.AsObject();
            switch (failure)
            {
                case "different":
                    document["id"] = "urn:other";
                    break;
                case "missing":
                    document.Remove("id");
                    break;
                case "relative":
                    document["id"] = "relative";
                    break;
                case "duplicate":
                    document["@id"] = "urn:thing";
                    break;
                case "oppositeKind":
                    document["@type"] = "tm:ThingModel";
                    break;
            }
            WotUpsertResourceRequest invalid = Request(group, created.Resource.ResourceId, "urn:thing", "v1");
            invalid.Content = ByteString.From(Encoding.UTF8.GetBytes(
                failure == "malformed" ? "{" : document.ToJsonString()));

            WotRegistryMutationResult result = await service.UpsertResourceAsync(invalid).ConfigureAwait(false);
            WotResourceVersion version = service.Current.FindResource(
                group.GroupId, created.Resource.ResourceId)!.FindVersion("v1")!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(service.Current, Is.SameAs(before));
                Assert.That(store.CommitCount, Is.EqualTo(commits));
                Assert.That(version.DocumentId, Is.EqualTo("urn:thing"));
            });
            Assert.That(await service.ReadContentAsync(version).ConfigureAwait(false), Is.EqualTo(original.Content));
        }

        [Test]
        public async Task FailedCommitLeavesNoAllocationAndRetryReportsCreation()
        {
            var store = new ObservedStore();
            using var service = new WotRegistryService(store);
            WotResourceGroup group = (await service.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:catalogue").ConfigureAwait(false)).Group;
            WotRegistrySnapshot before = service.Current;
            store.FailNextCommit = true;

            Assert.ThrowsAsync<IOException>(async () =>
                await service.CreateDocumentResourceAsync(group.GroupId, group.Kind, "urn:thing")
                    .ConfigureAwait(false));
            Assert.That(service.Current, Is.SameAs(before));
            WotDocumentResourceResult retry = await service.CreateDocumentResourceAsync(
                group.GroupId, group.Kind, "urn:thing").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(retry.CreatedResource && retry.CreatedVersion, Is.True);
                Assert.That(retry.Resource.ResourceId, Is.EqualTo("urn.thing"));
                Assert.That(retry.Resource.SourceId, Is.EqualTo("urn:thing"));
                Assert.That(retry.Resource.Versions, Has.Length.EqualTo(1));
            });
        }

        [Test]
        public async Task NamesCannotSupplyMissingGenericAuthority()
        {
            var store = new ObservedStore();
            using var service = new WotRegistryService(store);
            await service.GetOrCreateGroupAsync(
                "legacy", WoTDocumentKindEnum.ThingModel, name: "urn:catalogue").ConfigureAwait(false);
            WotRegistrySnapshot before = service.Current;

            ServiceResultException missing = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await service.ProvisionConfiguredGroupAsync("legacy", true, default).ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(service.Current, Is.SameAs(before));
                Assert.That(store.CommitCount, Is.EqualTo(1));
            });
        }

        internal static WotUpsertResourceRequest Request(
            WotResourceGroup group,
            string resourceId,
            string sourceId,
            string versionId)
        {
            var document = new JsonObject
            {
                ["@context"] = "https://www.w3.org/2022/wot/td/v1.1",
                ["@type"] = group.Kind == WoTDocumentKindEnum.ThingModel ? "tm:ThingModel" : "uav:object",
                ["id"] = sourceId,
                ["title"] = "A readable title"
            };
            return new WotUpsertResourceRequest
            {
                GroupId = group.GroupId,
                ResourceId = resourceId,
                Kind = group.Kind,
                VersionId = versionId,
                SetAsDefault = false,
                Content = ByteString.From(Encoding.UTF8.GetBytes(document.ToJsonString()))
            };
        }

        private sealed class ObservedStore : IWotRegistryStore, IWotRegistryResourceStoreProvider
        {
            public ObservedStore(IXRegistryResourceStore? content = null)
            {
                ResourceStore = content ?? m_inner.ResourceStore;
            }

            public IXRegistryResourceStore ResourceStore { get; }
            public int CommitCount { get; private set; }
            public bool FailNextCommit { get; set; }
            public Task CommitEntered => m_entered!.Task;

            public ValueTask<WotRegistrySnapshot> LoadAsync(CancellationToken cancellationToken = default)
            {
                return m_inner.LoadAsync(cancellationToken);
            }

            public async ValueTask CommitAsync(
                WotRegistrySnapshot snapshot,
                CancellationToken cancellationToken = default)
            {
                CommitCount++;
                if (FailNextCommit)
                {
                    FailNextCommit = false;
                    throw new IOException("Injected pre-commit failure.");
                }
                if (m_release is { } release)
                {
                    m_entered!.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    m_release = null;
                }
                cancellationToken.ThrowIfCancellationRequested();
                await m_inner.CommitAsync(snapshot, cancellationToken).ConfigureAwait(false);
            }

            public void PauseNextCommit()
            {
                m_entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public void ReleaseCommit()
            {
                m_release!.TrySetResult(true);
            }

            private readonly InMemoryWotRegistryStore m_inner = new();
            private TaskCompletionSource<bool>? m_entered;
            private TaskCompletionSource<bool>? m_release;
        }
    }
}
