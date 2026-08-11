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

namespace Opc.Ua.Aas
{
    /// <summary>
    /// The components an AAS String NodeId identifier decomposes into.
    /// </summary>
    /// <remarks>
    /// The encoding of clause 6.1.3 is reversible, so a Client holding a
    /// NodeId recovers the AAS identifier and <c>idShortPath</c> the AAS API
    /// addresses the same entity by, without asking the Server.
    /// </remarks>
    public readonly struct AasParsedNodeId : IEquatable<AasParsedNodeId>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AasParsedNodeId"/> struct.
        /// </summary>
        /// <param name="kind">The node kind.</param>
        /// <param name="id">The Identifiable's identifier, or the owner's identifier for an element.</param>
        /// <param name="idShortPath">The element's path within its owner, or <c>null</c> for an Identifiable.</param>
        public AasParsedNodeId(AasNodeKind kind, string id, string? idShortPath)
        {
            Kind = kind;
            Id = id ?? throw new ArgumentNullException(nameof(id));
            IdShortPath = idShortPath;
        }

        /// <summary>
        /// Gets the kind of node the identifier names.
        /// </summary>
        public AasNodeKind Kind { get; }

        /// <summary>
        /// Gets the authored AAS identifier: the Identifiable's own identifier,
        /// or for a submodel element the identifier of the Identifiable that
        /// owns it.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the metamodel <c>idShortPath</c> of a submodel element within
        /// its owner, or <c>null</c> where the identifier names an Identifiable.
        /// </summary>
        public string? IdShortPath { get; }

        /// <summary>
        /// Gets a value indicating whether the identifier names one of the
        /// three Identifiables rather than a submodel element.
        /// </summary>
        public bool IsIdentifiable => Kind != AasNodeKind.SubmodelElement;

        /// <inheritdoc/>
        public bool Equals(AasParsedNodeId other)
        {
            return Kind == other.Kind &&
                string.Equals(Id, other.Id, StringComparison.Ordinal) &&
                string.Equals(IdShortPath, other.IdShortPath, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is AasParsedNodeId other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Kind, Id, IdShortPath);
        }

        /// <summary>
        /// Compares two parsed identifiers for equality.
        /// </summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <returns><c>true</c> when the two describe the same entity.</returns>
        public static bool operator ==(AasParsedNodeId left, AasParsedNodeId right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two parsed identifiers for inequality.
        /// </summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <returns><c>true</c> when the two describe different entities.</returns>
        public static bool operator !=(AasParsedNodeId left, AasParsedNodeId right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return IdShortPath is null
                ? $"{Kind}({Id})"
                : $"{Kind}({Id}#{IdShortPath})";
        }
    }
}
