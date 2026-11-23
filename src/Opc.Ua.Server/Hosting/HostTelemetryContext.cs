// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// <see cref="ITelemetryContext"/> implementation that adapts the host's
    /// <see cref="ILoggerFactory"/> so a Generic-Host application has a single
    /// logging pipeline shared between user code and the OPC UA stack.
    /// </summary>
    /// <remarks>
    /// The host owns the lifetime of the supplied <see cref="ILoggerFactory"/>;
    /// this adapter does not dispose it.
    /// </remarks>
    public sealed class HostTelemetryContext : TelemetryContextBase
    {
        /// <summary>
        /// Creates a new <see cref="HostTelemetryContext"/> bound to the given
        /// <see cref="ILoggerFactory"/>.
        /// </summary>
        public HostTelemetryContext(ILoggerFactory loggerFactory)
            : base(loggerFactory)
        {
        }
    }
}
