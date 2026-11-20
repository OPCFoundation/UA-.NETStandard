// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Serves local regression fixtures by their relative reference.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Some fixtures reference one another by relative path, so converting one
    /// can require this resolver to expose the rest of the local document set.
    /// </para>
    /// <para>
    /// Nothing here reaches the network. WoT Binding Section 5.1.5 resolves a
    /// reference against the documents held together with the referring one,
    /// and a test that fetched an example over HTTP would be testing the
    /// network rather than the mapping.
    /// </para>
    /// </remarks>
    internal sealed class WotSpecExampleResolver : IWotThingResolver
    {
        /// <summary>
        /// The shared instance. The resolver holds no state across calls.
        /// </summary>
        public static WotSpecExampleResolver Instance { get; } = new WotSpecExampleResolver();

        /// <inheritdoc/>
        public ValueTask<WotResolverResult> ResolveThingAsync(
            string reference,
            WotResolutionContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = reference;
            int slash = name.LastIndexOf('/', StringComparison.Ordinal);
            if (slash >= 0)
            {
                name = name.Substring(slash + 1);
            }
            if (!name.EndsWith(".jsonld", StringComparison.Ordinal) ||
                !TryReadExample(name, out byte[] bytes))
            {
                return new ValueTask<WotResolverResult>(WotResolverResult.NotFound);
            }
            return new ValueTask<WotResolverResult>(
                WotResolverResult.FromBytes(bytes, "application/tm+json"));
        }

        /// <summary>
        /// Reads one embedded fixture, or reports that the fixture set does
        /// not hold it.
        /// </summary>
        internal static bool TryReadExample(string name, out byte[] bytes)
        {
            Assembly assembly = typeof(WotSpecExampleResolver).Assembly;
            string? resource = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(
                    ".Wot.Assets." + name, StringComparison.Ordinal));
            if (resource is null)
            {
                bytes = [];
                return false;
            }
            using Stream stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing fixture '{name}'.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
            return true;
        }

        /// <summary>
        /// Converts one fixture with the rest of the set available to it.
        /// </summary>
        internal static async Task<WotConversionResult<UANodeSet>> ConvertAsync(
            WotDocument document)
        {
            return await WotNodeSetConverter
                .ToNodeSetResultAsync(document, null, Instance, null, NodeResolver)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// The address space a local fixture assumes a Server already holds.
        /// </summary>
        /// <remarks>
        /// Fixture 01 places its Pump under a production line the Server owns
        /// (<c>uav:componentOf</c> naming <c>...;s=Line01</c>). That Node is
        /// not in any document of the set, and it is not supposed to be: the
        /// point of the term is to attach a converted document to something
        /// that already exists. Converting the example without a Server to
        /// attach to reports the parent as unresolved, correctly, so a test
        /// that exercises the conversion has to model the Server - which is all
        /// this resolver is.
        /// </remarks>
        internal static IWotNodeResolver NodeResolver { get; } = new ExampleNodeResolver();

        private sealed class ExampleNodeResolver : IWotNodeResolver
        {
            public ValueTask<bool> HoldsNamespaceAsync(
                string namespaceUri, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<bool>(string.Equals(
                    namespaceUri, PumpNamespace, StringComparison.Ordinal));
            }

            public ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
                string namespaceUri,
                string browseName,
                WotExpectedNodeClass expected,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<ArrayOf<WotResolvedNode>>(
                    ArrayOf<WotResolvedNode>.Empty);
            }

            public ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
                string expandedNodeId, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Only the parent the examples attach to. Answering anything
                // else would let a test pass because the resolver invented a
                // Node rather than because the mapping found one.
                return new ValueTask<WotResolvedNode?>(
                    string.Equals(expandedNodeId, LineNodeId, StringComparison.Ordinal)
                        ? new WotResolvedNode(LineNodeId, WotExpectedNodeClass.Any)
                        : null);
            }

            private const string PumpNamespace = "http://example.com/demo/pump";
            private const string LineNodeId = "nsu=" + PumpNamespace + ";s=Line01";
        }
    }
}
