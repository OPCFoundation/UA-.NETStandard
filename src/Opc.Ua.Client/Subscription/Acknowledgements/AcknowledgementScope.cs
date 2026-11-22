// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Opc.Ua.Client
{
    /// <summary>
    /// One lifetime of a subscription as the owner of acknowledgements: the subscription
    /// instance, its message generation and its server-assigned id. Scopes compare by that
    /// identity, so handles of the same lifetime are equal even if more than one scope
    /// instance was created for it. Owners cache the scope of their current lifetime to avoid
    /// allocating one per message.
    /// </summary>
    /// <param name="owner">The subscription instance that produced the messages.</param>
    /// <param name="generation">The owner's message generation.</param>
    /// <param name="subscriptionId">The server-assigned subscription id.</param>
    internal sealed class AcknowledgementScope(object owner, long generation, uint subscriptionId) :
        IEquatable<AcknowledgementScope>
    {
        /// <summary>
        /// Gets the subscription instance that produced the messages.
        /// </summary>
        public object Owner { get; } = owner;

        /// <summary>
        /// Gets the owner's message generation.
        /// </summary>
        public long Generation { get; } = generation;

        /// <summary>
        /// Gets the server-assigned subscription id.
        /// </summary>
        public uint SubscriptionId { get; } = subscriptionId;

        /// <summary>
        /// Compares two scopes, either of which may be <c>null</c>, by lifetime identity.
        /// </summary>
        public static bool Equals(AcknowledgementScope? left, AcknowledgementScope? right)
        {
            return ReferenceEquals(left, right) || (left != null && left.Equals(right));
        }

        /// <summary>
        /// Returns the scope for the owner's current lifetime, reusing
        /// <paramref name="current"/> while it still describes it. A lookup for an older
        /// generation never replaces the cached scope of a newer one.
        /// </summary>
        /// <param name="current">The cached scope, if any.</param>
        /// <param name="owner">The subscription instance.</param>
        /// <param name="generation">The owner's current message generation.</param>
        /// <param name="subscriptionId">The owner's current server id.</param>
        public static AcknowledgementScope GetOrCreate(
            ref AcknowledgementScope? current,
            object owner,
            long generation,
            uint subscriptionId)
        {
            AcknowledgementScope? scope = Volatile.Read(ref current);
            while (true)
            {
                if (scope != null && scope.Generation == generation && scope.SubscriptionId == subscriptionId)
                {
                    return scope;
                }
                var created = new AcknowledgementScope(owner, generation, subscriptionId);
                if (scope != null && scope.Generation > generation)
                {
                    // A stale lookup: equal by identity to any other scope of its lifetime.
                    return created;
                }
                AcknowledgementScope? previous = Interlocked.CompareExchange(ref current, created, scope);
                if (ReferenceEquals(previous, scope))
                {
                    return created;
                }
                scope = previous;
            }
        }

        /// <inheritdoc/>
        public bool Equals(AcknowledgementScope? other)
        {
            return other != null &&
                ReferenceEquals(Owner, other.Owner) &&
                Generation == other.Generation &&
                SubscriptionId == other.SubscriptionId;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return Equals(obj as AcknowledgementScope);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(RuntimeHelpers.GetHashCode(Owner), Generation, SubscriptionId);
        }
    }
}
