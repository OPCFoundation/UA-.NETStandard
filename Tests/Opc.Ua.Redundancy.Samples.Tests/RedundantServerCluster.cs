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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Samples.Tests
{
    /// <summary>
    /// One replica of a <see cref="RedundantServerCluster"/>: its stable node id, the
    /// endpoint clients connect to, and the launched sample process. The launch spec is
    /// retained so the replica can be restarted after being killed during a failover test.
    /// </summary>
    internal sealed class RedundantServerReplica
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RedundantServerReplica"/> class.
        /// </summary>
        /// <param name="nodeId">The stable HA node id (for example <c>replica-a</c>).</param>
        /// <param name="port">The TCP port the replica listens on.</param>
        /// <param name="serverUrl">The discovery/endpoint URL clients connect to.</param>
        /// <param name="arguments">The command-line arguments used to launch the replica.</param>
        /// <param name="environment">The environment variables used to launch the replica.</param>
        public RedundantServerReplica(
            string nodeId,
            int port,
            string serverUrl,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string> environment)
        {
            NodeId = nodeId;
            Port = port;
            ServerUrl = serverUrl;
            Arguments = arguments;
            Environment = environment;
            Process = Launch();
        }

        /// <summary>
        /// Gets the stable HA node id of this replica.
        /// </summary>
        public string NodeId { get; }

        /// <summary>
        /// Gets the TCP port this replica listens on.
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// Gets the discovery/endpoint URL clients connect to.
        /// </summary>
        public string ServerUrl { get; }

        /// <summary>
        /// Gets the command-line arguments used to launch the replica.
        /// </summary>
        public IReadOnlyList<string> Arguments { get; }

        /// <summary>
        /// Gets the environment variables used to launch the replica.
        /// </summary>
        public IReadOnlyDictionary<string, string> Environment { get; }

        /// <summary>
        /// Gets the currently launched sample process backing this replica.
        /// </summary>
        public SampleAppProcess Process { get; private set; }

        /// <summary>
        /// Kills the current process (if running) and starts a fresh one with the same launch spec.
        /// </summary>
        /// <returns>A task that completes once a new process has been started.</returns>
        public async Task RestartAsync()
        {
            await Process.DisposeAsync().ConfigureAwait(false);
            Process = Launch();
        }

        private SampleAppProcess Launch()
        {
            return new SampleAppProcess(NodeId, "RedundantServer", "RedundantServer", Arguments, Environment);
        }
    }

    /// <summary>
    /// Manages a set of RedundantServer sample processes running on localhost as one
    /// redundant server set, used by the sample high-availability integration tests.
    /// </summary>
    internal sealed class RedundantServerCluster : IAsyncDisposable
    {
        private RedundantServerCluster(IReadOnlyList<RedundantServerReplica> replicas, string pkiRoot)
        {
            Replicas = replicas;
            m_pkiRoot = pkiRoot;
        }

        /// <summary>
        /// Gets the replicas that make up the cluster.
        /// </summary>
        public IReadOnlyList<RedundantServerReplica> Replicas { get; }

        /// <summary>
        /// Gets the endpoint URL of the first replica, used as the client's bootstrap server URL.
        /// </summary>
        public string BootstrapServerUrl => Replicas[0].ServerUrl;

        /// <summary>
        /// Finds the replica with the given node id, or <c>null</c> when none matches.
        /// </summary>
        /// <param name="nodeId">The node id to search for.</param>
        /// <returns>The matching replica, or <c>null</c>.</returns>
        public RedundantServerReplica? FindByNodeId(string nodeId)
        {
            return Replicas.FirstOrDefault(
                replica => string.Equals(replica.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the replicas whose process is still running.
        /// </summary>
        /// <returns>The live replicas.</returns>
        public IReadOnlyList<RedundantServerReplica> LiveReplicas()
        {
            return [.. Replicas.Where(replica => !replica.Process.HasExited)];
        }

        /// <summary>
        /// Starts a strongly-consistent (Raft), active/passive RedundantServer cluster on localhost.
        /// </summary>
        /// <param name="count">The number of replicas (an odd Raft quorum, for example 3).</param>
        /// <param name="startupTimeout">How long to wait for each replica to report it is listening.</param>
        /// <param name="cancellationToken">A token used to cancel startup.</param>
        /// <returns>The started cluster.</returns>
        public static async Task<RedundantServerCluster> StartStrongAsync(
            int count,
            TimeSpan startupTimeout,
            CancellationToken cancellationToken = default)
        {
            int[] ports = TestPorts.GetFreePorts(count);
            int[] raftPorts = TestPorts.GetFreePorts(count);
            string pkiRoot = CreateFreshPkiRoot();
            string[] nodeIds = new string[count];
            string[] raftBinds = new string[count];
            for (int i = 0; i < count; i++)
            {
                nodeIds[i] = "replica-" + (char)('a' + i);
                raftBinds[i] = string.Format(CultureInfo.InvariantCulture, "tcp://127.0.0.1:{0}", raftPorts[i]);
            }

            var replicas = new List<RedundantServerReplica>(count);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var environment = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["HA_MODE"] = "ap",
                        ["HA_CONSISTENCY"] = "strong",
                        ["REDUNDANCY_MODE"] = "hot",
                        ["HA_INSECURE"] = "true",
                        ["HA_HOST"] = "127.0.0.1",
                        ["HA_NODE_ID"] = nodeIds[i],
                        ["HA_PKI_ROOT"] = Path.Combine(pkiRoot, nodeIds[i]),
                        ["HA_RAFT_ID"] = (i + 1).ToString(CultureInfo.InvariantCulture),
                        ["HA_RAFT_MEMBERS"] = count.ToString(CultureInfo.InvariantCulture),
                        ["HA_RAFT_BIND"] = raftBinds[i],
                        ["HA_RAFT_PEERS"] = string.Join(",", PeersExcept(raftBinds, i)),
                        ["HA_REDUNDANT_PEERS"] = BuildRedundantPeers(nodeIds, ports, i)
                    };

                    replicas.Add(new RedundantServerReplica(
                        nodeIds[i],
                        ports[i],
                        string.Format(CultureInfo.InvariantCulture, "opc.tcp://127.0.0.1:{0}/RedundantServer", ports[i]),
                        ["--port", ports[i].ToString(CultureInfo.InvariantCulture)],
                        environment));
                }

                foreach (RedundantServerReplica replica in replicas)
                {
                    await WaitUntilListeningAsync(replica.Process, startupTimeout, cancellationToken)
                        .ConfigureAwait(false);
                }

                return new RedundantServerCluster(replicas, pkiRoot);
            }
            catch
            {
                foreach (RedundantServerReplica replica in replicas)
                {
                    await replica.Process.DisposeAsync().ConfigureAwait(false);
                }

                throw;
            }
        }

        /// <summary>
        /// Starts a single-node, eventual-consistency, active/passive RedundantServer on localhost. This
        /// is the lightweight, dependency-free setup used by the short-haul connectivity smoke test.
        /// </summary>
        /// <param name="startupTimeout">How long to wait for the replica to report it is listening.</param>
        /// <param name="cancellationToken">A token used to cancel startup.</param>
        /// <returns>The started single-node cluster.</returns>
        public static async Task<RedundantServerCluster> StartSingleEventualAsync(
            TimeSpan startupTimeout,
            CancellationToken cancellationToken = default)
        {
            int port = TestPorts.GetFreePort();
            string pkiRoot = CreateFreshPkiRoot();
            var environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HA_MODE"] = "ap",
                ["HA_CONSISTENCY"] = "eventual",
                ["REDUNDANCY_MODE"] = "hot",
                ["HA_HOST"] = "127.0.0.1",
                ["HA_NODE_ID"] = "solo",
                ["HA_PKI_ROOT"] = Path.Combine(pkiRoot, "solo")
            };

            var replica = new RedundantServerReplica(
                "solo",
                port,
                string.Format(CultureInfo.InvariantCulture, "opc.tcp://127.0.0.1:{0}/RedundantServer", port),
                ["--port", port.ToString(CultureInfo.InvariantCulture)],
                environment);
            try
            {
                await WaitUntilListeningAsync(replica.Process, startupTimeout, cancellationToken).ConfigureAwait(false);
                return new RedundantServerCluster([replica], pkiRoot);
            }
            catch
            {
                await replica.Process.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Restarts a replica that was killed during a failover test and waits for it to begin listening,
        /// so the Raft quorum is restored before the next failover cycle.
        /// </summary>
        /// <param name="replica">The replica to restart.</param>
        /// <param name="startupTimeout">How long to wait for the replica to report it is listening.</param>
        /// <param name="cancellationToken">A token used to cancel the restart.</param>
        /// <returns>A task that completes once the replica is listening again.</returns>
        public static async Task RestartReplicaAsync(
            RedundantServerReplica replica,
            TimeSpan startupTimeout,
            CancellationToken cancellationToken = default)
        {
            await replica.RestartAsync().ConfigureAwait(false);
            await WaitUntilListeningAsync(replica.Process, startupTimeout, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            foreach (RedundantServerReplica replica in Replicas)
            {
                await replica.Process.DisposeAsync().ConfigureAwait(false);
            }

            try
            {
                if (Directory.Exists(m_pkiRoot))
                {
                    Directory.Delete(m_pkiRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of the throwaway per-run PKI store.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup of the throwaway per-run PKI store.
            }
        }

        private static string CreateFreshPkiRoot()
        {
            string root = Path.Combine(
                Path.GetTempPath(), "opcua-ha-sample-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static async Task WaitUntilListeningAsync(
            SampleAppProcess process,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (process.ContainsLine("listening at"))
                {
                    return;
                }

                if (process.ContainsLine("Failed to establish tcp listener sockets") ||
                    process.ContainsLine("Failed to create IPv4 listening socket"))
                {
                    throw new InvalidOperationException(
                        $"Sample server '{process.Name}' failed to bind its listening socket.");
                }

                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Sample server '{process.Name}' exited before it began listening.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException(
                $"Sample server '{process.Name}' did not report it was listening within {timeout}.");
        }

        private static IEnumerable<string> PeersExcept(string[] values, int index)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i != index)
                {
                    yield return values[i];
                }
            }
        }

        private static string BuildRedundantPeers(string[] nodeIds, int[] ports, int index)
        {
            var entries = new List<string>(nodeIds.Length - 1);
            for (int i = 0; i < nodeIds.Length; i++)
            {
                if (i == index)
                {
                    continue;
                }

                entries.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "urn:localhost:OPCFoundation:RedundantServer:{0}|RedundantServer {0}|opc.tcp://127.0.0.1:{1}/RedundantServer",
                    nodeIds[i],
                    ports[i]));
            }

            return string.Join(",", entries);
        }
        private readonly string m_pkiRoot;
    }
}
