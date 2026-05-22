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

namespace Opc.Ua.Conformance.Tests.AttributeServices
{
    /// <summary>
    /// compliance tests for Attribute Service Set – Write Values.
    /// Based on test scripts: Attribute Write 001–018 and Err tests.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AttributeWrite")]
    public class AttributeWriteTests : TestFixture
    {
        [Description("Write a single Int32 value and read it back.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "001")]
        public async Task AttributeWrite001WriteSingleValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(42))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "Write of Int32 value should return Good.");

            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
            Assert.That(readResponse.Results[0].WrappedValue.TryGetValue(out int readBack), Is.True);
            Assert.That(readBack, Is.EqualTo(42));
        }

        [Description("Write multiple values in a single call. Both should return Good.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "002")]
        public async Task AttributeWrite002WriteMultipleValuesAsync()
        {
            NodeId int32Node = ToNodeId(Constants.ScalarStaticInt32);
            NodeId doubleNode = ToNodeId(Constants.ScalarStaticDouble);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = int32Node,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(100))
                    },
                    new() {
                        NodeId = doubleNode,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(3.14))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(2));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "Write of Int32 value should return Good.");
            Assert.That(StatusCode.IsGood(writeResponse.Results[1]), Is.True,
                "Write of Double value should return Good.");
        }

        [Description("Write a Boolean value and read it back.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "001")]
        public async Task AttributeWrite003WriteBooleanAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(true))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "Write of Boolean value should return Good.");

            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results[0].WrappedValue.TryGetValue(out bool readBack), Is.True);
            Assert.That(readBack, Is.True);
        }

        [Description("Write a negative Int32 value and read it back.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "001")]
        public async Task AttributeWrite004WriteInt32Async()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(-12345))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True);

            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results[0].WrappedValue.TryGetValue(out int readBack), Is.True);
            Assert.That(readBack, Is.EqualTo(-12345));
        }

        [Description("Write a Double value and read it back.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "001")]
        public async Task AttributeWrite005WriteDoubleAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(2.71828))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True);

            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results[0].WrappedValue.TryGetValue(out double readBack), Is.True);
            Assert.That(readBack, Is.EqualTo(2.71828));
        }

        [Description("Write a String value and read it back.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "001")]
        public async Task AttributeWrite006WriteStringAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticString);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant("Hello World"))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True);

            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results[0].WrappedValue.TryGetValue(out string readBack), Is.True);
            Assert.That(readBack, Is.EqualTo("Hello World"));
        }

        [Description("Write a DateTime value. Expect Good.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "001")]
        public async Task AttributeWrite007WriteDateTimeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDateTime);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(DateTime.UtcNow))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "Write of DateTime value should return Good.");
        }

        [Description("Write a ByteString value. Expect Good.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "007")]
        public async Task AttributeWrite008WriteByteStringAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticByteString);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(new ByteString(new byte[] { 0x01, 0x02, 0x03 })))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "Write of ByteString value should return Good.");
        }

        [Description("Write a Float value and read it back.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "001")]
        public async Task AttributeWrite009WriteFloatAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticFloat);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(1.5f))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True);

            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results[0].WrappedValue.TryGetValue(out float readBack), Is.True);
            Assert.That(readBack, Is.EqualTo(1.5f));
        }

        [Description("Write an SByte value. Expect Good.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "001")]
        public async Task AttributeWrite010WriteSByteAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticSByte);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant((sbyte)-10))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "Write of SByte value should return Good.");
        }

        [Description("Write a Byte value. Expect Good.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "001")]
        public async Task AttributeWrite011WriteByteAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticByte);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant((byte)255))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "Write of Byte value should return Good.");
        }

        [Description("Write an Int16 value. Expect Good.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "001")]
        public async Task AttributeWrite012WriteInt16Async()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt16);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant((short)1000))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "Write of Int16 value should return Good.");
        }

        [Description("Write a UInt16 value. Expect Good.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "001")]
        public async Task AttributeWrite013WriteUInt16Async()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticUInt16);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant((ushort)50000))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "Write of UInt16 value should return Good.");
        }

        [Description("Write an Int64 value. Expect Good.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "001")]
        public async Task AttributeWrite014WriteInt64Async()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt64);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(123456789L))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "Write of Int64 value should return Good.");
        }

        [Description("Write a UInt64 value. Expect Good.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "001")]
        public async Task AttributeWrite015WriteUInt64Async()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticUInt64);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(999999999UL))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "Write of UInt64 value should return Good.");
        }

        [Description("Write a Guid value. Expect Good.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "001")]
        public async Task AttributeWrite016WriteGuidAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticGuid);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(new Uuid(Guid.NewGuid())))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                "Write of Guid value should return Good.");
        }

        [Description("Write Boolean, Int32, String, and Double in one call. Read all back and verify.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "002")]
        [Property("Tag", "003")]
        public async Task AttributeWrite017WriteAndReadBackMultipleTypesAsync()
        {
            NodeId boolNode = ToNodeId(Constants.ScalarStaticBoolean);
            NodeId int32Node = ToNodeId(Constants.ScalarStaticInt32);
            NodeId stringNode = ToNodeId(Constants.ScalarStaticString);
            NodeId doubleNode = ToNodeId(Constants.ScalarStaticDouble);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = boolNode,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(false))
                    },
                    new() {
                        NodeId = int32Node,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(777))
                    },
                    new() {
                        NodeId = stringNode,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant("MultiWrite"))
                    },
                    new() {
                        NodeId = doubleNode,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(9.81))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(4));
            for (int i = 0; i < 4; i++)
            {
                Assert.That(StatusCode.IsGood(writeResponse.Results[i]), Is.True,
                    $"Write result at index {i} should be Good.");
            }

            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = boolNode, AttributeId = Attributes.Value },
                    new() { NodeId = int32Node, AttributeId = Attributes.Value },
                    new() { NodeId = stringNode, AttributeId = Attributes.Value },
                    new() { NodeId = doubleNode, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(4));

            Assert.That(readResponse.Results[0].WrappedValue.TryGetValue(out bool boolVal), Is.True);
            Assert.That(boolVal, Is.False);

            Assert.That(readResponse.Results[1].WrappedValue.TryGetValue(out int intVal), Is.True);
            Assert.That(intVal, Is.EqualTo(777));

            Assert.That(readResponse.Results[2].WrappedValue.TryGetValue(out string strVal), Is.True);
            Assert.That(strVal, Is.EqualTo("MultiWrite"));

            Assert.That(readResponse.Results[3].WrappedValue.TryGetValue(out double dblVal), Is.True);
            Assert.That(dblVal, Is.EqualTo(9.81));
        }

        [Description("Write an Int32 array and read it back. Verify length.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "018")]
        public async Task AttributeWrite018WriteArrayValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);

            ArrayOf<int> arrayValue = new int[] { 10, 20, 30 }.ToArrayOf();

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(arrayValue))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                $"Write of Int32 array should return Good, got {writeResponse.Results[0]}.");

            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
            Assert.That(
                readResponse.Results[0].WrappedValue.TryGetValue(out ArrayOf<int> result), Is.True);
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Description("Err 001: Write to an invalid NodeId from Constants. Expect BadNodeIdUnknown.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "Err-002")]
        public async Task AttributeWriteErr001WriteToInvalidNodeIdAsync()
        {
            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = Constants.InvalidNodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(0))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(writeResponse.Results[0], Is.EqualTo(StatusCodes.BadNodeIdUnknown),
                "Writing to an invalid NodeId should return BadNodeIdUnknown.");
        }

        [Description("Err 002: Write a String to a node that expects Int32. Expect BadTypeMismatch.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "Err-008")]
        public async Task AttributeWriteErr002WriteWrongDataTypeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant("hello"))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(writeResponse.Results[0], Is.EqualTo(StatusCodes.BadTypeMismatch),
                "Writing wrong data type should return BadTypeMismatch.");
        }

        [Description("Err 003: Write to a completely made-up NodeId. Expect BadNodeIdUnknown.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "Err-003")]
        public async Task AttributeWriteErr003WriteBadNodeIdUnknownAsync()
        {
            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = new NodeId(99999, 99),
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(0))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(writeResponse.Results[0], Is.EqualTo(StatusCodes.BadNodeIdUnknown),
                "Writing to a non-existent NodeId should return BadNodeIdUnknown.");
        }

        [Description("Write Int32 with SourceTimestamp set to UtcNow. Server may accept or reject the timestamp.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "004")]
        public async Task AttributeWriteWithSourceTimestampAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            var dv = new DataValue(new Variant(99))
            {
                SourceTimestamp = DateTime.UtcNow
            };

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = dv
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(writeResponse.Results[0]) ||
                writeResponse.Results[0] == StatusCodes.BadWriteNotSupported,
                Is.True,
                $"Expected Good or BadWriteNotSupported, got {writeResponse.Results[0]}");
        }

        [Description("Write Int32 with ServerTimestamp set. Most servers ignore or reject server timestamp on write.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "004")]
        public async Task AttributeWriteWithServerTimestampAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            var dv = new DataValue(new Variant(100))
            {
                ServerTimestamp = DateTime.UtcNow
            };

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = dv
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(writeResponse.Results[0]) ||
                writeResponse.Results[0] == StatusCodes.BadWriteNotSupported,
                Is.True,
                $"Expected Good or BadWriteNotSupported, got {writeResponse.Results[0]}");
        }

        [Description("Write Int32 with both SourceTimestamp and ServerTimestamp set.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "004")]
        public async Task AttributeWriteWithBothTimestampsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DateTime now = DateTime.UtcNow;
            var dv = new DataValue(new Variant(101))
            {
                SourceTimestamp = now,
                ServerTimestamp = now
            };

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = dv
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(writeResponse.Results[0]) ||
                writeResponse.Results[0] == StatusCodes.BadWriteNotSupported,
                Is.True,
                $"Expected Good or BadWriteNotSupported, got {writeResponse.Results[0]}");
        }

        [Description("Write Int32 with explicit StatusCode.Good.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "004")]
        public async Task AttributeWriteWithStatusCodeGoodAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            var dv = new DataValue(new Variant(101))
            {
                StatusCode = StatusCodes.Good
            };

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = dv
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(writeResponse.Results[0]) ||
                writeResponse.Results[0] == StatusCodes.BadWriteNotSupported,
                Is.True,
                $"Expected Good or BadWriteNotSupported, got {writeResponse.Results[0]}");
        }

        [Description("Write Int32 with StatusCode.Bad. Some servers do not allow writing bad status.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "004")]
        public async Task AttributeWriteWithStatusCodeBadAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            var dv = new DataValue(new Variant(102))
            {
                StatusCode = StatusCodes.Bad
            };

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = dv
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(writeResponse.Results[0]) ||
                writeResponse.Results[0] == StatusCodes.BadWriteNotSupported,
                Is.True,
                $"Expected Good or BadWriteNotSupported, got {writeResponse.Results[0]}");
        }

        [Description("Write Int32 with SourceTimestamp set to one year in the past.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "004")]
        public async Task AttributeWriteWithSourceTimestampInPastAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            var dv = new DataValue(new Variant(103))
            {
                SourceTimestamp = DateTime.UtcNow.AddYears(-1)
            };

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = dv
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(writeResponse.Results[0]) ||
                writeResponse.Results[0] == StatusCodes.BadWriteNotSupported,
                Is.True,
                $"Expected Good or BadWriteNotSupported, got {writeResponse.Results[0]}");
        }

        [Description("Write Int32 with SourceTimestamp set to one hour in the future.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "004")]
        public async Task AttributeWriteWithSourceTimestampInFutureAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            var dv = new DataValue(new Variant(104))
            {
                SourceTimestamp = DateTime.UtcNow.AddHours(1)
            };

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = dv
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(writeResponse.Results[0]) ||
                writeResponse.Results[0] == StatusCodes.BadWriteNotSupported,
                Is.True,
                $"Expected Good or BadWriteNotSupported, got {writeResponse.Results[0]}");
        }

        [Description("Write Int32 with SourceTimestamp, then read back with TimestampsToReturn.Source and verify SourceTimestamp is set.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "004")]
        public async Task AttributeWriteReadBackTimestampAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DateTime sourceTs = DateTime.UtcNow;
            var dv = new DataValue(new Variant(200))
            {
                SourceTimestamp = sourceTs
            };

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = dv
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));

            // If the server does not support writing timestamps, skip read-back check
            if (writeResponse.Results[0] == StatusCodes.BadWriteNotSupported)
            {
                Assert.Ignore("Server does not support writing timestamps.");
            }

            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                $"Write should return Good, got {writeResponse.Results[0]}");

            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Source,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
            Assert.That(readResponse.Results[0].SourceTimestamp, Is.Not.EqualTo(DateTime.MinValue),
                "SourceTimestamp should be set after writing with a timestamp.");
        }

        [Description("Write Int32 with StatusCode = Uncertain. Server may accept or reject.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "004")]
        public async Task AttributeWriteStatusCodeOverrideToUncertainAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            var dv = new DataValue(new Variant(105))
            {
                StatusCode = StatusCodes.Uncertain
            };

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = dv
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(writeResponse.Results[0]) ||
                writeResponse.Results[0] == StatusCodes.BadWriteNotSupported,
                Is.True,
                $"Expected Good or BadWriteNotSupported, got {writeResponse.Results[0]}");
        }

        [Description("Write Int32 with SourceTimestamp = DateTime.MinValue. Server should accept the value and ignore or reset the timestamp.")]
        [Test]
        [Property("ConformanceUnit", "Attribute Write Values")]
        [Property("Tag", "004")]
        public async Task AttributeWriteValueWithMinDateTimeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            var dv = new DataValue(new Variant(106))
            {
                SourceTimestamp = DateTime.MinValue
            };

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = dv
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(writeResponse.Results[0]) ||
                writeResponse.Results[0] == StatusCodes.BadWriteNotSupported,
                Is.True,
                $"Expected Good or BadWriteNotSupported, got {writeResponse.Results[0]}");
        }
    }
}
