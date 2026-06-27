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

#nullable enable

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// Optional EventId synchronization provider for Transparent and HotAndMirrored redundancy.
    /// </summary>
    /// <remarks>
    /// OPC 10000-4 §6.6.2.2 requires EventIds to be synchronized for Transparent and HotAndMirrored
    /// <c>RedundantServerSet</c>s so clients do not double-process events after Failover. This provider is
    /// deterministic for the same replica-set seed, notifier, event type, source, time, severity, and message.
    /// Events that lack stable
    /// distinguishing fields should set <c>EventId</c> explicitly or use a stronger application-level event identity.
    /// </remarks>
    public sealed class DeterministicEventIdProvider : IEventIdProvider
    {
        /// <summary>
        /// Creates a deterministic event id provider.
        /// </summary>
        /// <param name="replicaSetSeed">
        /// A stable, non-secret seed shared by all replicas in the transparent set.
        /// </param>
        /// <exception cref="ArgumentException"><paramref name="replicaSetSeed"/> is empty.</exception>
        public DeterministicEventIdProvider(string replicaSetSeed)
        {
            if (string.IsNullOrWhiteSpace(replicaSetSeed))
            {
                throw new ArgumentException("A replica-set seed is required.", nameof(replicaSetSeed));
            }

            m_replicaSetSeed = replicaSetSeed;
        }

        /// <inheritdoc/>
        public ByteString CreateEventId(BaseObjectState notifier, ISystemContext context, BaseEventState eventState)
        {
            if (notifier == null)
            {
                throw new ArgumentNullException(nameof(notifier));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (eventState == null)
            {
                throw new ArgumentNullException(nameof(eventState));
            }

            var builder = new StringBuilder();
            Append(builder, m_replicaSetSeed);
            Append(builder, notifier.NodeId.ToString());
            Append(builder, (eventState.EventType?.Value ?? eventState.GetDefaultTypeDefinitionId(context)).ToString());
            Append(builder, (eventState.SourceNode?.Value ?? notifier.NodeId).ToString());
            Append(builder, eventState.Time?.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            Append(builder, eventState.ReceiveTime?.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            Append(builder, eventState.Severity?.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            Append(builder, eventState.Message?.Value.Text ?? string.Empty);

            byte[] bytes = Encoding.UTF8.GetBytes(builder.ToString());
#if NET8_0_OR_GREATER
            return ByteString.From(SHA256.HashData(bytes));
#else
            using SHA256 sha = SHA256.Create();
            return ByteString.From(sha.ComputeHash(bytes));
#endif
        }

        private static void Append(StringBuilder builder, string value)
        {
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(value);
            builder.Append('|');
        }

        private readonly string m_replicaSetSeed;
    }
}
