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
                WotVersionException? error = await Assert.ThrowsAsync<WotVersionException>(async () =>
                    await WotDependencyGraph.BuildClosuresAsync(service.Current, [source.Resource!], 64,
                        service.ReadContentAsync, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(error!.Code, Is.EqualTo("unsatisfied"));
            }
            Assert.That(service.Current.FindResource(WotRegistryGroups.ThingModels, "pump")!.DefaultVersionId,
                Is.EqualTo("1.0.0"));
        }

        [TestCase(null)]
        [TestCase("1.2.0")]
        public void DependencyProjectionSetsTheOptionalFloorBit(string? floor)
        {
            var observation = new WotDependencySnapshot("1.0.0", 1, "request", DateTime.UnixEpoch, false,
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

        private static WotUpsertResourceRequest Request(string? model)
        {
            string version = model is null ? string.Empty : ",\"version\":{\"model\":\"" + model + "\"}";
            return new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingModels,
                ResourceId = "pump",
                Kind = WoTDocumentKindEnum.ThingModel,
                Content = ByteString.From(Encoding.UTF8.GetBytes(
                    "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                    "{\"tm\":\"https://www.w3.org/2019/wot/tm#\"}],\"@type\":\"tm:ThingModel\"," +
                    "\"id\":\"urn:pump:v1\"" + version + "}"))
            };
        }
    }
}
