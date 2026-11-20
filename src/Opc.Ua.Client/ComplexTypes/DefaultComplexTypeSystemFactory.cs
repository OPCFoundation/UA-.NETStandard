/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Default complex-type system factory backed by
    /// <see cref="DefaultComplexTypeFactory"/>.
    /// </summary>
    public sealed class DefaultComplexTypeSystemFactory : IComplexTypeSystemFactory
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        public DefaultComplexTypeSystemFactory(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <inheritdoc/>
        public ComplexTypeSystem Create(ISession session)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }

#pragma warning disable CA2000 // ownership of the resolver transfers to the type system, which disposes it
            return new ComplexTypeSystem(
                new NodeCacheResolver(session, m_telemetry),
                new DefaultComplexTypeFactory(),
                m_telemetry,
                ownsResolver: true);
#pragma warning restore CA2000
        }

        private readonly ITelemetryContext m_telemetry;
    }
}
