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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Qualifies Session-independent execution before Owner or Durable admission.
    /// </summary>
    /// <remarks>
    /// The returned reservation must preserve the routing, configuration and resource conditions that
    /// guarantee both independent inputs and a self-contained response throughout the original invocation.
    /// A missing input continuation point or a positive Method hint is not sufficient evidence.
    /// Without an applicable standardized transfer contract, Session aliases, continuation points,
    /// Session-dependent Methods and potential response-created Session resources must be rejected.
    /// Do not execute the original request, probe its effects or downgrade its mode to decide eligibility.
    /// Treat the supplied request as read-only; qualification must apply to those exact frozen parameters.
    /// An unsupported or uncertain case throws BadServiceUnsupported before admission.
    /// The reservation is held until the invocation actually ends, not merely until its deadline or release.
    /// This qualification does not grant execution or collection permissions.
    /// </remarks>
    public interface IRecoverableExecutionEligibilityProvider
    {
        /// <summary>
        /// Reserves the conditions needed for the exact original request and selected non-Session mode.
        /// </summary>
        ValueTask<IAsyncDisposable> AcquireAsync(
            OperationContext context,
            IServiceRequest request,
            ServiceOperationRecoveryMode mode,
            RecoverableExecutionOwner owner,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Extends eligibility with restart-safe identification and fencing of a possibly surviving invocation.
    /// </summary>
    /// <remarks>
    /// The application must associate the supplied execution identity with its held eligibility conditions
    /// before admission can commit. Recovery must either prove termination or hold a fence that prevents
    /// conflicting work, including ordinary handler calls, while an earlier invocation may still run.
    /// A different process identity, an expired lease or an absent local Task is not termination evidence.
    /// Recovery never executes or resumes the original request. The Server retains active-work charges
    /// until termination is established, even when public outcome and history retention have ended.
    /// Disposing a local recovery reservation during Server shutdown must not make unproven surviving
    /// work safe to bypass; the underlying fence must remain effective independently of that process.
    /// </remarks>
    public interface IRecoverableExecutionDurableEligibilityProvider : IRecoverableExecutionEligibilityProvider
    {
        /// <summary>
        /// Holds the exact request's eligibility under a unique execution identity that can be recovered later.
        /// </summary>
        ValueTask<IAsyncDisposable> AcquireDurableAsync(
            OperationContext context,
            IServiceRequest request,
            RecoverableExecutionOwner owner,
            Uuid executionId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Restores termination evidence or a conflict-preventing reservation without dispatching original work.
        /// </summary>
        ValueTask<RecoverableExecutionRecoveredWork> RecoverAsync(
            RecoverableExecutionOwner owner,
            Uuid executionId,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Holds an application's qualified recovery reservation independently of any replacement Session.
    /// </summary>
    public sealed class RecoverableExecutionRecoveredWork : IAsyncDisposable
    {
        /// <summary>
        /// Captures a held fence and whether the application has established actual invocation termination.
        /// </summary>
        public RecoverableExecutionRecoveredWork(IAsyncDisposable reservation, bool terminationEstablished)
        {
            m_reservation = reservation ?? throw new ArgumentNullException(nameof(reservation));
            TerminationEstablished = terminationEstablished;
        }

        /// <summary>
        /// Gets whether actual termination, rather than merely loss of contact, has been established.
        /// False keeps the original work charged; a subsequent restart must qualify it again.
        /// </summary>
        public bool TerminationEstablished { get; }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            IAsyncDisposable? reservation = Interlocked.Exchange(ref m_reservation, null);
            return reservation?.DisposeAsync() ?? default;
        }

        private IAsyncDisposable? m_reservation;
    }

    /// <summary>
    /// Extends the installed resource-isolation provider with stable authenticated-owner accounting.
    /// </summary>
    /// <remarks>
    /// The Server calls this only after its owner provider validates the application and user.
    /// The returned classification must use the same pools, bounded owner table and hard limits as
    /// ordinary resource admission. A replacement Session or an explicit certificate alias must not
    /// multiply the owner's execution, retained-result or in-flight capacity.
    /// </remarks>
    public interface IRecoverableExecutionResourceIsolationProvider
    {
        /// <summary>
        /// Classifies owned work independently of the issuing Session's lifetime.
        /// </summary>
        ResourceIsolationOwner ClassifyRecoverableOwner(RecoverableExecutionOwnerId owner);
    }
}
