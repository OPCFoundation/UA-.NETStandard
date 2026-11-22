// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Programs
{
    /// <summary>
    /// Direct construction and focused helpers for generated Program proxies.
    /// </summary>
    public static class ProgramClientExtensions
    {
        /// <summary>
        /// Creates a Program client over a caller-owned session without requiring DI.
        /// </summary>
        public static ProgramClient GetProgramClient(this ISessionClient session, ITelemetryContext telemetry)
        {
            return new ProgramClient(session, telemetry);
        }

        /// <summary>
        /// Reads the wrapped invocation's capabilities and published signatures.
        /// </summary>
        public static ValueTask<ProgramCapabilities> ReadProgramCapabilitiesAsync(
            this ProgramStateMachineTypeClient proxy,
            CancellationToken ct = default)
        {
            return ForProxy(proxy).ReadCapabilitiesAsync(proxy.ObjectId, ct);
        }

        /// <summary>
        /// Reads the wrapped invocation's modern diagnostics with original quality and timestamps.
        /// </summary>
        public static ValueTask<ProgramDiagnosticResult> ReadProgramDiagnosticsAsync(
            this ProgramStateMachineTypeClient proxy,
            CancellationToken ct = default)
        {
            return ForProxy(proxy).ReadDiagnosticsAsync(proxy.ObjectId, ct);
        }

        /// <summary>
        /// Reads final results through the wrapped invocation's published model.
        /// </summary>
        public static ValueTask<ProgramFinalResults> ReadProgramFinalResultsAsync(
            this ProgramStateMachineTypeClient proxy,
            CancellationToken ct = default)
        {
            return ForProxy(proxy).ReadFinalResultsAsync(proxy.ObjectId, ct);
        }

        /// <summary>
        /// Deletes the wrapped invocation through the standard service.
        /// </summary>
        public static ValueTask DeleteProgramAsync(
            this ProgramStateMachineTypeClient proxy,
            CancellationToken ct = default)
        {
            return ForProxy(proxy).DeleteAsync(proxy.ObjectId, ct);
        }

        /// <summary>
        /// Reuses a proxy's session and telemetry without inheriting its internal resolution fallback.
        /// </summary>
        private static ProgramClient ForProxy(ProgramStateMachineTypeClient proxy)
        {
            if (proxy is null)
            {
                throw new ArgumentNullException(nameof(proxy));
            }
            return new ProgramClient(proxy.Session, proxy.Telemetry);
        }
    }
}
