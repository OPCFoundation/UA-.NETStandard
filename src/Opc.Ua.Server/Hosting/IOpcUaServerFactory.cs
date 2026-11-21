// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Creates the <see cref="StandardServer"/> instance used by the
    /// regular hosted OPC UA server.
    /// </summary>
    public interface IOpcUaServerFactory
    {
        /// <summary>
        /// Creates a server instance for the hosted service.
        /// </summary>
        /// <param name="telemetry">Telemetry context used by the server.</param>
        /// <param name="timeProvider">Time provider used by the server.</param>
        /// <returns>The server instance to start.</returns>
        StandardServer CreateServer(ITelemetryContext telemetry, TimeProvider timeProvider);
    }
}
