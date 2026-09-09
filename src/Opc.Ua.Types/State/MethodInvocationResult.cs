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
    /// A Method's operation result, ordered outputs and optional input diagnostics.
    /// </summary>
    public sealed class MethodInvocationResult
    {
        /// <summary>
        /// Initializes a complete invocation result. Input diagnostics use resolved
        /// strings rather than indexes into another service response.
        /// </summary>
        public MethodInvocationResult(
            ServiceResult operationResult,
            ArrayOf<Variant> outputArguments = default,
            ArrayOf<ServiceResult> inputArgumentResults = default)
        {
            OperationResult = operationResult ?? throw new ArgumentNullException(nameof(operationResult));
            InputArgumentResults = inputArgumentResults.Span.ToArray();
            foreach (ServiceResult result in InputArgumentResults)
            {
                if (result is null)
                {
                    throw new ArgumentException(
                        "Input results must retain every argument position.", nameof(inputArgumentResults));
                }
            }
            OutputArguments = outputArguments.Span.ToArray();
        }

        /// <summary>
        /// Gets the status and resolved operation diagnostics.
        /// </summary>
        public ServiceResult OperationResult { get; }

        /// <summary>
        /// Gets the output values in declaration order.
        /// </summary>
        public ArrayOf<Variant> OutputArguments { get; }

        /// <summary>
        /// Gets the input results in argument order, or an empty array when omitted.
        /// </summary>
        public ArrayOf<ServiceResult> InputArgumentResults { get; }
    }

    /// <summary>
    /// Processes a Method call with operation and per-input diagnostic results.
    /// </summary>
    public delegate ValueTask<MethodInvocationResult> MethodCalledWithResultEventHandlerAsync(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        ArrayOf<Variant> inputArguments,
        CancellationToken cancellationToken = default);
}
