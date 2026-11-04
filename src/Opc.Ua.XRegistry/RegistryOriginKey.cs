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

namespace Opc.Ua.XRegistry
{
    /// <summary>
    /// An immutable registry origin: an authoritative OriginUri, or an ApplicationUri paired
    /// with a portable registry-root NodeId. Locators and Session indexes are not identity.
    /// </summary>
    public sealed class RegistryOriginKey : IEquatable<RegistryOriginKey>
    {
        /// <summary>
        /// Captures and validates the origin fields of an entity reference.
        /// </summary>
        public RegistryOriginKey(RegistryEntityReferenceDataType reference)
        {
            if (reference is null)
            {
                throw new ArgumentNullException(nameof(reference));
            }
            OriginUri = reference.OriginUri ?? string.Empty;
            ApplicationUri = reference.ApplicationUri ?? string.Empty;
            RegistryNode = reference.RegistryNode;
            if (OriginUri.Length > 0)
            {
                Absolute(OriginUri);
                if (ApplicationUri.Length > 0 || !RegistryNode.IsNull)
                {
                    throw new ArgumentException("An origin must have exactly one authoritative form.",
                        nameof(reference));
                }
            }
            else
            {
                Absolute(ApplicationUri);
                if (RegistryNode.IsNull || string.IsNullOrEmpty(RegistryNode.NamespaceUri) ||
                    RegistryNode.NamespaceIndex != 0 || RegistryNode.ServerIndex != 0)
                {
                    throw new ArgumentException("The registry root requires a portable NodeId without Session indexes.",
                        nameof(reference));
                }
                Absolute(RegistryNode.NamespaceUri);
            }
        }

        /// <summary>Gets the authoritative origin URI, or an empty String for the paired form.</summary>
        public string OriginUri { get; }

        /// <summary>Gets the application URI, or an empty String for the URI form.</summary>
        public string ApplicationUri { get; }

        /// <summary>Gets the portable registry-root NodeId, or a null NodeId for the URI form.</summary>
        public ExpandedNodeId RegistryNode { get; }

        /// <inheritdoc/>
        public bool Equals(RegistryOriginKey? other)
        {
            return other is not null &&
                string.Equals(OriginUri, other.OriginUri, StringComparison.Ordinal) &&
                string.Equals(ApplicationUri, other.ApplicationUri, StringComparison.Ordinal) &&
                RegistryNode == other.RegistryNode;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is RegistryOriginKey other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(OriginUri, StringComparer.Ordinal);
            hash.Add(ApplicationUri, StringComparer.Ordinal);
            hash.Add(RegistryNode);
            return hash.ToHashCode();
        }

        private static void Absolute(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("A registry origin requires an absolute URI.");
            }
        }
    }
}
