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

namespace Opc.Ua.PubSub.Adapter.Session
{
    /// <summary>
    /// Helpers for expressing a node in adapter mapping configuration as a
    /// relative <em>browse path</em> instead of a concrete <see cref="NodeId"/>.
    /// A browse path is carried as a sentinel <see cref="NodeId"/> whose string
    /// identifier starts with a hierarchical separator (<c>/</c>) or an
    /// aggregates separator (<c>.</c>), for example <c>/2:Demo/2:CurrentTime</c>.
    /// The adapter resolves such sentinels to concrete NodeIds through
    /// <see cref="IServerSession.ResolveNodeIdAsync"/> the first time the node is
    /// used (the result is cached) so any read, write or method-call mapping can
    /// be authored without knowing the server-assigned identifiers in advance.
    /// </summary>
    /// <remarks>
    /// Each segment is parsed with <see cref="QualifiedName.Parse(string)"/> so a
    /// namespace-qualified browse name (<c>2:CurrentTime</c>) selects the target
    /// namespace. Hierarchical (<c>/</c>) segments resolve through
    /// <see cref="ReferenceTypeIds.HierarchicalReferences"/> and aggregates
    /// (<c>.</c>) segments through <see cref="ReferenceTypeIds.Aggregates"/>
    /// (subtypes included). Named reference types are not supported in this
    /// shorthand; supply a concrete <see cref="NodeId"/> for those cases.
    /// </remarks>
    public static class NodeBrowsePath
    {
        /// <summary>
        /// Creates a sentinel <see cref="NodeId"/> that carries the supplied
        /// relative browse path (for example <c>/2:Demo/2:CurrentTime</c>),
        /// resolved relative to the Objects folder when first used.
        /// </summary>
        /// <param name="relativePath">
        /// The relative browse path starting with <c>/</c> or <c>.</c>.
        /// </param>
        /// <returns>
        /// A sentinel <see cref="NodeId"/> understood by
        /// <see cref="IServerSession.ResolveNodeIdAsync"/>.
        /// </returns>
        /// <exception cref="ArgumentException"></exception>
        public static NodeId ToNodeId(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                throw new ArgumentException(
                    "Relative path must be specified.", nameof(relativePath));
            }
            if (!IsBrowsePathText(relativePath))
            {
                throw new ArgumentException(
                    "A relative browse path must start with '/' or '.'.",
                    nameof(relativePath));
            }
            return new NodeId(relativePath, 0);
        }

        /// <summary>
        /// Indicates whether the supplied <see cref="NodeId"/> is a browse-path
        /// sentinel (a namespace-zero string identifier starting with <c>/</c> or
        /// <c>.</c>) rather than a concrete node identifier.
        /// </summary>
        /// <param name="nodeId">
        /// The node identifier to test.
        /// </param>
        /// <returns>
        /// <c>true</c> when the value carries a relative browse path; otherwise
        /// <c>false</c>.
        /// </returns>
        public static bool IsBrowsePath(NodeId nodeId)
        {
            return !nodeId.IsNull &&
                nodeId.IdType == IdType.String &&
                nodeId.NamespaceIndex == 0 &&
                nodeId.IdentifierAsString is { Length: > 0 } text &&
                IsBrowsePathText(text);
        }

        /// <summary>
        /// Converts a browse-path sentinel <see cref="NodeId"/> into the
        /// <see cref="Opc.Ua.RelativePath"/> that a TranslateBrowsePathsToNodeIds
        /// request requires.
        /// </summary>
        /// <param name="nodeId">
        /// The browse-path sentinel created by <see cref="ToNodeId"/>.
        /// </param>
        /// <returns>
        /// The parsed relative path.
        /// </returns>
        /// <exception cref="ArgumentException"></exception>
        public static RelativePath ToRelativePath(NodeId nodeId)
        {
            if (!IsBrowsePath(nodeId))
            {
                throw new ArgumentException(
                    "The node id does not carry a relative browse path.",
                    nameof(nodeId));
            }
            return ParseRelativePath(nodeId.IdentifierAsString);
        }

        private static bool IsBrowsePathText(string text)
        {
            return text.Length > 0 && (text[0] == '/' || text[0] == '.');
        }

        private static RelativePath ParseRelativePath(string text)
        {
            var elements = new List<RelativePathElement>();
            int index = 0;
            while (index < text.Length)
            {
                char separator = text[index];
                index++;
                int start = index;
                while (index < text.Length && text[index] != '/' && text[index] != '.')
                {
                    // Allow an escaped separator inside a browse name.
                    if (text[index] == '&' && index + 1 < text.Length)
                    {
                        index++;
                    }
                    index++;
                }

                string segment = text.Substring(start, index - start);
                if (segment.Length == 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSyntaxError,
                        "Empty browse name in relative path '{0}'.",
                        text);
                }

                elements.Add(new RelativePathElement
                {
                    ReferenceTypeId = separator == '.'
                        ? ReferenceTypeIds.Aggregates
                        : ReferenceTypeIds.HierarchicalReferences,
                    IsInverse = false,
                    IncludeSubtypes = true,
                    TargetName = QualifiedName.Parse(segment)
                });
            }

            return new RelativePath { Elements = elements.ToArrayOf() };
        }
    }
}
