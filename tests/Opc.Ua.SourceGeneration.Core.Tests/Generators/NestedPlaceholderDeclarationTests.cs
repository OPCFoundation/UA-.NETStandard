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
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Retains explicitly declared nested placeholders without instantiating template nodes by default.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    public sealed class NestedPlaceholderDeclarationTests
    {
        /// <summary>
        /// A program placeholder inside a job placeholder keeps its explicit numeric identity and required properties.
        /// </summary>
        [TestCase("i=11510")]
        [TestCase("i=11508")]
        public void NestedPlaceholdersPreserveDeclaredIdsAndChildren(string modellingRule)
        {
            using var files = new VirtualFileSystem();
            string path = Path.Combine(Directory.GetCurrentDirectory(), "NestedPlaceholders.NodeSet2.xml");
            files.Add(path, Encoding.UTF8.GetBytes(
                kNodeSet.Replace("PLACEHOLDER_RULE", modellingRule, StringComparison.Ordinal)));
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            var nodesets = new NodesetFileCollection(
                [(path, new NodesetFileOptions())], [], files, telemetry);
            nodesets.GenerateCode(
                files, string.Empty, telemetry, new GeneratorOptions { OmitFluentApi = true });
            string code = Encoding.UTF8.GetString(files.Get(files.CreatedFiles.Single(
                file => file.EndsWith(".NodeStates.ex.g.cs", StringComparison.Ordinal))));
            MethodDeclarationSyntax[] methods = CSharpSyntaxTree.ParseText(code).GetRoot()
                .DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
            MethodDeclarationSyntax container = FindDeclarationFactory(methods, 76);
            MethodDeclarationSyntax placeholder = FindDeclarationFactory(methods, 93);
            string placeholderName = placeholder.Identifier.ValueText;
            Assert.That(container.ToString(), Does.Contain(placeholderName + "(context, state,"),
                "A locally declared template must survive even when nested beneath another placeholder.");
            MethodDeclarationSyntax name = FindDeclarationFactory(methods, 214);
            MethodDeclarationSyntax number = FindDeclarationFactory(methods, 255);
            Assert.Multiple(() =>
            {
                Assert.That(placeholder.ToString(), Does.Contain(name.Identifier.ValueText + "(context, state,"));
                Assert.That(placeholder.ToString(), Does.Contain(number.Identifier.ValueText + "(context, state,"));
                Assert.That(methods.Single(
                    method => method.Identifier.ValueText == "CreateInstanceOfJobListType").ToString(),
                    Does.Not.Contain(FindDeclarationFactory(methods, 74).Identifier.ValueText + "(context,"));
            });
        }

        private static MethodDeclarationSyntax FindDeclarationFactory(
            MethodDeclarationSyntax[] methods,
            uint identifier)
        {
            string assignment = $"global::Opc.Ua.NodeId.Create({identifier}u,";
            MethodDeclarationSyntax? factory = methods.SingleOrDefault(method =>
                method.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(expression =>
                    expression.Left.ToString() == "nodeState.NodeId" &&
                    expression.Right.ToString().StartsWith(assignment, StringComparison.Ordinal)));
            Assert.That(factory, Is.Not.Null, $"No generated factory retains declared numeric NodeId {identifier}.");
            return factory!;
        }

        private const string kNodeSet =
            """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>http://test.org/UA/NestedPlaceholders/</Uri></NamespaceUris>
              <Models>
                <Model ModelUri="http://test.org/UA/NestedPlaceholders/" Version="1.0.0"
                  PublicationDate="2024-01-01T00:00:00Z">
                  <RequiredModel ModelUri="http://opcfoundation.org/UA/" Version="1.05.02"
                    PublicationDate="2022-11-01T00:00:00Z" />
                </Model>
              </Models>
              <UAObjectType NodeId="ns=1;i=59" BrowseName="1:ProgramType">
                <DisplayName>ProgramType</DisplayName>
                <References><Reference ReferenceType="i=45" IsForward="false">i=58</Reference></References>
              </UAObjectType>
              <UAObjectType NodeId="ns=1;i=30" BrowseName="1:JobListType">
                <DisplayName>JobListType</DisplayName>
                <References>
                  <Reference ReferenceType="i=45" IsForward="false">i=23518</Reference>
                  <Reference ReferenceType="i=49">ns=1;i=74</Reference>
                </References>
              </UAObjectType>
              <UAObject NodeId="ns=1;i=74" BrowseName="&lt;OrderedObject&gt;" SymbolicName="OrderedObject"
                ParentNodeId="ns=1;i=30">
                <DisplayName>&lt;OrderedObject&gt;</DisplayName>
                <References>
                  <Reference ReferenceType="i=49" IsForward="false">ns=1;i=30</Reference>
                  <Reference ReferenceType="i=40">i=58</Reference>
                  <Reference ReferenceType="i=37">i=11510</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=76</Reference>
                </References>
              </UAObject>
              <UAObject NodeId="ns=1;i=76" BrowseName="1:ProductionPrograms" ParentNodeId="ns=1;i=74">
                <DisplayName>ProductionPrograms</DisplayName>
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=74</Reference>
                  <Reference ReferenceType="i=40">i=23518</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                  <Reference ReferenceType="i=49">ns=1;i=93</Reference>
                </References>
              </UAObject>
              <UAObject NodeId="ns=1;i=93" BrowseName="&lt;OrderedObject&gt;" SymbolicName="OrderedObject"
                ParentNodeId="ns=1;i=76">
                <DisplayName>&lt;OrderedObject&gt;</DisplayName>
                <References>
                  <Reference ReferenceType="i=49" IsForward="false">ns=1;i=76</Reference>
                  <Reference ReferenceType="i=40">ns=1;i=59</Reference>
                  <Reference ReferenceType="i=37">PLACEHOLDER_RULE</Reference>
                  <Reference ReferenceType="i=46">ns=1;i=214</Reference>
                  <Reference ReferenceType="i=46">ns=1;i=255</Reference>
                </References>
              </UAObject>
              <UAVariable NodeId="ns=1;i=214" BrowseName="1:Name" ParentNodeId="ns=1;i=93" DataType="i=12">
                <DisplayName>Name</DisplayName>
                <References>
                  <Reference ReferenceType="i=46" IsForward="false">ns=1;i=93</Reference>
                  <Reference ReferenceType="i=40">i=68</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                </References>
              </UAVariable>
              <UAVariable NodeId="ns=1;i=255" BrowseName="NumberInList" ParentNodeId="ns=1;i=93" DataType="i=5">
                <DisplayName>NumberInList</DisplayName>
                <References>
                  <Reference ReferenceType="i=46" IsForward="false">ns=1;i=93</Reference>
                  <Reference ReferenceType="i=40">i=68</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                </References>
              </UAVariable>
            </UANodeSet>
            """;
    }
}
