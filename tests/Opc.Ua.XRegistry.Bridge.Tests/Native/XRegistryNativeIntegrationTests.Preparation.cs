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
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.XRegistry.Bridge.Model;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    public sealed partial class XRegistryNativeIntegrationTests
    {
        [Test]
        public async Task NativePreparationDoesNotPublishUntilCommitAndReusesExactPreviewAsync()
        {
            XRegistryEndpointDescription profile = await m_native.InspectAsync(s_writer).ConfigureAwait(false);
            IXRegistryPreparedOperation operation = await m_native.PrepareAsync(
                Request(XRegistryAction.Merge, "/",
                    /*lang=json,strict*/ """{"epoch":0,"name":"prepared"}""")).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable lifetime = operation.ConfigureAwait(false);
            XRegistryResponse before = await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(profile.SupportsPreparedMutations, Is.True);
                Assert.That(before.Metadata.TryGetProperty("name", out _), Is.False);
                Assert.That(before.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(m_forwarder.Mutations, Is.Empty);
                Assert.That(operation.Response.Metadata.GetProperty("name").GetString(), Is.EqualTo("prepared"));
            });
            XRegistryResponse committed = await operation.CommitAsync().ConfigureAwait(false);
            XRegistryResponse after = await m_native.ExecuteAsync(Request(XRegistryAction.Read,
                "/")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(committed, Is.SameAs(operation.Response));
                Assert.That(after.Metadata.GetProperty("name").GetString(), Is.EqualTo("prepared"));
                Assert.That(after.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
                Assert.That(m_forwarder.Mutations, Has.Count.EqualTo(1));
            });
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await operation.CommitAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task AbortingNativePreparationLeavesNoEntityOrJournalOutcomeAsync()
        {
            IXRegistryPreparedOperation operation = await m_native.PrepareAsync(
                Request(XRegistryAction.Replace, "/schemagroups/aborted", "{}") with { OperationId = "aborted" })
                .ConfigureAwait(false);
            await operation.DisposeAsync().ConfigureAwait(false);
            XRegistryResponse missing = await m_native.ExecuteAsync(Request(XRegistryAction.Read,
                "/schemagroups/aborted"))
                .ConfigureAwait(false);
            XRegistryOperationOutcome outcome = await m_native.GetOperationOutcomeAsync("aborted", s_writer)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(missing.StatusCode, Is.EqualTo(404));
                Assert.That(outcome.State, Is.EqualTo(XRegistryOperationState.Unknown));
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task UnrelatedRegistryMutationInvalidatesNativePreparedCommitAsync()
        {
            IXRegistryPreparedOperation operation = await m_native.PrepareAsync(
                Request(XRegistryAction.Replace, "/schemagroups/prepared", "{}")).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable lifetime = operation.ConfigureAwait(false);
            await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Replace, "/schemagroups/other", "{}"))
                .ConfigureAwait(false);
            XRegistryResponse rejected = await operation.CommitAsync().ConfigureAwait(false);
            XRegistryResponse missing = await m_native.ExecuteAsync(Request(XRegistryAction.Read,
                "/schemagroups/prepared"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(409));
                Assert.That(rejected.Error?.Code, Is.EqualTo("concurrent_change"));
                Assert.That(missing.StatusCode, Is.EqualTo(404));
            });
        }

        [Test]
        public async Task ExpiredNativePreparationCannotMutateAndReleasesItsProviderLeaseAsync()
        {
            IXRegistryPreparedOperation operation = await m_native.PrepareAsync(
                Request(XRegistryAction.Merge, "/",
                    /*lang=json,strict*/ """{"name":"expired"}""")).ConfigureAwait(false);
            m_clock.Advance(m_options.FileLifetime);
            ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.CommitAsync().ConfigureAwait(false));
            await operation.DisposeAsync().ConfigureAwait(false);
            XRegistryResponse root = await m_native.ExecuteAsync(Request(XRegistryAction.Read,
                "/")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(root.Metadata.TryGetProperty("name", out _), Is.False);
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task RevokedOriginalNativeCallerCannotCommitAPreparedOperationAsync()
        {
            IXRegistryPreparedOperation operation = await m_native.PrepareAsync(
                Request(XRegistryAction.Merge, "/",
                    /*lang=json,strict*/ """{"name":"revoked"}""")).ConfigureAwait(false);
            m_denyWrites = true;
            ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.CommitAsync().ConfigureAwait(false));
            await operation.DisposeAsync().ConfigureAwait(false);
            XRegistryResponse root = await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task NativeResponseTransferQuotaFailsBeforeRegistryCommitAsync()
        {
            XRegistryBridgeNativeOptions options = m_options with
            {
                NamespaceUri = "urn:native-test:preparation-quota:" + Guid.NewGuid().ToString("N"),
                MaxOpenFiles = 1
            };
            var factory = new CapturingFactory(new XRegistryBridgeNodeManagerFactory(m_forwarder, options));
            await m_server.NodeManagerLifecycle.AddAsync(factory, callerContext: null).ConfigureAwait(false);
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            var endpoint = new XRegistryOpcUaEndpoint(m_session, factory.Manager!.RegistryNodeId, options, m_telemetry);
            ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/",
                    /*lang=json,strict*/ """{"name":"no-response-space"}"""))
                    .ConfigureAwait(false));
            XRegistryResponse root = await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task PreparedTicketCannotBeCommittedByAnotherAuthenticatedSessionAsync()
        {
            XRegistryRequest request = Request(XRegistryAction.Merge, "/",
                /*lang=json,strict*/ """{"name":"session-owner"}""");
            RegistryBridgeTypeClient bridge = Bridge(m_session);
            (NodeId ticket, uint handle) = await bridge.BeginRequestAsync().ConfigureAwait(false);
            var file = new FileTypeClient(m_session, ticket, m_telemetry);
            await WriteChunksAsync(file, handle, m_codec.EncodeRequest(request)).ConfigureAwait(false);
            await file.CloseAsync(handle).ConfigureAwait(false);
            (NodeId preview, uint previewHandle) = await bridge.PrepareRequestAsync(ticket, string.Empty,
                m_codec.ComputeRequestDigest(request)).ConfigureAwait(false);
            await ReadTransferAsync(bridge, preview, previewHandle).ConfigureAwait(false);
            ManagedSession other = await ConnectAsync().ConfigureAwait(false);
            await using ConfiguredAsyncDisposable otherLifetime = other.ConfigureAwait(false);
            ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await Bridge(other).CommitPreparedRequestAsync(ticket).ConfigureAwait(false));
            await bridge.AbortRequestAsync(ticket).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }
    }
}
