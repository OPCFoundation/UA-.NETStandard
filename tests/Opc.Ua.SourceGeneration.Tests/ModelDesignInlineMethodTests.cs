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
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Tests ModelDesign methods whose arguments are declared on the method
    /// itself rather than on a separate method type declaration.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ModelDesignInlineMethodTests
    {
        [Test]
        public void InlineObjectTypeMethodWithArgumentsUsesBaseMethodState()
        {
            string generated = GenerateAndCompile(
                ObjectType(InlineBoost) + Instance("ThermostatType"));

            Assert.Multiple(() =>
            {
                Assert.That(generated, Does.Not.Contain("BoostMethodState"));
                Assert.That(generated, Does.Contain("global::Opc.Ua.MethodState? Boost"));
                Assert.That(generated, Does.Contain("state.CreateOrReplaceInputArguments("));
                Assert.That(generated, Does.Contain("state.CreateOrReplaceOutputArguments("));
                Assert.That(generated, Does.Contain("Name = \"Degrees\""));
                Assert.That(generated, Does.Contain("Name = \"NewSetpoint\""));
            });
        }

        [Test]
        public void InlineObjectMethodWithArgumentsUsesBaseMethodState()
        {
            string generated = GenerateAndCompile(
                Instance("ua:BaseObjectType", InlineBoost));

            Assert.Multiple(() =>
            {
                Assert.That(generated, Does.Not.Contain("BoostMethodState"));
                Assert.That(generated, Does.Contain("state.CreateOrReplaceInputArguments("));
                Assert.That(generated, Does.Contain("Name = \"Degrees\""));
            });
        }

        [Test]
        public void MethodTypeDeclarationKeepsTypedMethodState()
        {
            string generated = GenerateAndCompile(
                BoostMethodType +
                ObjectType(
                    """
                    <opc:Method SymbolicName="Boost" TypeDefinition="BoostMethodType"
                      ModellingRule="Mandatory" />
                    """) +
                Instance("ThermostatType"));

            Assert.Multiple(() =>
            {
                Assert.That(generated, Does.Contain("class BoostMethodState"));
                Assert.That(generated, Does.Contain("global::Heating.BoostMethodState? Boost"));
            });
        }

        private static string GenerateAndCompile(string nodes)
        {
            CSharpCompilation compilation = OptimizationLevel.Release
                .CreateCompilation()
                .AddCode(
                    new Dictionary<string, string>().WithOpcUaGeneratedStack(),
                    LanguageVersion.CSharp13);
            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true"
                });
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new ModelSourceGenerator())
                .WithUpdatedParseOptions(
                    new CSharpParseOptions()
                        .WithKind(SourceCodeKind.Regular)
                        .WithLanguageVersion(LanguageVersion.CSharp13))
                .AddAdditionalTexts(
                    [EmbeddedText.Create("Heating.xml", ModelHeader + nodes + ModelFooter)])
                .WithUpdatedAnalyzerConfigOptions(options);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> generatorDiagnostics);

            Assert.That(
                generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty);
            Assert.That(
                outputCompilation.GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()),
                Is.Empty);

            return string.Join(
                "\n",
                driver.GetRunResult().Results
                    .SelectMany(result => result.GeneratedSources)
                    .Select(source => source.SourceText.ToString()));
        }

        private static string ObjectType(string method)
        {
            return
                $"""
                <opc:ObjectType SymbolicName="ThermostatType" BaseType="ua:BaseObjectType">
                  <opc:Children>
                    {method}
                  </opc:Children>
                </opc:ObjectType>
                """;
        }

        private static string Instance(string typeDefinition, string method = null)
        {
            string children = method == null
                ? string.Empty
                : $"<opc:Children>{method}</opc:Children>";
            return
                $"""
                <opc:Object SymbolicName="Thermostat" TypeDefinition="{typeDefinition}">
                  {children}
                  <opc:References>
                    <opc:Reference IsInverse="true">
                      <opc:ReferenceType>ua:Organizes</opc:ReferenceType>
                      <opc:TargetId>ua:ObjectsFolder</opc:TargetId>
                    </opc:Reference>
                  </opc:References>
                </opc:Object>
                """;
        }

        private const string InlineBoost =
            """
            <opc:Method SymbolicName="Boost" ModellingRule="Mandatory">
              <opc:InputArguments>
                <opc:Argument Name="Degrees" DataType="ua:Double" />
              </opc:InputArguments>
              <opc:OutputArguments>
                <opc:Argument Name="NewSetpoint" DataType="ua:Double" />
              </opc:OutputArguments>
            </opc:Method>
            """;

        private const string BoostMethodType =
            """
            <opc:Method SymbolicName="BoostMethodType">
              <opc:InputArguments>
                <opc:Argument Name="Degrees" DataType="ua:Double" />
              </opc:InputArguments>
              <opc:OutputArguments>
                <opc:Argument Name="NewSetpoint" DataType="ua:Double" />
              </opc:OutputArguments>
            </opc:Method>
            """;

        private const string ModelHeader =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns="http://example.com/Heating/"
              TargetNamespace="http://example.com/Heating/"
              TargetXmlNamespace="http://example.com/Heating/Types.xsd"
              TargetVersion="1.0.0"
              TargetPublicationDate="2026-01-01T00:00:00Z">
              <opc:Namespaces>
                <opc:Namespace Name="Heating" Prefix="Heating"
                  XmlNamespace="http://example.com/Heating/Types.xsd"
                  XmlPrefix="Heating">http://example.com/Heating/</opc:Namespace>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua"
                  XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd"
                  XmlPrefix="OpcUa">http://opcfoundation.org/UA/</opc:Namespace>
              </opc:Namespaces>

            """;

        private const string ModelFooter =
            """

            </opc:ModelDesign>
            """;
    }
}
