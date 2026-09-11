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
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry.Bridge.Model;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Client;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;
using Quickstarts.ReferenceServer;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    [TestFixture]
    [NonParallelizable]
    [Category("XRegistryNative")]
    public sealed partial class XRegistryNativeIntegrationTests
    {
        [OneTimeSetUp]
        public async Task StartServerAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_root = Path.Combine(Path.GetTempPath(), "XRegistryNativeTests", Guid.NewGuid().ToString("N"));
            m_allowedUser = "native-allowed-" + Guid.NewGuid().ToString("N");
            m_deniedUser = "native-denied-" + Guid.NewGuid().ToString("N");
            m_roleDeniedUser = "native-role-" + Guid.NewGuid().ToString("N");
            m_password = ByteString.From(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N")));
            m_forwarder = new ForwardingEndpoint();
            await ResetEndpointAsync().ConfigureAwait(false);
            m_options = new XRegistryBridgeNativeOptions
            {
                ProjectionContext = s_writer,
                ContextFactory = MapCaller,
                AuthorizeCallerAsync = AuthorizeInboundAsync,
                TimeProvider = m_clock,
                ChunkSize = 256,
                MaxMessageBytes = 2 * 1024 * 1024,
                MaxDocumentBytes = 1024 * 1024,
                MaxBufferedBytes = 8 * 1024 * 1024
            };
            m_serverFixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true,
                MaxChannelCount = 64
            };
            await m_serverFixture.LoadConfigurationAsync(Path.Combine(m_root, "server")).ConfigureAwait(false);
            m_serverFixture.Config.ServerConfiguration!.UserTokenPolicies =
            [
                new UserTokenPolicy
                {
                    PolicyId = "native-test-anonymous",
                    TokenType = UserTokenType.Anonymous
                },
                new UserTokenPolicy
                {
                    PolicyId = "native-test-username",
                    TokenType = UserTokenType.UserName,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                }
            ];
            m_server = await m_serverFixture.StartAsync(Path.Combine(m_root, "server")).ConfigureAwait(false);
            Assert.That(m_server.UserDatabase.CreateUser(m_allowedUser, m_password.Span, [Role.Operator]),
                Is.True);
            Assert.That(m_server.UserDatabase.CreateUser(m_deniedUser, m_password.Span, [Role.Operator]),
                Is.True);
            Assert.That(m_server.UserDatabase.CreateUser(m_roleDeniedUser, m_password.Span, [Role.Observer]), Is.True);
            m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend("urn:native-tests:namespace-padding");
            var factory = new CapturingFactory(new XRegistryBridgeNodeManagerFactory(m_forwarder, m_options));
            await m_server.NodeManagerLifecycle.AddAsync(factory, callerContext: null).ConfigureAwait(false);
            m_manager = factory.Manager!;
            m_baseOptions = m_options with
            {
                NamespaceUri = "urn:opcfoundation.org:xregistry:bridge:base-test",
                EnableExperimentalExtension = false
            };
            var baseFactory = new CapturingFactory(
                new XRegistryBridgeNodeManagerFactory(m_forwarder, m_baseOptions));
            await m_server.NodeManagerLifecycle.AddAsync(baseFactory, callerContext: null).ConfigureAwait(false);
            m_baseManager = baseFactory.Manager!;
            m_application = new ApplicationInstance(m_telemetry) { ApplicationName = "NativeBridgeTestClient" };
            string pki = Path.Combine(m_root, "client");
            ArrayOf<CertificateIdentifier> certificates =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    "CN=NativeBridgeTestClient, O=OPC Foundation, DC=localhost", CertificateStoreType.Directory, pki);
            m_clientConfiguration = await m_application
                .Build("urn:localhost:opcfoundation.org:NativeBridgeTestClient",
                    "http://opcfoundation.org/UA/NativeBridgeTestClient")
                .SetMaxByteStringLength(4 * 1024 * 1024)
                .AsClient()
                .AddSecurityConfiguration(certificates, pki)
                .SetAutoAcceptUntrustedCertificates(true)
                .CreateAsync().ConfigureAwait(false);
            bool certificate = await m_application.CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);
            Assert.That(certificate, Is.True);
            var url = new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}");
            using DiscoveryClient discovery = await DiscoveryClient.CreateAsync(m_clientConfiguration, url,
                EndpointConfiguration.Create(m_clientConfiguration)).ConfigureAwait(false);
            ArrayOf<EndpointDescription> endpoints = await discovery.GetEndpointsAsync(default).ConfigureAwait(false);
            EndpointDescription endpoint = endpoints.ToList().Single(candidate =>
                candidate.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                candidate.SecurityPolicyUri == SecurityPolicies.Basic256Sha256);
            string endpointUrl = endpoint.EndpointUrl ?? throw new InvalidOperationException("Endpoint URL missing.");
            endpoint.EndpointUrl = new UriBuilder(endpointUrl) { Host = "localhost" }.Uri.AbsoluteUri;
            Assert.That(endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            m_configuredEndpoint = new ConfiguredEndpoint(null, endpoint,
                EndpointConfiguration.Create(m_clientConfiguration));
            EndpointDescription insecure = endpoints.ToList().Single(candidate =>
                candidate.SecurityMode == MessageSecurityMode.None);
            string insecureUrl = insecure.EndpointUrl ?? throw new InvalidOperationException("Endpoint URL missing.");
            insecure.EndpointUrl = new UriBuilder(insecureUrl) { Host = "localhost" }.Uri.AbsoluteUri;
            m_insecureEndpoint = new ConfiguredEndpoint(null, insecure,
                EndpointConfiguration.Create(m_clientConfiguration));
            m_session = await ConnectAsync().ConfigureAwait(false);
            m_native = new XRegistryOpcUaEndpoint(m_session, m_manager.RegistryNodeId, m_options, m_telemetry);
            m_generic = new GenericXRegistryClient(m_session, XRegistryWellKnown.XRegistryNamespaceUri,
                m_manager.RegistryNodeId, m_telemetry);
        }

        [SetUp]
        public async Task ResetAsync()
        {
            m_denyWrites = false;
            m_denyReads = false;
            m_authorizationEntered = null;
            m_authorizationRelease = null;
            await ResetEndpointAsync().ConfigureAwait(false);
            await m_manager.RefreshAsync().ConfigureAwait(false);
            await m_baseManager.RefreshAsync().ConfigureAwait(false);
            m_forwarder.Mutations.Clear();
        }

        [TearDown]
        public void ReleaseStore()
        {
            m_forwarder.Inner.Dispose();
            m_store?.Dispose();
        }

        [OneTimeTearDown]
        public async Task StopServerAsync()
        {
            if (m_session is not null)
            {
                await m_session.DisposeAsync().ConfigureAwait(false);
            }
            if (m_serverFixture is not null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }
            m_server?.Dispose();
            m_forwarder?.Inner?.Dispose();
            m_store?.Dispose();
            if (m_application is not null)
            {
                await m_application.DisposeAsync().ConfigureAwait(false);
            }
            if (m_root is not null && Directory.Exists(m_root))
            {
                Directory.Delete(m_root, true);
            }
        }

        [Test]
        public async Task InspectAndRoundTripUseGeneratedTransportAsync()
        {
            XRegistryEndpointDescription description = await m_native.InspectAsync(s_writer).ConfigureAwait(false);
            XRegistryResponse response = await m_native.ExecuteAsync(Request(
                XRegistryAction.Merge, "/",
                    /*lang=json,strict*/ """{"epoch":0,"name":"native"}""")).ConfigureAwait(false);
            XRegistryResponse root = await m_native.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(description.Profile, Is.EqualTo("experimental-native-bridge-v1"));
                Assert.That(description.SupportsAtomicMutations, Is.True);
                Assert.That(description.SupportsConditionalMutations, Is.True);
                Assert.That(description.SupportsWriteTouch, Is.True);
                Assert.That(description.SupportsOperationReplay, Is.True);
                Assert.That(response.StatusCode, Is.EqualTo(200));
                Assert.That(root.Metadata.GetProperty("name").GetString(), Is.EqualTo("native"));
                Assert.That(root.Metadata.GetProperty("epoch").GetUInt32(), Is.EqualTo(1));
                Assert.That(m_forwarder.Mutations, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public async Task SealedUploadRequiresMatchingDigestAndCommitAsync()
        {
            XRegistryRequest request = Request(XRegistryAction.Merge, "/",
                /*lang=json,strict*/ """{"epoch":0,"name":"sealed"}""");
            RegistryBridgeTypeClient bridge = Bridge(m_session);
            (NodeId upload, uint handle) = await bridge.BeginRequestAsync().ConfigureAwait(false);
            var file = new FileTypeClient(m_session, upload, m_telemetry);
            await WriteChunksAsync(file, handle, m_codec.EncodeRequest(request)).ConfigureAwait(false);
            await file.CloseAsync(handle).ConfigureAwait(false);
            Assert.That(m_forwarder.Mutations, Is.Empty, "File Close must only seal the transport upload.");
            ServiceResultException badDigest = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await bridge.CommitRequestAsync(upload, string.Empty, new string('0', 64)).ConfigureAwait(false));
            Assert.That(badDigest.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(m_forwarder.Mutations, Is.Empty);
            ServiceResultException badIdentity = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await bridge.CommitRequestAsync(upload, "another-operation", m_codec.ComputeRequestDigest(request))
                    .ConfigureAwait(false));
            Assert.That(badIdentity.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(m_forwarder.Mutations, Is.Empty);
            (NodeId responseFile, uint responseHandle) = await bridge.CommitRequestAsync(
                upload, string.Empty, m_codec.ComputeRequestDigest(request)).ConfigureAwait(false);
            XRegistryResponse result = m_codec.DecodeResponse(await ReadTransferAsync(
                bridge, responseFile, responseHandle).ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(result.Metadata.GetProperty("name").GetString(), Is.EqualTo("sealed"));
                Assert.That(result.Metadata.GetProperty("epoch").GetUInt32(), Is.EqualTo(1));
                Assert.That(m_forwarder.Mutations, Has.Count.EqualTo(1));
            });
            ServiceResultException repeated = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await bridge.CommitRequestAsync(upload, string.Empty, m_codec.ComputeRequestDigest(request))
                    .ConfigureAwait(false));
            Assert.That(repeated.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            await bridge.AbortRequestAsync(upload).ConfigureAwait(false);
        }

        [Test]
        public async Task AbortAndSessionOwnershipAreEnforcedAsync()
        {
            RegistryBridgeTypeClient bridge = Bridge(m_session);
            (NodeId upload, uint handle) = await bridge.BeginRequestAsync().ConfigureAwait(false);
            await using ManagedSession other = await ConnectAsync().ConfigureAwait(false);
            ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await new FileTypeClient(other, upload, m_telemetry).WriteAsync(handle, ByteString.Empty)
                    .ConfigureAwait(false));
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            ServiceResultException abortDenied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await Bridge(other).AbortRequestAsync(upload).ConfigureAwait(false));
            Assert.That(abortDenied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            await bridge.AbortRequestAsync(upload).ConfigureAwait(false);
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await bridge.CommitRequestAsync(upload, string.Empty, "unused").ConfigureAwait(false));
            Assert.That(m_forwarder.Mutations, Is.Empty);
        }

        [Test]
        public async Task OutcomeDelegatesWithoutInventingHttpReplayAsync()
        {
            XRegistryRequest request = Request(XRegistryAction.Merge, "/",
                /*lang=json,strict*/ """{"name":"once"}""") with
            {
                OperationId = "native-stable-operation"
            };
            XRegistryResponse first = await m_native.ExecuteAsync(request).ConfigureAwait(false);
            XRegistryResponse repeated = await m_native.ExecuteAsync(request).ConfigureAwait(false);
            XRegistryOperationOutcome known = await m_native.GetOperationOutcomeAsync(request.OperationId, s_writer)
                .ConfigureAwait(false);
            XRegistryOperationOutcome unknown = await m_native.GetOperationOutcomeAsync("absent-operation", s_writer)
                .ConfigureAwait(false);
            XRegistryResponse collision = await m_native.ExecuteAsync(
                Request(XRegistryAction.Merge, "/", /*lang=json,strict*/ """{"name":"different"}""") with
                {
                    OperationId = request.OperationId
                })
                .ConfigureAwait(false);
            XRegistryResponse persisted = await m_native.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(repeated.Metadata.GetProperty("epoch").GetUInt32(),
                    Is.EqualTo(first.Metadata.GetProperty("epoch").GetUInt32()));
                Assert.That(known.State, Is.EqualTo(XRegistryOperationState.Committed));
                Assert.That(known.Response!.Metadata.GetProperty("name").GetString(), Is.EqualTo("once"));
                Assert.That(unknown.State, Is.EqualTo(XRegistryOperationState.Unknown));
                Assert.That(unknown.Response, Is.Null);
                Assert.That(collision.IsSuccess, Is.False);
                Assert.That(persisted.Metadata.GetProperty("name").GetString(), Is.EqualTo("once"));
                Assert.That(persisted.Metadata.GetProperty("epoch").GetUInt32(),
                    Is.EqualTo(first.Metadata.GetProperty("epoch").GetUInt32()));
            });
            m_forwarder.AdvertiseReplay = false;
            XRegistryEndpointDescription description = await m_native.InspectAsync(s_writer).ConfigureAwait(false);
            XRegistryResponse rejected = await m_native.ExecuteAsync(
                request with { OperationId = "not-durable" }).ConfigureAwait(false);
            ServiceResultException unsupported = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_native.GetOperationOutcomeAsync("not-durable", s_writer).ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(description.SupportsOperationReplay, Is.False);
                Assert.That(rejected.StatusCode, Is.EqualTo(405));
                Assert.That(unsupported.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(m_forwarder.Mutations, Has.Count.EqualTo(3));
            });
        }

        [TestCase("atomic")]
        [TestCase("conditional")]
        [TestCase("touch")]
        public async Task UnqualifiedEndpointRejectsBeforeMutationAsync(string missing)
        {
            m_forwarder.AdvertiseAtomic = missing != "atomic";
            m_forwarder.AdvertiseConditional = missing != "conditional";
            m_forwarder.AdvertiseTouch = missing != "touch";
            XRegistryResponse response = await m_native.ExecuteAsync(
                Request(XRegistryAction.Merge, "/",
                    /*lang=json,strict*/ """{"name":"forbidden"}""")).ConfigureAwait(false);
            XRegistryEndpointDescription profile = await m_native.InspectAsync(s_writer).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(405));
                Assert.That(response.Error!.Code, Is.EqualTo("action_not_supported"));
                Assert.That(profile.SupportsAtomicMutations, Is.False);
                Assert.That(profile.SupportsConditionalMutations, Is.False);
                Assert.That(profile.SupportsWriteTouch, Is.False);
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task NativeEnvelopeNeverOverridesAuthenticatedCallerAsync()
        {
            XRegistryResponse response = await m_native.ExecuteAsync(
                Request(XRegistryAction.Merge, "/", /*lang=json,strict*/ """{"name":"trusted"}""") with
                {
                    Context = new XRegistryCallContext("forged-admin")
                    {
                        IsAuthenticated = true,
                        Roles = ["administrator"]
                    }
                }).ConfigureAwait(false);
            XRegistryRequest actual = m_forwarder.Mutations.Single();
            Assert.Multiple(() =>
            {
                Assert.That(response.IsSuccess, Is.True);
                Assert.That(actual.Context.Subject, Is.EqualTo(s_writer.Subject));
                Assert.That(actual.Context.Roles.ToArray(), Is.EqualTo(s_writer.Roles.ToArray()));
                Assert.That(actual.Context.SessionId, Is.EqualTo(m_session.SessionId.ToString()));
            });
        }

        [Test]
        public async Task BaseMethodsAndPropertyWritesAwaitAuthoritativeEndpointAsync()
        {
            NodeId group = await m_generic.CreateGroupAsync(m_manager.RegistryNodeId, "created").ConfigureAwait(false);
            (NodeId resource, string version, uint handle) = await m_generic.GetGroup(group)
                .CreateResourceAsync("doc", "v1", false).ConfigureAwait(false);
            Assert.That(handle, Is.Zero);
            Assert.That(version, Is.EqualTo("v1"));
            AttributesTypeClient labels = await m_generic.GetResource(resource).GetLabelsAsync(m_telemetry)
                .ConfigureAwait(false) ??
                throw new InvalidOperationException("Labels missing.");
            await labels.AddAttributeAsync("team", "native", 0).ConfigureAwait(false);
            NodeId description = await ChildAsync(resource, "Description").ConfigureAwait(false);
            m_forwarder.Entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_forwarder.Release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<WriteResponse> write = m_session.WriteAsync(null,
                [new WriteValue { NodeId = description, AttributeId = Attributes.Value,
                    Value = new DataValue(Variant.From("awaited")) }], CancellationToken.None).AsTask();
            await m_forwarder.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            Assert.That(write.IsCompleted, Is.False);
            XRegistryResponse before = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/created/schemas/doc/versions/v1")).ConfigureAwait(false);
            Assert.That(before.Metadata.TryGetProperty("description", out _), Is.False);
            m_forwarder.Release.SetResult(true);
            WriteResponse completed = await write.ConfigureAwait(false);
            Assert.That(completed.Results[0], Is.EqualTo(StatusCodes.Good));
            XRegistryResponse after = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/created/schemas/doc/versions/v1")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(after.Metadata.GetProperty("description").GetString(), Is.EqualTo("awaited"));
                Assert.That(after.Metadata.GetProperty("labels").GetProperty("team").GetString(),
                    Is.EqualTo("native"));
                Assert.That(m_forwarder.Mutations, Has.Count.EqualTo(4));
            });
        }

        [Test]
        public async Task CombinedPropertyWritesAreRejectedWithoutPartialMutationAsync()
        {
            NodeId group = await m_generic.CreateGroupAsync(m_manager.RegistryNodeId, "created").ConfigureAwait(false);
            NodeId name = await ChildAsync(group, "Name").ConfigureAwait(false);
            NodeId description = await ChildAsync(group, "Description").ConfigureAwait(false);
            m_forwarder.Mutations.Clear();
            WriteResponse response = await m_session.WriteAsync(null,
            [
                new WriteValue
                {
                    NodeId = name,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(Variant.From("one"))
                },
                new WriteValue { NodeId = description, AttributeId = Attributes.Value,
                    Value = new DataValue(Variant.From("two")) }
            ], CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.Results.ToArray(), Is.All.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task FileCloseIsPinnedConditionalAndCleanCloseDoesNotTouchAsync()
        {
            await SeedAsync("/schemagroups/g/schemas/r",
                                     /*lang=json,strict*/
                                     """{"versionid":"v1","schema":"first","contenttype":"text/plain"}""")
                                         .ConfigureAwait(false);
            NodeId group = await FindChildEntityAsync(m_manager.RegistryNodeId, "/schemagroups/g")
                .ConfigureAwait(false);
            NodeId logical = await FindChildEntityAsync(group, "/schemagroups/g/schemas/r").ConfigureAwait(false);
            ResourceTypeClient resource = m_generic.GetResource(logical);
            uint handle = await resource.OpenAsync(3).ConfigureAwait(false);
            await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Create, "/schemagroups/g/schemas/r",
                                     /*lang=json,strict*/
                                     """{"versionid":"v2"}""") with
            {
                Document = ByteString.From(Encoding.UTF8.GetBytes("second")),
                ContentType = "text/plain"
            }).ConfigureAwait(false);
            await m_manager.RefreshAsync().ConfigureAwait(false);
            ByteString pinned = await resource.ReadAsync(handle, 256).ConfigureAwait(false);
            Assert.That(Utf8(pinned), Is.EqualTo("first"));
            await resource.SetPositionAsync(handle, 0).ConfigureAwait(false);
            await resource.WriteAsync(handle, ByteString.From(Encoding.UTF8.GetBytes("other"))).ConfigureAwait(false);
            m_forwarder.Mutations.Clear();
            await resource.CloseAsync(handle).ConfigureAwait(false);
            XRegistryResponse first = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/g/schemas/r/versions/v1") with
                {
                    View = XRegistryView.Default
                })
                .ConfigureAwait(false);
            XRegistryResponse second = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/g/schemas/r/versions/v2") with
                {
                    View = XRegistryView.Default
                })
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(Utf8(first.Document), Is.EqualTo("other"));
                Assert.That(Utf8(second.Document), Is.EqualTo("second"));
                Assert.That(m_forwarder.Mutations.Single().Path, Is.EqualTo("/schemagroups/g/schemas/r/versions/v1"));
                Assert.That(m_forwarder.Mutations.Single().Metadata.GetProperty("epoch").GetUInt32(), Is.Zero);
            });
            uint clean = await resource.OpenAsync(2).ConfigureAwait(false);
            await resource.CloseAsync(clean).ConfigureAwait(false);
            uint identical = await resource.OpenAsync(6).ConfigureAwait(false);
            await resource.WriteAsync(identical, second.Document).ConfigureAwait(false);
            await resource.CloseAsync(identical).ConfigureAwait(false);
            Assert.That(m_forwarder.Mutations, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task StaleFileCloseDoesNotOverwriteTheUpstreamEditAsync()
        {
            await SeedAsync("/schemagroups/g/schemas/r", /*lang=json,strict*/ """{"versionid":"v1","schema":"start"}""")
                .ConfigureAwait(false);
            NodeId group = await FindChildEntityAsync(m_manager.RegistryNodeId, "/schemagroups/g")
                .ConfigureAwait(false);
            NodeId logical = await FindChildEntityAsync(group, "/schemagroups/g/schemas/r").ConfigureAwait(false);
            ResourceTypeClient resource = m_generic.GetResource(logical);
            uint handle = await resource.OpenAsync(6).ConfigureAwait(false);
            await resource.WriteAsync(handle, ByteString.From(Encoding.UTF8.GetBytes("stale"))).ConfigureAwait(false);
            await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Replace,
                "/schemagroups/g/schemas/r/versions/v1") with
            {
                Document = ByteString.From(Encoding.UTF8.GetBytes("fresh"))
            }).ConfigureAwait(false);
            ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await resource.CloseAsync(handle).ConfigureAwait(false));
            XRegistryResponse current = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/g/schemas/r/versions/v1") with
                {
                    View = XRegistryView.Default
                })
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(Utf8(current.Document), Is.EqualTo("fresh"));
            });
        }

        [Test]
        public async Task RetiredFileHandlesCannotAliasARecreatedVersionAsync()
        {
            const string path = "/schemagroups/g/schemas/r";
            await SeedAsync(path,
                /*lang=json,strict*/ """{"versionid":"v1","schema":"original"}""").ConfigureAwait(false);
            NodeId group = await FindChildEntityAsync(m_manager.RegistryNodeId, "/schemagroups/g")
                .ConfigureAwait(false);
            NodeId node = await FindChildEntityAsync(group, path).ConfigureAwait(false);
            ResourceTypeClient file = m_generic.GetResource(node);
            uint oldHandle = await file.OpenAsync(1).ConfigureAwait(false);
            XRegistryResponse deleted = await m_native.ExecuteAsync(Request(XRegistryAction.Delete, path))
                .ConfigureAwait(false);
            Assert.That(deleted.IsSuccess, Is.True);
            await SeedAsync(path,
                /*lang=json,strict*/ """{"versionid":"v1","schema":"replacement"}""").ConfigureAwait(false);
            uint newHandle = await file.OpenAsync(1).ConfigureAwait(false);
            ServiceResultException retired = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await file.ReadAsync(oldHandle, 256).ConfigureAwait(false));
            ByteString current = await file.ReadAsync(newHandle, 256).ConfigureAwait(false);
            await file.CloseAsync(newHandle).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(newHandle, Is.Not.EqualTo(oldHandle));
                Assert.That(retired.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(Utf8(current), Is.EqualTo("replacement"));
            });
        }

        [Test]
        public async Task BaseFallbackReadsActualHierarchyAndRefusesWritesAsync()
        {
            await SeedAsync("/schemagroups/g/schemas/r", /*lang=json,strict*/ """{"versionid":"v1","schema":"first"}""")
                .ConfigureAwait(false);
            await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Create, "/schemagroups/g/schemas/r",
                                     /*lang=json,strict*/
                                     """{"versionid":"v2"}""") with
            {
                Document = ByteString.From(Encoding.UTF8.GetBytes("second")),
                ContentType = "text/plain"
            }).ConfigureAwait(false);
            await m_baseManager.RefreshAsync().ConfigureAwait(false);
            var native = new XRegistryOpcUaEndpoint(
                m_session, m_baseManager.RegistryNodeId, m_baseOptions, m_telemetry);
            XRegistryEndpointDescription profile = await native.InspectAsync(s_writer).ConfigureAwait(false);
            XRegistryResponse versions = await native.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/g/schemas/r/versions")).ConfigureAwait(false);
            XRegistryResponse content = await native.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/g/schemas/r") with { View = XRegistryView.Default })
                .ConfigureAwait(false);
            m_forwarder.Mutations.Clear();
            XRegistryResponse denied = await native.ExecuteAsync(
                Request(XRegistryAction.Merge, "/",
                    /*lang=json,strict*/ """{"name":"must-not-write"}""")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(profile.Profile, Is.EqualTo("native-base-readonly-distinct-core"));
                Assert.That(profile.SupportsAtomicMutations, Is.False);
                Assert.That(versions.Metadata.EnumerateObject().Select(value => value.Name),
                    Is.EquivalentTo(s_versions));
                Assert.That(Utf8(content.Document), Is.EqualTo("second"));
                Assert.That(denied.StatusCode, Is.EqualTo(405));
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task ProjectionPreservesCollectionIdentityAndVersionsAsync()
        {
            await ResetEndpointAsync(/*lang=json,strict*/ """
                {"groups":{
                  "left":{"plural":"left","singular":"leftitem",
                    "resources":{"docs":{"plural":"docs","singular":"doc","hasdocument":true},
                    "assets":{"plural":"assets","singular":"asset","hasdocument":true}}},
                  "right":{"plural":"right","singular":"rightitem",
                    "resources":{"docs":{"plural":"docs","singular":"doc","hasdocument":true}}}
                }}
                """).ConfigureAwait(false);
            await SeedAsync("/left/same/docs/r",
                /*lang=json,strict*/ """{"versionid":"a b","doc":"left"}""").ConfigureAwait(false);
            await SeedAsync("/left/same/assets/r",
                /*lang=json,strict*/ """{"versionid":"a b","doc":"asset"}""").ConfigureAwait(false);
            await SeedAsync("/right/same/docs/r",
                /*lang=json,strict*/ """{"versionid":"a b","doc":"right"}""").ConfigureAwait(false);
            NodeId left = await FindChildEntityAsync(m_manager.RegistryNodeId, "/left/same").ConfigureAwait(false);
            NodeId right = await FindChildEntityAsync(m_manager.RegistryNodeId, "/right/same").ConfigureAwait(false);
            NodeId leftResource = await FindChildEntityAsync(left, "/left/same/docs/r").ConfigureAwait(false);
            NodeId rightResource = await FindChildEntityAsync(right, "/right/same/docs/r").ConfigureAwait(false);
            NodeId asset = await FindChildEntityAsync(left, "/left/same/assets/r").ConfigureAwait(false);
            DataValue leftId = await m_session.ReadValueAsync(await ChildAsync(left, "GroupId").ConfigureAwait(false))
                .ConfigureAwait(false);
            DataValue rightId = await m_session.ReadValueAsync(
                await ChildAsync(right, "GroupId").ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(left, Is.Not.EqualTo(right));
                Assert.That(leftResource, Is.Not.EqualTo(rightResource));
                Assert.That(asset, Is.Not.EqualTo(leftResource));
                Assert.That(leftId.WrappedValue.TryGetValue(out string id) ? id : null, Is.EqualTo("same"));
                Assert.That(rightId.WrappedValue.TryGetValue(out string rightValue) ? rightValue : null,
                    Is.EqualTo("same"));
            });
            ServiceResultException ambiguous = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_generic.CreateGroupAsync(m_manager.RegistryNodeId, "new").ConfigureAwait(false));
            Assert.That(ambiguous.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            ServiceResultException ambiguousResource = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_generic.GetGroup(left).CreateResourceAsync("new", "v1", false).ConfigureAwait(false));
            Assert.That(ambiguousResource.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            XRegistryResponse exact = await m_native.ExecuteAsync(
                Request(XRegistryAction.Read, "/right/same/docs/r/versions/a%20b") with
                {
                    View = XRegistryView.Default
                })
                .ConfigureAwait(false);
            Assert.That(Utf8(exact.Document), Is.EqualTo("right"));
        }

        [Test]
        public async Task TypedMetadataAndChunkedBinaryRemainLosslessAsync()
        {
            await ResetEndpointAsync(/*lang=json,strict*/ """
                {"groups":{"schemagroups":{"plural":"schemagroups","singular":"schemagroup",
                "resources":{"schemas":{"plural":"schemas","singular":"schema","hasdocument":true,
                "attributes":{"*":{"type":"any"}}}}}}}
                """).ConfigureAwait(false);
            await m_manager.RefreshAsync().ConfigureAwait(false);
            byte[] bytes = [.. Enumerable.Range(0, 16_387).Select(index => (byte)(index % 251))];
            XRegistryResponse response = await m_native.ExecuteAsync(Request(XRegistryAction.Replace,
                "/schemagroups/g/schemas/r/versions/v1",
                                     /*lang=json,strict*/
                                     """{"count":42,"nested":{"enabled":true,"items":[null,1,"two"]}}""") with
            {
                Document = ByteString.From(bytes),
                ContentType = "application/octet-stream"
            }).ConfigureAwait(false);
            XRegistryResponse actual = await m_native.ExecuteAsync(Request(
                XRegistryAction.Read, "/schemagroups/g/schemas/r/versions/v1") with
            { View = XRegistryView.Default })
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.IsSuccess, Is.True);
                Assert.That(actual.Document.ToArray(), Is.EqualTo(bytes));
                Assert.That(actual.Metadata.GetProperty("count").ValueKind, Is.EqualTo(JsonValueKind.Number));
                Assert.That(actual.Metadata.GetProperty("nested").GetProperty("enabled").GetBoolean(), Is.True);
                Assert.That(actual.Metadata.GetProperty("nested").GetProperty("items")[0].ValueKind,
                    Is.EqualTo(JsonValueKind.Null));
                Assert.That(actual.ContentType, Is.EqualTo("application/octet-stream"));
            });
        }

        [Test]
        public void CommunicationFailureIsNotLegacyDiscovery()
        {
            var namespaces = new NamespaceTable();
            ushort ns = namespaces.GetIndexOrAppend("urn:unreachable-registry");
            var session = new Mock<ISession>();
            session.Setup(value => value.NamespaceUris).Returns(namespaces);
            session.Setup(value => value.BrowseAsync(It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadNoCommunication));
            var native = new XRegistryOpcUaEndpoint(session.Object, new NodeId("root", ns), m_options, m_telemetry);
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await native.InspectAsync(s_writer).ConfigureAwait(false));
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            session.Verify(value => value.ReadAsync(It.IsAny<RequestHeader>(), It.IsAny<double>(),
                It.IsAny<TimestampsToReturn>(), It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task EmptyDocumentRemainsPresentAndZeroEpochIsConditionalAsync()
        {
            XRegistryResponse created = await m_native.ExecuteAsync(Request(
                XRegistryAction.Replace, "/schemagroups/g/schemas/r/versions/v1") with
            { Document = ByteString.Empty })
                .ConfigureAwait(false);
            XRegistryResponse actual = await m_native.ExecuteAsync(Request(
                XRegistryAction.Read, "/schemagroups/g/schemas/r/versions/v1") with
            { View = XRegistryView.Default })
                .ConfigureAwait(false);
            XRegistryResponse first = await m_native.ExecuteAsync(Request(XRegistryAction.Merge,
                "/schemagroups/g/schemas/r/versions/v1",
                    /*lang=json,strict*/ """{"epoch":0,"name":"changed"}""")).ConfigureAwait(false);
            XRegistryResponse stale = await m_native.ExecuteAsync(Request(XRegistryAction.Merge,
                "/schemagroups/g/schemas/r/versions/v1",
                    /*lang=json,strict*/ """{"epoch":0,"name":"stale"}""")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(actual.Document.IsNull, Is.False);
                Assert.That(actual.Document.Length, Is.Zero);
                Assert.That(first.StatusCode, Is.EqualTo(200));
                Assert.That(stale.Error!.Code, Is.EqualTo("mismatched_epoch"));
            });
        }

        [Test]
        public async Task LegacyFallbackEnumeratesRealSiblingVersionsWithoutInventingDefaultAsync()
        {
            string namespaceUri = "urn:native-tests:legacy:" + Guid.NewGuid().ToString("N");
            var factory = new LegacyXRegistryFixture(namespaceUri);
            await m_server.NodeManagerLifecycle.AddAsync(factory, callerContext: null).ConfigureAwait(false);
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            using var model = JsonDocument.Parse("""
                {"groups":{"schemagroups":{"plural":"schemagroups","singular":"schemagroup",
                "resources":{"schemas":{"plural":"schemas","singular":"schema","hasdocument":true}}}}}
                """);
            var native = new XRegistryOpcUaEndpoint(m_session, factory.RootId,
                m_options with { BaseModel = model.RootElement }, m_telemetry);
            XRegistryEndpointDescription profile = await native.InspectAsync(s_writer).ConfigureAwait(false);
            XRegistryResponse versions = await native.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/g/schemas/r/versions")).ConfigureAwait(false);
            XRegistryResponse resource = await native.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/g/schemas/r")).ConfigureAwait(false);
            XRegistryResponse meta = await native.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/g/schemas/r/meta")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(profile.Profile, Is.EqualTo("native-base-readonly-flat-explicit-versions"));
                Assert.That(versions.Metadata.EnumerateObject().Select(entry => entry.Name),
                    Is.EquivalentTo(s_versions));
                Assert.That(versions.Metadata.GetProperty("v1").GetProperty("xid").GetString(),
                    Is.EqualTo("/schemagroups/g/schemas/r/versions/v1"));
                Assert.That(versions.Metadata.GetProperty("v2").GetProperty("xid").GetString(),
                    Is.EqualTo("/schemagroups/g/schemas/r/versions/v2"));
                Assert.That(resource.StatusCode, Is.EqualTo(405));
                Assert.That(meta.StatusCode, Is.EqualTo(405));
            });
        }

        [TestCase("{broken")]
        [TestCase(/*lang=json,strict*/ """{"format":1,"action":1,"path":"relative","view":1,"parameters":[]}""")]
        public async Task UnsealedMalformedAndExpiredUploadsNeverReachEndpointAsync(string malformedEnvelope)
        {
            RegistryBridgeTypeClient bridge = Bridge(m_session);
            XRegistryRequest request = Request(XRegistryAction.Merge, "/", /*lang=json,strict*/ """{"name":"never"}""");
            (NodeId upload, uint handle) = await bridge.BeginRequestAsync().ConfigureAwait(false);
            ServiceResultException unsealed = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await bridge.CommitRequestAsync(upload, string.Empty, m_codec.ComputeRequestDigest(request))
                    .ConfigureAwait(false));
            Assert.That(unsealed.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            var file = new FileTypeClient(m_session, upload, m_telemetry);
            await file.WriteAsync(handle, ByteString.From(Encoding.UTF8.GetBytes(malformedEnvelope)))
                .ConfigureAwait(false);
            await file.CloseAsync(handle).ConfigureAwait(false);
            ServiceResultException malformed = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await bridge.CommitRequestAsync(upload, string.Empty, "unused").ConfigureAwait(false));
            Assert.That(malformed.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            await bridge.AbortRequestAsync(upload).ConfigureAwait(false);
            (upload, handle) = await bridge.BeginRequestAsync().ConfigureAwait(false);
            file = new FileTypeClient(m_session, upload, m_telemetry);
            await WriteChunksAsync(file, handle, m_codec.EncodeRequest(request)).ConfigureAwait(false);
            await file.CloseAsync(handle).ConfigureAwait(false);
            m_clock.Advance(m_options.FileLifetime);
            ServiceResultException expired = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await bridge.CommitRequestAsync(upload, string.Empty, m_codec.ComputeRequestDigest(request))
                    .ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(expired.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
            (NodeId replacement, _) = await bridge.BeginRequestAsync().ConfigureAwait(false);
            await bridge.AbortRequestAsync(replacement).ConfigureAwait(false);
        }

        [Test]
        public async Task NativeAtomicRejectionHasNoPartialProjectionAndJournalReportsRejectedAsync()
        {
            XRegistryResponse response = await m_native.ExecuteAsync(Request(XRegistryAction.Merge, "/",
                                     /*lang=json,strict*/
                                     """{"name":"not-committed","schemagroups":{"first":{},"invalid":null}}""") with
            {
                OperationId = "rejected-native-operation"
            }).ConfigureAwait(false);
            XRegistryResponse root = await m_native.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            XRegistryResponse group = await m_native.ExecuteAsync(Request(XRegistryAction.Read, "/schemagroups/first"))
                .ConfigureAwait(false);
            XRegistryOperationOutcome outcome = await m_native.GetOperationOutcomeAsync(
                "rejected-native-operation", s_writer)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.IsSuccess, Is.False);
                Assert.That(root.Metadata.TryGetProperty("name", out _), Is.False);
                Assert.That(root.Metadata.GetProperty("epoch").GetUInt32(), Is.Zero);
                Assert.That(group.StatusCode, Is.EqualTo(404));
                Assert.That(outcome.State, Is.EqualTo(XRegistryOperationState.Rejected));
                Assert.That(outcome.Response!.StatusCode, Is.EqualTo(response.StatusCode));
            });
        }

        [Test]
        public async Task UpstreamOutageIsNeverServedAsCurrentGoodDataAsync()
        {
            XRegistryResponse before = await m_native.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.That(before.StatusCode, Is.EqualTo(200));
            m_forwarder.FailReads = true;
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_native.ExecuteAsync(Request(XRegistryAction.Read, "/")).ConfigureAwait(false));
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            m_forwarder.FailReads = false;
            XRegistryResponse after = await m_native.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.That(after.Metadata.GetProperty("registryid").GetString(), Is.EqualTo("native-test-registry"));
        }

        [Test]
        public async Task ModelDocumentCapabilityChangesRebindExistingResourceFilesAsync()
        {
            await ResetEndpointAsync(/*lang=json,strict*/ """
                {"groups":{"schemagroups":{"plural":"schemagroups","singular":"schemagroup",
                "resources":{"schemas":{"plural":"schemas","singular":"schema","hasdocument":false}}}}}
                """).ConfigureAwait(false);
            XRegistryResponse created = await m_native.ExecuteAsync(Request(
                XRegistryAction.Replace, "/schemagroups/g/schemas/r/versions/v1", "{}")).ConfigureAwait(false);
            Assert.That(created.IsSuccess, Is.True);
            NodeId group = await FindChildEntityAsync(m_manager.RegistryNodeId, "/schemagroups/g")
                .ConfigureAwait(false);
            NodeId logical = await FindChildEntityAsync(group, "/schemagroups/g/schemas/r").ConfigureAwait(false);
            ResourceTypeClient file = m_generic.GetResource(logical);
            ServiceResultException unavailable = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await file.OpenAsync(2).ConfigureAwait(false));
            Assert.That(unavailable.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            XRegistryResponse model = await m_native.ExecuteAsync(Request(XRegistryAction.Merge, "/modelsource",
                                     /*lang=json,strict*/
                                     """{"groups":{"schemagroups":{"resources":{"schemas":{"hasdocument":true}}}}}"""))
                .ConfigureAwait(false);
            Assert.That(model.IsSuccess, Is.True, model.Error?.Detail);
            uint handle = await file.OpenAsync(6).ConfigureAwait(false);
            await file.WriteAsync(handle, ByteString.From(Encoding.UTF8.GetBytes("enabled"))).ConfigureAwait(false);
            await file.CloseAsync(handle).ConfigureAwait(false);
            XRegistryResponse actual = await m_native.ExecuteAsync(Request(
                XRegistryAction.Read, "/schemagroups/g/schemas/r/versions/v1") with
            { View = XRegistryView.Default })
                .ConfigureAwait(false);
            Assert.That(Utf8(actual.Document), Is.EqualTo("enabled"));
        }

        [Test]
        public async Task JournalInterfaceIsRequiredEvenWhenEndpointAdvertisesReplayAsync()
        {
            var bare = new Mock<IXRegistryEndpoint>();
            bare.Setup(endpoint => endpoint.InspectAsync(
                It.IsAny<XRegistryCallContext>(), It.IsAny<CancellationToken>()))
                .Returns((XRegistryCallContext context, CancellationToken ct) =>
                    m_forwarder.InspectAsync(context, ct));
            bare.Setup(endpoint => endpoint.ExecuteAsync(
                It.IsAny<XRegistryRequest>(), It.IsAny<CancellationToken>()))
                .Returns((XRegistryRequest request, CancellationToken ct) => m_forwarder.ExecuteAsync(request, ct));
            XRegistryBridgeNativeOptions options = m_options with
            {
                NamespaceUri = "urn:native-tests:no-journal:" + Guid.NewGuid().ToString("N")
            };
            var factory = new CapturingFactory(new XRegistryBridgeNodeManagerFactory(bare.Object, options));
            await m_server.NodeManagerLifecycle.AddAsync(factory, callerContext: null).ConfigureAwait(false);
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            var native = new XRegistryOpcUaEndpoint(m_session, factory.Manager!.RegistryNodeId, options, m_telemetry);
            XRegistryEndpointDescription description = await native.InspectAsync(s_writer).ConfigureAwait(false);
            XRegistryResponse response = await native.ExecuteAsync(Request(XRegistryAction.Merge, "/", "{}") with
            {
                OperationId = "not-available"
            }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(description.SupportsAtomicMutations, Is.True);
                Assert.That(description.SupportsOperationReplay, Is.False);
                Assert.That(response.StatusCode, Is.EqualTo(405));
            });
            bare.Verify(endpoint => endpoint.ExecuteAsync(
                It.Is<XRegistryRequest>(request => request.IsMutation), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task InsecureClientMutationIsRejectedBeforeStagingAsync()
        {
            await using ManagedSession insecure = await ConnectInsecureAsync().ConfigureAwait(false);
            var native = new XRegistryOpcUaEndpoint(insecure, m_manager.RegistryNodeId, m_options, m_telemetry);
            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await native.ExecuteAsync(Request(XRegistryAction.Merge, "/",
                    /*lang=json,strict*/ """{"name":"insecure"}"""))
                    .ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
                Assert.That(m_forwarder.Mutations, Is.Empty);
            });
        }

        private async Task ResetEndpointAsync(string? modelJson = null)
        {
            m_forwarder.Inner?.Dispose();
            m_store?.Dispose();
            using var model = JsonDocument.Parse(modelJson ?? /*lang=json,strict*/ """
                {"groups":{"schemagroups":{"plural":"schemagroups","singular":"schemagroup",
                "resources":{"schemas":{"plural":"schemas","singular":"schema","hasdocument":true}}}}}
                """);
            m_store = new FileXRegistryTransactionStore(Path.Combine(m_root, "state", Guid.NewGuid().ToString("N")));
            m_forwarder.Inner = new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                RegistryId = "native-test-registry",
                Model = model.RootElement,
                PublicRoot = new Uri("https://registry.example.test/")
            }, m_store);
            m_forwarder.AdvertiseAtomic = true;
            m_forwarder.AdvertiseConditional = true;
            m_forwarder.AdvertiseTouch = true;
            m_forwarder.AdvertiseReplay = true;
            m_forwarder.FailReads = false;
            m_forwarder.Entered = null;
            m_forwarder.Release = null;
            m_forwarder.Mutations.Clear();
            await m_forwarder.Inner.InspectAsync(s_writer).ConfigureAwait(false);
        }

        private async Task SeedAsync(string path, string json)
        {
            var body = (JsonObject)JsonNode.Parse(json)!;
            string content = body["schema"]?.GetValue<string>() ??
                body["doc"]?.GetValue<string>() ??
                    throw new InvalidOperationException("The binary seed requires a document.");
            body.Remove("schema");
            body.Remove("doc");
            XRegistryResponse response = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Replace, path, body.ToJsonString()) with
                {
                    Document = ByteString.From(Encoding.UTF8.GetBytes(content)),
                    ContentType = "text/plain"
                }).ConfigureAwait(false);
            Assert.That(response.IsSuccess, Is.True, response.Error?.Detail);
            await m_manager.RefreshAsync().ConfigureAwait(false);
        }

        private async Task<ManagedSession> ConnectAsync(
            string? subject = null, ConfiguredEndpoint? endpoint = null, bool anonymous = false)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IUserIdentity identity = anonymous
                ? new UserIdentity()
                : new UserIdentity(subject ?? m_allowedUser, m_password.Span);
            return await ManagedSession.CreateAsync(m_clientConfiguration, endpoint ?? m_configuredEndpoint,
                new DefaultSessionFactory(m_telemetry), identity, telemetry: m_telemetry,
                sessionName: "NativeBridge-" + Guid.NewGuid().ToString("N"), ct: timeout.Token).ConfigureAwait(false);
        }

        private Task<ManagedSession> ConnectInsecureAsync()
        {
            return ConnectAsync(endpoint: m_insecureEndpoint);
        }

        private XRegistryCallContext MapCaller(ISystemContext context)
        {
            Interlocked.Increment(ref m_mappingCalls);
            return s_writer with
            {
                SessionId = context is ISessionSystemContext { SessionId: { } id } ? id.ToString() : null
            };
        }

        private async ValueTask<bool> AuthorizeInboundAsync(
            ISystemContext context, bool isMutation, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!isMutation)
            {
                return !m_denyReads;
            }
            m_authorizationEntered?.TrySetResult(true);
            if (m_authorizationRelease is not null)
            {
                return await m_authorizationRelease.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            if (m_denyWrites ||
                context is not ISessionSystemContext { UserIdentity: { } identity } ||
                (identity.DisplayName != m_allowedUser && identity.DisplayName != m_roleDeniedUser))
            {
                return false;
            }
            foreach (NodeId role in identity.GrantedRoleIds)
            {
                if (role == Ua.ObjectIds.WellKnownRole_Operator)
                {
                    return true;
                }
            }
            return false;
        }

        private RegistryBridgeTypeClient Bridge(ManagedSession session)
        {
            ushort ns = checked((ushort)session.NamespaceUris.GetIndex(m_options.NamespaceUri));
            return new RegistryBridgeTypeClient(
                session, new NodeId(m_options.RootIdentifier + "/Bridge", ns), m_telemetry);
        }

        private async Task WriteChunksAsync(FileTypeClient file, uint handle, ByteString bytes)
        {
            for (int offset = 0; offset < bytes.Length; offset += m_options.ChunkSize)
            {
                await file.WriteAsync(handle,
                    new ByteString(bytes.Slice(offset, Math.Min(m_options.ChunkSize, bytes.Length - offset))))
                    .ConfigureAwait(false);
            }
        }

        private async Task<ByteString> ReadTransferAsync(RegistryBridgeTypeClient bridge, NodeId id, uint handle)
        {
            var file = new FileTypeClient(m_session, id, m_telemetry);
            ByteString bytes = await XRegistryOpcUaEndpoint.ReadFileAsync(
                file, handle, m_options.ChunkSize, m_options.MaxMessageBytes, CancellationToken.None)
                .ConfigureAwait(false);
            await file.CloseAsync(handle).ConfigureAwait(false);
            await bridge.AbortRequestAsync(id).ConfigureAwait(false);
            return bytes;
        }

        private async Task<NodeId> ChildAsync(NodeId parent, string name)
        {
            ArrayOf<ReferenceDescription> children = await XRegistryOpcUaEndpoint.BrowseAsync(
                m_session, parent, m_options, CancellationToken.None).ConfigureAwait(false);
            ReferenceDescription child = children.ToList().Single(reference =>
                reference.BrowseName.Name == name &&
                m_session.NamespaceUris.GetString(reference.BrowseName.NamespaceIndex) ==
                    XRegistryWellKnown.XRegistryNamespaceUri);
            return ExpandedNodeId.ToNodeId(child.NodeId, m_session.NamespaceUris);
        }

        private async Task<NodeId> FindChildEntityAsync(NodeId parent, string path)
        {
            ArrayOf<ReferenceDescription> children = await XRegistryOpcUaEndpoint.BrowseAsync(
                m_session, parent, m_options, CancellationToken.None).ConfigureAwait(false);
            foreach (ReferenceDescription reference in children.ToList())
            {
                var type = ExpandedNodeId.ToNodeId(reference.TypeDefinition, m_session.NamespaceUris);
                if (type != ExpandedNodeId.ToNodeId(ObjectTypeIds.GroupType, m_session.NamespaceUris) &&
                    type != ExpandedNodeId.ToNodeId(ObjectTypeIds.ResourceType, m_session.NamespaceUris))
                {
                    continue;
                }
                var node = ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris);
                DataValue xid = await m_session.ReadValueAsync(await ChildAsync(node, "Xid").ConfigureAwait(false))
                    .ConfigureAwait(false);
                if (xid.WrappedValue.TryGetValue(out string value) && value == path)
                {
                    return node;
                }
            }
            throw new InvalidOperationException("No native entity for " + path);
        }

        private static XRegistryRequest Request(XRegistryAction action, string path, string? json = null)
        {
            using JsonDocument? document = json is null ? null : JsonDocument.Parse(json);
            return new XRegistryRequest(action, path)
            {
                Context = s_writer,
                View = XRegistryView.Metadata,
                Metadata = document?.RootElement ?? default
            };
        }

        private static string Utf8(ByteString bytes)
        {
            return Encoding.UTF8.GetString(bytes.Span.ToArray());
        }

        private sealed class ForwardingEndpoint : IXRegistryOperationJournalEndpoint, IXRegistryPreparedEndpoint
        {
            public XRegistryTransactionalEndpoint Inner { get; set; } = null!;
            public bool AdvertiseAtomic { get; set; } = true;
            public bool AdvertiseConditional { get; set; } = true;
            public bool AdvertiseTouch { get; set; } = true;
            public bool AdvertiseReplay { get; set; } = true;
            public bool FailReads { get; set; }
            public ConcurrentQueue<XRegistryRequest> Mutations { get; } = new();
            public TaskCompletionSource<bool>? Entered { get; set; }
            public TaskCompletionSource<bool>? Release { get; set; }

            public async ValueTask<XRegistryEndpointDescription> InspectAsync(
                XRegistryCallContext context, CancellationToken cancellationToken = default)
            {
                XRegistryEndpointDescription description = await Inner.InspectAsync(context, cancellationToken)
                    .ConfigureAwait(false);
                return description with
                {
                    SupportsAtomicMutations = AdvertiseAtomic && description.SupportsAtomicMutations,
                    SupportsConditionalMutations = AdvertiseConditional && description.SupportsConditionalMutations,
                    SupportsWriteTouch = AdvertiseTouch && description.SupportsWriteTouch,
                    SupportsOperationReplay = AdvertiseReplay && description.SupportsOperationReplay
                };
            }

            public async ValueTask<XRegistryResponse> ExecuteAsync(
                XRegistryRequest request, CancellationToken cancellationToken = default)
            {
                if (!request.IsMutation && FailReads)
                {
                    throw new ServiceResultException(StatusCodes.BadNoCommunication);
                }
                if (request.IsMutation)
                {
                    Mutations.Enqueue(request);
                    Entered?.TrySetResult(true);
                    if (Release is not null)
                    {
                        await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                return await Inner.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            }

            public ValueTask<XRegistryOperationOutcome> GetOperationOutcomeAsync(
                string operationId, XRegistryCallContext context, CancellationToken cancellationToken = default)
            {
                return Inner.GetOperationOutcomeAsync(operationId, context, cancellationToken);
            }

            public async ValueTask<IXRegistryPreparedOperation> PrepareAsync(
                XRegistryRequest request, CancellationToken cancellationToken = default)
            {
                IXRegistryPreparedOperation operation = await Inner.PrepareAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                return new ForwardedPreparation(this, request, operation);
            }

            private sealed class ForwardedPreparation(
                ForwardingEndpoint owner, XRegistryRequest request, IXRegistryPreparedOperation inner)
                : IXRegistryPreparedOperation
            {
                public XRegistryResponse Response => inner.Response;

                public async ValueTask<XRegistryResponse> CommitAsync(CancellationToken cancellationToken = default)
                {
                    owner.Mutations.Enqueue(request);
                    owner.Entered?.TrySetResult(true);
                    if (owner.Release is not null)
                    {
                        await owner.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    return await inner.CommitAsync(cancellationToken).ConfigureAwait(false);
                }

                public ValueTask DisposeAsync()
                {
                    return inner.DisposeAsync();
                }
            }
        }

        private sealed class CapturingFactory(IAsyncNodeManagerFactory inner) : IAsyncNodeManagerFactory
        {
            public XRegistryBridgeNodeManager? Manager { get; private set; }
            public ArrayOf<string> NamespacesUris => inner.NamespacesUris;

            public async ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server, ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                IAsyncNodeManager manager = await inner.CreateAsync(server, configuration, cancellationToken)
                    .ConfigureAwait(false);
                Manager = (XRegistryBridgeNodeManager)manager;
                return manager;
            }
        }

        private sealed class ManualClock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow()
            {
                return new DateTimeOffset(Interlocked.Read(ref m_ticks), TimeSpan.Zero);
            }

            public void Advance(TimeSpan value)
            {
                Interlocked.Add(ref m_ticks, value.Ticks);
            }

            private long m_ticks = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        }

        private static readonly XRegistryCallContext s_writer = new("native-test-operator")
        {
            IsAuthenticated = true,
            Roles = ["xregistry.write"]
        };

        private static readonly string[] s_versions = ["v1", "v2"];

        private readonly XRegistryProtocolCodec m_codec = new();
        private readonly ManualClock m_clock = new();
        private ITelemetryContext m_telemetry = null!;
        private string m_allowedUser = null!;
        private string m_deniedUser = null!;
        private string m_roleDeniedUser = null!;
        private ByteString m_password;
        private bool m_denyWrites;
        private bool m_denyReads;
        private int m_mappingCalls;
        private TaskCompletionSource<bool>? m_authorizationEntered;
        private TaskCompletionSource<bool>? m_authorizationRelease;
        private string m_root = null!;
        private ForwardingEndpoint m_forwarder = null!;
        private FileXRegistryTransactionStore? m_store;
        private XRegistryBridgeNativeOptions m_options = null!;
        private XRegistryBridgeNativeOptions m_baseOptions = null!;
        private ServerFixture<ReferenceServer> m_serverFixture = null!;
        private ReferenceServer m_server = null!;
        private XRegistryBridgeNodeManager m_manager = null!;
        private XRegistryBridgeNodeManager m_baseManager = null!;
        private ApplicationInstance m_application = null!;
        private ApplicationConfiguration m_clientConfiguration = null!;
        private ConfiguredEndpoint m_configuredEndpoint = null!;
        private ConfiguredEndpoint m_insecureEndpoint = null!;
        private ManagedSession m_session = null!;
        private XRegistryOpcUaEndpoint m_native = null!;
        private GenericXRegistryClient m_generic = null!;
    }
}
