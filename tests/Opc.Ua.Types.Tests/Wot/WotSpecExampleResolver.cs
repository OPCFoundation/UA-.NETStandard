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
 *
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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;
using Opc.Ua.Wot;

#nullable enable

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Serves the vendored specification examples by their relative reference.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The published examples reference one another by relative path - example
    /// 02 links its event affordances to the definitions example 27 declares -
    /// so converting one of them is converting a small document set. This
    /// resolver is that set: it answers a reference with the embedded bytes of
    /// the example it names, and answers nothing else.
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
            int slash = name.LastIndexOf('/');
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
        /// Reads one vendored example, or reports that the set does not hold
        /// it.
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
        /// Converts one vendored example with the rest of the set available to
        /// it, which is what a document that links a sibling needs.
        /// </summary>
        internal static async Task<WotConversionResult<UANodeSet>> ConvertAsync(
            WotDocument document)
        {
            return await WotNodeSetConverter
                .ToNodeSetResultAsync(document, null, Instance, null, NodeResolver)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// The address space the published examples assume a Server already
        /// holds.
        /// </summary>
        /// <remarks>
        /// Example 01 places its Pump under a production line the Server owns
        /// (<c>uav:componentOf</c> naming <c>...;s=Line01</c>). That Node is
        /// not in any document of the set, and it is not supposed to be: the
        /// point of the term is to attach a converted document to something
        /// that already exists. Converting the example without a Server to
        /// attach to reports the parent as unresolved, correctly, so a test
        /// that wants the conversion has to model the Server - which is all
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
                        ? new WotResolvedNode("ns=1;s=Line01", WotExpectedNodeClass.Any)
                        : null);
            }

            private const string PumpNamespace = "http://example.com/demo/pump";
            private const string LineNodeId = "nsu=" + PumpNamespace + ";s=Line01";
        }
    }
}
