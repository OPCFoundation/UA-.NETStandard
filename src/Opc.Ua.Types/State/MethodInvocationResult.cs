// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        /// <param name="operationResult">The operation result used to initialize the instance.</param>
        /// <param name="outputArguments">The output arguments used to initialize the instance.</param>
        /// <param name="inputArgumentResults">The input argument results used to initialize the instance.</param>
        /// <exception cref="ArgumentNullException">The operation result is null.</exception>
        /// <exception cref="ArgumentException">An input result is null.</exception>
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
    /// <param name="context">The system context.</param>
    /// <param name="method">The method state.</param>
    /// <param name="objectId">The object identifier.</param>
    /// <param name="inputArguments">The input arguments.</param>
    /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
    public delegate ValueTask<MethodInvocationResult> MethodCalledWithResultEventHandlerAsync(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        ArrayOf<Variant> inputArguments,
        CancellationToken cancellationToken = default);
}
