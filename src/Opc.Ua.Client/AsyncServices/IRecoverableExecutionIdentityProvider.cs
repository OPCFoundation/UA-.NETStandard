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
    /// Verifies authenticated Server, application and user continuity for Client-local recovery.
    /// </summary>
    /// <remarks>
    /// Implementations must map the activated Session's authenticated credentials to stable, non-secret
    /// identities. Display names, advertised ApplicationUris and trust in a replacement certificate alone
    /// are not continuity evidence. Renewal requires an explicit authorized mapping; revoked, ambiguous or
    /// unavailable mappings must throw. The Server independently authorizes its current mapped owner.
    /// The borrowed Session must not be closed, reactivated or retained by this provider.
    /// </remarks>
    public interface IRecoverableExecutionIdentityProvider
    {
        /// <summary>
        /// Resolves the currently authenticated identities, failing closed when continuity cannot be verified.
        /// </summary>
        ValueTask<RecoverableExecutionIdentity> VerifyAsync(ISession session, CancellationToken ct = default);
    }

    /// <summary>
    /// Contains non-secret stable identity keys supplied by an explicit authenticated-continuity mapping.
    /// </summary>
    public sealed class RecoverableExecutionIdentity
    {
        /// <summary>
        /// Captures independent logical Server, application and user identity keys, not bearer credentials.
        /// </summary>
        public RecoverableExecutionIdentity(
            ByteString logicalServerIdentity, ByteString applicationIdentity, ByteString userIdentity)
        {
            Validate(logicalServerIdentity, nameof(logicalServerIdentity));
            Validate(applicationIdentity, nameof(applicationIdentity));
            Validate(userIdentity, nameof(userIdentity));
            m_server = logicalServerIdentity.Copy();
            m_application = applicationIdentity.Copy();
            m_user = userIdentity.Copy();
        }

        /// <summary>
        /// Gets the verified logical Server identity across supported restarts.
        /// </summary>
        public ByteString LogicalServerIdentity => m_server.Copy();

        /// <summary>
        /// Gets the authenticated Client application continuity key.
        /// </summary>
        public ByteString ApplicationIdentity => m_application.Copy();

        /// <summary>
        /// Gets the authenticated user continuity key, distinct from a display name.
        /// </summary>
        public ByteString UserIdentity => m_user.Copy();

        /// <summary>
        /// Compares all three identity dimensions without conflating applications sharing a user.
        /// </summary>
        internal bool Matches(RecoverableExecutionIdentity other)
        {
            return m_server == other.m_server && m_application == other.m_application && m_user == other.m_user;
        }

        /// <summary>
        /// Bounds keys before they enter protected recovery records.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static void Validate(ByteString value, string parameter)
        {
            if (value.Length is <= 0 or > 4096)
            {
                throw new ArgumentException("A bounded, nonempty authenticated identity key is required.", parameter);
            }
        }

        private readonly ByteString m_server;
        private readonly ByteString m_application;
        private readonly ByteString m_user;
    }
}
