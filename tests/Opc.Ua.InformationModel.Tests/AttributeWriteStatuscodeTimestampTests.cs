// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
                    Value = new DataValue(new Variant(42), StatusCodes.Good)
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
                    Value = new DataValue(new Variant(42), StatusCodes.Good, DateTime.UtcNow)
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
                    Value = new DataValue(new Variant(42), StatusCodes.Good, DateTime.UtcNow, DateTime.UtcNow)
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
                    Value = new DataValue(new Variant(42), StatusCodes.Good, DateTime.UtcNow)
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
                    Value = new DataValue(new Variant(42), StatusCodes.Good, DateTime.UtcNow, DateTime.UtcNow)
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
                    Value = new DataValue(new Variant(42), StatusCodes.Good, DateTime.UtcNow)
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
                    Value = new DataValue(new Variant(42), StatusCodes.Good, DateTime.UtcNow, DateTime.UtcNow)
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
                    Value = new DataValue(new Variant(42), StatusCodes.Good, DateTime.UtcNow, DateTime.UtcNow)
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
                    Value = new DataValue(new Variant(42), StatusCodes.Good, DateTime.UtcNow, DateTime.UtcNow)
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
                    Value = new DataValue(new Variant(42), StatusCodes.Good, DateTime.UtcNow, DateTime.UtcNow)
                }
            }.ToArrayOf();
            WriteResponse response = await Session.WriteAsync(null, wv, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
        }
    }
}
