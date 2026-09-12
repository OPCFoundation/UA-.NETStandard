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

using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    [TestFixture]
    public sealed class OpcUaWotCallContractTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task DirectAndDiCallsHonorTheConfiguredInputContext(bool dependencyInjection)
        {
            const string schema = /*lang=json,strict*/ """
                {
                  "type":"object","uav:argumentLayout":"named","uav:fieldOrder":["Node","Name"],
                  "required":["Node","Name"],
                  "properties":{
                    "Name":{"type":"string","uav:dataTypeId":"i=20"},
                    "Node":{"type":"string","uav:dataTypeId":"i=17"}
                  }
                }
                """;
            using var harness = new CallHarness(schema, schema, dependencyInjection);
            ushort callerIndex = harness.Caller.NamespaceUris.GetIndexOrAppend("urn:call:source");
            ushort sourceIndex = harness.Source.NamespaceUris.GetIndexOrAppend("urn:call:source");
            Assert.That(callerIndex, Is.Not.EqualTo(sourceIndex));
            harness.Outputs =
            [
                new Variant(new NodeId("Output", sourceIndex)),
                new Variant(new QualifiedName("OutputName", sourceIndex))
            ];
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync(
            [
                new Variant(new NodeId("Input", callerIndex)),
                new Variant(new QualifiedName("InputName", callerIndex))
            ]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(harness.Inputs.Count, Is.EqualTo(2));
            Assert.That(harness.Inputs[0], Is.EqualTo(new Variant(new NodeId("Input", sourceIndex))));
            Assert.That(harness.Inputs[1], Is.EqualTo(new Variant(new QualifiedName("InputName", sourceIndex))));
            Assert.That(harness.ObjectId, Is.EqualTo(new NodeId("Owner", sourceIndex)));
            Assert.That(harness.MethodId, Is.EqualTo(new NodeId("Run", sourceIndex)));
            Assert.That(harness.CallCount, Is.EqualTo(1));
            Assert.That(harness.ConnectCount, Is.EqualTo(1));
            Assert.That(result.Outputs, Has.Count.EqualTo(2));
            Assert.That(result.Context, Is.Not.Null);
            Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out NodeId output), Is.True);
            Assert.That(NodeId.ToExpandedNodeId(output, result.Context!.NamespaceUris),
                Is.EqualTo(new ExpandedNodeId("Output", "urn:call:source")));
            Assert.That(result.Outputs[1].WrappedValue.TryGetValue(out QualifiedName name), Is.True);
            Assert.That(name.Name, Is.EqualTo("OutputName"));
            Assert.That(result.Context.NamespaceUris.GetString(name.NamespaceIndex), Is.EqualTo("urn:call:source"));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public async Task ResolvedNativeLayoutsKeepEveryOrderedInputAndOutput(int count)
        {
            string? schema = count switch
            {
                0 => null,
                1 => /*lang=json,strict*/ """
                    {"@context":{"native":"http://opcfoundation.org/UA/"},
                     "type":"integer","uav:dataTypeName":"native:UInt16"}
                    """,
                _ => /*lang=json,strict*/ """
                    {
                      "type":"object","uav:argumentLayout":"named","uav:fieldOrder":["Last","First","Middle"],
                      "properties":{
                        "First":{"type":"integer","uav:dataTypeId":"i=5"},
                        "Middle":{"type":"integer","uav:dataTypeId":"i=5"},
                        "Last":{"type":"integer","uav:dataTypeId":"i=5"}
                      }
                    }
                    """
            };
            using var harness = new CallHarness(schema, schema);
            ArrayOf<Variant> values = count switch
            {
                0 => [],
                1 => [new Variant(ushort.MaxValue)],
                _ => [new Variant((ushort)31), new Variant((ushort)11), new Variant((ushort)21)]
            };
            harness.Outputs = values;
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync(values.Span.ToArray()).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(harness.Inputs, Is.EqualTo(values));
            Assert.That(result.Outputs.Select(value => value.WrappedValue), Is.EqualTo(values));
            Assert.That(harness.CallCount, Is.EqualTo(1));
            Assert.That(channel.Form.Payload.InputLayout!.ArgumentCount, Is.EqualTo(count));
            Assert.That(channel.Form.Payload.OutputLayout!.ArgumentCount, Is.EqualTo(count));
        }

        [TestCase("wrong-type")]
        [TestCase("untyped-null")]
        [TestCase("wrong-rank")]
        [TestCase("missing")]
        [TestCase("extra")]
        public async Task InvalidNativeInputsAreRejectedBeforeCallingTheSource(string invalid)
        {
            const string schema = /*lang=json,strict*/ """
                {"@context":{"native":"http://opcfoundation.org/UA/"},
                 "type":"integer","uav:dataTypeName":"native:UInt16"}
                """;
            using var harness = new CallHarness(schema, null);
            ArrayOf<ushort> array = [7];
            ArrayOf<Variant> inputs = invalid switch
            {
                "wrong-type" => [new Variant(7)],
                "untyped-null" => [Variant.Null],
                "wrong-rank" => [new Variant(array)],
                "missing" => [],
                _ => [new Variant((ushort)7), new Variant((ushort)8)]
            };
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync(inputs.Span.ToArray()).ConfigureAwait(false);

            StatusCode expected = invalid switch
            {
                "missing" => StatusCodes.BadArgumentsMissing,
                "extra" => StatusCodes.BadTooManyArguments,
                _ => StatusCodes.BadTypeMismatch
            };
            Assert.That(result.Status, Is.EqualTo(expected), result.Error);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(harness.CallCount, Is.Zero);
        }

        [Test]
        public void PublicInputPreflightRejectsInvalidValuesWithoutOpeningASession()
        {
            using var harness = new CallHarness(
                /*lang=json,strict*/ """{"type":"integer","uav:dataTypeId":"i=5"}""", null);

            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                harness.Form.Payload.ValidateInputs([new Variant(7)], harness.Caller));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(harness.ConnectCount, Is.Zero);
            Assert.That(harness.CallCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExplicitPayloadLayoutsWorkUnlessTheyConflictWithCapturedFacts(bool conflicting)
        {
            using var harness = new CallHarness(
                /*lang=json,strict*/ """{"type":"integer","uav:dataTypeId":"i=6"}""", null);
            using var other = new CallHarness(/*lang=json,strict*/ """{"type":"boolean"}""", null);
            WotCompiledForm original = harness.Form;
            WotPayloadDescriptor payload = new WotPayloadDescriptor("application/octet-stream", "binary")
                .WithArgumentLayouts(original.Payload.InputLayout!, original.Payload.OutputLayout!);
            if (conflicting)
            {
                payload = payload.WithSchema(other.Form.Payload.Schema!);
            }
            var form = new WotCompiledForm(
                original.Binding, original.AffordanceKind, original.AffordanceName, original.JsonPointer,
                original.Operation, original.OpToken, original.Endpoint, original.Addressing,
                original.OperationInfo, payload, original.Security, original.IsExecutable);
            if (conflicting)
            {
                ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    await using IWotBindingChannel channel = await harness.OpenAsync(form).ConfigureAwait(false);
                });
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(harness.ConnectCount, Is.Zero);
                Assert.That(harness.CallCount, Is.Zero);
                return;
            }
            await using IWotBindingChannel valid = await harness.OpenAsync(form).ConfigureAwait(false);

            WotInvokeResult result = await valid.InvokeAsync([new Variant(7)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(harness.Inputs.Count, Is.EqualTo(1));
            Assert.That(harness.Inputs[0], Is.EqualTo(new Variant(7)));
            Assert.That(harness.CallCount, Is.EqualTo(1));
            Assert.That(harness.ConnectCount, Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(2)]
        public async Task InvalidInputCountsAreRejectedBeforeAnyBrowsePathService(int count)
        {
            var context = ServiceMessageContext.CreateEmpty(TelemetryExtensions.InternalOnly__TelemetryHook());
            context.NamespaceUris.Append("urn:call:source");
            var session = new Mock<ISession>();
            session.SetupGet(value => value.NamespaceUris).Returns(context.NamespaceUris);
            session.SetupGet(value => value.ServerUris).Returns(context.ServerUris);
            session.SetupGet(value => value.Factory).Returns(context.Factory);
            session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new BrowsePathResult
                        {
                            StatusCode = StatusCodes.Good,
                            Targets = [new BrowsePathTarget
                            {
                                TargetId = new ExpandedNodeId("Run", 1), RemainingPathIndex = uint.MaxValue
                            }]
                        }
                    ]
                });
            session.Setup(value => value.ReadAsync(
                It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new DataValue(new Variant((int)NodeClass.Method))]
                });
            var registry = new WotProtocolBinderRegistry(
                [new OpcUaBindingPlanner()],
                [new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    SessionFactory = (_, _) => new ValueTask<ISession>(session.Object), DisposeSession = false
                })]);
            const string json = /*lang=json,strict*/ """
                {
                  "@context":{"source":"urn:call:source"},"title":"Path call",
                  "uav:id":"nsu=urn:call:source;s=Owner",
                  "actions":{"run":{
                    "input":{"type":"integer","uav:dataTypeId":"i=6"},
                    "forms":[{
                      "href":"opc.tcp://call-source.invalid:4840","op":"invokeaction",
                      "uav:browsePath":"source:Run","uav:callObjectId":"nsu=urn:call:source;s=Owner"
                    }]
                  }
                  }
                }
                """;
            WotCompiledForm form = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "path-call", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(json)))
                .CompiledForms.Single();
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(form).ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync(
                count == 0 ? [] : [new Variant(1), new Variant(2)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(count == 0
                ? StatusCodes.BadArgumentsMissing : StatusCodes.BadTooManyArguments), result.Error);
            session.Verify(value => value.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(value => value.ReadAsync(
                It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestCase("missing")]
        [TestCase("extra")]
        [TestCase("wrong-type")]
        [TestCase("wrong-rank")]
        [TestCase("untyped-null")]
        public async Task InvalidNativeOutputsAreRejectedWithoutPartialSuccessOrRetry(string invalid)
        {
            const string schema = /*lang=json,strict*/ """
                {
                  "type":"object","uav:argumentLayout":"named","uav:fieldOrder":["Minimum","Maximum"],
                  "properties":{
                    "Maximum":{"type":"integer","uav:dataTypeId":"i=5"},
                    "Minimum":{"type":"integer","uav:dataTypeId":"i=5"}
                  }
                }
                """;
            using var harness = new CallHarness(null, schema);
            ArrayOf<ushort> array = [2];
            harness.Outputs = invalid switch
            {
                "missing" => [new Variant((ushort)1)],
                "extra" => [new Variant((ushort)1), new Variant((ushort)2), new Variant((ushort)3)],
                "wrong-type" => [new Variant((ushort)1), new Variant(2)],
                "wrong-rank" => [new Variant((ushort)1), new Variant(array)],
                _ => [new Variant((ushort)1), Variant.Null]
            };
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadDecodingError), result.Error);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(harness.CallCount, Is.EqualTo(1));
            Assert.That(harness.ConnectCount, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task JsonOptionalAndDefaultDoNotShortenTheNativeSignature(bool hasDefault)
        {
            string schema = $$"""
                {
                  "type":"object","uav:argumentLayout":"named","uav:fieldOrder":["Value","Note"],
                  "required":["Value"],
                  "properties":{
                    "Value":{"type":"integer","uav:dataTypeId":"i=6"},
                    "Note":{"type":["string","null"],"uav:dataTypeId":"i=12"{{(hasDefault
                        ? ",\"default\":\"not-native-optional-metadata\"" : string.Empty)}}}
                  }
                }
                """;
            using var harness = new CallHarness(schema, null);
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(1)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadArgumentsMissing), result.Error);
            Assert.That(harness.CallCount, Is.Zero);
            Assert.That(result.Outputs, Is.Empty);
        }

        [Test]
        public async Task NativeNullsStayPresentAndDoNotBecomeSchemaDefaults()
        {
            const string schema = /*lang=json,strict*/ """
                {
                  "type":"object","uav:argumentLayout":"named",
                  "uav:fieldOrder":["Text","Comment","Bytes","Node","Array"],
                  "required":["Text","Comment","Bytes","Node","Array"],
                  "properties":{
                    "Text":{"type":["string","null"],"uav:dataTypeId":"i=12","default":"not used"},
                    "Comment":{"type":["string","null"],"uav:dataTypeId":"i=21","default":"not used"},
                    "Bytes":{"type":["string","null"],"uav:dataTypeId":"i=15","default":"AQ=="},
                    "Node":{"type":["string","null"],"uav:dataTypeId":"i=17","default":"i=85"},
                    "Array":{"type":["array","null"],"uav:dataTypeId":"i=6","uav:valueRank":1,"default":[1]}
                  }
                }
                """;
            using var harness = new CallHarness(schema, schema);
            ArrayOf<Variant> values =
            [
                Variant.Null,
                new Variant(LocalizedText.Null),
                new Variant(default(ByteString)),
                new Variant(NodeId.Null),
                new Variant(ArrayOf<int>.Null)
            ];
            harness.Outputs = values;
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync(values.Span.ToArray()).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(harness.CallCount, Is.EqualTo(1));
            Assert.That(harness.Inputs.Count, Is.EqualTo(5));
            Assert.That(result.Outputs, Has.Count.EqualTo(5));
            for (int index = 0; index < values.Count; index++)
            {
                Assert.That(harness.Inputs[index].TypeInfo, Is.EqualTo(values[index].TypeInfo));
                Assert.That(harness.Inputs[index], Is.EqualTo(values[index]));
                Assert.That(result.Outputs[index].WrappedValue.TypeInfo, Is.EqualTo(values[index].TypeInfo));
                Assert.That(result.Outputs[index].WrappedValue, Is.EqualTo(values[index]));
            }
            Assert.That(result.Outputs[4].WrappedValue.TryGetValue(out ArrayOf<int> array), Is.True);
            Assert.That(array.IsNull, Is.True);
        }

        [Test]
        public async Task SuppliedZeroFalseAndEmptyStringDoNotBecomeDefaults()
        {
            const string schema = /*lang=json,strict*/ """
                {
                  "type":"object","uav:argumentLayout":"named","uav:fieldOrder":["Number","Flag","Text"],
                  "properties":{
                    "Number":{"type":"integer","uav:dataTypeId":"i=6","default":9},
                    "Flag":{"type":"boolean","default":true},
                    "Text":{"type":"string","default":"not used"}
                  }
                }
                """;
            using var harness = new CallHarness(schema, schema);
            ArrayOf<Variant> values = [new Variant(0), new Variant(false), new Variant(string.Empty)];
            harness.Outputs = values;
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync(values.Span.ToArray()).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(harness.Inputs, Is.EqualTo(values));
            Assert.That(result.Outputs.Select(value => value.WrappedValue), Is.EqualTo(values));
            Assert.That(harness.CallCount, Is.EqualTo(1));
        }

        [TestCase("Acknowledge", false)]
        [TestCase("Acknowledge", true)]
        [TestCase("Confirm", false)]
        [TestCase("Confirm", true)]
        [TestCase("AddComment", false)]
        [TestCase("AddComment", true)]
        public async Task DirectAndDiConditionCallsInsertTypedNullInsteadOfTheJsonDefault(
            string action, bool dependencyInjection)
        {
            using var harness = new CallHarness(
                s_conditionInput, null, dependencyInjection, conditionAction: action);
            var eventId = new ByteString(new byte[] { 1, 7, 9 });
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(eventId)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(harness.Inputs.Count, Is.EqualTo(2));
            Assert.That(harness.Inputs[0], Is.EqualTo(new Variant(eventId)));
            Assert.That(harness.Inputs[1].TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.LocalizedText));
            Assert.That(harness.Inputs[1].TryGetValue(out LocalizedText comment), Is.True);
            Assert.That(comment.IsNull, Is.True);
            Assert.That(harness.CallCount, Is.EqualTo(1));
            Assert.That(harness.ConnectCount, Is.EqualTo(1));
        }

        [TestCase("event-id")]
        [TestCase("comment")]
        [TestCase("missing-event-id")]
        public async Task InvalidConditionValuesAreRejectedBeforeTheNativeCall(string invalid)
        {
            using var harness = new CallHarness(s_conditionInput, null, conditionAction: "Acknowledge");
            ArrayOf<Variant> values = invalid switch
            {
                "event-id" => [new Variant("not bytes"), new Variant(LocalizedText.Null)],
                "comment" => [new Variant(new ByteString(new byte[] { 1 })), new Variant("not LocalizedText")],
                _ => []
            };
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync(values.Span.ToArray()).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(invalid == "missing-event-id"
                ? StatusCodes.BadArgumentsMissing : StatusCodes.BadTypeMismatch), result.Error);
            Assert.That(harness.CallCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExplicitSingleStructureIsOneNativeArgumentInDirectAndDiCalls(bool dependencyInjection)
        {
            const string schema = /*lang=json,strict*/ """
                {
                  "type":"object","uav:argumentLayout":"single","uav:dataTypeId":"i=296",
                  "properties":{"Name":{"type":"string"},"DataType":{"type":"string","uav:dataTypeId":"i=17"}}
                }
                """;
            using var harness = new CallHarness(schema, schema, dependencyInjection);
            ushort callerIndex = harness.Caller.NamespaceUris.GetIndexOrAppend("urn:call:source");
            ushort sourceIndex = harness.Source.NamespaceUris.GetIndexOrAppend("urn:call:source");
            var input = new Argument
            {
                Name = "Whole value",
                DataType = new NodeId("InputType", callerIndex),
                ValueRank = ValueRanks.Scalar
            };
            var expected = new Variant(new ExtensionObject(input));
            harness.Outputs =
            [
                new Variant(new ExtensionObject(new Argument
                {
                    Name = "Whole value",
                    DataType = new NodeId("OutputType", sourceIndex),
                    ValueRank = ValueRanks.Scalar
                }))
            ];
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([expected]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(harness.Inputs.Count, Is.EqualTo(1));
            Assert.That(harness.Inputs[0].TryGetValue(out ExtensionObject sent), Is.True);
            Assert.That(sent.TryGetValue(out Argument? sentArgument, harness.Source), Is.True);
            Assert.That(sentArgument!.Name, Is.EqualTo("Whole value"));
            Assert.That(sentArgument.DataType, Is.EqualTo(new NodeId("InputType", sourceIndex)));
            Assert.That(result.Outputs, Has.Count.EqualTo(1));
            Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out ExtensionObject returned), Is.True);
            Assert.That(returned.TryGetValue(out Argument? returnedArgument, result.Context), Is.True);
            Assert.That(returnedArgument!.Name, Is.EqualTo("Whole value"));
            Assert.That(returnedArgument.DataType, Is.EqualTo(new NodeId("OutputType", sourceIndex)));
            Variant local = WotBindingValueMapper.Translate(
                result.Outputs[0].WrappedValue, result.Context!, harness.Caller);
            Assert.That(local.TryGetValue(out ExtensionObject localExtension), Is.True);
            Assert.That(localExtension.TryGetValue(out Argument? localArgument, harness.Caller), Is.True);
            Assert.That(localArgument!.DataType, Is.EqualTo(new NodeId("OutputType", callerIndex)));
            Assert.That(harness.CallCount, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DecodedStructureCannotInventASourceOrTargetNamespace(bool missingTarget)
        {
            using var harness = new CallHarness(
                /*lang=json,strict*/ """
                {"type":"object","uav:argumentLayout":"single","uav:dataTypeId":"i=296"}
                """, null);
            ushort index = missingTarget
                ? harness.Caller.NamespaceUris.GetIndexOrAppend("urn:not-at-source") : ushort.MaxValue;
            var argument = new Argument { Name = "Unknown", DataType = new NodeId("Type", index) };
            await using IWotBindingChannel channel = await harness.OpenAsync().ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(new ExtensionObject(argument))])
                .ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNodeIdInvalid), result.Error);
            Assert.That(harness.CallCount, Is.Zero);
            Assert.That(harness.Source.NamespaceUris.GetIndex("urn:not-at-source"), Is.EqualTo(-1));
        }

        private sealed class CallHarness : IDisposable
        {
            public CallHarness(
                string? input, string? output, bool dependencyInjection = false, string? conditionAction = null)
            {
                Caller = ServiceMessageContext.CreateEmpty(TelemetryExtensions.InternalOnly__TelemetryHook());
                Caller.NamespaceUris.Append("urn:call:caller");
                Caller.NamespaceUris.Append("urn:call:source");
                Caller.Factory.Builder.AddOpcUa().Commit();
                Source = ServiceMessageContext.CreateEmpty(TelemetryExtensions.InternalOnly__TelemetryHook());
                Source.NamespaceUris.Append("urn:call:source");
                Source.Factory.Builder.AddOpcUa().Commit();
                var session = new Mock<ISession>();
                session.SetupGet(value => value.NamespaceUris).Returns(Source.NamespaceUris);
                session.SetupGet(value => value.ServerUris).Returns(Source.ServerUris);
                session.SetupGet(value => value.Factory).Returns(Source.Factory);
                session.Setup(value => value.CallAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                    .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, calls, _) =>
                    {
                        CallCount++;
                        Assert.That(calls.Count, Is.EqualTo(1));
                        ObjectId = calls[0].ObjectId;
                        MethodId = calls[0].MethodId;
                        Inputs = calls[0].InputArguments;
                        return new ValueTask<CallResponse>(new CallResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results =
                            [
                                new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = Outputs }
                            ]
                        });
                    });
                var planner = new OpcUaBindingPlanner();
                var executor = new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    SessionFactory = (endpoint, _) =>
                    {
                        ConnectCount++;
                        Assert.That(new Uri(endpoint), Is.EqualTo(new Uri("opc.tcp://call-source.invalid:4840")));
                        return new ValueTask<ISession>(session.Object);
                    },
                    DisposeSession = false
                });
                var services = new ServiceCollection();
                services.AddSingleton<IWotProtocolBinder>(planner);
                services.AddSingleton<IWotBindingExecutor>(executor);
                services.AddSingleton<IServiceMessageContext>(Caller);
                services.EnsureWotBinderRegistry();
                m_services = services.BuildServiceProvider();
                m_registry = dependencyInjection
                    ? m_services.GetRequiredService<WotProtocolBinderRegistry>()
                    : new WotProtocolBinderRegistry([planner], [executor]) { MessageContext = Caller };
                string condition = conditionAction is null
                    ? string.Empty : "\"uav:conditionAction\":\"" + conditionAction + "\",";
                string json = $$"""
                    {
                      "title":"Call contracts",
                      "actions":{"run":{
                        {{condition}}
                        {{(input is null ? string.Empty : "\"input\":" + input + ",")}}
                        {{(output is null ? string.Empty : "\"output\":" + output + ",")}}
                        "forms":[{
                          "href":"opc.tcp://call-source.invalid:4840","op":"invokeaction",
                          "contentType":"application/octet-stream","uav:id":"nsu=urn:call:source;s=Run",
                          "uav:callObjectId":"nsu=urn:call:source;s=Owner",
                          "uav:componentOf":"nsu=urn:local;s=NotTheSourceOwner"
                        }]
                      }
                      }
                    }
                    """;
                WotBindingPlan plan = m_registry.Prepare(WotBindingPlanRequest.FromDocument(
                    "call-contract", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(json)));
                Form = plan.CompiledForms.Single();
            }

            public ServiceMessageContext Caller { get; }

            public ServiceMessageContext Source { get; }

            public WotCompiledForm Form { get; }

            public ArrayOf<Variant> Inputs { get; private set; }

            public ArrayOf<Variant> Outputs { get; set; }

            public NodeId ObjectId { get; private set; }

            public NodeId MethodId { get; private set; }

            public int CallCount { get; private set; }

            public int ConnectCount { get; private set; }

            public ValueTask<IWotBindingChannel> OpenAsync(WotCompiledForm? form = null)
            {
                return m_registry.OpenChannelAsync(form ?? Form);
            }

            public void Dispose()
            {
                m_services.Dispose();
            }

            private readonly ServiceProvider m_services;
            private readonly WotProtocolBinderRegistry m_registry;
        }

        private const string s_conditionInput = /*lang=json,strict*/ """
            {
              "@context":{"native":"http://opcfoundation.org/UA/"},
              "type":"object","uav:argumentLayout":"named","uav:fieldOrder":["EventId","Comment"],
              "required":["EventId"],
              "properties":{
                "EventId":{"type":"string","contentEncoding":"base64","uav:dataTypeName":"native:ByteString"},
                "Comment":{"type":"string","uav:dataTypeName":"native:LocalizedText","default":"not used"}
              }
            }
            """;
    }
}
