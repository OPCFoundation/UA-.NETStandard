// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.Client;

namespace Opc.Ua.MachineTool.Client
{
    /// <summary>
    /// Adds typed workflows without taking ownership of an existing session.
    /// </summary>
    public static class MachineToolClientExtensions
    {
        /// <summary>
        /// Creates a typed client over an application-owned session.
        /// </summary>
        /// <param name="session">The connected session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <returns>The non-owning client.</returns>
        public static MachineToolClient CreateMachineToolClient(this ISession session, ITelemetryContext telemetry)
        {
            return new MachineToolClient(session, telemetry);
        }
    }
}
