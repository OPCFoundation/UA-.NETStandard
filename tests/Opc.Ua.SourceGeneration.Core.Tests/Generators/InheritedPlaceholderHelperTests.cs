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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Covers CLR helper shadowing for specialized placeholders across different wire namespaces.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    public sealed class InheritedPlaceholderHelperTests
    {
        /// <summary>
        /// Identical CLR helper names hide inherited helpers even when the placeholder's browse namespace changes.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void SpecializedPlaceholderHelperCompilesWithoutHidingWarnings(
            bool retainBrowseNamespace,
            bool indirectBase)
        {
            using var files = new VirtualFileSystem();
            string basePath = Path.Combine(Directory.GetCurrentDirectory(), "PlaceholderBase.ModelDesign.xml");
            string derivedPath = Path.Combine(Directory.GetCurrentDirectory(), "PlaceholderDerived.ModelDesign.xml");
            files.Add(basePath, Encoding.UTF8.GetBytes(kBaseDesign));
            files.Add(derivedPath, Encoding.UTF8.GetBytes(kDerivedDesign
                .Replace("BASE_TYPE", indirectBase ? "IntermediateType" : "MultiToolType", StringComparison.Ordinal)
                .Replace("TOOL_NAME", retainBrowseNamespace ? "b:Tool_Placeholder" : "Tool_Placeholder",
                    StringComparison.Ordinal)));
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            var options = new GeneratorOptions { OmitFluentApi = true };
            Generators.GenerateCode(
                new DesignFileCollection { Targets = [basePath] },
                files, string.Empty, telemetry, options);
            Generators.GenerateCode(
                new DesignFileCollection { Targets = [derivedPath], Dependencies = [basePath] },
                files, string.Empty, telemetry, options);
            Dictionary<string, string> sources = files.CreatedFiles
                .Where(path => path.EndsWith(".g.cs", StringComparison.Ordinal))
                .ToDictionary(path => path, path => Encoding.UTF8.GetString(files.Get(path)));
            ClassDeclarationSyntax derived = sources.Values
                .SelectMany(code => CSharpSyntaxTree.ParseText(code).GetRoot()
                    .DescendantNodes().OfType<ClassDeclarationSyntax>())
                .Single(type => type.Identifier.ValueText == "MultiSensorState");
            MethodDeclarationSyntax specialized = derived.Members.OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "AddTool_Placeholder");
            MethodDeclarationSyntax newSlot = derived.Members.OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "AddAccessory_Placeholder");
            CSharpCompilation compilation = OptimizationLevel.Release.CreateCompilation()
                .AddCode(sources.WithOpcUaGeneratedStack(), LanguageVersion.Latest);
            Diagnostic[] errors =
            [
                .. compilation.GetDiagnostics().Where(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error || diagnostic.Id is "CS0108" or "CS0109")
            ];
            Assert.Multiple(() =>
            {
                Assert.That(specialized.Modifiers.Any(SyntaxKind.NewKeyword), Is.True);
                Assert.That(newSlot.Modifiers.Any(SyntaxKind.NewKeyword), Is.False);
                Assert.That(specialized.ReturnType.ToString(),
                    Is.EqualTo("global::Test.PlaceholderDerived.SensorState"));
                Assert.That(errors.Select(diagnostic => diagnostic.ToString()), Is.Empty);
            });
        }

        private const string kBaseDesign =
            """
            <opc:ModelDesign xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/" xmlns="http://test.org/UA/PlaceholderBase/"
              TargetNamespace="http://test.org/UA/PlaceholderBase/">
              <opc:Namespaces>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua">http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="PlaceholderBase"
                  Prefix="Test.PlaceholderBase">http://test.org/UA/PlaceholderBase/</opc:Namespace>
              </opc:Namespaces>
              <opc:ObjectType SymbolicName="ToolType" BaseType="ua:BaseObjectType" />
              <opc:ObjectType SymbolicName="MultiToolType" BaseType="ua:BaseObjectType">
                <opc:Children>
                  <opc:Object SymbolicName="Tool_Placeholder" TypeDefinition="ToolType"
                    ModellingRule="OptionalPlaceholder" />
                </opc:Children>
              </opc:ObjectType>
              <opc:ObjectType SymbolicName="IntermediateType" BaseType="MultiToolType" />
            </opc:ModelDesign>
            """;

        private const string kDerivedDesign =
            """
            <opc:ModelDesign xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/" xmlns:b="http://test.org/UA/PlaceholderBase/"
              xmlns="http://test.org/UA/PlaceholderDerived/"
              TargetNamespace="http://test.org/UA/PlaceholderDerived/">
              <opc:Namespaces>
                <opc:Namespace Name="PlaceholderDerived"
                  Prefix="Test.PlaceholderDerived">http://test.org/UA/PlaceholderDerived/</opc:Namespace>
                <opc:Namespace Name="PlaceholderBase"
                  Prefix="Test.PlaceholderBase">http://test.org/UA/PlaceholderBase/</opc:Namespace>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua">http://opcfoundation.org/UA/</opc:Namespace>
              </opc:Namespaces>
              <opc:ObjectType SymbolicName="SensorType" BaseType="b:ToolType">
                <opc:Children>
                  <opc:Property SymbolicName="IsQualified" DataType="ua:Boolean" ModellingRule="Mandatory" />
                </opc:Children>
              </opc:ObjectType>
              <opc:ObjectType SymbolicName="MultiSensorType" BaseType="b:BASE_TYPE">
                <opc:Children>
                  <opc:Object SymbolicName="TOOL_NAME" TypeDefinition="SensorType"
                    ModellingRule="OptionalPlaceholder" />
                  <opc:Object SymbolicName="Accessory_Placeholder" TypeDefinition="ua:BaseObjectType"
                    ModellingRule="OptionalPlaceholder" />
                </opc:Children>
              </opc:ObjectType>
            </opc:ModelDesign>
            """;
    }
}
