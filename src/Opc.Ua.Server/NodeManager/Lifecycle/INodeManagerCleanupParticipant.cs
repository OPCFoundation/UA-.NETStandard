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
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Exposes retained resource failures to the lifecycle's authorized recovery owner.
    /// </summary>
    /// <remarks>
    /// The participant retains each failed resource and its original exception privately.
    /// It must not repeat a failed cleanup merely because recovery is requested. Failure identifiers
    /// identify exact retained failures and must not be reused for another resource or failure.
    /// Identifiers must be opaque and must not encode resource names, paths, or other sensitive data.
    /// Discovery and recovery operations must remain available until the lifecycle owner has finished
    /// cleanup, including after the manager's disposal callback has returned.
    /// </remarks>
    public interface INodeManagerCleanupParticipant
    {
        /// <summary>
        /// Captures the retained failures after validating the supplied discovery capability.
        /// </summary>
        /// <param name="access">The active discovery capability for this participant.</param>
        /// <returns>A non-null snapshot of the retained failures.</returns>
        /// <remarks>
        /// The returned storage must remain stable after this call returns. Later changes to the participant's
        /// failure registry must not mutate an earlier snapshot. The capability expires when the callback returns
        /// or throws.
        /// </remarks>
        /// <exception cref="UnauthorizedAccessException">The capability is invalid or has expired.</exception>
        ArrayOf<NodeManagerCleanupFailure> GetCleanupFailures(NodeManagerCleanupAccess access);

        /// <summary>
        /// Resolves one retained resource for the host's trusted cleanup verifier.
        /// </summary>
        /// <param name="verification">The one-use capability for this participant and failure.</param>
        /// <returns>The exact synchronous or asynchronous resource retained for the failure.</returns>
        /// <remarks>
        /// The participant must consume the capability while checking its failure registry. It must reject
        /// an unknown failure, an active drain, or an already consumed capability. This operation must not
        /// dispose the resource or remove the failure. The host passes the reference only to its trusted verifier;
        /// it is never included in an operator discovery result. The capability expires when the callback returns
        /// or throws.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The failure cannot be resolved.</exception>
        NodeManagerCleanupResource GetCleanupResource(NodeManagerCleanupVerification verification);

        /// <summary>
        /// Clears one exact failure after the host has authorized and verified its actual cleanup.
        /// </summary>
        /// <param name="confirmation">The one-use capability for this participant and failure.</param>
        /// <remarks>
        /// The participant must consume the capability and update its failure registry atomically.
        /// It must reject an unknown failure, an active drain, or an already consumed capability.
        /// Successful confirmation does not invoke the resource's disposal callback. The capability expires
        /// when the callback returns or throws.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The failure cannot be confirmed.</exception>
        void ConfirmCleanup(NodeManagerCleanupConfirmation confirmation);
    }

    /// <summary>
    /// A one-use capability to resolve an exact resource for trusted host verification.
    /// </summary>
    public sealed class NodeManagerCleanupVerification
    {
        /// <summary>
        /// Creates a verification capability for the exact participant and failure.
        /// </summary>
        internal NodeManagerCleanupVerification(
            INodeManagerCleanupParticipant participant,
            Guid failureId,
            NodeManagerCleanupVerificationContext? context = null)
        {
            m_capability = new NodeManagerCleanupConfirmation(participant, failureId);
            Context = context;
        }

        /// <summary>
        /// Gets the verification context to pass to any recovery call made while resolving the resource.
        /// </summary>
        public NodeManagerCleanupVerificationContext? Context { get; }

        /// <summary>
        /// Consumes the capability for the exact participant and returns the failure identifier.
        /// </summary>
        /// <param name="participant">The participant receiving the verification request.</param>
        /// <param name="failureId">The retained failure identifier when consumption succeeds.</param>
        /// <returns>Whether the capability was valid and had not already been consumed.</returns>
        public bool TryConsume(INodeManagerCleanupParticipant participant, out Guid failureId)
        {
            return m_capability.TryConsume(participant, out failureId);
        }

        /// <summary>
        /// Gets whether the participant consumed this capability.
        /// </summary>
        internal bool IsConsumed => m_capability.IsConsumed;

        /// <summary>
        /// Prevents any further use of this capability.
        /// </summary>
        internal void Revoke()
        {
            m_capability.Revoke();
        }

        private readonly NodeManagerCleanupConfirmation m_capability;
    }

    /// <summary>
    /// Describes a retained resource failure without exposing the resource or its exception.
    /// </summary>
    public sealed class NodeManagerCleanupFailure
    {
        /// <summary>
        /// Creates a description of one exact retained failure.
        /// </summary>
        /// <param name="id">A nonempty identifier that is not reused for another failure.</param>
        /// <param name="resourceKind">A bounded, nonempty resource-kind description.</param>
        /// <param name="resourceName">An optional bounded resource name.</param>
        /// <param name="statusCode">A bad status describing the cleanup failure.</param>
        public NodeManagerCleanupFailure(
            Guid id,
            string resourceKind,
            string? resourceName,
            StatusCode statusCode)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException("A cleanup failure identifier is required.", nameof(id));
            }
            if (string.IsNullOrWhiteSpace(resourceKind) || resourceKind.Length > 128)
            {
                throw new ArgumentException(
                    "A resource kind of at most 128 characters is required.", nameof(resourceKind));
            }
            if (resourceName?.Length > 256)
            {
                throw new ArgumentException("A resource name cannot exceed 256 characters.", nameof(resourceName));
            }
            if (!StatusCode.IsBad(statusCode))
            {
                throw new ArgumentException("A cleanup failure must have a bad status.", nameof(statusCode));
            }
            Id = id;
            ResourceKind = resourceKind;
            ResourceName = resourceName;
            StatusCode = statusCode;
        }

        /// <summary>
        /// Gets the participant-issued identifier of this exact retained failure.
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// Gets a bounded resource-kind description.
        /// </summary>
        public string ResourceKind { get; }

        /// <summary>
        /// Gets an optional resource name; it is not an identity or cleanup proof.
        /// </summary>
        public string? ResourceName { get; }

        /// <summary>
        /// Gets the participant-supplied failure status.
        /// </summary>
        public StatusCode StatusCode { get; }
    }

    /// <summary>
    /// A short-lived discovery capability issued to one cleanup participant.
    /// </summary>
    public sealed class NodeManagerCleanupAccess
    {
        /// <summary>
        /// Creates a discovery capability for the exact participant.
        /// </summary>
        internal NodeManagerCleanupAccess(
            INodeManagerCleanupParticipant participant,
            NodeManagerCleanupVerificationContext? context = null)
        {
            m_participant = participant;
            Context = context;
        }

        /// <summary>
        /// Gets the verification context to pass to nested recovery, if this discovery is part of verification.
        /// </summary>
        public NodeManagerCleanupVerificationContext? Context { get; }

        /// <summary>
        /// Checks whether this capability is active for the exact participant.
        /// </summary>
        /// <param name="participant">The participant receiving the discovery call.</param>
        /// <returns>Whether the capability is active for that participant.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="participant"/> is null.</exception>
        public bool IsAuthorizedFor(INodeManagerCleanupParticipant participant)
        {
            if (participant is null)
            {
                throw new ArgumentNullException(nameof(participant));
            }
            return ReferenceEquals(Volatile.Read(ref m_participant), participant);
        }

        /// <summary>
        /// Ends the discovery call's authority.
        /// </summary>
        internal void Revoke()
        {
            Interlocked.Exchange(ref m_participant, null);
        }

        private INodeManagerCleanupParticipant? m_participant;
    }

    /// <summary>
    /// A one-use confirmation capability for one participant's exact retained failure.
    /// </summary>
    public sealed class NodeManagerCleanupConfirmation
    {
        /// <summary>
        /// Creates a confirmation capability for the exact participant and failure.
        /// </summary>
        internal NodeManagerCleanupConfirmation(INodeManagerCleanupParticipant participant, Guid failureId)
        {
            m_participant = participant;
            m_failureId = failureId;
        }

        /// <summary>
        /// Consumes the capability for the exact participant and returns the failure identifier.
        /// </summary>
        /// <param name="participant">The participant receiving the confirmation call.</param>
        /// <param name="failureId">The retained failure identifier when consumption succeeds.</param>
        /// <returns>Whether the capability was valid and had not already been consumed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="participant"/> is null.</exception>
        public bool TryConsume(INodeManagerCleanupParticipant participant, out Guid failureId)
        {
            if (participant is null)
            {
                throw new ArgumentNullException(nameof(participant));
            }
            if (!ReferenceEquals(Volatile.Read(ref m_participant), participant) ||
                Interlocked.CompareExchange(ref m_state, kConsumed, kActive) != kActive)
            {
                failureId = Guid.Empty;
                return false;
            }
            Interlocked.Exchange(ref m_participant, null);
            failureId = m_failureId;
            return true;
        }

        /// <summary>
        /// Gets whether the participant consumed this capability.
        /// </summary>
        internal bool IsConsumed => Volatile.Read(ref m_state) == kConsumed;

        /// <summary>
        /// Prevents any further use of this capability.
        /// </summary>
        internal void Revoke()
        {
            Interlocked.CompareExchange(ref m_state, kRevoked, kActive);
            Interlocked.Exchange(ref m_participant, null);
        }

        private const int kActive = 0;
        private const int kConsumed = 1;
        private const int kRevoked = 2;
        private INodeManagerCleanupParticipant? m_participant;
        private readonly Guid m_failureId;
        private int m_state;
    }
}
