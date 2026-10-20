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
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.UserManagement;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Exercises the Reference Server's registered password verifier through fresh, socket-free activations.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    public sealed class ReferenceServerAuthenticationRegressionTests
    {
        /// <summary>
        /// Application provisioning acceptance never authenticates an unapproved certificate user.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR12ProvisioningAutoAcceptCannotAuthenticateUntrustedUserCertificateAsync(bool peerTrusted)
        {
            using Certificate user = CertificateBuilder.Create("CN=Unapproved R12 User")
                .SetRSAKeySize(2048).CreateForRSA();
            using var validator = new CertificateManager(NUnitTelemetryContext.Create());
            validator.MapFromSecurityConfiguration(new SecurityConfiguration
            {
                AutoAcceptUntrustedCertificates = true,
                UseValidatedCertificates = true,
                TrustedPeerCertificates = new CertificateTrustList
                {
                    TrustedCertificates = peerTrusted
                        ? [new CertificateIdentifier { RawData = user.RawData }]
                        : []
                }
            });
            validator.AcceptError = static (_, error) => error.StatusCode == StatusCodes.BadCertificateUntrusted;
            await using var harness = new AuthenticationHarness(validator);
            CreateSessionResult created = await harness.CreateSessionAsync().ConfigureAwait(false);

            await Assert.ThatAsync(
                async () => await harness.ActivateCertificateAsync(created, user).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadIdentityTokenRejected));

            Assert.That(created.Session.Activated, Is.False);
            Assert.That(created.Session.EffectiveIdentity, Is.Null);
            using Certificate applicationCertificate = CertificateBuilder.Create("CN=Bootstrap R12 Application")
                .SetRSAKeySize(2048).CreateForRSA();
            CertificateValidationResult application = await validator.ValidateAsync(applicationCertificate)
                .ConfigureAwait(false);
            Assert.That(application.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good),
                "Application bootstrap acceptance must remain enabled.");
        }

        /// <summary>
        /// A permissive Users cache is not authority for the sample's strict user-certificate verifier.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR12PermissiveCertificateCacheCannotCrossIntoStrictUsersValidationAsync()
        {
            using Certificate user = CertificateBuilder.Create("CN=Cached R12 User")
                .SetRSAKeySize(2048).CreateForRSA();
            using var validator = new CertificateManager(NUnitTelemetryContext.Create());
            validator.MapFromSecurityConfiguration(new SecurityConfiguration
            {
                AutoAcceptUntrustedCertificates = true,
                UseValidatedCertificates = true
            });
            validator.AcceptError = static (_, error) => error.StatusCode == StatusCodes.BadCertificateUntrusted;
            CertificateValidationResult warmed = await validator.ValidateAsync(user, TrustListIdentifier.Users)
                .ConfigureAwait(false);
            Assert.That(warmed.IsValid, Is.True);
            await using var harness = new AuthenticationHarness(validator);
            CreateSessionResult created = await harness.CreateSessionAsync().ConfigureAwait(false);

            await Assert.ThatAsync(
                async () => await harness.ActivateCertificateAsync(created, user).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadIdentityTokenRejected));

            Assert.That(created.Session.Activated, Is.False);
            Assert.That(created.Session.EffectiveIdentity, Is.Null);
            using var chain = new CertificateCollection { user };
            CertificateValidationResult strict = await validator.ValidateAsync(
                chain, TrustListIdentifier.Users,
                new Opc.Ua.Security.Certificates.CertificateValidationOptions
                {
                    AcceptError = static (_, _) => false
                }).ConfigureAwait(false);
            Assert.That(strict.IsValid, Is.False);
        }

        /// <summary>
        /// Explicit Users trust grants only the intended user role and still requires private-key possession.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR12TrustedUserCertificateAndApplicationBootstrapRemainSupportedAsync(bool badProof)
        {
            using Certificate user = CertificateBuilder.Create("CN=Trusted R12 User")
                .SetRSAKeySize(2048).CreateForRSA();
            using var validator = new CertificateManager(NUnitTelemetryContext.Create());
            validator.MapFromSecurityConfiguration(new SecurityConfiguration
            {
                AutoAcceptUntrustedCertificates = true,
                TrustedUserCertificates = new CertificateTrustList
                {
                    TrustedCertificates = [new CertificateIdentifier { RawData = user.RawData }]
                }
            });
            await using var harness = new AuthenticationHarness(validator);
            CreateSessionResult created = await harness.CreateSessionAsync().ConfigureAwait(false);
            if (badProof)
            {
                await Assert.ThatAsync(
                    async () => await harness.ActivateCertificateAsync(created, user, true).ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo((StatusCode)StatusCodes.BadUserSignatureInvalid));
                Assert.That(created.Session.Activated, Is.False);
                return;
            }

            await harness.ActivateCertificateAsync(created, user).ConfigureAwait(false);

            Assert.That(created.Session.Activated, Is.True);
            Assert.That(created.Session.EffectiveIdentity.GrantedRoleIds.ToArray(),
                Does.Contain(ObjectIds.WellKnownRole_AuthenticatedUser));
            Assert.That(created.Session.EffectiveIdentity.GrantedRoleIds.ToArray(),
                Does.Not.Contain(ObjectIds.WellKnownRole_SecurityAdmin));
            Assert.That(created.Session.EffectiveIdentity.GrantedRoleIds.ToArray(),
                Does.Not.Contain(ObjectIds.WellKnownRole_ConfigureAdmin));
        }

        /// <summary>
        /// Users trust and provisioning acceptance do not suppress invalid lifetime or key usage.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR12TrustedInvalidUserCertificateCannotAuthenticateAsync(bool expired)
        {
            using Certificate invalid = expired
                ? CertificateBuilder.Create("CN=Expired R12 User")
                    .SetNotBefore(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                    .SetNotAfter(new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                    .SetRSAKeySize(2048).CreateForRSA()
                : CertificateBuilder.Create("CN=Wrong Purpose R12 User")
                    .AddExtension(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true))
                    .SetRSAKeySize(2048).CreateForRSA();
            using var validator = new CertificateManager(NUnitTelemetryContext.Create());
            validator.MapFromSecurityConfiguration(new SecurityConfiguration
            {
                AutoAcceptUntrustedCertificates = true,
                TrustedUserCertificates = new CertificateTrustList
                {
                    TrustedCertificates = [new CertificateIdentifier { RawData = invalid.RawData }]
                }
            });
            await using var harness = new AuthenticationHarness(validator);
            CreateSessionResult created = await harness.CreateSessionAsync().ConfigureAwait(false);

            await Assert.ThatAsync(
                async () => await harness.ActivateCertificateAsync(created, invalid).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(expired ? StatusCodes.BadIdentityTokenRejected : StatusCodes.BadIdentityTokenInvalid));

            Assert.That(created.Session.Activated, Is.False);
            Assert.That(created.Session.EffectiveIdentity, Is.Null);
        }

        /// <summary>
        /// Disabling an account preserves its password but denies a new activation until the account is enabled.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF09DisabledAccountCannotFreshAuthenticateAsync()
        {
            await using var harness = new AuthenticationHarness();
            CreateSessionResult original = await harness.CreateSessionAsync().ConfigureAwait(false);
            await harness.ActivateAsync(original, AuthenticationHarness.UserName, harness.Password)
                .ConfigureAwait(false);
            Assert.That(original.Session.Activated, Is.True);
            Assert.That(original.Session.EffectiveIdentity.GrantedRoleIds.Contains(
                ObjectIds.WellKnownRole_SecurityAdmin), Is.True);
            Assert.That(original.Session.EffectiveIdentity.GrantedRoleIds.Contains(
                ObjectIds.WellKnownRole_ConfigureAdmin), Is.True);
            await harness.Manager.CloseSessionAsync(original.SessionId).ConfigureAwait(false);

            harness.SetDisabled(true);
            Assert.That(harness.Reference.UserDatabase.CheckCredentials(
                AuthenticationHarness.UserName, Encoding.UTF8.GetBytes(harness.Password)), Is.True,
                "Disabling must be tested with the still-correct password, not a password reset.");
            CreateSessionResult disabled = await harness.CreateSessionAsync().ConfigureAwait(false);
            Assert.That(disabled.SessionId, Is.Not.EqualTo(original.SessionId));

            ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.ActivateAsync(disabled, AuthenticationHarness.UserName, harness.Password)
                    .ConfigureAwait(false))
                ?? throw new AssertionException("Expected a service failure.");

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(disabled.Session.Activated, Is.False);
            Assert.That(disabled.Session.EffectiveIdentity, Is.Null);
            await harness.Manager.CloseSessionAsync(disabled.SessionId).ConfigureAwait(false);

            harness.SetDisabled(false);
            CreateSessionResult enabled = await harness.CreateSessionAsync().ConfigureAwait(false);
            await harness.ActivateAsync(enabled, AuthenticationHarness.UserName, harness.Password)
                .ConfigureAwait(false);
            Assert.That(enabled.Session.Activated, Is.True);
            Assert.That(enabled.Session.EffectiveIdentity.GrantedRoleIds.Contains(
                ObjectIds.WellKnownRole_SecurityAdmin), Is.True);
            Assert.That(enabled.Session.EffectiveIdentity.GrantedRoleIds.Contains(
                ObjectIds.WellKnownRole_ConfigureAdmin), Is.True);
        }

        /// <summary>
        /// Empty, unknown, and incorrect credentials never activate a fresh session.
        /// </summary>
        [TestCase("", "invalid")]
        [TestCase("missing-account", "invalid")]
        [TestCase(AuthenticationHarness.UserName, "")]
        [TestCase(AuthenticationHarness.UserName, "incorrect")]
        [Category("SecurityRegression")]
        public async Task SecurityF09InvalidCredentialsCannotActivateAsync(string userName, string password)
        {
            await using var harness = new AuthenticationHarness();
            CreateSessionResult created = await harness.CreateSessionAsync().ConfigureAwait(false);

            ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.ActivateAsync(created, userName, password).ConfigureAwait(false))
                ?? throw new AssertionException("Expected a service failure.");

            Assert.That(StatusCode.IsBad(error.StatusCode), Is.True);
            Assert.That(created.Session.Activated, Is.False);
            Assert.That(created.Session.EffectiveIdentity, Is.Null);
        }

        /// <summary>
        /// Wires the sample's actual authenticator registration to an in-memory session manager.
        /// </summary>
        private sealed class AuthenticationHarness : IAsyncDisposable
        {
            /// <summary>
            /// Creates test-owned account, certificate, identity registry, and diagnostics without starting a host.
            /// </summary>
            public AuthenticationHarness(CertificateManager? userValidator = null)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                var configuration = new ApplicationConfiguration
                {
                    ApplicationUri = "urn:opcfoundation:security-f09",
                    ServerConfiguration = new ServerConfiguration
                    {
                        MinSessionTimeout = 1_000,
                        MaxSessionTimeout = 60_000,
                        MaxSessionCount = 10
                    }
                };
                Reference = new ReferenceServer(telemetry);
                if (userValidator != null)
                {
                    typeof(ReferenceServer)
                        .GetField("m_userCertificateValidator", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .SetValue(Reference, userValidator);
                }
                Password = Guid.NewGuid().ToString("N");
                m_users = (IUserManagement)typeof(ReferenceServer)
                    .GetField("m_userManagement", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(Reference)!;
                Assert.That(m_users.ModifyUser(
                    UserName, true, Password, false, 0, false, string.Empty, "test-administrator").StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                m_data = new ServerInternalData(
                    new ServerProperties(), configuration, ServiceMessageContext.CreateEmpty(telemetry));
                // Supply only the already-existing startup context needed by the real verifier's namespace mapping.
                typeof(StandardServer).GetField("m_serverInternal", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(Reference, m_data);
                typeof(ReferenceServer)
                    .GetMethod("RegisterIdentityAuthenticators", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(Reference, [m_data]);

                var server = new Mock<IServerInternal>();
                server.SetupGet(value => value.Telemetry).Returns(telemetry);
                server.SetupGet(value => value.NamespaceUris).Returns(m_data.NamespaceUris);
                server.SetupGet(value => value.MessageContext).Returns(m_data.MessageContext);
                server.SetupGet(value => value.IdentityRegistry).Returns(m_data.IdentityRegistry);
                server.SetupGet(value => value.UserManagement).Returns(m_users);
                server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
                var diagnostics = new Mock<IDiagnosticsNodeManager>();
                int nextSession = 0;
                diagnostics.Setup(value => value.CreateSessionDiagnosticsAsync(
                        It.IsAny<ServerSystemContext>(),
                        It.IsAny<SessionDiagnosticsDataType>(),
                        It.IsAny<NodeValueSimpleEventHandler>(),
                        It.IsAny<SessionSecurityDiagnosticsDataType>(),
                        It.IsAny<NodeValueSimpleEventHandler>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<NodeId>(new NodeId((uint)Interlocked.Increment(ref nextSession), 1)));
                server.SetupGet(value => value.DiagnosticsNodeManager).Returns(diagnostics.Object);
                Manager = new SessionManager(server.Object, configuration);
                server.SetupGet(value => value.SessionManager).Returns(Manager);
                m_certificate = CertificateBuilder.Create("CN=Security F09 Test").SetRSAKeySize(2048).CreateForRSA();
                var endpoint = new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost/security-f09",
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy
                        {
                            PolicyId = "username",
                            TokenType = UserTokenType.UserName,
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                        },
                        new UserTokenPolicy
                        {
                            PolicyId = "certificate",
                            TokenType = UserTokenType.Certificate,
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                        }
                    ]
                };
                m_context = new OperationContext(
                    new RequestHeader(),
                    new SecureChannelContext("security-f09", endpoint, RequestEncoding.Binary),
                    RequestType.ActivateSession,
                    RequestLifetime.None);
            }

            /// <summary>
            /// Gets the sample server whose registered verifier is under test.
            /// </summary>
            public ReferenceServer Reference { get; }

            /// <summary>
            /// Gets the test-only password shared by the account's activation attempts.
            /// </summary>
            public string Password { get; }

            /// <summary>
            /// Gets the real session manager used for creation and activation.
            /// </summary>
            public SessionManager Manager { get; }

            /// <summary>
            /// Creates a distinct session for a fresh activation attempt.
            /// </summary>
            public ValueTask<CreateSessionResult> CreateSessionAsync()
            {
                return Manager.CreateSessionAsync(
                    m_context, m_certificate, "security-f09", default, new ApplicationDescription(),
                    m_context.ChannelContext!.EndpointDescription!.EndpointUrl, null, [], 60_000, 65_536);
            }

            /// <summary>
            /// Activates through the sample's registered authenticator rather than testing its account flag alone.
            /// </summary>
            public async ValueTask ActivateAsync(CreateSessionResult created, string userName, string password)
            {
                var token = new UserNameIdentityTokenHandler(userName, Encoding.UTF8.GetBytes(password));
                token.UpdatePolicy(m_context.ChannelContext!.EndpointDescription!.UserIdentityTokens[0]);
                try
                {
                    await token.EncryptAsync(
                        m_certificate, created.ServerNonce.ToArray(), SecurityPolicies.Basic256Sha256,
                        m_data.MessageContext).ConfigureAwait(false);
                    await Manager.ActivateSessionAsync(
                        m_context, created.AuthenticationToken, new SignatureData(), new ExtensionObject(token.Token),
                        new SignatureData(), []).ConfigureAwait(false);
                }

                finally
                {
                    if (token.DecryptedPassword is byte[] cleartext)
                    {
                        Array.Clear(cleartext, 0, cleartext.Length);
                    }
                }
            }

            /// <summary>
            /// Submits a real certificate token and private-key proof through fresh SessionManager activation.
            /// </summary>
            public async ValueTask ActivateCertificateAsync(
                CreateSessionResult created, Certificate certificate, bool badProof = false)
            {
                SecurityPolicyInfo policy = SecurityPolicies.Default.GetInfo(SecurityPolicies.Basic256Sha256)!;
                byte[] signatureData = policy.GetUserTokenSignatureData(
                    null, created.ServerNonce.ToArray(), m_certificate.RawData, null, null, null, null,
                    MessageSecurityMode.None);
                SignatureData proof = await SecurityPolicies.Default.CreateSignatureDataAsync(
                    policy, certificate, signatureData, CancellationToken.None).ConfigureAwait(false);
                if (badProof)
                {
                    proof.Signature = ByteString.From([0, 1, 2]);
                }
                var token = new X509IdentityToken
                {
                    PolicyId = "certificate",
                    CertificateData = certificate.RawData.ToByteString()
                };
                await Manager.ActivateSessionAsync(
                    m_context, created.AuthenticationToken, new SignatureData(),
                    new ExtensionObject(token), proof, []).ConfigureAwait(false);
            }

            /// <summary>
            /// Changes only account metadata through the real UserManagement implementation.
            /// </summary>
            public void SetDisabled(bool disabled)
            {
                ServiceResult result = m_users.ModifyUser(
                    UserName, false, string.Empty, true,
                    disabled ? UserConfigurationMask.Disabled : 0, false, string.Empty, "administrator");
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            }

            /// <summary>
            /// Closes sessions and detaches the test-only startup context before releasing all owned resources.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                foreach (ISession session in Manager.GetSessions())
                {
                    await Manager.CloseSessionAsync(session.Id).ConfigureAwait(false);
                }
                Manager.Dispose();
                m_context.Dispose();
                m_certificate.Dispose();
                typeof(StandardServer).GetField("m_serverInternal", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(Reference, null);
                Reference.Dispose();
                await m_data.DisposeAsync().ConfigureAwait(false);
            }

            /// <summary>
            /// Identifies the isolated account owned by each harness.
            /// </summary>
            public const string UserName = "sysadmin";

            /// <summary>
            /// Holds the sample's real account-management service.
            /// </summary>
            private readonly IUserManagement m_users;

            /// <summary>
            /// Supplies the sample verifier's namespace and identity-registry context.
            /// </summary>
            private readonly ServerInternalData m_data;

            /// <summary>
            /// Owns the in-memory certificate referenced by the sessions.
            /// </summary>
            private readonly Certificate m_certificate;

            /// <summary>
            /// Supplies the socket-free activation channel.
            /// </summary>
            private readonly OperationContext m_context;
        }
    }
}
