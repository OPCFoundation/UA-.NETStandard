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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.AI.Client
{
    /// <summary>
    /// The browsing and calling this client is built on.
    /// </summary>
    /// <remarks>
    /// Deliberately generic. Everything here works against any Server, which is
    /// what keeps the scenario code above honest: it cannot accidentally depend on
    /// something only this sample's Server does.
    /// </remarks>
    public sealed partial class AiBrowseClient
    {
        /// <summary>
        /// Creates a client over a connected session.
        /// </summary>
        /// <param name="session">The connected session to browse and call through.</param>
        /// <param name="namespaceIndex">
        /// The index the Server assigned to the AI namespace.
        /// </param>
        public AiBrowseClient(ISession session, ushort namespaceIndex)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            m_ns = namespaceIndex;
        }

        /// <summary>
        /// The index the Server assigned to the AI namespace.
        /// </summary>
        public ushort NamespaceIndex => m_ns;

        /// <summary>
        /// Finds the AI namespace and prepares a client, or returns null when the
        /// Server does not implement the specification.
        /// </summary>
        /// <param name="session">The connected session.</param>
        public static AiBrowseClient? TryCreate(ISession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            int index = session.NamespaceUris.GetIndex(Namespaces.AI);
            return index < 0 ? null : new AiBrowseClient(session, (ushort)index);
        }

        private readonly ISession m_session;
        private readonly ushort m_ns;
        public async Task<List<ReferenceDescription>> BrowseAsync(
            NodeId node,
            CancellationToken ct)
        {
            var description = new BrowseDescription
            {
                NodeId = node,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };

            BrowseResponse response = await m_session.BrowseAsync(
                null,
                null,
                0,
                [description],
                ct).ConfigureAwait(false);

            BrowseResult result = response.Results[0];
            var references = new List<ReferenceDescription>(result.References.Span.ToArray());

            // Continuation points are not optional politeness: a Server is free to
            // return one for any browse, and ignoring it silently truncates the
            // address space this client claims to have walked.
            ByteString continuation = result.ContinuationPoint;

            while (!continuation.IsNull && continuation.Length > 0)
            {
                BrowseNextResponse next = await m_session.BrowseNextAsync(
                    null,
                    false,
                    [continuation],
                    ct).ConfigureAwait(false);

                references.AddRange(next.Results[0].References.Span.ToArray());
                continuation = next.Results[0].ContinuationPoint;
            }

            return references;
        }

        /// <summary>
        /// Browses a named folder under a node and returns what is in it.
        /// </summary>
        public async Task<IReadOnlyList<NodeId>> BrowseFolderAsync(
            NodeId parent,
            string browseName,
            CancellationToken ct)
        {
            NodeId folder = await FindChildAsync(parent, browseName, ct).ConfigureAwait(false);

            if (folder.IsNull)
            {
                return Array.Empty<NodeId>();
            }

            List<ReferenceDescription> children =
                await BrowseAsync(folder, ct).ConfigureAwait(false);

            return [.. children
                .Where(r => r.NodeClass == NodeClass.Object)
                .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, m_session.NamespaceUris))];
        }

        /// <summary>
        /// Finds a child by browse name in the AI namespace.
        /// </summary>
        public async Task<NodeId> FindChildAsync(
            NodeId parent,
            string browseName,
            CancellationToken ct)
        {
            if (parent.IsNull)
            {
                return NodeId.Null;
            }

            List<ReferenceDescription> children =
                await BrowseAsync(parent, ct).ConfigureAwait(false);

            foreach (ReferenceDescription child in children)
            {
                if (child.BrowseName.Name == browseName)
                {
                    return ExpandedNodeId.ToNodeId(child.NodeId, m_session.NamespaceUris);
                }
            }

            return NodeId.Null;
        }

        /// <summary>
        /// Follows one non-hierarchical reference by its browse name.
        /// </summary>
        /// <remarks>
        /// UsesModel, FallsBackTo and ImportedFrom are not hierarchical, so an
        /// ordinary child browse will not find them. That is the point of them: the
        /// model is not owned by the deployment, it is used by it, and the same
        /// artefact can be used by several.
        /// </remarks>
        public async Task<NodeId> FollowAsync(
            NodeId source,
            string referenceTypeName,
            CancellationToken ct)
        {
            var description = new BrowseDescription
            {
                NodeId = source,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.NonHierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };

            BrowseResponse response = await m_session.BrowseAsync(
                null,
                null,
                0,
                [description],
                ct).ConfigureAwait(false);

            // Materialised before the loop: a span enumerator cannot survive the
            // await inside it, and the read has to happen per reference because the
            // reference type is identified by its browse name rather than a NodeId
            // the client can predict.
            ReferenceDescription[] references = response.Results[0].References.Span.ToArray();

            foreach (ReferenceDescription reference in references)
            {
                NodeId typeId = ExpandedNodeId.ToNodeId(
                    reference.ReferenceTypeId, m_session.NamespaceUris);

                DataValue name = await ReadAsync(typeId, ct, Attributes.BrowseName)
                    .ConfigureAwait(false);

                if (name.WrappedValue.AsBoxedObject(Variant.BoxingBehavior.Legacy)
                        is QualifiedName qualified &&
                    qualified.Name == referenceTypeName)
                {
                    return ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris);
                }
            }

            return NodeId.Null;
        }

        public async Task<DataValue> ReadAsync(
            NodeId node,
            CancellationToken ct,
            uint attribute = Attributes.Value)
        {
            if (node.IsNull)
            {
                return DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown);
            }

            ReadResponse response = await m_session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = node, AttributeId = attribute }],
                ct).ConfigureAwait(false);

            return response.Results[0];
        }

        public async Task<IList<object>> CallAsync(
            NodeId objectId,
            NodeId methodId,
            Variant[] inputs,
            CancellationToken ct)
        {
            if (methodId.IsNull)
            {
                return Array.Empty<object>();
            }

            var request = new CallMethodRequest
            {
                ObjectId = objectId,
                MethodId = methodId,
                InputArguments = new ArrayOf<Variant>(inputs)
            };

            CallResponse response = await m_session.CallAsync(
                null, [request], ct).ConfigureAwait(false);

            CallMethodResult result = response.Results[0];

            if (StatusCode.IsBad(result.StatusCode))
            {
                Console.WriteLine("    call failed: {0}", result.StatusCode);
                return Array.Empty<object>();
            }

            var outputs = new List<object>(result.OutputArguments.Count);

            for (int index = 0; index < result.OutputArguments.Count; index++)
            {
                outputs.Add(result.OutputArguments[index].AsBoxedObject(Variant.BoxingBehavior.Legacy) ?? string.Empty);
            }

            return outputs;
        }

        public async Task WriteFileAsync(NodeId file, byte[] content, CancellationToken ct)
        {
            NodeId open = await FindChildAsync(file, "Open", ct).ConfigureAwait(false);
            NodeId write = await FindChildAsync(file, "Write", ct).ConfigureAwait(false);
            NodeId close = await FindChildAsync(file, "Close", ct).ConfigureAwait(false);

            const byte writeEraseExisting = 6;

            IList<object> opened = await CallAsync(file, open, [Variant.From(writeEraseExisting)], ct)
                .ConfigureAwait(false);

            if (opened.Count == 0)
            {
                return;
            }

            var handle = (uint)opened[0];

            // Chunked, because the whole reason a transfer exists is that the
            // payload did not fit in one message - writing it in one Write would
            // reintroduce the limit the transfer was opened to escape.
            const int chunk = 2048;

            for (int offset = 0; offset < content.Length; offset += chunk)
            {
                int length = Math.Min(chunk, content.Length - offset);
                byte[] slice = new byte[length];
                Array.Copy(content, offset, slice, 0, length);

                await CallAsync(file, write, [Variant.From(handle), Variant.From(ByteString.From(slice))], ct)
                    .ConfigureAwait(false);
            }

            await CallAsync(file, close, [Variant.From(handle)], ct).ConfigureAwait(false);
        }

        public async Task<byte[]> ReadFileAsync(NodeId file, CancellationToken ct)
        {
            NodeId open = await FindChildAsync(file, "Open", ct).ConfigureAwait(false);
            NodeId read = await FindChildAsync(file, "Read", ct).ConfigureAwait(false);
            NodeId close = await FindChildAsync(file, "Close", ct).ConfigureAwait(false);

            const byte readMode = 1;

            IList<object> opened = await CallAsync(file, open, [Variant.From(readMode)], ct)
                .ConfigureAwait(false);

            if (opened.Count == 0)
            {
                return [];
            }

            var handle = (uint)opened[0];
            var buffer = new List<byte>();

            while (true)
            {
                IList<object> chunk = await CallAsync(file, read, [Variant.From(handle), Variant.From(4096)], ct)
                    .ConfigureAwait(false);

                if (chunk.Count == 0 || chunk[0] is not ByteString data || data.Length == 0)
                {
                    break;
                }

                buffer.AddRange(data.Span.ToArray());
            }

            await CallAsync(file, close, [Variant.From(handle)], ct).ConfigureAwait(false);
            return [.. buffer];
        }

        /// <summary>
        /// Renders a structure without knowing its type.
        /// </summary>
        /// <remarks>
        /// The AI data types are decoded into concrete classes only when the client
        /// references the generated model. This client deliberately does not, so it
        /// prints whatever fields the value turns out to carry - which is also the
        /// honest thing for a generic client to do.
        /// </remarks>
        public static string Describe(object? value)
        {
            if (value is null)
            {
                return "(null)";
            }

            if (value is ExtensionObject extension)
            {
                // A generic client has no decoder for a domain structure it does not
                // reference, so it shows what the encoding will give it rather than
                // pretending to understand the type.
                return extension.TryGetAsJson(out string? json) && json is not null
                    ? json
                    : extension.ToString() ?? string.Empty;
            }

            if (value is string or ValueType)
            {
                return value.ToString() ?? string.Empty;
            }

            if (value is ByteString encoded)
            {
                // A structure this client has no decoder for, because it does not
                // reference the generated model. Saying so is more useful than
                // printing the bytes: the value is fine, the client simply chose
                // not to know the type.
                return FormattableString.Invariant(
                    $"(encoded structure, {encoded.Length} bytes)");
            }

            if (value is System.Collections.IEnumerable sequence and not string)
            {
                var items = new List<string>();

                foreach (object? item in sequence)
                {
                    items.Add(Describe(item));
                }

                return items.Count > 0 ? string.Join("; ", items) : "(empty)";
            }

            IEnumerable<PropertyInfo> properties = value
                .GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 &&
                            p.DeclaringType != typeof(object));

            var parts = new List<string>();

            foreach (PropertyInfo property in properties)
            {
                object? item = property.GetValue(value);

                if (item is not null && property.Name is not ("TypeId" or "BinaryEncodingId"
                    or "XmlEncodingId" or "JsonEncodingId"))
                {
                    parts.Add(property.Name + "=" + item);
                }
            }

            return parts.Count > 0 ? string.Join(", ", parts) : value.ToString() ?? string.Empty;
        }
    }
}
