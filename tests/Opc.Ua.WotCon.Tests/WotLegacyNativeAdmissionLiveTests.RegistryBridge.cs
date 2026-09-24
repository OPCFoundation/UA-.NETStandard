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
using Moq;
using NUnit.Framework;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests
{
    public sealed partial class WotLegacyNativeAdmissionLiveTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task LegacyRetirementRecoveryDoesNotRepeatTheBackingDecision(bool committed)
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            string directory = Path.Combine(fixture.Options.ThingDescriptionStorageFolder!, "registry");
            bool interrupt = false;
            using var store = new FileWotRegistryStore(directory, directorySyncFailureInjector: null,
                manifestReplace: (source, destination, backup) =>
                {
                    if (interrupt)
                    {
                        File.Move(destination, backup);
                        throw new IOException("The retirement decision requires authoritative recovery.");
                    }
                    File.Replace(source, destination, backup);
                });
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync();
            fixture.Options.RegistryBridge = registry;
            using WotMaterializationCoordinator coordinator = fixture.CreateRegistryCoordinator(registry);
            using IDisposable hosted = registry.RegisterLifecycleCoordinator(coordinator);
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            ByteString content = Document();
            await asset.UploadThingDescriptionAsync(content.Span.ToArray());
            WotResource assigned = registry.Current.AllResources().Single();
            interrupt = true;

            await Assert.ThatAsync(async () => await fixture.Client.DeleteAssetAsync(asset.AssetId),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadInvalidState));

            File.Move(Directory.GetFiles(directory,
                committed ? "manifest.json.tmp-*" : "manifest.json.replace-backup-*").Single(),
                Path.Combine(directory, "manifest.json"));
            interrupt = false;
            Assert.That(await coordinator.RecoverAsync(), Is.False,
                "This bridge has registry metadata but no separate committed registry projection to restore.");
            WotResource recovered = registry.Current.FindResourceByXid(assigned.Xid)!;
            long generation = registry.Current.Generation;
            long epoch = recovered.MetaEpoch;
            Assert.That(recovered.Enabled, Is.EqualTo(!committed));
            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(content.Span.ToArray()));
            int changes = 0;
            registry.Changed += (_, _) => changes++;

            await fixture.Client.DeleteAssetAsync(asset.AssetId);

            WotResource retained = registry.Current.FindResourceByXid(assigned.Xid)!;
            Assert.That(registry.Current.Generation, Is.EqualTo(generation + (committed ? 0 : 1)),
                "Recovered committed retirement must not make another registry decision.");
            Assert.That(retained.MetaEpoch, Is.EqualTo(epoch + (committed ? 0 : 1)));
            Assert.That(changes, Is.EqualTo(committed ? 0 : 1));
            Assert.That(retained.Enabled, Is.False);
            Assert.That(retained.LoadState, Is.EqualTo(WoTLoadStateEnum.Retired));
            Assert.That(retained.ActiveVersionId, Is.Null);
            Assert.That(retained.RootNodeId.IsNull, Is.True);
            Assert.That(await registry.ReadContentAsync(retained.DefaultVersion!), Is.EqualTo(content));
            Assert.That(fixture.Provider.Connects, Is.EqualTo(1));
            ReadResponse removed = await fixture.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = asset.AssetId, AttributeId = Attributes.NodeClass }], default);
            Assert.That(removed.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            await fixture.RestartManagerAsync();
            Assert.That(fixture.Provider.Connects, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RefusedLegacyDeleteRetainsNativeGraphAndCanonicalFileOverTransport(bool exception)
        {
            using var registry = new WotRegistryService();
            var bridge = new Mock<IWotRegistryService>(MockBehavior.Strict);
            bridge.SetupGet(value => value.Current).Returns(() => registry.Current);
            bridge.Setup(value => value.UpsertResourceAsync(
                It.IsAny<WotUpsertResourceRequest>(), It.IsAny<CancellationToken>()))
                .Returns((WotUpsertResourceRequest request, CancellationToken token) =>
                    registry.UpsertResourceAsync(request, token));
            bool refuse = true;
            bridge.Setup(value => value.DeleteResourceAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                .Returns((string group, string resource, long? epoch, CancellationToken token) =>
                {
                    if (!refuse)
                    {
                        return registry.DeleteResourceAsync(group, resource, epoch, token);
                    }
                    if (exception)
                    {
                        throw new IOException("private-store-location");
                    }
                    return new ValueTask<WotRegistryMutationResult>(new WotRegistryMutationResult(
                        WoTOutcomeEnum.Rejected, null, registry.Current.Generation, [], "Refused.")
                    {
                        StatusCode = StatusCodes.BadUserAccessDenied
                    });
                });
            await using Fixture fixture = await Fixture.CreateAsync();
            fixture.Options.RegistryBridge = bridge.Object;
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            ByteString content = Document();
            await asset.UploadThingDescriptionAsync(content.Span.ToArray());
            WotResource assigned = registry.Current.AllResources().Single();
            NodeId type = await fixture.TypeAsync(asset.AssetId);
            ArrayOf<WotAssetVariableEntry> properties = await PropertiesAsync(asset);
            Assert.That(properties.Count, Is.GreaterThan(0));
            Assert.That(fixture.Provider.Connects, Is.EqualTo(1));

            await Assert.ThatAsync(async () => await fixture.Client.DeleteAssetAsync(asset.AssetId),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(exception ? StatusCodes.BadResourceUnavailable : StatusCodes.BadUserAccessDenied));

            Assert.That(await fixture.TypeAsync(asset.AssetId), Is.EqualTo(type));
            Assert.That((await PropertiesAsync(asset)).ToList().Select(property => property.NodeId),
                Is.EqualTo(properties.ToList().Select(property => property.NodeId)));
            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(content.Span.ToArray()));
            Assert.That(fixture.Provider.Connects, Is.EqualTo(1));
            Assert.That(registry.Current.AllResources().Single(), Is.SameAs(assigned));

            refuse = false;
            await fixture.Client.DeleteAssetAsync(asset.AssetId);

            bridge.Verify(value => value.DeleteResourceAsync(
                assigned.GroupId, assigned.ResourceId, null, It.IsAny<CancellationToken>()), Times.Exactly(2));
            Assert.That(registry.Current.AllResources(), Is.Empty);
            ReadResponse removed = await fixture.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = asset.AssetId, AttributeId = Attributes.NodeClass }], default);
            Assert.That(removed.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            await fixture.RestartManagerAsync();
            Assert.That(fixture.Provider.Connects, Is.EqualTo(1));
            int restored = 0;
            await foreach (WotAssetEntry _ in fixture.Client.EnumerateAssetsAsync())
            {
                restored++;
            }
            Assert.That(restored, Is.Zero);
        }

        private sealed partial class Fixture
        {
            public WotMaterializationCoordinator CreateRegistryCoordinator(WotRegistryService registry)
            {
                return new WotMaterializationCoordinator(
                    registry, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle))
                {
                    ServerNamespaceUris = m_server.CurrentInstance.NamespaceUris,
                    DeletePolicy = WoTDeletePolicyEnum.Retire
                };
            }
        }
    }
}
