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

using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Exercises <see cref="WotTargetVariableResolver"/> against a lightweight
    /// <see cref="NodeManagerBuilder"/> graph (no running server): exact NodeId
    /// resolution, type-only unique/ambiguous resolution, exact+type validation,
    /// and the deterministic failure statuses for missing, malformed, ambiguous,
    /// wrong-node-class and type-mismatch mappings.
    /// </summary>
    [TestFixture]
    public sealed class WotTargetVariableResolverTests
    {
        private const string NsUri = "http://test.org/UA/Wot/";

        [Test]
        public void MapToNodeIdResolvesExactVariable()
        {
            (NodeManagerBuilder builder, ushort ns, BaseDataVariableState var1, _, _) = CreateGraph();
            var resolver = new WotTargetVariableResolver();

            BaseVariableState resolved = resolver.Resolve(
                builder, new WotTargetMappingDescriptor(targetNodeId: $"ns={ns};s=Var1"));

            Assert.That(resolved, Is.SameAs(var1));
        }

        [Test]
        public void MapToNodeIdPortableNsuFormResolvesAgainstNamespaceUris()
        {
            (NodeManagerBuilder builder, _, BaseDataVariableState var1, _, _) = CreateGraph();
            var resolver = new WotTargetVariableResolver();

            BaseVariableState resolved = resolver.Resolve(
                builder, new WotTargetMappingDescriptor(targetNodeId: $"nsu={NsUri};s=Var1"));

            Assert.That(resolved, Is.SameAs(var1));
        }

        [Test]
        public void MapToNodeIdMissingNodeThrowsBadNodeIdUnknown()
        {
            (NodeManagerBuilder builder, ushort ns, _, _, _) = CreateGraph();
            var resolver = new WotTargetVariableResolver();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                resolver.Resolve(builder, new WotTargetMappingDescriptor(targetNodeId: $"ns={ns};s=NoSuchVar")));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void MapToNodeIdMalformedTextThrowsBadNodeIdInvalid()
        {
            (NodeManagerBuilder builder, _, _, _, _) = CreateGraph();
            var resolver = new WotTargetVariableResolver();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                resolver.Resolve(builder, new WotTargetMappingDescriptor(targetNodeId: "not a node id")));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void MapToNodeIdMalformedTextMessageNamesTheOffendingTerm()
        {
            // The parser's own ServiceResultException (BadNodeIdInvalid) must
            // be wrapped, not rethrown verbatim, so the message names which
            // target-mapping term ('uav:mapToNodeId') carried the bad text.
            (NodeManagerBuilder builder, _, _, _, _) = CreateGraph();
            var resolver = new WotTargetVariableResolver();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                resolver.Resolve(builder, new WotTargetMappingDescriptor(targetNodeId: "not a node id")));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(ex.Message, Does.Contain("uav:mapToNodeId"));
        }

        [Test]
        public void MapToTypeUnresolvableNamespaceUriWrapsServiceResultExceptionAsBadNodeIdInvalid()
        {
            // 'nsu=' referencing a namespace absent from the builder's table
            // makes the parser itself throw a ServiceResultException; that
            // must still be wrapped as BadNodeIdInvalid naming 'uav:mapToType',
            // not rethrown with the parser's own (unrelated) message.
            (NodeManagerBuilder builder, _, _, _, _) = CreateGraph();
            var resolver = new WotTargetVariableResolver();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                resolver.Resolve(
                    builder,
                    new WotTargetMappingDescriptor(targetTypeNodeId: "nsu=http://no.such.namespace/;i=1")));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(ex.Message, Does.Contain("uav:mapToType"));
        }

        [Test]
        public void MapToNodeIdWrongNodeClassThrowsBadTypeMismatch()
        {
            (NodeManagerBuilder builder, ushort ns, _, _, BaseObjectState obj) = CreateGraph();
            var resolver = new WotTargetVariableResolver();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                resolver.Resolve(builder, new WotTargetMappingDescriptor(
                    targetNodeId: $"ns={ns};s={obj.NodeId.IdentifierAsString}")));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void MapToTypeUniqueResolvesVariable()
        {
            (NodeManagerBuilder builder, ushort ns, BaseDataVariableState var1, _, _) = CreateGraph();
            var resolver = new WotTargetVariableResolver();

            BaseVariableState resolved = resolver.Resolve(
                builder, new WotTargetMappingDescriptor(targetTypeNodeId: $"ns={ns};i=1"));

            Assert.That(resolved, Is.SameAs(var1));
        }

        [Test]
        public void MapToTypeAmbiguousThrowsBadBrowseNameDuplicated()
        {
            (NodeManagerBuilder builder, ushort ns, _, _, _) = CreateGraph(secondVariableSameType: true);
            var resolver = new WotTargetVariableResolver();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                resolver.Resolve(builder, new WotTargetMappingDescriptor(targetTypeNodeId: $"ns={ns};i=1")));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public void MapToTypeNoMatchThrowsBadNodeIdUnknown()
        {
            (NodeManagerBuilder builder, ushort ns, _, _, _) = CreateGraph();
            var resolver = new WotTargetVariableResolver();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                resolver.Resolve(builder, new WotTargetMappingDescriptor(targetTypeNodeId: $"ns={ns};i=999")));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void BothMatchingDataTypeResolvesAndValidates()
        {
            (NodeManagerBuilder builder, ushort ns, BaseDataVariableState var1, _, _) = CreateGraph();
            var resolver = new WotTargetVariableResolver();

            BaseVariableState resolved = resolver.Resolve(
                builder,
                new WotTargetMappingDescriptor(targetNodeId: $"ns={ns};s=Var1", targetTypeNodeId: $"ns={ns};i=1"));

            Assert.That(resolved, Is.SameAs(var1));
        }

        [Test]
        public void BothMismatchingDataTypeThrowsBadTypeMismatch()
        {
            (NodeManagerBuilder builder, ushort ns, _, _, _) = CreateGraph();
            var resolver = new WotTargetVariableResolver();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                resolver.Resolve(
                    builder,
                    new WotTargetMappingDescriptor(targetNodeId: $"ns={ns};s=Var1", targetTypeNodeId: $"ns={ns};i=2")));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void NeitherTermPresentThrowsBadNodeIdInvalid()
        {
            (NodeManagerBuilder builder, _, _, _, _) = CreateGraph();
            var resolver = new WotTargetVariableResolver();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                resolver.Resolve(builder, WotTargetMappingDescriptor.Empty));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        private static (NodeManagerBuilder Builder, ushort Ns, BaseDataVariableState Var1,
            BaseDataVariableState? Var2, BaseObjectState Obj) CreateGraph(bool secondVariableSameType = false)
        {
            var namespaceUris = new NamespaceTable();
            ushort ns = (ushort)namespaceUris.Append(NsUri);

            var ctx = new SystemContext(telemetry: null!)
            {
                NamespaceUris = namespaceUris
            };

            var dataType1 = new NodeId(1, ns);
            var dataType2 = new NodeId(2, ns);

            var root = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Root", ns),
                BrowseName = new QualifiedName("Root", ns),
                DisplayName = new LocalizedText("Root")
            };

            var var1 = new BaseDataVariableState(root)
            {
                NodeId = new NodeId("Var1", ns),
                BrowseName = new QualifiedName("Var1", ns),
                DisplayName = new LocalizedText("Var1"),
                DataType = dataType1,
                ValueRank = ValueRanks.Scalar
            };
            root.AddChild(var1);

            BaseDataVariableState? var2 = null;
            if (secondVariableSameType)
            {
                var2 = new BaseDataVariableState(root)
                {
                    NodeId = new NodeId("Var2", ns),
                    BrowseName = new QualifiedName("Var2", ns),
                    DisplayName = new LocalizedText("Var2"),
                    DataType = dataType1,
                    ValueRank = ValueRanks.Scalar
                };
                root.AddChild(var2);
            }

            var byId = new Dictionary<NodeId, NodeState>
            {
                [root.NodeId] = root,
                [var1.NodeId] = var1
            };
            if (var2 is not null)
            {
                byId[var2.NodeId] = var2;
            }

            var builder = new NodeManagerBuilder(
                ctx,
                nodeManager: Mock.Of<IAsyncNodeManager>(),
                defaultNamespaceIndex: ns,
                rootResolver: q => q == root.BrowseName ? root : null!,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState? n) ? n : null!,
                typeIdResolver: _ => [],
                dataTypeIdResolver: dataTypeId =>
                {
                    var matches = new List<NodeState>();
                    foreach (NodeState node in byId.Values)
                    {
                        if (node is BaseVariableState v && v.DataType == dataTypeId)
                        {
                            matches.Add(node);
                        }
                    }
                    return matches.ToArrayOf();
                });

            return (builder, ns, var1, var2, root);
        }
    }
}
