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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Checks the real borrowed-session deadline, exact scope, response-shape and generation boundaries.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionBorrowedSessionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task CallsPreserveServerStatusAndOmitOverBudgetOutputsWithoutLeakingThemAsync(bool oversized)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            NodeId node = fixture.Remote.Id("device");
            NodeId method = fixture.Remote.Id("method");
            var options = new McpPubSubOptions { MaxFields = 1 };
            await using var session = new McpPubSubBorrowedSession(fixture.Sessions,
                fixture.Sessions.GetSessionInfoOrThrow(PubSubExpansionTestContext.SessionName),
                "call", [node], options, fixture.Clock, method);
            if (oversized)
            {
                fixture.Remote.Reply(StatusCodes.BadInvalidArgument, Variant.From("private-output"), Variant.From(2));
            }
            else
            {
                fixture.Remote.Reply(StatusCodes.Good, Variant.From(17));
            }

            RemoteCallResult result = await session.CallAsync(node, method, [Variant.From(7)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(oversized ? StatusCodes.BadInvalidArgument : StatusCodes.Good));
            Assert.That(result.OutputArguments.Count, Is.EqualTo(oversized ? 0 : 1));
            if (!oversized)
            {
                Assert.That(result.OutputArguments[0].TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(17));
            }
            Assert.That(session.Status()["calls"]!.GetValue<long>(), Is.EqualTo(1));
            Assert.That(session.Status()["outputsOmitted"]!.GetValue<bool>(), Is.EqualTo(oversized));
            Assert.That(session.Status()["lastCallStatus"]!.GetValue<uint>(), Is.EqualTo(result.Status.Code));
            Assert.That(session.Status().ToJsonString(), Does.Not.Contain("private-output"));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Remote.Calls[0].InputArguments[0].TryGetValue(out int sent), Is.True);
            Assert.That(sent, Is.EqualTo(7));
        }

        [TestCase("read")]
        [TestCase("call")]
        public async Task MalformedResponseCountsAreNotCountedAsSuccessfulOperationsAsync(string operation)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            NodeId node = fixture.Remote.Id("node");
            NodeId method = fixture.Remote.Id("method");
            await using var session = new McpPubSubBorrowedSession(fixture.Sessions,
                fixture.Sessions.GetSessionInfoOrThrow(PubSubExpansionTestContext.SessionName),
                operation, [node], new McpPubSubOptions(), fixture.Clock, method);
            fixture.Remote.Session.Setup(value => value.ReadAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = []
                }));
            fixture.Remote.Session.Setup(value => value.CallAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = []
                }));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (operation == "read")
                {
                    _ = await session.ReadAsync([new ReadValueId { NodeId = node, AttributeId = Attributes.Value }])
                        .ConfigureAwait(false);
                }
                else
                {
                    _ = await session.CallAsync(node, method, []).ConfigureAwait(false);
                }
            });

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(session.Status()[operation == "read" ? "reads" : "calls"]!.GetValue<long>(), Is.Zero);
        }

        [TestCase("session-id")]
        [TestCase("namespace")]
        public async Task ChangedLiveIdentityInvalidatesResolveBeforeAnyRemoteRequestAsync(string changed)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            NodeId node = fixture.Remote.Id("node");
            await using var session = new McpPubSubBorrowedSession(fixture.Sessions,
                fixture.Sessions.GetSessionInfoOrThrow(PubSubExpansionTestContext.SessionName),
                "read", [node], new McpPubSubOptions(), fixture.Clock);
            await session.ConnectAsync().ConfigureAwait(false);
            Assert.That(await session.ResolveNodeIdAsync(node).ConfigureAwait(false), Is.EqualTo(node));
            if (changed == "session-id")
            {
                fixture.Remote.Session.SetupGet(value => value.SessionId).Returns(fixture.Remote.Id("replacement"));
            }
            else
            {
                fixture.Remote.Context.NamespaceUris.Append("urn:changed:namespace");
            }

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await session.ResolveNodeIdAsync(node).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Remote.Operations, Is.Empty);
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
            await session.Invalidated.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(fixture.Remote.Session.Object.Connected, Is.True);
        }

        [Test]
        public async Task RateBudgetRejectsTheSecondReadAndRecoversAtTheNextClockWindowAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            NodeId node = fixture.Remote.Id("value");
            fixture.Remote.Values[(node, Attributes.Value)] = new DataValue(Variant.From(5));
            await using var session = new McpPubSubBorrowedSession(fixture.Sessions,
                fixture.Sessions.GetSessionInfoOrThrow(PubSubExpansionTestContext.SessionName),
                "read", [node], new McpPubSubOptions { MaxMessagesPerSecond = 1 }, fixture.Clock);
            ArrayOf<ReadValueId> request = [new ReadValueId { NodeId = node, AttributeId = Attributes.Value }];

            ArrayOf<DataValue> first = await session.ReadAsync(request).ConfigureAwait(false);
            ServiceResultException? limited = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await session.ReadAsync(request).ConfigureAwait(false));
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
            ArrayOf<DataValue> second = await session.ReadAsync(request).ConfigureAwait(false);

            Assert.That(limited!.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(first[0].WrappedValue.TryGetValue(out int firstValue), Is.True);
            Assert.That(second[0].WrappedValue.TryGetValue(out int secondValue), Is.True);
            Assert.That(firstValue, Is.EqualTo(5));
            Assert.That(secondValue, Is.EqualTo(5));
            Assert.That(fixture.Remote.Reads, Has.Count.EqualTo(2));
            Assert.That(session.Status()["reads"]!.GetValue<long>(), Is.EqualTo(2));
        }

        [TestCase("node")]
        [TestCase("attribute")]
        [TestCase("index-range")]
        public async Task OutOfScopeWritesCannotReachTheBorrowedSessionAsync(string invalid)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            NodeId allowed = fixture.Remote.Id("allowed");
            await using var session = new McpPubSubBorrowedSession(fixture.Sessions,
                fixture.Sessions.GetSessionInfoOrThrow(PubSubExpansionTestContext.SessionName),
                "write", [allowed], new McpPubSubOptions(), fixture.Clock);
            var write = new WriteValue
            {
                NodeId = invalid == "node" ? fixture.Remote.Id("other") : allowed,
                AttributeId = invalid == "attribute" ? Attributes.DisplayName : Attributes.Value,
                IndexRange = invalid == "index-range" ? "0:1" : null,
                Value = new DataValue(Variant.From(1))
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await session.WriteAsync([write]).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(session.Status()["writes"]!.GetValue<long>(), Is.Zero);
            Assert.That(fixture.Remote.Operations, Is.Empty);
        }

        [Test]
        public async Task BorrowedAdapterCannotCreateImplicitSubscriptionsAndDisposalDoesNotCloseHostSessionAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            NodeId node = fixture.Remote.Id("value");
            var session = new McpPubSubBorrowedSession(fixture.Sessions,
                fixture.Sessions.GetSessionInfoOrThrow(PubSubExpansionTestContext.SessionName),
                "read", [node], new McpPubSubOptions(), fixture.Clock);
            try
            {
                await Assert.ThatAsync(
                    async () => await session.CreateDataChangeSubscriptionAsync(1000).ConfigureAwait(false),
                    Throws.TypeOf<NotSupportedException>()).ConfigureAwait(false);
                await Assert.ThatAsync(
                    async () => await session.StartModelChangeMonitoringAsync().ConfigureAwait(false),
                    Throws.TypeOf<NotSupportedException>()).ConfigureAwait(false);
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(session.IsConnected, Is.False);
            Assert.That(fixture.Remote.Session.Object.Connected, Is.True);
            Assert.That(fixture.Remote.Operations, Is.Empty);
            await Assert.ThatAsync(async () => await session.ConnectAsync().ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>()).ConfigureAwait(false);
        }
    }
}
#endif
