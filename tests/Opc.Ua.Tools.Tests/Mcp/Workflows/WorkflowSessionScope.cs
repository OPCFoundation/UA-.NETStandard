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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.Client;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpCompanion.Workflows
{
    /// <summary>
    /// Test-only service-scope provider for deterministic proxy/binder contracts.
    /// Real dispatch fencing is covered by the SDK's context tests and WorkflowLiveTests.
    /// </summary>
    internal sealed class WorkflowTestSessionFactory : IWorkflowSessionFactory
    {
        /// <summary>
        /// Gets every borrowed test scope for ownership assertions.
        /// </summary>
        internal List<WorkflowTestSessionScope> Scopes { get; } = [];

        /// <inheritdoc/>
        public ValueTask<IWorkflowSessionScope> CaptureAsync(
            OpcUaSessionManager.SessionInfo registration, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var scope = new WorkflowTestSessionScope(registration);
            Scopes.Add(scope);
            return ValueTask.FromResult<IWorkflowSessionScope>(scope);
        }
    }

    /// <summary>
    /// Forwards only the service calls needed by this suite, with independent namespace/identity capture.
    /// It is not a substitute for the production ManagedSession authentication-token fence.
    /// </summary>
    internal sealed class WorkflowTestSessionScope : IWorkflowSessionScope
    {
        /// <summary>
        /// Captures a distinct session façade, making accidental use of the unscoped registration observable.
        /// </summary>
        internal WorkflowTestSessionScope(OpcUaSessionManager.SessionInfo registration)
        {
            Registration = registration;
            SessionId = registration.Session.SessionId;
            m_context = new ServiceMessageContext(
                registration.Session.MessageContext, registration.Session.MessageContext.Telemetry);
            m_session.SetupGet(session => session.MessageContext).Returns(m_context);
            m_session.SetupGet(session => session.NamespaceUris).Returns(m_context.NamespaceUris);
            m_session.SetupGet(session => session.NodeCache).Returns(registration.Session.NodeCache);
            m_session.SetupGet(session => session.Connected).Returns(() => registration.Session.Connected);
            m_session.SetupGet(session => session.SessionId).Returns(SessionId);
            m_session.SetupGet(session => session.SessionName).Returns(registration.Session.SessionName);
            m_session.SetupGet(session => session.ConfiguredEndpoint).Returns(registration.Session.ConfiguredEndpoint);
            Access = registration.WithSession(m_session.Object);
            SetupCalls();
        }

        /// <inheritdoc/>
        public OpcUaSessionManager.SessionInfo Registration { get; }

        /// <inheritdoc/>
        public OpcUaSessionManager.SessionInfo Access { get; }

        /// <inheritdoc/>
        public NodeId SessionId { get; }

        /// <inheritdoc/>
        public Task Invalidated
        {
            get
            {
                if (Registration.Session.SessionId != SessionId)
                {
                    m_invalidated.TrySetResult();
                }
                return m_invalidated.Task;
            }
        }

        /// <summary>
        /// Gets whether the workflow released only its local scope.
        /// </summary>
        internal bool Disposed { get; private set; }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            m_invalidated.TrySetResult();
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Keeps setup at the UA service boundary rather than replacing generated method implementations.
        /// </summary>
        private void SetupCalls()
        {
            m_session.Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ViewDescription? view, uint maximum,
                    ArrayOf<BrowseDescription> nodes, CancellationToken token) =>
                    Send((session, ct) => session.BrowseAsync(header, view, maximum, nodes, ct), token));
            m_session.Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<bool>(),
                    It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, bool release, ArrayOf<ByteString> points, CancellationToken token) =>
                    Send((session, ct) => session.BrowseNextAsync(header, release, points, ct), token));
            m_session.Setup(session => session.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ArrayOf<BrowsePath> paths, CancellationToken token) =>
                    Send((session, ct) => session.TranslateBrowsePathsToNodeIdsAsync(header, paths, ct), token));
            m_session.Setup(session => session.ReadAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, double age, TimestampsToReturn timestamps,
                    ArrayOf<ReadValueId> nodes, CancellationToken token) =>
                    Send((session, ct) => session.ReadAsync(header, age, timestamps, nodes, ct), token));
            m_session.Setup(session => session.WriteAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ArrayOf<WriteValue> nodes, CancellationToken token) =>
                    Send((session, ct) => session.WriteAsync(header, nodes, ct), token));
            m_session.Setup(session => session.CallAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ArrayOf<CallMethodRequest> methods, CancellationToken token) =>
                    Send((session, ct) => session.CallAsync(header, methods, ct), token));
            m_session.Setup(session => session.HistoryReadAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ExtensionObject>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(), It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ExtensionObject details, TimestampsToReturn timestamps, bool release,
                    ArrayOf<HistoryReadValueId> nodes, CancellationToken token) =>
                    Send((session, ct) => session.HistoryReadAsync(
                        header, details, timestamps, release, nodes, ct), token));
            m_session.Setup(session => session.HistoryUpdateAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<ExtensionObject>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ArrayOf<ExtensionObject> details, CancellationToken token) =>
                    Send((session, ct) => session.HistoryUpdateAsync(header, details, ct), token));
        }

        /// <summary>
        /// Stops stale test-generation calls before they reach the recording service.
        /// </summary>
        /// <typeparam name="T">The typed UA service response.</typeparam>
        /// <exception cref="ServiceResultException"></exception>
        private ValueTask<T> Send<T>(
            Func<ISession, CancellationToken, ValueTask<T>> send, CancellationToken ct)
        {
            if (Invalidated.IsCompleted)
            {
                throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
            }
            return send(Registration.Session, ct);
        }

        private readonly Mock<ISession> m_session = new(MockBehavior.Strict);
        private readonly ServiceMessageContext m_context;
        private readonly TaskCompletionSource m_invalidated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
#endif
