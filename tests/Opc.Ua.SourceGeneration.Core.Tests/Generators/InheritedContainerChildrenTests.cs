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
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Schema.Model;
using Opc.Ua.SourceGeneration.Dependency;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Preserves inherited mandatory children of generic containers such as DI ParameterSet.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class InheritedContainerChildrenTests
    {
        /// <summary>
        /// File and payload dependencies retain required descendants of inherited or redeclared generic containers.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public void InheritedMandatoryDescendantsAreEmitted(
            bool redeclareContainer,
            bool nested,
            bool usePayload)
        {
            Dictionary<string, string> generated = Generate(
                redeclareContainer, nested, out ObjectTypeDesign derived, usePayload);
            string containerPath = nested ? "ParameterSet_Group" : "ParameterSet";
            string readingPath = containerPath + "_Reading";
            Assert.That(derived.Hierarchy.Nodes.ContainsKey(readingPath), Is.True,
                "The dependency must carry mandatory descendants into the consumer hierarchy before emission.");
            HierarchyNode inherited = derived.Hierarchy.Nodes[readingPath];
            HierarchyNode adHocChild = derived.Hierarchy.Nodes[nested ? containerPath : readingPath];

            Assert.Multiple(() =>
            {
                Assert.That(inherited.Inherited, Is.True);
                if (!usePayload)
                {
                    Assert.That(adHocChild.AdHocInstance, Is.True);
                }
                Assert.That(inherited.ExplicitlyDefined, Is.False);
                Assert.That(inherited.Instance, Is.TypeOf<VariableDesign>());
                Assert.That(((VariableDesign)inherited.Instance!).ModellingRule, Is.EqualTo(ModellingRule.Mandatory));
                Assert.That(inherited.Instance!.SymbolicName.Namespace, Is.EqualTo(kBaseNamespace));
            });

            string code = generated.Single(
                file => file.Key.EndsWith(".NodeStates.ex.g.cs", StringComparison.Ordinal)).Value;
            string readingFactory = "CreateDerivedType_" + readingPath;
            MethodDeclarationSyntax container = FindMethod(code, "CreateDerivedType_" + containerPath);
            string unconditionalStatements = string.Join("\n",
                container.Body!.Statements.OfType<ExpressionStatementSyntax>()
                    .Select(statement => statement.ToString()));
            Assert.That(unconditionalStatements, Does.Contain(readingFactory + "(context, state,"),
                "The required reading must be initialized for instances, not only inside the type-only branch.");

            MethodDeclarationSyntax reading = FindMethod(code, readingFactory);
            Assert.Multiple(() =>
            {
                Assert.That(reading.ReturnType.ToString(),
                    Is.EqualTo("global::Opc.Ua.BaseDataVariableState<double>"));
                Assert.That(reading.ToString(), Does.Contain(
                    "GetIndexOrAppend(global::Test.ContainerBase.Namespaces.ContainerBase)"));
                Assert.That(generated.Single(
                    file => file.Key.EndsWith(".NodeStates.g.cs", StringComparison.Ordinal)).Value,
                    Does.Contain("global::Test.ContainerBase.BaseState"));
            });

            if (nested)
            {
                MethodDeclarationSyntax parameterSet = FindMethod(code, "CreateDerivedType_ParameterSet");
                Assert.That(
                    parameterSet.Body!.Statements.OfType<ExpressionStatementSyntax>()
                        .Select(statement => statement.ToString()),
                    Has.Some.Contains("CreateDerivedType_ParameterSet_Group(context, state,"));
            }
        }

        /// <summary>
        /// Retaining mandatory descendants does not introduce unrequested optional or unmodelled children.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void InheritedOptionalAndUnmodelledChildrenRemainAbsent(bool redeclareContainer)
        {
            Dictionary<string, string> generated = Generate(
                redeclareContainer, false, out ObjectTypeDesign derived);
            var optional = (VariableDesign)derived.Hierarchy.Nodes["ParameterSet_OptionalReading"].Instance!;
            Assert.That(optional.ModellingRule, Is.EqualTo(ModellingRule.Optional));

            string code = generated.Single(
                file => file.Key.EndsWith(".NodeStates.ex.g.cs", StringComparison.Ordinal)).Value;
            MethodDeclarationSyntax container = FindMethod(code, "CreateDerivedType_ParameterSet");
            Assert.Multiple(() =>
            {
                Assert.That(container.ToString(), Does.Not.Contain("OptionalReading"));
                Assert.That(container.ToString(), Does.Not.Contain("ImplementationDetail"));
                Assert.That(
                    container.ToString().Contains("LocalFlag", StringComparison.Ordinal),
                    Is.EqualTo(redeclareContainer));
            });
        }

        /// <summary>
        /// Legacy metadata without descendants still resolves its direct members and consumer redeclarations.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LegacyPayloadWithoutDescendantsRetainsExistingConsumerBehavior(bool redeclareContainer)
        {
            Dictionary<string, string> generated = Generate(
                redeclareContainer, false, out ObjectTypeDesign derived, usePayload: true, legacyPayload: true);
            Assert.That(derived.Hierarchy.Nodes.ContainsKey("ParameterSet"), Is.True);
            Assert.That(derived.Hierarchy.Nodes.ContainsKey("ParameterSet_Reading"), Is.False);
            string code = generated.Single(
                file => file.Key.EndsWith(".NodeStates.ex.g.cs", StringComparison.Ordinal)).Value;
            MethodDeclarationSyntax container = FindMethod(code, "CreateDerivedType_ParameterSet");
            Assert.That(container.ToString().Contains("LocalFlag", StringComparison.Ordinal),
                Is.EqualTo(redeclareContainer));
        }

        private static Dictionary<string, string> Generate(
            bool redeclareContainer,
            bool nested,
            out ObjectTypeDesign derived,
            bool usePayload = false,
            bool legacyPayload = false)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var files = new VirtualFileSystem();
            string basePath = Path.Combine(Directory.GetCurrentDirectory(), "ContainerBase.ModelDesign.xml");
            string derivedPath = Path.Combine(Directory.GetCurrentDirectory(), "ContainerDerived.ModelDesign.xml");
            string reading =
                """
                <opc:Variable SymbolicName="Reading" TypeDefinition="ua:BaseDataVariableType"
                  DataType="ua:Double" ValueRank="Scalar" ModellingRule="Mandatory" />
                """;
            if (nested)
            {
                reading =
                    """
                    <opc:Object SymbolicName="Group" TypeDefinition="ua:BaseObjectType" ModellingRule="Mandatory">
                      <opc:Children>
                    """ + reading +
                    """
                      </opc:Children>
                    </opc:Object>
                    """;
            }
            files.Add(basePath, Encoding.UTF8.GetBytes(
                kBaseDesign.Replace("<!-- Reading -->", reading, StringComparison.Ordinal)));
            files.Add(derivedPath, Encoding.UTF8.GetBytes(
                kDerivedDesign.Replace("<!-- Children -->", redeclareContainer ? kRedeclaration : string.Empty,
                    StringComparison.Ordinal)));
            IFileSystem source = typeof(Generators).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design")
                .WithFallback(files);
            var dependencies = new Dictionary<string, ModelDependencyV1>();
            if (usePayload)
            {
                IModelDesign producer = source.OpenModelDesign(
                    new DesignFileCollection
                    {
                        Targets = [basePath],
                        Options = new DesignFileOptions()
                    },
                    [],
                    telemetry,
                    useAllowSubtypes: false);
                var dependencyGenerator = new ModelDependencyGenerator(new GeneratorContext
                {
                    FileSystem = files,
                    ModelDesign = producer,
                    OutputFolder = string.Empty,
                    Telemetry = telemetry,
                    Options = new GeneratorOptions { OmitFluentApi = true }
                });
                dependencyGenerator.Emit();
                string payloadCode = Encoding.UTF8.GetString(files.Get(files.CreatedFiles.Single(
                    file => file.EndsWith(".ModelDependencies.g.cs", StringComparison.Ordinal))));
                AttributeSyntax attribute = CSharpSyntaxTree.ParseText(payloadCode).GetRoot()
                    .DescendantNodes().OfType<AttributeSyntax>().Single();
                var literal = (LiteralExpressionSyntax)attribute.ArgumentList!.Arguments.Last().Expression;
                ModelDependencyV1? payload = ModelDependencyV1.FromBase64Payload(literal.Token.ValueText);
                Assert.That(payload, Is.Not.Null);
                if (legacyPayload)
                {
                    foreach (DependencyNode node in payload!.Nodes)
                    {
                        foreach (DependencyChild child in node.Children)
                        {
                            child.Children = [];
                        }
                    }
                    payload = ModelDependencyV1.FromBase64Payload(payload.ToBase64Payload());
                }
                dependencies.Add(kBaseNamespace, payload!);
            }
            IModelDesign model = source.OpenModelDesign(
                new DesignFileCollection
                {
                    Targets = [derivedPath],
                    Dependencies = usePayload ? [] : [basePath],
                    Options = new DesignFileOptions()
                },
                [],
                telemetry,
                useAllowSubtypes: false,
                referencedDependencies: dependencies);
            derived = model.Nodes.OfType<ObjectTypeDesign>()
                .Single(type => type.SymbolicName.Name == "DerivedType");

            var generator = new NodeStateGenerator(new GeneratorContext
            {
                FileSystem = files,
                ModelDesign = model,
                OutputFolder = string.Empty,
                Telemetry = telemetry,
                Options = new GeneratorOptions { OmitFluentApi = true }
            });
            generator.Emit();
            return files.CreatedFiles
                .Where(file => file.EndsWith(".g.cs", StringComparison.Ordinal))
                .ToDictionary(file => file, file => Encoding.UTF8.GetString(files.Get(file)));
        }

        private static MethodDeclarationSyntax FindMethod(string code, string name)
        {
            MethodDeclarationSyntax? method = CSharpSyntaxTree.ParseText(code).GetRoot()
                .DescendantNodes().OfType<MethodDeclarationSyntax>()
                .SingleOrDefault(candidate => candidate.Identifier.ValueText == name);
            Assert.That(method, Is.Not.Null, $"No generated factory {name}.");
            return method!;
        }

        private const string kBaseNamespace = "http://test.org/UA/ContainerBase/";
        private const string kBaseDesign =
            """
            <opc:ModelDesign xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns="http://test.org/UA/ContainerBase/"
              TargetNamespace="http://test.org/UA/ContainerBase/">
              <opc:Namespaces>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua">http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="ContainerBase"
                  Prefix="Test.ContainerBase">http://test.org/UA/ContainerBase/</opc:Namespace>
              </opc:Namespaces>
              <opc:ObjectType SymbolicName="BaseType" BaseType="ua:BaseObjectType">
                <opc:Children>
                  <opc:Object SymbolicName="ParameterSet" TypeDefinition="ua:BaseObjectType" ModellingRule="Mandatory">
                    <opc:Children>
                      <!-- Reading -->
                      <opc:Variable SymbolicName="OptionalReading" TypeDefinition="ua:BaseDataVariableType"
                        DataType="ua:Double" ValueRank="Scalar" ModellingRule="Optional" />
                      <opc:Variable SymbolicName="ImplementationDetail" TypeDefinition="ua:BaseDataVariableType"
                        DataType="ua:String" ValueRank="Scalar" ModellingRule="None" />
                    </opc:Children>
                  </opc:Object>
                </opc:Children>
              </opc:ObjectType>
            </opc:ModelDesign>
            """;

        private const string kDerivedDesign =
            """
            <opc:ModelDesign xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns:b="http://test.org/UA/ContainerBase/"
              xmlns="http://test.org/UA/ContainerDerived/"
              TargetNamespace="http://test.org/UA/ContainerDerived/">
              <opc:Namespaces>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua">http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="ContainerBase"
                  Prefix="Test.ContainerBase">http://test.org/UA/ContainerBase/</opc:Namespace>
                <opc:Namespace Name="ContainerDerived"
                  Prefix="Test.ContainerDerived">http://test.org/UA/ContainerDerived/</opc:Namespace>
              </opc:Namespaces>
              <opc:ObjectType SymbolicName="DerivedType" BaseType="b:BaseType">
                <!-- Children -->
              </opc:ObjectType>
            </opc:ModelDesign>
            """;

        private const string kRedeclaration =
            """
            <opc:Children>
              <opc:Object SymbolicName="b:ParameterSet" TypeDefinition="ua:BaseObjectType" ModellingRule="Mandatory">
                <opc:Children>
                  <opc:Variable SymbolicName="LocalFlag" TypeDefinition="ua:BaseDataVariableType"
                    DataType="ua:Boolean" ValueRank="Scalar" ModellingRule="Mandatory" />
                </opc:Children>
              </opc:Object>
            </opc:Children>
            """;
    }
}
