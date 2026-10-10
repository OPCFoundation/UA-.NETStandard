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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Materialization;
using Opc.Ua.WotCon.Tests.Registry;
using Opc.Ua.XRegistry;

namespace Opc.Ua.WotCon.Tests
{
    public sealed partial class WotRegistryIdentityLiveTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task SessionDiscardBeforeStoreCommitRejectsPreparedNativeAllocation(bool getOrCreate)
        {
            WotRegistryGroupClient group = await CreateBarrierGroupAsync().ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            int changes = 0;
            m_registry.Changed += (_, _) => Interlocked.Increment(ref changes);
            var trace = new NativeCallTrace();
            InstallNativeTrace(group, getOrCreate, trace);
            m_store.PauseNextCommit();
            Task<WotRegistryResourceAllocation> request = InvokeBarrierAsync(
                group, getOrCreate, "urn:review:session-barrier");
            try
            {
                await m_store.CommitEntered.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That(trace.Owner, Is.Not.Null);
                await m_server.CurrentInstance.CloseSessionAsync(
                    null!, trace.Owner!.Id, false, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                m_store.ReleaseCommit();
            }
            StatusCode status = await ObserveNativeStatusAsync(request).ConfigureAwait(false);
            await trace.Completed.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            WotRegistrySnapshot stored = await m_store.LoadAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(trace.Calls, Is.EqualTo(1));
                Assert.That(trace.Owner!.IsClosing, Is.True);
                Assert.That(status, Is.EqualTo(StatusCodes.BadSessionClosed));
                Assert.That(trace.Status, Is.EqualTo(StatusCodes.BadSessionClosed));
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That(stored, Is.SameAs(before));
                Assert.That(m_store.SuccessfulCommits, Is.EqualTo(1));
                Assert.That(changes, Is.Zero);
                Assert.That(m_registry.Current.FindGroup(group.GroupId)!.Resources, Is.Empty);
                Assert.That(trace.Handle, Is.Zero);
            });
            using var retryFixture = new ClientFixture(false, false, m_telemetry);
            await retryFixture.LoadClientConfigurationAsync(m_root).ConfigureAwait(false);
            using ISession retrySession = await retryFixture.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            WotRegistryClient retryClient = await WotRegistryClient.ForServerAsync(retrySession, m_telemetry)
                .ConfigureAwait(false);
            (WotRegistryGroupClient retryGroup, bool createdGroup) = await retryClient.GetOrCreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:review:continuation").ConfigureAwait(false);
            WotRegistryResourceAllocation retry = await InvokeBarrierAsync(
                retryGroup, getOrCreate, "urn:review:session-barrier").ConfigureAwait(false);
            await AssertOpenAndCleanCloseAsync(retry).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(createdGroup, Is.False);
                Assert.That(retryGroup.GroupNodeId, Is.EqualTo(group.GroupNodeId));
                Assert.That(m_store.SuccessfulCommits, Is.EqualTo(2));
                Assert.That(m_store.CommitCount, Is.EqualTo(3));
                Assert.That(changes, Is.EqualTo(1));
            });
            await retrySession.CloseAsync().ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ServerCallCancellationBeforeCommitReleasesPreparedNativeHandle(bool getOrCreate)
        {
            WotRegistryGroupClient group = await CreateBarrierGroupAsync().ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            int changes = 0;
            m_registry.Changed += (_, _) => Interlocked.Increment(ref changes);
            using var cancellation = new CancellationTokenSource();
            var trace = new NativeCallTrace();
            (MethodState method, GenericMethodCalledEventHandler2Async original) =
                InstallNativeTrace(group, getOrCreate, trace, cancellation.Token);
            m_store.PauseNextCommit();
            Task<WotRegistryResourceAllocation> request = InvokeBarrierAsync(
                group, getOrCreate, "urn:review:cancel-barrier");
            try
            {
                await m_store.CommitEntered.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                cancellation.Cancel();
            }
            finally
            {
                m_store.ReleaseCommit();
            }
            StatusCode status = await ObserveNativeStatusAsync(request).ConfigureAwait(false);
            await trace.Completed.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsBad(status), Is.True);
                Assert.That(trace.CancellationObserved, Is.True);
                Assert.That(trace.Calls, Is.EqualTo(1));
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That(m_store.SuccessfulCommits, Is.EqualTo(1));
                Assert.That(changes, Is.Zero);
            });
            method.OnCallMethod2Async = original;
            WotRegistryResourceAllocation retry = await InvokeBarrierAsync(
                group, getOrCreate, "urn:review:cancel-barrier").ConfigureAwait(false);
            await AssertOpenAndCleanCloseAsync(retry).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(m_store.SuccessfulCommits, Is.EqualTo(2));
                Assert.That(m_store.CommitCount, Is.EqualTo(3));
                Assert.That(changes, Is.EqualTo(1));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SessionCloseAfterStoreCommitPreservesCommittedNativeAllocation(bool getOrCreate)
        {
            WotRegistryGroupClient group = await CreateBarrierGroupAsync().ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            int changes = 0;
            m_registry.Changed += (_, _) => Interlocked.Increment(ref changes);
            var trace = new NativeCallTrace();
            InstallNativeTrace(group, getOrCreate, trace);
            m_store.PauseNextCommit(afterCommit: true);
            Task<WotRegistryResourceAllocation> request = InvokeBarrierAsync(
                group, getOrCreate, "urn:review:committed-barrier");
            try
            {
                await m_store.CommitEntered.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That(m_store.SuccessfulCommits, Is.EqualTo(2));
                await m_server.CurrentInstance.CloseSessionAsync(
                    null!, trace.Owner!.Id, false, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                m_store.ReleaseCommit();
            }
            StatusCode status = await ObserveNativeStatusAsync(request).ConfigureAwait(false);
            await trace.Completed.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            WotRegistrySnapshot stored = await m_store.LoadAsync().ConfigureAwait(false);
            FileState version = m_manager.FindPredefinedNode<FileState>(trace.Version)!;
            Assert.Multiple(() =>
            {
                Assert.That(status, Is.EqualTo(StatusCodes.Good));
                Assert.That(trace.Status, Is.EqualTo(StatusCodes.Good));
                Assert.That(trace.Owner!.IsClosing, Is.True);
                Assert.That(m_registry.Current, Is.SameAs(stored));
                Assert.That(stored.Generation, Is.EqualTo(before.Generation + 1));
                Assert.That(stored.FindGroup(group.GroupId)!.Resources, Has.Count.EqualTo(1));
                Assert.That(m_store.SuccessfulCommits, Is.EqualTo(2));
                Assert.That(changes, Is.EqualTo(1));
                Assert.That(version.OpenCount!.Value, Is.Zero);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NotCommittedStoreFailureReleasesNativeHandleAndRetryPublishesOnce(bool getOrCreate)
        {
            WotRegistryGroupClient group = await CreateBarrierGroupAsync().ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            int changes = 0;
            m_registry.Changed += (_, _) => Interlocked.Increment(ref changes);
            m_store.FailNextCommit = true;
            StatusCode status = await ObserveNativeStatusAsync(
                InvokeBarrierAsync(group, getOrCreate, "urn:review:commit-failure")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(status, Is.EqualTo(StatusCodes.BadResourceUnavailable));
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That(m_store.SuccessfulCommits, Is.EqualTo(1));
                Assert.That(changes, Is.Zero);
            });
            WotRegistryResourceAllocation retry = await InvokeBarrierAsync(
                group, getOrCreate, "urn:review:commit-failure").ConfigureAwait(false);
            await AssertOpenAndCleanCloseAsync(retry).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(m_store.SuccessfulCommits, Is.EqualTo(2));
                Assert.That(m_store.CommitCount, Is.EqualTo(3));
                Assert.That(changes, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ConcurrentNativeAssignmentRetainsOneManagerAndCleanRetryFlags()
        {
            WotRegistryGroupClient group = await CreateBarrierGroupAsync().ConfigureAwait(false);
            m_store.PauseNextCommit();
            Task<WotRegistryResourceAllocation> first = InvokeBarrierAsync(group, false, "urn:review:concurrent");
            Task<WotRegistryResourceAllocation> follower;
            try
            {
                await m_store.CommitEntered.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                follower = InvokeBarrierAsync(group, true, "urn:review:concurrent");
            }
            finally
            {
                m_store.ReleaseCommit();
            }
            WotRegistryResourceAllocation allocation = await first.WaitAsync(TimeSpan.FromSeconds(20))
                .ConfigureAwait(false);
            Assert.That(await ObserveNativeStatusAsync(follower).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(m_registry.Current.FindGroup(group.GroupId)!.Resources, Has.Count.EqualTo(1));
            Assert.That(m_store.SuccessfulCommits, Is.EqualTo(2));
            await AssertOpenAndCleanCloseAsync(allocation).ConfigureAwait(false);
            (WotRegistryResourceAllocation retry, bool createdResource, bool createdVersion) =
                await group.GetOrCreateThingDescriptionResourceAsync("urn:review:concurrent", "v1", true)
                    .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(retry.Version.ResourceNodeId, Is.EqualTo(allocation.Version.ResourceNodeId));
                Assert.That(createdResource || createdVersion, Is.False);
            });
            await AssertOpenAndCleanCloseAsync(retry).ConfigureAwait(false);
        }

        [Test]
        public async Task PreservingReadFailureReleasesNativeWriterWithoutReplacingCommittedBytes()
        {
            WotRegistryGroupClient group = await CreateBarrierGroupAsync().ConfigureAwait(false);
            WotRegistryResourceAllocation allocation = await InvokeBarrierAsync(
                group, false, "urn:review:content").ConfigureAwait(false);
            ByteString content = WotRegistryIdentityTests.Request(
                m_registry.Current.FindGroup(group.GroupId)!, allocation.Version.ResourceId,
                "urn:review:content", "v1").Content;
            await allocation.Version.Proxy.WriteAsync(allocation.FileHandle, content).ConfigureAwait(false);
            await allocation.Version.Proxy.CloseAsync(allocation.FileHandle).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            int writes = m_store.Blobs.Writes;
            int commits = m_store.SuccessfulCommits;
            m_store.Blobs.FailNextRead = true;
            StatusCode status = await ObserveNativeStatusAsync(
                InvokeBarrierAsync(group, true, "urn:review:content")).ConfigureAwait(false);
            FileState node = m_manager.FindPredefinedNode<FileState>(allocation.Version.ResourceNodeId)!;
            Assert.Multiple(() =>
            {
                Assert.That(status, Is.EqualTo(StatusCodes.BadResourceUnavailable));
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That(m_store.Blobs.Writes, Is.EqualTo(writes));
                Assert.That(m_store.SuccessfulCommits, Is.EqualTo(commits));
                Assert.That(node.OpenCount!.Value, Is.Zero);
            });
            WotRegistryResourceAllocation retry = await InvokeBarrierAsync(
                group, true, "urn:review:content").ConfigureAwait(false);
            await AssertOpenAndCleanCloseAsync(retry).ConfigureAwait(false);
            Assert.That(await retry.Version.DownloadAsync().ConfigureAwait(false), Is.EqualTo(content));
            Assert.That(m_registry.Current, Is.SameAs(before));
        }

        [TestCase(WoTDocumentKindEnum.ThingDescription, false)]
        [TestCase(WoTDocumentKindEnum.ThingDescription, true)]
        [TestCase(WoTDocumentKindEnum.ThingModel, false)]
        [TestCase(WoTDocumentKindEnum.ThingModel, true)]
        public async Task TypedOnlyProviderKeepsDistinctNativeRoles(WoTDocumentKindEnum kind, bool getOrCreate)
        {
            await UseTypedOnlyProviderAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await m_client.CreateDocumentGroupAsync(kind, "urn:review:custom")
                .ConfigureAwait(false);
            MethodState method = FindTypedMethod(group, kind, getOrCreate);
            Argument[] inputs = method.InputArguments!.Value.ToArray()
                ?? throw new AssertionException("Typed input arguments are missing.");
            Argument[] outputs = method.OutputArguments!.Value.ToArray()
                ?? throw new AssertionException("Typed output arguments are missing.");
            Assert.Multiple(() =>
            {
                Assert.That(method.Parent!.NodeId, Is.EqualTo(group.GroupNodeId));
                Assert.That(method.BrowseName.NamespaceIndex,
                    Is.EqualTo(m_client.Session.NamespaceUris.GetIndex(Namespaces.WotCon)));
                Assert.That(method.Executable && method.UserExecutable, Is.True);
                Assert.That(inputs.Select(argument => argument.Name),
                    Is.EqualTo(new[] { kind == WoTDocumentKindEnum.ThingModel ? "ModelId" : "ThingId",
                        "VersionId", "RequestFileOpen" }));
                Assert.That(inputs.Select(argument => argument.DataType),
                    Is.EqualTo(new[] { Ua.DataTypeIds.UriString, Ua.DataTypeIds.String, Ua.DataTypeIds.Boolean }));
                Assert.That(inputs.All(argument => argument.ValueRank == ValueRanks.Scalar), Is.True);
                Assert.That(outputs, Has.Length.EqualTo(getOrCreate ? 7 : 5));
                Assert.That(outputs.All(argument => argument.ValueRank == ValueRanks.Scalar), Is.True);
                Assert.That(outputs.Take(5).Select(argument => argument.Name),
                    Is.EqualTo(s_nativeOutputNames));
                Assert.That(outputs.Take(5).Select(argument => argument.DataType),
                    Is.EqualTo(new[] { Ua.DataTypeIds.NodeId, Ua.DataTypeIds.NodeId, Ua.DataTypeIds.String,
                        Ua.DataTypeIds.String, Ua.DataTypeIds.UInt32 }));
                Assert.That(outputs.Skip(5).Select(argument => argument.Name),
                    Is.EqualTo(getOrCreate ? s_creationFlagNames : []));
                Assert.That(outputs.Skip(5).All(argument => argument.DataType == Ua.DataTypeIds.Boolean), Is.True);
            });
            WotRegistrySnapshot beforeOpen = m_registry.Current;
            ServiceResultException unsupported = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await InvokeTypedAsync(group, getOrCreate, "urn:review:open", "v1", true).ConfigureAwait(false))!;
            Assert.That(unsupported.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(m_registry.Current, Is.SameAs(beforeOpen));

            WotRegistryResourceAllocation first = await InvokeTypedAsync(
                group, getOrCreate, "urn:review:fileless", "v1", false).ConfigureAwait(false);
            WotResource initial = m_registry.Current.FindResource(group.GroupId, first.Version.ResourceId)!;
            WotRegistryResourceAllocation next = await InvokeTypedAsync(
                group, getOrCreate, "urn:review:fileless", "v2", false).ConfigureAwait(false);
            (WotRegistryResourceAllocation selected, bool createdResource, bool createdVersion) =
                await group.GetOrCreateDocumentResourceAsync("urn:review:fileless").ConfigureAwait(false);
            WotResource stored = m_registry.Current.FindResource(group.GroupId, first.Version.ResourceId)!;
            ResourceState logical = m_manager.FindPredefinedNode<ResourceState>(first.LogicalResource.ResourceNodeId)!;
            ResourceState version = m_manager.FindPredefinedNode<ResourceState>(first.Version.ResourceNodeId)!;
            Assert.Multiple(() =>
            {
                Assert.That(first.LogicalResource.ResourceNodeId, Is.Not.EqualTo(first.Version.ResourceNodeId));
                Assert.That(next.LogicalResource.ResourceNodeId, Is.EqualTo(first.LogicalResource.ResourceNodeId));
                Assert.That(next.Version.ResourceNodeId, Is.Not.EqualTo(first.Version.ResourceNodeId));
                Assert.That(selected.Version.ResourceNodeId, Is.EqualTo(first.Version.ResourceNodeId));
                Assert.That(first.FileHandle | next.FileHandle | selected.FileHandle, Is.Zero);
                Assert.That(createdResource || createdVersion, Is.False);
                Assert.That(stored.SourceId, Is.EqualTo("urn:review:fileless"));
                Assert.That(initial.DesiredVersionId, Is.EqualTo("v1"));
                Assert.That(stored.DefaultVersionId, Is.EqualTo("v1"));
                Assert.That(stored.DesiredVersionId, Is.EqualTo(initial.DesiredVersionId));
                Assert.That(stored.ActiveVersionId, Is.Null);
                Assert.That(m_store.Blobs.Reads + m_store.Blobs.Writes, Is.Zero);
                Assert.That(logical.Versions, Is.Not.Null);
            });
            NodeId versions = await WotConBrowsePathResolver.ResolveChildAsync(
                m_client.Session, logical.NodeId, Ua.ReferenceTypeIds.HasComponent,
                m_client.Session.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri),
                "Versions", StatusCodes.BadNodeIdUnknown, "Typed Versions container is missing.", default)
                .ConfigureAwait(false);
            NodeId expectedVersionsType = ExpandedNodeId.ToNodeId(
                kind == WoTDocumentKindEnum.ThingModel
                    ? ObjectTypeIds.ThingModelVersionsType : ObjectTypeIds.ThingDescriptionVersionsType,
                m_client.Session.NamespaceUris);
            NodeId expectedDocumentType = ExpandedNodeId.ToNodeId(
                kind == WoTDocumentKindEnum.ThingModel
                    ? ObjectTypeIds.ThingModelFileType : ObjectTypeIds.ThingDescriptionFileType,
                m_client.Session.NamespaceUris);
            Assert.Multiple(() =>
            {
                Assert.That(versions, Is.EqualTo(logical.Versions!.NodeId));
                Assert.That(logical.Versions.TypeDefinitionId, Is.EqualTo(expectedVersionsType));
                Assert.That(version.TypeDefinitionId, Is.EqualTo(expectedDocumentType));
                Assert.That(version.Parent, Is.SameAs(logical.Versions));
                Assert.That(logical.Versions.NodeId, Is.Not.EqualTo(logical.NodeId));
                Assert.That(version.NodeId, Is.Not.EqualTo(logical.Versions.NodeId));
            });
            Assert.That(await ReadStringAsync(logical.NodeId, "Xid", XRegistryWellKnown.XRegistryNamespaceUri)
                .ConfigureAwait(false), Is.EqualTo(stored.Xid));
            Assert.That(await ReadStringAsync(version.NodeId, "Xid", XRegistryWellKnown.XRegistryNamespaceUri)
                .ConfigureAwait(false), Is.EqualTo(stored.Xid + "/versions/v1"));
            Assert.That(await ReadStringAsync(logical.NodeId, "DesiredVersionId", Namespaces.WotCon)
                .ConfigureAwait(false), Is.EqualTo("v1"));
            Assert.That(await ReadStringAsync(logical.NodeId, "ActiveVersionId", Namespaces.WotCon)
                .ConfigureAwait(false), Is.Empty);
        }

        private async Task<WotRegistryGroupClient> CreateBarrierGroupAsync()
        {
            return await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:review:continuation").ConfigureAwait(false);
        }

        private static Task<WotRegistryResourceAllocation> InvokeBarrierAsync(
            WotRegistryGroupClient group,
            bool getOrCreate,
            string source)
        {
            return InvokeTypedAsync(group, getOrCreate, source, "v1", true);
        }

        private static async Task<WotRegistryResourceAllocation> InvokeTypedAsync(
            WotRegistryGroupClient group,
            bool getOrCreate,
            string source,
            string version,
            bool requestFileOpen)
        {
            if (getOrCreate)
            {
                (WotRegistryResourceAllocation allocation, _, _) =
                    await group.GetOrCreateDocumentResourceAsync(source, version, requestFileOpen)
                        .ConfigureAwait(false);
                return allocation;
            }
            return await group.CreateDocumentResourceAsync(source, version, requestFileOpen).ConfigureAwait(false);
        }

        private static async Task<StatusCode> ObserveNativeStatusAsync(Task<WotRegistryResourceAllocation> request)
        {
            try
            {
                await request.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                return StatusCodes.Good;
            }
            catch (ServiceResultException error)
            {
                return error.StatusCode;
            }
        }

        private async Task AssertOpenAndCleanCloseAsync(WotRegistryResourceAllocation allocation)
        {
            FileState version = m_manager.FindPredefinedNode<FileState>(allocation.Version.ResourceNodeId)!;
            Assert.That(allocation.FileHandle, Is.Not.Zero);
            Assert.That(await allocation.Version.Proxy.GetPositionAsync(allocation.FileHandle)
                .ConfigureAwait(false), Is.Zero);
            Assert.That(version.OpenCount!.Value, Is.EqualTo(1));
            await allocation.Version.Proxy.CloseAsync(allocation.FileHandle).ConfigureAwait(false);
            Assert.That(version.OpenCount.Value, Is.Zero);
        }

        private MethodState FindTypedMethod(WotRegistryGroupClient group, WoTDocumentKindEnum kind, bool getOrCreate)
        {
            if (kind == WoTDocumentKindEnum.ThingModel)
            {
                ThingModelGroupState state = m_manager.FindPredefinedNode<ThingModelGroupState>(group.GroupNodeId)!;
                return getOrCreate ? state.GetOrCreateThingModelResource! : state.CreateThingModelResource!;
            }
            ThingDescriptionGroupState description =
                m_manager.FindPredefinedNode<ThingDescriptionGroupState>(group.GroupNodeId)!;
            return getOrCreate
                ? description.GetOrCreateThingDescriptionResource! : description.CreateThingDescriptionResource!;
        }

        private (MethodState Method, GenericMethodCalledEventHandler2Async Original) InstallNativeTrace(
            WotRegistryGroupClient group,
            bool getOrCreate,
            NativeCallTrace trace,
            CancellationToken cancellationToken = default)
        {
            MethodState method = FindTypedMethod(group, WoTDocumentKindEnum.ThingDescription, getOrCreate);
            GenericMethodCalledEventHandler2Async original = method.OnCallMethod2Async!;
            method.OnCallMethod2Async = async (context, called, receiver, input, output, token) =>
            {
                Interlocked.Increment(ref trace.Calls);
                if (context is SessionSystemContext { OperationContext: Ua.Server.OperationContext operation })
                {
                    trace.Owner = operation.Session;
                }
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
                try
                {
                    ServiceResult result = await original(
                        context, called, receiver, input, output, linked.Token).ConfigureAwait(false);
                    trace.Status = ServiceResult.IsGood(result) ? StatusCodes.Good : result.StatusCode;
                    if (output.Count >= 5)
                    {
                        if (output[1].TryGetValue(out NodeId version))
                        {
                            trace.Version = version;
                        }
                        if (output[4].TryGetValue(out uint handle))
                        {
                            trace.Handle = handle;
                        }
                    }
                    return result;
                }
                catch (OperationCanceledException)
                {
                    trace.CancellationObserved = true;
                    throw;
                }
                finally
                {
                    trace.Completed.TrySetResult(true);
                }
            };
            return (method, original);
        }

        private async Task UseTypedOnlyProviderAsync()
        {
            var provider = new Mock<IWotRegistryService>(MockBehavior.Strict);
            Mock<IWotTypedRegistryService> typed = provider.As<IWotTypedRegistryService>();
            provider.Setup(value => value.Current).Returns(() => m_registry.Current);
            provider.Setup(value => value.Bounds).Returns(() => m_registry.Bounds);
            provider.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken ct) => m_registry.InitializeAsync(ct));
            provider.SetupAdd(value => value.Changed += It.IsAny<EventHandler<WotRegistryChangedEventArgs>>())
                .Callback<EventHandler<WotRegistryChangedEventArgs>>(handler => m_registry.Changed += handler);
            provider.SetupRemove(value => value.Changed -= It.IsAny<EventHandler<WotRegistryChangedEventArgs>>())
                .Callback<EventHandler<WotRegistryChangedEventArgs>>(handler => m_registry.Changed -= handler);
            typed.Setup(value => value.CreateDocumentGroupAsync(
                It.IsAny<WoTDocumentKindEnum>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((WoTDocumentKindEnum kind, string uri, CancellationToken ct) =>
                    m_registry.CreateDocumentGroupAsync(kind, uri, ct));
            typed.Setup(value => value.GetOrCreateDocumentGroupAsync(
                It.IsAny<WoTDocumentKindEnum>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((WoTDocumentKindEnum kind, string uri, CancellationToken ct) =>
                    m_registry.GetOrCreateDocumentGroupAsync(kind, uri, ct));
            typed.Setup(value => value.CreateDocumentResourceAsync(
                It.IsAny<string>(), It.IsAny<WoTDocumentKindEnum>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((
                    string group,
                    WoTDocumentKindEnum kind,
                    string source,
                    string version,
                    CancellationToken ct) =>
                    m_registry.CreateDocumentResourceAsync(group, kind, source, version, ct));
            typed.Setup(value => value.GetOrCreateDocumentResourceAsync(
                It.IsAny<string>(), It.IsAny<WoTDocumentKindEnum>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((
                    string group,
                    WoTDocumentKindEnum kind,
                    string source,
                    string version,
                    CancellationToken ct) =>
                    m_registry.GetOrCreateDocumentResourceAsync(group, kind, source, version, ct));
            IWotRegistryService registry = provider.Object;
            Assert.That(registry, Is.Not.InstanceOf<IWotVersionedRegistryService>());
            if (m_useDependencyInjection)
            {
                var services = new ServiceCollection();
                services.AddSingleton(m_telemetry);
                services.AddOpcUa().AddWotRegistryServer(Configure).AddWotRegistryClient();
                services.AddSingleton(registry);
                m_typedServices = services.BuildServiceProvider();
                registry = m_typedServices.GetRequiredService<IWotRegistryService>();
                Assert.That(m_typedServices.GetRequiredService<IWotTypedRegistryService>(), Is.SameAs(typed.Object));
            }
            await m_server.NodeManagerLifecycle.RemoveAsync(m_registration, null).ConfigureAwait(false);
            m_coordinator.Dispose();
            m_coordinator = new WotMaterializationCoordinator(
                registry, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle),
                documentConverter: new FakeWotDocumentConverter());
            m_registration = await m_server.NodeManagerLifecycle.AddAsync(
                new WotRegistryNodeManagerFactory(m_options, registry, m_coordinator), null).ConfigureAwait(false);
            m_manager = (WotRegistryNodeManager)m_registration.NodeManager;
            m_client = m_typedServices is not null
                ? await m_typedServices
                    .GetRequiredService<Func<ManagedSession, CancellationToken, Task<WotRegistryClient>>>()(
                        m_managed!, CancellationToken.None).ConfigureAwait(false)
                : await WotRegistryClient.ForServerAsync(m_bootstrapSession, m_telemetry).ConfigureAwait(false);
        }

        private sealed class NativeCallTrace
        {
            public readonly TaskCompletionSource<bool> Completed =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Ua.Server.ISession? Owner;
            public StatusCode Status;
            public NodeId Version;
            public uint Handle;
            public int Calls;
            public bool CancellationObserved;
        }

        private static readonly string[] s_nativeOutputNames =
        [
            "LogicalResourceNodeId", "VersionNodeId", "AssignedResourceId", "AssignedVersionId", "FileHandle"
        ];

        private static readonly string[] s_creationFlagNames = ["CreatedResource", "CreatedVersion"];
    }
}
