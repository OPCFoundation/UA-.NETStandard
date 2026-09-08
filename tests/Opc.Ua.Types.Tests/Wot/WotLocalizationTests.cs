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

#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Covers the localized-text mapping of WoT Binding Section 9.1.1: every
    /// locale of a <c>DisplayName</c> and a <c>Description</c> survives through
    /// <c>title</c>/<c>titles</c> and <c>description</c>/<c>descriptions</c>,
    /// with a stated default locale and a deterministic fallback.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotLocalizationTests
    {
        [Test]
        public void MultilingualRootProjectsTitlesAndDescriptions()
        {
            UANodeSet source = CreateMultilingualNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            JsonElement root = document.RootElement;
            Assert.Multiple(() =>
            {
                Assert.That(root.GetProperty("title").GetString(), Is.EqualTo("Pump"));
                Assert.That(
                    root.GetProperty("titles").GetProperty("en").GetString(),
                    Is.EqualTo("Pump"));
                Assert.That(
                    root.GetProperty("titles").GetProperty("de").GetString(),
                    Is.EqualTo("Pumpe"));
                Assert.That(
                    root.GetProperty("description").GetString(),
                    Is.EqualTo("A pump."));
                Assert.That(
                    root.GetProperty("descriptions").GetProperty("de").GetString(),
                    Is.EqualTo("Eine Pumpe."));
            });
        }

        [Test]
        public void TheDocumentDeclaresTheDefaultLocaleItProjects()
        {
            UANodeSet source = CreateMultilingualNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(document.TryGetContext(out JsonElement context), Is.True);
            Assert.That(context.ValueKind, Is.EqualTo(JsonValueKind.Array));
            JsonElement bindings = context[1];
            Assert.That(bindings.GetProperty("@language").GetString(), Is.EqualTo("en"));
        }

        [Test]
        public void TheSingularMemberIsAlwaysTheDefaultLocaleEntry()
        {
            UANodeSet source = CreateMultilingualNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            JsonElement speed = document.Properties["Speed"];
            Assert.Multiple(() =>
            {
                Assert.That(
                    speed.GetProperty("title").GetString(),
                    Is.EqualTo(speed.GetProperty("titles").GetProperty("en").GetString()),
                    "Restating one value in two places is only safe while the two agree.");
                Assert.That(
                    speed.GetProperty("titles").GetProperty("fr").GetString(),
                    Is.EqualTo("Vitesse"));
            });
        }

        [Test]
        public void ADefaultLocaleThatIsNotEnglishIsHonoured()
        {
            UANodeSet source = CreateMultilingualNodeSet(rootLocale: "de");

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(document.TryGetContext(out JsonElement context), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(
                    context[1].GetProperty("@language").GetString(),
                    Is.EqualTo("de"),
                    "The locale the root states is the locale the document states.");
                Assert.That(
                    document.RootElement.GetProperty("title").GetString(),
                    Is.EqualTo("Pumpe"),
                    "The singular member is the default-locale projection.");
            });
        }

        [Test]
        public void ASingleLocaleRoundTripsThroughTheSingularMemberAlone()
        {
            UANodeSet source = WotAnalogTestData.CreateAnalogNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.Multiple(() =>
            {
                Assert.That(
                    document.RootElement.GetProperty("title").GetString(),
                    Is.EqualTo("AnalogDeviceType"));
                Assert.That(
                    document.RootElement.TryGetProperty("titles", out _),
                    Is.False,
                    "One locale needs no map.");
            });
        }

        [Test]
        public void EveryLocaleSurvivesTheNodeSetRoundTrip()
        {
            UANodeSet source = CreateMultilingualNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);

            UANode root = restored.Items!.First(n => n is UAObjectType);
            Assert.Multiple(() =>
            {
                Assert.That(root.DisplayName, Has.Length.EqualTo(2));
                Assert.That(
                    root.DisplayName![0].Locale,
                    Is.EqualTo("en"),
                    "The default locale's entry is the one the Node's own " +
                    "DisplayName carries.");
                Assert.That(root.DisplayName![0].Value, Is.EqualTo("Pump"));
                Assert.That(
                    root.DisplayName!.Any(t => t.Locale == "de" && t.Value == "Pumpe"),
                    Is.True);
                Assert.That(
                    root.Description!.Any(t => t.Locale == "de" && t.Value == "Eine Pumpe."),
                    Is.True);
            });
        }

        [Test]
        public void MultilingualNodeSetNeedsNoStructuredFallback()
        {
            UANodeSet source = CreateMultilingualNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(
                document.RootElement.TryGetProperty("uav:nodes", out _),
                Is.False,
                "Every locale is expressible readably now.");
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);
            Assert.That(
                NodeSetComparer.CompareEquivalent(
                    source, restored).AreEquivalent,
                Is.True);
        }

        [Test]
        public void ALocaleSetWithoutTheDefaultLocaleIsStatedReadably()
        {
            UANodeSet source = CreateMultilingualNodeSet(
                speedTitle: WotAnalogTestData.Text(("fr", "Vitesse"), ("de", "Drehzahl")));

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            JsonElement speed = document.Properties["Speed"];
            Assert.Multiple(() =>
            {
                Assert.That(
                    speed.TryGetProperty("titles", out JsonElement titles),
                    Is.True,
                    "Section 9.1.1 makes the plural member authoritative, so every " +
                    "locale the source carries is stated readably even where the " +
                    "default locale is not among them.");
                Assert.That(titles.GetProperty("de").GetString(), Is.EqualTo("Drehzahl"));
                Assert.That(titles.GetProperty("fr").GetString(), Is.EqualTo("Vitesse"));
                Assert.That(
                    speed.GetProperty("title").GetString(),
                    Is.EqualTo("Drehzahl"),
                    "'de' sorts before 'fr' by Unicode code point, so the singular " +
                    "member carries it as a display fallback that asserts no locale - " +
                    "and not the entry that happened to come first in the source.");
            });
        }

        [Test]
        public void ALocaleSetWithoutTheDefaultLocaleNeedsNoStructuredFallback()
        {
            UANodeSet source = CreateMultilingualNodeSet(
                speedTitle: WotAnalogTestData.Text(("de", "Drehzahl"), ("fr", "Vitesse")));

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(
                document.RootElement.TryGetProperty("uav:nodes", out _),
                Is.False,
                "Section 9.2: a LocalizedText whose locales do not include the " +
                "document's default locale is carried in full by the plural member, " +
                "so it is an ordinary document rather than an exceptional one.");
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);
            Assert.That(
                NodeSetComparer.CompareEquivalent(
                    source, restored).AreEquivalent,
                Is.True);
        }

        [Test]
        public void ALocaleSetWithoutTheDefaultLocaleIsValidAgainstTheBinding()
        {
            using WotDocument document = WotUnitsAndRangesTests.ParseThingModel(
                "\"title\":\"Pumpendrehzahl\"," +
                "\"titles\":{\"de\":\"Pumpendrehzahl\",\"fr\":\"Vitesse de la pompe\"}");

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                document,
                new WotNodeSetConverterOptions
                {
                    ConformanceMode = WotConformanceMode.Strict
                });

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.InvalidLocalizedText),
                Is.False,
                "Requiring the default locale would make the commonest real NodeSet - " +
                "one authored in the plant's language - unrepresentable readably " +
                "(Section 9.1.1). " +
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void ASingularMemberThatIsNotTheCodePointFirstFallbackIsReported()
        {
            using WotDocument document = WotUnitsAndRangesTests.ParseThingModel(
                "\"title\":\"Vitesse de la pompe\"," +
                "\"titles\":{\"de\":\"Pumpendrehzahl\",\"fr\":\"Vitesse de la pompe\"}");

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                document,
                new WotNodeSetConverterOptions
                {
                    ConformanceMode = WotConformanceMode.Strict
                });

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.InvalidLocalizedText),
                Is.True,
                "The fallback is the code-point-first entry and not an arbitrary one, " +
                "or two consumers would present different text from one document.");
        }

        [Test]
        public void LocalizedMethodArgumentsSurviveBothDirections()
        {
            using WotDocument original = WotUnitsAndRangesTests.ParseThingModel(
                "\"actions\":{\"reset\":{\"@type\":\"uav:method\",\"title\":\"Reset\"," +
                "\"input\":{\"type\":\"object\"," +
                "\"uav:fieldOrder\":[\"Reason\"]," +
                "\"properties\":{\"Reason\":{\"type\":\"string\"," +
                "\"description\":\"Why the pump was reset.\"}}}}}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(original);
            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);

            JsonElement reason = restored.Actions["reset"]
                .GetProperty("input")
                .GetProperty("properties")
                .GetProperty("Reason");
            Assert.That(
                reason.GetProperty("description").GetString(),
                Is.EqualTo("Why the pump was reset."));
        }

        [Test]
        public void LocalizedEventFieldsSurviveBothDirections()
        {
            using WotDocument original = WotUnitsAndRangesTests.ParseThingModel(
                "\"events\":{\"overTemp\":{\"@type\":\"uav:eventType\"," +
                "\"title\":\"Over Temperature\"," +
                "\"uav:browseName\":\"pump:OverTemperatureEventType\"," +
                "\"data\":{\"type\":\"object\",\"properties\":{" +
                "\"Temperature\":{\"type\":\"number\",\"title\":\"Temperature\"," +
                "\"description\":\"The temperature that tripped the event.\"," +
                "\"uav:browseName\":\"pump:Temperature\"}}}}}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(original);
            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);

            JsonElement temperature = restored.Events["OverTemperatureEventType"]
                .GetProperty("data")
                .GetProperty("properties")
                .GetProperty("Temperature");
            Assert.Multiple(() =>
            {
                Assert.That(
                    temperature.GetProperty("title").GetString(),
                    Is.EqualTo("Temperature"));
                Assert.That(
                    temperature.GetProperty("description").GetString(),
                    Is.EqualTo("The temperature that tripped the event."));
            });
        }

        [Test]
        public void LocalizedDataTypeFieldsAndEnumsSurviveBothDirections()
        {
            using WotDocument original = WotUnitsAndRangesTests.ParseThingModel(
                "\"uav:dataTypeDefinitions\":[" +
                "{\"@id\":\"nsu=urn:test:pump;i=3001\"," +
                "\"@type\":\"uav:StructureDefinition\"," +
                "\"uav:dataTypeName\":\"pump:Reading\"," +
                "\"title\":\"Reading\",\"description\":\"One reading.\"," +
                "\"uav:structureType\":\"Structure\"," +
                "\"uav:fields\":[{\"@type\":\"uav:StructureField\"," +
                "\"uav:fieldName\":\"Value\",\"uav:fieldDataTypeId\":\"i=11\"," +
                "\"title\":\"Value\",\"description\":\"The measured value.\"}]}," +
                "{\"@id\":\"nsu=urn:test:pump;i=3002\"," +
                "\"@type\":\"uav:EnumDefinition\"," +
                "\"uav:dataTypeName\":\"pump:State\"," +
                "\"uav:enumFields\":[{\"@type\":\"uav:EnumField\"," +
                "\"uav:enumName\":\"Idle\",\"uav:enumValue\":0," +
                "\"title\":\"Idle\",\"description\":\"Not running.\"}]}]");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(original);

            UADataType reading = nodeSet.Items!.OfType<UADataType>()
                .Single(d => d.BrowseName!.EndsWith(":Reading", StringComparison.Ordinal));
            UADataType state = nodeSet.Items!.OfType<UADataType>()
                .Single(d => d.BrowseName!.EndsWith(":State", StringComparison.Ordinal));
            Assert.Multiple(() =>
            {
                Assert.That(reading.Definition!.Field![0].DisplayName![0].Value, Is.EqualTo("Value"));
                Assert.That(
                    reading.Definition!.Field![0].Description![0].Value,
                    Is.EqualTo("The measured value."));
                Assert.That(state.Definition!.Field![0].DisplayName![0].Value, Is.EqualTo("Idle"));
            });

            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);
            JsonElement definitions = restored.RootElement
                .GetProperty("uav:dataTypeDefinitions");
            JsonElement structure = definitions.EnumerateArray()
                .Single(d => d.GetProperty("uav:dataTypeName").GetString()!
                    .EndsWith(":Reading", StringComparison.Ordinal));
            JsonElement enumeration = definitions.EnumerateArray()
                .Single(d => d.GetProperty("uav:dataTypeName").GetString()!
                    .EndsWith(":State", StringComparison.Ordinal));
            Assert.Multiple(() =>
            {
                Assert.That(
                    structure.GetProperty("uav:fields")[0].GetProperty("title").GetString(),
                    Is.EqualTo("Value"),
                    "A field's DisplayName was silently dropped before.");
                Assert.That(
                    structure.GetProperty("uav:fields")[0]
                        .GetProperty("description").GetString(),
                    Is.EqualTo("The measured value."));
                Assert.That(
                    enumeration.GetProperty("uav:enumFields")[0]
                        .GetProperty("title").GetString(),
                    Is.EqualTo("Idle"));
            });
        }

        [Test]
        public void TitlesWithoutTitleIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"speed\":{\"type\":\"number\"," +
                "\"titles\":{\"en\":\"Speed\"}}}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidLocalizedText),
                Is.True);
        }

        [Test]
        public void TitlesWithoutADefaultLocaleEntryIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"speed\":{\"type\":\"number\",\"title\":\"Speed\"," +
                "\"titles\":{\"de\":\"Drehzahl\"}}}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidLocalizedText),
                Is.True,
                "The singular member would then state a locale the plural " +
                "member denies.");
        }

        [Test]
        public void TitleDisagreeingWithTheDefaultLocaleEntryIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"speed\":{\"type\":\"number\",\"title\":\"Speed\"," +
                "\"titles\":{\"en\":\"Velocity\",\"de\":\"Drehzahl\"}}}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidLocalizedText),
                Is.True);
        }

        [Test]
        public void DescriptionsThatAreNotAMapAreReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"speed\":{\"type\":\"number\"," +
                "\"description\":\"Speed\",\"descriptions\":[\"Speed\"]}}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidLocalizedText),
                Is.True);
        }

        [Test]
        public void ValidPluralMembersProduceNoDiagnostic()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"speed\":{\"type\":\"number\",\"title\":\"Speed\"," +
                "\"titles\":{\"en\":\"Speed\",\"de\":\"Drehzahl\"}," +
                "\"description\":\"The speed.\"," +
                "\"descriptions\":{\"en\":\"The speed.\",\"de\":\"Die Drehzahl.\"}}}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidLocalizedText),
                Is.False,
                WotAnalogTestData.Describe(result.Diagnostics));
            Assert.That(result.Value, Is.Not.Null);

            UAVariable speed = result.Value!.Items!.OfType<UAVariable>()
                .Single(v => v.BrowseName == "1:speed");
            Assert.Multiple(() =>
            {
                Assert.That(speed.DisplayName, Has.Length.EqualTo(2));
                Assert.That(speed.DisplayName![0].Locale, Is.EqualTo("en"));
                Assert.That(speed.Description, Has.Length.EqualTo(2));
            });
        }

        private static UANodeSet CreateMultilingualNodeSet(
            string rootLocale = "en",
            Export.LocalizedText[]? speedTitle = null)
        {
            // A NodeSet2 document may only use a name where a NodeId is
            // expected if it declares that name, so the fixture declares what
            // it uses and is a document a Server could load.
            return NodeSetAliasCompleter.Complete(new UANodeSet
            {
                NamespaceUris = ["urn:test:pump"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:pump" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1000",
                        BrowseName = "1:PumpType",
                        DisplayName = rootLocale == "de"
                            ? WotAnalogTestData.Text(("de", "Pumpe"), ("en", "Pump"))
                            : WotAnalogTestData.Text(("en", "Pump"), ("de", "Pumpe")),
                        Description = rootLocale == "de"
                            ? WotAnalogTestData.Text(
                                ("de", "Eine Pumpe."), ("en", "A pump."))
                            : WotAnalogTestData.Text(
                                ("en", "A pump."), ("de", "Eine Pumpe.")),
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = "i=58"
                            },
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = true,
                                Value = "ns=1;i=1001"
                            }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=1001",
                        BrowseName = "1:Speed",
                        DisplayName = speedTitle ??
                            WotAnalogTestData.Text(
                                ("en", "Speed"), ("de", "Drehzahl"), ("fr", "Vitesse")),
                        ParentNodeId = "ns=1;i=1000",
                        DataType = "i=11",
                        AccessLevel = 1,
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasTypeDefinition",
                                IsForward = true,
                                Value = "i=63"
                            },
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = false,
                                Value = "ns=1;i=1000"
                            }
                        ]
                    }
                ]
            }, WotNodeSetAliases.Instance)!;
        }

        private static WotConversionResult<UANodeSet> Convert(string members)
        {
            using WotDocument document = WotUnitsAndRangesTests.ParseThingModel(members);
            return WotNodeSetConverter.ToNodeSetResult(document);
        }
    }
}
