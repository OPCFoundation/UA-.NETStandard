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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Export;

namespace UaLens.NodeSets.Loading
{
    /// <summary>
    /// A parsed document and the local path or approved repository URL that supplied it.
    /// </summary>
    internal sealed record NodeSetDocument(string Source, UANodeSet NodeSet)
    {
        internal long SizeBytes { get; init; }

        public static Task<NodeSetDocument> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            return NodeSetDocumentReader.ReadAsync(path, cancellationToken);
        }
    }

    /// <summary>
    /// A required model revision. Version is a display label, not an ordered version.
    /// ModelVersion takes precedence over PublicationDate as defined by Part 6, Annex F.
    /// </summary>
    internal sealed record NodeSetRequirement(
        string ModelUri,
        string? Version = null,
        DateTime? PublicationDate = null,
        string? ModelVersion = null);

    /// <summary>
    /// Resolves a missing model. Returning null cancels the entire open operation.
    /// </summary>
    internal interface INodeSetDependencyResolver
    {
        Task<NodeSetDocument?> ResolveAsync(NodeSetRequirement requirement, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Loads a complete dependency closure without publishing a partial result.
    /// </summary>
    internal interface INodeSetLoader
    {
        Task<ArrayOf<NodeSetDocument>> LoadAsync(
            ArrayOf<string> paths,
            INodeSetDependencyResolver resolver,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Finds models in an explicitly approved repository, not at arbitrary namespace URLs.
    /// </summary>
    internal interface INodeSetRepository
    {
        Task<NodeSetDocument?> FindAsync(
            NodeSetRequirement requirement,
            CancellationToken cancellationToken = default);
    }
}
