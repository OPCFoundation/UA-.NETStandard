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

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Server.Tests
{
    /// <summary>
    /// Coverage for PublishedActionMethod server Method binding.
    /// </summary>
    [TestFixture]
    [TestSpec("PubSub Actions", Summary = "PublishedActionMethod server Method binding")]
    public class ServerMethodActionHandlerTests
    {
        [Test]
        public async Task HandleAsync_WithPublishedActionMethod_InvokesServerMethodAndReturnsOutputs()
        {
            NodeId objectId = new("DemoObject", 2);
            NodeId methodId = new("DemoMethod", 2);
            CallMethodRequest? capturedRequest = null;
            OperationContext? capturedContext = null;
            var nodeManager = new Mock<IMasterNodeManager>(MockBehavior.Strict);
            nodeManager
                .Setup(m => m.CallAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<OperationContext, ArrayOf<CallMethodRequest>, CancellationToken>((context, requests, _) =>
                {
                    capturedContext = context;
                    capturedRequest = requests[0];
                })
                .Returns(new ValueTask<(ArrayOf<CallMethodResult>, ArrayOf<DiagnosticInfo>)>((
                    [
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments =
                            [
                                Variant.From(42),
                                Variant.From("done")
                            ]
                        }
                    ],
                    [])));
            var handler = new ServerMethodActionHandler(
                nodeManager.Object,
                new ActionMethodDataType
                {
                    ObjectId = objectId,
                    MethodId = methodId
                },
                NUnitTelemetryContext.Create());

            PubSubActionHandlerResult result = await handler.HandleAsync(new PubSubActionInvocation
            {
                RequestId = 77,
                TimeoutHint = 1_000,
                Target = new PubSubActionTarget
                {
                    DataSetWriterId = 10,
                    ActionTargetId = 1,
                    ActionName = "Demo"
                },
                InputFields =
                [
                    new DataSetField { Name = "A", Value = Variant.From(5) },
                    new DataSetField { Name = "B", Value = Variant.From(7) }
                ]
            }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(result.OutputFields, Has.Count.EqualTo(2));
                Assert.That(result.OutputFields[0].Name, Is.EqualTo("OutputArgument0"));
                Assert.That(result.OutputFields[0].Value.TryGetValue(out int answer), Is.True);
                Assert.That(answer, Is.EqualTo(42));
                Assert.That(result.OutputFields[1].Value.TryGetValue(out string? text), Is.True);
                Assert.That(text, Is.EqualTo("done"));
                Assert.That(capturedContext, Is.Not.Null);
                Assert.That(capturedContext!.RequestType, Is.EqualTo(RequestType.Call));
                Assert.That(capturedContext.ClientHandle, Is.EqualTo(77));
                Assert.That(capturedContext.UserIdentity, Is.Not.Null);
                Assert.That(
                    capturedContext.UserIdentity.TokenType,
                    Is.EqualTo(UserTokenType.Anonymous));
                Assert.That(capturedRequest, Is.Not.Null);
                Assert.That(capturedRequest!.ObjectId, Is.EqualTo(objectId));
                Assert.That(capturedRequest.MethodId, Is.EqualTo(methodId));
                Assert.That(capturedRequest.InputArguments, Has.Count.EqualTo(2));
                Assert.That(capturedRequest.InputArguments[0].TryGetValue(out int a), Is.True);
                Assert.That(a, Is.EqualTo(5));
                Assert.That(capturedRequest.InputArguments[1].TryGetValue(out int b), Is.True);
                Assert.That(b, Is.EqualTo(7));
            });
        }

        [Test]
        public async Task HandleAsync_WithConfiguredServiceIdentity_InvokesMethodUnderThatIdentity()
        {
            OperationContext? capturedContext = null;
            var nodeManager = new Mock<IMasterNodeManager>(MockBehavior.Strict);
            nodeManager
                .Setup(m => m.CallAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<OperationContext, ArrayOf<CallMethodRequest>, CancellationToken>((context, _, _) =>
                {
                    capturedContext = context;
                })
                .Returns(new ValueTask<(ArrayOf<CallMethodResult>, ArrayOf<DiagnosticInfo>)>((
                    [
                        new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = [] }
                    ],
                    [])));
            var serviceIdentity = new UserIdentity("svc", System.Text.Encoding.UTF8.GetBytes("pw"));
            var handler = new ServerMethodActionHandler(
                nodeManager.Object,
                new ActionMethodDataType
                {
                    ObjectId = new NodeId("DemoObject", 2),
                    MethodId = new NodeId("DemoMethod", 2)
                },
                NUnitTelemetryContext.Create(),
                serviceIdentity);

            await handler.HandleAsync(new PubSubActionInvocation
            {
                Target = new PubSubActionTarget { ActionName = "Demo" },
                InputFields = []
            }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(capturedContext, Is.Not.Null);
                Assert.That(capturedContext!.UserIdentity, Is.SameAs(serviceIdentity));
                Assert.That(
                    capturedContext.UserIdentity.TokenType,
                    Is.EqualTo(UserTokenType.UserName));
            });
        }

        [Test]
        public async Task Register_WithPublishedActionMethod_InvokingRegisteredHandlerRunsServerMethod()
        {
            IPubSubActionHandler? registeredHandler = null;
            PubSubActionTarget? registeredTarget = null;
            var application = new Mock<IPubSubApplication>(MockBehavior.Strict);
            application.Setup(a => a.RegisterActionHandler(
                    It.IsAny<PubSubActionTarget>(),
                    It.IsAny<IPubSubActionHandler>(),
                    It.IsAny<bool>(),
                    It.IsAny<PubSubResponseAddressPolicy?>()))
                .Callback<PubSubActionTarget, IPubSubActionHandler, bool, PubSubResponseAddressPolicy?>(
                    (target, handler, _, _) =>
                {
                    registeredTarget = target;
                    registeredHandler = handler;
                });
            var nodeManager = new Mock<IMasterNodeManager>(MockBehavior.Strict);
            nodeManager
                .Setup(m => m.CallAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<(ArrayOf<CallMethodResult>, ArrayOf<DiagnosticInfo>)>((
                    [
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments = [Variant.From("method-output")]
                        }
                    ],
                    [])));
            var action = new PublishedActionMethodDataType
            {
                ActionTargets =
                [
                    new ActionTargetDataType
                    {
                        ActionTargetId = 4,
                        Name = "CallDemo"
                    }
                ],
                ActionMethods =
                [
                    new ActionMethodDataType
                    {
                        ObjectId = new NodeId("DemoObject", 2),
                        MethodId = new NodeId("DemoMethod", 2)
                    }
                ]
            };

            PubSubActionMethodRegistrar.Register(
                application.Object,
                nodeManager.Object,
                new PubSubActionMethodRegistration(22, action, "conn"),
                NUnitTelemetryContext.Create());

            Assert.That(registeredHandler, Is.Not.Null);
            PubSubActionHandlerResult result = await registeredHandler!.HandleAsync(new PubSubActionInvocation
            {
                Target = registeredTarget!,
                InputFields = []
            }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(registeredTarget, Is.Not.Null);
                Assert.That(registeredTarget!.ConnectionName, Is.EqualTo("conn"));
                Assert.That(registeredTarget.DataSetWriterId, Is.EqualTo(22));
                Assert.That(registeredTarget.ActionTargetId, Is.EqualTo(4));
                Assert.That(registeredTarget.ActionName, Is.EqualTo("CallDemo"));
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(result.OutputFields, Has.Count.EqualTo(1));
                Assert.That(result.OutputFields[0].Value.TryGetValue(out string? value), Is.True);
                Assert.That(value, Is.EqualTo("method-output"));
            });
        }

        [Test]
        public async Task Register_WithServiceIdentity_RegisteredHandlerRunsMethodUnderThatIdentity()
        {
            IPubSubActionHandler? registeredHandler = null;
            PubSubActionTarget? registeredTarget = null;
            var application = new Mock<IPubSubApplication>(MockBehavior.Strict);
            application.Setup(a => a.RegisterActionHandler(
                    It.IsAny<PubSubActionTarget>(),
                    It.IsAny<IPubSubActionHandler>(),
                    It.IsAny<bool>(),
                    It.IsAny<PubSubResponseAddressPolicy?>()))
                .Callback<PubSubActionTarget, IPubSubActionHandler, bool, PubSubResponseAddressPolicy?>(
                    (target, handler, _, _) =>
                {
                    registeredTarget = target;
                    registeredHandler = handler;
                });
            OperationContext? capturedContext = null;
            var nodeManager = new Mock<IMasterNodeManager>(MockBehavior.Strict);
            nodeManager
                .Setup(m => m.CallAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<OperationContext, ArrayOf<CallMethodRequest>, CancellationToken>((context, _, _) =>
                {
                    capturedContext = context;
                })
                .Returns(new ValueTask<(ArrayOf<CallMethodResult>, ArrayOf<DiagnosticInfo>)>((
                    [
                        new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = [] }
                    ],
                    [])));
            var action = new PublishedActionMethodDataType
            {
                ActionTargets = [new ActionTargetDataType { ActionTargetId = 4, Name = "CallDemo" }],
                ActionMethods =
                [
                    new ActionMethodDataType
                    {
                        ObjectId = new NodeId("DemoObject", 2),
                        MethodId = new NodeId("DemoMethod", 2)
                    }
                ]
            };
            var serviceIdentity = new UserIdentity("svc", System.Text.Encoding.UTF8.GetBytes("pw"));

            PubSubActionMethodRegistrar.Register(
                application.Object,
                nodeManager.Object,
                new PubSubActionMethodRegistration(22, action, "conn", serviceIdentity),
                NUnitTelemetryContext.Create());

            Assert.That(registeredHandler, Is.Not.Null);
            await registeredHandler!.HandleAsync(new PubSubActionInvocation
            {
                Target = registeredTarget!,
                InputFields = []
            }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(capturedContext, Is.Not.Null);
                Assert.That(capturedContext!.UserIdentity, Is.SameAs(serviceIdentity));
                Assert.That(
                    capturedContext.UserIdentity.TokenType,
                    Is.EqualTo(UserTokenType.UserName));
            });
        }
    }
}
