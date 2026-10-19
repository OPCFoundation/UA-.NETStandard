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

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.ActivateAsync(disabled, AuthenticationHarness.UserName, harness.Password)
                    .ConfigureAwait(false))!;

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

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.ActivateAsync(created, userName, password).ConfigureAwait(false))!;

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
            public AuthenticationHarness()
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
