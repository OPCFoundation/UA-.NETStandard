// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Client.Redundancy
{
    /// <summary>
    /// Fluent builder for a <see cref="ClientReplicaCoordinator"/>. The direct
    /// constructor remains available as a fallback for DI scenarios.
    /// </summary>
    public sealed class ClientReplicaSetBuilder
    {
        /// <summary>
        /// Creates a builder bound to a telemetry context.
        /// </summary>
        public ClientReplicaSetBuilder(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Sets the stable replica id.
        /// </summary>
        public ClientReplicaSetBuilder WithNodeId(string nodeId)
        {
            m_options = m_options with { NodeId = nodeId };
            return this;
        }

        /// <summary>
        /// Sets the follower standby behavior.
        /// </summary>
        public ClientReplicaSetBuilder WithStandbyMode(ClientStandbyMode mode)
        {
            m_options = m_options with { Mode = mode };
            return this;
        }

        /// <summary>
        /// Enables or disables token-reuse fast activation when a follower is
        /// promoted on a mirrored server.
        /// </summary>
        public ClientReplicaSetBuilder WithTokenReuse(bool enable = true)
        {
            m_options = m_options with { EnableTokenReuse = enable };
            return this;
        }

        /// <summary>
        /// Supplies a validator for a connected standby endpoint that differs from the endpoint
        /// saved in the leader handoff. The validator must confirm that both endpoints belong
        /// to the same mirrored server set.
        /// </summary>
        public ClientReplicaSetBuilder ValidateMirroredEndpoint(
            Func<EndpointDescription, EndpointDescription, CancellationToken, ValueTask<bool>> validator)
        {
            m_options = m_options with { ValidateMirroredEndpointAsync = validator };
            return this;
        }

        /// <summary>
        /// Supplies the session factory used per standby mode.
        /// </summary>
        public ClientReplicaSetBuilder UseSession(Func<CancellationToken, ValueTask<ManagedSession>> factory)
        {
            m_options = m_options with { CreateSessionAsync = factory };
            return this;
        }

        /// <summary>
        /// Supplies the leader subscription/publishing configuration.
        /// </summary>
        public ClientReplicaSetBuilder ConfigureLeader(Func<ManagedSession, bool, CancellationToken, ValueTask> configure)
        {
            m_options = m_options with { ConfigureLeaderAsync = configure };
            return this;
        }

        /// <summary>
        /// Sets the shared store, leader election, and record protector seams.
        /// </summary>
        public ClientReplicaSetBuilder UseRedundancy(
            ILeaderElection election,
            ISharedKeyValueStore store,
            IRecordProtector protector)
        {
            m_election = election;
            m_store = store;
            m_protector = protector;
            return this;
        }

        /// <summary>
        /// Builds the coordinator.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public ClientReplicaCoordinator Build()
        {
            return new ClientReplicaCoordinator(
                m_options,
                m_election ?? throw new InvalidOperationException("UseRedundancy must be called."),
                m_store ?? throw new InvalidOperationException("UseRedundancy must be called."),
                m_protector ?? NullRecordProtector.Instance,
                m_telemetry);
        }

        private readonly ITelemetryContext m_telemetry;
        private ClientReplicaOptions m_options = new();
        private ILeaderElection? m_election;
        private ISharedKeyValueStore? m_store;
        private IRecordProtector? m_protector;
    }
}
