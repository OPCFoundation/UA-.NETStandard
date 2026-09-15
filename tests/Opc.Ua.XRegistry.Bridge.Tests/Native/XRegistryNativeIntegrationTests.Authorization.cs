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
using System.Text;
using System.Threading;
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
        [TestCase("extension")]
        [TestCase("create")]
        [TestCase("delete")]
        [TestCase("labels")]
        [TestCase("property")]
        [TestCase("open")]
        public async Task AuthenticatedUnlistedSubjectCannotMutateOperatorEndpointAsync(string operation)
        {
            await SeedAsync("/schemagroups/g/schemas/r",
                /*lang=json,strict*/ """{"versionid":"v1","schema":"unchanged"}""")
                .ConfigureAwait(false);
            NodeId group = await FindChildEntityAsync(m_manager.RegistryNodeId, "/schemagroups/g")
                .ConfigureAwait(false);
            NodeId resource = await FindChildEntityAsync(group, "/schemagroups/g/schemas/r")
                .ConfigureAwait(false);
            NodeId property = await ChildAsync(resource, "Description").ConfigureAwait(false);
            await using ManagedSession denied = await ConnectAsync(m_deniedUser).ConfigureAwait(false);
            Assert.That(denied.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
            var native = new XRegistryOpcUaEndpoint(denied, m_manager.RegistryNodeId, m_options, m_telemetry);
            XRegistryEndpointDescription profile = await native.InspectAsync(s_writer).ConfigureAwait(false);
            while (m_forwarder.Mutations.TryDequeue(out _))
            {
            }
            ServiceResultException rejection = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                var file = new ResourceTypeClient(denied, resource, m_telemetry);
                switch (operation)
                {
                    case "extension":
                        await native.ExecuteAsync(Request(XRegistryAction.Merge, "/",
                            /*lang=json,strict*/ """{"name":"denied"}"""))
                            .ConfigureAwait(false);
                        break;
                    case "create":
                        await new RegistryTypeClient(denied, m_manager.RegistryNodeId, m_telemetry)
                            .CreateGroupAsync("denied").ConfigureAwait(false);
                        break;
                    case "delete":
                        await file.DeleteAsync(0).ConfigureAwait(false);
                        break;
                    case "labels":
                        AttributesTypeClient labels = await file.GetLabelsAsync(m_telemetry).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Labels missing.");
                        await labels.AddAttributeAsync("denied", "value", 0).ConfigureAwait(false);
                        break;
                    case "property":
                        WriteResponse written = await denied.WriteAsync(null,
                            [new WriteValue
                            {
                                NodeId = property,
                                AttributeId = Attributes.Value,
                                Value = new DataValue(Variant.From("denied"))
                            }], CancellationToken.None).ConfigureAwait(false);
                        if (StatusCode.IsBad(written.Results[0]))
                        {
                            throw new ServiceResultException(written.Results[0]);
                        }
                        break;
                    case "open":
                        await file.OpenAsync(2).ConfigureAwait(false);
                        break;
                    default:
                        throw new InvalidOperationException("Unexpected mutation.");
                }
            });
            Assert.Multiple(() =>
            {
                Assert.That(profile.SupportsAtomicMutations, Is.False);
                Assert.That(profile.SupportsConditionalMutations, Is.False);
                Assert.That(profile.SupportsWriteTouch, Is.False);
                Assert.That(rejection.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task MissingCallerPolicyMakesTheNativeEndpointReadOnlyAsync()
        {
            XRegistryBridgeNativeOptions options = m_options with
            {
                NamespaceUri = "urn:native-tests:no-write-policy:" + Guid.NewGuid().ToString("N"),
                AuthorizeCallerAsync = null
            };
            var factory = new CapturingFactory(new XRegistryBridgeNodeManagerFactory(m_forwarder, options));
            await m_server.NodeManagerLifecycle.AddAsync(factory, callerContext: null).ConfigureAwait(false);
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            var native = new XRegistryOpcUaEndpoint(m_session, factory.Manager!.RegistryNodeId, options, m_telemetry);
            XRegistryEndpointDescription profile = await native.InspectAsync(s_writer).ConfigureAwait(false);
            ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await native.ExecuteAsync(Request(XRegistryAction.Merge, "/",
                    /*lang=json,strict*/ """{"name":"denied"}"""))
                    .ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(profile.SupportsAtomicMutations, Is.False);
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task AllowlistedSubjectWithoutRequiredNativeRoleCannotMutateAsync()
        {
            await using ManagedSession observer = await ConnectAsync(m_roleDeniedUser).ConfigureAwait(false);
            var native = new XRegistryOpcUaEndpoint(observer, m_manager.RegistryNodeId, m_options, m_telemetry);
            ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await native.ExecuteAsync(Request(XRegistryAction.Merge, "/",
                    /*lang=json,strict*/ """{"name":"denied"}"""))
                    .ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(observer.Identity.DisplayName, Is.EqualTo(m_roleDeniedUser));
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task AnonymousSessionCannotInheritOperatorAuthenticationAsync()
        {
            await using ManagedSession anonymous = await ConnectAsync(anonymous: true).ConfigureAwait(false);
            var native = new XRegistryOpcUaEndpoint(anonymous, m_manager.RegistryNodeId, m_options, m_telemetry);
            int mappedBefore = Volatile.Read(ref m_mappingCalls);
            ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await native.InspectAsync(s_writer).ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(Volatile.Read(ref m_mappingCalls), Is.EqualTo(mappedBefore));
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task InsecureGeneratedCommitCannotBypassServerEncryptionAsync()
        {
            await using ManagedSession insecure = await ConnectInsecureAsync().ConfigureAwait(false);
            RegistryBridgeTypeClient bridge = Bridge(insecure);
            XRegistryRequest request = Request(XRegistryAction.Merge, "/",
                /*lang=json,strict*/ """{"name":"insecure"}""");
            (NodeId upload, uint handle) = await bridge.BeginRequestAsync().ConfigureAwait(false);
            var file = new FileTypeClient(insecure, upload, m_telemetry);
            await WriteChunksAsync(file, handle, m_codec.EncodeRequest(request)).ConfigureAwait(false);
            await file.CloseAsync(handle).ConfigureAwait(false);
            ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await bridge.CommitRequestAsync(upload, string.Empty, m_codec.ComputeRequestDigest(request))
                    .ConfigureAwait(false));
            await bridge.AbortRequestAsync(upload).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task RevokedWritePermissionBlocksBufferedWriteAndCloseAsync()
        {
            await SeedAsync("/schemagroups/g/schemas/r",
                /*lang=json,strict*/ """{"versionid":"v1","schema":"unchanged"}""")
                .ConfigureAwait(false);
            NodeId group = await FindChildEntityAsync(m_manager.RegistryNodeId, "/schemagroups/g")
                .ConfigureAwait(false);
            NodeId node = await FindChildEntityAsync(group, "/schemagroups/g/schemas/r").ConfigureAwait(false);
            ResourceTypeClient file = m_generic.GetResource(node);
            uint handle = await file.OpenAsync(6).ConfigureAwait(false);
            await file.WriteAsync(handle, ByteString.From(Encoding.UTF8.GetBytes("staged"))).ConfigureAwait(false);
            while (m_forwarder.Mutations.TryDequeue(out _))
            {
            }
            m_denyWrites = true;
            ServiceResultException write = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await file.WriteAsync(handle, ByteString.From(Encoding.UTF8.GetBytes("denied")))
                    .ConfigureAwait(false));
            ServiceResultException close = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await file.CloseAsync(handle).ConfigureAwait(false));
            XRegistryResponse actual = await m_forwarder.Inner.ExecuteAsync(Request(
                XRegistryAction.Read, "/schemagroups/g/schemas/r/versions/v1") with
            { View = XRegistryView.Default })
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(write.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(close.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(Utf8(actual.Document), Is.EqualTo("unchanged"));
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task RevokedReadPermissionBlocksPinnedFileReadsAsync()
        {
            await SeedAsync("/schemagroups/g/schemas/r",
                /*lang=json,strict*/ """{"versionid":"v1","schema":"private"}""")
                .ConfigureAwait(false);
            NodeId group = await FindChildEntityAsync(m_manager.RegistryNodeId, "/schemagroups/g")
                .ConfigureAwait(false);
            NodeId node = await FindChildEntityAsync(group, "/schemagroups/g/schemas/r").ConfigureAwait(false);
            ResourceTypeClient file = m_generic.GetResource(node);
            uint handle = await file.OpenAsync(1).ConfigureAwait(false);
            m_denyReads = true;
            ServiceResultException read = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await file.ReadAsync(handle, 256).ConfigureAwait(false));
            m_denyReads = false;
            await file.CloseAsync(handle).ConfigureAwait(false);
            Assert.That(read.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task AsynchronousCallerAuthorizationCompletesBeforeOperatorMutationAsync()
        {
            m_authorizationEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_authorizationRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<XRegistryResponse> operation = m_native.ExecuteAsync(
                Request(XRegistryAction.Merge, "/", /*lang=json,strict*/ """{"name":"denied"}""")).AsTask();
            await m_authorizationEntered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(operation.IsCompleted, Is.False);
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
            m_authorizationRelease.SetResult(false);
            ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }
    }
}
