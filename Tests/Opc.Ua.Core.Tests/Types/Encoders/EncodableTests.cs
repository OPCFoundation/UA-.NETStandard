/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// Tests for the IEncodeable classes.
    /// </summary>
    [TestFixture, Category("EncodableTypes")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class EncodableTypesTests : EncoderCommon
    {
        #region DataPointSources
        [DatapointSource]
        public Type[] TypeArray = typeof(BaseObjectState).Assembly.GetExportedTypes().Where(type => IsEncodeableType(type)).ToArray();
        #endregion

        #region Test Methods
        /// <summary>
        /// Verify encode and decode of an encodable type.
        /// </summary>
        [Theory]
        [Category("EncodableTypes")]
        public void ActivateEncodableType(
            EncodingType encoderType,
            Type systemType
            )
        {
            IEncodeable testObject = CreateDefaultEncodableType(systemType) as IEncodeable;
            Assert.NotNull(testObject);
            Assert.False(testObject.BinaryEncodingId.IsNull);
            Assert.False(testObject.TypeId.IsNull);
            Assert.False(testObject.XmlEncodingId.IsNull);
            Assert.AreNotEqual(testObject.TypeId, testObject.BinaryEncodingId);
            Assert.AreNotEqual(testObject.TypeId, testObject.XmlEncodingId);
            Assert.AreNotEqual(testObject.BinaryEncodingId, testObject.XmlEncodingId);
            EncodeDecode(encoderType, BuiltInType.ExtensionObject, new ExtensionObject(testObject.TypeId, testObject));
        }

        /// <summary>
        /// Create an instance of an encodeable type with default values.
        /// </summary>
        /// <param name="systemType">The type to create</param>
        private object CreateDefaultEncodableType(Type systemType)
        {
            object instance = Activator.CreateInstance(systemType);
            SetDefaultEncodeableType(systemType, instance);
            return instance;
        }

        /// <summary>
        /// Set encodeable type properties recursively
        /// to expected default values.
        /// </summary>
        private void SetDefaultEncodeableType(Type systemType, object typeInstance)
        {
            foreach (var property in typeInstance.GetType().GetProperties())
            {
                if (property.CanWrite)
                {
                    var typeInfo = TypeInfo.Construct(property.PropertyType);
                    switch (typeInfo.BuiltInType)
                    {
                        case BuiltInType.ExtensionObject:
                            object propertyObject = property.GetValue(typeInstance);
                            if (propertyObject == null &&
                                property.PropertyType.IsAssignableFrom(typeof(ExtensionObject)))
                            {
                                property.SetValue(typeInstance, ExtensionObject.Null);
                            }
                            else if (propertyObject != null &&
                                propertyObject is IEncodeable)
                            {
                                SetDefaultEncodeableType(property.PropertyType, propertyObject);
                            }
                            break;
                        case BuiltInType.Null:
                            break;
                        case BuiltInType.DataValue:
                            if (property.GetValue(typeInstance) == null)
                            {
                                property.SetValue(typeInstance, new DataValue());
                            }
                            break;
                        case BuiltInType.NodeId:
                            if (property.GetValue(typeInstance) == null)
                            {
                                property.SetValue(typeInstance, NodeId.Null);
                            }
                            break;
                        case BuiltInType.ExpandedNodeId:
                            if (property.GetValue(typeInstance) == null)
                            {
                                property.SetValue(typeInstance, ExpandedNodeId.Null);
                            }
                            break;
                        case BuiltInType.DiagnosticInfo:
                            if (property.GetValue(typeInstance) == null)
                            {
                                property.SetValue(typeInstance, new DiagnosticInfo());
                            }
                            break;
                        default:
                            if (typeInfo.ValueRank == ValueRanks.Scalar)
                            {
                                var value = TypeInfo.GetDefaultValue(typeInfo.BuiltInType);
                                property.SetValue(typeInstance, value);
                            }
                            break;
                    }
                }
            }

        }
        #endregion
    }
}
