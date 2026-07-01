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

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Evaluation context passed to a <see cref="PubSubResponseAddressPolicy"/>
    /// when an inbound PubSub Action request asks the responder to publish its
    /// response to a requestor-supplied address (topic).
    /// </summary>
    public readonly record struct PubSubResponseAddressContext
    {
        /// <summary>
        /// Name of the connection that received the Action request.
        /// </summary>
        public string ConnectionName { get; init; }

        /// <summary>
        /// DataSetWriterId that owns the Action target.
        /// </summary>
        public ushort DataSetWriterId { get; init; }

        /// <summary>
        /// ActionTargetId addressed by the request.
        /// </summary>
        public ushort ActionTargetId { get; init; }

        /// <summary>
        /// Requestor-supplied response address. For topic-based transports
        /// (e.g. MQTT) this is the publish topic the response would be sent to;
        /// it is attacker-controlled and must be validated. Datagram transports
        /// (e.g. UDP) ignore it.
        /// </summary>
        public string? ResponseAddress { get; init; }

        /// <summary>
        /// <see langword="true"/> when the connection transport routes messages
        /// by topic (MQTT/JSON) and therefore honors <see cref="ResponseAddress"/>;
        /// <see langword="false"/> for datagram transports that ignore it (UDP).
        /// </summary>
        public bool TransportUsesTopics { get; init; }
    }

    /// <summary>
    /// Restricts where a PubSub Action responder is allowed to publish its
    /// response (SA-ACT-03). A response is otherwise sent to the
    /// <c>ResponseAddress</c> taken verbatim from the inbound request; on
    /// topic-based transports (MQTT/JSON) that lets an attacker pick an arbitrary
    /// topic and turn the responder into a publishing proxy / reflector. This
    /// policy validates the requestor-supplied address before the response is
    /// emitted and lets the responder drop out-of-policy responses.
    /// </summary>
    /// <remarks>
    /// Datagram transports (UDP) ignore the response address entirely, so every
    /// built-in policy permits responses when
    /// <see cref="PubSubResponseAddressContext.TransportUsesTopics"/> is
    /// <see langword="false"/>; the restriction only applies to MQTT/JSON.
    /// </remarks>
    public sealed class PubSubResponseAddressPolicy
    {
        private readonly Func<PubSubResponseAddressContext, bool> m_predicate;

        private PubSubResponseAddressPolicy(
            string description,
            Func<PubSubResponseAddressContext, bool> predicate)
        {
            Description = description;
            m_predicate = predicate;
        }

        /// <summary>
        /// Human-readable description of the policy, used for diagnostics.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Safe default policy. Permits responses on datagram transports (which
        /// ignore the address) but rejects every requestor-supplied topic on
        /// topic-based transports (MQTT/JSON), because an arbitrary topic cannot
        /// be trusted. Configure <see cref="Matching"/> to opt specific topics in.
        /// </summary>
        public static PubSubResponseAddressPolicy Default => DenyRequestorTopics;

        /// <summary>
        /// Rejects any non-empty requestor-supplied response topic on topic-based
        /// transports; allows datagram transports and empty addresses.
        /// </summary>
        public static PubSubResponseAddressPolicy DenyRequestorTopics { get; } =
            new(
                "DenyRequestorTopics",
                context => !context.TransportUsesTopics
                    || string.IsNullOrEmpty(context.ResponseAddress));

        /// <summary>
        /// Honors any requestor-supplied response address. This restores the
        /// unrestricted (pre-SA-ACT-03) behavior and exposes the responder as a
        /// publishing proxy on topic-based transports; use only on trusted,
        /// isolated networks.
        /// </summary>
        public static PubSubResponseAddressPolicy AllowAll { get; } =
            new("AllowAll", static _ => true);

        /// <summary>
        /// Allows a response only when the requestor-supplied address matches one
        /// of the supplied patterns. A pattern is matched case-sensitively and may
        /// contain <c>*</c> as a wildcard for any (possibly empty) run of
        /// characters. Datagram transports and empty addresses are always allowed.
        /// </summary>
        /// <param name="patterns">Allowed response-address patterns.</param>
        /// <returns>The configured policy.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="patterns"/> is <see langword="null"/>.
        /// </exception>
        public static PubSubResponseAddressPolicy Matching(params string[] patterns)
        {
            if (patterns is null)
            {
                throw new ArgumentNullException(nameof(patterns));
            }
            string[] copy = (string[])patterns.Clone();
            string description = "Matching(" + string.Join(", ", copy) + ")";
            return new PubSubResponseAddressPolicy(
                description,
                context =>
                {
                    if (!context.TransportUsesTopics
                        || string.IsNullOrEmpty(context.ResponseAddress))
                    {
                        return true;
                    }
                    for (int i = 0; i < copy.Length; i++)
                    {
                        if (MatchesWildcard(copy[i], context.ResponseAddress))
                        {
                            return true;
                        }
                    }
                    return false;
                });
        }

        /// <summary>
        /// Creates a custom policy from a predicate.
        /// </summary>
        /// <param name="description">Diagnostic description of the policy.</param>
        /// <param name="predicate">
        /// Returns <see langword="true"/> to allow the response.
        /// </param>
        /// <returns>The configured policy.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="predicate"/> is <see langword="null"/>.
        /// </exception>
        public static PubSubResponseAddressPolicy Create(
            string description,
            Func<PubSubResponseAddressContext, bool> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }
            return new PubSubResponseAddressPolicy(description ?? string.Empty, predicate);
        }

        /// <summary>
        /// Evaluates whether a response may be published for the supplied context.
        /// </summary>
        /// <param name="context">Response-routing context.</param>
        /// <returns>
        /// <see langword="true"/> if the response address is permitted.
        /// </returns>
        public bool IsAllowed(in PubSubResponseAddressContext context)
        {
            return m_predicate(context);
        }

        private static bool MatchesWildcard(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return string.IsNullOrEmpty(value);
            }
            int patternIndex = 0;
            int valueIndex = 0;
            int starIndex = -1;
            int matchIndex = 0;
            while (valueIndex < value.Length)
            {
                if (patternIndex < pattern.Length
                    && (pattern[patternIndex] == value[valueIndex]))
                {
                    patternIndex++;
                    valueIndex++;
                }
                else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
                {
                    starIndex = patternIndex;
                    matchIndex = valueIndex;
                    patternIndex++;
                }
                else if (starIndex != -1)
                {
                    patternIndex = starIndex + 1;
                    matchIndex++;
                    valueIndex = matchIndex;
                }
                else
                {
                    return false;
                }
            }
            while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                patternIndex++;
            }
            return patternIndex == pattern.Length;
        }
    }
}
