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
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Encoders.Tests
{
    /// <summary>
    /// Verifies the actual source-generated RSE model and its ordinary Call representation.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class RseModelContractTests
    {
        /// <summary>
        /// Resolves model identities through the namespace URI instead of the source file's index.
        /// </summary>
        [TestCase(0)]
        [TestCase(3)]
        public void GeneratedModelContainsExactlyTheDeclaredNamespaceLocalNodes(int padding)
        {
            ServiceMessageContext messageContext = CreateContext(padding, out ushort namespaceIndex);
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = messageContext.NamespaceUris
            };
            Dictionary<NodeId, NodeState> nodes = CreateNodes(context);
            var identifiers = new HashSet<uint>();
            foreach (NodeState node in nodes.Values)
            {
                Assert.That(node.NodeId.NamespaceIndex, Is.EqualTo(namespaceIndex));
                Assert.That(node.NodeId.TryGetValue(out uint identifier), Is.True);
                Assert.That(identifiers.Add(identifier), Is.True);
            }
            Assert.That(identifiers, Is.EquivalentTo(Enumerable.Range(1, 96).Select(value => (uint)value)));
            Assert.That(nodes.Values.Count(node => node.NodeClass == NodeClass.DataType), Is.EqualTo(12));
            Assert.That(nodes.Values.Count(node => node.NodeClass == NodeClass.ObjectType), Is.EqualTo(4));
            Assert.That(nodes.Values.Count(node => node.NodeClass == NodeClass.Method), Is.EqualTo(5));
        }

        /// <summary>
        /// Preserves the source metadata rather than advertising generated or dynamic identifiers as static.
        /// </summary>
        [Test]
        public void NamespaceMetadataPreservesTheDeclaredStaticRange()
        {
            ServiceMessageContext messageContext = CreateContext(2, out ushort namespaceIndex);
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = messageContext.NamespaceUris
            };
            Dictionary<NodeId, NodeState> nodes = CreateNodes(context);
            var range = nodes[new NodeId(95u, namespaceIndex)] as BaseVariableState;
            Assert.That(range, Is.Not.Null);
            Assert.That(range!.WrappedValue.TryGetValue(out ArrayOf<string> values), Is.True);
            Assert.That(values, Is.EqualTo(new ArrayOf<string>(["1:96"])));
            var uri = nodes[new NodeId(90u, namespaceIndex)] as BaseVariableState;
            Assert.That(uri, Is.Not.Null);
            Assert.That(uri!.WrappedValue.TryGetValue(out string? value), Is.True);
            Assert.That(value, Is.EqualTo(RseNamespaces.Rse));
        }

        /// <summary>
        /// Keeps Capabilities beneath the well-known manager and its type declaration Mandatory.
        /// </summary>
        [Test]
        public void CapabilitiesAreQualifiedAndNestedUnderTheManager()
        {
            ServiceMessageContext messageContext = CreateContext(2, out ushort namespaceIndex);
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = messageContext.NamespaceUris
            };
            Dictionary<NodeId, NodeState> nodes = CreateNodes(context);
            var manager = nodes[new NodeId(73u, namespaceIndex)] as BaseObjectState;
            var capabilities = nodes[new NodeId(71u, namespaceIndex)] as BaseObjectState;
            var declaration = nodes[new NodeId(70u, namespaceIndex)] as BaseObjectState;
            Assert.That(manager, Is.Not.Null);
            Assert.That(capabilities, Is.Not.Null);
            Assert.That(declaration, Is.Not.Null);
            Assert.That(manager!.TypeDefinitionId, Is.EqualTo(new NodeId(69u, namespaceIndex)));
            Assert.That(capabilities!.Parent, Is.SameAs(manager));
            Assert.That(capabilities.BrowseName, Is.EqualTo(new QualifiedName("Capabilities", namespaceIndex)));
            Assert.That(capabilities.TypeDefinitionId, Is.EqualTo(new NodeId(39u, namespaceIndex)));
            Assert.That(declaration!.ModellingRuleId, Is.EqualTo(ObjectIds.ModellingRule_Mandatory));
        }

        /// <summary>
        /// Each manager Method has one scalar typed argument in each direction.
        /// </summary>
        [TestCase("Submit", 74u, 15u, 19u)]
        [TestCase("GetResult", 77u, 23u, 27u)]
        [TestCase("Acknowledge", 80u, 31u, 35u)]
        [TestCase("Cancel", 83u, 31u, 35u)]
        [TestCase("Abandon", 86u, 31u, 35u)]
        public void ManagerMethodsHaveCanonicalScalarArguments(
            string name, uint methodId, uint requestId, uint responseId)
        {
            ServiceMessageContext messageContext = CreateContext(2, out ushort namespaceIndex);
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = messageContext.NamespaceUris
            };
            Dictionary<NodeId, NodeState> nodes = CreateNodes(context);
            var method = nodes[new NodeId(methodId, namespaceIndex)] as MethodState;
            Assert.That(method, Is.Not.Null);
            Assert.That(method!.BrowseName, Is.EqualTo(new QualifiedName(name, namespaceIndex)));
            Assert.That(method.ModellingRuleId, Is.EqualTo(ObjectIds.ModellingRule_Mandatory));
            Assert.That(method.InputArguments, Is.Not.Null);
            Assert.That(method.OutputArguments, Is.Not.Null);
            Assert.That(method.InputArguments!.Value.Count, Is.EqualTo(1));
            Assert.That(method.OutputArguments!.Value.Count, Is.EqualTo(1));
            Argument request = method.InputArguments.Value[0];
            Argument response = method.OutputArguments.Value[0];
            Assert.That(request.Name, Is.EqualTo("Request"));
            Assert.That(request.DataType, Is.EqualTo(new NodeId(requestId, namespaceIndex)));
            Assert.That(request.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(response.Name, Is.EqualTo("Response"));
            Assert.That(response.DataType, Is.EqualTo(new NodeId(responseId, namespaceIndex)));
            Assert.That(response.ValueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        /// <summary>
        /// The submission body has no wrapper header and retains its declared binary field order.
        /// </summary>
        [Test]
        public void SubmissionUsesHeaderlessCanonicalBinaryFieldOrder()
        {
            ServiceMessageContext context = CreateContext(3, out ushort namespaceIndex);
            Uuid id = new Guid("95c03745-16c4-4f97-9bf5-0948d733a5bd");
            var request = new SubmitRequestDataType
            {
                ClientOperationId = id,
                RecoveryMode = ServiceOperationRecoveryMode.Owner,
                Request = new ExtensionObject(new ReadRequest
                {
                    RequestHeader = new RequestHeader { RequestHandle = 31 }
                }),
                ExecutionAllowance = 101.5,
                ResultRetentionTime = 202.75,
                WaitTime = 3.25
            };
            byte[] bytes = BinaryEncoder.EncodeMessage(request, context);
            using var decoder = new BinaryDecoder(bytes, context);
            Assert.That(decoder.ReadNodeId(null), Is.EqualTo(new NodeId(16u, namespaceIndex)));
            Assert.That(decoder.ReadGuid(null), Is.EqualTo(id));
            Assert.That(decoder.ReadInt32(null), Is.EqualTo(2));
            Assert.That(decoder.ReadExtensionObject(null).TryGetValue(out ReadRequest? original), Is.True);
            Assert.That(original!.RequestHeader.RequestHandle, Is.EqualTo(31u));
            Assert.That(decoder.ReadDouble(null), Is.EqualTo(101.5));
            Assert.That(decoder.ReadDouble(null), Is.EqualTo(202.75));
            Assert.That(decoder.ReadDouble(null), Is.EqualTo(3.25));
            Assert.That(decoder.Position, Is.EqualTo(bytes.Length));
        }

        /// <summary>
        /// Submit is a typed scalar argument of ordinary Call on both codecs.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void OrdinaryCallCarriesTypedSubmissionAndOriginalHeader(bool json)
        {
            ServiceMessageContext context = CreateContext(3, out ushort namespaceIndex);
            var submission = new SubmitRequestDataType
            {
                ClientOperationId = new Guid("95c03745-16c4-4f97-9bf5-0948d733a5bd"),
                RecoveryMode = ServiceOperationRecoveryMode.Session,
                Request = new ExtensionObject(new ReadRequest
                {
                    RequestHeader = new RequestHeader { RequestHandle = 13 },
                    NodesToRead = [new ReadValueId { NodeId = new NodeId(2258u), AttributeId = Attributes.Value }]
                }),
                ExecutionAllowance = 2500.5,
                ResultRetentionTime = 5000.25,
                WaitTime = 0
            };
            var request = new CallRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 29 },
                MethodsToCall =
                [
                    new CallMethodRequest
                    {
                        ObjectId = new NodeId(73u, namespaceIndex),
                        MethodId = new NodeId("server-assigned-submit", 1),
                        InputArguments = [Variant.FromStructure(submission)]
                    }
                ]
            };
            byte[] encoded = json
                ? [.. JsonEncoder.EncodeMessage(request, new byte[8192], context)]
                : BinaryEncoder.EncodeMessage(request, context);
            CallRequest decoded = json
                ? JsonDecoder.DecodeMessage<CallRequest>(encoded, context)
                : BinaryDecoder.DecodeMessage<CallRequest>(encoded, context);

            Assert.That(decoded.RequestHeader.RequestHandle, Is.EqualTo(29u));
            Assert.That(decoded.MethodsToCall.Count, Is.EqualTo(1));
            CallMethodRequest method = decoded.MethodsToCall[0];
            Assert.That(method.ObjectId, Is.EqualTo(new NodeId(73u, namespaceIndex)));
            Assert.That(method.MethodId, Is.EqualTo(new NodeId("server-assigned-submit", 1)));
            Assert.That(method.InputArguments.Count, Is.EqualTo(1));
            Assert.That(method.InputArguments[0].TryGetValue<SubmitRequestDataType>(
                out SubmitRequestDataType? recovered, context), Is.True);
            Assert.That(recovered!.TypeId, Is.EqualTo(new ExpandedNodeId(15u, RseNamespaces.Rse)));
            Assert.That(recovered.RecoveryMode, Is.EqualTo(ServiceOperationRecoveryMode.Session));
            Assert.That(recovered.ExecutionAllowance, Is.EqualTo(2500.5));
            Assert.That(recovered.ResultRetentionTime, Is.EqualTo(5000.25));
            Assert.That(recovered.WaitTime, Is.Zero);
            Assert.That(recovered.Request.TryGetValue(out ReadRequest? original), Is.True);
            Assert.That(original!.RequestHeader.RequestHandle, Is.EqualTo(13u));
            Assert.That(original.NodesToRead[0].NodeId, Is.EqualTo(new NodeId(2258u)));
        }

        /// <summary>
        /// Control arguments contain only the token, without an action, recovery mode or header.
        /// </summary>
        [Test]
        public void ControlUsesATokenOnlyBinaryBody()
        {
            ServiceMessageContext context = CreateContext(2, out ushort namespaceIndex);
            var token = ByteString.From(1, 2, 3, 4);
            var request = new OperationControlRequestDataType { ServerToken = token };
            byte[] encoded = BinaryEncoder.EncodeMessage(request, context);
            using var decoder = new BinaryDecoder(encoded, context);

            Assert.That(decoder.ReadNodeId(null), Is.EqualTo(new NodeId(32u, namespaceIndex)));
            Assert.That(decoder.ReadByteString(null), Is.EqualTo(token));
            Assert.That(decoder.Position, Is.EqualTo(encoded.Length));
            Assert.That((int)ServiceOperationRecoveryMode.Session, Is.EqualTo(1));
            Assert.That((int)ServiceOperationRecoveryMode.Owner, Is.EqualTo(2));
            Assert.That((int)ServiceOperationRecoveryMode.Durable, Is.EqualTo(3));
            Assert.That((int)ServiceOperationOutcome.ExecutionIndeterminate, Is.EqualTo(4));
        }

        private static ServiceMessageContext CreateContext(int padding, out ushort namespaceIndex)
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            for (int index = 0; index < padding; index++)
            {
                context.NamespaceUris.GetIndexOrAppend($"urn:rse-model-test:other:{index}");
            }
            namespaceIndex = context.NamespaceUris.GetIndexOrAppend(RseNamespaces.Rse);
            context.Factory.Builder.AddOpcUaRse().Commit();
            return context;
        }

        private static Dictionary<NodeId, NodeState> CreateNodes(ISystemContext context)
        {
            NodeStateCollection roots = OpcUaRseExtensions.AddOpcUaRse([], context);
            var pending = new Stack<NodeState>(roots);
            var nodes = new Dictionary<NodeId, NodeState>();
            while (pending.Count != 0)
            {
                NodeState node = pending.Pop();
                if (!nodes.TryAdd(node.NodeId, node))
                {
                    continue;
                }
                var children = new List<BaseInstanceState>();
                node.GetChildren(context, children);
                foreach (BaseInstanceState child in children)
                {
                    pending.Push(child);
                }
            }
            return nodes;
        }
    }
}
