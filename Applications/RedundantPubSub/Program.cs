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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Redundancy;
using Opc.Ua.Redundancy;
using Raft;
using Raft.Configuration;
using Raft.Storage;
using Raft.Transport.NanoMsg;

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
        /// <exception cref="InvalidOperationException">Thrown when the configured role is not recognized.</exception>
        public static async Task<int> Main(string[] args)
        {
            var options = SampleOptions.Parse(args, Environment.GetEnvironmentVariable);
            using CancellationTokenSource shutdown = CreateShutdownTokenSource();
            try
            {
                return options.Role switch
                {
                    SampleRole.Publisher => await RunPublisherAsync(options, shutdown.Token).ConfigureAwait(false),
                    SampleRole.Subscriber => await RunSubscriberAsync(options, shutdown.Token).ConfigureAwait(false),
                    SampleRole.Demo => await RunDemoAsync(options, shutdown.Token).ConfigureAwait(false),
                    _ => throw new InvalidOperationException("Unknown sample role.")
                };
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                return 0;
            }
        }

        private static async Task<int> RunPublisherAsync(SampleOptions options, CancellationToken cancellationToken)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            ConfigureLogging(builder);
            AddDistributedStore(builder.Services, options);
            AddPubSubRedundancy(builder.Services, options);
            AddPublisher(builder.Services, options, options.OwnerId);
            using IHost host = builder.Build();
            LogComponentRoles(host.Services, options.OwnerId, "Publisher");
            ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RedundantPubSub.Publisher");
            logger.LogInformation(
                "Publisher {OwnerId} starting: mode={Mode}, election={Election}, endpoint={Endpoint}.",
                options.OwnerId,
                options.HaMode,
                options.Election,
                options.Endpoint);
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        private static async Task<int> RunSubscriberAsync(SampleOptions options, CancellationToken cancellationToken)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            ConfigureLogging(builder);
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton<SequenceContinuityMonitor>();
            builder.Services.AddHostedService<RawUdpSequenceMonitor>();

            // A subscriber configured with a distributed election (a Raft member set)
            // participates in PubSub high availability: its ReaderGroup is governed by the
            // activation coordinator, so only the elected-active replica dispatches received
            // data sets to the sink while standby replicas stay paused until promoted.
            bool highlyAvailable = options.RaftMembers > 1 || options.RaftPeers.Count > 0;
            if (highlyAvailable)
            {
                AddDistributedStore(builder.Services, options);
                AddPubSubRedundancy(builder.Services, options);
            }

            AddSubscriber(builder.Services, options);
            using IHost host = builder.Build();
            if (highlyAvailable)
            {
                LogComponentRoles(host.Services, options.OwnerId, "Subscriber");
            }

            ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RedundantPubSub.Subscriber");
            logger.LogInformation(
                "Subscriber {OwnerId} starting: highAvailability={HighlyAvailable}, endpoint={Endpoint}.",
                options.OwnerId,
                highlyAvailable,
                options.Endpoint);
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        private static async Task<int> RunDemoAsync(SampleOptions options, CancellationToken cancellationToken)
        {
            using var store = new InMemorySharedKeyValueStore();
            var election = new ManualLeaderElection();
            election.SetLeader("publisher-a");
            using IHost subscriber = BuildDemoSubscriber(options);
            using IHost publisherA = BuildDemoPublisher(options, "publisher-a", store, election);
            using IHost publisherB = BuildDemoPublisher(options, "publisher-b", store, election);
            await subscriber.StartAsync(cancellationToken).ConfigureAwait(false);
            await publisherA.StartAsync(cancellationToken).ConfigureAwait(false);
            await publisherB.StartAsync(cancellationToken).ConfigureAwait(false);
            SequenceContinuityMonitor monitor = subscriber.Services.GetRequiredService<SequenceContinuityMonitor>();
            await EmitDemoSequencesAsync(options, store, monitor, "publisher-a", 0, 3, cancellationToken)
                .ConfigureAwait(false);
            await Task.Delay(options.DemoFirstActiveDuration, cancellationToken).ConfigureAwait(false);
            Console.WriteLine("FAILOVER: stopping publisher-a; publisher-b is promoted.");
            await publisherA.StopAsync(cancellationToken).ConfigureAwait(false);
            election.SetLeader("publisher-b");
            uint start = 0;
            if (options.HaMode == PubSubRedundancyMode.Hot)
            {
                var checkpointStore = new SharedStorePubSubWriterCheckpointStore(store);
                start = (await checkpointStore
                    .GetSequenceNumberAsync("demo-writer-group", options.DataSetWriterId, cancellationToken)
                    .ConfigureAwait(false) ??
                    0) +
                    5;
            }
            await EmitDemoSequencesAsync(options, store, monitor, "publisher-b", start, 2, cancellationToken)
                .ConfigureAwait(false);
            await Task.Delay(options.DemoSecondActiveDuration, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        private static async Task EmitDemoSequencesAsync(
            SampleOptions options,
            ISharedKeyValueStore store,
            SequenceContinuityMonitor monitor,
            string ownerId,
            uint start,
            int count,
            CancellationToken cancellationToken)
        {
            var checkpointStore = new SharedStorePubSubWriterCheckpointStore(store);
            uint sequence = start;
            for (int ii = 0; ii < count; ii++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sequence++;
                monitor.OnSequence(sequence, BuildDemoFields(ownerId));
                if (options.HaMode == PubSubRedundancyMode.Hot)
                {
                    await checkpointStore
                        .SetSequenceNumberAsync("demo-writer-group", options.DataSetWriterId, sequence, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        private static ArrayOf<DataSetField> BuildDemoFields(string ownerId)
        {
            return new ArrayOf<DataSetField>(new[]
            {
                new DataSetField { Name = "OwnerId", Value = new Variant(ownerId) }
            });
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
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton<SequenceContinuityMonitor>();
            builder.Services.AddHostedService<RawUdpSequenceMonitor>();
            AddSubscriber(builder.Services, options);
            return builder.Build();
        }

        private static void AddDistributedStore(IServiceCollection services, SampleOptions options)
        {
            DefaultRaftConsensus consensus = BuildRaftCluster(options);
            services.AddSingleton<IRaftConsensus>(consensus);
            services.AddSingleton<ISharedKeyValueStore>(sp => new RaftSharedKeyValueStore(
                sp.GetRequiredService<IRaftConsensus>(), ownsConsensus: false));
            services.AddSingleton<IRecordProtector>(_ => CreateRecordProtector(options));
            services.AddHostedService<RaftLifetimeService>();
            if (options.Election == PubSubRedundancyElection.LeaderElection)
            {
                services.AddSingleton<ILeaderElection>(sp => new RaftLeaderElection(
                    sp.GetRequiredService<IRaftConsensus>(),
                    sp.GetRequiredService<ILogger<RaftLeaderElection>>()));
            }
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

        private static void AddPublisher(IServiceCollection services, SampleOptions options, string ownerId)
        {
            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .AddPublisher()
                .AddUdpTransport()
                .AddDataSetSource(SampleConstants.DataSetName, new HaDataSetSource(ownerId))
                .ConfigureApplication(app => app
                    .WithApplicationId($"urn:opcfoundation:RedundantPubSub:Publisher:{ownerId}")
                    .UseConfiguration(BuildPublisherConfiguration(options))));
        }

        private static void AddSubscriber(IServiceCollection services, SampleOptions options)
        {
            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .AddSubscriber()
                .AddUdpTransport()
                .AddSubscribedDataSetSink(
                    SampleConstants.ReaderName,
                    sp => new HaSubscriberSink(sp.GetRequiredService<ILogger<HaSubscriberSink>>()))
                .ConfigureApplication(app => app
                    .WithApplicationId("urn:opcfoundation:RedundantPubSub:Subscriber")
                    .UseConfiguration(BuildSubscriberConfiguration(options))));
        }

        private static PubSubConfigurationDataType BuildPublisherConfiguration(SampleOptions options)
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
                    .AddWriterGroup("WriterGroup 1", group => group
                        .WithWriterGroupId(options.WriterGroupId)
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
                            }))))
                .Build();
        }

        private static PubSubConfigurationDataType BuildSubscriberConfiguration(SampleOptions options)
        {
            return PubSubConfigurationBuilder.Create()
                .AddConnection("Subscriber Connection", connection => connection
                    .WithPublisherId(new Variant(options.PublisherId))
                    .WithTransportProfile(Profiles.PubSubUdpUadpTransport)
                    .WithAddress(options.Endpoint)
                    .AddReaderGroup("ReaderGroup 1", group => group
                        .WithMaxNetworkMessageSize(1500)
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
                                .AddField("OwnerId", (byte)DataTypes.String, DataTypeIds.String)))))
                .Build();
        }

        private static DefaultRaftConsensus BuildRaftCluster(SampleOptions options)
        {
            var memberIds = new List<ulong>(options.RaftMembers);
            for (int ii = 1; ii <= options.RaftMembers; ii++)
            {
                memberIds.Add((ulong)ii);
            }

            var transportOptions = new NanoMsgBusTransportOptions { BindAddress = options.RaftBind };
            foreach (string peer in options.RaftPeers)
            {
                transportOptions.Peers.Add(peer);
            }

#pragma warning disable CA2000
            var transport = new NanoMsgBusTransport(transportOptions);
            var storage = new MemoryStorage(new ConfState(memberIds));
            return DefaultRaftConsensus.CreateCluster(
                options.RaftId,
                transport,
                storage,
                new RaftNodeOptions { TickInterval = TimeSpan.FromMilliseconds(50) },
                config =>
                {
                    config.ElectionTick = 10;
                    config.PreVote = true;
                    config.CheckQuorum = true;
                },
                TimeSpan.FromSeconds(30));
#pragma warning restore CA2000
        }

        private static AesCbcHmacRecordProtector CreateRecordProtector(SampleOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.RecordKeyBase64))
            {
                return new AesCbcHmacRecordProtector(Convert.FromBase64String(options.RecordKeyBase64));
            }
            if (!options.Insecure)
            {
                throw new InvalidOperationException(
                    "Set HA_RECORD_KEY to a shared base64 32-byte key, or set HA_INSECURE=true for this demo.");
            }

            Console.Error.WriteLine("[HA][WARNING] HA_INSECURE=true: using a well-known non-secret demo key.");
            byte[] demoKey = SHA256.HashData(Encoding.UTF8.GetBytes("OPCFoundation/RedundantPubSub/insecure-demo"));
            return new AesCbcHmacRecordProtector(demoKey);
        }

        private static void LogComponentRoles(IServiceProvider services, string ownerId, string roleLabel)
        {
            IPubSubActivationCoordinator coordinator = services.GetRequiredService<IPubSubActivationCoordinator>();
            ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("RedundantPubSub.HA");
            coordinator.RoleChanged += (_, e) => logger.LogInformation(
                "{RoleLabel} {OwnerId}: {ComponentId} -> {Role}.",
                roleLabel,
                ownerId,
                e.ComponentId,
                e.Role);
        }

        private static void ConfigureLogging(HostApplicationBuilder builder)
        {
            builder.Logging.ClearProviders();
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
}
