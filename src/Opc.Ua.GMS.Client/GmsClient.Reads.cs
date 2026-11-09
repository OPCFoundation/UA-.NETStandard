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
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.GMS.Contracts;

namespace Opc.Ua.GMS.Client
{
    public sealed partial class GmsClient
    {
        /// <summary>
        /// Reads variable members which do not have generated scalar accessors, using their exact model browse paths.
        /// Generated proxies remain responsible for object navigation and all standard method calls.
        /// </summary>
        private async ValueTask<ArrayOf<GmsVariableReading>> ReadFieldsAsync(
            NodeId parent, ArrayOf<QualifiedName> names, ReadBudget budget, CancellationToken ct)
        {
            ArrayOf<NodeId> nodes = await ResolveFieldsAsync(parent, names, ct).ConfigureAwait(false);
            ArrayOf<DataValue> values = await ReadValuesAsync(nodes, budget, ct).ConfigureAwait(false);
            var result = new GmsVariableReading[nodes.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new GmsVariableReading(nodes[i], values[i]);
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Resolves exact namespace-qualified scalar members without confusing a failed service with an absent field.
        /// </summary>
        private async ValueTask<ArrayOf<NodeId>> ResolveFieldsAsync(
            NodeId parent, ArrayOf<QualifiedName> names, CancellationToken ct)
        {
            var paths = new BrowsePath[names.Count];
            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = new BrowsePath
                {
                    StartingNode = parent,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.Aggregates, IncludeSubtypes = true,
                                TargetName = names[i]
                            }
                        ]
                    }
                };
            }
            ArrayOf<BrowsePath> requests = paths.ToArrayOf();
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, requests, ct).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, requests);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
            var result = new NodeId[paths.Length];
            for (int i = 0; i < result.Length; i++)
            {
                BrowsePathResult entry = response.Results[i];
                if (entry.StatusCode == StatusCodes.BadNoMatch || entry.StatusCode == StatusCodes.BadNotFound)
                {
                    continue;
                }
                if (StatusCode.IsBad(entry.StatusCode))
                {
                    throw new ServiceResultException(entry.StatusCode);
                }
                if (entry.Targets.Count != 1 || entry.Targets[0].RemainingPathIndex != uint.MaxValue)
                {
                    throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                        "A native GMS field did not resolve to one complete local target.");
                }
                result[i] = ExpandedNodeId.ToNodeId(entry.Targets[0].TargetId, Session.NamespaceUris);
                if (result[i].IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported, "Remote GMS fields are not supported.");
                }
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Reads complete DataValues, not typed getters which would discard bad quality and optional presence.
        /// </summary>
        private async ValueTask<ArrayOf<DataValue>> ReadValuesAsync(
            ArrayOf<NodeId> nodes, ReadBudget budget, CancellationToken ct)
        {
            var requests = new List<ReadValueId>();
            var positions = new List<int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!nodes[i].IsNull)
                {
                    requests.Add(new ReadValueId { NodeId = nodes[i], AttributeId = Attributes.Value });
                    positions.Add(i);
                }
            }
            var result = new DataValue[nodes.Count];
            if (requests.Count != 0)
            {
                ArrayOf<ReadValueId> values = requests.ToArrayOf();
                ReadResponse response = await Session.ReadAsync(
                    null, 0, TimestampsToReturn.Both, values, ct).ConfigureAwait(false);
                ClientBase.ValidateResponse(response.Results, values);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, values);
                budget.Capture(response.Results);
                for (int i = 0; i < positions.Count; i++)
                {
                    result[positions[i]] = response.Results[i];
                }
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Delegates continuation-point ownership to the common paged Browser and bounds every examined reference.
        /// </summary>
        private async IAsyncEnumerable<ReferenceDescription> BrowseAsync(
            NodeId parent, NodeClass nodeClass, ReadBudget budget, [EnumeratorCancellation] CancellationToken ct)
        {
            var browser = new Browser(Session) { MaxReferencesReturned = 64 };
            await foreach (BrowseResult page in browser.BrowseStreamAsync(null, null,
                [new BrowseDescription
                {
                    NodeId = parent, BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HasComponent, IncludeSubtypes = true,
                    NodeClassMask = (uint)nodeClass, ResultMask = (uint)BrowseResultMask.All
                }], ct).ConfigureAwait(false))
            {
                if (StatusCode.IsBad(page.StatusCode))
                {
                    throw new ServiceResultException(page.StatusCode);
                }
                for (int index = 0; index < page.References.Count; index++)
                {
                    budget.Reference();
                    yield return page.References[index];
                }
            }
        }

        /// <summary>
        /// Qualifies a native GMS declaration against the connected server's actual namespace table.
        /// </summary>
        private QualifiedName Name(string name) =>
            new(name, (ushort)Session.NamespaceUris.GetIndex(GmsModel.NamespaceUri));

        /// <summary>
        /// Qualifies a DI declaration, independent of namespace order.
        /// </summary>
        private QualifiedName DiName(string name) =>
            new(name, (ushort)Session.NamespaceUris.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi));

        /// <summary>
        /// Qualifies an inherited MachineTool declaration.
        /// </summary>
        private QualifiedName MachineName(string name) =>
            new(name, (ushort)Session.NamespaceUris.GetIndex(MachineTool.Namespaces.MachineTool));

        /// <summary>
        /// Requires modeled presence without promoting bad or unavailable samples to good data.
        /// </summary>
        private static void Require(in GmsVariableReading reading, string name)
        {
            if (!reading.IsPresent)
            {
                throw Missing(name);
            }
        }

        /// <summary>
        /// Identifies a missing mandatory descendant without confusing it with unavailable data.
        /// </summary>
        private static ServiceResultException Missing(string name) =>
            new(StatusCodes.BadTypeDefinitionInvalid, $"The native GMS model is missing mandatory '{name}'.");

        /// <summary>
        /// Enforces finite aggregate read bounds without modifying the server or its native sample values.
        /// </summary>
        private sealed class ReadBudget(GmsClientOptions options, IServiceMessageContext context)
        {
            /// <summary>
            /// Counts a concrete native inventory element once.
            /// </summary>
            internal void Element(NodeId node)
            {
                if (node.IsNull || !m_nodes.Add(node) || m_nodes.Count > options.MaximumElements)
                {
                    throw Limit("The native inventory is repeated, cyclic or exceeds its element bound.");
                }
            }

            /// <summary>
            /// Counts all examined references, including unrecognized vendor members.
            /// </summary>
            internal void Reference()
            {
                if (++m_references > options.MaximumReferences)
                {
                    throw Limit("The native inventory exceeds its examined-reference bound.");
                }
            }

            /// <summary>
            /// Uses the stack's native namespace-aware encoder to account for returned samples.
            /// </summary>
            internal void Capture(ArrayOf<DataValue> values)
            {
                foreach (DataValue value in values)
                {
                    if (value.IsNull)
                    {
                        continue;
                    }
                    using var encoder = new JsonEncoder(context);
                    encoder.WriteDataValue("Value", value);
                    m_bytes = checked(m_bytes + Encoding.UTF8.GetByteCount(encoder.CloseAndReturnText()));
                    if (m_bytes > options.MaximumPayloadBytes)
                    {
                        throw Limit("The native GMS read exceeds its raw sample-payload bound.");
                    }
                }
            }

            /// <summary>
            /// Reports truncation as a failed bounded workflow, never as a complete inventory.
            /// </summary>
            private static ServiceResultException Limit(string message) =>
                new(StatusCodes.BadEncodingLimitsExceeded, message);

            /// <summary>The distinct concrete native inventory identifiers.</summary>
            private readonly HashSet<NodeId> m_nodes = [];

            /// <summary>The complete examined-reference count.</summary>
            private int m_references;

            /// <summary>The complete accounted raw sample-payload byte count.</summary>
            private long m_bytes;
        }
    }
}
