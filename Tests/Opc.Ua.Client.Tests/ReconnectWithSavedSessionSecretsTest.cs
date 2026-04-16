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
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Tests that reconnect a session on a new channel using saved session secrets.
    /// Each fixture instance handles one security policy. The eight boolean combinations of
    /// anonymous × sequentialPublishing × sendInitialValues run in parallel within each
    /// fixture instance, while the fixture instances themselves also run in parallel.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ReconnectWithSavedSessionSecrets")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [TestFixtureSource(nameof(SecurityPolicyArgs))]
    [Parallelizable(ParallelScope.Fixtures)]
    public class ReconnectWithSavedSessionSecretsTest : ClientTestFramework
    {
        /// <summary>
        /// Fixture argument list: one fixture instance per security policy.
        /// </summary>
        public static readonly object[] SecurityPolicyArgs =
        [
            new object[] { SecurityPolicies.None },
            new object[] { SecurityPolicies.ECC_nistP256 },
            new object[] { SecurityPolicies.Basic256Sha256 },
            new object[] { SecurityPolicies.RSA_DH_AesGcm },
            new object[] { SecurityPolicies.RSA_DH_ChaChaPoly }
        ];

        private readonly string m_securityPolicy;

        /// <summary>
        /// Initializes a new fixture instance for the given <paramref name="securityPolicy"/>.
        /// </summary>
        public ReconnectWithSavedSessionSecretsTest(string securityPolicy)
            : base(Utils.UriSchemeOpcTcp)
        {
            m_securityPolicy = securityPolicy;
        }

        /// <summary>
        /// Start one server per fixture instance and configure a TestableSessionFactory.
        /// Tests create their own sessions independently.
        /// </summary>
        [OneTimeSetUp]
        public override async Task OneTimeSetUpAsync()
        {
            // Use a shared session for namespace URI lookups; tests create their own sessions.
            SingleSession = true;
            MaxChannelCount = 1000;
            await OneTimeSetUpCoreAsync(securityNone: true).ConfigureAwait(false);

            // Set the session factory once; all parallel tests within this fixture use it.
            ClientFixture.SessionFactory = new TestableSessionFactory(Telemetry);
        }

        /// <inheritdoc/>
        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <inheritdoc/>
        [SetUp]
        public override Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        /// <inheritdoc/>
        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        /// <summary>
        /// Reconnect a session using saved session secrets for all combinations of
        /// <paramref name="anonymous"/>, <paramref name="sequentialPublishing"/>, and
        /// <paramref name="sendInitialValues"/>. Each combination runs in parallel with the
        /// others within this fixture and also across the three security-policy instances.
        /// </summary>
        [Test]
        [Combinatorial]
        [Order(352)]
        [Parallelizable]
        public Task ReconnectWithSavedSessionSecretsAsync(
            [Values] bool anonymous,
            [Values] bool sequentialPublishing,
            [Values] bool sendInitialValues)
        {
            return ReconnectWithSavedSessionSecretsCoreAsync(
                m_securityPolicy,
                anonymous,
                sequentialPublishing,
                sendInitialValues);
        }

        /// <summary>
        /// ECC variant of the reconnect test (explicit — not run in normal CI).
        /// </summary>
        [Test]
        [Combinatorial]
        [Order(351)]
        [Explicit]
        [Parallelizable]
        public Task ReconnectWithSavedSessionSecretsECCAsync(
            [Values(
                SecurityPolicies.ECC_nistP256,
                SecurityPolicies.ECC_nistP384,
                SecurityPolicies.ECC_brainpoolP256r1,
                SecurityPolicies.ECC_brainpoolP384r1
            )]
                string securityPolicy,
            [Values] bool anonymous,
            [Values] bool sequentialPublishing,
            [Values] bool sendInitialValues)
        {
            return ReconnectWithSavedSessionSecretsCoreAsync(
                securityPolicy,
                anonymous,
                sequentialPublishing,
                sendInitialValues);
        }

        private async Task ReconnectWithSavedSessionSecretsCoreAsync(
            string securityPolicy,
            bool anonymous,
            bool sequentialPublishing,
            bool sendInitialValues)
        {
            await IgnoreIfPolicyNotAdvertisedAsync(securityPolicy).ConfigureAwait(false);

            const int kTestSubscriptions = 5;
            const int kDelay = 2_000;
            const int kQueueSize = 10;

            ServiceResultException sre;
            UserIdentity userIdentity = anonymous
                ? new UserIdentity()
                : new UserIdentity("user1", "password"u8);

            // the first channel determines the endpoint
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, securityPolicy, Endpoints)
                .ConfigureAwait(false);
            Assert.That(endpoint, Is.Not.Null);

            UserTokenPolicy identityPolicy = endpoint.Description.FindUserTokenPolicy(
                userIdentity.TokenType,
                userIdentity.IssuedTokenType,
                endpoint.Description.SecurityPolicyUri);
            if (identityPolicy == null)
            {
                Assert.Ignore(
                    $"No UserTokenPolicy found for {userIdentity.TokenType} / {userIdentity.IssuedTokenType}");
            }

            // the active channel
            ISession session1 = await ClientFixture.ConnectAsync(endpoint, userIdentity)
                .ConfigureAwait(false);
            Assert.That(session1, Is.Not.Null);
            NodeId sessionId1 = session1.SessionId;

            int session1ConfigChanged = 0;
            session1.SessionConfigurationChanged += (sender, e) => session1ConfigChanged++;

            ServerStatusDataType value1 = await session1.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            Assert.That(value1, Is.Not.Null);

            var originSubscriptions = new SubscriptionCollection(kTestSubscriptions);
            int[] originSubscriptionCounters = new int[kTestSubscriptions];
            int[] originSubscriptionFastDataCounters = new int[kTestSubscriptions];
            int[] targetSubscriptionCounters = new int[kTestSubscriptions];
            int[] targetSubscriptionFastDataCounters = new int[kTestSubscriptions];
            var subscriptionTemplate = new TestableSubscription(session1.DefaultSubscription)
            {
                PublishingInterval = 1_000,
                KeepAliveCount = 5,
                PublishingEnabled = true,
                RepublishAfterTransfer = true,
                SequentialPublishing = sequentialPublishing
            };

            await CreateSubscriptionsAsync(
                session1,
                subscriptionTemplate,
                originSubscriptions,
                originSubscriptionCounters,
                originSubscriptionFastDataCounters,
                kTestSubscriptions,
                kQueueSize).ConfigureAwait(false);

            // wait
            await Task.Delay(kDelay).ConfigureAwait(false);

            // save the session configuration
            var configStream = new MemoryStream();
            session1.SaveSessionConfiguration(configStream);

            byte[] configStreamArray = configStream.ToArray();
            TestContext.Out.WriteLine($"SessionSecrets: {configStream.Length} bytes");
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(configStreamArray));

            var subscriptionStream = new MemoryStream();
            session1.Save(
                subscriptionStream,
                session1.Subscriptions);

            byte[] subscriptionStreamArray = subscriptionStream.ToArray();
            TestContext.Out.WriteLine($"Subscriptions: {subscriptionStreamArray.Length} bytes");
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(subscriptionStreamArray));

            // read the session configuration
            var loadConfigurationStream = new MemoryStream(configStreamArray);
            var sessionConfiguration = SessionConfiguration.Create(
                loadConfigurationStream,
                Telemetry);

            // create the inactive channel
            ITransportChannel channel2 = await ClientFixture
                .CreateChannelAsync(sessionConfiguration.ConfiguredEndpoint, false)
                .ConfigureAwait(false);
            Assert.That(channel2, Is.Not.Null);

            // prepare the inactive session with the new channel
            ISession session2 = ClientFixture.CreateSession(
                channel2,
                sessionConfiguration.ConfiguredEndpoint);

            int session2ConfigChanged = 0;
            session2.SessionConfigurationChanged += (sender, e) => session2ConfigChanged++;

            // apply the saved session configuration
            bool success = session2.ApplySessionConfiguration(sessionConfiguration);

            // restore the subscriptions
            var loadSubscriptionStream = new MemoryStream(subscriptionStreamArray);
            var restoredSubscriptions = new SubscriptionCollection(
                session2.Load(loadSubscriptionStream, true, [typeof(SubscriptionState)]));

            // hook notifications for log output
            int ii = 0;
            foreach (Subscription subscription in restoredSubscriptions)
            {
                subscription.Handle = ii;
                subscription.FastDataChangeCallback = (s, n, _) =>
                {
                    TestContext.Out.WriteLine(
                        $"FastDataChangeHandlerTarget: {s.Id}-{n.SequenceNumber}-{n.MonitoredItems.Count}");
                    targetSubscriptionFastDataCounters[(int)subscription.Handle]++;
                };
                subscription
                    .MonitoredItems.ToList()
                    .ForEach(i =>
                        i.Notification += (item, _) =>
                        {
                            targetSubscriptionCounters[(int)subscription.Handle]++;
                            foreach (DataValue value in item.DequeueValues())
                            {
                                TestContext.Out.WriteLine(
                                    "Tra:{0}: {1:20}, {2}, {3}, {4}",
                                    subscription.Id,
                                    item.DisplayName,
                                    value.WrappedValue,
                                    value.SourceTimestamp,
                                    value.StatusCode);
                            }
                        });
                ii++;
            }

            // hook callback to renew the user identity
            session2.RenewUserIdentity += (_, _) => userIdentity;

            // activate the session from saved session secrets on the new channel
            await session2.ReconnectAsync(channel2).ConfigureAwait(false);

            // reactivate restored subscriptions
            bool reactivateResult = await session2
                .ReactivateSubscriptionsAsync(restoredSubscriptions, sendInitialValues)
                .ConfigureAwait(false);
            Assert.That(reactivateResult, Is.True);

            await Task.Delay(2 * kDelay).ConfigureAwait(false);

            try
            {
                Assert.That(session2.SessionId, Is.EqualTo(sessionId1));

                DataValue value2 = await session2
                    .ReadValueAsync(VariableIds.Server_ServerStatus)
                    .ConfigureAwait(false);
                Assert.That(value2, Is.Not.Null);

                for (ii = 0; ii < kTestSubscriptions; ii++)
                {
                    uint monitoredItemCount = restoredSubscriptions[ii].MonitoredItemCount;
                    string errorText = $"Error in test subscription {ii}";

                    // the static subscription doesn't resend data until there is a data change
                    if (ii == 0 && !sendInitialValues)
                    {
                        Assert.That(targetSubscriptionCounters[ii], Is.Zero, errorText);
                        Assert.That(targetSubscriptionFastDataCounters[ii], Is.Zero, errorText);
                    }
                    else if (ii == 0)
                    {
                        Assert.That(
                            targetSubscriptionCounters[ii],
                            Is.EqualTo(monitoredItemCount),
                            errorText);
                        Assert.That(targetSubscriptionFastDataCounters[ii], Is.EqualTo(1), errorText);
                    }
                    else
                    {
                        Assert.That(
                            targetSubscriptionCounters[ii],
                            Is.GreaterThanOrEqualTo(monitoredItemCount),
                            errorText);
                        Assert.That(targetSubscriptionFastDataCounters[ii], Is.GreaterThanOrEqualTo(1), errorText);
                    }
                }

                await Task.Delay(kDelay).ConfigureAwait(false);

                // verify that reconnect created subclassed version of subscription and monitored item
                foreach (Subscription s in session2.Subscriptions)
                {
                    Assert.That(s.GetType(), Is.EqualTo(typeof(TestableSubscription)));
                    foreach (MonitoredItem m in s.MonitoredItems)
                    {
                        Assert.That(m.GetType(), Is.EqualTo(typeof(TestableMonitoredItem)));
                    }
                }

                // cannot read using a closed channel, validate the status code
                if (endpoint.EndpointUrl.ToString()
                    .StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
                {
                    sre = Assert.ThrowsAsync<ServiceResultException>(() =>
                        session1.ReadValueAsync<ServerStatusDataType>(
                            VariableIds.Server_ServerStatus));
                    Assert.That(
                        sre.StatusCode,
                        Is.EqualTo(StatusCodes.BadSecureChannelIdInvalid),
                        sre.Message);
                }
                else
                {
                    ServerStatusDataType result =
                        await session1.ReadValueAsync<ServerStatusDataType>(
                            VariableIds.Server_ServerStatus).ConfigureAwait(false);
                    Assert.That(result, Is.Not.Null);
                }
            }
            finally
            {
                session1.DeleteSubscriptionsOnClose = true;
                session2.DeleteSubscriptionsOnClose = true;
                await session1.CloseAsync(1000, true).ConfigureAwait(false);
                await session2.CloseAsync(1000, true).ConfigureAwait(false);
                Utils.SilentDispose(session1);
                Utils.SilentDispose(session2);
            }

            Assert.That(session1ConfigChanged, Is.Zero);
            Assert.That(session2ConfigChanged, Is.GreaterThanOrEqualTo(0));
        }
    }
}
