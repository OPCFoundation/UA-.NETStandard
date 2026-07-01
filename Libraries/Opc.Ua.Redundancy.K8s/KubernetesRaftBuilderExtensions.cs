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

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Redundancy;
using Opc.Ua.Server.Hosting;
using Raft;
using Raft.Configuration;
using Raft.Storage;
using Raft.Storage.File;
using Raft.Transport.NanoMsg;

namespace Opc.Ua.Redundancy.K8s
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: fluent registration of a Kubernetes-hosted multi-node Raft consensus
    /// backend (RaftCs over NanoMsg, with an optional file WAL) on the <see cref="IOpcUaServerBuilder"/>.
    /// </summary>
    public static class KubernetesRaftBuilderExtensions
    {
        /// <summary>
        /// Registers an <see cref="IRaftConsensus"/> for a Kubernetes StatefulSet: this pod's ordinal becomes its
        /// Raft node id, peers are the other ordinals resolved through the headless Service DNS, the transport is
        /// <c>NanoMsgBusTransport</c>, and storage is a crash-safe file WAL on a PersistentVolume (or in-memory).
        /// Compose with <c>UseRedundancyConsistency</c> so the shared store and election use this consensus replica.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Consensus options (HeadlessServiceName and ReplicaCount are required).</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
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
            if (string.IsNullOrEmpty(options.HeadlessServiceName))
            {
                throw new ArgumentException("HeadlessServiceName is required.", nameof(configure));
            }

            builder.Services.TryAddSingleton<IRaftConsensus>(_ => BuildConsensus(options));
            return builder;
        }

        private static RaftCsConsensus BuildConsensus(KubernetesRaftConsensusOptions options)
        {
            string podName = string.IsNullOrEmpty(options.PodName) ? Environment.MachineName : options.PodName!;
            int ordinal = ParseOrdinal(podName);
            if (ordinal < 0 || ordinal >= options.ReplicaCount)
            {
                throw new InvalidOperationException(
                    $"Pod ordinal {ordinal} derived from '{podName}' is out of range [0,{options.ReplicaCount}).");
            }

            ulong nodeId = (ulong)(ordinal + 1);
            string statefulSet = options.StatefulSetName ?? podName.Substring(0, podName.LastIndexOf('-'));

            var memberIds = new List<ulong>(options.ReplicaCount);
            for (int ii = 0; ii < options.ReplicaCount; ii++)
            {
                memberIds.Add((ulong)(ii + 1));
            }

            var transportOptions = new NanoMsgBusTransportOptions
            {
                BindAddress = string.IsNullOrEmpty(options.BindAddress)
                    ? string.Create(CultureInfo.InvariantCulture, $"tcp://0.0.0.0:{options.RaftPort}")
                    : options.BindAddress
            };
            for (int ii = 0; ii < options.ReplicaCount; ii++)
            {
                if (ii == ordinal)
                {
                    continue;
                }
                string host = string.IsNullOrEmpty(options.Namespace)
                    ? $"{statefulSet}-{ii}.{options.HeadlessServiceName}"
                    : $"{statefulSet}-{ii}.{options.HeadlessServiceName}.{options.Namespace}.svc";
                transportOptions.Peers.Add(
                    string.Create(CultureInfo.InvariantCulture, $"tcp://{host}:{options.RaftPort}"));
            }

            // CA2000: this is a DI factory; ownership of the transport (via the
            // node) and the storage transfers to the returned adapter, which the
            // container disposes.
#pragma warning disable CA2000
            var transport = new NanoMsgBusTransport(transportOptions);

            IRaftWritableStorage storage;
            IAsyncDisposable? ownedResources;
            if (options.UseDurableStorage)
            {
                var fileStorage = new FileRaftStorage(
                    new FileRaftStorageOptions(options.StoragePath) { Fsync = options.Fsync });
                // A fresh WAL has no membership; bootstrap the static voter set.
                if (fileStorage.InitialState().ConfState.Voters.Count == 0)
                {
                    fileStorage.SetConfState(new ConfState(memberIds));
                }
                storage = fileStorage;
                ownedResources = new SyncDisposableAdapter(fileStorage);
            }
            else
            {
                storage = new MemoryStorage(new ConfState(memberIds));
                ownedResources = null;
            }

            return RaftCsConsensus.CreateCluster(
                nodeId,
                transport,
                storage,
                new RaftNodeOptions { TickInterval = options.TickInterval },
                config =>
                {
                    config.ElectionTick = options.ElectionTick;
                    config.HeartbeatTick = options.HeartbeatTick;
                    config.PreVote = options.PreVote;
                    config.CheckQuorum = options.CheckQuorum;
                },
                options.ReadyTimeout,
                ownedResources);
#pragma warning restore CA2000
        }

        private static int ParseOrdinal(string podName)
        {
            int dash = podName.LastIndexOf('-');
            if (dash < 0 || dash == podName.Length - 1 ||
                !int.TryParse(
                    podName.AsSpan(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int ordinal))
            {
                throw new InvalidOperationException(
                    $"Cannot derive a StatefulSet ordinal from pod name '{podName}'.");
            }
            return ordinal;
        }

        private sealed class SyncDisposableAdapter : IAsyncDisposable
        {
            public SyncDisposableAdapter(IDisposable inner)
            {
                m_inner = inner;
            }

            public ValueTask DisposeAsync()
            {
                m_inner.Dispose();
                return default;
            }

            private readonly IDisposable m_inner;
        }
    }
}
