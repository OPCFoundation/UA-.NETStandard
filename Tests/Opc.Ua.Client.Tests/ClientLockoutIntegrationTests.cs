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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Tests;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Integration tests for client lockout functionality after failed authentication attempts.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("Security")]
    [Category("ClientLockout")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ClientLockoutIntegrationTests
    {
        private ServerFixture<ReferenceServer> m_serverFixture;
        private ClientFixture m_clientFixture;
        private ReferenceServer m_server;
        private string m_pkiRoot;
        private Uri m_serverUrl;
        private EndpointDescriptionCollection m_endpoints;
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();

            m_serverFixture = new ServerFixture<ReferenceServer>
            (t => new ReferenceServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true,
                AllNodeManagers = false,
                OperationLimits = true
            };

            await m_serverFixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);

            m_serverFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.UserName));

            m_server = await m_serverFixture.StartAsync().ConfigureAwait(false);
            m_server.TokenValidator = new TokenValidatorMock();

            m_clientFixture = new ClientFixture(false, false, m_telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);

            m_serverUrl = new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}");

            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 10000;

            using DiscoveryClient discoveryClient = await DiscoveryClient.CreateAsync(
                m_serverUrl,
                endpointConfiguration,
                m_telemetry).ConfigureAwait(false);

            m_endpoints = await discoveryClient.GetEndpointsAsync(null).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }

            Utils.SilentDispose(m_clientFixture);

            try
            {
                if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
                {
                    Directory.Delete(m_pkiRoot, true);
                }
            }
            catch
            {
            }
        }

        [Test]
        public async Task ClientIsLockedOutAfterMultipleFailedPasswordAttemptsAsync()
        {
            EndpointDescription endpoint = m_endpoints.FirstOrDefault(
                e => e.SecurityMode == MessageSecurityMode.None);
            Assert.That(endpoint, Is.Not.Null, "No endpoint with SecurityMode.None found");

            var endpointConfiguration = EndpointConfiguration.Create(m_clientFixture.Config);
            var configuredEndpoint = new ConfiguredEndpoint(null, endpoint, endpointConfiguration);

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    using ISession session = await m_clientFixture.SessionFactory.CreateAsync(
                        m_clientFixture.Config,
                        configuredEndpoint,
                        false,
                        false,
                        $"LockoutTestSession_{attempt}",
                        60000,
                        new UserIdentity("invaliduser", System.Text.Encoding.UTF8.GetBytes("wrongpassword")),
                        null).ConfigureAwait(false);

                    Assert.Fail("Session creation should have failed with invalid credentials");
                }
                catch (ServiceResultException ex)
                {
                    Assert.That(
                        ex.StatusCode == StatusCodes.BadIdentityTokenInvalid ||
                        ex.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                        ex.StatusCode == StatusCodes.BadUserAccessDenied,
                        Is.True,
                        $"Attempt {attempt + 1}: Expected authentication failure, got {ex.StatusCode}");
                }
            }

            ServiceResultException lockoutException = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                using ISession session = await m_clientFixture.SessionFactory.CreateAsync(
                    m_clientFixture.Config,
                    configuredEndpoint,
                    false,
                    false,
                    "LockoutTestSession_Final",
                    60000,
                    new UserIdentity("user1", System.Text.Encoding.UTF8.GetBytes("password")),
                    null).ConfigureAwait(false);
            });

            Assert.That(lockoutException, Is.Not.Null);
            Assert.That(lockoutException.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task SuccessfulLoginAfterFailuresResetsLockoutCounterAsync()
        {
            EndpointDescription endpoint = m_endpoints.FirstOrDefault(
                e => e.SecurityMode == MessageSecurityMode.None);
            Assert.That(endpoint, Is.Not.Null, "No endpoint with SecurityMode.None found");

            var endpointConfiguration = EndpointConfiguration.Create(m_clientFixture.Config);
            var configuredEndpoint = new ConfiguredEndpoint(null, endpoint, endpointConfiguration);

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using ISession session = await m_clientFixture.SessionFactory.CreateAsync(
                        m_clientFixture.Config,
                        configuredEndpoint,
                        false,
                        false,
                        $"ResetTestSession_{attempt}",
                        60000,
                        new UserIdentity("resetuser", System.Text.Encoding.UTF8.GetBytes("wrongpassword")),
                        null).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                }
            }

            using (ISession successSession = await m_clientFixture.SessionFactory.CreateAsync(
                m_clientFixture.Config,
                configuredEndpoint,
                false,
                false,
                "ResetTestSession_Success",
                60000,
                new UserIdentity(),
                null).ConfigureAwait(false))
            {
                Assert.That(successSession, Is.Not.Null);
                Assert.That(successSession.Connected, Is.True);
            }

            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    using ISession session = await m_clientFixture.SessionFactory.CreateAsync(
                        m_clientFixture.Config,
                        configuredEndpoint,
                        false,
                        false,
                        $"PostResetTestSession_{attempt}",
                        60000,
                        new UserIdentity("resetuser", System.Text.Encoding.UTF8.GetBytes("wrongpassword")),
                        null).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                }
            }

            using (ISession successSession = await m_clientFixture.SessionFactory.CreateAsync(
                m_clientFixture.Config,
                configuredEndpoint,
                false,
                false,
                "ResetTestSession_Success",
                60000,
                new UserIdentity(),
                null).ConfigureAwait(false))
            {
                Assert.That(successSession, Is.Not.Null);
                Assert.That(successSession.Connected, Is.True);
            }
        }

        [Test]
        public async Task AnonymousLoginFailsWhileLockedOutAsync()
        {
            EndpointDescription endpoint = m_endpoints.FirstOrDefault(
                e => e.SecurityMode == MessageSecurityMode.None);
            Assert.That(endpoint, Is.Not.Null, "No endpoint with SecurityMode.None found");

            var endpointConfiguration = EndpointConfiguration.Create(m_clientFixture.Config);
            var configuredEndpoint = new ConfiguredEndpoint(null, endpoint, endpointConfiguration);

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    using ISession session = await m_clientFixture.SessionFactory.CreateAsync(
                        m_clientFixture.Config,
                        configuredEndpoint,
                        false,
                        false,
                        $"AnonLockoutTestSession_{attempt}",
                        60000,
                        new UserIdentity("anonlockoutuser", System.Text.Encoding.UTF8.GetBytes("wrongpassword")),
                        null).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                }
            }

            ServiceResultException lockoutException = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                using ISession anonSession = await m_clientFixture.SessionFactory.CreateAsync(
                m_clientFixture.Config,
                configuredEndpoint,
                false,
                false,
                "AnonTestSession",
                60000,
                new UserIdentity(),
                null).ConfigureAwait(false);
            });

            Assert.That(lockoutException, Is.Not.Null);
            Assert.That(lockoutException.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }
    }
}
