/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Optional provider capability for the runtime parameters of OPC 30010 6.1.3.1.
    /// </summary>
    /// <remarks>
    /// Supported code names and their indexes remain stable for the provider lifetime.
    /// Writes occur only while idle. A successful write commits the complete configuration;
    /// a rejected or cancelled write must leave the previous configuration intact.
    /// Providers own persistence and the encoding of scan data in the selected format.
    /// </remarks>
    public interface IAutoIdScanParametersProvider : IAutoIdReaderProvider
    {
        /// <summary>
        /// Gets the supported code names, indexed by the CodeTypes variable.
        /// </summary>
        ArrayOf<string> SupportedCodeTypes { get; }

        /// <summary>
        /// Reads the initial configuration before the reader is published.
        /// </summary>
        ValueTask<AutoIdScanParameters> ReadParametersAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits a validated configuration, or throws without changing the existing configuration.
        /// </summary>
        ValueTask WriteParametersAsync(
            AutoIdScanParameters parameters,
            CancellationToken cancellationToken = default);
    }

}
