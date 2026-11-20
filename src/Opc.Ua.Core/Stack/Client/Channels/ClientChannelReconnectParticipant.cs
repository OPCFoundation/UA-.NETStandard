// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Minimal reconnect participant used by service clients that do not own session state.
    /// </summary>
    internal sealed class ClientChannelReconnectParticipant : IReconnectParticipant
    {
        /// <summary>
        /// Initializes a participant for a client sharing a managed channel.
        /// </summary>
        /// <param name="idPrefix">The client-specific identifier prefix.</param>
        /// <param name="endpoint">The endpoint used to compute the managed channel key.</param>
        public ClientChannelReconnectParticipant(string idPrefix, ConfiguredEndpoint endpoint)
        {
            if (string.IsNullOrEmpty(idPrefix))
            {
                throw new ArgumentNullException(nameof(idPrefix));
            }

            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            Id = idPrefix + "-" + Guid.NewGuid().ToString("N");
        }

        /// <inheritdoc/>
        public string Id { get; }

        /// <inheritdoc/>
        public ConfiguredEndpoint Endpoint { get; }

        /// <inheritdoc/>
        public IRetryBudget? CreateReconnectBudget(TimeProvider timeProvider)
        {
            return null;
        }

        /// <inheritdoc/>
        public ValueTask<ParticipantReconnectResult> OnReconnectAsync(
            IManagedTransportChannel channel,
            int reconnectAttempt,
            CancellationToken ct)
        {
            return new ValueTask<ParticipantReconnectResult>(ParticipantReconnectResult.Reactivated);
        }
    }
}
