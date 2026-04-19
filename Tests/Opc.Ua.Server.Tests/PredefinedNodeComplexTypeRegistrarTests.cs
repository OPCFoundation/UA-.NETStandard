using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("ComplexTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class PredefinedNodeComplexTypeRegistrarTests
    {
        [Test]
        public void RegisterDataTypesAddsEnumAndStructureTypesToEncodeableFactory()
        {
            SystemContext context = CreateSystemContext();
            ushort namespaceIndex = context.NamespaceUris.GetIndexOrAppend("urn:server-complex-types");

            var enumDataType = new DataTypeState
            {
                NodeId = new NodeId(1001, namespaceIndex),
                BrowseName = new QualifiedName("MyEnum", namespaceIndex),
                SuperTypeId = DataTypeIds.Enumeration,
                DataTypeDefinition = new ExtensionObject(new EnumDefinition
                {
                    Fields =
                    [
                        new EnumField
                        {
                            Name = "First",
                            Value = 0
                        }
                    ]
                })
            };

            var structureDataType = new DataTypeState
            {
                NodeId = new NodeId(1002, namespaceIndex),
                BrowseName = new QualifiedName("MyStruct", namespaceIndex),
                SuperTypeId = DataTypeIds.Structure,
                DataTypeDefinition = new ExtensionObject(new StructureDefinition
                {
                    BaseDataType = DataTypeIds.Structure,
                    StructureType = StructureType.Structure,
                    Fields =
                    [
                        new StructureField
                        {
                            Name = "State",
                            DataType = enumDataType.NodeId,
                            ValueRank = ValueRanks.Scalar
                        }
                    ]
                })
            };

            var binaryEncoding = new BaseObjectState(null)
            {
                NodeId = new NodeId(2001, namespaceIndex),
                BrowseName = new QualifiedName(BrowseNames.DefaultBinary, 0),
                TypeDefinitionId = ObjectTypeIds.DataTypeEncodingType
            };

            structureDataType.AddReference(ReferenceTypeIds.HasEncoding, false, binaryEncoding.NodeId);

            var predefinedNodes = new NodeStateCollection
            {
                enumDataType,
                structureDataType,
                binaryEncoding
            };

            PredefinedNodeComplexTypeRegistrar.RegisterDataTypes(context, predefinedNodes);

            ExpandedNodeId enumTypeId = NodeId.ToExpandedNodeId(enumDataType.NodeId, context.NamespaceUris);
            ExpandedNodeId structureTypeId = NodeId.ToExpandedNodeId(structureDataType.NodeId, context.NamespaceUris);
            ExpandedNodeId binaryEncodingId = NodeId.ToExpandedNodeId(binaryEncoding.NodeId, context.NamespaceUris);

            bool enumRegistered = context.EncodeableFactory.TryGetEnumeratedType(enumTypeId, out IEnumeratedType enumType);
            bool structureRegistered = context.EncodeableFactory.TryGetEncodeableType(structureTypeId, out IEncodeableType structureType);
            bool encodingRegistered = context.EncodeableFactory.TryGetEncodeableType(binaryEncodingId, out IEncodeableType encodingType);

            Assert.That(enumRegistered, Is.True);
            Assert.That(enumType, Is.Not.Null);
            Assert.That(structureRegistered, Is.True);
            Assert.That(structureType, Is.Not.Null);
            Assert.That(encodingRegistered, Is.True);
            Assert.That(encodingType, Is.SameAs(structureType));
        }

        private static SystemContext CreateSystemContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(telemetry);

            return new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                TypeTable = new TypeTable(messageContext.NamespaceUris),
                EncodeableFactory = messageContext.Factory
            };
        }
    }
}
