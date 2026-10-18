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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Selects prefer-recoverable execution for ordinary asynchronous Read, Write and Call operations.
    /// </summary>
    public sealed record RecoverableSessionOptions
    {
        /// <summary>
        /// Gets the exact execution allowance in milliseconds, or null to use the Server's advertised default.
        /// </summary>
        public double? ExecutionAllowance { get; init; }

        /// <summary>
        /// Gets the exact retention interval in milliseconds, or null to use the Server's advertised default.
        /// </summary>
        public double? ResultRetentionTime { get; init; }

        /// <summary>
        /// Gets an optional asynchronous acceptance callback, run after typed validation and before caller completion.
        /// </summary>
        public Func<RecoverableAcceptanceContext, CancellationToken, ValueTask>? AcceptAsync { get; init; }
    }

    /// <summary>
    /// Carries a decoded caller result to an acceptance callback without exposing protocol tokens.
    /// </summary>
    public sealed class RecoverableAcceptanceContext
    {
        private RecoverableAcceptanceContext(
            ResultValue value, RecoverableReceipt? receipt, ServiceResult? failure = null)
        {
            m_value = value;
            Receipt = receipt;
            Failure = failure;
        }

        /// <summary>
        /// Gets the real recoverable receipt, or null when the operation executed ordinarily.
        /// A receipt cannot be acknowledged until caller acceptance completes.
        /// </summary>
        public RecoverableReceipt? Receipt { get; }

        /// <summary>
        /// Gets a legitimate original failure being accepted, or null for a returned caller result.
        /// </summary>
        public ServiceResult? Failure { get; }

        /// <summary>
        /// Gets the caller result when its type matches the requested type, including a valid null result.
        /// </summary>
        /// <typeparam name="TResult">The exact caller result type.</typeparam>
        public bool TryGetResult<TResult>([MaybeNullWhen(false)] out TResult result)
        {
            if (m_value is ResultValue<TResult> typed)
            {
                result = typed.Value;
                return true;
            }
            result = default;
            return false;
        }

        /// <summary>
        /// Creates a typed acceptance context without boxing or reflective invocation.
        /// </summary>
        /// <typeparam name="TResult">The caller result type.</typeparam>
        internal static RecoverableAcceptanceContext Create<TResult>(TResult value, RecoverableReceipt? receipt)
        {
            return new RecoverableAcceptanceContext(new ResultValue<TResult>(value), receipt);
        }

        /// <summary>
        /// Creates acceptance metadata for a failed original outcome without fabricating a typed result.
        /// </summary>
        internal static RecoverableAcceptanceContext ForFailure(ServiceResult failure, RecoverableReceipt? receipt)
        {
            return new RecoverableAcceptanceContext(s_failureValue, receipt, failure);
        }

        private abstract record ResultValue;
        private sealed record ResultValue<TResult>(TResult Value) : ResultValue;
        private sealed record FailureValue : ResultValue;
        private static readonly FailureValue s_failureValue = new();
        private readonly ResultValue m_value;
    }
}
