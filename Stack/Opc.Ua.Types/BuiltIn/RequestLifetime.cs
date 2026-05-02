/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Tracks the lifetime and cancellation state of a single request.
    /// </summary>
    public sealed class RequestLifetime : IDisposable
    {
        private static readonly Lazy<RequestLifetime> s_default = new(
            () =>
            {
                var r = new RequestLifetime();
                r.MarkCompleted();
                return r;
            });

        /// <summary>
        /// A default instance of the RequestLifetime class that is already completed and cannot be cancelled.
        /// </summary>
        public static RequestLifetime None => s_default.Value;

        /// <summary>
        /// Gets the token that will be cancelled when the request is aborted.
        /// </summary>
        public CancellationToken CancellationToken { get; }

        /// <summary>
        /// Gets the derived UA StatusCode for the cancellation.
        /// </summary>
        public StatusCode StatusCode => m_statusCode;

        /// <summary>
        /// Initializes a new instance of the RequestLifetime class.
        /// </summary>
        public RequestLifetime(params CancellationToken[] externalTokens)
        {
            if (externalTokens != null && externalTokens.Length > 0)
            {
                m_cts = CancellationTokenSource.CreateLinkedTokenSource(externalTokens);
            }
            else
            {
                m_cts = new CancellationTokenSource();
            }
            CancellationToken = m_cts.Token;
            m_statusCode = StatusCodes.Good;
        }

        /// <summary>
        /// Attempts to cancel the request and assigns the corresponding status code.
        /// </summary>
        public bool TryCancel(StatusCode statusCode)
        {
            if (m_disposed || m_cts.IsCancellationRequested)
            {
                return false;
            }

            m_statusCode = statusCode;

            try
            {
                m_cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Completes the lifetime and prevents further changes.
        /// </summary>
        public void MarkCompleted()
        {
            Dispose();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!m_disposed)
            {
                m_disposed = true;
                m_cts.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        private readonly CancellationTokenSource m_cts;
        private StatusCode m_statusCode;
        private bool m_disposed;
    }
}
