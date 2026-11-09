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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Verifies against a running server that
    /// <see cref="SecurityConfiguration.AllowUnencryptedUserNameTokens"/>
    /// gates a UserName token whose password would travel in plain text,
    /// and that an opted-in session warns before every such send: the
    /// initial activation, a changed identity and a reconnect.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("Client")]
    [Category("Session")]
    public sealed class SessionUnencryptedUserNameTokenTests
    {
        private const string kWarningEventName = "SendingUnencryptedUserNameToken";
        private const int kSessionTimeout = 60000;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                Path.GetTempPath(),
                "uunt-" + Guid.NewGuid().ToString("N")[..8]);

            m_server = new ServerFixture<ReferenceServer>(context => new ReferenceServer(context))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            await m_server.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            ServerConfiguration serverConfiguration = m_server.Config.ServerConfiguration!;

            // A channel-bound UserName policy. The server encrypts the
            // password with the channel security policy on a Sign channel
            // and with Basic256Sha256 on a None channel.
            serverConfiguration.UserTokenPolicies += new UserTokenPolicy(UserTokenType.UserName);

            // The policy a legacy device offers: the password is sent as is.
            serverConfiguration.UserTokenPolicies += new UserTokenPolicy(UserTokenType.UserName)
            {
                SecurityPolicyUri = SecurityPolicies.None
            };

            await m_server.StartAsync(m_pkiRoot).ConfigureAwait(false);

            // The session logs through the telemetry of the application
            // configuration it is created with, so the recording provider
            // has to sit in the client fixture's telemetry.
            m_logs = new RecordingLoggerProvider(LogLevel.Information);
            m_nunitOutput = new NUnitForwardingProvider();
            m_telemetry = DefaultTelemetry.Create(builder => builder
                .SetMinimumLevel(LogLevel.Information)
                .AddProvider(m_logs)
                .AddProvider(m_nunitOutput));
            m_client = new ClientFixture(m_telemetry);
            await m_client.LoadClientConfigurationAsync(m_pkiRoot, "UnencryptedUserNameTokenClient")
                .ConfigureAwait(false);

            var serverUrl = new Uri(
                $"{Utils.UriSchemeOpcTcp}://localhost:{m_server.Port}/{nameof(ReferenceServer)}");
            m_endpoints = await m_client.GetEndpointsAsync(serverUrl).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_client != null)
            {
                await m_client.DisposeAsync().ConfigureAwait(false);
            }
            if (m_server != null)
            {
                await m_server.StopAsync().ConfigureAwait(false);
            }
            (m_telemetry as IDisposable)?.Dispose();
            m_nunitOutput?.Dispose();
            m_logs?.Dispose();
            if (m_pkiRoot != null && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        [SetUp]
        public void SetUp()
        {
            // Only the records a test produces itself count.
            m_logStart = m_logs.Records.Count;
        }

        [TearDown]
        public void TearDown()
        {
            m_client.Config.SecurityConfiguration.AllowUnencryptedUserNameTokens = false;
        }

        /// <summary>
        /// Without the opt-in the client refuses to send the password in
        /// plain text. The open fails before anything reaches the server.
        /// </summary>
        [TestCase(MessageSecurityMode.None)]
        [TestCase(MessageSecurityMode.Sign)]
        public async Task DefaultRejectsPlaintextPasswordAsync(MessageSecurityMode securityMode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            EndpointDescription endpoint = FindEndpoint(securityMode);
            UserIdentity identity = CreatePlaintextUserIdentity(endpoint);

            ServiceResultException exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await CreateSessionAsync(
                    endpoint, identity, "rejected-plaintext", timeout.Token).ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                Assert.That(
                    exception.Message,
                    Does.Contain(nameof(SecurityConfiguration.AllowUnencryptedUserNameTokens)));
                Assert.That(Warnings(), Is.Empty);
            });
        }

        /// <summary>
        /// With the opt-in the password is sent in plain text over a channel
        /// that does not encrypt, and the session warns about it.
        /// </summary>
        [TestCase(MessageSecurityMode.None)]
        [TestCase(MessageSecurityMode.Sign)]
        public async Task OptInSendsPlaintextPasswordAndWarnsAsync(MessageSecurityMode securityMode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            m_client.Config.SecurityConfiguration.AllowUnencryptedUserNameTokens = true;
            EndpointDescription endpoint = FindEndpoint(securityMode);
            UserIdentity identity = CreatePlaintextUserIdentity(endpoint);

            using ISession session = await CreateSessionAsync(
                endpoint, identity, "allowed-plaintext", timeout.Token).ConfigureAwait(false);

            Assert.That(session.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
            DataValue value = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));

            RecordedLogRecord[] warnings = Warnings();
            Assert.That(warnings, Has.Length.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(warnings[0].LogLevel, Is.EqualTo(LogLevel.Warning));
                Assert.That(
                    warnings[0].Properties["SecurityMode"]?.ToString(),
                    Is.EqualTo(securityMode.ToString()));
                Assert.That(warnings[0].Properties, Contains.Key("EndpointUrl"));
            });

            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// A SignAndEncrypt channel protects the password itself, so a None
        /// user token policy needs neither the opt-in nor a warning.
        /// </summary>
        [Test]
        public async Task EncryptedChannelNeedsNoOptInAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            EndpointDescription endpoint = FindEndpoint(MessageSecurityMode.SignAndEncrypt);
            UserIdentity identity = CreatePlaintextUserIdentity(endpoint);

            using ISession session = await CreateSessionAsync(
                endpoint, identity, "encrypted-channel", timeout.Token).ConfigureAwait(false);

            Assert.That(session.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
            DataValue value = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(Warnings(), Is.Empty);

            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// The opt-in does not touch a UserName policy that encrypts the
        /// password: a Sign channel with an encrypted token logs nothing.
        /// </summary>
        [Test]
        public async Task EncryptedTokenDoesNotWarnAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            m_client.Config.SecurityConfiguration.AllowUnencryptedUserNameTokens = true;
            EndpointDescription endpoint = FindEndpoint(MessageSecurityMode.Sign);
            UserIdentity identity = CreateUserIdentity(
                endpoint,
                policy => string.IsNullOrEmpty(policy.SecurityPolicyUri));

            using ISession session = await CreateSessionAsync(
                endpoint, identity, "encrypted-token", timeout.Token).ConfigureAwait(false);

            Assert.That(session.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
            DataValue value = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(Warnings(), Is.Empty);

            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// The warning precedes every plaintext send, not only the first one.
        /// Changing the identity re-activates the session on the same channel
        /// and a reconnect activates it on a new channel.
        /// </summary>
        [Test]
        public async Task OptInWarnsOnEveryActivationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            m_client.Config.SecurityConfiguration.AllowUnencryptedUserNameTokens = true;
            EndpointDescription endpoint = FindEndpoint(MessageSecurityMode.None);
            UserIdentity identity = CreatePlaintextUserIdentity(endpoint);

            using ISession session = await CreateSessionAsync(
                endpoint, identity, "every-activation", timeout.Token).ConfigureAwait(false);
            Assert.That(Warnings(), Has.Length.EqualTo(1));

            await session.UpdateSessionAsync(identity, default, timeout.Token).ConfigureAwait(false);
            Assert.That(Warnings(), Has.Length.EqualTo(2));

            await session.ReconnectAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(Warnings(), Has.Length.EqualTo(3));

            DataValue value = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));

            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Switching an open session to a UserName identity is gated like the
        /// initial activation, so an anonymous session cannot bypass the check.
        /// </summary>
        [Test]
        public async Task DefaultRejectsPlaintextPasswordOnUpdateSessionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            EndpointDescription endpoint = FindEndpoint(MessageSecurityMode.None);
            UserIdentity identity = CreatePlaintextUserIdentity(endpoint);

            using ISession session = await CreateSessionAsync(
                endpoint, new UserIdentity(), "anonymous-then-plaintext", timeout.Token)
                .ConfigureAwait(false);
            Assert.That(session.Identity.TokenType, Is.EqualTo(UserTokenType.Anonymous));

            ServiceResultException exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await session.UpdateSessionAsync(identity, default, timeout.Token)
                    .ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                Assert.That(session.Identity.TokenType, Is.EqualTo(UserTokenType.Anonymous));
                Assert.That(Warnings(), Is.Empty);
            });

            // The rejected update left the anonymous session intact.
            DataValue value = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));

            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// A private copy of the server endpoint with the given security mode,
        /// so a policy id lookup in one test cannot leak into another.
        /// </summary>
        private EndpointDescription FindEndpoint(MessageSecurityMode securityMode)
        {
            string securityPolicyUri = securityMode == MessageSecurityMode.None
                ? SecurityPolicies.None
                : SecurityPolicies.Basic256Sha256;
            EndpointDescription endpoint = m_endpoints.ToList().First(candidate =>
                candidate.SecurityMode == securityMode &&
                candidate.SecurityPolicyUri == securityPolicyUri);
            return CoreUtils.Clone(endpoint)!;
        }

        /// <summary>
        /// A UserName identity bound to the endpoint's UserName policy that
        /// does not encrypt the password.
        /// </summary>
        private static UserIdentity CreatePlaintextUserIdentity(EndpointDescription endpoint)
        {
            return CreateUserIdentity(
                endpoint,
                policy => policy.SecurityPolicyUri == SecurityPolicies.None);
        }

        private static UserIdentity CreateUserIdentity(
            EndpointDescription endpoint,
            Func<UserTokenPolicy, bool> predicate)
        {
            UserTokenPolicy policy = endpoint.UserIdentityTokens.ToList().First(candidate =>
                candidate.TokenType == UserTokenType.UserName && predicate(candidate));
            return new UserIdentity("user1", "password"u8) { PolicyId = policy.PolicyId! };
        }

        /// <summary>
        /// Opens a raw session on the client fixture's configuration.
        /// </summary>
        private Task<ISession> CreateSessionAsync(
            EndpointDescription description,
            IUserIdentity identity,
            string sessionName,
            CancellationToken ct)
        {
            var endpoint = new ConfiguredEndpoint(
                null,
                description,
                EndpointConfiguration.Create(m_client.Config));
            var factory = new DefaultSessionFactory(m_telemetry);
            return factory.CreateAsync(
                m_client.Config,
                endpoint,
                false,
                false,
                sessionName,
                kSessionTimeout,
                identity,
                default,
                ct);
        }

        /// <summary>
        /// The plain text warnings the current test produced.
        /// </summary>
        private RecordedLogRecord[] Warnings()
        {
            return m_logs.Records
                .Skip(m_logStart)
                .Where(record => record.EventId.Name == kWarningEventName)
                .ToArray();
        }

        /// <summary>
        /// Keeps the client logs in the NUnit test output next to the
        /// recording provider, for diagnosing a failed run.
        /// </summary>
        private sealed class NUnitForwardingProvider : ILoggerProvider
        {
            public ILogger CreateLogger(string categoryName)
            {
                return m_output.CreateLogger(categoryName);
            }

            public void Dispose()
            {
            }

            private readonly ITelemetryContext m_output = NUnitTelemetryContext.Create();
        }

        private ServerFixture<ReferenceServer> m_server = null!;
        private ClientFixture m_client = null!;
        private ITelemetryContext m_telemetry = null!;
        private RecordingLoggerProvider m_logs = null!;
        private NUnitForwardingProvider m_nunitOutput = null!;
        private ArrayOf<EndpointDescription> m_endpoints;
        private string m_pkiRoot = null!;
        private int m_logStart;
    }
}
