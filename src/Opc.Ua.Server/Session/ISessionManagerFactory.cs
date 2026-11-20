/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Factory for the server's <see cref="ISessionManager"/>.
    /// </summary>
    /// <remarks>
    /// Register an implementation (e.g. via
    /// <c>StandardServer.SessionManagerFactory</c>) so the server builds a
    /// custom session manager — for example a distributed one that mirrors
    /// session state across replicas — instead of the default
    /// <see cref="SessionManager"/>. This keeps the customization additive: the
    /// default path is unchanged when no factory is supplied.
    /// </remarks>
    public interface ISessionManagerFactory
    {
        /// <summary>
        /// Creates the session manager for the server.
        /// </summary>
        /// <param name="server">The hosting server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="timeProvider">The server time provider.</param>
        /// <param name="serverCertificateProvider">
        /// Resolves the server's instance certificate for a given security
        /// policy URI (used by distributed managers to reconstruct a mirrored
        /// session). Returns a new caller-owned certificate handle that the
        /// session manager disposes after the session takes its own reference,
        /// or <c>null</c> when no certificate is configured for the policy.
        /// </param>
        /// <returns>The session manager to use.</returns>
        ISessionManager Create(
            IServerInternal server,
            ApplicationConfiguration configuration,
            TimeProvider timeProvider,
            Func<string, Certificate?> serverCertificateProvider);
    }
}
