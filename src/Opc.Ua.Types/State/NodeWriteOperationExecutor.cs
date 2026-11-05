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
    /// Runs a complete node write operation without exposing its synchronization mechanism.
    /// </summary>
    internal sealed class NodeWriteOperationExecutor
    {
        /// <summary>
        /// Creates an executor for synchronous and asynchronous write operations.
        /// </summary>
        /// <param name="executeSynchronously">Runs one synchronous write operation.</param>
        /// <param name="executeAsynchronously">Runs one asynchronous write operation.</param>
        /// <param name="executeSynchronousFallback">
        /// Runs synchronous fallback work inside async admission and completes its synchronous reentrant writers.
        /// </param>
        internal NodeWriteOperationExecutor(
            Func<Func<ServiceResult>, ServiceResult> executeSynchronously,
            Func<Func<CancellationToken, ValueTask<ServiceResult>>, CancellationToken, ValueTask<ServiceResult>>
                executeAsynchronously,
            Func<Func<ServiceResult>, ValueTask<ServiceResult>> executeSynchronousFallback)
        {
            m_executeSynchronously = executeSynchronously
                ?? throw new ArgumentNullException(nameof(executeSynchronously));
            m_executeAsynchronously = executeAsynchronously
                ?? throw new ArgumentNullException(nameof(executeAsynchronously));
            m_executeSynchronousFallback = executeSynchronousFallback
                ?? throw new ArgumentNullException(nameof(executeSynchronousFallback));
        }

        /// <summary>
        /// Executes the full synchronous node write.
        /// </summary>
        /// <param name="operation">The validation, callback, and cache-commit operation.</param>
        /// <returns>The operation result.</returns>
        internal ServiceResult ExecuteSynchronously(Func<ServiceResult> operation)
        {
            return m_executeSynchronously(operation);
        }

        /// <summary>
        /// Executes the full asynchronous node write.
        /// </summary>
        /// <param name="operation">The validation, callback, and cache-commit operation.</param>
        /// <param name="cancellationToken">The operation cancellation token.</param>
        /// <returns>The operation result after its cache commit or failure.</returns>
        internal ValueTask<ServiceResult> ExecuteAsynchronouslyAsync(
            Func<CancellationToken, ValueTask<ServiceResult>> operation,
            CancellationToken cancellationToken)
        {
            return m_executeAsynchronously(operation, cancellationToken);
        }

        /// <summary>
        /// Runs synchronous fallback work inside the admitted asynchronous operation, then awaits synchronous
        /// writers admitted during the fallback body.
        /// </summary>
        /// <param name="operation">The complete synchronous fallback operation.</param>
        /// <returns>The operation result.</returns>
        internal ValueTask<ServiceResult> ExecuteSynchronousFallbackWithinAsyncOperation(
            Func<ServiceResult> operation)
        {
            return m_executeSynchronousFallback(operation);
        }

        /// <summary>
        /// Runs a complete synchronous write operation.
        /// </summary>
        private readonly Func<Func<ServiceResult>, ServiceResult> m_executeSynchronously;

        /// <summary>
        /// Runs a complete asynchronous write operation.
        /// </summary>
        private readonly Func<
            Func<CancellationToken, ValueTask<ServiceResult>>,
            CancellationToken,
            ValueTask<ServiceResult>> m_executeAsynchronously;

        /// <summary>
        /// Runs synchronous fallback work inside an asynchronous operation.
        /// </summary>
        private readonly Func<Func<ServiceResult>, ValueTask<ServiceResult>> m_executeSynchronousFallback;
    }
}
