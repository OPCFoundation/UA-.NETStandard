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
            await using var channelOwner = channel.ConfigureAwait(false);

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

        [Test]
        public async Task ContextualInvocationTranslatesNodeIdsAndQualifiedNamesByUri()
        {
            ServiceMessageContext caller = ServiceMessageContext.CreateEmpty(
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
            await using var owner = channel.ConfigureAwait(false);

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
            ServiceMessageContext caller = ServiceMessageContext.CreateEmpty(
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
            await using var owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync(new WotInvokeRequest(
                [new Variant(new NodeId("Unknown", local))], caller)).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(namespaces.Count, Is.EqualTo(2));
            session.Verify(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private static WotCompiledForm CreateForm()
        {
            return new WotCompiledForm(
                new WotBindingIdentity("opc.opcua", "1", "urn:test"), WotAffordanceKind.Action,
                "run", "/actions/run/forms/0", WoTBindingCapabilityEnum.InvokeAction, "invokeaction",
                new WotEndpointDescriptor("opc.tcp", "source", 4840, "opc.tcp://source:4840"),
                new WotAddressingDescriptor("nsu=urn:source;s=Run",
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
