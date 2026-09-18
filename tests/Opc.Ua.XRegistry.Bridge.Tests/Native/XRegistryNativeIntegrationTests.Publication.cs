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
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    public sealed partial class XRegistryNativeIntegrationTests
    {
        [Test]
        public async Task WideEpochsRemainLosslessInExtendedReadsAndFileWriteGuardsAsync()
        {
            const string path = "/schemagroups/g/schemas/r/versions/v1";
            await SeedAsync("/schemagroups/g/schemas/r",
                /*lang=json,strict*/ """{"versionid":"v1","schema":"original"}""").ConfigureAwait(false);
            ByteString before = await m_store!.LoadAsync().ConfigureAwait(false);
            var state = (JsonObject)JsonNode.Parse(before.Span)!;
            state["entries"]![path]!["metadata"]!["epoch"] = JsonNode.Parse("184467440737095516160");
            Assert.That(await m_store.CommitAsync(before, m_codec.EncodeJson(state)).ConfigureAwait(false), Is.True);
            await m_manager.RefreshAsync().ConfigureAwait(false);
            NodeId group = await FindChildEntityAsync(m_manager.RegistryNodeId, "/schemagroups/g")
                .ConfigureAwait(false);
            NodeId resource = await FindChildEntityAsync(group, "/schemagroups/g/schemas/r").ConfigureAwait(false);
            NodeId epochNode = await ChildAsync(resource, "Epoch").ConfigureAwait(false);
            ReadResponse scalar = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = epochNode, AttributeId = Attributes.Value }], CancellationToken.None)
                .ConfigureAwait(false);
            XRegistryResponse extended = await m_native.ExecuteAsync(Request(XRegistryAction.Read, path))
                .ConfigureAwait(false);
            var file = new ResourceTypeClient(m_session, resource, m_telemetry);
            uint handle = await file.OpenAsync(6).ConfigureAwait(false);
            await file.WriteAsync(handle, ByteString.From(Encoding.UTF8.GetBytes("updated"))).ConfigureAwait(false);
            await file.CloseAsync(handle).ConfigureAwait(false);
            XRegistryResponse updated = await m_native.ExecuteAsync(Request(XRegistryAction.Read, path))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(scalar.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(extended.Metadata.GetProperty("epoch").GetRawText(), Is.EqualTo("184467440737095516160"));
                Assert.That(updated.Metadata.GetProperty("epoch").GetRawText(), Is.EqualTo("184467440737095516161"));
                Assert.That(m_forwarder.Mutations.Last().Metadata.GetProperty("epoch").GetRawText(),
                    Is.EqualTo("184467440737095516160"));
                Assert.That(m_manager.IsNotificationDegraded, Is.True,
                    "The UInt32 companion event cannot truthfully advertise the wider epoch.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeProjectionQuotaRejectsBeforeAuthoritativeMutationAsync(bool baseMethod)
        {
            XRegistryBridgeNativeOptions options = m_options with
            {
                NamespaceUri = "urn:native-projection-quota:" + Guid.NewGuid().ToString("N"),
                MaxEntities = 1
            };
            var factory = new CapturingFactory(new XRegistryBridgeNodeManagerFactory(m_forwarder, options));
            await m_server.NodeManagerLifecycle.AddAsync(factory, callerContext: null).ConfigureAwait(false);
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            var native = new XRegistryOpcUaEndpoint(m_session, factory.Manager!.RegistryNodeId, options, m_telemetry);
            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (baseMethod)
                {
                    await new RegistryTypeClient(m_session, factory.Manager.RegistryNodeId, m_telemetry)
                        .CreateGroupAsync("candidate").ConfigureAwait(false);
                }
                else
                {
                    await native.ExecuteAsync(Request(XRegistryAction.Replace, "/schemagroups/candidate", "{}") with
                    {
                        OperationId = "projection-quota"
                    }).ConfigureAwait(false);
                }
            });
            XRegistryResponse group = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/candidate")).ConfigureAwait(false);
            XRegistryResponse root = await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(group.StatusCode, Is.EqualTo(404));
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task AddressSpaceObserverFailureCannotUndoAnAlreadyCommittedMutationAsync()
        {
            ILocalAddressSpace space = ((ILocalAddressSpaceSource)m_manager).CreateLocalAddressSpace();
            bool failed = false;
            void RejectGroup(NodeState node)
            {
                if (!failed && node is GroupState)
                {
                    failed = true;
                    throw new InvalidOperationException("Injected node registration failure.");
                }
            }
            space.NodeAdded += RejectGroup;
            try
            {
                XRegistryResponse result = await m_native.ExecuteAsync(
                    Request(XRegistryAction.Replace, "/schemagroups/candidate", "{}")).ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(201));
            }
            finally
            {
                space.NodeAdded -= RejectGroup;
            }
            XRegistryResponse absent = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/candidate")).ConfigureAwait(false);
            ArrayOf<ReferenceDescription> children = await XRegistryOpcUaEndpoint.BrowseAsync(
                m_session, m_manager.RegistryNodeId, m_options, CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(failed, Is.True);
                Assert.That(absent.StatusCode, Is.EqualTo(200));
                Assert.That(children.ToList().Any(reference => ExpandedNodeId.ToNodeId(reference.TypeDefinition,
                    m_session.NamespaceUris) == ExpandedNodeId.ToNodeId(ObjectTypeIds.GroupType,
                    m_session.NamespaceUris)), Is.True, "The committed group remains browseable.");
                Assert.That(m_forwarder.Mutations, Has.Count.EqualTo(1));
                Assert.That(m_manager.IsProjectionDegraded, Is.False);
            });
        }

        [Test]
        public async Task StagedProjectionIsUnavailableUntilTheCommitIsKnownAsync()
        {
            m_forwarder.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            m_forwarder.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<XRegistryResponse> commit = m_native.ExecuteAsync(Request(XRegistryAction.Merge, "/",
                /*lang=json,strict*/ """{"name":"staged"}""")).AsTask();
            try
            {
                await m_forwarder.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                ServiceResultException blocked = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await XRegistryOpcUaEndpoint.BrowseAsync(m_session, m_manager.RegistryNodeId, m_options,
                        CancellationToken.None).ConfigureAwait(false));
                XRegistryResponse before = await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(blocked.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
                    Assert.That(before.Metadata.TryGetProperty("name", out _), Is.False);
                });
                (NodeId upload, _) = await Bridge(m_session).BeginRequestAsync().ConfigureAwait(false);
                await Bridge(m_session).AbortRequestAsync(upload).ConfigureAwait(false);
            }
            finally
            {
                m_forwarder.Release.TrySetResult(true);
            }
            XRegistryResponse after = await commit.ConfigureAwait(false);
            Assert.That(after.Metadata.GetProperty("name").GetString(), Is.EqualTo("staged"));
        }

        [Test]
        public async Task LocalAddressSpaceObserversCannotSeeAPreparedProjectionBeforeCommitAsync()
        {
            ILocalAddressSpace space = ((ILocalAddressSpaceSource)m_manager).CreateLocalAddressSpace();
            int added = 0;
            void Observe(NodeState node)
            {
                if (node is GroupState)
                {
                    Interlocked.Increment(ref added);
                }
            }
            space.NodeAdded += Observe;
            IXRegistryPreparedOperation prepared = await m_native.PrepareAsync(
                Request(XRegistryAction.Replace, "/schemagroups/observer", "{}")).ConfigureAwait(false);
            m_forwarder.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            m_forwarder.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<XRegistryResponse> committing = prepared.CommitAsync().AsTask();
            try
            {
                await m_forwarder.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                Assert.That(Volatile.Read(ref added), Is.Zero);
                XRegistryResponse absent = await m_forwarder.Inner.ExecuteAsync(
                    Request(XRegistryAction.Read, "/schemagroups/observer")).ConfigureAwait(false);
                Assert.That(absent.StatusCode, Is.EqualTo(404));
                m_forwarder.Release.TrySetResult(true);
                Assert.That((await committing.ConfigureAwait(false)).StatusCode, Is.EqualTo(201));
                Assert.That(Volatile.Read(ref added), Is.EqualTo(1));
            }
            finally
            {
                m_forwarder.Release.TrySetResult(true);
                await prepared.DisposeAsync().ConfigureAwait(false);
                space.NodeAdded -= Observe;
            }
        }

        [Test]
        public async Task RejectedDeletionRestoresTheProjectionAndRetainsPinnedFileHandlesAsync()
        {
            await SeedAsync("/schemagroups/g/schemas/r",
                /*lang=json,strict*/ """{"versionid":"v1","schema":"retained"}""").ConfigureAwait(false);
            NodeId group = await FindChildEntityAsync(m_manager.RegistryNodeId, "/schemagroups/g")
                .ConfigureAwait(false);
            NodeId resource = await FindChildEntityAsync(group, "/schemagroups/g/schemas/r").ConfigureAwait(false);
            var file = new ResourceTypeClient(m_session, resource, m_telemetry);
            uint handle = await file.OpenAsync(1).ConfigureAwait(false);
            m_forwarder.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            m_forwarder.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<XRegistryResponse> deleting = m_native.ExecuteAsync(
                Request(XRegistryAction.Delete, "/schemagroups/g/schemas/r")).AsTask();
            try
            {
                await m_forwarder.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                XRegistryResponse other = await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Merge, "/",
                    /*lang=json,strict*/ """{"name":"concurrent"}""")).ConfigureAwait(false);
                Assert.That(other.StatusCode, Is.EqualTo(200));
            }
            finally
            {
                m_forwarder.Release.TrySetResult(true);
            }
            XRegistryResponse rejected = await deleting.ConfigureAwait(false);
            ByteString retained = await file.ReadAsync(handle, 64).ConfigureAwait(false);
            await file.CloseAsync(handle).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.Error?.Code, Is.EqualTo("concurrent_change"));
                Assert.That(Utf8(retained), Is.EqualTo("retained"));
                Assert.That(m_manager.IsProjectionDegraded, Is.False);
            });
        }
    }
}
