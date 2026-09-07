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

#if NET10_0
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Configuration;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Exercises mission discovery over a hosted OPC UA server and a real client Session.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class IntentMissionHostedClientTests
    {
        [Test]
        public async Task HostedMissionIsListedWhileActiveAndAfterTerminalCompletion()
        {
            await using var fixture = new HostedMissionFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await using HostedMissionClient client = await fixture.ConnectAsync().ConfigureAwait(false);
            RobotIntentClient discovery = new(client.Session, fixture.Telemetry, client.Streaming);
            ArrayOf<RobotIntentNodeLookupEntry> controllers = await discovery
                .DiscoverControllersAsync()
                .ConfigureAwait(false);
            Assert.That(controllers, Has.Count.EqualTo(1));
            RobotIntentControllerClient controller = discovery.Controller(controllers[0].NodeId);
            await using CommandAuthorityLease authority = await controller.RequireAuthorityAsync()
                .ConfigureAwait(false);

            MissionSubmissionResult admission = await controller.SubmitMissionAsync(CreateMission("hosted-mission"))
                .ConfigureAwait(false);
            Assert.That(admission.Accepted, Is.True, admission.Message.Text);
            await AwaitWithTimeoutAsync(fixture.WaitForExecutionStartAsync(), TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            RobotIntentControllerState state = await controller.ReadStateAsync().ConfigureAwait(false);
            ArrayOf<MissionSnapshot> activeMissions = await controller.ListMissionsAsync().ConfigureAwait(false);
            MissionSnapshot[] activeMissionArray = activeMissions.ToArray()!;

            Assert.Multiple(() =>
            {
                Assert.That(state.ActiveMission.Available, Is.True);
                Assert.That(state.ActiveMission.Value, Is.EqualTo(admission.Operation));
                Assert.That(
                    activeMissionArray,
                    Has.One.Matches<MissionSnapshot>(snapshot =>
                        snapshot.MissionId == "hosted-mission" &&
                        snapshot.MissionNode == admission.Operation &&
                        !RobotIntentRules.IsTerminal(snapshot.ExecutionState)));
            });

            fixture.ReleaseExecution();
            await WaitUntilAsync(
                async () =>
                {
                    ArrayOf<MissionSnapshot> missions = await controller.ListMissionsAsync().ConfigureAwait(false);
                    for (int ii = 0; ii < missions.Count; ii++)
                    {
                        if (missions[ii].MissionNode == admission.Operation &&
                            RobotIntentRules.IsTerminal(missions[ii].ExecutionState))
                        {
                            return true;
                        }
                    }
                    return false;
                },
                "hosted mission terminal completion").ConfigureAwait(false);

            ArrayOf<MissionSnapshot> terminalMissions = await controller.ListMissionsAsync().ConfigureAwait(false);
            MissionSnapshot[] terminalMissionArray = terminalMissions.ToArray()!;
            Assert.That(
                terminalMissionArray,
                Has.One.Matches<MissionSnapshot>(snapshot =>
                    snapshot.MissionId == "hosted-mission" &&
                    snapshot.MissionNode == admission.Operation &&
                    snapshot.ExecutionState == ExecutionStateEnum.Succeeded));
        }

        [Test]
        public async Task HostedFailedMissionPublishesFailureAndMessageToClient()
        {
            await using var fixture = new HostedMissionFixture();
            fixture.SetOutcome(IntentOutcome.Fail(
                IntentFailureEnum.SafetyLimitExceeded,
                "hosted safety limit"));
            await fixture.StartAsync().ConfigureAwait(false);
            await using HostedMissionClient client = await fixture.ConnectAsync().ConfigureAwait(false);
            RobotIntentClient discovery = new(client.Session, fixture.Telemetry, client.Streaming);
            ArrayOf<RobotIntentNodeLookupEntry> controllers = await discovery
                .DiscoverControllersAsync()
                .ConfigureAwait(false);
            RobotIntentControllerClient controller = discovery.Controller(controllers[0].NodeId);
            await using CommandAuthorityLease authority = await controller.RequireAuthorityAsync()
                .ConfigureAwait(false);

            MissionSubmissionResult admission = await controller
                .SubmitMissionAsync(CreateMission("hosted-failure"))
                .ConfigureAwait(false);
            Assert.That(admission.Accepted, Is.True, admission.Message.Text);
            await AwaitWithTimeoutAsync(
                fixture.WaitForExecutionStartAsync(),
                TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            fixture.ReleaseExecution();
            MissionSnapshot? terminal = null;
            await WaitUntilAsync(
                async () =>
                {
                    ArrayOf<MissionSnapshot> missions = await controller
                        .ListMissionsAsync()
                        .ConfigureAwait(false);
                    terminal = missions.ToArray()!.FirstOrDefault(snapshot =>
                        snapshot.MissionNode == admission.Operation &&
                        RobotIntentRules.IsTerminal(snapshot.ExecutionState));
                    return terminal != null;
                },
                "hosted failed mission result").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(terminal, Is.Not.Null);
                Assert.That(terminal!.ExecutionState, Is.EqualTo(ExecutionStateEnum.Failed));
                Assert.That(terminal.Failure, Is.EqualTo(IntentFailureEnum.SafetyLimitExceeded));
                Assert.That(terminal.FailureMessage.Text, Is.EqualTo("hosted safety limit"));
            });
        }

        private static MissionDataType CreateMission(string missionId)
        {
            return new MissionDataType
            {
                MissionId = missionId,
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "gated",
                        SequenceId = 1,
                        Released = true,
                        Intent = new WaitIntentDataType
                        {
                            IntentId = $"{missionId}/gated",
                            Duration = 1.0
                        }
                    }
                ]
            };
        }

        private static async Task AwaitWithTimeoutAsync(Task task, TimeSpan timeout)
        {
            if (await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) != task)
            {
                throw new TimeoutException();
            }
            await task.ConfigureAwait(false);
        }

        private static async ValueTask WaitUntilAsync(
            Func<ValueTask<bool>> predicate,
            string description)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (await predicate().ConfigureAwait(false))
                {
                    return;
                }
                await Task.Delay(25).ConfigureAwait(false);
            }
            Assert.Fail($"Timed out waiting for {description}.");
        }

        private sealed class HostedMissionFixture : IAsyncDisposable
        {
            public string ServerUrl { get; private set; } = string.Empty;

            public ITelemetryContext Telemetry { get; } = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));

            private GatedExecutor Executor { get; } = new();

            public Task<bool> WaitForExecutionStartAsync()
            {
                return Executor.Started.Task;
            }

            public void ReleaseExecution()
            {
                Executor.Release();
            }

            public void SetOutcome(IntentOutcome outcome)
            {
                Executor.Outcome = outcome;
            }

            public async ValueTask StartAsync()
            {
                Exception? lastFailure = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        await StartAttemptAsync().ConfigureAwait(false);
                        return;
                    }
                    catch (Exception exception)
                    {
                        lastFailure = exception;
                        await StopHostAsync().ConfigureAwait(false);
                    }
                }
                throw new InvalidOperationException(
                    "The hosted mission test server did not become available.",
                    lastFailure);
            }

            public async ValueTask<HostedMissionClient> ConnectAsync()
            {
                EndpointDescription? endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                    m_clientConfiguration,
                    ServerUrl,
                    useSecurity: false,
                    Telemetry,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(endpointDescription, Is.Not.Null, "The hosted server endpoint must be discoverable.");
                var endpoint = new ConfiguredEndpoint(
                    null,
                    endpointDescription!,
                    EndpointConfiguration.Create(m_clientConfiguration));
                var sessionFactory = new DefaultSessionFactory(Telemetry)
                {
                    SubscriptionEngineFactory = DefaultSubscriptionEngineFactory.Instance
                };
                ISession session = await sessionFactory.CreateAsync(
                    m_clientConfiguration,
                    endpoint,
                    updateBeforeConnect: false,
                    sessionName: "mission-hosted-client",
                    sessionTimeout: 60000,
                    identity: new UserIdentity(new AnonymousIdentityToken()),
                    preferredLocales: default,
                    ct: CancellationToken.None).ConfigureAwait(false);
                if (!session.TryGetSubscriptionManager(out ISubscriptionManager? manager))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "The hosted mission client Session did not expose a subscription manager.");
                }
                return new HostedMissionClient(session, new StreamingSubscription(manager));
            }

            public async ValueTask DisposeAsync()
            {
                await StopHostAsync().ConfigureAwait(false);
            }

            private async ValueTask StartAttemptAsync()
            {
                int port = GetFreeTcpPort();
                ServerUrl = FormattableString.Invariant($"opc.tcp://localhost:{port}/MissionHosted");
                HostApplicationBuilder builder = Host.CreateApplicationBuilder();
                builder.Logging.ClearProviders();
                builder.Logging.SetMinimumLevel(LogLevel.Warning);
                builder.Services.AddSingleton<IIntentExecutor>(Executor);
                builder.Services
                    .AddOpcUa()
                    .AddServer(options =>
                    {
                        options.ApplicationName = "MissionHostedServer";
                        options.ApplicationUri = "urn:localhost:OPCFoundation:MissionHostedServer";
                        options.ProductUri = "uri:opcfoundation.org:MissionHostedServer";
                        options.AutoAcceptUntrustedCertificates = true;
                        options.EndpointUrls.Add(ServerUrl);
                        options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy
                        {
                            TokenType = UserTokenType.Anonymous
                        });
                    })
                    .ConfigureRoles(options => options.Roles.Add(new Opc.Ua.Server.RoleDefinitionOptions
                    {
                        Name = "Operator",
                        Identities =
                        {
                            new Opc.Ua.Server.RoleIdentityMappingOptions
                            {
                                CriteriaType = IdentityCriteriaType.Anonymous
                            }
                        }
                    }))
                    .AddRobotIntent(options =>
                        options.InstanceNamespaceUri = "urn:tests:robot-intent:mission-hosted")
                    .ConfigureRobotIntent(ConfigureRobotIntentAsync);
                m_host = builder.Build();
                await m_host.StartAsync().ConfigureAwait(false);
                m_clientConfiguration = await CreateClientConfigurationAsync().ConfigureAwait(false);
                m_clientConfigurationReady = true;
                await WaitForEndpointAsync().ConfigureAwait(false);
            }

            private async ValueTask StopHostAsync()
            {
                if (m_host != null)
                {
                    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await m_host.StopAsync(stop.Token).ConfigureAwait(false);
                    m_host.Dispose();
                    m_host = null;
                }
                if (m_clientConfigurationReady &&
                    m_clientConfiguration.CertificateManager is IDisposable certificateManager)
                {
                    certificateManager.Dispose();
                    m_clientConfigurationReady = false;
                }
            }

            private static async ValueTask ConfigureRobotIntentAsync(
                IRobotIntentBuildContext context,
                CancellationToken cancellationToken)
            {
                await context.AddIntentControllerAsync(
                    "MissionController",
                    controller => controller
                        .WithOperationalMode(OperationalModeEnum.AutomaticExternal)
                        .WithReady(true)
                        .Accepts<WaitIntentDataType>(retrySupported: true),
                    cancellationToken).ConfigureAwait(false);
            }

            private async ValueTask<ApplicationConfiguration> CreateClientConfigurationAsync()
            {
                string pkiRoot = Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "pki",
                    Guid.NewGuid().ToString("N"));
                var configuration = new ApplicationConfiguration(Telemetry)
                {
                    ApplicationName = "MissionHostedClient",
                    ApplicationUri = "urn:localhost:OPCFoundation:MissionHostedClient",
                    ApplicationType = ApplicationType.Client,
                    SecurityConfiguration = new SecurityConfiguration
                    {
                        ApplicationCertificate = new CertificateIdentifier
                        {
                            StoreType = CertificateStoreType.Directory,
                            StorePath = Path.Combine(pkiRoot, "own"),
                            SubjectName = "CN=MissionHostedClient, O=OPC Foundation"
                        },
                        TrustedIssuerCertificates = Store(Path.Combine(pkiRoot, "issuer")),
                        TrustedPeerCertificates = Store(Path.Combine(pkiRoot, "trusted")),
                        RejectedCertificateStore = Store(Path.Combine(pkiRoot, "rejected")),
                        AutoAcceptUntrustedCertificates = true
                    },
                    TransportQuotas = new TransportQuotas { MaxMessageSize = 4 * 1024 * 1024 },
                    ClientConfiguration = new ClientConfiguration(),
                    ServerConfiguration = new ServerConfiguration()
                };
                await configuration.ValidateAsync(ApplicationType.Client).ConfigureAwait(false);
                var application = new ApplicationInstance(configuration, Telemetry);
                await application.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false);
                configuration.CertificateManager ??= CertificateManagerFactory.Create(
                    configuration.SecurityConfiguration,
                    Telemetry);
                configuration.CertificateManager.AcceptError = static (_, _) => true;
                return configuration;
            }

            private async ValueTask WaitForEndpointAsync()
            {
                Exception? lastException = null;
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        EndpointDescription? endpoint = await CoreClientUtils.SelectEndpointAsync(
                            m_clientConfiguration,
                            ServerUrl,
                            useSecurity: false,
                            Telemetry,
                            CancellationToken.None).ConfigureAwait(false);
                        if (endpoint != null)
                        {
                            return;
                        }
                    }
                    catch (Exception exception)
                    {
                        lastException = exception;
                    }
                    await Task.Delay(100).ConfigureAwait(false);
                }
                throw new TimeoutException(
                    $"Hosted OPC UA endpoint '{ServerUrl}' did not become available. " +
                    $"Last error: {lastException?.Message}");
            }

            private static CertificateTrustList Store(string path)
            {
                return new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = path
                };
            }

            private static int GetFreeTcpPort()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }

            private ApplicationConfiguration m_clientConfiguration = null!;
            private bool m_clientConfigurationReady;
            private IHost? m_host;

            private sealed class GatedExecutor : IIntentExecutor
            {
                public TaskCompletionSource<bool> Started { get; } = new(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                public IntentOutcome Outcome { get; set; } = IntentOutcome.Success;

                public ValueTask<IntentOutcome> ExecuteAsync(
                    IntentExecution execution,
                    CancellationToken cancellationToken)
                {
                    Started.TrySetResult(true);
                    return WaitForReleaseAsync();
                }

                public bool CanCancel(IntentExecution execution)
                {
                    return true;
                }

                public void Release()
                {
                    m_release.TrySetResult(true);
                }

                private async ValueTask<IntentOutcome> WaitForReleaseAsync()
                {
                    await m_release.Task.ConfigureAwait(false);
                    return Outcome;
                }

                private readonly TaskCompletionSource<bool> m_release = new(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        private sealed class HostedMissionClient(
            ISession session,
            IStreamingSubscription streaming)
            : IAsyncDisposable
        {
            public ISession Session { get; } = session;

            public IStreamingSubscription Streaming { get; } = streaming;

            public async ValueTask DisposeAsync()
            {
                if (Session.Connected)
                {
                    await Session.CloseAsync(1000, true).ConfigureAwait(false);
                }
                await Streaming.DisposeAsync().ConfigureAwait(false);
                Session.Dispose();
            }
        }
    }
}
#endif
