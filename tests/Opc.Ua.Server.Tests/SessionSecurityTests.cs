/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Session")]
    [Category("Security")]
    [NonParallelizable]
    public sealed class SessionSecurityTests
    {
        private const string AnonymousPolicyId = "anonymous";
        private const string UserNamePolicyId = "username";
        private static readonly ICertificateFactory s_certificateFactory =
            DefaultCertificateFactory.Instance;

        private ITelemetryContext m_telemetry = null!;
        private Mock<IServerInternal> m_server = null!;
        private Certificate m_serverCertificate = null!;
        private Certificate m_clientCertificate = null!;
        private Certificate m_otherClientCertificate = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_serverCertificate = CreateCertificate("CN=SessionSecurityServer");
            m_clientCertificate = CreateCertificate("CN=SessionSecurityClient");
            m_otherClientCertificate = CreateCertificate("CN=OtherSessionSecurityClient");

            m_server = new Mock<IServerInternal>();
            m_server.Setup(s => s.Telemetry).Returns(m_telemetry);
            m_server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            m_server.Setup(s => s.SubscriptionStore).Returns((ISubscriptionStore)null!);
            m_server.Setup(s => s.MessageContext).Returns(
                ServiceMessageContext.CreateEmpty(m_telemetry));
            var serverDiagnostics = new ServerDiagnosticsSummaryDataType();
            m_server.Setup(s => s.DiagnosticsWriteLock).Returns(serverDiagnostics);
            m_server.Setup(s => s.ServerDiagnostics).Returns(serverDiagnostics);

            var diagnostics = new Mock<IDiagnosticsNodeManager>();
            diagnostics
                .Setup(d => d.CreateSessionDiagnosticsAsync(
                    It.IsAny<ServerSystemContext>(),
                    It.IsAny<SessionDiagnosticsDataType>(),
                    It.IsAny<NodeValueSimpleEventHandler>(),
                    It.IsAny<SessionSecurityDiagnosticsDataType>(),
                    It.IsAny<NodeValueSimpleEventHandler>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeId>(new NodeId(1001, 1)));
            diagnostics
                .Setup(d => d.DeleteSessionDiagnosticsAsync(
                    It.IsAny<ServerSystemContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            m_server.Setup(s => s.DiagnosticsNodeManager).Returns(diagnostics.Object);
            m_server.Setup(s => s.DefaultSystemContext).Returns(
                new ServerSystemContext(m_server.Object));
        }

        [TearDown]
        public void TearDown()
        {
            m_otherClientCertificate.Dispose();
            m_clientCertificate.Dispose();
            m_serverCertificate.Dispose();
        }

        [Test]
        public async Task NewChannelWithSameUsernameIgnoresDisplayAliasChangesAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(
                MessageSecurityMode.SignAndEncrypt,
                includeUserName: true);
            int authenticationCount = 0;
            using SecuritySessionManager manager = CreateManager(
                _ => Interlocked.Increment(ref authenticationCount) == 1
                    ? "First display alias"
                    : "Second display alias");
            CreatedSession created = await CreateAndActivateAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate,
                CreateUserNameToken("alice")).ConfigureAwait(false);

            OperationContext newContext = CreateContext(
                endpoint,
                "channel-2",
                m_clientCertificate);
            SignatureData signature = CreateClientSignature(
                newContext,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);

            (bool _, ByteString newNonce, _) = await manager.ActivateSessionAsync(
                newContext,
                created.Result.AuthenticationToken,
                signature,
                CreateUserNameToken("alice"),
                null,
                [],
                default).ConfigureAwait(false);

            Assert.That(newNonce.Length, Is.InRange(32, 128));
            Assert.That(created.Result.Session.SecureChannelId, Is.EqualTo("channel-2"));
            AssertRequestRejectedOnOldChannel(created, newContext);
        }

        [Test]
        public async Task NewChannelWithDifferentCertificateIsRejectedAndOldChannelRemainsValidAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(MessageSecurityMode.SignAndEncrypt);
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateAndActivateAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate,
                default).ConfigureAwait(false);
            OperationContext newContext = CreateContext(
                endpoint,
                "channel-2",
                m_otherClientCertificate);
            SignatureData signature = CreateClientSignature(
                newContext,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.ActivateSessionAsync(
                    newContext,
                    created.Result.AuthenticationToken,
                    signature,
                    default,
                    null,
                    [],
                    default).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            AssertRequestAcceptedOnOriginalChannel(created);
        }

        [Test]
        public async Task NewChannelWithDifferentUsernameIsRejectedWhenDisplayAliasMatchesAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(
                MessageSecurityMode.SignAndEncrypt,
                includeUserName: true);
            using SecuritySessionManager manager = CreateManager(
                _ => "Shared display alias");
            CreatedSession created = await CreateAndActivateAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate,
                CreateUserNameToken("alice")).ConfigureAwait(false);
            OperationContext newContext = CreateContext(
                endpoint,
                "channel-2",
                m_clientCertificate);
            SignatureData signature = CreateClientSignature(
                newContext,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.ActivateSessionAsync(
                    newContext,
                    created.Result.AuthenticationToken,
                    signature,
                    CreateUserNameToken("alice-alias"),
                    null,
                    [],
                    default).ConfigureAwait(false))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadIdentityChangeNotSupported));
            AssertRequestAcceptedOnOriginalChannel(created);
        }

        [Test]
        public void ClientUserIdUsesExactTokenSemantics()
        {
            var anonymousToken = new AnonymousIdentityTokenHandler();
            Assert.That(
                ClientUserIdResolver.Resolve(anonymousToken, new UserIdentity()),
                Is.Null);

            var userNameToken = new UserNameIdentityTokenHandler("ExactUser", [1, 2, 3]);
            var aliasedUserName = new UserIdentity(userNameToken)
            {
                DisplayName = "Display Alias"
            };
            Assert.That(
                ClientUserIdResolver.Resolve(userNameToken, aliasedUserName),
                Is.EqualTo("ExactUser"));

            var x509Token = new X509IdentityTokenHandler(new X509IdentityToken
            {
                CertificateData = m_clientCertificate.RawData.ToByteString()
            });
            var aliasedCertificate = new UserIdentity(x509Token)
            {
                DisplayName = "Certificate Alias"
            };
            Assert.That(
                ClientUserIdResolver.Resolve(x509Token, aliasedCertificate),
                Is.EqualTo(m_clientCertificate.Subject));

            var issuedToken = new IssuedIdentityTokenHandler(new IssuedIdentityToken
            {
                PolicyId = Profiles.JwtUserToken
            });
            var jwtIdentity = new JwtUserIdentity(
                issuedToken,
                new Dictionary<string, object?>(),
                [],
                [],
                "https://issuer.example/",
                "subject-42");
            var roleBasedJwtIdentity = new RoleBasedIdentity(
                jwtIdentity,
                [Role.AuthenticatedUser],
                new NamespaceTable());
            Assert.That(
                ClientUserIdResolver.Resolve(issuedToken, roleBasedJwtIdentity),
                Is.EqualTo("https://issuer.example/subject-42"));

            var jwtWithoutIssuer = new JwtUserIdentity(
                issuedToken,
                new Dictionary<string, object?>(),
                [],
                [],
                null,
                "subject-only");
            Assert.That(
                ClientUserIdResolver.Resolve(issuedToken, jwtWithoutIssuer),
                Is.EqualTo("subject-only"));

            var jwtToken = new IssuedIdentityTokenHandler(
                Profiles.JwtUserToken,
                CreateJwt("https://issuer.example/", "parsed-subject"));
            var authenticatedJwt = new UserIdentity(jwtToken)
            {
                DisplayName = "Display Alias"
            };
            Assert.That(
                ClientUserIdResolver.Resolve(jwtToken, authenticatedJwt),
                Is.EqualTo("https://issuer.example/parsed-subject"));
        }

        [Test]
        public void ClientUserIdRejectsIssuedIdentityWithoutStableOwner()
        {
            var issuedToken = new IssuedIdentityTokenHandler(
                Profiles.JwtUserToken,
                "opaque-token"u8);
            var aliasedIdentity = new UserIdentity(issuedToken)
            {
                DisplayName = "Shared display alias"
            };

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => ClientUserIdResolver.Resolve(issuedToken, aliasedIdentity))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task ActivateSessionRejectsAnonymousWhenEndpointOmitsAnonymousPolicyAsync()
        {
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840/SessionSecurity",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                UserIdentityTokens = new[]
                {
                    new UserTokenPolicy
                    {
                        PolicyId = UserNamePolicyId,
                        TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                }.ToArrayOf()
            };
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateSessionAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate).ConfigureAwait(false);
            SignatureData signature = CreateClientSignature(
                created.Context,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.ActivateSessionAsync(
                    created.Context,
                    created.Result.AuthenticationToken,
                    signature,
                    default,
                    null,
                    [],
                    default).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task ActivationValidationRejectsMissingSecureChannelContextAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(MessageSecurityMode.SignAndEncrypt);
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateSessionAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate).ConfigureAwait(false);
            var contextWithoutChannel = new OperationContext(
                new RequestHeader(),
                null,
                RequestType.ActivateSession,
                RequestLifetime.None);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await created.Result.Session.ValidateBeforeActivateAsync(
                    contextWithoutChannel,
                    new SignatureData(),
                    default,
                    null!,
                    default).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelIdInvalid));
        }

        [Test]
        public async Task SynchronousActivationValidationRequiresAsyncPathForSecureEndpointsAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(
                MessageSecurityMode.SignAndEncrypt,
                includeUserName: true);
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateSessionAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate).ConfigureAwait(false);
            SignatureData signature = CreateClientSignature(
                created.Context,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);
            var session = (Opc.Ua.Server.Session)created.Result.Session;

            // The retained synchronous contract cannot verify a user token that
            // requires decryption, so it fails closed and directs callers to
            // ValidateBeforeActivateAsync instead of validating with less rigour.
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => session.ValidateBeforeActivate(
                    created.Context,
                    signature,
                    CreateUserNameToken("alice"),
                    null!,
                    out _,
                    out _))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task ActivationValidationDecodesBinaryEncodedUserIdentityTokenAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(
                MessageSecurityMode.None,
                includeUserName: true,
                securityPolicyUri: SecurityPolicies.None);
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateSessionAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate).ConfigureAwait(false);
            SignatureData signature = CreateClientSignature(
                created.Context,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);

            // Clients may send the UserIdentityToken as a raw binary body; the
            // Server has to decode it against the matching UserTokenPolicy.
            ExtensionObject binaryToken = EncodeAsBinaryBody(new UserNameIdentityToken
            {
                PolicyId = UserNamePolicyId,
                UserName = "alice",
                Password = ByteString.From([1, 2, 3])
            });

            (IUserIdentityTokenHandler identityToken, UserTokenPolicy? policy) =
                await created.Result.Session.ValidateBeforeActivateAsync(
                    created.Context,
                    signature,
                    binaryToken,
                    null!,
                    default).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(identityToken, Is.TypeOf<UserNameIdentityTokenHandler>());
                Assert.That(
                    ((UserNameIdentityToken)identityToken.Token).UserName,
                    Is.EqualTo("alice"));
                Assert.That(policy!.PolicyId, Is.EqualTo(UserNamePolicyId));
            });
        }

        [Test]
        public async Task ActivationValidationRejectsBinaryTokenWithUnknownPolicyAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(
                MessageSecurityMode.None,
                includeUserName: true,
                securityPolicyUri: SecurityPolicies.None);
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateSessionAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate).ConfigureAwait(false);
            SignatureData signature = CreateClientSignature(
                created.Context,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);
            ExtensionObject binaryToken = EncodeAsBinaryBody(new UserNameIdentityToken
            {
                PolicyId = "policy-that-does-not-exist",
                UserName = "alice",
                Password = ByteString.From([1, 2, 3])
            });

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await created.Result.Session.ValidateBeforeActivateAsync(
                    created.Context,
                    signature,
                    binaryToken,
                    null!,
                    default).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task ActivateSessionWrapsUnexpectedAuthenticationFailuresAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(
                MessageSecurityMode.SignAndEncrypt,
                includeUserName: true);
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateSessionAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate).ConfigureAwait(false);
            SignatureData signature = CreateClientSignature(
                created.Context,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);
            manager.FailNextAuthentication(
                new InvalidOperationException("identity store unavailable"));

            // A non-ServiceResultException from an identity provider must surface
            // as Bad_IdentityTokenInvalid rather than escaping as an internal fault.
            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.ActivateSessionAsync(
                    created.Context,
                    created.Result.AuthenticationToken,
                    signature,
                    CreateUserNameToken("alice"),
                    null,
                    [],
                    default).ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(
                    exception.StatusCode,
                    Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
                Assert.That(
                    exception.InnerException,
                    Is.TypeOf<InvalidOperationException>());
            });
        }

        [Test]
        public async Task ActivationSequenceIncreasesWithEveryActivationAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(MessageSecurityMode.SignAndEncrypt);
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateAndActivateAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate,
                default).ConfigureAwait(false);
            long firstSequence = manager.ObservedActivationSequence;

            SignatureData signature = CreateClientSignature(
                created.Context,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);
            await manager.ActivateSessionAsync(
                created.Context,
                created.Result.AuthenticationToken,
                signature,
                default,
                null,
                [],
                default).ConfigureAwait(false);

            // Listeners persist activation state after the gate is released, so
            // they need a monotonic stamp to discard superseded writes.
            Assert.Multiple(() =>
            {
                Assert.That(firstSequence, Is.GreaterThan(0));
                Assert.That(
                    manager.ObservedActivationSequence,
                    Is.GreaterThan(firstSequence));
            });
        }

        [Test]
        public async Task RestoredTransferSecurityStateRejectsInconsistentArgumentsAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(MessageSecurityMode.SignAndEncrypt);
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateSessionAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate).ConfigureAwait(false);
            ISession session = created.Result.Session;
            ByteString channelCertificate = ByteString.From(m_clientCertificate.RawData);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => manager.ApplyRestoredTransferSecurityState(
                        null!,
                        channelCertificate,
                        SecurityPolicies.Basic256Sha256,
                        MessageSecurityMode.SignAndEncrypt,
                        UserTokenType.Anonymous,
                        null),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => manager.ApplyRestoredTransferSecurityState(
                        session,
                        channelCertificate,
                        string.Empty,
                        MessageSecurityMode.SignAndEncrypt,
                        UserTokenType.Anonymous,
                        null),
                    Throws.TypeOf<ArgumentException>());
                Assert.That(
                    () => manager.ApplyRestoredTransferSecurityState(
                        session,
                        channelCertificate,
                        SecurityPolicies.Basic256Sha256,
                        MessageSecurityMode.SignAndEncrypt,
                        (UserTokenType)42,
                        "user1"),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => manager.ApplyRestoredTransferSecurityState(
                        session,
                        channelCertificate,
                        SecurityPolicies.Basic256Sha256,
                        MessageSecurityMode.SignAndEncrypt,
                        UserTokenType.Anonymous,
                        "user1"),
                    Throws.TypeOf<ArgumentException>());
                Assert.That(
                    () => manager.ApplyRestoredTransferSecurityState(
                        session,
                        channelCertificate,
                        SecurityPolicies.Basic256Sha256,
                        MessageSecurityMode.SignAndEncrypt,
                        UserTokenType.UserName,
                        null),
                    Throws.TypeOf<ArgumentException>());
            });
        }

        [Test]
        public async Task NewChannelWithChangedSecurityModeIsRejectedAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(MessageSecurityMode.SignAndEncrypt);
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateAndActivateAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate,
                default).ConfigureAwait(false);
            EndpointDescription changedEndpoint = CreateEndpoint(MessageSecurityMode.Sign);
            OperationContext newContext = CreateContext(
                changedEndpoint,
                "channel-2",
                m_clientCertificate);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.ActivateSessionAsync(
                    newContext,
                    created.Result.AuthenticationToken,
                    new SignatureData(),
                    default,
                    null,
                    [],
                    default).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
            AssertRequestAcceptedOnOriginalChannel(created);
        }

        [Test]
        public async Task NewChannelWithChangedSecurityPolicyIsRejectedAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(MessageSecurityMode.SignAndEncrypt);
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateAndActivateAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate,
                default).ConfigureAwait(false);
            EndpointDescription changedEndpoint = CreateEndpoint(
                MessageSecurityMode.SignAndEncrypt,
                securityPolicyUri: SecurityPolicies.Aes256_Sha256_RsaPss);
            OperationContext newContext = CreateContext(
                changedEndpoint,
                "channel-2",
                m_clientCertificate);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.ActivateSessionAsync(
                    newContext,
                    created.Result.AuthenticationToken,
                    new SignatureData(),
                    default,
                    null,
                    [],
                    default).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
            AssertRequestAcceptedOnOriginalChannel(created);
        }

        [Test]
        public async Task AnonymousSessionCannotMoveToSignOnlyChannelAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(MessageSecurityMode.Sign);
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateAndActivateAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate,
                default).ConfigureAwait(false);
            OperationContext newContext = CreateContext(
                endpoint,
                "channel-2",
                m_clientCertificate);
            SignatureData signature = CreateClientSignature(
                newContext,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.ActivateSessionAsync(
                    newContext,
                    created.Result.AuthenticationToken,
                    signature,
                    default,
                    null,
                    [],
                    default).ConfigureAwait(false))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadIdentityChangeNotSupported));
            AssertRequestAcceptedOnOriginalChannel(created);
        }

        [Test]
        public async Task SecuredActivationRequiresCreateSessionClientCertificateAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(MessageSecurityMode.SignAndEncrypt);
            using SecuritySessionManager manager = CreateManager();
            OperationContext context = CreateContext(
                endpoint,
                "channel-1",
                m_clientCertificate);
            ByteString clientNonce = ByteString.From(CreateBytes(32, 0x21));
            CreateSessionResult result = await manager.CreateSessionAsync(
                context,
                m_serverCertificate,
                "MissingClientCertificate",
                clientNonce,
                new ApplicationDescription
                {
                    ApplicationUri = "urn:test:missing-client-certificate",
                    ApplicationName = new LocalizedText("Missing Client Certificate"),
                    ApplicationType = ApplicationType.Client
                },
                endpoint.EndpointUrl,
                null,
                [],
                60_000,
                64 * 1024,
                default).ConfigureAwait(false);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.ActivateSessionAsync(
                    context,
                    result.AuthenticationToken,
                    new SignatureData(),
                    default,
                    null,
                    [],
                    default).ConfigureAwait(false))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadApplicationSignatureInvalid));
        }

        [Test]
        public async Task SecuredIdentityCanUsePublicAsyncSessionValidationAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(
                MessageSecurityMode.SignAndEncrypt,
                includeUserName: true);
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateSessionAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate).ConfigureAwait(false);
            SignatureData signature = CreateClientSignature(
                created.Context,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);
            ISession session = created.Result.Session;

            (IUserIdentityTokenHandler identityToken, UserTokenPolicy? policy) =
                await session.ValidateBeforeActivateAsync(
                    created.Context,
                    signature,
                    CreateUserNameToken("alice"),
                    null!,
                    default).ConfigureAwait(false);

            Assert.That(identityToken, Is.TypeOf<UserNameIdentityTokenHandler>());
            Assert.That(policy!.PolicyId, Is.EqualTo(UserNamePolicyId));
        }

        [Test]
        public async Task FailedFirstActivationDoesNotPoisonCreateSessionSecurityStateAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(MessageSecurityMode.SignAndEncrypt);
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateSessionAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate).ConfigureAwait(false);
            OperationContext wrongContext = CreateContext(
                endpoint,
                "channel-2",
                m_otherClientCertificate);
            SignatureData wrongSignature = CreateClientSignature(
                wrongContext,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);

            ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.ActivateSessionAsync(
                    wrongContext,
                    created.Result.AuthenticationToken,
                    wrongSignature,
                    default,
                    null,
                    [],
                    default).ConfigureAwait(false))!;
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelIdInvalid));

            SignatureData originalSignature = CreateClientSignature(
                created.Context,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);
            (_, ByteString serverNonce, _) = await manager.ActivateSessionAsync(
                created.Context,
                created.Result.AuthenticationToken,
                originalSignature,
                default,
                null,
                [],
                default).ConfigureAwait(false);

            Assert.That(serverNonce.Length, Is.InRange(32, 128));
        }

        [Test]
        public async Task AuthenticationCancellationIsPropagatedAndDoesNotLockOutClientAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(MessageSecurityMode.SignAndEncrypt);
            using SecuritySessionManager manager = CreateManager(
                maxFailedAuthenticationAttempts: 1);
            CreatedSession created = await CreateSessionAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate).ConfigureAwait(false);
            SignatureData signature = CreateClientSignature(
                created.Context,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);
            var cancellation = new OperationCanceledException("authentication cancelled");
            manager.CancelNextAuthentication(cancellation);

            OperationCanceledException propagated = Assert.ThrowsAsync<OperationCanceledException>(
                async () => await manager.ActivateSessionAsync(
                    created.Context,
                    created.Result.AuthenticationToken,
                    signature,
                    default,
                    null,
                    [],
                    default).ConfigureAwait(false))!;
            Assert.That(propagated, Is.SameAs(cancellation));

            (_, ByteString serverNonce, _) = await manager.ActivateSessionAsync(
                created.Context,
                created.Result.AuthenticationToken,
                signature,
                default,
                null,
                [],
                default).ConfigureAwait(false);
            Assert.That(serverNonce.Length, Is.InRange(32, 128));
        }

        [Test]
        public async Task CloseSessionCancellationDuringActivationLeavesSessionAvailableAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(MessageSecurityMode.SignAndEncrypt);
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateAndActivateAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate,
                default).ConfigureAwait(false);
            OperationContext newContext = CreateContext(
                endpoint,
                "channel-2",
                m_clientCertificate);
            SignatureData signature = CreateClientSignature(
                newContext,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);
            var authenticationEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseAuthentication = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            manager.PauseNextAuthentication(authenticationEntered, releaseAuthentication);
            Task activation = manager.ActivateSessionAsync(
                newContext,
                created.Result.AuthenticationToken,
                signature,
                default,
                null,
                [],
                default).AsTask();
            await authenticationEntered.Task.ConfigureAwait(false);

            using var cancellation = new CancellationTokenSource();
            Task closeOperation = manager.CloseSessionAsync(
                created.Result.SessionId,
                cancellation.Token).AsTask();
            try
            {
                Assert.That(closeOperation.IsCompleted, Is.False);
                cancellation.Cancel();
                Task completed = await Task.WhenAny(
                    closeOperation,
                    Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                Assert.That(completed, Is.SameAs(closeOperation));
                Assert.CatchAsync<OperationCanceledException>(
                    async () => await closeOperation.ConfigureAwait(false));
                Assert.That(
                    manager.GetSession(created.Result.AuthenticationToken),
                    Is.SameAs(created.Result.Session));
            }
            finally
            {
                releaseAuthentication.TrySetResult(true);
            }

            await activation.ConfigureAwait(false);
            await manager.CloseSessionAsync(created.Result.SessionId).ConfigureAwait(false);
            Assert.That(manager.GetSession(created.Result.AuthenticationToken), Is.Null);
        }

        [Test]
        public async Task SessionActivatedCallbackRunsAfterActivationGateIsReleasedAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(MessageSecurityMode.SignAndEncrypt);
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateSessionAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate).ConfigureAwait(false);
            SignatureData signature = CreateClientSignature(
                created.Context,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);
            ValueTask closeOperation = default;
            bool completedInsideCallback = false;
            manager.SessionActivated += (session, _) =>
            {
                closeOperation = manager.CloseSessionAsync(session.Id);
                completedInsideCallback = closeOperation.IsCompleted;
            };

            await manager.ActivateSessionAsync(
                created.Context,
                created.Result.AuthenticationToken,
                signature,
                default,
                null,
                [],
                default).ConfigureAwait(false);
            await closeOperation.ConfigureAwait(false);

            Assert.That(completedInsideCallback, Is.True);
            Assert.That(manager.GetSession(created.Result.AuthenticationToken), Is.Null);
        }

        [Test]
        public async Task ActivationCommittedHookRunsAfterActivationGateIsReleasedAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(MessageSecurityMode.SignAndEncrypt);
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateSessionAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate).ConfigureAwait(false);
            SignatureData signature = CreateClientSignature(
                created.Context,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);
            manager.ProbeGateOnNextActivation();

            await manager.ActivateSessionAsync(
                created.Context,
                created.Result.AuthenticationToken,
                signature,
                default,
                null,
                [],
                default).ConfigureAwait(false);
            await manager.PendingCallbackOperation.ConfigureAwait(false);

            Assert.That(manager.CallbackObservedReleasedGate, Is.True);
            Assert.That(manager.GetSession(created.Result.AuthenticationToken), Is.Null);
        }

        [Test]
        public async Task ConcurrentActivationConsumesServerNonceOnceAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(MessageSecurityMode.SignAndEncrypt);
            using SecuritySessionManager manager = CreateManager();
            CreatedSession created = await CreateAndActivateAsync(
                manager,
                endpoint,
                "channel-1",
                m_clientCertificate,
                default).ConfigureAwait(false);
            OperationContext newContext = CreateContext(
                endpoint,
                "channel-2",
                m_clientCertificate);
            SignatureData signature = CreateClientSignature(
                newContext,
                created.ClientNonce,
                created.ServerNonce,
                m_clientCertificate);
            var authenticationEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseAuthentication = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            manager.PauseNextAuthentication(authenticationEntered, releaseAuthentication);

            Task<Exception?> first = CaptureAsync(manager.ActivateSessionAsync(
                newContext,
                created.Result.AuthenticationToken,
                signature,
                default,
                null,
                [],
                default));
            await authenticationEntered.Task.ConfigureAwait(false);
            Task<Exception?> second = CaptureAsync(manager.ActivateSessionAsync(
                newContext,
                created.Result.AuthenticationToken,
                signature,
                default,
                null,
                [],
                default));
            releaseAuthentication.SetResult(true);

            Exception?[] exceptions = await Task.WhenAll(first, second).ConfigureAwait(false);

            Assert.That(exceptions.Count(e => e == null), Is.EqualTo(1));
            ServiceResultException rejected = exceptions
                .OfType<ServiceResultException>()
                .Single();
            Assert.That(
                rejected.StatusCode,
                Is.EqualTo(StatusCodes.BadApplicationSignatureInvalid));
        }

        private SecuritySessionManager CreateManager(
            Func<IUserIdentityTokenHandler, string>? canonicalize = null,
            int maxFailedAuthenticationAttempts = 0)
        {
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MinSessionTimeout = 1000,
                    MaxSessionTimeout = 60_000,
                    MaxSessionCount = 20,
                    MaxFailedAuthenticationAttempts = maxFailedAuthenticationAttempts,
                    MaxRequestAge = 60_000,
                    MaxBrowseContinuationPoints = 10,
                    MaxHistoryContinuationPoints = 10
                }
            };
            var manager = new SecuritySessionManager(
                m_server.Object,
                configuration,
                canonicalize);
            m_server.Setup(s => s.SessionManager).Returns(manager);
            return manager;
        }

        private async Task<CreatedSession> CreateAndActivateAsync(
            SecuritySessionManager manager,
            EndpointDescription endpoint,
            string channelId,
            Certificate clientCertificate,
            ExtensionObject userIdentityToken)
        {
            CreatedSession created = await CreateSessionAsync(
                manager,
                endpoint,
                channelId,
                clientCertificate).ConfigureAwait(false);
            SignatureData signature = CreateClientSignature(
                created.Context,
                created.ClientNonce,
                created.ServerNonce,
                clientCertificate);
            (_, ByteString serverNonce, _) = await manager.ActivateSessionAsync(
                created.Context,
                created.Result.AuthenticationToken,
                signature,
                userIdentityToken,
                null,
                [],
                default).ConfigureAwait(false);
            return created with
            {
                ServerNonce = serverNonce
            };
        }

        private async Task<CreatedSession> CreateSessionAsync(
            SecuritySessionManager manager,
            EndpointDescription endpoint,
            string channelId,
            Certificate clientCertificate)
        {
            OperationContext context = CreateContext(endpoint, channelId, clientCertificate);
            ByteString clientNonce = ByteString.From(CreateBytes(32, 0x11));
            CreateSessionResult result = await manager.CreateSessionAsync(
                context,
                m_serverCertificate,
                "SecuritySession",
                clientNonce,
                new ApplicationDescription
                {
                    ApplicationUri = "urn:test:session-security-client",
                    ApplicationName = new LocalizedText("Session Security Client"),
                    ApplicationType = ApplicationType.Client
                },
                endpoint.EndpointUrl,
                clientCertificate.AddRef(),
                [],
                60_000,
                64 * 1024,
                default).ConfigureAwait(false);
            return new CreatedSession(result, context, clientNonce, result.ServerNonce);
        }

        private SignatureData CreateClientSignature(
            OperationContext context,
            ByteString clientNonce,
            ByteString serverNonce,
            Certificate signingCertificate)
        {
            SecurityPolicyInfo securityPolicy = SecurityPolicyRegistry.Default.GetInfo(
                context.ChannelContext!.EndpointDescription!.SecurityPolicyUri!)!;
            byte[] dataToSign = securityPolicy.GetClientSignatureData(
                context.ChannelContext.ChannelThumbprint,
                serverNonce.ToArray(),
                m_serverCertificate.RawData,
                context.ChannelContext.ServerChannelCertificate,
                context.ChannelContext.ClientChannelCertificate,
                clientNonce.ToArray());
            return SecurityPolicyRegistry.Default.CreateSignatureData(
                securityPolicy,
                signingCertificate,
                dataToSign);
        }

        private OperationContext CreateContext(
            EndpointDescription endpoint,
            string channelId,
            Certificate clientCertificate)
        {
            var channelContext = new SecureChannelContext(
                channelId,
                endpoint,
                RequestEncoding.Binary,
                clientCertificate.RawData,
                m_serverCertificate.RawData,
                CreateBytes(32, 0x71));
            return new OperationContext(
                new RequestHeader(),
                channelContext,
                RequestType.ActivateSession,
                RequestLifetime.None);
        }

        private static EndpointDescription CreateEndpoint(
            MessageSecurityMode securityMode,
            bool includeUserName = false,
            string securityPolicyUri = SecurityPolicies.Basic256Sha256)
        {
            var policies = new[]
            {
                new UserTokenPolicy
                {
                    PolicyId = AnonymousPolicyId,
                    TokenType = UserTokenType.Anonymous,
                    SecurityPolicyUri = SecurityPolicies.None
                }
            }.ToList();
            if (includeUserName)
            {
                policies.Add(new UserTokenPolicy
                {
                    PolicyId = UserNamePolicyId,
                    TokenType = UserTokenType.UserName,
                    SecurityPolicyUri = SecurityPolicies.None
                });
            }
            return new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840/SessionSecurity",
                SecurityMode = securityMode,
                SecurityPolicyUri = securityPolicyUri,
                UserIdentityTokens = policies.ToArrayOf()
            };
        }

        private static ExtensionObject CreateUserNameToken(string userName)
        {
            return new ExtensionObject(new UserNameIdentityToken
            {
                PolicyId = UserNamePolicyId,
                UserName = userName,
                Password = ByteString.From([1, 2, 3])
            });
        }

        /// <summary>
        /// Wraps an identity token as an <see cref="ExtensionObject"/> that carries
        /// the binary encoded body instead of the decoded instance.
        /// </summary>
        private ExtensionObject EncodeAsBinaryBody(UserIdentityToken token)
        {
            using var encoder = new BinaryEncoder(m_server.Object.MessageContext);
            token.Encode(encoder);
            byte[]? body = encoder.CloseAndReturnBuffer();
            return new ExtensionObject(
                token.BinaryEncodingId,
                body is null ? ByteString.Empty : ByteString.From(body));
        }

        private static Certificate CreateCertificate(string subject)
        {
            return s_certificateFactory
                .CreateCertificate(subject)
                .SetRSAKeySize(CertificateFactory.DefaultKeySize)
                .CreateForRSA();
        }

        private static byte[] CreateBytes(int length, byte seed)
        {
            byte[] bytes = new byte[length];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)(seed + i);
            }
            return bytes;
        }

        private static byte[] CreateJwt(string issuer, string subject)
        {
            static string Base64UrlEncode(string value)
            {
                return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
            }

            string payload = $"{{\"iss\":\"{issuer}\",\"sub\":\"{subject}\"}}";
            return System.Text.Encoding.UTF8.GetBytes(
                $"{Base64UrlEncode("{}")}.{Base64UrlEncode(payload)}.signature");
        }

        private static void AssertRequestAcceptedOnOriginalChannel(CreatedSession created)
        {
            Assert.That(
                () => created.Result.Session.ValidateRequest(
                    new RequestHeader(),
                    created.Context.ChannelContext!,
                    RequestType.Read),
                Throws.Nothing);
        }

        private static void AssertRequestRejectedOnOldChannel(
            CreatedSession created,
            OperationContext newContext)
        {
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => created.Result.Session.ValidateRequest(
                    new RequestHeader(),
                    created.Context.ChannelContext!,
                    RequestType.Read))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelIdInvalid));
            Assert.That(
                () => created.Result.Session.ValidateRequest(
                    new RequestHeader(),
                    newContext.ChannelContext!,
                    RequestType.Read),
                Throws.Nothing);
        }

        private static async Task<Exception?> CaptureAsync(
            ValueTask<(
                bool IdentityContextChanged,
                ByteString ServerNonce,
                ServiceResult ActivationStatus)> activation)
        {
            try
            {
                await activation.ConfigureAwait(false);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private sealed record CreatedSession(
            CreateSessionResult Result,
            OperationContext Context,
            ByteString ClientNonce,
            ByteString ServerNonce);

        private sealed class SecuritySessionManager : SessionManager
        {
            public SecuritySessionManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                Func<IUserIdentityTokenHandler, string>? canonicalize)
                : base(server, configuration)
            {
                m_canonicalize = canonicalize ?? (token => token.DisplayName);
            }

            public void PauseNextAuthentication(
                TaskCompletionSource<bool> entered,
                TaskCompletionSource<bool> release)
            {
                m_authenticationEntered = entered;
                m_releaseAuthentication = release;
                Volatile.Write(ref m_pauseNextAuthentication, 1);
            }

            public void CancelNextAuthentication(OperationCanceledException exception)
            {
                m_cancellationException = exception ??
                    throw new ArgumentNullException(nameof(exception));
            }

            public void FailNextAuthentication(Exception exception)
            {
                m_authenticationFailure = exception ??
                    throw new ArgumentNullException(nameof(exception));
            }

            public bool CallbackObservedReleasedGate { get; private set; }

            public long ObservedActivationSequence { get; private set; }

            public Task PendingCallbackOperation { get; private set; } =
                Task.CompletedTask;

            public void ProbeGateOnNextActivation()
            {
                Volatile.Write(ref m_probeGateOnNextActivation, 1);
            }

            /// <summary>
            /// Exposes the protected restore hook so its argument validation can
            /// be exercised without a full distributed Session restore.
            /// </summary>
            public void ApplyRestoredTransferSecurityState(
                ISession session,
                ByteString originalClientChannelCertificate,
                string securityPolicyUri,
                MessageSecurityMode securityMode,
                UserTokenType clientUserTokenType,
                string? clientUserId)
            {
                SetRestoredSessionTransferSecurityState(
                    session,
                    originalClientChannelCertificate,
                    securityPolicyUri,
                    securityMode,
                    clientUserTokenType,
                    clientUserId);
            }

            protected override async ValueTask<(
                IUserIdentity? Identity,
                IUserIdentity? EffectiveIdentity,
                ServiceResult? Error)> AuthenticateUserIdentityAsync(
                    ISession session,
                    IUserIdentityTokenHandler newIdentity,
                    UserTokenPolicy? userTokenPolicy,
                    EndpointDescription endpointDescription,
                    CancellationToken cancellationToken)
            {
                OperationCanceledException? cancellation = Interlocked.Exchange(
                    ref m_cancellationException,
                    null);
                if (cancellation != null)
                {
                    throw cancellation;
                }

                Exception? failure = Interlocked.Exchange(
                    ref m_authenticationFailure,
                    null);
                if (failure != null)
                {
                    throw failure;
                }

                if (Interlocked.Exchange(ref m_pauseNextAuthentication, 0) == 1)
                {
                    m_authenticationEntered!.SetResult(true);
                    await m_releaseAuthentication!.Task
                        .WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                var identity = new UserIdentity(newIdentity)
                {
                    DisplayName = m_canonicalize(newIdentity)
                };
                return (identity, identity, null);
            }

            protected override ValueTask OnSessionActivatedAsync(
                NodeId authenticationToken,
                ISession session,
                ByteString serverNonce,
                UserTokenType clientUserTokenType,
                string? clientUserId,
                long activationSequence,
                CancellationToken cancellationToken)
            {
                ObservedActivationSequence = activationSequence;
                if (Interlocked.Exchange(ref m_probeGateOnNextActivation, 0) == 1)
                {
                    ValueTask closeOperation = CloseSessionAsync(
                        session.Id,
                        CancellationToken.None);
                    CallbackObservedReleasedGate = closeOperation.IsCompleted;
                    PendingCallbackOperation = closeOperation.AsTask();
                }
                return default;
            }

            private readonly Func<IUserIdentityTokenHandler, string> m_canonicalize;
            private TaskCompletionSource<bool>? m_authenticationEntered;
            private TaskCompletionSource<bool>? m_releaseAuthentication;
            private OperationCanceledException? m_cancellationException;
            private Exception? m_authenticationFailure;
            private int m_pauseNextAuthentication;
            private int m_probeGateOnNextActivation;
        }
    }
}
