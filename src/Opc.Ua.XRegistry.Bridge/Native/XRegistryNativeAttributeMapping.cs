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
using System.Linq;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    /// <summary>
    /// Distinguishes logical entities that may share a native ResourceType representation.
    /// </summary>
    public enum XRegistryNativeAttributeScope
    {
        /// <summary>
        /// The registry root.
        /// </summary>
        Registry,

        /// <summary>
        /// One Group in the selected model collection.
        /// </summary>
        Group,

        /// <summary>
        /// The logical Resource, independent of its current default Version identity.
        /// </summary>
        Resource,

        /// <summary>
        /// Resource Meta, using a native path relative to the logical Resource.
        /// </summary>
        Meta,

        /// <summary>
        /// One explicitly identified Version.
        /// </summary>
        Version
    }

    /// <summary>
    /// Selects model-canonical text or an actual OPC UA typed value.
    /// </summary>
    public enum XRegistryNativeAttributeEncoding
    {
        /// <summary>
        /// A native scalar or array with a compatible built-in DataType and ValueRank.
        /// </summary>
        Typed,

        /// <summary>
        /// A String containing a model scalar or canonical JSON for a compound value.
        /// </summary>
        CanonicalString
    }

    /// <summary>
    /// A browse-path segment qualified by namespace URI, never by a cached namespace index.
    /// </summary>
    public sealed record XRegistryNativeBrowseName(string NamespaceUri, string Name);

    /// <summary>
    /// Explicit model-to-native Property mapping. A model path names collection types without entity IDs.
    /// Compound logical objects can map individual leaves, or use canonical JSON String properties.
    /// </summary>
    public sealed record XRegistryNativeAttributeMapping
    {
        /// <summary>
        /// Defines a mapping without connecting, browsing or inferring native addresses.
        /// </summary>
        public XRegistryNativeAttributeMapping(
            string modelPath, XRegistryNativeAttributeScope scope,
            ArrayOf<string> attributePath, ArrayOf<XRegistryNativeBrowseName> browsePath)
        {
            ModelPath = XRegistryPath.Normalize(modelPath);
            Scope = scope;
            AttributePath = attributePath.Span.ToArray();
            BrowsePath = browsePath.Span.ToArray();
            if (scope is < XRegistryNativeAttributeScope.Registry or > XRegistryNativeAttributeScope.Version ||
                attributePath.Count is < 1 or > 32 ||
                browsePath.Count is < 1 or > 32 ||
                attributePath.ToList().Any(string.IsNullOrWhiteSpace) ||
                browsePath.ToList().Any(part => part is null ||
                    string.IsNullOrEmpty(part.Name) ||
                    !Uri.TryCreate(part.NamespaceUri, UriKind.Absolute, out _)) ||
                XRegistryPath.GetSegments(ModelPath).Count != (scope switch
                {
                    XRegistryNativeAttributeScope.Registry => 0,
                    XRegistryNativeAttributeScope.Group => 1,
                    _ => 2
                }))
            {
                throw new ArgumentException(
                    "A mapping requires an exact model scope, logical path and URI-qualified native path.");
            }
        }

        /// <summary>
        /// Gets the model collection path, such as /schemagroups/schemas.
        /// </summary>
        public string ModelPath { get; }

        /// <summary>
        /// Gets the logical entity role.
        /// </summary>
        public XRegistryNativeAttributeScope Scope { get; }

        /// <summary>
        /// Gets literal logical attribute keys, including nested object/map keys.
        /// </summary>
        public ArrayOf<string> AttributePath { get; }

        /// <summary>
        /// Gets the native property path relative to the owning entity node.
        /// Meta mappings are relative to the logical Resource node.
        /// </summary>
        public ArrayOf<XRegistryNativeBrowseName> BrowsePath { get; }

        /// <summary>
        /// Gets the value encoding. Native String fields are not HTTP-percent-encoded headers.
        /// </summary>
        public XRegistryNativeAttributeEncoding Encoding { get; init; }

        /// <summary>
        /// Gets the exact projected built-in type, or Null to choose a lossless model default.
        /// A nondefault type also constrains discovered native values.
        /// </summary>
        public BuiltInType NativeType { get; init; }

        /// <summary>
        /// Optional registered structure activator implementing IStructure. Its field names must match
        /// the logical object model exactly. No reflection or unregistered type discovery is performed.
        /// </summary>
        public IEncodeableType? StructureType { get; init; }

        /// <summary>
        /// Namespace-URI type identity paired with StructureType.
        /// Local nonzero namespace indexes are not stable profiles.
        /// </summary>
        public ExpandedNodeId StructureTypeId { get; init; }

        /// <summary>
        /// Enables qualified native writes. Readonly/immutable model rules and endpoint guarantees still apply.
        /// </summary>
        public bool Writable { get; init; }

        internal bool Matches(string path)
        {
            ArrayOf<string> segments = XRegistryPath.GetSegments(path);
            XRegistryNativeAttributeScope scope = segments.Count switch
            {
                0 => XRegistryNativeAttributeScope.Registry,
                2 => XRegistryNativeAttributeScope.Group,
                4 => XRegistryNativeAttributeScope.Resource,
                5 when segments[4] == "meta" => XRegistryNativeAttributeScope.Meta,
                6 when segments[4] == "versions" => XRegistryNativeAttributeScope.Version,
                _ => (XRegistryNativeAttributeScope)(-1)
            };
            return scope == Scope &&
                ModelPath == (segments.Count == 0 ? "/" :
                    XRegistryPath.FromSegments(segments.Count == 2 ? [segments[0]] : [segments[0], segments[2]]));
        }
    }
}
