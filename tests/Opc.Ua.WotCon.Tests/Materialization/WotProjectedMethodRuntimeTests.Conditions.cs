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
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Export;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using Quickstarts.ReferenceServer;
using WotAffordanceKind = Opc.Ua.WotCon.Bindings.WotAffordanceKind;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotProjectedMethodRuntimeTests
    {
        [TestCase("Enable", false)]
        [TestCase("Disable", false)]
        [TestCase("Enable", true)]
        [TestCase("Disable", true)]
        [Category("Integration")]
        [NonParallelizable]
        public async Task DefaultConditionMethodsForwardWithoutOverwritingApplicationHandlers(
            string action, bool applicationHandler)
        {
            WotCompiledForm eventForm = CreateConditionForm(WotAffordanceKind.Event, "alarm");
            WotCompiledForm actionForm = CreateConditionForm(WotAffordanceKind.Action, action);
            var channel = new FakeWotBindingChannel(actionForm)
            {
                OnInvoke = (inputs, _) =>
                {
                    Assert.That(inputs, Is.Empty, "Enable and Disable keep their zero-input native signatures.");
                    return new ValueTask<WotInvokeResult>(new WotInvokeResult(StatusCodes.Good));
                }
            };
            var channels = new FakeWotBindingChannelFactory();
            channels.SetChannel(actionForm, channel);
            var conditions = new CapturingDefaultConditionFactory(action, applicationHandler);
            var bindings = new WotProjectionBindingRuntimeFactory(
                channels, null, new WotProjectionEventPublisher(), conditions, new WotProjectionBindingRuntimeOptions());
            string directory = Path.Combine(Path.GetTempPath(), "w-cond-" + Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            ReferenceServer? server = null;
            try
            {
                server = await fixture.StartAsync(directory).ConfigureAwait(false);
                var host = new LifecycleWotProjectionHost(server.NodeManagerLifecycle, bindings);
                WotProjectionDocument projection = CreateConditionProjection(action, eventForm, actionForm);
                if (applicationHandler)
                {
                    ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                        async () => await host.AddAsync(projection).ConfigureAwait(false));
                    Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                    Assert.That(error.Message, Does.Contain("invocation handler"));
                    Assert.That(conditions.Condition, Is.Not.Null);
                    MethodState retained = action == "Enable"
                        ? conditions.Condition!.Enable! : conditions.Condition!.Disable!;
                    Assert.That(retained.OnCallMethod?.Target, Is.SameAs(conditions));
                    Assert.That(conditions.ApplicationCalls, Is.Zero);
                    Assert.That(channel.InvokeCount, Is.Zero);
                    return;
                }
                WotProjectionHandle handle = await host.AddAsync(projection).ConfigureAwait(false);
                try
                {
                    Assert.That(conditions.Condition, Is.Not.Null);
                    ConditionState condition = conditions.Condition!;
                    var standard = (MethodState)condition.FindChild(
                        server.CurrentInstance.DefaultSystemContext, QualifiedName.From(action))!;
                    using var client = new ClientFixture(NUnitTelemetryContext.Create());
                    await client.LoadClientConfigurationAsync(directory).ConfigureAwait(false);
                    using ISession session = await client.ConnectAsync(
                        new UriBuilder(Utils.UriSchemeOpcTcp, "localhost", fixture.Port).Uri, SecurityPolicies.None)
                        .ConfigureAwait(false);
                    try
                    {
                        NodeId owner = ExpandedNodeId.Parse("nsu=urn:wot-condition;s=Owner", session.NamespaceUris);
                        NodeId method = ExpandedNodeId.Parse("nsu=urn:wot-condition;s=Action", session.NamespaceUris);
                        CallResponse response = await session.CallAsync(
                            null,
                            [
                                new CallMethodRequest { ObjectId = owner, MethodId = method },
                                new CallMethodRequest { ObjectId = condition.NodeId, MethodId = standard.NodeId }
                            ], CancellationToken.None).ConfigureAwait(false);

                        Assert.That(response.Results.Count, Is.EqualTo(2));
                        Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(response.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(channel.InvokeCount, Is.EqualTo(2));
                        Assert.That(channels.OpenCount, Is.EqualTo(1));
                    }
                    finally
                    {
                        await session.CloseAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    await host.RemoveAsync(handle).ConfigureAwait(false);
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                server?.Dispose();
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private static WotCompiledForm CreateConditionForm(WotAffordanceKind kind, string name)
        {
            bool isEvent = kind == WotAffordanceKind.Event;
            WoTBindingCapabilityEnum operation = isEvent
                ? WoTBindingCapabilityEnum.SubscribeEvent : WoTBindingCapabilityEnum.InvokeAction;
            string token = isEvent ? "subscribeevent" : "invokeaction";
            return new WotCompiledForm(
                new WotBindingIdentity("test", "1", "urn:test"), kind, name,
                (isEvent ? "/events/" : "/actions/") + name + "/forms/0", operation, token,
                new WotEndpointDescriptor("test", null, -1, "test://condition-source"),
                new WotAddressingDescriptor(isEvent ? "i=2253" : name,
                    ImmutableDictionary<string, string>.Empty.Add("componentOf", "nsu=urn:source;s=Condition")),
                new WotOperationDescriptor(operation, token, isEvent ? "Monitor" : "Call"),
                new WotPayloadDescriptor("application/json", "json"), [], true,
                targetMapping: null,
                eventSelection: isEvent
                    ? new WotEventSelection(
                        [.. WotEventSelection.Default.Clauses, new WotResolvedEventSelectClause("i=2782", string.Empty)],
                        WotEventSelectionOrigin.Standard)
                    : null,
                securityFloor: null);
        }

        private static WotProjectionDocument CreateConditionProjection(
            string action, WotCompiledForm eventForm, WotCompiledForm actionForm)
        {
            var nodes = new UANodeSet
            {
                NamespaceUris = ["urn:wot-condition"],
                Models = [new ModelTableEntry { ModelUri = "urn:wot-condition", Version = "1.0.0" }],
                Items =
                [
                    new UAObject
                    {
                        NodeId = "ns=1;s=Owner", BrowseName = "1:Owner",
                        References =
                        [
                            new Reference { ReferenceType = "i=35", IsForward = false, Value = "i=85" },
                            new Reference { ReferenceType = "i=47", Value = "ns=1;s=Action" },
                            new Reference { ReferenceType = "i=40", Value = "i=58" }
                        ]
                    },
                    new UAMethod
                    {
                        NodeId = "ns=1;s=Action", BrowseName = "1:" + action, ParentNodeId = "ns=1;s=Owner",
                        References = [new Reference { ReferenceType = "i=47", IsForward = false, Value = "ns=1;s=Owner" }]
                    },
                    new UAObjectType
                    {
                        NodeId = "ns=1;s=AlarmType", BrowseName = "1:AlarmType",
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "i=45", IsForward = false,
                                Value = Ua.ObjectTypeIds.ConditionType.ToString()
                            }
                        ]
                    }
                ]
            };
            using var output = new MemoryStream();
            nodes.Write(output);
            WotBindingPlan plan = new WotBindingPlan("condition-call", [], [eventForm, actionForm], [], [])
                .WithProjectedAffordances(
                [
                    new WotProjectedAffordance(
                        WotAffordanceKind.Event, "alarm", "/events/alarm", "nsu=urn:wot-condition;s=AlarmType",
                        "nsu=urn:wot-condition;s=Owner", conditionTypeId: Ua.ObjectTypeIds.ConditionType.ToString()),
                    new WotProjectedAffordance(
                        WotAffordanceKind.Action, action, "/actions/" + action, "nsu=urn:wot-condition;s=Action",
                        "nsu=urn:wot-condition;s=Owner", conditionAction: action, actsOn: "alarm")
                ]);
            return new WotProjectionDocument(
                "condition-call",
                [new WotProjectionSource("condition-call", ["urn:wot-condition"], output.ToArray())],
                [plan]);
        }

        private sealed class CapturingDefaultConditionFactory(string action, bool applicationHandler)
            : IWotProjectionConditionFactory
        {
            public ConditionState? Condition { get; private set; }

            public int ApplicationCalls { get; private set; }

            public async ValueTask<ConditionState> CreateAsync(
                INodeManagerBuilder builder,
                BaseObjectState notifier,
                WotProjectedAffordance declaration,
                NodeId eventTypeId,
                CancellationToken cancellationToken = default)
            {
                Condition = await new WotProjectionConditionFactory().CreateAsync(
                    builder, notifier, declaration, eventTypeId, cancellationToken).ConfigureAwait(false);
                if (applicationHandler)
                {
                    var method = (MethodState)Condition.FindChild(builder.Context, QualifiedName.From(action))!;
                    method.OnCallMethod = (_, _, _, _) =>
                    {
                        ApplicationCalls++;
                        return ServiceResult.Bad;
                    };
                }
                return Condition;
            }
        }
    }
}
