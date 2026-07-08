/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.DataSets
{
    /// <summary>
    /// Validates the TargetVariables sink: positional + field-id
    /// resolution, override handling delegation, last-good caching
    /// and write-failure isolation.
    /// </summary>
    [TestFixture]
    [TestSpec("6.2.10", Summary = "TargetVariablesSink resolution + writes")]
    public class TargetVariablesSinkTests
    {
        [Test]
        [TestSpec("6.2.10")]
        public async Task WriteAsync_AppliesValuesPositionallyAsync()
        {
            var writer = new RecordingWriter();
            var config = new TargetVariablesDataType
            {
                TargetVariables =
                [
                    new FieldTargetDataType
                    {
                        TargetNodeId = new NodeId("a", 1),
                        AttributeId = Attributes.Value
                    },
                    new FieldTargetDataType
                    {
                        TargetNodeId = new NodeId("b", 1),
                        AttributeId = Attributes.Value
                    }
                ]
            };
            var sink = new TargetVariablesSink(config, writer);
            var fields = new List<DataSetField>
            {
                new() { Name = "field0", Value = new Variant(1.0) },
                new() { Name = "field1", Value = new Variant(2.0) }
            };
            await sink.WriteAsync(fields).ConfigureAwait(false);

            Assert.That(writer.Writes, Has.Count.EqualTo(2));
            Assert.That(writer.Writes[0].NodeId, Is.EqualTo(new NodeId("a", 1)));
            Assert.That(writer.Writes[0].Value.WrappedValue, Is.EqualTo(new Variant(1.0)));
            Assert.That(writer.Writes[1].NodeId, Is.EqualTo(new NodeId("b", 1)));
            Assert.That(writer.Writes[1].Value.WrappedValue, Is.EqualTo(new Variant(2.0)));
        }

        [Test]
        [TestSpec("6.2.10")]
        public async Task WriteAsync_HonoursOverrideValueHandlingAsync()
        {
            var writer = new RecordingWriter();
            var config = new TargetVariablesDataType
            {
                TargetVariables =
                [
                    new FieldTargetDataType
                    {
                        TargetNodeId = new NodeId("override", 1),
                        AttributeId = Attributes.Value,
                        OverrideValueHandling = OverrideValueHandling.OverrideValue,
                        OverrideValue = new Variant(99.0)
                    }
                ]
            };
            var sink = new TargetVariablesSink(config, writer);
            await sink.WriteAsync([
                new DataSetField
                {
                    Name = "f",
                    Value = new Variant(1.0),
                    StatusCode = (StatusCode)StatusCodes.BadInternalError
                }
            ]).ConfigureAwait(false);

            Assert.That(writer.Writes, Has.Count.EqualTo(1));
            Assert.That(writer.Writes[0].Value.WrappedValue, Is.EqualTo(new Variant(99.0)));
        }

        [Test]
        [TestSpec("6.2.10")]
        public async Task WriteAsync_CachesLastGoodForLastUsableHandlingAsync()
        {
            var writer = new RecordingWriter();
            var config = new TargetVariablesDataType
            {
                TargetVariables =
                [
                    new FieldTargetDataType
                    {
                        TargetNodeId = new NodeId("last", 1),
                        AttributeId = Attributes.Value,
                        OverrideValueHandling = OverrideValueHandling.LastUsableValue
                    }
                ]
            };
            var sink = new TargetVariablesSink(config, writer);
            await sink.WriteAsync([
                new DataSetField { Name = "f", Value = new Variant(11.0) }
            ]).ConfigureAwait(false);
            await sink.WriteAsync([
                new DataSetField
                {
                    Name = "f",
                    Value = new Variant(22.0),
                    StatusCode = (StatusCode)StatusCodes.BadInternalError
                }
            ]).ConfigureAwait(false);

            Assert.That(writer.Writes, Has.Count.EqualTo(2));
            Assert.That(writer.Writes[1].Value.WrappedValue,
                Is.EqualTo(new Variant(11.0)),
                "Bad inbound must reuse last-good (11.0) under LastUsableValue.");
        }

        [Test]
        [TestSpec("6.2.10")]
        public async Task WriteAsync_BadWrite_DoesNotPoisonLastGoodAsync()
        {
            var writer = new RecordingWriter
            {
                NextStatus = (StatusCode)StatusCodes.BadInternalError
            };
            var config = new TargetVariablesDataType
            {
                TargetVariables =
                [
                    new FieldTargetDataType
                    {
                        TargetNodeId = new NodeId("x", 1),
                        AttributeId = Attributes.Value,
                        OverrideValueHandling = OverrideValueHandling.LastUsableValue
                    }
                ]
            };
            var sink = new TargetVariablesSink(config, writer);
            await sink.WriteAsync([
                new DataSetField { Name = "f", Value = new Variant(1.0) }
            ]).ConfigureAwait(false);
            writer.NextStatus = (StatusCode)StatusCodes.Good;
            await sink.WriteAsync([
                new DataSetField
                {
                    Name = "f",
                    Value = new Variant(2.0),
                    StatusCode = (StatusCode)StatusCodes.BadInternalError
                }
            ]).ConfigureAwait(false);

            // First write failed → last-good empty → second write must use override (null)
            // → no write recorded for the second sample (resolver returned null).
            Assert.That(writer.Writes, Has.Count.EqualTo(1));
        }

        private sealed class RecordingWriter : ITargetVariableWriter
        {
            public List<(NodeId NodeId, uint AttributeId, DataValue Value)> Writes { get; }
                = [];
            public StatusCode NextStatus { get; set; } = (StatusCode)StatusCodes.Good;

            public ValueTask<StatusCode> WriteAsync(
                NodeId nodeId,
                uint attributeId,
                string? writeIndexRange,
                DataValue value,
                CancellationToken cancellationToken = default)
            {
                Writes.Add((nodeId, attributeId, value));
                return new ValueTask<StatusCode>(NextStatus);
            }
        }
    }
}
