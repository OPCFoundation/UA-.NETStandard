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

namespace Opc.Ua.Client
{
    /// <summary>
    /// One lifetime of a subscription as the owner of acknowledgements: the subscription
    /// instance, its message generation and its server-assigned id. Owners keep one instance
    /// per lifetime, so handles of the same lifetime compare equal by reference.
    /// </summary>
    /// <param name="owner">The subscription instance that produced the messages.</param>
    /// <param name="generation">The owner's message generation.</param>
    /// <param name="subscriptionId">The server-assigned subscription id.</param>
    internal sealed class AcknowledgementScope(object owner, long generation, uint subscriptionId)
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
        /// Returns the scope for the owner's current lifetime, reusing
        /// <paramref name="current"/> while it still describes it.
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
            AcknowledgementScope? scope = System.Threading.Volatile.Read(ref current);
            if (scope != null && scope.Generation == generation && scope.SubscriptionId == subscriptionId)
            {
                return scope;
            }
            var created = new AcknowledgementScope(owner, generation, subscriptionId);
            AcknowledgementScope? previous = System.Threading.Interlocked.CompareExchange(
                ref current, created, scope);
            return ReferenceEquals(previous, scope) ||
                previous == null ||
                previous.Generation != generation ||
                previous.SubscriptionId != subscriptionId
                ? created
                : previous;
        }
    }
}
