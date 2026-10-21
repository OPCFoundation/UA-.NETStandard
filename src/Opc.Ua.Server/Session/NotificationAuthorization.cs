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
using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Identifies one authorization epoch for notifications, independently of Session object identity.
    /// </summary>
    internal sealed class NotificationAuthorization
    {
        /// <summary>
        /// Captures the authenticated principal and a detached set of its effective grants.
        /// </summary>
        internal NotificationAuthorization(
            IUserIdentityTokenHandler identityToken,
            IUserIdentity identity,
            IUserIdentity effectiveIdentity,
            string? clientApplicationUri)
        {
            m_tokenType = identityToken.TokenType;
            m_clientApplicationUri = clientApplicationUri;
            m_hasPrincipal = ClientUserIdResolver.TryResolveContinuityKey(
                identityToken, identity, out m_principal);
            m_roles = [.. effectiveIdentity.GrantedRoleIds];
            while (effectiveIdentity is RoleBasedIdentity roleBasedIdentity)
            {
                effectiveIdentity = roleBasedIdentity.InnerIdentity;
            }
            m_effectiveIdentity = effectiveIdentity;
            if (effectiveIdentity.TokenHandler != null)
            {
                m_hasEffectivePrincipal = ClientUserIdResolver.TryResolveContinuityKey(
                    effectiveIdentity.TokenHandler, effectiveIdentity, out m_effectivePrincipal);
            }
        }

        /// <summary>
        /// Determines whether reactivation or a legitimate transfer preserves the principal and grants.
        /// </summary>
        internal bool IsEquivalentTo(NotificationAuthorization other, bool sameSession = false)
        {
            bool samePrincipal = m_tokenType == UserTokenType.Anonymous
                ? sameSession ||
                    (!string.IsNullOrEmpty(m_clientApplicationUri) &&
                        string.Equals(m_clientApplicationUri, other.m_clientApplicationUri, StringComparison.Ordinal))
                : m_hasPrincipal && other.m_hasPrincipal &&
                    string.Equals(m_principal, other.m_principal, StringComparison.Ordinal);
            return m_tokenType == other.m_tokenType &&
                samePrincipal &&
                ((m_hasEffectivePrincipal && other.m_hasEffectivePrincipal &&
                    string.Equals(m_effectivePrincipal, other.m_effectivePrincipal, StringComparison.Ordinal)) ||
                    (!m_hasEffectivePrincipal && !other.m_hasEffectivePrincipal &&
                    (m_tokenType == UserTokenType.Anonymous ||
                        m_effectiveIdentity.Equals(other.m_effectiveIdentity)))) &&
                (sameSession
                    ? m_roles.SetEquals(other.m_roles)
                    : m_roles.IsSubsetOf(other.m_roles));
        }

        /// <summary>
        /// Captures a built-in epoch or a principal-and-grants snapshot for a custom Session implementation.
        /// </summary>
        internal static NotificationAuthorization? Capture(ISession? session)
        {
            if (session is Session concreteSession)
            {
                return concreteSession.NotificationAuthorization;
            }
            return session?.IdentityToken != null && session.Identity != null
                ? new NotificationAuthorization(
                    session.IdentityToken, session.Identity, session.EffectiveIdentity ?? session.Identity,
                    session.ClientApplicationUri)
                : null;
        }

        /// <summary>
        /// Checks provenance at the final admission boundary, failing closed while built-in grants are stale.
        /// </summary>
        internal static bool IsCurrent(ISession? session, NotificationAuthorization? authorization)
        {
            if (session?.IsIdentityStale == true)
            {
                return false;
            }
            NotificationAuthorization? current = Capture(session);
            if (session is Session)
            {
                return authorization != null && ReferenceEquals(authorization, current);
            }
            return ReferenceEquals(authorization, current) ||
                (authorization != null && current != null && authorization.IsEquivalentTo(current, sameSession: true));
        }

        /// <summary>The authenticated token type.</summary>
        private readonly UserTokenType m_tokenType;

        /// <summary>The client application identity required for anonymous transfer continuity.</summary>
        private readonly string? m_clientApplicationUri;

        /// <summary>Whether the authenticated principal has a trustworthy continuity key.</summary>
        private readonly bool m_hasPrincipal;

        /// <summary>The token-specific principal continuity key.</summary>
        private readonly string? m_principal;

        /// <summary>The unwrapped effective identity, including application-specific identity equality.</summary>
        private readonly IUserIdentity m_effectiveIdentity;

        /// <summary>Whether the application-mapped identity has a stable principal key.</summary>
        private readonly bool m_hasEffectivePrincipal;

        /// <summary>The application-mapped principal key, independent of renewed token bytes.</summary>
        private readonly string? m_effectivePrincipal;

        /// <summary>The effective grants at the start of this epoch.</summary>
        private readonly HashSet<NodeId> m_roles;
    }
}
