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
using Opc.Ua.Client;
using Opc.Ua.Client.Redundancy;

namespace Opc.Ua.Redundancy.Client
{
    /// <summary>
    /// Fluent non-DI builder for a transparent redundant client session facade.
    /// </summary>
    public sealed class RedundantClientSessionBuilder
    {
        /// <summary>
        /// Creates a builder bound to a telemetry context.
        /// </summary>
        public RedundantClientSessionBuilder(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Sets the stable replica id.
        /// </summary>
        public RedundantClientSessionBuilder WithNodeId(string nodeId)
        {
            m_options = m_options with { NodeId = nodeId };
            return this;
        }

        /// <summary>
        /// Sets the follower standby behavior.
        /// </summary>
        public RedundantClientSessionBuilder WithStandbyMode(ClientStandbyMode mode)
        {
            m_options = m_options with { Mode = mode };
            return this;
        }

        /// <summary>
        /// Enables or disables token-reuse fast activation when a follower is
        /// promoted on a mirrored server.
        /// </summary>
        public RedundantClientSessionBuilder WithTokenReuse(bool enable = true)
        {
            m_options = m_options with { EnableTokenReuse = enable };
            return this;
        }

        /// <summary>
        /// Supplies the session factory used per standby mode.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public RedundantClientSessionBuilder UseSession(Func<CancellationToken, ValueTask<ManagedSession>> factory)
        {
            m_options = m_options with
            {
                CreateSessionAsync = factory ?? throw new ArgumentNullException(nameof(factory)),
            };
            return this;
        }

        /// <summary>
        /// Supplies the leader subscription/publishing configuration.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public RedundantClientSessionBuilder ConfigureLeader(
            Func<ManagedSession, bool, CancellationToken, ValueTask> configure
        )
        {
            m_options = m_options with
            {
                ConfigureLeaderAsync = configure ?? throw new ArgumentNullException(nameof(configure)),
            };
            return this;
        }

        /// <summary>
        /// Sets the shared store, leader election, and record protector seams.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public RedundantClientSessionBuilder UseRedundancy(
            ILeaderElection election,
            ISharedKeyValueStore store,
            IRecordProtector? protector = null
        )
        {
            m_election = election ?? throw new ArgumentNullException(nameof(election));
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_protector = protector ?? NullRecordProtector.Instance;
            return this;
        }

        /// <summary>
        /// Builds the redundant client session facade.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public RedundantClientSession Build()
        {
#pragma warning disable CA2000
            // Ownership transfers to RedundantClientSession, which disposes the coordinator in DisposeAsync.
            // TODO: remove this suppression if CA2000 learns this ownership transfer pattern.
            var coordinator = new ClientReplicaCoordinator(
                m_options,
                m_election ?? throw new InvalidOperationException("UseRedundancy must be called."),
                m_store ?? throw new InvalidOperationException("UseRedundancy must be called."),
                m_protector ?? NullRecordProtector.Instance,
                m_telemetry
            );
#pragma warning restore CA2000
            return new RedundantClientSession(coordinator);
        }

        private readonly ITelemetryContext m_telemetry;
        private ClientReplicaOptions m_options = new();
        private ILeaderElection? m_election;
        private ISharedKeyValueStore? m_store;
        private IRecordProtector? m_protector;
    }
}
