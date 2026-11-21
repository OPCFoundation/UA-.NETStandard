// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Client.Alarms
{
    /// <summary>
    /// Dependency-injected factory that produces a fresh
    /// <see cref="AlarmClient"/> bound to a caller-supplied
    /// <see cref="ISessionClient"/> (typically the connected
    /// <see cref="ManagedSession"/>) and the host's
    /// <see cref="ITelemetryContext"/>.
    /// </summary>
    /// <remarks>
    /// Registered as a singleton by the
    /// <c>IOpcUaBuilder.AddAlarms()</c> fluent extension. Consumers
    /// resolve this factory and call <see cref="Create(ISessionClient)"/>
    /// per session to obtain a Part 9 alarm/condition client scoped to
    /// that session. The factory mirrors
    /// <c>ComplexTypeSystemFactory</c> and is the recommended path for
    /// DI-hosted client applications; the
    /// <c>AlarmClientSessionExtensions.GetAlarmClient</c> extension
    /// remains as the non-DI fallback.
    /// </remarks>
    public sealed class AlarmClientFactory
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="telemetry">The shared telemetry context. Held
        /// for future use (logger / activity source plumbing into
        /// <see cref="AlarmClient"/>) so registering this factory does
        /// not require a future API churn when telemetry surfaces are
        /// added to the alarm client.</param>
        /// <exception cref="ArgumentNullException"><paramref name="telemetry"/>
        /// is <c>null</c>.</exception>
        public AlarmClientFactory(ITelemetryContext telemetry)
        {
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Creates a new <see cref="AlarmClient"/> bound to
        /// <paramref name="session"/>.
        /// </summary>
        /// <param name="session">The client session to invoke
        /// Part 9 condition / alarm / dialog methods through.</param>
        /// <returns>A fresh <see cref="AlarmClient"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="session"/>
        /// is <c>null</c>.</exception>
        public AlarmClient Create(ISessionClient session)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            return new AlarmClient(session, Telemetry);
        }

        /// <summary>
        /// Returns the shared <see cref="ITelemetryContext"/> the
        /// factory was initialized with. Exposed for derived
        /// scenarios; the standard <see cref="Create(ISessionClient)"/>
        /// path threads it implicitly.
        /// </summary>
        public ITelemetryContext Telemetry { get; }
    }
}
