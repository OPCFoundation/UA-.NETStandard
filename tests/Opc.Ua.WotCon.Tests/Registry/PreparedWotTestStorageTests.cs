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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Materialization;

namespace Opc.Ua.WotCon.Tests.Registry
{
    [TestFixture]
    public sealed class PreparedWotTestStorageTests
    {
        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                nameof(PreparedWotTestStorageTests), Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PreparedStorageUsesActualCapabilityOrExplicitContentInjection(bool forceLeasedContent)
        {
            using var stock = new FileWotRegistryStore(m_root);
            using var storage = new PreparedWotTestStorage(m_root, forceLeasedContent);
            using FileWotRegistryStore store = storage.OpenStore();

            Assert.That(storage.ContentStore is not null,
                Is.EqualTo(forceLeasedContent || !stock.SupportsPreparedCommits));
            Assert.That(store.SupportsPreparedCommits, Is.True);
            if (storage.ContentStore is { } content)
            {
                Assert.That(store.ResourceStore, Is.SameAs(content));
            }
        }

        [TestCase("overwrite")]
        [TestCase("partial-write")]
        [TestCase("delete")]
        public async Task LeasedContentRejectsMutationUntilEveryOwnerReleases(string mutation)
        {
            using var content = new RecordingLeasedResourceStore();
            ByteString original = ByteString.From([1, 2, 3]);
            await content.WriteAsync("key", 0, original).ConfigureAwait(false);
            using IWotRegistryContentLease first = await content.AcquireContentLeaseAsync("key").ConfigureAwait(false);
            using IWotRegistryContentLease second = await content.AcquireContentLeaseAsync("key").ConfigureAwait(false);

            async Task MutateAsync()
            {
                if (mutation == "delete")
                {
                    await content.DeleteAsync("key").ConfigureAwait(false);
                }
                else
                {
                    await content.WriteAsync("key", mutation == "partial-write" ? 1 : 0, ByteString.From([9]))
                        .ConfigureAwait(false);
                }
            }

            Assert.That(content.ActiveLeaseCount, Is.EqualTo(2));
            await content.WriteAsync("key", 0, original).ConfigureAwait(false);
            await Assert.ThatAsync(MutateAsync, Throws.TypeOf<IOException>()).ConfigureAwait(false);
            first.Dispose();
            Assert.That(content.ActiveLeaseCount, Is.EqualTo(1));
            await Assert.ThatAsync(MutateAsync, Throws.TypeOf<IOException>()).ConfigureAwait(false);
            Assert.That(second.ResourceKey, Is.EqualTo("key"));
            Assert.That(second.ContentLength, Is.EqualTo(original.Length));
            Assert.That(await second.ReadAsync(0, original.Length).ConfigureAwait(false), Is.EqualTo(original));
            Assert.That(await content.ReadAsync("key", 0, original.Length).ConfigureAwait(false), Is.EqualTo(original));
            second.Dispose();
            Assert.That(content.ActiveLeaseCount, Is.Zero);

            await MutateAsync().ConfigureAwait(false);

            if (mutation == "delete")
            {
                Assert.That(await content.GetLengthAsync("key").ConfigureAwait(false), Is.EqualTo(-1));
            }
            else
            {
                Assert.That(await content.ReadAsync("key", mutation == "partial-write" ? 1 : 0, 1)
                    .ConfigureAwait(false), Is.EqualTo(ByteString.From([9])));
            }
        }

        [Test]
        public async Task InjectedContentRetainsRealManifestAndContentAcrossStoreReopen()
        {
            using var storage = new PreparedWotTestStorage(m_root, forceLeasedContent: true);
            ByteString bytes = ByteString.From(TestMaterialization.Td("urn:reopened-test-content"));
            long generation;
            string xid;
            using (FileWotRegistryStore store = storage.OpenStore())
            using (var registry = new WotRegistryService(store))
            {
                await registry.InitializeAsync().ConfigureAwait(false);
                WotRegistryMutationResult added = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "source",
                    VersionId = "v1",
                    Content = bytes
                }).ConfigureAwait(false);
                Assert.That(added.Changed, Is.True, added.Message);
                generation = registry.Current.Generation;
                xid = added.Resource!.Xid;
            }
            string manifest = Path.Combine(m_root, "manifest.json");
            byte[] before = File.ReadAllBytes(manifest);
            using FileWotRegistryStore reopened = storage.OpenStore();
            using var recovered = new WotRegistryService(reopened);
            await recovered.InitializeAsync().ConfigureAwait(false);
            WotResource resource = recovered.Current.FindResourceByXid(xid)!;

