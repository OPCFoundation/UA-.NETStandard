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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Schema.OpenApi;

namespace Opc.Ua.Schema.Tests.OpenApi
{
    /// <summary>
    /// Compares the OpenAPI document the stack generates from its routes
    /// and types with the document the OPC Foundation model compiler
    /// publishes (OPC 10000-6, G.3). Everything the stack and the
    /// publication both describe has to be equal; the differences that
    /// remain are listed here with their reason, and the tests fail when
    /// a listed difference disappears as well as when a new one appears.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Equality covers the paths, the operation ids, the request and
    /// response schema of every operation, the set of component schemas
    /// the operations reach, the enumeration members and values, and for
    /// every structure the property names, the property types and formats
    /// (including the integer ranges) and the inheritance.
    /// </para>
    /// <para>
    /// Not compared are the keywords that carry documentation or a default
    /// value rather than a contract: <c>description</c> (the publication
    /// links every schema to the specification), <c>default</c> (the
    /// publication states the .NET default of each property, the Compact
    /// encoding omits default values) and the document level
    /// <c>info</c>, <c>servers</c> and license data.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("WebApiOpenApiConformance")]
    [Parallelizable]
    public class WebApiOpenApiConformanceTests
    {
        /// <summary>
        /// The differences between the generated and the published
        /// document. Part 6 Table 42 names the status field of a DataValue
        /// "Status"; the publication names it "StatusCode"
        /// (UA-Nodeset#146). The stack follows the specification text, and
        /// its encoder and decoder only know "Status".
        /// </summary>
        private static readonly string[] s_knownDifferences =
        [
            "DataValue: property 'Status' only in the generated document",
            "DataValue: property 'StatusCode' only in the published document"
        ];

        /// <summary>
        /// The second difference: the encoder writes the JSON literal
        /// <c>null</c> for a null element of an array (OPC 10000-6, 5.4.5),
        /// so the generated document marks the items of an array
        /// <c>nullable</c> where the type of the element has a null value
        /// (strings, Guid, ByteString, NodeId, ExpandedNodeId,
        /// QualifiedName, XmlElement, LocalizedText, ExtensionObject,
        /// DataValue, DiagnosticInfo, Variant and the structures); the
        /// publication does not. The affected properties are derived from
        /// the published items in
        /// <c>ArrayItemsThatCanBeNullAreNullableInTheGeneratedDocument</c>.
        /// </summary>
        private const string kNullableItemsDifference = "array items are nullable only in the generated document";

        /// <summary>
        /// The third difference: JSON has no NaN and no infinity, so the
        /// encoder writes a Float or Double that is one of them as the
        /// string <c>NaN</c>, <c>Infinity</c> or <c>-Infinity</c>
        /// (OPC 10000-6, 5.4.2). The generated document allows those
        /// strings next to the number (<c>oneOf</c>); the publication
        /// allows the number only. The affected properties are derived from
        /// the published schemas in
        /// <c>FloatingPointPropertiesAllowTheStringsOfNotANumberAndTheInfinities</c>.
        /// </summary>
        private const string kSpecialValuesDifference =
            "floating point numbers also allow NaN and the infinities as strings only in the generated document";

