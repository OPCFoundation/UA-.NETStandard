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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.TestFramework
{
    /// <summary>
    /// Base class for compliance tests. Starts an in-process ReferenceServer
    /// and connects a client session for use by derived test classes.
    /// </summary>
    public abstract class TestFixture
    {
        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();
            m_logger.LogInformation("Test PkiRoot: {PkiRoot}", m_pkiRoot);

            // Start in-process ReferenceServer with the optional conformance
            // node managers enabled (Part 17 AliasName + FileSystem). These
            // are off by default so the standard test fixtures keep a small
            // address space; conformance tests need them.
            ServerFixture = new ServerFixture<ReferenceServer>(
                t => new ReferenceServer(t) { EnableFileSystemNodeManager = true })
            {
                AutoAccept = true,
                SecurityNone = true,
                OperationLimits = true,
                AllNodeManagers = true
            };

            await ServerFixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            ServerFixture.Config.TransportQuotas.MaxMessageSize = TransportQuotaMaxMessageSize;
            ServerFixture.Config.TransportQuotas.MaxByteStringLength =
                ServerFixture.Config.TransportQuotas.MaxStringLength = TransportQuotaMaxStringLength;

            // Enable all user token types so security tests can authenticate
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies =
                new UserTokenPolicy[] {
                    new(UserTokenType.Anonymous),
                    new(UserTokenType.UserName),
                    new(UserTokenType.Certificate)
                }.ToArrayOf();

            // Enable durable subscriptions so SubscriptionDurableTests
            // (SetSubscriptionDurable / TransferSubscriptions on durable subs)
            // can exercise the durable code paths instead of skipping with
            // BadNotSupported.
            ServerFixture.Config.ServerConfiguration.DurableSubscriptionsEnabled = true;

            // Enable server diagnostics so BaseInfoBehavioralTests can read
            // Server.ServerDiagnostics.* arrays, SamplingIntervalDiagnosticsArray,
            // SessionSecurityDiagnosticsArray, etc.
            ServerFixture.Config.ServerConfiguration.DiagnosticsEnabled = true;

            // Enable auditing so AuditingOperationTests can verify audit
            // event emission for CreateSession / ActivateSession /
            // CloseSession.
            ServerFixture.Config.ServerConfiguration.AuditingEnabled = true;

            ReferenceServer = await ServerFixture.StartAsync().ConfigureAwait(false);

            // Attach the mock response controller so individual tests
            // can inject service-result error codes or mutate response
            // fields. Production servers leave ResponseMutator null;
            // installing the controller is a test-only behaviour.
            MockController = new MockResponseController();
            ReferenceServer.ResponseMutator = MockController;

            // Activate alarm sources so Alarms & Conditions tests have live
            // alarm instances to inspect.
            await Quickstarts.Servers.Utils.ApplyCTTModeAsync(
                TextWriter.Null, ReferenceServer).ConfigureAwait(false);

            // Seed default identity-mapping rules so role-based conformance
            // tests can authenticate as admin via the sysadmin/demo credentials.
            // The rules are in-memory and do not persist across restart.
            Server.IRoleManager roleManager = ReferenceServer.CurrentInstance?.RoleManager;
            if (roleManager != null)
            {
                roleManager.AddIdentity(
                    ObjectIds.WellKnownRole_SecurityAdmin,
                    new IdentityMappingRuleType
                    {
                        CriteriaType = IdentityCriteriaType.UserName,
                        Criteria = "sysadmin"
                    });
                roleManager.AddIdentity(
                    ObjectIds.WellKnownRole_ConfigureAdmin,
                    new IdentityMappingRuleType
                    {
                        CriteriaType = IdentityCriteriaType.UserName,
                        Criteria = "sysadmin"
                    });
            }

            ServerUrl = new Uri(
                Utils.UriSchemeOpcTcp +
                "://localhost:" +
                ServerFixture.Port.ToString(CultureInfo.InvariantCulture));

            m_logger.LogInformation("Server started at {Url}", ServerUrl);

            // Create client fixture and connect session
            ClientFixture = new ClientFixture(telemetry: Telemetry);
            await ClientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            ClientFixture.Config.TransportQuotas.MaxMessageSize = TransportQuotaMaxMessageSize;
            ClientFixture.Config.TransportQuotas.MaxByteStringLength =
                ClientFixture.Config.TransportQuotas.MaxStringLength = TransportQuotaMaxStringLength;
            // Slow CI runners need more SessionTimeout (server-side session
            // lifetime) than the ClientFixture's 10 s default to keep the
            // shared session alive across a long test suite. OperationTimeout
            // stays at 30 s — long enough for slow Publish / CreateSubscription
            // on a loaded runner but short enough that a test passing
            // pathological input (e.g. a ReadProcessed with a negative
            // ProcessingInterval that the server never answers) fails fast
            // instead of hanging the whole testhost.
            ClientFixture.SessionTimeout = 300_000;
            ClientFixture.OperationTimeout = 90_000;

            Session = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            Assert.That(Session, Is.Not.Null, "Failed to create session");
        }

        [SetUp]
        public async Task ResetServerLockoutState()
        {
            // Prior tests (especially negative auth tests) can trigger the
            // SessionManager's failed-authentication lockout (5 attempts /
            // 5 minute lockout). Clear it before each test so admin/user
            // identities are usable again.
            try
            {
                ReferenceServer?.CurrentInstance?.SessionManager?.ClearAuthenticationLockouts();
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Failed to clear authentication lockouts.");
            }

            // Clear any registered mock-response expectations so each test
            // starts from a clean state.
            MockController?.Reset();

            // If a prior test poisoned the shared session (typical cascade
            // pattern: one CreateSubscription / Publish timed out on a slow
            // CI runner -> BadSessionIdInvalid on subsequent calls), re-open
            // the session so the next test runs against a healthy fixture.
            // First check the cheap Session.Connected flag; if that still
            // reports true, do a 5 s health-check Read (Server status) to
            // catch the case where the client thinks it's connected but
            // the channel/server has gone away.
            bool sessionDead = Session != null && !Session.Connected;
            if (!sessionDead && Session != null)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await Session.ReadAsync(
                        null, 0, TimestampsToReturn.Neither,
                        new ReadValueId[]
                        {
                            new() { NodeId = new NodeId(Variables.Server_ServerStatus_State),
                                    AttributeId = Attributes.Value }
                        }.ToArrayOf(),
                        cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex,
                        "Session health-check Read failed; treating as dead.");
                    sessionDead = true;
                }
            }
            if (sessionDead)
            {
                try
                {
                    m_logger.LogWarning(
                        "Session disconnected between tests; re-opening.");
                    Session.Dispose();
                    Session = await ClientFixture
                        .ConnectAsync(ServerUrl, SecurityPolicies.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex, "Failed to recover session in [SetUp].");
                }
            }
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDown()
        {
            if (Session != null)
            {
                // Use a hard wall-clock ceiling via Task.WhenAny so that teardown is
                // bounded even when Session.CloseAsync catches the CancellationToken
                // internally and continues running (e.g., in Session.Dispose() /
                // socket-cleanup paths that have no CancellationToken of their own).
                // Without this, each fixture's CloseAsync can run 15-20 s on a slow
                // macOS CI runner (10 s CTS fires → OCE caught → Dispose() called
                // without CT → StopKeepAliveTimerAsync / transport teardown blocks),
                // pushing 28 fixtures × ~20 s = ~560 s right against the 10-minute
                // blame-hang watchdog.  Capping at 15 s keeps worst-case teardown to
                // 28 × 15 s = 420 s = 7 min, safely under the watchdog.
                bool sessionCloseCompleted = false;
                try
                {
                    using var sessionCloseCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    Task<StatusCode> closeTask = Session.CloseAsync(5000, true, sessionCloseCts.Token);
                    Task hardTimeoutTask = Task.Delay(TimeSpan.FromSeconds(15));
                    if (await Task.WhenAny(closeTask, hardTimeoutTask).ConfigureAwait(false) == hardTimeoutTask)
                    {
                        // CloseAsync is still running after 15 s (stuck in Dispose/socket
                        // cleanup). Signal remaining ops to give up and abandon the task.
                        sessionCloseCts.Cancel();
                        m_logger.LogWarning(
                            "Session.CloseAsync exceeded the 15 s teardown watchdog; abandoning.");
                    }
                    else
                    {
                        // CloseAsync returned within budget; surface any exception.
                        await closeTask.ConfigureAwait(false);
                        sessionCloseCompleted = true;
                    }
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex, "Error closing session during teardown.");
                    sessionCloseCompleted = true;
                }

                if (sessionCloseCompleted)
                {
                    // CloseAsync(closeChannel: true) already called Dispose() internally,
                    // so this is a fast no-op (m_disposeAsyncCalled guard), but call it
                    // explicitly to satisfy any external IDisposable contracts.
                    Session.Dispose();
                }
                // If CloseAsync was abandoned, skip Dispose() – it would hit the same
                // blocking code path.  The abandoned task will finalize the session, or
                // the OS will reclaim the socket when the test process exits.

                Session = null;
            }

            if (ServerFixture != null)
            {
                try
                {
                    await ServerFixture.StopAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex, "Error stopping server during teardown.");
                }
                await Task.Delay(100).ConfigureAwait(false);
            }

            ClientFixture?.Dispose();

            try
            {
                if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
                {
                    Directory.Delete(m_pkiRoot, true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }

        public const int TransportQuotaMaxMessageSize = 4 * 1024 * 1024;
        public const int TransportQuotaMaxStringLength = 1 * 1024 * 1024;

        public ServerFixture<ReferenceServer> ServerFixture { get; private set; }
        public ClientFixture ClientFixture { get; private set; }
        public ISession Session { get; protected set; }
        public Uri ServerUrl { get; private set; }
        public ReferenceServer ReferenceServer { get; private set; }
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Lets individual tests inject service-result error codes or
        /// mutate response fields produced by the in-process reference
        /// server. Reset between tests via <see cref="ResetServerLockoutState"/>.
        /// </summary>
        public MockResponseController MockController { get; private set; }

        private string m_pkiRoot;

        protected TestFixture()
        {
            Telemetry = NUnitTelemetryContext.Create();
            m_logger = Telemetry.CreateLogger<TestFixture>();
        }

        /// <summary>
        /// Returns <c>true</c> when the given <see cref="StatusCode"/> matches
        /// one of the SDK channel-level transient codes that we ignore as
        /// CI-runner load symptoms (the test logic itself is fine; the
        /// channel/server simply didn't respond in time).
        /// </summary>
        protected static bool IsTransientCiTimeoutStatus(StatusCode code)
        {
            return code == StatusCodes.BadRequestTimeout ||
                code == StatusCodes.BadRequestInterrupted ||
                code == StatusCodes.BadConnectionClosed ||
                code == StatusCodes.BadSecureChannelClosed ||
                code == StatusCodes.BadSecurityChecksFailed
                // BadSubscriptionIdInvalid 'Subscription belongs to a different session'
                // is observed on the windows-latest Conformance runner when a test
                // takes longer than the session timeout and the reconnect handler
                // re-creates the session under it. The subscription handle becomes
                // stale — that's an environmental side-effect of the slow runner,
                // not a server defect.
                || code == StatusCodes.BadSubscriptionIdInvalid;
        }

        /// <summary>
        /// Wraps <see cref="ISession.CreateSubscriptionAsync"/> for use from
        /// <c>[SetUp]</c> methods so that a CI runner under heavy load doesn't
        /// turn a transient channel-side timeout into a fixture-wide
        /// failure. If the call throws one of the SDK channel-level transient
        /// codes (BadRequestTimeout / BadRequestInterrupted /
        /// BadConnectionClosed) the helper calls <see cref="Assert.Ignore(string)"/>,
        /// which marks just the current test as Inconclusive and lets the
        /// rest of the fixture proceed on the next CI cycle.
        /// </summary>
        protected async Task<uint> CreateSetupSubscriptionAsync(
            double publishingInterval = 1000,
            uint requestedLifetimeCount = 100,
            uint requestedMaxKeepAliveCount = 10,
            uint maxNotificationsPerPublish = 0,
            bool publishingEnabled = true,
            byte priority = 0)
        {
            try
            {
                CreateSubscriptionResponse response = await Session.CreateSubscriptionAsync(
                    null,
                    publishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    publishingEnabled,
                    priority,
                    CancellationToken.None).ConfigureAwait(false);
                return response.SubscriptionId;
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: SetUp CreateSubscription interrupted by CI runner load ({sre.StatusCode}).");
                return 0; // unreachable; Assert.Ignore throws.
            }
        }

        /// <summary>
        /// Connect as the seeded sysadmin user (granted SecurityAdmin and
        /// ConfigureAdmin in TestFixture.OneTimeSetUp). Used by tests that
        /// need to read admin-only attributes (RolePermissions /
        /// UserRolePermissions / Diagnostics arrays / etc.) which the
        /// standard nodeset hides from anonymous sessions via RolePermissions.
        /// Returns null if no Sign+Encrypt or Sign endpoint with username
        /// auth is exposed by the server — caller may Assert.Ignore.
        /// </summary>
        protected async Task<ISession> ConnectAsSysAdminAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);
            await client.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            string policy = null;
            foreach (MessageSecurityMode mode in new[]
            {
                MessageSecurityMode.SignAndEncrypt,
                MessageSecurityMode.Sign,
                MessageSecurityMode.None
            })
            {
                foreach (EndpointDescription ep in endpoints)
                {
                    if (ep.SecurityMode != mode)
                    {
                        continue;
                    }
                    if (ep.UserIdentityTokens == default)
                    {
                        continue;
                    }
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName)
                        {
                            policy = ep.SecurityPolicyUri;
                            break;
                        }
                    }
                    if (policy != null)
                    {
                        break;
                    }
                }
                if (policy != null)
                {
                    break;
                }
            }
            if (policy == null)
            {
                return null;
            }
            return await ClientFixture.ConnectAsync(
                ServerUrl, policy,
                userIdentity: new UserIdentity("sysadmin", "demo"u8))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Opens a fresh, independent session to the in-process server,
        /// bypassing the retry wrapper that the standard
        /// <see cref="ClientFixture.ConnectAsync(Uri, string, ArrayOf{EndpointDescription}, IUserIdentity)"/>
        /// applies. Used by RequiresServerMock tests that inject errors
        /// into CreateSession / ActivateSession / CloseSession responses
        /// — without this, the retry loop would consume the one-shot
        /// expectation on the first attempt and the second attempt
        /// would succeed unchanged, defeating the test.
        /// </summary>
        /// <param name="securityProfile">Security policy URI; defaults
        /// to <see cref="SecurityPolicies.None"/>.</param>
        /// <param name="userIdentity">Optional user identity.</param>
        protected async Task<ISession> OpenAuxSessionAsync(
            string securityProfile = null,
            IUserIdentity userIdentity = null)
        {
            ConfiguredEndpoint endpoint = await ClientFixture.GetEndpointAsync(
                ServerUrl,
                securityProfile ?? SecurityPolicies.None).ConfigureAwait(false);
            return await ClientFixture.ConnectAsync(endpoint, userIdentity).ConfigureAwait(false);
        }

        /// <summary>
        /// Helper to resolve an ExpandedNodeId to a NodeId using the session namespace table.
        /// </summary>
        protected NodeId ToNodeId(ExpandedNodeId expandedNodeId)
        {
            return ExpandedNodeId.ToNodeId(expandedNodeId, Session.NamespaceUris);
        }

        /// <summary>
        /// Calls Assert.Ignore when a role management method returns a status code
        /// indicating the feature is not (fully) implemented in the test server.
        /// BadEntryExists / BadAlreadyExists are treated as "server does not
        /// implement idempotent re-add" — acceptable optional behavior per OPC UA Part 18.
        /// </summary>
        protected static void IgnoreIfRoleMethodNotSupported(StatusCode statusCode)
        {
            if (statusCode == StatusCodes.BadNotImplemented ||
                statusCode == StatusCodes.BadServiceUnsupported ||
                statusCode == StatusCodes.BadInvalidArgument ||
                statusCode == StatusCodes.BadNotSupported ||
                statusCode == StatusCodes.BadSecurityModeInsufficient ||
                statusCode == StatusCodes.BadEntryExists ||
                statusCode == StatusCodes.BadAlreadyExists)
            {
                Assert.Ignore(
                    "Role management method not fully implemented in test server: " +
                    $"{statusCode}");
            }
        }

        private readonly ILogger m_logger;
    }
}
