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

using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    [TestFixture]
    public sealed class WotConditionInvocationTests
    {
        [TestCase("Acknowledge", false)]
        [TestCase("Acknowledge", true)]
        [TestCase("Confirm", false)]
        [TestCase("Confirm", true)]
        [TestCase("AddComment", false)]
        [TestCase("AddComment", true)]
        public async Task OmittedOptionalCommentIsSentAsTheSecondNullLocalizedText(
            string action, bool contextual)
        {
            WotCompiledForm form = Compile(action, commentRequired: false);
            Assert.That(form.ConditionInvocation, Is.Not.Null);
            Assert.That(form.ConditionInvocation!.CommentOptional, Is.True);
            form = form.WithExecutable(false).WithExecutable(true)
                .WithTargetMapping(new WotTargetMappingDescriptor(targetNodeId: null));
            Assert.That(form.ConditionInvocation!.CommentOptional, Is.True);
            ByteString eventId = new(new byte[] { 1, 2, 3, 4 });
            ServiceMessageContext context = Context();
            var session = new Mock<ISession>();
            session.SetupGet(value => value.NamespaceUris).Returns(context.NamespaceUris);
            session.SetupGet(value => value.ServerUris).Returns(context.ServerUris);
            session.SetupGet(value => value.Factory).Returns(context.Factory);
            session.Setup(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, _) =>
                {
                    Assert.That(requests.Count, Is.EqualTo(1));
                    Assert.That(requests[0].MethodId, Is.EqualTo(MethodId(action)));
                    Assert.That(requests[0].InputArguments.Count, Is.EqualTo(2));
                    Assert.That(requests[0].InputArguments[0].TryGetValue(out ByteString actual), Is.True);
                    Assert.That(actual, Is.EqualTo(eventId));
                    Assert.That(requests[0].InputArguments[1].TypeInfo.BuiltInType,
                        Is.EqualTo(BuiltInType.LocalizedText));
                    Assert.That(requests[0].InputArguments[1].TryGetValue(out LocalizedText comment), Is.True);
                    Assert.That(comment.IsNull, Is.True);
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                    });
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, form, new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using var owner = channel.ConfigureAwait(false);

            WotInvokeResult result = contextual
                ? await channel.InvokeAsync(new WotInvokeRequest([new Variant(eventId)], context))
                    .ConfigureAwait(false)
                : await channel.InvokeAsync([new Variant(eventId)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [TestCase("Acknowledge", true)]
        [TestCase("Ordinary", false)]
        public async Task RequiredCommentsAndOrdinaryActionsAreNotDefaulted(string action, bool required)
        {
            WotCompiledForm form = Compile(action, required);
            ServiceMessageContext context = Context();
            var session = new Mock<ISession>();
            session.SetupGet(value => value.NamespaceUris).Returns(context.NamespaceUris);
            session.Setup(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, _) =>
                {
                    Assert.That(requests[0].InputArguments.Count, Is.EqualTo(1));
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.BadArgumentsMissing }]
                    });
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, form, new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using var owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync(
                [new Variant(new ByteString(new byte[] { 1 }))]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadArgumentsMissing));
        }

        [Test]
        public void SuppliedCommentAndMalformedArgumentCountsArePreserved()
        {
            WotCompiledForm form = Compile("Acknowledge", commentRequired: false);
            WotConditionInvocation invocation = form.ConditionInvocation!;
            var comment = new LocalizedText("de-DE", "Operator comment");
            ArrayOf<Variant> supplied = [new Variant(new ByteString(new byte[] { 1 })), new Variant(comment)];

            ArrayOf<Variant> normalized = invocation.NormalizeInputs(supplied);

            Assert.That(normalized.Count, Is.EqualTo(2));
            Assert.That(normalized[1].TryGetValue(out LocalizedText actual), Is.True);
            Assert.That(actual, Is.EqualTo(comment));
            Assert.That(invocation.NormalizeInputs([]).Count, Is.Zero);
            Assert.That(invocation.NormalizeInputs(supplied.AddItem(new Variant(3))).Count, Is.EqualTo(3));
        }

        private static ServiceMessageContext Context()
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            context.NamespaceUris.Append("urn:source");
            return context;
        }

        private static WotCompiledForm Compile(string action, bool commentRequired)
        {
            string annotation = action == "Ordinary"
                ? string.Empty : "\"uav:conditionAction\":\"" + action + "\",";
            string required = commentRequired ? """["EventId","Comment"]""" : """["EventId"]""";
            string methodId = MethodId(action).ToString();
            string document = $$"""
                {
                  "securityDefinitions": { "none": { "scheme": "nosec" } },
                  "security": ["none"],
                  "actions": {
                    "call": {
                      {{annotation}}
                      "input": {
                        "type": "object",
                        "uav:fieldOrder": ["EventId","Comment"],
                        "required": {{required}},
                        "properties": {
                          "EventId": {
                            "type": "string", "contentEncoding": "base64",
                            "uav:mapToType": "i=15", "uav:valueRank": -1
                          },
                          "Comment": { "type": "string", "uav:mapToType": "i=21", "uav:valueRank": -1 }
                        }
                      },
                      "forms": [{
                        "href": "opc.tcp://source:4840", "op": "invokeaction",
                        "contentType": "application/octet-stream",
                        "uav:id": "{{methodId}}", "uav:componentOf": "nsu=urn:source;s=Alarm"
                      }]
                    }
                  }
                }
                """;
            WotBindingPlanRequest request = WotBindingPlanRequest.FromDocument(
                "resource", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(document));
            WotBindingCompilation compilation = new OpcUaBindingPlanner().Compile(
                request.Forms.Single(),
                request.CreateContext(WotPayloadCodecRegistry.Default, WotBindingBounds.Default));
            Assert.That(compilation.HasErrors, Is.False);
            return compilation.Entries.Single();
        }

        private static NodeId MethodId(string action)
        {
            return action switch
            {
                "Confirm" => Ua.MethodIds.AcknowledgeableConditionType_Confirm,
                "AddComment" => Ua.MethodIds.ConditionType_AddComment,
                _ => Ua.MethodIds.AcknowledgeableConditionType_Acknowledge
            };
        }
    }
}
