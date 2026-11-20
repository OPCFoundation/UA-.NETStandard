// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Allows tests and diagnostics tools to mutate the service response
    /// produced by the server before it is returned to the client. The
    /// mutator is invoked from <see cref="EndpointBase"/> after the
    /// service has produced the response and before the response is
    /// sent on the wire. Production servers should leave
    /// <see cref="IServerBase.ResponseMutator"/> null. Test code can
    /// install an implementation to inject service-result error codes,
    /// alter individual response fields (e.g. nonces, IDs, arrays), or
    /// otherwise simulate a misbehaving server for client conformance
    /// testing.
    /// </summary>
    public interface IServiceResponseMutator
    {
        /// <summary>
        /// Returns the (possibly mutated) response that should be sent
        /// back to the client. Implementations are free to return the
        /// same instance, modify it in-place, or return a new instance.
        /// Implementations must be thread-safe — multiple service calls
        /// can be in flight on the same server simultaneously.
        /// </summary>
        /// <param name="request">The service request being processed.</param>
        /// <param name="response">The response produced by the server.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The response to send to the client.</returns>
        ValueTask<IServiceResponse> MutateResponseAsync(
            IServiceRequest request,
            IServiceResponse response,
            CancellationToken cancellationToken = default);
    }
}
