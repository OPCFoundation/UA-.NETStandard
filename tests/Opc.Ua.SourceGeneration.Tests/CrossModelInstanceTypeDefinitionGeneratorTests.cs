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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Regression tests for issue #4353 at the source generator driver
    /// level: an Object whose TypeDefinition is an ObjectType with
    /// children declared in a sibling AdditionalFile ModelDesign used to
    /// fail with MODELGEN003 / NullReferenceException in
    /// NodeStateGenerator while building the node state factories for
    /// the inherited children.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class CrossModelInstanceTypeDefinitionGeneratorTests
    {
        [TestCase(false, "Initialize")]
        [TestCase(true, "Initialize")]
        [TestCase(false, "Clone")]
        [TestCase(true, "Clone")]
        [TestCase(false, "Copy")]
        [TestCase(true, "Copy")]
        [TestCase(false, "Recreate")]
        [TestCase(true, "Recreate")]
        public void DerivedMandatoryChildrenSurviveInheritedOptionalInitialization(bool prepareOptional, string operation)
        {
            CSharpCompilation compilation = OptimizationLevel.Release.CreateCompilation()
                .AddCode(new Dictionary<string, string>().WithOpcUaGeneratedStack(), LanguageVersion.CSharp11)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                    """
                    public static class InitializationProbe
                    {
                        public static string Run(bool prepareOptional, string operation)
                        {
                            var context = new Opc.Ua.SystemContext(null);
                            context.NamespaceUris = new Opc.Ua.NamespaceTable();
                            ushort baseIndex = context.NamespaceUris.GetIndexOrAppend("http://test.org/UA/OptionalBase/");
                            ushort derivedIndex = context.NamespaceUris.GetIndexOrAppend("http://test.org/UA/Specialized/");
                            var state = new Test.Specialized.SpecializedState(null);
                            if (prepareOptional)
                            {
                                state.CreateChild(context, new Opc.Ua.QualifiedName("Label", baseIndex), false);
                            }
                            string optionalBefore = state.Label == null ? "null" :
                                state.Label.NodeId + "/" + state.Label.ModellingRuleId + "/" + state.Label.DataType;
                            state.Create(context, new Opc.Ua.NodeId("Probe", derivedIndex),
                                new Opc.Ua.QualifiedName("Probe", derivedIndex), new Opc.Ua.LocalizedText("Probe"), false);
                            var parameterSet = state.ParameterSet;
                            if (parameterSet == null)
                            {
                                return "ParameterSet is missing";
                            }
                            var temperature = parameterSet.FindChild(context,
                                new Opc.Ua.QualifiedName("Temperature", derivedIndex)) as Opc.Ua.BaseVariableState;
                            if (temperature == null)
                            {
                                return "Specialized mandatory Temperature is missing";
                            }
                            if (!ReferenceEquals(temperature.Parent, parameterSet) || temperature.Value.GetFloat() != 20f)
                            {
                                return "Specialized parent or default changed";
                            }
                            if (prepareOptional)
                            {
                                if (state.Label == null || state.Label.DataType != new Opc.Ua.NodeId(23751) ||
                                    state.Label.ReferenceTypeId != Opc.Ua.ReferenceTypeIds.HasProperty ||
                                    state.Label.AccessLevel != Opc.Ua.AccessLevels.CurrentRead)
                                {
                                    return "Explicit optional declaration metadata changed; before=" + optionalBefore +
                                        "; after=" + state.Label?.NodeId + "/" + state.Label?.ModellingRuleId +
                                        "/" + state.Label?.DataType;
                                }
                            }
                            else if (state.Label != null)
                            {
                                return "An unrequested optional child was created";
                            }
                            if (operation == "Recreate")
                            {
                                var reused = new Test.Specialized.SpecializedState(null);
                                reused.Create(context, state);
                                reused.Delete(context);
                                if (reused.Label != null)
                                {
                                    reused.Label.DataType = new Opc.Ua.NodeId(12);
                                }
                                reused.Create(context, new Opc.Ua.NodeId("Reused", derivedIndex),
                                    new Opc.Ua.QualifiedName("Reused", derivedIndex),
                                    new Opc.Ua.LocalizedText("Reused"), false);
                                if (prepareOptional && reused.Label.DataType != new Opc.Ua.NodeId(23751))
                                {
                                    return "Recreated optional child declaration metadata was not restored";
                                }
                                if (!prepareOptional && reused.Label != null)
                                {
                                    return "Recreation added an unrequested optional child";
                                }
                                return "";
                            }
                            if (operation != "Initialize")
                            {
                                var root = new Opc.Ua.BaseObjectState(null);
                                root.NodeId = new Opc.Ua.NodeId("Root", derivedIndex);
                                root.BrowseName = new Opc.Ua.QualifiedName("Root", derivedIndex);
                                root.AddChild(state);
                                var method = new Opc.Ua.ReadMethodState(root);
                                method.Create(context, new Opc.Ua.NodeId("Read", derivedIndex),
                                    new Opc.Ua.QualifiedName("Read", derivedIndex), new Opc.Ua.LocalizedText("Read"), false);
                                root.AddChild(method);
                                Opc.Ua.BaseObjectState copy;
                                if (operation == "Clone")
                                {
                                    copy = (Opc.Ua.BaseObjectState)root.Clone();
                                }
                                else
                                {
                                    copy = new Opc.Ua.BaseObjectState(null);
                                    copy.Create(context, root);
                                }
                                return CheckChildren(context, root, copy);
                            }
                            return "";
                        }

                        private static string CheckChildren(Opc.Ua.ISystemContext context,
                            Opc.Ua.NodeState source, Opc.Ua.NodeState copy)
                        {
                            var sourceChildren = new System.Collections.Generic.List<Opc.Ua.BaseInstanceState>();
                            var copiedChildren = new System.Collections.Generic.List<Opc.Ua.BaseInstanceState>();
                            source.GetChildren(context, sourceChildren);
                            copy.GetChildren(context, copiedChildren);
                            if (sourceChildren.Count != copiedChildren.Count)
                            {
                                return "Child count changed";
                            }
                            foreach (var child in sourceChildren)
                            {
                                var copiedChild = copy.FindChild(context, child.BrowseName);
                                if (copiedChild == null || ReferenceEquals(child, copiedChild) ||
                                    !ReferenceEquals(copiedChild.Parent, copy) || !ReferenceEquals(child.Parent, source))
                                {
                                    return "Copy ownership changed at " + child.BrowseName;
                                }
                                string error = CheckChildren(context, child, copiedChild);
                                if (error.Length != 0)
                                {
                                    return error;
                                }
                                var originalName = child.DisplayName;
                                copiedChild.DisplayName = new Opc.Ua.LocalizedText("Changed copy");
                                if (child.DisplayName != originalName)
                                {
                                    return "Mutating copy changed source";
                                }
                            }
                            return "";
                        }
                    }
                    """, new CSharpParseOptions(LanguageVersion.CSharp11)));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new ModelSourceGenerator())
                .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.CSharp11))
                .AddAdditionalTexts(
                [
                    EmbeddedText.Create("OptionalBase/Model.xml", OptionalBaseDesign),
                    EmbeddedText.Create("Specialized/Model.xml", SpecializedDesign)
                ])
                .WithUpdatedAnalyzerConfigOptions(new AnalyzerOptionsProvider(new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true",
                    ["build_property.ModelSourceGeneratorOmitEventRecords"] = "true"
                }));
            driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);
            Assert.That(diagnostics, Is.Empty, string.Join("\n", diagnostics));
            using var stream = new MemoryStream();
            var emitted = outputCompilation.Emit(stream);
            Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
            Assembly assembly = Assembly.Load(stream.ToArray());
            string result = (string)assembly.GetType("InitializationProbe").GetMethod("Run")
                .Invoke(null, [prepareOptional, operation]);
            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Both designs are AdditionalFiles of the same compilation (the
        /// minimal repro of #4353) in both orderings. The generated
        /// instance factories reference the typed state classes emitted
        /// for the dependency model, so the combined output must compile.
        /// </summary>
        [TestCase(true, "Label")]
        [TestCase(false, "Label")]
        [TestCase(true, "CloneChild")]
        [TestCase(true, "NeedsOptionalInitialization")]
        public void InstanceOfObjectTypeAcrossModelDesignAdditionalFiles(bool instanceModelFirst, string childName)
        {
            var generator = new ModelSourceGenerator();

            CSharpCompilation compilation = OptimizationLevel.Release.CreateCompilation()
                .AddCode(
                    new Dictionary<string, string>().WithOpcUaGeneratedStack(),
                    LanguageVersion.CSharp11);

            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true",
                    ["build_property.ModelSourceGeneratorOmitEventRecords"] = "true"
                });

            AdditionalText modelA = EmbeddedText.Create(
                "A/ModelA.xml", ModelADesign.Replace("Label", childName, StringComparison.Ordinal));
            AdditionalText modelB = EmbeddedText.Create(
                "B/ModelB.xml", ModelBDesign.Replace("Label", childName, StringComparison.Ordinal));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(LanguageVersion.CSharp11))
                .AddAdditionalTexts(
                    instanceModelFirst ? [modelB, modelA] : [modelA, modelB])
                .WithUpdatedAnalyzerConfigOptions(options);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);

            Assert.That(
                diagnostics,
                Is.Empty,
                string.Join("\n", diagnostics.Select(d => d.ToString())));

            string generated = string.Join(
                "\n",
                driver.GetRunResult().Results[0].GeneratedSources
                    .Select(s => s.SourceText.ToString()));
            Assert.That(generated, Does.Contain("CreateWidget1("),
                "The instance factory must be emitted.");
            Assert.That(generated, Does.Contain($"CreateWidget1_{childName}("),
                "The factory for the property inherited from the " +
                "dependency type must be emitted.");
            Assert.That(generated, Does.Contain($"CloneChild({childName}, state)"));
            Assert.That(generated, Does.Contain("global::Test.ModelA.WidgetState"),
                "The instance must use the typed state class generated " +
                "for the dependency ObjectType.");

            outputCompilation.GetDiagnostics().Check(
                TestContext.Out,
                out int errors,
                out _);
            Assert.That(errors, Is.Zero, $"Compilation produced {errors} errors");
        }

        /// <summary>
        /// The upstream model is available only through the generated
        /// ModelDependencyV1 metadata on a referenced assembly. Variable
        /// metadata and defaults must be identical to the AdditionalFiles path.
        /// </summary>
        [Test]
        public void InstanceMetadataAcrossReferencedAssemblyPayload()
        {
            var generator = new ModelSourceGenerator();
            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true",
                    ["build_property.ModelSourceGeneratorOmitEventRecords"] = "true"
                });
            var parseOptions = new CSharpParseOptions()
                .WithKind(SourceCodeKind.Regular)
                .WithLanguageVersion(LanguageVersion.CSharp11);

            CSharpCompilation producer = OptimizationLevel.Release
                .CreateCompilation("ModelAProducer")
                .AddCode(
                    new Dictionary<string, string>().WithOpcUaGeneratedStack(),
                    LanguageVersion.CSharp11);
            GeneratorDriver producerDriver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(parseOptions)
                .AddAdditionalTexts([EmbeddedText.Create("A/ModelA.xml", ModelADesign)])
                .WithUpdatedAnalyzerConfigOptions(options);

            producerDriver.RunGeneratorsAndUpdateCompilation(
                producer,
                out Compilation producerOutput,
                out ImmutableArray<Diagnostic> producerDiagnostics);

            Assert.That(
                producerDiagnostics,
                Is.Empty,
                string.Join("\n", producerDiagnostics.Select(d => d.ToString())));

            CSharpCompilation consumer = OptimizationLevel.Release
                .CreateCompilation("ModelBConsumer")
                .AddCode(
                    new Dictionary<string, string>().WithOpcUaGeneratedStack(),
                    LanguageVersion.CSharp11)
                .AddReferences(((CSharpCompilation)producerOutput).ToMetadataReference());
            GeneratorDriver consumerDriver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(parseOptions)
                .AddAdditionalTexts([EmbeddedText.Create("B/ModelB.xml", ModelBDesign)])
                .WithUpdatedAnalyzerConfigOptions(options);

            consumerDriver = consumerDriver.RunGeneratorsAndUpdateCompilation(
                consumer,
                out _,
                out ImmutableArray<Diagnostic> consumerDiagnostics);

            Assert.That(
                consumerDiagnostics,
                Is.Empty,
                string.Join("\n", consumerDiagnostics.Select(d => d.ToString())));

            string generated = string.Join(
                "\n",
                consumerDriver.GetRunResult().Results[0].GeneratedSources
                    .Select(s => s.SourceText.ToString()));
            Assert.Multiple(() =>
            {
                Assert.That(generated, Does.Contain("CreateWidget1_Label("));
                Assert.That(generated, Does.Contain("CreateWidget1_OwnedLabels("));
                Assert.That(
                    generated,
                    Does.Contain(
                        "state.AccessLevel = global::Opc.Ua.AccessLevels.CurrentReadOrWrite;"));
                Assert.That(
                    generated,
                    Does.Contain(
                        "state.UserAccessLevel = global::Opc.Ua.AccessLevels.CurrentReadOrWrite;"));
                Assert.That(
                    generated,
                    Does.Contain("state.MinimumSamplingInterval = 250;"));
                Assert.That(generated, Does.Contain("state.Historizing = true;"));
                Assert.That(generated, Does.Contain("Unlabeled"));
                Assert.That(generated, Does.Contain("First"));
                Assert.That(generated, Does.Contain("Second"));
            });
        }

        private const string OptionalBaseDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
                xmlns:ua="http://opcfoundation.org/UA/" xmlns="http://test.org/UA/OptionalBase/"
                TargetNamespace="http://test.org/UA/OptionalBase/">
                <opc:Namespaces>
                    <opc:Namespace Name="OpcUa" Prefix="Opc.Ua"
                        XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd"
                        >http://opcfoundation.org/UA/</opc:Namespace>
                    <opc:Namespace Name="OptionalBase" Prefix="Test.OptionalBase"
                        >http://test.org/UA/OptionalBase/</opc:Namespace>
                </opc:Namespaces>
                <opc:ObjectType SymbolicName="OptionalBaseType" BaseType="ua:BaseObjectType">
                    <opc:Children>
                        <opc:Object SymbolicName="ParameterSet" TypeDefinition="ua:BaseObjectType"
                            ModellingRule="Optional" />
                        <opc:Property SymbolicName="Label" DataType="ua:UriString" ValueRank="Scalar"
                            ModellingRule="Optional" />
                    </opc:Children>
                </opc:ObjectType>
            </opc:ModelDesign>
            """;

        private const string SpecializedDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
                xmlns:ua="http://opcfoundation.org/UA/" xmlns:s0="http://test.org/UA/OptionalBase/"
                xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd"
                xmlns="http://test.org/UA/Specialized/" TargetNamespace="http://test.org/UA/Specialized/">
                <opc:Namespaces>
                    <opc:Namespace Name="Specialized" Prefix="Test.Specialized"
                        >http://test.org/UA/Specialized/</opc:Namespace>
                    <opc:Namespace Name="OptionalBase" Prefix="Test.OptionalBase"
                        >http://test.org/UA/OptionalBase/</opc:Namespace>
                    <opc:Namespace Name="OpcUa" Prefix="Opc.Ua"
                        XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd"
                        >http://opcfoundation.org/UA/</opc:Namespace>
                </opc:Namespaces>
                <opc:ObjectType SymbolicName="SpecializedType" BaseType="s0:OptionalBaseType">
                    <opc:Children>
                        <opc:Object SymbolicName="s0:ParameterSet" TypeDefinition="ua:BaseObjectType"
                            ModellingRule="Mandatory">
                            <opc:Children>
                                <opc:Variable SymbolicName="Temperature" DataType="ua:Float" ValueRank="Scalar"
                                    ModellingRule="Mandatory">
                                    <opc:DefaultValue><uax:Float>20</uax:Float></opc:DefaultValue>
                                </opc:Variable>
                            </opc:Children>
                        </opc:Object>
                    </opc:Children>
                </opc:ObjectType>
            </opc:ModelDesign>
            """;

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
                </opc:Children>
              </opc:ObjectType>
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
    }
}
