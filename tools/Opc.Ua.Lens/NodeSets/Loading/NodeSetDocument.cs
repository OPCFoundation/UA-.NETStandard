/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
