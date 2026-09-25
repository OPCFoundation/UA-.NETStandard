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
                referencedModels: CreateReferencedModels(
                    ModelAUri, "Test.ModelA", "ModelA", CreateModelAPayload()));

            AssertGeneratedDerivedStruct(generated);
        }

        /// <summary>
        /// When a model is supplied both as a referenced-assembly payload
        /// and as a design file (e.g. the upstream project is referenced
        /// while its design XML is still in AdditionalFiles), the design
        /// file wins and the payload is skipped. Importing both used to
        /// fail with a duplicate symbolic id.
        /// </summary>
        [Test]
        public void PayloadForFileBackedModelIsIgnored()
        {
            Dictionary<string, string> generated = Generate(
                targets: [m_modelBPath],
                dependencies: [m_modelBPath, m_modelAPath],
                referencedModels: CreateReferencedModels(
                    ModelAUri, "Test.ModelA", "ModelA", CreateModelAPayload()));

            string bsd = generated.Keys
                .Where(f => f.EndsWith(".Types.bsd", System.StringComparison.Ordinal))
                .Select(f => generated[f])
                .FirstOrDefault();
            Assert.That(bsd, Is.Not.Null, "No binary schema generated.");
            // The design file's field set wins over the payload's: the
            // Details field exists only in the design file.
            Assert.That(bsd, Does.Contain("Name=\"Make\" TypeName=\"opc:CharArray\""));
            Assert.That(bsd, Does.Contain("Name=\"Details\""));
            Assert.That(bsd, Does.Contain("<opc:Field Name=\"Extra\" TypeName=\"opc:UInt32\" />"));
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
            File.WriteAllText(modelDPath, ModelDDesign);

            Dictionary<string, string> generated = Generate(
                targets: [modelDPath],
                dependencies: [modelDPath, m_modelAPath],
                referencedModels: CreateReferencedModels(
                    modelCUri, "Test.ModelC", "ModelC", payload));

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
        /// A2-7: a consumer subtype of a payload-only structure with optional
        /// fields has to continue the base's encoding mask (one mask on the
        /// wire) and publish the inherited fields with their optional flag
        /// and array dimensions. The payload used to drop IsOptional,
        /// AllowSubTypes and ArrayDimensions, so the subtype wrote a second
        /// mask and republished the fields as mandatory rank 2 matrices.
        /// </summary>
        [Test]
        public void SubtypeOfPayloadStructureWithOptionalFieldsContinuesTheEncodingMask()
        {
            var payload = new ModelDependencyV1 { ModelUri = ModelAUri };
            payload.Nodes.Add(new DependencyNode
            {
                SymbolicName = "OptBase",
                SymbolicNamespace = ModelAUri,
                ClassName = "OptBase",
                Kind = DependencyNodeKind.DataType,
                BaseTypeName = "Structure",
                BaseTypeNamespace = Ua.Types.Namespaces.OpcUa,
                NumericId = 10,
                Fields =
                [
                    new DependencyDataField(
                        "Id", "Int32", Ua.Types.Namespaces.OpcUa, (int)ValueRank.Scalar),
                    new DependencyDataField(
                        "Note", "String", Ua.Types.Namespaces.OpcUa, (int)ValueRank.Scalar)
                    {
                        IsOptional = true
                    },
                    new DependencyDataField(
                        "Cube", "Double", Ua.Types.Namespaces.OpcUa, (int)ValueRank.OneOrMoreDimensions)
                    {
                        IsOptional = true,
                        ArrayDimensions = "0,0,0"
                    }
                ]
            });
            // Through the wire format, as a referenced assembly carries it.
            payload = ModelDependencyV1.FromBase64Payload(payload.ToBase64Payload());

            string modelEPath = Path.Combine(m_rootPath, "B", "ModelE.xml");
            File.WriteAllText(modelEPath, ModelEDesign);

            Dictionary<string, string> generated = Generate(
                targets: [modelEPath],
                dependencies: [modelEPath],
                referencedModels: CreateReferencedModels(
                    ModelAUri, "Test.ModelA", "ModelA", payload));

            string dataTypes = generated.Keys
                .Where(f => f.EndsWith("DataTypes.g.cs", System.StringComparison.Ordinal))
                .Select(f => generated[f])
                .Single();
            Assert.That(dataTypes, Does.Contain("class OptDerived"));
            Assert.That(dataTypes, Does.Not.Contain("WriteEncodingMask"),
                "the base writes the one encoding mask");
            Assert.That(dataTypes, Does.Not.Contain("ReadEncodingMask"),
                "the base reads the one encoding mask");
            Assert.That(dataTypes, Does.Contain("override uint EncodingMask"));

            int definition = dataTypes.IndexOf(
                "StructureDefinition CreateOptDerived(", System.StringComparison.Ordinal);
            Assert.That(definition, Is.GreaterThanOrEqualTo(0));
            string text = dataTypes[definition..];
            text = text[..text.IndexOf("\n        }", System.StringComparison.Ordinal)];
            Assert.That(text, Does.Contain("StructureType.StructureWithOptionalFields"));
            Assert.That(FieldText(text, "Note"), Does.Contain("IsOptional = true"));
            Assert.That(FieldText(text, "Id"), Does.Contain("IsOptional = false"));
            string cube = FieldText(text, "Cube");
            Assert.That(cube, Does.Contain("IsOptional = true"));
            Assert.That(cube, Does.Contain("0, 0, 0").Or.Contain("0u, 0u, 0u"),
                "the three dimensions survive: " + cube);
        }

        /// <summary>
        /// A2-8: a dependency design file is linked, not validated, so its
        /// single-dimension "matrix" field (OneOrMoreDimensions with one
        /// array dimension) was not normalised to the array it is. A target
        /// structure that contains or subtypes such a dependency structure
        /// was then taken for one with an inline matrix and dropped from the
        /// binary schema, although the dependency's own build encodes the
        /// field as a length-prefixed array.
        /// </summary>
        [Test]
        public void DependencySingleDimensionMatrixFieldIsNormalisedToAnArray()
        {
            string vectorPath = Path.Combine(m_rootPath, "A", "ModelV.xml");
            string holderPath = Path.Combine(m_rootPath, "B", "ModelH.xml");
            File.WriteAllText(vectorPath, ModelVDesign);
            File.WriteAllText(holderPath, ModelHDesign);

            Dictionary<string, string> generated = Generate(
                targets: [holderPath],
                dependencies: [holderPath, vectorPath]);

            string bsd = generated.Keys
                .Where(f => f.EndsWith(".Types.bsd", System.StringComparison.Ordinal))
                .Select(f => generated[f])
                .FirstOrDefault();
            Assert.That(bsd, Is.Not.Null, "No binary schema generated.");
            Assert.That(bsd, Does.Contain("<opc:StructuredType Name=\"HolderStruct\""));
            Assert.That(bsd, Does.Contain("<opc:StructuredType Name=\"SubVectorStruct\""));

            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var virtualFileSystem = new VirtualFileSystem();
            IFileSystem fileSystem = typeof(ModelDesignValidator).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design")
                .WithFallback(virtualFileSystem);
            IModelDesign model = fileSystem.OpenModelDesign(
                new DesignFileCollection
                {
                    Targets = [holderPath],
                    Dependencies = [vectorPath],
                    Options = new DesignFileOptions()
                },
                exclusions: null,
                telemetry,
                useAllowSubtypes: false);
            var vector = model.Nodes
                .OfType<DataTypeDesign>()
                .Single(n => n.SymbolicName.Name == "SubVectorStruct")
                .BaseTypeNode as DataTypeDesign;
            Assert.That(vector, Is.Not.Null);
            Assert.That(vector.Fields.Single(f => f.Name == "Values").ValueRank, Is.EqualTo(ValueRank.Array));
            Assert.That(vector.HasInlineMatrixField(), Is.False);
            var grid = model.Nodes
                .OfType<DataTypeDesign>()
                .Single(n => n.SymbolicName.Name == "SubGridStruct")
                .BaseTypeNode as DataTypeDesign;
            Assert.That(grid, Is.Not.Null);
            // A matrix without dimensions is taken as rank 2 (ValueRank-0
            // normalisation), as for the target's own types.
            Assert.That(grid.Fields.Single(f => f.Name == "Grid").ArrayDimensions, Is.EqualTo("0,0"));
            Assert.That(grid.HasInlineMatrixField(), Is.True, "Grid is a real matrix");
            Assert.That(
                model.Nodes.OfType<DataTypeDesign>().Single(n => n.SymbolicName.Name == "HolderStruct")
                    .HasInlineMatrixField(),
                Is.False);
        }

        /// <summary>
        /// A2-5: a QualifiedName / NodeId default authored in a dependency
        /// design numbers the namespaces of that design (index 1 = the
        /// dependency). Children the target inherits from the dependency type
        /// (subtype, child object, instance) used to resolve that index
        /// against the target design's namespaces and name the target's URI.
        /// </summary>
        [Test]
        public void DependencyDesignDefaultValuesKeepTheirNamespaces()
        {
            string depPath = Path.Combine(m_rootPath, "A", "Dep.xml");
            string tgtPath = Path.Combine(m_rootPath, "B", "Tgt.xml");
            File.WriteAllText(depPath, NamespacedDefaultsDependencyDesign);
            File.WriteAllText(tgtPath, NamespacedDefaultsTargetDesign);

            Dictionary<string, string> generated = Generate(
                targets: [tgtPath],
                dependencies: [tgtPath, depPath]);

            string[] values = [.. generated
                .Where(f => f.Key.EndsWith(".cs", System.StringComparison.Ordinal))
                .SelectMany(f => f.Value.Split('\n'))
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("baseState.WrappedValue", System.StringComparison.Ordinal) &&
                    ContainsOrdinal(l, "GetIndexOrAppend"))];

            Assert.That(values, Is.Not.Empty);
            Assert.That(values, Has.None.Contains("http://test.org/UA/Tgt/"));
            Assert.That(values, Has.Some.EqualTo(
                "baseState.WrappedValue = global::Opc.Ua.Variant.From(" +
                "new global::Opc.Ua.QualifiedName(\"X\", " +
                "context.NamespaceUris.GetIndexOrAppend(\"http://test.org/UA/Dep/\")));"));
            Assert.That(values, Has.Some.EqualTo(
                "baseState.WrappedValue = global::Opc.Ua.Variant.From(" +
                "global::Opc.Ua.NodeId.Parse(\"i=77\").WithNamespaceIndex(" +
                "context.NamespaceUris.GetIndexOrAppend(\"http://test.org/UA/Dep/\")));"));
        }

        private const string NamespacedDefaultsDependencyDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd"
              xmlns="http://test.org/UA/Dep/"
              TargetNamespace="http://test.org/UA/Dep/">
              <opc:Namespaces>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="Dep" Prefix="Test.Dep">http://test.org/UA/Dep/</opc:Namespace>
              </opc:Namespaces>
              <opc:ObjectType SymbolicName="DepType" BaseType="ua:BaseObjectType">
                <opc:Children>
                  <opc:Property SymbolicName="Label" DataType="ua:QualifiedName" ModellingRule="Mandatory">
                    <opc:DefaultValue><uax:QualifiedName><uax:NamespaceIndex>1</uax:NamespaceIndex><uax:Name>X</uax:Name></uax:QualifiedName></opc:DefaultValue>
                  </opc:Property>
                  <opc:Property SymbolicName="Target" DataType="ua:NodeId" ModellingRule="Mandatory">
                    <opc:DefaultValue><uax:NodeId><uax:Identifier>ns=1;i=77</uax:Identifier></uax:NodeId></opc:DefaultValue>
                  </opc:Property>
                </opc:Children>
              </opc:ObjectType>
            </opc:ModelDesign>
            """;

        private const string NamespacedDefaultsTargetDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns:dep="http://test.org/UA/Dep/"
              xmlns="http://test.org/UA/Tgt/"
              TargetNamespace="http://test.org/UA/Tgt/">
              <opc:Namespaces>
                <opc:Namespace Name="Tgt" Prefix="Test.Tgt">http://test.org/UA/Tgt/</opc:Namespace>
                <opc:Namespace Name="Dep" Prefix="Test.Dep">http://test.org/UA/Dep/</opc:Namespace>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
              </opc:Namespaces>
              <opc:ObjectType SymbolicName="SubType" BaseType="dep:DepType">
                <opc:Children>
                  <opc:Property SymbolicName="Own" DataType="ua:Int32" ModellingRule="Mandatory" />
                </opc:Children>
              </opc:ObjectType>
              <opc:ObjectType SymbolicName="HostType" BaseType="ua:BaseObjectType">
                <opc:Children>
                  <opc:Object SymbolicName="Inner" TypeDefinition="dep:DepType" ModellingRule="Mandatory" />
                </opc:Children>
              </opc:ObjectType>
              <opc:Object SymbolicName="Instance1" TypeDefinition="dep:DepType" />
            </opc:ModelDesign>
            """;

        private static string FieldText(string definition, string name)
        {
            int start = definition.IndexOf("Name = \"" + name + "\"", System.StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), name);
            int end = definition.IndexOf("new global::Opc.Ua.StructureField", start, System.StringComparison.Ordinal);
            return end < 0 ? definition[start..] : definition[start..end];
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

        private static bool ContainsOrdinal(string text, string value)
        {
#if NETFRAMEWORK
            return text.IndexOf(value, System.StringComparison.Ordinal) >= 0;
#else
            return text.Contains(value, System.StringComparison.Ordinal);
#endif
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
            IReadOnlyDictionary<string, ModelDependencyReference> referencedModels = null)
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
                referencedModels: referencedModels,
                nodeManagerBindings: null,
                reportBindingDiagnostic: null,
                sharedUsedBindings: null,
                bindingModelCount: 0,
                reportFluentAccessorsOnlyDiagnostic: null,
                referencedModelProviders: null,
                referencedAccessorProviders: null);
            return fileSystem.CreatedFiles
                .ToDictionary(c => c, c => Encoding.UTF8.GetString(fileSystem.Get(c)));
        }

        /// <summary>
        /// Wraps a dependency payload the way a referenced assembly's
        /// [assembly: ModelDependency] attribute carries it.
        /// </summary>
        private static Dictionary<string, ModelDependencyReference> CreateReferencedModels(
            string modelUri,
            string prefix,
            string name,
            ModelDependencyV1 payload)
        {
            return new Dictionary<string, ModelDependencyReference>
            {
                [modelUri] = new ModelDependencyReference(
                    "ReferencedModelAssembly",
                    modelUri,
                    prefix,
                    "1.0.0",
                    null,
                    name,
                    payload.ToBase64Payload())
            };
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

        /// <summary>
        /// Dependency of A2-8: VectorStruct has a single-dimension "matrix"
        /// field (an array) and a matrix field without dimensions.
        /// </summary>
        private const string ModelVDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns="http://test.org/UA/ModelV/"
              TargetNamespace="http://test.org/UA/ModelV/">
              <opc:Namespaces>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="ModelV" Prefix="Test.ModelV">http://test.org/UA/ModelV/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="VectorStruct" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Values" DataType="ua:Double" ValueRank="OneOrMoreDimensions" ArrayDimensions="5" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="GridStruct" BaseType="VectorStruct">
                <opc:Fields>
                  <opc:Field Name="Grid" DataType="ua:Double" ValueRank="OneOrMoreDimensions" />
                </opc:Fields>
              </opc:DataType>
            </opc:ModelDesign>
            """;

        /// <summary>
        /// Target of A2-8: contains and subtypes ModelV's VectorStruct.
        /// </summary>
        private const string ModelHDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns:s0="http://test.org/UA/ModelV/"
              xmlns="http://test.org/UA/ModelH/"
              TargetNamespace="http://test.org/UA/ModelH/">
              <opc:Namespaces>
                <opc:Namespace Name="ModelH" Prefix="Test.ModelH">http://test.org/UA/ModelH/</opc:Namespace>
                <opc:Namespace Name="ModelV" Prefix="Test.ModelV">http://test.org/UA/ModelV/</opc:Namespace>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="HolderStruct" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Vector" DataType="s0:VectorStruct" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="SubVectorStruct" BaseType="s0:VectorStruct">
                <opc:Fields>
                  <opc:Field Name="Extra" DataType="ua:UInt32" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="SubGridStruct" BaseType="s0:GridStruct">
                <opc:Fields>
                  <opc:Field Name="Extra" DataType="ua:UInt32" />
                </opc:Fields>
              </opc:DataType>
            </opc:ModelDesign>
            """;

        /// <summary>
        /// Subtypes the payload-only OptBase (ModelA) and adds an optional
        /// field of its own.
        /// </summary>
        private const string ModelEDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns:s0="http://test.org/UA/ModelA/"
              xmlns="http://test.org/UA/ModelE/"
              TargetNamespace="http://test.org/UA/ModelE/">
              <opc:Namespaces>
                <opc:Namespace Name="ModelE" Prefix="Test.ModelE">http://test.org/UA/ModelE/</opc:Namespace>
                <opc:Namespace Name="ModelA" Prefix="Test.ModelA">http://test.org/UA/ModelA/</opc:Namespace>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="OptDerived" BaseType="s0:OptBase">
                <opc:Fields>
                  <opc:Field Name="Extra" DataType="ua:UInt32" IsOptional="true" />
                </opc:Fields>
              </opc:DataType>
            </opc:ModelDesign>
            """;

        /// <summary>
        /// Target of the three-level chain: subtypes MidStruct, which the
        /// ModelC payload defines as a subtype of ModelA's BaseStruct. The
        /// grandparent's namespace has to be declared here as well so the
        /// chain resolves all the way down to the design file.
        /// </summary>
        private const string ModelDDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns:s0="http://test.org/UA/ModelC/"
              xmlns="http://test.org/UA/ModelD/"
              TargetNamespace="http://test.org/UA/ModelD/">
              <opc:Namespaces>
                <opc:Namespace Name="ModelD" Prefix="Test.ModelD">http://test.org/UA/ModelD/</opc:Namespace>
                <opc:Namespace Name="ModelC" Prefix="Test.ModelC">http://test.org/UA/ModelC/</opc:Namespace>
                <opc:Namespace Name="ModelA" Prefix="Test.ModelA">http://test.org/UA/ModelA/</opc:Namespace>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="DerivedStruct" BaseType="s0:MidStruct">
                <opc:Fields>
                  <opc:Field Name="Extra" DataType="ua:UInt32" />
                </opc:Fields>
              </opc:DataType>
            </opc:ModelDesign>
            """;
    }
}
