/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Client.AliasNames
{
    /// <summary>
    /// Dependency-injected factory for per-session alias-name clients.
    /// </summary>
    public sealed class AliasNameClientFactory
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        public AliasNameClientFactory(ITelemetryContext telemetry)
        {
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Creates a client rooted at <paramref name="categoryId"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public AliasNameClient Create(
            ISession session,
            NodeId categoryId,
            AliasNameClientOptions? options = null)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            return new AliasNameClient(session, categoryId, options);
        }

        /// <summary>
        /// Opens the standard Aliases category.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public AliasNameClient OpenStandardAliases(
            ISession session,
            AliasNameClientOptions? options = null)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            return AliasNameClient.OpenStandardAliases(session, options);
        }

        /// <summary>
        /// The shared telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }
    }
}
