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
    /// <summary>
    /// Verifies placeholder references in generated type and instance factories.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    public sealed class PlaceholderReferenceGeneratorTests
    {
        /// <summary>
        /// Keeps nested placeholder references on templates without exposing them on runtime instances.
        /// </summary>
        [TestCase("MandatoryPlaceholder")]
        [TestCase("OptionalPlaceholder")]
        public void PlaceholderReferencesAreRetainedOnlyForTheTypeTemplate(string rule)
        {
            GeneratorDriver driver = GenerateModel(
                sModel.Replace("PLACEHOLDER_RULE", rule, StringComparison.Ordinal), out Compilation output);
            InvocationExpressionSyntax[] references = GetGeneratedReferenceCalls(driver);
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

        /// <summary>
        /// Keeps direct and external placeholder references only in guarded, compilable type factories.
        /// </summary>
        [Test]
        public void DirectPlaceholderReferencesCompileForTypeAndInstanceFactories(
            [Values("MandatoryPlaceholder", "OptionalPlaceholder")] string rule,
            [Values("ObjectType", "VariableType")] string rootType)
        {
            string baseType = rootType == "ObjectType" ? "BaseObjectType" : "BaseDataVariableType";
            string model = sDirectChildModel
                .Replace("ROOT_TYPE", rootType, StringComparison.Ordinal)
                .Replace("BASE_TYPE", baseType, StringComparison.Ordinal)
                .Replace("PLACEHOLDER_RULE", rule, StringComparison.Ordinal);
            GeneratorDriver driver = GenerateModel(model, out Compilation output);

            output.GetDiagnostics().Check(TestContext.Out, out int errors, out _);
            Assert.That(errors, Is.Zero);

            InvocationExpressionSyntax[] references = GetGeneratedReferenceCalls(driver);
            InvocationExpressionSyntax[] placeholders = [.. references.Where(call => IsForwardReference(call, "47"))];
            Assert.That(placeholders, Has.Length.EqualTo(2),
                "The type template must retain both direct-child and external placeholder references.");
            foreach (InvocationExpressionSyntax reference in placeholders)
            {
                MethodDeclarationSyntax factory = reference.Ancestors().OfType<MethodDeclarationSyntax>().Single();
                Assert.That(factory.Identifier.ValueText, Is.EqualTo("CreateDeviceType"),
                    "Instance-only factories must omit every placeholder reference.");
                Assert.That(factory.ParameterList.Parameters.Any(
                    parameter => parameter.Identifier.ValueText == "forInstance"), Is.True);
                Assert.That(reference.Ancestors().OfType<IfStatementSyntax>()
                    .Any(statement => statement.Condition.ToString() == "!forInstance"), Is.True);
            }

            InvocationExpressionSyntax[] ordinary = [.. references.Where(call => IsForwardReference(call, "35"))];
            Assert.That(ordinary.Select(reference =>
                reference.Ancestors().OfType<MethodDeclarationSyntax>().Single().Identifier.ValueText),
                Is.EquivalentTo(sFactoryNames));
            foreach (InvocationExpressionSyntax reference in ordinary)
            {
                Assert.That(reference.Ancestors().OfType<IfStatementSyntax>()
                    .Any(statement => statement.Condition.ToString() == "!forInstance"), Is.False,
                    "Ordinary external references must remain unconditional in both factory contexts.");
            }
        }

        private static GeneratorDriver GenerateModel(string model, out Compilation output)
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
                .AddAdditionalTexts([EmbeddedText.Create("Placeholders.xml", model)])
                .WithUpdatedAnalyzerConfigOptions(options);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation, out output, out ImmutableArray<Diagnostic> diagnostics);

            Assert.That(diagnostics, Is.Empty, string.Join("\n", diagnostics));
            return driver;
        }

        private static InvocationExpressionSyntax[] GetGeneratedReferenceCalls(GeneratorDriver driver)
        {
            return [.. driver.GetRunResult().Results[0].GeneratedSources
                .SelectMany(source => source.SyntaxTree.GetRoot().DescendantNodes()
                    .OfType<InvocationExpressionSyntax>())
                .Where(call => call.Expression.ToString() == "state.AddReference")];
        }

        private static bool IsForwardReference(InvocationExpressionSyntax call, string referenceType)
        {
            return call.ArgumentList.Arguments[1].Expression.IsKind(SyntaxKind.FalseLiteralExpression) &&
                call.ArgumentList.Arguments[0].Expression.DescendantNodes().OfType<LiteralExpressionSyntax>()
                    .Any(literal => literal.Token.ValueText == referenceType);
        }

        private static readonly string[] sFactoryNames = ["CreateDeviceType", "CreateInstanceOfDeviceType"];
        private const string sDirectChildModel = """
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
              <opc:ROOT_TYPE SymbolicName="DeviceType" BaseType="ua:BASE_TYPE">
                    <opc:Children>
                      <opc:Variable SymbolicName="Value_Placeholder" TypeDefinition="ua:BaseDataVariableType"
                          DataType="ua:String" ModellingRule="PLACEHOLDER_RULE" />
                    </opc:Children>
                    <opc:References>
                      <opc:Reference>
                        <opc:ReferenceType>ua:HasComponent</opc:ReferenceType>
                        <opc:TargetId>DeviceType_Value_Placeholder</opc:TargetId>
                      </opc:Reference>
                      <opc:Reference>
                        <opc:ReferenceType>ua:HasComponent</opc:ReferenceType>
                        <opc:TargetId>ExternalType_Value_Placeholder</opc:TargetId>
                      </opc:Reference>
                      <opc:Reference>
                        <opc:ReferenceType>ua:Organizes</opc:ReferenceType>
                        <opc:TargetId>ua:ObjectsFolder</opc:TargetId>
                      </opc:Reference>
                    </opc:References>
              </opc:ROOT_TYPE>
              <opc:ObjectType SymbolicName="ExternalType" BaseType="ua:BaseObjectType">
                    <opc:Children>
                      <opc:Variable SymbolicName="Value_Placeholder" TypeDefinition="ua:BaseDataVariableType"
                          DataType="ua:String" ModellingRule="PLACEHOLDER_RULE" />
                    </opc:Children>
              </opc:ObjectType>
            </opc:ModelDesign>
            """;

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
