// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Redundancy;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Udp;
using Opc.Ua.Redundancy;
using RedundancySample;

namespace RedundantPubSub
{
    /// <summary>
    /// OPC UA PubSub active/standby high-availability sample.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Starts the selected role.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        public static async Task<int> Main(string[] args)
        {
            try
            {
                return await RunAsync(args).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or FormatException
                or ServiceResultException)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        private static async Task<int> RunAsync(string[] args)
        {
            Func<string, string?> read = SampleOptions.CreateSettingsReader(args, Environment.GetEnvironmentVariable);
            SampleOptions options = SampleOptions.Parse(read);
            if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out Uri? endpoint) ||
                !string.Equals(endpoint.Scheme, "opc.udp", StringComparison.Ordinal) || endpoint.Port <= 0)
            {
                throw new InvalidOperationException("PUBSUB_ENDPOINT must be an opc.udp URL with an explicit port.");
            }
            if (!options.Distributed && (!options.Insecure || !endpoint.IsLoopback))
            {
                throw new InvalidOperationException(
                    "Unsigned PubSub is limited to loopback demos with HA_INSECURE=true.");
            }
            if (options.Distributed && (options.Insecure || options.Role == SampleRole.Demo))
            {
                throw new InvalidOperationException("A networked Raft set cannot be started in an insecure demo.");
            }
            if (options.Role == SampleRole.Demo && options.Election != PubSubRedundancyElection.LeaderElection)
            {
                throw new InvalidOperationException("The scripted demo requires HA_ELECTION=leader-election.");
            }
            if (!options.Distributed && SampleFreshness.IsEnabled(read("HA_ENROLL_ONLY")))
            {
                throw new InvalidOperationException("HA_ENROLL_ONLY requires a networked Raft set.");
            }

