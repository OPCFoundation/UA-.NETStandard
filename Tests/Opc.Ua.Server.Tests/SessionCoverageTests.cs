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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using ServerSession = Opc.Ua.Server.Session;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Direct-construction unit tests for <see cref="Session"/> that exercise the
    /// branches reachable without a full secure-channel crypto handshake:
    /// constructor guards, request validation, diagnostic counters, locale
    /// updates, continuation points and the anonymous / username identity token
    /// validation and activation paths under <see cref="MessageSecurityMode.None"/>.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    [Parallelizable]
    public class SessionCoverageTests
    {
        private static readonly ICertificateFactory s_factory = DefaultCertificateFactory.Instance;
        private static readonly string[] s_preferredLocales = ["en-US", "de-DE"];
        private static readonly string[] s_singlePreferredLocale = ["en-US"];

        private ITelemetryContext m_telemetry = null!;
        private Mock<IServerInternal> m_serverMock = null!;
        private Certificate m_serverCertificate = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_serverMock = new Mock<IServerInternal>();
            m_serverMock.Setup(s => s.Telemetry).Returns(m_telemetry);
            m_serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            var sessionManagerMock = new Mock<ISessionManager>();
            m_serverMock.Setup(s => s.SessionManager).Returns(sessionManagerMock.Object);
            m_serverCertificate = s_factory.CreateCertificate("CN=SessionCoverageServer").CreateForRSA();
        }

        [TearDown]
        public void TearDown()
        {
            m_serverCertificate?.Dispose();
        }

        private static EndpointDescription CreateEndpoint(
            string securityPolicyUri = null!,
            MessageSecurityMode securityMode = MessageSecurityMode.None,
            UserTokenPolicy[]? tokens = null)
        {
            return new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840/Coverage",
                SecurityMode = securityMode,
                SecurityPolicyUri = securityPolicyUri ?? SecurityPolicies.None,
                UserIdentityTokens = tokens != null ? new ArrayOf<UserTokenPolicy>(tokens) : default
            };
        }

        private static OperationContext CreateContext(
            EndpointDescription endpoint,
            string channelId = "channel-1",
            RequestType requestType = RequestType.ActivateSession)
        {
            var channelContext = new SecureChannelContext(channelId, endpoint, RequestEncoding.Binary);
            var requestHeader = new RequestHeader();
            return new OperationContext(requestHeader, channelContext, requestType, RequestLifetime.None);
        }

        private ServerSession CreateSession(
            EndpointDescription endpoint,
            string channelId = "channel-1",
            Certificate? clientCertificate = null)
        {
            OperationContext context = CreateContext(endpoint, channelId);
            return new ServerSession(
                context,
                m_serverMock.Object,
                m_serverCertificate,
                new NodeId(Guid.NewGuid()),
                ByteString.Empty,
                Nonce.CreateNonce(SecurityPolicies.None),
                "CoverageSession",
                new ApplicationDescription { ApplicationUri = "urn:coverage:client" },
                endpoint.EndpointUrl!,
                clientCertificate!,
                new CertificateCollection(),
                60_000,
                10,
                10);
        }

        private static string GetSupportedEphemeralKeyPolicy()
        {
            foreach (string policyUri in new[]
                     {
                         SecurityPolicies.ECC_nistP256,
                         SecurityPolicies.ECC_nistP384,
                         SecurityPolicies.ECC_brainpoolP256r1,
                         SecurityPolicies.ECC_brainpoolP384r1,
                         SecurityPolicies.ECC_curve25519,
                         SecurityPolicies.ECC_curve448,
                         SecurityPolicies.Aes128_Sha256_RsaOaep,
                         SecurityPolicies.Aes256_Sha256_RsaPss,
                         SecurityPolicies.Basic256Sha256
                     })
            {
                SecurityPolicyInfo? info = SecurityPolicies.GetInfo(policyUri);
                if (info?.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
                {
                    return policyUri;
                }
            }

            Assert.Fail("No security policy with an ephemeral key algorithm is registered.");
            return SecurityPolicies.None;
        }

        [Test]
        public void ConstructorWithNullContextThrows()
        {
            Assert.That(
                () => new ServerSession(
                    null!,
                    m_serverMock.Object,
                    m_serverCertificate,
                    new NodeId(Guid.NewGuid()),
                    ByteString.Empty,
                    Nonce.CreateNonce(SecurityPolicies.None),
                    "s",
                    new ApplicationDescription(),
                    "opc.tcp://localhost:4840",
                    null!,
                    new CertificateCollection(),
                    60_000,
                    10,
                    10),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorWithNullServerThrows()
        {
            EndpointDescription endpoint = CreateEndpoint();
            OperationContext context = CreateContext(endpoint);

            Assert.That(
                () => new ServerSession(
                    context,
                    null!,
                    m_serverCertificate,
                    new NodeId(Guid.NewGuid()),
                    ByteString.Empty,
                    Nonce.CreateNonce(SecurityPolicies.None),
                    "s",
                    new ApplicationDescription(),
                    endpoint.EndpointUrl!,
                    null!,
                    new CertificateCollection(),
                    60_000,
                    10,
                    10),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorInitializesDiagnostics()
        {
            using ServerSession session = CreateSession(CreateEndpoint());

            Assert.That(session.SecureChannelId, Is.EqualTo("channel-1"));
            Assert.That(session.Activated, Is.False);
            Assert.That(session.SessionDiagnostics, Is.Not.Null);
            Assert.That(session.SessionDiagnostics.SessionName, Is.EqualTo("CoverageSession"));
            Assert.That(session.Identity, Is.Not.Null);
        }

        [Test]
        public void SetUserTokenSecurityPolicyThenGetNewEphemeralKeyReturnsNullForNoPolicy()
        {
            using ServerSession session = CreateSession(CreateEndpoint());

            Assert.That(session.GetNewEphemeralKey(), Is.Null);
        }

        [Test]
        public void GetNewEphemeralKeyReturnsKeyWhenPolicySet()
        {
            using ServerSession session = CreateSession(CreateEndpoint());
            session.SetUserTokenSecurityPolicy(SecurityPolicies.Basic256Sha256);

            EphemeralKeyType? key = session.GetNewEphemeralKey();

            Assert.That(key, Is.Not.Null);
            Assert.That(key!.PublicKey.IsEmpty, Is.False);
        }

        [Test]
        public void IsSecureChannelValidReflectsChannelId()
        {
            using ServerSession session = CreateSession(CreateEndpoint(), channelId: "abc");

            Assert.That(session.IsSecureChannelValid("abc"), Is.True);
            Assert.That(session.IsSecureChannelValid("other"), Is.False);
        }

        [Test]
        public void UpdateLocaleIdsReturnsTrueOnChangeAndFalseOnRepeat()
        {
            using ServerSession session = CreateSession(CreateEndpoint());

            Assert.That(session.UpdateLocaleIds(new ArrayOf<string>(s_preferredLocales)), Is.True);
            Assert.That(session.UpdateLocaleIds(new ArrayOf<string>(s_preferredLocales)), Is.False);
            Assert.That(session.PreferredLocales, Is.EqualTo(s_preferredLocales));
        }

        [Test]
        public void ValidateRequestWithNullHeaderThrows()
        {
            using ServerSession session = CreateSession(CreateEndpoint());
            var channelContext = new SecureChannelContext("channel-1", CreateEndpoint(), RequestEncoding.Binary);

            Assert.That(
                () => session.ValidateRequest(null!, channelContext, RequestType.Read),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ValidateRequestWithNullChannelOnCloseThrowsSessionIdInvalid()
        {
            using ServerSession session = CreateSession(CreateEndpoint());

            var ex = Assert.Throws<ServiceResultException>(
                () => session.ValidateRequest(new RequestHeader(), null!, RequestType.CloseSession));
            Assert.That(ex!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadSessionIdInvalid));
        }

        [Test]
        public void ValidateRequestWithWrongChannelThrowsSecureChannelIdInvalid()
        {
            using ServerSession session = CreateSession(CreateEndpoint());
            var channelContext = new SecureChannelContext("wrong-channel", CreateEndpoint(), RequestEncoding.Binary);

            var ex = Assert.Throws<ServiceResultException>(
                () => session.ValidateRequest(new RequestHeader(), channelContext, RequestType.Read));
            Assert.That(ex!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadSecureChannelIdInvalid));
        }

        [Test]
        public void ValidateRequestWhenNotActivatedThrowsSessionNotActivated()
        {
            using ServerSession session = CreateSession(CreateEndpoint());
            var channelContext = new SecureChannelContext("channel-1", CreateEndpoint(), RequestEncoding.Binary);

            var ex = Assert.Throws<ServiceResultException>(
                () => session.ValidateRequest(new RequestHeader(), channelContext, RequestType.Read));
            Assert.That(ex!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadSessionNotActivated));
        }

        [TestCase(RequestType.Read)]
        [TestCase(RequestType.HistoryRead)]
        [TestCase(RequestType.Write)]
        [TestCase(RequestType.HistoryUpdate)]
        [TestCase(RequestType.Call)]
        [TestCase(RequestType.CreateMonitoredItems)]
        [TestCase(RequestType.ModifyMonitoredItems)]
        [TestCase(RequestType.SetMonitoringMode)]
        [TestCase(RequestType.SetTriggering)]
        [TestCase(RequestType.DeleteMonitoredItems)]
        [TestCase(RequestType.CreateSubscription)]
        [TestCase(RequestType.ModifySubscription)]
        [TestCase(RequestType.SetPublishingMode)]
        [TestCase(RequestType.Publish)]
        [TestCase(RequestType.Republish)]
        [TestCase(RequestType.TransferSubscriptions)]
        [TestCase(RequestType.DeleteSubscriptions)]
        [TestCase(RequestType.AddNodes)]
        [TestCase(RequestType.AddReferences)]
        [TestCase(RequestType.DeleteNodes)]
        [TestCase(RequestType.DeleteReferences)]
        [TestCase(RequestType.Browse)]
        [TestCase(RequestType.BrowseNext)]
        [TestCase(RequestType.TranslateBrowsePathsToNodeIds)]
        [TestCase(RequestType.QueryFirst)]
        [TestCase(RequestType.QueryNext)]
        [TestCase(RequestType.RegisterNodes)]
        [TestCase(RequestType.UnregisterNodes)]
        public void ValidateRequestUpdatesDiagnosticCounterForRequestType(RequestType requestType)
        {
            using ServerSession session = CreateSession(CreateEndpoint());
            var channelContext = new SecureChannelContext("channel-1", CreateEndpoint(), RequestEncoding.Binary);

            // Non-activated session records the counter (error path) then throws.
            Assert.That(
                () => session.ValidateRequest(new RequestHeader(), channelContext, requestType),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(session.SessionDiagnostics.TotalRequestCount.TotalCount, Is.GreaterThan(0));
        }

        [Test]
        public void ValidateRequestWithUnknownRequestTypeThrowsUnexpected()
        {
            using ServerSession session = CreateSession(CreateEndpoint());
            var channelContext = new SecureChannelContext("channel-1", CreateEndpoint(), RequestEncoding.Binary);

            Assert.That(
                () => session.ValidateRequest(new RequestHeader(), channelContext, (RequestType)9999),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ValidateDiagnosticInfoWithoutAdditionalInfoMaskDoesNothing()
        {
            using ServerSession session = CreateSession(CreateEndpoint());
            var header = new RequestHeader { ReturnDiagnostics = 0 };

            Assert.That(() => session.ValidateDiagnosticInfo(header), Throws.Nothing);
            Assert.That(header.ReturnDiagnostics, Is.Zero);
        }

        [Test]
        public void SaveAndRestoreContinuationPointRoundTrips()
        {
            using ServerSession session = CreateSession(CreateEndpoint());
            var id = Guid.NewGuid();
            var cp = new ContinuationPoint { Id = id };

            session.SaveContinuationPoint(cp);
            ContinuationPoint? restored = session.RestoreContinuationPoint(
                new ByteString(id.ToByteArray()));

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.Id, Is.EqualTo(id));
            restored.Dispose();
        }

        [Test]
        public void SaveContinuationPointWithNullThrows()
        {
            using ServerSession session = CreateSession(CreateEndpoint());

            Assert.That(
                () => session.SaveContinuationPoint(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void RestoreContinuationPointReturnsNullForUnknownAndBadLength()
        {
            using ServerSession session = CreateSession(CreateEndpoint());
            session.SaveContinuationPoint(new ContinuationPoint { Id = Guid.NewGuid() });

            Assert.That(session.RestoreContinuationPoint(new ByteString(new byte[] { 1, 2, 3 })), Is.Null);
            Assert.That(session.RestoreContinuationPoint(new ByteString(Guid.NewGuid().ToByteArray())), Is.Null);
        }

        [Test]
        public void SaveContinuationPointEvictsOldestWhenOverCapacity()
        {
            using ServerSession session = CreateSession(CreateEndpoint());

            // Capacity is 10 (maxBrowseContinuationPoints); adding 12 evicts the oldest.
            var first = new ContinuationPoint { Id = Guid.NewGuid() };
            session.SaveContinuationPoint(first);
            for (int i = 0; i < 11; i++)
            {
                session.SaveContinuationPoint(new ContinuationPoint { Id = Guid.NewGuid() });
            }

            Assert.That(
                session.RestoreContinuationPoint(new ByteString(first.Id.ToByteArray())),
                Is.Null,
                "The oldest continuation point should have been evicted.");
        }

        [Test]
        public void SaveAndRestoreHistoryContinuationPointRoundTrips()
        {
            using ServerSession session = CreateSession(CreateEndpoint());
            var id = Guid.NewGuid();
            var payload = new object();

            session.SaveHistoryContinuationPoint(id, payload);
            object? restored = session.RestoreHistoryContinuationPoint(new ByteString(id.ToByteArray()));

            Assert.That(restored, Is.SameAs(payload));
        }

        [Test]
        public void SaveHistoryContinuationPointWithNullThrows()
        {
            using ServerSession session = CreateSession(CreateEndpoint());

            Assert.That(
                () => session.SaveHistoryContinuationPoint(Guid.NewGuid(), null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void RestoreHistoryContinuationPointReturnsNullForBadLength()
        {
            using ServerSession session = CreateSession(CreateEndpoint());
            session.SaveHistoryContinuationPoint(Guid.NewGuid(), new object());

            Assert.That(session.RestoreHistoryContinuationPoint(new ByteString(new byte[] { 1, 2 })), Is.Null);
        }

        [Test]
        public void ConstructorWithNullChannelContextThrows()
        {
            var requestHeader = new RequestHeader();
            var context = new OperationContext(
                requestHeader, null!, RequestType.ActivateSession, RequestLifetime.None);

            var ex = Assert.Throws<ServiceResultException>(
                () => new ServerSession(
                    context,
                    m_serverMock.Object,
                    m_serverCertificate,
                    new NodeId(Guid.NewGuid()),
                    ByteString.Empty,
                    Nonce.CreateNonce(SecurityPolicies.None),
                    "s",
                    new ApplicationDescription(),
                    "opc.tcp://localhost:4840",
                    null!,
                    new CertificateCollection(),
                    60_000,
                    10,
                    10));
            Assert.That(ex!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadSecureChannelIdInvalid));
        }

        [Test]
        public void ValidateDiagnosticInfoGrantsUserPermissionInfoForSecurityAdmin()
        {
            var tokens = new[]
            {
                new UserTokenPolicy { PolicyId = "anon", TokenType = UserTokenType.Anonymous }
            };
            EndpointDescription endpoint = CreateEndpoint(tokens: tokens);
            using ServerSession session = CreateSession(endpoint, channelId: "channel-1");
            OperationContext context = CreateContext(endpoint, channelId: "channel-1");

            session.ValidateBeforeActivate(
                context,
                new SignatureData(),
                default,
                new SignatureData(),
                out IUserIdentityTokenHandler? handler,
                out _);

            var effectiveIdentity = new Mock<IUserIdentity>();
            effectiveIdentity.Setup(i => i.TokenType).Returns(UserTokenType.Anonymous);
            effectiveIdentity.Setup(i => i.DisplayName).Returns("admin");
            effectiveIdentity
                .Setup(i => i.GrantedRoleIds)
                .Returns(new ArrayOf<NodeId>(new[] { ObjectIds.WellKnownRole_SecurityAdmin }));

            session.Activate(
                context,
                handler!,
                new UserIdentity(),
                effectiveIdentity.Object,
                default,
                Nonce.CreateNonce(SecurityPolicies.None));

            const uint mask = (uint)DiagnosticsMasks.ServiceAdditionalInfo;
            var header = new RequestHeader { ReturnDiagnostics = mask };

            session.ValidateDiagnosticInfo(header);

            Assert.That(
                header.ReturnDiagnostics & (uint)DiagnosticsMasks.UserPermissionAdditionalInfo,
                Is.Not.Zero);
        }

        [Test]
        public void ValidateBeforeActivateWithClientCertificateAndEmptySignatureThrows()
        {
            using Certificate clientCertificate =
                s_factory.CreateCertificate("CN=CoverageClient").CreateForRSA();
            EndpointDescription endpoint = CreateEndpoint(
                SecurityPolicies.Basic256Sha256, MessageSecurityMode.Sign);
            using ServerSession session = CreateSession(
                endpoint, channelId: "channel-1", clientCertificate: clientCertificate);
            OperationContext context = CreateContext(endpoint, channelId: "channel-1");

            var ex = Assert.Throws<ServiceResultException>(
                () => session.ValidateBeforeActivate(
                    context,
                    new SignatureData(),
                    default,
                    new SignatureData(),
                    out _,
                    out _));
            Assert.That(ex!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadApplicationSignatureInvalid));
        }

        [Test]
        public void SaveHistoryContinuationPointEvictsOldestWhenOverCapacity()
        {
            using ServerSession session = CreateSession(CreateEndpoint());
            var first = Guid.NewGuid();

            session.SaveHistoryContinuationPoint(first, new object());
            for (int i = 0; i < 11; i++)
            {
                session.SaveHistoryContinuationPoint(Guid.NewGuid(), new object());
            }

            Assert.That(
                session.RestoreHistoryContinuationPoint(new ByteString(first.ToByteArray())),
                Is.Null,
                "The oldest history continuation point should have been evicted.");
        }

        [Test]
        public void ValidateBeforeActivateWithSecurityPolicyMismatchThrows()
        {
            using ServerSession session = CreateSession(CreateEndpoint(SecurityPolicies.None));
            OperationContext context = CreateContext(
                CreateEndpoint(SecurityPolicies.Basic256Sha256, MessageSecurityMode.Sign));

            var ex = Assert.Throws<ServiceResultException>(
                () => session.ValidateBeforeActivate(
                    context,
                    new SignatureData(),
                    default,
                    new SignatureData(),
                    out _,
                    out _));
            Assert.That(ex!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadSecurityPolicyRejected));
        }

        [Test]
        public void ValidateBeforeActivateWithWrongChannelThrows()
        {
            using ServerSession session = CreateSession(CreateEndpoint(), channelId: "channel-1");
            OperationContext context = CreateContext(CreateEndpoint(), channelId: "other-channel");

            var ex = Assert.Throws<ServiceResultException>(
                () => session.ValidateBeforeActivate(
                    context,
                    new SignatureData(),
                    default,
                    new SignatureData(),
                    out _,
                    out _));
            Assert.That(ex!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadSecureChannelIdInvalid));
        }

        [Test]
        public void ValidateBeforeActivateWithAnonymousTokenSucceeds()
        {
            var tokens = new UserTokenPolicy[]
            {
                new UserTokenPolicy { PolicyId = "anon", TokenType = UserTokenType.Anonymous }
            };
            EndpointDescription endpoint = CreateEndpoint(tokens: tokens);
            using ServerSession session = CreateSession(endpoint);
            OperationContext context = CreateContext(endpoint);

            session.ValidateBeforeActivate(
                context,
                new SignatureData(),
                default,
                new SignatureData(),
                out IUserIdentityTokenHandler? handler,
                out UserTokenPolicy? policy);

            Assert.That(handler, Is.Not.Null);
            Assert.That(policy, Is.Not.Null);
            Assert.That(policy!.TokenType, Is.EqualTo(UserTokenType.Anonymous));
        }

        [Test]
        public void ValidateBeforeActivateAnonymousNotSupportedThrows()
        {
            var tokens = new UserTokenPolicy[]
            {
                new UserTokenPolicy { PolicyId = "user", TokenType = UserTokenType.UserName }
            };
            EndpointDescription endpoint = CreateEndpoint(tokens: tokens);
            using ServerSession session = CreateSession(endpoint);
            OperationContext context = CreateContext(endpoint);

            var ex = Assert.Throws<ServiceResultException>(
                () => session.ValidateBeforeActivate(
                    context,
                    new SignatureData(),
                    default,
                    new SignatureData(),
                    out _,
                    out _));
            Assert.That(ex!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public void ValidateBeforeActivateWithUserNameTokenResolvesPolicy()
        {
            var tokens = new UserTokenPolicy[]
            {
                new UserTokenPolicy
                {
                    PolicyId = "user",
                    TokenType = UserTokenType.UserName,
                    SecurityPolicyUri = SecurityPolicies.None
                }
            };
            EndpointDescription endpoint = CreateEndpoint(tokens: tokens);
            using ServerSession session = CreateSession(endpoint);
            OperationContext context = CreateContext(endpoint);

            var userToken = new UserNameIdentityToken
            {
                PolicyId = "user",
                UserName = "bob",
                Password = new ByteString(new byte[] { 1, 2, 3 })
            };

            session.ValidateBeforeActivate(
                context,
                new SignatureData(),
                new ExtensionObject(userToken),
                new SignatureData(),
                out IUserIdentityTokenHandler? handler,
                out UserTokenPolicy? policy);

            Assert.That(handler, Is.Not.Null);
            Assert.That(policy, Is.Not.Null);
            Assert.That(policy!.TokenType, Is.EqualTo(UserTokenType.UserName));
        }

        [Test]
        public void ActivateFirstThenReactivateSwitchesChannel()
        {
            var tokens = new UserTokenPolicy[]
            {
                new UserTokenPolicy { PolicyId = "anon", TokenType = UserTokenType.Anonymous }
            };
            EndpointDescription endpoint = CreateEndpoint(tokens: tokens);
            using ServerSession session = CreateSession(endpoint, channelId: "channel-1");
            OperationContext context = CreateContext(endpoint, channelId: "channel-1");

            session.ValidateBeforeActivate(
                context,
                new SignatureData(),
                default,
                new SignatureData(),
                out IUserIdentityTokenHandler? handler,
                out _);

            bool changed = session.Activate(
                context,
                handler!,
                new UserIdentity(),
                new UserIdentity(),
                new ArrayOf<string>(s_singlePreferredLocale),
                Nonce.CreateNonce(SecurityPolicies.None));

            Assert.That(session.Activated, Is.True);
            Assert.That(changed, Is.True);

            // Re-activate on a different channel exercises the RE-ACTIVATION branch.
            OperationContext reactivateContext = CreateContext(endpoint, channelId: "channel-2");
            session.Activate(
                reactivateContext,
                handler!,
                new UserIdentity(),
                new UserIdentity(),
                new ArrayOf<string>(s_singlePreferredLocale),
                Nonce.CreateNonce(SecurityPolicies.None));

            Assert.That(session.SecureChannelId, Is.EqualTo("channel-2"));
        }

        [Test]
        public void ProcessCreateSessionAdditionalParametersReturnsNullForNullInput()
        {
            AdditionalParametersType? result =
                SessionSecurityPolicyHelper.ProcessCreateSessionAdditionalParameters(
                    Mock.Of<ISession>(),
                    null!,
                    NullLogger<SessionCoverageTests>.Instance);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void ProcessCreateSessionAdditionalParametersPreservesUnknownParameters()
        {
            var input = new AdditionalParametersType
            {
                Parameters =
                [
                    new KeyValuePair
                    {
                        Key = new QualifiedName("Unknown"),
                        Value = Variant.From("value")
                    }
                ]
            };

            AdditionalParametersType? result =
                SessionSecurityPolicyHelper.ProcessCreateSessionAdditionalParameters(
                    Mock.Of<ISession>(),
                    input,
                    NullLogger<SessionCoverageTests>.Instance);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Parameters, Has.Count.EqualTo(1));
            Assert.That(result.Parameters[0].Key, Is.EqualTo(new QualifiedName("Unknown")));
            Assert.That(result.Parameters[0].Value.TryGetValue(out string value), Is.True);
            Assert.That(value, Is.EqualTo("value"));
        }

        [Test]
        public void ProcessCreateSessionAdditionalParametersRejectsUnsupportedEcdhPolicy()
        {
            var input = new AdditionalParametersType
            {
                Parameters =
                [
                    new KeyValuePair
                    {
                        Key = QualifiedName.From(AdditionalParameterNames.ECDHPolicyUri),
                        Value = Variant.From(SecurityPolicies.None)
                    }
                ]
            };

            AdditionalParametersType? result =
                SessionSecurityPolicyHelper.ProcessCreateSessionAdditionalParameters(
                    Mock.Of<ISession>(),
                    input,
                    NullLogger<SessionCoverageTests>.Instance);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Parameters, Has.Count.EqualTo(1));
            Assert.That(result.Parameters[0].Key, Is.EqualTo(QualifiedName.From(AdditionalParameterNames.ECDHKey)));
            Assert.That(result.Parameters[0].Value.TryGetValue(out StatusCode status), Is.True);
            Assert.That(status.Code, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
        }

        [Test]
        public void ProcessCreateSessionAdditionalParametersCreatesEcdhKeyForSupportedPolicy()
        {
            var key = new EphemeralKeyType();
            string policyUri = GetSupportedEphemeralKeyPolicy();
            var session = new Mock<ISession>();
            session.Setup(s => s.GetNewEphemeralKey()).Returns(key);
            var input = new AdditionalParametersType
            {
                Parameters =
                [
                    new KeyValuePair
                    {
                        Key = QualifiedName.From(AdditionalParameterNames.ECDHPolicyUri),
                        Value = Variant.From(policyUri)
                    }
                ]
            };

            AdditionalParametersType? result =
                SessionSecurityPolicyHelper.ProcessCreateSessionAdditionalParameters(
                    session.Object,
                    input,
                    NullLogger<SessionCoverageTests>.Instance);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Parameters, Has.Count.EqualTo(1));
            Assert.That(result.Parameters[0].Key, Is.EqualTo(QualifiedName.From(AdditionalParameterNames.ECDHKey)));
            Variant value = result.Parameters[0].Value;
            Assert.That(value.TypeInfo, Is.EqualTo(TypeInfo.Scalars.ExtensionObject));
            Assert.That(
                value.TryGetStructure<EphemeralKeyType>(out EphemeralKeyType? actualKey),
                Is.True);
            Assert.That(actualKey, Is.SameAs(key));
            session.Verify(s => s.SetUserTokenSecurityPolicy(policyUri), Times.Once);
            session.Verify(s => s.GetNewEphemeralKey(), Times.Once);
        }

        [Test]
        public void ProcessActivateSessionAdditionalParametersAppendsKeyWhenAvailable()
        {
            var key = new EphemeralKeyType();
            var session = new Mock<ISession>();
            session.Setup(s => s.GetNewEphemeralKey()).Returns(key);
            var input = new AdditionalParametersType
            {
                Parameters =
                [
                    new KeyValuePair
                    {
                        Key = new QualifiedName("Existing"),
                        Value = Variant.From(1)
                    }
                ]
            };

            AdditionalParametersType result =
                SessionSecurityPolicyHelper.ProcessActivateSessionAdditionalParameters(
                    session.Object,
                    input);

            Assert.That(result, Is.Not.SameAs(input));
            Assert.That(result.Parameters, Has.Count.EqualTo(2));
            Assert.That(result.Parameters[1].Key, Is.EqualTo(QualifiedName.From(AdditionalParameterNames.ECDHKey)));
            Variant value = result.Parameters[1].Value;
            Assert.That(value.TypeInfo, Is.EqualTo(TypeInfo.Scalars.ExtensionObject));
            Assert.That(
                value.TryGetStructure<EphemeralKeyType>(out EphemeralKeyType? actualKey),
                Is.True);
            Assert.That(actualKey, Is.SameAs(key));
            session.Verify(s => s.GetNewEphemeralKey(), Times.Once);
        }

        [Test]
        public void ProcessActivateSessionAdditionalParametersReturnsInputWhenNoKeyAvailable()
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.GetNewEphemeralKey()).Returns((EphemeralKeyType?)null);
            var input = new AdditionalParametersType();

            AdditionalParametersType result =
                SessionSecurityPolicyHelper.ProcessActivateSessionAdditionalParameters(
                    session.Object,
                    input);

            Assert.That(result, Is.SameAs(input));
        }
    }
}
