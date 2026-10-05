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

using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.Schema.OpenApi;

namespace Opc.Ua.Schema.Tests.OpenApi
{
    /// <summary>
    /// Tests of the OpenAPI schemas of the built-in types.
    /// </summary>
    [TestFixture]
    [Category("WebApiOpenApi")]
    [Parallelizable]
    public class OpenApiBuiltInSchemasTests
    {
        private static readonly string[] s_extensionObjectProperties = ["UaTypeId", "UaEncoding", "UaBody"];

        [Test]
        public void UndescribedBuiltInTypesAcceptAnyValue()
        {
            JsonObject schema = OpenApiBuiltInSchemas.CreateScalar(BuiltInType.Null);

            Assert.That(schema.ToJsonString(), Is.EqualTo("{}"));
        }

        [Test]
        public void ScalarBuiltInTypesHaveNoComponent()
        {
            Assert.That(OpenApiBuiltInSchemas.GetComponentName(BuiltInType.Int32), Is.Null);
            Assert.That(OpenApiBuiltInSchemas.GetComponentName(BuiltInType.Null), Is.Null);
        }

        [TestCase(BuiltInType.StatusCode, "StatusCode")]
        [TestCase(BuiltInType.LocalizedText, "LocalizedText")]
        [TestCase(BuiltInType.ExtensionObject, "ExtensionObject")]
        [TestCase(BuiltInType.DataValue, "DataValue")]
        [TestCase(BuiltInType.DiagnosticInfo, "DiagnosticInfo")]
        [TestCase(BuiltInType.Variant, "Variant")]
        public void StructuredBuiltInTypesAreComponents(BuiltInType type, string name)
        {
            Assert.That(OpenApiBuiltInSchemas.GetComponentName(type), Is.EqualTo(name));
            JsonObject component = OpenApiBuiltInSchemas.CreateComponent(name);
            Assert.That((string?)component["type"], Is.EqualTo("object"));
            Assert.That(component["properties"]!.AsObject(), Is.Not.Empty);
        }

        [Test]
        public void ExtensionObjectAllowsTheInlineBodyOfAJsonEncodedStructure()
        {
            JsonObject component = OpenApiBuiltInSchemas.CreateComponent("ExtensionObject");

            Assert.That(component.ContainsKey("additionalProperties"), Is.False);
            Assert.That(
                component["properties"]!.AsObject().Select(p => p.Key),
                Is.EqualTo(s_extensionObjectProperties));
        }
    }
}
