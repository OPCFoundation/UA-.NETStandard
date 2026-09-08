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
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Combines readable document containment with bounded native ownership
    /// evidence, without treating an instance's type definition as ownership.
    /// </summary>
    internal sealed class WotProjectionDeclarationContext
    {
        public WotProjectionDeclarationContext(
            WotRegistrySnapshot snapshot,
            int maxJsonDepth,
            Func<WotResourceVersion, CancellationToken, ValueTask<ByteString>> readContent,
            int maxNodes = 1_000_000)
        {
            m_snapshot = snapshot;
            m_maxJsonDepth = maxJsonDepth;
            m_readContent = readContent;
            m_native = new WotNativeOwnershipIndex(maxNodes);
        }

        public void AddNodeSet(
            string resourceXid, UANodeSet nodeSet, ExpandedNodeId root, ByteString document)
        {
            using var json = JsonDocument.Parse(
                document.Memory, new JsonDocumentOptions { MaxDepth = m_maxJsonDepth });
            string? rootIdentifier = json.RootElement.TryGetProperty("uav:id", out JsonElement identity) &&
                identity.ValueKind == JsonValueKind.String ? identity.GetString() : null;
            m_roots[resourceXid] = m_native.Add(nodeSet, rootIdentifier, root);
            if (json.RootElement.TryGetProperty("uav:nodes", out _) ||
                json.RootElement.TryGetProperty("uav:nodeSet", out _))
            {
                m_nativeRoots.Add(resourceXid);
            }
            m_declarations.Clear();
        }

        public void AddAvailableNativePartitions(
            IReadOnlyDictionary<string, ByteString> contents,
            WotNodeSetConverterOptions options,
            CancellationToken cancellationToken)
        {
            long totalBytes = 0;
            foreach (WotResource resource in m_snapshot.AllResources())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!resource.Enabled || resource.DefaultVersion is not { } version ||
                    !contents.TryGetValue(version.DigestHex, out ByteString content))
                {
                    continue;
                }
                try
                {
                    using WotDocument document = WotDocument.Parse(content.Memory, options);
                    if (!document.TryGetNativeProjection(out _) && !document.TryGetEnvelope(out _))
                    {
                        continue;
                    }
                    totalBytes += content.Length;
                    if (totalBytes > options.MaxResolverTotalBytes)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadEncodingLimitsExceeded, "Native ownership inputs exceed the byte bound.");
                    }
                    WotConversionResult<UANodeSet> restored = WotNodeSetConverter.ToNodeSetResult(document, options);
                    if (restored.Value is not null && !restored.HasErrors)
                    {
                        AddNodeSet(resource.Xid, restored.Value,
                            WotNodeSetConverter.TrySelectProjectionRoot(restored.Value), content);
                    }
                }
                catch (Exception exception) when (exception is JsonException or FormatException)
                {
                    // Invalid documents supply no ownership proof. Their normal
                    // resource conversion reports the validation failure.
                }
            }
        }

        public void AddDependencyNodeSet(UANodeSet nodeSet)
        {
            m_native.Add(nodeSet, null, ExpandedNodeId.Null);
            m_declarations.Clear();
        }

        public async ValueTask<bool> IsDeclarationAsync(
            WotResource resource, CancellationToken cancellationToken)
        {
            var active = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<(WotResource Resource, bool Exit)>();
            pending.Push((resource, false));
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (WotResource current, bool exit) = pending.Pop();
                if (exit)
                {
                    m_declarations[current.Xid] = m_parents[current.Xid]
                        .Contains(parent => m_declarations[parent.Xid]);
                    active.Remove(current.Xid);
                    continue;
                }
                if (m_declarations.ContainsKey(current.Xid))
                {
                    continue;
                }
                if (!active.Add(current.Xid))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "The declaration containment graph contains a cycle.");
                }
                if ((current.Kind == WoTDocumentKindEnum.ThingModel && !m_nativeRoots.Contains(current.Xid)) ||
                    (m_roots.TryGetValue(current.Xid, out ExpandedNodeId root) && m_native.IsDeclaration(root)))
                {
                    m_declarations[current.Xid] = true;
                    active.Remove(current.Xid);
                    continue;
                }

                var parents = new Dictionary<string, WotResource>(StringComparer.Ordinal);
                if (current.DefaultVersion is { } version)
                {
                    ByteString content = await m_readContent(version, cancellationToken).ConfigureAwait(false);
                    foreach ((string href, string relation) in WotDependencyGraph.ExtractReferences(
                        content.Memory, m_maxJsonDepth))
                    {
                        if (WotDependencyGraph.IsContainmentRelation(relation) &&
                            WotDependencyGraph.Resolve(m_snapshot, href) is { } parent)
                        {
                            parents[parent.Xid] = parent;
                        }
                    }
                }
                m_parents[current.Xid] = parents.Values.ToArrayOf();
                pending.Push((current, true));
                foreach (WotResource parent in parents.Values)
                {
                    pending.Push((parent, false));
                }
            }
            return m_declarations[resource.Xid];
        }

        private readonly WotRegistrySnapshot m_snapshot;
        private readonly int m_maxJsonDepth;
        private readonly Func<WotResourceVersion, CancellationToken, ValueTask<ByteString>> m_readContent;
        private readonly Dictionary<string, bool> m_declarations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ArrayOf<WotResource>> m_parents = new(StringComparer.Ordinal);
        private readonly WotNativeOwnershipIndex m_native;
        private readonly Dictionary<string, ExpandedNodeId> m_roots = new(StringComparer.Ordinal);
        private readonly HashSet<string> m_nativeRoots = new(StringComparer.Ordinal);
    }
}
