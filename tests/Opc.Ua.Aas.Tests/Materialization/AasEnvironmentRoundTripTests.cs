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

using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Export;

namespace Opc.Ua.Aas.Tests.Materialization
{
    /// <summary>
    /// Tests both directions of the clause 6.4 lossless round trip.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasEnvironmentRoundTripTests
    {
        [TestCaseSource(nameof(ConformanceCorpus))]
        public void MaterializeThenSerializeProducesEquivalentEnvironment(AasEnvironment environment)
        {
            AasMaterializationResult materialized = AasEnvironmentMaterializer.Materialize(environment);
            AasSerializationResult serialized = AasEnvironmentSerializer.Serialize(materialized.NodeSet);

            AasRoundTripComparison comparison = AasRoundTripComparer.Compare(environment, serialized.Environment);
            string diagnostics = string.Join(
                System.Environment.NewLine,
                serialized.Diagnostics.Select(diagnostic => diagnostic.Message));

            Assert.Multiple(() =>
            {
                Assert.That(materialized.HasErrors, Is.False);
                Assert.That(serialized.HasErrors, Is.False, diagnostics);
                Assert.That(comparison.Differences, Is.Empty);
            });
        }

        [TestCaseSource(nameof(ConformanceCorpus))]
        public void SerializeThenMaterializeProducesEquivalentNodeSet(AasEnvironment environment)
        {
            UANodeSet original = AasEnvironmentMaterializer.Materialize(environment).NodeSet;
            AasSerializationResult serialized = AasEnvironmentSerializer.Serialize(original);
            UANodeSet roundTripped = AasEnvironmentMaterializer.Materialize(serialized.Environment).NodeSet;

            string[] differences = CompareNodeSets(original, roundTripped);
            string diagnostics = string.Join(
                System.Environment.NewLine,
                serialized.Diagnostics.Select(diagnostic => diagnostic.Message));

            Assert.Multiple(() =>
            {
                Assert.That(serialized.HasErrors, Is.False, diagnostics);
                Assert.That(differences, Is.Empty);
            });
        }

        /// <summary>
        /// The comparer is the clause 6.4 equivalence relation, so a field it
        /// does not look at is a field the round trip is not actually proving.
        /// Each case here drops or alters one field that the oracle previously
        /// ignored, and every one of them has to be reported.
        /// </summary>
        [Test]
        public void LosingAFieldTheOracleOnceIgnoredIsReported()
        {
            var observed = Reference("urn:observed");

            Assert.Multiple(() =>
            {
                Assert.That(Differences(
                    new AasBlob { IdShort = Present("b"), ContentType = "application/octet-stream",
                        Value = AasOptional<ByteString>.Present(ByteString.From([1, 2, 3])) },
                    new AasBlob { IdShort = Present("b"), ContentType = "application/octet-stream",
                        Value = AasOptional<ByteString>.Present(ByteString.From([1, 2, 4])) }),
                    Is.Not.Empty, "Blob content.");

                Assert.That(Differences(
                    new AasFile { IdShort = Present("f"), ContentType = "text/plain",
                        Value = Present("a.txt") },
                    new AasFile { IdShort = Present("f"), ContentType = "application/pdf",
                        Value = Present("a.txt") }),
                    Is.Not.Empty, "File contentType.");

                Assert.That(Differences(
                    new AasReferenceElement { IdShort = Present("r"),
                        Value = AasOptional<AASReferenceDataType>.Present(Reference("urn:a")) },
                    new AasReferenceElement { IdShort = Present("r"),
                        Value = AasOptional<AASReferenceDataType>.Present(Reference("urn:b")) }),
                    Is.Not.Empty, "ReferenceElement value.");

                Assert.That(Differences(
                    new AasEntity { IdShort = Present("e"), EntityType = AASEntityTypeDataType.SelfManagedEntity },
                    new AasEntity { IdShort = Present("e"), EntityType = AASEntityTypeDataType.CoManagedEntity }),
                    Is.Not.Empty, "Entity entityType.");

                Assert.That(Differences(
                    new AasBasicEventElement { IdShort = Present("v"), Observed = observed,
                        Direction = AASDirectionDataType.Output, State = AASStateOfEventDataType.On },
                    new AasBasicEventElement { IdShort = Present("v"), Observed = observed,
                        Direction = AASDirectionDataType.Input, State = AASStateOfEventDataType.On }),
                    Is.Not.Empty, "BasicEventElement direction.");

                // The declared type is what gives the value its meaning, so
                // altering it while leaving the lexical form alone has to be
                // reported even though both sides read "1".
                Assert.That(Differences(
                    Property("p", AASDataTypeDefXsdDataType.Int, "1"),
                    Property("p", AASDataTypeDefXsdDataType.Short, "1")),
                    Is.Not.Empty, "Property valueType.");

                // Two references that name the same value through different
                // key kinds are not the same reference.
                Assert.That(Differences(
                    new AasReferenceElement { IdShort = Present("k"),
                        Value = AasOptional<AASReferenceDataType>.Present(
                            Reference("urn:x", AASKeyTypesDataType.Submodel)) },
                    new AasReferenceElement { IdShort = Present("k"),
                        Value = AasOptional<AASReferenceDataType>.Present(
                            Reference("urn:x", AASKeyTypesDataType.GlobalReference)) }),
                    Is.Not.Empty, "Reference key type.");
            });
        }

        private static IReadOnlyList<string> Differences(AasSubmodelElement left, AasSubmodelElement right)
        {
            return AasRoundTripComparer
                .Compare(Environment(Submodel("s", left)), Environment(Submodel("s", right)))
                .Differences;
        }

        private static AASReferenceDataType Reference(
            string value,
            AASKeyTypesDataType keyType = AASKeyTypesDataType.GlobalReference)
        {
            var key = new AASKeyDataType { Type = keyType, Value = value };
            return new AASReferenceDataType
            {
                Type = AASReferenceTypesDataType.ExternalReference,
                Keys = new ArrayOf<AASKeyDataType>(new[] { key })
            };
        }

        [Test]
        public void CorruptingAValueIsReported()
        {
            AasEnvironment left = Environment(Submodel("values", Property("p", AASDataTypeDefXsdDataType.Int, "1")));
            AasEnvironment right = Environment(Submodel("values", Property("p", AASDataTypeDefXsdDataType.Int, "2")));

            Assert.That(AasRoundTripComparer.Compare(left, right).Differences, Is.Not.Empty);
        }

        [Test]
        public void LosingDecimalDigitsThroughFixedPrecisionIsReported()
        {
            AasEnvironment left = Environment(Submodel(
                "values",
                Property("p", AASDataTypeDefXsdDataType.Decimal, "12345678901234567890.123456789")));
            AasEnvironment right = Environment(Submodel(
                "values",
                Property("p", AASDataTypeDefXsdDataType.Decimal, "12345678901234567000")));

            Assert.That(AasRoundTripComparer.Compare(left, right).Differences, Is.Not.Empty);
        }

        [Test]
        public void RestoringOrderedListInBrowseOrderIsReported()
        {
            var left = new AasSubmodelElementList
            {
                IdShort = Present("list"),
                TypeValueListElement = AASSubmodelElementsDataType.Property,
                Value = PresentElements(
                    Property("first", AASDataTypeDefXsdDataType.String, "a"),
                    Property("second", AASDataTypeDefXsdDataType.String, "b"))
            };
            var right = left with
            {
                Value = PresentElements(
                    Property("second", AASDataTypeDefXsdDataType.String, "b"),
                    Property("first", AASDataTypeDefXsdDataType.String, "a"))
            };

            Assert.That(AasRoundTripComparer.Compare(Environment(Submodel("s", left)), Environment(Submodel("s", right)))
                .Differences, Is.Not.Empty);
        }

        [Test]
        public void ConflatingAbsentWithEmptyIsReported()
        {
            AasEnvironment left = Environment(Submodel("s", new AasCapability { IdShort = Present("capability") }));
            AasEnvironment right = Environment(Submodel(
                "s",
                new AasCapability
                {
                    IdShort = Present("capability"),
                    Qualifiers = AasOptional<ArrayOf<AASQualifierDataType>>.Present(ArrayOf<AASQualifierDataType>.Empty)
                }));

            Assert.That(AasRoundTripComparer.Compare(left, right).Differences, Is.Not.Empty);
        }

        [Test]
        public void RewritingValueIntoCanonicalLexicalFormIsNotReported()
        {
            AasEnvironment left = Environment(Submodel(
                "values",
                Property("p", AASDataTypeDefXsdDataType.Decimal, "1.500000")));
            AasEnvironment right = Environment(Submodel(
                "values",
                Property("p", AASDataTypeDefXsdDataType.Decimal, "1.5")));

            Assert.That(AasRoundTripComparer.Compare(left, right).Differences, Is.Empty);
        }

        public static IEnumerable<TestCaseData> ConformanceCorpus()
        {
            yield return new TestCaseData(AbsentVersusEmpty()).SetName("AbsentVersusEmpty");
            yield return new TestCaseData(EveryElementType()).SetName("EveryElementType");
            yield return new TestCaseData(IdentifiableWithoutIdShort()).SetName("IdentifiableWithoutIdShort");
            yield return new TestCaseData(NonCanonicalLexicalForms()).SetName("NonCanonicalLexicalForms");
            yield return new TestCaseData(OrderingAndNesting()).SetName("OrderingAndNesting");
        }

        private static AasEnvironment AbsentVersusEmpty()
        {
            return Environment(new AasSubmodel
            {
                Id = "optional",
                IdShort = Present("optional"),
                Qualifiers = AasOptional<ArrayOf<AASQualifierDataType>>.Present(ArrayOf<AASQualifierDataType>.Empty),
                SubmodelElements = PresentElements(
                    new AasCapability { IdShort = Present("absent") },
                    new AasCapability
                    {
                        IdShort = Present("empty"),
                        DisplayName = AasOptional<ArrayOf<AASLangStringDataType>>.Present(
                            ArrayOf<AASLangStringDataType>.Empty)
                    })
            });
        }

        private static AasEnvironment EveryElementType()
        {
            AASReferenceDataType reference = Reference("target");
            return Environment(Submodel(
                "elements",
                Property("property", AASDataTypeDefXsdDataType.String, "value"),
                new AasMultiLanguageProperty
                {
                    IdShort = Present("multi"),
                    Value = AasOptional<ArrayOf<AASLangStringDataType>>.Present(new ArrayOf<AASLangStringDataType>(
                        new[] { Lang("en", "name"), Lang("de", "Name") }))
                },
                new AasRange
                {
                    IdShort = Present("range"),
                    ValueType = AASDataTypeDefXsdDataType.Int,
                    Min = AasOptional<Variant>.Present(new Variant("1")),
                    Max = AasOptional<Variant>.Present(new Variant("2"))
                },
                new AasBlob { IdShort = Present("blob"), ContentType = "application/octet-stream" },
                new AasFile { IdShort = Present("file"), ContentType = "text/plain", Value = Present("file.txt") },
                new AasReferenceElement { IdShort = Present("reference"), Value = Present(reference) },
                new AasRelationshipElement { IdShort = Present("relationship"), First = reference, Second = reference },
                new AasAnnotatedRelationshipElement
                {
                    IdShort = Present("annotated"),
                    First = reference,
                    Second = reference,
                    Annotations = PresentElements(new AasCapability { IdShort = Present("annotation") })
                },
                new AasSubmodelElementCollection
                {
                    IdShort = Present("collection"),
                    Value = PresentElements(new AasCapability { IdShort = Present("member") })
                },
                new AasSubmodelElementList
                {
                    IdShort = Present("list"),
                    TypeValueListElement = AASSubmodelElementsDataType.Property,
                    Value = PresentElements(Property(null, AASDataTypeDefXsdDataType.String, "member"))
                },
                new AasEntity
                {
                    IdShort = Present("entity"),
                    EntityType = AASEntityTypeDataType.SelfManagedEntity,
                    Statements = PresentElements(new AasCapability { IdShort = Present("statement") })
                },
                new AasBasicEventElement
                {
                    IdShort = Present("event"),
                    Observed = reference,
                    Direction = AASDirectionDataType.Input,
                    State = AASStateOfEventDataType.On
                },
                new AasOperation
                {
                    IdShort = Present("operation"),
                    InputVariables = PresentElements(Property("input", AASDataTypeDefXsdDataType.String, "in")),
                    OutputVariables = PresentElements(Property("output", AASDataTypeDefXsdDataType.String, "out")),
                    InoutputVariables = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                        ArrayOf<AasSubmodelElement>.Empty)
                },
                new AasCapability { IdShort = Present("capability") }));
        }

