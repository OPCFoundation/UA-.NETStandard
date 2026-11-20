// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Host-supplied evidence that this exact session and target belong to a live,
    /// verified owned simulation, not physical weighing equipment. No implementation
    /// is installed by default. Loopback, server names and model namespaces are not evidence.
    /// </summary>
    internal interface IWeighingSimulationPolicy
    {
        /// <summary>
        /// Authorizes the operation against the owned process lifecycle and authenticated
        /// server identity, or throws. Called at preparation and immediately before dispatch.
        /// This does not replace deployment authorization or a fresh user confirmation.
        /// </summary>
        ValueTask<WeighingSimulationGrant> AuthorizeAsync(
            CompanionContext context,
            CompanionTarget target,
            string operationId,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Non-secret identity of the verified owned run, policy revision and fixed expiry.
    /// Authorizations for the same run must retain its original deadline, not extend it.
    /// Restarting or revoking a run must invalidate the grant returned by the policy.
    /// </summary>
    internal sealed record WeighingSimulationGrant(Guid RunId, string Revision, DateTimeOffset ExpiresAt);
}