            Assert.That(reopened.ResourceStore, Is.SameAs(storage.ContentStore));
            Assert.That(reopened.SupportsPreparedCommits, Is.True);
            Assert.That(recovered.Current.Generation, Is.EqualTo(generation));
            Assert.That(resource.ResourceId, Is.EqualTo("source"));
            Assert.That(await recovered.ReadContentAsync(resource.DefaultVersion!).ConfigureAwait(false),
                Is.EqualTo(bytes));
            Assert.That(File.ReadAllBytes(manifest), Is.EqualTo(before));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task InjectedPreparedRuntimePublishesOrRollsBackTheRealNativeRegistryUnit(bool invalidPeer)
        {
            await using PreparedWotTestRuntime runtime = await PreparedWotTestRuntime.StartAsync(
                forceLeasedContent: true).ConfigureAwait(false);
            WotRegistryService registry = await runtime.CreateRegistryAsync().ConfigureAwait(false);
            var converter = new FakeWotDocumentConverter();
            string[] names = ["first", "second"];
            foreach (string name in names)
            {
                WotRegistryMutationResult added = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = name,
                    VersionId = "v1",
                    Content = ByteString.From(TestMaterialization.Td("urn:" + name))
                }).ConfigureAwait(false);
                Assert.That(added.Changed, Is.True, added.Message);
                string model = $"urn:wot:{WotRegistryGroups.ThingDescriptions}/{name}";
                runtime.Namespaces.GetIndexOrAppend(model);
                converter.SetRootNodeId(name, new ExpandedNodeId(5000u, model));
            }
            if (invalidPeer)
            {
                converter.MarkInvalid("second");
            }
            using var coordinator = new WotMaterializationCoordinator(
                registry, runtime.Host, documentConverter: converter)
            {
                ServerNamespaceUris = runtime.Namespaces
            };
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(Path.Combine(m_root, "pki")).ConfigureAwait(false);
            using ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{runtime.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                WotRegistrySnapshot before = registry.Current;
                WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest
                {
                    Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }
                }).ConfigureAwait(false);
                var nodes = names.Select(name => new ReadValueId
                {
                    NodeId = new NodeId(5000u, (ushort)runtime.Namespaces.GetIndex(
                        $"urn:wot:{WotRegistryGroups.ThingDescriptions}/{name}")),
                    AttributeId = Attributes.NodeClass
                }).ToArray();
                ReadResponse response = await session.ReadAsync(
                    null, 0, TimestampsToReturn.Neither, nodes, CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results, Has.Count.EqualTo(2));
                Assert.That(response.Results.ToArray()!.Select(value => value.StatusCode),
                    Is.All.EqualTo(invalidPeer ? StatusCodes.BadNodeIdUnknown : StatusCodes.Good));
                Assert.That(result.Summary.Outcome,
                    Is.EqualTo(invalidPeer ? WoTOutcomeEnum.Failed : WoTOutcomeEnum.Warning));
                Assert.That(registry.Current.Generation, Is.EqualTo(before.Generation + 1));
                Assert.That(registry.Current.RefreshGeneration, Is.EqualTo(invalidPeer ? 0u : 1u));
                Assert.That(coordinator.Generation, Is.EqualTo(invalidPeer ? 0u : 1u));
                Assert.That(coordinator.CommittedPublication.RegistrySnapshot, Is.SameAs(registry.Current));
                foreach (string name in names)
                {
                    WotResource original = before.FindResource(WotRegistryGroups.ThingDescriptions, name)!;
                    WotResource actual = registry.Current.FindResourceByXid(original.Xid)!;
                    Assert.That(actual.DefaultVersion!.Digest, Is.EqualTo(original.DefaultVersion!.Digest));
                    Assert.That(actual.MetaEpoch, Is.EqualTo(original.MetaEpoch));
                    Assert.That(actual.RootNodeId.IsNull, Is.EqualTo(invalidPeer));
                    Assert.That(actual.ActiveVersionId, Is.EqualTo(invalidPeer ? null : "v1"));
                }
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
            }
        }

        private string m_root = null!;
    }
}
