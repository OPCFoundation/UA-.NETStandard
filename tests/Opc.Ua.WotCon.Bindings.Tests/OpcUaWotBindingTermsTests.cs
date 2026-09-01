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
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Covers the WoT Binding 1.1 runtime terms the OPC UA binding compiles:
    /// the event select-clause list of Section 6.1, its documented default and
    /// the superseded <c>uav:eventFields</c> spelling, and the <c>auto</c>
    /// endpoint security floor of Section 5.7.1 together with its deterministic
    /// tie-break and fail-closed enforcement.
    /// </summary>
    [TestFixture]
    public sealed class OpcUaWotBindingTermsTests
    {
        [Test]
        public void AnEventWithoutASelectionCompilesTheDocumentedDefault()
        {
            WotBindingCompilation result = CompileEvent("{}");

            Assert.That(result.IsSupported, Is.True);
            WotEventSelection selection = result.Entries[0].EventSelection!;
            Assert.Multiple(() =>
            {
                Assert.That(selection.Origin, Is.EqualTo(WotEventSelectionOrigin.Default));
                Assert.That(selection.Clauses.Count, Is.EqualTo(8));
                Assert.That(
                    selection.Clauses.ToList().Select(c => c.BrowsePath),
                    Is.EqualTo(WotEventSelectClauses.DefaultFieldNames.ToList()));
            });
        }

        [Test]
        public void StandardSelectClausesReplaceTheDefaultInOrder()
        {
            WotBindingCompilation result = CompileEvent(
                "{\"uav:eventSelectClauses\":[" +
                "{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"Time\"}," +
                "{\"uav:typeDefinitionId\":\"i=2782\",\"uav:browsePath\":\"EnabledState/Id\"}," +
                "{\"uav:typeDefinitionId\":\"i=2782\",\"uav:browsePath\":\"\"}]}");

            Assert.That(result.IsSupported, Is.True);
            WotEventSelection selection = result.Entries[0].EventSelection!;
            Assert.Multiple(() =>
            {
                Assert.That(selection.Origin, Is.EqualTo(WotEventSelectionOrigin.Standard));
                Assert.That(selection.Clauses.Count, Is.EqualTo(3));
                Assert.That(selection.Clauses[0].TypeDefinitionId, Is.EqualTo("i=2041"));
                Assert.That(selection.Clauses[1].BrowsePath, Is.EqualTo("EnabledState/Id"));
                Assert.That(selection.Clauses[2].IsConditionIdSelection, Is.True);
                Assert.That(selection.Clauses[2].FieldName, Is.EqualTo("ConditionId"));
            });
        }

        [Test]
        public void ACompactPathElementResolvesThroughTheDocumentContext()
        {
            var context = new WotBindingPlanContext(
                namespacePrefixes: ImmutableDictionary<string, string>.Empty
                    .Add("pump", "http://example.com/demo/pump")
                    .Add("ua", "http://opcfoundation.org/UA/"));

            WotBindingCompilation result = CompileEvent(
                "{\"uav:eventSelectClauses\":[" +
                "{\"uav:typeDefinitionId\":\"nsu=http://example.com/demo/pump;i=6001\"," +
                "\"uav:browsePath\":\"pump:Temperature\"}," +
                "{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"ua:Message\"}]}",
                context);

            Assert.That(result.IsSupported, Is.True);
            WotEventSelection selection = result.Entries[0].EventSelection!;
            Assert.Multiple(() =>
            {
                Assert.That(
                    selection.Clauses[0].BrowsePath,
                    Is.EqualTo("nsu=http://example.com/demo/pump;Temperature"));
                Assert.That(selection.Clauses[1].BrowsePath, Is.EqualTo("Message"),
                    "A base-namespace element needs no qualification.");
            });
        }

        [Test]
        public void APathElementNamespaceIsEscapedSoItSurvivesTheSeparator()
        {
            var context = new WotBindingPlanContext(
                namespacePrefixes: ImmutableDictionary<string, string>.Empty
                    .Add("odd", "urn:example:a;b"));

            WotBindingCompilation result = CompileEvent(
                "{\"uav:eventSelectClauses\":[" +
                "{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"odd:Temperature\"}]}",
                context);

            Assert.That(result.IsSupported, Is.True);
            Assert.That(
                result.Entries[0].EventSelection!.Clauses[0].BrowsePath,
                Is.EqualTo("nsu=urn:example:a%3Bb;Temperature"),
                "';' terminates the NamespaceUri, so it is percent-escaped exactly as every " +
                "other nsu= producer escapes it.");
        }

        [Test]
        public void TwoClausesRequestingOnePathAreReported()
        {
            WotBindingCompilation result = CompileEvent(
                "{\"uav:eventSelectClauses\":[" +
                "{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"Message\"}," +
                "{\"uav:typeDefinitionId\":\"i=2782\",\"uav:browsePath\":\"Message\"}]}");

            Assert.That(result.IsSupported, Is.True);
            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotBindingDiagnosticCode.ConflictingFields),
                Is.True,
                Describe(result));
        }

        [Test]
        public void AFloorOnASchemeACombinationReferencesIsHonoured()
        {
            WotBindingPlanRequest request = RequestFrom(
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"securityDefinitions\":{" +
                "\"opcua_auto_sc\":{\"scheme\":\"auto\"," +
                "\"uav:minimumSecurity\":{\"uav:securityMode\":\"SignAndEncrypt\"}}," +
                "\"opcua_user_sc\":{\"scheme\":\"basic\"}," +
                "\"opcua_sc\":{\"scheme\":\"combo\"," +
                "\"allOf\":[\"opcua_auto_sc\",\"opcua_user_sc\"]}}," +
                "\"security\":\"opcua_sc\"," +
                "\"properties\":{\"speed\":{\"type\":\"number\",\"forms\":[" +
                "{\"href\":\"opc.tcp://server.example.com:4840\",\"uav:id\":\"i=2258\"," +
                "\"op\":[\"readproperty\"]}]}}}");

            WotBindingCompilation result = new OpcUaBindingPlanner().Compile(
                request.Forms[0],
                request.CreateContext(WotPayloadCodecRegistry.Default, WotBindingBounds.Default));

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].SecurityFloor, Is.Not.Null,
                "A floor stated on a scheme the combo references binds every form that " +
                "references the combo.");
            Assert.That(
                result.Entries[0].SecurityFloor!.SecurityMode, Is.EqualTo("SignAndEncrypt"));
        }

        [Test]
        public void AnUnboundPathPrefixFailsTheForm()
        {
            WotBindingCompilation result = CompileEvent(
                "{\"uav:eventSelectClauses\":[" +
                "{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"pump:Temperature\"}]}");

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSupported, Is.False);
                Assert.That(
                    result.Diagnostics.Any(d =>
                        d.Code == WotBindingDiagnosticCode.UnboundNamespacePrefix),
                    Is.True,
                    Describe(result));
            });
        }

        [Test]
        public void AClauseCarryingAWhereClauseFailsTheForm()
        {
            WotBindingCompilation result = CompileEvent(
                "{\"uav:eventSelectClauses\":[" +
                "{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"EventId\"," +
                "\"uav:whereClause\":{\"op\":\"OfType\"}}]}");

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSupported, Is.False);
                Assert.That(
                    result.Diagnostics.Any(d =>
                        d.Code == WotBindingDiagnosticCode.EventSelectClauseInvalid &&
                        d.Message.Contains("WhereClause", StringComparison.Ordinal)),
                    Is.True,
                    Describe(result));
            });
        }

        [Test]
        public void SelectClausesAuthoredOnAFormFailTheForm()
        {
            WotAffordanceForm form = MakeEventForm(
                "{\"href\":\"opc.tcp://server.example.com:4840\",\"uav:id\":\"i=2253\"," +
                "\"uav:eventSelectClauses\":[{\"uav:typeDefinitionId\":\"i=2041\"," +
                "\"uav:browsePath\":\"EventId\"}]}",
                "{}");

            WotBindingCompilation result = new OpcUaBindingPlanner().Compile(
                form, new WotBindingPlanContext());

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSupported, Is.False);
                Assert.That(
                    result.Diagnostics.Any(d =>
                        d.Code == WotBindingDiagnosticCode.EventSelectClauseInvalid &&
                        d.Message.Contains("not on a form", StringComparison.Ordinal)),
                    Is.True,
                    Describe(result));
            });
        }

        [Test]
        public void SelectClausesOnAPropertyAffordanceFailTheForm()
        {
            WotAffordanceForm form = MakePropertyForm(
                "{\"href\":\"opc.tcp://server.example.com:4840\",\"uav:id\":\"i=2258\"," +
                "\"op\":\"readproperty\"}",
                "{\"uav:eventSelectClauses\":[{\"uav:typeDefinitionId\":\"i=2041\"," +
                "\"uav:browsePath\":\"EventId\"}]}");

            WotBindingCompilation result = new OpcUaBindingPlanner().Compile(
                form, new WotBindingPlanContext());

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotBindingDiagnosticCode.EventSelectClauseInvalid),
                Is.True,
                Describe(result));
        }

        [Test]
        public void TheStandardTermWinsOverTheSupersededSpellingAndTheConflictIsReported()
        {
            WotAffordanceForm form = MakeEventForm(
                "{\"href\":\"opc.tcp://server.example.com:4840\",\"uav:id\":\"i=2253\"," +
                "\"uav:eventFields\":[\"LocalTime\"]}",
                "{\"uav:eventSelectClauses\":[{\"uav:typeDefinitionId\":\"i=2041\"," +
                "\"uav:browsePath\":\"EventId\"}]}");

            WotBindingCompilation result = new OpcUaBindingPlanner().Compile(
                form, new WotBindingPlanContext());

            Assert.That(result.IsSupported, Is.True);
            WotEventSelection selection = result.Entries[0].EventSelection!;
            Assert.Multiple(() =>
            {
                Assert.That(selection.Origin, Is.EqualTo(WotEventSelectionOrigin.Standard));
                Assert.That(selection.Clauses.Count, Is.EqualTo(1),
                    "The standardized list is complete; the superseded spelling is not merged.");
                Assert.That(
                    result.Diagnostics.Any(d =>
                        d.Code == WotBindingDiagnosticCode.ConflictingFields),
                    Is.True,
                    Describe(result));
            });
        }

        [Test]
        public void TheSupersededSpellingAloneIsReadAndReported()
        {
            WotBindingCompilation result = CompileEventWithForm(
                "{\"href\":\"opc.tcp://server.example.com:4840\",\"uav:id\":\"i=2253\"," +
                "\"uav:eventFields\":[\"LocalTime\"]}",
                "{}");

            Assert.That(result.IsSupported, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Entries[0].EventSelection!.Origin,
                    Is.EqualTo(WotEventSelectionOrigin.Legacy));
                Assert.That(
                    result.Diagnostics.Any(d =>
                        d.Code == WotBindingDiagnosticCode.UnknownVocabularyTerm),
                    Is.True,
                    Describe(result));
            });
        }

        [Test]
        public void AFormReferencingAnAutoSchemeCarriesItsFloor()
        {
            WotBindingPlanRequest request = RequestFrom(
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"securityDefinitions\":{\"opcua_auto_sc\":{\"scheme\":\"auto\"," +
                "\"uav:minimumSecurity\":{\"uav:securityMode\":\"Sign\"," +
                "\"uav:securityPolicy\":\"Basic256Sha256\"}}}," +
                "\"security\":\"opcua_auto_sc\"," +
                "\"properties\":{\"speed\":{\"type\":\"number\",\"forms\":[" +
                "{\"href\":\"opc.tcp://server.example.com:4840\",\"uav:id\":\"i=2258\"," +
                "\"op\":\"readproperty\"}]}}}");

            WotBindingCompilation result = new OpcUaBindingPlanner().Compile(
                request.Forms[0],
                request.CreateContext(WotPayloadCodecRegistry.Default, WotBindingBounds.Default));

            Assert.That(result.IsSupported, Is.True);
            WotSecurityFloor floor = result.Entries[0].SecurityFloor!;
            Assert.Multiple(() =>
            {
                Assert.That(floor.SecurityMode, Is.EqualTo("Sign"));
                Assert.That(floor.SecurityPolicy, Is.EqualTo("Basic256Sha256"));
            });
        }

        [Test]
        public void AFloorOnANonAutoSchemeFailsTheForm()
        {
            WotBindingPlanRequest request = RequestFrom(
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"securityDefinitions\":{\"opcua_channel_sc\":{\"scheme\":\"uav:channelsec\"," +
                "\"uav:minimumSecurity\":{\"uav:securityMode\":\"Sign\"}}}," +
                "\"security\":\"opcua_channel_sc\"," +
                "\"properties\":{\"speed\":{\"type\":\"number\",\"forms\":[" +
                "{\"href\":\"opc.tcp://server.example.com:4840\",\"uav:id\":\"i=2258\"," +
                "\"op\":\"readproperty\"}]}}}");

            WotBindingCompilation result = new OpcUaBindingPlanner().Compile(
                request.Forms[0],
                request.CreateContext(WotPayloadCodecRegistry.Default, WotBindingBounds.Default));

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSupported, Is.False);
                Assert.That(
                    result.Diagnostics.Any(d =>
                        d.Code == WotBindingDiagnosticCode.InvalidSecurityFloor),
                    Is.True,
                    Describe(result));
            });
        }

        [Test]
        public void AnUnconstrainedDocumentCarriesNoFloor()
        {
            WotBindingPlanRequest request = RequestFrom(
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"securityDefinitions\":{\"opcua_auto_sc\":{\"scheme\":\"auto\"}}," +
                "\"security\":\"opcua_auto_sc\"," +
                "\"properties\":{\"speed\":{\"type\":\"number\",\"forms\":[" +
                "{\"href\":\"opc.tcp://server.example.com:4840\",\"uav:id\":\"i=2258\"," +
                "\"op\":\"readproperty\"}]}}}");

            WotBindingCompilation result = new OpcUaBindingPlanner().Compile(
                request.Forms[0],
                request.CreateContext(WotPayloadCodecRegistry.Default, WotBindingBounds.Default));

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].SecurityFloor, Is.Null);
        }

        [Test]
        public void EndpointSelectionDiscardsEverythingBelowTheFloor()
        {
            EndpointDescription[] endpoints =
            [
                Endpoint("opc.tcp://a", MessageSecurityMode.None, SecurityPolicies.None, 0),
                Endpoint("opc.tcp://b", MessageSecurityMode.Sign, SecurityPolicies.Basic256, 10)
            ];

            EndpointDescription? selected = OpcUaWotEndpointSelector.Select(
                endpoints, new WotSecurityFloor("Sign", "Basic256Sha256"));

            Assert.That(selected, Is.Null,
                "A client shall fail rather than fall back below a stated floor.");
        }

        [Test]
        public void EndpointSelectionTakesTheStrongestModeThenPolicy()
        {
            EndpointDescription[] endpoints =
            [
                Endpoint("opc.tcp://a", MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicies.Basic256Sha256, 90),
                Endpoint("opc.tcp://b", MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicies.Aes256_Sha256_RsaPss, 10),
                Endpoint("opc.tcp://c", MessageSecurityMode.Sign,
                    SecurityPolicies.Aes256_Sha256_RsaPss, 99)
            ];

            EndpointDescription? selected = OpcUaWotEndpointSelector.Select(
                endpoints, new WotSecurityFloor("Sign", null));

            Assert.That(selected!.EndpointUrl, Is.EqualTo("opc.tcp://b"));
        }

        [Test]
        public void EndpointSelectionBreaksATieBySecurityLevelThenUrlThenPosition()
        {
            EndpointDescription[] byLevel =
            [
                Endpoint("opc.tcp://a", MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256, 1),
                Endpoint("opc.tcp://b", MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256, 5)
            ];
            EndpointDescription[] byUrl =
            [
                Endpoint("opc.tcp://z", MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256, 5),
                Endpoint("opc.tcp://a", MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256, 5)
            ];
            EndpointDescription[] byPosition =
            [
                Endpoint("opc.tcp://same", MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256, 5),
                Endpoint("opc.tcp://same", MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256, 5)
            ];

            Assert.Multiple(() =>
            {
                Assert.That(
                    OpcUaWotEndpointSelector.Select(byLevel, null)!.EndpointUrl,
                    Is.EqualTo("opc.tcp://b"));
                Assert.That(
                    OpcUaWotEndpointSelector.Select(byUrl, null)!.EndpointUrl,
                    Is.EqualTo("opc.tcp://a"));
                Assert.That(
                    OpcUaWotEndpointSelector.Select(byPosition, null),
                    Is.SameAs(byPosition[0]));
            });
        }

        [Test]
        public void AnUnnamedPolicyRanksBelowEveryNamedOne()
        {
            EndpointDescription[] endpoints =
            [
                Endpoint("opc.tcp://vendor", MessageSecurityMode.Sign,
                    "http://example.com/policy#Vendor", 99),
                Endpoint("opc.tcp://named", MessageSecurityMode.Sign, SecurityPolicies.None, 1)
            ];

            EndpointDescription? selected = OpcUaWotEndpointSelector.Select(endpoints, null);

            Assert.That(selected!.EndpointUrl, Is.EqualTo("opc.tcp://named"));
        }

        [Test]
        public void ActivateFailsClosedWhenTheSelectedEndpointIsBelowTheFloor()
        {
            Mock<ISession> session = MockSession(
                MessageSecurityMode.None, SecurityPolicies.None, "opc.tcp://weak");
            var executor = new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
            {
                SessionFactory = (endpoint, ct) => new ValueTask<ISession>(session.Object)
            });

            Assert.That(
                async () => await executor
                    .ActivateAsync(BuildForm(new WotSecurityFloor("Sign", "Basic256Sha256")),
                        new WotExecutorContext())
                    .ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>());
            session.Verify(s => s.Dispose(), Times.Once, "A rejected session is not leaked.");
        }

        [Test]
        public async Task ActivateAcceptsAnEndpointAtOrAboveTheFloorAsync()
        {
            Mock<ISession> session = MockSession(
                MessageSecurityMode.SignAndEncrypt,
                SecurityPolicies.Aes256_Sha256_RsaPss,
                "opc.tcp://strong");
            var executor = new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
            {
                SessionFactory = (endpoint, ct) => new ValueTask<ISession>(session.Object),
                DisposeSession = false
            });

            IWotBindingChannel channel = await executor
                .ActivateAsync(BuildForm(new WotSecurityFloor("Sign", "Basic256Sha256")),
                    new WotExecutorContext())
                .ConfigureAwait(false);

            Assert.That(channel, Is.Not.Null);
            await channel.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task TheConstrainedFactoryReceivesTheFloorAsync()
        {
            WotSecurityFloor? received = null;
            Mock<ISession> session = MockSession(
                MessageSecurityMode.SignAndEncrypt,
                SecurityPolicies.Aes256_Sha256_RsaPss,
                "opc.tcp://strong");
            var executor = new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
            {
                ConstrainedSessionFactory = (request, ct) =>
                {
                    received = request.MinimumSecurity;
                    return new ValueTask<ISession>(session.Object);
                },
                DisposeSession = false
            });

            IWotBindingChannel channel = await executor
                .ActivateAsync(BuildForm(new WotSecurityFloor("Sign", null)),
                    new WotExecutorContext())
                .ConfigureAwait(false);

            Assert.That(received, Is.Not.Null);
            Assert.That(received!.SecurityMode, Is.EqualTo("Sign"));
            await channel.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public void ActivateFailsClosedWhenTheSessionCannotStateItsEndpoint()
        {
            var session = new Mock<ISession>();
            var executor = new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
            {
                SessionFactory = (endpoint, ct) => new ValueTask<ISession>(session.Object)
            });

            Assert.That(
                async () => await executor
                    .ActivateAsync(BuildForm(new WotSecurityFloor("Sign", null)),
                        new WotExecutorContext())
                    .ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>());
        }

        private static Mock<ISession> MockSession(
            MessageSecurityMode mode, string policyUri, string endpointUrl)
        {
            var session = new Mock<ISession>();
            session.SetupGet(s => s.ConfiguredEndpoint).Returns(new ConfiguredEndpoint(
                null,
                new EndpointDescription
                {
                    EndpointUrl = endpointUrl,
                    SecurityMode = mode,
                    SecurityPolicyUri = policyUri
                },
                null));
            return session;
        }

        private static EndpointDescription Endpoint(
            string url, MessageSecurityMode mode, string policyUri, byte level)
        {
            return new EndpointDescription
            {
                EndpointUrl = url,
                SecurityMode = mode,
                SecurityPolicyUri = policyUri,
                SecurityLevel = level
            };
        }

        private static WotCompiledForm BuildForm(WotSecurityFloor floor)
        {
            return new WotCompiledForm(
                new WotBindingIdentity("opc.opcua", "10101", OpcUaBindingPlanner.BindingUri),
                WotAffordanceKind.Property,
                "speed",
                "/properties/speed/forms/0",
                WoTBindingCapabilityEnum.ReadProperty,
                "readproperty",
                new WotEndpointDescriptor("opc.tcp", "example.test", 4840, "opc.tcp://example.test"),
                new WotAddressingDescriptor("i=2258"),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.ReadProperty, "readproperty", "Read"),
                new WotPayloadDescriptor("application/json", "json"),
                ImmutableArray<WotCredentialReference>.Empty,
                isExecutable: true,
                targetMapping: null,
                eventSelection: null,
                securityFloor: floor);
        }

        private static WotBindingPlanRequest RequestFrom(string json)
        {
            return WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(json));
        }

        private static WotBindingCompilation CompileEvent(string affordanceJson)
        {
            return CompileEvent(affordanceJson, new WotBindingPlanContext());
        }

        private static WotBindingCompilation CompileEvent(
            string affordanceJson, WotBindingPlanContext context)
        {
            return CompileEventWithForm(
                "{\"href\":\"opc.tcp://server.example.com:4840\",\"uav:id\":\"i=2253\"}",
                affordanceJson,
                context);
        }

        private static WotBindingCompilation CompileEventWithForm(
            string formJson, string affordanceJson)
        {
            return CompileEventWithForm(formJson, affordanceJson, new WotBindingPlanContext());
        }

        private static WotBindingCompilation CompileEventWithForm(
            string formJson, string affordanceJson, WotBindingPlanContext context)
        {
            return new OpcUaBindingPlanner().Compile(
                MakeEventForm(formJson, affordanceJson), context);
        }

        private static WotAffordanceForm MakeEventForm(string formJson, string affordanceJson)
        {
            using var formDocument = JsonDocument.Parse(formJson);
            using var affordanceDocument = JsonDocument.Parse(affordanceJson);
            JsonElement formElement = formDocument.RootElement.Clone();
            return new WotAffordanceForm(
                WotAffordanceKind.Event,
                "overTemperature",
                ["subscribeevent", "unsubscribeevent"],
                formElement.TryGetProperty("href", out JsonElement href) ? href.GetString() : null,
                null,
                null,
                [],
                "/events/overTemperature/forms/0",
                formElement,
                affordanceDocument.RootElement.Clone());
        }

        private static WotAffordanceForm MakePropertyForm(string formJson, string affordanceJson)
        {
            using var formDocument = JsonDocument.Parse(formJson);
            using var affordanceDocument = JsonDocument.Parse(affordanceJson);
            JsonElement formElement = formDocument.RootElement.Clone();
            return new WotAffordanceForm(
                WotAffordanceKind.Property,
                "speed",
                ["readproperty"],
                formElement.TryGetProperty("href", out JsonElement href) ? href.GetString() : null,
                null,
                null,
                [],
                "/properties/speed/forms/0",
                formElement,
                affordanceDocument.RootElement.Clone());
        }

        private static string Describe(WotBindingCompilation result)
        {
            return string.Join("; ", result.Diagnostics.Select(d => d.ToString()));
        }
    }
}
