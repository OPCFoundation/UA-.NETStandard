// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Describes the outcome of a remote ConfigurationFile update.
    /// </summary>
    public enum ConfigurationFileUpdateOutcome
    {
        /// <summary>
        /// The server applied the update and did not request confirmation.
        /// </summary>
        Applied,

        /// <summary>
        /// The server applied the update and accepted its confirmation.
        /// </summary>
        Confirmed,

        /// <summary>
        /// The server applied the update, but the client did not confirm it.
        /// </summary>
        ConfirmationPending,

        /// <summary>
        /// The client did not receive enough information to determine whether the update or confirmation completed.
        /// </summary>
        OutcomeUnknown,

        /// <summary>
        /// The server rejected the update.
        /// </summary>
        Rejected
    }

    /// <summary>
    /// Reports the remote status, target results and confirmation state of a ConfigurationFile update.
    /// </summary>
    /// <param name="Outcome">The known outcome category.</param>
    /// <param name="NewVersion">The version returned by the server, or zero when unknown.</param>
    /// <param name="UpdateId">The exact update identifier returned by the server, or <see cref="Uuid.Empty"/>.</param>
    /// <param name="UpdateResults">The per-target status results returned by the server.</param>
    /// <param name="StatusCode">The last known service status.</param>
    public readonly record struct ConfigurationFileUpdateResult(
        ConfigurationFileUpdateOutcome Outcome,
        uint NewVersion,
        Uuid UpdateId,
        ArrayOf<StatusCode> UpdateResults,
        StatusCode StatusCode);
}
