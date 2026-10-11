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

using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// The StructureType of a generated StructureDefinition describes the
    /// whole encoding, inherited fields included.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class StructureDefinitionKindTests
    {
        /// <summary>
        /// A3-5: a subtype of a structure with an AllowSubTypes field
        /// inherits that field, which the base Encode writes as an
        /// ExtensionObject, so its definition must be
        /// StructureWithSubtypedValues with the inherited field flagged,
        /// with and without own fields.
        /// </summary>
        [Test]
        public void SubtypeOfStructureWithSubtypedValuesInheritsTheKind()
        {
            string dataTypes = GenerateDataTypes(Design);

            string withFields = GetDefinition(dataTypes, "DerivedWithFields");
            Assert.That(withFields, Does.Contain(
                "StructureType = global::Opc.Ua.StructureType.StructureWithSubtypedValues"));
            Assert.That(GetField(withFields, "Payload"), Does.Contain("IsOptional = true"));
            Assert.That(GetField(withFields, "Count"), Does.Contain("IsOptional = false"));

            string withoutFields = GetDefinition(dataTypes, "DerivedWithoutFields");
            Assert.That(withoutFields, Does.Contain(
                "StructureType = global::Opc.Ua.StructureType.StructureWithSubtypedValues"));
            Assert.That(GetField(withoutFields, "Payload"), Does.Contain("IsOptional = true"));

            // The base keeps its kind.
            Assert.That(GetDefinition(dataTypes, "SubtypedBase"), Does.Contain(
                "StructureType = global::Opc.Ua.StructureType.StructureWithSubtypedValues"));
        }

        private static string GetDefinition(string source, string typeName)
        {
            int start = source.IndexOf(
                "public static global::Opc.Ua.StructureDefinition Create" + typeName + "(",
                StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), typeName + " definition not found");
            int end = source.IndexOf("public static global::Opc.Ua.", start + 1, StringComparison.Ordinal);
            return end < 0 ? source[start..] : source[start..end];
        }

        private static string GetField(string definition, string fieldName)
        {
            int start = definition.IndexOf("Name = \"" + fieldName + "\"", StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), fieldName + " field not found");
            int end = definition.IndexOf("new global::Opc.Ua.StructureField", start, StringComparison.Ordinal);
            return end < 0 ? definition[start..] : definition[start..end];
        }

        private static string GenerateDataTypes(string design)
        {
            string root = Path.Combine(Path.GetTempPath(), "UA-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "Model.xml");
                File.WriteAllText(path, design);
                ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
                using var fileSystem = new VirtualFileSystem();
                Generators.GenerateCode(
                    new DesignFileCollection
                    {
                        Targets = [path],
                        Dependencies = [path],
                        Options = new DesignFileOptions()
                    },
                    fileSystem,
                    string.Empty,
                    telemetry,
                    new GeneratorOptions
                    {
                        OmitFluentApi = true,
                        OmitEventRecords = true
                    },
                    useAllowSubtypes: true,
                    identifierFiles: null!,
                    referencedModels: null!,
                    nodeManagerBindings: null!,
                    reportBindingDiagnostic: null!,
                    sharedUsedBindings: null!,
                    bindingModelCount: 0,
                    reportFluentAccessorsOnlyDiagnostic: null!,
                    referencedModelProviders: null!,
                    referencedAccessorProviders: null!);
                string file = fileSystem.CreatedFiles
                    .Single(f => f.EndsWith("DataTypes.g.cs", StringComparison.Ordinal));
                return Encoding.UTF8.GetString(fileSystem.Get(file));
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        private const string Design =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns="http://test.org/UA/Kinds/"
              TargetNamespace="http://test.org/UA/Kinds/">
              <opc:Namespaces>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="Kinds" Prefix="Test.Kinds">http://test.org/UA/Kinds/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="PayloadStruct" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Value" DataType="ua:Int32" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="SubtypedBase" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Name" DataType="ua:String" />
                  <opc:Field Name="Payload" DataType="PayloadStruct" AllowSubTypes="true" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="DerivedWithFields" BaseType="SubtypedBase">
                <opc:Fields>
                  <opc:Field Name="Count" DataType="ua:Int32" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="DerivedWithoutFields" BaseType="SubtypedBase" />
            </opc:ModelDesign>
            """;
    }
}
