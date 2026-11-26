// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// A borrowed, generation-bound service scope; disposal never owns the real UA session or channel.
    /// </summary>
    public interface IWorkflowSessionScope : IAsyncDisposable
    {
        /// <summary>
        /// Gets the original named registration, used only for ownership and registration-generation checks.
        /// </summary>
        OpcUaSessionManager.SessionInfo Registration { get; }

        /// <summary>
        /// Gets the service identity whose session enforces the captured generation at every dispatch.
        /// </summary>
        OpcUaSessionManager.SessionInfo Access { get; }

        /// <summary>
        /// Gets the original UA session id, not a mutable current id.
        /// </summary>
        NodeId SessionId { get; }

        /// <summary>
        /// Gets the signal completed when the captured service generation is no longer usable.
        /// </summary>
        Task Invalidated { get; }
    }

    /// <summary>
    /// Host extension point for obtaining an actually generation-bound client service scope.
    /// </summary>
    public interface IWorkflowSessionFactory
    {
        /// <summary>
        /// Captures service dispatch identity without opening another UA session or exposing a lock.
        /// </summary>
        ValueTask<IWorkflowSessionScope> CaptureAsync(
            OpcUaSessionManager.SessionInfo registration, CancellationToken ct = default);
    }

    /// <summary>
    /// Uses the SDK's guarded request context rather than an unsafe InnerSession object snapshot.
    /// </summary>
    public sealed class WorkflowSessionFactory : IWorkflowSessionFactory
    {
        /// <inheritdoc/>
        public async ValueTask<IWorkflowSessionScope> CaptureAsync(
            OpcUaSessionManager.SessionInfo registration, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(registration);
            SessionRequestContext context = await SessionRequestContext.CaptureAsync(registration.Session, ct)
                .ConfigureAwait(false);
            return new Scope(registration, context);
        }

        /// <summary>
        /// Adapts the SDK context to named MCP authority without transferring session ownership.
        /// </summary>
        private sealed class Scope : IWorkflowSessionScope
        {
            /// <summary>
            /// Keeps original registration identity separate from guarded service access.
            /// </summary>
            internal Scope(OpcUaSessionManager.SessionInfo registration, SessionRequestContext context)
            {
                Registration = registration;
                m_context = context;
                Access = registration.WithSession(context.Session);
            }

            /// <inheritdoc/>
            public OpcUaSessionManager.SessionInfo Registration { get; }

            /// <inheritdoc/>
            public OpcUaSessionManager.SessionInfo Access { get; }

            /// <inheritdoc/>
            public NodeId SessionId => m_context.SessionId;

            /// <inheritdoc/>
            public Task Invalidated => m_context.Invalidated;

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                return m_context.DisposeAsync();
            }

            private readonly SessionRequestContext m_context;
        }
    }
}
