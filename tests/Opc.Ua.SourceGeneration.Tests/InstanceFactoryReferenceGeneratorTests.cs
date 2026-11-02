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
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Verifies that instance factories preserve references needed by optional-child creation.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    public sealed class InstanceFactoryReferenceGeneratorTests
    {
        /// <summary>
        /// References to optional declarations remain on the instance factory so optional-child
        /// creation can rebase them. Mandatory siblings with underscores in their names and
        /// external references also remain on both factories.
        /// </summary>
        [TestCase(kHasAddIn, "CreateDeviceType", "CreateInstanceOfDeviceType",
            Description = "Mandatory child of an optional folder")]
        [TestCase(kHasNotifier, "CreateDeviceType", "CreateInstanceOfDeviceType",
            Description = "Optional direct child")]
        [TestCase(kHasComponent, "CreateDeviceType", "CreateInstanceOfDeviceType",
            Description = "Mandatory sibling with an underscore in its name")]
        [TestCase(kHasEventSource, "CreateDeviceType", "CreateInstanceOfDeviceType",
            Description = "Mandatory direct child")]
        [TestCase(kOrganizes, "CreateDeviceType", "CreateInstanceOfDeviceType",
            Description = "External node")]
        public void InstanceFactoryRetainsReferencesNeededForOptionalChildCreation(
            string referenceType, params string[] expectedFactories)
        {
            GeneratorDriver driver = GenerateModel(out Compilation output);

            output.GetDiagnostics().Check(TestContext.Out, out int errors, out _);
            Assert.That(errors, Is.Zero);

            string[] factories = [.. GetGeneratedReferenceCalls(driver)
                .Where(call => IsForwardReference(call, referenceType))
                .Select(call => call.Ancestors().OfType<MethodDeclarationSyntax>().First().Identifier.ValueText)
                .Where(name => name is "CreateDeviceType" or "CreateInstanceOfDeviceType")];
            Assert.That(factories, Is.EquivalentTo(expectedFactories));
        }

        private static GeneratorDriver GenerateModel(out Compilation output)
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
                .AddAdditionalTexts([EmbeddedText.Create("InstanceReferences.xml", kModel)])
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

        private const string kOrganizes = "35";
        private const string kHasEventSource = "36";
        private const string kHasNotifier = "48";
        private const string kHasAddIn = "17604";
        private const string kHasComponent = "47";

        private const string kModel = """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
                xmlns:ua="http://opcfoundation.org/UA/" xmlns="http://test.org/UA/InstanceReferences/"
                TargetNamespace="http://test.org/UA/InstanceReferences/">
              <opc:Namespaces>
                <opc:Namespace XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd"
                    Name="OpcUa" Prefix="Opc.Ua">http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="InstanceReferences"
                    Prefix="Test.InstanceReferences">http://test.org/UA/InstanceReferences/</opc:Namespace>
              </opc:Namespaces>
              <opc:ObjectType SymbolicName="DeviceType" BaseType="ua:BaseObjectType">
                <opc:Children>
                  <opc:Object SymbolicName="Blocks" TypeDefinition="ua:FolderType" ModellingRule="Optional">
                    <opc:Children>
                      <opc:Object SymbolicName="Block" TypeDefinition="ua:BaseObjectType"
                          ModellingRule="Mandatory" />
                    </opc:Children>
                  </opc:Object>
                  <opc:Object SymbolicName="Extra" TypeDefinition="ua:BaseObjectType" ModellingRule="Optional" />
                  <opc:Object SymbolicName="Status" TypeDefinition="ua:BaseObjectType" ModellingRule="Optional" />
                  <opc:Object SymbolicName="Status_Code" TypeDefinition="ua:BaseObjectType"
                      ModellingRule="Mandatory" />
                  <opc:Object SymbolicName="CurrentState" TypeDefinition="ua:BaseObjectType"
                      ModellingRule="Mandatory" />
                </opc:Children>
                <opc:References>
                  <opc:Reference>
                    <opc:ReferenceType>ua:HasAddIn</opc:ReferenceType>
                    <opc:TargetId>DeviceType_Blocks_Block</opc:TargetId>
                  </opc:Reference>
                  <opc:Reference>
                    <opc:ReferenceType>ua:HasNotifier</opc:ReferenceType>
                    <opc:TargetId>DeviceType_Extra</opc:TargetId>
                  </opc:Reference>
                  <opc:Reference>
                    <opc:ReferenceType>ua:HasComponent</opc:ReferenceType>
                    <opc:TargetId>DeviceType_Status_Code</opc:TargetId>
                  </opc:Reference>
                  <opc:Reference>
                    <opc:ReferenceType>ua:HasEventSource</opc:ReferenceType>
                    <opc:TargetId>DeviceType_CurrentState</opc:TargetId>
                  </opc:Reference>
                  <opc:Reference>
                    <opc:ReferenceType>ua:Organizes</opc:ReferenceType>
                    <opc:TargetId>ua:ObjectsFolder</opc:TargetId>
                  </opc:Reference>
                </opc:References>
              </opc:ObjectType>
            </opc:ModelDesign>
            """;
    }
}
