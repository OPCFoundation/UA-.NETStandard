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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Conformance.Tests.Mock
{
    /// <summary>
    /// In-process mock controller that lets a conformance test inject
    /// service-result error codes or mutate response fields produced by
    /// the in-process reference server. Implements
    /// <see cref="IServiceResponseMutator"/> and is attached to the
    /// server's <see cref="IServerBase.ResponseMutator"/> hook by the
    /// <see cref="TestFixture"/>.
    ///
    /// <para>
    /// Tests register expectations with <see cref="ExpectNextResponse{TResponse}"/>
    /// (matched once) or <see cref="WhenRequest{TRequest, TResponse}"/>
    /// (matched on every request of the given type until reset). The
    /// controller is reset between tests via <see cref="Reset"/>.
    /// </para>
    /// </summary>
    public sealed class MockResponseController : IServiceResponseMutator
    {
        private readonly object m_lock = new();
        private readonly List<Expectation> m_oneShot = [];
        private readonly List<Expectation> m_recurring = [];

        /// <summary>
        /// Registers a one-shot expectation. The next response of type
        /// <typeparamref name="TResponse"/> produced by the server is
        /// passed to <paramref name="mutate"/> and the (possibly
        /// modified) response is returned to the client. The expectation
        /// is consumed after a single match.
        /// </summary>
        /// <typeparam name="TResponse">The response type to match.</typeparam>
        /// <param name="mutate">The mutation to apply.</param>
        /// <returns>A handle that, when disposed, removes the
        /// expectation if it has not already fired.</returns>
        public IDisposable ExpectNextResponse<TResponse>(Action<TResponse> mutate)
            where TResponse : class, IServiceResponse
        {
            if (mutate == null)
            {
                throw new ArgumentNullException(nameof(mutate));
            }
            var entry = new Expectation
            {
                ResponseType = typeof(TResponse),
                Mutator = (req, resp) => mutate((TResponse)resp)
            };
            lock (m_lock)
            {
                m_oneShot.Add(entry);
            }
            return new ExpectationHandle(this, entry, oneShot: true);
        }

        /// <summary>
        /// Registers a recurring expectation. Every request of type
        /// <typeparamref name="TRequest"/> processed by the server
        /// passes its <typeparamref name="TResponse"/> through
        /// <paramref name="mutate"/> until the controller is
        /// <see cref="Reset"/> or the returned handle is disposed.
        /// </summary>
        /// <typeparam name="TRequest">The request type to match.</typeparam>
        /// <typeparam name="TResponse">The corresponding response
        /// type (no compile-time pairing — the caller is responsible
        /// for matching types).</typeparam>
        /// <param name="mutate">The mutation, with the original request
        /// available for inspection.</param>
        /// <returns>A handle that, when disposed, removes the
        /// expectation.</returns>
        public IDisposable WhenRequest<TRequest, TResponse>(
            Action<TRequest, TResponse> mutate)
            where TRequest : class, IServiceRequest
            where TResponse : class, IServiceResponse
        {
            if (mutate == null)
            {
                throw new ArgumentNullException(nameof(mutate));
            }
            var entry = new Expectation
            {
                RequestType = typeof(TRequest),
                ResponseType = typeof(TResponse),
                Mutator = (req, resp) => mutate((TRequest)req, (TResponse)resp)
            };
            lock (m_lock)
            {
                m_recurring.Add(entry);
            }
            return new ExpectationHandle(this, entry, oneShot: false);
        }

        /// <summary>
        /// Removes all registered expectations. Called by
        /// <see cref="TestFixture"/> in [SetUp] so each test starts
        /// from a clean state.
        /// </summary>
        public void Reset()
        {
            lock (m_lock)
            {
                m_oneShot.Clear();
                m_recurring.Clear();
            }
        }

        /// <summary>
        /// Returns true if any expectations remain unfired. Useful for
        /// per-test assertions on full coverage.
        /// </summary>
        public bool HasPendingOneShotExpectations
        {
            get
            {
                lock (m_lock)
                {
                    return m_oneShot.Count > 0;
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask<IServiceResponse> MutateResponseAsync(
            IServiceRequest request,
            IServiceResponse response,
            CancellationToken cancellationToken = default)
        {
            Expectation matched = null;
            lock (m_lock)
            {
                // Prefer one-shot expectations (FIFO).
                for (int i = 0; i < m_oneShot.Count; i++)
                {
                    Expectation e = m_oneShot[i];
                    if (Matches(e, request, response))
                    {
                        matched = e;
                        m_oneShot.RemoveAt(i);
                        break;
                    }
                }

                if (matched == null)
                {
                    foreach (Expectation e in m_recurring)
                    {
                        if (Matches(e, request, response))
                        {
                            matched = e;
                            break;
                        }
                    }
                }
            }

            if (matched != null)
            {
                matched.Mutator(request, response);
            }

            return new ValueTask<IServiceResponse>(response);
        }

        private static bool Matches(Expectation e, IServiceRequest request, IServiceResponse response)
        {
            if (e.RequestType != null && !e.RequestType.IsInstanceOfType(request))
            {
                return false;
            }
            if (e.ResponseType != null && !e.ResponseType.IsInstanceOfType(response))
            {
                return false;
            }
            return true;
        }

        private void Remove(Expectation e, bool oneShot)
        {
            lock (m_lock)
            {
                if (oneShot)
                {
                    m_oneShot.Remove(e);
                }
                else
                {
                    m_recurring.Remove(e);
                }
            }
        }

        private sealed class Expectation
        {
            public Type RequestType { get; init; }
            public Type ResponseType { get; init; }
            public Action<IServiceRequest, IServiceResponse> Mutator { get; init; }
        }

        private sealed class ExpectationHandle : IDisposable
        {
            private readonly MockResponseController m_owner;
            private readonly Expectation m_entry;
            private readonly bool m_oneShot;
            private int m_disposed;

            public ExpectationHandle(MockResponseController owner, Expectation entry, bool oneShot)
            {
                m_owner = owner;
                m_entry = entry;
                m_oneShot = oneShot;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    m_owner.Remove(m_entry, m_oneShot);
                }
            }
        }
    }
}
