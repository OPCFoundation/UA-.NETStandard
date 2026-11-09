/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
