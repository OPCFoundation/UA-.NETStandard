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
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Registry
{
    [TestFixture]
    public sealed class FileWotRegistryIdentityTests
    {
        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(Path.GetTempPath(), "wot-identities-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_root);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(m_root, recursive: true);
        }

        [Test]
        public async Task RestartRestoresExactAuthoritiesBeforeAnyDocumentExists()
        {
            const string catalogue = "https://Contoso.org/catalogue/?q=A#fragment";
            const string source = "urn:Example:A%2fb";
            WotDocumentResourceResult created;
            WotResourceGroup group;
            using (var store = new FileWotRegistryStore(m_root))
            using (var service = new WotRegistryService(store))
            {
                await service.InitializeAsync().ConfigureAwait(false);
                group = (await service.CreateDocumentGroupAsync(WoTDocumentKindEnum.ThingModel, catalogue)
                    .ConfigureAwait(false)).Group;
                created = await service.CreateDocumentResourceAsync(group.GroupId, group.Kind, source, "First")
                    .ConfigureAwait(false);
            }
            using var restartedStore = new FileWotRegistryStore(m_root);
            using var restarted = new WotRegistryService(restartedStore);
            await restarted.InitializeAsync().ConfigureAwait(false);
            long generation = restarted.Current.Generation;
            WotDocumentGroupResult sameGroup = await restarted.GetOrCreateDocumentGroupAsync(group.Kind, catalogue)
                .ConfigureAwait(false);
            WotDocumentResourceResult sameResource = await restarted.GetOrCreateDocumentResourceAsync(
                sameGroup.Group.GroupId, group.Kind, source, "First").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(sameGroup.Created, Is.False);
                Assert.That(sameGroup.Group.GroupId, Is.EqualTo(group.GroupId));
                Assert.That(sameGroup.Group.CatalogUri, Is.EqualTo(catalogue));
                Assert.That(sameGroup.Group.Name, Is.EqualTo(catalogue));
                Assert.That(sameResource.CreatedResource || sameResource.CreatedVersion, Is.False);
                Assert.That(sameResource.Resource.ResourceId, Is.EqualTo(created.Resource.ResourceId));
                Assert.That(sameResource.Resource.SourceId, Is.EqualTo(source));
                Assert.That(sameResource.Version.DocumentId, Is.EqualTo(source));
                Assert.That(sameResource.Version.VersionId, Is.EqualTo("First"));
                Assert.That(sameResource.Version.HasContent, Is.False);
                Assert.That(restarted.Current.Generation, Is.EqualTo(generation));
                Assert.That(Directory.GetFiles(m_root, "*.json", SearchOption.AllDirectories), Has.Length.EqualTo(1));
            });
        }

        [TestCase("catalogue")]
        [TestCase("groupMarker")]
        [TestCase("source")]
        [TestCase("resourceMarker")]
        [TestCase("falseResourceMarker")]
        [TestCase("falseGroupMarker")]
        [TestCase("default")]
        [TestCase("owner")]
        [TestCase("kind")]
        [TestCase("versionSource")]
        [TestCase("duplicateSource")]
        [TestCase("duplicateAssignment")]
        [TestCase("duplicateCatalogue")]
        public async Task CorruptAuthorityMapFailsClosedOnLoad(string fault)
        {
            using (var store = new FileWotRegistryStore(m_root))
            using (var service = new WotRegistryService(store))
            {
                await service.InitializeAsync().ConfigureAwait(false);
                WotResourceGroup group = (await service.CreateDocumentGroupAsync(
                    WoTDocumentKindEnum.ThingDescription, "urn:catalogue").ConfigureAwait(false)).Group;
                await service.CreateDocumentResourceAsync(group.GroupId, group.Kind, "urn:source", "v1")
                    .ConfigureAwait(false);
            }
            string path = Path.Combine(m_root, "manifest.json");
            JsonObject manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            JsonObject groupNode = manifest["Groups"]![0]!.AsObject();
            JsonObject resourceNode = groupNode["Resources"]![0]!.AsObject();
            switch (fault)
            {
                case "catalogue":
                    groupNode.Remove("CatalogUri");
                    break;
                case "groupMarker":
                    groupNode.Remove("AuthorityEstablished");
                    break;
                case "source":
                    resourceNode.Remove("ThingId");
                    break;
                case "resourceMarker":
                    resourceNode.Remove("AuthorityEstablished");
                    break;
                case "falseResourceMarker":
                    resourceNode["AuthorityEstablished"] = false;
                    break;
                case "falseGroupMarker":
                    groupNode["AuthorityEstablished"] = false;
                    break;
                case "default":
                    resourceNode["DefaultVersionId"] = "missing";
                    break;
                case "owner":
                    resourceNode["GroupId"] = "different";
                    break;
                case "kind":
                    resourceNode["Kind"] = 1;
                    break;
                case "versionSource":
                    resourceNode["Versions"]![0]!["DocumentId"] = "urn:different";
                    break;
                case "duplicateSource":
                case "duplicateAssignment":
                    JsonObject resourceCopy = resourceNode.DeepClone().AsObject();
                    resourceCopy["ResourceId"] = fault == "duplicateSource"
                        ? "another"
                        : resourceNode["ResourceId"]!.GetValue<string>().ToUpperInvariant();
                    groupNode["Resources"]!.AsArray().Add(resourceCopy);
                    break;
                case "duplicateCatalogue":
                    JsonObject groupCopy = groupNode.DeepClone().AsObject();
                    groupCopy["GroupId"] = "another";
                    groupCopy["Resources"] = null;
                    manifest["Groups"]!.AsArray().Add(groupCopy);
                    break;
            }
            File.WriteAllText(path, manifest.ToJsonString());
            using var reopened = new FileWotRegistryStore(m_root);

            Assert.ThrowsAsync<InvalidDataException>(async () => await reopened.LoadAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task ExplicitLegacyBindingPreservesAnUnambiguousAssignedIdentifier()
        {
            using (var store = new FileWotRegistryStore(m_root))
            using (var legacy = new WotRegistryService(store))
            {
                await legacy.InitializeAsync().ConfigureAwait(false);
                WotResourceGroup group = await legacy.GetOrCreateGroupAsync(
                    "legacy-catalog", WoTDocumentKindEnum.ThingModel).ConfigureAwait(false);
                await legacy.UpsertResourceAsync(WotRegistryIdentityTests.Request(
                    group, "old-model-id", "urn:Model:Authority", "v1")).ConfigureAwait(false);
            }
            var bindings = new WotRegistryIdentityBindings
            {
                Groups = [new WotRegistryGroupIdentity(
                    "legacy-catalog", WoTDocumentKindEnum.ThingModel, "urn:catalogue")]
            };
            using var restartedStore = new FileWotRegistryStore(m_root);
            using var service = new WotRegistryService(restartedStore, null, bindings);
            await service.InitializeAsync().ConfigureAwait(false);
            WotDocumentGroupResult bound = await service.GetOrCreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingModel, "urn:catalogue").ConfigureAwait(false);
            WotDocumentResourceResult resource = await service.GetOrCreateDocumentResourceAsync(
                bound.Group.GroupId, bound.Group.Kind, "urn:Model:Authority", "v1").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(bound.Created, Is.False);
                Assert.That(bound.Group.GroupId, Is.EqualTo("legacy-catalog"));
                Assert.That(bound.Group.CatalogUri, Is.EqualTo("urn:catalogue"));
                Assert.That(resource.Resource.ResourceId, Is.EqualTo("old-model-id"));
                Assert.That(resource.Resource.SourceId, Is.EqualTo("urn:Model:Authority"));
                Assert.That(resource.CreatedResource || resource.CreatedVersion, Is.False);
            });
        }

        private string m_root = null!;
    }
}
