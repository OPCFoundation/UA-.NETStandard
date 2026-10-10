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
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.SchemaRegistry.Formats;
using Opc.Ua.XRegistry;

namespace Opc.Ua.SchemaRegistry.Tests
{
    [TestFixture]
    [Category("SchemaRegistry")]
    public sealed class JsonSchemaFormatTests
    {
        [Test]
        public void FormatRegistrationIsIdempotentAndRequiresNoRegistryHost()
        {
            var services = new ServiceCollection();
            services.AddSchemaRegistryFormats().AddSchemaRegistryFormats();
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetServices<ISchemaFormatProvider>().Select(format => format.Format),
                Is.EquivalentTo(s_formats));
        }

        [TestCase("false", false)]
        [TestCase("true", true)]
        public void BooleanSchemasRemainDistinctFromObjects(string document, bool expected)
        {
            var provider = new JsonSchemaFormatProvider();
            SchemaContentDataType result = provider.Parse(Encoding.UTF8.GetBytes(document));
            Assert.That(result, Is.TypeOf<JsonSchemaContentDataType>());
            var content = (JsonSchemaContentDataType)result;
            Assert.That(content.Root, Is.TypeOf<JsonSchemaBooleanDataType>());
            Assert.That(((JsonSchemaBooleanDataType)content.Root).Value, Is.EqualTo(expected));
            Assert.That(Encoding.UTF8.GetString(provider.Serialize(content).ToArray()), Is.EqualTo(document));
        }

        [Test]
        public void StandardKeywordsAndExactUnknownDefaultsAreNativeAndRoundTrip()
        {
            const string source = "{\"type\":\"object\",\"properties\":{\"amount\":{\"type\":\"number\"," +
                "\"default\":1.00}},\"required\":[\"amount\"],\"x-vendor\":{\"value\":-0.00}}";
            var provider = new JsonSchemaFormatProvider();
            var content = (JsonSchemaContentDataType)provider.Parse(Encoding.UTF8.GetBytes(source));
            var root = (JsonSchemaObjectDataType)content.Root;
            Assert.That(root.Properties.Entries[0].Name, Is.EqualTo("amount"));
            var amount = (JsonSchemaObjectDataType)root.Properties.Entries[0].Value;
            Assert.That(amount.Default, Is.TypeOf<RegistryNumberValueDataType>());
            var number = (RegistryNumberValueDataType)amount.Default;
            Assert.That(number.Coefficient, Is.EqualTo(ByteString.FromHexString("64")));
            Assert.That(number.Exponent, Is.EqualTo(-2));
            Assert.That(root.AdditionalFields[0].Name, Is.EqualTo("x-vendor"));
            Assert.That(RegistryValues.Identical(
                RegistryValues.Parse(Encoding.UTF8.GetBytes(source)),
                RegistryValues.Parse(provider.Serialize(content).Span)), Is.True);
        }

        [Test]
        public void PresentNullExamplesCannotPassTheLosslessSerializationCheck()
        {
            var provider = new JsonSchemaFormatProvider();
            var content = (JsonSchemaContentDataType)provider.Parse("""{"examples":[]}"""u8);
            var root = (JsonSchemaObjectDataType)content.Root;
            root.Examples = ArrayOf<RegistryValueDataType>.Null;
            Assert.That(root.Examples.IsNull, Is.True);
            Assert.That(() => provider.Serialize(content), Throws.ArgumentException);
        }

