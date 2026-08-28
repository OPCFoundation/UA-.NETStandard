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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Schema.Model;
using Opc.Ua.SourceGeneration.Dependency;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Regression tests for issue #4332: a structure that subtypes a
    /// structure from a dependency ModelDesign must be source-generatable.
    /// Covers the design-file dependency flow (dependency supplied via
    /// AdditionalFiles), the reversed flow (the dependency list contains a
    /// downstream model that imports the target), and the cross-assembly
    /// flow (dependency supplied as a ModelDependencyV1 payload).
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class CrossModelStructureSubtypeTests
    {
        private const string ModelAUri = "http://test.org/UA/ModelA/";

        private string m_rootPath;
        private string m_modelAPath;
        private string m_modelBPath;

        [SetUp]
        public void SetUp()
        {
            m_rootPath = Path.Combine(
                Path.GetTempPath(),
                "UA-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(m_rootPath, "A"));
            Directory.CreateDirectory(Path.Combine(m_rootPath, "B"));
            m_modelAPath = Path.Combine(m_rootPath, "A", "ModelA.xml");
            m_modelBPath = Path.Combine(m_rootPath, "B", "ModelB.xml");
            File.WriteAllText(m_modelAPath, ModelADesign);
            File.WriteAllText(m_modelBPath, ModelBDesign);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(m_rootPath, true);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>
        /// The primary repro from #4332: the target model declares a
        /// structure that subtypes a structure from a dependency design
        /// file. The binary schema generator used to throw a
        /// NullReferenceException dereferencing the inherited field's
        /// unlinked DataTypeNode.
        /// </summary>
        [Test]
        public void StructureSubtypeAcrossDesignFileDependencyGenerates()
        {
            Dictionary<string, string> generated = Generate(
                targets: [m_modelBPath],
                dependencies: [m_modelBPath, m_modelAPath]);

            AssertGeneratedDerivedStruct(generated);
        }

        /// <summary>
        /// The same two design files, but the target is the upstream model
        /// and the dependency list contains the downstream model that
        /// imports the target (the source generator supplies every design
        /// file of the compilation as a dependency of every other). The
        /// downstream dependency must not fail to load because the target
        /// it imports has not been loaded yet.
        /// </summary>
        [Test]
        public void UpstreamTargetWithDownstreamDependencyGenerates()
        {
            Dictionary<string, string> generated = Generate(
                targets: [m_modelAPath],
                dependencies: [m_modelAPath, m_modelBPath]);

            string bsd = generated.Keys
                .Where(f => f.EndsWith(".Types.bsd", System.StringComparison.Ordinal))
                .Select(f => generated[f])
                .FirstOrDefault();
            Assert.That(bsd, Is.Not.Null, "No binary schema generated.");
            Assert.That(bsd, Does.Contain("<opc:StructuredType Name=\"BaseStruct\""));
            Assert.That(bsd, Does.Contain("<opc:Field Name=\"Make\" TypeName=\"opc:CharArray\" />"));
        }

        /// <summary>
        /// Cross-assembly flow of docs/ModelDependencies.md: the dependency
        /// model is not part of the compilation's design files but supplied
        /// as a ModelDependencyV1 payload recovered from a referenced
        /// assembly. Payload-materialised structures must be able to serve
        /// as the BaseType of a local structure.
        /// </summary>
        [Test]
        public void StructureSubtypeAcrossPayloadDependencyGenerates()
        {
            Dictionary<string, string> generated = Generate(
                targets: [m_modelBPath],
                dependencies: [m_modelBPath],
                referencedDependencies: new Dictionary<string, ModelDependencyV1>
                {
                    [ModelAUri] = CreateModelAPayload()
                });

            AssertGeneratedDerivedStruct(generated);
        }

        /// <summary>
        /// Three-level chain across all supply mechanisms: the target
        /// structure subtypes a payload structure whose own base structure
        /// is defined in a design-file dependency. The payload types must
        /// be re-linked after the upstream design files are loaded and
        /// before the target is validated, otherwise the target's basic
        /// data type classification walks a broken chain.
        /// </summary>
        [Test]
        public void StructureSubtypeOfPayloadWithDesignFileBaseGenerates()
        {
            const string modelCUri = "http://test.org/UA/ModelC/";
            var payload = new ModelDependencyV1 { ModelUri = modelCUri };
            payload.Nodes.Add(new DependencyNode
            {
                SymbolicName = "MidStruct",
                SymbolicNamespace = modelCUri,
                ClassName = "MidStruct",
                Kind = DependencyNodeKind.DataType,
                BaseTypeName = "BaseStruct",
                BaseTypeNamespace = ModelAUri,
                NumericId = 1,
                Fields =
                [
                    new DependencyDataField(
                        "Middle", "Int32", Ua.Types.Namespaces.OpcUa, (int)ValueRank.Scalar)
                ]
            });

            string modelDPath = Path.Combine(m_rootPath, "B", "ModelD.xml");
            File.WriteAllText(
                modelDPath,
                ModelBDesign
                    .Replace("http://test.org/UA/ModelB/", "http://test.org/UA/ModelD/")
                    .Replace("xmlns:s0=\"http://test.org/UA/ModelA/\"", "xmlns:s0=\"" + modelCUri + "\"")
                    .Replace(
                        "<opc:Namespace Name=\"ModelA\" Prefix=\"Test.ModelA\">http://test.org/UA/ModelA/</opc:Namespace>",
                        "<opc:Namespace Name=\"ModelC\" Prefix=\"Test.ModelC\">" + modelCUri + "</opc:Namespace>" +
                        "<opc:Namespace Name=\"ModelA\" Prefix=\"Test.ModelA\">http://test.org/UA/ModelA/</opc:Namespace>")
                    .Replace("BaseType=\"s0:BaseStruct\"", "BaseType=\"s0:MidStruct\""));

            Dictionary<string, string> generated = Generate(
                targets: [modelDPath],
                dependencies: [modelDPath, m_modelAPath],
                referencedDependencies: new Dictionary<string, ModelDependencyV1>
                {
                    [modelCUri] = payload
                });

            string bsd = generated.Keys
                .Where(f => f.EndsWith(".Types.bsd", System.StringComparison.Ordinal))
                .Select(f => generated[f])
                .FirstOrDefault();
            Assert.That(bsd, Is.Not.Null, "No binary schema generated.");
            Assert.That(bsd, Does.Contain("<opc:StructuredType Name=\"DerivedStruct\""));
            // Fields inherited from the design-file grandparent, the payload
            // parent, and the target's own field, in that order.
            Assert.That(bsd, Does.Contain("Name=\"Make\" TypeName=\"opc:CharArray\""));
            Assert.That(bsd, Does.Contain("Name=\"Middle\" TypeName=\"opc:Int32\""));
            Assert.That(bsd, Does.Contain("<opc:Field Name=\"Extra\" TypeName=\"opc:UInt32\" />"));
        }

        /// <summary>
        /// Validator-level checks for the payload flow: the payload
        /// materialised base structure must be linked well enough for the
        /// generators (BasicDataType, IsStructure, field data types).
        /// </summary>
        [Test]
        public void PayloadDependencyBaseTypeIsLinkedDataTypeDesign()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var virtualFileSystem = new VirtualFileSystem();
            IFileSystem fileSystem = typeof(ModelDesignValidator).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design")
                .WithFallback(virtualFileSystem);
            IModelDesign model = fileSystem.OpenModelDesign(
                new DesignFileCollection
                {
                    Targets = [m_modelBPath],
                    Options = new DesignFileOptions()
                },
                exclusions: null,
                telemetry,
                useAllowSubtypes: false,
                referencedDependencies: new Dictionary<string, ModelDependencyV1>
                {
                    [ModelAUri] = CreateModelAPayload()
                });

            DataTypeDesign derived = model.Nodes
                .OfType<DataTypeDesign>()
                .FirstOrDefault(n => n.SymbolicName.Name == "DerivedStruct");
            Assert.That(derived, Is.Not.Null);

            var baseStruct = derived.BaseTypeNode as DataTypeDesign;
            Assert.That(baseStruct, Is.Not.Null, "BaseTypeNode is not a DataTypeDesign.");
            Assert.That(baseStruct.SymbolicName.Name, Is.EqualTo("BaseStruct"));
            Assert.That(baseStruct.BasicDataType, Is.EqualTo(BasicDataType.UserDefined));
            Assert.That(baseStruct.IsStructure, Is.True);
            Assert.That(baseStruct.Fields, Is.Not.Null.And.Length.EqualTo(2));
            foreach (Parameter field in baseStruct.Fields)
            {
                Assert.That(field.DataTypeNode, Is.Not.Null,
                    $"Field {field.Name} has no DataTypeNode.");
                Assert.That(field.Parent, Is.SameAs(baseStruct),
                    $"Field {field.Name} has no Parent.");
            }
        }

        /// <summary>
        /// Validator-level checks for the design-file flow: the dependency
        /// model is loaded without full dictionary validation, but its data
        /// types must still be linked (BasicDataType, IsStructure,
        /// transitive structure classification and field data types).
        /// </summary>
        [Test]
        public void DesignFileDependencyDataTypesAreLinked()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var virtualFileSystem = new VirtualFileSystem();
            IFileSystem fileSystem = typeof(ModelDesignValidator).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design")
                .WithFallback(virtualFileSystem);
            IModelDesign model = fileSystem.OpenModelDesign(
                new DesignFileCollection
                {
                    Targets = [m_modelBPath],
                    Dependencies = [m_modelAPath],
                    Options = new DesignFileOptions()
                },
                exclusions: null,
                telemetry,
                useAllowSubtypes: false);

            DataTypeDesign derived = model.Nodes
                .OfType<DataTypeDesign>()
                .FirstOrDefault(n => n.SymbolicName.Name == "DerivedStruct");
            Assert.That(derived, Is.Not.Null);

            var baseStruct = derived.BaseTypeNode as DataTypeDesign;
            Assert.That(baseStruct, Is.Not.Null, "BaseTypeNode is not a DataTypeDesign.");
            Assert.That(baseStruct.BasicDataType, Is.EqualTo(BasicDataType.UserDefined));
            Assert.That(baseStruct.IsStructure, Is.True);
            foreach (Parameter field in baseStruct.Fields)
            {
                Assert.That(field.DataTypeNode, Is.Not.Null,
                    $"Field {field.Name} has no DataTypeNode.");
            }

            // The enumeration defined in the dependency must be classified
            // as well: it types a field of the inherited structure.
            DataTypeDesign status = baseStruct.Fields
                .Single(f => f.Name == "Status")
                .DataTypeNode;
            Assert.That(status.BasicDataType, Is.EqualTo(BasicDataType.Enumeration));
            Assert.That(status.IsEnumeration, Is.True);

            // Without UseAllowSubtypes, a dependency structure field that
            // allows subtypes degrades to the abstract Structure, matching
            // ValidateParameters for target fields.
            DataTypeDesign details = baseStruct.Fields
                .Single(f => f.Name == "Details")
                .DataTypeNode;
            Assert.That(details.SymbolicName.Name, Is.EqualTo("Structure"));
        }

        private static void AssertGeneratedDerivedStruct(Dictionary<string, string> generated)
        {
            string bsd = generated.Keys
                .Where(f => f.EndsWith(".Types.bsd", System.StringComparison.Ordinal))
                .Select(f => generated[f])
                .FirstOrDefault();
            Assert.That(bsd, Is.Not.Null, "No binary schema generated.");
            Assert.That(bsd, Does.Contain("<opc:StructuredType Name=\"DerivedStruct\""));
            // Inherited field from the dependency structure with its source type.
            Assert.That(bsd, Does.Contain("Name=\"Make\" TypeName=\"opc:CharArray\""));
            Assert.That(bsd, Does.Contain("SourceType=\""));
            Assert.That(bsd, Does.Not.Contain("opc:Boolean\" SourceType"),
                "Inherited field types must not degrade to the BasicDataType default.");
            // Own field of the derived structure.
            Assert.That(bsd, Does.Contain("<opc:Field Name=\"Extra\" TypeName=\"opc:UInt32\" />"));

            string xsd = generated.Keys
                .Where(f => f.EndsWith(".Types.xsd", System.StringComparison.Ordinal))
                .Select(f => generated[f])
                .FirstOrDefault();
            Assert.That(xsd, Is.Not.Null, "No xml schema generated.");
            // The xs:extension base must reference the dependency structure,
            // not degrade to the BasicDataType enum default (xs:boolean).
            Assert.That(xsd, Does.Contain(":BaseStruct\""));
            Assert.That(xsd, Does.Not.Contain("base=\"xs:boolean\""));

            string dataTypes = generated.Keys
                .Where(f => f.EndsWith("DataTypes.g.cs", System.StringComparison.Ordinal))
                .Select(f => generated[f])
                .FirstOrDefault();
            Assert.That(dataTypes, Is.Not.Null, "No data type code generated.");
            Assert.That(dataTypes, Does.Contain("class DerivedStruct"));
            Assert.That(dataTypes, Does.Contain("BaseStruct"));
        }

        private static Dictionary<string, string> Generate(
            IReadOnlyList<string> targets,
            IReadOnlyList<string> dependencies,
            IReadOnlyDictionary<string, ModelDependencyV1> referencedDependencies = null)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var fileSystem = new VirtualFileSystem();
            Generators.GenerateCode(
                new DesignFileCollection
                {
                    Targets = targets,
                    Dependencies = dependencies,
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
                useAllowSubtypes: false,
                identifierFiles: null,
                referencedModels: null,
                nodeManagerBindings: null,
                reportBindingDiagnostic: null,
                sharedUsedBindings: null,
                bindingModelCount: 0,
                reportFluentAccessorsOnlyDiagnostic: null,
                referencedModelProviders: null,
                referencedAccessorProviders: null,
                referencedDependencies: referencedDependencies);
            return fileSystem.CreatedFiles
                .ToDictionary(c => c, c => Encoding.UTF8.GetString(fileSystem.Get(c)));
        }

        /// <summary>
        /// Builds the payload a referenced assembly generated from ModelA
        /// would carry in its [assembly: ModelDependency] attribute (see
        /// ModelDependencyGenerator).
        /// </summary>
        private static ModelDependencyV1 CreateModelAPayload()
        {
            var payload = new ModelDependencyV1 { ModelUri = ModelAUri };
            payload.Nodes.Add(new DependencyNode
            {
                SymbolicName = "BaseStruct",
                SymbolicNamespace = ModelAUri,
                ClassName = "BaseStruct",
                Kind = DependencyNodeKind.DataType,
                BaseTypeName = "Structure",
                BaseTypeNamespace = Ua.Types.Namespaces.OpcUa,
                NumericId = 1,
                Fields =
                [
                    new DependencyDataField(
                        "Make", "String", Ua.Types.Namespaces.OpcUa, (int)ValueRank.Scalar),
                    new DependencyDataField(
                        "Status", "StatusEnum", ModelAUri, (int)ValueRank.Scalar)
                ]
            });
            payload.Nodes.Add(new DependencyNode
            {
                SymbolicName = "StatusEnum",
                SymbolicNamespace = ModelAUri,
                ClassName = "StatusEnum",
                Kind = DependencyNodeKind.DataType,
                BaseTypeName = "Enumeration",
                BaseTypeNamespace = Ua.Types.Namespaces.OpcUa,
                NumericId = 2,
                IsEnumeration = true,
                Fields =
                [
                    new DependencyDataField(
                        "Idle", "Int32", Ua.Types.Namespaces.OpcUa, (int)ValueRank.Scalar),
                    new DependencyDataField(
                        "Running", "Int32", Ua.Types.Namespaces.OpcUa, (int)ValueRank.Scalar)
                ]
            });
            return payload;
        }

        private const string ModelADesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns="http://test.org/UA/ModelA/"
              TargetNamespace="http://test.org/UA/ModelA/">
              <opc:Namespaces>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="ModelA" Prefix="Test.ModelA">http://test.org/UA/ModelA/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="StatusEnum" BaseType="ua:Enumeration">
                <opc:Fields>
                  <opc:Field Name="Idle" Identifier="0" />
                  <opc:Field Name="Running" Identifier="1" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="DetailsStruct" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Serial" DataType="ua:String" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="BaseStruct" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Make" DataType="ua:String" />
                  <opc:Field Name="Status" DataType="StatusEnum" />
                  <opc:Field Name="Details" DataType="DetailsStruct" AllowSubTypes="true" />
                </opc:Fields>
              </opc:DataType>
            </opc:ModelDesign>
            """;

        private const string ModelBDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns:s0="http://test.org/UA/ModelA/"
              xmlns="http://test.org/UA/ModelB/"
              TargetNamespace="http://test.org/UA/ModelB/">
              <opc:Namespaces>
                <opc:Namespace Name="ModelB" Prefix="Test.ModelB">http://test.org/UA/ModelB/</opc:Namespace>
                <opc:Namespace Name="ModelA" Prefix="Test.ModelA">http://test.org/UA/ModelA/</opc:Namespace>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="DerivedStruct" BaseType="s0:BaseStruct">
                <opc:Fields>
                  <opc:Field Name="Extra" DataType="ua:UInt32" />
                </opc:Fields>
              </opc:DataType>
            </opc:ModelDesign>
            """;
    }
}
