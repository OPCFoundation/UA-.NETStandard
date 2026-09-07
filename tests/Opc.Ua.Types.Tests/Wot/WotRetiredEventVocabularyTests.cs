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
 *
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
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

#nullable enable

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Pins the retirement of <c>uav:isEvent</c> and <c>uav:severity</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Neither term is defined by WoT Binding 1.1. Event identity is stated by
    /// the <c>@type: uav:eventType</c> annotation alone, and no default
    /// severity metadata exists: <c>Severity</c> is a field of an occurrence,
    /// declared by <c>BaseEventType</c>, and appears in the notification data
    /// schema rather than as affordance metadata.
    /// </para>
    /// <para>
    /// A term that is neither emitted nor understood is not thereby forgotten.
    /// A legacy document that carries one is still consumed permissively - the
    /// member survives as ordinary unknown residue - while strict authoring
    /// reports it, because a retired term and a misspelled one are
    /// indistinguishable to a consumer that simply drops what it cannot read.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotRetiredEventVocabularyTests
    {
        private const string IsEventTerm = "uav:isEvent";
        private const string SeverityTerm = "uav:severity";

        [Test]
        public void AnEventAffordanceStatesItsIdentityWithTheTypeAnnotationAlone()
        {
            using WotDocument document =
                WotNodeSetConverter.FromNodeSet(CreateEventNodeSet("700"));

            JsonElement affordance = document.Events["OverTemperatureEventType"];
            Assert.Multiple(() =>
            {
                Assert.That(
                    affordance.GetProperty("@type").GetString(),
                    Is.EqualTo(WotVocabulary.EventTypeAnnotation));
                Assert.That(affordance.TryGetProperty(IsEventTerm, out _), Is.False);
            });
        }

        [Test]
        public void AnAuthoredSeverityPropertyIsNeverEmittedAsAffordanceMetadata()
        {
            using WotDocument document =
                WotNodeSetConverter.FromNodeSet(CreateEventNodeSet("700"));

            Assert.That(
                document.Events["OverTemperatureEventType"]
                    .TryGetProperty(SeverityTerm, out _),
                Is.False);
        }

        /// <summary>
        /// A document that projects the EventType Node itself carries the same
        /// identity, so the root form of the retired flag is gone as well.
        /// </summary>
        [Test]
        public void AnEventTypeRootStatesNoRetiredFlag()
        {
            using WotDocument document =
                WotNodeSetConverter.FromNodeSet(CreateEventTypeOnlyNodeSet());

            Assert.Multiple(() =>
            {
                Assert.That(document.RootElement.TryGetProperty(IsEventTerm, out _), Is.False);
                Assert.That(document.RootElement.TryGetProperty(SeverityTerm, out _), Is.False);
            });
        }

        [TestCase(IsEventTerm, "true")]
        [TestCase(SeverityTerm, "700")]
        public void ALegacyTermIsCarriedAsOrdinaryResidue(string term, string value)
        {
            WotConversionResult<UANodeSet> result = Convert(EventWith(term, value));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);

            string extensions = result.Value!.Extensions is null
                ? string.Empty
                : string.Concat(result.Value.Extensions.Select(e => e.OuterXml));
            Assert.That(extensions, Does.Contain(term));
        }

        [TestCase(IsEventTerm, "true")]
        [TestCase(SeverityTerm, "700")]
        public void ALegacyTermSurvivesTheRoundTripUnchanged(string term, string value)
        {
            WotConversionResult<UANodeSet> result = Convert(EventWith(term, value));
            using WotDocument restored = WotNodeSetConverter.FromNodeSet(result.Value!);

            JsonElement affordance = restored.Events["OverTemperatureEventType"];
            Assert.That(affordance.TryGetProperty(term, out JsonElement carried), Is.True);
            Assert.That(
                carried.GetRawText(),
                Is.EqualTo(value));
        }

        /// <summary>
        /// A materialized Severity Property would state a default the author
        /// never wrote as an OPC UA fact, so nothing is synthesized.
        /// </summary>
        [Test]
        public void ALegacySeverityMaterializesNoNode()
        {
            WotConversionResult<UANodeSet> result = Convert(EventWith(SeverityTerm, "700"));

            Assert.That(
                result.Value!.Items!.OfType<UAVariable>()
                    .Any(v => string.Equals(
                        v.BrowseName, "Severity", StringComparison.Ordinal)),
                Is.False);
        }

        [TestCase(IsEventTerm)]
        [TestCase(SeverityTerm)]
        public void ARetiredTermIsNotAKnownTerm(string term)
        {
            Assert.That(WotBindingConformance.IsKnownTerm(term), Is.False);
        }

        [TestCase(IsEventTerm, "true")]
        [TestCase(SeverityTerm, "700")]
        public void StrictAuthoringReportsARetiredTermAsUnknown(string term, string value)
        {
            WotConversionResult<UANodeSet> result = Convert(
                EventWith(term, value),
                new WotNodeSetConverterOptions
                {
                    ConformanceMode = WotConformanceMode.Strict
                });

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.UnknownVocabularyTerm &&
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Message.Contains(term, StringComparison.Ordinal)),
                Is.True);
        }

        /// <summary>
        /// The two codes the retired terms used are reserved rather than
        /// renumbered, so a consumer that persisted either number still reads
        /// it back as the same member.
        /// </summary>
        [Test]
        public void TheRetiredDiagnosticCodesKeepTheirNumbers()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    (int)WotDiagnosticCode.EventAnnotationConflict, Is.EqualTo(6002));
                Assert.That(
                    (int)WotDiagnosticCode.InvalidEventSeverity, Is.EqualTo(6029));
            });
        }

        /// <summary>
        /// Nothing in the library raises either reserved code any more.
        /// </summary>
        [TestCase(IsEventTerm, "true")]
        [TestCase(SeverityTerm, "700")]
        [TestCase(SeverityTerm, "1001")]
        [TestCase(SeverityTerm, "\"high\"")]
        public void NoDiagnosticCarriesAReservedCode(string term, string value)
        {
            WotConversionResult<UANodeSet> permissive = Convert(EventWith(term, value));
            WotConversionResult<UANodeSet> strict = Convert(
                EventWith(term, value),
                new WotNodeSetConverterOptions
                {
                    ConformanceMode = WotConformanceMode.Strict
                });

            Assert.That(
                permissive.Diagnostics.Concat(strict.Diagnostics)
                    .Select(d => d.Code),
                Has.None.AnyOf(
                    WotDiagnosticCode.EventAnnotationConflict,
                    WotDiagnosticCode.InvalidEventSeverity));
        }

        /// <summary>
        /// The retired flag once contradicted the annotation; now it is inert
        /// residue and the affordance still projects an EventType.
        /// </summary>
        [Test]
        public void AContradictoryLegacyFlagNoLongerChangesTheMapping()
        {
            WotConversionResult<UANodeSet> result = Convert(EventWith(IsEventTerm, "false"));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);
            Assert.That(
                result.Value!.Items!.OfType<UAObjectType>()
                    .Any(t => string.Equals(
                        t.BrowseName,
                        "1:OverTemperatureEventType",
                        StringComparison.Ordinal)),
                Is.True);
        }

        private static string EventWith(string term, string value)
        {
            return "\"events\":{\"overTemperature\":{\"@type\":\"uav:eventType\"," +
                "\"uav:browseName\":\"pump:OverTemperatureEventType\"," +
                "\"" + term + "\":" + value + "}}";
        }

        /// <summary>
        /// Builds a type generating one EventType that declares its own
        /// Severity Property.
        /// </summary>
        private static UANodeSet CreateEventNodeSet(string severity)
        {
            var items = new List<UANode>
            {
                new UAObjectType
                {
                    NodeId = "ns=1;i=1001",
                    BrowseName = "1:MachineType",
                    DisplayName = [new Export.LocalizedText { Value = "MachineType" }],
                    References =
                    [
                        new Reference
                        {
                            ReferenceType = "HasSubtype", IsForward = false, Value = "i=58"
                        },
                        new Reference
                        {
                            ReferenceType = "GeneratesEvent",
                            IsForward = true,
                            Value = "ns=1;i=1002"
                        }
                    ]
                },
                new UAObjectType
                {
                    NodeId = "ns=1;i=1002",
                    BrowseName = "1:OverTemperatureEventType",
                    DisplayName =
                        [new Export.LocalizedText { Value = "OverTemperatureEventType" }],
                    References =
                    [
                        new Reference
                        {
                            ReferenceType = "HasSubtype", IsForward = false, Value = "i=2041"
                        },
                        new Reference
                        {
                            ReferenceType = "HasProperty",
                            IsForward = true,
                            Value = "ns=1;i=6001"
                        }
                    ]
                },
                new UAVariable
                {
                    NodeId = "ns=1;i=6001",
                    BrowseName = "Severity",
                    DisplayName = [new Export.LocalizedText { Value = "Severity" }],
                    ParentNodeId = "ns=1;i=1002",
                    DataType = "i=5",
                    AccessLevel = 1,
                    Value = WotTestData.ParseValue(
                        "<uax:UInt16 xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
                        severity + "</uax:UInt16>"),
                    References =
                    [
                        new Reference
                        {
                            ReferenceType = "HasTypeDefinition", IsForward = true, Value = "i=68"
                        },
                        new Reference
                        {
                            ReferenceType = "HasProperty",
                            IsForward = false,
                            Value = "ns=1;i=1002"
                        }
                    ]
                }
            };

            return new UANodeSet
            {
                NamespaceUris = ["urn:test:model"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:model" }],
                Items = [.. items]
            };
        }

        /// <summary>
        /// Builds a NodeSet whose only type is an EventType, so the generated
        /// document's root projects that EventType.
        /// </summary>
        private static UANodeSet CreateEventTypeOnlyNodeSet()
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:test:model"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:model" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1002",
                        BrowseName = "1:OverTemperatureEventType",
                        DisplayName =
                            [new Export.LocalizedText { Value = "OverTemperatureEventType" }],
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = "i=2041"
                            }
                        ]
                    }
                ]
            };
        }

        private static WotConversionResult<UANodeSet> Convert(
            string members,
            WotNodeSetConverterOptions? options = null)
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"," +
                members + "}");

            using WotDocument document = WotDocument.Parse(json);
            return options is null
                ? WotNodeSetConverter.ToNodeSetResult(document)
                : WotNodeSetConverter.ToNodeSetResult(document, options);
        }
    }
}