        [Test]
        public void PathsAndOperationsMatchThePublication(
            [Values] WebApiServiceSet serviceSet,
            [Values] bool includeSchemas)
        {
            JsonObject published = LoadPublication(serviceSet);
            JsonObject generated = Generate(serviceSet, includeSchemas);

            JsonObject publishedPaths = published["paths"]!.AsObject();
            JsonObject generatedPaths = generated["paths"]!.AsObject();
            Assert.That(
                generatedPaths.Select(p => p.Key),
                Is.EquivalentTo(publishedPaths.Select(p => p.Key)));

            foreach (KeyValuePair<string, JsonNode?> path in publishedPaths)
            {
                JsonObject publishedPath = path.Value!.AsObject();
                JsonObject generatedPath = generatedPaths[path.Key]!.AsObject();
                Assert.That(
                    generatedPath.Select(m => m.Key),
                    Is.EquivalentTo(publishedPath.Select(m => m.Key)),
                    path.Key);

                JsonObject publishedPost = publishedPath["post"]!.AsObject();
                JsonObject generatedPost = generatedPath["post"]!.AsObject();
                Assert.That(
                    (string?)generatedPost["operationId"],
                    Is.EqualTo((string?)publishedPost["operationId"]),
                    path.Key);
                if (includeSchemas)
                {
                    Assert.That(
                        BodySchema(generatedPost["requestBody"]!),
                        Is.EqualTo(BodySchema(publishedPost["requestBody"]!)),
                        path.Key);
                    Assert.That(
                        BodySchema(generatedPost["responses"]!["200"]!),
                        Is.EqualTo(BodySchema(publishedPost["responses"]!["200"]!)),
                        path.Key);
                }
            }
        }

        [Test]
        public void ComponentSchemasAreThoseTheOperationsReach(
            [Values] WebApiServiceSet serviceSet)
        {
            JsonObject published = LoadPublication(serviceSet);
            JsonObject generated = Generate(serviceSet, includeSchemas: true);

            HashSet<string> reachable = ReachableSchemas(published);
            string[] generatedNames = [.. Schemas(generated).Select(s => s.Key)];

            Assert.That(generatedNames, Is.EquivalentTo(reachable));
        }

        [Test]
        public void StructuresAndEnumerationsMatchThePublicationExceptKnownDifferences(
            [Values] WebApiServiceSet serviceSet)
        {
            JsonObject published = LoadPublication(serviceSet);
            JsonObject generated = Generate(serviceSet, includeSchemas: true);
            JsonObject publishedSchemas = Schemas(published);

            var differences = new List<string>();
            foreach (KeyValuePair<string, JsonNode?> schema in Schemas(generated))
            {
                JsonObject? counterpart = publishedSchemas[schema.Key]?.AsObject();
                if (counterpart == null)
                {
                    differences.Add($"{schema.Key}: schema only in the generated document");
                    continue;
                }
                Compare(schema.Key, schema.Value!.AsObject(), counterpart, differences);
            }

            Assert.That(
                differences.Where(d => !IsRecordedDifference(d)),
                Is.EquivalentTo(s_knownDifferences));
        }

        [Test]
        public void ArrayItemsThatCanBeNullAreNullableInTheGeneratedDocument(
            [Values] WebApiServiceSet serviceSet)
        {
            JsonObject published = LoadPublication(serviceSet);
            JsonObject generated = Generate(serviceSet, includeSchemas: true);
            JsonObject publishedSchemas = Schemas(published);

            var differences = new List<string>();
            foreach (KeyValuePair<string, JsonNode?> schema in Schemas(generated))
            {
                Compare(schema.Key, schema.Value!.AsObject(), publishedSchemas[schema.Key]!.AsObject(), differences);
            }
            string[] nullable = [.. differences
                .Where(d => d.EndsWith(kNullableItemsDifference, StringComparison.Ordinal))
                .Select(d => d[..d.IndexOf(':', StringComparison.Ordinal)])];

            // The oracle is the published schema of the items: an object
            // component (other than a StatusCode, which is always written)
            // or a string that is not a date, a 64 bit integer or an
            // enumeration has a null value; numbers and enumerations not.
            var expected = new List<string>();
            HashSet<string> reachable = ReachableSchemas(published);
            foreach (KeyValuePair<string, JsonNode?> schema in publishedSchemas.Where(s => reachable.Contains(s.Key)))
            {
                foreach (KeyValuePair<string, JsonNode?> property in
                    schema.Value!.AsObject()["properties"]?.AsObject() ?? [])
                {
                    JsonObject shape = Normalize(property.Value!, isPublication: true);
                    if ((string?)shape["type"] == "array" &&
                        shape["items"] is JsonObject items &&
                        CanBeNull(items, publishedSchemas))
                    {
                        expected.Add($"{schema.Key}.{property.Key}");
                    }
                }
            }

            Assert.That(nullable, Is.Not.Empty);
            Assert.That(nullable, Is.EquivalentTo(expected));
        }

