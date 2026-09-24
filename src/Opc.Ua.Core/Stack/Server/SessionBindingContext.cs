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

namespace Opc.Ua
{
    /// <summary>
    /// Immutable classification of one committed session activation.
    /// </summary>
    /// <remarks>
    /// This is not an authorization result. Neither an anonymous session nor an authenticated
    /// user automatically identifies a trusted tenant. Tenant mapping requires a separate policy.
    /// No authentication token, mutable identity, roles or certificate are exposed.
    /// </remarks>
    public sealed class SessionBindingContext
    {
        /// <summary>
        /// Creates a snapshot from state already validated by the session manager.
        /// </summary>
        public SessionBindingContext(
            NodeId sessionId,
            string secureChannelId,
            long activationSequence,
            UserTokenType userTokenType,
            string? clientUserId,
            string securityPolicyUri,
            MessageSecurityMode securityMode)
        {
            if (sessionId.IsNull)
            {
                throw new ArgumentException("A session id is required.", nameof(sessionId));
            }
            if (string.IsNullOrEmpty(secureChannelId))
            {
                throw new ArgumentException("A channel id is required.", nameof(secureChannelId));
            }
            if (activationSequence <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(activationSequence));
            }
            if (userTokenType is < UserTokenType.Anonymous or > UserTokenType.IssuedToken)
            {
                throw new ArgumentOutOfRangeException(nameof(userTokenType));
            }
            if (userTokenType == UserTokenType.Anonymous != (clientUserId == null))
            {
                throw new ArgumentException(
                    "Only anonymous identities have no continuity key.", nameof(clientUserId));
            }
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                throw new ArgumentException("A security policy URI is required.", nameof(securityPolicyUri));
            }
            if (securityMode is < MessageSecurityMode.None or > MessageSecurityMode.SignAndEncrypt)
            {
                throw new ArgumentOutOfRangeException(nameof(securityMode));
            }

            SessionId = sessionId;
            SecureChannelId = secureChannelId;
            ActivationSequence = activationSequence;
            UserTokenType = userTokenType;
            ClientUserId = clientUserId;
            SecurityPolicyUri = securityPolicyUri;
            SecurityMode = securityMode;
        }

        /// <summary>
        /// The server session id, not its authentication token.
        /// </summary>
        public NodeId SessionId { get; }

        /// <summary>
        /// The channel on which this activation committed.
        /// </summary>
        public string SecureChannelId { get; }

        /// <summary>
        /// Increasing activation version within this live session instance.
        /// </summary>
        /// <remarks>
        /// A restored session is a new instance. Compare snapshot identity as well as this
        /// sequence when retaining classification across restoration or server restart.
        /// </remarks>
        public long ActivationSequence { get; }

        /// <summary>
        /// The validated user token type; Anonymous does not establish a trusted identity.
        /// </summary>
        public UserTokenType UserTokenType { get; }

        /// <summary>
        /// The validated identity continuity key, or null for anonymous users.
        /// </summary>
        /// <remarks>
        /// This value is sensitive and must not be used as an unbounded metric label.
        /// </remarks>
        public string? ClientUserId { get; }

        /// <summary>
        /// The session's negotiated security policy.
        /// </summary>
        public string SecurityPolicyUri { get; }

        /// <summary>
        /// The session's negotiated message security mode.
        /// </summary>
        public MessageSecurityMode SecurityMode { get; }
    }
}
