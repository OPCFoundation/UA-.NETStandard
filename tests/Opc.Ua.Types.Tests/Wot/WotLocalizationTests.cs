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
using System.Text.Json.Nodes;
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

        [TestCase(false)]
        [TestCase(true)]
        public void ChildFallbackTextCarriesItsOwnSelectiveLanguageOverride(bool multipleLocales)
        {
            UANodeSet source = CreateMultilingualNodeSet(
                speedTitle: multipleLocales
                    ? WotAnalogTestData.Text(("de", "Drehzahl"), ("fr", "Vitesse"))
                    : WotAnalogTestData.Text(("de", "Drehzahl")));
            source.Items!.OfType<UAVariable>().Single().Description =
                WotAnalogTestData.Text(("en", "Shaft speed"));
            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            JsonElement speed = document.Properties["Speed"];
            Assert.That(speed.GetProperty("titles").GetProperty("de").GetString(), Is.EqualTo("Drehzahl"));
            Assert.That(speed.TryGetProperty("@context", out JsonElement local), Is.True);
            Assert.That(local.GetProperty("title").GetProperty("@language").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(local.TryGetProperty("description", out _), Is.False,
                "An English description must not lose its language because the title is a fallback.");
        }

        [TestCase(/*lang=json,strict*/ "{\"@language\":\"de\"}", "de")]
        [TestCase(/*lang=json,strict*/ "[{\"@language\":\"fr\"},{\"@language\":\"de\"}]", "de")]
        [TestCase("null", "")]
        public void PropertyTextUsesItsCarryingContextRatherThanTheRootLanguage(string context, string expectedLocale)
        {
            string json = $$$"""
                {
                  "@context":{
                    "uav":"http://opcfoundation.org/UA/WoT-Binding/",
                    "pump":"urn:test:pump","@language":"en"
                  },
                  "@type":["tm:ThingModel","uav:objectType"],
                  "title":"Pump","uav:id":"nsu=urn:test:pump;i=2000","uav:browseName":"pump:PumpType",
                  "properties":{
                    "Speed":{
                      "@context":{{{context}}},
                      "type":"number","uav:id":"nsu=urn:test:pump;i=2001",
                      "uav:browseName":"nsu=urn:test:pump;Speed",
                      "title":"Drehzahl","description":"Welle"
                    }
                  }
                }
                """;
            using var document = WotDocument.Parse(WotTestData.Utf8(json));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            UAVariable speed = result.Value!.Items!.OfType<UAVariable>().Single();
            Export.LocalizedText title = speed.DisplayName!.Single();
            Export.LocalizedText description = speed.Description!.Single();
            Assert.That(title.Locale, Is.EqualTo(expectedLocale));
            Assert.That(title.Value, Is.EqualTo("Drehzahl"));
            Assert.That(description.Locale, Is.EqualTo(expectedLocale));
            Assert.That(description.Value, Is.EqualTo("Welle"));

            WotConversionResult<WotDocument> exported = WotNodeSetConverter.FromNodeSetResult(result.Value);
            using WotDocument roundTrip = exported.Value!;
            Assert.That(exported.Success, Is.True, string.Join("; ", exported.Diagnostics));
            WotConversionResult<UANodeSet> restored = WotNodeSetConverter.ToNodeSetResult(roundTrip);
            Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics));
            UAVariable restoredSpeed = restored.Value!.Items!.OfType<UAVariable>().Single();
            Assert.That(restoredSpeed.DisplayName!.Single().Locale, Is.EqualTo(expectedLocale));
            Assert.That(restoredSpeed.Description!.Single().Locale, Is.EqualTo(expectedLocale));
        }

        [TestCase("de", "de")]
        [TestCase(null, "")]
        public void SingularTextUsesItsTermLanguageWithoutRelabelingTheOtherTerm(
            string? termLanguage, string expectedLocale)
        {
            string language = termLanguage is null ? "null" : "\"" + termLanguage + "\"";
            using WotDocument original = WotUnitsAndRangesTests.ParseThingModel(
                "\"properties\":{\"Speed\":{\"type\":\"number\",\"title\":\"Drehzahl\"," +
                "\"description\":\"Shaft speed\",\"@context\":{\"title\":{" +
                "\"@id\":\"https://www.w3.org/2019/wot/td#title\",\"@language\":" +
                language +
                "}}}}");
            JsonObject root = JsonNode.Parse(original.Utf8Json.Span)!.AsObject();
            root["@context"]![1]!["@language"] = "en";
            using var document = WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            UAVariable speed = result.Value!.Items!.OfType<UAVariable>().Single();
            Assert.That(speed.DisplayName!.Single().Locale, Is.EqualTo(expectedLocale));
            Assert.That(speed.Description!.Single().Locale, Is.EqualTo("en"));
            using WotDocument exported = WotNodeSetConverter.FromNodeSet(result.Value);
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(exported);
            UAVariable restoredSpeed = restored.Items!.OfType<UAVariable>().Single();
            Assert.That(restoredSpeed.DisplayName!.Single().Locale, Is.EqualTo(expectedLocale));
            Assert.That(restoredSpeed.Description!.Single().Locale, Is.EqualTo("en"));
        }

        [TestCase(70, 256, true)]
        [TestCase(30, 32, false)]
        public void ContextualPropertyPreservationHonorsCombinedDepthBounds(
            int annotationDepth, int maximumDepth, bool succeeds)
        {
            string annotation = "0";
            for (int depth = 0; depth < annotationDepth; depth++)
            {
                annotation = "{\"value\":" + annotation + "}";
            }
            using WotDocument document = WotUnitsAndRangesTests.ParseThingModel(
                "\"properties\":{\"Speed\":{\"type\":\"number\",\"title\":\"Drehzahl\"," +
                "\"@context\":{\"@language\":\"de\"},\"urn:test:annotation\":" +
                annotation +
                "}}");
            UANodeSet source = WotNodeSetConverter.ToNodeSet(document);

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                source, options: new WotNodeSetConverterOptions { MaxJsonDepth = maximumDepth });
            using WotDocument? restored = result.Value;

            Assert.That(result.Success, Is.EqualTo(succeeds), string.Join("; ", result.Diagnostics));
            if (succeeds)
            {
                JsonElement value = restored!.Properties["Speed"].GetProperty("urn:test:annotation");
                for (int depth = 0; depth < annotationDepth; depth++)
                {
                    value = value.GetProperty("value");
                }
                Assert.That(value.GetInt32(), Is.Zero);
            }
            else
            {
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Severity == WotDiagnosticSeverity.Error &&
                    diagnostic.Code == WotDiagnosticCode.ResidueInvalid), Is.True);
            }
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void ContextualPropertyPreservationHonorsTheCompleteByteLimit(int limitOffset)
        {
            using WotDocument document = ParseEnglishModel(
                "\"properties\":{\"Speed\":{\"type\":\"number\",\"title\":\"Drehzahl\"," +
                "\"@context\":{\"@language\":\"de\"},\"urn:test:annotation\":\"" +
                new string('x', 4096) +
                "\"}}");
            UANodeSet source = WotNodeSetConverter.ToNodeSet(document);
            using WotDocument expected = WotNodeSetConverter.FromNodeSet(source);
            int limit = expected.Utf8Json.Length + limitOffset;

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                source, options: new WotNodeSetConverterOptions { MaxJsonDocumentSize = limit });
            using WotDocument? actual = result.Value;

            Assert.That(result.Success, Is.EqualTo(limitOffset == 0), string.Join("; ", result.Diagnostics));
            if (limitOffset == 0)
            {
                Assert.That(actual!.Utf8Json.Length, Is.EqualTo(limit));
            }
            else
            {
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Severity == WotDiagnosticSeverity.Error &&
                    diagnostic.Code is WotDiagnosticCode.JsonDocumentTooLarge or WotDiagnosticCode.ResidueInvalid),
                    Is.True);
            }
        }

        [Test]
        public void ContextualPropertyCannotOverwriteContradictoryNativeFacts()
        {
            using WotDocument document = WotUnitsAndRangesTests.ParseThingModel(
                "\"properties\":{\"Speed\":{\"type\":\"number\",\"uav:mapToType\":\"i=11\"," +
                "\"title\":\"Drehzahl\",\"@context\":{\"@language\":\"de\"}}}");
            UANodeSet source = WotNodeSetConverter.ToNodeSet(document);
            UAVariable speed = source.Items!.OfType<UAVariable>().Single();
            speed.DataType = "i=10";

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(source);
            using WotDocument? restored = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict), Is.True);
            Assert.That(speed.DataType, Is.EqualTo("i=10"));
        }

        [Test]
        public void ContextualPropertyDoesNotDiscardUnstatedNativeFacts()
        {
            using WotDocument document = WotUnitsAndRangesTests.ParseThingModel(
                "\"properties\":{\"Speed\":{\"type\":\"number\",\"title\":\"Drehzahl\"," +
                "\"@context\":{\"@language\":\"de\",\"title\":{" +
                "\"@id\":\"https://www.w3.org/2019/wot/td#title\",\"@language\":\"de\"}}}}");
            UANodeSet source = WotNodeSetConverter.ToNodeSet(document);
            UAVariable speed = source.Items!.OfType<UAVariable>().Single();
            speed.DataType = "i=10";
            speed.AccessLevel = 1;

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(source);
            using WotDocument? exported = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(exported!);
            UAVariable restoredSpeed = restored.Items!.OfType<UAVariable>().Single();
            Assert.That(restoredSpeed.DataType, Is.EqualTo("i=10"));
            Assert.That(restoredSpeed.AccessLevel, Is.EqualTo(1));
            Assert.That(restoredSpeed.DisplayName!.Single().Locale, Is.EqualTo("de"));
        }

        [TestCase("actions", "uav:method", /*lang=json,strict*/ "{\"@language\":\"de\"}", "de")]
        [TestCase("actions", "uav:method", "null", "")]
        [TestCase("events", "uav:eventType", /*lang=json,strict*/ "{\"@language\":\"de\"}", "de")]
        [TestCase("events", "uav:eventType", "null", "")]
        public void ActionAndEventTextRetainsItsLocalContextAcrossRoundTrips(
            string collection, string nodeClass, string context, string locale)
        {
            using WotDocument document = ParseEnglishModel(
                "\"" +
                collection +
                "\":{\"Localized\":{\"@type\":\"" +
                nodeClass +
                "\"," +
                "\"@context\":" +
                context +
                ",\"title\":\"Ausloesen\",\"description\":\"Meldung\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=2001\",\"uav:browseName\":\"nsu=urn:test:pump;Localized\"}}");
            WotConversionResult<UANodeSet> imported = WotNodeSetConverter.ToNodeSetResult(document);
            Assert.That(imported.Success, Is.True, string.Join("; ", imported.Diagnostics));
            UANodeSet source = imported.Value!;
            for (int pass = 0; pass < 3; pass++)
            {
                UANode node = source.Items!.Single(item => item.NodeId == "ns=1;i=2001");
                Assert.That(node.DisplayName!.Single().Locale, Is.EqualTo(locale));
                Assert.That(node.Description!.Single().Locale, Is.EqualTo(locale));
                WotConversionResult<WotDocument> exported = WotNodeSetConverter.FromNodeSetResult(source);
                using WotDocument? roundTrip = exported.Value;
                Assert.That(exported.Success, Is.True, string.Join("; ", exported.Diagnostics));
                WotConversionResult<UANodeSet> restored = WotNodeSetConverter.ToNodeSetResult(roundTrip!);
                Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics));
                source = restored.Value!;
            }
        }

        [TestCase(/*lang=json,strict*/ "{\"@language\":\"de\"}", "de")]
        [TestCase(/*lang=json,strict*/ "[{\"@language\":\"fr\"},{\"@language\":\"de\"}]", "de")]
        [TestCase("null", "")]
        public void ArgumentDescriptionRetainsItsOwnContextAcrossRoundTrips(string context, string locale)
        {
            using WotDocument document = ParseEnglishModel(
                "\"actions\":{\"Run\":{\"@type\":\"uav:method\",\"input\":{" +
                "\"type\":\"object\",\"uav:fieldOrder\":[\"Reason\"]," +
                "\"properties\":{\"Reason\":{\"type\":\"string\"," +
                "\"description\":\"Grund\",\"@context\":" +
                context +
                "}}}}}");
            UANodeSet source = WotNodeSetConverter.ToNodeSet(document);
            for (int pass = 0; pass < 3; pass++)
            {
                UAVariable arguments = source.Items!.OfType<UAVariable>()
                    .Single(variable => variable.BrowseName == "InputArguments");
                System.Xml.XmlElement description = arguments.Value!
                    .GetElementsByTagName("Description", Namespaces.OpcUaXsd)
                    .OfType<System.Xml.XmlElement>().Single();
                string actualLocale = description.GetElementsByTagName("Locale", Namespaces.OpcUaXsd)
                    .OfType<System.Xml.XmlElement>().SingleOrDefault()?.InnerText ??
                    string.Empty;
                Assert.That(actualLocale, Is.EqualTo(locale));
                WotConversionResult<WotDocument> exported = WotNodeSetConverter.FromNodeSetResult(source);
                using WotDocument? roundTrip = exported.Value;
                Assert.That(exported.Success, Is.True, string.Join("; ", exported.Diagnostics));
                source = WotNodeSetConverter.ToNodeSet(roundTrip!);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DataTypeAndFieldLanguagesUseTheirOwnScopes(bool enumeration)
        {
            string fields = enumeration
                ? "\"uav:enumFields\":[{\"@type\":\"uav:EnumField\",\"uav:enumName\":\"Value\",\"uav:enumValue\":0,"
                : "\"uav:structureType\":\"Structure\",\"uav:fields\":[{\"@type\":\"uav:StructureField\"," +
                    "\"uav:fieldName\":\"Value\",\"uav:fieldDataTypeId\":\"i=11\",";
            string kind = enumeration ? "uav:EnumDefinition" : "uav:StructureDefinition";
            using WotDocument document = ParseEnglishModel(
                "\"uav:dataTypeDefinitions\":[{\"@id\":\"nsu=urn:test:pump;i=3001\"," +
                "\"@type\":\"" +
                kind +
                "\",\"uav:dataTypeName\":\"pump:Reading\"," +
                "\"@context\":{\"@language\":\"de\"},\"title\":\"Messung\"," +
                "\"description\":\"Eine Messung\"," +
                fields +
                "\"@context\":{\"@language\":\"fr\"},\"title\":\"Valeur\",\"description\":\"Une valeur\"}]}]");
            UANodeSet source = WotNodeSetConverter.ToNodeSet(document);
            for (int pass = 0; pass < 3; pass++)
            {
                UADataType dataType = source.Items!.OfType<UADataType>().Single();
                Assert.That(dataType.DisplayName!.Single().Locale, Is.EqualTo("de"));
                Assert.That(dataType.Description!.Single().Locale, Is.EqualTo("de"));
                DataTypeField field = dataType.Definition!.Field!.Single();
                Assert.That(field.DisplayName!.Single().Locale, Is.EqualTo("fr"));
                Assert.That(field.Description!.Single().Locale, Is.EqualTo("fr"));
                using WotDocument roundTrip = WotNodeSetConverter.FromNodeSet(source);
                source = WotNodeSetConverter.ToNodeSet(roundTrip);
            }
        }

        [Test]
        public void PropertyScopedLanguageAppliesToItsEntries()
        {
            using WotDocument original = ParseEnglishModel(
                "\"properties\":{\"Speed\":{\"type\":\"number\",\"title\":\"Drehzahl\",\"description\":\"Welle\"}}");
            JsonObject root = JsonNode.Parse(original.Utf8Json.Span)!.AsObject();
            root["@context"]!["properties"] = JsonNode.Parse(
                "{\"@id\":\"https://www.w3.org/2019/wot/td#hasPropertyAffordance\"," +
                "\"@container\":\"@index\",\"@context\":{\"@language\":\"de\"}}");
            using var document = WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));

            UANodeSet source = WotNodeSetConverter.ToNodeSet(document);

            UAVariable speed = source.Items!.OfType<UAVariable>().Single();
            Assert.That(speed.DisplayName!.Single().Locale, Is.EqualTo("de"));
            Assert.That(speed.Description!.Single().Locale, Is.EqualTo("de"));
            using WotDocument roundTrip = WotNodeSetConverter.FromNodeSet(source);
            UAVariable restored = WotNodeSetConverter.ToNodeSet(roundTrip).Items!.OfType<UAVariable>().Single();
            Assert.That(restored.DisplayName!.Single().Locale, Is.EqualTo("de"));
            Assert.That(restored.Description!.Single().Locale, Is.EqualTo("de"));
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

        [TestCase(false)]
        [TestCase(true)]
        public void ContextualPropertyRetainsItsReferencedDataTypeInTheReadableProjection(bool contextual)
        {
            string context = contextual ? "\"@context\":{\"@language\":\"en\"}," : string.Empty;
            using WotDocument document = ParseEnglishModel(
                "\"uav:dataTypeDefinitions\":[{\"@id\":\"urn:test:pump#Reading\"," +
                "\"uav:dataTypeId\":\"nsu=urn:test:pump;i=3001\",\"@type\":\"uav:StructureDefinition\"," +
                "\"uav:dataTypeName\":\"pump:Reading\",\"uav:structureType\":\"Structure\"," +
                "\"uav:fields\":[{\"@type\":\"uav:StructureField\",\"uav:fieldName\":\"Sample\"," +
                "\"uav:fieldDataTypeId\":\"i=11\"}]}]," +
                "\"properties\":{\"Speed\":{" +
                context +
                "\"type\":\"object\",\"properties\":{\"Sample\":{\"type\":\"number\"}}," +
                "\"required\":[\"Sample\"],\"uav:browseName\":\"pump:Speed\"," +
                "\"uav:dataTypeDefinition\":{\"@id\":\"urn:test:pump#Reading\"}}}");
            UANodeSet source = WotNodeSetConverter.ToNodeSet(document);

            using WotDocument exported = WotNodeSetConverter.FromNodeSet(source);
            using WotDocument readable = WithoutPreservation(exported);
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(readable);

            Assert.That(restored.Items!.OfType<UAVariable>().Single().DataType, Is.EqualTo("ns=1;i=3001"));
            Assert.That(restored.Items!.OfType<UADataType>().Single().Definition!.Field!.Single().Name,
                Is.EqualTo("Sample"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ContextualUnitPointerUsesTheRegeneratedAffordanceName(bool contextual)
        {
            string context = contextual ? "\"@context\":{\"@language\":\"de\"}," : string.Empty;
            using WotDocument document = ParseEnglishModel(
                "\"properties\":{\"speed\":{" +
                context +
                "\"type\":\"number\",\"title\":\"Drehzahl\"," +
                "\"unit\":\"rpm\",\"uav:unitProperty\":\"/properties/speedUnit\"}," +
                "\"speedUnit\":{\"type\":\"string\",\"uav:browseName\":\"ua:EngineeringUnits\"," +
                "\"uav:engineeringUnits\":{\"namespaceUri\":\"" +
                WotAnalogTestData.UnitAuthority +
                "\"," +
                "\"unitId\":5340017,\"displayName\":\"rpm\"}}}");
            UANodeSet source = WotNodeSetConverter.ToNodeSet(document);

            using WotDocument exported = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(exported.Properties["speed"].GetProperty("uav:unitProperty").GetString(),
                Is.EqualTo("/properties/EngineeringUnits"));
            using WotDocument readable = WithoutPreservation(exported);
            WotConversionResult<UANodeSet> restored = WotNodeSetConverter.ToNodeSetResult(readable);
            Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics));
        }

        [TestCase("title", "titles")]
        [TestCase("description", "descriptions")]
        public void NeutralSingularLanguageDoesNotChangeTheAmbientPluralSelection(string singular, string plural)
        {
            using WotDocument document = ParseLocalizedModel(
                "\"properties\":{\"Speed\":{\"type\":\"number\",\"" +
                singular +
                "\":\"Drehzahl\"," +
                "\"" +
                plural +
                "\":{\"de\":\"Drehzahl\",\"en\":\"Speed\"},\"@context\":{\"" +
                singular +
                "\":{\"@id\":\"https://www.w3.org/2019/wot/td#" +
                singular +
                "\",\"@language\":null}}}}",
                "fr");

            WotConversionResult<UANodeSet> imported = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(imported.Success, Is.True, string.Join("; ", imported.Diagnostics));
            UAVariable variable = imported.Value!.Items!.OfType<UAVariable>().Single();
            Export.LocalizedText[] texts = singular == "title" ? variable.DisplayName! : variable.Description!;
            Assert.That(texts[0].Locale, Is.EqualTo("de"));
            using WotDocument exported = WotNodeSetConverter.FromNodeSet(imported.Value);
            Assert.That(exported.RootElement.TryGetProperty("uav:nodes", out _), Is.False);
            Assert.That(exported.Properties["Speed"].GetProperty(singular).GetString(), Is.EqualTo("Drehzahl"));
            using WotDocument readable = WithoutPreservation(exported);
            Assert.That(WotNodeSetConverter.ToNodeSetResult(readable).Success, Is.True);
        }

        [Test]
        public void RetainedUnitTranslationsCannotOverrideTheSelectedNativeText()
        {
            using WotDocument document = ParseLocalizedModel(
                "\"properties\":{\"speed\":{\"type\":\"number\",\"unit\":\"Drehzahl\"," +
                "\"uav:unitProperty\":\"/properties/speedUnit\"}," +
                "\"speedUnit\":{\"type\":\"string\",\"uav:browseName\":\"ua:EngineeringUnits\"," +
                "\"uav:engineeringUnits\":{\"namespaceUri\":\"" +
                WotAnalogTestData.UnitAuthority +
                "\"," +
                "\"unitId\":5340017,\"displayName\":\"Drehzahl\"," +
                "\"displayNames\":{\"de\":\"Drehzahl\",\"en\":\"rotation\"}}}}", "de");
            UANodeSet source = WotNodeSetConverter.ToNodeSet(document);
            UAVariable unit = source.Items!.OfType<UAVariable>().Single(item => item.BrowseName == "EngineeringUnits");
            System.Xml.XmlElement display = unit.Value!.GetElementsByTagName("DisplayName", Namespaces.OpcUaXsd)
                .OfType<System.Xml.XmlElement>().Single();
            display.GetElementsByTagName("Text", Namespaces.OpcUaXsd)[0]!.InnerText = "Neue Drehzahl";

            using WotDocument exported = WotNodeSetConverter.FromNodeSet(source);

            JsonElement units = exported.Properties["EngineeringUnits"].GetProperty("uav:engineeringUnits");
            Assert.That(units.GetProperty("displayName").GetString(), Is.EqualTo("Neue Drehzahl"));
            Assert.That(units.GetProperty("displayNames").GetProperty("de").GetString(), Is.EqualTo("Neue Drehzahl"));
            Assert.That(units.GetProperty("displayNames").GetProperty("en").GetString(), Is.EqualTo("rotation"));
            WotConversionResult<UANodeSet> restored = WotNodeSetConverter.ToNodeSetResult(exported);
            Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MethodArgumentPreservesTranslationsOutsideItsSelectedNativeDescription(bool contextual)
        {
            string context = contextual ? ",\"@context\":{\"@language\":\"de\"}" : string.Empty;
            string text = contextual ? "Grund" : "Reason";
            using WotDocument document = ParseEnglishModel(
                "\"actions\":{\"Run\":{\"@type\":\"uav:method\",\"input\":{" +
                "\"type\":\"object\",\"uav:fieldOrder\":[\"Reason\"],\"properties\":{\"Reason\":{" +
                "\"type\":\"string\",\"description\":\"" +
                text +
                "\"," +
                "\"descriptions\":{\"de\":\"Grund\",\"en\":\"Reason\"}" +
                context +
                "}}}}}");
            UANodeSet source = WotNodeSetConverter.ToNodeSet(document);

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(source);
            using WotDocument? exported = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement argument = exported!.Actions["Run"].GetProperty("input")
                .GetProperty("properties").GetProperty("Reason");
            Assert.That(argument.GetProperty("description").GetString(), Is.EqualTo(text));
            Assert.That(argument.GetProperty("descriptions").GetProperty("de").GetString(), Is.EqualTo("Grund"));
            Assert.That(argument.GetProperty("descriptions").GetProperty("en").GetString(), Is.EqualTo("Reason"));
            using WotDocument readable = WithoutPreservation(exported);
            WotConversionResult<UANodeSet> restored = WotNodeSetConverter.ToNodeSetResult(readable);
            Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics));
        }

        [Test]
        public void RootNeutralDescriptionDoesNotCrossTheTdPropertyScope()
        {
            using WotDocument document = ParseLocalizedModel(
                "\"description\":\"Eine Pumpe\",\"descriptions\":{\"de\":\"Eine Pumpe\"}," +
                "\"properties\":{\"Speed\":{\"type\":\"number\",\"description\":\"Shaft speed\"}}",
                "en",
                ",\"description\":{\"@id\":\"https://www.w3.org/2019/wot/td#description\",\"@language\":null}");

            UANodeSet source = WotNodeSetConverter.ToNodeSet(document);

            Assert.That(source.Items!.OfType<UAVariable>().Single().Description!.Single().Locale, Is.EqualTo("en"));
            Assert.That(source.Items!.OfType<UAObjectType>().Single().Description!.Single().Locale, Is.EqualTo("de"));
        }

        [TestCase("action")]
        [TestCase("type")]
        [TestCase("field")]
        public void EnglishMetadataSurvivesAnUnscopedParentNeutralOverride(string target)
        {
            const string englishContext =
                "\"@context\":{\"description\":{\"@id\":\"https://www.w3.org/2019/wot/td#description\"," +
                "\"@language\":\"en\"}}";
            const string neutralContext =
                "\"@context\":{\"description\":{\"@id\":\"https://www.w3.org/2019/wot/td#description\"," +
                "\"@language\":null}}";
            string members = "\"description\":\"Eine Pumpe\",\"descriptions\":{\"de\":\"Eine Pumpe\"},";
            if (target == "action")
            {
                members += "\"actions\":{\"Run\":{\"@type\":\"uav:method\",\"description\":\"Operation\"," +
                    englishContext +
                    "}}";
            }
            else
            {
                string typeText = target == "type"
                    ? "\"description\":\"Operation\"," + englishContext
                    : "\"description\":\"Messung\",\"descriptions\":{\"de\":\"Messung\"}," + neutralContext;
                string fieldText = target == "field" ? ",\"description\":\"Operation\"," + englishContext : string.Empty;
                members += "\"uav:dataTypeDefinitions\":[{\"@id\":\"urn:reading\"," +
                    "\"uav:dataTypeId\":\"nsu=urn:test:pump;i=3001\",\"@type\":\"uav:StructureDefinition\"," +
                    "\"uav:dataTypeName\":\"pump:Reading\",\"uav:structureType\":\"Structure\"," +
                    typeText +
                    "," +
                    "\"uav:fields\":[{\"@type\":\"uav:StructureField\",\"uav:fieldName\":\"Value\"," +
                    "\"uav:fieldDataTypeId\":\"i=11\"" +
                    fieldText +
                    "}]}]";
            }
            using WotDocument document = ParseLocalizedModel(members, "en",
                ",\"description\":{\"@id\":\"https://www.w3.org/2019/wot/td#description\",\"@language\":null}");
            UANodeSet source = WotNodeSetConverter.ToNodeSet(document);
            Assert.That(Description(source).Locale, Is.EqualTo("en"));

            using WotDocument exported = WotNodeSetConverter.FromNodeSet(source);
            using WotDocument readable = WithoutPreservation(exported);
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(readable);

            Assert.That(Description(restored).Locale, Is.EqualTo("en"));
            Assert.That(Description(restored).Value, Is.EqualTo("Operation"));

            Export.LocalizedText Description(UANodeSet nodeSet)
            {
                return target switch
                {
                    "action" => nodeSet.Items!.OfType<UAMethod>().Single().Description!.Single(),
                    "type" => nodeSet.Items!.OfType<UADataType>().Single().Description!.Single(),
                    _ => nodeSet.Items!.OfType<UADataType>().Single().Definition!.Field!.Single().Description!.Single()
                };
            }
        }

        [TestCase(false, "de")]
        [TestCase(true, "fr")]
        public void IndexMapKeysDoNotActivatePropertyScopedTermDefinitions(bool localOverride, string expectedLocale)
        {
            string local = localOverride ? ",\"@context\":{\"@language\":\"fr\"}" : string.Empty;
            using WotDocument document = ParseLocalizedModel(
                "\"properties\":{\"Speed\":{\"type\":\"number\",\"title\":\"Drehzahl\"" + local + "}}",
                "en",
                ",\"properties\":{\"@id\":\"https://www.w3.org/2019/wot/td#hasPropertyAffordance\"," +
                "\"@container\":\"@index\",\"@context\":{\"@language\":\"de\"}}," +
                "\"Speed\":{\"@id\":\"urn:test:Speed\",\"@context\":{\"@language\":\"fr\"}}");

            UANodeSet source = WotNodeSetConverter.ToNodeSet(document);

            Assert.That(source.Items!.OfType<UAVariable>().Single().DisplayName!.Single().Locale,
                Is.EqualTo(expectedLocale));
        }

        [TestCase("properties", true)]
        [TestCase("actions", true)]
        [TestCase("events", true)]
        [TestCase("properties", false)]
        [TestCase("actions", false)]
        [TestCase("events", false)]
        public void EmptyTypeAnnotationsRetainTheGeneratedNativeMarker(string collection, bool contextual)
        {
            string nativeType = collection switch
            {
                "properties" => "uav:variable",
                "actions" => "uav:method",
                _ => "uav:eventType"
            };
            string context = contextual ? "\"@context\":{\"semantic\":\"urn:semantic#\"}," : string.Empty;
            using WotDocument document = ParseLocalizedModel(
                "\"" +
                collection +
                "\":{\"Value\":{" +
                context +
                "\"@type\":[],\"uav:browseName\":\"pump:Value\"}}", "en");
            UANodeSet source = WotNodeSetConverter.ToNodeSet(document);
            for (int pass = 0; pass < 3; pass++)
            {
                WotConversionResult<WotDocument> exported = WotNodeSetConverter.FromNodeSetResult(source);
                using WotDocument? restored = exported.Value;
                Assert.That(exported.Success, Is.True, string.Join("; ", exported.Diagnostics));
                JsonElement types = restored!.RootElement.GetProperty(collection).GetProperty("Value").GetProperty("@type");
                string[] tokens = types.ValueKind == JsonValueKind.String
                    ? [types.GetString()!] : [.. types.EnumerateArray().Select(item => item.GetString()!)];
                Assert.That(tokens, Is.EqualTo([nativeType]));
                source = WotNodeSetConverter.ToNodeSet(restored);
            }
        }

        [TestCase("properties", "neutral")]
        [TestCase("actions", "neutral")]
        [TestCase("events", "neutral")]
        [TestCase("properties", "tagged")]
        [TestCase("actions", "tagged")]
        [TestCase("events", "tagged")]
        [TestCase("properties", "child")]
        [TestCase("actions", "child")]
        [TestCase("events", "child")]
        public void AmbientSelectionLocaleSurvivesNeutralRootText(string collection, string control)
        {
            string rootLanguage = control == "tagged" ? "\"de\"" : "null";
            string childLanguage = control == "child" ? "\"@language\":\"de\"," : string.Empty;
            using WotDocument document = ParseLocalizedModel(
                "\"" +
                collection +
                "\":{\"Value\":{" +
                "\"@context\":{\"sem\":\"urn:local-semantic#\"," +
                childLanguage +
                "\"title\":{\"@id\":\"https://www.w3.org/2019/wot/td#title\",\"@language\":\"de\"}}," +
                "\"title\":\"Deutsch\",\"titles\":{\"de\":\"Deutsch\",\"en\":\"English\"}," +
                "\"uav:browseName\":\"pump:Value\"}}", "de",
                ",\"title\":{\"@id\":\"https://www.w3.org/2019/wot/td#title\",\"@language\":" + rootLanguage + "}");
            UANodeSet source = WotNodeSetConverter.ToNodeSet(document);
            for (int pass = 0; pass < 3; pass++)
            {
                using WotDocument exported = WotNodeSetConverter.FromNodeSet(source);
                JsonElement child = exported.RootElement.GetProperty(collection).GetProperty("Value");
                Assert.That(child.GetProperty("title").GetString(), Is.EqualTo("Deutsch"));
                Assert.That(child.GetProperty("titles").GetProperty("en").GetString(), Is.EqualTo("English"));
                using WotDocument readable = WithoutPreservation(exported);
                WotConversionResult<UANodeSet> restored = WotNodeSetConverter.ToNodeSetResult(readable);
                Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics));
                Assert.That(exported.RootElement.TryGetProperty("uav:nodes", out _), Is.False,
                    string.Join("; ", NodeSetComparer.CompareEquivalent(
                        source, restored.Value!, new WotNodeSetConverterOptions().ToComparisonOptions()).Differences));
                UANode root = restored.Value!.Items!.Single(node => node.NodeId == "ns=1;i=2000");
                Assert.That(root.DisplayName!.Single().Locale, Is.EqualTo(control == "tagged" ? "de" : string.Empty));
                source = restored.Value;
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RootSelectionCanDifferFromTheNativeRootTextLocale(bool explicitLocale)
        {
            using WotDocument initial = ParseLocalizedModel(
                "\"titles\":{\"de\":\"Pump\",\"en\":\"English root\"}," +
                "\"properties\":{\"Value\":{\"@context\":{\"sem\":\"urn:semantic#\"}," +
                "\"type\":\"string\",\"title\":\"Deutsch\",\"titles\":{\"de\":\"Deutsch\",\"en\":\"English\"}}}",
                "fr",
                ",\"title\":{\"@id\":\"https://www.w3.org/2019/wot/td#title\",\"@language\":null}");
            JsonObject root = JsonNode.Parse(initial.Utf8Json.Span)!.AsObject();
            if (!explicitLocale)
            {
                root["@context"]![1]!.AsObject().Remove("@language");
                root["@context"]![1]!["title"]!["@language"] = "de";
                root.Remove("titles");
                root["properties"]!["Value"]!["title"] = "English";
            }
            using var document = WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));
            UANodeSet source = WotNodeSetConverter.ToNodeSet(document);
            for (int pass = 0; pass < 3; pass++)
            {
                using WotDocument exported = WotNodeSetConverter.FromNodeSet(source);
                using WotDocument readable = WithoutPreservation(exported);
                WotConversionResult<UANodeSet> restored = WotNodeSetConverter.ToNodeSetResult(readable);
                Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics));
                Assert.That(exported.Properties["Value"].GetProperty("title").GetString(),
                    Is.EqualTo(explicitLocale ? "Deutsch" : "English"));
                Assert.That(exported.RootElement.TryGetProperty("uav:nodes", out _), Is.False);
                source = restored.Value!;
            }
        }

        private static WotDocument ParseLocalizedModel(string members, string language, string extraContext = "")
        {
            return WotDocument.Parse(WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\",{" +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\",\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"urn:test:pump\",\"@language\":\"" +
                language +
                "\"" +
                extraContext +
                "}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"],\"title\":\"Pump\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=2000\",\"uav:browseName\":\"pump:PumpType\"," +
                members +
                "}"));
        }

        private static WotDocument WithoutPreservation(WotDocument document)
        {
            JsonObject root = JsonNode.Parse(document.Utf8Json.Span)!.AsObject();
            root.Remove("uav:nodes");
            root.Remove("uav:nodeSet");
            return WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));
        }

        private static WotDocument ParseEnglishModel(string members)
        {
            return WotDocument.Parse(WotTestData.Utf8(
                "{\"@context\":{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\",\"pump\":\"urn:test:pump\",\"@language\":\"en\"}," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"],\"title\":\"Pump\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=2000\",\"uav:browseName\":\"pump:PumpType\"," +
                members +
                "}"));
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
