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
