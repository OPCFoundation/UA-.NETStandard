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
using Opc.Ua.Client;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// A borrowed, generation-bound service scope; disposal never owns the real UA session or channel.
    /// </summary>
    public interface IClientWorkflowSessionScope : IAsyncDisposable
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
    public interface IClientWorkflowSessionFactory
    {
        /// <summary>
        /// Captures service dispatch identity without opening another UA session or exposing a lock.
        /// </summary>
        ValueTask<IClientWorkflowSessionScope> CaptureAsync(
            OpcUaSessionManager.SessionInfo registration, CancellationToken ct = default);
    }

    /// <summary>
    /// Uses the SDK's guarded request context rather than an unsafe InnerSession object snapshot.
    /// </summary>
    public sealed class ClientWorkflowSessionFactory : IClientWorkflowSessionFactory
    {
        /// <inheritdoc/>
        public async ValueTask<IClientWorkflowSessionScope> CaptureAsync(
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
        private sealed class Scope : IClientWorkflowSessionScope
        {
            /// <summary>
            /// Keeps original registration identity separate from guarded service access.
            /// </summary>
            internal Scope(OpcUaSessionManager.SessionInfo registration, SessionRequestContext context)
            {
                Registration = registration;
                m_context = context;
                Access = new OpcUaSessionManager.SessionInfo
                {
                    Name = registration.Name,
                    Session = context.Session,
                    Endpoint = context.Session.ConfiguredEndpoint.Description,
                    AuthType = registration.AuthType,
                    ConnectedAt = registration.ConnectedAt
                };
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
