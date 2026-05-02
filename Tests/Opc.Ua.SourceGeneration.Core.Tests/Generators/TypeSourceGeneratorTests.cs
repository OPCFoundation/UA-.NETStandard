/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the DataTypeSourceGenerator class.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [Parallelizable]
    public class TypeSourceGeneratorTests
    {
        [Test]
        public void GenerateSimpleClassProducesEncodeDecodeAndActivator()
        {
            var model = new TypeSourceModel
            {
                ClassName = "MyConfig",
                Namespace = "MyApp.Config",
                NamespaceUri = "urn:myapp:config",
                NamespaceSymbol = "MyAppConfig",
                IsEnum = false,
                IsRecord = false,
                Fields =
                [
                    CreateField("Name", "String"),
                    CreateField("Port", "Int32"),
                    CreateField("Enabled", "Boolean")
                ]
            };

            string result = TypeSourceGenerator.Generate(model);

            Assert.That(result, Does.Contain("partial class MyConfig"));
            Assert.That(result, Does.Contain("IEncodeable"));
            Assert.That(result, Does.Contain("encoder.WriteString(\"Name\", Name)"));
            Assert.That(result, Does.Contain("encoder.WriteInt32(\"Port\", Port)"));
            Assert.That(result, Does.Contain("encoder.WriteBoolean(\"Enabled\", Enabled)"));
            Assert.That(result, Does.Contain("decoder.ReadString(\"Name\")"));
            Assert.That(result, Does.Contain("decoder.ReadInt32(\"Port\")"));
            Assert.That(result, Does.Contain("decoder.ReadBoolean(\"Enabled\")"));
            Assert.That(result, Does.Contain("public virtual void Encode"));
            Assert.That(result, Does.Contain("public virtual void Decode"));
            Assert.That(result, Does.Contain("public virtual bool IsEqual"));
            Assert.That(result, Does.Contain("public virtual object Clone"));
            Assert.That(result, Does.Contain("MyConfigActivator"));
            Assert.That(result, Does.Contain("EncodeableType<MyConfig>"));
            Assert.That(result, Does.Contain("AddMyAppConfigDataTypes"));
        }

        [Test]
        public void GenerateWithCustomFieldNamesUsesFieldName()
        {
            var model = new TypeSourceModel
            {
                ClassName = "TestType",
                Namespace = "Test.Ns",
                NamespaceUri = "urn:test",
                NamespaceSymbol = "TestNs",
                IsEnum = false,
                IsRecord = false,
                Fields =
                [
                    new TypeFieldModel
                    {
                        PropertyName = "ServerName",
                        FieldName = "server_name",
                        TypeName = "global::System.String",
                        ShortTypeName = "String",
                        Order = 0
                    }
                ]
            };

            string result = TypeSourceGenerator.Generate(model);

            Assert.That(result, Does.Contain("encoder.WriteString(\"server_name\", ServerName)"));
            Assert.That(result, Does.Contain("decoder.ReadString(\"server_name\")"));
        }

        [Test]
        public void GenerateRecordClassDelegatesCloneAndIsEqual()
        {
            var model = new TypeSourceModel
            {
                ClassName = "MyRecord",
                Namespace = "Test.Records",
                NamespaceUri = "urn:test:records",
                NamespaceSymbol = "TestRecords",
                IsEnum = false,
                IsRecord = true,
                Fields =
                [
                    CreateField("Value", "Int32")
                ]
            };

            string result = TypeSourceGenerator.Generate(model);

            Assert.That(result, Does.Contain("this with { }"));
            Assert.That(result, Does.Contain("Equals(encodeable as MyRecord)"));
            Assert.That(result, Does.Not.Contain("MemberwiseClone"));
        }

        [Test]
        public void GenerateSealedRecordOmitsVirtual()
        {
            var model = new TypeSourceModel
            {
                ClassName = "MySealedRecord",
                Namespace = "Test.Records",
                NamespaceUri = "urn:test:records",
                NamespaceSymbol = "TestRecords",
                IsEnum = false,
                IsRecord = true,
                IsSealed = true,
                Fields =
                [
                    CreateField("Value", "Int32")
                ]
            };

            string result = TypeSourceGenerator.Generate(model);

            Assert.That(result, Does.Contain("this with { }"));
            Assert.That(result, Does.Contain("public void Encode"));
            Assert.That(result, Does.Not.Contain("virtual void Encode"));
            Assert.That(result, Does.Not.Contain("override void Encode"));
        }

        [Test]
        public void GenerateDerivedRecordUsesOverrideAndBaseCall()
        {
            var model = new TypeSourceModel
            {
                ClassName = "MyDerivedRecord",
                Namespace = "Test.Records",
                NamespaceUri = "urn:test:records",
                NamespaceSymbol = "TestRecords",
                IsEnum = false,
                IsRecord = true,
                IsDerived = true,
                BaseTypeIsEncodeable = true,
                Fields =
                [
                    CreateField("Extra", "String")
                ]
            };

            string result = TypeSourceGenerator.Generate(model);

            Assert.That(result, Does.Contain("override void Encode"));
            Assert.That(result, Does.Contain("base.Encode(encoder)"));
            Assert.That(result, Does.Contain("override void Decode"));
            Assert.That(result, Does.Contain("base.Decode(decoder)"));
            Assert.That(result, Does.Contain("override bool IsEqual"));
            Assert.That(result, Does.Contain("Equals(encodeable as MyDerivedRecord)"));
            Assert.That(result, Does.Not.Contain("this with { }"),
                "Derived record does not re-declare Clone");
        }

        [Test]
        public void GenerateEnumProducesEnumeratedTypeActivator()
        {
            var model = new TypeSourceModel
            {
                ClassName = "MyEnum",
                Namespace = "Test.Enums",
                NamespaceUri = "urn:test:enums",
                NamespaceSymbol = "TestEnums",
                IsEnum = true,
                EnumMembers =
                [
                    new TypeEnumMember { Name = "None", Value = "0" },
                    new TypeEnumMember { Name = "Active", Value = "1" },
                    new TypeEnumMember { Name = "Disabled", Value = "2" }
                ]
            };

            string result = TypeSourceGenerator.Generate(model);

            Assert.That(result, Does.Contain("MyEnumActivator"));
            Assert.That(result, Does.Contain("EnumeratedType<MyEnum>"));
            Assert.That(result, Does.Contain("AddTestEnumsDataTypes"));
            Assert.That(result, Does.Not.Contain("partial class MyEnum"));
            Assert.That(result, Does.Not.Contain("void Encode"));
            Assert.That(result, Does.Not.Contain("void Decode"));
        }

        [Test]
        public void GenerateWithCustomTypeIdsUsesSpecifiedIds()
        {
            var model = new TypeSourceModel
            {
                ClassName = "CustomType",
                Namespace = "Custom.Ns",
                NamespaceUri = "http://custom.org/types",
                NamespaceSymbol = "CustomNs",
                DataTypeId = "i=12345",
                BinaryEncodingId = "i=12346",
                XmlEncodingId = "i=12347",
                IsEnum = false,
                IsRecord = false,
                Fields = []
            };

            string result = TypeSourceGenerator.Generate(model);

            Assert.That(result, Does.Contain("i=12345"));
            Assert.That(result, Does.Contain("i=12346"));
            Assert.That(result, Does.Contain("i=12347"));
        }

        [Test]
        public void GenerateWithNullTypeIdUsesStringIdentifier()
        {
            var model = new TypeSourceModel
            {
                ClassName = "AutoIdType",
                Namespace = "Auto.Ns",
                NamespaceUri = "urn:auto:ns",
                NamespaceSymbol = "AutoNs",
                IsEnum = false,
                IsRecord = false,
                Fields = []
            };

            string result = TypeSourceGenerator.Generate(model);

            Assert.That(result, Does.Contain("\"AutoIdType\""));
            Assert.That(result, Does.Contain("\"urn:auto:ns\""));
        }

        [Test]
        public void GenerateWithEncodeableFieldDefaultUsesExtensionObject()
        {
            var model = new TypeSourceModel
            {
                ClassName = "Parent",
                Namespace = "Test.Ns",
                NamespaceUri = "urn:test",
                NamespaceSymbol = "TestNs",
                IsEnum = false,
                IsRecord = false,
                Fields =
                [
                    new TypeFieldModel
                    {
                        PropertyName = "Child",
                        FieldName = "Child",
                        TypeName = "global::Test.Ns.ChildType",
                        ShortTypeName = "ChildType",
                        IsEncodeable = true,
                        Order = 0
                    }
                ]
            };

            string result = TypeSourceGenerator.Generate(model);

            Assert.That(result, Does.Contain("WriteEncodeableAsExtensionObject"));
            Assert.That(result, Does.Contain("ReadEncodeableAsExtensionObject"));
        }

        [Test]
        public void GenerateWithSealedEncodeableFieldUsesWriteEncodeable()
        {
            var model = new TypeSourceModel
            {
                ClassName = "Parent",
                Namespace = "Test.Ns",
                NamespaceUri = "urn:test",
                NamespaceSymbol = "TestNs",
                IsEnum = false,
                IsRecord = false,
                Fields =
                [
                    new TypeFieldModel
                    {
                        PropertyName = "Child",
                        FieldName = "Child",
                        TypeName = "global::Test.Ns.ChildType",
                        ShortTypeName = "ChildType",
                        IsEncodeable = true,
                        FieldTypeIsSealed = true,
                        Order = 0
                    }
                ]
            };

            string result = TypeSourceGenerator.Generate(model);

            Assert.That(result, Does.Contain("encoder.WriteEncodeable(\"Child\", Child)"));
            Assert.That(result, Does.Contain("decoder.ReadEncodeable<global::Test.Ns.ChildType>(\"Child\")"));
        }

        [Test]
        public void GenerateWithStructureHandlingInlineUsesWriteEncodeable()
        {
            var model = new TypeSourceModel
            {
                ClassName = "Parent",
                Namespace = "Test.Ns",
                NamespaceUri = "urn:test",
                NamespaceSymbol = "TestNs",
                IsEnum = false,
                IsRecord = false,
                Fields =
                [
                    new TypeFieldModel
                    {
                        PropertyName = "Child",
                        FieldName = "Child",
                        TypeName = "global::Test.Ns.ChildType",
                        ShortTypeName = "ChildType",
                        IsEncodeable = true,
                        StructureHandling = 1, // Inline
                        Order = 0
                    }
                ]
            };

            string result = TypeSourceGenerator.Generate(model);

            Assert.That(result, Does.Contain("encoder.WriteEncodeable(\"Child\", Child)"));
            Assert.That(result, Does.Not.Contain("ExtensionObject"));
        }

        [Test]
        public void GenerateWithArrayOfFieldClonesArray()
        {
            var model = new TypeSourceModel
            {
                ClassName = "WithArray",
                Namespace = "Test.Ns",
                NamespaceUri = "urn:test",
                NamespaceSymbol = "TestNs",
                IsEnum = false,
                IsRecord = false,
                Fields =
                [
                    new TypeFieldModel
                    {
                        PropertyName = "Items",
                        FieldName = "Items",
                        TypeName = "global::Opc.Ua.ArrayOf<global::System.String>",
                        ShortTypeName = "ArrayOf",
                        IsArray = true,
                        ElementShortTypeName = "String",
                        ElementTypeName = "global::System.String",
                        Order = 0
                    }
                ]
            };

            string result = TypeSourceGenerator.Generate(model);

            Assert.That(result, Does.Not.Contain("Clone(this.Items)"),
                "ArrayOf<T> is a value type, no deep clone needed");
            Assert.That(result, Does.Contain("WriteStringArray"));
            Assert.That(result, Does.Contain("ReadStringArray"));
        }

        [Test]
        public void TypeMapContainsAllBuiltInTypes()
        {
            string[] expectedTypes =
            [
                "Boolean", "SByte", "Byte", "Int16", "UInt16",
                "Int32", "UInt32", "Int64", "UInt64",
                "Float", "Double", "String", "DateTime",
                "Guid", "ByteString", "NodeId", "ExpandedNodeId",
                "StatusCode", "QualifiedName", "LocalizedText",
                "ExtensionObject", "DataValue", "Variant",
                "DiagnosticInfo"
            ];

            foreach (string typeName in expectedTypes)
            {
                Assert.That(TypeSourceGenerator.s_scalarTypeMap.ContainsKey(typeName),
                    Is.True,
                    $"Missing type mapping for {typeName}");
            }
        }

        [Test]
        public void ResolveEncoderDecoderForScalarField()
        {
            TypeFieldModel field = CreateField("Value", "Int32");
            (string writeMethod, string readMethod) = TypeSourceGenerator.ResolveEncoderDecoder(field);
            Assert.That(writeMethod, Is.EqualTo("WriteInt32"));
            Assert.That(readMethod, Is.EqualTo("ReadInt32"));
        }

        [Test]
        public void ResolveEncoderDecoderForArrayField()
        {
            TypeFieldModel field = CreateField("Values", "ArrayOf", isArray: true, elementType: "String");
            (string writeMethod, string readMethod) = TypeSourceGenerator.ResolveEncoderDecoder(field);
            Assert.That(writeMethod, Is.EqualTo("WriteStringArray"));
            Assert.That(readMethod, Is.EqualTo("ReadStringArray"));
        }

        [Test]
        public void ValidateAndFilterRejectsUnsupportedType()
        {
            var model = new TypeSourceModel
            {
                ClassName = "BadType",
                Namespace = "Test",
                NamespaceUri = "urn:test",
                NamespaceSymbol = "Test",
                IsEnum = false,
                Fields =
                [
                    CreateField("Good", "String"),
                    new TypeFieldModel
                    {
                        PropertyName = "Bad",
                        FieldName = "Bad",
                        TypeName = "global::MyApp.CustomThing",
                        ShortTypeName = "CustomThing",
                        Order = 1
                    }
                ]
            };

            IReadOnlyList<TypeSourceGeneratorDiagnostic> diagnostics =
                TypeSourceGenerator.ValidateAndFilter(model, out IReadOnlyList<TypeFieldModel> valid);

            Assert.That(valid, Has.Count.EqualTo(1));
            Assert.That(valid[0].PropertyName, Is.EqualTo("Good"));
            Assert.That(diagnostics, Has.Count.EqualTo(1));
            Assert.That(diagnostics[0].IsError, Is.False);
        }

        [Test]
        public void ValidateAndFilterReportsErrorForAnnotatedUnsupportedType()
        {
            var model = new TypeSourceModel
            {
                ClassName = "BadAnnotated",
                Namespace = "Test",
                NamespaceUri = "urn:test",
                NamespaceSymbol = "Test",
                IsEnum = false,
                Fields =
                [
                    new TypeFieldModel
                    {
                        PropertyName = "Bad",
                        FieldName = "Bad",
                        TypeName = "global::MyApp.CustomThing",
                        ShortTypeName = "CustomThing",
                        HasDataTypeFieldAttribute = true,
                        Order = 0
                    }
                ]
            };

            IReadOnlyList<TypeSourceGeneratorDiagnostic> diagnostics =
                TypeSourceGenerator.ValidateAndFilter(model, out IReadOnlyList<TypeFieldModel> valid);

            Assert.That(valid, Has.Count.EqualTo(0));
            Assert.That(diagnostics, Has.Count.EqualTo(1));
            Assert.That(diagnostics[0].IsError, Is.True);
        }

        [Test]
        public void GenerateNamespaceMatchesModel()
        {
            var model = new TypeSourceModel
            {
                ClassName = "NsTest",
                Namespace = "My.Deep.Namespace",
                NamespaceUri = "urn:my:deep",
                NamespaceSymbol = "MyDeepNamespace",
                IsEnum = false,
                IsRecord = false,
                Fields = []
            };

            string result = TypeSourceGenerator.Generate(model);

            Assert.That(result, Does.Contain("namespace My.Deep.Namespace"));
        }

        private static TypeFieldModel CreateField(
            string name, string shortType,
            bool isArray = false,
            string elementType = null)
        {
            return new TypeFieldModel
            {
                PropertyName = name,
                FieldName = name,
                TypeName = $"global::System.{shortType}",
                ShortTypeName = shortType,
                IsArray = isArray,
                ElementShortTypeName = elementType,
                Order = 0
            };
        }
    }
}
