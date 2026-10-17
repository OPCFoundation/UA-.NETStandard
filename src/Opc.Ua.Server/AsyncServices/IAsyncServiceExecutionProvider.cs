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
    /// Owns one Server's experimental recoverable executions, immutable outcomes and bounded terminal history.
    /// Returned responses keep their delivery resources until the transport calls IPooledEncodeable.Reuse.
    /// </summary>
    public interface IAsyncServiceExecutionProvider : IAsyncDisposable, IRequestParkingPolicy
    {
        /// <summary>
        /// Gets the explicitly eligible original request DataType identifiers.
        /// </summary>
        ArrayOf<NodeId> SupportedServices { get; }

        /// <summary>
        /// Reads aggregate metadata only; it does not enumerate operations or expose tokens.
        /// </summary>
        AsyncServiceExecutionStatistics GetStatistics();

        /// <summary>
        /// Admits and dispatches one original request, or rejects it without dispatch.
        /// </summary>
        ValueTask<SubmitServiceResponse> SubmitAsync(
            OperationContext context, SubmitServiceRequest request, CancellationToken ct);

        /// <summary>
        /// Collects shared operation state without dispatching the original request again.
        /// </summary>
        ValueTask<GetServiceResultResponse> GetAsync(
            OperationContext context, GetServiceResultRequest request, CancellationToken ct);

        /// <summary>
        /// Applies an explicit token-authorized control action.
        /// </summary>
        ValueTask<ControlServiceExecutionResponse> ControlAsync(
            OperationContext context, ControlServiceExecutionRequest request, CancellationToken ct);
    }

    /// <summary>
    /// Identifies a local authorization decision, not an additional wire enumeration.
    /// </summary>
    public enum AsyncServiceAccess
    {
        /// <summary>
        /// Admit a new operation.
        /// </summary>
        Submit,

        /// <summary>
        /// Disclose operation state or an available result.
        /// </summary>
        Collect,

        /// <summary>
        /// Request cancellation of owned work.
        /// </summary>
        Cancel,

        /// <summary>
        /// Acknowledge an available outcome.
        /// </summary>
        Acknowledge,

        /// <summary>
        /// Abandon recovery without cancelling work.
        /// </summary>
        Abandon
    }

    /// <summary>
    /// Adds current, action-specific application authorization to mandatory Session/creator and node checks.
    /// The policy receives metadata only and is re-evaluated after waiting.
    /// </summary>
    public interface IAsyncServiceAuthorizationPolicy
    {
        /// <summary>
        /// Authorizes an action or throws BadUserAccessDenied without changing the retained operation.
        /// An unknown lookup supplies a null ServiceId and, for a token lookup, an empty ClientOperationId.
        /// </summary>
        ValueTask AuthorizeAsync(
            OperationContext context,
            Uuid clientOperationId,
            NodeId serviceId,
            AsyncServiceAccess access,
            CancellationToken ct);
    }

    /// <summary>
    /// Provides a local aggregate snapshot for the privileged diagnostics model.
    /// </summary>
    /// <param name="ActiveOperationCount">Owned invocations that have not ended.</param>
    /// <param name="RetainedOutcomeCount">Outcomes still available for collection.</param>
    /// <param name="RetainedOutcomeBytes">Charged snapshot bytes, including in-flight owners after release.</param>
    /// <param name="TerminalRecordCount">Released records, including required active-work identities.</param>
    /// <param name="AdmittedCount">Cumulative accepted submissions.</param>
    /// <param name="RejectedCount">Cumulative submissions rejected without dispatch.</param>
    /// <param name="AcknowledgedCount">Cumulative first acknowledgements.</param>
    /// <param name="AbandonedCount">Cumulative first abandonments.</param>
    /// <param name="ExpiredCount">Cumulative result expirations.</param>
    /// <param name="ExecutionDeadlineCount">Cumulative immutable execution-deadline outcomes.</param>
    public readonly record struct AsyncServiceExecutionStatistics(
        uint ActiveOperationCount,
        uint RetainedOutcomeCount,
        ulong RetainedOutcomeBytes,
        uint TerminalRecordCount,
        ulong AdmittedCount,
        ulong RejectedCount,
        ulong AcknowledgedCount,
        ulong AbandonedCount,
        ulong ExpiredCount,
        ulong ExecutionDeadlineCount);

    /// <summary>
    /// Constructs an independently owned execution provider for a Server at startup.
    /// </summary>
    public interface IAsyncServiceExecutionProviderFactory
    {
        /// <summary>
        /// Creates the provider, whose shutdown and disposal are owned by the Server.
        /// </summary>
        IAsyncServiceExecutionProvider Create(
            StandardServer server,
            ApplicationConfiguration configuration,
            AsyncServiceExecutionOptions options,
            TimeProvider timeProvider);
    }

    /// <summary>
    /// Supplies the reference implementation without shared operation state between Servers.
    /// </summary>
    public sealed class DefaultAsyncServiceExecutionProviderFactory : IAsyncServiceExecutionProviderFactory
    {
        /// <summary>
        /// Creates a factory with an optional application authorization policy.
        /// </summary>
        public DefaultAsyncServiceExecutionProviderFactory(IAsyncServiceAuthorizationPolicy? authorizationPolicy = null)
        {
            m_authorizationPolicy = authorizationPolicy;
        }

        /// <summary>
        /// Gets the stateless default factory.
        /// </summary>
        public static DefaultAsyncServiceExecutionProviderFactory Instance { get; } = new();

        /// <inheritdoc/>
        public IAsyncServiceExecutionProvider Create(
            StandardServer server,
            ApplicationConfiguration configuration,
            AsyncServiceExecutionOptions options,
            TimeProvider timeProvider)
        {
            return new AsyncServiceExecutionProvider(
                server, configuration, options, timeProvider, m_authorizationPolicy);
        }

        private readonly IAsyncServiceAuthorizationPolicy? m_authorizationPolicy;
    }
}
