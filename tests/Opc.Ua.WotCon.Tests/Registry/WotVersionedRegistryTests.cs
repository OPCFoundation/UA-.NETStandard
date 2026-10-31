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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Wot;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Registry
{
    /// <summary>
    /// Verifies store publication, dependency floors and optional-field model encoding.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    public sealed class WotVersionedRegistryTests
    {
        private static readonly string[] s_authoredVersions = ["1.0.0", "1.1.0", "1.0.1"];
        private static readonly string[] s_generatedVersions = ["0.0.0-1", "0.0.0-2"];

        [TestCase("tm:extends")]
        [TestCase("tm:submodel")]
        public void LinkDependencyRetainsItsFloor(string relation)
        {
            ByteString content = ByteString.From(Encoding.UTF8.GetBytes(
                "{\"links\":[{\"rel\":\"" + relation +
                "\",\"href\":\"urn:parent\",\"tm:minVersion\":\"1.2.0\"}]}"));
            WotResourceDependencies metadata = WotDependencyGraph.ReadMetadata(content, 64);
            Assert.That(metadata.Error, Is.Empty);
            Assert.That(metadata.References.Count, Is.EqualTo(1));
            Assert.That(metadata.References[0].RequiredVersion, Is.EqualTo("1.2.0"));
        }

        [Test]
        public void ReferenceDependencyRetainsItsFloor()
        {
            ByteString content = ByteString.From(Encoding.UTF8.GetBytes(
                "{\"properties\":{\"p\":{\"tm:ref\":\"urn:parent#/properties/p\"," +
                "\"tm:minVersion\":\"1.2.0\"}}}"));
            WotResourceDependencies metadata = WotDependencyGraph.ReadMetadata(content, 64);
            Assert.That(metadata.Error, Is.Empty);
            Assert.That(metadata.References[0].RequiredVersion, Is.EqualTo("1.2.0"));
        }

        [TestCase("td:minVersion", "")]
        [TestCase("tm:minVersion", "invalid-floor")]
        public void ThingDescriptionReferenceUsesItsTargetFloorTerm(string floorTerm, string expectedError)
        {
            ByteString content = ByteString.From(Encoding.UTF8.GetBytes(
                "{\"@context\":{\"td\":\"https://www.w3.org/2019/wot/td#\"}," +
                "\"id\":\"urn:consumer\",\"links\":[{\"rel\":\"td:extends\"," +
                "\"href\":\"urn:base\",\"" + floorTerm + "\":\"1.2.0\"}]}"));

            WotResourceDependencies metadata = WotDependencyGraph.ReadMetadata(content, 64);

            Assert.That(metadata.Error, expectedError.Length == 0
                ? Is.Empty
                : Does.Contain(expectedError));
            if (expectedError.Length == 0)
            {
                Assert.That(metadata.References.ToList().Single().RequiredVersion, Is.EqualTo("1.2.0"));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DependencyOptionalFloorRoundTrips(bool present)
        {
            var value = new WoTDependencyDataType
            {
                SourceXid = "source",
                TargetXid = "target/versions/1.2.0",
                TargetUri = "urn:parent",
                RefType = "tm:extends",
                Resolved = true,
                RequiredVersion = "1.2.0",
                EncodingMask = present ? (uint)WoTDependencyDataTypeFields.RequiredVersion : 0
            };
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(null!);
            byte[] bytes;
            using (var encoder = new BinaryEncoder(context))
            {
                value.Encode(encoder);
                bytes = encoder.CloseAndReturnBuffer()!;
            }
            using var decoder = new BinaryDecoder(bytes, context);
            var decoded = new WoTDependencyDataType();
            decoded.Decode(decoder);
            Assert.Multiple(() =>
            {
                Assert.That(decoded.SourceXid, Is.EqualTo("source"));
                Assert.That(decoded.TargetXid, Is.EqualTo(value.TargetXid));
                Assert.That(decoded.TargetUri, Is.EqualTo(value.TargetUri));
                Assert.That(decoded.RefType, Is.EqualTo(value.RefType));
                Assert.That(decoded.Resolved, Is.True);
                Assert.That(decoded.EncodingMask, Is.EqualTo(value.EncodingMask));
                Assert.That(decoded.RequiredVersion, Is.EqualTo(present ? "1.2.0" : null));
            });
        }

        [Test]
        public async Task PublicationUsesAuthoredSemVerAndNeverReplacesHigherDefaultWithLatePatchAsync()
        {
            using var service = new WotRegistryService();
            foreach (string version in s_authoredVersions)
            {
                WotRegistryMutationResult result = await service.UpsertResourceAsync(Request(version))
                    .ConfigureAwait(false);
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            }
            WotResource resource = service.Current.FindResource(WotRegistryGroups.ThingModels, "pump")!;
            Assert.That(resource.Versions.Select(version => version.VersionId),
                Is.EquivalentTo(s_authoredVersions));
            Assert.That(resource.DefaultVersionId, Is.EqualTo("1.1.0"));
            await service.SetDefaultVersionAsync(WotRegistryGroups.ThingModels, "pump", "1.0.0")
                .ConfigureAwait(false);
            await service.UpsertResourceAsync(Request("1.2.0")).ConfigureAwait(false);
            Assert.That(service.Current.FindResource(WotRegistryGroups.ThingModels, "pump")!.DefaultVersionId,
                Is.EqualTo("1.0.0"));
        }

        [TestCase("1.2.0", "1.1.0", "version-mismatch")]
        [TestCase("1.2.0+build", null, "invalid-version")]
        public async Task PublicationRejectsInvalidAssignmentAsync(string model, string? supplied, string error)
        {
            using var service = new WotRegistryService();
            WotUpsertResourceRequest request = Request(model);
            request.VersionId = supplied;
            WotRegistryMutationResult result = await service.UpsertResourceAsync(request).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(result.Diagnostics.Concat(new[] { result.Message }), Does.Contain(error));
        }

        [Test]
        public async Task UnversionedPublicationGetsLowestFreeGeneratedIdAsync()
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(Request(null)).ConfigureAwait(false);
            WotUpsertResourceRequest second = Request("not-semver");
            await service.UpsertResourceAsync(second).ConfigureAwait(false);
            WotResource resource = service.Current.FindResource(WotRegistryGroups.ThingModels, "pump")!;
            Assert.That(resource.Versions.Select(version => version.VersionId),
                Is.EquivalentTo(s_generatedVersions));
        }

        [TestCase("1.0.0", "2.0.0")]
        [TestCase("2.0.0", "2.0.0")]
        [TestCase("2.1.0-beta", "3.0.0-beta")]
        public async Task StoredReferencesSelectHighestEligibleVersionAsync(string floor, string expected)
        {
            using var service = new WotRegistryService();
            foreach (string version in new[] { "1.0.0", "2.0.0", "3.0.0-beta" })
            {
                await service.UpsertResourceAsync(Request(version)).ConfigureAwait(false);
            }
            WotRegistryMutationResult source = await service.UpsertResourceAsync(
                Consumer("urn:pump:v1", floor)).ConfigureAwait(false);
            var closures = await WotDependencyGraph.BuildClosuresAsync(service.Current, [source.Resource!], 64,
                service.ReadContentAsync, CancellationToken.None).ConfigureAwait(false);
            WotResource model = closures.Single().Members.Single(member => member.ResourceId == "pump");
            Assert.That(model.DefaultVersionId, Is.EqualTo(expected));
            Assert.That(closures.Single().Dependencies.Single().RequiredVersion, Is.EqualTo(floor));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task StoredReferencesNeverOverrideAnUnsatisfiedStickyDefaultAsync(bool exactPin)
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(Request("1.0.0")).ConfigureAwait(false);
            await service.UpsertResourceAsync(Request("2.0.0")).ConfigureAwait(false);
            await service.SetDefaultVersionAsync(WotRegistryGroups.ThingModels, "pump", "1.0.0")
                .ConfigureAwait(false);
            WotResource model = service.Current.FindResource(WotRegistryGroups.ThingModels, "pump")!;
            WotRegistryMutationResult source = await service.UpsertResourceAsync(
                Consumer(exactPin ? model.Xid + "/versions/1.0.0" : "urn:pump:v1", "2.0.0"))
                .ConfigureAwait(false);
            if (exactPin)
            {
                var closures = await WotDependencyGraph.BuildClosuresAsync(service.Current, [source.Resource!], 64,
                    service.ReadContentAsync, CancellationToken.None).ConfigureAwait(false);
                Assert.That(closures.Single().Members.Single(member => member.ResourceId == "pump").DefaultVersionId,
                    Is.EqualTo("1.0.0"));
            }
            else
            {
                var closures = await WotDependencyGraph.BuildClosuresAsync(service.Current, [source.Resource!], 64,
                    service.ReadContentAsync, CancellationToken.None).ConfigureAwait(false);
                WotDependencyClosure closure = closures.Single();
                Assert.That(closure.IsProjectable, Is.False);
                Assert.That(closure.AcquisitionFailures.ToList().Single().Message, Does.Contain("unsatisfied"));
            }
            Assert.That(service.Current.FindResource(WotRegistryGroups.ThingModels, "pump")!.DefaultVersionId,
                Is.EqualTo("1.0.0"));
        }

        [Test]
        public async Task UnsatisfiedFloorFailsOnlyItsOwnClosureAndNeverBlocksDeletionAsync()
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(Request("1.0.0")).ConfigureAwait(false);
            WotRegistryMutationResult other = await service.UpsertResourceAsync(
                Request("1.0.0", "other", "urn:other:v1")).ConfigureAwait(false);
            WotRegistryMutationResult source = await service.UpsertResourceAsync(
                Consumer("urn:pump:v1", "2.0.0")).ConfigureAwait(false);

            var closures = await WotDependencyGraph.BuildClosuresAsync(service.Current,
                [source.Resource!, other.Resource!], 64, service.ReadContentAsync, CancellationToken.None)
                .ConfigureAwait(false);
            WotDependencyClosure failed = closures.Single(closure =>
                closure.Members.Any(member => member.ResourceId == "consumer"));
            WotDependencyClosure independent = closures.Single(closure =>
                closure.Members.Any(member => member.ResourceId == "other"));
            WotResource pump = service.Current.FindResource(WotRegistryGroups.ThingModels, "pump")!;
            WotDependentSet dependents = await WotDependencyGraph.FindDependentsWithFaultsAsync(service.Current,
                pump, 64, service.ReadContentAsync, CancellationToken.None).ConfigureAwait(false);
            WotDeleteResult deleted = await service.DeleteResourceAsync(WotRegistryGroups.ThingModels, "other",
                WoTDeletePolicyEnum.Force).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(failed.IsProjectable, Is.False);
                Assert.That(failed.AcquisitionFailures.ToList().Single().Message, Does.Contain("unsatisfied"));
                Assert.That(independent.IsProjectable, Is.True);
                Assert.That(dependents.Dependents.Select(dependent => dependent.Xid),
                    Does.Contain(source.Resource!.Xid));
                Assert.That(deleted.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(service.Current.FindResource(WotRegistryGroups.ThingModels, "other"), Is.Null);
            });
        }

        [Test]
        public async Task ATypeLinkPinnedToAVersionIgnoresTheDocumentFloorAsync()
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(Request("1.0.0")).ConfigureAwait(false);
            await service.UpsertResourceAsync(Request("1.1.0")).ConfigureAwait(false);
            WotResource model = service.Current.FindResource(WotRegistryGroups.ThingModels, "pump")!;
            WotRegistryMutationResult source = await service.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "consumer",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(Encoding.UTF8.GetBytes(
                    "{\"@type\":\"Thing\",\"id\":\"urn:consumer\",\"version\":{\"model\":\"invalid\"}," +
                    "\"links\":[{\"rel\":\"type\",\"href\":\"" + model.Xid + "/versions/1.0.0\"}]}"))
            }).ConfigureAwait(false);
            Assert.That(source.Resource!.DefaultVersion!.Dependencies!.Error, Is.Empty);
            var closures = await WotDependencyGraph.BuildClosuresAsync(service.Current, [source.Resource], 64,
                service.ReadContentAsync, CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(closures.Single().IsProjectable, Is.True);
                Assert.That(closures.Single().Members.Single(member => member.ResourceId == "pump").DefaultVersionId,
                    Is.EqualTo("1.0.0"));
            });
        }

        [Test]
        public async Task AssignedVersionIdThatDiffersOnlyByCaseIsRejectedAsync()
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(Request("1.0.0-alpha")).ConfigureAwait(false);
            WotRegistrySnapshot before = service.Current;
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await service.UpsertResourceAsync(Request("1.0.0-ALPHA")).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
                Assert.That(service.Current, Is.SameAs(before));
            });
        }

        [Test]
        public void LegacyNonSemVerVersionIdsNeitherBlockAssignmentNorWinSelection()
        {
            ArrayOf<WotResourceSnapshot> resources =
            [
                new WotResourceSnapshot("urn:pump:v1", false, "v1",
                [
                    new WotVersionSnapshot("v1", "authored", "sha256:1", null),
                    new WotVersionSnapshot("1.0.0", "authored", "sha256:2", "1.0.0")
                ])
            ];
            WotPublicationAssignment authored =
                WotVersionResolver.AssignPublishedVersion(resources, "urn:pump:v1", null, "1.1.0");
            WotPublicationAssignment generated =
                WotVersionResolver.AssignPublishedVersion(resources, "urn:pump:v1", null, null);
            WotResolutionResult selected = WotVersionResolver.Resolve(resources,
                new WotReferenceRequest("tm:extends", "urn:pump:v1", "1.0.0"), "urn:test");
            Assert.Multiple(() =>
            {
                Assert.That(authored.VersionId, Is.EqualTo("1.1.0"));
                Assert.That(authored.DefaultVersionId, Is.EqualTo("1.1.0"));
                Assert.That(generated.VersionId, Is.EqualTo("0.0.0-1"));
                Assert.That(selected.VersionId, Is.EqualTo("1.0.0"));
            });
        }

        [TestCase(null)]
        [TestCase("1.2.0")]
        public void DependencyProjectionSetsTheOptionalFloorBit(string? floor)
        {
            var observation = new WotDependencySnapshot("1.0.0", 1, "request",
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), false,
                default, [new WotDependency("source", "urn:target", null, "tm:extends", false)
                { RequiredVersion = floor }], []);
            WoTDependencyDataType edge = observation.ToDataType().Edges[0];
            Assert.That(edge.RequiredVersion, Is.EqualTo(floor ?? string.Empty));
            Assert.That(edge.EncodingMask, Is.EqualTo(floor is null ? 0 :
                (uint)WoTDependencyDataTypeFields.RequiredVersion));
        }

        [Test]
        public async Task GeneratedPlaceholderCanReceiveItsUnversionedModelAsync()
        {
            using var service = new WotRegistryService();
            var created = await service.GetOrCreateVersionAsync(WotRegistryGroups.ThingModels,
                "pump", string.Empty, WoTDocumentKindEnum.ThingModel).ConfigureAwait(false);
            Assert.That(created.Version.VersionId, Is.EqualTo("0.0.0-1"));
            WotUpsertResourceRequest request = Request(null);
            request.VersionId = created.Version.VersionId;
            WotRegistryMutationResult result = await service.UpsertResourceAsync(request).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(result.Resource!.Versions, Has.Length.EqualTo(1));
            Assert.That(result.Resource.Versions[0].HasContent, Is.True);
        }

        [Test]
        public async Task PersistentStoreRetainsFloorsAndOperatorDefaultPolicyAsync()
        {
            string root = Path.Combine(TestContext.CurrentContext.TestDirectory,
                "versioned-registry-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var store = new FileWotRegistryStore(root))
                using (var service = new WotRegistryService(store))
                {
                    await service.InitializeAsync().ConfigureAwait(false);
                    await service.UpsertResourceAsync(Request("1.0.0")).ConfigureAwait(false);
                    await service.UpsertResourceAsync(Request("2.0.0")).ConfigureAwait(false);
                    await service.SetDefaultVersionAsync(WotRegistryGroups.ThingModels, "pump", "1.0.0")
                        .ConfigureAwait(false);
                    await service.UpsertResourceAsync(Consumer("urn:pump:v1", "1.2.0"))
                        .ConfigureAwait(false);
                }
                using var reloadStore = new FileWotRegistryStore(root);
                using var reloaded = new WotRegistryService(reloadStore);
                await reloaded.InitializeAsync().ConfigureAwait(false);
                WotResource model = reloaded.Current.FindResource(WotRegistryGroups.ThingModels, "pump")!;
                Assert.That(model.DefaultVersionSticky, Is.True);
                Assert.That(model.DefaultVersionId, Is.EqualTo("1.0.0"));
                WotResource source = reloaded.Current.FindResource(WotRegistryGroups.ThingDescriptions, "consumer")!;
                Assert.That(source.DefaultVersion!.Dependencies!.References[0].RequiredVersion, Is.EqualTo("1.2.0"));
                await reloaded.UpsertResourceAsync(Request("3.0.0")).ConfigureAwait(false);
                Assert.That(reloaded.Current.FindResource(WotRegistryGroups.ThingModels, "pump")!.DefaultVersionId,
                    Is.EqualTo("1.0.0"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public async Task ThingDescriptionPublicationUsesAndPersistsInstanceVersionAsync()
        {
            string root = Path.Combine(TestContext.CurrentContext.TestDirectory,
                "td-instance-version-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var store = new FileWotRegistryStore(root))
                using (var service = new WotRegistryService(store))
                {
                    await service.InitializeAsync().ConfigureAwait(false);
                    WotRegistryMutationResult result = await service.UpsertResourceAsync(
                        ThingDescriptionRequest("2.3.4")).ConfigureAwait(false);

                    Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                    WotResourceVersion version = result.Resource!.Versions.Single();
                    Assert.Multiple(() =>
                    {
                        Assert.That(version.VersionId, Is.EqualTo("2.3.4"));
                        Assert.That(version.ModelVersion, Is.EqualTo("1.2.0"));
                        Assert.That(version.InstanceVersion, Is.EqualTo("2.3.4"));
                    });
                }

                using var reloadStore = new FileWotRegistryStore(root);
                using var reloaded = new WotRegistryService(reloadStore);
                await reloaded.InitializeAsync().ConfigureAwait(false);
                WotResourceVersion persisted = reloaded.Current
                    .FindResource(WotRegistryGroups.ThingDescriptions, "consumer")!
                    .Versions.Single();
                Assert.Multiple(() =>
                {
                    Assert.That(persisted.VersionId, Is.EqualTo("2.3.4"));
                    Assert.That(persisted.ModelVersion, Is.EqualTo("1.2.0"));
                    Assert.That(persisted.InstanceVersion, Is.EqualTo("2.3.4"));
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

        [Test]
        public async Task ThingDescriptionPublicationRejectsVersionIdThatDiffersFromInstanceVersionAsync()
        {
            using var service = new WotRegistryService();
            WotUpsertResourceRequest request = ThingDescriptionRequest("2.3.4");
            request.VersionId = "2.3.5";

            WotRegistryMutationResult result = await service.UpsertResourceAsync(request).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(result.Diagnostics.Concat(new[] { result.Message }), Does.Contain("version-mismatch"));
        }

        private static WotUpsertResourceRequest Consumer(string target, string floor)
        {
            return new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "consumer",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(Encoding.UTF8.GetBytes(
                    "{\"@type\":\"Thing\",\"id\":\"urn:consumer\",\"links\":[{\"rel\":\"tm:extends\"," +
                    "\"href\":\"" + target + "\",\"tm:minVersion\":\"" + floor + "\"}]}"))
            };
        }

        private static WotUpsertResourceRequest ThingDescriptionRequest(string instanceVersion)
        {
            return new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "consumer",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(Encoding.UTF8.GetBytes(
                    "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"]," +
                    "\"@type\":\"Thing\",\"id\":\"urn:consumer\"," +
                    "\"version\":{\"model\":\"1.2.0\",\"instance\":\"" + instanceVersion + "\"}}"))
            };
        }

        private static WotUpsertResourceRequest Request(string? model, string resourceId = "pump",
            string id = "urn:pump:v1")
        {
            string version = model is null ? string.Empty : ",\"version\":{\"model\":\"" + model + "\"}";
            return new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingModels,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingModel,
                Content = ByteString.From(Encoding.UTF8.GetBytes(
                    "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                    "{\"tm\":\"https://www.w3.org/2019/wot/tm#\"}],\"@type\":\"tm:ThingModel\"," +
                    "\"id\":\"" + id + "\"" + version + "}"))
            };
        }
    }
}
