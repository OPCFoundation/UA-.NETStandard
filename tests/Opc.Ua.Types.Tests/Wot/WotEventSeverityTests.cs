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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Exercises the event-severity mapping of WoT Binding Section 6.6:
    /// <c>uav:severity</c> is the EventType's own <c>Severity</c> Property, and
    /// its OPC 10000-5 range is a bound rather than a hint.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotEventSeverityTests
    {
        [Test]
        public void TheEventTypesSeverityPropertyBecomesTheTerm()
        {
            using WotDocument document =
                WotNodeSetConverter.FromNodeSet(CreateEventNodeSet("700"));

            Assert.That(
                document.Events["OverTemperatureEventType"]
                    .GetProperty("uav:severity").GetInt32(),
                Is.EqualTo(700));
        }

        /// <summary>
        /// An EventType that authors no default carries no term: Section 6.6
        /// says an omitted term means the server applies its own default, which
        /// is not the same claim as an authored one.
        /// </summary>
        [Test]
        public void AnEventTypeWithoutASeverityPropertyCarriesNoTerm()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(CreateEventNodeSet(null));

            Assert.That(
                document.Events["OverTemperatureEventType"]
                    .TryGetProperty("uav:severity", out _),
                Is.False);
        }

        /// <summary>
        /// A Property whose value OPC 10000-5 does not admit is not written as
        /// a term the specification would then make the document invalid for.
        /// </summary>
        [TestCase("0")]
        [TestCase("1001")]
        [TestCase("not-a-number")]
        public void AnOutOfRangeSeverityPropertyIsNotEmittedAsTheTerm(string severity)
        {
            using WotDocument document =
                WotNodeSetConverter.FromNodeSet(CreateEventNodeSet(severity));

            Assert.That(
                document.Events["OverTemperatureEventType"]
                    .TryGetProperty("uav:severity", out _),
                Is.False);
        }

        [TestCase(1)]
        [TestCase(500)]
        [TestCase(1000)]
        public void AnAuthoredSeverityMaterializesAsTheSeverityProperty(int severity)
        {
            WotConversionResult<UANodeSet> result = Convert(EventWithSeverity(
                severity.ToString(CultureInfo.InvariantCulture)));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);

            UAVariable property = result.Value.Items.OfType<UAVariable>()
                .Single(v => string.Equals(v.BrowseName, "Severity", StringComparison.Ordinal));
            Assert.That(property.DataType, Is.EqualTo("i=5"));
            Assert.That(property.Value.LocalName, Is.EqualTo("UInt16"));
            Assert.That(
                property.Value.InnerText,
                Is.EqualTo(severity.ToString(CultureInfo.InvariantCulture)));

            UAObjectType eventType = result.Value.Items.OfType<UAObjectType>()
                .Single(t => t.BrowseName.EndsWith(
                    ":OverTemperatureEventType", StringComparison.Ordinal));
            Assert.That(
                eventType.References.Any(r =>
                    string.Equals(r.ReferenceType, "HasProperty", StringComparison.Ordinal) &&
                    r.IsForward &&
                    string.Equals(r.Value, property.NodeId, StringComparison.Ordinal)),
                Is.True,
                "The EventType holds the Property it declares.");
            Assert.That(property.ParentNodeId, Is.EqualTo(eventType.NodeId));
        }

        /// <summary>
        /// The range is a bound: a value outside it is rejected, and rejected
        /// means neither materialized nor quietly moved to the nearest legal
        /// value, because a clamped severity is a number the author never wrote.
        /// </summary>
        [TestCase("0")]
        [TestCase("1001")]
        [TestCase("-1")]
        [TestCase("65536")]
        [TestCase("500.5")]
        [TestCase("\"700\"")]
        public void AnOutOfRangeSeverityIsRejectedRatherThanClamped(string severity)
        {
            WotConversionResult<UANodeSet> result = Convert(EventWithSeverity(severity));

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidEventSeverity &&
                    d.Severity == WotDiagnosticSeverity.Error),
                Is.True);
            Assert.That(
                result.Value.Items.OfType<UAVariable>()
                    .Any(v => string.Equals(
                        v.BrowseName, "Severity", StringComparison.Ordinal)),
                Is.False,
                "Nothing is materialized from a severity OPC 10000-5 does not admit.");
        }

        /// <summary>
        /// Reported is not the same as dropped: the rejected value is still
        /// carried, so the document a consumer reads back says what its author
        /// wrote rather than silently losing it.
        /// </summary>
        [Test]
        public void ARejectedSeverityIsStillCarried()
        {
            WotConversionResult<UANodeSet> result = Convert(EventWithSeverity("1001"));

            using WotDocument restored = WotNodeSetConverter.FromNodeSet(result.Value);

            Assert.That(
                restored.Events["OverTemperatureEventType"]
                    .GetProperty("uav:severity").GetInt32(),
                Is.EqualTo(1001));
        }

        /// <summary>
        /// Section 7 gives the term the event affordance as its domain: on a
        /// property or an action it states the default severity of something
        /// that has no occurrences.
        /// </summary>
        [Test]
        public void ASeverityOutsideAnEventAffordanceIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"speed\":{\"type\":\"number\",\"uav:severity\":500}}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidEventSeverity),
                Is.True);
        }

        /// <summary>
        /// A term the converter materializes must not also be carried as
        /// residue, or the same default would be stated twice - once as the
        /// Property the EventType gained and once as an Extension re-applied
        /// over the document generated from it.
        /// </summary>
        [Test]
        public void AMaterializedSeverityIsNotAlsoResidue()
        {
            WotConversionResult<UANodeSet> result = Convert(EventWithSeverity("700"));

            string extensions = result.Value.Extensions is null
                ? string.Empty
                : string.Concat(result.Value.Extensions.Select(e => e.OuterXml));
            Assert.That(extensions, Does.Not.Contain("uav:severity"));
        }

        [Test]
        public void SeveritySurvivesTheRoundTripAndStaysImportable()
        {
            UANodeSet source = CreateEventNodeSet("700");

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);
            UANodeSet restored =
                WotNodeSetConverter.ToNodeSet(WithoutNativeProjection(document));

            UAVariable property = restored.Items.OfType<UAVariable>()
                .Single(v => string.Equals(v.BrowseName, "Severity", StringComparison.Ordinal));
            Assert.That(property.Value.InnerText, Is.EqualTo("700"));

            WotNodeSetImportTests.AssertImportable(restored, "event severity");
        }

        /// <summary>
        /// The specification's own Thing Model states the severity of both its
        /// events, so converting it materializes both defaults. Until the term
        /// was mapped the example exercised nothing at all: its severities sat
        /// in an opaque configuration member the converter carried unread.
        /// </summary>
        [Test]
        public void ThePublishedThingModelMaterializesBothAuthoredSeverities()
        {
            using WotDocument document = WotDocument.Parse(ReadExample(ThingModelExample));

            Assert.That(
                document.Events["overTemperature"].GetProperty("uav:severity").GetInt32(),
                Is.EqualTo(500));

            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);
            Assert.That(
                result.Value.Items.OfType<UAVariable>()
                    .Where(v => string.Equals(
                        v.BrowseName, "Severity", StringComparison.Ordinal))
                    .Select(v => v.Value.InnerText)
                    .OrderBy(v => v, StringComparer.Ordinal),
                Is.EqualTo(s_publishedSeverities));
        }

        private static string EventWithSeverity(string severity)
        {
            return "\"events\":{\"overTemperature\":{\"@type\":\"uav:eventType\"," +
                "\"uav:isEvent\":true," +
                "\"uav:browseName\":\"pump:OverTemperatureEventType\"," +
                "\"uav:severity\":" + severity + "}}";
        }

        /// <summary>
        /// Builds a type generating one EventType, which declares its own
        /// Severity Property when a value is supplied.
        /// </summary>
        private static UANodeSet CreateEventNodeSet(string severity)
        {
            var items = new System.Collections.Generic.List<UANode>
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
                }
            };

            var eventReferences = new System.Collections.Generic.List<Reference>
            {
                new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=2041" }
            };
            if (severity is not null)
            {
                eventReferences.Add(new Reference
                {
                    ReferenceType = "HasProperty",
                    IsForward = true,
                    Value = "ns=1;i=6001"
                });
                var value = WotTestData.ParseValue(
                    "<uax:UInt16 xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
                    severity + "</uax:UInt16>");
                items.Add(new UAVariable
                {
                    NodeId = "ns=1;i=6001",
                    BrowseName = "Severity",
                    DisplayName = [new Export.LocalizedText { Value = "Severity" }],
                    ParentNodeId = "ns=1;i=1002",
                    DataType = "i=5",
                    AccessLevel = 1,
                    Value = value,
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
                });
            }

            items.Insert(1, new UAObjectType
            {
                NodeId = "ns=1;i=1002",
                BrowseName = "1:OverTemperatureEventType",
                DisplayName =
                    [new Export.LocalizedText { Value = "OverTemperatureEventType" }],
                References = [.. eventReferences]
            });

            return new UANodeSet
            {
                NamespaceUris = ["urn:test:model"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:model" }],
                Items = [.. items]
            };
        }

        private static WotDocument WithoutNativeProjection(WotDocument document)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (JsonProperty member in document.RootElement.EnumerateObject())
                {
                    if (member.Name is "uav:nodes" or "uav:nodeSet")
                    {
                        continue;
                    }
                    writer.WritePropertyName(member.Name);
                    member.Value.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            return WotDocument.Parse(buffer.ToArray());
        }

        private static WotConversionResult<UANodeSet> Convert(string members)
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
            return WotNodeSetConverter.ToNodeSetResult(document);
        }

        private static byte[] ReadExample(string name)
        {
            string resource = typeof(WotEventSeverityTests).Assembly
                .GetManifestResourceNames()
                .Single(n => n.EndsWith("Wot.Assets." + name, StringComparison.Ordinal));
            using Stream stream = typeof(WotEventSeverityTests).Assembly
                .GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing fixture '{name}'.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        private static readonly string[] s_publishedSeverities = ["500", "700"];

        private const string ThingModelExample = "02-thing-model-pump.jsonld";
    }
}