        [Test]
        public void EveryStandardKeywordFamilyHasANamedLosslessProjection()
        {
            const string source = """
                {
                  "$schema":"https://json-schema.org/draft/2020-12/schema", "$id":"urn:test:schema",
                  "$anchor":"root", "$dynamicAnchor":"node", "$ref":"#/$defs/item", "$dynamicRef":"#node",
                  "$comment":"note", "title":"All keywords", "description":"native",
                  "type":["string","number"], "const":{"value":1.00}, "default":-0.00,
                  "enum":[null,1.00], "examples":[true,"example"], "readOnly":false,
                  "writeOnly":true, "deprecated":false, "minimum":-2, "maximum":20,
                  "exclusiveMinimum":-1, "exclusiveMaximum":21, "multipleOf":0.01,
                  "minLength":0, "maxLength":20, "pattern":"^a", "format":"date-time",
                  "contentEncoding":"base64", "contentMediaType":"application/json", "contentSchema":false,
                  "minItems":0, "maxItems":10, "minContains":0, "maxContains":2, "uniqueItems":true,
                  "items":true, "prefixItems":[false], "contains":false, "unevaluatedItems":true,
                  "properties":{"value":true}, "patternProperties":{"^x":false},
                  "additionalProperties":false, "unevaluatedProperties":true, "propertyNames":true,
                  "required":["value"], "dependentRequired":{"value":["other"]},
                  "dependentSchemas":{"value":false}, "minProperties":0, "maxProperties":10,
                  "allOf":[true], "anyOf":[false], "oneOf":[true], "not":false,
                  "if":true, "then":false, "else":true, "$defs":{"item":false},
                  "$vocabulary":{"https://json-schema.org/draft/2020-12/vocab/core":true}
                }
                """;
            var provider = new JsonSchemaFormatProvider();
            var content = (JsonSchemaContentDataType)provider.Parse(Encoding.UTF8.GetBytes(source));
            var root = (JsonSchemaObjectDataType)content.Root;
            using JsonDocument original = JsonDocument.Parse(source);
            int count = 0;
            foreach (JsonProperty property in original.RootElement.EnumerateObject())
            {
                count++;
            }
            Assert.That(root.PresentFields.Count, Is.EqualTo(count));
            Assert.That(root.AdditionalFields.Count, Is.Zero, "Standard fields cannot hide in AdditionalFields.");
            Assert.That(RegistryValues.Identical(
                RegistryValues.Parse(Encoding.UTF8.GetBytes(source)),
                RegistryValues.Parse(provider.Serialize(content).Span)), Is.True);
        }

        [TestCase("false", "FCBCF165908DD18A")]
        [TestCase("true", "B5BEA41B6C623F7C")]
        public void FingerprintsMatchFixedSha256JcsVectors(string document, string expected)
        {
            var provider = new JsonSchemaFormatProvider();
            Assert.That(provider.ComputeSchemaId(Encoding.UTF8.GetBytes(document)),
                Is.EqualTo(ByteString.FromHexString(expected)));
        }

        [Test]
        public void SelectionUsesJsonPointerEscapingAndDoesNotFollowReferences()
        {
            var provider = new JsonSchemaFormatProvider();
            SchemaContentDataType content = provider.Parse(
                Encoding.UTF8.GetBytes("{\"properties\":{\"a/b~c\":{\"$ref\":\"urn:not:fetched\"}}}"));
            var selected = (JsonSchemaObjectDataType)provider.Select(content, "/properties/a~1b~0c");
            Assert.That(selected.Ref, Is.EqualTo("urn:not:fetched"));
            Assert.Throws<ArgumentException>(() => provider.Select(content, "/properties/a~2b"));
            Assert.Throws<ArgumentException>(() => provider.Select(content, "/properties/missing"));
        }

        [TestCase("/default")]
        [TestCase("/const")]
        public void AnnotationObjectsAreNotSelectedAsSubschemas(string selector)
        {
            var provider = new JsonSchemaFormatProvider();
            SchemaContentDataType content = provider.Parse(
                Encoding.UTF8.GetBytes("{\"default\":{\"type\":\"integer\"},\"const\":{\"type\":\"string\"}}"));
            Assert.Throws<ArgumentException>(() => provider.Select(content, selector));
        }

        [TestCase("{\"type\":[]}")]
        [TestCase("{\"required\":[\"same\",\"same\"]}")]
        [TestCase("{\"minLength\":-1}")]
        [TestCase("{\"minItems\":1.5}")]
        [TestCase("{\"multipleOf\":0}")]
        [TestCase("{\"anyOf\":[]}")]
        [TestCase("{\"readOnly\":1}")]
        [TestCase("{\"$id\":\"urn:test:document#nonempty\"}")]
        [TestCase("{\"$anchor\":\"1bad\"}")]
        public void InvalidStandardKeywordShapesAreRejected(string document)
        {
            var provider = new JsonSchemaFormatProvider();
            Assert.Throws<ArgumentException>(() => provider.Parse(Encoding.UTF8.GetBytes(document)));
        }

        [Test]
        public void UnlistedPopulatedFieldsAreNotSilentlyDiscarded()
        {
            var provider = new JsonSchemaFormatProvider();
            var content = (JsonSchemaContentDataType)provider.Parse(Encoding.UTF8.GetBytes("{}"));
            ((JsonSchemaObjectDataType)content.Root).Title = "would disappear";
            Assert.Throws<ArgumentException>(() => provider.Serialize(content));
        }

        private static readonly string[] s_formats = ["JsonSchema/2020-12", "Avro/1.11", "ApacheArrow/1.0"];
    }
}
