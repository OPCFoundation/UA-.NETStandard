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

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Information read from a referenced assembly's
    /// <c>OpcUaModelDependencyAttribute</c>. Value-type equatable so the
    /// incremental source-generator cache keys behave correctly.
    /// </summary>
    internal readonly struct ModelDependencyReference : IEquatable<ModelDependencyReference>
    {
        public ModelDependencyReference(
            string assemblyName,
            string modelUri,
            string prefix,
            string version,
            string publicationDate)
        {
            AssemblyName = assemblyName ?? string.Empty;
            ModelUri = modelUri ?? string.Empty;
            Prefix = prefix ?? string.Empty;
            Version = version ?? string.Empty;
            PublicationDate = publicationDate ?? string.Empty;
        }

        /// <summary>The assembly the attribute was read from.</summary>
        public string AssemblyName { get; }

        /// <summary>The OPC UA model URI recorded in the attribute.</summary>
        public string ModelUri { get; }

        /// <summary>The C# namespace the assembly used for this model's generated types.</summary>
        public string Prefix { get; }

        /// <summary>The model version string (may be empty).</summary>
        public string Version { get; }

        /// <summary>The model publication date (ISO-8601; may be empty).</summary>
        public string PublicationDate { get; }

        /// <inheritdoc/>
        public bool Equals(ModelDependencyReference other)
        {
            return string.Equals(AssemblyName, other.AssemblyName, StringComparison.Ordinal)
                && string.Equals(ModelUri, other.ModelUri, StringComparison.Ordinal)
                && string.Equals(Prefix, other.Prefix, StringComparison.Ordinal)
                && string.Equals(Version, other.Version, StringComparison.Ordinal)
                && string.Equals(PublicationDate, other.PublicationDate, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is ModelDependencyReference other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(AssemblyName);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(ModelUri);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Prefix);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Version);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(PublicationDate);
                return hash;
            }
        }

        public static bool operator ==(ModelDependencyReference left, ModelDependencyReference right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ModelDependencyReference left, ModelDependencyReference right)
        {
            return !left.Equals(right);
        }
    }
}
