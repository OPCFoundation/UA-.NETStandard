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
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Schema.Model;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Regression tests for issue #4353: an Object (or Variable) whose
    /// TypeDefinition names a type declared in a dependency ModelDesign
    /// must generate the same node-state factories as it would if the
    /// type were declared locally. The sibling of the #4332 structure
    /// subtyping tests on the instance-declaration path: the crash was a
    /// NullReferenceException in GetNodeStateClassName on the inherited
    /// children of the cross-model type, whose TypeDefinitionNode /
    /// DataTypeNode were never linked because dependency designs skip
    /// dictionary validation.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class CrossModelInstanceTypeDefinitionTests
    {
        private string m_rootPath;
        private string m_modelAPath;
        private string m_modelBPath;
        private string m_modelLocalPath;

        [SetUp]
        public void SetUp()
        {
            m_rootPath = Path.Combine(
                Path.GetTempPath(),
                "UA-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(m_rootPath, "A"));
            Directory.CreateDirectory(Path.Combine(m_rootPath, "B"));
            m_modelAPath = Path.Combine(m_rootPath, "A", "ModelA.xml");
            m_modelBPath = Path.Combine(m_rootPath, "B", "ModelB.xml");
            m_modelLocalPath = Path.Combine(m_rootPath, "B", "ModelLocal.xml");
            File.WriteAllText(m_modelAPath, ModelADesign);
            File.WriteAllText(m_modelBPath, ModelBDesign);
            File.WriteAllText(m_modelLocalPath, ModelLocalDesign);
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
        /// The primary repro from #4353: an Object instance in the target
        /// model whose TypeDefinition is an ObjectType with children
        /// declared in a dependency design file. NodeStateGenerator used
        /// to throw a NullReferenceException in GetNodeStateClassName on
        /// the inherited Label property.
        /// </summary>
        [Test]
        public void ObjectInstanceOfDependencyObjectTypeGenerates()
        {
            Dictionary<string, string> generated = Generate(
                targets: [m_modelBPath],
                dependencies: [m_modelBPath, m_modelAPath]);

            string code = GetNodeStateExtensions(generated);
            Assert.Multiple(() =>
            {
                Assert.That(code, Does.Contain("CreateWidget1("),
                    "The instance factory must be emitted.");
                Assert.That(code, Does.Contain("CreateWidget1_Label("),
                    "The factory for the property inherited from the " +
                    "dependency type must be emitted.");
                // The instance is created as the dependency's typed state
                // class, resolved through the dependency namespace prefix.
                Assert.That(code, Does.Contain("global::Test.ModelA.WidgetState"),
                    "The instance must use the typed state class of the " +
                    "dependency ObjectType.");
                // The method arguments of the dependency method must
                // materialise into the InputArguments property value.
                Assert.That(code, Does.Contain("\"Hard\""),
                    "The InputArguments value must carry the argument " +
                    "declared on the dependency method.");
                // The decoded default value of the dependency property must
                // flow into the generated value code.
                Assert.That(code, Does.Contain("Unlabeled"),
                    "The property value must carry the DefaultValue " +
                    "declared on the dependency type.");
                Assert.That(
                    code,
                    Does.Contain("global::Opc.Ua.AccessLevels.CurrentReadOrWrite"),
                    "The dependency variable AccessLevel must flow into the instance.");
            });
        }

        /// <summary>
        /// The expected behavior stated in #4353: the instance generates
        /// the same node-state factories as it would if the type were
        /// declared locally. Compares the full set of factory names of the
        /// instance subtree between a single-file control model and the
        /// cross-model variant.
        /// </summary>
        [Test]
        public void InstanceFactoriesMatchLocallyDeclaredType()
        {
            Dictionary<string, string> local = Generate(
                targets: [m_modelLocalPath],
                dependencies: [m_modelLocalPath]);
            Dictionary<string, string> crossModel = Generate(
                targets: [m_modelBPath],
                dependencies: [m_modelBPath, m_modelAPath]);

            HashSet<string> localFactories = ExtractFactoryNames(
                GetNodeStateExtensions(local), "Widget1");
            HashSet<string> crossModelFactories = ExtractFactoryNames(
                GetNodeStateExtensions(crossModel), "Widget1");

            Assert.That(localFactories, Is.Not.Empty,
                "The control model did not produce instance factories.");
            Assert.That(crossModelFactories, Is.EquivalentTo(localFactories),
                "The cross-model instance must generate the same " +
                "node-state factories as the locally declared type.");
        }

        /// <summary>
        /// The same two design files with the roles flipped: the target is
        /// the upstream type model and the dependency list contains the
        /// downstream instance model that imports it (the source generator
        /// supplies every design file of the compilation as a dependency
        /// of every other). Loading and linking the downstream instances
        /// must not fail.
        /// </summary>
        [Test]
        public void UpstreamTargetWithDownstreamInstanceModelGenerates()
        {
            Dictionary<string, string> generated = Generate(
                targets: [m_modelAPath],
                dependencies: [m_modelAPath, m_modelBPath]);

            string code = GetNodeStateExtensions(generated);
            Assert.That(code, Does.Contain("CreateWidgetType_Label("),
                "The type model must generate its own factories.");
        }

        /// <summary>
        /// Validator-level checks: the merged hierarchy of the target
        /// instance must carry linked TypeDefinitionNode / DataTypeNode
        /// references on the children materialised from the dependency
        /// type - that is what the generators dereference.
        /// </summary>
        [Test]
        public void DependencyTypeChildrenAreLinkedIntoInstanceHierarchy()
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

            ObjectDesign widget = model.Nodes
                .OfType<ObjectDesign>()
                .FirstOrDefault(n => n.SymbolicName.Name == "Widget1");
            Assert.That(widget, Is.Not.Null);
            Assert.That(widget.TypeDefinitionNode, Is.Not.Null,
                "The instance's own type definition must resolve to the " +
                "dependency ObjectType.");

            var label = widget.Hierarchy.Nodes["Label"].Instance as VariableDesign;
            Assert.That(label, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(label.TypeDefinitionNode, Is.Not.Null,
                    "Label's TypeDefinitionNode must be linked.");
                Assert.That(label.DataTypeNode, Is.Not.Null,
                    "Label's DataTypeNode must be linked.");
                Assert.That(label.DataTypeNode?.SymbolicName.Name, Is.EqualTo("String"));
                // The default value of the dependency property must be
                // decoded like ValidateInstance does for a local property.
                Assert.That(label.DecodedValue?.ToString(), Is.EqualTo("Unlabeled"),
                    "Label's DefaultValue must be decoded.");
                Assert.That(label.AccessLevel, Is.EqualTo(AccessLevel.ReadWrite));
                Assert.That(label.AccessLevelSpecified, Is.True);
                Assert.That(label.MinimumSamplingInterval, Is.EqualTo(250));
                Assert.That(label.MinimumSamplingIntervalSpecified, Is.True);
                Assert.That(label.Historizing, Is.True);
                Assert.That(label.HistorizingSpecified, Is.True);
            });

            var ownedLabels =
                widget.Hierarchy.Nodes["OwnedLabels"].Instance as VariableDesign;
            Assert.That(ownedLabels, Is.Not.Null);
            var decodedLabels = (string[])ownedLabels.DecodedValue;
            Assert.Multiple(() =>
            {
                Assert.That(ownedLabels.DefaultValue, Is.Not.Null);
                Assert.That(ownedLabels.DefaultValue.OuterXml, Does.Contain("First"));
                Assert.That(ownedLabels.DefaultValue.OuterXml, Does.Contain("Second"));
                Assert.That(decodedLabels, Has.Length.EqualTo(2));
                Assert.That(decodedLabels[0], Is.EqualTo("First"));
                Assert.That(decodedLabels[1], Is.EqualTo("Second"));
            });

            var level = widget.Hierarchy.Nodes["Level"].Instance as VariableDesign;
            Assert.That(level, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(level.TypeDefinitionNode, Is.Not.Null,
                    "Level's TypeDefinitionNode must be linked.");
                Assert.That(level.DataTypeNode, Is.Not.Null,
                    "Level's DataTypeNode must be linked.");
                // The variable declares no DataType of its own; it has to
                // inherit the restriction of the dependency VariableType.
                Assert.That(level.DataTypeNode?.SymbolicName.Name, Is.EqualTo("Double"));
                Assert.That(
                    (level.TypeDefinitionNode as VariableTypeDesign)?.DataTypeNode,
                    Is.Not.Null,
                    "The dependency VariableType's DataTypeNode must be linked.");
            });

            var reset = widget.Hierarchy.Nodes["Reset"].Instance as MethodDesign;
            Assert.That(reset, Is.Not.Null);
            Parameter hard = reset.InputArguments
                .FirstOrDefault(a => a.Name == "Hard");
            Parameter config = reset.InputArguments
                .FirstOrDefault(a => a.Name == "Config");
            Parameter result = reset.OutputArguments
                .FirstOrDefault(a => a.Name == "Result");
            Assert.Multiple(() =>
            {
                Assert.That(hard?.DataTypeNode?.SymbolicName.Name, Is.EqualTo("Boolean"),
                    "The method argument's DataTypeNode must be linked.");
                // Mirrors ValidateParameters: without UseAllowSubtypes a
                // structure argument that allows subtypes degrades to the
                // abstract Structure.
                Assert.That(config?.DataTypeNode?.SymbolicName.Name, Is.EqualTo("Structure"),
                    "The AllowSubTypes structure input argument must degrade " +
                    "to Structure like a locally validated method argument.");
                Assert.That(result?.DataTypeNode?.SymbolicName.Name, Is.EqualTo("Structure"),
                    "The AllowSubTypes structure output argument must degrade " +
                    "to Structure like a locally validated method argument.");
            });
        }

        private static string GetNodeStateExtensions(Dictionary<string, string> generated)
        {
            string code = generated.Keys
                .Where(f => f.EndsWith(".NodeStates.ex.g.cs", StringComparison.Ordinal))
                .Select(f => generated[f])
                .FirstOrDefault();
            Assert.That(code, Is.Not.Null, "No node state extensions generated.");
            return code;
        }

        /// <summary>
        /// Collects the names of all emitted factory methods for the
        /// instance subtree of <paramref name="instanceName"/>. Factory
        /// names derive from symbolic names only, so they are identical
        /// for a local and a cross-model type declaration.
        /// </summary>
        private static HashSet<string> ExtractFactoryNames(string code, string instanceName)
        {
            return Regex.Matches(
                    code,
                    @"internal static [^\r\n(]*\b(Create" + Regex.Escape(instanceName) + @"\w*)\(")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .ToHashSet();
        }

        private static Dictionary<string, string> Generate(
            IReadOnlyList<string> targets,
            IReadOnlyList<string> dependencies)
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
                referencedAccessorProviders: null);
            return fileSystem.CreatedFiles
                .ToDictionary(c => c, c => Encoding.UTF8.GetString(fileSystem.Get(c)));
        }

        /// <summary>
        /// The dependency model: an ObjectType with children (the trigger
        /// of #4353 is precisely the inherited child of a cross-model type
        /// declaration) and a VariableType with a DataType restriction
        /// used by one of the children.
        /// </summary>
        private const string ModelADesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd"
              xmlns="http://test.org/UA/ModelA/"
              TargetNamespace="http://test.org/UA/ModelA/">
              <opc:Namespaces>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="ModelA" Prefix="Test.ModelA">http://test.org/UA/ModelA/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="WidgetConfigType" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Setting" DataType="ua:String" />
                </opc:Fields>
              </opc:DataType>
              <opc:VariableType SymbolicName="WidgetLevelType" BaseType="ua:BaseDataVariableType" DataType="ua:Double" ValueRank="Scalar">
                <opc:Children>
                  <opc:Property SymbolicName="HighLimit" DataType="ua:Double" ValueRank="Scalar" />
                </opc:Children>
              </opc:VariableType>
              <opc:ObjectType SymbolicName="WidgetType" BaseType="ua:BaseObjectType">
                <opc:Children>
                  <opc:Property SymbolicName="Label" DataType="ua:String" ValueRank="Scalar"
                    AccessLevel="ReadWrite" MinimumSamplingInterval="250" Historizing="true">
                    <opc:DefaultValue>
                      <uax:String>Unlabeled</uax:String>
                    </opc:DefaultValue>
                  </opc:Property>
                  <opc:Variable SymbolicName="OwnedLabels" DataType="ua:String" ValueRank="Array"
                    AccessLevel="ReadWrite">
                    <opc:DefaultValue>
                      <uax:ListOfString>
                        <uax:String>First</uax:String>
                        <uax:String>Second</uax:String>
                      </uax:ListOfString>
                    </opc:DefaultValue>
                  </opc:Variable>
                  <opc:Variable SymbolicName="Level" TypeDefinition="WidgetLevelType" />
                  <opc:Method SymbolicName="Reset">
                    <opc:InputArguments>
                      <opc:Argument Name="Hard" DataType="ua:Boolean" ValueRank="Scalar" />
                      <opc:Argument Name="Config" DataType="WidgetConfigType" ValueRank="Scalar" AllowSubTypes="true" />
                    </opc:InputArguments>
                    <opc:OutputArguments>
                      <opc:Argument Name="Result" DataType="WidgetConfigType" ValueRank="Scalar" AllowSubTypes="true" />
                    </opc:OutputArguments>
                  </opc:Method>
                </opc:Children>
              </opc:ObjectType>
            </opc:ModelDesign>
            """;

        /// <summary>
        /// The target model of the repro: only an instance of the
        /// dependency ObjectType, organized under the ObjectsFolder.
        /// </summary>
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
              <opc:Object SymbolicName="Widget1" TypeDefinition="s0:WidgetType">
                <opc:References>
                  <opc:Reference IsInverse="true">
                    <opc:ReferenceType>ua:Organizes</opc:ReferenceType>
                    <opc:TargetId>ua:ObjectsFolder</opc:TargetId>
                  </opc:Reference>
                </opc:References>
              </opc:Object>
            </opc:ModelDesign>
            """;

        /// <summary>
        /// The control model: the same type declarations and the same
        /// instance in a single design file. This is the shape that always
        /// generated correctly and defines the expected factory set.
        /// </summary>
        private const string ModelLocalDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd"
              xmlns="http://test.org/UA/ModelL/"
              TargetNamespace="http://test.org/UA/ModelL/">
              <opc:Namespaces>
                <opc:Namespace Name="ModelL" Prefix="Test.ModelL">http://test.org/UA/ModelL/</opc:Namespace>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="WidgetConfigType" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Setting" DataType="ua:String" />
                </opc:Fields>
              </opc:DataType>
              <opc:VariableType SymbolicName="WidgetLevelType" BaseType="ua:BaseDataVariableType" DataType="ua:Double" ValueRank="Scalar">
                <opc:Children>
                  <opc:Property SymbolicName="HighLimit" DataType="ua:Double" ValueRank="Scalar" />
                </opc:Children>
              </opc:VariableType>
              <opc:ObjectType SymbolicName="WidgetType" BaseType="ua:BaseObjectType">
                <opc:Children>
                  <opc:Property SymbolicName="Label" DataType="ua:String" ValueRank="Scalar"
                    AccessLevel="ReadWrite" MinimumSamplingInterval="250" Historizing="true">
                    <opc:DefaultValue>
                      <uax:String>Unlabeled</uax:String>
                    </opc:DefaultValue>
                  </opc:Property>
                  <opc:Variable SymbolicName="OwnedLabels" DataType="ua:String" ValueRank="Array"
                    AccessLevel="ReadWrite">
                    <opc:DefaultValue>
                      <uax:ListOfString>
                        <uax:String>First</uax:String>
                        <uax:String>Second</uax:String>
                      </uax:ListOfString>
                    </opc:DefaultValue>
                  </opc:Variable>
                  <opc:Variable SymbolicName="Level" TypeDefinition="WidgetLevelType" />
                  <opc:Method SymbolicName="Reset">
                    <opc:InputArguments>
                      <opc:Argument Name="Hard" DataType="ua:Boolean" ValueRank="Scalar" />
                      <opc:Argument Name="Config" DataType="WidgetConfigType" ValueRank="Scalar" AllowSubTypes="true" />
                    </opc:InputArguments>
                    <opc:OutputArguments>
                      <opc:Argument Name="Result" DataType="WidgetConfigType" ValueRank="Scalar" AllowSubTypes="true" />
                    </opc:OutputArguments>
                  </opc:Method>
                </opc:Children>
              </opc:ObjectType>
              <opc:Object SymbolicName="Widget1" TypeDefinition="WidgetType">
                <opc:References>
                  <opc:Reference IsInverse="true">
                    <opc:ReferenceType>ua:Organizes</opc:ReferenceType>
                    <opc:TargetId>ua:ObjectsFolder</opc:TargetId>
                  </opc:Reference>
                </opc:References>
              </opc:Object>
            </opc:ModelDesign>
            """;
    }
}
