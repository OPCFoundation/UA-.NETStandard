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

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for Attribute Write StatusCode & TimeStamp.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AttributeServices")]
    public class AttributeWriteStatuscodeTimestampTests : TestFixture
    {
        [Description("Write to a single valid Node a VTQ by passing the Value and Quality only. */")]
        [Test]
        public async Task WriteValueWithStatusCodeOnlySucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                    {
                        StatusCode = StatusCodes.Good
                    }
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Description("Write to a single valid Node a VTQ by passing the Value, Quality and sourceTimestamp. */")]
        [Test]
        public async Task WriteValueWithStatusCodeAndSourceTimestampSucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                    {
                        StatusCode = StatusCodes.Good,
                        SourceTimestamp = DateTime.UtcNow
                    }
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Description("Write to a single valid Node a VTQ by passing the Value, Quality, sourceTimestamp and serverTimestamp. */")]
        [Test]
        public async Task WriteValueWithStatusCodeAndBothTimestampsSucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                    {
                        StatusCode = StatusCodes.Good,
                        ServerTimestamp = DateTime.UtcNow,
                        SourceTimestamp = DateTime.UtcNow
                    }
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Description("Write to a single valid Node a Value and SourceTimestamp. */")]
        [Test]
        public async Task WriteValueWithSourceTimestampOnlySucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                    {
                        SourceTimestamp = DateTime.UtcNow
                    }
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Description("Write to a single valid Node a Value only. Do not specify a StatusCode or any Timestamps.*/")]
        [Test]
        public async Task WriteValueWithoutStatusCodeOrTimestampsSucceedsAsync()
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
        }

        [Description("Write to the value attribute a Value, a TimestampServer and TimestampSource, but do not specify a StatusCode. */")]
        [Test]
        public async Task WriteValueWithBothTimestampsAndNoStatusCodeSucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                    {
                        ServerTimestamp = DateTime.UtcNow,
                        SourceTimestamp = DateTime.UtcNow
                    }
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Description("Write to the Value attribute a Value, StatusCode and TimestampSource, but do not specify a TimestampServer. */")]
        [Test]
        public async Task WriteValueWithStatusCodeAndSourceTimestampNoServerSucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                    {
                        StatusCode = StatusCodes.Good,
                        SourceTimestamp = DateTime.UtcNow
                    }
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Description("Write to a single valid Node a Value and ServerTimestamp only. Expect Good or Bad_WriteNotSupported. */")]
        [Test]
        public async Task WriteValueWithServerTimestampOnlySucceedsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                    {
                        StatusCode = StatusCodes.Good,
                        ServerTimestamp = DateTime.UtcNow,
                        SourceTimestamp = DateTime.UtcNow
                    }
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Description("Create one monitored item. call Publish(). Write a status code to the Value attribute (don�t change the value of the Value attribute). call Publish(). Write the existing value and")]
        [Test]
        public async Task WriteStatusCodeAndValueWithMonitoredItemPublishesNotificationsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                    {
                        StatusCode = StatusCodes.Good,
                        ServerTimestamp = DateTime.UtcNow,
                        SourceTimestamp = DateTime.UtcNow
                    }
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Description("Create one monitored item with a filter of StatusValueTimestamp.")]
        [Test]
        public async Task WriteVqtWithStatusValueTimestampFilterPublishesNotificationsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                    {
                        StatusCode = StatusCodes.Good,
                        ServerTimestamp = DateTime.UtcNow,
                        SourceTimestamp = DateTime.UtcNow
                    }
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
        }

        [Description("CreateMonitoredItems: filter=VQT; no deadband. Write a VQT. Call Publish. */")]
        [Test]
        public async Task WriteVqtWithVqtFilterPublishesNotificationsAsync()
        {
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new() {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(42))
                    {
                        StatusCode = StatusCodes.Good,
                        ServerTimestamp = DateTime.UtcNow,
                        SourceTimestamp = DateTime.UtcNow
                    }
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
        }
    }
}
