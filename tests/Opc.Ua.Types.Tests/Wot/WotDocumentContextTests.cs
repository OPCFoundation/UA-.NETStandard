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
    /// The JSON-LD context a generated document declares
    /// (WoT Binding Sections 4.1 and 9.1.1).
    /// </summary>
    /// <remarks>
    /// Two facts decide whether a generated document means what it says when a
    /// JSON-LD processor reads it. The Binding context has to be named, because
    /// several Binding terms are short members under a type-scoped context and
    /// a short member is a term only while the context defining it is in scope.
    /// And where a text states no entry for the document's default locale, the
    /// terms carrying it have to be re-declared without a language, because a
    /// context declaring <c>@language</c> would otherwise tag a German string
    /// as English.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotDocumentContextTests
    {
        private const string BindingContext =
            "http://opcfoundation.org/UA/WoT-Binding/v1.1/opc-ua-wot-binding.context.jsonld";

        private const string TitleIri = "https://www.w3.org/2019/wot/td#title";
        private const string DescriptionIri = "https://www.w3.org/2019/wot/td#description";

        [Test]
        public void AGeneratedDocumentNamesBothContextIdentities()
        {
            using WotDocument document =
                WotNodeSetConverter.FromNodeSet(WotTestData.CreateRichNodeSet());

            List<string> identities = ContextIdentities(document);
            Assert.Multiple(() =>
            {
                Assert.That(
                    identities,
                    Does.Contain("https://www.w3.org/2022/wot/td/v1.1"),
                    "A Thing Model is a W3C Thing Model first.");
                Assert.That(
                    identities,
                    Does.Contain(BindingContext),
                    "The Binding context mints the scoped short members a generated " +
                    "document writes, so a document that omits it loses them on expansion.");
            });
        }

        /// <summary>
        /// The identity is version-pinned, because a document states which
        /// revision it was written against and a context that moves under a
        /// document is a document whose meaning changed without being edited.
        /// </summary>
        [Test]
        public void TheBindingContextIdentityIsVersionPinned()
        {
            using WotDocument document =
                WotNodeSetConverter.FromNodeSet(WotTestData.CreateRichNodeSet());

            string identity = ContextIdentities(document)
                .Single(i => i.StartsWith(
                    "http://opcfoundation.org/UA/WoT-Binding/", StringComparison.Ordinal));

            Assert.That(
                identity,
                Does.Contain("/v" + WotBindingConformance.CurrentRevision + "/"),
                "The pinned segment is the revision this library implements.");
        }

        /// <summary>
        /// A document whose texts are all in its own default locale needs no
        /// override, and adding one unconditionally would strip the language
        /// tag from every document this library writes.
        /// </summary>
        [Test]
        public void ADocumentWhoseTextIsInItsDefaultLocaleCarriesNoOverride()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                CreateLocalizedNodeSet(("en", "Pump"), ("de", "Pumpe")));

            Assert.Multiple(() =>
            {
                Assert.That(DocumentLocale(document), Is.EqualTo("en"));
                Assert.That(OverrideEntries(document), Is.Empty);
            });
        }

        /// <summary>
        /// The document's default locale is derived from the root, so it is a
        /// Node <em>other</em> than the root whose text can fall outside it.
        /// </summary>
        [Test]
        public void ADocumentWithoutTextInItsDefaultLocaleCarriesOneOverride()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                CreateMixedLocaleNodeSet());

            List<JsonElement> overrides = OverrideEntries(document);
            Assert.That(overrides, Has.Count.EqualTo(1), "Exactly one override, not one per Node.");

            JsonElement entry = overrides[0];
            Assert.Multiple(() =>
            {
                Assert.That(DocumentLocale(document), Is.EqualTo("en"));
                Assert.That(
                    entry.GetProperty("title").GetProperty("@id").GetString(),
                    Is.EqualTo(TitleIri));
                Assert.That(
                    entry.GetProperty("title").GetProperty("@language").ValueKind,
                    Is.EqualTo(JsonValueKind.Null));
                Assert.That(
                    entry.GetProperty("description").GetProperty("@id").GetString(),
                    Is.EqualTo(DescriptionIri));
                Assert.That(
                    entry.GetProperty("description").GetProperty("@language").ValueKind,
                    Is.EqualTo(JsonValueKind.Null));
            });
        }

        /// <summary>
        /// A single locale is written as the singular member alone and is the
        /// document's own language by definition, so it is never the case the
        /// override exists for.
        /// </summary>
        [Test]
        public void ASingleLocaleNeedsNoOverride()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                CreateLocalizedNodeSet(("de", "Pumpe")));

            Assert.That(OverrideEntries(document), Is.Empty);
        }

        /// <summary>
        /// The override is derived from the projected Nodes, so it is not also
        /// carried as residue - which would state it twice and would force the
        /// structured fallback the readable mapping exists to avoid.
        /// </summary>
        [Test]
        public void TheGeneratedOverrideIsNotAlsoResidue()
        {
            UANodeSet source = CreateMixedLocaleNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.WhenRequired
                });

            Assert.That(
                document.RootElement.TryGetProperty("uav:nodes", out _),
                Is.False,
                "The context the forward direction writes is re-derivable, so it does not " +
                "make the readable mapping incomplete.");
        }

        /// <summary>
        /// An author's own override of the same terms says something different
        /// from the derived one, so it is preserved rather than dropped.
        /// </summary>
        [Test]
        public void AnAuthoredContextEntryIsPreserved()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}," +
                "{\"title\":{\"@id\":\"" + TitleIri + "\",\"@language\":\"de\"}}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"}");

            using WotDocument authored = WotDocument.Parse(json);
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(authored);

            string extensions = nodeSet.Extensions is null
                ? string.Empty
                : string.Concat(nodeSet.Extensions.Select(e => e.OuterXml));
            Assert.That(
                extensions,
                Does.Contain("Pointer=\"/@context/-\""),
                "An override that is not the derived one states an author's intent, so it " +
                "is carried rather than treated as re-derivable.");
        }

        /// <summary>
        /// Section 6.4.1 mints the EUInformation text members as short members
        /// scoped to the object, so an override at the root cannot reach them:
        /// the scoped context is entered on the object and nowhere else.
        /// </summary>
        [Test]
        public void EngineeringUnitsCarryTheirOwnNodeLocalOverride()
        {
            using WotDocument document =
                WotNodeSetConverter.FromNodeSet(CreateUnitNodeSet("de", "U/min"));

            JsonElement units = document.Properties["EngineeringUnits"]
                .GetProperty("uav:engineeringUnits");

            Assert.Multiple(() =>
            {
                Assert.That(units.TryGetProperty("@context", out JsonElement local), Is.True);
                Assert.That(
                    local.GetProperty("displayName").GetProperty("@id").GetString(),
                    Is.EqualTo("uav:unitDisplayName"));
                Assert.That(
                    local.GetProperty("displayName").GetProperty("@language").ValueKind,
                    Is.EqualTo(JsonValueKind.Null));
                Assert.That(
                    local.GetProperty("description").GetProperty("@id").GetString(),
                    Is.EqualTo("uav:unitDescription"));
                Assert.That(
                    units.GetProperty("namespaceUri").GetString(),
                    Is.EqualTo("http://www.opcfoundation.org/UA/units/un/cefact"),
                    "namespaceUri is an IRI-valued short member of the Binding context.");
            });
        }

        [Test]
        public void EngineeringUnitsInTheDefaultLocaleCarryNoOverride()
        {
            using WotDocument document =
                WotNodeSetConverter.FromNodeSet(CreateUnitNodeSet("en", "rpm"));

            Assert.That(
                document.Properties["EngineeringUnits"]
                    .GetProperty("uav:engineeringUnits")
                    .TryGetProperty("@context", out _),
                Is.False);
        }

        /// <summary>
        /// The whole context is a function of the NodeSet, so two conversions
        /// of the same source produce the same bytes.
        /// </summary>
        [Test]
        public void TheGeneratedContextIsDeterministic()
        {
            UANodeSet source = CreateLocalizedNodeSet(("de", "Pumpe"), ("fr", "Pompe"));

            static string ContextOf(UANodeSet nodeSet)
            {
                using WotDocument document = WotNodeSetConverter.FromNodeSet(nodeSet);
                return document.RootElement.GetProperty("@context").GetRawText();
            }

            string once = ContextOf(source);
            string twice = ContextOf(source);
            Assert.That(twice, Is.EqualTo(once));
        }

        /// <summary>
        /// A context entry that is <em>almost</em> the derived override is not
        /// it, and the difference is what the author meant.
        /// </summary>
        [TestCase("{\"title\":{\"@id\":\"" + TitleIri + "\",\"@language\":null}}",
            TestName = "OnlyOneOfTheTwoTermsIsNotTheDerivedOverride")]
        [TestCase("{\"title\":{\"@id\":\"" + TitleIri + "\",\"@language\":null}," +
            "\"description\":{\"@id\":\"urn:other\",\"@language\":null}}",
            TestName = "AnotherIriIsNotTheDerivedOverride")]
        [TestCase("{\"title\":{\"@id\":\"" + TitleIri + "\",\"@language\":null}," +
            "\"description\":{\"@id\":\"" + DescriptionIri + "\",\"@language\":\"de\"}}",
            TestName = "ATaggedTermIsNotTheDerivedOverride")]
        [TestCase("{\"title\":{\"@id\":\"" + TitleIri + "\",\"@language\":null}," +
            "\"description\":{\"@id\":\"" + DescriptionIri + "\",\"@language\":null," +
            "\"@container\":\"@set\"}}",
            TestName = "AnExtraKeywordIsNotTheDerivedOverride")]
        [TestCase("{\"title\":{\"@id\":\"" + TitleIri + "\",\"@language\":null}," +
            "\"description\":{\"@language\":null}}",
            TestName = "AMissingIdIsNotTheDerivedOverride")]
        [TestCase("{\"title\":{\"@id\":\"" + TitleIri + "\",\"@language\":null}," +
            "\"description\":{\"@id\":" + "42" + ",\"@language\":null}}",
            TestName = "ANonStringIdIsNotTheDerivedOverride")]
        [TestCase("{\"title\":{\"@id\":\"" + TitleIri + "\",\"@language\":null}," +
            "\"description\":\"" + DescriptionIri + "\"}",
            TestName = "AStringTermDefinitionIsNotTheDerivedOverride")]
        [TestCase("{\"title\":{\"@id\":\"" + TitleIri + "\",\"@language\":null}," +
            "\"description\":{\"@id\":\"" + DescriptionIri + "\",\"@language\":null}," +
            "\"forms\":{\"@id\":\"urn:x\"}}",
            TestName = "AnUnrelatedTermIsNotTheDerivedOverride")]
        public void AnAlmostMatchingContextEntryIsPreserved(string entry)
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}," + entry + "]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"}");

            using WotDocument authored = WotDocument.Parse(json);
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(authored);

            string extensions = nodeSet.Extensions is null
                ? string.Empty
                : string.Concat(nodeSet.Extensions.Select(e => e.OuterXml));
            Assert.That(
                extensions,
                Does.Contain("Pointer=\"/@context/-\""),
                "Only the exact derived shape is re-derivable; everything else is the " +
                "author's and is carried.");
        }

        /// <summary>
        /// A non-object entry in the context array is not a term definition at
        /// all, and is carried like any other thing the converter did not write.
        /// </summary>
        [Test]
        public void AnUnrecognizedContextIdentityIsPreserved()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}," +
                "\"https://example.com/other.jsonld\"]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"}");

            using WotDocument authored = WotDocument.Parse(json);
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(authored);

            string extensions = nodeSet.Extensions is null
                ? string.Empty
                : string.Concat(nodeSet.Extensions.Select(e => e.OuterXml));
            Assert.That(extensions, Does.Contain("Pointer=\"/@context/-\""));
        }

        /// <summary>
        /// The Binding context identity the forward direction writes is
        /// re-derived rather than carried, exactly like the W3C one.
        /// </summary>
        [Test]
        public void TheBindingContextIdentityIsNotResidue()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}," +
                "\"" + BindingContext + "\"]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"}");

            using WotDocument authored = WotDocument.Parse(json);
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(authored);

            string extensions = nodeSet.Extensions is null
                ? string.Empty
                : string.Concat(nodeSet.Extensions.Select(e => e.OuterXml));
            Assert.That(extensions, Does.Not.Contain("Pointer=\"/@context/-\""));
        }

        /// <summary>
        /// A Node with no text at all states nothing that could fall outside
        /// the default locale.
        /// </summary>
        [Test]
        public void ANodeWithoutTextNeedsNoOverride()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                new UANodeSet
                {
                    NamespaceUris = ["urn:test:model"],
                    Models = [new ModelTableEntry { ModelUri = "urn:test:model" }],
                    Items =
                    [
                        new UAObjectType
                        {
                            NodeId = "ns=1;i=1001",
                            BrowseName = "1:PumpType",
                            References =
                            [
                                new Reference
                                {
                                    ReferenceType = "HasSubtype",
                                    IsForward = false,
                                    Value = "i=58"
                                }
                            ]
                        }
                    ]
                });

            Assert.That(OverrideEntries(document), Is.Empty);
        }

        /// <summary>
        /// A NodeSet with no Nodes at all states no text, so nothing can fall
        /// outside its default locale.
        /// </summary>
        [Test]
        public void AnEmptyNodeSetNeedsNoOverride()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                new UANodeSet
                {
                    NamespaceUris = ["urn:test:model"],
                    Models = [new ModelTableEntry { ModelUri = "urn:test:model" }]
                });

            Assert.That(OverrideEntries(document), Is.Empty);
        }

        private static List<string> ContextIdentities(WotDocument document)
        {
            var identities = new List<string>();
            foreach (JsonElement item in
                document.RootElement.GetProperty("@context").EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    identities.Add(item.GetString()!);
                }
            }
            return identities;
        }

        private static string? DocumentLocale(WotDocument document)
        {
            foreach (JsonElement item in
                document.RootElement.GetProperty("@context").EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("@language", out JsonElement language) &&
                    language.ValueKind == JsonValueKind.String)
                {
                    return language.GetString();
                }
            }
            return null;
        }

        private static List<JsonElement> OverrideEntries(WotDocument document)
        {
            var entries = new List<JsonElement>();
            foreach (JsonElement item in
                document.RootElement.GetProperty("@context").EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("title", out _))
                {
                    entries.Add(item);
                }
            }
            return entries;
        }

        private static UANodeSet CreateLocalizedNodeSet(
            params (string Locale, string Text)[] texts)
        {
            var displayName = new Export.LocalizedText[texts.Length];
            for (int ii = 0; ii < texts.Length; ii++)
            {
                displayName[ii] = new Export.LocalizedText
                {
                    Locale = texts[ii].Locale,
                    Value = texts[ii].Text
                };
            }
            return new UANodeSet
            {
                NamespaceUris = ["urn:test:model"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:model" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1001",
                        BrowseName = "1:PumpType",
                        DisplayName = displayName,
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = "i=58"
                            }
                        ]
                    }
                ]
            };
        }

        /// <summary>
        /// A NodeSet whose root names the document's default locale and whose
        /// child Variable states its text in two other languages.
        /// </summary>
        private static UANodeSet CreateMixedLocaleNodeSet()
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:test:model"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:model" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1001",
                        BrowseName = "1:PumpType",
                        DisplayName =
                        [
                            new Export.LocalizedText { Locale = "en", Value = "Pump" }
                        ],
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
                                ReferenceType = "HasProperty",
                                IsForward = true,
                                Value = "ns=1;i=6001"
                            }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=6001",
                        BrowseName = "1:Speed",
                        DisplayName =
                        [
                            new Export.LocalizedText { Locale = "de", Value = "Drehzahl" },
                            new Export.LocalizedText
                            {
                                Locale = "fr",
                                Value = "Vitesse de rotation"
                            }
                        ],
                        ParentNodeId = "ns=1;i=1001",
                        DataType = "i=11",
                        AccessLevel = 1,
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasTypeDefinition",
                                IsForward = true,
                                Value = "i=68"
                            },
                            new Reference
                            {
                                ReferenceType = "HasProperty",
                                IsForward = false,
                                Value = "ns=1;i=1001"
                            }
                        ]
                    }
                ]
            };
        }

        /// <summary>
        /// A NodeSet carrying one <c>EngineeringUnits</c> Property whose
        /// EUInformation states its text in the given locale.
        /// </summary>
        /// <remarks>
        /// OPC 10000-8 gives an EUInformation exactly one DisplayName and one
        /// Description, each a single LocalizedText, so what decides the
        /// override here is whether that one locale is the document's.
        /// </remarks>
        private static UANodeSet CreateUnitNodeSet(string locale, string text)
        {
            string value =
                "<uax:ExtensionObject xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
                "<uax:TypeId><uax:Identifier>i=888</uax:Identifier></uax:TypeId>" +
                "<uax:Body><uax:EUInformation>" +
                "<uax:NamespaceUri>http://www.opcfoundation.org/UA/units/un/cefact" +
                "</uax:NamespaceUri>" +
                "<uax:UnitId>5340017</uax:UnitId>" +
                "<uax:DisplayName>" +
                "<uax:Locale>" + locale + "</uax:Locale>" +
                "<uax:Text>" + text + "</uax:Text>" +
                "</uax:DisplayName>" +
                "<uax:Description>" +
                "<uax:Locale>" + locale + "</uax:Locale>" +
                "<uax:Text>" + text + "</uax:Text>" +
                "</uax:Description>" +
                "</uax:EUInformation></uax:Body></uax:ExtensionObject>";

            return new UANodeSet
            {
                NamespaceUris = ["urn:test:model"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:model" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1001",
                        BrowseName = "1:PumpType",
                        DisplayName =
                        [
                            new Export.LocalizedText { Locale = "en", Value = "Pump" }
                        ],
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
                                ReferenceType = "HasProperty",
                                IsForward = true,
                                Value = "ns=1;i=6001"
                            }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=6001",
                        BrowseName = "EngineeringUnits",
                        DisplayName =
                        [
                            new Export.LocalizedText
                            {
                                Locale = "en",
                                Value = "EngineeringUnits"
                            }
                        ],
                        ParentNodeId = "ns=1;i=1001",
                        DataType = "i=887",
                        AccessLevel = 1,
                        Value = WotTestData.ParseValue(value),
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasTypeDefinition",
                                IsForward = true,
                                Value = "i=68"
                            },
                            new Reference
                            {
                                ReferenceType = "HasProperty",
                                IsForward = false,
                                Value = "ns=1;i=1001"
                            }
                        ]
                    }
                ]
            };
        }
    }
}