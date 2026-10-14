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
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.Tools.Tests.McpCompanion;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Runs shipped adapters through actual application codecs and exact borrowed-session scopes.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionAdapterOperationTests
    {
        [Test]
        public async Task BoundSinkWritesDecodedTypedValuesAndReportsActualPartialResultsAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: Options("write")).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            NodeId countNode = fixture.Remote.Id("count");
            NodeId flagNode = fixture.Remote.Id("flag");
            var device = new ConcurrentDictionary<NodeId, DataValue>();
            device[countNode] = new DataValue(Variant.From(7));
            device[flagNode] = new DataValue(Variant.From(true));
            var attempted = new ConcurrentQueue<(WriteValue Write, StatusCode Status)>();
            var batchSizes = new ConcurrentQueue<int>();
            var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Remote.Session.Setup(session => session.WriteAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<WriteValue> values, CancellationToken _) =>
                {
                    ArrayOf<StatusCode> results = values.ConvertAll(write =>
                    {
                        StatusCode result = write.NodeId == flagNode ? StatusCodes.BadNotWritable : StatusCodes.Good;
                        if (StatusCode.IsGood(result))
                        {
                            device[write.NodeId] = write.Value;
                        }
                        attempted.Enqueue((write, result));
                        return result;
                    });
                    batchSizes.Enqueue(values.Count);
                    if (attempted.Count >= 2)
                    {
                        written.TrySetResult();
                    }
                    return ValueTask.FromResult(new WriteResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = results
                    });
                });
            PubSubApplicationInput input = PubSubExpansionTestContext.Input(mode: "Subscriber");
            string handle = (await fixture.Runtimes.CreateAsync(input, lifetime.Token).ConfigureAwait(false))
                ["handle"]!.GetValue<string>();
            CallToolResult bound = await fixture.Services.GetRequiredService<PubSubAdapterTools>()
                .BindSinkAsync(handle, Variables(fixture), Session, lifetime.Token).ConfigureAwait(false);
            Assert.That(bound.IsError, Is.False, RemoteModelTestSession.Payload(bound).GetRawText());
            Assert.That(fixture.Transports.Factories, Is.Zero);
            _ = await fixture.Runtimes.StartAsync(handle, lifetime.Token).ConfigureAwait(false);
            var timestamp = DateTimeUtc.From(fixture.Clock.GetUtcNow().AddSeconds(-2));
            await fixture.Transports.Created.Single().InjectAsync(await PubSubExpansionFrames.EncodeAsync(
                PubSubExpansionFrames.Data(input,
                [
                    new DataSetField { Name = "Count", Value = Variant.From(31), SourceTimestamp = timestamp },
                    new DataSetField { Name = "Enabled", Value = Variant.From(false), SourceTimestamp = timestamp }
                ], dataValues: true), fixture, lifetime.Token).ConfigureAwait(false), lifetime.Token)
                .ConfigureAwait(false);

            await written.Task.WaitAsync(lifetime.Token).ConfigureAwait(false);
            (WriteValue Write, StatusCode Status)[] outcomes = [.. attempted];
            WriteValue[] writes = Array.ConvertAll(outcomes, outcome => outcome.Write);

            Assert.That(writes, Has.Length.EqualTo(2));
            Assert.That(batchSizes.ToArray(), Is.EqualTo(s_singleItemWrites),
                "The shipped TargetVariables writer dispatches one standard Write request per field.");
            Assert.That(writes[0].NodeId, Is.EqualTo(countNode));
            Assert.That(writes[1].NodeId, Is.EqualTo(flagNode));
            Assert.That(writes[0].AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(writes[0].Value.WrappedValue.TryGetValue(out int count), Is.True);
            Assert.That(count, Is.EqualTo(31));
            Assert.That(writes[1].Value.WrappedValue.TryGetValue(out bool flag), Is.True);
            Assert.That(flag, Is.False);
            Assert.That(writes[0].Value.SourceTimestamp, Is.EqualTo(timestamp));
            Assert.That(outcomes[0].Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(outcomes[1].Status, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(device[countNode].WrappedValue.TryGetValue(out int applied), Is.True);
            Assert.That(applied, Is.EqualTo(31));
            Assert.That(device[flagNode].WrappedValue.TryGetValue(out bool unchanged), Is.True);
            Assert.That(unchanged, Is.True, "A refused second field must not be represented as atomically applied.");
            Assert.That(device, Has.Count.EqualTo(2));
            JsonObject state;
            do
            {
                state = await fixture.Runtimes.StatusAsync(handle, lifetime.Token).ConfigureAwait(false);
                if (state["bindings"]![0]!["writes"]!.GetValue<long>() < 2)
                {
                    await Task.Delay(1, lifetime.Token).ConfigureAwait(false);
                }
            }
            while (state["bindings"]![0]!["writes"]!.GetValue<long>() < 2);
            Assert.That(state["bindings"]![0]!["writes"]!.GetValue<long>(), Is.EqualTo(2));
            Assert.That(state["bindings"]![0]!["lastWriteResults"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(state["bindings"]![0]!["lastWriteResults"]![0]!.GetValue<uint>(),
                Is.EqualTo(StatusCodes.BadNotWritable.Code));
            JsonObject buffer = await fixture.Runtimes.ReadAsync(handle, ct: lifetime.Token).ConfigureAwait(false);
            Assert.That(buffer["externalSink"]!.GetValue<bool>(), Is.True);
            Assert.That(buffer["dataSets"]!.AsArray(), Is.Empty,
                "The external sink is not a second buffered subscriber.");
            _ = await fixture.Runtimes.RemoveAsync(handle, lifetime.Token).ConfigureAwait(false);
            Assert.That(fixture.Remote.Session.Object.Connected, Is.True);
        }

        [Test]
        public async Task BoundSourceSamplesActualRemoteValuesAndCannotBeOverwrittenByManualPublishAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: Options("read")).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            fixture.Remote.Values[(fixture.Remote.Id("count"), Attributes.Value)] = new DataValue(Variant.From(29));
            fixture.Remote.Values[(fixture.Remote.Id("flag"), Attributes.Value)] = new DataValue(Variant.From(true));
            string handle = (await fixture.Runtimes.CreateAsync(
                PubSubExpansionTestContext.Input(), lifetime.Token).ConfigureAwait(false))
                ["handle"]!.GetValue<string>();
            CallToolResult bound = await fixture.Services.GetRequiredService<PubSubAdapterTools>()
                .BindSourceAsync(handle, Variables(fixture), Session, lifetime.Token).ConfigureAwait(false);
            Assert.That(bound.IsError, Is.False);
            _ = await fixture.Runtimes.StartAsync(handle, lifetime.Token).ConfigureAwait(false);
            PubSubExpansionTransport transport = fixture.Transports.Created.Single();
            while (!transport.Sent.Any(bytes => PubSubExpansionFrames.Decode(bytes, fixture) is UadpNetworkMessage))
            {
                fixture.Clock.Advance(TimeSpan.FromMilliseconds(50));
                await Task.Delay(1, lifetime.Token).ConfigureAwait(false);
            }
            UadpNetworkMessage message = transport.Sent.Select(bytes => PubSubExpansionFrames.Decode(bytes, fixture))
                .OfType<UadpNetworkMessage>().First();

            Assert.That(message.DataSetMessages[0].Fields[0].Value.TryGetValue(out int count), Is.True);
            Assert.That(count, Is.EqualTo(29));
            Assert.That(message.DataSetMessages[0].Fields[1].Value.TryGetValue(out bool flag), Is.True);
            Assert.That(flag, Is.True);
            Assert.That(fixture.Remote.Reads.All(read => read.AttributeId == Attributes.Value), Is.True);
            Assert.That(fixture.Remote.Reads.Select(read => read.NodeId),
                Does.Contain(fixture.Remote.Id("count")).And.Contain(fixture.Remote.Id("flag")));
            ServiceResultException? refusal = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Runtimes.PublishAsync(handle, new PubSubPublishInput
                {
                    Fields =
                    [new PubSubFieldValueInput { Name = "Count", Value = PubSubExpansionTestContext.Element("0") }]
                }, lifetime.Token).ConfigureAwait(false));
            Assert.That(refusal!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            _ = await fixture.Runtimes.RemoveAsync(handle, lifetime.Token).ConfigureAwait(false);
            Assert.That(transport.DisposeCalls, Is.EqualTo(1));
            Assert.That(fixture.Remote.Session.Object.Connected, Is.True);
        }

        [Test]
        public async Task BoundActionInvokesOnlyTheDeclaredMethodAndReturnsItsTypedResultAsync()
        {
            McpPubSubOptions options = Options("call") with { AllowSecurityDowngrade = true };
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: options).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            PubSubApplicationInput input = PubSubExpansionTestContext.Input(mode: "Subscriber");
            string handle = (await fixture.Runtimes.CreateAsync(input, lifetime.Token).ConfigureAwait(false))
                ["handle"]!.GetValue<string>();
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(14));
            CallToolResult bound = await fixture.Services.GetRequiredService<PubSubAdapterTools>().BindActionAsync(
                handle, new PubSubActionBindingInput
                {
                    ObjectId = fixture.Remote.Id("device").ToString(),
                    MethodId = fixture.Remote.Id("execute").ToString(),
                    ActionTargetId = 9,
                    OutputFieldNames = ["result"],
                    AllowUnsecured = true
                }, Session, lifetime.Token).ConfigureAwait(false);
            Assert.That(bound.IsError, Is.False, RemoteModelTestSession.Payload(bound).GetRawText());
            Assert.That(fixture.Remote.Calls, Is.Empty);
            _ = await fixture.Runtimes.StartAsync(handle, lifetime.Token).ConfigureAwait(false);
            var request = new UadpActionRequestMessage
            {
                PublisherId = PublisherId.FromUInt16(42),
                WriterGroupId = input.WriterGroupId,
                DataSetWriterId = input.DataSetWriterId,
                ActionTargetId = 9,
                RequestId = 71,
                RequestorId = Variant.From((ushort)99),
                CorrelationData = ByteString.From(Convert.FromHexString("A1B2C3")),
                Payload =
                [
                    new DataSetField { Name = "Count", Value = Variant.From(7) },
                    new DataSetField { Name = "Enabled", Value = Variant.From(false) }
                ]
            };
            PubSubExpansionTransport transport = fixture.Transports.Created.Single();
            await transport.InjectAsync(await PubSubExpansionFrames.EncodeAsync(request, fixture, lifetime.Token)
                .ConfigureAwait(false), lifetime.Token).ConfigureAwait(false);
            await PubSubExpansionFrames.UntilAsync(() => transport.Sent.Any(bytes =>
                PubSubExpansionFrames.Decode(bytes, fixture) is UadpActionResponseMessage), lifetime.Token)
                .ConfigureAwait(false);

            UadpActionResponseMessage response = transport.Sent
                .Select(bytes => PubSubExpansionFrames.Decode(bytes, fixture))
                .OfType<UadpActionResponseMessage>().Single();
            CallMethodRequest call = fixture.Remote.Calls.Single();
            Assert.That(call.ObjectId, Is.EqualTo(fixture.Remote.Id("device")));
            Assert.That(call.MethodId, Is.EqualTo(fixture.Remote.Id("execute")));
            Assert.That(call.InputArguments[0].TryGetValue(out int count), Is.True);
            Assert.That(count, Is.EqualTo(7));
            Assert.That(call.InputArguments[1].TryGetValue(out bool flag), Is.True);
            Assert.That(flag, Is.False);
            Assert.That(response.RequestId, Is.EqualTo(71));
            Assert.That(response.ActionTargetId, Is.EqualTo(9));
            Assert.That(response.ActionState, Is.EqualTo(ActionState.Done));
            Assert.That(response.CorrelationData, Is.EqualTo(request.CorrelationData));
            Assert.That(response.Payload, Has.Count.EqualTo(1));
            Assert.That(response.Payload[0].Value.TryGetValue(out int result), Is.True);
            Assert.That(result, Is.EqualTo(14));
            JsonObject state = await fixture.Runtimes.StatusAsync(handle, lifetime.Token).ConfigureAwait(false);
            Assert.That(state["bindings"]![0]!["calls"]!.GetValue<long>(), Is.EqualTo(1));
            Assert.That(state["bindings"]![0]!["lastCallStatus"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.Good.Code));
            _ = await fixture.Runtimes.RemoveAsync(handle, lifetime.Token).ConfigureAwait(false);
            Assert.That(fixture.Remote.Session.Object.Connected, Is.True);
        }

        [TestCase("wrong-mode")]
        [TestCase("running")]
        [TestCase("duplicate")]
        [TestCase("wrong-order")]
        [TestCase("attribute")]
        public async Task RejectedBindingPreservesTheExistingRuntimeAndBindingAsync(string invalid)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: Options("write")).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            PubSubApplicationInput input = PubSubExpansionTestContext.Input(
                mode: invalid == "wrong-mode" ? "Publisher" : "Subscriber");
            string handle = (await fixture.Runtimes.CreateAsync(input).ConfigureAwait(false))
                ["handle"]!.GetValue<string>();
            PubSubAdapterTools tools = fixture.Services.GetRequiredService<PubSubAdapterTools>();
            if (invalid == "running")
            {
                _ = await fixture.Runtimes.StartAsync(handle).ConfigureAwait(false);
            }
            if (invalid == "duplicate")
            {
                CallToolResult first = await tools.BindSinkAsync(handle, Variables(fixture), Session)
                    .ConfigureAwait(false);
                Assert.That(first.IsError,
                    Is.False);
            }
            PubSubVariablesInput variables = Variables(fixture);
            if (invalid == "wrong-order")
            {
                variables = variables with { Variables = [variables.Variables[1], variables.Variables[0]] };
            }
            if (invalid == "attribute")
            {
                variables = variables with
                {
                    Variables = [variables.Variables[0] with { AttributeId = Attributes.DisplayName },
                        variables.Variables[1]]
                };
            }

            CallToolResult rejected = await tools.BindSinkAsync(handle, variables, Session).ConfigureAwait(false);

            if (invalid is "wrong-order" or "attribute")
            {
                RemoteModelTestSession.ArgumentError(rejected);
            }
            else
            {
                RemoteModelTestSession.Error(rejected, StatusCodes.BadInvalidState);
            }
            JsonObject state = await fixture.Runtimes.StatusAsync(handle).ConfigureAwait(false);
            Assert.That(state["bindingCount"]!.GetValue<int>(), Is.EqualTo(invalid == "duplicate" ? 1 : 0));
            Assert.That(state["isRunning"]!.GetValue<bool>(), Is.EqualTo(invalid == "running"));
            Assert.That(fixture.Remote.Calls, Is.Empty);
            _ = await fixture.Runtimes.RemoveAsync(handle).ConfigureAwait(false);
            Assert.That(fixture.Remote.Session.Object.Connected, Is.True);
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public async Task UnsecuredConsentAndExactMethodScopeAreBothRequiredForActionBindingAsync(
            bool hostConsent, bool correctMethod)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: Options("call") with { AllowSecurityDowngrade = hostConsent }).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            string handle = (await fixture.Runtimes.CreateAsync(PubSubExpansionTestContext.Input())
                .ConfigureAwait(false))["handle"]!.GetValue<string>();

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubAdapterTools>().BindActionAsync(
                handle, new PubSubActionBindingInput
                {
                    ObjectId = fixture.Remote.Id("device").ToString(),
                    MethodId = fixture.Remote.Id(correctMethod ? "execute" : "other").ToString(),
                    ActionTargetId = 9,
                    AllowUnsecured = true
                }, Session).ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That((await fixture.Runtimes.StatusAsync(handle).ConfigureAwait(false))
                ["bindingCount"]!.GetValue<int>(), Is.Zero);
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }

        private static PubSubVariablesInput Variables(PubSubExpansionTestContext fixture)
        {
            return new PubSubVariablesInput
            {
                Variables =
                [
                    new PubSubVariableInput { Name = "Count", NodeId = fixture.Remote.Id("count").ToString() },
                    new PubSubVariableInput { Name = "Enabled", NodeId = fixture.Remote.Id("flag").ToString() }
                ]
            };
        }

        private static McpPubSubOptions Options(string operation)
        {
            return new McpPubSubOptions
            {
                AdapterRules = operation == "call"
                    ?
                    [
                        new McpPubSubAdapterRule
                        {
                            SessionName = Session, Endpoint = "opc.tcp://unused.invalid:4840", Operation = "call",
                            NodeId = "nsu=urn:test:remote-model;s=device",
                            MethodId = "nsu=urn:test:remote-model;s=execute"
                        }
                    ]
                    : new ArrayOf<McpPubSubAdapterRule>(s_nodeNames.Select(name => new McpPubSubAdapterRule
                    {
                        SessionName = Session,
                        Endpoint = "opc.tcp://unused.invalid:4840",
                        Operation = operation,
                        NodeId = "nsu=urn:test:remote-model;s=" + name
                    }).ToArray())
            };
        }

        private const string Session = PubSubExpansionTestContext.SessionName;
        private static readonly string[] s_nodeNames = ["count", "flag"];
        private static readonly int[] s_singleItemWrites = [1, 1];
    }
}
#endif
