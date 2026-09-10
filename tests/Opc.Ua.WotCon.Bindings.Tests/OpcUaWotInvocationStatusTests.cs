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

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Bindings.OpcUa;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    [TestFixture]
    public sealed class OpcUaWotInvocationStatusTests
    {
        [Test]
        public async Task ExplicitCallReceiverWinsOverUnrelatedLocalPlacement()
        {
            var namespaces = new NamespaceTable();
            ushort sourceNamespace = namespaces.GetIndexOrAppend("urn:source");
            namespaces.Append("urn:local");
            var session = new Mock<ISession>();
            session.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            session.Setup(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, _) =>
                {
                    Assert.That(requests.Count, Is.EqualTo(1));
                    Assert.That(requests[0].ObjectId, Is.EqualTo(new NodeId("Owner", sourceNamespace)));
                    Assert.That(requests[0].MethodId, Is.EqualTo(new NodeId("Run", sourceNamespace)));
                    Assert.That(requests[0].InputArguments.IsEmpty, Is.True);
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                    });
                });
            WotCompiledForm form = CreateForm(ImmutableDictionary<string, string>.Empty
                .Add("callObjectId", "nsu=urn:source;s=Owner")
                .Add("componentOf", "nsu=urn:local;s=Projection"));
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, form, new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [TestCase("")]
        [TestCase("not-a-node-id")]
        [TestCase("nsu=urn:absent;s=Owner")]
        [TestCase("ns=1;s=Owner")]
        [TestCase("svr=1;i=1")]
        [TestCase("i=0")]
        public async Task InvalidExplicitCallReceiverCannotFallBackToLegacyPlacement(string receiver)
        {
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            session.Setup(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                });
            WotCompiledForm form = CreateForm(ImmutableDictionary<string, string>.Empty
                .Add("callObjectId", receiver)
                .Add("componentOf", "nsu=urn:source;s=Owner"));
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, form, new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(result.Error, Does.Contain("uav:callObjectId"));
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task RejectedInvocationPreservesEveryArgumentResultAndResolvesSourceDiagnostics()
        {
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(namespaces);
            session.Setup(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["urn:source:diagnostics", "SpeedRejected", "de", "Drehzahl zu hoch", "CallRejected"]
                    },
                    DiagnosticInfos = [new DiagnosticInfo { NamespaceUri = 0, SymbolicId = 4 }],
                    Results =
                    [
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.BadInvalidArgument,
                            InputArgumentResults = [StatusCodes.Good, StatusCodes.BadOutOfRange],
                            InputArgumentDiagnosticInfos =
                            [
                                null!,
                                new DiagnosticInfo
                                {
                                    NamespaceUri = 0,
                                    SymbolicId = 1,
                                    Locale = 2,
                                    LocalizedText = 3,
                                    AdditionalInfo = "limit: 100",
                                    InnerStatusCode = StatusCodes.BadInvalidArgument,
                                    InnerDiagnosticInfo = new DiagnosticInfo { SymbolicId = 4 }
                                }
                            ]
                        }
                    ]
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, CreateForm(), new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(1), new Variant(200)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(result.InputArgumentResults.Count, Is.EqualTo(2));
            Assert.That(result.InputArgumentResults[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            ServiceResult rejected = result.InputArgumentResults[1];
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(rejected.NamespaceUri, Is.EqualTo("urn:source:diagnostics"));
            Assert.That(rejected.SymbolicId, Is.EqualTo("SpeedRejected"));
            Assert.That(rejected.LocalizedText, Is.EqualTo(new LocalizedText("de", "Drehzahl zu hoch")));
            Assert.That(rejected.AdditionalInfo, Is.EqualTo("limit: 100"));
            Assert.That(rejected.InnerResult?.SymbolicId, Is.EqualTo("CallRejected"));
            Assert.That(result.OperationResult.SymbolicId, Is.EqualTo("CallRejected"));
            Assert.That(result.OperationResult.NamespaceUri, Is.EqualTo("urn:source:diagnostics"));
            Assert.That(result.Outputs, Is.Empty);
            session.Verify(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [TestCase("short-inputs")]
        [TestCase("long-inputs")]
        [TestCase("short-diagnostics")]
        [TestCase("invalid-index")]
        [TestCase("negative-index")]
        [TestCase("invalid-operation-index")]
        [TestCase("successful-inputs")]
        public async Task MalformedCallDetailsFailWithoutRetryingOrReturningPartialResults(string malformed)
        {
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:source");
            var method = new CallMethodResult
            {
                StatusCode = StatusCodes.BadInvalidArgument,
                InputArgumentResults = [StatusCodes.Good, StatusCodes.BadOutOfRange]
            };
            switch (malformed)
            {
                case "short-inputs":
                    method.InputArgumentResults = [StatusCodes.BadOutOfRange];
                    break;
                case "long-inputs":
                    method.InputArgumentResults = [StatusCodes.Good, StatusCodes.BadOutOfRange, StatusCodes.Good];
                    break;
                case "short-diagnostics":
                    method.InputArgumentDiagnosticInfos = [new DiagnosticInfo()];
                    break;
                case "invalid-index":
                    method.InputArgumentDiagnosticInfos = [null!, new DiagnosticInfo { SymbolicId = 2 }];
                    break;
                case "negative-index":
                    method.InputArgumentDiagnosticInfos = [null!, new DiagnosticInfo { LocalizedText = -2 }];
                    break;
                case "successful-inputs":
                    method.StatusCode = StatusCodes.Good;
                    break;
            }
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(namespaces);
            session.Setup(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    ResponseHeader = new ResponseHeader { StringTable = ["available"] },
                    DiagnosticInfos = malformed == "invalid-operation-index"
                        ? [new DiagnosticInfo { NamespaceUri = 1 }] : [],
                    Results = [method]
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, CreateForm(), new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(1), new Variant(200)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(result.InputArgumentResults.IsEmpty, Is.True);
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            session.Verify(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public async Task InvocationDiagnosticDepthIsBoundedWithoutDiscardingValidNestedText(int depth)
        {
            var diagnostic = new DiagnosticInfo { SymbolicId = 0 };
            for (int i = 0; i < depth; i++)
            {
                diagnostic = new DiagnosticInfo { SymbolicId = 0, InnerDiagnosticInfo = diagnostic };
            }
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            session.Setup(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    ResponseHeader = new ResponseHeader { StringTable = ["ValidationContext"] },
                    Results =
                    [
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.BadInvalidArgument,
                            InputArgumentResults = [StatusCodes.BadTypeMismatch],
                            InputArgumentDiagnosticInfos = [diagnostic]
                        }
                    ]
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, CreateForm(), new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(1)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(depth < 5
                ? StatusCodes.BadInvalidArgument : StatusCodes.BadDecodingError));
            Assert.That(result.Outputs, Is.Empty);
            if (depth < 5)
            {
                ServiceResult? nested = result.InputArgumentResults[0];
                for (int i = 0; i <= depth; i++)
                {
                    Assert.That(nested, Is.Not.Null);
                    Assert.That(nested!.SymbolicId, Is.EqualTo("ValidationContext"));
                    nested = nested.InnerResult;
                }
                Assert.That(nested, Is.Null);
            }
            else
            {
                Assert.That(result.InputArgumentResults.IsEmpty, Is.True);
            }
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [TestCaseSource(nameof(s_statusCases))]
        public async Task InvocationRetainsTheOperationStatusAndOrderedArguments(StatusCode status)
        {
            var namespaces = new NamespaceTable();
            ushort sourceIndex = namespaces.GetIndexOrAppend("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(namespaces);
            session.Setup(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, _) =>
                {
                    Assert.That(requests.Count, Is.EqualTo(1));
                    Assert.That(requests[0].ObjectId, Is.EqualTo(new NodeId("Owner", sourceIndex)));
                    Assert.That(requests[0].MethodId, Is.EqualTo(new NodeId("Run", sourceIndex)));
                    Assert.That(requests[0].InputArguments.Count, Is.EqualTo(1));
                    Assert.That(requests[0].InputArguments[0].TryGetValue(out int input) && input == 31, Is.True);
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new CallMethodResult
                            {
                                StatusCode = status,
                                OutputArguments = [new Variant(32), new Variant("finished")]
                            }
                        ]
                    });
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, CreateForm(), new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable channelOwner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(31)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(status));
            if (StatusCode.IsBad(status))
            {
                Assert.That(result.Outputs, Is.Empty);
            }
            else
            {
                Assert.That(result.Outputs, Has.Count.EqualTo(2));
                Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out int number) && number == 32, Is.True);
                Assert.That(result.Outputs[1].WrappedValue.TryGetValue(out string text) && text == "finished",
                    Is.True);
            }
        }

        [TestCase(DiagnosticsMasks.None)]
        [TestCase(DiagnosticsMasks.OperationAll)]
        [TestCase(DiagnosticsMasks.All | DiagnosticsMasks.UserPermissionAdditionalInfo)]
        public async Task ContextualCallForwardsRequestedDiagnosticsWithoutLocalPermission(DiagnosticsMasks mask)
        {
            var context = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            context.NamespaceUris.Append("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(value => value.NamespaceUris).Returns(context.NamespaceUris);
            session.SetupGet(value => value.ServerUris).Returns(context.ServerUris);
            session.SetupGet(value => value.Factory).Returns(context.Factory);
            session.Setup(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((header, _, _) =>
                {
                    Assert.That(header?.ReturnDiagnostics ?? 0, Is.EqualTo((uint)(mask & DiagnosticsMasks.All)));
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                    });
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, CreateForm(), new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync(new WotInvokeRequest([], context, mask))
                .ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ContextualInvocationTranslatesNodeIdsAndQualifiedNamesByUri()
        {
            var caller = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            caller.NamespaceUris.Append("urn:local-only");
            ushort local = caller.NamespaceUris.GetIndexOrAppend("urn:source");
            var namespaces = new NamespaceTable();
            ushort remote = namespaces.GetIndexOrAppend("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(namespaces);
            session.SetupGet(s => s.ServerUris).Returns(new StringTable());
            session.SetupGet(s => s.Factory).Returns(caller.Factory);
            session.Setup(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, _) =>
                {
                    Assert.That(requests[0].InputArguments[0].TryGetValue(out NodeId node), Is.True);
                    Assert.That(node, Is.EqualTo(new NodeId("Input", remote)));
                    Assert.That(requests[0].InputArguments[1].TryGetValue(out QualifiedName name), Is.True);
                    Assert.That(name, Is.EqualTo(new QualifiedName("Mode", remote)));
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new CallMethodResult
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments =
                                [
                                    new Variant(new NodeId("Output", remote)),
                                    new Variant(new QualifiedName("Result", remote))
                                ]
                            }
                        ]
                    });
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, CreateForm(), new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync(new WotInvokeRequest(
                [new Variant(new NodeId("Input", local)), new Variant(new QualifiedName("Mode", local))], caller))
                .ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Context, Is.Not.Null);
            Variant output = WotBindingValueMapper.Translate(result.Outputs[0].WrappedValue, result.Context!, caller);
            Variant qualified = WotBindingValueMapper.Translate(
                result.Outputs[1].WrappedValue, result.Context!, caller);
            Assert.That(output.TryGetValue(out NodeId outputId), Is.True);
            Assert.That(outputId, Is.EqualTo(new NodeId("Output", local)));
            Assert.That(qualified.TryGetValue(out QualifiedName outputName), Is.True);
            Assert.That(outputName, Is.EqualTo(new QualifiedName("Result", local)));
            Assert.That(namespaces.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task MissingRemoteNamespaceFailsBeforeCallingAndDoesNotInventASessionIndex()
        {
            var caller = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            ushort local = caller.NamespaceUris.GetIndexOrAppend("urn:absent-remotely");
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(namespaces);
            session.SetupGet(s => s.ServerUris).Returns(new StringTable());
            session.SetupGet(s => s.Factory).Returns(caller.Factory);
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, CreateForm(), new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync(new WotInvokeRequest(
                [new Variant(new NodeId("Unknown", local))], caller)).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(namespaces.Count, Is.EqualTo(2));
            session.Verify(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private static WotCompiledForm CreateForm(ImmutableDictionary<string, string>? addressing = null)
        {
            return new WotCompiledForm(
                new WotBindingIdentity("opc.opcua", "1", "urn:test"), WotAffordanceKind.Action,
                "run", "/actions/run/forms/0", WoTBindingCapabilityEnum.InvokeAction, "invokeaction",
                new WotEndpointDescriptor("opc.tcp", "source", 4840, "opc.tcp://source:4840"),
                new WotAddressingDescriptor("nsu=urn:source;s=Run",
                    addressing ??
                    ImmutableDictionary<string, string>.Empty.Add("componentOf", "nsu=urn:source;s=Owner")),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.InvokeAction, "invokeaction", "Call"),
                new WotPayloadDescriptor("application/octet-stream", "binary"), [], true);
        }

        private static readonly TestCaseData[] s_statusCases =
        [
            new TestCaseData(StatusCodes.Good),
            new TestCaseData(StatusCodes.Uncertain),
            new TestCaseData(StatusCodes.BadUserAccessDenied)
        ];
    }
}
