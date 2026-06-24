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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Adapter.Actions;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Adapter.Tests.Unit
{
    /// <summary>
    /// Unit tests for <see cref="ExternalServerActionHandler"/>: input/output
    /// field mapping, unmapped-target and fault handling.
    /// </summary>
    [TestFixture]
    public sealed class ExternalServerActionHandlerTests
    {
        private const ushort WriterId = 5;
        private const ushort TargetId = 9;

        [Test]
        public void ConstructorNullSessionThrows()
        {
            Assert.That(
                () => new ExternalServerActionHandler(
                    null!, new ExternalActionMethodMap(), AdapterTestHelpers.Telemetry()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("session"));
        }

        [Test]
        public void ConstructorNullMethodMapThrows()
        {
            var session = new Mock<IExternalServerSession>().Object;
            Assert.That(
                () => new ExternalServerActionHandler(
                    session, null!, AdapterTestHelpers.Telemetry()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("methodMap"));
        }

        [Test]
        public void HandleAsyncNullInvocationThrows()
        {
            Mock<IExternalServerSession> session = AdapterTestHelpers.ConnectedSession();
            var handler = new ExternalServerActionHandler(
                session.Object, new ExternalActionMethodMap(), AdapterTestHelpers.Telemetry());

            Assert.That(
                async () => await handler.HandleAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task HandleAsyncUnmappedTargetReturnsBadNodeIdUnknownAsync()
        {
            Mock<IExternalServerSession> session = AdapterTestHelpers.ConnectedSession();
            var handler = new ExternalServerActionHandler(
                session.Object, new ExternalActionMethodMap(), AdapterTestHelpers.Telemetry());

            PubSubActionHandlerResult result = await handler
                .HandleAsync(new PubSubActionInvocation
                {
                    Target = new PubSubActionTarget
                    {
                        DataSetWriterId = WriterId,
                        ActionTargetId = TargetId
                    }
                })
                .ConfigureAwait(false);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            session.Verify(
                s => s.CallAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(),
                    It.IsAny<ArrayOf<Variant>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task HandleAsyncMapsInputsCallsSessionAndMapsNamedOutputsAsync()
        {
            Mock<IExternalServerSession> session = AdapterTestHelpers.ConnectedSession();
            var objectId = new NodeId(100u);
            var methodId = new NodeId(101u);
            ArrayOf<Variant> capturedArgs = default;
            session
                .Setup(s => s.CallAsync(
                    objectId, methodId,
                    It.IsAny<ArrayOf<Variant>>(), It.IsAny<CancellationToken>()))
                .Callback<NodeId, NodeId, ArrayOf<Variant>, CancellationToken>(
                    (_, _, args, _) => capturedArgs = args)
                .Returns(new ValueTask<ExternalCallResult>(new ExternalCallResult(
                    (StatusCode)StatusCodes.Good,
                    new[] { new Variant(3.5f) }.ToArrayOf())));

            string[] outputNames = ["Sum"];
            var map = new ExternalActionMethodMap()
                .Add(WriterId, TargetId, objectId, methodId, outputNames.ToArrayOf());
            var handler = new ExternalServerActionHandler(
                session.Object, map, AdapterTestHelpers.Telemetry());

            PubSubActionHandlerResult result = await handler
                .HandleAsync(new PubSubActionInvocation
                {
                    Target = new PubSubActionTarget
                    {
                        DataSetWriterId = WriterId,
                        ActionTargetId = TargetId
                    },
                    InputFields = new[]
                    {
                        new DataSetField { Name = "a", Value = new Variant(1.5f) },
                        new DataSetField { Name = "b", Value = new Variant((uint)2) }
                    }.ToArrayOf()
                })
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(capturedArgs.Count, Is.EqualTo(2));
            Assert.That(capturedArgs[0], Is.EqualTo(new Variant(1.5f)));
            Assert.That(capturedArgs[1], Is.EqualTo(new Variant((uint)2)));
            Assert.That(result.OutputFields.Count, Is.EqualTo(1));
            Assert.That(result.OutputFields[0].Name, Is.EqualTo("Sum"));
            Assert.That(result.OutputFields[0].Value, Is.EqualTo(new Variant(3.5f)));
        }

        [Test]
        public async Task HandleAsyncOutputsUseGeneratedNamesWhenUnnamedAsync()
        {
            Mock<IExternalServerSession> session = AdapterTestHelpers.ConnectedSession();
            var objectId = new NodeId(1u);
            var methodId = new NodeId(2u);
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(),
                    It.IsAny<ArrayOf<Variant>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ExternalCallResult>(new ExternalCallResult(
                    (StatusCode)StatusCodes.Good,
                    new[] { new Variant(10), new Variant(20) }.ToArrayOf())));

            var map = new ExternalActionMethodMap().Add(WriterId, TargetId, objectId, methodId);
            var handler = new ExternalServerActionHandler(
                session.Object, map, AdapterTestHelpers.Telemetry());

            PubSubActionHandlerResult result = await handler
                .HandleAsync(new PubSubActionInvocation
                {
                    Target = new PubSubActionTarget
                    {
                        DataSetWriterId = WriterId,
                        ActionTargetId = TargetId
                    }
                })
                .ConfigureAwait(false);

            Assert.That(result.OutputFields.Count, Is.EqualTo(2));
            Assert.That(result.OutputFields[0].Name, Is.EqualTo("Output0"));
            Assert.That(result.OutputFields[1].Name, Is.EqualTo("Output1"));
        }

        [Test]
        public async Task HandleAsyncResolvesByActionNameWhenPairMissingAsync()
        {
            Mock<IExternalServerSession> session = AdapterTestHelpers.ConnectedSession();
            var objectId = new NodeId(1u);
            var methodId = new NodeId(2u);
            session
                .Setup(s => s.CallAsync(
                    objectId, methodId,
                    It.IsAny<ArrayOf<Variant>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ExternalCallResult>(new ExternalCallResult(
                    (StatusCode)StatusCodes.Good, ArrayOf<Variant>.Empty)));

            var map = new ExternalActionMethodMap().Add("Reset", objectId, methodId);
            var handler = new ExternalServerActionHandler(
                session.Object, map, AdapterTestHelpers.Telemetry());

            PubSubActionHandlerResult result = await handler
                .HandleAsync(new PubSubActionInvocation
                {
                    Target = new PubSubActionTarget { ActionName = "Reset" }
                })
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            session.Verify(
                s => s.CallAsync(
                    objectId, methodId,
                    It.IsAny<ArrayOf<Variant>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task HandleAsyncCallFaultReturnsBadUnexpectedErrorAsync()
        {
            Mock<IExternalServerSession> session = AdapterTestHelpers.ConnectedSession();
            var objectId = new NodeId(1u);
            var methodId = new NodeId(2u);
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(),
                    It.IsAny<ArrayOf<Variant>>(), It.IsAny<CancellationToken>()))
                .Throws(ServiceResultException.Create(StatusCodes.BadMethodInvalid, "x"));

            var map = new ExternalActionMethodMap().Add(WriterId, TargetId, objectId, methodId);
            var handler = new ExternalServerActionHandler(
                session.Object, map, AdapterTestHelpers.Telemetry());

            PubSubActionHandlerResult result = await handler
                .HandleAsync(new PubSubActionInvocation
                {
                    Target = new PubSubActionTarget
                    {
                        DataSetWriterId = WriterId,
                        ActionTargetId = TargetId
                    }
                })
                .ConfigureAwait(false);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        public async Task HandleAsyncPropagatesServerStatusOnFailedCallAsync()
        {
            Mock<IExternalServerSession> session = AdapterTestHelpers.ConnectedSession();
            var objectId = new NodeId(1u);
            var methodId = new NodeId(2u);
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(),
                    It.IsAny<ArrayOf<Variant>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ExternalCallResult>(new ExternalCallResult(
                    (StatusCode)StatusCodes.BadArgumentsMissing, ArrayOf<Variant>.Empty)));

            var map = new ExternalActionMethodMap().Add(WriterId, TargetId, objectId, methodId);
            var handler = new ExternalServerActionHandler(
                session.Object, map, AdapterTestHelpers.Telemetry());

            PubSubActionHandlerResult result = await handler
                .HandleAsync(new PubSubActionInvocation
                {
                    Target = new PubSubActionTarget
                    {
                        DataSetWriterId = WriterId,
                        ActionTargetId = TargetId
                    }
                })
                .ConfigureAwait(false);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadArgumentsMissing));
        }
    }
}
