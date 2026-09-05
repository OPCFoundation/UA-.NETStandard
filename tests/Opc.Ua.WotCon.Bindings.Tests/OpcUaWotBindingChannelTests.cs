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
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Bindings.Planners;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Covers previously-uncovered paths in <c>OpcUaWotBindingChannel</c> against a
    /// live in-process OPC UA reference server: invalid / malformed NodeId resolution
    /// returning <c>BadNodeIdInvalid</c>, null-callback argument guards, null-inputs
    /// mapping, non-Good DataValue preservation, extra event fields in
    /// <c>BuildEventFilter</c>, and <c>ServiceResultException</c> propagation from
    /// failed subscription creation.
    /// </summary>
    [TestFixture]
    public sealed class OpcUaWotBindingChannelTests
    {
        private const string ReferenceServerNamespace =
            "http://opcfoundation.org/Quickstarts/ReferenceServer";

        private const string AddMethodNodeId =
            "nsu=" + ReferenceServerNamespace + ";s=Methods_Add";

        private const string MethodsObjectNodeId =
            "nsu=" + ReferenceServerNamespace + ";s=Methods";

        private const string TriggerNode01Id =
            "nsu=" + ReferenceServerNamespace + ";s=NodeIds_Events_TriggerNode01";

        private const string ServerObjectNodeId =
            "nsu=http://opcfoundation.org/UA/;i=2253";

        /// <summary>
        /// A string that cannot be parsed as a NodeId or ExpandedNodeId so that
        /// <c>TryResolveNodeId</c> returns <c>false</c>.
        /// </summary>
        private const string InvalidNodeId = "INVALID-NOT-A-NODE";

        /// <summary>
        /// The ordinal-sorted transport-index keys the <c>selectclauses</c>
        /// affordance yields, and nothing else. The affordance links to no
        /// EventType definition, so Section 6.1 makes the four clauses it
        /// writes the <em>complete</em> selection: three named fields and the
        /// empty path that selects <c>ConditionId</c>. The implicit
        /// <c>BaseEventType</c> default is what an affordance that states no
        /// selection at all falls back to, not a floor under an authored one.
        /// The key is the joined browse path the selection carries, so the
        /// NamespaceUri-qualified clause appears in full: its URI carries '/'
        /// but the path has one element, not four.
        /// </summary>
        private static readonly string[] s_authoredSelectClauseFields =
        [
            "ConditionId",
            "Message",
            "SourceName",
            "nsu=http://opcfoundation.org/UA/;Severity"
        ];

        /// <summary>
        /// The ordinal-sorted <c>data</c> member names those same clauses
        /// materialize into. A member name drops the namespace qualification,
        /// so a URI slash never nests a field under a fragment of its own
        /// NamespaceUri.
        /// </summary>
        private static readonly string[] s_authoredSelectClauseMembers =
        [
            "ConditionId",
            "Message",
            "Severity",
            "SourceName"
        ];

        private ServerFixture<ReferenceServer> m_serverFixture = null!;
        private ISession m_session = null!;
        private WotProtocolBinderRegistry m_registry = null!;
        private WotBindingPlan m_plan = null!;

        /// <summary>
        /// Starts an in-process reference server, connects a single shared session,
        /// and compiles a Thing Description that exposes edge-case affordances for every
        /// uncovered channel path.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string pkiRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            m_serverFixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = "opc.tcp",
                SecurityNone = true,
                AutoAccept = true,
                AllNodeManagers = true
            };
            await m_serverFixture.StartAsync(pkiRoot).ConfigureAwait(false);

            var clientFixture = new ClientFixture(telemetry);
            await clientFixture.LoadClientConfigurationAsync(pkiRoot).ConfigureAwait(false);
            var url = new Uri("opc.tcp://localhost:" +
                m_serverFixture.Port.ToString(CultureInfo.InvariantCulture));
            m_session = await clientFixture.ConnectAsync(url, SecurityPolicies.None).ConfigureAwait(false);

            m_registry = new WotProtocolBinderRegistry(
                [new OpcUaBindingPlanner()],
                [
                    new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                    {
                        SessionFactory = (endpoint, ct) => new ValueTask<ISession>(m_session),
                        DisposeSession = false,
                        ObserveInterval = TimeSpan.FromMilliseconds(100)
                    })
                ],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });

            m_plan = m_registry.Prepare(await WotBindingPlanRequest.FromDocumentAsync(
                "xid",
                WoTDocumentKindEnum.ThingDescription,
                System.Text.Encoding.UTF8.GetBytes(BuildThingDescription(url.ToString())),
                new BaseEventTypeResolver()).ConfigureAwait(false));

            Assert.That(m_plan.Diagnostics.Any(d => d.IsError), Is.False,
                "The channel-test Thing Description must compile without diagnostic errors: " +
                string.Join("; ", m_plan.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        }

        /// <summary>
        /// Closes the shared session and stops the server.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_session is not null)
            {
                await m_session.CloseAsync().ConfigureAwait(false);
                m_session.Dispose();
            }

            if (m_serverFixture is not null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ReadAsyncWithMalformedNodeIdReturnsBadNodeIdInvalidAsync()
        {
            WotCompiledForm form = m_plan.CompiledForms.First(
                f => f.AffordanceName == "badid" && f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await m_registry.OpenChannelAsync(form).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);

                Assert.That(result.Success, Is.False,
                    "A malformed NodeId must produce a BadNodeIdInvalid read result.");
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            }
        }

        [Test]
        public async Task ReadAsyncWithNonExistentNodeIdPreservesServerBadStatusAsync()
        {
            WotCompiledForm form = m_plan.CompiledForms.First(
                f => f.AffordanceName == "nonexistent" && f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await m_registry.OpenChannelAsync(form).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);

                Assert.That(result.Success, Is.False,
                    "Reading a well-formed but non-existent NodeId must return a bad DataValue status.");
                Assert.That(StatusCode.IsBad(result.Status), Is.True);
            }
        }

        [Test]
        public async Task WriteAsyncWithMalformedNodeIdReturnsBadNodeIdInvalidAsync()
        {
            WotCompiledForm form = m_plan.CompiledForms.First(
                f => f.AffordanceName == "badid" && f.Operation == WoTBindingCapabilityEnum.WriteProperty);

            IWotBindingChannel channel = await m_registry.OpenChannelAsync(form).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotWriteResult result = await channel.WriteAsync(new DataValue(new Variant(42)))
                    .ConfigureAwait(false);

                Assert.That(result.Success, Is.False,
                    "A malformed NodeId must produce a BadNodeIdInvalid write result.");
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            }
        }

        [Test]
        public async Task InvokeAsyncWithoutComponentOfMetadataReturnsBadNodeIdInvalidAsync()
        {
            WotCompiledForm form = m_plan.CompiledForms.First(
                f => f.AffordanceName == "nocomponentof" &&
                     f.Operation == WoTBindingCapabilityEnum.InvokeAction);

            IWotBindingChannel channel = await m_registry.OpenChannelAsync(form).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
                Assert.That(result.Error, Does.Contain("uav:componentOf"),
                    "The error message must name the missing uav:componentOf field.");
            }
        }

        [Test]
        public async Task InvokeAsyncWithInvalidComponentOfNodeIdReturnsBadNodeIdInvalidAsync()
        {
            WotCompiledForm form = m_plan.CompiledForms.First(
                f => f.AffordanceName == "badcomponentof" &&
                     f.Operation == WoTBindingCapabilityEnum.InvokeAction);

            IWotBindingChannel channel = await m_registry.OpenChannelAsync(form).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

                Assert.That(result.Success, Is.False,
                    "An unparseable uav:componentOf NodeId must produce BadNodeIdInvalid.");
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            }
        }

        [Test]
        public async Task InvokeAsyncWithInvalidMethodNodeIdReturnsBadNodeIdInvalidAsync()
        {
            WotCompiledForm form = m_plan.CompiledForms.First(
                f => f.AffordanceName == "badmethodid" &&
                     f.Operation == WoTBindingCapabilityEnum.InvokeAction);

            IWotBindingChannel channel = await m_registry.OpenChannelAsync(form).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

                Assert.That(result.Success, Is.False,
                    "An unparseable method NodeId must produce BadNodeIdInvalid.");
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            }
        }

        [Test]
        public async Task InvokeAsyncWithNullInputsPassesEmptyArgumentsAndMapsServerErrorAsync()
        {
            WotCompiledForm form = m_plan.CompiledForms.First(
                f => f.AffordanceName == "nullinputs" &&
                     f.Operation == WoTBindingCapabilityEnum.InvokeAction);

            IWotBindingChannel channel = await m_registry.OpenChannelAsync(form).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                // null inputs → 'inputs is null ? [] : [.. inputs]' takes the empty-array branch;
                // Methods_Add requires 2 arguments so the server rejects the call → bad result.
                WotInvokeResult result = await channel.InvokeAsync(null!).ConfigureAwait(false);

                Assert.That(result.Success, Is.False,
                    "Calling Methods_Add with null (zero) inputs must fail server-side and return a bad result.");
            }
        }

        [Test]
        public async Task ObserveAsyncWithNullCallbackThrowsArgumentNullExceptionAsync()
        {
            WotCompiledForm form = m_plan.CompiledForms.First(
                f => f.AffordanceName == "badid" && f.Operation == WoTBindingCapabilityEnum.ObserveProperty);

            IWotBindingChannel channel = await m_registry.OpenChannelAsync(form).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                Assert.That(
                    async () => await channel.ObserveAsync(null!).ConfigureAwait(false),
                    Throws.InstanceOf<ArgumentNullException>());
            }
        }

        [Test]
        public async Task ObserveAsyncWithMalformedNodeIdThrowsServiceResultExceptionAsync()
        {
            WotCompiledForm form = m_plan.CompiledForms.First(
                f => f.AffordanceName == "badid" && f.Operation == WoTBindingCapabilityEnum.ObserveProperty);

            IWotBindingChannel channel = await m_registry.OpenChannelAsync(form).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                Assert.That(
                    async () => await channel.ObserveAsync(_ => { }).ConfigureAwait(false),
                    Throws.InstanceOf<ServiceResultException>());
            }
        }

        [Test]
        public async Task SubscribeEventAsyncWithNullCallbackThrowsArgumentNullExceptionAsync()
        {
            WotCompiledForm form = m_plan.CompiledForms.First(
                f => f.AffordanceName == "badevent" && f.Operation == WoTBindingCapabilityEnum.SubscribeEvent);

            IWotBindingChannel channel = await m_registry.OpenChannelAsync(form).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                Assert.That(
                    async () => await channel.SubscribeEventAsync(null!).ConfigureAwait(false),
                    Throws.InstanceOf<ArgumentNullException>());
            }
        }

        [Test]
        public async Task SubscribeEventAsyncWithMalformedNodeIdThrowsServiceResultExceptionAsync()
        {
            WotCompiledForm form = m_plan.CompiledForms.First(
                f => f.AffordanceName == "badevent" && f.Operation == WoTBindingCapabilityEnum.SubscribeEvent);

            IWotBindingChannel channel = await m_registry.OpenChannelAsync(form).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                Assert.That(
                    async () => await channel.SubscribeEventAsync(_ => { }).ConfigureAwait(false),
                    Throws.InstanceOf<ServiceResultException>());
            }
        }

        [Test]
        public async Task SubscribeEventAsyncWithExtraEventFieldsIncludesFieldInNotificationAsync()
        {
            WotCompiledForm form = m_plan.CompiledForms.First(
                f => f.AffordanceName == "extrafields" && f.Operation == WoTBindingCapabilityEnum.SubscribeEvent);

            var received = new ConcurrentQueue<WotNotification>();
            IWotBindingChannel channel = await m_registry.OpenChannelAsync(form).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                IWotSubscription subscription = await channel
                    .SubscribeEventAsync(received.Enqueue).ConfigureAwait(false);
                await using (subscription.ConfigureAwait(false))
                {
                    NodeId triggerNodeId = ResolvePortableNodeId(TriggerNode01Id);
                    var writeValue = new WriteValue
                    {
                        NodeId = triggerNodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(77))
                    };
                    WriteResponse writeResponse = await m_session
                        .WriteAsync(null, new WriteValue[] { writeValue }, CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                        "Writing the trigger node must succeed to fire a BaseEvent.");

                    WotNotification? notification = null;
                    for (int i = 0; i < 100 && notification is null; i++)
                    {
                        if (!received.TryDequeue(out notification))
                        {
                            await Task.Delay(50).ConfigureAwait(false);
                        }
                    }

                    Assert.That(notification, Is.Not.Null,
                        "The extra-fields subscription must deliver the triggered event.");
                    Assert.That(notification!.EventFields.ContainsKey("LocalTime"), Is.True,
                        "The 'LocalTime' extra uav:eventFields select clause must appear in EventFields.");
                }
            }
        }

        [Test]
        public async Task SubscribeEventAsyncSelectsExactlyTheAuthoredSelectClausesAsync()
        {
            WotCompiledForm form = m_plan.CompiledForms.First(
                f => f.AffordanceName == "selectclauses" &&
                    f.Operation == WoTBindingCapabilityEnum.SubscribeEvent);

            Assert.That(form.EventSelection, Is.Not.Null);
            Assert.That(
                form.EventSelection!.Origin, Is.EqualTo(WotEventSelectionOrigin.Standard));

            var received = new ConcurrentQueue<WotNotification>();
            IWotBindingChannel channel = await m_registry.OpenChannelAsync(form).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                IWotSubscription subscription = await channel
                    .SubscribeEventAsync(received.Enqueue).ConfigureAwait(false);
                await using (subscription.ConfigureAwait(false))
                {
                    NodeId triggerNodeId = ResolvePortableNodeId(TriggerNode01Id);
                    var writeValue = new WriteValue
                    {
                        NodeId = triggerNodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(78))
                    };
                    WriteResponse writeResponse = await m_session
                        .WriteAsync(null, new WriteValue[] { writeValue }, CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                        "Writing the trigger node must succeed to fire a BaseEvent.");

                    WotNotification? notification = null;
                    for (int i = 0; i < 100 && notification is null; i++)
                    {
                        if (!received.TryDequeue(out notification))
                        {
                            await Task.Delay(50).ConfigureAwait(false);
                        }
                    }

                    Assert.That(notification, Is.Not.Null,
                        "The select-clause subscription must deliver the triggered event.");
                    Assert.That(
                        notification!.EventFields.Keys.OrderBy(k => k, StringComparer.Ordinal),
                        Is.EqualTo(s_authoredSelectClauseFields),
                        "The affordance links to no EventType definition, so the clauses it " +
                        "writes are the complete selection: three named fields and the empty " +
                        "path that selects the ConditionId member.");
                    Assert.That(
                        notification.Data.Members.Keys.OrderBy(k => k, StringComparer.Ordinal),
                        Is.EqualTo(s_authoredSelectClauseMembers),
                        "A NamespaceUri carrying '/' is one path element, so the field is " +
                        "data.Severity and nothing is nested under 'nsu=http:', an empty " +
                        "member and 'opcfoundation.org'.");
                    Assert.That(
                        notification.Data["Severity"]!.HasValue,
                        Is.True,
                        "The qualified clause fills a value member, not an object.");
                }
            }
        }

        /// <summary>
        /// Builds a Thing Description JSON that includes edge-case affordances for every
        /// uncovered <c>OpcUaWotBindingChannel</c> path.
        /// </summary>
        private static string BuildThingDescription(string endpoint)
        {
            return
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"@type\":\"uav:object\"," +
                "\"title\":\"channel-tests\"," +
                "\"properties\":{" +
                // badid: malformed NodeId for ReadProperty / WriteProperty / ObserveProperty
                "\"badid\":{\"type\":\"integer\",\"observable\":true,\"forms\":[" +
                "{\"href\":\"" + endpoint + "\",\"uav:id\":\"" + InvalidNodeId + "\"," +
                "\"op\":[\"readproperty\"]}," +
                "{\"href\":\"" + endpoint + "\",\"uav:id\":\"" + InvalidNodeId + "\"," +
                "\"op\":[\"writeproperty\"]}," +
                "{\"href\":\"" + endpoint + "\",\"uav:id\":\"" + InvalidNodeId + "\"," +
                "\"op\":[\"observeproperty\"]}" +
                "]}," +
                // nonexistent: valid NodeId format but no such node on the server
                "\"nonexistent\":{\"type\":\"integer\",\"forms\":[" +
                "{\"href\":\"" + endpoint + "\",\"uav:id\":\"i=99999999\",\"op\":[\"readproperty\"]}" +
                "]}}," +
                "\"actions\":{" +
                // nocomponentof: missing uav:componentOf → InvokeAsync returns BadNodeIdInvalid
                "\"nocomponentof\":{\"forms\":[" +
                "{\"href\":\"" + endpoint + "\",\"uav:id\":\"i=2258\",\"op\":[\"invokeaction\"]}" +
                "]}," +
                // badcomponentof: uav:componentOf is an invalid NodeId string
                "\"badcomponentof\":{\"forms\":[" +
                "{\"href\":\"" + endpoint + "\",\"uav:id\":\"i=2258\"," +
                "\"uav:componentOf\":\"" + InvalidNodeId + "\",\"op\":[\"invokeaction\"]}" +
                "]}," +
                // badmethodid: valid componentOf but invalid method NodeId
                "\"badmethodid\":{\"forms\":[" +
                "{\"href\":\"" + endpoint + "\",\"uav:id\":\"" + InvalidNodeId + "\"," +
                "\"uav:componentOf\":\"" + MethodsObjectNodeId + "\",\"op\":[\"invokeaction\"]}" +
                "]}," +
                // nullinputs: real method invoked with null inputs → empty args → server error
                "\"nullinputs\":{\"forms\":[" +
                "{\"href\":\"" + endpoint + "\",\"uav:id\":\"" + AddMethodNodeId + "\"," +
                "\"uav:componentOf\":\"" + MethodsObjectNodeId + "\",\"op\":[\"invokeaction\"]}" +
                "]}}," +
                "\"events\":{" +
                // badevent: malformed NodeId for SubscribeEvent
                "\"badevent\":{\"forms\":[" +
                "{\"href\":\"" + endpoint + "\",\"uav:id\":\"" + InvalidNodeId + "\"," +
                "\"op\":[\"subscribeevent\"]}" +
                "]}," +
                // extrafields: real event notifier with uav:eventFields → exercises the
                // superseded spelling, which adds to the implicit BaseEventType default
                "\"extrafields\":{\"forms\":[" +
                "{\"href\":\"" + endpoint + "\",\"uav:id\":\"" + ServerObjectNodeId + "\"," +
                "\"op\":[\"subscribeevent\"],\"uav:eventFields\":[\"LocalTime\"]}" +
                "]}," +
                // selectclauses: the standardized uav:eventSelectClauses list, which
                // overlays the implicit BaseEventType default and includes the empty-path
                // ConditionId selection
                "\"selectclauses\":{\"uav:eventSelectClauses\":[" +
                "{\"tm:ref\":\"./base-event.tm.jsonld\",\"uav:browsePath\":\"Message\"}," +
                "{\"tm:ref\":\"./base-event.tm.jsonld\"," +
                "\"uav:browsePath\":\"nsu=http://opcfoundation.org/UA/;Severity\"}," +
                "{\"tm:ref\":\"./base-event.tm.jsonld\",\"uav:browsePath\":\"SourceName\"}," +
                "{\"tm:ref\":\"./base-event.tm.jsonld\",\"uav:browsePath\":\"\"}]," +
                "\"forms\":[" +
                "{\"href\":\"" + endpoint + "\",\"uav:id\":\"" + ServerObjectNodeId + "\"," +
                "\"op\":[\"subscribeevent\"]}" +
                "]}}}";
        }

        /// <summary>
        /// Resolves a portable <c>nsu=</c> NodeId string against the connected session's
        /// namespace table.
        /// </summary>
        private NodeId ResolvePortableNodeId(string value)
        {
            var expanded = ExpandedNodeId.Parse(value);
            return ExpandedNodeId.ToNodeId(expanded, m_session.NamespaceUris);
        }

        /// <summary>
        /// Serves the one EventType definition this fixture's select clauses
        /// name: the Thing Model of <c>BaseEventType</c>, which declares every
        /// standard field (WoT Binding Section 6.1).
        /// </summary>
        /// <remarks>
        /// A clause names its EventType by reference, and a reference resolves
        /// against the documents a consumer holds rather than over the network.
        /// The fixture therefore holds this one, which is what a registry
        /// closure or a document set would hold in a deployment.
        /// </remarks>
        private sealed class BaseEventTypeResolver : IWotThingResolver
        {
            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                System.Threading.CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<WotResolverResult>(
                    reference.EndsWith("base-event.tm.jsonld", StringComparison.Ordinal)
                        ? WotResolverResult.FromBytes(
                            System.Text.Encoding.UTF8.GetBytes(BaseEventTypeModel))
                        : WotResolverResult.NotFound);
            }

            private const string BaseEventTypeModel =
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:eventType\"]," +
                "\"title\":\"BaseEventType\",\"uav:id\":\"i=2041\"," +
                "\"uav:browseName\":\"BaseEventType\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"uav:fieldOrder\":[\"ConditionId\",\"Message\",\"Severity\",\"SourceName\"]," +
                "\"properties\":{" +
                "\"ConditionId\":{\"type\":\"string\"}," +
                "\"Message\":{\"type\":\"string\"}," +
                "\"Severity\":{\"uav:browseName\":" +
                "\"nsu=http://opcfoundation.org/UA/;Severity\",\"type\":\"integer\"}," +
                "\"SourceName\":{\"type\":\"string\"}}}}";
        }
    }
}
