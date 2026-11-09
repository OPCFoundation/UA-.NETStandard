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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Gds;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Pins independently bounded catalogues and invokes structured requests through the real MCP SDK binder.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class GdsBindingTests
    {
        [TestCase(McpToolProfile.Gds, "gds_", 9)]
        [TestCase(McpToolProfile.Certificates, "certificate_", 23)]
        [TestCase(McpToolProfile.Credentials, "credential_", 10)]
        [TestCase(McpToolProfile.Onboarding, "onboarding_", 4)]
        public async Task ProfilesRemainIndividuallyBoundedWithSixDeduplicatedConnectionsAsync(
            McpToolProfile profile, string prefix, int expectedCount)
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaGdsTools(profile)
                .WithOpcUaGdsTools(new McpToolProfileSet(profile)).WithOpcUaConnectionTools();
            await using ServiceProvider provider = services.BuildServiceProvider();
            string[] names = [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];

            Assert.That(names, Is.Unique);
            Assert.That(names.Count(name => name.StartsWith(prefix, StringComparison.Ordinal)),
                Is.EqualTo(expectedCount));
            Assert.That(names, Has.Length.EqualTo(expectedCount + 6));
            Assert.That(names.Where(name => !name.StartsWith(prefix, StringComparison.Ordinal)),
                Is.EquivalentTo(s_connections));
        }

        [Test]
        public async Task ComposedAndFullRegistrationShareOneDomainSetAndOneConnectionSetAsync()
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaGdsTools(McpToolProfileSet.Parse("gds,credentials,onboarding"))
                .WithOpcUaGdsTools(McpToolProfile.Certificates).WithOpcUaGdsTools(McpToolProfile.Full);
            await using ServiceProvider provider = services.BuildServiceProvider();
            string[] names = [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];

            Assert.That(names, Is.Unique);
            Assert.That(names, Has.Length.EqualTo(52));
            Assert.That(names.Any(name => name.Contains("RequestAccessToken", StringComparison.OrdinalIgnoreCase)),
                Is.False);
        }

        [TestCase(McpToolProfile.Core)]
        [TestCase(McpToolProfile.WotDocuments)]
        [TestCase(McpToolProfile.Schemas)]
        public async Task UnrelatedProfilesDoNotAcquireGdsOrConnectionToolsAsync(McpToolProfile profile)
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaGdsTools(profile);
            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetServices<McpServerTool>(), Is.Empty);
        }

        [Test]
        public async Task DisabledCapabilitiesHideAllDomainToolsAndDirectInvocationStillRefusesAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(enabled: false).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            ListToolsResult catalogue = await fixture.ListAsync().ConfigureAwait(false);
            CallToolResult call = await fixture.Services.GetRequiredService<GdsDirectoryTools>().GetApplicationAsync(
                fixture.Remote.Id("directory").ToString(), fixture.Remote.Id("app").ToString(), "selected")
                .ConfigureAwait(false);

            Assert.That(catalogue.Tools.Select(tool => tool.Name),
                Is.EquivalentTo(s_connections));
            RemoteModelTestSession.Error(call, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.Remote.Operations, Is.Empty);
        }

        [Test]
        public async Task AdvertisedContractsHaveConcreteBoundedArraysAndAtMostThreeContainerLevelsAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            ListToolsResult catalogue = await fixture.ListAsync().ConfigureAwait(false);
            Tool[] domain = [.. catalogue.Tools.Where(tool => tool.Name.Contains('_', StringComparison.Ordinal))];

            Assert.That(domain, Has.Length.EqualTo(46));
            foreach (Tool tool in domain)
            {
                Assert.That(tool.InputSchema.GetProperty("properties").EnumerateObject().Count(),
                    Is.LessThanOrEqualTo(8), tool.Name);
                Assert.That(ContainerDepth(tool.InputSchema), Is.LessThanOrEqualTo(3), tool.Name);
                Assert.That(tool.InputSchema.GetProperty("additionalProperties").GetBoolean(), Is.False, tool.Name);
            }
            JsonElement names = domain.Single(tool => tool.Name == "gds_register_application")
                .InputSchema.GetProperty("properties").GetProperty("applicationNames");
            Assert.That(names.GetProperty("type").GetString(), Is.EqualTo("array"));
            Assert.That(names.GetProperty("items").GetProperty("properties").TryGetProperty("locale", out _), Is.True);
            Assert.That(names.GetProperty("maxItems").GetInt32(), Is.EqualTo(100));
            JsonElement tickets = domain.Single(tool => tool.Name == "onboarding_register_tickets")
                .InputSchema.GetProperty("properties").GetProperty("input").GetProperty("properties")
                .GetProperty("ticketReferences");
            Assert.That(tickets.GetProperty("type").GetString(), Is.EqualTo("array"));
            Assert.That(tickets.GetProperty("items").GetProperty("type").GetString(), Is.EqualTo("string"));
            Assert.That(tickets.GetProperty("minItems").GetInt32(), Is.EqualTo(1));
            Assert.That(tickets.GetProperty("maxItems").GetInt32(), Is.EqualTo(100));
        }

        [Test]
        public async Task SdkRegistrationBindsLocalizedNamesAndTypedArraysWithoutJsonStringEscapesAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            NodeId assigned = fixture.Remote.Id("assigned-application");
            fixture.Remote.Reply(StatusCodes.GoodResultsMayBeIncomplete, Variant.From(assigned));

            CallToolResult result = await fixture.InvokeAsync("gds_register_application", new JsonObject
            {
                ["directoryNodeId"] = fixture.Remote.Id("directory").ToString(),
                ["applicationNames"] = new JsonArray(
                    new JsonObject { ["locale"] = "en", ["text"] = "Pump" },
                    new JsonObject { ["locale"] = "de", ["text"] = "Pumpe" }),
                ["input"] = new JsonObject
                {
                    ["applicationUri"] = "urn:test:application",
                    ["applicationType"] = "Server",
                    ["productUri"] = "urn:test:product",
                    ["discoveryUrls"] = new JsonArray("opc.tcp://plant.invalid:4840"),
                    ["serverCapabilities"] = new JsonArray("DA", "HD")
                }
            }).ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            CallMethodRequest call = fixture.Remote.Calls.Single();
            Assert.That(call.ObjectId, Is.EqualTo(fixture.Remote.Id("directory")));
            Assert.That(call.MethodId,
                Is.EqualTo(fixture.Remote.Model(Gds.MethodIds.DirectoryType_RegisterApplication)));
            Assert.That(call.InputArguments[0].TryGetStructure<ApplicationRecordDataType>(fixture.Remote.Context,
                out ApplicationRecordDataType? application), Is.True);
            Assert.That(application!.ApplicationNames.ToArray(),
                Is.EqualTo([new LocalizedText("en", "Pump"), new LocalizedText("de", "Pumpe")]));
            Assert.That(application.DiscoveryUrls.ToArray(), Is.EqualTo(s_discoveryUrls));
            Assert.That(application.ServerCapabilities.ToArray(), Is.EqualTo(s_capabilities));
            Assert.That(application.ApplicationType, Is.EqualTo(ApplicationType.Server));
            Assert.That(application.ApplicationId.IsNull, Is.True);
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("applicationId").GetString(), Is.EqualTo(assigned.ToString()));
            Assert.That(json.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete.Code));
        }

        [Test]
        public async Task SdkRejectsAnUnknownRootArgumentBeforeAnyUaOperationAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();

            CallToolResult result = await fixture.InvokeAsync("credential_cancel_key_request", new JsonObject
            {
                ["serviceNodeId"] = fixture.Remote.Id("key-service").ToString(),
                ["requestId"] = fixture.Remote.Id("request").ToString(),
                ["password"] = "not-an-accepted-argument"
            }).ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadInvalidArgument);
            Assert.That(RemoteModelTestSession.Payload(result).GetRawText(),
                Does.Not.Contain("not-an-accepted-argument"));
            Assert.That(fixture.Remote.Operations, Is.Empty);
        }

        [Test]
        public async Task CommittedRegistrationKeepsCapturedSessionIdAfterCancellationAndDisconnectAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            using var cancellation = new CancellationTokenSource();
            NodeId originalSession = fixture.Remote.Session.Object.SessionId;
            fixture.Remote.OnCall = _ =>
            {
                fixture.Connection.Disconnect();
                cancellation.Cancel();
                return new CallMethodResult
                {
                    StatusCode = StatusCodes.Good,
                    OutputArguments = [Variant.From(fixture.Remote.Id("committed"))]
                };
            };

            CallToolResult result = await fixture.Services.GetRequiredService<GdsDirectoryTools>()
                .RegisterApplicationAsync(fixture.Remote.Id("directory").ToString(), new GdsApplicationInput
                {
                    ApplicationUri = "urn:app",
                    ProductUri = "urn:product",
                    ApplicationNames = [new GdsApplicationName { Text = "Committed" }]
                }, "selected", cancellation.Token).ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("applicationId").GetString(),
                Is.EqualTo(fixture.Remote.Id("committed").ToString()));
            Assert.That(json.GetProperty("sessionId").GetString(), Is.EqualTo(originalSession.ToString()));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Counts concrete object/array containers, not JSON-schema bookkeeping objects.
        /// </summary>
        private static int ContainerDepth(JsonElement schema)
        {
            int child = 0;
            if (schema.TryGetProperty("properties", out JsonElement properties))
            {
                foreach (JsonProperty property in properties.EnumerateObject())
                {
                    child = Math.Max(child, ContainerDepth(property.Value));
                }
                return child + 1;
            }
            if (schema.TryGetProperty("items", out JsonElement items))
            {
                return ContainerDepth(items) + 1;
            }
            return 0;
        }

        private static readonly string[] s_connections =
            ["Connect", "ConnectCertificate", "ConnectJwt", "Disconnect", "GetEndpoints", "GetConnectionStatus"];

        private static readonly string[] s_discoveryUrls = ["opc.tcp://plant.invalid:4840"];
        private static readonly string[] s_capabilities = ["DA", "HD"];
    }
}
#endif
