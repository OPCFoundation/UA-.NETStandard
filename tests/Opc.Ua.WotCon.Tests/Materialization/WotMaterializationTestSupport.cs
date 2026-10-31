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
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// A test <see cref="IWotProjectionHost"/> that records the add / shadow /
    /// remove operations without a running server.
    /// </summary>
    internal sealed class FakeWotProjectionHost : IWotProjectionHost
    {
        /// <summary>
        /// Gets the recorded projection host operations in execution order.
        /// </summary>
        public List<HostOperation> Operations { get; } = new();

        /// <summary>
        /// Gets the number of recorded projection additions.
        /// </summary>
        public int AddCount { get; private set; }

        /// <summary>
        /// Gets the number of recorded shadow reloads.
        /// </summary>
        public int ShadowCount { get; private set; }

        /// <summary>
        /// Gets the number of recorded immediate reloads.
        /// </summary>
        public int ImmediateCount { get; private set; }

        /// <summary>
        /// Gets the number of recorded projection removals.
        /// </summary>
        public int RemoveCount { get; private set; }

        /// <summary>
        /// Gets or sets the warning injected into the next replacement publication or reload.
        /// </summary>
        public string NextReloadWarning { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether the next reload attempt fails.
        /// </summary>
        public bool FailNextReload { get; set; }

        /// <summary>
        /// Records committed projection changes and updates the corresponding operation counters.
        /// </summary>
        /// <param name="changes">The committed projection changes to record.</param>
        public void RecordCommitted(ArrayOf<WotProjectionChange> changes)
        {
            bool replaced = false;
            foreach (WotProjectionChange change in changes)
            {
                if (change.Document is null)
                {
                    RemoveCount++;
                    Operations.Add(new HostOperation("remove", null, change.Current!.ClosureKey));
                }
                else if (change.Current is null)
                {
                    AddCount++;
                    Operations.Add(new HostOperation("add", change.Document));
                }
                else
                {
                    replaced = true;
                    if (change.RetirementPolicy == WotProjectionRetirementPolicy.Invalidate)
                    {
                        ImmediateCount++;
                        Operations.Add(new HostOperation("immediate", change.Document));
                    }
                    else
                    {
                        ShadowCount++;
                        Operations.Add(new HostOperation("shadow", change.Document));
                    }
                }
            }
            if (replaced && !string.IsNullOrEmpty(NextReloadWarning))
            {
                string warning = NextReloadWarning;
                NextReloadWarning = string.Empty;
                throw new InvalidOperationException(warning);
            }
        }

        /// <inheritdoc cref="IWotProjectionHost.AddAsync"/>
        public ValueTask<WotProjectionHandle> AddAsync(
            WotProjectionDocument document, CancellationToken cancellationToken = default)
        {
            AddCount++;
            Operations.Add(new HostOperation("add", document));
            return new ValueTask<WotProjectionHandle>(MakeHandle(document));
        }

        /// <inheritdoc cref="IWotProjectionHost.ShadowReloadAsync"/>
        public ValueTask<WotProjectionHandle> ShadowReloadAsync(
            WotProjectionHandle current, WotProjectionDocument document,
            CancellationToken cancellationToken = default)
        {
            ShadowCount++;
            Operations.Add(new HostOperation("shadow", document));
            if (FailNextReload)
            {
                FailNextReload = false;
                throw new InvalidOperationException("Injected projection reload failure.");
            }
            long gen = (current?.Generation ?? 0) + 1;
            string warning = NextReloadWarning;
            NextReloadWarning = string.Empty;
            return new ValueTask<WotProjectionHandle>(MakeHandle(document, gen, warning));
        }

        /// <inheritdoc cref="IWotProjectionHost.ImmediateReloadAsync"/>
        public ValueTask<WotProjectionHandle> ImmediateReloadAsync(
            WotProjectionHandle current, WotProjectionDocument document,
            CancellationToken cancellationToken = default)
        {
            ImmediateCount++;
            Operations.Add(new HostOperation("immediate", document));
            if (FailNextReload)
            {
                FailNextReload = false;
                throw new InvalidOperationException("Injected projection reload failure.");
            }
            long gen = (current?.Generation ?? 0) + 1;
            string warning = NextReloadWarning;
            NextReloadWarning = string.Empty;
            return new ValueTask<WotProjectionHandle>(MakeHandle(document, gen, warning));
        }

        /// <inheritdoc cref="IWotProjectionHost.RemoveAsync"/>
        public ValueTask RemoveAsync(
            WotProjectionHandle handle, CancellationToken cancellationToken = default)
        {
            RemoveCount++;
            Operations.Add(new HostOperation("remove", null, handle?.ClosureKey ?? string.Empty));
            return default;
        }

        private static WotProjectionHandle MakeHandle(
            WotProjectionDocument document,
            long gen = 1,
            string warning = "")
        {
            return new WotProjectionHandle(
                document.ClosureKey,
                gen,
                new FakeProjectionRegistration(),
                ImmutableArray<NodeId>.Empty,
                0,
                warning);
        }

        private sealed class FakeProjectionRegistration : IWotProjectionRegistration
        {
            public Guid Id { get; } = Guid.NewGuid();
        }
    }

    /// <summary>
    /// Records a projection operation, its document, and the affected closure identity.
    /// </summary>
    internal sealed class HostOperation
    {
        /// <summary>
        /// Captures a projection operation and derives its closure identity from the document when present.
        /// </summary>
        /// <param name="op">The projection operation name to record.</param>
        /// <param name="document">The WoT document associated with the operation.</param>
        /// <param name="closureKey">The closure identity used when no document is supplied.</param>
        public HostOperation(string op, WotProjectionDocument? document, string closureKey = "")
        {
            Op = op;
            Document = document;
            ClosureKey = document?.ClosureKey ?? closureKey;
        }

        /// <summary>
        /// Gets the recorded projection operation name.
        /// </summary>
        public string Op { get; }

        /// <summary>
        /// Gets the projection document supplied to the operation, if any.
        /// </summary>
        public WotProjectionDocument? Document { get; }

        /// <summary>
        /// Gets the dependency closure identity affected by the operation.
        /// </summary>
        public string ClosureKey { get; }

        /// <summary>
        /// Gets the names of source partitions in the recorded projection document.
        /// </summary>
        public IReadOnlyList<string> SourceNames
        {
            get
            {
                var names = new List<string>();
                if (Document is not null)
                {
                    foreach (WotProjectionSource source in Document.Sources)
                    {
                        names.Add(source.Name);
                    }
                }
                return names;
            }
        }
    }

    /// <summary>
    /// A deterministic <see cref="IWotDocumentConverter"/> that returns a canned
    /// NodeSet2 per resource id, or a failure for ids marked invalid.
    /// </summary>
    internal sealed class FakeWotDocumentConverter : IWotDocumentConverter
    {
        private readonly Dictionary<string, int> m_nodeCounts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ExpandedNodeId> m_rootNodeIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> m_invalid = new(StringComparer.Ordinal);
        private readonly HashSet<string> m_projectionInvalid = new(StringComparer.Ordinal);

        /// <summary>
        /// Sets the number of nodes returned when converting the specified resource.
        /// </summary>
        /// <param name="resourceId">The resource identifier selected by the operation.</param>
        /// <param name="nodeCount">The number of nodes to generate for the test resource.</param>
        public void SetNodeCount(string resourceId, int nodeCount)
            => m_nodeCounts[resourceId] = nodeCount;

        /// <summary>
        /// Sets the projected root node identifier returned for the specified resource.
        /// </summary>
        /// <param name="resourceId">The resource identifier selected by the operation.</param>
        /// <param name="rootNodeId">The projected root node identifier to return for the resource.</param>
        public void SetRootNodeId(string resourceId, ExpandedNodeId rootNodeId)
            => m_rootNodeIds[resourceId] = rootNodeId;

        /// <summary>
        /// Configures the specified resource to fail document conversion.
        /// </summary>
        /// <param name="resourceId">The resource identifier selected by the operation.</param>
        public void MarkInvalid(string resourceId) => m_invalid.Add(resourceId);

        /// <summary>
        /// Configures the specified resource to fail in the projection phase.
        /// </summary>
        /// <param name="resourceId">The resource identifier selected by the operation.</param>
        public void MarkProjectionInvalid(string resourceId) => m_projectionInvalid.Add(resourceId);

        /// <summary>
        /// Removes an injected document-conversion failure for the specified resource.
        /// </summary>
        /// <param name="resourceId">The resource identifier selected by the operation.</param>
        public void ClearInvalid(string resourceId) => m_invalid.Remove(resourceId);

        /// <summary>
        /// When set, every converted NodeSet declares a dependency on this namespace, so the
        /// coordinator has an unmet dependency to resolve.
        /// </summary>
        public string? RequiredNamespace { get; set; }

        /// <inheritdoc cref="IWotDocumentConverter.ConvertAsync"/>
        public ValueTask<WotConversionOutput> ConvertAsync(
            WotResource resource,
            ByteString content,
            WotRegistrySnapshot snapshot,
            IReadOnlyDictionary<string, ByteString> contents,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (m_invalid.Contains(resource.ResourceId))
            {
                return new ValueTask<WotConversionOutput>(
                    WotConversionOutput.Failure($"Injected conversion failure for '{resource.ResourceId}'."));
            }
            if (m_projectionInvalid.Contains(resource.ResourceId))
            {
                return new ValueTask<WotConversionOutput>(
                    WotConversionOutput.Failure(
                        WoTPhaseEnum.Projection,
                        $"Injected projection failure for '{resource.ResourceId}'."));
            }
            int nodeCount = m_nodeCounts.TryGetValue(resource.ResourceId, out int c) ? c : 2;
            UANodeSet nodeSet = TestNodeSets.Make(
                $"urn:wot:{resource.GroupId}/{resource.ResourceId}", nodeCount, RequiredNamespace);
            if (m_rootNodeIds.TryGetValue(resource.ResourceId, out ExpandedNodeId rootNodeId))
            {
                return new ValueTask<WotConversionOutput>(
                    new WotConversionOutput(nodeSet, [], rootNodeId));
            }
            return new ValueTask<WotConversionOutput>(WotConversionOutput.Success(nodeSet));
        }
    }

    /// <summary>
    /// Creates small deterministic NodeSets for materialization tests.
    /// </summary>
    internal static class TestNodeSets
    {
        /// <summary>
        /// Encodes a test NodeSet as UTF-8 XML with an optional required model namespace.
        /// </summary>
        /// <param name="modelUri">The model URI assigned to the generated test NodeSet.</param>
        /// <param name="requiredNamespace">The optional required model namespace in the test NodeSet.</param>
        public static byte[] XmlBytes(string modelUri, string? requiredNamespace = null)
        {
            return Encoding.UTF8.GetBytes(Xml(modelUri, requiredNamespace));
        }

        /// <summary>
        /// Creates a test NodeSet with the requested model URI, node count, and optional required namespace.
        /// </summary>
        /// <param name="modelUri">The model URI assigned to the generated test NodeSet.</param>
        /// <param name="nodeCount">The number of nodes to generate for the test resource.</param>
        /// <param name="requiredNamespace">The optional required model namespace in the test NodeSet.</param>
        public static UANodeSet Make(
            string modelUri, int nodeCount, string? requiredNamespace = null)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Xml(modelUri, requiredNamespace, nodeCount)));
            return UANodeSet.Read(stream)!;
        }

        private static string Xml(
            string modelUri,
            string? requiredNamespace = null,
            int nodeCount = 1)
        {
            var builder = new StringBuilder();
            builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            builder.Append("<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">");
            builder.Append("<NamespaceUris><Uri>").Append(modelUri).Append("</Uri></NamespaceUris>");
            builder.Append("<Models><Model ModelUri=\"").Append(modelUri)
                .Append("\" Version=\"1.0.0\" PublicationDate=\"2026-01-01T00:00:00Z\"");
            if (string.IsNullOrEmpty(requiredNamespace))
            {
                builder.Append(" /></Models>");
            }
            else
            {
                builder.Append("><RequiredModel ModelUri=\"").Append(requiredNamespace)
                    .Append("\" Version=\"1.0.0\" PublicationDate=\"2026-01-01T00:00:00Z\" />")
                    .Append("</Model></Models>");
            }
            for (int i = 0; i < nodeCount; i++)
            {
                int id = 5000 + i;
                builder.Append("<UAObject NodeId=\"ns=1;i=")
                    .Append(id.ToString(CultureInfo.InvariantCulture))
                    .Append("\" BrowseName=\"1:Node").Append(i).Append("\"><DisplayName>Node")
                    .Append(i).Append("</DisplayName></UAObject>");
            }
            builder.Append("</UANodeSet>");
            return builder.ToString();
        }
    }
}
