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
                        r.ReferenceType, "HasEncoding", StringComparison.Ordinal) && r.IsForward)
                    .Select(r => r.Value)
                    .OrderBy(v => v, StringComparer.Ordinal)
                    .ToArray(),
                Is.EqualTo(new[]
                {
                    TypeId + "/Default Binary",
                    TypeId + "/Default JSON",
                    TypeId + "/Default XML"
                }));
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
                    nodeSet.Items!.OfType<UADataType>().Single().References is { } refs && refs
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
                "\"uav:structureType\":\"" + structureType + "\"," +
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
                "\"uav:dataTypeDefinitions\":[" + definition + "]}");

            using WotDocument document = WotDocument.Parse(json);
            return WotNodeSetConverter.ToNodeSetResult(document);
        }
    }
}
