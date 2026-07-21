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
        public List<HostOperation> Operations { get; } = new();

        public int AddCount { get; private set; }
        public int ShadowCount { get; private set; }
        public int RemoveCount { get; private set; }

        public ValueTask<WotProjectionHandle> AddAsync(
            WotProjectionDocument document, CancellationToken cancellationToken = default)
        {
            AddCount++;
            Operations.Add(new HostOperation("add", document));
            return new ValueTask<WotProjectionHandle>(MakeHandle(document));
        }

        public ValueTask<WotProjectionHandle> ShadowReloadAsync(
            WotProjectionHandle current, WotProjectionDocument document,
            CancellationToken cancellationToken = default)
        {
            ShadowCount++;
            Operations.Add(new HostOperation("shadow", document));
            long gen = (current?.Generation ?? 0) + 1;
            return new ValueTask<WotProjectionHandle>(MakeHandle(document, gen));
        }

        public ValueTask RemoveAsync(
            WotProjectionHandle handle, CancellationToken cancellationToken = default)
        {
            RemoveCount++;
            Operations.Add(new HostOperation("remove", null, handle?.ClosureKey ?? string.Empty));
            return default;
        }

        private static WotProjectionHandle MakeHandle(WotProjectionDocument document, long gen = 1)
        {
            return new WotProjectionHandle(
                document.ClosureKey, gen, new object(), ImmutableArray<NodeId>.Empty, 0);
        }
    }

    internal sealed class HostOperation
    {
        public HostOperation(string op, WotProjectionDocument? document, string closureKey = "")
        {
            Op = op;
            Document = document;
            ClosureKey = document?.ClosureKey ?? closureKey;
        }

        public string Op { get; }
        public WotProjectionDocument? Document { get; }
        public string ClosureKey { get; }

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
        private readonly HashSet<string> m_invalid = new(StringComparer.Ordinal);

        public void SetNodeCount(string resourceId, int nodeCount)
            => m_nodeCounts[resourceId] = nodeCount;

        public void MarkInvalid(string resourceId) => m_invalid.Add(resourceId);

        public void ClearInvalid(string resourceId) => m_invalid.Remove(resourceId);

        public WotConversionOutput Convert(
            WotResource resource, ReadOnlyMemory<byte> content, WotRegistrySnapshot snapshot)
        {
            if (m_invalid.Contains(resource.ResourceId))
            {
                return WotConversionOutput.Failure(
                    $"Injected conversion failure for '{resource.ResourceId}'.");
            }
            int nodeCount = m_nodeCounts.TryGetValue(resource.ResourceId, out int c) ? c : 2;
            UANodeSet nodeSet = TestNodeSets.Make(
                $"urn:wot:{resource.GroupId}/{resource.ResourceId}", nodeCount);
            return WotConversionOutput.Success(nodeSet);
        }
    }

    internal static class TestNodeSets
    {
        public static UANodeSet Make(string modelUri, int nodeCount)
        {
            var builder = new StringBuilder();
            builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            builder.Append("<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">");
            builder.Append("<NamespaceUris><Uri>").Append(modelUri).Append("</Uri></NamespaceUris>");
            builder.Append("<Models><Model ModelUri=\"").Append(modelUri)
                .Append("\" Version=\"1.0.0\" PublicationDate=\"2026-01-01T00:00:00Z\" /></Models>");
            for (int i = 0; i < nodeCount; i++)
            {
                int id = 5000 + i;
                builder.Append("<UAObject NodeId=\"ns=1;i=")
                    .Append(id.ToString(CultureInfo.InvariantCulture))
                    .Append("\" BrowseName=\"1:Node").Append(i).Append("\"><DisplayName>Node")
                    .Append(i).Append("</DisplayName></UAObject>");
            }
            builder.Append("</UANodeSet>");
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(builder.ToString()));
            return UANodeSet.Read(stream)!;
        }
    }
}
