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
    /// Retains explicit wire method declarations independently of generated method state class identities.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    public sealed class InheritedMethodDeclarationTests
    {
        /// <summary>
        /// Explicit external and core declarations survive file and assembly-payload dependency paths.
        /// </summary>
        [TestCase(false, "Results_Read", 7001u, 7008u, "http://test.org/UA/MethodBase/")]
        [TestCase(true, "Results_Read", 7001u, 7008u, "http://test.org/UA/MethodBase/")]
        [TestCase(false, "Transfer_GenerateFileForWrite", 7002u, 15749u, "http://opcfoundation.org/UA/")]
        [TestCase(true, "Transfer_GenerateFileForWrite", 7002u, 15749u, "http://opcfoundation.org/UA/")]
        public void ExplicitInheritedDeclarationSurvivesDependencyImport(
            bool usePayload,
            string methodPath,
            uint methodId,
            uint declarationId,
            string declarationNamespace)
        {
            using var files = new VirtualFileSystem();
            string basePath = Path.Combine(Directory.GetCurrentDirectory(), "MethodBase.NodeSet2.xml");
            string consumerPath = Path.Combine(Directory.GetCurrentDirectory(), "MethodConsumer.NodeSet2.xml");
            files.Add(basePath, Encoding.UTF8.GetBytes(kBaseNodeSet));
            files.Add(consumerPath, Encoding.UTF8.GetBytes(kConsumerNodeSet));
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            IFileSystem source = typeof(Generators).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design").WithFallback(files);
            var dependencies = new Dictionary<string, ModelDependencyV1>();
            if (usePayload)
            {
                IModelDesign producer = source.OpenModelDesign(
                    new DesignFileCollection { Targets = [basePath] }, [], telemetry, useAllowSubtypes: true);
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
                    path => path.EndsWith(".ModelDependencies.g.cs", StringComparison.Ordinal))));
                AttributeSyntax attribute = CSharpSyntaxTree.ParseText(payloadCode).GetRoot()
                    .DescendantNodes().OfType<AttributeSyntax>().Single();
                var literal = (LiteralExpressionSyntax)attribute.ArgumentList!.Arguments.Last().Expression;
                ModelDependencyV1? payload = ModelDependencyV1.FromBase64Payload(literal.Token.ValueText);
                Assert.That(payload, Is.Not.Null);
                dependencies.Add("http://test.org/UA/MethodBase/", payload!);
            }
            IModelDesign model = source.OpenModelDesign(
                new DesignFileCollection
                {
                    Targets = [consumerPath],
                    Dependencies = usePayload ? [] : [basePath]
                },
                [], telemetry, useAllowSubtypes: true, referencedDependencies: dependencies);
            ObjectTypeDesign consumer = model.Nodes.OfType<ObjectTypeDesign>()
                .Single(type => type.SymbolicName.Name == "ConsumerType");
            var method = (MethodDesign)consumer.Hierarchy.Nodes[methodPath].Instance!;
            var generator = new NodeStateGenerator(new GeneratorContext
            {
                FileSystem = files,
                ModelDesign = model,
                OutputFolder = string.Empty,
                Telemetry = telemetry,
                Options = new GeneratorOptions { OmitFluentApi = true }
            });
            generator.Emit();
            string code = Encoding.UTF8.GetString(files.Get(files.CreatedFiles.Single(
                path => path.EndsWith(".NodeStates.ex.g.cs", StringComparison.Ordinal))));
            MethodDeclarationSyntax factory = CSharpSyntaxTree.ParseText(code).GetRoot()
                .DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(candidate => candidate.Identifier.ValueText == "CreateConsumerType_" + methodPath);
            AssignmentExpressionSyntax declaration = factory.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                .Single(assignment => assignment.Left.ToString() == "baseState.MethodDeclarationId");
            Assert.Multiple(() =>
            {
                Assert.That(method.NumericId, Is.EqualTo(methodId));
                Assert.That(method.MethodDeclarationNode, Is.Not.Null);
                Assert.That(method.MethodDeclarationNode!.FindNumericIdentifier(), Is.EqualTo(declarationId));
                Assert.That(method.MethodDeclarationNode!.SymbolicId.Namespace, Is.EqualTo(declarationNamespace));
                Assert.That(declaration.Right.ToString(), Does.StartWith(
                    $"global::Opc.Ua.NodeId.Create({declarationId}u,"));
            });
        }

        private const string kBaseNodeSet =
            """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
              xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
              <NamespaceUris><Uri>http://test.org/UA/MethodBase/</Uri></NamespaceUris>
              <Models>
                <Model ModelUri="http://test.org/UA/MethodBase/" Version="1.0.0"
                  PublicationDate="2024-01-01T00:00:00Z">
                  <RequiredModel ModelUri="http://opcfoundation.org/UA/" Version="1.05.02"
                    PublicationDate="2022-11-01T00:00:00Z" />
                </Model>
              </Models>
              <UAObjectType NodeId="ns=1;i=1004" BrowseName="1:ResultManagementType">
                <DisplayName>ResultManagementType</DisplayName>
                <References>
                  <Reference ReferenceType="i=45" IsForward="false">i=58</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=7008</Reference>
                </References>
              </UAObjectType>
              <UAMethod NodeId="ns=1;i=7008" BrowseName="1:Read" ParentNodeId="ns=1;i=1004">
                <DisplayName>Read</DisplayName>
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1004</Reference>
                  <Reference ReferenceType="i=37">i=80</Reference>
                  <Reference ReferenceType="i=46">ns=1;i=6001</Reference>
                </References>
              </UAMethod>
              <UAVariable NodeId="ns=1;i=6001" BrowseName="InputArguments" ParentNodeId="ns=1;i=7008"
                DataType="i=296" ValueRank="1">
                <DisplayName>InputArguments</DisplayName>
                <References>
                  <Reference ReferenceType="i=46" IsForward="false">ns=1;i=7008</Reference>
                  <Reference ReferenceType="i=40">i=68</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                </References>
                <Value><uax:ListOfExtensionObject><uax:ExtensionObject>
                  <uax:TypeId><uax:Identifier>i=297</uax:Identifier></uax:TypeId>
                  <uax:Body><uax:Argument>
                    <uax:Name>Timeout</uax:Name>
                    <uax:DataType><uax:Identifier>i=6</uax:Identifier></uax:DataType>
                    <uax:ValueRank>-1</uax:ValueRank>
                    <uax:ArrayDimensions />
                  </uax:Argument></uax:Body>
                </uax:ExtensionObject></uax:ListOfExtensionObject></Value>
              </UAVariable>
            </UANodeSet>
            """;

        private const string kConsumerNodeSet =
            """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris>
                <Uri>http://test.org/UA/MethodConsumer/</Uri>
                <Uri>http://test.org/UA/MethodBase/</Uri>
              </NamespaceUris>
              <Models>
                <Model ModelUri="http://test.org/UA/MethodConsumer/" Version="1.0.0"
                  PublicationDate="2024-01-01T00:00:00Z">
                  <RequiredModel ModelUri="http://opcfoundation.org/UA/" Version="1.05.02"
                    PublicationDate="2022-11-01T00:00:00Z" />
                  <RequiredModel ModelUri="http://test.org/UA/MethodBase/" Version="1.0.0"
                    PublicationDate="2024-01-01T00:00:00Z" />
                </Model>
              </Models>
              <UAObjectType NodeId="ns=1;i=1002" BrowseName="1:ConsumerType">
                <DisplayName>ConsumerType</DisplayName>
                <References>
                  <Reference ReferenceType="i=45" IsForward="false">i=58</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=5019</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=5024</Reference>
                </References>
              </UAObjectType>
              <UAObject NodeId="ns=1;i=5019" BrowseName="1:Results" ParentNodeId="ns=1;i=1002">
                <DisplayName>Results</DisplayName>
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1002</Reference>
                  <Reference ReferenceType="i=40">ns=2;i=1004</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=7001</Reference>
                </References>
              </UAObject>
              <UAMethod NodeId="ns=1;i=7001" BrowseName="2:Read" ParentNodeId="ns=1;i=5019"
                MethodDeclarationId="ns=2;i=7008">
                <DisplayName>Read</DisplayName>
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=5019</Reference>
                  <Reference ReferenceType="i=37">i=80</Reference>
                </References>
              </UAMethod>
              <UAObject NodeId="ns=1;i=5024" BrowseName="1:Transfer" ParentNodeId="ns=1;i=1002">
                <DisplayName>Transfer</DisplayName>
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1002</Reference>
                  <Reference ReferenceType="i=40">i=15744</Reference>
                  <Reference ReferenceType="i=37">i=80</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=7002</Reference>
                </References>
              </UAObject>
              <UAMethod NodeId="ns=1;i=7002" BrowseName="GenerateFileForWrite" ParentNodeId="ns=1;i=5024"
                MethodDeclarationId="i=15749">
                <DisplayName>GenerateFileForWrite</DisplayName>
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=5024</Reference>
                  <Reference ReferenceType="i=37">i=80</Reference>
                </References>
              </UAMethod>
            </UANodeSet>
            """;
    }
}
