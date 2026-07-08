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
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Adapter.Subscriber;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ServerSubscribedDataSetSink"/>: argument
    /// validation and that the produced sink writes to the external session.
    /// </summary>
    [TestFixture]
    public sealed class ServerSubscribedDataSetSinkTests
    {
        private static TargetVariablesDataType TargetVariables(NodeId nodeId)
        {
            return new TargetVariablesDataType
            {
                TargetVariables =
                [
                    new FieldTargetDataType
                    {
                        TargetNodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                ]
            };
        }

        [Test]
        public void CreateNullConfigurationThrows()
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();

            Assert.That(
                () => ServerSubscribedDataSetSink.Create(
                    null!, session.Object, AdapterTestHelpers.Telemetry()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configuration"));
        }

        [Test]
        public void CreateNullSessionThrows()
        {
            Assert.That(
                () => ServerSubscribedDataSetSink.Create(
                    new TargetVariablesDataType(), null!, AdapterTestHelpers.Telemetry()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("session"));
        }

        [Test]
        public void CreateNullTelemetryThrows()
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();

            Assert.That(
                () => ServerSubscribedDataSetSink.Create(
                    new TargetVariablesDataType(), session.Object, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("telemetry"));
        }

        [Test]
        public async Task CreatedSinkWritesFieldToSession()
        {
            var nodeId = new NodeId("target", 1);
            WriteValue? captured = null;
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session
                .Setup(s => s.WriteAsync(
                    It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Callback((ArrayOf<WriteValue> writes, CancellationToken ct) =>
                    captured = writes.Count > 0 ? writes[0] : null)
                .Returns(new ValueTask<ArrayOf<StatusCode>>(
                    new[] { (StatusCode)StatusCodes.Good }.ToArrayOf()));

            ISubscribedDataSetSink sink = ServerSubscribedDataSetSink.Create(
                TargetVariables(nodeId), session.Object, AdapterTestHelpers.Telemetry());

            var fields = new List<DataSetField>
            {
                new() { Name = "field0", Value = new Variant(3.14) }
            };
            await sink.WriteAsync(fields).ConfigureAwait(false);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.NodeId, Is.EqualTo(nodeId));
            Assert.That(captured.Value.WrappedValue, Is.EqualTo(new Variant(3.14)));
        }
    }
}
