using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TypeInfoTests
    {
        [Test]
        public void Construct_ForListOfInt_ReturnsIntArray()
        {
            TypeInfo typeInfo = TypeInfo.Construct(typeof(List<int>));
            Assert.That(typeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(typeInfo.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void Construct_ForNonEnumerableGenericType_ReturnsUnknown()
        {
            TypeInfo typeInfo = TypeInfo.Construct(typeof(Task<int>));
            Assert.That(typeInfo.BuiltInType, Is.EqualTo(BuiltInType.Null));
            Assert.That(typeInfo.ValueRank, Is.EqualTo(ValueRanks.Any));
        }

        [Test]
        public void IsInstanceOfBaseDataTypeAcceptsTypedArray()
        {
            var namespaceUris = new NamespaceTable();
            TypeInfo typeInfo = TypeInfo.IsInstanceOfDataType(
                new[] { 1, 2, 3 },
                DataTypeIds.BaseDataType,
                ValueRanks.OneDimension,
                namespaceUris,
                new TypeTable(namespaceUris));

            Assert.That(typeInfo, Is.Not.Null);
            Assert.That(typeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
        }

        [Test]
        public void IsInstanceOfBaseDataTypeAcceptsVariantArrayContainingArray()
        {
            Variant[] value =
            [
                new Variant(1),
                new Variant(new StatusCode[] { StatusCodes.Good, StatusCodes.Bad })
            ];
            var namespaceUris = new NamespaceTable();

            TypeInfo typeInfo = TypeInfo.IsInstanceOfDataType(
                value,
                DataTypeIds.BaseDataType,
                ValueRanks.OneDimension,
                namespaceUris,
                new TypeTable(namespaceUris));

            Assert.That(typeInfo, Is.Not.Null);
            Assert.That(typeInfo.BuiltInType, Is.EqualTo(BuiltInType.Variant));
        }

        [Test]
        public void IsInstanceOfSpecificDataTypeStillRejectsMismatch()
        {
            var namespaceUris = new NamespaceTable();
            TypeInfo typeInfo = TypeInfo.IsInstanceOfDataType(
                new[] { 1, 2, 3 },
                DataTypeIds.String,
                ValueRanks.OneDimension,
                namespaceUris,
                new TypeTable(namespaceUris));

            Assert.That(typeInfo, Is.Null);
        }
    }
}
