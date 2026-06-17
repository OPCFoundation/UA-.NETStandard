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

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// Receives structured PubSub transport security events.
    /// </summary>
    public interface IPubSubSecurityEventSink
    {
        /// <summary>
        /// Notifies the sink of a security-relevant PubSub event.
        /// </summary>
        /// <param name="securityEvent">The structured event.</param>
        void OnSecurityEvent(PubSubSecurityEvent securityEvent);
    }

    /// <summary>
    /// Security-relevant PubSub event kinds.
    /// </summary>
    public enum PubSubSecurityEventKind
    {
        /// <summary>
        /// UADP security token lookup failed.
        /// </summary>
        UnknownTokenRejected,

        /// <summary>
        /// UADP signature verification failed.
        /// </summary>
        SignatureVerificationFailed,

        /// <summary>
        /// UADP replay or nonce reuse was rejected.
        /// </summary>
        ReplayRejected,

        /// <summary>
        /// SKS issued keys for a security group.
        /// </summary>
        SksKeysIssued,

        /// <summary>
        /// SKS denied a key request.
        /// </summary>
        SksKeyRequestDenied
    }

    /// <summary>
    /// Outcome for a structured PubSub security event.
    /// </summary>
    public enum PubSubSecurityEventOutcome
    {
        /// <summary>
        /// The operation succeeded.
        /// </summary>
        Success,

        /// <summary>
        /// The operation was rejected.
        /// </summary>
        Rejected,

        /// <summary>
        /// The operation failed integrity verification.
        /// </summary>
        Failed
    }

    /// <summary>
    /// Structured PubSub security event payload.
    /// </summary>
    public sealed class PubSubSecurityEvent
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubSecurityEvent"/>.
        /// </summary>
        public PubSubSecurityEvent(
            PubSubSecurityEventKind kind,
            DateTimeOffset timestamp,
            PubSubSecurityEventOutcome outcome,
            uint? tokenId = null,
            string? securityGroupId = null,
            string? publisherId = null,
            string? callerIdentity = null)
        {
            Kind = kind;
            Timestamp = timestamp;
            Outcome = outcome;
            TokenId = tokenId;
            SecurityGroupId = securityGroupId;
            PublisherId = publisherId;
            CallerIdentity = callerIdentity;
        }

        /// <summary>
        /// Event kind.
        /// </summary>
        public PubSubSecurityEventKind Kind { get; }

        /// <summary>
        /// Event timestamp.
        /// </summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>
        /// Event outcome.
        /// </summary>
        public PubSubSecurityEventOutcome Outcome { get; }

        /// <summary>
        /// Security token id, when available.
        /// </summary>
        public uint? TokenId { get; }

        /// <summary>
        /// Security group id, when available.
        /// </summary>
        public string? SecurityGroupId { get; }

        /// <summary>
        /// Publisher id, when available.
        /// </summary>
        public string? PublisherId { get; }

        /// <summary>
        /// Caller identity, when available.
        /// </summary>
        public string? CallerIdentity { get; }
    }
}
