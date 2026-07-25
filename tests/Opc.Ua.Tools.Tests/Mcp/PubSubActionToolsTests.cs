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

#if NET10_0
using System;
using System.Globalization;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PubSubActionToolsTests
    {
        [Test]
        public void ParseFieldsSupportsJsonAndNameValueSyntax()
        {
            ArrayOf<DataSetField> empty = PubSubActionTools.ParseFields(null);
            ArrayOf<DataSetField> json = PubSubActionTools.ParseFields(
                "{\"Count\":{\"value\":5,\"dataType\":\"Int32\"},\"Flag\":true}");
            ArrayOf<DataSetField> text = PubSubActionTools.ParseFields(
                "b:Boolean=true;i8:SByte=-8;u8:Byte=8;" +
                "i16:Int16=-16;u16:UInt16=16;i32:Int32=-32;u32:UInt32=32;" +
                "i64:Int64=-64;u64:UInt64=64;f:Float=1.25;d:Double=2.5;" +
                "dt:DateTime=2026-01-01T00:00:00Z;s=value");

            Assert.That(empty, Is.Empty);
            Assert.That(json, Has.Count.EqualTo(2));
            Assert.That(json[0].Value.TryGetValue(out int count), Is.True);
            Assert.That(count, Is.EqualTo(5));
            Assert.That(text, Has.Count.EqualTo(13));
            Assert.That(text[0].Value.TryGetValue(out bool flag), Is.True);
            Assert.That(flag, Is.True);
            Assert.That(text[^1].Value.TryGetValue(out string? value), Is.True);
            Assert.That(value, Is.EqualTo("value"));
        }

        [Test]
        public void ParseFieldsRejectsInvalidInput()
        {
            Assert.That(
                () => PubSubActionTools.ParseFields("[1,2]"),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => PubSubActionTools.ParseFields("missing-value"),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void SummarizeMapsResponseAndOutputFields()
        {
            var response = new PubSubActionResponse
            {
                Target = new PubSubActionTarget
                {
                    ConnectionName = "connection",
                    DataSetWriterId = 1,
                    ActionTargetId = 2,
                    ActionName = "action"
                },
                RequestId = 3,
                CorrelationData = new ByteString(new byte[] { 1, 2, 3 }),
                StatusCode = StatusCodes.BadTimeout,
                ActionState = ActionState.Done,
                OutputFields =
                [
                    new DataSetField
                    {
                        Name = "Result",
                        Value = new Variant(42),
                        StatusCode = StatusCodes.Good
                    },
                    new DataSetField { Name = "Null", Value = Variant.Null }
                ]
            };

            PubSubActionResponseSummary summary =
                PubSubActionTools.Summarize(response);

            Assert.That(summary.ConnectionName, Is.EqualTo("connection"));
            Assert.That(summary.CorrelationData, Is.EqualTo("AQID"));
            Assert.That(summary.StatusCode, Is.EqualTo("BadTimeout"));
            Assert.That(summary.OutputFields, Has.Count.EqualTo(2));
            Assert.That(summary.OutputFields[0].BuiltInType, Is.EqualTo("Int32"));
            Assert.That(summary.OutputFields[1].BuiltInType, Is.Empty);
        }

        [Test]
        public void FieldConversionHelpersPreserveAndTransformValues()
        {
            ArrayOf<DataSetField> fields =
            [
                new DataSetField
                {
                    Name = "Input",
                    Value = new Variant(42),
                    StatusCode = StatusCodes.Good,
                    SourceTimestamp = new DateTimeUtc(
                        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                }
            ];

            ArrayOf<DataSetField> copied = PubSubActionTools.CopyFields(fields);
            ArrayOf<Variant> inputs = PubSubActionTools.CreateInputArguments(fields);
            ArrayOf<DataSetField> outputs =
                PubSubActionTools.CreateOutputFields([new Variant("result")]);
            ArrayOf<PubSubActionFieldValue> summary =
                PubSubActionTools.SummarizeFields(fields);

            Assert.That(copied, Has.Count.EqualTo(1));
            Assert.That(copied[0].Name, Is.EqualTo("Input"));
            Assert.That(inputs, Has.Count.EqualTo(1));
            Assert.That(inputs[0].TryGetValue(out int input), Is.True);
            Assert.That(input, Is.EqualTo(42));
            Assert.That(outputs[0].Name, Is.EqualTo("output0"));
            Assert.That(summary[0].BuiltInType, Is.EqualTo("Int32"));
            Assert.That(PubSubActionTools.CopyFields(default), Is.Empty);
            Assert.That(PubSubActionTools.CreateInputArguments(default), Is.Empty);
            Assert.That(PubSubActionTools.SummarizeFields(default), Is.Empty);
        }

        [Test]
        public async Task ActionToolsRequireRuntimeAndValidateArgumentsAsync()
        {
            await using PubSubRuntimeManager manager =
                PubSubMcpTestHelpers.NewManager();

            Assert.That(
                () => PubSubActionTools.InvokeActionAsync(
                    manager,
                    1,
                    inputFields: "Value:Int32=1",
                    timeoutMs: 0),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => PubSubActionTools.RegisterActionResponderAsync(
                    manager,
                    1,
                    2),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => PubSubActionTools.BindActionMethodAsync(
                    manager,
                    McpTestEnvironment.SessionManager,
                    1,
                    2,
                    string.Empty,
                    "i=1"),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => PubSubActionTools.ListActionTargetsAsync(null!),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => PubSubActionTools.ListActionRespondersAsync(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task ActiveRuntimeRegistersEchoAndMethodRespondersAsync()
        {
            await using PubSubRuntimeManager manager =
                PubSubMcpTestHelpers.NewManager();
            int port = PubSubMcpTestHelpers.ReserveEphemeralLoopbackPort();
            await manager.StartPublisherAsync(
                $"opc.udp://127.0.0.1:{port}",
                1,
                2,
                "Value:Int32").ConfigureAwait(false);

            PubSubActionResponderRegistration echo =
                await PubSubActionTools.RegisterActionResponderAsync(
                    manager,
                    1,
                    10,
                    "echo",
                    "connection").ConfigureAwait(false);
            PubSubActionResponderRegistration method =
                await PubSubActionTools.BindActionMethodAsync(
                    manager,
                    McpTestEnvironment.SessionManager,
                    1,
                    11,
                    ObjectIds.Server.ToString(null, CultureInfo.InvariantCulture),
                    MethodIds.Server_GetMonitoredItems.ToString(
                        null,
                        CultureInfo.InvariantCulture),
                    sessionName: McpTestEnvironment.SessionName)
                    .ConfigureAwait(false);
            ArrayOf<PubSubActionTargetInfo> targets =
                await PubSubActionTools.ListActionTargetsAsync(manager)
                    .ConfigureAwait(false);
            ArrayOf<PubSubActionResponderRegistration> responders =
                await PubSubActionTools.ListActionRespondersAsync(manager)
                    .ConfigureAwait(false);

            Assert.That(echo.ResponderKind, Is.EqualTo("echo"));
            Assert.That(method.ResponderKind, Is.EqualTo("method"));
            Assert.That(targets.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(responders, Has.Count.EqualTo(2));
        }
    }
}
#endif
