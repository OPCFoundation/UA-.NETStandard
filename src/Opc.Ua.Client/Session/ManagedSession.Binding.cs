// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A managed session that wraps an unmanaged <see cref="Session"/>
    /// and automatically handles connection lifecycle, reconnection
    /// with configurable policy, and server redundancy failover.
    /// </summary>
    public partial class ManagedSession : ISessionBindingProvider
    {
        /// <summary>
        /// Captures a client for the current authenticated binding.
        /// </summary>
        /// <inheritdoc/>
        /// <param name="ct">The cancellation token passed to the asynchronous operation.</param>
        public async ValueTask<ISessionClient> CreateBindingAsync(CancellationToken ct = default)
        {
            using (await m_serviceLock.ReaderLockAsync(ct).ConfigureAwait(false))
            {
                Session session = InnerSession;
                long generation = Interlocked.Read(ref m_bindingGeneration);
                return await session.CreateBindingAsync(
                    () => Volatile.Read(ref m_disposed) == 0 &&
                        Interlocked.Read(ref m_bindingGeneration) == generation &&
                        ReferenceEquals(m_session, session),
                    ct).ConfigureAwait(false);
            }
        }

        private void SetBindingSession(Session? session)
        {
            Interlocked.Increment(ref m_bindingGeneration);
            if (session != null && m_acknowledgements != null)
            {
                // The inner session's engines consult the managed session's configuration.
                session.Acknowledgements = m_acknowledgements;
            }
            m_session = session;
        }

        private long m_bindingGeneration;
    }
}
