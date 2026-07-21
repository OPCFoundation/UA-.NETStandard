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

namespace Opc.Ua.WotCon.Binding
{
    /// <summary>
    /// The stable identity of a protocol binder. A binder is uniquely identified
    /// by its <see cref="Id"/> and <see cref="Version"/>, so multiple versions of
    /// the same binding may be registered and coexist. Deterministic selection
    /// uses the binding identity together with pinned identification rules rather
    /// than the URI scheme alone.
    /// </summary>
    public sealed class WotBindingIdentity : IEquatable<WotBindingIdentity>
    {
        /// <summary>Initializes a new immutable binder identity.</summary>
        /// <param name="id">The stable, human-meaningful binder id (for example "opc.http").</param>
        /// <param name="version">The binder version (for example "1.1.0").</param>
        /// <param name="bindingUri">The protocol-binding vocabulary URI.</param>
        /// <param name="displayName">An optional human-readable display name.</param>
        public WotBindingIdentity(string id, string version, string bindingUri, string? displayName = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Version = version ?? throw new ArgumentNullException(nameof(version));
            BindingUri = bindingUri ?? throw new ArgumentNullException(nameof(bindingUri));
            DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName!;
        }

        /// <summary>Gets the stable, human-meaningful binder id.</summary>
        public string Id { get; }

        /// <summary>Gets the binder version.</summary>
        public string Version { get; }

        /// <summary>Gets the protocol-binding vocabulary URI.</summary>
        public string BindingUri { get; }

        /// <summary>Gets the human-readable display name.</summary>
        public string DisplayName { get; }

        /// <summary>Gets the composite selection key (<c>id@version</c>).</summary>
        public string Key => Id + "@" + Version;

        /// <inheritdoc/>
        public bool Equals(WotBindingIdentity? other)
        {
            return other is not null &&
                string.Equals(Id, other.Id, StringComparison.Ordinal) &&
                string.Equals(Version, other.Version, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as WotBindingIdentity);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(Id) * 397) ^
                    StringComparer.Ordinal.GetHashCode(Version);
            }
        }

        /// <inheritdoc/>
        public override string ToString() => Key;
    }
}
