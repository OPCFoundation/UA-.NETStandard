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
using Opc.Ua.Tools.Tests.McpCompanion;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Exact adapter scope, borrowed-session generation and real per-write outcomes.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionAdapterTests
    {
        [Test]
        public async Task SourceBindingIsStoppedUntilStartAndClosesAfterGenerationReplacementAsync()
        {
            var options = new McpPubSubOptions
            {
                AdapterRules = new ArrayOf<McpPubSubAdapterRule>(s_sourceNodes.Select(node =>
                    new McpPubSubAdapterRule
                    {
                        SessionName = PubSubExpansionTestContext.SessionName,
                        Endpoint = "opc.tcp://unused.invalid:4840",
                        Operation = "read",
                        NodeId = "nsu=urn:test:remote-model;s=" + node
                    }).ToArray())
            };
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext
                .CreateAsync(options: options)
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            string handle = (await fixture.Runtimes.CreateAsync(PubSubExpansionTestContext.Input())
                .ConfigureAwait(false))
                ["handle"]!.GetValue<string>();

            CallToolResult binding = await fixture.Services.GetRequiredService<PubSubAdapterTools>()
                .BindSourceAsync(handle, new PubSubVariablesInput
                {
                    Variables =
                    [
                        new PubSubVariableInput { Name = "Count", NodeId = fixture.Remote.Id("count").ToString() },
                        new PubSubVariableInput { Name = "Enabled", NodeId = fixture.Remote.Id("flag").ToString() }
                    ]
                }, PubSubExpansionTestContext.SessionName).ConfigureAwait(false);
            JsonObject status = await fixture.Runtimes.StatusAsync(handle).ConfigureAwait(false);

            Assert.That(binding.IsError, Is.False);
            Assert.That(status["bindingCount"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(status["isRunning"]!.GetValue<bool>(), Is.False);
            Assert.That(status["bindings"]![0]!["operation"]!.GetValue<string>(), Is.EqualTo("read"));
            Assert.That(status["bindings"]![0]!["ownsSession"]!.GetValue<bool>(), Is.False);
            Assert.That(fixture.Transports.Factories, Is.Zero);
            Assert.That(fixture.Remote.Reads, Is.Empty);

            _ = await fixture.Runtimes.StartAsync(handle).ConfigureAwait(false);
            await fixture.Sessions.RegisterExistingSessionAsync(
                PubSubExpansionTestContext.SessionName, fixture.Remote.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
            await fixture.Transports.Created.Single().Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            JsonObject invalidated = await fixture.Runtimes.StatusAsync(handle).ConfigureAwait(false);
            Assert.That(invalidated["isRunning"]!.GetValue<bool>(), Is.False);
            Assert.That(invalidated["state"]!.GetValue<string>(), Is.EqualTo("BindingInvalidated"));
            Assert.That(fixture.Remote.Session.Object.Connected, Is.True, "The borrowed session is not disposed.");
        }

        [Test]
        public async Task BorrowedSessionReadPreservesValuesAndRejectsReplacedGenerationAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            NodeId node = fixture.Remote.Id("value");
            fixture.Remote.Values[(node, Attributes.Value)] =
                new DataValue(Variant.From(23), StatusCodes.UncertainInitialValue);
            OpcUaSessionManager.SessionInfo info = fixture.Sessions.GetSessionInfoOrThrow(
                PubSubExpansionTestContext.SessionName);
            await using var borrowed = new McpPubSubBorrowedSession(
                fixture.Sessions, info, "read", [node], new McpPubSubOptions(), fixture.Clock);
            ArrayOf<ReadValueId> request = [new ReadValueId { NodeId = node, AttributeId = Attributes.Value }];

            ArrayOf<DataValue> first = await borrowed.ReadAsync(request).ConfigureAwait(false);
            await fixture.Sessions.RegisterExistingSessionAsync(
                PubSubExpansionTestContext.SessionName, fixture.Remote.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            ServiceResultException? stale = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await borrowed.ReadAsync(request).ConfigureAwait(false));

            Assert.That(first.ToList().Single().WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(23));
            Assert.That(first.ToList().Single().StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
            Assert.That(stale!.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Remote.Reads, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task BorrowedSinkPreservesMixedWriteResultsInsteadOfClaimingAtomicSuccessAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            NodeId first = fixture.Remote.Id("first");
            NodeId second = fixture.Remote.Id("second");
            ArrayOf<WriteValue> recorded = default;
            fixture.Remote.Session.Setup(session => session.WriteAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<WriteValue> writes, CancellationToken _) =>
                {
                    recorded = writes;
                    return ValueTask.FromResult(new WriteResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [StatusCodes.Good, StatusCodes.BadNotWritable]
                    });
                });
            await using var borrowed = new McpPubSubBorrowedSession(fixture.Sessions,
                fixture.Sessions.GetSessionInfoOrThrow(PubSubExpansionTestContext.SessionName),
                "write", [first, second], new McpPubSubOptions(), fixture.Clock);

            ArrayOf<StatusCode> statuses = await borrowed.WriteAsync(
            [
                new WriteValue
                {
                    NodeId = first, AttributeId = Attributes.Value, Value = new DataValue(Variant.From(7))
                },
                new WriteValue
                {
                    NodeId = second, AttributeId = Attributes.Value, Value = new DataValue(Variant.From(false))
                }
            ]).ConfigureAwait(false);

            Assert.That(recorded.ToList().Select(write => write.NodeId), Is.EqualTo([first, second]));
            Assert.That(recorded[0].Value.WrappedValue.TryGetValue(out int count), Is.True);
            Assert.That(count, Is.EqualTo(7));
            Assert.That(statuses.ToArray(),
                Is.EqualTo([StatusCodes.Good, StatusCodes.BadNotWritable]));
            Assert.That(borrowed.Status()["lastWriteResults"]![1]!.GetValue<uint>(),
                Is.EqualTo(StatusCodes.BadNotWritable.Code));
            Assert.That(borrowed.Status()["writes"]!.GetValue<long>(), Is.EqualTo(1));
        }

        [Test]
        public async Task MissingAdapterNodeScopeRefusesBeforeBindingOrServiceTrafficAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            string handle = (await fixture.Runtimes.CreateAsync(PubSubExpansionTestContext.Input())
                .ConfigureAwait(false))
                ["handle"]!.GetValue<string>();

            CallToolResult rejected = await fixture.Services.GetRequiredService<PubSubAdapterTools>()
                .BindSourceAsync(handle, new PubSubVariablesInput
                {
                    Variables =
                    [new PubSubVariableInput { Name = "Count", NodeId = fixture.Remote.Id("unapproved").ToString() }]
                }, PubSubExpansionTestContext.SessionName).ConfigureAwait(false);

            RemoteModelTestSession.Error(rejected, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.Remote.Operations, Is.Empty);
            JsonObject state = await fixture.Runtimes.StatusAsync(handle).ConfigureAwait(false);
            Assert.That(state["bindingCount"]!.GetValue<int>(),
                Is.Zero);
        }

        private static readonly string[] s_sourceNodes = ["count", "flag"];
    }
}
#endif
