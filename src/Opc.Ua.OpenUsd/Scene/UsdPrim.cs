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
using System.Text;

namespace Opc.Ua.OpenUsd.Scene
{
    /// <summary>
    /// A prim in a composed USD stage — the unit that becomes an OPC UA Object in the
    /// materialized address space (draft OPC UA — OpenUSD Scene Materialization §5.2, §5.3).
    /// The prim hierarchy *is* the node hierarchy, so browsing the address space is
    /// browsing the scene.
    /// </summary>
    public sealed class UsdPrim
    {
        /// <summary>
        /// Creates a prim.
        /// </summary>
        /// <param name="name">The prim name (the last element of its path).</param>
        /// <param name="typeName">The USD schema type token, or an empty string when the
        /// prim is untyped.</param>
        public UsdPrim(string name, string typeName = "")
        {
            Name = name ?? string.Empty;
            TypeName = typeName ?? string.Empty;
        }

        /// <summary>
        /// The prim name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The USD schema type token exactly as authored (for example <c>Xform</c>,
        /// <c>Mesh</c>). Retained even when the type is unknown to this model, so an
        /// unknown typed schema degrades rather than being dropped (§8.4).
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// Whether the prim is a definition, an override or a class.
        /// </summary>
        public UsdSpecifierEnum Specifier { get; set; } = UsdSpecifierEnum.Def;

        /// <summary>
        /// The model kind of the prim.
        /// </summary>
        public UsdPrimKindEnum Kind { get; set; } = UsdPrimKindEnum.Unspecified;

        /// <summary>
        /// Whether the prim is active. An inactive prim is pruned from the composed scene.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Whether the prim is instanceable.
        /// </summary>
        public bool Instanceable { get; set; }

        /// <summary>
        /// Free-form documentation authored on the prim.
        /// </summary>
        public string Documentation { get; set; } = string.Empty;

        /// <summary>
        /// The prim's attributes, in authored order.
        /// </summary>
        public IList<UsdAttribute> Attributes { get; } = new List<UsdAttribute>();

        /// <summary>
        /// The prim's relationships, in authored order.
        /// </summary>
        public IList<UsdRelationship> Relationships { get; } = new List<UsdRelationship>();

        /// <summary>
        /// The prim's composition arcs (§5.6).
        /// </summary>
        public IList<UsdCompositionArc> Composition { get; } = new List<UsdCompositionArc>();

        /// <summary>
        /// The API schemas applied to the prim (§5.6, §8.2).
        /// </summary>
        public IList<UsdApiSchema> ApiSchemas { get; } = new List<UsdApiSchema>();

        /// <summary>
        /// The prim's variant sets and their selections (§5.6).
        /// </summary>
        public IList<UsdVariantSet> VariantSets { get; } = new List<UsdVariantSet>();

        /// <summary>
        /// Metadata authored on the prim that has no well-known typed member; materialized
        /// under the prim's <c>Metadata</c> folder (§6.1).
        /// </summary>
        public IDictionary<string, UsdValue> Metadata { get; } =
            new Dictionary<string, UsdValue>(StringComparer.Ordinal);

        /// <summary>
        /// The child prims, in authored order.
        /// </summary>
        public IList<UsdPrim> Children { get; } = new List<UsdPrim>();

        /// <summary>
        /// The parent prim, or <c>null</c> for a root prim.
        /// </summary>
        public UsdPrim? Parent { get; internal set; }

        /// <summary>
        /// The absolute SdfPath of the prim, composed from the parent chain.
        /// </summary>
        public string Path
        {
            get
            {
                var parts = new List<string>();
                for (UsdPrim? c = this; c != null; c = c.Parent)
                {
                    parts.Add(c.Name);
                }
                parts.Reverse();
                var sb = new StringBuilder();
                foreach (string part in parts)
                {
                    sb.Append('/').Append(part);
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// Adds a child prim and links it to this parent.
        /// </summary>
        /// <param name="child">The child to add.</param>
        /// <returns>The added child, for chaining.</returns>
        public UsdPrim AddChild(UsdPrim child)
        {
            if (child == null)
            {
                throw new ArgumentNullException(nameof(child));
            }
            child.Parent = this;
            Children.Add(child);
            return child;
        }

        /// <summary>
        /// Finds a prim at the given absolute path within this subtree.
        /// </summary>
        /// <param name="path">The absolute SdfPath to find.</param>
        /// <returns>The prim, or <c>null</c> when the subtree contains no such path.</returns>
        public UsdPrim? Find(string path)
        {
            if (string.Equals(Path, path, StringComparison.Ordinal))
            {
                return this;
            }
            foreach (UsdPrim child in Children)
            {
                UsdPrim? found = child.Find(path);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        /// <summary>
        /// Enumerates this prim and every descendant, depth first in authored order.
        /// </summary>
        /// <returns>The prim subtree.</returns>
        public IEnumerable<UsdPrim> DescendantsAndSelf()
        {
            yield return this;
            foreach (UsdPrim child in Children)
            {
                foreach (UsdPrim descendant in child.DescendantsAndSelf())
                {
                    yield return descendant;
                }
            }
        }
    }
}