        private static AasEnvironment IdentifiableWithoutIdShort()
        {
            return new AasEnvironment
            {
                Submodels = AasOptional<ArrayOf<AasSubmodel>>.Present(new ArrayOf<AasSubmodel>(
                    new[] { new AasSubmodel { Id = "urn:without:idshort" } }))
            };
        }

        private static AasEnvironment NonCanonicalLexicalForms()
        {
            return Environment(Submodel(
                "lexical",
                Property("decimal", AASDataTypeDefXsdDataType.Decimal, "1.500000"),
                Property("boolean", AASDataTypeDefXsdDataType.Boolean, "1"),
                Property("integer", AASDataTypeDefXsdDataType.Int, "+42")));
        }

        private static AasEnvironment OrderingAndNesting()
        {
            return Environment(Submodel(
                "nesting",
                new AasSubmodelElementList
                {
                    IdShort = Present("ordered"),
                    TypeValueListElement = AASSubmodelElementsDataType.Property,
                    Value = PresentElements(
                        Property(null, AASDataTypeDefXsdDataType.String, "a"),
                        Property(null, AASDataTypeDefXsdDataType.String, "b"))
                },
                new AasSubmodelElementList
                {
                    IdShort = Present("unordered"),
                    OrderRelevant = AasOptional<bool>.Present(false),
                    TypeValueListElement = AASSubmodelElementsDataType.Property,
                    Value = PresentElements(
                        Property(null, AASDataTypeDefXsdDataType.String, "x"),
                        Property(null, AASDataTypeDefXsdDataType.String, "y"))
                },
                new AasSubmodelElementCollection
                {
                    IdShort = Present("collection"),
                    Value = PresentElements(new AasSubmodelElementCollection
                    {
                        IdShort = Present("nested"),
                        Value = PresentElements(Property("leaf", AASDataTypeDefXsdDataType.String, "value"))
                    })
                }));
        }

