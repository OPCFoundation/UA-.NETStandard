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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database.Linq;
using Opc.Ua.Gds.Server.Diagnostics;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Gds.Server.Identity;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Gds.Tests.AuthorizationService
{
    [TestFixture]
    [Category("AuthorizationService")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class RefreshTokenTests
    {
        private const string Issuer = "urn:opcua:test:gds";
        private const string Audience = "urn:opcua:test:server";
        private static readonly string[] s_authenticatedUserRole = ["AuthenticatedUser"];

        private ServerFixture<ReferenceServer> m_serverFixture;
        private ClientFixture m_clientFixture;
        private ISession m_session;
        private Uri m_serverUrl;
        private string m_pkiRoot;
        private Certificate m_signingCertificate;
        private InProcessCertificateProvider m_certificateProvider;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "RefreshTokenTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_pkiRoot);

            m_signingCertificate = CertificateBuilder
                .Create("CN=GDS JWT Signing, O=OPC Foundation")
                .SetECCurve(ECCurve.NamedCurves.nistP256)
                .CreateForECDsa();
            m_certificateProvider = new InProcessCertificateProvider(m_signingCertificate);

            var options = new AuthorizationServiceOptions
            {
                IssuerUri = Issuer,
                SigningCertificate = new CertificateIdentifier { Thumbprint = m_signingCertificate.Thumbprint }
            };
            options.AllowedAudiences.Add(Audience);
            options.DefaultScopes.Add("read");

            var issuer = new EcdsaJwtIssuer(options, m_certificateProvider, NUnitTelemetryContext.Create());
            var provider = new InMemoryAccessTokenProvider(issuer, options);
            var manager = new AuthorizationServiceManager(provider, issuer, options);

            string databaseStorePath = Path.Combine(m_pkiRoot, "gds", "gdsdb.json");
            var gdsConfig = new GlobalDiscoveryServerConfiguration
            {
                DatabaseStorePath = databaseStorePath
            };

            m_serverFixture = new ServerFixture<ReferenceServer>(
                telemetry =>
                {
                    var server = new ReferenceServer(telemetry);
                    server.AddNodeManager(new AuthorizationServiceNodeManagerFactory(gdsConfig, manager));
                    return server;
                })
            {
                AutoAccept = true,
                SecurityNone = true,
                OperationLimits = true
            };

            await m_serverFixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            m_serverFixture.Config.TransportQuotas.MaxMessageSize = 4 * 1024 * 1024;
            m_serverFixture.Config.TransportQuotas.MaxByteStringLength =
                m_serverFixture.Config.TransportQuotas.MaxStringLength = 1 * 1024 * 1024;
            m_serverFixture.Config.ServerConfiguration.AuditingEnabled = true;
            m_serverFixture.Config.ServerConfiguration.UserTokenPolicies =
                new UserTokenPolicy[] {
                    new(UserTokenType.Anonymous),
                    new(UserTokenType.UserName)
                }.ToArrayOf();

            await m_serverFixture.StartAsync().ConfigureAwait(false);
            m_serverUrl = new Uri(
                Utils.UriSchemeOpcTcp + "://localhost:" +
                m_serverFixture.Port.ToString(CultureInfo.InvariantCulture));

            m_clientFixture = new ClientFixture(telemetry: NUnitTelemetryContext.Create());
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            m_clientFixture.Config.TransportQuotas.MaxMessageSize = 4 * 1024 * 1024;
            m_clientFixture.Config.TransportQuotas.MaxByteStringLength =
                m_clientFixture.Config.TransportQuotas.MaxStringLength = 1 * 1024 * 1024;

            m_session = await m_clientFixture
                .ConnectAsync(m_serverUrl, SecurityPolicies.Basic256Sha256,
                    default, new UserIdentity("sysadmin", "demo"u8))
                .ConfigureAwait(false);

            if (!m_session.Factory.ContainsEncodeableType(DataTypeIds.ApplicationRecordDataType))
            {
                m_session.Factory.Builder.AddOpcUaGds().Commit();
            }
            if (!m_session.MessageContext.Factory.ContainsEncodeableType(DataTypeIds.ApplicationRecordDataType))
            {
                m_session.MessageContext.Factory.Builder.AddOpcUaGds().Commit();
            }
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDown()
        {
            if (m_session != null)
            {
                await m_session.CloseAsync(5000, true).ConfigureAwait(false);
                m_session.Dispose();
            }
            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }
            m_clientFixture?.Dispose();
            m_certificateProvider?.Dispose();
            m_signingCertificate?.Dispose();

            if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, true);
            }
        }

        [Test]
        public async Task RefreshTokenRoundTripThroughAuthorizationServiceClient()
        {
            AuthorizationServiceClient client = await CreateClientAsync().ConfigureAwait(false);
            (string accessToken, _, string refreshToken) = await StartAndFinishAsync(client).ConfigureAwait(false);

            (string refreshedAccessToken, _, string newRefreshToken, _) = await client
                .RefreshTokenAsync(Audience, refreshToken)
                .ConfigureAwait(false);

            Assert.That(accessToken, Is.Not.EqualTo(refreshedAccessToken));
            Assert.That(newRefreshToken, Is.Not.Null.And.Not.Empty);
            Assert.That(newRefreshToken, Is.Not.EqualTo(refreshToken));

            ECDsa verifier = m_signingCertificate.GetECDsaPublicKey();
            using var resolver = new StaticIssuerKeyResolver(
                Issuer,
                new[] { new IssuerVerificationKey(m_signingCertificate.Thumbprint, verifier, "ES256") });
            AuthenticationResult result = await new JwtAuthenticator(resolver, Audience, TimeSpan.Zero)
                .AuthenticateAsync(CreateContext(refreshedAccessToken))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
        }

        [Test]
        public async Task RefreshTokenAuditEventIsEmittedOnSuccessWithoutSecret()
        {
            AuthorizationServiceClient client = await CreateClientAsync().ConfigureAwait(false);
            (_, _, string refreshToken) = await StartAndFinishAsync(client).ConfigureAwait(false);

            await client.RefreshTokenAsync(Audience, refreshToken).ConfigureAwait(false);

            CapturingAuditEventServer auditServer = ReportRefreshAuditEvent();
            AuditUpdateMethodEventState auditEvent = auditServer.Events.Single();
            string payload = FlattenAuditEvent(auditEvent);
            Assert.That(payload, Does.Contain(Audience));
            Assert.That(payload, Does.Not.Contain(refreshToken));
            Assert.That(auditEvent.Status!.Value, Is.True);
        }

        [Test]
        public async Task RefreshTokenAuditEventIsEmittedOnFailure()
        {
            AuthorizationServiceClient client = await CreateClientAsync().ConfigureAwait(false);
            (_, _, string refreshToken) = await StartAndFinishAsync(client).ConfigureAwait(false);
            await client.RefreshTokenAsync(Audience, refreshToken).ConfigureAwait(false);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.RefreshTokenAsync(Audience, refreshToken).ConfigureAwait(false));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));

            CapturingAuditEventServer auditServer = ReportRefreshAuditEvent(ex);
            AuditUpdateMethodEventState auditEvent = auditServer.Events.Single();
            string payload = FlattenAuditEvent(auditEvent);
            Assert.That(payload, Does.Contain(Audience));
            Assert.That(payload, Does.Contain("already been used"));
            Assert.That(payload, Does.Not.Contain(refreshToken));
            Assert.That(auditEvent.Status!.Value, Is.False);
        }

        private async Task<AuthorizationServiceClient> CreateClientAsync()
        {
            NodeId folderId = ExpandedNodeId.ToNodeId(ObjectIds.AuthorizationServices, m_session.NamespaceUris);
            ReferenceDescription[] children = await BrowseChildrenAsync(folderId).ConfigureAwait(false);
            NodeId serviceTypeId = ExpandedNodeId.ToNodeId(
                ObjectTypeIds.AuthorizationServiceType,
                m_session.NamespaceUris);
            ReferenceDescription service = children.FirstOrDefault(reference =>
                    reference.NodeClass == NodeClass.Object &&
                    ExpandedNodeId.ToNodeId(reference.TypeDefinition, m_session.NamespaceUris) == serviceTypeId) ??
                children.FirstOrDefault(reference => reference.NodeClass == NodeClass.Object);
            if (service != null)
            {
                return new AuthorizationServiceClient(
                    m_session,
                    ExpandedNodeId.ToNodeId(service.NodeId, m_session.NamespaceUris));
            }

            return new AuthorizationServiceClient(
                m_session,
                new NodeId(
                    "AuthorizationServices/Default",
                    (ushort)m_session.NamespaceUris.GetIndex(Namespaces.OpcUaGds)));
        }

        private async Task<(string accessToken, DateTime accessTokenExpiryTime, string refreshToken)> StartAndFinishAsync(
            AuthorizationServiceClient client)
        {
            (_, Guid requestId) = await client
                .StartRequestTokenAsync(
                    Audience,
                    "jwt",
                    ByteString.From(Encoding.UTF8.GetBytes("read")))
                .ConfigureAwait(false);

            (string accessToken,
                DateTime accessTokenExpiryTime,
                string refreshToken,
                _) = await client
                .FinishRequestTokenAsync(
                    requestId,
                    s_authenticatedUserRole.ToArrayOf(),
                    new UserNameIdentityToken { UserName = "sysadmin" },
                    new SignatureData())
                .ConfigureAwait(false);

            Assert.That(refreshToken, Is.Not.Null.And.Not.Empty);
            return (accessToken, accessTokenExpiryTime, refreshToken);
        }

        private async Task<ReferenceDescription[]> BrowseChildrenAsync(NodeId nodeId)
        {
            BrowseResponse response = await m_session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[] {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            return [.. response.Results[0].References];
        }

        private static string FlattenVariant(Variant variant)
        {
            object value = variant.AsBoxedObject(Variant.BoxingBehavior.None);
            switch (value)
            {
                case null:
                    return string.Empty;
                case ArrayOf<Variant> values:
                    return string.Join(" ", values.ToArray().Select(FlattenVariant));
                case Variant[] values:
                    return string.Join(" ", values.Select(FlattenVariant));
                case LocalizedText text:
                    return text.Text ?? string.Empty;
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        private static CapturingAuditEventServer ReportRefreshAuditEvent(Exception exception = null)
        {
            var auditServer = new CapturingAuditEventServer();
            ILogger logger = NUnitTelemetryContext.Create().CreateLogger<RefreshTokenTests>();
            auditServer.ReportAccessTokenIssuedAuditEvent(
                auditServer.DefaultAuditContext,
                Opc.Ua.ObjectIds.Server,
                new MethodState(null),
                [Audience],
                logger,
                exception);
            return auditServer;
        }

        private static string FlattenAuditEvent(AuditUpdateMethodEventState auditEvent)
        {
            string inputArguments = auditEvent.InputArguments?.Value == null
                ? string.Empty
                : string.Join(" ", auditEvent.InputArguments.Value.ToArray().Select(FlattenVariant));
            string message = auditEvent.Message?.Value.Text ?? string.Empty;
            return inputArguments + " " + message;
        }

        private static AuthenticationContext CreateContext(string jwt)
        {
            return new AuthenticationContext(
                new IssuedIdentityTokenHandler(Profiles.JwtUserToken, Encoding.UTF8.GetBytes(jwt)),
                new UserTokenPolicy
                {
                    TokenType = UserTokenType.IssuedToken,
                    PolicyId = "jwt",
                    IssuedTokenType = Profiles.JwtUserToken
                },
                new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
        }

        private sealed class CapturingAuditEventServer : IAuditEventServer
        {
            public CapturingAuditEventServer()
            {
                var context = new SystemContext(NUnitTelemetryContext.Create())
                {
                    NamespaceUris = new NamespaceTable()
                };
                context.NamespaceUris.GetIndexOrAppend(Namespaces.OpcUaGds);
                DefaultAuditContext = context;
            }

            public bool Auditing => true;

            public ISystemContext DefaultAuditContext { get; }

            public List<AuditUpdateMethodEventState> Events { get; } = [];

            public void ReportAuditEvent(ISystemContext context, AuditEventState e)
            {
                if (e is AuditUpdateMethodEventState auditEvent)
                {
                    Events.Add(auditEvent);
                }
            }
        }

        private sealed class AuthorizationServiceNodeManagerFactory : INodeManagerFactory
        {
            private readonly GlobalDiscoveryServerConfiguration m_gdsConfiguration;
            private readonly Opc.Ua.Gds.Server.IAccessTokenProvider m_accessTokenProvider;

            public AuthorizationServiceNodeManagerFactory(
                GlobalDiscoveryServerConfiguration gdsConfiguration,
                Opc.Ua.Gds.Server.IAccessTokenProvider accessTokenProvider)
            {
                m_gdsConfiguration = gdsConfiguration;
                m_accessTokenProvider = accessTokenProvider;
            }

            public ArrayOf<string> NamespacesUris =>
            [
                "http://opcfoundation.org/UA/GDS/applications/",
                Namespaces.OpcUaGds
            ];

            public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
            {
                if (!server.MessageContext.Factory.ContainsEncodeableType(DataTypeIds.ApplicationRecordDataType))
                {
                    server.MessageContext.Factory.Builder.AddOpcUaGds().Commit();
                }

                string databaseStorePath = Utils.ReplaceSpecialFolderNames(m_gdsConfiguration.DatabaseStorePath);
                string databaseDir = Path.GetDirectoryName(databaseStorePath);
                if (!string.IsNullOrEmpty(databaseDir))
                {
                    Directory.CreateDirectory(databaseDir);
                }

                LinqApplicationsDatabase database = string.IsNullOrEmpty(databaseDir)
                    ? new LinqApplicationsDatabase()
                    : JsonApplicationsDatabase.Load(databaseStorePath);

                var applications = new ApplicationsNodeManager(
                    server,
                    configuration,
                    database,
                    database,
                    new CertificateGroup(server.Telemetry),
                    autoApprove: true)
                {
                    AccessTokenProvider = m_accessTokenProvider
                };

                return applications;
            }
        }
    }
}
