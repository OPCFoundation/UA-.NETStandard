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

using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises the replica factory adapter and identity-preserving topology codec in the native test host.
    /// </summary>
    public sealed class ReplicaNodeIdentityAotTests
    {
        /// <summary>
        /// Keeps independent factory results and hydrated child identifiers equal without reflection-based discovery.
        /// </summary>
        [Test]
        public async Task ReplicaFactoryAndHydrationPreserveWireNodeIdsAsync()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            using var disposableTelemetry = telemetry as IDisposable;
            var first = new ReplicaNodeIdFactory("aot-replicas", ["urn:aot:shared"]);
            var second = new ReplicaNodeIdFactory("aot-replicas", ["urn:aot:shared"]);
            SystemContext left = CreateContext(first, telemetry, "urn:aot:left");
            SystemContext right = CreateContext(second, telemetry, "urn:aot:right");
            IRebasableNodeIdFactory leftFactory = first.WithDefaultNamespaceIndex(2).WithCollisionDetection(false);
            IRebasableNodeIdFactory rightFactory = second.Apply(new DefaultNodeIdFactory(defaultNamespaceIndex: 2));
            var original = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("Machine", 2),
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };
            var independent = new BaseObjectState(null)
            {
                BrowseName = original.BrowseName,
                TypeDefinitionId = original.TypeDefinitionId
            };
            original.NodeId = leftFactory.New(left, original);
            independent.NodeId = rightFactory.New(right, independent);
            var child = new BaseDataVariableState(original)
            {
                BrowseName = new QualifiedName("Target", 2),
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = DataTypeIds.NodeId,
                ValueRank = ValueRanks.Scalar,
                Value = Variant.From(original.NodeId)
            };
            child.NodeId = leftFactory.New(left, child);
            original.AddChild(child);
            first.ValidateRegistration(left, original);
            ByteString encoded = NodeStateSerializer.Serialize(left, original);
            NodeState hydrated = NodeStateSerializer.Deserialize(right, encoded);
            second.ValidateReplicatedTree(right, hydrated);
            second.ValidateRegistration(right, hydrated);
            var children = new List<BaseInstanceState>();
            hydrated.GetChildren(right, children);

            await Assert.That(leftFactory.DetectsCollisions).IsTrue();
            await Assert.That(independent.NodeId).IsEqualTo(original.NodeId);
            await Assert.That(hydrated.NodeId).IsEqualTo(original.NodeId);
            await Assert.That(children.Count).IsEqualTo(1);
            await Assert.That(children[0].NodeId).IsEqualTo(child.NodeId);
            await Assert.That(rightFactory.New(right, children[0])).IsEqualTo(child.NodeId);
            await Assert.That(((BaseVariableState)children[0]).Value).IsEqualTo(Variant.From(original.NodeId));
        }

        private static SystemContext CreateContext(
            ReplicaNodeIdFactory identity,
            ITelemetryContext telemetry,
            string applicationUri)
        {
            var messages = ServiceMessageContext.CreateEmpty(telemetry);
            messages.NamespaceUris.Append(applicationUri);
            identity.PrepareNamespaces(messages.NamespaceUris);
            return new SystemContext(telemetry)
            {
                NamespaceUris = messages.NamespaceUris,
                ServerUris = messages.ServerUris,
                EncodeableFactory = messages.Factory
            };
        }
    }
}