        private static string[] CompareNodeSets(UANodeSet expected, UANodeSet actual)
        {
            var differences = new List<string>();
            Dictionary<string, UANode> right = actual.Items!.ToDictionary(node => node.NodeId!, StringComparer.Ordinal);
            foreach (UANode left in expected.Items!)
            {
                if (!right.TryGetValue(left.NodeId!, out UANode? candidate))
                {
                    differences.Add(left.NodeId + " missing.");
                    continue;
                }

                if (!string.Equals(left.BrowseName, candidate.BrowseName, StringComparison.Ordinal))
                {
                    differences.Add(left.NodeId + " BrowseName differs.");
                }

                string[] leftReferences = References(left);
                string[] rightReferences = References(candidate);
                if (!leftReferences.SequenceEqual(rightReferences))
                {
                    differences.Add(left.NodeId + " References differ.");
                }

                if (left is UAVariable leftVariable && candidate is UAVariable rightVariable &&
                    !ValuesEquivalent(leftVariable, rightVariable))
                {
                    differences.Add(left.NodeId + " Value differs.");
                }
            }

            if (right.Count != expected.Items!.Length)
            {
                differences.Add("Node count differs.");
            }

            return differences.ToArray();
        }

        private static bool ValuesEquivalent(UAVariable left, UAVariable right)
        {
            if (AasXsdTypeMap.TryGetValueType(ParseNodeId(left.DataType), NamespaceTable(), out AASDataTypeDefXsdDataType valueType))
            {
                if (valueType == AASDataTypeDefXsdDataType.Decimal &&
                    TryReadDecimalLexical(left.Value, out string? leftDecimal) &&
                    TryReadDecimalLexical(right.Value, out string? rightDecimal))
                {
                    return AasValueSpaceComparer.AreEquivalent(leftDecimal, rightDecimal, valueType);
                }

                Variant leftValue = ReadVariant(left);
                Variant rightValue = ReadVariant(right);
                return AasValueSpaceComparer.AreEquivalent(leftValue, rightValue, valueType);
            }

            string leftXml = left.Value?.OuterXml ?? string.Empty;
            string rightXml = right.Value?.OuterXml ?? string.Empty;
            return string.Equals(leftXml, rightXml, StringComparison.Ordinal);
        }

