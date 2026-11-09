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

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        public async Task ProtectedActionStillRequiresTypedSecretSafeToolsAsync(
            bool protectedMethod, bool omitReceiver)
        {
            await using RemoteModelTestSession remote = await RemoteModelTestSession.CreateAsync(
                includeWot: false, includeRegistry: false).ConfigureAwait(false);
            ushort ns = remote.Context.NamespaceUris.GetIndexOrAppend(Gds.Namespaces.OpcUaGds);
            NodeId method = protectedMethod ? new NodeId("secret-method", ns) : remote.Id("method");
            NodeId owner = protectedMethod ? remote.Id("owner") : new NodeId("secret-owner", ns);
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
            var source = new JsonObject
            {
                ["@context"] = "https://www.w3.org/2022/wot/td/v1.1",
                ["title"] = "Protected operation",
                ["base"] = endpoint.AbsoluteUri,
                ["uav:id"] = NodeId.ToExpandedNodeId(owner, remote.Context.NamespaceUris).ToString(),
                ["actions"] = new JsonObject
                {
                    ["issue"] = new JsonObject
                    {
                        ["forms"] = new JsonArray(new JsonObject
                        {
                            ["href"] = endpoint.AbsoluteUri,
                            ["uav:id"] = method.ToString(),
                            ["uav:callObjectId"] =
                                NodeId.ToExpandedNodeId(owner, remote.Context.NamespaceUris).ToString(),
                            ["op"] = "invokeaction"
                        })
                    }
                }
            };
            if (omitReceiver)
            {
                ((JsonObject)source["actions"]!["issue"]!["forms"]![0]!).Remove("uav:callObjectId");
            }
            string document = source.ToJsonString();
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
        /// Verifies that the target grant and dispatch retain the same inferred source receiver.
        /// </summary>
        [Test]
        public async Task SourceReceiverMatchesGrantedAndDispatchedPairAsync()
        {
            await using RemoteModelTestSession remote = await RemoteModelTestSession.CreateAsync(
                includeWot: false, includeRegistry: false).ConfigureAwait(false);
            NodeId owner = remote.Id("owner");
            NodeId method = remote.Id("method");
            remote.Values[(method, Attributes.NodeClass)] = new DataValue(Variant.From((int)NodeClass.Method));
            remote.Browse(owner, ReferenceTypeIds.HasComponent,
                [new ReferenceDescription { NodeId = method, NodeClass = NodeClass.Method }]);
            remote.OnCall = _ => new CallMethodResult { StatusCode = StatusCodes.Good };
            Uri endpoint = remote.Session.Object.ConfiguredEndpoint.EndpointUrl ??
                throw new InvalidOperationException("The test session must have an endpoint.");
            string ownerText = NodeId.ToExpandedNodeId(owner, remote.Context.NamespaceUris).ToString();
            string methodText = NodeId.ToExpandedNodeId(method, remote.Context.NamespaceUris).ToString();
            string resource = "object=" + ownerText + ";method=" + methodText;
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
                ["title"] = "Original source action",
                ["base"] = endpoint.AbsoluteUri,
                ["uav:id"] = ownerText,
                ["actions"] = new JsonObject
                {
                    ["run"] = new JsonObject
                    {
                        ["forms"] = new JsonArray(new JsonObject
                        {
                            ["href"] = endpoint.AbsoluteUri,
                            ["uav:id"] = methodText,
                            ["op"] = "invokeaction"
                        })
                    }
                }
            }.ToJsonString();
            JsonObject opened = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("source-action",
                new WotDocumentSource { Text = document },
                Selection(document) with { Affordance = "run", Kind = WotAffordanceKind.Action },
                ["invokeaction"], options: new WotRuntimeOpenOptions { SessionName = "selected" })
                .ConfigureAwait(false));
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.InvokeActionAsync(
                opened["runtime"]!["handle"]!.GetValue<string>()).ConfigureAwait(false));

            Assert.That(remote.Calls, Has.Count.EqualTo(1));
            Assert.That(remote.Calls[0].ObjectId, Is.EqualTo(owner));
            Assert.That(remote.Calls[0].MethodId, Is.EqualTo(method));
            Assert.That(connection.Requests.OfType<CallRequest>().Count(), Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies selected published 1.0 authorization and dispatch use the
        /// same owner candidate from the original action source.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PublishedOneZeroComponentOfAuthorizationMatchesDispatchedPairAsync(bool browsePath)
        {
            await using RemoteModelTestSession remote = await RemoteModelTestSession.CreateAsync(
                includeWot: false, includeRegistry: false).ConfigureAwait(false);
            NodeId owner = remote.Id("owner");
            NodeId method = remote.Id("method");
            remote.Values[(method, Attributes.NodeClass)] = new DataValue(Variant.From((int)NodeClass.Method));
            remote.Browse(owner, ReferenceTypeIds.HasComponent,
                [new ReferenceDescription { NodeId = method, NodeClass = NodeClass.Method }]);
            remote.Translations[(ObjectIds.RootFolder, new QualifiedName("Method"))] = new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = [new BrowsePathTarget { TargetId = method, RemainingPathIndex = uint.MaxValue }]
            };
            remote.OnCall = _ => new CallMethodResult { StatusCode = StatusCodes.Good };
            Uri endpoint = remote.Session.Object.ConfiguredEndpoint.EndpointUrl ??
                throw new InvalidOperationException("The test session must have an endpoint.");
            string ownerText = NodeId.ToExpandedNodeId(owner, remote.Context.NamespaceUris).ToString();
            string methodText = NodeId.ToExpandedNodeId(method, remote.Context.NamespaceUris).ToString();
            string resource = "object=" + ownerText + ";method=" + methodText;
            string preflightResource = "object=" + ownerText +
                ";browsePath=" + Uri.EscapeDataString("i=84") + "|" + Uri.EscapeDataString(
                    "[[\"http://opcfoundation.org/UA/\",\"Method\",\"http://opcfoundation.org/UA/\"," +
                    "\"HierarchicalReferences\",false,true]]");
            await using var fixture = new WotRuntimeTestContext(options => options.NetworkRules =
                [WotRuntimeTestContext.Rule(McpCapability.WotInvoke, ["Call"], [resource, preflightResource],
                    endpoint.Scheme, endpoint.IdnHost, endpoint.Port)], realExecutors: true);
            await using GdsManagedSession connection = await GdsManagedSession.CreateAsync(remote, fixture.Clock)
                .ConfigureAwait(false);
            await fixture.Services.GetRequiredService<OpcUaSessionManager>()
                .RegisterExistingSessionAsync("selected", connection.Managed, "Anonymous").ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            string document = PublishedActionDocument(endpoint, ownerText, methodText, browsePath);
            JsonObject opened = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("published-action",
                new WotDocumentSource { Text = document },
                Selection(document) with { Affordance = "run", Kind = WotAffordanceKind.Action },
                ["invokeaction"], options: new WotRuntimeOpenOptions
                {
                    SessionName = "selected",
                    BindingProcessingMode = WotBindingProcessingMode.PublishedOneZero
                })
                .ConfigureAwait(false));
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.InvokeActionAsync(
                opened["runtime"]!["handle"]!.GetValue<string>()).ConfigureAwait(false));

            Assert.That(remote.Calls, Has.Count.EqualTo(1));
            Assert.That(remote.Calls[0].ObjectId, Is.EqualTo(owner));
            Assert.That(remote.Calls[0].MethodId, Is.EqualTo(method));
            Assert.That(connection.Requests.OfType<CallRequest>().Count(), Is.EqualTo(1));
            Assert.That(Selection(document).BindingId, Is.EqualTo("opc.opcua"));
            Assert.That(Selection(document).BindingVersion, Is.EqualTo("10101"));
        }

        /// <summary>
        /// Verifies that path grants identify captured namespaces and reference semantics before preflight requests.
        /// </summary>
        [TestCase("same", false)]
        [TestCase("same", true)]
        [TestCase("prefixAlias", false)]
        [TestCase("targetNamespace", false)]
        [TestCase("targetNamespace", true)]
        [TestCase("referenceNamespace", false)]
        [TestCase("inverse", false)]
        [TestCase("subtypes", false)]
        public async Task PublishedBrowsePathGrantPinsCapturedStepsAsync(string change, bool directMethod)
        {
            await using RemoteModelTestSession remote = await RemoteModelTestSession.CreateAsync(
                includeWot: false, includeRegistry: false).ConfigureAwait(false);
            ushort grantedNamespace = remote.Context.NamespaceUris.GetIndexOrAppend("urn:granted");
            remote.Context.NamespaceUris.GetIndexOrAppend("urn:rebound");
            remote.Context.NamespaceUris.GetIndexOrAppend("urn:references");
            remote.Context.NamespaceUris.GetIndexOrAppend("urn:other-references");
            NodeId owner = remote.Id("owner");
            NodeId method = remote.Id("method");
            remote.Values[(method, Attributes.NodeClass)] = new DataValue(Variant.From((int)NodeClass.Method));
            remote.Browse(owner, ReferenceTypeIds.HasComponent,
                [new ReferenceDescription { NodeId = method, NodeClass = NodeClass.Method }]);
            remote.Translations[(ObjectIds.RootFolder, new QualifiedName("Run", grantedNamespace))] =
                new BrowsePathResult
                {
                    StatusCode = StatusCodes.Good,
                    Targets = [new BrowsePathTarget { TargetId = method, RemainingPathIndex = uint.MaxValue }]
                };
            remote.OnCall = _ => new CallMethodResult { StatusCode = StatusCodes.Good };
            Uri endpoint = remote.Session.Object.ConfiguredEndpoint.EndpointUrl ??
                throw new InvalidOperationException("The test session must have an endpoint.");
            string ownerText = NodeId.ToExpandedNodeId(owner, remote.Context.NamespaceUris).ToString();
            string methodText = NodeId.ToExpandedNodeId(method, remote.Context.NamespaceUris).ToString();
            string callResource = "object=" + ownerText + ";method=" + methodText;
            bool explicitReference = change is "referenceNamespace" or "inverse" or "subtypes";
            string grantedSteps = explicitReference
                ? """[["urn:granted","Run","urn:references","Related",false,true]]"""
                : """[["urn:granted","Run","http://opcfoundation.org/UA/","HierarchicalReferences",false,true]]""";
            string pathResource = "object=" + ownerText + ";browsePath=" +
                Uri.EscapeDataString("i=84") + "|" + Uri.EscapeDataString(grantedSteps);
            await using var fixture = new WotRuntimeTestContext(options => options.NetworkRules =
                [WotRuntimeTestContext.Rule(McpCapability.WotInvoke, ["Call"], [callResource, pathResource],
                    endpoint.Scheme, endpoint.IdnHost, endpoint.Port)], realExecutors: true);
            await using GdsManagedSession connection = await GdsManagedSession.CreateAsync(remote, fixture.Clock)
                .ConfigureAwait(false);
            await fixture.Services.GetRequiredService<OpcUaSessionManager>()
                .RegisterExistingSessionAsync("selected", connection.Managed, "Anonymous").ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            JsonObject source = JsonNode.Parse(PublishedActionDocument(endpoint, ownerText, methodText, true))!
                .AsObject();
            source["uav:browsePathAnchor"] = "i=84";
            source["@context"] = new JsonObject
            {
                [change == "prefixAlias" ? "alias" : "p"] =
                    change == "targetNamespace" ? "urn:rebound" : "urn:granted",
                ["r"] = change == "referenceNamespace" ? "urn:other-references" : "urn:references"
            };
            JsonObject form = source["actions"]!["run"]!["forms"]![0]!.AsObject();
            form["uav:browsePath"] = change switch
            {
                "prefixAlias" => "/alias:Run",
                "referenceNamespace" => "<r:Related>p:Run",
                "inverse" => "<!r:Related>p:Run",
                "subtypes" => "<#r:Related>p:Run",
                _ => "/p:Run"
            };
            if (directMethod)
            {
                form["uav:id"] = methodText;
            }
            string document = source.ToJsonString();
            bool denied = change is not ("same" or "prefixAlias");

            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("captured-steps",
                new WotDocumentSource { Text = document },
                Selection(document) with { Affordance = "run", Kind = WotAffordanceKind.Action },
                ["invokeaction"], options: new WotRuntimeOpenOptions
                {
                    SessionName = "selected",
                    BindingProcessingMode = WotBindingProcessingMode.PublishedOneZero
                }).ConfigureAwait(false), error: denied);

            if (denied)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(result["statusCodeValue"]!.GetValue<uint>(),
                        Is.EqualTo(StatusCodes.BadUserAccessDenied.Code), result.ToJsonString());
                    Assert.That(connection.Requests, Is.Empty);
                    Assert.That(remote.Reads, Is.Empty);
                    Assert.That(remote.Paths, Is.Empty);
                    Assert.That(remote.Browses, Is.Empty);
                    Assert.That(remote.Calls, Is.Empty);
                });
            }
            else
            {
                _ = WotRuntimeTestContext.Payload(await fixture.Tools.InvokeActionAsync(
                    result["runtime"]!["handle"]!.GetValue<string>()).ConfigureAwait(false));
                Assert.That(remote.Paths, Is.Not.Empty);
                Assert.That(remote.Paths[0].RelativePath.Elements[0].TargetName,
                    Is.EqualTo(new QualifiedName("Run", grantedNamespace)));
                Assert.That(remote.Calls, Has.Count.EqualTo(1));
                Assert.That(remote.Calls[0].ObjectId, Is.EqualTo(owner));
                Assert.That(remote.Calls[0].MethodId, Is.EqualTo(method));
            }
        }

        /// <summary>
        /// Verifies that channel and identity mismatches reject a named session before preflight requests.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task OpenWithIncompatibleSessionDeniesBeforeSessionServicesAsync(bool requireIdentity)
        {
            await using RemoteModelTestSession remote = await RemoteModelTestSession.CreateAsync(
                includeWot: false, includeRegistry: false,
                mode: requireIdentity ? MessageSecurityMode.SignAndEncrypt : MessageSecurityMode.None)
                .ConfigureAwait(false);
            NodeId owner = remote.Id("owner");
            NodeId method = remote.Id("method");
            remote.Values[(method, Attributes.NodeClass)] = new DataValue(Variant.From((int)NodeClass.Method));
            remote.Browse(owner, ReferenceTypeIds.HasComponent,
                [new ReferenceDescription { NodeId = method, NodeClass = NodeClass.Method }]);
            Uri endpoint = remote.Session.Object.ConfiguredEndpoint.EndpointUrl ??
                throw new InvalidOperationException("The test session must have an endpoint.");
            string ownerText = NodeId.ToExpandedNodeId(owner, remote.Context.NamespaceUris).ToString();
            string methodText = NodeId.ToExpandedNodeId(method, remote.Context.NamespaceUris).ToString();
            string callResource = "object=" + ownerText + ";method=" + methodText;
            string document = new JsonObject
            {
                ["@context"] = new JsonArray(
                    JsonValue.Create("https://www.w3.org/2022/wot/td/v1.1"),
                    new JsonObject { ["uav"] = "http://opcfoundation.org/UA/WoT-Binding/" }),
                ["title"] = "Action with incompatible session security",
                ["base"] = endpoint.AbsoluteUri,
                ["securityDefinitions"] = new JsonObject
                {
                    ["secure"] = new JsonObject
                    {
                        ["scheme"] = "uav:channelsec",
                        ["uav:securityMode"] = "SignAndEncrypt",
                        ["uav:securityPolicy"] = "Basic256Sha256"
                    },
                    ["identity"] = new JsonObject
                    {
                        ["scheme"] = "uav:authentication",
                        ["uav:userIdentityToken"] = "UserName"
                    },
                    ["combined"] = new JsonObject
                    {
                        ["scheme"] = "combo",
                        ["allOf"] = new JsonArray(JsonValue.Create("secure"), JsonValue.Create("identity"))
                    }
                },
                ["actions"] = new JsonObject
                {
                    ["run"] = new JsonObject
                    {
                        ["uav:componentOf"] = new JsonArray(JsonValue.Create(ownerText)),
                        ["forms"] = new JsonArray(new JsonObject
                        {
                            ["href"] = endpoint.AbsoluteUri,
                            ["uav:id"] = methodText,
                            ["security"] = new JsonArray(JsonValue.Create(requireIdentity ? "combined" : "secure")),
                            ["op"] = "invokeaction"
                        })
                    }
                }
            }.ToJsonString();
            await using var fixture = new WotRuntimeTestContext(options => options.NetworkRules =
                [WotRuntimeTestContext.Rule(McpCapability.WotInvoke, ["Call"], [callResource],
                    endpoint.Scheme, endpoint.IdnHost, endpoint.Port)], realExecutors: true);
            await using GdsManagedSession connection = await GdsManagedSession.CreateAsync(remote, fixture.Clock)
                .ConfigureAwait(false);
            await fixture.Services.GetRequiredService<OpcUaSessionManager>()
                .RegisterExistingSessionAsync("selected", connection.Managed, "Anonymous").ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();

            JsonObject denied = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("security-conflict",
                new WotDocumentSource { Text = document },
                Selection(document) with { Affordance = "run", Kind = WotAffordanceKind.Action },
                ["invokeaction"], options: new WotRuntimeOpenOptions
                {
                    SessionName = "selected",
                    BindingProcessingMode = WotBindingProcessingMode.PublishedOneZero
                })
                .ConfigureAwait(false), error: true);

            uint expectedStatus = requireIdentity
                ? StatusCodes.BadIdentityTokenRejected.Code
                : StatusCodes.BadSecurityModeRejected.Code;
            Assert.That(denied["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(expectedStatus), denied.ToJsonString());
            Assert.Multiple(() =>
            {
                Assert.That(connection.Requests, Is.Empty, "No service request may reach the named session.");
                Assert.That(remote.Reads, Is.Empty, "Open must deny before reading NodeClass or Method metadata.");
                Assert.That(remote.Paths, Is.Empty, "Open must deny before translating any browse path.");
                Assert.That(remote.Browses, Is.Empty, "Open must deny before browsing method ownership.");
                Assert.That(remote.Calls, Is.Empty, "Open must not invoke the Method.");
            });
        }

        /// <summary>
        /// Verifies a receiver outside the caller's exact resource grant is
        /// denied before any OPC UA preflight service request.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PublishedOneZeroDeniedComponentOfReceiverIssuesNoCallAsync(bool browsePath)
        {
            await using RemoteModelTestSession remote = await RemoteModelTestSession.CreateAsync(
                includeWot: false, includeRegistry: false).ConfigureAwait(false);
            NodeId sourceOwner = remote.Id("owner");
            NodeId grantedOwner = remote.Id("other-owner");
            NodeId method = remote.Id("method");
            remote.Values[(method, Attributes.NodeClass)] = new DataValue(Variant.From((int)NodeClass.Method));
            remote.Browse(sourceOwner, ReferenceTypeIds.HasComponent,
                [new ReferenceDescription { NodeId = method, NodeClass = NodeClass.Method }]);
            remote.OnCall = _ => new CallMethodResult { StatusCode = StatusCodes.Good };
            Uri endpoint = remote.Session.Object.ConfiguredEndpoint.EndpointUrl ??
                throw new InvalidOperationException("The test session must have an endpoint.");
            string sourceOwnerText = NodeId.ToExpandedNodeId(sourceOwner, remote.Context.NamespaceUris).ToString();
            string grantedOwnerText = NodeId.ToExpandedNodeId(grantedOwner, remote.Context.NamespaceUris).ToString();
            string methodText = NodeId.ToExpandedNodeId(method, remote.Context.NamespaceUris).ToString();
            string grantedResource = "object=" + grantedOwnerText + ";method=" + methodText;
            await using var fixture = new WotRuntimeTestContext(options => options.NetworkRules =
                [WotRuntimeTestContext.Rule(McpCapability.WotInvoke, ["Call"], [grantedResource],
                    endpoint.Scheme, endpoint.IdnHost, endpoint.Port)], realExecutors: true);
            await using GdsManagedSession connection = await GdsManagedSession.CreateAsync(remote, fixture.Clock)
                .ConfigureAwait(false);
            await fixture.Services.GetRequiredService<OpcUaSessionManager>()
                .RegisterExistingSessionAsync("selected", connection.Managed, "Anonymous").ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            string document = PublishedActionDocument(endpoint, sourceOwnerText, methodText, browsePath);
            JsonObject denied = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("denied-published-action",
                new WotDocumentSource { Text = document },
                Selection(document) with { Affordance = "run", Kind = WotAffordanceKind.Action },
                ["invokeaction"], options: new WotRuntimeOpenOptions
                {
                    SessionName = "selected",
                    BindingProcessingMode = WotBindingProcessingMode.PublishedOneZero
                }).ConfigureAwait(false), error: true);

            Assert.That(denied["statusCodeValue"]!.GetValue<uint>(),
                Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(connection.Requests.OfType<CallRequest>(), Is.Empty);
            Assert.That(remote.Calls, Is.Empty);
            Assert.Multiple(() =>
            {
                Assert.That(remote.Reads, Is.Empty, "Authorization denial must precede all Read requests.");
                Assert.That(remote.Paths, Is.Empty,
                    "Authorization denial must precede all TranslateBrowsePathsToNodeIds requests.");
                Assert.That(remote.Browses, Is.Empty, "Authorization denial must precede all Browse requests.");
            });
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
        /// Creates a source TD whose action owner is stated only as the
        /// published action-level array-valued componentOf provenance.
        /// </summary>
        private static string PublishedActionDocument(
            Uri endpoint, string owner, string method, bool browsePath = false)
        {
            var form = new JsonObject
            {
                ["href"] = endpoint.AbsoluteUri,
                ["op"] = "invokeaction"
            };
            form[browsePath ? "uav:browsePath" : "uav:id"] = browsePath ? "/Method" : method;
            return new JsonObject
            {
                ["@context"] = "https://www.w3.org/2022/wot/td/v1.1",
                ["title"] = "Published source action",
                ["base"] = endpoint.AbsoluteUri,
                ["actions"] = new JsonObject
                {
                    ["run"] = new JsonObject
                    {
                        ["uav:componentOf"] = new JsonArray(JsonValue.Create(owner)),
                        ["forms"] = new JsonArray(form)
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
