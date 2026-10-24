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
using Opc.Ua.Redundancy;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Owns one Server's experimental recoverable executions, immutable outcomes and bounded terminal history.
    /// Delivery resources are transferred to the whole outer Call response until transport completion.
    /// </summary>
    public interface IAsyncServiceExecutionProvider : IAsyncDisposable, IRequestParkingPolicy, IRequestControlPolicy
    {
        /// <summary>
        /// Gets the explicitly eligible original request DataType identifiers.
        /// </summary>
        ArrayOf<NodeId> SupportedServices { get; }

        /// <summary>
        /// Gets the recovery modes currently qualified for admission.
        /// </summary>
        ArrayOf<ServiceOperationRecoveryMode> SupportedRecoveryModes { get; }

        /// <summary>
        /// Restores qualified state and reservations before the Server publishes the optional manager.
        /// </summary>
        ValueTask InitializeAsync(CancellationToken ct);

        /// <summary>
        /// Captures the generated live manager and its Server-assigned Method identities at startup.
        /// </summary>
        void BindManager(RecoverableExecutionManagerState manager);

        /// <summary>
        /// Recognizes an invocation of an installed manager Method without authorizing it.
        /// </summary>
        bool IsManagerCall(CallMethodRequest request);

        /// <summary>
        /// Enforces the current message limit on the complete outer response, including all batch siblings.
        /// </summary>
        void ValidateCallResponse(OperationContext context, CallResponse response);

        /// <summary>
        /// Reads aggregate metadata only; it does not enumerate operations or expose tokens.
        /// </summary>
        AsyncServiceExecutionStatistics GetStatistics();

        /// <summary>
        /// Admits and dispatches one original request, or rejects it without dispatch.
        /// </summary>
        ValueTask<SubmitResponseDataType> SubmitAsync(
            OperationContext context, SubmitRequestDataType request, CancellationToken ct);

        /// <summary>
        /// Collects shared operation state without dispatching the original request again.
        /// </summary>
        ValueTask<GetResultResponseDataType> GetAsync(
            OperationContext context, GetResultRequestDataType request, CancellationToken ct);

        /// <summary>
        /// Acknowledges an accepted outcome without renewing retention.
        /// </summary>
        ValueTask<ControlResponseDataType> AcknowledgeAsync(
            OperationContext context, OperationControlRequestDataType request, CancellationToken ct);

        /// <summary>
        /// Requests stopping owned work without promising rollback.
        /// </summary>
        ValueTask<ControlResponseDataType> CancelAsync(
            OperationContext context, OperationControlRequestDataType request, CancellationToken ct);

        /// <summary>
        /// Waives result recovery without cancelling work.
        /// </summary>
        ValueTask<ControlResponseDataType> AbandonAsync(
            OperationContext context, OperationControlRequestDataType request, CancellationToken ct);
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
    /// <param name="ExecutionIndeterminateCount">Cumulative restart-indeterminate outcomes.</param>
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
        ulong ExecutionDeadlineCount,
        ulong ExecutionIndeterminateCount = 0);

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
        public DefaultAsyncServiceExecutionProviderFactory(
            IAsyncServiceAuthorizationPolicy? authorizationPolicy = null,
            IRecoverableExecutionOwnerProvider? ownerProvider = null,
            IRecoverableExecutionEligibilityProvider? eligibilityProvider = null,
            ISharedKeyValueStore? durableStore = null,
            IRecordProtector? recordProtector = null,
            IRecoverableExecutionClock? durableClock = null,
            IRecoverableExecutionCodecQualifier? codecQualifier = null)
        {
            m_authorizationPolicy = authorizationPolicy;
            m_ownerProvider = ownerProvider;
            m_eligibilityProvider = eligibilityProvider;
            m_durableStore = durableStore;
            m_recordProtector = recordProtector;
            m_durableClock = durableClock;
            m_codecQualifier = codecQualifier;
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
                server, configuration, options, timeProvider, m_authorizationPolicy, m_ownerProvider,
                m_eligibilityProvider, m_durableStore, m_recordProtector, m_durableClock, m_codecQualifier);
        }

        private readonly IAsyncServiceAuthorizationPolicy? m_authorizationPolicy;
        private readonly IRecoverableExecutionOwnerProvider? m_ownerProvider;
        private readonly IRecoverableExecutionEligibilityProvider? m_eligibilityProvider;
        private readonly ISharedKeyValueStore? m_durableStore;
        private readonly IRecordProtector? m_recordProtector;
        private readonly IRecoverableExecutionClock? m_durableClock;
        private readonly IRecoverableExecutionCodecQualifier? m_codecQualifier;
    }
}