        [Test]
        public void FloatingPointPropertiesAllowTheStringsOfNotANumberAndTheInfinities(
            [Values] WebApiServiceSet serviceSet)
        {
            JsonObject published = LoadPublication(serviceSet);
            JsonObject generated = Generate(serviceSet, includeSchemas: true);
            JsonObject publishedSchemas = Schemas(published);

            var differences = new List<string>();
            foreach (KeyValuePair<string, JsonNode?> schema in Schemas(generated))
            {
                Compare(schema.Key, schema.Value!.AsObject(), publishedSchemas[schema.Key]!.AsObject(), differences);
            }
            string[] special = [.. differences
                .Where(d => d.EndsWith(kSpecialValuesDifference, StringComparison.Ordinal))
                .Select(d => d[..d.IndexOf(':', StringComparison.Ordinal)])];

            // The oracle is the published schema: a number, or an array of numbers.
            var expected = new List<string>();
            HashSet<string> reachable = ReachableSchemas(published);
            foreach (KeyValuePair<string, JsonNode?> schema in publishedSchemas.Where(s => reachable.Contains(s.Key)))
            {
                foreach (KeyValuePair<string, JsonNode?> property in
                    schema.Value!.AsObject()["properties"]?.AsObject() ?? [])
                {
                    JsonObject shape = Normalize(property.Value!, isPublication: true);
                    JsonObject element = (string?)shape["type"] == "array" && shape["items"] is JsonObject items
                        ? items
                        : shape;
                    if ((string?)element["type"] == "number")
                    {
                        expected.Add($"{schema.Key}.{property.Key}");
                    }
                }
            }

            Assert.That(special, Is.Not.Empty);
            Assert.That(special, Is.EquivalentTo(expected));
        }

        private static bool IsRecordedDifference(string difference)
        {
            return difference.EndsWith(kNullableItemsDifference, StringComparison.Ordinal) ||
                difference.EndsWith(kSpecialValuesDifference, StringComparison.Ordinal);
        }

        private static bool CanBeNull(JsonObject items, JsonObject schemas)
        {
            if ((string?)items["$ref"] is string reference)
            {
                string name = reference.Split('/')[^1];
                return name != "StatusCode" && (string?)schemas[name]!["type"] == "object";
            }
            return (string?)items["type"] == "string" &&
                (string?)items["format"] is null or
                    "uuid" or "byte" or "UaNodeId" or "UaExpandedNodeId" or "UaQualifiedName";
        }

        [Test]
        public void SessionlessSchemasAreIdenticalToAllServicesSchemas()
        {
            JsonObject all = Schemas(Generate(WebApiServiceSet.AllServices, includeSchemas: true));
            JsonObject sessionless = Schemas(Generate(WebApiServiceSet.Sessionless, includeSchemas: true));

            Assert.That(sessionless, Is.Not.Empty);
            foreach (KeyValuePair<string, JsonNode?> schema in sessionless)
            {
                Assert.That(
                    all[schema.Key]?.ToJsonString(),
                    Is.EqualTo(schema.Value!.ToJsonString()),
                    schema.Key);
            }
        }

        [Test]
        public void GeneratedDocumentIsAFractionOfThePublicationInSize()
        {
            JsonObject published = LoadPublication(WebApiServiceSet.AllServices);
            int publishedLength = published.ToJsonString().Length;

            int withoutSchemas = Generate(WebApiServiceSet.AllServices, includeSchemas: false)
                .ToJsonString().Length;
            int withSchemas = Generate(WebApiServiceSet.AllServices, includeSchemas: true)
                .ToJsonString().Length;

            Assert.That(withoutSchemas, Is.LessThan(withSchemas));
            Assert.That(withSchemas, Is.LessThan(publishedLength / 2));
        }

