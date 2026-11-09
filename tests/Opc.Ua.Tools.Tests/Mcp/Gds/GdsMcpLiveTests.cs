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

#if NET10_0
using System;
using System.Buffers.Text;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database.Linq;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Gds.Server.Identity;
using Opc.Ua.Mcp;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;
using UaRole = Opc.Ua.Server.Role;
using UaSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Runs SDK-bound GDS directory and push-metadata tools against an encrypted local reference server.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class GdsMcpLiveTests
    {
        /// <summary>
        /// Starts only an isolated local server with runtime-generated test credentials and owned PKI paths.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_root = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"gds-live-mcp-{Guid.NewGuid():N}");
            Directory.CreateDirectory(m_root);
            string password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "aA7!";
            m_fixture = new ServerFixture<ReferenceServer>(telemetry =>
            {
                var server = new ReferenceServer(telemetry);
                m_certificateGroups = new CertificateGroup(telemetry);
                server.AddNodeManager(new LiveGdsFactory(m_certificateGroups));
                return server;
            })
            {
                AutoAccept = true,
                SecurityNone = true,
                OperationLimits = true
            };
            await m_fixture.LoadConfigurationAsync(Path.Combine(m_root, "server-pki")).ConfigureAwait(false);
            m_fixture.Config.ServerConfiguration!.UserTokenPolicies =
                [new UserTokenPolicy(UserTokenType.Anonymous), new UserTokenPolicy(UserTokenType.UserName)];
            m_fixture.Config.UpdateExtension(null, new GlobalDiscoveryServerConfiguration
            {
                AuthoritiesStorePath = Path.Combine(m_root, "authorities"),
                ApplicationCertificatesStorePath = Path.Combine(m_root, "applications"),
                BaseCertificateGroupStorePath = Path.Combine(m_root, "groups"),
                CertificateGroups = [],
                DatabaseStorePath = Path.Combine(m_root, "database.json")
            });
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
            var userManagement = m_server.CurrentInstance!.UserManagement
                ?? throw new AssertionException("The reference server must provide user management.");
            ServiceResult provisioned = userManagement.AddUser(
                "gds-test-operator", password, UserConfigurationMask.None, string.Empty);
            Assert.That(ServiceResult.IsGood(provisioned), Is.True);
            m_server.UserDatabase.CreateUser("gds-test-operator", Encoding.UTF8.GetBytes(password),
                [GdsRole.DiscoveryAdmin, GdsRole.CertificateAuthorityAdmin, UaRole.SecurityAdmin,
                    UaRole.AuthenticatedUser]);
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore(new OpcUaMcpOptions
            {
                TransferRoot = m_root,
                MaxTransferBytes = 1024 * 1024,
                Execution = new McpExecutionOptions
                {
                    EnabledCapabilities =
                        [McpCapability.DirectoryAdministration, McpCapability.CertificateAdministration,
                            McpCapability.CredentialIssuance],
                    CallerGrants =
                    [
                        new McpCallerGrant
                        {
                            Caller = GdsTestContext.Owner,
                            Capabilities =
                                [McpCapability.DirectoryAdministration, McpCapability.CertificateAdministration,
                                    McpCapability.CredentialIssuance],
                            SessionNames = ["encrypted", "anonymous", "unencrypted"]
                        }
                    ]
                }
            });
            var secretStore = new InMemorySecretStore("GdsMcpLive");
            services.AddOpcUaMcpSecrets(new SecretRegistry(secretStore), issuanceStore: secretStore);
            services.AddOpcUaMcpGds();
            m_client = new ClientFixture(NUnitTelemetryContext.Create());
            await m_client.LoadClientConfigurationAsync(Path.Combine(m_root, "client-pki"), "GdsMcpLiveClient")
                .ConfigureAwait(false);
            m_client.Config.SecurityConfiguration.AutoAcceptUntrustedCertificates = false;
            ICertificateManager clientCertificates = m_client.Config.CertificateManager!;
            await clientCertificates.UpdateAsync(
                m_client.Config.SecurityConfiguration, m_client.Config.ApplicationUri).ConfigureAwait(false);
            using var serverCertificate = m_fixture.Config.CertificateManager!
                .AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256)
                ?? throw new AssertionException("The local server must provide its secure endpoint certificate.");
            using Certificate trustedCertificate = Certificate.FromRawData(serverCertificate.Certificate.RawData);
            await using (ITrustListTransaction trust = await clientCertificates
                .BeginUpdateAsync(TrustListIdentifier.Peers).ConfigureAwait(false))
            {
                await trust.AddTrustedCertificateAsync(trustedCertificate).ConfigureAwait(false);
                await trust.CommitAsync().ConfigureAwait(false);
            }
            services.AddSingleton(new OpcUaClientOptions { Configuration = m_client.Config });
            services.AddMcpServer().WithStreamServerTransport(Stream.Null, Stream.Null)
                .WithOpcUaGdsTools(McpToolProfileSet.Parse("gds,certificates,credentials"));
            m_services = services.BuildServiceProvider();
            m_sessions = m_services.GetRequiredService<OpcUaSessionManager>();
            string endpoint = $"opc.tcp://localhost:{m_fixture.Port}";
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await m_sessions.ConnectAsync("encrypted", endpoint, "SignAndEncrypt", "Basic256Sha256",
                "Username", "gds-test-operator", password, false, startup.Token).ConfigureAwait(false);
            await m_sessions.ConnectAsync("anonymous", endpoint, "SignAndEncrypt", "Basic256Sha256",
                "Anonymous", null, null, true, startup.Token).ConfigureAwait(false);
            await m_sessions.ConnectAsync("unencrypted", endpoint, "None", "None",
                "Anonymous", null, null, true, startup.Token).ConfigureAwait(false);
            UaSession session = m_sessions.GetSessionOrThrow("encrypted");
            m_directory = ExpandedNodeId.ToNodeId(Gds.ObjectIds.Directory, session.NamespaceUris).ToString();
        }

        /// <summary>
        /// Releases the owned clients/server and deletes only the fixture's generated artifacts.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            if (m_sessions is not null)
            {
                foreach (OpcUaSessionManager.SessionInfo session in m_sessions.GetAllSessions())
                {
                    await m_sessions.DisconnectAsync(session.Name).ConfigureAwait(false);
                }
                m_sessions.Dispose();
            }
            if (m_services is not null)
            {
                await m_services.DisposeAsync().ConfigureAwait(false);
            }
            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            m_server?.Dispose();
            m_certificateGroups?.Dispose();
            if (m_client is not null)
            {
                await m_client.DisposeAsync().ConfigureAwait(false);
            }
            if (m_root is not null && Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        [Test]
        public async Task EncryptedSdkDirectoryCrudPreservesLocalizedRecordAndServerAuthorizationAsync()
        {
            CallToolResult created = await CallAsync("gds_register_application", new JsonObject
            {
                ["directoryNodeId"] = m_directory,
                ["applicationNames"] = new JsonArray(new JsonObject { ["locale"] = "en", ["text"] = "MCP live" }),
                ["input"] = new JsonObject
                {
                    ["applicationUri"] = "urn:test:mcp:gds:live-application",
                    ["applicationType"] = "Server",
                    ["productUri"] = "urn:test:mcp:gds:product",
                    ["discoveryUrls"] = new JsonArray("opc.tcp://unused.invalid:4840"),
                    ["serverCapabilities"] = new JsonArray("DA")
                }
            }).ConfigureAwait(false);
            Assert.That(created.IsError, Is.False, RemoteModelTestSession.Payload(created).GetRawText());
            string application = RemoteModelTestSession.Payload(created).GetProperty("applicationId").GetString()!;
            CallToolResult read = await CallAsync("gds_get_application", new JsonObject
            {
                ["directoryNodeId"] = m_directory,
                ["applicationId"] = application
            }).ConfigureAwait(false);
            Assert.That(read.IsError, Is.False);
            Assert.That(RemoteModelTestSession.Payload(read).GetProperty("application").GetProperty("ApplicationUri")
                .GetString(), Is.EqualTo("urn:test:mcp:gds:live-application"));
            CallToolResult denied = await CallAsync("gds_unregister_application", new JsonObject
            {
                ["directoryNodeId"] = m_directory,
                ["applicationId"] = application
            }, "anonymous").ConfigureAwait(false);
            RemoteModelTestSession.Error(denied, StatusCodes.BadUserAccessDenied);

            CallToolResult updated = await CallAsync("gds_update_application", new JsonObject
            {
                ["directoryNodeId"] = m_directory,
                ["applicationNames"] = new JsonArray(new JsonObject { ["locale"] = "de", ["text"] = "Aktualisiert" }),
                ["input"] = new JsonObject
                {
                    ["applicationId"] = application,
                    ["applicationUri"] = "urn:test:mcp:gds:live-application",
                    ["applicationType"] = "Server",
                    ["productUri"] = "urn:test:mcp:gds:product",
                    ["discoveryUrls"] = new JsonArray("opc.tcp://unused.invalid:4840"),
                    ["serverCapabilities"] = new JsonArray("DA")
                }
            }).ConfigureAwait(false);
            Assert.That(updated.IsError, Is.False);
            CallToolResult reread = await CallAsync("gds_get_application", new JsonObject
            {
                ["directoryNodeId"] = m_directory,
                ["applicationId"] = application
            }).ConfigureAwait(false);
            Assert.That(RemoteModelTestSession.Payload(reread)
                .GetProperty("application").GetProperty("ApplicationNames")[0]
                .GetProperty("Text").GetString(), Is.EqualTo("Aktualisiert"));
            CallToolResult removed = await CallAsync("gds_unregister_application", new JsonObject
            {
                ["directoryNodeId"] = m_directory,
                ["applicationId"] = application
            }).ConfigureAwait(false);
            Assert.That(removed.IsError, Is.False);
        }

        [Test]
        public async Task EncryptedPushInspectionReportsServerCapabilitiesAndRefusesPlaintextSessionAsync()
        {
            string configuration = ObjectIds.ServerConfiguration.ToString();
            CallToolResult inspected = await CallAsync("certificate_inspect_push", new JsonObject
            {
                ["configurationNodeId"] = configuration,
                ["maxResults"] = 10
            }).ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(inspected);
            Assert.That(inspected.IsError, Is.False, json.GetRawText());
            Assert.That(json.GetProperty("properties").GetProperty("SupportsTransactions")
                .GetProperty("dataValue").GetProperty("Value").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("groups").GetProperty("items").GetArrayLength(), Is.GreaterThan(0));
            Assert.That(json.GetProperty("sessionId").GetString(),
                Is.EqualTo(m_sessions!.GetSessionOrThrow("encrypted").SessionId.ToString()));

            CallToolResult denied = await CallAsync("certificate_inspect_push", new JsonObject
            {
                ["configurationNodeId"] = configuration
            }, "unencrypted").ConfigureAwait(false);
            RemoteModelTestSession.Error(denied, StatusCodes.BadSecurityModeInsufficient);
        }

        [Test]
        public async Task EncryptedAuthorizationIssuesSignedAudienceBoundTokensAndRefreshesOnlyByReferenceAsync()
        {
            CallToolResult listed = await CallAsync("credential_list_services", new JsonObject
            {
                ["kind"] = "Authorization",
                ["maxResults"] = 10
            }).ConfigureAwait(false);
            Assert.That(listed.IsError, Is.False, RemoteModelTestSession.Payload(listed).GetRawText());
            string service = RemoteModelTestSession.Payload(listed).GetProperty("items")[0]
                .GetProperty("nodeId").GetString()!;
            CallToolResult started = await CallAsync("credential_start_token_request", new JsonObject
            {
                ["serviceNodeId"] = service,
                ["resourceId"] = kAudience,
                ["policyId"] = "jwt"
            }).ConfigureAwait(false);
            Assert.That(started.IsError, Is.False, RemoteModelTestSession.Payload(started).GetRawText());
            string requestId = RemoteModelTestSession.Payload(started).GetProperty("requestId").GetString()!;
            CallToolResult issued = await CallAsync("credential_finish_token_request", new JsonObject
            {
                ["serviceNodeId"] = service,
                ["requestId"] = requestId,
                ["input"] = new JsonObject
                {
                    ["requestedRoles"] = new JsonArray(),
                    ["proof"] = new JsonObject { ["kind"] = "Anonymous" }
                }
            }).ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(issued);
            Assert.That(issued.IsError, Is.False, json.GetRawText());
            string accessReference = json.GetProperty("accessToken").GetProperty("reference").GetString()!;
            string refreshReference = json.GetProperty("refreshToken").GetProperty("reference").GetString()!;
            ServiceProvider services = m_services ?? throw new InvalidOperationException("The fixture is not started.");
            McpSecretReferences secrets = services.GetRequiredService<McpSecretReferences>();
            using (services.GetRequiredService<McpCallerContext>().EnterTrustedCaller(GdsTestContext.Owner))
            {
                using ISecret access = await secrets.ResolveAsync(
                    accessReference, kAudience, McpCapability.CredentialIssuance).ConfigureAwait(false);
                string accessText = Encoding.UTF8.GetString(access.Bytes);
                string[] segments = accessText.Split('.');
                Assert.That(segments, Has.Length.EqualTo(3));
                using var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(segments[1]));
                Assert.That(claims.RootElement.GetProperty("aud").GetString(), Is.EqualTo(kAudience));
                Assert.That(claims.RootElement.GetProperty("iss").GetString(), Is.EqualTo(kIssuer));
                Assert.That(json.GetRawText().Contains(accessText, StringComparison.Ordinal), Is.False);
            }
            CallToolResult refreshed = await CallAsync("credential_refresh_token", new JsonObject
            {
                ["serviceNodeId"] = service,
                ["resourceId"] = kAudience,
                ["refreshReference"] = refreshReference
            }).ConfigureAwait(false);
            Assert.That(refreshed.IsError, Is.False, RemoteModelTestSession.Payload(refreshed).GetRawText());
            Assert.That(RemoteModelTestSession.Payload(refreshed).GetProperty("previousReference").GetString(),
                Is.EqualTo("invalidated"));
            using (services.GetRequiredService<McpCallerContext>().EnterTrustedCaller(GdsTestContext.Owner))
            {
                Assert.That(() => secrets.RequireReference(
                    refreshReference, kAudience, McpCapability.CredentialIssuance),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadUserAccessDenied));
            }
        }

        /// <summary>
        /// Invokes the real registered SDK tool within the host-authenticated caller scope.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private async Task<CallToolResult> CallAsync(
            string name, JsonObject arguments, string sessionName = "encrypted")
        {
            ServiceProvider services = m_services ?? throw new InvalidOperationException("The fixture is not started.");
            using IDisposable scope = services.GetRequiredService<McpCallerContext>()
                .EnterTrustedCaller(GdsTestContext.Owner);
            arguments["sessionName"] = sessionName;
            using var document = JsonDocument.Parse(arguments.ToJsonString());
            McpServerTool tool = services.GetServices<McpServerTool>()
                .Single(candidate => candidate.ProtocolTool.Name == name);
            var request = new RequestContext<CallToolRequestParams>(
                services.GetRequiredService<McpServer>(),
                new JsonRpcRequest { Method = "tools/call", Id = new RequestId("gds-live") },
                new CallToolRequestParams
                {
                    Name = name,
                    Arguments = document.RootElement.EnumerateObject()
                        .ToDictionary(property => property.Name, property => property.Value.Clone())
                })
            {
                MatchedPrimitive = tool
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            return await tool.InvokeAsync(request, timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Installs the production GDS node manager with an in-memory application database for this local fixture.
        /// </summary>
        private sealed class LiveGdsFactory(ICertificateGroup groups) : IAsyncNodeManagerFactory
        {
            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris => [.. ApplicationsNodeManager.DefaultNamespaceUris()];

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server, ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                server.MessageContext.Factory.Builder.AddOpcUaGds().Commit();
                var database = new LinqApplicationsDatabase();
                var options = new AuthorizationServiceOptions { IssuerUri = kIssuer };
                options.AllowedAudiences.Add(kAudience);
                options.DefaultScopes.Add("read");
                var issuer = new CertificateJwtIssuer(
                    options, configuration.CertificateManager!.CertificateProvider!, server.Telemetry);
                var provider = new InMemoryAccessTokenProvider(issuer, options);
                var authorization = new AuthorizationServiceManager(provider, issuer, options);
                authorization.Initialize(configuration);
                IAsyncNodeManager manager = new ApplicationsNodeManager(
                    server, configuration, database, database, groups, autoApprove: false)
                {
                    AccessTokenProvider = authorization
                };
                return ValueTask.FromResult(manager);
            }
        }

        private string? m_root;
        private string m_directory = string.Empty;
        private ServerFixture<ReferenceServer>? m_fixture;
        private ReferenceServer? m_server;
        private CertificateGroup? m_certificateGroups;
        private ServiceProvider? m_services;
        private OpcUaSessionManager? m_sessions;
        /// <summary>The fixture-owned client configuration and application certificates.</summary>
        private ClientFixture? m_client;
        private const string kAudience = "urn:test:mcp:gds:token-audience";
        private const string kIssuer = "urn:test:mcp:gds:token-issuer";
    }
}
#endif
