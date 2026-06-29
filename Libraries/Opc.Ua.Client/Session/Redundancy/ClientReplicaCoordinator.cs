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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Client.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a client replica set. Two or more client
    /// processes elect a leader (via <see cref="ILeaderElection"/>) that holds the
    /// active <see cref="ManagedSession"/> and subscriptions; followers stand by per
    /// <see cref="ClientStandbyMode"/> and take over on leader loss. The leader
    /// publishes its protected session secrets through an
    /// <see cref="ISharedKeyValueStore"/> so a promoted follower can reuse the
    /// AuthenticationToken to ActivateSession against a HotAndMirrored server, or
    /// recreate the session and transfer subscriptions otherwise.
    /// </summary>
    public sealed class ClientReplicaCoordinator : IAsyncDisposable
    {
        /// <summary>
        /// Creates a client replica coordinator.
        /// </summary>
        public ClientReplicaCoordinator(
            ClientReplicaOptions options,
            ILeaderElection election,
            ISharedKeyValueStore sessionStore,
            IRecordProtector recordProtector,
            ITelemetryContext telemetry)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_election = election ?? throw new ArgumentNullException(nameof(election));
            m_store = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            m_protector = recordProtector ?? throw new ArgumentNullException(nameof(recordProtector));
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (options.CreateSessionAsync == null)
            {
                throw new ArgumentException(
                    "ClientReplicaOptions.CreateSessionAsync must be set.", nameof(options));
            }

            // Fail-closed: secrets must not be written in cleartext to a networked store.
            if (sessionStore is not InMemorySharedKeyValueStore && recordProtector is NullRecordProtector)
            {
                throw new InvalidOperationException(
                    "A non-in-memory session store requires a real IRecordProtector; refusing to mirror " +
                    "client session secrets in cleartext.");
            }

            m_logger = telemetry.CreateLogger<ClientReplicaCoordinator>();
            m_election.LeadershipChanged += OnLeadershipChanged;
        }

        /// <summary>
        /// True when this replica is the active leader.
        /// </summary>
        public bool IsLeader => m_election.IsLeader;

        /// <summary>
        /// The active managed session, or null when this replica has none yet.
        /// </summary>
        public ManagedSession? CurrentSession => m_session;

        /// <summary>
        /// Raised after this replica's role changes; true when promoted to leader.
        /// </summary>
        public event Action<bool>? RoleChanged;

        /// <summary>
        /// Starts election and, for Warm/Hot standby, connects the standby session.
        /// </summary>
        public async ValueTask StartAsync(CancellationToken ct = default)
        {
            if (m_options.Mode != ClientStandbyMode.Cold)
            {
                m_session = await m_options.CreateSessionAsync!(ct).ConfigureAwait(false);
            }
            m_election.Start();
        }

        private void OnLeadershipChanged(bool isLeader)
        {
            _ = Task.Run(() => HandleRoleChangeAsync(isLeader));
        }

        private async Task HandleRoleChangeAsync(bool isLeader)
        {
            try
            {
                if (isLeader)
                {
                    bool fastActivated = await EnsureLeaderSessionAsync(m_cts.Token).ConfigureAwait(false);
                    if (m_options.ConfigureLeaderAsync != null && m_session != null)
                    {
                        await m_options.ConfigureLeaderAsync(m_session, fastActivated, m_cts.Token)
                            .ConfigureAwait(false);
                    }
                    await PublishSecretsAsync(m_cts.Token).ConfigureAwait(false);
                }
                else if (m_options.Mode == ClientStandbyMode.Cold && m_session != null)
                {
                    ManagedSession demoted = m_session;
                    m_session = null;
                    await demoted.DisposeAsync().ConfigureAwait(false);
                }
                RoleChanged?.Invoke(isLeader);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Client replica role change to leader={IsLeader} failed.", isLeader);
            }
        }

        private async ValueTask<bool> EnsureLeaderSessionAsync(CancellationToken ct)
        {
            m_session ??= await m_options.CreateSessionAsync!(ct).ConfigureAwait(false);
            if (!m_options.EnableTokenReuse)
            {
                return false;
            }

            try
            {
                (bool found, ByteString stored) = await m_store
                    .TryGetAsync(m_options.SessionRecordKey, ct).ConfigureAwait(false);
                if (found && m_protector.TryUnprotect(stored, out ByteString plaintext) && !plaintext.IsNull)
                {
                    using var decoder = new BinaryDecoder(plaintext.ToArray(), m_session.MessageContext);
                    var config = decoder.ReadEncodeable<SessionConfiguration>(null);
                    if (m_session.ApplySessionConfiguration(config))
                    {
                        await m_session.ReactivateMirroredSessionAsync(m_session.ConfiguredEndpoint, ct)
                            .ConfigureAwait(false);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                m_logger.LogInformation(ex, "Token-reuse fast-activate failed; using a fresh session.");
            }
            return false;
        }

        private async ValueTask PublishSecretsAsync(CancellationToken ct)
        {
            if (m_session == null)
            {
                return;
            }
            using var stream = new System.IO.MemoryStream();
            m_session.SaveSessionConfiguration(stream);
            ByteString protectedRecord = m_protector.Protect(new ByteString(stream.ToArray()));
            await m_store.SetAsync(m_options.SessionRecordKey, protectedRecord, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_election.LeadershipChanged -= OnLeadershipChanged;
            m_cts.Cancel();
            await m_election.DisposeAsync().ConfigureAwait(false);
            if (m_session != null)
            {
                await m_session.DisposeAsync().ConfigureAwait(false);
                m_session = null;
            }
            m_cts.Dispose();
        }

        private readonly ClientReplicaOptions m_options;
        private readonly ILeaderElection m_election;
        private readonly ISharedKeyValueStore m_store;
        private readonly IRecordProtector m_protector;
        private readonly ILogger m_logger;
        private readonly CancellationTokenSource m_cts = new();
        private ManagedSession? m_session;
    }
}
