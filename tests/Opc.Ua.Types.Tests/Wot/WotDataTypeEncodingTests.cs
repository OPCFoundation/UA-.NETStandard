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
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

#nullable enable

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// A materialized Structure or Union carries the three DataTypeEncoding
    /// Objects a client needs to name the encoding it asked for.
    /// </summary>
    /// <remarks>
    /// A NodeSet has no DefaultEncodingId attribute: the fact a
    /// StructureDefinition states in an address space is carried in NodeSet
    /// form solely by the HasEncoding reference to the "Default Binary"
    /// Object. Asserting the Objects and the references is therefore the only
    /// way to assert the requirement at all, and it is what a consumer reading
    /// the file actually resolves.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotDataTypeEncodingTests
    {
        private const string TypeId = "ns=1;s=DataTypes/SampleSet";

        [TestCase("Default Binary")]
        [TestCase("Default XML")]
        [TestCase("Default JSON")]
        public void AConcreteStructureExposesEveryDefaultEncoding(string encoding)
        {
            UANodeSet nodeSet = Materialize(Structure());

            UAObject? node = Encodings(nodeSet)
                .FirstOrDefault(o => string.Equals(
                    o.BrowseName, encoding, StringComparison.Ordinal));

            Assert.Multiple(() =>
            {
                Assert.That(node, Is.Not.Null, EncodingNames(nodeSet));
                Assert.That(
                    node!.NodeId,
                    Is.EqualTo(TypeId + "/" + encoding),
                    "§6.11.7 derives the encoding identity from the name-derived " +
                    "String NodeId so the suffix always yields a valid identifier.");
                Assert.That(
                    (node!.References ?? []).Any(r =>
                        string.Equals(
                            r.ReferenceType, "HasTypeDefinition", StringComparison.Ordinal) &&
                        r.IsForward &&
                        string.Equals(
                            r.Value, "i=76", StringComparison.Ordinal)),
                    Is.True,
                    "An encoding Object is an instance of DataTypeEncodingType.");
                Assert.That(
                    (node!.References ?? []).Any(r =>
                        string.Equals(
                            r.ReferenceType, "HasEncoding", StringComparison.Ordinal) &&
                        !r.IsForward &&
                        string.Equals(r.Value, TypeId, StringComparison.Ordinal)),
                    Is.True,
                    "The inverse HasEncoding names the DataType the encoding encodes.");
            });
        }

        [Test]
        public void TheDataTypeReferencesEveryEncodingItExposes()
        {
            UANodeSet nodeSet = Materialize(Structure());

            UADataType type = nodeSet.Items!.OfType<UADataType>().Single();

            Assert.That(
                (type.References ?? [])
                    .Where(r => string.Equals(
                        r.ReferenceType, "HasEncoding", StringComparison.Ordinal) &&
                        r.IsForward)
                    .Select(r => r.Value)
                    .OrderBy(v => v, StringComparer.Ordinal)
                    .ToArray(),
                Is.EqualTo(
                [
                    TypeId + "/Default Binary",
                    TypeId + "/Default JSON",
                    TypeId + "/Default XML"
                ]));
        }

        /// <summary>
        /// A Union is a Structure for this purpose, so it exposes the same
        /// three Objects.
        /// </summary>
        [Test]
        public void AUnionExposesTheSameEncodings()
        {
            UANodeSet nodeSet = Materialize(
                Structure(structureType: "Union"));

            Assert.That(Encodings(nodeSet), Has.Count.EqualTo(3), EncodingNames(nodeSet));
        }

        /// <summary>
        /// An authored identity wins over the derived one, because the
        /// authored NodeId is the one an existing address space already
        /// publishes.
        /// </summary>
        [Test]
        public void AnAuthoredEncodingIdentityIsUsedAsWritten()
        {
            UANodeSet nodeSet = Materialize(
                Structure(extra: "\"uav:binaryEncodingId\":\"nsu=urn:test:pump;i=5001\","));

            UAObject binary = Encodings(nodeSet)
                .Single(o => string.Equals(
                    o.BrowseName, "Default Binary", StringComparison.Ordinal));

            Assert.Multiple(() =>
            {
                Assert.That(binary.NodeId, Is.EqualTo("ns=1;i=5001"));
                Assert.That(
                    Encodings(nodeSet)
                        .Single(o => string.Equals(
                            o.BrowseName, "Default XML", StringComparison.Ordinal))
                        .NodeId,
                    Is.EqualTo(TypeId + "/Default XML"),
                    "Authoring one identity does not disturb the others.");
            });
        }

        /// <summary>
        /// A concrete type reachable only from inside other Structures may
        /// state <c>uav:hasDefaultEncoding</c> false, and then no encoding
        /// Object is generated: advertising three Objects nothing can reach is
        /// worse than advertising none.
        /// </summary>
        [Test]
        public void HasDefaultEncodingFalseSuppressesEveryEncoding()
        {
            UANodeSet nodeSet = Materialize(
                Structure(extra: "\"uav:hasDefaultEncoding\":false,"));

            Assert.Multiple(() =>
            {
                Assert.That(Encodings(nodeSet), Is.Empty, EncodingNames(nodeSet));
                Assert.That(
                    nodeSet.Items!.OfType<UADataType>().Single().References is { } refs &&
                    refs
                        .Any(r => string.Equals(
                            r.ReferenceType, "HasEncoding", StringComparison.Ordinal)),
                    Is.False);
            });
        }

        [Test]
        public void HasDefaultEncodingTrueIsTheDefaultRestated()
        {
            UANodeSet nodeSet = Materialize(
                Structure(extra: "\"uav:hasDefaultEncoding\":true,"));

            Assert.That(Encodings(nodeSet), Has.Count.EqualTo(3), EncodingNames(nodeSet));
        }

        /// <summary>
        /// An abstract type has a null DefaultEncodingId and no encoding
        /// Objects, because no value of it is ever encoded on its own.
        /// </summary>
        [Test]
        public void AnAbstractStructureExposesNoEncoding()
        {
            UANodeSet nodeSet = Materialize(
                Structure(extra: "\"uav:isAbstract\":true,"));

            Assert.That(Encodings(nodeSet), Is.Empty, EncodingNames(nodeSet));
        }

        [Test]
        public void AnAbstractStructureStatingAnEncodingIdentityIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                Structure(
                    extra: "\"uav:isAbstract\":true," +
                        "\"uav:binaryEncodingId\":\"nsu=urn:test:pump;i=5001\","));

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                    d.Message.Contains(
                        "uav:binaryEncodingId", StringComparison.Ordinal)),
                Is.True,
                Messages(result));
        }

        [Test]
        public void AnEnumerationExposesNoEncoding()
        {
            UANodeSet nodeSet = Materialize(
                "{\"@id\":\"urn:test:pump#Mode\"," +
                "\"@type\":\"uav:EnumDefinition\"," +
                "\"uav:dataTypeName\":\"pump:Mode\"," +
                "\"uav:dataTypeSubtypeOf\":\"i=29\"," +
                "\"uav:fields\":[{\"@type\":\"uav:EnumField\"," +
                "\"uav:fieldName\":\"Idle\",\"uav:value\":0}]}");

            Assert.That(Encodings(nodeSet), Is.Empty, EncodingNames(nodeSet));
        }

        /// <summary>
        /// Only a kind that has encodings to begin with may say anything about
        /// them, so an Enumeration stating the term is an authoring error and
        /// not a silently ignored one.
        /// </summary>
        [Test]
        public void AnEnumerationStatingHasDefaultEncodingIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "{\"@id\":\"urn:test:pump#Mode\"," +
                "\"@type\":\"uav:EnumDefinition\"," +
                "\"uav:dataTypeName\":\"pump:Mode\"," +
                "\"uav:dataTypeSubtypeOf\":\"i=29\"," +
                "\"uav:hasDefaultEncoding\":true," +
                "\"uav:fields\":[{\"@type\":\"uav:EnumField\"," +
                "\"uav:fieldName\":\"Idle\",\"uav:value\":0}]}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                    d.Message.Contains(
                        "uav:hasDefaultEncoding", StringComparison.Ordinal)),
                Is.True,
                Messages(result));
        }

        /// <summary>
        /// A <c>uav:SimpleDataType</c> is a named subtype of a built-in type
        /// and carries no DataTypeDefinition, so it has no encoding Objects to
        /// say anything about either. Stating the term on one is the same
        /// authoring error, and the type is still materialized - with no
        /// encoding and no place in the nested-only set, because a rejected
        /// term states nothing about reachability.
        /// </summary>
        [Test]
        public void ASimpleDataTypeStatingHasDefaultEncodingIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "{\"@id\":\"urn:test:pump#Serial\"," +
                "\"@type\":\"uav:SimpleDataType\"," +
                "\"uav:dataTypeName\":\"pump:Serial\"," +
                "\"uav:dataTypeSubtypeOf\":\"i=12\"," +
                "\"uav:hasDefaultEncoding\":true}");

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Diagnostics.Any(d =>
                        d.Severity == WotDiagnosticSeverity.Error &&
                        d.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                        d.Message.Contains(
                            "uav:hasDefaultEncoding", StringComparison.Ordinal)),
                    Is.True,
                    Messages(result));
                Assert.That(
                    result.Value!.Items!.OfType<UADataType>()
                        .Single(d => string.Equals(
                            d.BrowseName, "1:Serial", StringComparison.Ordinal))
                        .Definition,
                    Is.Null,
                    "A SimpleDataType carries no DataTypeDefinition.");
                Assert.That(Encodings(result.Value!), Is.Empty);
            });
        }

        [Test]
        public void AnInheritedFieldCannotChangeItsDataType()
        {
            WotConversionResult<UANodeSet> result = Convert(
                """
                {
                  "@id": "urn:test:pump#Base",
                  "@type": "uav:StructureDefinition",
                  "uav:dataTypeName": "pump:Base",
                  "uav:fields": [{ "uav:fieldName": "A", "uav:fieldDataTypeId": "i=1" }]
                },
                {
                  "@id": "urn:test:pump#Derived",
                  "@type": "uav:StructureDefinition",
                  "uav:dataTypeName": "pump:Derived",
                  "uav:dataTypeSubtypeOf": { "@id": "urn:test:pump#Base" },
                  "uav:fields": [{ "uav:fieldName": "A", "uav:fieldDataTypeId": "i=12" }]
                }
                """);

            Assert.That(result.HasErrors, Is.True, Messages(result));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid), Is.True, Messages(result));
        }

        [Test]
        public void SubtypeCyclesThroughDataTypeIdsAreRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                """
                {
                  "@id": "urn:test:pump#A",
                  "@type": "uav:StructureDefinition",
                  "uav:dataTypeName": "pump:A",
                  "uav:dataTypeId": "nsu=urn:test:pump;i=2001",
                  "uav:dataTypeSubtypeOf": { "uav:dataTypeId": "nsu=urn:test:pump;i=2002" },
                  "uav:fields": []
                },
                {
                  "@id": "urn:test:pump#B",
                  "@type": "uav:StructureDefinition",
                  "uav:dataTypeName": "pump:B",
                  "uav:dataTypeId": "nsu=urn:test:pump;i=2002",
                  "uav:dataTypeSubtypeOf": { "uav:dataTypeId": "nsu=urn:test:pump;i=2001" },
                  "uav:fields": []
                }
                """);

            Assert.That(result.HasErrors, Is.True, Messages(result));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                diagnostic.Message.Contains("ancestor", StringComparison.Ordinal)), Is.True, Messages(result));
        }

        [TestCase("i=26")]
        [TestCase("i=27")]
        [TestCase("i=28")]
        public void SimpleAliasesCannotTerminateAtAnAbstractNumericType(string terminal)
        {
            WotConversionResult<UANodeSet> result = Convert(
                $$"""
                {
                  "@id": "urn:test:pump#Scalar",
                  "@type": "uav:SimpleDataType",
                  "uav:dataTypeName": "pump:Scalar",
                  "uav:dataTypeSubtypeOf": { "uav:dataTypeId": "{{terminal}}" }
                }
                """);

            Assert.That(result.HasErrors, Is.True, Messages(result));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                diagnostic.Message.Contains(terminal, StringComparison.Ordinal)), Is.True, Messages(result));
        }

        [Test]
        public void AnOptionSetMayUseAnUnsignedSimpleAlias()
        {
            WotConversionResult<UANodeSet> result = Convert(
                """
                {
                  "@id": "urn:test:pump#Word",
                  "@type": "uav:SimpleDataType",
                  "uav:dataTypeName": "pump:Word",
                  "uav:dataTypeId": "nsu=urn:test:pump;i=3001",
                  "uav:dataTypeSubtypeOf": { "uav:dataTypeId": "i=5" }
                },
                {
                  "@id": "urn:test:pump#Flags",
                  "@type": "uav:EnumDefinition",
                  "uav:dataTypeName": "pump:Flags",
                  "uav:dataTypeId": "nsu=urn:test:pump;i=3002",
                  "uav:isOptionSet": true,
                  "uav:dataTypeSubtypeOf": { "@id": "urn:test:pump#Word" },
                  "uav:enumFields": [{ "uav:enumName": "First", "uav:enumValue": 0 }]
                }
                """);

            Assert.That(result.HasErrors, Is.False, Messages(result));
            UADataType flags = result.Value!.Items!.OfType<UADataType>()
                .Single(node => node.NodeId == "ns=1;i=3002");
            Assert.That(flags.References!.Single(reference =>
                reference.ReferenceType == "HasSubtype" && !reference.IsForward).Value,
                Is.EqualTo("ns=1;i=3001"));
            Assert.That(flags.Definition!.IsOptionSet, Is.True);
            Assert.That(flags.Definition.Field!.Single().Name, Is.EqualTo("First"));
            Assert.That(flags.Definition.Field!.Single().Value, Is.Zero);
        }

        [TestCase("\"uav:valueRank\":1", "\"uav:valueRank\":2", "Structure", "ValueRank")]
        [TestCase("\"uav:valueRank\":2,\"uav:arrayDimensions\":[2,3]",
            "\"uav:valueRank\":2,\"uav:arrayDimensions\":[2,4]", "Structure", "ArrayDimensions")]
        [TestCase("\"uav:isOptional\":false", "\"uav:isOptional\":true",
            "StructureWithOptionalFields", "IsOptional")]
        [TestCase("\"uav:allowSubtypes\":false", "\"uav:allowSubtypes\":true",
            "StructureWithSubtypedValues", "AllowSubTypes")]
        [TestCase("\"uav:maxStringLength\":10", "\"uav:maxStringLength\":20", "Structure", "MaxStringLength")]
        [TestCase("\"title\":\"First\"", "\"title\":\"Second\"", "Structure", "DisplayName")]
        [TestCase("\"description\":\"First\"", "\"description\":\"Second\"", "Structure", "Description")]
        public void EveryInheritedFieldAttributeIsValidated(
            string baseAttributes,
            string derivedAttributes,
            string structureType,
            string attribute)
        {
            WotConversionResult<UANodeSet> result = Convert(
                $$"""
                {
                  "@id": "urn:test:pump#Base", "@type": "uav:StructureDefinition",
                  "uav:dataTypeName": "pump:Base", "uav:dataTypeId": "nsu=urn:test:pump;i=2201",
                  "uav:structureType": "{{structureType}}",
                  "uav:fields": [{ "uav:fieldName": "A", "uav:fieldDataTypeId": "i=12", {{baseAttributes}} }]
                },
                {
                  "@id": "urn:test:pump#Derived", "@type": "uav:StructureDefinition",
                  "uav:dataTypeName": "pump:Derived", "uav:dataTypeId": "nsu=urn:test:pump;i=2202",
                  "uav:structureType": "{{structureType}}",
                  "uav:dataTypeSubtypeOf": { "uav:dataTypeId": "nsu=urn:test:pump;i=2201" },
                  "uav:fields": [{ "uav:fieldName": "A", "uav:fieldDataTypeId": "i=12", {{derivedAttributes}} }]
                }
                """);

            Assert.That(result.Success, Is.False);
            WotDiagnostic error = result.Diagnostics.Single(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid);
            Assert.That(error.Message, Does.Contain(attribute));
            Assert.That(error.Location?.NodeId, Is.EqualTo("nsu=urn:test:pump;i=2202"));
            Assert.That(error.Location?.JsonPointer, Is.EqualTo("/uav:dataTypeDefinitions/1/uav:fields/0"));
        }

        [TestCase(/*lang=json,strict*/ """{"@id":"urn:test:pump#B"}""", /*lang=json,strict*/ """{"@id":"urn:test:pump#A"}""")]
        [TestCase("\"urn:test:pump#B\"", "\"urn:test:pump#A\"")]
        [TestCase("\"nsu=urn:test:pump;i=2302\"", "\"nsu=urn:test:pump;i=2301\"")]
        [TestCase(/*lang=json,strict*/ """{"uav:dataTypeName":"pump:B"}""", /*lang=json,strict*/ """{"uav:dataTypeName":"pump:A"}""")]
        [TestCase("\"pump:B\"", "\"pump:A\"")]
        public void EveryResolvedSubtypeReferenceFormParticipatesInCycleChecks(string baseOfA, string baseOfB)
        {
            WotConversionResult<UANodeSet> result = Convert(
                $$"""
                {
                  "@id": "urn:test:pump#A", "@type": "uav:StructureDefinition",
                  "uav:dataTypeName": "pump:A", "uav:dataTypeId": "nsu=urn:test:pump;i=2301",
                  "uav:dataTypeSubtypeOf": {{baseOfA}}, "uav:fields": []
                },
                {
                  "@id": "urn:test:pump#B", "@type": "uav:StructureDefinition",
                  "uav:dataTypeName": "pump:B", "uav:dataTypeId": "nsu=urn:test:pump;i=2302",
                  "uav:dataTypeSubtypeOf": {{baseOfB}}, "uav:fields": []
                }
                """);

            Assert.That(result.Success, Is.False);
            WotDiagnostic cycle = result.Diagnostics.Single(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                diagnostic.Message.Contains("ancestor", StringComparison.Ordinal));
            Assert.That(cycle.Location?.NodeId, Is.EqualTo("nsu=urn:test:pump;i=2301"));
            Assert.That(cycle.Location?.JsonPointer, Is.EqualTo("/uav:dataTypeDefinitions/0/uav:dataTypeSubtypeOf"));
        }

        [TestCase("i=3", 7, false)]
        [TestCase("i=3", 8, true)]
        [TestCase("i=5", 15, false)]
        [TestCase("i=5", 16, true)]
        [TestCase("i=7", 31, false)]
        [TestCase("i=7", 32, true)]
        [TestCase("i=9", 63, false)]
        [TestCase("i=9", 64, true)]
        public void OptionSetAliasChainsUseTheTerminalUnsignedWidth(string terminal, int bit, bool invalid)
        {
            WotConversionResult<UANodeSet> result = Convert(
                $$"""
                {
                  "@id": "urn:test:pump#Word1", "@type": "uav:SimpleDataType",
                  "uav:dataTypeName": "pump:Word1", "uav:dataTypeId": "nsu=urn:test:pump;i=2401",
                  "uav:dataTypeSubtypeOf": { "uav:dataTypeId": "{{terminal}}" }
                },
                {
                  "@id": "urn:test:pump#Word2", "@type": "uav:SimpleDataType",
                  "uav:dataTypeName": "pump:Word2", "uav:dataTypeId": "nsu=urn:test:pump;i=2402",
                  "uav:dataTypeSubtypeOf": { "uav:dataTypeName": "pump:Word1" }
                },
                {
                  "@id": "urn:test:pump#Flags", "@type": "uav:EnumDefinition",
                  "uav:dataTypeName": "pump:Flags", "uav:dataTypeId": "nsu=urn:test:pump;i=2403",
                  "uav:isOptionSet": true, "uav:dataTypeSubtypeOf": "pump:Word2",
                  "uav:enumFields": [{ "uav:enumName": "Flag", "uav:enumValue": {{bit}} }]
                }
                """);

            Assert.That(result.HasErrors, Is.EqualTo(invalid), Messages(result));
            UADataType flags = result.Value!.Items!.OfType<UADataType>().Single(node => node.NodeId == "ns=1;i=2403");
            Assert.That(flags.References!.Single(reference =>
                reference.ReferenceType == "HasSubtype" && !reference.IsForward).Value,
                Is.EqualTo("ns=1;i=2402"));
            if (invalid)
            {
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                    diagnostic.Message.Contains("bit " + bit, StringComparison.Ordinal)), Is.True);
            }
            else
            {
                Assert.That(flags.Definition!.Field!.Single().Value, Is.EqualTo(bit));
            }
        }

        [TestCase("Structure", "i=12756", false)]
        [TestCase("Union", "i=22", false)]
        [TestCase("Union", "i=12756", true)]
        public void UnionKindsAndAbstractnessCannotContradictTheirDefinition(
            string structureType,
            string baseType,
            bool isAbstract)
        {
            string extra = "\"uav:dataTypeSubtypeOf\":{\"uav:dataTypeId\":\"" +
                baseType +
                "\"}," +
                "\"uav:isAbstract\":" +
                (isAbstract ? "true" : "false") +
                ",";
            WotConversionResult<UANodeSet> result = Convert(Structure(structureType, extra));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                diagnostic.Location?.JsonPointer == (isAbstract
                    ? "/uav:dataTypeDefinitions/0/uav:isAbstract"
                    : "/uav:dataTypeDefinitions/0/uav:dataTypeSubtypeOf")), Is.True, Messages(result));
        }

        [Test]
        public void InheritedFieldDefaultsAndIdentityAliasesAreEquivalent()
        {
            WotConversionResult<UANodeSet> result = Convert(
                """
                {
                  "@id": "urn:test:pump#Base", "@type": "uav:StructureDefinition",
                  "uav:dataTypeName": "pump:Base", "uav:dataTypeId": "nsu=urn:test:pump;i=2701",
                  "uav:fields": [{ "uav:fieldName": "A", "uav:fieldDataTypeName": "ua:String" }]
                },
                {
                  "@id": "urn:test:pump#Derived", "@type": "uav:StructureDefinition",
                  "uav:dataTypeName": "pump:Derived", "uav:dataTypeId": "nsu=urn:test:pump;i=2702",
                  "uav:dataTypeSubtypeOf": "pump:Base",
                  "uav:fields": [{
                    "uav:fieldName": "A", "uav:fieldDataTypeId": "nsu=http://opcfoundation.org/UA/;i=12",
                    "uav:valueRank": -1, "uav:arrayDimensions": [],
                    "uav:isOptional": false, "uav:allowSubtypes": false, "uav:maxStringLength": 0
                  }]
                }
                """);

            Assert.That(result.Success, Is.True, Messages(result));
            UADataType derived = result.Value!.Items!.OfType<UADataType>().Single(node => node.NodeId == "ns=1;i=2702");
            Assert.That(derived.Definition!.Field!.Single().DataType, Is.EqualTo("i=12"));
            Assert.That(derived.References!.Single(reference =>
                reference.ReferenceType == "HasSubtype" && !reference.IsForward).Value,
                Is.EqualTo("ns=1;i=2701"));
        }

        [Test]
        public void AnInheritedEnumerationFieldCannotChangeItsValue()
        {
            WotConversionResult<UANodeSet> result = Convert(
                """
                {
                  "@id": "urn:test:pump#Base", "@type": "uav:EnumDefinition",
                  "uav:dataTypeName": "pump:Base",
                  "uav:enumFields": [{ "uav:enumName": "A", "uav:enumValue": 0 }]
                },
                {
                  "@id": "urn:test:pump#Derived", "@type": "uav:EnumDefinition",
                  "uav:dataTypeName": "pump:Derived",
                  "uav:dataTypeSubtypeOf": { "uav:dataTypeName": "pump:Base" },
                  "uav:enumFields": [{ "uav:enumName": "A", "uav:enumValue": 1 }]
                }
                """);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                diagnostic.Message.Contains("Value", StringComparison.Ordinal) &&
                diagnostic.Location?.JsonPointer == "/uav:dataTypeDefinitions/1/uav:enumFields/0"), Is.True);
        }

        private static List<UAObject> Encodings(UANodeSet nodeSet)
        {
            return [.. nodeSet.Items!
                .OfType<UAObject>()
                .Where(o => o.References is not null &&
                    o.References.Any(r => string.Equals(
                        r.ReferenceType, "HasTypeDefinition", StringComparison.Ordinal) &&
                        string.Equals(r.Value, "i=76", StringComparison.Ordinal)))];
        }

        private static string EncodingNames(UANodeSet nodeSet)
        {
            return string.Join(
                ", ",
                nodeSet.Items!.OfType<UAObject>().Select(o => o.BrowseName + "=" + o.NodeId));
        }

        private static string Messages(WotConversionResult<UANodeSet> result)
        {
            return string.Join("; ", result.Diagnostics.Select(d => d.Message));
        }

        private static string Structure(
            string structureType = "Structure", string extra = "")
        {
            return "{\"@id\":\"urn:test:pump#SampleSet\"," +
                "\"@type\":\"uav:StructureDefinition\"," +
                "\"uav:dataTypeName\":\"pump:SampleSet\"," +
                extra +
                "\"uav:structureType\":\"" +
                structureType +
                "\"," +
                "\"uav:fields\":[{\"@type\":\"uav:StructureField\"," +
                "\"uav:fieldName\":\"Sample\"," +
                "\"uav:fieldDataTypeName\":\"ua:Double\"," +
                "\"uav:fieldDataTypeId\":\"i=11\"}]}";
        }

        private static UANodeSet Materialize(string definition)
        {
            WotConversionResult<UANodeSet> result = Convert(definition);

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty,
                Messages(result));
            Assert.That(result.Value, Is.Not.Null);
            return result.Value!;
        }

        private static WotConversionResult<UANodeSet> Convert(string definition)
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"," +
                "\"uav:dataTypeDefinitions\":[" +
                definition +
                "]}");

            using var document = WotDocument.Parse(json);
            return WotNodeSetConverter.ToNodeSetResult(document);
        }
    }
}
