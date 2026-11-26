// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Raft;
using Raft.Configuration;
using Raft.Storage.File;

namespace Opc.Ua.Redundancy.Kubernetes
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: fluent registration of a Kubernetes-hosted multi-node Raft consensus
    /// backend (RaftCs over mutually authenticated TLS, with a durable file WAL) on the
    /// <see cref="IOpcUaServerBuilder"/>.
    /// </summary>
    public static class KubernetesRaftBuilderExtensions
    {
        /// <summary>
        /// Registers an <see cref="IRaftConsensus"/> for a Kubernetes StatefulSet: this pod's ordinal becomes its
        /// Raft node id, each peer is pinned to its provisioned TLS leaf, and storage is a crash-safe file WAL
        /// on a PersistentVolume. Credentials and peer routes are resolved asynchronously before serving requests.
        /// Compose with <c>UseRedundancyConsistency</c> so the shared store and election use this consensus replica.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">The static membership, certificate resolver, and deployment settings.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public static IOpcUaServerBuilder UseKubernetesRaftConsensus(
            this IOpcUaServerBuilder builder,
            Action<KubernetesRaftConsensusOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new KubernetesRaftConsensusOptions();
            configure?.Invoke(options);

            if (options.ReplicaCount < 1)
            {
                throw new ArgumentException("ReplicaCount must be at least 1.", nameof(configure));
            }
            if (options.ReadyTimeout <= TimeSpan.Zero ||
                options.ReadyTimeout > TimeSpan.FromMilliseconds(int.MaxValue))
            {
                throw new ArgumentException("ReadyTimeout must be a positive startup deadline.", nameof(configure));
            }

            if (options.UseInProcessTestTransport)
            {
                if (options.ReplicaCount != 1)
                {
                    throw new ArgumentException("The in-process test backend requires one replica.", nameof(configure));
                }
            }
            else
            {
                ValidateNetworkOptions(options);
            }

            builder.Services.RemoveAll<IRaftConsensus>();
            builder.Services.AddSingleton<IRaftConsensus>(sp => BuildConsensus(sp, options));
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IServerPreStartupTask, RaftStartupTask>());
            return builder;
        }

        private static void ValidateNetworkOptions(KubernetesRaftConsensusOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.HeadlessServiceName))
            {
                throw new ArgumentException("HeadlessServiceName is required.");
            }
            if (options.ResolveMemberCertificateAsync == null)
            {
                throw new ArgumentException("ResolveMemberCertificateAsync is required for mutual TLS.");
            }
            if (options.TransportOptions == null ||
                options.TransportOptions.MaxFrameLength < KubernetesRaftConsensusOptions.kMinimumTransportFrameLength)
            {
                throw new ArgumentException("TransportOptions must allow a 16 MiB Raft frame.");
            }
            if (!options.UseDurableStorage || string.IsNullOrWhiteSpace(options.StoragePath))
            {
                throw new ArgumentException("Network Raft requires a durable WAL directory.");
            }
            if (options.RaftPort is <= IPEndPoint.MinPort or > IPEndPoint.MaxPort)
            {
                throw new ArgumentException("RaftPort must be between 1 and 65535.");
            }
        }

        private static DefaultRaftConsensus BuildConsensus(
            IServiceProvider services,
            KubernetesRaftConsensusOptions options)
        {
            if (options.UseInProcessTestTransport)
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")) ||
                    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("POD_IP")) ||
                    services.GetService<IKubernetesApiClient>() is { IsInCluster: true })
                {
                    throw new InvalidOperationException("The in-process Raft test backend cannot run in Kubernetes.");
                }
                return DefaultRaftConsensus.CreateSingleNode(readyTimeout: options.ReadyTimeout);
            }

            string podName = string.IsNullOrEmpty(options.PodName) ? Environment.MachineName : options.PodName;
            int ordinal = ParseOrdinal(podName);
            if (ordinal >= options.ReplicaCount)
            {
                throw new InvalidOperationException(
                    $"Pod ordinal {ordinal} derived from '{podName}' is out of range [0,{options.ReplicaCount}).");
            }

            ulong nodeId = (ulong)(ordinal + 1);
            string statefulSet = options.StatefulSetName ??
                podName[..podName.LastIndexOf('-', StringComparison.Ordinal)];
            IPEndPoint listenEndPoint = ResolveListenEndPoint(options);
            var memberIds = new ulong[options.ReplicaCount];
            for (int ii = 0; ii < memberIds.Length; ii++)
            {
                memberIds[ii] = (ulong)(ii + 1);
            }
            var configuredMembership = new ConfState(memberIds);

            var fileStorage = new FileRaftStorage(
                new FileRaftStorageOptions(options.StoragePath) { Fsync = options.Fsync });
            try
            {
                ConfState storedMembership = fileStorage.InitialState().ConfState;
                if (storedMembership.Equals(new ConfState()))
                {
                    fileStorage.SetConfState(configuredMembership);
                }
                else if (!storedMembership.Equals(configuredMembership))
                {
                    throw new InvalidOperationException("The Raft WAL membership does not match the StatefulSet.");
                }

                // TODO: Remove this suppression when ownership transfer is modeled by CA2000.
                // The node owns the deferred transport; the consensus adapter owns the WAL.
#pragma warning disable CA2000
                return DefaultRaftConsensus.CreateCluster(
                    nodeId,
                    new KubernetesRaftTransport(services, options, listenEndPoint, statefulSet, nodeId),
                    fileStorage,
                    new RaftNodeOptions
                    {
                        TickInterval = options.TickInterval,
                        MaxIncomingMessageBytes = options.TransportOptions.MaxFrameLength
                    },
                    config =>
                    {
                        config.ElectionTick = options.ElectionTick;
                        config.HeartbeatTick = options.HeartbeatTick;
                        config.PreVote = options.PreVote;
                        config.CheckQuorum = options.CheckQuorum;
                    },
                    options.ReadyTimeout,
                    new SyncDisposableAdapter(fileStorage));
#pragma warning restore CA2000
            }
            catch
            {
                fileStorage.Dispose();
                throw;
            }
        }

        private static IPEndPoint ResolveListenEndPoint(KubernetesRaftConsensusOptions options)
        {
            if (options.BindAddress is string bindAddress)
            {
                const string tcpPrefix = "tcp://";
                string address = bindAddress.StartsWith(tcpPrefix, StringComparison.OrdinalIgnoreCase)
                    ? bindAddress[tcpPrefix.Length..]
                    : bindAddress;
                if (IPAddress.TryParse(address, out IPAddress? bindIp))
                {
                    return new IPEndPoint(bindIp, options.RaftPort);
                }
                if (IPEndPoint.TryParse(address, out IPEndPoint? endPoint) && endPoint.Port != 0)
                {
                    return endPoint;
                }
                throw new InvalidOperationException("BindAddress must specify a listener IP address.");
            }

            if (!IPAddress.TryParse(Environment.GetEnvironmentVariable("POD_IP"), out IPAddress? podIp) ||
                podIp.Equals(IPAddress.Any) || podIp.Equals(IPAddress.IPv6Any))
            {
                throw new InvalidOperationException("Configure the POD_IP environment variable or BindAddress.");
            }
            return new IPEndPoint(podIp, options.RaftPort);
        }

        private static int ParseOrdinal(string podName)
        {
            int dash = podName.LastIndexOf('-', StringComparison.Ordinal);
            if (dash <= 0 ||
                dash == podName.Length - 1 ||
                !int.TryParse(
                    podName.AsSpan(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int ordinal))
            {
                throw new InvalidOperationException(
                    $"Cannot derive a StatefulSet ordinal from pod name '{podName}'.");
            }
            return ordinal;
        }

        /// <summary>
        /// Awaits Raft startup before the server starts its node managers or exposes endpoints.
        /// </summary>
        private sealed class RaftStartupTask(IRaftConsensus consensus) : IServerPreStartupTask
        {
            /// <inheritdoc/>
            public ValueTask OnServerStartingAsync(
                IServerContext server,
                CancellationToken cancellationToken = default)
            {
                return consensus.StartAsync(cancellationToken);
            }
        }

        /// <summary>
        /// Adapts a synchronous <see cref="IDisposable"/> so it can be disposed through the
        /// <see cref="IAsyncDisposable"/> contract.
        /// </summary>
        private sealed class SyncDisposableAdapter : IAsyncDisposable
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="SyncDisposableAdapter"/> class.
            /// </summary>
            /// <param name="inner">The synchronous disposable to wrap.</param>
            public SyncDisposableAdapter(IDisposable inner)
            {
                m_inner = inner;
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                m_inner.Dispose();
                return default;
            }

            private readonly IDisposable m_inner;
        }
    }
}
