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
            await using var harness = new ProjectionHarness();
            await harness.AttachAsync().ConfigureAwait(false);
            GroupState group = await harness.CreateThingModelGroupAsync().ConfigureAwait(false);
            await harness.WaitUntilReconciliationBlockedAsync().ConfigureAwait(false);

            ThingModelFileState v1 = await harness.CreateVersionAsync(group, "v1").ConfigureAwait(false);
            await harness.UploadAsync(v1, "urn:model-titles-first", "1.0.0").ConfigureAwait(false);
            ThingModelFileState v2 = await harness.CreateVersionAsync(group, "v2").ConfigureAwait(false);
            await harness.UploadAsync(v2, "urn:model-titles-second", "2.0.0").ConfigureAwait(false);
            await harness.SetDefaultVersionAsync(v2, "v2").ConfigureAwait(false);

            WotResource stored = harness.Registry.Current.FindResource(
                WotRegistryGroups.ThingModels,
                "model-titles")!;
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
            await using var harness = new ProjectionHarness();
            await harness.AttachAsync().ConfigureAwait(false);
            GroupState group = await harness.CreateThingModelGroupAsync().ConfigureAwait(false);
            await harness.WaitUntilReconciliationBlockedAsync().ConfigureAwait(false);

            ThingModelFileState v1 = await harness.CreateVersionAsync(group, "v1").ConfigureAwait(false);
            await harness.UploadAsync(v1, "urn:model-titles-first", "1.0.0").ConfigureAwait(false);
            ThingModelFileState v2 = await harness.CreateVersionAsync(group, "v2").ConfigureAwait(false);
            await harness.UploadAsync(v2, "urn:model-titles-second", "2.0.0").ConfigureAwait(false);

            WotResource stored = harness.Registry.Current.FindResource(
                WotRegistryGroups.ThingModels,
                "model-titles")!;
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
            await using var harness = new ProjectionHarness();
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
            public ProjectionHarness()
            {
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

                Registry = new WotRegistryService();
                m_coordinator = new WotMaterializationCoordinator(Registry, Mock.Of<IWotProjectionHost>());
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
                m_registryNode = new RegistryState(null);
                m_registryNode.Create(
                    m_manager.SystemContext,
                    new NodeId("registry", namespaces.GetIndexOrAppend(Namespaces.WotCon)),
                    new QualifiedName("Registry", namespaces.GetIndexOrAppend(Namespaces.WotCon)),
                    new LocalizedText("Registry"),
                    assignNodeIds: false);
                m_registryNode.AddCreateGroup(m_manager.SystemContext);
            }

            public WotRegistryService Registry { get; }

            public async ValueTask AttachAsync()
            {
                await Registry.InitializeAsync().ConfigureAwait(false);
                await m_projection.AttachAsync(m_registryNode, CancellationToken.None).ConfigureAwait(false);
            }

            public async ValueTask<GroupState> CreateThingModelGroupAsync()
            {
                await CallAsync(
                    m_registryNode.CreateGroup!,
                    [new Variant(WotRegistryGroups.ThingModels)]).ConfigureAwait(false);
                var children = new List<BaseInstanceState>();
                m_registryNode.GetChildren(m_manager.SystemContext, children);
                return children.Find(child => child is ThingModelGroupState) as GroupState
                    ?? throw new InvalidOperationException("The Thing Model group was not projected.");
            }

            public Task<bool> WaitUntilReconciliationBlockedAsync()
            {
                return m_entered.Task;
            }

            public async ValueTask DrainReconciliationAsync()
            {
                m_release.TrySetResult(true);
                await m_queue.WhenIdleAsync().ConfigureAwait(false);
            }

            public async ValueTask<ThingModelFileState> CreateVersionAsync(GroupState group, string versionId)
            {
                await CallAsync(
                    group.CreateResource!,
                    [new Variant("model-titles"), new Variant(versionId), new Variant(false)]).ConfigureAwait(false);
                WotResource resource = Registry.Current.FindResource(WotRegistryGroups.ThingModels, "model-titles")!;
                return (ThingModelFileState)m_projection.EventSourceFor($"{resource.Xid}/versions/{versionId}");
            }

            public async ValueTask UploadAsync(ThingModelFileState version, string title, string modelVersion)
            {
                uint handle = 0;
                ServiceResult opened = version.Open!.OnCall!(
                    m_manager.SystemContext, version.Open, version.NodeId, 6, ref handle);
                Assert.That(ServiceResult.IsGood(opened), Is.True, opened.ToString());
                ByteString content = ByteString.From(Encoding.UTF8.GetBytes(
                    "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                    "\"@type\":\"tm:ThingModel\",\"id\":\"urn:model-titles\"," +
                    "\"title\":\"" + title + "\",\"version\":{\"model\":\"" + modelVersion + "\"}}"));
                ServiceResult written = version.Write!.OnCall!(
                    m_manager.SystemContext, version.Write, version.NodeId, handle, content);
                Assert.That(ServiceResult.IsGood(written), Is.True, written.ToString());
                CloseMethodStateResult closed = await version.Close!.OnCallAsync!(
                    m_manager.SystemContext,
                    version.Close,
                    version.NodeId,
                    handle,
                    CancellationToken.None).ConfigureAwait(false);
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
                await m_queue.CompleteAsync().ConfigureAwait(false);
                m_queue.Dispose();
                m_projection.Dispose();
                m_manager.Dispose();
                m_coordinator.Dispose();
                Registry.Dispose();
                m_monitoredItemQueueFactory.Dispose();
            }

            private async ValueTask CallAsync(MethodState method, ArrayOf<Variant> input)
            {
                NodeState parent = method.Parent ??
                    throw new InvalidOperationException("A projected method must have a parent.");
                ServiceResult result = await method.OnCallMethod2Async!(
                    m_manager.SystemContext,
                    method,
                    parent.NodeId,
                    input,
                    [],
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
            }

            private void OnRegistryChanged(object? sender, WotRegistryChangedEventArgs change)
            {
                m_queue.Enqueue(change);
            }

            private readonly WotRegistryNodeManager m_manager;
            private readonly WotMaterializationCoordinator m_coordinator;
            private readonly WotRegistryProjection m_projection;
            private readonly WotRegistryReconcileQueue m_queue;
            private readonly RegistryState m_registryNode;
            private readonly MonitoredItemQueueFactory m_monitoredItemQueueFactory;
            private readonly TaskCompletionSource<bool> m_entered = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> m_release = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
