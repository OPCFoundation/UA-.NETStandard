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
using System.Collections.Generic;
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
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Server.Hosting;
using Quickstarts.ReferenceServer;
using UaLens.Connection;
using UaLens.Diagnostics;
using UaLens.Plugins.Continuity;
using UaLens.Subscriptions;
using StandardServer = Opc.Ua.Server.StandardServer;
using V2SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

namespace UaLens.Tests.Samples
{
    public sealed partial class RepositorySampleLiveTests
    {
        /// <summary>
        /// Exercises actual transfer and recreation while preserving another primary-session subscription.
        /// </summary>
        [TestCase((int)ContinuityScenario.RecreateOwnedSubscription)]
        [TestCase((int)ContinuityScenario.TransferOnLoad)]
        [TestCase((int)ContinuityScenario.RecreateOnLoad)]
        [Explicit("Hosts private-PKI sessions and actual owned V2 subscription transfer/recreation.")]
        [Category("RepositorySampleProbe")]
        public Task PrivateContinuityPreservesRevisedSettingsAndUnrelatedSubscriptionsAsync(
            int scenario)
        {
            return WithPrivateCompanionServerAsync(false, async (context, _, root, token) =>
            {
                Assert.That(context.Session.TryGetSubscriptionManager(out ISubscriptionManager? manager), Is.True);
                var sentinelTimeline = new ContinuityTimeline();
                var sentinelHandler = new ContinuityNotificationHandler(sentinelTimeline,
                    DiagnosticCorrelation.Session(context.Session), false, null);
                ISubscription sentinel = manager!.Add(sentinelHandler, new OptionsMonitor<V2SubscriptionOptions>(new()
                {
                    PublishingInterval = TimeSpan.FromMilliseconds(250),
                    KeepAliveCount = 10,
                    LifetimeCount = 100,
                    RecoveryPolicy = SubscriptionRecoveryPolicy.ReportOnly
                }));
                await using (sentinel.ConfigureAwait(false))
                {
                    await WaitForContinuityAsync(() => sentinel.Created, token).ConfigureAwait(false);
                    uint[] unrelatedIds = ((IPartitionedSubscription)sentinel).PartitionIds.ToArray();
                    Assert.That(unrelatedIds, Is.Not.Empty);
                    EndpointDescription endpoint = context.Session.Endpoint;
                    UserTokenPolicy policy = endpoint.UserIdentityTokens.ToList()
                        .Single(value => value.TokenType == UserTokenType.Anonymous);
                    ConnectionProfile profile = ConnectionProfile.Create(
                        endpoint, policy, SubscriptionEngineKind.ChannelV2);
                    var credentials = new ProfileCredentialProvider();
                    var connectionBackend = new StackConnectionBackend(context.Telemetry,
                        ct => AppConfig.BuildAsync(context.Telemetry, Path.Combine(root, "client"), ct));
                    await using (connectionBackend.ConfigureAwait(false))
                    {
                        var sessions = new WorkspaceContinuitySessionFactory(() => profile,
                            credentials.GetAsync, context.Telemetry, connectionBackend);
                        var timeline = new ContinuityTimeline();
                        var backend = new V2ContinuityBackend(() => context.Session, timeline,
                            context.Telemetry, sessions: sessions);
                        await using (backend.ConfigureAwait(false))
                        {
                            ContinuityConfiguration configuration = ContinuityState.Validate(new ContinuityStateDto
                            {
                                Scenario = scenario,
                                Targets = ["i=2258", "i=2259"],
                                PublishingIntervalMs = 100,
                                SamplingIntervalMs = 20,
                                QueueSize = 17,
                                KeepAliveCount = 7,
                                LifetimeCount = 77,
                                ItemsPerPartition = 1
                            });
                            Assert.That(backend.CheckSetup(configuration).CanStart, Is.True);
                            await backend.StartAsync(configuration, token).ConfigureAwait(false);
                            await WaitForContinuityAsync(() => timeline.Snapshot().Counters.Values >= 2, token)
                                .ConfigureAwait(false);
                            Dictionary<string, string> before = ContinuityMetrics(backend);
                            string oldIds = before["Server partition ids"];
                            long previousValues = timeline.Snapshot().Counters.Values;
                            long previousCreations = timeline.Snapshot().Counters.CreationObservations;
                            ContinuityStepResult step = await backend.StepAsync(token).ConfigureAwait(false);
                            Assert.That(step.WaitingForRestore, Is.False);
                            await WaitForContinuityAsync(
                                () => timeline.Snapshot().Counters.Values > previousValues, token)
                                .ConfigureAwait(false);
                            Dictionary<string, string> after = ContinuityMetrics(backend);
                            foreach (string setting in s_continuityRevisedSettings)
                            {
                                Assert.That(after[setting], Is.EqualTo(before[setting]), setting);
                            }
                            ContinuityCounters counters = timeline.Snapshot().Counters;
                            if (scenario == (int)ContinuityScenario.TransferOnLoad)
                            {
                                Assert.That(after["Server partition ids"], Is.EqualTo(oldIds));
                                Assert.That(counters.TransferObservations, Is.GreaterThan(0));
                                Assert.That(counters.CreationObservations, Is.EqualTo(previousCreations));
                                Assert.That(step.Description, Does.StartWith("Transfer transition observed."));
                            }
                            else
                            {
                                Assert.That(after["Server partition ids"], Is.Not.EqualTo(oldIds));
                                Assert.That(counters.CreationObservations, Is.GreaterThan(previousCreations));
                                Assert.That(timeline.Snapshot().Entries.ToList()
                                    .Any(value => value.Kind == ContinuityEvidenceKind.Recreated), Is.True);
                            }
                            Assert.That(counters.BadValues, Is.Zero);
                            Assert.That(counters.UnknownPartitionValues, Is.Zero);
                            await backend.StopAsync(token).ConfigureAwait(false);
                            Assert.That(context.Session.Connected, Is.True);
                            Assert.That(sentinel.Created, Is.True);
                            Assert.That(((IPartitionedSubscription)sentinel).PartitionIds.ToArray(),
                                Is.EqualTo(unrelatedIds));
                            Assert.That(manager.Items, Has.Exactly(1).SameAs(sentinel));
                            await RequireOwnedReadAsync(context.Session, token).ConfigureAwait(false);
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Restores real Quickstarts durable state across two separately owned server processes.
        /// </summary>
        [Test]
        [Explicit("Gracefully restarts only the owned private-PKI durable reference-server process.")]
        [Category("RepositorySampleProbe")]
        public async Task PrivateDurableContinuityRestoresAcrossGracefulOwnedProcessRestartAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), "UaLens-owned-durable-" + Guid.NewGuid());
            Directory.CreateDirectory(root);
            using var port = new TcpListener(IPAddress.Loopback, 0);
            port.Start();
            int number = ((IPEndPoint)port.LocalEndpoint).Port;
            port.Stop();
            string endpoint = $"opc.tcp://localhost:{number}/UaLensOwnedCompanion";
            string assembly = typeof(RepositorySampleLiveTests).Assembly.Location;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            OwnedCompanionServerProcess? restored = null;
            try
            {
                OwnedCompanionServerProcess source = await OwnedCompanionServerProcess.StartAsync(
                    assembly, OwnedCompanionServerKind.DurableReference, root, endpoint, deadline.Token)
                    .ConfigureAwait(false);
                await using (source.ConfigureAwait(false))
                {
                    ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
                    await ExercisePrivateCompanionAsync(telemetry, root, Path.Combine(root, "server"), endpoint,
                        (_, _) => Task.CompletedTask, async (context, _, _, token) =>
                        {
                            context.Session.KeepAliveInterval = 250;
                            NodeId primarySessionId = context.Session.SessionId;
                            UserTokenPolicy policy = context.Session.Endpoint.UserIdentityTokens.ToList()
                                .Single(value => value.TokenType == UserTokenType.Anonymous);
                            ConnectionProfile profile = ConnectionProfile.Create(
                                context.Session.Endpoint, policy, SubscriptionEngineKind.ChannelV2);
                            var credentials = new ProfileCredentialProvider();
                            var connectionBackend = new StackConnectionBackend(telemetry,
                                ct => AppConfig.BuildAsync(telemetry, Path.Combine(root, "client"), ct));
                            await using (connectionBackend.ConfigureAwait(false))
                            {
                                var sessions = new WorkspaceContinuitySessionFactory(() => profile,
                                    credentials.GetAsync, telemetry, connectionBackend,
                                    gracefulDurableSampleConfigured: true);
                                var timeline = new ContinuityTimeline();
                                var backend = new V2ContinuityBackend(() => context.Session, timeline,
                                    telemetry, sessions: sessions);
                                await using (backend.ConfigureAwait(false))
                                {
                                    ContinuityConfiguration configuration = ContinuityState.Validate(
                                        new ContinuityStateDto
                                        {
                                            Scenario = (int)ContinuityScenario.GracefulDurableRestore,
                                            Targets = ["i=2258"],
                                            PublishingIntervalMs = 250,
                                            SamplingIntervalMs = 100,
                                            QueueSize = 17,
                                            KeepAliveCount = 7,
                                            LifetimeCount = 77,
                                            DurableLifetimeHours = 1
                                        });
                                    Assert.That(backend.CheckSetup(configuration).CanStart, Is.True);
                                    await backend.StartAsync(configuration, token).ConfigureAwait(false);
                                    await WaitForContinuityAsync(() => timeline.Snapshot().Counters.Values > 0, token)
                                        .ConfigureAwait(false);
                                    Dictionary<string, string> before = ContinuityMetrics(backend);
                                    long values = timeline.Snapshot().Counters.Values;
                                    long creations = timeline.Snapshot().Counters.CreationObservations;

                                    ContinuityStepResult saved = await backend.StepAsync(token).ConfigureAwait(false);

                                    Assert.That(saved.WaitingForRestore, Is.True);
                                    Assert.That(context.Session.Connected, Is.True);
                                    await source.DisposeAsync().ConfigureAwait(false);
                                    Assert.That(source.ExitedNormally, Is.True);
                                    string store = Path.Combine(
                                        root, "Durable Subscriptions", "subscriptionsStore.bin");
                                    Assert.That(File.Exists(store), Is.True);
                                    Assert.That(new FileInfo(store).Length, Is.GreaterThan(0));
                                    restored = await OwnedCompanionServerProcess.StartAsync(
                                        assembly, OwnedCompanionServerKind.DurableReference,
                                        root, endpoint, token).ConfigureAwait(false);
                                    await WaitForContinuityAsync(() => context.Session.Connected &&
                                        !context.Session.Reconnecting &&
                                        context.Session.SessionId != primarySessionId, token).ConfigureAwait(false);
                                    await RequireOwnedReadAsync(context.Session, token).ConfigureAwait(false);

                                    ContinuityStepResult loaded = await backend.StepAsync(token).ConfigureAwait(false);

                                    Assert.That(loaded.WaitingForRestore, Is.False);
                                    Assert.That(loaded.Description, Does.StartWith("Transfer transition observed."));
                                    await WaitForContinuityAsync(
                                        () => timeline.Snapshot().Counters.Values > values, token).ConfigureAwait(false);
                                    Dictionary<string, string> after = ContinuityMetrics(backend);
                                    Assert.That(after["Server partition ids"],
                                        Is.EqualTo(before["Server partition ids"]));
                                    foreach (string setting in s_continuityRevisedSettings.Take(4))
                                    {
                                        Assert.That(after[setting], Is.EqualTo(before[setting]), setting);
                                    }
                                    Assert.That(timeline.Snapshot().Counters.CreationObservations,
                                        Is.EqualTo(creations));
                                    Assert.That(timeline.Snapshot().Counters.TransferObservations, Is.GreaterThan(0));
                                    Assert.That(timeline.Snapshot().Counters.BadValues, Is.Zero);
                                    await backend.StopAsync(token).ConfigureAwait(false);
                                    Assert.That(context.Session.Disposed, Is.False);
                                    await RequireOwnedReadAsync(context.Session, token).ConfigureAwait(false);
                                }
                            }
                        }, deadline.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    if (restored is not null)
                    {
                        await restored.DisposeAsync().ConfigureAwait(false);
                        Assert.That(restored.ExitedNormally, Is.True);
                    }
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private static async Task RunOwnedDurableServerAsync(
            string root,
            string endpoint,
            Func<Task> ready,
            Func<Task<string>> receive,
            Func<string, Task> reply,
            CancellationToken token)
        {
            var started = new OwnedCompanionStartup();
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<IServerStartupTask>(started);
            builder.Services.AddSingleton<IOpcUaServerFactory>(new OwnedDurableServerFactory());
            builder.Services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "UaLensOwnedCompanion";
                options.ApplicationUri = "urn:localhost:UaLensOwnedCompanion";
                options.ProductUri = "urn:opcfoundation.org:UaLensOwnedCompanion";
                options.PkiRoot = Path.Combine(root, "server");
                options.AutoAcceptUntrustedCertificates = false;
                options.IncludeUnsecurePolicyNone = false;
                options.EndpointUrls.Add(endpoint);
                options.ConfigureBuilder = configuration => configuration
                    .SetDurableSubscriptionsEnabled(true)
                    .SetMaxDurableEventQueueSize(1000)
                    .SetMaxDurableNotificationQueueSize(1000)
                    .SetMaxDurableSubscriptionLifetime(3600)
                    .SetShutdownDelay(0);
            });
            using IHost host = builder.Build();
            try
            {
                await host.StartAsync(token).ConfigureAwait(false);
                await WaitForOwnedCompanionStartupAsync(host, started, token).ConfigureAwait(false);
                await ready().ConfigureAwait(false);
                if (await receive().ConfigureAwait(false) != "stop")
                {
                    throw new InvalidOperationException("The owned durable fixture accepts only a graceful stop.");
                }
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await host.StopAsync(cleanup.Token).ConfigureAwait(false);
            }
            await reply("stopped").ConfigureAwait(false);
        }

        private static Dictionary<string, string> ContinuityMetrics(V2ContinuityBackend backend)
        {
            return backend.CaptureDiagnostics().ToList().ToDictionary(value => value.Name, value => value.Value);
        }

        private static async Task WaitForContinuityAsync(Func<bool> condition, CancellationToken token)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            while (!condition())
            {
                await Task.Delay(25, deadline.Token).ConfigureAwait(false);
            }
        }

        private static readonly string[] s_continuityRevisedSettings =
        [
            "Revised publishing interval ms",
            "Revised lifetime / keep-alive counts",
            "Applied max notifications / publish",
            "Item 1: sample ms / queue / status",
            "Item 2: sample ms / queue / status"
        ];

        /// <summary>
        /// Constructs the reference server with its supported telemetry-only constructor.
        /// </summary>
        private sealed class OwnedDurableServerFactory : IOpcUaServerFactory
        {
            /// <inheritdoc/>
            public StandardServer CreateServer(ITelemetryContext telemetry, TimeProvider timeProvider)
            {
                return new ReferenceServer(telemetry);
            }
        }
    }
}
