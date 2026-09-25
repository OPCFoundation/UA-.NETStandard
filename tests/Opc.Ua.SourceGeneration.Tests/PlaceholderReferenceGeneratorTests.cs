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
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    [TestFixture]
    [Category("SourceGeneration")]
    public sealed class PlaceholderReferenceGeneratorTests
    {
        [TestCase("MandatoryPlaceholder")]
        [TestCase("OptionalPlaceholder")]
        public void PlaceholderReferencesAreRetainedOnlyForTheTypeTemplate(string rule)
        {
            CSharpCompilation compilation = OptimizationLevel.Release.CreateCompilation()
                .AddCode(new Dictionary<string, string>().WithOpcUaGeneratedStack(), LanguageVersion.CSharp11);
            var options = new AnalyzerOptionsProvider(new Dictionary<string, string>
            {
                ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true",
                ["build_property.ModelSourceGeneratorOmitEventRecords"] = "true"
            });
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new ModelSourceGenerator())
                .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.CSharp11))
                .AddAdditionalTexts([EmbeddedText.Create("Placeholders.xml", sModel.Replace(
                    "PLACEHOLDER_RULE", rule, StringComparison.Ordinal))])
                .WithUpdatedAnalyzerConfigOptions(options);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);

            Assert.That(diagnostics, Is.Empty, string.Join("\n", diagnostics));
            InvocationExpressionSyntax[] references = [.. driver.GetRunResult().Results[0].GeneratedSources
                .SelectMany(source => source.SyntaxTree.GetRoot().DescendantNodes()
                    .OfType<InvocationExpressionSyntax>())
                .Where(call => call.Expression.ToString() == "state.AddReference")];
            InvocationExpressionSyntax[] placeholders = [.. references.Where(call => IsForwardReference(call, "47"))];
            Assert.That(placeholders, Has.Length.EqualTo(1),
                "The type template must retain the placeholder's forward HasComponent reference.");
            foreach (InvocationExpressionSyntax reference in placeholders)
            {
                Assert.That(reference.Ancestors().OfType<IfStatementSyntax>()
                    .Any(statement => statement.Condition.ToString() == "!forInstance"), Is.True,
                    "A runtime instance must not link to an uninstantiated type declaration: " + reference);
            }
            InvocationExpressionSyntax[] ordinary = [.. references.Where(call => IsForwardReference(call, "35"))];
            Assert.That(ordinary, Has.Length.EqualTo(1));
            foreach (InvocationExpressionSyntax reference in ordinary)
            {
                Assert.That(reference.Ancestors().OfType<IfStatementSyntax>()
                    .Any(statement => statement.Condition.ToString() == "!forInstance"), Is.False,
                    "Ordinary external references must remain available on runtime instances.");
            }
            output.GetDiagnostics().Check(TestContext.Out, out int errors, out _);
            Assert.That(errors, Is.Zero);
        }

        private static bool IsForwardReference(InvocationExpressionSyntax call, string referenceType)
        {
            return call.ArgumentList.Arguments[1].Expression.IsKind(SyntaxKind.FalseLiteralExpression) &&
                call.ArgumentList.Arguments[0].Expression.DescendantNodes().OfType<LiteralExpressionSyntax>()
                    .Any(literal => literal.Token.ValueText == referenceType);
        }

        private const string sModel = """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
                xmlns:ua="http://opcfoundation.org/UA/" xmlns="http://test.org/UA/PlaceholderReferences/"
                TargetNamespace="http://test.org/UA/PlaceholderReferences/">
              <opc:Namespaces>
                <opc:Namespace XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd"
                    Name="OpcUa" Prefix="Opc.Ua">http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="PlaceholderReferences"
                    Prefix="Test.PlaceholderReferences">http://test.org/UA/PlaceholderReferences/</opc:Namespace>
              </opc:Namespaces>
              <opc:ObjectType SymbolicName="DeviceType" BaseType="ua:BaseObjectType">
                <opc:Children>
                  <opc:Object SymbolicName="Endpoints" TypeDefinition="ua:FolderType" ModellingRule="Mandatory">
                    <opc:Children>
                      <opc:Object SymbolicName="Endpoint_Placeholder" TypeDefinition="ua:BaseObjectType"
                          ModellingRule="PLACEHOLDER_RULE" />
                    </opc:Children>
                    <opc:References>
                      <opc:Reference>
                        <opc:ReferenceType>ua:HasComponent</opc:ReferenceType>
                        <opc:TargetId>DeviceType_Endpoints_Endpoint_Placeholder</opc:TargetId>
                      </opc:Reference>
                      <opc:Reference>
                        <opc:ReferenceType>ua:Organizes</opc:ReferenceType>
                        <opc:TargetId>ua:ObjectsFolder</opc:TargetId>
                      </opc:Reference>
                    </opc:References>
                  </opc:Object>
                </opc:Children>
              </opc:ObjectType>
            </opc:ModelDesign>
            """;
    }
}