            using CancellationTokenSource shutdown = CreateShutdownTokenSource();
            try
            {
                return options.Role switch
                {
                    SampleRole.Publisher or SampleRole.Subscriber =>
                        await RunRoleAsync(options, read, shutdown.Token).ConfigureAwait(false),
                    SampleRole.Demo => await RunDemoAsync(options, shutdown.Token).ConfigureAwait(false),
                    _ => throw new InvalidOperationException("Unknown sample role.")
                };
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                return 0;
            }
        }

        private static async Task<int> RunRoleAsync(
            SampleOptions options,
            Func<string, string?> read,
            CancellationToken ct)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            ConfigureLogging(builder);

            bool publisher = options.Role == SampleRole.Publisher;
            if (options.Distributed && options.HaMode != PubSubRedundancyMode.Cold &&
                options.Election != PubSubRedundancyElection.LeaseStore)
            {
                throw new InvalidOperationException(
                    "External warm/hot PubSub requires HA_ELECTION=lease-store to fence state writes.");
            }
            var raft = options.Distributed ? SampleCluster.ReadRaft(read, "HA") : default;
            if (options.Distributed)
            {
                _ = SampleCluster.Require(read, "OWNER_ID");
                _ = SampleCluster.Require(read, "PUBSUB_ENDPOINT");
            }
            using AesCbcHmacRecordProtector? protector = options.Distributed
                ? await SampleFreshness.AddAsync(builder.Services, read, "HA", ct).ConfigureAwait(false)
                : null;
            using SampleCluster? cluster = options.Distributed
                ? await SampleCluster.LoadAsync(read, "HA", raft.Peers!.Select(peer => peer.Pin).ToArray(), ct)
                    .ConfigureAwait(false)
                : null;
            (string Id, string SksUrl)? security = options.Distributed
                ? (SampleCluster.Require(read, "PUBSUB_SECURITY_GROUP"),
                    SampleCluster.Require(read, "PUBSUB_SKS_URL"))
                : null;

            if (options.Distributed)
            {
                builder.Services.AddSingleton<IRaftConsensus>(
                    _ => cluster!.CreateRaft(raft.Id, raft.Bind!, raft.Peers!));
                builder.Services.AddSingleton<ISharedKeyValueStore>(sp => new RaftSharedKeyValueStore(
                    sp.GetRequiredService<IRaftConsensus>(), ownsConsensus: false));
                if (options.Election == PubSubRedundancyElection.LeaderElection)
                {
                    builder.Services.AddSingleton<ILeaderElection>(sp => new RaftLeaderElection(
                        sp.GetRequiredService<IRaftConsensus>(),
                        sp.GetRequiredService<ILogger<RaftLeaderElection>>()));
                }
                AddPubSubRedundancy(builder.Services, options);
            }
            else if (publisher)
            {
                builder.Services.AddSingleton<ISharedKeyValueStore, InMemorySharedKeyValueStore>();
                builder.Services.AddSingleton<ILeaderElection>(_ => new StaticLeaderElection(true));
                AddPubSubRedundancy(builder.Services, options);
            }

            if (publisher)
            {
                AddPublisher(builder.Services, options, options.OwnerId, security);
            }
            else
            {
                if (!options.Distributed)
                {
                    AddRawMonitor(builder.Services, options);
                }
                AddSubscriber(builder.Services, options, security);
            }

            IHost host = builder.Build();
            await using var lifetime = ((IAsyncDisposable)host).ConfigureAwait(false);
            if (options.Distributed)
            {
                if (await SampleFreshness.VerifyOrEnrollAsync(host.Services, read, "HA", ct).ConfigureAwait(false))
                {
                    return 0;
                }
                if (host.Services.GetRequiredService<ISharedKeyValueStore>() is not RaftSharedKeyValueStore raftStore)
                {
                    throw new InvalidOperationException("The distributed PubSub sample requires a Raft payload store.");
                }
                await raftStore.TryGetAsync("sample/readiness", ct).ConfigureAwait(false);
            }

            if (options.Distributed || publisher)
            {
                LogComponentRoles(host.Services, options.OwnerId, publisher ? "Publisher" : "Subscriber");
            }

            ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RedundantPubSub");
            if (publisher)
            {
                logger.PublisherStarting(options.OwnerId, options.HaMode, options.Election, options.Endpoint);
            }
            else
            {
                logger.SubscriberStarting(options.OwnerId, options.Distributed, options.Endpoint);
            }
            await host.StartAsync(ct).ConfigureAwait(false);
            await host.WaitForShutdownAsync(ct).ConfigureAwait(false);
            return 0;
        }

        private static async Task<int> RunDemoAsync(SampleOptions options, CancellationToken cancellationToken)
        {
            const uint simulatedPostFailoverMargin = 5;
            const int simulatedPreFailoverCount = 3;
            const int simulatedPostFailoverCount = 2;
            using var store = new InMemorySharedKeyValueStore();
            var election = new ManualLeaderElection();
            election.SetLeader("publisher-a");
            using IHost subscriber = BuildDemoSubscriber(options);
            using IHost publisherA = BuildDemoPublisher(options, "publisher-a", store, election);
            using IHost publisherB = BuildDemoPublisher(options, "publisher-b", store, election);
            if (options.HaMode == PubSubRedundancyMode.Warm)
            {
                Console.WriteLine(
                    "WARM MODE: standby activation is exercised, but without hot checkpoints the promoted " +
                    "publisher restarts its local SequenceNumber state in this demo.");
            }
            await subscriber.StartAsync(cancellationToken).ConfigureAwait(false);
            await publisherA.StartAsync(cancellationToken).ConfigureAwait(false);
            await publisherB.StartAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine(
                "SIMULATED DEMO NARRATIVE: the SequenceNumber continuity lines below are illustrative and do not come " +
                "from the live UDP pipeline also running in this mode.");
            uint? simulatedLastSequence = null;
            string simulatedLastOwner = "unknown";
            EmitSimulatedSequences(
                ownerId: "publisher-a",
                start: 0,
                count: simulatedPreFailoverCount,
                ref simulatedLastSequence,
                ref simulatedLastOwner);
            await Task.Delay(options.DemoFirstActiveDuration, cancellationToken).ConfigureAwait(false);
            Console.WriteLine("FAILOVER: stopping publisher-a; publisher-b is promoted.");
            await publisherA.StopAsync(cancellationToken).ConfigureAwait(false);
            election.SetLeader("publisher-b");
            EmitSimulatedSequences(
                ownerId: "publisher-b",
                start: options.HaMode == PubSubRedundancyMode.Hot
                    ? simulatedPreFailoverCount + simulatedPostFailoverMargin
                    : 0,
                count: simulatedPostFailoverCount,
                ref simulatedLastSequence,
                ref simulatedLastOwner);
            await Task.Delay(options.DemoSecondActiveDuration, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        private static void EmitSimulatedSequences(
            string ownerId,
            uint start,
            int count,
            ref uint? lastSequence,
            ref string lastOwner)
        {
            uint sequence = start;
            for (int ii = 0; ii < count; ii++)
            {
                sequence++;
                if (lastSequence is null)
                {
                    Console.WriteLine("SIMULATED: SequenceNumber {0} received.", sequence);
                }
                else if (sequence <= lastSequence.Value)
                {
                    Console.WriteLine(
                        "SIMULATED: DATA LOSS: sequence reset {0} -> {1} (subscriber must reset de-duplication).",
                        lastSequence.Value,
                        sequence);
                }
                else if (!string.Equals(lastOwner, ownerId, StringComparison.Ordinal))
                {
                    Console.WriteLine(
                        "SIMULATED: HA OK: sequence continued {0} -> {1} across failover (gap, no reset).",
                        lastSequence.Value,
                        sequence);
                }
                else
                {
                    Console.WriteLine("SIMULATED: SequenceNumber {0} -> {1}.", lastSequence.Value, sequence);
                }

                lastSequence = sequence;
                lastOwner = ownerId;
            }
        }

        private static IHost BuildDemoPublisher(
            SampleOptions options,
            string ownerId,
            ISharedKeyValueStore store,
            ManualLeaderElection election)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            ConfigureLogging(builder);
            builder.Services.AddSingleton(store);
            builder.Services.AddSingleton(election.ForOwner(ownerId));
            AddPubSubRedundancy(builder.Services, options with { OwnerId = ownerId });
            AddPublisher(builder.Services, options with { OwnerId = ownerId }, ownerId);
            IHost host = builder.Build();
            LogComponentRoles(host.Services, ownerId, "Publisher");
            return host;
        }

        private static IHost BuildDemoSubscriber(SampleOptions options)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            ConfigureLogging(builder);
            AddRawMonitor(builder.Services, options);
            AddSubscriber(builder.Services, options);
            return builder.Build();
        }

        private static void AddRawMonitor(IServiceCollection services, SampleOptions options)
        {
            services.AddSingleton(options);
            services.AddSingleton<SequenceContinuityMonitor>();
            services.AddHostedService<RawUdpSequenceMonitor>();
        }

        private static void AddPubSubRedundancy(IServiceCollection services, SampleOptions options)
        {
            services.AddSingleton<IServiceMessageContext>(sp => ServiceMessageContext.CreateEmpty(
                sp.GetRequiredService<ITelemetryContext>()));
            services.AddPubSubRedundancy(o =>
            {
                o.Mode = options.HaMode;
                o.Election = options.Election;
                o.OwnerId = options.OwnerId;
                o.LeaseDuration = options.LeaseDuration;
            });
        }

        private static void AddPublisher(
            IServiceCollection services,
            SampleOptions options,
            string ownerId,
            (string Id, string SksUrl)? security = null)
        {
            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .AddPublisher()
                .AddUdpTransport(udp => ConfigureUdp(udp, options.Insecure))
                .AddDataSetSource(SampleConstants.DataSetName, new HaDataSetSource(ownerId))
                .ConfigureApplication((sp, app) =>
                {
                    if (security != null)
                    {
                        app.AddSecurityKeyProvider(sp.GetRequiredService<IPubSubSecurityKeyProvider>());
                    }
                    app.WithApplicationId($"urn:opcfoundation:RedundantPubSub:Publisher:{ownerId}")
                        .UseConfiguration(BuildPublisherConfiguration(options, security));
                }));
        }

        private static void AddSubscriber(
            IServiceCollection services,
            SampleOptions options,
            (string Id, string SksUrl)? security = null)
        {
            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .AddSubscriber()
                .AddUdpTransport(udp => ConfigureUdp(udp, options.Insecure))
                .AddSubscribedDataSetSink(
                    SampleConstants.ReaderName,
                    sp => new HaSubscriberSink(sp.GetRequiredService<ILogger<HaSubscriberSink>>()))
                .ConfigureApplication((sp, app) =>
                {
                    if (security != null)
                    {
                        app.AddSecurityKeyProvider(sp.GetRequiredService<IPubSubSecurityKeyProvider>());
                    }
                    app.WithApplicationId("urn:opcfoundation:RedundantPubSub:Subscriber")
                        .UseConfiguration(BuildSubscriberConfiguration(options, security));
                }));
        }

        private static void ConfigureUdp(UdpTransportOptions options, bool insecureDemo)
        {
            if (insecureDemo)
            {
                options.WithDatagramPolicy(new LoopbackUdpPolicy());
            }
        }

        private static PubSubConfigurationDataType BuildPublisherConfiguration(
            SampleOptions options,
            (string Id, string SksUrl)? security)
        {
            return PubSubConfigurationBuilder.Create()
                .AddPublishedDataSet(SampleConstants.DataSetName, ds => ds
                    .AddField("Counter", (byte)DataTypes.Int64, DataTypeIds.Int64)
                    .AddField("SourceTimestamp", (byte)DataTypes.DateTime, DataTypeIds.DateTime)
                    .AddField("OwnerId", (byte)DataTypes.String, DataTypeIds.String))
                .AddConnection("Publisher Connection", connection => connection
                    .WithPublisherId(new Variant(options.PublisherId))
                    .WithTransportProfile(Profiles.PubSubUdpUadpTransport)
                    .WithAddress(options.Endpoint)
                    .AddWriterGroup("WriterGroup 1", group =>
                    {
                        if (security is { } secured)
                        {
                            group.WithSecurity(MessageSecurityMode.SignAndEncrypt, secured.Id, secured.SksUrl);
                        }
                        group.WithWriterGroupId(options.WriterGroupId)
                        .WithPublishingInterval(options.IntervalMs)
                        .WithMessageSettings(new UadpWriterGroupMessageDataType
                        {
                            NetworkMessageContentMask = (uint)(
                                UadpNetworkMessageContentMask.PublisherId |
                                UadpNetworkMessageContentMask.GroupHeader |
                                UadpNetworkMessageContentMask.WriterGroupId |
                                UadpNetworkMessageContentMask.PayloadHeader |
                                UadpNetworkMessageContentMask.NetworkMessageNumber |
                                UadpNetworkMessageContentMask.SequenceNumber)
                        })
                        .WithTransportSettings(new DatagramWriterGroupTransportDataType())
                        .AddDataSetWriter("Writer 1", writer => writer
                            .WithDataSetWriterId(options.DataSetWriterId)
                            .WithDataSetName(SampleConstants.DataSetName)
                            .WithKeyFrameCount(1)
                            .WithFieldContentMask(DataSetFieldContentMask.RawData)
                            .WithMessageSettings(new UadpDataSetWriterMessageDataType
                            {
                                DataSetMessageContentMask = (uint)(
                                    UadpDataSetMessageContentMask.Status |
                                    UadpDataSetMessageContentMask.SequenceNumber)
                            }));
                    }))
                .Build();
        }

        private static PubSubConfigurationDataType BuildSubscriberConfiguration(
            SampleOptions options,
            (string Id, string SksUrl)? security)
        {
            return PubSubConfigurationBuilder.Create()
                .AddConnection("Subscriber Connection", connection => connection
                    .WithPublisherId(new Variant(options.PublisherId))
                    .WithTransportProfile(Profiles.PubSubUdpUadpTransport)
                    .WithAddress(options.Endpoint)
                    .AddReaderGroup("ReaderGroup 1", group =>
                    {
                        if (security is { } secured)
                        {
                            group.WithSecurity(MessageSecurityMode.SignAndEncrypt, secured.Id, secured.SksUrl);
                        }
                        group.WithMaxNetworkMessageSize(1500)
                        .AddDataSetReader(SampleConstants.ReaderName, reader => reader
                            .WithFilter(new Variant(options.PublisherId), options.WriterGroupId, options.DataSetWriterId)
                            .WithFieldContentMask(DataSetFieldContentMask.RawData)
                            .WithMessageReceiveTimeout(5000)
                            .WithMessageSettings(new UadpDataSetReaderMessageDataType
                            {
                                NetworkMessageContentMask = (uint)(
                                    UadpNetworkMessageContentMask.PublisherId |
                                    UadpNetworkMessageContentMask.GroupHeader |
                                    UadpNetworkMessageContentMask.WriterGroupId |
                                    UadpNetworkMessageContentMask.PayloadHeader |
                                    UadpNetworkMessageContentMask.NetworkMessageNumber |
                                    UadpNetworkMessageContentMask.SequenceNumber),
                                DataSetMessageContentMask = (uint)(
                                    UadpDataSetMessageContentMask.Status |
                                    UadpDataSetMessageContentMask.SequenceNumber)
                            })
                            .WithMirrorSubscribedDataSet(SampleConstants.ReaderName)
                            .WithDataSetMetaData(SampleConstants.DataSetName, metaData => metaData
                                .WithoutFieldIds()
                                .AddField("Counter", (byte)DataTypes.Int64, DataTypeIds.Int64)
                                .AddField("SourceTimestamp", (byte)DataTypes.DateTime, DataTypeIds.DateTime)
                                .AddField("OwnerId", (byte)DataTypes.String, DataTypeIds.String)));
                    }))
                .Build();
        }

        private static void LogComponentRoles(IServiceProvider services, string ownerId, string roleLabel)
        {
            IPubSubActivationCoordinator coordinator = services.GetRequiredService<IPubSubActivationCoordinator>();
            ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("RedundantPubSub.HA");
            coordinator.RoleChanged += (_, e) => logger.ComponentRoleChanged(
                roleLabel,
                ownerId,
                e.ComponentId,
                e.Role);
        }

        private static void ConfigureLogging(HostApplicationBuilder builder)
        {
            builder.Logging.ClearProviders();
            builder.Logging.EnableOpcUaRedaction();
            builder.Logging.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            });
            builder.Logging.SetMinimumLevel(LogLevel.Information);
            builder.Services.AddSingleton(_ => DefaultTelemetry.Create(logging =>
                logging.SetMinimumLevel(LogLevel.Information)));
        }

        private static CancellationTokenSource CreateShutdownTokenSource()
        {
            var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                shutdown.Cancel();
            };
            return shutdown;
        }
    }

    internal static partial class ProgramLog
    {
        [LoggerMessage(EventId = RedundantPubSubEventIds.Program + 0, Level = LogLevel.Information,
            Message = "Publisher {OwnerId} starting: mode={Mode}, election={Election}, endpoint={Endpoint}.")]
        public static partial void PublisherStarting(
            this ILogger logger,
            string ownerId,
            PubSubRedundancyMode mode,
            PubSubRedundancyElection election,
            string endpoint);

        [LoggerMessage(EventId = RedundantPubSubEventIds.Program + 1, Level = LogLevel.Information,
            Message = "Subscriber {OwnerId} starting: highAvailability={HighlyAvailable}, endpoint={Endpoint}.")]
        public static partial void SubscriberStarting(
            this ILogger logger,
            string ownerId,
            bool highlyAvailable,
            string endpoint);

        [LoggerMessage(EventId = RedundantPubSubEventIds.Program + 2, Level = LogLevel.Information,
            Message = "{RoleLabel} {OwnerId}: {ComponentId} -> {Role}.")]
        public static partial void ComponentRoleChanged(
            this ILogger logger,
            string roleLabel,
            string ownerId,
            string componentId,
            PubSubComponentRole role);
    }
}
