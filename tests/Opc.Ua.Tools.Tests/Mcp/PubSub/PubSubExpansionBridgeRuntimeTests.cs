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
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tools.Tests.McpCompanion;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Exercises real owned bridge activation, encoded forwarding, output bounds and failure cleanup.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionBridgeRuntimeTests
    {
        [Test]
        public async Task StartedBridgeForwardsOnlyTransformedFramesAndStopsBothOwnedEndpointsAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            PubSubBridgeTools tools = fixture.Services.GetRequiredService<PubSubBridgeTools>();
            PubSubBridgeInput route = Route() with
            {
                ExcludeFields = ["Enabled"],
                PromoteFields = ["Count"],
                WriterGroupId = 19,
                PreserveMetadataVersion = true,
                DropKeepAlive = true
            };
            CallToolResult validation = await tools.ValidateAsync(route, lifetime.Token).ConfigureAwait(false);
            Assert.That(validation.IsError, Is.False);
            CallToolResult created = await tools.CreateAsync(route, lifetime.Token).ConfigureAwait(false);
            string handle = RemoteModelTestSession.Payload(created).GetProperty("handle").GetString()!;
            JsonElement leases = RemoteModelTestSession.Payload(tools.List()).GetProperty("data");
            Assert.That(leases.GetArrayLength(), Is.EqualTo(1));
            Assert.That(leases[0].GetProperty("handle").GetString(), Is.EqualTo(handle));
            CallToolResult started = await tools.StartAsync(handle, lifetime.Token).ConfigureAwait(false);
            Assert.That(started.IsError, Is.False, RemoteModelTestSession.Payload(started).GetRawText());
            Assert.That(RemoteModelTestSession.Payload(started).GetProperty("running").GetBoolean(), Is.True);
            _ = await tools.StartAsync(handle, lifetime.Token).ConfigureAwait(false);
            Assert.That(fixture.Transports.Factories, Is.EqualTo(2),
                "Repeated start must not replace either endpoint.");
            PubSubExpansionTransport source = fixture.Transports.Created.Single(
                transport => transport.Direction == PubSubTransportDirection.Receive);
            PubSubExpansionTransport target = fixture.Transports.Created.Single(
                transport => transport.Direction == PubSubTransportDirection.Send);
            Assert.That(DataFrames(target, fixture, route.Target, new McpPubSubOptions().MaxMessageBytes), Is.Empty,
                "Startup metadata is permitted; the bridge target must not run its own cyclic data publisher.");

            UadpNetworkMessage keepAlive = PubSubExpansionFrames.Data(route.Source, []) with
            {
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = route.Source.DataSetWriterId,
                        MessageType = PubSubDataSetMessageType.KeepAlive
                    }
                ]
            };
            await source.InjectAsync(await PubSubExpansionFrames.EncodeAsync(keepAlive, fixture, lifetime.Token)
                .ConfigureAwait(false), lifetime.Token).ConfigureAwait(false);
            await source.InjectAsync(await PubSubExpansionFrames.EncodeAsync(
                PubSubExpansionFrames.Data(route.Source,
                [
                    new DataSetField { Name = "Count", Value = Variant.From(37) },
                    new DataSetField { Name = "Enabled", Value = Variant.From(false) }
                ]), fixture, lifetime.Token).ConfigureAwait(false), lifetime.Token).ConfigureAwait(false);
            await PubSubExpansionFrames.UntilAsync(() =>
                DataFrames(target, fixture, route.Target, new McpPubSubOptions().MaxMessageBytes).Length > 0,
                lifetime.Token).ConfigureAwait(false);
            UadpNetworkMessage[] data = DataFrames(
                target, fixture, route.Target, new McpPubSubOptions().MaxMessageBytes);
            Assert.That(data, Has.Length.EqualTo(1), "The keep-alive must not be forwarded as a second data frame.");
            UadpNetworkMessage decoded = data[0];
            Assert.That(decoded.WriterGroupId, Is.EqualTo(19));
            Assert.That(decoded.DataSetMessages, Has.Count.EqualTo(1));
            Assert.That(decoded.DataSetMessages[0].Fields, Has.Count.EqualTo(1));
            Assert.That(decoded.DataSetMessages[0].Fields[0].Value.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(37));
            Assert.That(decoded.DataSetMessages[0].MetaDataVersion.MajorVersion, Is.EqualTo(1));

            JsonElement status;
            do
            {
                status = RemoteModelTestSession.Payload(
                    await tools.StatusAsync(handle, lifetime.Token).ConfigureAwait(false));
                if (status.GetProperty("forwardedMessages").GetInt64() == 0)
                {
                    await Task.Delay(1, lifetime.Token).ConfigureAwait(false);
                }
            }
            while (status.GetProperty("forwardedMessages").GetInt64() == 0);
            Assert.That(status.GetProperty("forwardedMessages").GetInt64(),
                Is.EqualTo(1));
            Assert.That(status.GetProperty("targetCyclicPublishing").GetBoolean(),
                Is.False);
            CallToolResult stopped = await tools.StopAsync(handle, lifetime.Token).ConfigureAwait(false);
            Assert.That(RemoteModelTestSession.Payload(stopped).GetProperty("running").GetBoolean(), Is.False);
            Assert.That(source.DisposeCalls, Is.EqualTo(1));
            Assert.That(target.DisposeCalls, Is.EqualTo(1));
            Assert.That(fixture.Registry.List(), Has.Count.EqualTo(1), "Stop retains the explicit bridge lease.");
            _ = await tools.RemoveAsync(handle, lifetime.Token).ConfigureAwait(false);
            Assert.That(fixture.Registry.List(), Is.Empty);
            Assert.That(source.DisposeCalls, Is.EqualTo(1));
            Assert.That(target.DisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task OversizedTranscodeOutputIsRejectedBeforeAnyTargetFrameIsSentAsync()
        {
            var options = new McpPubSubOptions { MaxMessageBytes = 1024 };
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: options).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            PubSubBridgeInput route = Route();
            ArrayOf<PubSubFieldInput> fields =
                [new PubSubFieldInput { Name = "Text", Type = BuiltInType.String }];
            route = route with
            {
                Source = route.Source with { Fields = fields },
                Target = route.Target with { Fields = fields }
            };
            PubSubBridgeTools tools = fixture.Services.GetRequiredService<PubSubBridgeTools>();
            CallToolResult created = await tools.CreateAsync(route, lifetime.Token).ConfigureAwait(false);
            string handle = RemoteModelTestSession.Payload(created).GetProperty("handle").GetString()!;
            CallToolResult started = await tools.StartAsync(handle, lifetime.Token).ConfigureAwait(false);
            Assert.That(started.IsError, Is.False);
            PubSubExpansionTransport source = fixture.Transports.Created.Single(
                transport => transport.Direction == PubSubTransportDirection.Receive);
            PubSubExpansionTransport target = fixture.Transports.Created.Single(
                transport => transport.Direction == PubSubTransportDirection.Send);
            await source.InjectAsync(await PubSubExpansionFrames.EncodeAsync(
                PubSubExpansionFrames.Data(route.Source,
                    [new DataSetField { Name = "Text", Value = Variant.From(new string('x', 2048)) }]),
                fixture, lifetime.Token).ConfigureAwait(false), lifetime.Token).ConfigureAwait(false);

            JsonElement status;
            do
            {
                status = RemoteModelTestSession.Payload(
                    await tools.StatusAsync(handle, lifetime.Token).ConfigureAwait(false));
                if (status.GetProperty("oversizedDropped").GetInt64() == 0)
                {
                    await Task.Delay(1, lifetime.Token).ConfigureAwait(false);
                }
            }
            while (status.GetProperty("oversizedDropped").GetInt64() == 0);

            Assert.That(status.GetProperty("oversizedDropped").GetInt64(), Is.EqualTo(1));
            Assert.That(status.GetProperty("forwardedMessages").GetInt64(), Is.Zero);
            Assert.That(DataFrames(target, fixture, route.Target, options.MaxMessageBytes), Is.Empty,
                "No data frame may bypass the output ceiling; only bounded, exact startup metadata is permitted.");
            _ = await tools.RemoveAsync(handle, lifetime.Token).ConfigureAwait(false);
            Assert.That(source.DisposeCalls, Is.EqualTo(1));
            Assert.That(target.DisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task SourceActivationFailureClosesTheAlreadyStartedTargetAndRetainsStoppedLeaseAsync()
        {
            var transports = new PubSubExpansionTransports { FailFactoryAt = 2 };
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                transports: transports).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            PubSubBridgeTools tools = fixture.Services.GetRequiredService<PubSubBridgeTools>();
            CallToolResult created = await tools.CreateAsync(Route()).ConfigureAwait(false);
            string handle = RemoteModelTestSession.Payload(created).GetProperty("handle").GetString()!;

            CallToolResult failed = await tools.StartAsync(handle).ConfigureAwait(false);

            RemoteModelTestSession.Error(failed, StatusCodes.BadConfigurationError);
            Assert.That(transports.Factories, Is.EqualTo(2));
            Assert.That(transports.Created, Has.Count.EqualTo(1));
            Assert.That(transports.Created.Single().DisposeCalls, Is.EqualTo(1));
            CallToolResult status = await tools.StatusAsync(handle).ConfigureAwait(false);
            Assert.That(RemoteModelTestSession.Payload(status).GetProperty("running").GetBoolean(), Is.False);
            Assert.That(RemoteModelTestSession.Payload(status).GetProperty("target")
                .GetProperty("isRunning").GetBoolean(), Is.False);
            Assert.That(fixture.Registry.List(), Has.Count.EqualTo(1));
            _ = await tools.RemoveAsync(handle).ConfigureAwait(false);
            Assert.That(transports.Created.Single().DisposeCalls, Is.EqualTo(1));
        }

        [TestCase("unknown-source")]
        [TestCase("duplicate-selection")]
        [TestCase("unknown-promotion")]
        [TestCase("publisher-source")]
        [TestCase("subscriber-target")]
        public void InvalidTransformOperandsAreRejectedBeforeCreatingEitherApplication(string invalid)
        {
            PubSubBridgeInput route = Route();
            route = invalid switch
            {
                "unknown-source" => route with
                {
                    RenameFields = [new PubSubFieldRenameInput { Source = "missing", Target = "new" }]
                },
                "duplicate-selection" => route with { SelectFields = ["Count", "Count"] },
                "unknown-promotion" => route with { PromoteFields = ["missing"] },
                "publisher-source" => route with { Source = route.Source with { Mode = "Publisher" } },
                _ => route with { Target = route.Target with { Mode = "Subscriber" } }
            };

            Assert.That(() => McpPubSubBridge.Validate(route, new McpPubSubOptions()), Throws.ArgumentException);
        }

        /// <summary>
        /// Checks every emitted frame; only declared startup metadata is exempt from data-frame assertions.
        /// </summary>
        private static UadpNetworkMessage[] DataFrames(
            PubSubExpansionTransport transport, PubSubExpansionTestContext fixture,
            PubSubApplicationInput target, int maximumFrameBytes)
        {
            var data = new List<UadpNetworkMessage>();
            foreach (byte[] frame in transport.Sent.ToArray())
            {
                Assert.That(frame, Has.Length.LessThanOrEqualTo(maximumFrameBytes));
                PubSubNetworkMessage? message = PubSubExpansionFrames.Decode(frame, fixture);
                if (message is UadpNetworkMessage values)
                {
                    data.Add(values);
                    continue;
                }
                Assert.That(message, Is.TypeOf<UadpDiscoveryResponseMessage>(),
                    "Unknown or undecodable traffic must not be ignored by the no-data assertion.");
                var metadata = (UadpDiscoveryResponseMessage)message!;
                Assert.That(metadata.DiscoveryType, Is.EqualTo(UadpDiscoveryType.DataSetMetaData));
                Assert.That(metadata.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(metadata.DataSetWriterId, Is.EqualTo(target.DataSetWriterId));
                Assert.That(metadata.DataSetMetaData, Is.Not.Null);
                Assert.That(metadata.DataSetMetaData!.Name, Is.EqualTo(target.DataSetName));
                Assert.That(metadata.DataSetMetaData.Fields.ToList().Select(field => field.Name),
                    Is.EqualTo(target.Fields.ToList().Select(field => field.Name)));
            }
            return [.. data];
        }

        private static PubSubBridgeInput Route()
        {
            return new PubSubBridgeInput
            {
                Name = "wire-bridge",
                Source = PubSubExpansionTestContext.Input("source", "Subscriber"),
                Target = PubSubExpansionTestContext.Input("target")
            };
        }
    }
}
#endif
