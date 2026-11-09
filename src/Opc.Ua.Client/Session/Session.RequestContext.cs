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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Provides the local client and identity check for generation-bound request contexts.
    /// </summary>
    public partial class Session
    {
        /// <summary>
        /// Signals identity replacement before a request context can capture the new generation.
        /// </summary>
        internal event Action? RequestContextChanged;

        /// <summary>
        /// Identifies the non-owning local session returned by a request context.
        /// </summary>
        internal bool IsGenerationBound => this is RequestContextSession;

        /// <summary>
        /// Captures one identity consistently, including when recovery recreates this object in place.
        /// </summary>
        internal SessionRequestContext CreateRequestContext(
            ManagedSession? owner, bool requireConfidentiality = false)
        {
            lock (m_requestContextIdentity)
            {
                return new SessionRequestContext(owner, this, m_requestContextGeneration, requireConfidentiality);
            }
        }

        /// <summary>
        /// Begins dispatch before another session identity can replace the captured one.
        /// </summary>
        internal ValueTask<IServiceResponse> SendRequestContextAsync(
            SessionRequestContext context, IServiceRequest request, CancellationToken ct)
        {
            lock (m_requestContextIdentity)
            {
                return context.SendAsync(this, m_requestContextGeneration, AuthenticationToken, request, ct);
            }
        }

        /// <summary>
        /// Reuses the generated Session services with independent local metadata and no connection ownership.
        /// </summary>
        internal Session CreateRequestContextSession(ITransportChannel channel, SessionConfiguration configuration)
        {
            return new RequestContextSession(channel, this, configuration);
        }

        /// <summary>
        /// A local Session implementation whose channel is a generation-checked, non-owning adapter.
        /// </summary>
        private sealed class RequestContextSession : Session
        {
            /// <summary>
            /// Initializes only local client infrastructure, not a connection or subscription copy.
            /// </summary>
            internal RequestContextSession(
                ITransportChannel channel, Session original, SessionConfiguration configuration)
                : base(
                    channel,
                    original.m_configuration,
                    new ConfiguredEndpoint(null,
                        CoreUtils.Clone(original.ConfiguredEndpoint.Description)!,
                        CoreUtils.Clone(original.ConfiguredEndpoint.Configuration)),
                    channel.MessageContext,
                    original.SubscriptionEngineFactory,
                    original.m_timeProvider,
                    original.m_securityPolicies)
            {
                m_identity = original.m_identity;
                m_sessionName = original.m_sessionName;
                m_sessionTimeout = original.m_sessionTimeout;
                m_preferredLocales = original.m_preferredLocales;
                ReturnDiagnostics = original.ReturnDiagnostics;
                DefaultTimeoutHint = original.DefaultTimeoutHint;
                OperationLimits.MaxNodesPerRead = original.OperationLimits.MaxNodesPerRead;
                OperationLimits.MaxNodesPerHistoryReadData = original.OperationLimits.MaxNodesPerHistoryReadData;
                OperationLimits.MaxNodesPerHistoryReadEvents = original.OperationLimits.MaxNodesPerHistoryReadEvents;
                OperationLimits.MaxNodesPerWrite = original.OperationLimits.MaxNodesPerWrite;
                OperationLimits.MaxNodesPerHistoryUpdateData = original.OperationLimits.MaxNodesPerHistoryUpdateData;
                OperationLimits.MaxNodesPerHistoryUpdateEvents =
                    original.OperationLimits.MaxNodesPerHistoryUpdateEvents;
                OperationLimits.MaxNodesPerMethodCall = original.OperationLimits.MaxNodesPerMethodCall;
                OperationLimits.MaxNodesPerBrowse = original.OperationLimits.MaxNodesPerBrowse;
                OperationLimits.MaxNodesPerRegisterNodes = original.OperationLimits.MaxNodesPerRegisterNodes;
                OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds =
                    original.OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds;
                OperationLimits.MaxNodesPerNodeManagement = original.OperationLimits.MaxNodesPerNodeManagement;
                OperationLimits.MaxMonitoredItemsPerCall = original.OperationLimits.MaxMonitoredItemsPerCall;
                ServerCapabilities.MaxBrowseContinuationPoints =
                    original.ServerCapabilities.MaxBrowseContinuationPoints;
                ServerCapabilities.MaxQueryContinuationPoints = original.ServerCapabilities.MaxQueryContinuationPoints;
                ServerCapabilities.MaxHistoryContinuationPoints =
                    original.ServerCapabilities.MaxHistoryContinuationPoints;
                ServerCapabilities.MinSupportedSampleRate = original.ServerCapabilities.MinSupportedSampleRate;
                ServerCapabilities.MaxArrayLength = original.ServerCapabilities.MaxArrayLength;
                ServerCapabilities.MaxStringLength = original.ServerCapabilities.MaxStringLength;
                ServerCapabilities.MaxByteStringLength = original.ServerCapabilities.MaxByteStringLength;
                ServerCapabilities.MaxSessions = original.ServerCapabilities.MaxSessions;
                ServerCapabilities.MaxSubscriptions = original.ServerCapabilities.MaxSubscriptions;
                ServerCapabilities.MaxMonitoredItems = original.ServerCapabilities.MaxMonitoredItems;
                ServerCapabilities.MaxSubscriptionsPerSession = original.ServerCapabilities.MaxSubscriptionsPerSession;
                ServerCapabilities.MaxMonitoredItemsPerSubscription =
                    original.ServerCapabilities.MaxMonitoredItemsPerSubscription;
                ServerCapabilities.MaxSelectClauseParameters = original.ServerCapabilities.MaxSelectClauseParameters;
                ServerCapabilities.MaxWhereClauseParameters = original.ServerCapabilities.MaxWhereClauseParameters;
                ServerCapabilities.MaxMonitoredItemsQueueSize = original.ServerCapabilities.MaxMonitoredItemsQueueSize;
                SessionCreated(configuration.SessionId, configuration.AuthenticationToken);
                m_systemContext.SessionId = configuration.SessionId;
                m_systemContext.UserIdentity = configuration.Identity;
            }

            /// <inheritdoc/>
            public override async Task<StatusCode> CloseAsync(
                int timeout, bool closeChannel, CancellationToken ct = default)
            {
                await DisposeAsync().ConfigureAwait(false);
                return StatusCodes.Good;
            }

            /// <inheritdoc/>
            protected override ValueTask DisposeAsyncCore(bool disposing)
            {
                AbandonServerSession();
                return base.DisposeAsyncCore(disposing);
            }
        }

        /// <summary>Serializes the short identity-validation and send-start operation with SessionCreated.</summary>
        private readonly Lock m_requestContextIdentity = new();

        /// <summary>Changes even when a server reuses a public identifier during recreation.</summary>
        private long m_requestContextGeneration;
    }
}