        private static string[] References(UANode node)
        {
            return (node.References ?? Array.Empty<Reference>())
                .Select(reference => reference.ReferenceType + "|" + reference.IsForward + "|" + reference.Value)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private static AasEnvironment Environment(AasSubmodel submodel)
        {
            return new AasEnvironment { Submodels = PresentSubmodels(submodel) };
        }

        private static AasSubmodel Submodel(string id, params AasSubmodelElement[] elements)
        {
            return new AasSubmodel
            {
                Id = id,
                IdShort = Present(id),
                SubmodelElements = PresentElements(elements)
            };
        }

        private static AasProperty Property(string? idShort, AASDataTypeDefXsdDataType valueType, string value)
        {
            return new AasProperty
            {
                IdShort = idShort is null ? AasOptional<string>.Absent : Present(idShort),
                ValueType = valueType,
                Value = AasOptional<Variant>.Present(new Variant(value))
            };
        }

        private static AASReferenceDataType Reference(string value)
        {
            AASKeyDataType key = Generated<AASKeyDataType>();
            key.Type = AASKeyTypesDataType.GlobalReference;
            key.Value = value;
            AASReferenceDataType reference = Generated<AASReferenceDataType>();
            reference.Type = AASReferenceTypesDataType.ExternalReference;
            reference.Keys = new ArrayOf<AASKeyDataType>(new[] { key });
            return reference;
        }

        private static AASLangStringDataType Lang(string language, string text)
        {
            return new AASLangStringDataType { Language = language, Text = text };
        }

        private static AasOptional<string> Present(string value)
        {
            return AasOptional<string>.Present(value);
        }

        private static AasOptional<T> Present<T>(T value)
            where T : class
        {
            return AasOptional<T>.Present(value);
        }

        private static AasOptional<ArrayOf<AasSubmodelElement>> PresentElements(params AasSubmodelElement[] values)
        {
            return AasOptional<ArrayOf<AasSubmodelElement>>.Present(new ArrayOf<AasSubmodelElement>(values));
        }

        private static AasOptional<ArrayOf<AasSubmodel>> PresentSubmodels(params AasSubmodel[] values)
        {
            return AasOptional<ArrayOf<AasSubmodel>>.Present(new ArrayOf<AasSubmodel>(values));
        }

        private static Variant ReadVariant(UAVariable variable)
        {
            if (variable.Value is null)
            {
                return Variant.Null;
            }

            using var decoder = new XmlDecoder(variable.Value, ServiceMessageContext.CreateEmpty(null!));
            return decoder.ReadVariantValue();
        }

        private static NodeId ParseNodeId(string? text)
        {
            return string.IsNullOrEmpty(text) ? NodeId.Null : NodeId.Parse(text);
        }

        private static NamespaceTable NamespaceTable()
        {
            var table = new NamespaceTable();
            table.GetIndexOrAppend(Opc.Ua.Namespaces.OpcUa);
            table.GetIndexOrAppend(Opc.Ua.Aas.V3.Namespaces.AasV3);
            return table;
        }

        private static bool TryReadDecimalLexical(System.Xml.XmlElement? value, out string lexical)
        {
            lexical = string.Empty;
            System.Xml.XmlElement? body = FirstElement(value, "Body");
            System.Xml.XmlElement? dec = FirstElement(body, "Decimal");
            if (dec is null ||
                !int.TryParse(
                    ChildText(dec, "Scale"),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int scale))
            {
                return false;
            }

            string digits = ChildText(dec, "Value");
            bool negative = digits.StartsWith('-');
            if (negative)
            {
                digits = digits.Substring(1);
            }

            if (scale <= 0)
            {
                lexical = (negative ? "-" : string.Empty) + digits + new string('0', -scale);
                return true;
            }

            if (digits.Length <= scale)
            {
                digits = new string('0', scale - digits.Length + 1) + digits;
            }

            int point = digits.Length - scale;
            lexical = (negative ? "-" : string.Empty) + digits.Insert(point, ".");
            return true;
        }

        private static System.Xml.XmlElement? FirstElement(System.Xml.XmlElement? element, string localName)
        {
            if (element is null)
            {
                return null;
            }

            foreach (System.Xml.XmlNode child in element.ChildNodes)
            {
                if (child is System.Xml.XmlElement childElement &&
                    string.Equals(childElement.LocalName, localName, StringComparison.Ordinal))
                {
                    return childElement;
                }
            }

            return null;
        }

        private static string ChildText(System.Xml.XmlElement? element, string localName)
        {
            return FirstElement(element, localName)?.InnerText ?? string.Empty;
        }

        private static T Generated<T>()
            where T : class, new()
        {
            return new T();
        }
    }
}
