// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua
{
    /// <summary>
    /// An interface to an object that describes a node local to the server.
    /// </summary>
    public interface IOperationContext
    {
        /// <summary>
        /// The locales to use if available.
        /// </summary>
        /// <value>The preferred locales.</value>
        ArrayOf<string> PreferredLocales { get; }

        /// <summary>
        /// The mask to use when collecting any diagnostic information.
        /// </summary>
        /// <value>The diagnostics mask.</value>
        DiagnosticsMasks DiagnosticsMask { get; }

        /// <summary>
        /// The table of strings which is used to store diagnostic string data.
        /// </summary>
        /// <value>The string table.</value>
        StringTable? StringTable { get; }

        /// <summary>
        /// When the operation times out.
        /// </summary>
        /// <value>The operation deadline.</value>
        DateTime OperationDeadline { get; }

        /// <summary>
        /// The current status of the operation (bad if the operation has been aborted).
        /// </summary>
        /// <value>The operation status.</value>
        StatusCode OperationStatus { get; }

        /// <summary>
        /// The audit identifier associated with the operation.
        /// </summary>
        /// <value>The audit entry identifier.</value>
        string? AuditEntryId { get; }
    }
}
