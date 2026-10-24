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
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration.Tests
{
    /// <summary>
    /// Exercises scoped, allocation-preserving ModelDesign generation without model-name exceptions.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    public sealed class ExplicitModelGenerationTests
    {
        /// <summary>
        /// Inherited members remain available to runtime factories but do not receive published identifiers.
        /// </summary>
        [Test]
        public void InheritedNodesRemainUnallocatedOnlyInsideExplicitScope()
        {
            using var files = CreateFiles(
                kBaseType +
                """
                <opc:ObjectType SymbolicName="ExactType" BaseType="BaseType" PreserveExplicitNodes="true">
                  <opc:Children>
                    <opc:Property SymbolicName="Own" DataType="ua:UInt32" ModellingRule="Mandatory" />
                  </opc:Children>
                </opc:ObjectType>
                <opc:ObjectType SymbolicName="DefaultType" BaseType="BaseType" />
                """,
                "BaseType,1001,ObjectType\nBaseType_Value,1002,Variable\n" +
                "ExactType,1010,ObjectType\nExactType_Own,1011,Variable\nDefaultType,1020,ObjectType\n");
            IModelDesign model = Validate(files);
            NodeDesign exact = Find(model, "ExactType");
            NodeDesign normal = Find(model, "DefaultType");
            Assert.Multiple(() =>
            {
                Assert.That(exact.Hierarchy.Nodes["Value"].Instance!.HasIdentifier(), Is.False);
                Assert.That(exact.Hierarchy.Nodes["Own"].Instance!.NumericId, Is.EqualTo(1011u));
                Assert.That(normal.Hierarchy.Nodes["Value"].Instance!.HasIdentifier(), Is.True);
                Assert.That(Emit(model, files, identifiers: true), Does.Not.Contain("const uint ExactType_Value"));
                Assert.That(Emit(model, files, identifiers: true), Does.Not.Contain("const uint DefaultType_Value"));
                Assert.That(Emit(model, files, identifiers: false),
                    Does.Contain("CreateInstanceOfExactType"));
            });
        }

        /// <summary>
        /// Authored enumeration metadata is retained rather than replaced by a Mandatory property.
        /// </summary>
        [TestCase("None")]
        [TestCase("Optional")]
        public void EnumMetadataRetainsItsExplicitModellingRule(string rule)
        {
            using var files = CreateFiles(
                $$"""
                <opc:DataType SymbolicName="Mode" BaseType="ua:Enumeration" PreserveExplicitNodes="true">
                  <opc:Children>
                    <opc:Property SymbolicName="ua:EnumStrings" DataType="ua:LocalizedText"
                        ModellingRule="{{rule}}" ValueRank="Array" ArrayDimensions="2">
                      <opc:DefaultValue>
                        <uax:ListOfLocalizedText>
                          <uax:LocalizedText><uax:Text>Off</uax:Text></uax:LocalizedText>
                          <uax:LocalizedText><uax:Text>On</uax:Text></uax:LocalizedText>
                        </uax:ListOfLocalizedText>
                      </opc:DefaultValue>
                    </opc:Property>
                  </opc:Children>
                  <opc:Fields>
                    <opc:Field Name="Off" Identifier="0" />
                    <opc:Field Name="On" Identifier="1" />
                  </opc:Fields>
                </opc:DataType>
                """,
                "Mode,1100,DataType\nMode_EnumStrings,1101,Variable\n");
            IModelDesign model = Validate(files);
            var mode = (DataTypeDesign)Find(model, "Mode");
            Assert.That(mode.Children.Items, Has.Length.EqualTo(1));
            var property = (PropertyDesign)mode.Children.Items[0];
            Assert.Multiple(() =>
            {
                Assert.That(property.ModellingRule.ToString(), Is.EqualTo(rule));
                Assert.That(property.ArrayDimensions, Is.EqualTo("2"));
                Assert.That(property.DefaultValue!.OuterXml, Does.Contain("Off"));
                Assert.That(property.DefaultValue.OuterXml, Does.Contain("On"));
                Assert.That(Emit(model, files, identifiers: true), Does.Contain("Mode_EnumStrings = 1101"));
            });
        }

        /// <summary>
        /// An exact enumeration without metadata must not gain an invented EnumStrings node.
        /// </summary>
        [Test]
        public void EnumWithoutDeclaredMetadataDoesNotInventAProperty()
        {
            using var files = CreateFiles(
                """
                <opc:DataType SymbolicName="Mode" BaseType="ua:Enumeration" PreserveExplicitNodes="true">
                  <opc:Fields><opc:Field Name="Off" Identifier="0" /></opc:Fields>
                </opc:DataType>
                """,
                "Mode,1100,DataType\n");
            IModelDesign model = Validate(files);
            Assert.That(Find(model, "Mode").Children?.Items ?? [], Is.Empty);
            Assert.That(Emit(model, files, identifiers: true), Does.Not.Contain("Mode_EnumStrings"));
        }

        /// <summary>
        /// Encodings retain their allocated identity and HasEncoding edge without legacy dictionary projection.
        /// </summary>
        [Test]
        public void EncodingGraphDoesNotInventDictionaryDescriptions()
        {
            using var files = CreateFiles(
                """
                <opc:DataType SymbolicName="Payload" BaseType="ua:Structure" PreserveExplicitNodes="true">
                  <opc:Fields><opc:Field Name="Value" DataType="ua:UInt32" /></opc:Fields>
                </opc:DataType>
                """,
                "Payload,1200,DataType\nPayload_Encoding_DefaultBinary,1201,Object\n" +
                "Payload_Encoding_DefaultXml,1202,Object\n");
            IModelDesign model = Validate(files);
            Assert.That(model.Nodes.Select(node => node.SymbolicId.Name), Is.EquivalentTo(s_encodingSymbols));
            foreach (string suffix in new[] { "DefaultBinary", "DefaultXml" })
            {
                NodeDesign encoding = Find(model, "Payload_Encoding_" + suffix);
                Assert.That(encoding.References, Has.Length.EqualTo(1));
                Assert.Multiple(() =>
                {
                    Assert.That(encoding.References[0].ReferenceType.Name, Is.EqualTo("HasEncoding"));
                    Assert.That(encoding.References[0].IsInverse, Is.True);
                    Assert.That(encoding.References[0].TargetId.Name, Is.EqualTo("Payload"));
                });
            }
        }

        /// <summary>
        /// Real instance allocations emit constants without changing their absence of modelling rules.
        /// </summary>
        [Test]
        public void ExplicitInstanceConstantsDoNotRequireModellingRules()
        {
            using var files = CreateFiles(
                """
                <opc:Object SymbolicName="Installed" TypeDefinition="ua:BaseObjectType"
                    PreserveExplicitNodes="true">
                  <opc:Children>
                    <opc:Property SymbolicName="Value" DataType="ua:UInt32" ModellingRule="None" />
                    <opc:Method SymbolicName="Reset" ModellingRule="None" />
                  </opc:Children>
                </opc:Object>
                <opc:Object SymbolicName="DefaultInstalled" TypeDefinition="ua:BaseObjectType">
                  <opc:Children>
                    <opc:Property SymbolicName="Value" DataType="ua:UInt32" ModellingRule="None" />
                  </opc:Children>
                </opc:Object>
                """,
                "Installed,1300,Object\nInstalled_Value,1301,Variable\nInstalled_Reset,1302,Method\n" +
                "DefaultInstalled,1310,Object\nDefaultInstalled_Value,1311,Variable\n");
            IModelDesign model = Validate(files);
            string source = Emit(model, files, identifiers: true);
            Assert.Multiple(() =>
            {
                Assert.That(source, Does.Contain("Installed_Value = 1301"));
                Assert.That(source, Does.Contain("Installed_Reset = 1302"));
                Assert.That(source, Does.Not.Contain("const uint DefaultInstalled_Value"));
                Assert.That(
                    ((InstanceDesign)Find(model, "Installed").Hierarchy.Nodes["Value"].Instance!).ModellingRule,
                    Is.EqualTo(ModellingRule.None));
            });
        }

        /// <summary>
        /// Removing an earlier sibling cannot renumber a compatibility-pinned inherited declaration.
        /// </summary>
        [Test]
        public void StableSiblingAllocationsSurviveEarlierSiblingRemoval()
        {
            const string stable = """<opc:ObjectType SymbolicName="StableType" BaseType="BaseType" />""";
            const string transient = """<opc:ObjectType SymbolicName="TransientType" BaseType="BaseType" />""";
            const string csv = "BaseType,1001,ObjectType\nBaseType_Value,1002,Variable\n" +
                "StableType,1400,ObjectType\nTransientType,1410,ObjectType\n";
            using var baselineFiles = CreateFiles(kBaseType + transient + stable, csv);
            IModelDesign baseline = Validate(baselineFiles);
            uint published = Find(baseline, "StableType").Hierarchy.Nodes["Value"].Instance!.NumericId;
            using var changedFiles = CreateFiles(
                kBaseType + stable,
                csv,
                $"StableType_Value,{published},Variable\n");
            IModelDesign changed = Validate(changedFiles);
            Assert.That(
                Find(changed, "StableType").Hierarchy.Nodes["Value"].Instance!.NumericId,
                Is.EqualTo(published));
        }

        /// <summary>
        /// A legacy inherited allocation may differ from an ignored CSV row without changing that authored row.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ImplicitCompatibilityIsRestrictedToInheritedAllocations(bool becomesExplicit)
        {
            const string stable = """<opc:ObjectType SymbolicName="StableType" BaseType="BaseType" />""";
            const string transient = """<opc:ObjectType SymbolicName="TransientType" BaseType="BaseType" />""";
            const string csv = "BaseType,1001,ObjectType\nBaseType_Value,1002,Variable\n" +
                "StableType,1400,ObjectType\nStableType_Value,1490,Variable\nTransientType,1410,ObjectType\n";
            using var baselineFiles = CreateFiles(kBaseType + transient + stable, csv);
            IModelDesign baseline = Validate(baselineFiles);
            uint published = Find(baseline, "StableType").Hierarchy.Nodes["Value"].Instance!.NumericId;
            Assert.That(published, Is.Not.EqualTo(1490u), "The unchanged legacy allocator ignores this implicit row.");
            string replacement = becomesExplicit
                ? """
                  <opc:ObjectType SymbolicName="StableType" BaseType="BaseType">
                    <opc:Children>
                      <opc:Property SymbolicName="Value" DataType="ua:UInt32" ModellingRule="Mandatory" />
                    </opc:Children>
                  </opc:ObjectType>
                  """
                : stable;
            using var changedFiles = CreateFiles(
                kBaseType + replacement,
                csv,
                $"StableType_Value,{published},Variable,Implicit\n");
            if (becomesExplicit)
            {
                Assert.Throws<InvalidOperationException>(() => Validate(changedFiles));
            }
            else
            {
                IModelDesign changed = Validate(changedFiles);
                Assert.That(Find(changed, "StableType").Hierarchy.Nodes["Value"].Instance!.NumericId,
                    Is.EqualTo(published));
            }
        }

        /// <summary>
        /// Compatibility input cannot override a different authoritative allocation.
        /// </summary>
        [Test]
        public void ConflictingCompatibilityAllocationIsRejected()
        {
            using var files = CreateFiles(
                kBaseType,
                "BaseType,1001,ObjectType\nBaseType_Value,1002,Variable\n",
                "BaseType_Value,1900,Variable\n");
            Assert.Throws<InvalidOperationException>(() => Validate(files));
        }

        /// <summary>
        /// Explicit namespace metadata is not recomputed from unrelated model-version or dynamic-ID defaults.
        /// </summary>
        [Test]
        public void NamespaceMetadataValuesRemainAuthored()
        {
            using var files = CreateFiles(
                """
                <opc:Object SymbolicName="Metadata" TypeDefinition="ua:NamespaceMetadataType"
                    PreserveExplicitNodes="true">
                  <opc:Children>
                    <opc:Property SymbolicName="ua:StaticNumericNodeIdRange" DataType="ua:String" ValueRank="Array">
                      <opc:DefaultValue>
                        <uax:ListOfString><uax:String>1500:1599</uax:String></uax:ListOfString>
                      </opc:DefaultValue>
                    </opc:Property>
                    <opc:Property SymbolicName="ua:NamespaceVersion" DataType="ua:String">
                      <opc:DefaultValue><uax:String>2.0.0</uax:String></opc:DefaultValue>
                    </opc:Property>
                    <opc:Property SymbolicName="ua:NamespacePublicationDate" DataType="ua:DateTime">
                      <opc:DefaultValue><uax:DateTime>2026-02-01T00:00:00Z</uax:DateTime></opc:DefaultValue>
                    </opc:Property>
                  </opc:Children>
                </opc:Object>
                """,
                "Metadata,1500,Object\nMetadata_StaticNumericNodeIdRange,1501,Variable\n" +
                "Metadata_NamespaceVersion,1502,Variable\nMetadata_NamespacePublicationDate,1503,Variable\n");
            IModelDesign model = Validate(files);
            NodeDesign metadata = Find(model, "Metadata");
            string Value(string name)
            {
                return ((VariableDesign)metadata.Hierarchy.Nodes[name].Instance!).DefaultValue.InnerText;
            }
            Assert.Multiple(() =>
            {
                Assert.That(Value("StaticNumericNodeIdRange"), Is.EqualTo("1500:1599"));
                Assert.That(Value("NamespaceVersion"), Is.EqualTo("2.0.0"));
                Assert.That(Value("NamespacePublicationDate"), Is.EqualTo("2026-02-01T00:00:00Z"));
            });
        }

        /// <summary>
        /// Exact method argument metadata distinguishes an explicitly empty dimension array from null.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void MethodArgumentDimensionsRetainNullVersusEmpty(bool isNull)
        {
            string dimensions = isNull
                ? """<uax:ArrayDimensions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:nil="true" />"""
                : "<uax:ArrayDimensions />";
            using var files = CreateFiles(
                $$"""
                <opc:ObjectType SymbolicName="ControllerType" BaseType="ua:BaseObjectType"
                    PreserveExplicitNodes="true">
                  <opc:Children>
                    <opc:Method SymbolicName="Reset" ModellingRule="Mandatory">
                      <opc:Children>
                        <opc:Property SymbolicName="ua:InputArguments" DataType="ua:Argument"
                            ValueRank="Array" ArrayDimensions="1" ModellingRule="Mandatory">
                          <opc:DefaultValue>
                            <uax:ListOfExtensionObject><uax:ExtensionObject>
                              <uax:TypeId><uax:Identifier>i=297</uax:Identifier></uax:TypeId>
                              <uax:Body><uax:Argument>
                                <uax:Name>value</uax:Name>
                                <uax:DataType><uax:Identifier>i=7</uax:Identifier></uax:DataType>
                                <uax:ValueRank>-1</uax:ValueRank>
                                {{dimensions}}
                              </uax:Argument></uax:Body>
                            </uax:ExtensionObject></uax:ListOfExtensionObject>
                          </opc:DefaultValue>
                        </opc:Property>
                      </opc:Children>
                      <opc:InputArguments><opc:Argument Name="value" DataType="ua:UInt32" /></opc:InputArguments>
                    </opc:Method>
                  </opc:Children>
                </opc:ObjectType>
                """,
                "ControllerType,1600,ObjectType\nControllerType_Reset,1601,Method\n" +
                "ControllerType_Reset_InputArguments,1602,Variable\n");
            IModelDesign model = Validate(files);
            string source = Emit(model, files, identifiers: false);
            Assert.That(source, Does.Contain(
                "ArrayDimensions = " + (isNull ? "default" : "global::Opc.Ua.ArrayOf<uint>.Empty")));
        }

        /// <summary>
        /// Standalone exact Methods preserve authored argument properties and reject missing property metadata.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void StandaloneMethodArgumentMetadataIsNeverSynthesized(bool omitProperty)
        {
            string property = omitProperty ? string.Empty :
                """
                <opc:Children>
                  <opc:Property SymbolicName="ua:InputArguments" DataType="ua:Argument"
                      ValueRank="Array" ArrayDimensions="1" ModellingRule="None">
                    <opc:DefaultValue>
                      <uax:ListOfExtensionObject><uax:ExtensionObject>
                        <uax:TypeId><uax:Identifier>i=297</uax:Identifier></uax:TypeId>
                        <uax:Body><uax:Argument>
                          <uax:Name>value</uax:Name>
                          <uax:DataType><uax:Identifier>i=7</uax:Identifier></uax:DataType>
                          <uax:ValueRank>-1</uax:ValueRank>
                          <uax:ArrayDimensions />
                        </uax:Argument></uax:Body>
                      </uax:ExtensionObject></uax:ListOfExtensionObject>
                    </opc:DefaultValue>
                  </opc:Property>
                </opc:Children>
                """;
            using var files = CreateFiles(
                $$"""
                <opc:Method SymbolicName="Reset" PreserveExplicitNodes="true">
                  {{property}}
                  <opc:InputArguments><opc:Argument Name="value" DataType="ua:UInt32" /></opc:InputArguments>
                </opc:Method>
                """,
                "Reset,1800,Method\nReset_InputArguments,1801,Variable\n");
            if (omitProperty)
            {
                Assert.Throws<InvalidOperationException>(() => Validate(files));
            }
            else
            {
                IModelDesign model = Validate(files);
                NodeDesign method = Find(model, "Reset");
                Assert.That(method.Children.Items, Has.Length.EqualTo(1));
                Assert.That(method.Children.Items[0].ModellingRule, Is.EqualTo(ModellingRule.None));
            }
        }

        /// <summary>
        /// An explicit concrete child is not suppressed by the stack's optional-member policy.
        /// </summary>
        [Test]
        public void ExplicitInstanceOverridesOptionalTypePolicy()
        {
            using var files = CreateFiles(
                """
                <opc:ObjectType SymbolicName="ControllerType" BaseType="ua:BaseObjectType">
                  <opc:Children>
                    <opc:Property SymbolicName="Value" DataType="ua:UInt32" ModellingRule="Optional" />
                  </opc:Children>
                </opc:ObjectType>
                <opc:Object SymbolicName="Installed" TypeDefinition="ControllerType" PreserveExplicitNodes="true">
                  <opc:Children>
                    <opc:Property SymbolicName="Value" DataType="ua:UInt32" ModellingRule="None" />
                  </opc:Children>
                </opc:Object>
                """,
                "ControllerType,1700,ObjectType\nControllerType_Value,1701,Variable\n" +
                "Installed,1710,Object\nInstalled_Value,1711,Variable\n");
            IModelDesign model = Validate(files);
            string source = Emit(model, files, identifiers: false, useTypeRules: true);
            string factory = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "CreateInstalled" &&
                    method.Modifiers.Any(token => token.IsKind(SyntaxKind.InternalKeyword)) &&
                    method.Modifiers.Any(token => token.IsKind(SyntaxKind.StaticKeyword)))
                .ToString();
            Assert.Multiple(() =>
            {
                Assert.That(factory, Does.Contain("CreateInstalled_Value(context"));
                int guard = factory.IndexOf("if (!forInstance)", StringComparison.Ordinal);
                Assert.That(
                    guard < 0 || factory.IndexOf("CreateInstalled_Value(context", StringComparison.Ordinal) < guard,
                    Is.True,
                    "The concrete child must be created outside the type-only block.");
            });
        }

        /// <summary>
        /// The generated loader omits undeclared nodes while a compiled runtime factory retains inherited members.
        /// </summary>
        [Test]
        public void CompiledFactoriesPreserveExactNodesAndRuntimeInheritance()
        {
            using var files = CreateFiles(
                kBaseType +
                """
                <opc:ObjectType SymbolicName="ExactType" BaseType="BaseType" PreserveExplicitNodes="true">
                  <opc:Children>
                    <opc:Property SymbolicName="Own" DataType="ua:UInt32" ModellingRule="Mandatory" />
                  </opc:Children>
                </opc:ObjectType>
                <opc:Object SymbolicName="Installed" TypeDefinition="ExactType" PreserveExplicitNodes="true">
                  <opc:Children>
                    <opc:Property SymbolicName="Own" DataType="ua:UInt32" ModellingRule="None" />
                  </opc:Children>
                </opc:Object>
                """,
                "BaseType,1001,ObjectType\nBaseType_Value,1002,Variable\n" +
                "ExactType,1010,ObjectType\nExactType_Own,1011,Variable\n" +
                "Installed,1020,Object\nInstalled_Own,1021,Variable\n");
            IModelDesign model = Validate(files);
            using var outputs = new VirtualFileSystem();
            var context = new GeneratorContext
            {
                ModelDesign = model,
                FileSystem = files.WithFallback(outputs),
                OutputFolder = string.Empty,
                Telemetry = new SilentTelemetry(),
                Options = new GeneratorOptions
                {
                    OmitFluentApi = true,
                    UseTypeDefinitionModellingRules = true
                }
            };
            _ = new ConstantsGenerator(context).Emit().ToArray();
            _ = new NodeIdGenerator(context).Emit().ToArray();
            _ = new NodeStateGenerator(context).Emit().ToArray();
            Dictionary<string, string> sources = outputs.CreatedFiles
                .ToDictionary(name => name, name => Encoding.UTF8.GetString(outputs.Get(name)));
            sources.Add("Probe.cs", kCompiledProbe);
            using var stream = new MemoryStream();
            var result = OptimizationLevel.Release.CreateCompilation()
                .AddCode(sources.WithOpcUaGeneratedStack(), LanguageVersion.Latest)
                .Emit(stream);
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
            Assembly assembly = Assembly.Load(stream.ToArray());
            string? error = assembly.GetType("ExplicitProbe")!.GetMethod("Run")!.Invoke(
                null, [new SilentTelemetry()]) as string;
            Assert.That(error, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// An explicit scope must fail closed when an authored node lacks an allocation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void MissingExplicitAllocationIsRejected(bool omitChild)
        {
            using var files = CreateFiles(
                """
                <opc:Object SymbolicName="Installed" TypeDefinition="ua:BaseObjectType"
                    PreserveExplicitNodes="true">
                  <opc:Children><opc:Property SymbolicName="Value" DataType="ua:UInt32" /></opc:Children>
                </opc:Object>
                """,
                omitChild ? "Installed,1300,Object\n" : "Installed_Value,1301,Variable\n");
            Assert.Throws<InvalidOperationException>(() => Validate(files));
        }

        /// <summary>
        /// Builds a small independent model with optional baseline allocation input.
        /// </summary>
        private static VirtualFileSystem CreateFiles(string nodes, string csv, string? compatibility = null)
        {
            var files = new VirtualFileSystem();
            string attribute = compatibility == null ? string.Empty : "IdentifierCompatibilityFile=\"Baseline.csv\"";
            files.Add("Fixture.xml", Encoding.UTF8.GetBytes(
                $$"""
                <opc:ModelDesign xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
                    xmlns:ua="http://opcfoundation.org/UA/"
                    xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd"
                    xmlns="urn:test:explicit" TargetNamespace="urn:test:explicit"
                    TargetVersion="1.0.0" TargetPublicationDate="2026-01-01T00:00:00Z" {{attribute}}>
                  <opc:Namespaces>
                    <opc:Namespace Name="Fixture" Prefix="Fixture">urn:test:explicit</opc:Namespace>
                    <opc:Namespace Name="OpcUa" Prefix="Opc.Ua">http://opcfoundation.org/UA/</opc:Namespace>
                  </opc:Namespaces>
                  {{nodes}}
                </opc:ModelDesign>
                """));
            files.Add("Fixture.csv", Encoding.UTF8.GetBytes(csv));
            if (compatibility != null)
            {
                files.Add("Baseline.csv", Encoding.UTF8.GetBytes(compatibility));
            }
            return files;
        }

        /// <summary>
        /// Invokes the real model validator with the normal embedded standard model.
        /// </summary>
        private static IModelDesign Validate(VirtualFileSystem files)
        {
            IFileSystem fileSystem = files.WithFallback(
                typeof(Generators).Assembly.AsFileSystem("Opc.Ua.SourceGeneration.Design"));
            return fileSystem.OpenModelDesign(
                new DesignFileCollection { Targets = ["Fixture.xml"], IdentifierFilePath = "Fixture.csv" },
                [],
                new SilentTelemetry());
        }

        /// <summary>
        /// Locates a declaration in the target model.
        /// </summary>
        private static NodeDesign Find(IModelDesign model, string symbol)
        {
            return model.Nodes.Single(node => node.SymbolicId.Name == symbol);
        }

        /// <summary>
        /// Executes the identifier or factory emitter, not an XML approximation.
        /// </summary>
        private static string Emit(
            IModelDesign model,
            VirtualFileSystem inputs,
            bool identifiers,
            bool useTypeRules = false)
        {
            using var outputs = new VirtualFileSystem();
            var context = new GeneratorContext
            {
                ModelDesign = model,
                FileSystem = inputs.WithFallback(outputs),
                Telemetry = new SilentTelemetry(),
                OutputFolder = string.Empty,
                Options = new GeneratorOptions
                {
                    OmitFluentApi = true,
                    UseTypeDefinitionModellingRules = useTypeRules
                }
            };
            IGenerator generator = identifiers ? new NodeIdGenerator(context) : new NodeStateGenerator(context);
            _ = generator.Emit().ToArray();
            return string.Join("\n", outputs.CreatedFiles.Select(file => Encoding.UTF8.GetString(outputs.Get(file))));
        }

        /// <summary>
        /// Keeps tests independent from logging infrastructure.
        /// </summary>
        private sealed class SilentTelemetry : TelemetryContextBase
        {
            /// <summary>
            /// Creates the logger-free telemetry context.
            /// </summary>
            public SilentTelemetry()
                : base(NullLoggerFactory.Instance)
            {
            }
        }

        /// <summary>Encoding identities in the exact fixture.</summary>
        private static readonly string[] s_encodingSymbols =
            ["Payload", "Payload_Encoding_DefaultBinary", "Payload_Encoding_DefaultXml"];

        /// <summary>Shared type with one inherited mandatory child.</summary>
        private const string kBaseType =
            """
            <opc:ObjectType SymbolicName="BaseType" BaseType="ua:BaseObjectType">
              <opc:Children>
                <opc:Property SymbolicName="Value" DataType="ua:UInt32" ModellingRule="Mandatory" />
              </opc:Children>
            </opc:ObjectType>
            """;

        /// <summary>Runs only freshly generated fixture factories against the real node-state runtime.</summary>
        private const string kCompiledProbe =
            """
            using System.Collections.Generic;
            using System.Linq;
            using Opc.Ua;
            using Fixture;

            public static class ExplicitProbe
            {
                public static string Run(ITelemetryContext telemetry)
                {
                    var context = new SystemContext(telemetry) { NamespaceUris = new NamespaceTable() };
                    context.NamespaceUris.GetIndexOrAppend("urn:test:application");
                    ushort ns = context.NamespaceUris.GetIndexOrAppend("urn:test:explicit");
                    var nodes = new NodeStateCollection();
                    nodes.AddFixture(context);
                    NodeState type = nodes.Single(node => node.NodeId == new NodeId(1010u, ns));
                    NodeState installed = nodes.Single(node => node.NodeId == new NodeId(1020u, ns));
                    var typeChildren = new List<BaseInstanceState>();
                    type.GetChildren(context, typeChildren);
                    var instanceChildren = new List<BaseInstanceState>();
                    installed.GetChildren(context, instanceChildren);
                    if (typeChildren.Count != 1 || typeChildren[0].NodeId != new NodeId(1011u, ns) ||
                        instanceChildren.Count != 1 || instanceChildren[0].NodeId != new NodeId(1021u, ns))
                    {
                        return "The compiled predefined graph invented or lost a child.";
                    }
                    if (!instanceChildren[0].ModellingRuleId.IsNull)
                    {
                        return "An ordinary instance gained a modelling rule.";
                    }
                    if (installed is not ExactState exact || exact.Own == null ||
                        installed.FindChild(context, new QualifiedName("Own", ns)) != exact.Own)
                    {
                        return "The explicit concrete child is not bound to its typed state slot.";
                    }
                    ExactState runtime = context.CreateInstanceOfExactType();
                    return runtime.Value == null || runtime.Own == null
                        ? "The runtime factory lost an inherited mandatory member."
                        : "";
                }
            }
            """;
    }
}