        private static void Compare(
            string name,
            JsonObject generated,
            JsonObject published,
            List<string> differences)
        {
            bool generatedIsEnumeration = generated.ContainsKey("enum");
            if (generatedIsEnumeration != published.ContainsKey("enum"))
            {
                differences.Add($"{name}: enumeration in only one document");
                return;
            }
            if (generatedIsEnumeration)
            {
                CompareKeyword(name, "enum", generated, published, differences);
                CompareKeyword(name, "x-enum-varnames", generated, published, differences);
                CompareKeyword(name, "type", generated, published, differences);
                CompareKeyword(name, "format", generated, published, differences);
                return;
            }

            foreach (string keyword in s_structureKeywords)
            {
                CompareKeyword(name, keyword, generated, published, differences);
            }

            JsonObject generatedProperties = generated["properties"]?.AsObject() ?? [];
            JsonObject publishedProperties = published["properties"]?.AsObject() ?? [];
            foreach (KeyValuePair<string, JsonNode?> property in generatedProperties)
            {
                if (!publishedProperties.TryGetPropertyValue(property.Key, out JsonNode? counterpart))
                {
                    differences.Add($"{name}: property '{property.Key}' only in the generated document");
                    continue;
                }
                JsonObject generatedNormalized = Normalize(property.Value!, isPublication: false);
                if (UnwrapNullableItems(generatedNormalized))
                {
                    differences.Add($"{name}.{property.Key}: {kNullableItemsDifference}");
                }
                generatedNormalized = UnwrapSpecialValues(generatedNormalized, out bool hadSpecialValues);
                if (hadSpecialValues)
                {
                    differences.Add($"{name}.{property.Key}: {kSpecialValuesDifference}");
                }
                string generatedShape = generatedNormalized.ToJsonString();
                string publishedShape = Normalize(counterpart!, isPublication: true).ToJsonString();
                if (generatedShape != publishedShape)
                {
                    differences.Add(
                        $"{name}.{property.Key}: generated {generatedShape}, published {publishedShape}");
                }
            }
            foreach (KeyValuePair<string, JsonNode?> property in publishedProperties)
            {
                if (!generatedProperties.ContainsKey(property.Key))
                {
                    differences.Add($"{name}: property '{property.Key}' only in the published document");
                }
            }
        }

        private static void CompareKeyword(
            string name,
            string keyword,
            JsonObject generated,
            JsonObject published,
            List<string> differences)
        {
            string? generatedValue = generated[keyword]?.ToJsonString();
            string? publishedValue = published[keyword]?.ToJsonString();
            if (generatedValue != publishedValue)
            {
                differences.Add(
                    $"{name}: '{keyword}' is {generatedValue ?? "absent"} in the generated " +
                    $"and {publishedValue ?? "absent"} in the published document");
            }
        }

        /// <summary>
        /// Reduces a property schema to the contract: drops the documentation
        /// and default keywords, writes a reference to an enumeration the way
        /// the generator does and orders the keywords. The publication names
        /// the enumeration of a property in <c>format</c> instead of
        /// referencing its schema.
        /// </summary>
        private static JsonObject Normalize(JsonNode schema, bool isPublication)
        {
            JsonObject source = schema.AsObject();
            if (isPublication &&
                (string?)source["type"] == "integer" &&
                (string?)source["format"] is string format &&
                !s_integerFormats.Contains(format))
            {
                return new JsonObject { ["$ref"] = "#/components/schemas/" + format };
            }

            var result = new JsonObject();
            foreach (KeyValuePair<string, JsonNode?> keyword in source.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (keyword.Key is "description" or "default")
                {
                    continue;
                }
                result[keyword.Key] = keyword.Key == "items"
                    ? Normalize(keyword.Value!, isPublication)
                    : keyword.Value?.DeepClone();
            }
            return result;
        }

