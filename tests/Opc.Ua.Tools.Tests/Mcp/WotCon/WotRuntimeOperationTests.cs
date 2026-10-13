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
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Tools.Tests.McpRuntime;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Verifies typed UA result preservation and exactly-once MCP dispatch of explicit mutations.
    /// </summary>
    [TestFixture]
    public sealed class WotRuntimeOperationTests
    {
        [Test]
        public async Task ReadPreservesTypedValueQualityAndTimestampsAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            var source = (DateTimeUtc)new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            var server = (DateTimeUtc)new DateTime(2026, 2, 3, 4, 5, 7, DateTimeKind.Utc);
            var quality = new StatusCode(StatusCodes.Uncertain.Code | 0x480u);
            var expected = new DataValue(Variant.From(false), quality, source, server);
            fixture.Factory.Read = _ => ValueTask.FromResult(new WotReadResult(quality, expected));
            string handle = await fixture.OpenAsync().ConfigureAwait(false);

            JsonObject result = WotRuntimeTestContext.Payload(
                await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false));
            using var decoder = new JsonDecoder(result.ToJsonString(),
                ServiceMessageContext.CreateEmpty(McpRuntimeTestContext.CreateTelemetry()));
            DataValue actual = decoder.ReadDataValue("dataValue");

            Assert.That(actual.WrappedValue.TryGetValue(out bool boolean), Is.True);
            Assert.That(boolean, Is.False);
            Assert.That(actual.StatusCode.Code, Is.EqualTo(expected.StatusCode.Code));
            Assert.That(actual.SourceTimestamp, Is.EqualTo(source));
            Assert.That(actual.ServerTimestamp, Is.EqualTo(server));
            Assert.That(result["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(quality.Code));
            Assert.That(fixture.Factory.ReadCalls, Is.EqualTo(1));
        }

        [TestCase("[]", 0)]
        [TestCase("[0,65535]", 2)]
        public async Task TypedArraysAreNotCoercedToStringsOrScalarsAsync(string json, int count)
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            string handle = await fixture.OpenAsync(operations: ["writeproperty"]).ConfigureAwait(false);
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.WritePropertyAsync(handle,
                WotRuntimeTestContext.Value(BuiltInType.UInt16, json)).ConfigureAwait(false));

            Assert.That(fixture.Factory.Writes.Single().WrappedValue.TryGetValue(out ArrayOf<ushort> written), Is.True);
            Assert.That(written.Count, Is.EqualTo(count));
            Assert.That(written.IsNull, Is.False);
            if (count > 0)
            {
                Assert.That(written.ToArray(), Is.EqualTo(new ushort[] { 0, ushort.MaxValue }));
            }
        }

        [Test]
        public async Task WriteFailureIsNeverReplayedAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            fixture.Factory.Write = (_, _) => ValueTask.FromResult(
                new WotWriteResult(StatusCodes.BadCommunicationError, "credential-must-not-be-returned"));
            string handle = await fixture.OpenAsync(operations: ["writeproperty"]).ConfigureAwait(false);
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.WritePropertyAsync(handle,
                WotRuntimeTestContext.Value(BuiltInType.Int32, "11")).ConfigureAwait(false), error: true);

            Assert.That(fixture.Factory.Writes, Has.Count.EqualTo(1));
            Assert.That(fixture.Factory.OpenedForms, Has.Count.EqualTo(1));
            Assert.That(result["automaticallyRetried"]!.GetValue<bool>(), Is.False);
            Assert.That(result.ToJsonString(), Does.Not.Contain("credential-must-not-be-returned"));
            Assert.That(result["statusCodeValue"]!.GetValue<uint>(),
                Is.EqualTo(StatusCodes.BadCommunicationError.Code));
        }

        [Test]
        public async Task LateCompletedWriteKeepsCommittedResultAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            using var cancellation = new CancellationTokenSource();
            TaskCompletionSource<bool> dispatched = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<WotWriteResult> completed = McpRuntimeTestContext.Completion<WotWriteResult>();
            fixture.Factory.Write = async (_, _) =>
            {
                dispatched.TrySetResult(true);
                return await completed.Task.ConfigureAwait(false);
            };
            string handle = await fixture.OpenAsync(operations: ["writeproperty"]).ConfigureAwait(false);
            Task<CallToolResult> writing = fixture.Tools.WritePropertyAsync(handle,
                WotRuntimeTestContext.Value(BuiltInType.Int32, "12"), cancellation.Token);
            await McpRuntimeTestContext.AwaitAsync(dispatched.Task).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            completed.TrySetResult(new WotWriteResult(StatusCodes.Good));
            JsonObject result = WotRuntimeTestContext.Payload(
                await McpRuntimeTestContext.AwaitAsync(writing).ConfigureAwait(false));

            Assert.That(result["completion"]!.GetValue<string>(), Is.EqualTo("completed"));
            Assert.That(result["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.Good.Code));
            Assert.That(fixture.Factory.Writes, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task OversizedValueIsRejectedBeforeActivationAsync()
        {
            await using var fixture = new WotRuntimeTestContext(
                options: new WotRuntimeOptions { MaxPayloadBytes = 128 });
            using IDisposable caller = fixture.Enter();
            string handle = await fixture.OpenAsync(operations: ["writeproperty"]).ConfigureAwait(false);
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.WritePropertyAsync(handle,
                WotRuntimeTestContext.Value(BuiltInType.String, "\"" + new string('x', 129) + "\""))
                .ConfigureAwait(false), error: true);

            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
            Assert.That(fixture.Factory.Writes, Is.Empty);
        }

        [Test]
        public async Task InvalidNumericPartitionIsRejectedBeforeActivationAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            string handle = await fixture.OpenAsync(operations: ["writeproperty"]).ConfigureAwait(false);
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.WritePropertyAsync(handle,
                WotRuntimeTestContext.Value(BuiltInType.UInt16, "65536")).ConfigureAwait(false), error: true);

            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
            Assert.That(fixture.Factory.Writes, Is.Empty);
        }

        [Test]
        public async Task InvocationPreservesOrderedOutputsAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            fixture.Factory.Invoke = (_, _) => ValueTask.FromResult(new WotInvokeResult(StatusCodes.Good,
                [
                    new DataValue(Variant.From(0)),
                    new DataValue(Variant.From(false)),
                    new DataValue(Variant.From("done"))
                ]));
            string handle = await OpenActionAsync(fixture).ConfigureAwait(false);
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.InvokeActionAsync(handle,
                [WotRuntimeTestContext.Value(BuiltInType.UInt32, "4000000000")]).ConfigureAwait(false));
            JsonArray outputs = result["outputs"]!.AsArray();

            Assert.That(outputs, Has.Count.EqualTo(3));
            Assert.That(outputs[0]!["Value"]!.GetValue<int>(), Is.Zero);
            Assert.That(outputs[1]!["Value"]!.GetValue<bool>(), Is.False);
            Assert.That(outputs[2]!["Value"]!.GetValue<string>(), Is.EqualTo("done"));
            Assert.That(fixture.Factory.Invocations.Single()[0].TryGetValue(out uint input), Is.True);
            Assert.That(input, Is.EqualTo(4_000_000_000));
        }

        [Test]
        public async Task ExcessHttpArgumentsAreRejectedInsteadOfDiscardedAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            string handle = await OpenActionAsync(fixture).ConfigureAwait(false);
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.InvokeActionAsync(handle,
                [
                    WotRuntimeTestContext.Value(BuiltInType.Int32, "1"),
                    WotRuntimeTestContext.Value(BuiltInType.Int32, "2")
                ])
                .ConfigureAwait(false), error: true);

            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
            Assert.That(fixture.Factory.Invocations, Is.Empty);
        }

        [Test]
        public async Task OversizedActionOutputDoesNotEraseCompletedMutationAsync()
        {
            await using var fixture = new WotRuntimeTestContext(
                options: new WotRuntimeOptions { MaxResultBytes = 1024 });
            using IDisposable caller = fixture.Enter();
            fixture.Factory.Invoke = (_, _) => ValueTask.FromResult(new WotInvokeResult(StatusCodes.Good,
                [new DataValue(Variant.From(new string('x', 2048)))]));
            string handle = await OpenActionAsync(fixture).ConfigureAwait(false);
            JsonObject result = WotRuntimeTestContext.Payload(
                await fixture.Tools.InvokeActionAsync(handle).ConfigureAwait(false));

            Assert.That(result["completion"]!.GetValue<string>(), Is.EqualTo("completed"));
            Assert.That(result["outputsOmitted"]!.GetValue<bool>(), Is.True);
            Assert.That(result.ContainsKey("outputs"), Is.False);
            Assert.That(fixture.Factory.Invocations, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ExecutorExceptionsNeverEchoSecretMaterialAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            fixture.Factory.Read = _ => throw new InvalidOperationException("private-token-in-provider-message");
            string handle = await fixture.OpenAsync().ConfigureAwait(false);
            JsonObject result = WotRuntimeTestContext.Payload(
                await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false), error: true);

            Assert.That(result.ToJsonString(), Does.Not.Contain("private-token-in-provider-message"));
            Assert.That(result["completion"]!.GetValue<string>(), Is.EqualTo("notConfirmed"));
            Assert.That(fixture.Factory.ReadCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Opens one real action plan over the fake executor transport seam.
        /// </summary>
        private static async Task<string> OpenActionAsync(WotRuntimeTestContext fixture)
        {
            const string document = /*lang=json,strict*/ """
                {
                  "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
                  "id": "urn:test:wot:action",
                  "title": "Runtime action",
                  "securityDefinitions": {"nosec_sc": {"scheme": "nosec"}},
                  "security": "nosec_sc",
                  "actions": {
                    "command": {"forms": [{"href": "http://device.example/action", "op": "invokeaction"}]}
                  }
                }
                """;
            WotRuntimeSelection selection = WotRuntimeTestContext.Selection(document) with
            {
                Affordance = "command",
                Kind = WotAffordanceKind.Action
            };
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("action",
                new WotDocumentSource { Text = document }, selection, ["invokeaction"]).ConfigureAwait(false));
            return result["runtime"]!["handle"]!.GetValue<string>();
        }
    }
}
#endif
