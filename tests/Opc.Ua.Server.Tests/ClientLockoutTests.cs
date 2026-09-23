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

using System;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for client lockout functionality after failed authentication attempts.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    [Category("Security")]
    public class ClientLockoutTests
    {
        private ServerFixture<StandardServer> m_fixture;
        private StandardServer m_server;

        /// <summary>
        /// Starts the reference server with anonymous and username policies for authentication lockout tests.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            await m_fixture.LoadConfigurationAsync().ConfigureAwait(false);
            m_fixture.Config.ServerConfiguration.UserTokenPolicies =
            [
                new UserTokenPolicy(UserTokenType.Anonymous),
                new UserTokenPolicy(UserTokenType.UserName)
            ];
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [TearDown]
        public void ClearAuthenticationLockouts()
        {
            m_server.CurrentInstance.SessionManager.ClearAuthenticationLockouts();
        }

        [Test]
        public async Task FailedAuthenticationAttemptsAreTrackedAsync()
        {
            const string sessionName = nameof(FailedAuthenticationAttemptsAreTrackedAsync);
            ArrayOf<EndpointDescription> endpoints = m_server.GetEndpoints();
            EndpointDescription endpoint = FindTcpEndpoint(endpoints);

            SecureChannelContext secureChannelContext = CreateSecureChannelContext(sessionName, endpoint);
            var requestHeader = new RequestHeader();

            CreateSessionResponse createResponse = await m_server.CreateSessionAsync(
                secureChannelContext,
                requestHeader,
                null,
                null,
                null,
                sessionName,
                default,
                default,
                ServerFixtureUtils.DefaultSessionTimeout,
                ServerFixtureUtils.DefaultMaxResponseMessageSize,
                RequestLifetime.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(createResponse.ResponseHeader);
            requestHeader.AuthenticationToken = createResponse.AuthenticationToken;

            var invalidToken = new UserNameIdentityToken
            {
                UserName = "invaliduser",
                Password = System.Text.Encoding.UTF8.GetBytes("wrongpassword").ToByteString(),
                PolicyId = "0"
            };

            for (int i = 0; i < 4; i++)
            {
                ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () => await m_server.ActivateSessionAsync(
                        secureChannelContext,
                        requestHeader,
                        createResponse.ServerSignature,
                        [],
                        [],
                        new ExtensionObject(invalidToken),
                        null,
                        RequestLifetime.None).ConfigureAwait(false));

                Assert.That(exception, Is.Not.Null);
                Assert.That(
                    exception.StatusCode == StatusCodes.BadIdentityTokenInvalid ||
                    exception.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                    exception.StatusCode == StatusCodes.BadUserAccessDenied,
                    Is.True,
                    $"Expected authentication failure status code, got {exception.StatusCode}");
            }

            await m_server.CloseSessionAsync(
                secureChannelContext,
                requestHeader,
                true,
                RequestLifetime.None).ConfigureAwait(false);
        }

        [Test]
        public async Task ClientIsLockedOutAfterFiveFailedAttemptsAsync()
        {
            const string sessionName = nameof(ClientIsLockedOutAfterFiveFailedAttemptsAsync);
            ArrayOf<EndpointDescription> endpoints = m_server.GetEndpoints();
            EndpointDescription endpoint = FindTcpEndpoint(endpoints);
            UserTokenPolicy userNamePolicy = endpoint.UserIdentityTokens.Find(
                policy => policy.TokenType == UserTokenType.UserName)
                ?? throw new AssertionException("The endpoint must advertise a Username token policy.");
            userNamePolicy.SecurityPolicyUri = SecurityPolicies.None;

            SecureChannelContext secureChannelContext = CreateSecureChannelContext(
                sessionName, endpoint, IPAddress.Parse("192.0.2.10"));
            var requestHeader = new RequestHeader
            {
                ReturnDiagnostics = (uint)DiagnosticsMasks.ServiceLocalizedText
            };

            CreateSessionResponse createResponse = await m_server.CreateSessionAsync(
                secureChannelContext,
                requestHeader,
                null,
                null,
                null,
                sessionName,
                default,
                default,
                ServerFixtureUtils.DefaultSessionTimeout,
                ServerFixtureUtils.DefaultMaxResponseMessageSize,
                RequestLifetime.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(createResponse.ResponseHeader);
            requestHeader.AuthenticationToken = createResponse.AuthenticationToken;

            var invalidToken = new UserNameIdentityToken
            {
                UserName = "lockoutuser",
                Password = System.Text.Encoding.UTF8.GetBytes("wrongpassword").ToByteString(),
                PolicyId = userNamePolicy.PolicyId
            };

            var validToken = new UserNameIdentityToken
            {
                UserName = "user1",
                Password = System.Text.Encoding.UTF8.GetBytes("password").ToByteString(),
                PolicyId = userNamePolicy.PolicyId
            };

            for (int i = 0; i < 5; i++)
            {
                ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await m_server.ActivateSessionAsync(
                        secureChannelContext,
                        requestHeader,
                        createResponse.ServerSignature,
                        [],
                        [],
                        new ExtensionObject(invalidToken),
                        null,
                        RequestLifetime.None).ConfigureAwait(false));
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }

            CreateSessionResponse newCreateResponse = await m_server.CreateSessionAsync(
                secureChannelContext,
                requestHeader,
                null,
                null,
                null,
                sessionName + "_2",
                default,
                default,
                ServerFixtureUtils.DefaultSessionTimeout,
                ServerFixtureUtils.DefaultMaxResponseMessageSize,
                RequestLifetime.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(newCreateResponse.ResponseHeader);
            requestHeader.AuthenticationToken = newCreateResponse.AuthenticationToken;

            ServiceResultException lockoutException = Assert.ThrowsAsync<ServiceResultException>(async () => await m_server.ActivateSessionAsync(
                    secureChannelContext,
                    requestHeader,
                    newCreateResponse.ServerSignature,
                    [],
                    [],
                    new ExtensionObject(validToken),
                    null,
                    RequestLifetime.None).ConfigureAwait(false));

            Assert.That(lockoutException, Is.Not.Null);
            Assert.That(lockoutException.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(lockoutException.Message, Does.Contain("Too many failed authentication attempts"));

            await m_server.CloseSessionAsync(
                secureChannelContext,
                requestHeader,
                true,
                RequestLifetime.None).ConfigureAwait(false);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task UnsecuredLockoutUsesObservedPeerInsteadOfApplicationUriAsync(bool hasPeerAddress)
        {
            const string sessionName = nameof(UnsecuredLockoutUsesObservedPeerInsteadOfApplicationUriAsync);
            ArrayOf<EndpointDescription> endpoints = m_server.GetEndpoints();
            EndpointDescription endpoint = FindTcpEndpoint(endpoints);
            UserTokenPolicy userNamePolicy = endpoint.UserIdentityTokens.Find(
                policy => policy.TokenType == UserTokenType.UserName)
                ?? throw new AssertionException("The endpoint must advertise a Username token policy.");
            userNamePolicy.SecurityPolicyUri = SecurityPolicies.None;
            var invalidToken = new UserNameIdentityToken
            {
                UserName = "user1",
                Password = System.Text.Encoding.UTF8.GetBytes("wrongpassword").ToByteString(),
                PolicyId = userNamePolicy.PolicyId
            };
            var validToken = new UserNameIdentityToken
            {
                UserName = "user1",
                Password = System.Text.Encoding.UTF8.GetBytes("password").ToByteString(),
                PolicyId = userNamePolicy.PolicyId
            };

            for (int i = 0; i < 5; i++)
            {
                SecureChannelContext firstContext = CreateSecureChannelContext(
                    hasPeerAddress ? sessionName + i : "shared-listener",
                    endpoint,
                    hasPeerAddress ? IPAddress.Loopback : null);
                var firstHeader = new RequestHeader();
                CreateSessionResponse firstSession = await m_server.CreateSessionAsync(
                    firstContext,
                    firstHeader,
                    new ApplicationDescription { ApplicationUri = "urn:attacker:" + i },
                    null,
                    null,
                    sessionName + i,
                    default,
                    default,
                    ServerFixtureUtils.DefaultSessionTimeout,
                    ServerFixtureUtils.DefaultMaxResponseMessageSize,
                    RequestLifetime.None).ConfigureAwait(false);
                firstHeader.AuthenticationToken = firstSession.AuthenticationToken;
                ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await m_server.ActivateSessionAsync(
                        firstContext,
                        firstHeader,
                        firstSession.ServerSignature,
                        [],
                        [],
                        new ExtensionObject(invalidToken),
                        null,
                        RequestLifetime.None).ConfigureAwait(false));
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                await m_server.CloseSessionAsync(
                    firstContext,
                    firstHeader,
                    true,
                    RequestLifetime.None).ConfigureAwait(false);
            }

            SecureChannelContext victimContext = CreateSecureChannelContext(
                hasPeerAddress ? sessionName + "-victim" : "shared-listener",
                endpoint,
                hasPeerAddress ? IPAddress.Loopback : null);
            var victimHeader = new RequestHeader
            {
                ReturnDiagnostics = (uint)DiagnosticsMasks.ServiceLocalizedText
            };
            CreateSessionResponse victimSession = await m_server.CreateSessionAsync(
                victimContext,
                victimHeader,
                new ApplicationDescription { ApplicationUri = "urn:legitimate-client" },
                null,
                null,
                sessionName + "-victim",
                default,
                default,
                ServerFixtureUtils.DefaultSessionTimeout,
                ServerFixtureUtils.DefaultMaxResponseMessageSize,
                RequestLifetime.None).ConfigureAwait(false);
            victimHeader.AuthenticationToken = victimSession.AuthenticationToken;

            async Task<ActivateSessionResponse> ActivateVictimAsync()
            {
                return await m_server.ActivateSessionAsync(
                    victimContext,
                    victimHeader,
                    victimSession.ServerSignature,
                    [],
                    [],
                    new ExtensionObject(validToken),
                    null,
                    RequestLifetime.None).ConfigureAwait(false);
            }

            if (hasPeerAddress)
            {
                ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(ActivateVictimAsync);
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(exception.Message, Does.Contain("Too many failed authentication attempts"));
            }
            else
            {
                ActivateSessionResponse activated = await ActivateVictimAsync().ConfigureAwait(false);
                Assert.That(activated.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            }
            await m_server.CloseSessionAsync(
                victimContext,
                victimHeader,
                true,
                RequestLifetime.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that successful username authentication clears prior failed attempts for the client application.
        /// </summary>
        /// <exception cref="AssertionException"></exception>
        [Test]
        public async Task SuccessfulAuthenticationClearsFailedAttemptsAsync()
        {
            const string sessionName = nameof(SuccessfulAuthenticationClearsFailedAttemptsAsync);
            ArrayOf<EndpointDescription> endpoints = m_server.GetEndpoints();
            EndpointDescription endpoint = FindTcpEndpoint(endpoints);
            UserTokenPolicy userNamePolicy = endpoint.UserIdentityTokens.Find(
                policy => policy.TokenType == UserTokenType.UserName)
                ?? throw new AssertionException("The endpoint must advertise a Username token policy.");

            SecureChannelContext secureChannelContext = CreateSecureChannelContext(sessionName, endpoint);
            var requestHeader = new RequestHeader();

            CreateSessionResponse createResponse = await m_server.CreateSessionAsync(
                secureChannelContext,
                requestHeader,
                null,
                null,
                null,
                sessionName,
                default,
                default,
                ServerFixtureUtils.DefaultSessionTimeout,
                ServerFixtureUtils.DefaultMaxResponseMessageSize,
                RequestLifetime.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(createResponse.ResponseHeader);
            requestHeader.AuthenticationToken = createResponse.AuthenticationToken;

            var invalidToken = new UserNameIdentityToken
            {
                UserName = "clearuser",
                Password = System.Text.Encoding.UTF8.GetBytes("wrongpassword").ToByteString(),
                PolicyId = userNamePolicy.PolicyId
            };

            // Only successful authentication with a real (non-anonymous) identity
            // clears the failed authentication counter. Anonymous activations
            // intentionally do not reset the counter to prevent attackers from
            // interleaving anonymous logins to bypass the lockout.
            var validToken = new UserNameIdentityToken
            {
                UserName = "user1",
                Password = System.Text.Encoding.UTF8.GetBytes("password").ToByteString(),
                PolicyId = userNamePolicy.PolicyId
            };

            for (int i = 0; i < 3; i++)
            {
                try
                {
                    await m_server.ActivateSessionAsync(
                        secureChannelContext,
                        requestHeader,
                        createResponse.ServerSignature,
                        [],
                        [],
                        new ExtensionObject(invalidToken),
                        null,
                        RequestLifetime.None).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                }
            }

            ActivateSessionResponse activateResponse = await m_server.ActivateSessionAsync(
                secureChannelContext,
                requestHeader,
                createResponse.ServerSignature,
                [],
                [],
                new ExtensionObject(validToken),
                null,
                RequestLifetime.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(activateResponse.ResponseHeader);

            for (int i = 0; i < 4; i++)
            {
                try
                {
                    await m_server.ActivateSessionAsync(
                        secureChannelContext,
                        requestHeader,
                        createResponse.ServerSignature,
                        [],
                        [],
                        new ExtensionObject(invalidToken),
                        null,
                        RequestLifetime.None).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                }
            }

            activateResponse = await m_server.ActivateSessionAsync(
                secureChannelContext,
                requestHeader,
                createResponse.ServerSignature,
                [],
                [],
                new ExtensionObject(validToken),
                null,
                RequestLifetime.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(activateResponse.ResponseHeader);

            await m_server.CloseSessionAsync(
                secureChannelContext,
                requestHeader,
                true,
                RequestLifetime.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that disabling lockout permits valid authentication after failures exceed the normal threshold.
        /// </summary>
        /// <exception cref="AssertionException"></exception>
        [Test]
        public async Task ClientIsNotLockedOutWhenLockoutDisabledAsync()
        {
            // A server configured with MaxFailedAuthenticationAttempts <= 0 disables
            // the brute-force lockout entirely: a client may fail authentication more
            // than the normal threshold (5) and still authenticate afterwards. This
            // supports legitimate high-volume single-certificate clients (for example
            // the many-sessions load test) where transient connect failures must not
            // lock the shared certificate out.
            var fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            await fixture.LoadConfigurationAsync().ConfigureAwait(false);
            fixture.Config.ServerConfiguration.MaxFailedAuthenticationAttempts = 0;
            fixture.Config.ServerConfiguration.UserTokenPolicies =
            [
                new UserTokenPolicy(UserTokenType.Anonymous),
                new UserTokenPolicy(UserTokenType.UserName)
            ];
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);

            try
            {
                const string sessionName = nameof(ClientIsNotLockedOutWhenLockoutDisabledAsync);
                ArrayOf<EndpointDescription> endpoints = server.GetEndpoints();
                EndpointDescription endpoint = FindTcpEndpoint(endpoints);
                UserTokenPolicy userNamePolicy = endpoint.UserIdentityTokens.Find(
                    policy => policy.TokenType == UserTokenType.UserName)
                    ?? throw new AssertionException("The endpoint must advertise a Username token policy.");

                SecureChannelContext secureChannelContext = CreateSecureChannelContext(sessionName, endpoint);
                var requestHeader = new RequestHeader();

                CreateSessionResponse createResponse = await server.CreateSessionAsync(
                    secureChannelContext,
                    requestHeader,
                    null,
                    null,
                    null,
                    sessionName,
                    default,
                    default,
                    ServerFixtureUtils.DefaultSessionTimeout,
                    ServerFixtureUtils.DefaultMaxResponseMessageSize,
                    RequestLifetime.None).ConfigureAwait(false);

                ServerFixtureUtils.ValidateResponse(createResponse.ResponseHeader);
                requestHeader.AuthenticationToken = createResponse.AuthenticationToken;

                var invalidToken = new UserNameIdentityToken
                {
                    UserName = "lockoutuser",
                    Password = System.Text.Encoding.UTF8.GetBytes("wrongpassword").ToByteString(),
                    PolicyId = userNamePolicy.PolicyId
                };

                var validToken = new UserNameIdentityToken
                {
                    UserName = "user1",
                    Password = System.Text.Encoding.UTF8.GetBytes("password").ToByteString(),
                    PolicyId = userNamePolicy.PolicyId
                };

                // Fail authentication well beyond the normal lockout threshold (5).
                for (int i = 0; i < 7; i++)
                {
                    try
                    {
                        await server.ActivateSessionAsync(
                            secureChannelContext,
                            requestHeader,
                            createResponse.ServerSignature,
                            [],
                            [],
                            new ExtensionObject(invalidToken),
                            null,
                            RequestLifetime.None).ConfigureAwait(false);
                    }
                    catch (ServiceResultException)
                    {
                    }
                }

                // With the lockout disabled a valid token still activates: it would be
                // rejected with BadUserAccessDenied if the client had been locked out.
                ActivateSessionResponse activateResponse = await server.ActivateSessionAsync(
                    secureChannelContext,
                    requestHeader,
                    createResponse.ServerSignature,
                    [],
                    [],
                    new ExtensionObject(validToken),
                    null,
                    RequestLifetime.None).ConfigureAwait(false);

                ServerFixtureUtils.ValidateResponse(activateResponse.ResponseHeader);

                await server.CloseSessionAsync(
                    secureChannelContext,
                    requestHeader,
                    true,
                    RequestLifetime.None).ConfigureAwait(false);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private static EndpointDescription FindTcpEndpoint(ArrayOf<EndpointDescription> endpoints)
        {
            EndpointDescription endpoint = endpoints.Find(e =>
                e.TransportProfileUri.Equals(Profiles.UaTcpTransport, StringComparison.Ordinal) ||
                e.TransportProfileUri.Equals(Profiles.HttpsBinaryTransport, StringComparison.Ordinal))
                ?? throw new NotSupportedException("No supported transport profile found.");

            endpoint.SecurityMode = MessageSecurityMode.None;
            endpoint.SecurityPolicyUri = SecurityPolicies.None;
            return endpoint;
        }

        private static SecureChannelContext CreateSecureChannelContext(
            string sessionName,
            EndpointDescription endpoint,
            IPAddress peerAddress = null)
        {
            return new SecureChannelContext(
                sessionName,
                endpoint,
                RequestEncoding.Binary,
                clientChannelCertificate: null,
                serverChannelCertificate: null,
                channelThumbprint: null,
                peerAddress: peerAddress);
        }
    }
}
