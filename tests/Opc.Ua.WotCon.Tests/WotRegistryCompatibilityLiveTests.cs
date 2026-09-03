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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Materialization;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture]
    [Category("WotCon")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class WotRegistryCompatibilityLiveTests
    {
        [Test]
        public async Task RegistryWithoutOptionalVersionCapabilitySupportsDefaultLifecycle()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string pkiRoot = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                nameof(WotRegistryCompatibilityLiveTests),
                Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            ReferenceServer server = await fixture.StartAsync(pkiRoot).ConfigureAwait(false);
            using var inner = new WotRegistryService();
            var legacy = new LegacyOnlyRegistryService(inner);
            var coordinator = new WotMaterializationCoordinator(
                legacy,
                new LifecycleWotProjectionHost(server.NodeManagerLifecycle),
                documentConverter: new FakeWotDocumentConverter());
            var materializationEvents = new List<WotMaterializationEventArgs>();
            coordinator.Event += (_, e) => materializationEvents.Add(e);
            var options = new WotRegistryServerOptions
            {
                AutoRefresh = true,
                ManagementAccess = new WotManagementAccessPolicy
                {
                    MinimumSecurityMode = MessageSecurityMode.None,
                    AllowAnonymous = true,
                    RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
                }
            };
            var factory = new WotRegistryNodeManagerFactory(options, legacy, coordinator);
            Opc.Ua.Server.NodeManagerRegistration registration =
                await server.NodeManagerLifecycle
                .AddAsync(factory, callerContext: null)
                .ConfigureAwait(false);
            var nodeManager = (WotRegistryNodeManager)registration.NodeManager;
            using var clientFixture = new ClientFixture(false, false, telemetry);
            await clientFixture.LoadClientConfigurationAsync(pkiRoot).ConfigureAwait(false);
            string url = $"{Utils.UriSchemeOpcTcp}://localhost:{fixture.Port}";
            ISession session = await clientFixture
                .ConnectAsync(new Uri(url), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                WotRegistryClient client = await WotRegistryClient
                    .ForServerAsync(session, telemetry)
                    .ConfigureAwait(false);
                WotRegistryGroupClient group = await client
                    .CreateThingDescriptionGroupAsync()
                    .ConfigureAwait(false);
                await Task.Delay(250).ConfigureAwait(false);
                materializationEvents.Clear();
                uint generationBeforePlaceholder = coordinator.Generation;
                (WotRegistryResourceClient resource, string versionId) = await group
                    .CreateResourceAsync("legacy")
                    .ConfigureAwait(false);
                NodeState projectedResource = nodeManager.FindPredefinedNode<NodeState>(
                    resource.ResourceNodeId)!;
                var reportedEvents = new List<BaseEventState>();
                projectedResource.OnReportEvent = (_, _, target) =>
                {
                    if (target is BaseEventState evt)
                    {
                        reportedEvents.Add(evt);
                    }
                };
                await Task.Delay(250).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(coordinator.Generation, Is.EqualTo(generationBeforePlaceholder));
                    Assert.That(materializationEvents.Any(e => e.Kind is
                        WotMaterializationEventKind.ValidationFailure or
                        WotMaterializationEventKind.LoadFailure), Is.False);
                });

                await resource.UploadNewVersionAsync(
                        ByteString.From(TestMaterialization.Td("urn:legacy")))
                    .ConfigureAwait(false);
                bool materialized = await WaitForAsync(
                    () => inner.Current.FindResource(
                        WotRegistryGroups.ThingDescriptions,
                        "legacy")?.LoadState == WoTLoadStateEnum.Active)
                    .ConfigureAwait(false);
                WoTValidationOutcomeDataType validation = await resource
                    .ValidateAsync()
                    .ConfigureAwait(false);
                WotResource stored = inner.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "legacy")!;
                await resource.SetEnabledAsync(false, expectedEpoch: 0).ConfigureAwait(false);
                await resource.SetDefaultVersionAsync(
                        stored.DefaultVersionId!,
                        expectedEpoch: 0)
                    .ConfigureAwait(false);
                await resource.DeleteAsync(expectedEpoch: 0).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(versionId, Is.Empty);
                    Assert.That(materialized, Is.True);
                    Assert.That(validation.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Success));
                    Assert.That(stored.DefaultVersion, Is.Not.Null);
                    Assert.That(
                        reportedEvents.Any(evt =>
                            evt is WoTResourceEventState &&
                            evt.SourceNode!.Value == resource.ResourceNodeId),
                        Is.True);
                    Assert.That(inner.Current.FindResource(
                        WotRegistryGroups.ThingDescriptions,
                        "legacy"), Is.Null);
                });
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                session.Dispose();
                coordinator.Dispose();
                await fixture.StopAsync().ConfigureAwait(false);
                server.Dispose();
                if (Directory.Exists(pkiRoot))
                {
                    Directory.Delete(pkiRoot, recursive: true);
                }
            }
        }

        private static async Task<bool> WaitForAsync(Func<bool> condition)
        {
            for (int i = 0; i < 100; i++)
            {
                if (condition())
                {
                    return true;
                }
                await Task.Delay(50).ConfigureAwait(false);
            }
            return condition();
        }

        private sealed class LegacyOnlyRegistryService : IWotRegistryService
        {
            public LegacyOnlyRegistryService(IWotRegistryService inner)
            {
                m_inner = inner;
            }

            public WotRegistrySnapshot Current => m_inner.Current;
            public WotRegistryPersistenceBounds Bounds => m_inner.Bounds;

            public event EventHandler<WotRegistryChangedEventArgs>? Changed
            {
                add => m_inner.Changed += value;
                remove => m_inner.Changed -= value;
            }

            public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
                => m_inner.InitializeAsync(cancellationToken);

            public ValueTask<WotResourceGroup> GetOrCreateGroupAsync(
                string groupId,
                WoTDocumentKindEnum kind,
                string? name = null,
                CancellationToken cancellationToken = default)
                => m_inner.GetOrCreateGroupAsync(groupId, kind, name, cancellationToken);

            public ValueTask<WotResourceGroup?> TryCreateGroupAsync(
                string groupId,
                WoTDocumentKindEnum kind,
                string? name = null,
                CancellationToken cancellationToken = default)
                => m_inner.TryCreateGroupAsync(groupId, kind, name, cancellationToken);

            public ValueTask<WotRegistryMutationResult> DeleteGroupAsync(
                string groupId,
                long? expectedEpoch = null,
                CancellationToken cancellationToken = default)
                => m_inner.DeleteGroupAsync(groupId, expectedEpoch, cancellationToken);

            public ValueTask<(WotResource Resource, bool Created)> GetOrCreateResourceAsync(
                string groupId,
                string resourceId,
                WoTDocumentKindEnum kind,
                CancellationToken cancellationToken = default)
                => m_inner.GetOrCreateResourceAsync(
                    groupId,
                    resourceId,
                    kind,
                    cancellationToken);

            public ValueTask<WotResource?> TryCreateResourceAsync(
                string groupId,
                string resourceId,
                WoTDocumentKindEnum kind,
                CancellationToken cancellationToken = default)
                => m_inner.TryCreateResourceAsync(
                    groupId,
                    resourceId,
                    kind,
                    cancellationToken);

            public ValueTask<WoTValidationOutcomeDataType> ValidateResourceAsync(
                string groupId,
                string resourceId,
                CancellationToken cancellationToken = default)
                => m_inner.ValidateResourceAsync(groupId, resourceId, cancellationToken);

            public ValueTask<WotRegistryMutationResult> UpsertResourceAsync(
                WotUpsertResourceRequest request,
                CancellationToken cancellationToken = default)
                => m_inner.UpsertResourceAsync(request, cancellationToken);

            public ValueTask<ByteString> ReadContentAsync(
                WotResourceVersion version,
                CancellationToken cancellationToken = default)
                => m_inner.ReadContentAsync(version, cancellationToken);

            public ValueTask<ByteString> ReadContentChunkAsync(
                string digestHex,
                long offset,
                int count,
                CancellationToken cancellationToken = default)
                => m_inner.ReadContentChunkAsync(
                    digestHex,
                    offset,
                    count,
                    cancellationToken);

            public ValueTask<WotRegistryMutationResult> DeleteResourceAsync(
                string groupId,
                string resourceId,
                long? expectedEpoch = null,
                CancellationToken cancellationToken = default)
                => m_inner.DeleteResourceAsync(
                    groupId,
                    resourceId,
                    expectedEpoch,
                    cancellationToken);

            public ValueTask<WotRegistryMutationResult> SetDefaultVersionAsync(
                string groupId,
                string resourceId,
                string versionId,
                long? expectedEpoch = null,
                CancellationToken cancellationToken = default)
                => m_inner.SetDefaultVersionAsync(
                    groupId,
                    resourceId,
                    versionId,
                    expectedEpoch,
                    cancellationToken);

            public ValueTask<WotRegistryMutationResult> SetEnabledAsync(
                string groupId,
                string resourceId,
                bool enabled,
                long? expectedEpoch = null,
                CancellationToken cancellationToken = default)
                => m_inner.SetEnabledAsync(
                    groupId,
                    resourceId,
                    enabled,
                    expectedEpoch,
                    cancellationToken);

            public ValueTask<WotRegistryMutationResult> AddRegistryLabelAsync(
                string key,
                string value,
                long? expectedEpoch = null,
                CancellationToken cancellationToken = default)
                => m_inner.AddRegistryLabelAsync(
                    key,
                    value,
                    expectedEpoch,
                    cancellationToken);

            public ValueTask<WotRegistryMutationResult> RemoveRegistryLabelAsync(
                string key,
                long? expectedEpoch = null,
                CancellationToken cancellationToken = default)
                => m_inner.RemoveRegistryLabelAsync(key, expectedEpoch, cancellationToken);

            public ValueTask<WotRegistryMutationResult> AddGroupLabelAsync(
                string groupId,
                string key,
                string value,
                long? expectedEpoch = null,
                CancellationToken cancellationToken = default)
                => m_inner.AddGroupLabelAsync(
                    groupId,
                    key,
                    value,
                    expectedEpoch,
                    cancellationToken);

            public ValueTask<WotRegistryMutationResult> RemoveGroupLabelAsync(
                string groupId,
                string key,
                long? expectedEpoch = null,
                CancellationToken cancellationToken = default)
                => m_inner.RemoveGroupLabelAsync(
                    groupId,
                    key,
                    expectedEpoch,
                    cancellationToken);

            public ValueTask<WotRegistryMutationResult> AddResourceLabelAsync(
                string groupId,
                string resourceId,
                string key,
                string value,
                long? expectedEpoch = null,
                CancellationToken cancellationToken = default)
                => m_inner.AddResourceLabelAsync(
                    groupId,
                    resourceId,
                    key,
                    value,
                    expectedEpoch,
                    cancellationToken);

            public ValueTask<WotRegistryMutationResult> RemoveResourceLabelAsync(
                string groupId,
                string resourceId,
                string key,
                long? expectedEpoch = null,
                CancellationToken cancellationToken = default)
                => m_inner.RemoveResourceLabelAsync(
                    groupId,
                    resourceId,
                    key,
                    expectedEpoch,
                    cancellationToken);

            public ValueTask ApplyProjectionResultsAsync(
                IReadOnlyList<WotResourceProjection> projections,
                CancellationToken cancellationToken = default)
                => m_inner.ApplyProjectionResultsAsync(projections, cancellationToken);

            private readonly IWotRegistryService m_inner;
        }
    }
}