        /// <summary>
        /// Takes the nullable form (<c>nullable</c> next to the item schema
        /// or next to an <c>allOf</c> with the reference) off the items of an
        /// array schema, so the shape compares with the publication.
        /// </summary>
        /// <returns><c>true</c> when the items were nullable.</returns>
        private static bool UnwrapNullableItems(JsonObject shape)
        {
            if (shape["items"] is not JsonObject items ||
                items["nullable"] is not JsonValue flag ||
                !flag.GetValue<bool>())
            {
                return false;
            }
            if (items["allOf"] is JsonArray { Count: 1 } allOf)
            {
                shape["items"] = allOf[0]!.DeepClone();
            }
            else
            {
                items.Remove("nullable");
            }
            return true;
        }

        /// <summary>
        /// Replaces the number or special value string alternatives of a
        /// property, or of the items of an array, by the number alone, so
        /// the shape compares with the publication.
        /// </summary>
        private static JsonObject UnwrapSpecialValues(JsonObject shape, out bool unwrapped)
        {
            if (shape["oneOf"] is JsonArray { Count: 2 } alternatives &&
                alternatives[0] is JsonObject number &&
                (string?)number["type"] == "number")
            {
                unwrapped = true;
                return Normalize(number, isPublication: false);
            }
            unwrapped = false;
            if (shape["items"] is JsonObject items)
            {
                shape["items"] = UnwrapSpecialValues(items, out unwrapped);
            }
            return shape;
        }

        private static HashSet<string> ReachableSchemas(JsonObject publication)
        {
            JsonObject schemas = Schemas(publication);
            var reachable = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(References(publication["paths"]!));
            while (pending.Count > 0)
            {
                string name = pending.Pop();
                if (!reachable.Add(name))
                {
                    continue;
                }
                foreach (string reference in References(schemas[name]!))
                {
                    pending.Push(reference);
                }
            }
            return reachable;
        }

        private static IEnumerable<string> References(JsonNode node)
        {
            switch (node)
            {
                case JsonObject jsonObject:
                    foreach (KeyValuePair<string, JsonNode?> member in jsonObject)
                    {
                        if (member.Key == "$ref")
                        {
                            yield return ((string)member.Value!).Split('/')[^1];
                        }
                        else if (member.Key == "format" &&
                            (string?)jsonObject["type"] == "integer" &&
                            !s_integerFormats.Contains((string)member.Value!))
                        {
                            // the publication names an enumeration in format
                            yield return (string)member.Value!;
                        }
                        else if (member.Value != null)
                        {
                            foreach (string reference in References(member.Value))
                            {
                                yield return reference;
                            }
                        }
                    }
                    break;
                case JsonArray jsonArray:
                    foreach (JsonNode? item in jsonArray)
                    {
                        if (item == null)
                        {
                            continue;
                        }
                        foreach (string reference in References(item))
                        {
                            yield return reference;
                        }
                    }
                    break;
            }
        }

        private static string BodySchema(JsonNode body)
        {
            return (string)body["content"]!["application/json"]!["schema"]!["$ref"]!;
        }

        private static JsonObject Schemas(JsonObject document)
        {
            return document["components"]!["schemas"]!.AsObject();
        }

        private static JsonObject Generate(WebApiServiceSet serviceSet, bool includeSchemas)
        {
            return new WebApiOpenApiGenerator().Generate(serviceSet, includeSchemas);
        }

        private static JsonObject LoadPublication(WebApiServiceSet serviceSet)
        {
            string fileName = serviceSet == WebApiServiceSet.Sessionless
                ? "opc.ua.openapi.sessionless.json"
                : "opc.ua.openapi.allservices.json";
            string resource = "Opc.Ua.Schema.Tests.OpenApi.Normative." + fileName;
            using Stream stream = typeof(WebApiOpenApiConformanceTests).Assembly
                .GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"The test resource '{resource}' is missing.");
            return JsonNode.Parse(stream)!.AsObject();
        }

        private static readonly string[] s_structureKeywords = ["type", "allOf", "additionalProperties"];

        private static readonly HashSet<string> s_integerFormats = new(StringComparer.Ordinal)
        {
            "int32",
            "int64"
        };
    }
}
