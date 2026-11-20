// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Dependency-injected factory that produces a fresh
    /// <see cref="ComplexTypeSystem"/> bound to a caller-supplied
    /// <see cref="ISession"/> and the host's
    /// <see cref="ITelemetryContext"/>.
    /// </summary>
    /// <remarks>
    /// Registered as a singleton by
    /// <c>IOpcUaBuilder.AddComplexTypes()</c>. Consumers (typically
    /// <c>ManagedSession</c> hosts) resolve this factory and call
    /// <see cref="Create(ISession)"/> per session to obtain a
    /// type-loader scoped to that session.
    /// </remarks>
    public sealed class ComplexTypeSystemFactory : IComplexTypeSystemFactory
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="telemetry">The shared telemetry context.</param>
        /// <exception cref="ArgumentNullException"><paramref name="telemetry"/> is <c>null</c>.</exception>
        public ComplexTypeSystemFactory(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Creates a new <see cref="ComplexTypeSystem"/> bound to
        /// <paramref name="session"/> and the host's
        /// <see cref="ITelemetryContext"/>.
        /// </summary>
        /// <param name="session">The client session to load custom
        /// types for.</param>
        /// <returns>A fresh <see cref="ComplexTypeSystem"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
        public ComplexTypeSystem Create(ISession session)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
#pragma warning disable CA2000 // ownership of the resolver transfers to the type system, which disposes it
            return new ComplexTypeSystem(
                new NodeCacheResolver(session, m_telemetry),
                new ComplexTypeBuilderFactory(),
                m_telemetry,
                ownsResolver: true);
#pragma warning restore CA2000
        }

        private readonly ITelemetryContext m_telemetry;
    }
}
