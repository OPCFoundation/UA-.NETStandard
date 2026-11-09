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
using Opc.Ua;

namespace UaLens.Plugins.Companions
{
    /// <summary>
    /// Adapts one generated method call to an already verified instance method,
    /// rejecting uncertain replies and retries without owning the borrowed channel.
    /// </summary>
    internal sealed class PreparedCompanionMethodClient : SessionClient
    {
        /// <summary>
        /// Pins the generated declaration, instance binding and domain freshness checks.
        /// </summary>
        public PreparedCompanionMethodClient(
            CompanionContext context, NodeId owner, ExpandedNodeId declaration, NodeId method,
            bool returnsNode, Action requireCurrent, Action consume)
            : this(context, owner, declaration, method,
                output => ValidateStandardOutput(context, returnsNode, output), requireCurrent, consume)
        {
        }

        /// <summary>
        /// Pins a generated method and its exact typed output contract before dispatch.
        /// </summary>
        public PreparedCompanionMethodClient(
            CompanionContext context, NodeId owner, ExpandedNodeId declaration, NodeId method,
            Action<ArrayOf<Variant>> validateOutput, Action requireCurrent, Action consume)
            : base((context ?? throw new ArgumentNullException(nameof(context))).Session.TransportChannel,
                context.Telemetry)
        {
            ArgumentNullException.ThrowIfNull(validateOutput);
            ArgumentNullException.ThrowIfNull(requireCurrent);
            ArgumentNullException.ThrowIfNull(consume);
            if (owner.IsNull || declaration.IsNull || method.IsNull)
            {
                throw new ArgumentException("A verified object and method binding is required.");
            }
            m_context = context;
            m_owner = owner;
            m_declaration = declaration;
            m_method = method;
            m_validateOutput = validateOutput;
            m_requireCurrent = requireCurrent;
            m_consume = consume;
        }

        /// <summary>
        /// Gets the actual returned status, never a default success before dispatch.
        /// </summary>
        public StatusCode ResultStatus { get; private set; } = StatusCodes.BadNoData;

        /// <inheritdoc/>
        public override async ValueTask<CallResponse> CallAsync(
            RequestHeader? requestHeader, ArrayOf<CallMethodRequest> methodsToCall, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            m_requireCurrent();
            NodeId declaration = ExpandedNodeId.ToNodeId(m_declaration, m_context.Session.NamespaceUris);
            if (methodsToCall.Count != 1 ||
                methodsToCall[0].ObjectId != m_owner ||
                methodsToCall[0].MethodId != declaration ||
                Interlocked.Exchange(ref m_sent, 1) != 0)
            {
                throw new InvalidOperationException("The generated call differs from the preparation or was repeated.");
            }
            m_consume();
            CallResponse response = await m_context.Session.CallAsync(null,
            [
                new CallMethodRequest
                {
                    ObjectId = m_owner,
                    MethodId = m_method,
                    InputArguments = methodsToCall[0].InputArguments
                }
            ], ct).ConfigureAwait(false);
            if (response.ResponseHeader is null ||
                !StatusCode.IsGood(response.ResponseHeader.ServiceResult) ||
                response.Results.Count != 1)
            {
                throw new ServiceResultException(response.ResponseHeader is { } header &&
                    !StatusCode.IsGood(header.ServiceResult) ? header.ServiceResult : StatusCodes.BadDecodingError,
                    "The call outcome is unknown. Inspect; do not replay the preparation.");
            }
            CallMethodResult result = response.Results[0];
            ResultStatus = result.StatusCode;
            if (!StatusCode.IsGood(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode,
                    "The call was rejected or its outcome is uncertain. Do not replay the preparation.");
            }
            if (result.InputArgumentResults.Count != 0 &&
                result.InputArgumentResults.Count != methodsToCall[0].InputArguments.Count)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Incomplete method argument results.");
            }
            foreach (StatusCode status in result.InputArgumentResults)
            {
                if (!StatusCode.IsGood(status))
                {
                    throw new ServiceResultException(status, "A method argument was not accepted.");
                }
            }
            m_validateOutput(result.OutputArguments);
            m_requireCurrent();
            ct.ThrowIfCancellationRequested();
            return response;
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            ReleaseChannel();
            base.Dispose(disposing);
        }

        private static void ValidateStandardOutput(
            CompanionContext context, bool returnsNode, ArrayOf<Variant> output)
        {
            if (output.Count != (returnsNode ? 1 : 0) ||
                (returnsNode &&
                    (!output[0].TryGetValue(out NodeId node) ||
                        node.IsNull ||
                        node.NamespaceIndex >= context.Session.NamespaceUris.Count)))
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError,
                    "The method returned an invalid outcome. Inspect; do not repeat it.");
            }
        }

        private readonly CompanionContext m_context;
        private readonly NodeId m_owner;
        private readonly ExpandedNodeId m_declaration;
        private readonly NodeId m_method;
        private readonly Action<ArrayOf<Variant>> m_validateOutput;
        private readonly Action m_requireCurrent;
        private readonly Action m_consume;
        private int m_sent;
    }
}
