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
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Tools.Tests.McpRuntime;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Uses the existing real ManagedSession fixture below the generation-bound SDK request seam.
    /// </summary>
    [TestFixture]
    public sealed class WotRuntimeOpcUaTests
    {
        [Test]
        public async Task SessionGenerationPreventsFurtherRuntimeDispatchAsync()
        {
            await using RemoteModelTestSession remote = await RemoteModelTestSession.CreateAsync(
                includeWot: false, includeRegistry: false).ConfigureAwait(false);
            remote.Values[(remote.Id("temperature"), Attributes.Value)] = new DataValue(Variant.From(25));
            Uri endpoint = remote.Session.Object.ConfiguredEndpoint.EndpointUrl ??
                throw new InvalidOperationException("The test session must have an endpoint.");
            string resource = NodeId.ToExpandedNodeId(
                remote.Id("temperature"), remote.Context.NamespaceUris).ToString();
            await using WotRuntimeTestContext fixture = Create(endpoint, resource);
            await using GdsManagedSession connection = await GdsManagedSession.CreateAsync(remote, fixture.Clock)
                .ConfigureAwait(false);
            OpcUaSessionManager manager = fixture.Services.GetRequiredService<OpcUaSessionManager>();
            await manager.RegisterExistingSessionAsync("selected", connection.Managed, "Anonymous")
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            string handle = await OpenPropertyAsync(fixture, endpoint, remote.Id("temperature")).ConfigureAwait(false);
            JsonObject before = WotRuntimeTestContext.Payload(
                await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false));
            Assert.That(before["dataValue"]!["Value"]!.GetValue<int>(), Is.EqualTo(25));
            int dispatched = connection.Requests.Count;

            connection.Recreate("replacement-generation");
            _ = WotRuntimeTestContext.Payload(
                await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false), error: true);

            Assert.That(connection.Requests, Has.Count.EqualTo(dispatched));
            Assert.That(connection.Inner.SessionId, Is.EqualTo(remote.Id("replacement-generation")));
            Assert.That(connection.Managed.Connected, Is.True);
        }

        /// <summary>
        /// Verifies that generation invalidation disposes an idle channel without another runtime request.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [Test]
        public async Task SessionInvalidationClosesOwnedChannelWithoutAnotherRequestAsync()
        {
            await using RemoteModelTestSession remote = await RemoteModelTestSession.CreateAsync(
                includeWot: false, includeRegistry: false).ConfigureAwait(false);
            Uri endpoint = remote.Session.Object.ConfiguredEndpoint.EndpointUrl ??
                throw new InvalidOperationException("The test session must have an endpoint.");
            string resource = NodeId.ToExpandedNodeId(
                remote.Id("temperature"), remote.Context.NamespaceUris).ToString();
            await using WotRuntimeTestContext fixture = Create(endpoint, resource, realExecutors: false);
            await using GdsManagedSession connection = await GdsManagedSession.CreateAsync(remote, fixture.Clock)
                .ConfigureAwait(false);
            await fixture.Services.GetRequiredService<OpcUaSessionManager>()
                .RegisterExistingSessionAsync("selected", connection.Managed, "Anonymous").ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            string handle = await OpenPropertyAsync(fixture, endpoint, remote.Id("temperature")).ConfigureAwait(false);
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false));
            Assert.That(fixture.Factory.DisposeCalls, Is.Zero);

            connection.Recreate("invalidated-generation");
            await McpRuntimeTestContext.AwaitAsync(fixture.Factory.Disposed.Task).ConfigureAwait(false);

            Assert.That(fixture.Factory.DisposeCalls, Is.EqualTo(1));
            Assert.That(fixture.Registry.List().Contains(info =>
                info.Handle == handle && info.State == McpRuntimeState.Ready), Is.False);
            Assert.That(connection.Managed.Connected, Is.True);
            Assert.That(connection.Inner.SessionId, Is.EqualTo(remote.Id("invalidated-generation")));
        }

        [Test]
        public async Task CloseDoesNotDisposeNamedSessionAsync()
        {
            await using RemoteModelTestSession remote = await RemoteModelTestSession.CreateAsync(
                includeWot: false, includeRegistry: false).ConfigureAwait(false);
            remote.Values[(remote.Id("temperature"), Attributes.Value)] = new DataValue(Variant.From(25));
            Uri endpoint = remote.Session.Object.ConfiguredEndpoint.EndpointUrl ??
                throw new InvalidOperationException("The test session must have an endpoint.");
            string resource = NodeId.ToExpandedNodeId(
                remote.Id("temperature"), remote.Context.NamespaceUris).ToString();
            await using WotRuntimeTestContext fixture = Create(endpoint, resource);
            await using GdsManagedSession connection = await GdsManagedSession.CreateAsync(remote, fixture.Clock)
                .ConfigureAwait(false);
            OpcUaSessionManager manager = fixture.Services.GetRequiredService<OpcUaSessionManager>();
            await manager.RegisterExistingSessionAsync("selected", connection.Managed, "Anonymous")
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            string handle = await OpenPropertyAsync(fixture, endpoint, remote.Id("temperature")).ConfigureAwait(false);
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false));
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.CloseAsync(handle).ConfigureAwait(false));

            Assert.That(connection.Managed.Connected, Is.True);
            Assert.That(manager.GetSessionOrThrow("selected"), Is.SameAs(connection.Managed));
            Assert.That(connection.Requests.OfType<CloseSessionRequest>(), Is.Empty);
            Assert.That(connection.Inner.SessionId, Is.EqualTo(remote.Id("session")));
        }

        [Test]
        public async Task NamedSessionEndpointMismatchIsRejectedBeforeAnyRequestAsync()
        {
            await using RemoteModelTestSession remote = await RemoteModelTestSession.CreateAsync(
                includeWot: false, includeRegistry: false).ConfigureAwait(false);
            Uri endpoint = remote.Session.Object.ConfiguredEndpoint.EndpointUrl ??
                throw new InvalidOperationException("The test session must have an endpoint.");
            string resource = NodeId.ToExpandedNodeId(
                remote.Id("temperature"), remote.Context.NamespaceUris).ToString();
            await using WotRuntimeTestContext fixture = Create(endpoint, resource);
            await using GdsManagedSession connection = await GdsManagedSession.CreateAsync(remote, fixture.Clock)
                .ConfigureAwait(false);
            await fixture.Services.GetRequiredService<OpcUaSessionManager>()
                .RegisterExistingSessionAsync("selected", connection.Managed, "Anonymous").ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            var other = new UriBuilder(endpoint) { Host = "unapproved.example" };
            string document = PropertyDocument(other.Uri, remote.Id("temperature"));
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("mismatch",
                new WotDocumentSource { Text = document }, Selection(document), ["readproperty"],
                options: new WotRuntimeOpenOptions { SessionName = "selected" }).ConfigureAwait(false), error: true);

            Assert.That(result["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(connection.Requests, Is.Empty);
        }

        [Test]
        public async Task ProtectedActionStillRequiresTypedSecretSafeToolsAsync()
        {
            await using RemoteModelTestSession remote = await RemoteModelTestSession.CreateAsync(
                includeWot: false, includeRegistry: false).ConfigureAwait(false);
            ushort ns = remote.Context.NamespaceUris.GetIndexOrAppend(Gds.Namespaces.OpcUaGds);
            var method = new NodeId("secret-method", ns);
            var owner = new NodeId("secret-owner", ns);
            Uri endpoint = remote.Session.Object.ConfiguredEndpoint.EndpointUrl ??
                throw new InvalidOperationException("The test session must have an endpoint.");
            string resource = "object=" +
                NodeId.ToExpandedNodeId(owner, remote.Context.NamespaceUris) +
                ";method=" +
                NodeId.ToExpandedNodeId(method, remote.Context.NamespaceUris);
            await using var fixture = new WotRuntimeTestContext(options => options.NetworkRules =
                [WotRuntimeTestContext.Rule(McpCapability.WotInvoke, ["Call"], [resource],
                    endpoint.Scheme, endpoint.IdnHost, endpoint.Port)], realExecutors: true);
            await using GdsManagedSession connection = await GdsManagedSession.CreateAsync(remote, fixture.Clock)
                .ConfigureAwait(false);
            await fixture.Services.GetRequiredService<OpcUaSessionManager>()
                .RegisterExistingSessionAsync("selected", connection.Managed, "Anonymous").ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            string document = new JsonObject
            {
                ["@context"] = "https://www.w3.org/2022/wot/td/v1.1",
                ["title"] = "Protected operation",
                ["actions"] = new JsonObject
                {
                    ["issue"] = new JsonObject
                    {
                        ["forms"] = new JsonArray(new JsonObject
                        {
                            ["href"] = endpoint.AbsoluteUri,
                            ["uav:id"] = method.ToString(),
                            ["uav:componentOf"] = owner.ToString(),
                            ["op"] = "invokeaction"
                        })
                    }
                }
            }.ToJsonString();
            JsonObject opened = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("protected",
                new WotDocumentSource { Text = document },
                Selection(document) with { Affordance = "issue", Kind = WotAffordanceKind.Action },
                ["invokeaction"], options: new WotRuntimeOpenOptions { SessionName = "selected" })
                .ConfigureAwait(false));
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.InvokeActionAsync(
                opened["runtime"]!["handle"]!.GetValue<string>()).ConfigureAwait(false), error: true);

            Assert.That(result["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(connection.Requests.OfType<CallRequest>(), Is.Empty);
        }

        /// <summary>
        /// Creates an exact read grant for a host-owned named session endpoint.
        /// </summary>
        private static WotRuntimeTestContext Create(Uri endpoint, string resource, bool realExecutors = true)
        {
            return new WotRuntimeTestContext(options => options.NetworkRules =
                [WotRuntimeTestContext.Rule(McpCapability.WotRuntime, ["Read"], [resource],
                    endpoint.Scheme, endpoint.IdnHost, endpoint.Port)], realExecutors: realExecutors);
        }

        /// <summary>
        /// Opens a property without another session manager or a connection request.
        /// </summary>
        private static async Task<string> OpenPropertyAsync(
            WotRuntimeTestContext fixture, Uri endpoint, NodeId node)
        {
            string document = PropertyDocument(endpoint, node);
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("ua",
                new WotDocumentSource { Text = document }, Selection(document), ["readproperty"],
                options: new WotRuntimeOpenOptions { SessionName = "selected" }).ConfigureAwait(false));
            return result["runtime"]!["handle"]!.GetValue<string>();
        }

        /// <summary>
        /// Builds a TD through standard JSON nodes so node identifier strings are escaped correctly.
        /// </summary>
        private static string PropertyDocument(Uri endpoint, NodeId node)
        {
            return new JsonObject
            {
                ["@context"] = "https://www.w3.org/2022/wot/td/v1.1",
                ["title"] = "Borrowed UA property",
                ["properties"] = new JsonObject
                {
                    ["temperature"] = new JsonObject
                    {
                        ["type"] = "number",
                        ["forms"] = new JsonArray(new JsonObject
                        {
                            ["href"] = endpoint.AbsoluteUri,
                            ["uav:id"] = node.ToString(),
                            ["op"] = "readproperty"
                        })
                    }
                }
            }.ToJsonString();
        }

        /// <summary>
        /// Pins the existing OPC UA binder version exactly.
        /// </summary>
        private static WotRuntimeSelection Selection(string document)
        {
            return WotRuntimeTestContext.Selection(document) with { BindingId = "opc.opcua", BindingVersion = "10101" };
        }
    }
}
#endif
