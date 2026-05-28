/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Client;
using Opc.Ua.Di.Client;

#nullable enable

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Tests for <see cref="StateMachineClient"/>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("StateMachine")]
    public sealed class StateMachineClientTests
    {
        private static readonly NodeId kSmId = new NodeId("SM", 2);

        private static Mock<ISession> CreateSessionMock()
        {
            var mock = new Mock<ISession>();
            mock.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            return mock;
        }

        private static ITelemetryContext NullTelemetry()
        {
            return new Mock<ITelemetryContext>().Object;
        }

        [Test]
        public void ConstructorRejectsNullSession()
        {
            Assert.Throws<ArgumentNullException>(
                () => new StateMachineClient(null!, kSmId, NullTelemetry()));
        }

        [Test]
        public void ConstructorRejectsNullStateMachineId()
        {
            Mock<ISession> s = CreateSessionMock();
            Assert.Throws<ArgumentException>(
                () => new StateMachineClient(s.Object, NodeId.Null, NullTelemetry()));
        }

        [Test]
        public void ConstructorRejectsNullTelemetry()
        {
            Mock<ISession> s = CreateSessionMock();
            Assert.Throws<ArgumentNullException>(
                () => new StateMachineClient(s.Object, kSmId, null!));
        }

        [Test]
        public async Task ReadCurrentStateAsyncReturnsNumericId()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            NodeId currentStateIdNode = new NodeId("SM_CurrentState_Id", 2);

            sessionMock
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = new BrowsePathResult[]
                    {
                        new BrowsePathResult
                        {
                            StatusCode = StatusCodes.Good,
                            Targets = new BrowsePathTarget[]
                            {
                                new BrowsePathTarget
                                {
                                    TargetId = new ExpandedNodeId(currentStateIdNode)
                                }
                            }.ToArrayOf()
                        }
                    }.ToArrayOf()
                });

            sessionMock
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = new DataValue[]
                    {
                        new DataValue(new Variant(new NodeId(42u, 0)))
                    }.ToArrayOf()
                });

            var client = new StateMachineClient(
                sessionMock.Object, kSmId, NullTelemetry());

            uint stateId = await client.ReadCurrentStateAsync();
            Assert.That(stateId, Is.EqualTo(42u));
        }

        [Test]
        public void ReadCurrentStateAsyncThrowsWhenBrowsePathFails()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            sessionMock
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = new BrowsePathResult[]
                    {
                        new BrowsePathResult
                        {
                            StatusCode = StatusCodes.BadNotFound,
                            Targets = global::Opc.Ua.ArrayOf.Empty<BrowsePathTarget>()
                        }
                    }.ToArrayOf()
                });

            var client = new StateMachineClient(
                sessionMock.Object, kSmId, NullTelemetry());

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadCurrentStateAsync());
        }

        [Test]
        public async Task InvokeCauseAsyncCallsMethod()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            NodeId methodId = new NodeId("StartMethod", 2);
            CallMethodRequest? captured = null;

            sessionMock
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader?, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, reqs, _) =>
                    {
                        if (reqs.Count > 0) { captured = reqs[0]; }
                    })
                .ReturnsAsync(new CallResponse
                {
                    Results = new CallMethodResult[]
                    {
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments = global::Opc.Ua.ArrayOf.Empty<Variant>()
                        }
                    }.ToArrayOf()
                });

            var client = new StateMachineClient(
                sessionMock.Object, kSmId, NullTelemetry());

            await client.InvokeCauseAsync(methodId);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.ObjectId, Is.EqualTo(kSmId));
            Assert.That(captured.MethodId, Is.EqualTo(methodId));
        }

        [Test]
        public void InvokeCauseAsyncRejectsNullMethodId()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            var client = new StateMachineClient(
                sessionMock.Object, kSmId, NullTelemetry());

            Assert.ThrowsAsync<ArgumentException>(
                async () => await client.InvokeCauseAsync(NodeId.Null));
        }

        [Test]
        public void InvokeCauseAsyncThrowsOnBadStatus()
        {
            Mock<ISession> sessionMock = CreateSessionMock();
            sessionMock
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results = new CallMethodResult[]
                    {
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.BadUserAccessDenied,
                            OutputArguments = global::Opc.Ua.ArrayOf.Empty<Variant>()
                        }
                    }.ToArrayOf()
                });

            var client = new StateMachineClient(
                sessionMock.Object, kSmId, NullTelemetry());

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.InvokeCauseAsync(
                    new NodeId("Method", 2)));
        }
    }
}
