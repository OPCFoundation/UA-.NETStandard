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
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Opc.Ua.XRegistry
{
    /// <summary>
    /// Constructs the symbolic <c>GroupId</c> and <c>ResourceId</c> identifiers
    /// an xRegistry entity is known by.
    /// </summary>
    /// <remarks>
    /// See xRegistry Section 6.9. An identifier is derived from the entity's
    /// source identity - the domain-defined string naming what the entity is -
    /// and never from a document, a digest or a fingerprint of one. A resource
    /// is a stable umbrella over its versions, so its identifier has to stay
    /// invariant while its document changes from version to version.
    /// <para>
    /// The result reads like a reverse-DNS symbol, for example
    /// <c>org.contoso.assets.pump</c>, and uses only
    /// <c>A-Z a-z 0-9 _ . -</c> so that one identifier is simultaneously safe
    /// in a URL, on a command line, and as a file name.
    /// </para>
    /// <para>
    /// The construction is deliberately lossy and one-way: distinct source
    /// identities can normalize to the same token, which is what the
    /// allocator resolves. A consumer obtains the assigned identifier from the
    /// registry or resolves the exact source-identity Property. Allocation must
    /// reuse the registry's durable authority mapping before constructing a new
    /// candidate. Never attempt to recover an identity by inverting a token.
    /// </para>
    /// </remarks>
    public static class XRegistryIdentifier
    {
        /// <summary>
        /// The identifier used when no label of the source identity survives
        /// normalization.
        /// </summary>
        public const string Empty = "_";

        /// <summary>
        /// The greatest length an identifier may have.
        /// </summary>
        public const int MaxLength = 128;

        /// <summary>
        /// The maximum collision ordinal attempted after all SHA-256 suffix lengths are occupied.
        /// </summary>
        public const uint MaxCollisionOrdinal = 1024;

        /// <summary>
        /// Constructs the symbolic identifier for a source identity.
        /// </summary>
        /// <param name="sourceIdentity">
        /// The domain-defined string naming what the entity is, for example an
        /// OPC UA namespace URI, an authored asset identifier or a W3C Thing
        /// identifier.
        /// </param>
        /// <returns>The symbolic identifier.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="sourceIdentity"/> is <c>null</c>.
        /// </exception>
        public static string FromSourceIdentity(string sourceIdentity)
        {
            if (sourceIdentity is null)
            {
                throw new ArgumentNullException(nameof(sourceIdentity));
            }

            return Allocate(sourceIdentity, [], string.Empty, MaxCollisionOrdinal);
        }

        /// <summary>
        /// Constructs the symbolic identifier for a source identity, appending
        /// the disambiguator when the result would collide with a sibling.
        /// </summary>
        /// <remarks>
        /// A collision is tested case-insensitively, because the identifier has
        /// to be unambiguous in the static-file-server representation as well,
        /// where a file name may not be case sensitive.
        /// </remarks>
        /// <param name="sourceIdentity">The entity's source identity.</param>
        /// <param name="siblings">
        /// The identifiers already in use in the same collection.
        /// </param>
        /// <returns>The symbolic identifier.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="sourceIdentity"/> or <paramref name="siblings"/> is
        /// <c>null</c>.
        /// </exception>
        public static string FromSourceIdentity(
            string sourceIdentity,
            IEnumerable<string> siblings)
        {
            if (sourceIdentity is null)
            {
                throw new ArgumentNullException(nameof(sourceIdentity));
            }
            if (siblings is null)
            {
                throw new ArgumentNullException(nameof(siblings));
            }

            return Allocate(sourceIdentity, siblings, string.Empty, MaxCollisionOrdinal);
        }

        /// <summary>
        /// Allocates a bounded identifier in a domain's readable prefix, after the caller has
        /// checked its durable exact-authority map under the same transaction.
        /// </summary>
        /// <param name="sourceIdentity">The exact source identity, never document bytes or a content digest.</param>
        /// <param name="siblings">Occupied identifiers, compared case-insensitively.</param>
        /// <param name="prefix">A readable domain prefix, such as <c>td.</c> or <c>tm.</c>.</param>
        /// <param name="maxCollisionOrdinal">The finite positive bound for the final collision ordinal.</param>
        /// <exception cref="ArgumentNullException">The source identity or prefix is null.</exception>
        /// <exception cref="ArgumentException">The prefix is not an identifier-safe domain prefix.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The ordinal bound is zero.</exception>
        /// <exception cref="InvalidOperationException">All bounded candidates are occupied.</exception>
        public static string FromSourceIdentity(
            string sourceIdentity,
            ArrayOf<string> siblings,
            string prefix,
            uint maxCollisionOrdinal = MaxCollisionOrdinal)
        {
            if (sourceIdentity is null)
            {
                throw new ArgumentNullException(nameof(sourceIdentity));
            }
            if (prefix is null)
            {
                throw new ArgumentNullException(nameof(prefix));
            }
            if (prefix.Length > 32 ||
                (prefix.Length > 0 &&
                    (prefix[0] is '.' or '-' || prefix[^1] != '.' || Trim(prefix).Length == 0)))
            {
                throw new ArgumentException("A domain prefix must be readable and end with '.'.", nameof(prefix));
            }
            foreach (char c in prefix)
            {
                if (!IsAllowed(c))
                {
                    throw new ArgumentException("The domain prefix contains an invalid character.", nameof(prefix));
                }
            }
            if (maxCollisionOrdinal == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCollisionOrdinal));
            }
            return Allocate(sourceIdentity, siblings.ToArray() ?? [], prefix, maxCollisionOrdinal);
        }

        /// <summary>
        /// Computes the disambiguator for a source identity.
        /// </summary>
        /// <remarks>
        /// The first eight lower-case hexadecimal characters of the SHA-256 of
        /// the UTF-8 encoding of the exact source identity. It is a function of
        /// the identity rather than of any document, so it does not change when
        /// a new version is written.
        /// </remarks>
        /// <param name="sourceIdentity">The entity's source identity.</param>
        /// <returns>The eight-character disambiguator.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="sourceIdentity"/> is <c>null</c>.
        /// </exception>
        public static string Disambiguator(string sourceIdentity)
        {
            if (sourceIdentity is null)
            {
                throw new ArgumentNullException(nameof(sourceIdentity));
            }
            return IdentityHash(sourceIdentity)[..DisambiguatorLength];
        }

        private static string IdentityHash(string sourceIdentity)
        {
#if NET5_0_OR_GREATER
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourceIdentity));
#else
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sourceIdentity));
#endif
            var builder = new StringBuilder(64);
            for (int ii = 0; ii < hash.Length; ii++)
            {
                builder.Append(hash[ii].ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static List<string> CollectLabels(string sourceIdentity)
        {
            SplitIdentity(
                sourceIdentity,
                out string authority,
                out string port,
                out List<string> pathSegments);

            var labels = new List<string>();
            AppendAuthorityLabels(authority, port, labels);
            foreach (string segment in pathSegments)
            {
                AppendNormalized(PercentDecode(segment), labels);
            }
            return labels;
        }

        private static void SplitIdentity(
            string sourceIdentity,
            out string authority,
            out string port,
            out List<string> pathSegments)
        {
            authority = string.Empty;
            port = string.Empty;

            int scheme = sourceIdentity.IndexOf("://", StringComparison.Ordinal);
            if (scheme > 0)
            {
                // An absolute URI with an authority: the host and its port
                // become the authority, the URI path becomes the path, and the
                // scheme, userinfo, query and fragment are discarded.
                string rest = StripQueryAndFragment(sourceIdentity[(scheme + 3)..]);
                int slash = rest.IndexOf('/', StringComparison.Ordinal);
                string hostPart = slash < 0 ? rest : rest[..slash];
                string pathPart = slash < 0 ? string.Empty : rest[slash..];
                pathPart = StripQueryAndFragment(pathPart);

                int at = hostPart.LastIndexOf('@');
                if (at >= 0)
                {
                    hostPart = hostPart[(at + 1)..];
                }
                int colon = hostPart.LastIndexOf(':');
                int bracket = hostPart.LastIndexOf(']');
                if (colon >= 0 && colon > bracket)
                {
                    port = hostPart[(colon + 1)..];
                    hostPart = hostPart[..colon];
                }
                authority = hostPart;
                pathSegments = [.. pathPart.Split('/')];
                return;
            }

            if (sourceIdentity.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
            {
                // A URN has no authority, and is split on ':' so that the
                // leading urn survives as the first label and a URN can never
                // alias a bare path.
                pathSegments = [.. StripQueryAndFragment(sourceIdentity).Split(':')];
                return;
            }

            pathSegments = [.. sourceIdentity.Split('/')];
        }

        private static void AppendAuthorityLabels(
            string authority,
            string port,
            List<string> labels)
        {
            if (authority.Length > 0)
            {
                string[] parts = authority.Split('.');
                for (int ii = parts.Length - 1; ii >= 0; ii--)
                {
                    AppendNormalized(parts[ii], labels);
                }
            }
            if (port.Length > 0)
            {
                AppendNormalized(port, labels);
            }
        }

        private static void AppendNormalized(string label, List<string> labels)
        {
            string normalized = Normalize(label);
            if (normalized.Length > 0)
            {
                labels.Add(normalized);
            }
        }

        private static string Normalize(string label)
        {
            if (label.Length == 0)
            {
                return string.Empty;
            }

            // Replace every run of characters outside the output alphabet with
            // a single '-', then collapse runs of '-' and of '.', and strip
            // them from both ends. Letter case is preserved.
            var builder = new StringBuilder(label.Length);
            bool pendingDash = false;
            foreach (char c in label)
            {
                if (IsAllowed(c))
                {
                    if (pendingDash)
                    {
                        Append(builder, '-');
                        pendingDash = false;
                    }
                    Append(builder, c);
                    continue;
                }
                pendingDash = true;
            }
            return Trim(builder.ToString());
        }

        private static void Append(StringBuilder builder, char c)
        {
            if (builder.Length == 0)
            {
                builder.Append(c);
                return;
            }
            char last = builder[^1];
            if ((c == '-' && last == '-') || (c == '.' && last == '.'))
            {
                return;
            }
            builder.Append(c);
        }

        private static string Trim(string value)
        {
            int start = 0;
            int end = value.Length;
            while (start < end && (value[start] == '-' || value[start] == '.'))
            {
                start++;
            }
            while (end > start && (value[end - 1] == '-' || value[end - 1] == '.'))
            {
                end--;
            }
            return value[start..end];
        }

        private static bool IsAllowed(char c)
        {
            return c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
                or '_' or '.' or '-';
        }

        private static string StripQueryAndFragment(string value)
        {
            int cut = value.IndexOfAny(['?', '#']);
            return cut < 0 ? value : value[..cut];
        }

        private static string PercentDecode(string segment)
        {
            if (segment.IndexOf('%', StringComparison.Ordinal) < 0)
            {
                return segment;
            }
            try
            {
                return Uri.UnescapeDataString(segment);
            }
            catch (UriFormatException)
            {
                return segment;
            }
        }

        private static string Allocate(
            string sourceIdentity,
            IEnumerable<string> siblings,
            string prefix,
            uint maxCollisionOrdinal)
        {
            List<string> labels = CollectLabels(sourceIdentity);
            string readable = prefix + (labels.Count == 0 ? Empty : string.Join(".", labels));
            var occupied = new HashSet<string>(siblings, StringComparer.OrdinalIgnoreCase);
            if (readable.Length <= MaxLength && !occupied.Contains(readable))
            {
                return readable;
            }
            string hash = IdentityHash(sourceIdentity);
            for (int length = DisambiguatorLength; length <= hash.Length; length *= 2)
            {
                string candidate = WithSuffix(readable, hash[..length]);
                if (!occupied.Contains(candidate))
                {
                    return candidate;
                }
            }
            for (uint ordinal = 1; ; ordinal++)
            {
                string candidate = WithSuffix(
                    readable,
                    hash + "." + ordinal.ToString(CultureInfo.InvariantCulture));
                if (!occupied.Contains(candidate))
                {
                    return candidate;
                }
                if (ordinal == maxCollisionOrdinal)
                {
                    throw new InvalidOperationException("The bounded identifier collision sequence is exhausted.");
                }
            }
        }

        private static string WithSuffix(string readable, string suffix)
        {
            int budget = MaxLength - 1 - suffix.Length;
            string prefix = readable[..Math.Min(readable.Length, budget)].TrimEnd('.', '-');
            return prefix + "." + suffix;
        }

        private const int DisambiguatorLength = 8;
    }
}
