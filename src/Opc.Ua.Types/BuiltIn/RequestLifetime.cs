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
        /// <summary>
        /// Initializes a new instance of the RequestLifetime class.
        /// </summary>
        public RequestLifetime(params CancellationToken[] externalTokens)
        {
            m_cts = new CancellationTokenSource();
            CancellationToken = m_cts.Token;
            m_externalRegistrations = externalTokens is { Length: > 0 }
                ? new CancellationTokenRegistration[externalTokens.Length]
                : [];
            try
            {
                for (int i = 0; i < m_externalRegistrations.Length; i++)
                {
                    m_externalRegistrations[i] = externalTokens[i].Register(
                        static state => ((RequestLifetime)state!).TryCancel(StatusCodes.Good),
                        this);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

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
        public StatusCode StatusCode => new(unchecked((uint)Volatile.Read(ref m_state)));

        /// <summary>
        /// Gets or sets the optional sink that is notified when the request
        /// parks (suspends waiting for an out-of-band completion, such as a held
        /// <c>Publish</c>). When set, the request-processing worker is released
        /// at the park point instead of remaining blocked for the whole wait.
        /// <c>null</c> (the default) preserves the legacy inline behavior.
        /// </summary>
        /// <remarks>
        /// Endpoints supply the sink only for eligible requests. Handlers notify the existing sink rather
        /// than replacing it, and follow the terminal waiting-point contract of <see cref="IRequestParkSink"/>.
        /// The server's global decoupling switch may keep the worker attached even when a sink is present.
        /// </remarks>
        public IRequestParkSink? ParkSink { get; set; }

        /// <summary>
        /// Attempts to cancel the request and assigns the corresponding status code.
        /// </summary>
        /// <remarks>
        /// Concurrent callers (a client Cancel, a Session close and the request timeout) race to
        /// cancel the same request. Only the first caller claims the cancellation, so its status
        /// code is the one reported and a later caller can never overwrite it.
        /// </remarks>
        /// <returns><c>true</c> when this call cancelled the request.</returns>
        public bool TryCancel(StatusCode statusCode)
        {
            long requested = c_cancelled | c_cancelling | statusCode.Code;
            if (Interlocked.CompareExchange(ref m_state, requested, 0) != 0)
            {
                return false;
            }

            try
            {
                m_cts.Cancel();
                return true;
            }
            finally
            {
                long observed;
                long completed;
                do
                {
                    observed = Volatile.Read(ref m_state);
                    completed = observed & ~c_cancelling;
                }
                while (Interlocked.CompareExchange(ref m_state, completed, observed) != observed);

                if ((observed & c_completed) != 0)
                {
                    DisposeResources();
                }
            }
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
            long observed;
            long completed;
            do
            {
                observed = Volatile.Read(ref m_state);
                if ((observed & c_completed) != 0)
                {
                    return;
                }
                completed = observed | c_completed;
            }
            while (Interlocked.CompareExchange(ref m_state, completed, observed) != observed);

            if ((observed & c_cancelling) == 0)
            {
                DisposeResources();
            }
            GC.SuppressFinalize(this);
        }

        private void DisposeResources()
        {
            foreach (CancellationTokenRegistration registration in m_externalRegistrations)
            {
                registration.Dispose();
            }
            m_cts.Dispose();
        }

        // The lower bits publish the winning status together with its terminal ownership.
        private const long c_cancelled = 1L << 32;
        private const long c_cancelling = 1L << 33;
        private const long c_completed = 1L << 34;
        private static readonly Lazy<RequestLifetime> s_default = new(
            () =>
            {
                var r = new RequestLifetime();
                r.MarkCompleted();
                return r;
            });
        private readonly CancellationTokenSource m_cts;
        private readonly CancellationTokenRegistration[] m_externalRegistrations;
        private long m_state;
    }
}
