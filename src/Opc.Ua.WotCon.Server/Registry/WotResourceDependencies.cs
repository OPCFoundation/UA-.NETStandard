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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// An authored semantic reference indexed when an exact Version is stored.
    /// LookupUri is expanded in the original carrying context; TargetUri is never rewritten.
    /// </summary>
    public sealed class WotResourceReference
    {
        /// <summary>
        /// Initializes an immutable reference.
        /// </summary>
        public WotResourceReference(string targetUri, string lookupUri, string refType, bool requiresOrdering)
            : this(targetUri, lookupUri, refType, requiresOrdering, WotResourceReferenceLookup.Document)
        {
        }

        internal WotResourceReference(
            string targetUri, string lookupUri, string refType, bool requiresOrdering, WotResourceReferenceLookup lookup)
        {
            TargetUri = targetUri ?? throw new ArgumentNullException(nameof(targetUri));
            LookupUri = lookupUri ?? throw new ArgumentNullException(nameof(lookupUri));
            RefType = refType ?? throw new ArgumentNullException(nameof(refType));
            RequiresOrdering = requiresOrdering;
            Lookup = lookup;
        }

        /// <summary>
        /// Gets the original authored target.
        /// </summary>
        public string TargetUri { get; }

        /// <summary>
        /// Gets the contextual lookup identity, not permission to fetch it.
        /// </summary>
        public string LookupUri { get; }

        /// <summary>
        /// Gets the semantic reference kind.
        /// </summary>
        public string RefType { get; }

        /// <summary>
        /// Gets whether this edge imposes dependency-first ordering.
        /// </summary>
        public bool RequiresOrdering { get; }

        internal WotResourceReferenceLookup Lookup { get; }
    }

    /// <summary>
    /// Immutable content-derived dependency metadata. It permits selection and reverse
    /// dependency expansion without acquiring unselected document bodies.
    /// </summary>
    public sealed class WotResourceDependencies
    {
        /// <summary>
        /// Initializes metadata for the supplied exact content digest.
        /// </summary>
        public WotResourceDependencies(
            ByteString contentDigest,
            ArrayOf<WotResourceReference> references,
            ArrayOf<string> ownedModelUris,
            ArrayOf<string> requiredModelUris,
            ArrayOf<string> definedNodeIds,
            string error = "")
            : this(contentDigest, references, ownedModelUris, requiredModelUris, definedNodeIds, error, [], [])
        {
        }

        internal WotResourceDependencies(
            ByteString contentDigest,
            ArrayOf<WotResourceReference> references,
            ArrayOf<string> ownedModelUris,
            ArrayOf<string> requiredModelUris,
            ArrayOf<string> definedNodeIds,
            string error,
            ArrayOf<string> dataTypeDefinitionIds,
            ArrayOf<string> dataTypeDefinitionNames)
        {
            ContentDigest = ByteString.From(contentDigest.Span.ToArray());
            References = references.Span.ToArray().ToArrayOf();
            OwnedModelUris = ownedModelUris.Span.ToArray().ToArrayOf();
            RequiredModelUris = requiredModelUris.Span.ToArray().ToArrayOf();
            DefinedNodeIds = definedNodeIds.Span.ToArray().ToArrayOf();
            DataTypeDefinitionIds = dataTypeDefinitionIds.Span.ToArray().ToArrayOf();
            DataTypeDefinitionNames = dataTypeDefinitionNames.Span.ToArray().ToArrayOf();
            Error = error ?? throw new ArgumentNullException(nameof(error));
        }

        /// <summary>
        /// Gets the source bytes' digest, binding this index to one exact input.
        /// </summary>
        public ByteString ContentDigest { get; }

        /// <summary>
        /// Gets the semantic outgoing references.
        /// </summary>
        public ArrayOf<WotResourceReference> References { get; }

        /// <summary>
        /// Gets the native model partitions declared by this document.
        /// </summary>
        public ArrayOf<string> OwnedModelUris { get; }

        /// <summary>
        /// Gets the model namespaces required by this document.
        /// </summary>
        public ArrayOf<string> RequiredModelUris { get; }

        /// <summary>
        /// Gets the document's declared portable Node identities.
        /// </summary>
        public ArrayOf<string> DefinedNodeIds { get; }

        /// <summary>
        /// Gets an indexing failure. Empty means the index is complete for this document.
        /// </summary>
        public string Error { get; }

        internal ArrayOf<string> DataTypeDefinitionIds { get; }
        internal ArrayOf<string> DataTypeDefinitionNames { get; }

        internal const int CurrentIndexVersion = 2;
    }

    internal enum WotResourceReferenceLookup
    {
        Document,
        DataTypeName,
        DataTypeNodeId
    }
}
