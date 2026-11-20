/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Routes the load-direction <b>eligibility</b> keyspaces (health <c>ServiceLevel</c> and the endpoint directory)
    /// to the linearizable store when <see cref="LoadDirectionOptions.StrongEligibility"/> is set; the high-churn load
    /// keyspace is deliberately left eventual.
    /// </summary>
    internal sealed class LoadDirectionStrongKeyspaceProvider : IStrongKeyspaceProvider
    {
        /// <summary>
        /// Creates a provider for the strongly consistent load-direction eligibility key prefixes.
        /// </summary>
        /// <param name="options">The load-direction options that define the key prefixes.</param>
        public LoadDirectionStrongKeyspaceProvider(LoadDirectionOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public ArrayOf<string> GetStrongKeyPrefixes()
        {
            return [m_options.ServiceLevelKeyPrefix, m_options.EndpointKeyPrefix];
        }

        private readonly LoadDirectionOptions m_options;
    }
}
