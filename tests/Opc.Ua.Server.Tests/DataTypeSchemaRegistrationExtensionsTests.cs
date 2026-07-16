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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Deterministic, offline unit tests for <see cref="DataTypeSchemaRegistrationExtensions"/>.
    /// </summary>
    [TestFixture]
    [Category("DataTypeSchemaRegistration")]
    [Parallelizable(ParallelScope.All)]
    public class DataTypeSchemaRegistrationExtensionsTests
    {
        private static DataTypeState CreateStructureDataType(uint id)
        {
            var definition = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields = new[]
                {
                    new StructureField
                    {
                        Name = "Value",
                        DataType = new NodeId((uint)BuiltInType.Int32),
                        ValueRank = ValueRanks.Scalar,
                        IsOptional = false
                    }
                }
            };

            return new DataTypeState
            {
                NodeId = new NodeId(id, 1),
                BrowseName = new QualifiedName("StructType" + id, 1),
                DataTypeDefinition = new ExtensionObject(definition)
            };
        }

        [Test]
        public void RegisterDataTypeSchemasAsyncWithNullServerThrows()
        {
            var registry = new DataTypeDefinitionRegistry();

            Assert.That(
                async () => await ((IServerInternal)null!)
                    .RegisterDataTypeSchemasAsync(registry)
                    .ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void RegisterDataTypeSchemasAsyncWithNullRegistryThrows()
        {
            var server = new Mock<IServerInternal>();

            Assert.That(
                async () => await server.Object
                    .RegisterDataTypeSchemasAsync(null!)
                    .ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void RegisterDataTypeSchemasWithNullNodesThrows()
        {
            var registry = new DataTypeDefinitionRegistry();

            Assert.That(
                () => ((IEnumerable<NodeState>)null!).RegisterDataTypeSchemas(registry),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void RegisterDataTypeSchemasWithNullRegistryThrows()
        {
            var nodes = new List<NodeState>();

            Assert.That(
                () => nodes.RegisterDataTypeSchemas(null!),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void TryRegisterDataTypeSchemaWithNullNodeThrows()
        {
            var registry = new DataTypeDefinitionRegistry();

            Assert.That(
                () => ((DataTypeState)null!).TryRegisterDataTypeSchema(registry),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void TryRegisterDataTypeSchemaWithNullRegistryThrows()
        {
            DataTypeState node = CreateStructureDataType(4001);

            Assert.That(
                () => node.TryRegisterDataTypeSchema(null!),
                Throws.TypeOf<System.ArgumentNullException>());
        }

        [Test]
        public void TryRegisterDataTypeSchemaRegistersDefinition()
        {
            DataTypeState node = CreateStructureDataType(4002);
            var registry = new DataTypeDefinitionRegistry();

            bool result = node.TryRegisterDataTypeSchema(registry);

            Assert.That(result, Is.True);
        }

        [Test]
        public void TryRegisterDataTypeSchemaWithoutDefinitionReturnsFalse()
        {
            var node = new DataTypeState
            {
                NodeId = new NodeId(4003, 1),
                BrowseName = new QualifiedName("NoDef", 1)
            };
            var registry = new DataTypeDefinitionRegistry();

            bool result = node.TryRegisterDataTypeSchema(registry);

            Assert.That(result, Is.False);
        }

        [Test]
        public void RegisterDataTypeSchemasCountsOnlyDataTypeStates()
        {
            var nodes = new List<NodeState>
            {
                CreateStructureDataType(4010),
                CreateStructureDataType(4011),
                new BaseObjectState(null)
            };
            var registry = new DataTypeDefinitionRegistry();

            int count = nodes.RegisterDataTypeSchemas(registry);

            Assert.That(count, Is.EqualTo(2));
        }

        [Test]
        public async Task RegisterDataTypeSchemasAsyncWalksTypeTreeAsync()
        {
            var namespaceUris = new NamespaceTable();
            var typeTree = new TypeTable(namespaceUris);
            DataTypeState dataType = CreateStructureDataType(4020);

            var nodeManager = new Mock<IMasterNodeManager>();
            nodeManager
                .Setup(m => m.FindNodeInAddressSpaceAsync(
                    It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId id, CancellationToken ct) =>
                    new ValueTask<NodeState?>(id == DataTypeIds.BaseDataType ? dataType : null));

            var server = new Mock<IServerInternal>();
            server.SetupGet(s => s.NamespaceUris).Returns(namespaceUris);
            server.SetupGet(s => s.TypeTree).Returns(typeTree);
            server.SetupGet(s => s.NodeManager).Returns(nodeManager.Object);

            var registry = new DataTypeDefinitionRegistry();

            int count = await server.Object
                .RegisterDataTypeSchemasAsync(registry)
                .ConfigureAwait(false);

            Assert.That(count, Is.EqualTo(1));
        }
    }
}
