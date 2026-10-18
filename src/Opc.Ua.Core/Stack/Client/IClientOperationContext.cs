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

namespace Opc.Ua
{
    /// <summary>
    /// Optional cooperation between a session and typed Client operations that accept results after decoding.
    /// </summary>
    public interface IClientOperationContext
    {
        /// <summary>
        /// Gets whether generated proxies must establish a typed operation before dispatch.
        /// </summary>
        bool RequiresTypedOperationContext { get; }

        /// <summary>
        /// Gets whether the current asynchronous flow is already inside a typed operation.
        /// </summary>
        bool HasOperationContext { get; }

        /// <summary>
        /// Executes and accepts one caller operation, including its normal typed result validation.
        /// </summary>
        /// <typeparam name="TResult">The caller's final result type.</typeparam>
        ValueTask<TResult> ExecuteOperationAsync<TResult>(
            Func<CancellationToken, ValueTask<TResult>> operation,
            CancellationToken ct = default);

        /// <summary>
        /// Identifies an exception produced from a validated original response, not from local decoding or policy.
        /// </summary>
        void ObserveOriginalFailure(ServiceResultException failure);
    }

    /// <summary>
    /// Preserves ordinary session behavior while allowing an opted-in session to delay result acknowledgement.
    /// </summary>
    public static class ClientOperationContext
    {
        /// <summary>
        /// Preserves exception identity while marking a validated original failure for caller acceptance.
        /// </summary>
        public static ServiceResultException OriginalFailure(ISessionClient session, ServiceResultException failure)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (failure == null)
            {
                throw new ArgumentNullException(nameof(failure));
            }
            if (session is IClientOperationContext context)
            {
                context.ObserveOriginalFailure(failure);
            }
            return failure;
        }

        /// <summary>
        /// Runs a typed operation through an optional session completion context.
        /// </summary>
        /// <typeparam name="TResult">The caller's final result type.</typeparam>
        public static ValueTask<TResult> ExecuteAsync<TResult>(
            ISessionClient session,
            Func<CancellationToken, ValueTask<TResult>> operation,
            CancellationToken ct = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            return session is IClientOperationContext context
                ? context.ExecuteOperationAsync(operation, ct)
                : operation(ct);
        }

        /// <summary>
        /// Runs an operation without output arguments through an optional session completion context.
        /// </summary>
        public static ValueTask ExecuteAsync(
            ISessionClient session,
            Func<CancellationToken, ValueTask> operation,
            CancellationToken ct = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            return new ValueTask(ExecuteAsync(session, async token =>
            {
                await operation(token).ConfigureAwait(false);
                return StatusCodes.Good;
            }, ct).AsTask());
        }
    }
}
