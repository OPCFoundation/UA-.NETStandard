// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.AutoID.Server.Builders
{
    /// <summary>
    /// Optional reader-builder capability for standard diagnostic projections.
    /// </summary>
    public interface IAutoIdDiagnosticsBuilder : IAutoIdReaderBuilder
    {
        /// <summary>
        /// Publishes read-only Diagnostics from the existing presence, log and access reports.
        /// </summary>
        /// <remarks>
        /// Includes Presence, a single Message column in Logbook, and LastAccess. RFID readers
        /// also expose the optional RFID access fields. Unreported measurements have UncertainInitialValue;
        /// fields absent from a subsequent access report have BadNoData rather than
        /// retaining an earlier access. Access-event publication is configured independently.
        /// </remarks>
        IAutoIdReaderBuilder WithDiagnostics();
    }
}
