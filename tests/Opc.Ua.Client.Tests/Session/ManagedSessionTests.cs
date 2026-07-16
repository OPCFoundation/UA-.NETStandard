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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Tests;

#pragma warning disable CA2000

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Unit tests for ManagedSession components including
    /// <see cref="ReconnectPolicy"/>, <see cref="ConnectionState"/>,
    /// <see cref="ConnectionStateMachine"/>, and
    /// <see cref="Client.ManagedSession"/> factory methods.
    /// </summary>
    [TestFixture]
    public sealed class ManagedSessionTests
    {
        [Test]
        public void ExponentialBackoffIncreasesDelay()
        {
            var policy = new ReconnectPolicy
            {
                Strategy = BackoffStrategy.Exponential,
                InitialDelay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromMinutes(10),
                JitterFactor = 0.0
            };

            TimeSpan? delay0 = policy.GetNextDelay(0);
            TimeSpan? delay1 = policy.GetNextDelay(1);
            TimeSpan? delay2 = policy.GetNextDelay(2);

            Assert.That(delay0, Is.Not.Null);
            Assert.That(delay1, Is.Not.Null);
            Assert.That(delay2, Is.Not.Null);

            // Exponential: 1s * 2^0 = 1s, 1s * 2^1 = 2s, 1s * 2^2 = 4s
            Assert.That(delay0.Value.TotalSeconds, Is.EqualTo(1.0));
            Assert.That(delay1.Value.TotalSeconds, Is.EqualTo(2.0));
            Assert.That(delay2.Value.TotalSeconds, Is.EqualTo(4.0));
        }

        [Test]
        public void LinearBackoffIncreasesLinearly()
        {
            var policy = new ReconnectPolicy
            {
                Strategy = BackoffStrategy.Linear,
                InitialDelay = TimeSpan.FromSeconds(2),
                MaxDelay = TimeSpan.FromMinutes(10),
                JitterFactor = 0.0
            };

            TimeSpan? delay0 = policy.GetNextDelay(0);
            TimeSpan? delay1 = policy.GetNextDelay(1);
            TimeSpan? delay2 = policy.GetNextDelay(2);

            Assert.That(delay0, Is.Not.Null);
            Assert.That(delay1, Is.Not.Null);
            Assert.That(delay2, Is.Not.Null);

            // Linear: 2s * (0+1) = 2s, 2s * (1+1) = 4s, 2s * (2+1) = 6s
            Assert.That(delay0.Value.TotalSeconds, Is.EqualTo(2.0));
            Assert.That(delay1.Value.TotalSeconds, Is.EqualTo(4.0));
            Assert.That(delay2.Value.TotalSeconds, Is.EqualTo(6.0));
        }

        [Test]
        public void ConstantBackoffReturnsSameDelay()
        {
            var policy = new ReconnectPolicy
            {
                Strategy = BackoffStrategy.Constant,
                InitialDelay = TimeSpan.FromSeconds(5),
                JitterFactor = 0.0
            };

            TimeSpan? delay0 = policy.GetNextDelay(0);
            TimeSpan? delay1 = policy.GetNextDelay(1);
            TimeSpan? delay2 = policy.GetNextDelay(5);

            Assert.That(delay0, Is.Not.Null);
            Assert.That(delay1, Is.Not.Null);
            Assert.That(delay2, Is.Not.Null);

            Assert.That(delay0.Value.TotalSeconds, Is.EqualTo(5.0));
            Assert.That(delay1.Value.TotalSeconds, Is.EqualTo(5.0));
            Assert.That(delay2.Value.TotalSeconds, Is.EqualTo(5.0));
        }

        [Test]
        public void MaxDelayIsCapped()
        {
            var policy = new ReconnectPolicy
            {
                Strategy = BackoffStrategy.Exponential,
                InitialDelay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(10),
                JitterFactor = 0.0
            };

            // Attempt 10: 1s * 2^10 = 1024s, should be capped to 10s
            TimeSpan? delay = policy.GetNextDelay(10);

            Assert.That(delay, Is.Not.Null);
            Assert.That(
                delay.Value.TotalSeconds,
                Is.EqualTo(10.0));
        }

        [Test]
        public void MaxRetriesStopsAfterLimit()
        {
            var policy = new ReconnectPolicy
            {
                Strategy = BackoffStrategy.Constant,
                InitialDelay = TimeSpan.FromSeconds(1),
                MaxRetries = 3,
                JitterFactor = 0.0
            };

            Assert.That(policy.GetNextDelay(0), Is.Not.Null);
            Assert.That(policy.GetNextDelay(1), Is.Not.Null);
            Assert.That(policy.GetNextDelay(2), Is.Not.Null);

            // attempt >= MaxRetries => null
            Assert.That(policy.GetNextDelay(3), Is.Null);
            Assert.That(policy.GetNextDelay(4), Is.Null);
        }

        [Test]
        public void UnlimitedRetriesNeverReturnsNull()
        {
            var policy = new ReconnectPolicy
            {
                Strategy = BackoffStrategy.Constant,
                InitialDelay = TimeSpan.FromSeconds(1),
                MaxRetries = 0,
                JitterFactor = 0.0
            };

            for (int i = 0; i < 100; i++)
            {
                Assert.That(
                    policy.GetNextDelay(i),
                    Is.Not.Null,
                    $"GetNextDelay returned null at attempt {i}");
            }
        }

        [Test]
        public void JitterAppliesVariation()
        {
            var policy = new ReconnectPolicy
            {
                Strategy = BackoffStrategy.Constant,
                InitialDelay = TimeSpan.FromSeconds(10),
                MaxDelay = TimeSpan.FromSeconds(30),
                JitterFactor = 0.5
            };

            var delays = new HashSet<double>();
            for (int i = 0; i < 50; i++)
            {
                TimeSpan? delay = policy.GetNextDelay(0);
                Assert.That(delay, Is.Not.Null);
                delays.Add(delay.Value.TotalMilliseconds);
            }

            // With 50% jitter over 50 iterations we expect variation
            Assert.That(delays, Has.Count.GreaterThan(1),
                "Jitter should produce varying delays");
        }

        [Test]
        public async Task ManagedSessionPropagatesBudgetToChannelManagerAsync()
        {
#if !NETSTANDARD2_1 && !NET8_0_OR_GREATER
            Assert.Ignore(
                "IClientChannelManager.ReconnectAsync(channel, budget, ct) is only available on net8.0+/netstandard2.1.");
#else
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ApplicationConfiguration configuration = CreateClientConfiguration(telemetry);
            ConfiguredEndpoint endpoint = CreateEndpoint();
            IServiceMessageContext messageContext = configuration.CreateMessageContext();

            var managedChannel = new Mock<IManagedTransportChannel>();
            managedChannel.SetupGet(c => c.MessageContext).Returns(messageContext);

            IRetryBudget? capturedBudget = null;
            var channelManager = new Mock<IClientChannelManager>();
            channelManager.Setup(m => m.ReconnectAsync(
                    managedChannel.Object,
                    It.IsAny<IRetryBudget>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IManagedTransportChannel, IRetryBudget, CancellationToken>(
                    (_, budget, _) => capturedBudget = budget)
                .Returns(new ValueTask());

            using var innerSession = new Session(
                managedChannel.Object,
                configuration,
                endpoint,
                engineFactory: DefaultSubscriptionEngineFactory.Instance);
            innerSession.BindManagedChannel(channelManager.Object, managedChannel.Object);

            using Client.ManagedSession managedSession = CreateManagedSessionWithInner(
                configuration,
                endpoint,
                innerSession,
                telemetry);
            var budget = new RetryBudget(TimeSpan.FromSeconds(30));

            ServiceResult result = await InvokeHandleReconnectAsync(
                    managedSession,
                    budget)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(capturedBudget, Is.SameAs(budget));
#endif
        }

        [Test]
        public async Task HandleReconnectAsyncUsesAlternateNetworkEndpointAfterBadSecureChannelClosedAsync()
        {
#if !NETSTANDARD2_1 && !NET8_0_OR_GREATER
            Assert.Ignore(
                "IClientChannelManager.ReconnectAsync(channel, budget, ct) is only available on net8.0+/netstandard2.1.");
#else
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ApplicationConfiguration configuration = CreateClientConfiguration(telemetry);
            ConfiguredEndpoint primaryEndpoint = CreateEndpoint();
            ConfiguredEndpoint alternateEndpoint = CreateEndpoint();
            alternateEndpoint.Description.EndpointUrl = "opc.tcp://alternate:4840";
            IServiceMessageContext messageContext = configuration.CreateMessageContext();

            var managedChannel = new Mock<IManagedTransportChannel>();
            managedChannel.SetupGet(c => c.MessageContext).Returns(messageContext);
            managedChannel.SetupGet(c => c.State).Returns(ChannelState.Closed);

            ConfiguredEndpoint? capturedEndpoint = null;
            var channelManager = new Mock<IClientChannelManager>();
            channelManager.Setup(m => m.ReconnectAsync(
                    managedChannel.Object,
                    It.IsAny<IRetryBudget>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadSecureChannelClosed));
            channelManager.Setup(m => m.GetAsync(
                    It.IsAny<ConfiguredEndpoint>(),
                    It.IsAny<Func<IManagedTransportChannel, IReconnectParticipant>>(),
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<CancellationToken>()))
                .Callback<ConfiguredEndpoint, Func<IManagedTransportChannel, IReconnectParticipant>, ITransportWaitingConnection?, CancellationToken>(
                    (endpoint, _, _, _) => capturedEndpoint = endpoint)
                .Throws(new ServiceResultException(StatusCodes.BadUnexpectedError));

            using var innerSession = new Session(
                managedChannel.Object,
                configuration,
                primaryEndpoint,
                engineFactory: DefaultSubscriptionEngineFactory.Instance);
            innerSession.BindManagedChannel(channelManager.Object, managedChannel.Object);

            using Client.ManagedSession managedSession = CreateManagedSessionWithInner(
                configuration,
                primaryEndpoint,
                innerSession,
                telemetry,
                new NetworkRedundancyOptions
                {
                    AlternateEndpoints = new ArrayOf<ConfiguredEndpoint>(new[] { alternateEndpoint }.AsMemory())
                });
            var budget = new RetryBudget(TimeSpan.FromSeconds(30));

            ServiceResult result = await InvokeHandleReconnectAsync(
                    managedSession,
                    budget)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsBad(result), Is.True);
            Assert.That(capturedEndpoint, Is.SameAs(alternateEndpoint));
#endif
        }

        [Test]
        public void ConnectionStateChangedEventArgsHasCorrectProperties()
        {
            var error = new ServiceResult(StatusCodes.BadCommunicationError);
            var args = new ConnectionStateChangedEventArgs
            {
                PreviousState = ConnectionState.Connected,
                NewState = ConnectionState.Reconnecting,
                Error = error,
                ReconnectAttempt = 3
            };

            Assert.That(args.PreviousState, Is.EqualTo(ConnectionState.Connected));
            Assert.That(args.NewState, Is.EqualTo(ConnectionState.Reconnecting));
            Assert.That(args.Error, Is.Not.Null);
            Assert.That(args.Error.StatusCode, Is.EqualTo(StatusCodes.BadCommunicationError));
            Assert.That(args.ReconnectAttempt, Is.EqualTo(3));
        }

        [Test]
        public void InitialStateIsDisconnected()
        {
            var policy = new ReconnectPolicy();
            ILogger logger = NUnitTelemetryContext.Create().CreateLogger("ManagedSession");

            using var cts = new CancellationTokenSource();
            var sm = new ConnectionStateMachine(policy, logger);

            Assert.That(sm.State, Is.EqualTo(ConnectionState.Disconnected));
        }

        [Test]
        public async Task DisposeTransitionsToClosedState()
        {
            var policy = new ReconnectPolicy();
            ILogger logger = NUnitTelemetryContext.Create().CreateLogger("ManagedSession");

            var sm = new ConnectionStateMachine(policy, logger);
            sm.Start();

            await sm.DisposeAsync().ConfigureAwait(false);

            Assert.That(sm.State, Is.EqualTo(ConnectionState.Closed));
        }

        [Test]
        public void CreateAsyncThrowsOnNullConfiguration()
        {
            var mockFactory = new Mock<ISessionFactory>();
            mockFactory.Setup(f => f.Telemetry)
                .Returns(new Mock<ITelemetryContext>().Object);

            var endpoint = new ConfiguredEndpoint(
                null,
                new EndpointDescription("opc.tcp://localhost:4840"));

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await Client.ManagedSession.CreateAsync(
                    configuration: null,
                    endpoint: endpoint,
                    sessionFactory: mockFactory.Object).ConfigureAwait(false));
        }

        [Test]
        public void CreateAsyncThrowsOnNullEndpoint()
        {
            var mockFactory = new Mock<ISessionFactory>();
            mockFactory.Setup(f => f.Telemetry)
                .Returns(new Mock<ITelemetryContext>().Object);

            var config = new ApplicationConfiguration
            {
                ApplicationName = "TestApp",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier()
                }
            };

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await Client.ManagedSession.CreateAsync(
                    configuration: config,
                    endpoint: null,
                    sessionFactory: mockFactory.Object).ConfigureAwait(false));
        }

        [Test]
        public async Task CreateAsyncDisposesManagedSessionWhenInitialWaitIsCanceledAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ApplicationConfiguration configuration = CreateClientConfiguration(telemetry);
            ConfiguredEndpoint endpoint = CreateEndpoint();
            IServiceMessageContext messageContext = configuration.CreateMessageContext();

            var channel = new Mock<IManagedTransportChannel>();
            channel.SetupGet(c => c.MessageContext).Returns(messageContext);
            var innerSession = new Session(
                channel.Object,
                configuration,
                endpoint,
                engineFactory: DefaultSubscriptionEngineFactory.Instance);

            var sessionFactory = new Mock<ISessionFactory>();
            sessionFactory.SetupGet(f => f.Telemetry).Returns(telemetry);
            sessionFactory.Setup(f => f.CreateAsync(
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<ConfiguredEndpoint>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<string>(),
                    It.IsAny<uint>(),
                    It.IsAny<IUserIdentity?>(),
                    It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISession)innerSession);

            Client.ManagedSession? managedSession = null;
            var fetchStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var fetchCompletion = new TaskCompletionSource<ServerRedundancyInfo>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var redundancyHandler = new Mock<IServerRedundancyHandler>();
            redundancyHandler.Setup(h => h.FetchRedundancyInfoAsync(
                    It.IsAny<ISession>(),
                    It.IsAny<CancellationToken>()))
                .Returns((ISession session, CancellationToken ct) =>
                {
                    managedSession = (Client.ManagedSession)session;
                    fetchStarted.TrySetResult(true);
                    ct.Register(() => fetchCompletion.TrySetCanceled());
                    return new ValueTask<ServerRedundancyInfo>(fetchCompletion.Task);
                });

            using var cancellation = new CancellationTokenSource();
            Task<Client.ManagedSession> createTask = Client.ManagedSession.CreateAsync(
                configuration,
                endpoint,
                sessionFactory.Object,
                redundancyHandler: redundancyHandler.Object,
                ct: cancellation.Token);

            Task startedTask = await Task.WhenAny(
                    fetchStarted.Task,
                    Task.Delay(TimeSpan.FromSeconds(5)))
                .ConfigureAwait(false);
            Assert.That(startedTask, Is.SameAs(fetchStarted.Task));

            cancellation.Cancel();

            Task completedTask = await Task.WhenAny(
                    createTask,
                    Task.Delay(TimeSpan.FromSeconds(5)))
                .ConfigureAwait(false);
            Assert.That(completedTask, Is.SameAs(createTask));
            Assert.CatchAsync<OperationCanceledException>(() => createTask);
            Assert.That(fetchCompletion.Task.IsCanceled, Is.True);
            Assert.That(managedSession, Is.Not.Null);
            Assert.That(
                managedSession!.StateMachine.State,
                Is.EqualTo(ConnectionState.Closed));
            Assert.Throws<ServiceResultException>(() => _ = managedSession.InnerSession);
        }

        [Test]
        public async Task CreateAsyncLogsDisposeFailureAndPreservesCancellationAsync()
        {
            var logger = new Mock<ILogger>();
            logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            var disposeFailure = new InvalidOperationException("Test cleanup failure.");

            // The stack emits source-generated log messages whose state has an empty
            // ToString(); match on the EventId name (the log method name) instead.
#pragma warning disable CA1873 // Moq Setup on ILogger.Log is an expression tree, not an executed logging call.
            logger.Setup(l => l.Log(
                    It.IsAny<LogLevel>(),
                    It.Is<EventId>(eventId => eventId.Name == "ConnectionStateMachineWorkerExiting"),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Throws(disposeFailure);
#pragma warning restore CA1873

            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
                .Returns(logger.Object);
            var telemetry = new Mock<ITelemetryContext>();
            telemetry.SetupGet(t => t.LoggerFactory).Returns(loggerFactory.Object);

            var sessionFactory = new Mock<ISessionFactory>();
            sessionFactory.SetupGet(f => f.Telemetry).Returns(telemetry.Object);
            var connectStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var connectCompletion = new TaskCompletionSource<IDisposable>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var connectGate = new Mock<IClientConnectGate>();
            connectGate.Setup(g => g.AcquireAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken ct) =>
                {
                    connectStarted.TrySetResult(true);
                    ct.Register(() => connectCompletion.TrySetCanceled());
                    return new ValueTask<IDisposable>(connectCompletion.Task);
                });

            using var cancellation = new CancellationTokenSource();
            Task<Client.ManagedSession> createTask = Client.ManagedSession.CreateAsync(
                CreateClientConfiguration(telemetry.Object),
                CreateEndpoint(),
                sessionFactory.Object,
                connectGate: connectGate.Object,
                ct: cancellation.Token);

            Task startedTask = await Task.WhenAny(
                    connectStarted.Task,
                    Task.Delay(TimeSpan.FromSeconds(5)))
                .ConfigureAwait(false);
            Assert.That(startedTask, Is.SameAs(connectStarted.Task));

            cancellation.Cancel();

            Task completedTask = await Task.WhenAny(
                    createTask,
                    Task.Delay(TimeSpan.FromSeconds(5)))
                .ConfigureAwait(false);
            Assert.That(completedTask, Is.SameAs(createTask));
            Assert.CatchAsync<OperationCanceledException>(() => createTask);
#pragma warning disable CA1873 // Moq Verify on ILogger.Log is an expression tree, not an executed logging call.
            logger.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.Is<EventId>(eventId =>
                        eventId.Name == "ManagedSessionDisposalAfterConnectionFailureFailed"),
                    It.IsAny<It.IsAnyType>(),
                    It.Is<Exception?>(exception => ReferenceEquals(exception, disposeFailure)),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
#pragma warning restore CA1873
        }

#pragma warning disable IDE0051, RCS1213 // Test scaffold kept for reconnect-path tests that need the private handler.
        private static Task<ServiceResult> InvokeHandleReconnectAsync(
            Client.ManagedSession managedSession,
            IRetryBudget budget)
        {
            MethodInfo? method = typeof(Client.ManagedSession).GetMethod(
                "HandleReconnectAsync",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                [typeof(IRetryBudget), typeof(CancellationToken)],
                null);

            Assert.That(method, Is.Not.Null);

            var task = (Task<ServiceResult>?)method!.Invoke(
                managedSession,
                [budget, CancellationToken.None]);

            Assert.That(task, Is.Not.Null);
            return task!;
        }
#pragma warning restore IDE0051, RCS1213

#pragma warning disable IDE0051, RCS1213 // Test scaffold kept for tests that inject an already-created inner Session.
        private static Client.ManagedSession CreateManagedSessionWithInner(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            Session innerSession,
            ITelemetryContext telemetry,
            NetworkRedundancyOptions? networkRedundancy = null)
        {
            ILogger<Client.ManagedSession> logger = telemetry.CreateLogger<Client.ManagedSession>();
            var sessionFactory = new Mock<ISessionFactory>();
            sessionFactory.SetupGet(f => f.Telemetry).Returns(telemetry);
            var reconnectPolicy = new ReconnectPolicy(new ReconnectPolicyOptions
            {
                MaxTotalReconnectTime = TimeSpan.FromSeconds(30)
            });

            ConstructorInfo? ctor = typeof(Client.ManagedSession).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                [
                    typeof(ApplicationConfiguration),
                    typeof(ConfiguredEndpoint),
                    typeof(ISessionFactory),
                    typeof(IReconnectPolicy),
                    typeof(IServerRedundancyHandler),
                    typeof(ILogger),
                    typeof(IUserIdentity),
                    typeof(IClientIdentityProvider),
                    typeof(TimeProvider),
                    typeof(ArrayOf<string>),
                    typeof(string),
                    typeof(uint),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(NetworkRedundancyOptions),
                    typeof(IClientChannelManager),
                    typeof(IClientConnectGate)
                ],
                null);

            Assert.That(ctor, Is.Not.Null);

            var managedSession = (Client.ManagedSession)ctor!.Invoke(
            [
                configuration,
                endpoint,
                sessionFactory.Object,
                reconnectPolicy,
                null,
                logger,
                null,
                null,
                null,
                default(ArrayOf<string>),
                "TestManagedSession",
                60000u,
                false,
                false,
                false,
                false,
                networkRedundancy,
                null,
                null
            ]);

            typeof(Client.ManagedSession)
                .GetField(
                    "m_session",
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(managedSession, innerSession);

            return managedSession;
        }
#pragma warning restore IDE0051, RCS1213

#pragma warning disable IDE0051, RCS1213 // Test scaffold kept for ManagedSession construction tests.
        private static ApplicationConfiguration CreateClientConfiguration(
            ITelemetryContext telemetry)
        {
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "ManagedSessionTests",
                ApplicationType = ApplicationType.Client,
                ApplicationUri = "urn:localhost:ManagedSessionTests",
                ProductUri = "urn:localhost:ManagedSessionTests",
                ClientConfiguration = new ClientConfiguration
                {
                    DefaultSessionTimeout = 60000,
                    MinSubscriptionLifetime = 10000
                },
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier()
                },
                TransportQuotas = new TransportQuotas
                {
                    OperationTimeout = 6000
                }
            };
        }
#pragma warning restore IDE0051, RCS1213

#pragma warning disable IDE0051, RCS1213 // Test scaffold kept for ManagedSession endpoint construction tests.
        private static ConfiguredEndpoint CreateEndpoint()
        {
            var description = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "anonymous",
                        TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ]
            };
            description.Server.ApplicationUri = description.EndpointUrl;
            description.Server.ApplicationType = ApplicationType.Server;

            return new ConfiguredEndpoint(
                null,
                description,
                new EndpointConfiguration { OperationTimeout = 6000 })
            {
                UpdateBeforeConnect = false
            };
        }
#pragma warning restore IDE0051, RCS1213
    }
}
