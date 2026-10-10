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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Materialization;
using Opc.Ua.XRegistry;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotRegistryProjectionOrderingTests
    {
        [Test]
        public async Task ThingModelMetadataIsVisibleWhenMutationsCompleteWithQueuedReconciliation()
        {
            await using ProjectionHarness harness = await ProjectionHarness.StartAsync().ConfigureAwait(false);
            await harness.AttachAsync().ConfigureAwait(false);
            GroupState group = await harness.CreateThingModelGroupAsync().ConfigureAwait(false);
            await harness.WaitUntilReconciliationBlockedAsync().ConfigureAwait(false);

            ThingModelFileState v1 = await harness.CreateVersionAsync(group, "v1").ConfigureAwait(false);
            await harness.UploadAsync(v1, "urn:model-titles-first", "1.0.0").ConfigureAwait(false);
            ThingModelFileState v2 = await harness.CreateVersionAsync(group, "v2").ConfigureAwait(false);
            await harness.UploadAsync(v2, "urn:model-titles-second", "2.0.0").ConfigureAwait(false);
            await harness.SetDefaultVersionAsync(v2, "v2").ConfigureAwait(false);

            WotResource stored = harness.StoredResource;
            string v1Title = v1.ModelTitle!.Value;
            string v1ModelVersion = v1.ModelVersion!.Value;
            string v2Title = v2.ModelTitle!.Value;
            string v2ModelVersion = v2.ModelVersion!.Value;
            bool v1IsDefault = v1.IsDefault!.Value;
            bool v2IsDefault = v2.IsDefault!.Value;
            await harness.DrainReconciliationAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(stored.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(stored.Title, Is.EqualTo("urn:model-titles-second"));
                Assert.That(stored.FindVersion("v2")!.Title, Is.EqualTo("urn:model-titles-second"));
                Assert.That(stored.FindVersion("v2")!.ModelVersion, Is.EqualTo("2.0.0"));
                Assert.That(v1Title, Is.EqualTo("urn:model-titles-first"));
                Assert.That(v1ModelVersion, Is.EqualTo("1.0.0"));
                Assert.That(v2Title, Is.EqualTo("urn:model-titles-second"));
                Assert.That(v2ModelVersion, Is.EqualTo("2.0.0"));
                Assert.That(v1IsDefault, Is.False);
                Assert.That(v2IsDefault, Is.True);
                Assert.That(v1.ModelTitle.Value, Is.EqualTo("urn:model-titles-first"));
                Assert.That(v1.ModelVersion.Value, Is.EqualTo("1.0.0"));
                Assert.That(v2.ModelTitle.Value, Is.EqualTo("urn:model-titles-second"));
                Assert.That(v2.ModelVersion.Value, Is.EqualTo("2.0.0"));
                Assert.That(v1.IsDefault.Value, Is.False);
                Assert.That(v2.IsDefault.Value, Is.True);
            });
        }

        [Test]
        public async Task ThingModelUploadPublishesVersionMetadataBeforeReturning()
        {
            await using ProjectionHarness harness = await ProjectionHarness.StartAsync().ConfigureAwait(false);
            await harness.AttachAsync().ConfigureAwait(false);
            GroupState group = await harness.CreateThingModelGroupAsync().ConfigureAwait(false);
            await harness.WaitUntilReconciliationBlockedAsync().ConfigureAwait(false);

            ThingModelFileState v1 = await harness.CreateVersionAsync(group, "v1").ConfigureAwait(false);
            await harness.UploadAsync(v1, "urn:model-titles-first", "1.0.0").ConfigureAwait(false);
            ThingModelFileState v2 = await harness.CreateVersionAsync(group, "v2").ConfigureAwait(false);
            await harness.UploadAsync(v2, "urn:model-titles-second", "2.0.0").ConfigureAwait(false);

            WotResource stored = harness.StoredResource;
            Assert.Multiple(() =>
            {
                Assert.That(stored.DefaultVersionId, Is.EqualTo("v1"));
                Assert.That(stored.Title, Is.EqualTo("urn:model-titles-first"));
                Assert.That(stored.FindVersion("v2")!.Title, Is.EqualTo("urn:model-titles-second"));
                Assert.That(stored.FindVersion("v2")!.ModelVersion, Is.EqualTo("2.0.0"));
                Assert.That(v1.ModelTitle!.Value, Is.EqualTo("urn:model-titles-first"));
                Assert.That(v1.ModelVersion!.Value, Is.EqualTo("1.0.0"));
                Assert.That(v1.IsDefault!.Value, Is.True);
                Assert.That(v2.ModelTitle!.Value, Is.EqualTo("urn:model-titles-second"));
                Assert.That(v2.ModelVersion!.Value, Is.EqualTo("2.0.0"));
                Assert.That(v2.IsDefault!.Value, Is.False);
            });
        }

        [Test]
        public async Task SetEnabledPublishesStateBeforeReturning()
        {
            await using ProjectionHarness harness = await ProjectionHarness.StartAsync().ConfigureAwait(false);
            await harness.AttachAsync().ConfigureAwait(false);
            GroupState group = await harness.CreateThingModelGroupAsync().ConfigureAwait(false);
            await harness.WaitUntilReconciliationBlockedAsync().ConfigureAwait(false);
            ThingModelFileState version = await harness.CreateVersionAsync(group, "v1").ConfigureAwait(false);

            await harness.SetEnabledAsync(version, false).ConfigureAwait(false);
            bool enabledAfterDisable = version.Enabled!.Value;
            await harness.SetEnabledAsync(version, true).ConfigureAwait(false);
            bool enabledAfterEnable = version.Enabled.Value;

            Assert.Multiple(() =>
            {
                Assert.That(enabledAfterDisable, Is.False);
                Assert.That(enabledAfterEnable, Is.True);
            });
        }

        private sealed class ProjectionHarness : IAsyncDisposable
        {
            private ProjectionHarness(PreparedWotTestRuntime runtime, WotRegistryService registry)
            {
                m_runtime = runtime;
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                var namespaces = new NamespaceTable();
                namespaces.Append(Namespaces.WotCon);
                namespaces.Append(XRegistryWellKnown.XRegistryNamespaceUri);
                var server = new Mock<IServerInternal>();
                server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
                server.SetupGet(value => value.ServerUris).Returns(new StringTable());
                server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
                server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
                server.SetupGet(value => value.Telemetry).Returns(telemetry);
                server.SetupGet(value => value.NodeManager).Returns(Mock.Of<IMasterNodeManager>());
                m_monitoredItemQueueFactory = new MonitoredItemQueueFactory(telemetry);
                server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(m_monitoredItemQueueFactory);
                server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));

                Registry = registry;
                m_coordinator = new WotMaterializationCoordinator(Registry, runtime.Host);
                m_lifecycleRegistration = Registry.RegisterLifecycleCoordinator(m_coordinator);
                var options = new WotRegistryServerOptions { AutoRefresh = false };
                m_manager = new WotRegistryNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    options,
                    Registry,
                    m_coordinator);
                m_projection = new WotRegistryProjection(m_manager, Registry, options);
                m_queue = new WotRegistryReconcileQueue(async change =>
                {
                    m_entered.TrySetResult(true);
                    await m_release.Task.ConfigureAwait(false);
                    await m_projection.ReconcileAsync(
                        change.Previous,
                        change.Current,
                        CancellationToken.None).ConfigureAwait(false);
                });
                Registry.Changed += OnRegistryChanged;
                m_registryNode = new WoTRegistryState(null);
                m_registryNode.Create(
                    m_manager.SystemContext,
                    new NodeId("registry", namespaces.GetIndexOrAppend(Namespaces.WotCon)),
                    new QualifiedName("Registry", namespaces.GetIndexOrAppend(Namespaces.WotCon)),
                    new LocalizedText("Registry"),
                    assignNodeIds: false);
                m_registryNode.AddCreateDocumentGroup(m_manager.SystemContext);
            }

            public WotRegistryService Registry { get; }

            public WotResource StoredResource => Registry.Current.FindResource(m_groupId, m_resourceId) ??
                throw new InvalidOperationException("The typed Model resource was not committed.");

            public static async Task<ProjectionHarness> StartAsync()
            {
                PreparedWotTestRuntime runtime = await PreparedWotTestRuntime.StartAsync().ConfigureAwait(false);
                try
                {
                    WotRegistryService registry = await runtime.CreateRegistryAsync().ConfigureAwait(false);
                    return new ProjectionHarness(runtime, registry);
                }
                catch
                {
                    await runtime.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            public async ValueTask AttachAsync()
            {
                await Registry.InitializeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await m_projection.AttachAsync(m_registryNode, CancellationToken.None)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }

            public async ValueTask<GroupState> CreateThingModelGroupAsync()
            {
                List<Variant> output = await CallWithResultAsync(
                    m_registryNode.CreateDocumentGroup!,
                    [new Variant((int)WoTDocumentKindEnum.ThingModel), new Variant("urn:catalogue:model-titles")])
                    .ConfigureAwait(false);
                Assert.That(output[0].TryGetValue(out NodeId groupNodeId), Is.True);
                Assert.That(output[1].TryGetValue(out string groupId), Is.True);
                m_groupId = groupId;
                ThingModelGroupState? group = m_manager.FindPredefinedNode<ThingModelGroupState>(groupNodeId);
                Assert.That(Registry.Current.FindGroup(groupId)?.CatalogUri, Is.EqualTo("urn:catalogue:model-titles"));
                return group
                    ?? throw new InvalidOperationException("The Thing Model group was not projected.");
            }

            public Task<bool> WaitUntilReconciliationBlockedAsync()
            {
                return m_entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }

            public async ValueTask DrainReconciliationAsync()
            {
                m_release.TrySetResult(true);
                await m_queue.WhenIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }

            public async ValueTask<ThingModelFileState> CreateVersionAsync(GroupState group, string versionId)
            {
                if (group is not ThingModelGroupState modelGroup)
                {
                    throw new InvalidOperationException("A typed Thing Model group is required.");
                }
                List<Variant> output = await CallWithResultAsync(
                    modelGroup.GetOrCreateThingModelResource!,
                    [new Variant("urn:model-titles"), new Variant(versionId), new Variant(false)])
                    .ConfigureAwait(false);
                Assert.That(output[0].TryGetValue(out NodeId logicalNodeId), Is.True);
                Assert.That(output[1].TryGetValue(out NodeId versionNodeId), Is.True);
                Assert.That(output[2].TryGetValue(out string resourceId), Is.True);
                Assert.That(output[3].TryGetValue(out string assignedVersionId), Is.True);
                m_resourceId = resourceId;
                WotResource resource = StoredResource;
                ThingModelFileState version = m_manager.FindPredefinedNode<ThingModelFileState>(versionNodeId) ??
                    throw new InvalidOperationException("The returned Thing Model Version was not projected.");
                Assert.Multiple(() =>
                {
                    Assert.That(logicalNodeId.IsNull || versionNodeId.IsNull, Is.False);
                    Assert.That(logicalNodeId, Is.Not.EqualTo(versionNodeId));
                    Assert.That(resource.SourceId, Is.EqualTo("urn:model-titles"));
                    Assert.That(assignedVersionId, Is.EqualTo(versionId));
                    Assert.That(m_projection.EventSourceForFailure(resource.Xid, assignedVersionId),
                        Is.SameAs(version));
                });
                return version;
            }

            public async ValueTask UploadAsync(ThingModelFileState version, string title, string modelVersion)
            {
                OpenMethodStateResult opened = await version.Open!.OnCallAsync!(
                    m_manager.SystemContext, version.Open, version.NodeId, 6, CancellationToken.None)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(opened.ServiceResult), Is.True, opened.ServiceResult.ToString());
                uint handle = opened.FileHandle;
                ByteString content = ByteString.From(Encoding.UTF8.GetBytes(
                    "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                    "\"@type\":\"tm:ThingModel\",\"id\":\"urn:model-titles\"," +
                    "\"title\":\"" + title + "\",\"version\":{\"model\":\"" + modelVersion + "\"}}"));
                ServiceResult written = await version.Write!.CallAsync(
                    m_manager.SystemContext, version.NodeId, [new Variant(handle), new Variant(content)],
                    new List<ServiceResult>(), new List<Variant>(), CancellationToken.None)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(written), Is.True, written.ToString());
                CloseMethodStateResult closed = await version.Close!.OnCallAsync!(
                    m_manager.SystemContext,
                    version.Close,
                    version.NodeId,
                    handle,
                    CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(closed.ServiceResult), Is.True, closed.ServiceResult.ToString());
            }

            public ValueTask SetDefaultVersionAsync(ThingModelFileState version, string versionId)
            {
                return CallAsync(version.SetDefaultVersion!, [new Variant(versionId), new Variant(0u)]);
            }

            public ValueTask SetEnabledAsync(ThingModelFileState version, bool enabled)
            {
                return CallAsync(version.SetEnabled!, [new Variant(enabled), new Variant(0u)]);
            }

            public async ValueTask DisposeAsync()
            {
                Registry.Changed -= OnRegistryChanged;
                m_release.TrySetResult(true);
                try
                {
                    await m_queue.CompleteAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                finally
                {
                    m_queue.Dispose();
                    m_projection.Dispose();
                    m_manager.Dispose();
                    m_lifecycleRegistration.Dispose();
                    m_coordinator.Dispose();
                    m_monitoredItemQueueFactory.Dispose();
                    await m_runtime.DisposeAsync().ConfigureAwait(false);
                }
            }

            private async ValueTask CallAsync(MethodState method, ArrayOf<Variant> input)
            {
                await CallWithResultAsync(method, input).ConfigureAwait(false);
            }

            private async ValueTask<List<Variant>> CallWithResultAsync(MethodState method, ArrayOf<Variant> input)
            {
                NodeState parent = method.Parent ??
                    throw new InvalidOperationException("A projected method must have a parent.");
                var output = new List<Variant>();
                ServiceResult result = await method.OnCallMethod2Async!(
                    m_manager.SystemContext,
                    method,
                    parent.NodeId,
                    input,
                    output,
                    CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
                return output;
            }

            private void OnRegistryChanged(object? sender, WotRegistryChangedEventArgs change)
            {
                m_queue.Enqueue(change);
            }

            private readonly WotRegistryNodeManager m_manager;
            private readonly PreparedWotTestRuntime m_runtime;
            private readonly IDisposable m_lifecycleRegistration;
            private readonly WotMaterializationCoordinator m_coordinator;
            private readonly WotRegistryProjection m_projection;
            private readonly WotRegistryReconcileQueue m_queue;
            private readonly WoTRegistryState m_registryNode;
            private readonly MonitoredItemQueueFactory m_monitoredItemQueueFactory;
            private readonly TaskCompletionSource<bool> m_entered = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> m_release = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            private string m_groupId = string.Empty;
            private string m_resourceId = string.Empty;
        }
    }
}
