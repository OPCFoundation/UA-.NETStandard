/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// Shared helpers for the AliasName conformance tests. Provides constants
    /// for the standard NodeIds defined by OPC UA Part 17 and reusable helper
    /// methods for browsing the Aliases hierarchy and invoking the FindAlias
    /// method on alias categories.
    /// </summary>
    internal static class AliasNameTestHelpers
    {
        public const uint AliasNameTypeId = 23455;
        public const uint AliasNameCategoryTypeId = 23456;
        public const uint AliasNameDataTypeId = 23468;
        public const uint AliasNameDataTypeBinaryEncodingId = 23499;
        public const uint AliasForReferenceTypeId = 23469;
        public const uint AliasesObjectId = 23470;

        public static readonly NodeId AliasNameTypeNodeId = new(AliasNameTypeId);
        public static readonly NodeId AliasNameCategoryTypeNodeId = new(AliasNameCategoryTypeId);
        public static readonly NodeId AliasNameDataTypeNodeId = new(AliasNameDataTypeId);
        public static readonly NodeId AliasForNodeId = new(AliasForReferenceTypeId);
        public static readonly NodeId AliasesNodeId = new(AliasesObjectId);

        /// <summary>
        /// Read a single attribute from a node.
        /// </summary>
        public static async Task<DataValue> ReadAttributeAsync(
            ISession session, NodeId nodeId, uint attributeId)
        {
            ReadResponse response = await session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = nodeId, AttributeId = attributeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        /// <summary>
        /// Forward-browse a node's hierarchical references and return the
        /// child references.
        /// </summary>
        public static Task<IList<ReferenceDescription>> BrowseChildrenAsync(
            ISession session, NodeId nodeId)
        {
            return BrowseChildrenAsync(
                session, nodeId, ReferenceTypeIds.HierarchicalReferences);
        }

        /// <summary>
        /// Forward-browse the given reference type from a node and return
        /// the child references.
        /// </summary>
        public static async Task<IList<ReferenceDescription>> BrowseChildrenAsync(
            ISession session, NodeId nodeId, NodeId referenceTypeId)
        {
            BrowseResponse response = await session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = referenceTypeId,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                $"Browse of {nodeId} failed: {response.Results[0].StatusCode}");

            return response.Results[0].References.ToArray();
        }

        /// <summary>
        /// Locates the AliasNameCategory child of <see cref="AliasesNodeId"/>
        /// with the given browse-name and the FindAlias method declared on it.
        /// Skips the calling test (Assert.Ignore) when the server does not
        /// expose the category or method.
        /// </summary>
        public static async Task<(NodeId CategoryId, NodeId FindAliasMethodId)>
            FindCategoryAsync(ISession session, string categoryBrowseName)
        {
            IList<ReferenceDescription> children =
                await BrowseChildrenAsync(session, AliasesNodeId).ConfigureAwait(false);

            // The standard NodeSet exposes empty placeholder TagVariables /
            // Topics objects in namespace 0 with no working FindAlias
            // implementation. Prefer a category whose NodeId is NOT in
            // namespace 0 (i.e. provided by the server's address space).
            NodeId categoryId = NodeId.Null;
            NodeId fallbackId = NodeId.Null;
            foreach (ReferenceDescription child in children)
            {
                if (child.BrowseName.Name != categoryBrowseName ||
                    child.NodeClass != NodeClass.Object)
                {
                    continue;
                }

                var resolved = ExpandedNodeId.ToNodeId(
                    child.NodeId, session.NamespaceUris);
                if (resolved.NamespaceIndex != 0)
                {
                    categoryId = resolved;
                    break;
                }
                if (fallbackId.IsNull)
                {
                    fallbackId = resolved;
                }
            }

            if (categoryId.IsNull)
            {
                categoryId = fallbackId;
            }

            if (categoryId.IsNull)
            {
                Assert.Ignore(
                    $"Server does not expose an AliasNameCategory '{categoryBrowseName}' under Aliases (i=23470).");
            }

            NodeId methodId = await FindMethodAsync(
                session, categoryId, "FindAlias").ConfigureAwait(false);

            if (methodId.IsNull)
            {
                Assert.Ignore(
                    $"Server does not expose a FindAlias method on category '{categoryBrowseName}'.");
            }

            return (categoryId, methodId);
        }

        /// <summary>
        /// Locates a method node with the given browse-name as a child of
        /// <paramref name="parent"/>. Returns <see cref="NodeId.Null"/>
        /// when no such method exists.
        /// </summary>
        public static async Task<NodeId> FindMethodAsync(
            ISession session, NodeId parent, string methodBrowseName)
        {
            IList<ReferenceDescription> children =
                await BrowseChildrenAsync(session, parent).ConfigureAwait(false);

            foreach (ReferenceDescription child in children)
            {
                if (child.NodeClass == NodeClass.Method &&
                    child.BrowseName.Name == methodBrowseName)
                {
                    return ExpandedNodeId.ToNodeId(
                        child.NodeId, session.NamespaceUris);
                }
            }
            return NodeId.Null;
        }

        /// <summary>
        /// Calls <c>FindAlias(pattern, referenceTypeFilter)</c> on the given
        /// category and returns the raw <see cref="CallMethodResult"/>.
        /// </summary>
        public static async Task<CallMethodResult> CallFindAliasAsync(
            ISession session,
            NodeId categoryId,
            NodeId findAliasMethodId,
            string pattern,
            NodeId referenceTypeFilter)
        {
            CallResponse response = await session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = categoryId,
                        MethodId = findAliasMethodId,
                        InputArguments = new Variant[]
                        {
                            new(pattern),
                            new(referenceTypeFilter)
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        /// <summary>
        /// Decodes the AliasNameDataType[] returned by FindAlias from the
        /// CallMethodResult's first output argument. The server encodes each
        /// record as { QualifiedName AliasName, NodeId[] ReferencedNodes }.
        /// </summary>
        public static IList<AliasRecord> DecodeAliasResults(
            ISession session, CallMethodResult result)
        {
            var records = new List<AliasRecord>();
            if (result == null || result.OutputArguments.Count == 0)
            {
                return records;
            }

            if (!result.OutputArguments[0].TryGetValue(out ArrayOf<ExtensionObject> aliasArray))
            {
                return records;
            }

            for (int i = 0; i < aliasArray.Count; i++)
            {
                ExtensionObject ext = aliasArray.Span[i];
                if (ext.IsNull)
                {
                    continue;
                }

                if (ext.TryGetValue(out AliasNameDataType typed,
                    session.MessageContext) &&
                    typed != null)
                {
                    NodeId[] refs;
                    if (typed.ReferencedNodes.Count > 0)
                    {
                        refs = new NodeId[typed.ReferencedNodes.Count];
                        for (int j = 0; j < typed.ReferencedNodes.Count; j++)
                        {
                            refs[j] = ExpandedNodeId.ToNodeId(
                                typed.ReferencedNodes[j], session.NamespaceUris);
                        }
                    }
                    else
                    {
                        refs = [];
                    }
                    records.Add(new AliasRecord(typed.AliasName, refs));
                    continue;
                }

                if (ext.TryGetAsBinary(out ByteString body) && !body.IsNull)
                {
                    using var decoder = new BinaryDecoder(
                        body.ToArray(), session.MessageContext);
                    QualifiedName aliasName = decoder.ReadQualifiedName("AliasName");
                    ArrayOf<NodeId> referenced =
                        decoder.ReadNodeIdArray("ReferencedNodes");
                    records.Add(new AliasRecord(
                        aliasName,
                        referenced.ToArray() ?? []));
                }
            }
            return records;
        }

        /// <summary>
        /// Plain record describing a single alias entry returned by FindAlias.
        /// </summary>
        internal sealed record AliasRecord(QualifiedName AliasName, NodeId[] ReferencedNodes);
    }
}
