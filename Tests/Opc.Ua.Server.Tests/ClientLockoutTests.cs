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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
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

        [Test]
        public async Task FailedAuthenticationAttemptsAreTrackedAsync()
        {
            const string sessionName = nameof(FailedAuthenticationAttemptsAreTrackedAsync);
            EndpointDescriptionCollection endpoints = m_server.GetEndpoints();
            EndpointDescription endpoint = FindTcpEndpoint(endpoints);

            var secureChannelContext = new SecureChannelContext(
                sessionName,
                endpoint,
                RequestEncoding.Binary,
                null,
                null);
            var requestHeader = new RequestHeader();

            CreateSessionResponse createResponse = await m_server.CreateSessionAsync(
                secureChannelContext,
                requestHeader,
                null,
                null,
                null,
                sessionName,
                null,
                null,
                ServerFixtureUtils.DefaultSessionTimeout,
                ServerFixtureUtils.DefaultMaxResponseMessageSize,
                CancellationToken.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(createResponse.ResponseHeader);
            requestHeader.AuthenticationToken = createResponse.AuthenticationToken;

            var invalidToken = new UserNameIdentityToken
            {
                UserName = "invaliduser",
                Password = System.Text.Encoding.UTF8.GetBytes("wrongpassword"),
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
                        CancellationToken.None).ConfigureAwait(false));

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
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task ClientIsLockedOutAfterFiveFailedAttemptsAsync()
        {
            const string sessionName = nameof(ClientIsLockedOutAfterFiveFailedAttemptsAsync);
            EndpointDescriptionCollection endpoints = m_server.GetEndpoints();
            EndpointDescription endpoint = FindTcpEndpoint(endpoints);

            var secureChannelContext = new SecureChannelContext(
                sessionName,
                endpoint,
                RequestEncoding.Binary,
                null,
                null);
            var requestHeader = new RequestHeader();

            CreateSessionResponse createResponse = await m_server.CreateSessionAsync(
                secureChannelContext,
                requestHeader,
                null,
                null,
                null,
                sessionName,
                null,
                null,
                ServerFixtureUtils.DefaultSessionTimeout,
                ServerFixtureUtils.DefaultMaxResponseMessageSize,
                CancellationToken.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(createResponse.ResponseHeader);
            requestHeader.AuthenticationToken = createResponse.AuthenticationToken;

            var invalidToken = new UserNameIdentityToken
            {
                UserName = "lockoutuser",
                Password = System.Text.Encoding.UTF8.GetBytes("wrongpassword"),
                PolicyId = "0"
            };

            var validToken = new UserNameIdentityToken
            {
                UserName = "user1",
                Password = System.Text.Encoding.UTF8.GetBytes("password"),
                PolicyId = "0"
            };

            for (int i = 0; i < 5; i++)
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
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                }
            }

            CreateSessionResponse newCreateResponse = await m_server.CreateSessionAsync(
                secureChannelContext,
                requestHeader,
                null,
                null,
                null,
                sessionName + "_2",
                null,
                null,
                ServerFixtureUtils.DefaultSessionTimeout,
                ServerFixtureUtils.DefaultMaxResponseMessageSize,
                CancellationToken.None).ConfigureAwait(false);

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
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(lockoutException, Is.Not.Null);
            Assert.That(lockoutException.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));

            await m_server.CloseSessionAsync(
                secureChannelContext,
                requestHeader,
                true,
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task SuccessfulAuthenticationClearsFailedAttemptsAsync()
        {
            const string sessionName = nameof(SuccessfulAuthenticationClearsFailedAttemptsAsync);
            EndpointDescriptionCollection endpoints = m_server.GetEndpoints();
            EndpointDescription endpoint = FindTcpEndpoint(endpoints);

            var secureChannelContext = new SecureChannelContext(
                sessionName,
                endpoint,
                RequestEncoding.Binary,
                null,
                null);
            var requestHeader = new RequestHeader();

            CreateSessionResponse createResponse = await m_server.CreateSessionAsync(
                secureChannelContext,
                requestHeader,
                null,
                null,
                null,
                sessionName,
                null,
                null,
                ServerFixtureUtils.DefaultSessionTimeout,
                ServerFixtureUtils.DefaultMaxResponseMessageSize,
                CancellationToken.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(createResponse.ResponseHeader);
            requestHeader.AuthenticationToken = createResponse.AuthenticationToken;

            var invalidToken = new UserNameIdentityToken
            {
                UserName = "clearuser",
                Password = System.Text.Encoding.UTF8.GetBytes("wrongpassword"),
                PolicyId = "0"
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
                        CancellationToken.None).ConfigureAwait(false);
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
                ExtensionObject.Null,
                null,
                CancellationToken.None).ConfigureAwait(false);

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
                        CancellationToken.None).ConfigureAwait(false);
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
                ExtensionObject.Null,
                null,
                CancellationToken.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(activateResponse.ResponseHeader);

            await m_server.CloseSessionAsync(
                secureChannelContext,
                requestHeader,
                true,
                CancellationToken.None).ConfigureAwait(false);
        }

        private static EndpointDescription FindTcpEndpoint(EndpointDescriptionCollection endpoints)
        {
            EndpointDescription endpoint = endpoints.Find(e =>
                e.TransportProfileUri.Equals(Profiles.UaTcpTransport, StringComparison.Ordinal) ||
                e.TransportProfileUri.Equals(Profiles.HttpsBinaryTransport, StringComparison.Ordinal))
                ?? throw new NotSupportedException("No supported transport profile found.");

            endpoint.SecurityMode = MessageSecurityMode.None;
            endpoint.SecurityPolicyUri = SecurityPolicies.None;
            return endpoint;
        }
    }
}
