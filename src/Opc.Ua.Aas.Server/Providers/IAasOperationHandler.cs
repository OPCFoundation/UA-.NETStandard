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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Aas.Server
{
    /// <summary>
    /// Executes an AAS Operation element.
    /// </summary>
    public interface IAasOperationHandler
    {
        /// <summary>
        /// Invokes an operation.
        /// </summary>
        ValueTask<AasOperationInvokeResult> InvokeAsync(
            AasOperationInvokeRequest request,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The operation invocation request.
    /// </summary>
    public sealed class AasOperationInvokeRequest
    {
        /// <summary>
        /// Initializes a request.
        /// </summary>
        public AasOperationInvokeRequest(
            NodeId operationNodeId,
            ArrayOf<Variant> inputValues,
            ArrayOf<Variant> inoutputValues,
            double clientTimeout)
        {
            OperationNodeId = operationNodeId;
            InputValues = inputValues;
            InoutputValues = inoutputValues;
            ClientTimeout = clientTimeout;
        }

        /// <summary>
        /// Gets the operation node id.
        /// </summary>
        public NodeId OperationNodeId { get; }

        /// <summary>
        /// Gets the input values.
        /// </summary>
        public ArrayOf<Variant> InputValues { get; }

        /// <summary>
        /// Gets the in-out values.
        /// </summary>
        public ArrayOf<Variant> InoutputValues { get; }

        /// <summary>
        /// Gets the requested timeout, or zero for the server default.
        /// </summary>
        public double ClientTimeout { get; }
    }

    /// <summary>
    /// The operation invocation result.
    /// </summary>
    public sealed class AasOperationInvokeResult
    {
        /// <summary>
        /// Initializes a result.
        /// </summary>
        public AasOperationInvokeResult(
            ArrayOf<Variant> outputValues,
            ArrayOf<Variant> inoutputResults,
            bool success,
            string diagnostic)
        {
            OutputValues = outputValues;
            InoutputResults = inoutputResults;
            Success = success;
            Diagnostic = diagnostic ?? string.Empty;
        }

        /// <summary>
        /// Gets the output values.
        /// </summary>
        public ArrayOf<Variant> OutputValues { get; }

        /// <summary>
        /// Gets the in-out results.
        /// </summary>
        public ArrayOf<Variant> InoutputResults { get; }

        /// <summary>
        /// Gets whether the operation execution succeeded.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Gets the operation diagnostic.
        /// </summary>
        public string Diagnostic { get; }
    }

    /// <summary>
    /// Default operation handler that reports an executed but unimplemented operation.
    /// </summary>
    public sealed class DefaultAasOperationHandler : IAasOperationHandler
    {
        /// <inheritdoc/>
        public ValueTask<AasOperationInvokeResult> InvokeAsync(
            AasOperationInvokeRequest request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<AasOperationInvokeResult>(
                new AasOperationInvokeResult([], [], false, "No AAS operation handler is configured."));
        }
    }
}
