/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace OnboardingDevice
{
    /// <summary>
    /// Holds the session borrowed by one active pull attempt.
    /// </summary>
    internal sealed class DevicePullSession
    {
        /// <summary>
        /// Borrows a one-shot factory; each attempt owns the session it creates.
        /// </summary>
        public DevicePullSession(Func<CancellationToken, Task<ManagedSession>> connect)
        {
            m_connect = connect ?? throw new ArgumentNullException(nameof(connect));
        }

        /// <summary>
        /// Opens one attempt and rejects overlapping attempts that could mutate the same device identity.
        /// </summary>
        public async ValueTask<Attempt> BeginAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (m_active || m_retirementFailed)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "A pull attempt is active or its session could not be retired.");
                }
                m_active = true;
            }
            ManagedSession session;
            try
            {
                session = await m_connect(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                lock (m_gate)
                {
                    m_active = false;
                }
                throw;
            }
            if (session == null || session.Disposed)
            {
                lock (m_gate)
                {
                    m_active = false;
                }
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The pull session factory returned no usable session.");
            }
            lock (m_gate)
            {
                m_session = session;
            }
            return new Attempt(this, session);
        }

        /// <summary>
        /// Returns the attempt's session without opening or replacing a connection.
        /// </summary>
        public Task<ManagedSession> GetCurrentAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (m_session == null || m_session.Disposed)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "No usable pull attempt session is active.");
                }
                return Task.FromResult(m_session);
            }
        }

        /// <summary>
        /// Retires only the session owned by the finishing attempt.
        /// </summary>
        private async ValueTask RetireAsync(ManagedSession session)
        {
            lock (m_gate)
            {
                if (!ReferenceEquals(m_session, session))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The pull attempt no longer owns this session.");
                }
                m_session = null;
            }
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                lock (m_gate)
                {
                    m_retirementFailed = true;
                }
                throw;
            }
            finally
            {
                lock (m_gate)
                {
                    m_active = false;
                }
            }
        }

        /// <summary>
        /// Owns one session until the attempt completes or fails.
        /// </summary>
        internal sealed class Attempt : IAsyncDisposable
        {
            /// <summary>
            /// Creates an attempt around its newly opened session.
            /// </summary>
            public Attempt(DevicePullSession owner, ManagedSession session)
            {
                m_owner = owner;
                Session = session;
            }

            /// <summary>
            /// Gets the session shared by the attempt's workflow and installer.
            /// </summary>
            public ManagedSession Session { get; }

            /// <summary>
            /// Disposes this attempt's session once.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                DevicePullSession? owner = Interlocked.Exchange(ref m_owner, null);
                try
                {
                    if (owner != null)
                    {
                        await owner.RetireAsync(Session).ConfigureAwait(false);
                    }
                }
                finally
                {
                    GC.SuppressFinalize(this);
                }
            }

            /// <summary>The owner used to retire this attempt.</summary>
            private DevicePullSession? m_owner;
        }

        /// <summary>The one-shot connection factory.</summary>
        private readonly Func<CancellationToken, Task<ManagedSession>> m_connect;

        /// <summary>Protects the active attempt and retirement status.</summary>
        private readonly Lock m_gate = new();

        /// <summary>The session currently borrowed by the workflow.</summary>
        private ManagedSession? m_session;

        /// <summary>Whether an attempt is connecting, running, or retiring.</summary>
        private bool m_active;

        /// <summary>Whether a failed retirement prevents another attempt.</summary>
        private bool m_retirementFailed;
    }
}
