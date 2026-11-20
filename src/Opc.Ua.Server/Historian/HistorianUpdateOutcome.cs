// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Result of applying one history update detail to a provider.
    /// </summary>
    /// <typeparam name="T">
    /// The provider-side value type retained for audit OldValues.
    /// </typeparam>
    public sealed record HistorianUpdateOutcome<T>
    {
        /// <summary>
        /// Creates an update outcome.
        /// </summary>
        public HistorianUpdateOutcome(
            ArrayOf<StatusCode> operationResults,
            ArrayOf<T> oldValues = default,
            ArrayOf<DiagnosticInfo> diagnosticInfos = default,
            bool transactionRolledBack = false)
        {
            OperationResults = operationResults.IsNull
                ? []
                : operationResults;
            OldValues = oldValues.IsNull ? [] : oldValues;
            DiagnosticInfos = diagnosticInfos.IsNull
                ? []
                : diagnosticInfos;
            if (!DiagnosticInfos.IsEmpty &&
                DiagnosticInfos.Count != OperationResults.Count)
            {
                throw new ArgumentException(
                    "DiagnosticInfos must align with OperationResults.",
                    nameof(diagnosticInfos));
            }
            TransactionRolledBack = transactionRolledBack;
        }

        /// <summary>
        /// One status for every requested update entry.
        /// </summary>
        public ArrayOf<StatusCode> OperationResults { get; }

        /// <summary>
        /// Values replaced or deleted by the operation, for audit reporting.
        /// </summary>
        public ArrayOf<T> OldValues { get; }

        /// <summary>
        /// Optional diagnostics aligned with <see cref="OperationResults"/>.
        /// </summary>
        public ArrayOf<DiagnosticInfo> DiagnosticInfos { get; }

        /// <summary>
        /// Whether an atomic provider rolled back the entire requested batch.
        /// </summary>
        public bool TransactionRolledBack { get; }
    }
}
