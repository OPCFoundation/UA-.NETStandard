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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Integrates opt-in recoverable execution with the existing session and typed operation interfaces.
    /// </summary>
    public partial class ManagedSession : IClientOperationContext, IMethodCallRetryPolicy
    {
        /// <inheritdoc/>
        bool IClientOperationContext.RequiresTypedOperationContext => IsRecoverableApplicationFlow;

        /// <inheritdoc/>
        bool IClientOperationContext.HasOperationContext => m_recoverableExecution?.HasOperationContext == true;

        /// <inheritdoc/>
        bool IMethodCallRetryPolicy.AllowMethodIdFallback => m_recoverableExecution?.AllowMethodIdFallback != false;

        /// <summary>
        /// Session maintenance is not an application operation and must never create receipt obligations.
        /// </summary>
        private bool IsRecoverableApplicationFlow =>
            m_recoverableExecution is { IsSuppressed: false } && !StateMachine.IsWorkerFlow;

        /// <inheritdoc/>
        void IClientOperationContext.ObserveOriginalFailure(ServiceResultException failure)
        {
            m_recoverableExecution?.ObserveOriginalFailure(failure);
        }

        /// <inheritdoc/>
        ValueTask<TResult> IClientOperationContext.ExecuteOperationAsync<TResult>(
            Func<CancellationToken, ValueTask<TResult>> operation, CancellationToken ct)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            return IsRecoverableApplicationFlow
                ? m_recoverableExecution!.ExecuteAsync(operation, ct)
                : operation(ct);
        }

        /// <summary>
        /// Begins a receipt scope without changing session ownership or enabling a new execution mode.
        /// </summary>
        internal RecoverableAcknowledgementScope BeginAcknowledgementScope(
            bool requireReceipt, Func<RecoverableAcceptanceContext, CancellationToken, ValueTask>? accept)
        {
            if (!IsRecoverableApplicationFlow)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Receipt scopes require an opted-in application call flow.");
            }
            return m_recoverableExecution?.BeginScope(requireReceipt, accept) ??
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Prefer-recoverable mode was not selected when creating the session.");
        }

        /// <summary>
        /// Runs library-owned read-only member resolution without treating preparation as the application operation.
        /// </summary>
        /// <typeparam name="TResult">The resolved metadata type.</typeparam>
        internal async ValueTask<TResult> PrepareClientOperationAsync<TResult>(
            Func<CancellationToken, ValueTask<TResult>> prepare, CancellationToken ct)
        {
            using IDisposable? preparation = m_recoverableExecution?.Suppress();
            return await prepare(ct).ConfigureAwait(false);
        }

        private RecoverableSessionExecution? m_recoverableExecution;
    }
}
