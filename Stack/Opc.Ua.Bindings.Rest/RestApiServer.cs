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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Adapter that bridges the OPC UA REST controller pipeline to an
    /// <see cref="ITransportListenerCallback"/> supplied by the host UA
    /// server. The host listener calls
    /// <see cref="Attach(ITransportListenerCallback)"/> once during
    /// startup; until then the dispatcher returns
    /// <see cref="StatusCodes.BadServerHalted"/> faults so controllers
    /// continue to surface a well-formed response shape.
    /// </summary>
    /// <remarks>
    /// Single instance per host (registered as a singleton in DI). The
    /// shared-host startup contributor (added to
    /// <c>Opc.Ua.Bindings.Https</c>'s pipeline) attaches the parent
    /// <see cref="ITransportListenerCallback"/>; the own-host
    /// <c>RestApiTransportListener</c> attaches its own callback when
    /// the host's
    /// <c>ITransportListener.SetCallback(ITransportListenerCallback)</c>
    /// is invoked by the server.
    /// </remarks>
    public sealed class RestApiServer : IRestApiServer
    {
        private readonly System.Threading.Lock m_lock = new();
        private ITransportListenerCallback? m_callback;
        private IServiceMessageContext m_messageContext;
        private string m_listenerId;

        /// <summary>
        /// Initializes a new dispatcher that returns
        /// <see cref="StatusCodes.BadServerHalted"/> until
        /// <see cref="Attach(ITransportListenerCallback)"/> is called.
        /// </summary>
        /// <param name="messageContext">
        /// The encoding context to expose to controllers. Replaced when
        /// <see cref="Attach(ITransportListenerCallback)"/> is called with
        /// a listener that exposes its own context.
        /// </param>
        /// <param name="listenerId">
        /// Logical listener identifier used to populate
        /// <see cref="SecureChannelContext.SecureChannelId"/> when no
        /// transport-level channel exists.
        /// </param>
        public RestApiServer(IServiceMessageContext messageContext, string listenerId)
        {
            m_messageContext = messageContext
                ?? throw new ArgumentNullException(nameof(messageContext));
            m_listenerId = listenerId
                ?? throw new ArgumentNullException(nameof(listenerId));
        }

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext
        {
            get
            {
                lock (m_lock)
                {
                    return m_messageContext;
                }
            }
        }

        /// <inheritdoc/>
        public bool IsReady
        {
            get
            {
                lock (m_lock)
                {
                    return m_callback != null;
                }
            }
        }

        /// <summary>
        /// Logical listener identifier surfaced through
        /// <see cref="SecureChannelContext.SecureChannelId"/> when no
        /// transport channel is available (sessionless REST requests).
        /// </summary>
        public string ListenerId
        {
            get
            {
                lock (m_lock)
                {
                    return m_listenerId;
                }
            }
        }

        /// <summary>
        /// Wires the dispatcher up to the host server's listener callback.
        /// Idempotent — subsequent calls replace the registration so the
        /// REST pipeline tracks listener restarts.
        /// </summary>
        /// <param name="callback">The listener callback. May be <c>null</c>
        /// to detach (e.g. shutdown).</param>
        public void Attach(ITransportListenerCallback? callback)
        {
            lock (m_lock)
            {
                m_callback = callback;
            }
        }

        /// <summary>
        /// Updates the encoding context exposed by
        /// <see cref="MessageContext"/>. Called by the host listener after
        /// the message context becomes available.
        /// </summary>
        /// <param name="messageContext">The new encoding context.</param>
        public void UpdateMessageContext(IServiceMessageContext messageContext)
        {
            if (messageContext == null)
            {
                throw new ArgumentNullException(nameof(messageContext));
            }
            lock (m_lock)
            {
                m_messageContext = messageContext;
            }
        }

        /// <summary>
        /// Updates the logical listener identifier exposed by
        /// <see cref="ListenerId"/>. Called by the host listener when it
        /// becomes available.
        /// </summary>
        /// <param name="listenerId">The listener identifier.</param>
        public void UpdateListenerId(string listenerId)
        {
            if (listenerId == null)
            {
                throw new ArgumentNullException(nameof(listenerId));
            }
            lock (m_lock)
            {
                m_listenerId = listenerId;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<IServiceResponse> InvokeAsync(
            IServiceRequest request,
            RestApiInvocationContext context,
            CancellationToken ct)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            ITransportListenerCallback? callback;
            lock (m_lock)
            {
                callback = m_callback;
            }

            if (callback == null)
            {
                return new ServiceFault
                {
                    ResponseHeader = new ResponseHeader
                    {
                        Timestamp = DateTime.UtcNow,
                        RequestHandle = request.RequestHeader?.RequestHandle ?? 0,
                        ServiceResult = StatusCodes.BadServerHalted,
                        StringTable = new ArrayOf<string>(),
                        AdditionalHeader = new ExtensionObject()
                    }
                };
            }

            var secureChannelContext = new SecureChannelContext(
                context.SecureChannelId,
                context.Endpoint,
                RequestEncoding.Json,
                context.ClientCertificate,
                context.ServerCertificate);

            try
            {
                return await callback
                    .ProcessRequestAsync(secureChannelContext, request, ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                return new ServiceFault
                {
                    ResponseHeader = new ResponseHeader
                    {
                        Timestamp = DateTime.UtcNow,
                        RequestHandle = request.RequestHeader?.RequestHandle ?? 0,
                        ServiceResult = sre.StatusCode,
                        StringTable = new ArrayOf<string>(),
                        AdditionalHeader = new ExtensionObject()
                    }
                };
            }
        }
    }
}
