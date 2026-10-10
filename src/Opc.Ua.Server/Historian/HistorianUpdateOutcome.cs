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
