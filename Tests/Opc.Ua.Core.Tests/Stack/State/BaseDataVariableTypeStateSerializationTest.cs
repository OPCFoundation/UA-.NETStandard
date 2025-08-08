using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Iced.Intel;
using NUnit.Framework;
using static Opc.Ua.NodeState;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests serialization and deserialization of ValueRank attribute for
    /// BaseVariableState and BaseVariableTypeState.
    /// The default ValueRank attribute value should be ValueRanks.Any as specified in the specification
    /// Serialization of the attribute in case ValueRanks.Any is used is ommited thus saving space.
    /// </summary>
    [TestFixture]
    [SetCulture("en-us"), SetUICulture("en-us")]
    public class ValueRankSerializationTestForBaseVariableStateAndBaseVariableTypeState
    {
        [Test]
        public void ValueRankPersistBaseVariableTypeState(
            [Values(ValueRanks.ScalarOrOneDimension, ValueRanks.Any, ValueRanks.Scalar,
            ValueRanks.OneOrMoreDimensions, ValueRanks.OneDimension, ValueRanks.TwoDimensions)] int valueRank)
        {
            var typeNode = new BaseDataVariableTypeState();
            var serviceMessageContext = new ServiceMessageContext();
            var systemContext = new SystemContext() { NamespaceUris = serviceMessageContext.NamespaceUris };
            typeNode.Create(
                    new SystemContext() { NamespaceUris = serviceMessageContext.NamespaceUris },
                    VariableTypeIds.DataItemType,
                    BrowseNames.DataItemType,
                    new LocalizedText("DataItemType"),
                    true);

            typeNode.ValueRank = valueRank;
            var loadedVariable = new BaseDataVariableTypeState();
            using (var stream = new MemoryStream())
            {
                typeNode.SaveAsBinary(systemContext, stream);
                stream.Position = 0;
                loadedVariable.LoadAsBinary(systemContext, stream);
            }

            Assert.AreEqual(valueRank, loadedVariable.ValueRank);
        }

        [Test]
        public void ValueRankPersistBaseVariableState(
             [Values(ValueRanks.ScalarOrOneDimension, ValueRanks.Any, ValueRanks.Scalar,
            ValueRanks.OneOrMoreDimensions, ValueRanks.OneDimension, ValueRanks.TwoDimensions)] int valueRank)
        {
            // Here this type node is used just as support for the instanceNode to refer to
            var typeNode = new BaseDataVariableTypeState();
            var serviceMessageContext = new ServiceMessageContext();
            var systemContext = new SystemContext() { NamespaceUris = serviceMessageContext.NamespaceUris };
            typeNode.Create(
                    new SystemContext() { NamespaceUris = serviceMessageContext.NamespaceUris },
                    VariableTypeIds.DataItemType,
                    BrowseNames.DataItemType,
                    new LocalizedText("DataItemType"),
                    true);

            // The instance BaseAnalogState node is a subtype of BaseVariableState for
            // which valueRank attribute is tested
            var instanceNode = new BaseAnalogState(typeNode) {
                ValueRank = valueRank
            };
            var loadedVariable = new BaseAnalogState(typeNode);
            using (var stream = new MemoryStream())
            {
                instanceNode.SaveAsBinary(systemContext, stream);
                stream.Position = 0;
                loadedVariable.LoadAsBinary(systemContext, stream);
            }

            Assert.AreEqual(valueRank, loadedVariable.ValueRank);
        }
    }
}
