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

using System.IO;
using NUnit.Framework;
using Opc.Ua.Tests;
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
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ValueRankSerializationTestForBaseVariableStateAndBaseVariableTypeState
    {
        [Test]
        public void ValueRankPersistBaseVariableTypeState(
            [Values(
                ValueRanks.ScalarOrOneDimension,
                ValueRanks.Any,
                ValueRanks.Scalar,
                ValueRanks.OneOrMoreDimensions,
                ValueRanks.OneDimension,
                ValueRanks.TwoDimensions
            )]
                int valueRank)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var typeNode = new BaseDataVariableTypeState();
            var serviceMessageContext = new ServiceMessageContext(telemetry);
            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = serviceMessageContext.NamespaceUris
            };
            typeNode.Create(
                new SystemContext(telemetry) { NamespaceUris = serviceMessageContext.NamespaceUris },
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
            [Values(
                ValueRanks.ScalarOrOneDimension,
                ValueRanks.Any,
                ValueRanks.Scalar,
                ValueRanks.OneOrMoreDimensions,
                ValueRanks.OneDimension,
                ValueRanks.TwoDimensions
            )]
                int valueRank)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Here this type node is used just as support for the instanceNode to refer to
            var typeNode = new BaseDataVariableTypeState();
            var serviceMessageContext = new ServiceMessageContext(telemetry);
            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = serviceMessageContext.NamespaceUris
            };
            typeNode.Create(
                new SystemContext(telemetry) { NamespaceUris = serviceMessageContext.NamespaceUris },
                VariableTypeIds.DataItemType,
                BrowseNames.DataItemType,
                new LocalizedText("DataItemType"),
                true);

            // The instance BaseAnalogState node is a subtype of BaseVariableState for
            // which valueRank attribute is tested
            var instanceNode = new BaseAnalogState(typeNode) { ValueRank = valueRank };
            var loadedVariable = new BaseAnalogState(typeNode);
            using (var stream = new MemoryStream())
            {
                instanceNode.SaveAsBinary(systemContext, stream);
                stream.Position = 0;
                loadedVariable.LoadAsBinary(systemContext, stream);
            }

            Assert.AreEqual(valueRank, loadedVariable.ValueRank);
        }

        /// <summary>
        /// Test that byte[] array is correctly returned as Byte array type, not ByteString.
        /// </summary>
        [Test]
        public void ByteArrayWrappedValueReturnsCorrectBuiltInType()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Create a BaseDataVariableState with byte[] value
            var variableState = new BaseDataVariableState<byte[]>(null);
            var serviceMessageContext = new ServiceMessageContext(telemetry);
            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = serviceMessageContext.NamespaceUris
            };

            variableState.Create(
                systemContext,
                new NodeId(1000),
                new QualifiedName("ByteArrayVariable"),
                new LocalizedText("ByteArrayVariable"),
                true);

            // Set DataType to Byte and ValueRank to OneDimension (array)
            variableState.DataType = DataTypeIds.Byte;
            variableState.ValueRank = ValueRanks.OneDimension;

            // Set a byte array value
            byte[] testValue = [1, 2, 3, 4, 5];
            variableState.Value = testValue;

            // Get the WrappedValue and verify it's a Byte array, not ByteString
            Variant wrappedValue = variableState.WrappedValue;

            Assert.AreEqual(BuiltInType.Byte, wrappedValue.TypeInfo.BuiltInType, "BuiltInType should be Byte");
            Assert.AreEqual(ValueRanks.OneDimension, wrappedValue.TypeInfo.ValueRank, "ValueRank should be OneDimension");
            Assert.AreEqual(testValue, wrappedValue.Value, "Value should match the original byte array");
        }

        /// <summary>
        /// Test that ByteString is still correctly returned as ByteString type.
        /// </summary>
        [Test]
        public void ByteStringWrappedValueReturnsCorrectBuiltInType()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Create a BaseDataVariableState for ByteString testing
            var variableState = new BaseDataVariableState<byte[]>(null);
            var serviceMessageContext = new ServiceMessageContext(telemetry);
            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = serviceMessageContext.NamespaceUris
            };

            variableState.Create(
                systemContext,
                new NodeId(1001),
                new QualifiedName("ByteStringVariable"),
                new LocalizedText("ByteStringVariable"),
                true);

            // Set DataType to ByteString and ValueRank to Scalar
            variableState.DataType = DataTypeIds.ByteString;
            variableState.ValueRank = ValueRanks.Scalar;

            // Set a byte array value (which represents a ByteString)
            byte[] testValue = [1, 2, 3, 4, 5];
            variableState.Value = testValue;

            // Get the WrappedValue and verify it's a ByteString
            Variant wrappedValue = variableState.WrappedValue;

            Assert.AreEqual(BuiltInType.ByteString, wrappedValue.TypeInfo.BuiltInType, "BuiltInType should be ByteString");
            Assert.AreEqual(ValueRanks.Scalar, wrappedValue.TypeInfo.ValueRank, "ValueRank should be Scalar");
            Assert.AreEqual(testValue, wrappedValue.Value, "Value should match the original byte array");
        }
    }
}
