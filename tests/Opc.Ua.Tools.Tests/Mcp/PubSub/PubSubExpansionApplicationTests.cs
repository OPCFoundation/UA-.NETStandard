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
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tools.Tests.McpCompanion;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Exercises named application tools through SDK binding and the private runtime credential lifetime.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionApplicationTests
    {
        [Test]
        public async Task SdkApplicationWorkflowKeepsStateQuotaAndPublishAcknowledgementSemanticsAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: new McpPubSubOptions { MaxMessagesPerSecond = 1 }).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            PubSubApplicationInput input = PubSubExpansionTestContext.Input();
            CallToolResult capabilities = await fixture.InvokeAsync("pubsub_capabilities", [])
                .ConfigureAwait(false);
            Assert.That(capabilities.IsError, Is.False);
            CallToolResult validated = await fixture.InvokeAsync("pubsub_application_validate", Input(input))
                .ConfigureAwait(false);
            Assert.That(RemoteModelTestSession.Payload(validated).GetProperty("valid").GetBoolean(), Is.True);
            Assert.That(fixture.Transports.Factories, Is.Zero);
            CallToolResult created = await fixture.InvokeAsync("pubsub_application_create", Input(input))
                .ConfigureAwait(false);
            string handle = RemoteModelTestSession.Payload(created).GetProperty("handle").GetString()!;
            CallToolResult listed = await fixture.InvokeAsync("pubsub_application_list", [])
                .ConfigureAwait(false);
            JsonElement leases = RemoteModelTestSession.Payload(listed).GetProperty("data");
            Assert.That(leases.GetArrayLength(), Is.EqualTo(1));
            Assert.That(leases[0].GetProperty("handle").GetString(), Is.EqualTo(handle));
            using (fixture.Enter(PubSubExpansionTestContext.Other))
            {
                Assert.That(RemoteModelTestSession.Payload(await fixture.InvokeAsync(
                    "pubsub_application_list", []).ConfigureAwait(false))
                    .GetProperty("data").GetArrayLength(), Is.Zero);
            }
            CallToolResult started = await fixture.InvokeAsync("pubsub_application_start", Handle(handle))
                .ConfigureAwait(false);
            Assert.That(RemoteModelTestSession.Payload(started).GetProperty("isRunning").GetBoolean(), Is.True);
            var publish = new PubSubPublishInput
            {
                Fields =
                [new PubSubFieldValueInput { Name = "Count", Value = PubSubExpansionTestContext.Element("99") }]
            };
            JsonObject arguments = Handle(handle);
            arguments["fields"] = JsonSerializer.SerializeToNode(publish.Fields,
                McpPubSubExpansionJsonContext.Default.PubSubFieldValues);

            CallToolResult updated = await fixture.InvokeAsync("pubsub_application_publish", arguments)
                .ConfigureAwait(false);
            CallToolResult limited = await fixture.InvokeAsync("pubsub_application_publish", arguments)
                .ConfigureAwait(false);

            Assert.That(updated.IsError, Is.False);
            Assert.That(RemoteModelTestSession.Payload(updated).GetProperty("updatedFields").GetInt32(), Is.EqualTo(1));
            Assert.That(RemoteModelTestSession.Payload(updated).GetProperty("delivery").GetString(),
                Does.Contain("not an end-to-end delivery acknowledgement"));
            RemoteModelTestSession.Error(limited, StatusCodes.BadTooManyOperations);
            CallToolResult status = await fixture.InvokeAsync("pubsub_application_status", Handle(handle))
                .ConfigureAwait(false);
            Assert.That(RemoteModelTestSession.Payload(status).GetProperty("isRunning").GetBoolean(), Is.True);
            CallToolResult stopped = await fixture.InvokeAsync("pubsub_application_stop", Handle(handle))
                .ConfigureAwait(false);
            Assert.That(RemoteModelTestSession.Payload(stopped).GetProperty("isRunning").GetBoolean(), Is.False);
            Assert.That(fixture.Registry.List(), Has.Count.EqualTo(1));
            CallToolResult removed = await fixture.InvokeAsync("pubsub_application_remove", Handle(handle))
                .ConfigureAwait(false);
            Assert.That(RemoteModelTestSession.Payload(removed).GetProperty("removed").GetBoolean(), Is.True);
            Assert.That(fixture.Registry.List(), Is.Empty);
            Assert.That(fixture.Transports.Created.Single().DisposeCalls, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RuntimeCredentialsAreRestrictedToTheExpectedAliasAndDisposedOnStopOrStartFailureAsync(
            bool failStart)
        {
            var provider = new CredentialProbe(failStart);
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                transports: provider).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            byte[] expected = RandomNumberGenerator.GetBytes(32);
            await fixture.Store.SetAsync(new SecretIdentifier("password", fixture.Store.StoreType), expected)
                .ConfigureAwait(false);
            PubSubApplicationInput input = PubSubExpansionTestContext.Input() with
            {
                Connection = new PubSubConnectionInput
                {
                    Transport = PubSubTransportKind.Mqtt,
                    Encoding = PubSubMessageEncoding.Json,
                    Endpoint = "mqtts://broker.test:8883",
                    Topic = "data",
                    MetadataTopic = "metadata",
                    CredentialReference = "alias:mqtt-password",
                    UserName = "test-user"
                }
            };
            string handle = (await fixture.Runtimes.CreateAsync(input).ConfigureAwait(false))
                ["handle"]!.GetValue<string>();
            Assert.That(provider.Credentials, Is.Null);

            if (failStart)
            {
                ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await fixture.Runtimes.StartAsync(handle).ConfigureAwait(false));
                Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            }
            else
            {
                _ = await fixture.Runtimes.StartAsync(handle).ConfigureAwait(false);
                ISecretRegistry credentials = provider.Credentials!;
                using ISecret? wrongStore = credentials.TryGet(new SecretIdentifier("password", "different"));
                using ISecret? wrongName = credentials.TryGet(new SecretIdentifier("different", "McpRuntime"));
                Assert.That(wrongStore, Is.Null);
                Assert.That(wrongName, Is.Null);
                Assert.That(() => credentials.RegisterStore(fixture.Store), Throws.TypeOf<NotSupportedException>());
                using ISecret? view = await credentials.GetAsync(new SecretIdentifier("password", "McpRuntime"))
                    .ConfigureAwait(false);
                Assert.That(view, Is.Not.Null);
                Assert.That(view!.Bytes.ToArray(), Is.EqualTo(expected));
                view.Dispose();
                Assert.That(view.Bytes.ToArray(), Is.All.Zero);
                _ = await fixture.Runtimes.StopAsync(handle).ConfigureAwait(false);
            }

            Assert.That(provider.Credentials, Is.Not.Null);
            Assert.That(() => provider.Credentials!.TryGet(new SecretIdentifier("password", "McpRuntime")),
                Throws.TypeOf<ObjectDisposedException>());
            using ISecret? stored = await fixture.Store.GetAsync(
                new SecretIdentifier("password", fixture.Store.StoreType))
                .ConfigureAwait(false);
            Assert.That(stored!.Bytes.ToArray(), Is.EqualTo(expected),
                "A runtime never zeroizes the host-owned store.");
            JsonObject status = await fixture.Runtimes.StatusAsync(handle).ConfigureAwait(false);
            Assert.That(status["isRunning"]!.GetValue<bool>(), Is.False);
            Assert.That(status.ToJsonString(), Does.Not.Contain(Convert.ToBase64String(expected)));
            Assert.That(provider.Transports.Created, Has.Count.EqualTo(failStart ? 0 : 1));
            _ = await fixture.Runtimes.RemoveAsync(handle).ConfigureAwait(false);
        }

        private static JsonObject Input(PubSubApplicationInput input)
        {
            return PubSubExpansionTestContext.ApplicationArguments(input);
        }

        private static JsonObject Handle(string handle)
        {
            return new JsonObject { ["handle"] = handle };
        }

        /// <summary>
        /// Observes only the documented provider credential seam, not private runtime fields.
        /// </summary>
        private sealed class CredentialProbe(bool failStart) : IMcpPubSubTransportProvider
        {
            internal ISecretRegistry? Credentials { get; private set; }
            internal PubSubExpansionTransports Transports { get; } = new();

            public JsonArray Capabilities()
            {
                return Transports.Capabilities();
            }

            public IPubSubTransportFactory CreateFactory(
                PubSubApplicationInput input, ISecretRegistry? credentials = null)
            {
                Credentials = credentials;
                if (failStart)
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError);
                }
                return Transports.CreateFactory(input, credentials);
            }

            public IPubSubTransportFactory CreateLegacyFactory(string endpoint, bool publisher)
            {
                return Transports.CreateLegacyFactory(endpoint, publisher);
            }
        }
    }
}
#endif
