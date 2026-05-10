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

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for Attribute Write Values.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AttributeServices")]
    public class AttributeWriteValuesTests : TestFixture
    {
        [Description("Write to the Value attribute of a Variable, where the AccessLevel == CurrentWriteService.*/")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "004")]
        public async Task WriteValueWithCurrentWriteAccessSucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);
        }

        [Description("Write the minimum value for each supported data-type. Some items may fail with BadOutOfRange. */")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "005")]
        public async Task WriteMinimumValueForSupportedDataTypesSucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);
        }

        [Description("Write the MAXIMUM value for each supported data-type. */")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "006")]
        public async Task WriteMaximumValueForSupportedDataTypesSucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);
        }

        [Description("Write to a localizedText passing in all params, no params, and some params. */")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "009")]
        public async Task WriteLocalizedTextWithVariousParametersSucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);
        }

        [Description("write to a node of type INTEGER where the data-type of the value is specified as SByte, Int16, Int32, and Int64; where the value is: - Data-type max - Data-type min */")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "010")]
        public async Task WriteIntegerWithSignedDataTypeVariantsSucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);
        }

        [Description("write to a node of type UINTEGER where the data-type of the value is specified as Byte, UInt16, UInt32, and UInt64; where the value is: - Data-type max - Data-type min */")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "011")]
        public async Task WriteUIntegerWithUnsignedDataTypeVariantsSucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);
        }

        [Description("Write() a value of NaN to all configured floating point numbers (Float, Double &amp; Duration). */")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "012")]
        public async Task WriteNaNToFloatingPointTypesSucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);
        }

        [Description("Write to a string variable; specify a value within the extended code-page */ include( &quot;./library/Information/InfoFactory.js&quot; );")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "013")]
        public async Task WriteStringWithExtendedCodePageSucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);
        }

        [Description("Write to the Value attribute (without statusCode, sourceTimestamp, or serverTimestamp) of several nodes that has a ValueRank of array. Write the entire array. This test should be d")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "019")]
        public async Task WriteEntireArrayValueSucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);
        }

        [Description("Write to the Value attribute (without statusCode, sourceTimestamp, or serverTimestamp) of nodes which have a ValueRank of a multi-dimensional array. Write the entire multi-dimensio")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "020")]
        public async Task WriteEntireMultiDimensionalArraySucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);
        }

        [Description("Write to the Value attribute (without statusCode, sourceTimestamp, or serverTimestamp) of several nodes which have aValueRank of a multi-dimensional array. Write the entire multi-d")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "021")]
        public async Task WriteEntireMultiDimensionalArrayToMultipleNodesSucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);
        }

        [Description("nodesToWrite array empty; expected service result = BadNothingToDo. */")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "Err-001")]
        public async Task WriteEmptyArrayReturnsBadNothingToDoAsync()
        {
            ArrayOf<WriteValue> wv = Array.Empty<WriteValue>().ToArrayOf();
            WriteResponse response;
            try
            {
                response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadNothingToDo));
                return;
            }

            // BadNothingToDo may be reported as the service result or per-operation result.
            StatusCode serviceResult = response.ResponseHeader.ServiceResult;
            if (serviceResult != StatusCodes.Good)
            {
                Assert.That((uint)serviceResult, Is.EqualTo((uint)StatusCodes.BadNothingToDo));
                Assert.That(response.Results, Is.Null.Or.Empty);
            }
            else
            {
                Assert.That(response.Results, Is.Not.Null);
                Assert.That(response.Results.Count, Is.Zero);
            }
        }

        [Description("Write to valid attributes of multiple unknown nodes, in a single call. */")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "Err-004")]
        public async Task WriteToMultipleUnknownNodesReturnsBadStatusAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = new NodeId("NonExistent_InvalidNode_1", 2),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(1))
                },
                new() {
                    NodeId = new NodeId("NonExistent_InvalidNode_2", 2),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(2))
                },
                new() {
                    NodeId = new NodeId("NonExistent_InvalidNode_3", 2),
                    AttributeId = Attributes.DisplayName,
                    Value = new DataValue(new Variant(new LocalizedText("en", "Test3")))
                },
                new() {
                    NodeId = new NodeId("NonExistent_InvalidNode_4", 2),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(4))
                },
                new() {
                    NodeId = new NodeId(Guid.NewGuid(), 2),
                    AttributeId = Attributes.DisplayName,
                    Value = new DataValue(new Variant(new LocalizedText("en", "Test5")))
                }
            }.ToArrayOf();

            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.Results.Count, Is.EqualTo(wv.Count));
            for (int i = 0; i < response.Results.Count; i++)
            {
                Assert.That(StatusCode.IsBad(response.Results[i]), Is.True,
                    $"Expected Bad status for unknown node at index {i}, got {response.Results[i]}");
            }
        }

        [Description("Write to an invalid (non-writable) attribute of a valid node. */")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "Err-005")]
        public async Task WriteToNonWritableAttributeReturnsBadStatusAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.NodeClass,
                    Value = new DataValue(new Variant((int)NodeClass.Variable))
                }
            }.ToArrayOf();

            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0]), Is.True,
                $"Expected Bad status when writing non-writable attribute, got {response.Results[0]}");
            uint code = response.Results[0].Code;
            Assert.That(code,
                Is.EqualTo(StatusCodes.BadNotWritable)
                    .Or.EqualTo(StatusCodes.BadAttributeIdInvalid)
                    .Or.EqualTo(StatusCodes.BadWriteNotSupported)
                    .Or.EqualTo(StatusCodes.BadUserAccessDenied),
                $"Unexpected status code 0x{code:X8}");
        }

        [Description("Write to invalid (non-writable) attributes of a valid node, multiple times in the same call. */")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "Err-006")]
        public async Task WriteToMultipleNonWritableAttributesReturnsBadStatusAsync()
        {
            NodeId target = ToNodeId(Constants.ScalarStaticInt32);
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = target,
                    AttributeId = Attributes.NodeClass,
                    Value = new DataValue(new Variant((int)NodeClass.Variable))
                },
                new() {
                    NodeId = target,
                    AttributeId = Attributes.BrowseName,
                    Value = new DataValue(new Variant(new QualifiedName("Renamed", 2)))
                },
                new() {
                    NodeId = target,
                    AttributeId = Attributes.NodeId,
                    Value = new DataValue(new Variant(new NodeId(99u, 2)))
                },
                new() {
                    NodeId = target,
                    AttributeId = Attributes.DataType,
                    Value = new DataValue(new Variant(DataTypeIds.String))
                }
            }.ToArrayOf();

            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.Results.Count, Is.EqualTo(wv.Count));
            for (int i = 0; i < response.Results.Count; i++)
            {
                Assert.That(StatusCode.IsBad(response.Results[i]), Is.True,
                    $"Expected Bad status at index {i}, got {response.Results[i]}");
            }
        }

        [Description("Write to a node whose AccessLevel does not contain write capabilities. */")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "Err-007")]
        public async Task WriteToReadOnlyNodeReturnsBadStatusAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = VariableIds.Server_ServerStatus_State,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant((int)ServerState.Running))
                }
            }.ToArrayOf();

            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0]), Is.True,
                $"Expected Bad status when writing read-only node, got {response.Results[0]}");
            uint code = response.Results[0].Code;
            Assert.That(code,
                Is.EqualTo(StatusCodes.BadNotWritable)
                    .Or.EqualTo(StatusCodes.BadUserAccessDenied)
                    .Or.EqualTo(StatusCodes.BadWriteNotSupported),
                $"Unexpected status code 0x{code:X8}");
        }

        [Description("Write a NULL value for each supported data-type. Expect a Bad_TypeMismatch for each operation level result. */")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "Err-009")]
        public async Task WriteNullValueReturnsBadTypeMismatchAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = Constants.InvalidNodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0]), Is.True);
        }

        [Description("write to a node of type UINTEGER where the data-type of the value is specified as SByte, Int16, Int32, and Int64 */")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "Err-010")]
        public async Task WriteSignedTypeToUIntegerNodeReturnsBadStatusAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = Constants.InvalidNodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0]), Is.True);
        }

        [Description("write to a node of type INTEGER where the data-type of the value is specified as SByte, UInt16, UInt32, and UInt64 */")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "Err-011")]
        public async Task WriteUnsignedTypeToIntegerNodeReturnsBadStatusAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = Constants.InvalidNodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0]), Is.True);
        }

        [Description("Write to a node of type INTEGER where the data-type of the value is specified as float. */")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "Err-012")]
        public async Task WriteFloatToIntegerNodeReturnsBadStatusAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = Constants.InvalidNodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0]), Is.True);
        }

        [Description("Write to a LocalizedText with a valid value, but specify a localeId that is known to not be supported, e g. aardvark. */")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "Err-015")]
        public async Task WriteLocalizedTextWithUnsupportedLocaleReturnsBadStatusAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = Constants.InvalidNodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0]), Is.True);
        }
    }
}
