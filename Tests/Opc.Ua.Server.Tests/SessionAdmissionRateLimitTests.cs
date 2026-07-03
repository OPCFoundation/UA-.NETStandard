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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Integration tests for session-establishment admission control: a rejecting
    /// rate limiter causes <c>CreateSession</c> / <c>ActivateSession</c> to return
    /// <c>BadServerTooBusy</c> with a machine-readable retry-after hint.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    [Category("RateLimiting")]
    public class SessionAdmissionRateLimitTests
    {
        private ServerFixture<StandardServer> m_fixture;
        private StandardServer m_server;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [TearDown]
        public void RestorePermissiveAdmission()
        {
            // Isolate tests: leave the server with admission disabled between tests.
            m_server.RateLimiterProvider = new DefaultServerRateLimiterProvider(
                new ServerRateLimitOptions { Enabled = false });
        }

        [Test]
        public void CreateSessionRejectedWithBadServerTooBusy()
        {
            m_server.RateLimiterProvider = new AlwaysRejectProvider(TimeSpan.FromMilliseconds(2000));

            const string sessionName = nameof(CreateSessionRejectedWithBadServerTooBusy);
            EndpointDescription endpoint = FindTcpEndpoint(m_server.GetEndpoints());
            SecureChannelContext context = CreateSecureChannelContext(sessionName, endpoint);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_server.CreateSessionAsync(
                    context,
                    new RequestHeader(),
                    null,
                    null,
                    null,
                    sessionName,
                    default,
                    default,
                    ServerFixtureUtils.DefaultSessionTimeout,
                    ServerFixtureUtils.DefaultMaxResponseMessageSize,
                    RequestLifetime.None).ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadServerTooBusy));
            // The retry-after hint is encoded in the fault's AdditionalInfo.
            Assert.That(exception.AdditionalInfo, Does.Contain("RetryAfterMs=2000"));
        }

        [Test]
        public async Task ActivateSessionRejectedWithBadServerTooBusyAsync()
        {
            const string sessionName = nameof(ActivateSessionRejectedWithBadServerTooBusyAsync);
            EndpointDescription endpoint = FindTcpEndpoint(m_server.GetEndpoints());
            SecureChannelContext context = CreateSecureChannelContext(sessionName, endpoint);
            var requestHeader = new RequestHeader();

            // Create a session while admission is permissive (TearDown state).
            CreateSessionResponse createResponse = await m_server.CreateSessionAsync(
                context,
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

            // Now reject admission; ActivateSession must fail fast with BadServerTooBusy
            // before the identity token is even processed.
            m_server.RateLimiterProvider = new AlwaysRejectProvider(retryAfter: null);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_server.ActivateSessionAsync(
                    context,
                    requestHeader,
                    createResponse.ServerSignature,
                    [],
                    [],
                    new ExtensionObject(new AnonymousIdentityToken { PolicyId = "0" }),
                    null,
                    RequestLifetime.None).ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadServerTooBusy));
        }

        [Test]
        public async Task CreateSessionSucceedsWhenAdmissionPermissiveAsync()
        {
            // TearDown leaves the server with admission disabled, so a create succeeds.
            const string sessionName = nameof(CreateSessionSucceedsWhenAdmissionPermissiveAsync);
            EndpointDescription endpoint = FindTcpEndpoint(m_server.GetEndpoints());
            SecureChannelContext context = CreateSecureChannelContext(sessionName, endpoint);
            var requestHeader = new RequestHeader();

            CreateSessionResponse createResponse = await m_server.CreateSessionAsync(
                context,
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

            await m_server.CloseSessionAsync(
                context,
                requestHeader,
                true,
                RequestLifetime.None).ConfigureAwait(false);
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
            EndpointDescription endpoint)
        {
            return new SecureChannelContext(
                sessionName,
                endpoint,
                RequestEncoding.Binary,
                clientChannelCertificate: null,
                serverChannelCertificate: null,
                channelThumbprint: null);
        }

        /// <summary>
        /// A rate-limiter provider that rejects every session establishment,
        /// optionally carrying a retry-after hint.
        /// </summary>
        private sealed class AlwaysRejectProvider : IServerRateLimiterProvider
        {
            private readonly TimeSpan? m_retryAfter;

            public AlwaysRejectProvider(TimeSpan? retryAfter)
            {
                m_retryAfter = retryAfter;
            }

            public int ListenBacklog => 0;

            public IConnectionRateLimiter ConnectionRateLimiter => null;

            public bool TryAcquireSessionEstablishment(out IDisposable lease, out TimeSpan? retryAfter)
            {
                lease = null;
                retryAfter = m_retryAfter;
                return false;
            }

            public void Dispose()
            {
            }
        }
    }
}
