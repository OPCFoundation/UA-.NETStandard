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

#nullable enable

using NUnit.Framework;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="NodeStateSerializer"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class NodeStateSerializerTests
    {
        private const ushort NamespaceIndex = 1;
        private SystemContext m_context = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:serializer");
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public void ObjectRoundTripsAsBaseObjectState()
        {
            var original = new BaseObjectState(null)
            {
                NodeId = new NodeId("obj", NamespaceIndex),
                BrowseName = new QualifiedName("Obj", NamespaceIndex),
                DisplayName = new LocalizedText("Obj")
            };

            ByteString payload = NodeStateSerializer.Serialize(m_context, original);
            NodeState restored = NodeStateSerializer.Deserialize(m_context, payload);

            Assert.That(restored, Is.InstanceOf<BaseObjectState>());
            Assert.That(restored.NodeClass, Is.EqualTo(NodeClass.Object));
            Assert.That(restored.BrowseName, Is.EqualTo(original.BrowseName));
        }

        [Test]
        public void VariableRoundTripsWithValue()
        {
            var original = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("var", NamespaceIndex),
                BrowseName = new QualifiedName("Var", NamespaceIndex),
                DisplayName = new LocalizedText("Var"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                Value = new Variant(2.5)
            };

            ByteString payload = NodeStateSerializer.Serialize(m_context, original);
            NodeState restored = NodeStateSerializer.Deserialize(m_context, payload);

            Assert.That(restored, Is.InstanceOf<BaseDataVariableState>());
            Assert.That(restored.NodeClass, Is.EqualTo(NodeClass.Variable));
            var variable = (BaseDataVariableState)restored;
            Assert.That(variable.Value, Is.EqualTo(new Variant(2.5)));
            Assert.That(variable.DataType, Is.EqualTo(DataTypeIds.Double));
        }

        [Test]
        public void MethodRoundTripsAsMethodState()
        {
            var original = new MethodState(null)
            {
                NodeId = new NodeId("m", NamespaceIndex),
                BrowseName = new QualifiedName("DoIt", NamespaceIndex),
                DisplayName = new LocalizedText("DoIt"),
                Executable = true,
                UserExecutable = true
            };

            ByteString payload = NodeStateSerializer.Serialize(m_context, original);
            NodeState restored = NodeStateSerializer.Deserialize(m_context, payload);

            Assert.That(restored, Is.InstanceOf<MethodState>());
            Assert.That(((MethodState)restored).Executable, Is.True);
        }

        [Test]
        public void DeserializeTooShortPayloadThrows()
        {
            var tooShort = ByteString.From(new byte[] { 1, 2 });

            Assert.That(
                () => NodeStateSerializer.Deserialize(m_context, tooShort),
                Throws.TypeOf<ServiceResultException>());
        }
    }
}
