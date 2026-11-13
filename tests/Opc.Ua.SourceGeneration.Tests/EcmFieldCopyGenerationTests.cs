/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using RoslynAnalyzerOptions = Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Covers model and final-compilation eligibility of the closed ECM publication copies.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    public sealed class EcmFieldCopyGenerationTests
    {
        /// <summary>
        /// Both verified generated structures compile with exact-type field-copy methods.
        /// </summary>
        [Test]
        public async Task EcmCopiesCompileWithVerifiedGeneratedShapes()
        {
            var result = Generate(Model);
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
            string copies = result.Driver.GetRunResult().Results[0].GeneratedSources.Single(
                source => source.HintName.EndsWith("PublicationCopies.g.cs", StringComparison.Ordinal))
                .SourceText.ToString();
            foreach (string name in (string[])["AcPeDataType", "AcPpDataType"])
            {
                Assert.That(copies, Does.Contain("source.GetType() != typeof(" + name + ")"));
                Assert.That(copies.IndexOf("source.GetType() != typeof(" + name + ")", StringComparison.Ordinal),
                    Is.LessThan(copies.IndexOf("return new " + name, StringComparison.Ordinal)));
            }
            foreach (string name in (string[])["L1", "L2", "L3", "L1L2", "L2L3", "L3L1"])
            {
                Assert.That(copies, Does.Contain(name + " = source." + name));
            }
            Assert.That(copies, Does.Not.Contain(".Clone("));
            Assert.That(copies, Does.Not.Contain(".Encode("));
            Assert.That(copies, Does.Not.Contain("m_pooledSentinel"));
            INamedTypeSymbol? acPe = result.Output.GetTypeByMetadataName("Opc.Ua.ECM.AcPeDataType");
            Assert.That(acPe, Is.Not.Null);
            Assert.That(acPe!.DeclaringSyntaxReferences, Has.Length.EqualTo(2));
            Assert.That(await Analyze(result.Output, result.Options).ConfigureAwait(false), Is.Empty);
        }

        /// <summary>
        /// The ordinary model generation path does not emit publication helpers.
        /// </summary>
        [Test]
        public async Task EcmCopiesRequireExplicitModelOptIn()
        {
            var result = Generate(Model, enabled: false);
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Driver.GetRunResult().Results[0].GeneratedSources.Any(
                source => source.HintName.EndsWith("PublicationCopies.g.cs", StringComparison.Ordinal)), Is.False);
            Compilation output = result.Output.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                "namespace Opc.Ua.ECM { public partial class AcPeDataType { public byte[] Extra = new byte[1]; } }",
                new CSharpParseOptions(LanguageVersion.CSharp13), "Extra.cs"));
            Assert.That(await Analyze(output, result.Options).ConfigureAwait(false), Is.Empty);
        }

        /// <summary>
        /// Nested, mutable, optional, array, union and derived model shapes cannot emit a primitive copy.
        /// </summary>
        [TestCase("DataType=\"Float\"", "DataType=\"ByteString\"")]
        [TestCase("DataType=\"Float\"", "DataType=\"Variant\"")]
        [TestCase("DataType=\"Float\"", "DataType=\"LocalizedText\"")]
        [TestCase("DataType=\"Float\"", "DataType=\"Float\" ValueRank=\"1\"")]
        [TestCase("DataType=\"Float\"", "DataType=\"Float\" IsOptional=\"true\"")]
        [TestCase("BrowseName=\"1:AcPeDataType\"", "BrowseName=\"1:AcPeDataType\" IsAbstract=\"true\"")]
        [TestCase("<Definition Name=\"1:AcPeDataType\">", "<Definition Name=\"1:AcPeDataType\" IsUnion=\"true\">")]
        [TestCase(">i=22</Reference>", ">i=3000</Reference>")]
        [TestCase("<Field Name=\"L3\" DataType=\"Float\" />",
            "<Field Name=\"L3\" DataType=\"Float\" /><Field Name=\"Extra\" DataType=\"Float\" />")]
        public void EcmCopiesRejectUnsupportedModelFields(string original, string replacement)
        {
            var result = Generate(Model.Replace(original, replacement, StringComparison.Ordinal));
            Assert.That(result.Diagnostics.Any(d => d.Id == "MODELGEN039"), Is.True);
            Assert.That(result.Driver.GetRunResult().Results[0].GeneratedSources.Any(
                source => source.HintName.EndsWith("PublicationCopies.g.cs", StringComparison.Ordinal)), Is.False);
        }

        /// <summary>
        /// Source and peer-generated partial state are rejected after all generators have run.
        /// </summary>
        [TestCase("Extra.cs", "public byte[] Extra = new byte[1];")]
        [TestCase("Extra.g.cs", "public byte[] Extra = new byte[1];")]
        [TestCase("Extra.g.cs", "public object Extra { get; set; } = new object();")]
        [TestCase("Extra.g.cs", "public event System.Action Changed;")]
        public async Task EcmCopiesRejectAdditionalPartialState(string path, string member)
        {
            var result = Generate(Model);
            Compilation output = result.Output.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                "namespace Opc.Ua.ECM { public partial class AcPeDataType { " + member + " } }",
                new CSharpParseOptions(LanguageVersion.CSharp13), path));
            ImmutableArray<Diagnostic> diagnostics = await Analyze(output, result.Options).ConfigureAwait(false);
            Assert.That(diagnostics.Any(d => d.Id == "MODELGEN039"), Is.True);
            Assert.That(diagnostics.All(d => d.Id == "MODELGEN039"), Is.True);
        }

        /// <summary>
        /// A partial declaration cannot introduce inherited state into a copied structure.
        /// </summary>
        [Test]
        public async Task EcmCopiesRejectAdditionalBaseClass()
        {
            var result = Generate(Model);
            Compilation output = result.Output.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                """
                namespace Opc.Ua.ECM
                {
                    public class ExtraBase
                    {
                        public object Extra = new object();
                    }
                    public partial class AcPpDataType : ExtraBase
                    {
                    }
                }
                """, new CSharpParseOptions(LanguageVersion.CSharp13), "Extra.g.cs"));
            Assert.That((await Analyze(output, result.Options).ConfigureAwait(false))
                .Any(d => d.Id == "MODELGEN039"), Is.True);
        }

        /// <summary>
        /// Final verification also detects unsupported fields in a single generated declaration.
        /// </summary>
        [Test]
        public async Task EcmCopiesRejectExtraGeneratedStorage()
        {
            var result = Generate(Model);
            SyntaxTree types = result.Output.SyntaxTrees.Single(
                tree => tree.FilePath.EndsWith("Opc.Ua.ECM.DataTypes.g.cs", StringComparison.Ordinal));
            string changed = types.ToString();
            int start = changed.IndexOf("class AcPeDataType", StringComparison.Ordinal);
            int opening = changed.IndexOf('{', start, StringComparison.Ordinal);
            changed = changed.Insert(opening + 1, "\r\nprivate byte[] m_extra = new byte[1];\r\n");
            Compilation output = result.Output.ReplaceSyntaxTree(types, CSharpSyntaxTree.ParseText(
                changed, (CSharpParseOptions)types.Options, types.FilePath));
            Assert.That((await Analyze(output, result.Options).ConfigureAwait(false))
                .Any(d => d.Id == "MODELGEN039"), Is.True);
        }

        /// <summary>
        /// Pool bookkeeping cannot introduce mutable payload storage or another field.
        /// </summary>
        [TestCase("private object m_pooledSentinel;")]
        [TestCase("private int m_pooledSentinel; private int m_extra;")]
        [TestCase("private int m_otherSentinel;")]
        public async Task EcmCopiesRejectModifiedPoolingStorage(string replacement)
        {
            var result = Generate(Model);
            SyntaxTree types = result.Output.SyntaxTrees.Single(
                tree => tree.FilePath.EndsWith("Opc.Ua.ECM.DataTypes.g.cs", StringComparison.Ordinal));
            string changed = types.ToString().Replace(
                "private int m_pooledSentinel;", replacement, StringComparison.Ordinal);
            Assert.That(changed, Is.Not.EqualTo(types.ToString()));
            Compilation output = result.Output.ReplaceSyntaxTree(types, CSharpSyntaxTree.ParseText(
                changed, (CSharpParseOptions)types.Options, types.FilePath));
            Assert.That((await Analyze(output, result.Options).ConfigureAwait(false))
                .Any(d => d.Id == "MODELGEN039"), Is.True);
        }

        /// <summary>
        /// Final verification disallows virtual accessors in an otherwise unchanged generated declaration.
        /// </summary>
        [Test]
        public async Task EcmCopiesRejectVirtualGeneratedAccessor()
        {
            var result = Generate(Model);
            SyntaxTree types = result.Output.SyntaxTrees.Single(
                tree => tree.FilePath.EndsWith("Opc.Ua.ECM.DataTypes.g.cs", StringComparison.Ordinal));
            SyntaxNode root = await types.GetRootAsync().ConfigureAwait(false);
            var property = root.DescendantNodes().OfType<PropertyDeclarationSyntax>()
                .Single(node => node.Identifier.ValueText == "L1");
            SyntaxNode changed = root.ReplaceNode(property, property.WithModifiers(
                property.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.VirtualKeyword))));
            Compilation output = result.Output.ReplaceSyntaxTree(
                types, types.WithRootAndOptions(changed, types.Options));
            Assert.That((await Analyze(output, result.Options).ConfigureAwait(false))
                .Any(d => d.Id == "MODELGEN039"), Is.True);
        }

        /// <summary>
        /// A field getter cannot be replaced with a custom call in the final generated declaration.
        /// </summary>
        [Test]
        public async Task EcmCopiesRejectCustomGeneratedAccessor()
        {
            var result = Generate(Model);
            SyntaxTree types = result.Output.SyntaxTrees.Single(
                tree => tree.FilePath.EndsWith("Opc.Ua.ECM.DataTypes.g.cs", StringComparison.Ordinal));
            SyntaxNode root = await types.GetRootAsync().ConfigureAwait(false);
            ClassDeclarationSyntax declaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Single(node => node.Identifier.ValueText == "AcPeDataType" &&
                    node.Members.OfType<PropertyDeclarationSyntax>().Any(
                        property => property.Identifier.ValueText == "L1"));
            PropertyDeclarationSyntax property = declaration.Members.OfType<PropertyDeclarationSyntax>()
                .Single(node => node.Identifier.ValueText == "L1");
            AccessorDeclarationSyntax getter = property.AccessorList!.Accessors.Single(
                accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
            ClassDeclarationSyntax changed = declaration.ReplaceNode(getter, getter.WithExpressionBody(
                SyntaxFactory.ArrowExpressionClause(SyntaxFactory.InvocationExpression(
                    SyntaxFactory.IdentifierName("CustomRead")))));
            changed = changed.AddMembers(SyntaxFactory.ParseMemberDeclaration(
                "private float CustomRead() => throw new System.InvalidOperationException();")!);
            Compilation output = result.Output.ReplaceSyntaxTree(types, types.WithRootAndOptions(
                root.ReplaceNode(declaration, changed), types.Options));
            Assert.That((await Analyze(output, result.Options).ConfigureAwait(false))
                .Any(d => d.Id == "MODELGEN039"), Is.True);
        }

        /// <summary>
        /// Runs the existing model generator with the ECM-only opt-in.
        /// </summary>
        private static (Compilation Output, GeneratorDriver Driver, ImmutableArray<Diagnostic> Diagnostics,
            RoslynAnalyzerOptions Options) Generate(string model, bool enabled = true)
        {
            AdditionalText text = EmbeddedText.Create("Opc.Ua.ECM.NodeSet2.xml", model);
            var options = new AnalyzerOptionsProvider(new Dictionary<string, string>
            {
                ["build_property.ModelSourceGeneratorEcmFieldCopies"] = enabled.ToString(),
                ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true",
                ["build_property.ModelSourceGeneratorOmitEventRecords"] = "true",
                ["build_property.ModelSourceGeneratorOmitObjectTypeProxies"] = "true"
            });
            options.TextOptions[text.Path] = new Dictionary<string, string>
            {
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorModelUri"] =
                    "http://opcfoundation.org/UA/ECM/",
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorName"] = "ECM",
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorPrefix"] = "Opc.Ua.ECM"
            };
            options.SyntaxOptions.Add(_ => new Dictionary<string, string>());
            CSharpCompilation compilation = OptimizationLevel.Release.CreateCompilation()
                .AddCode(new Dictionary<string, string>
                {
                    ["EcmStatusCodes.cs"] = """
                        namespace Opc.Ua
                        {
                            public static partial class StatusCodes
                            {
                                public const uint BadNotSupported = 0x803D0000u;
                            }
                        }
                        """
                }.WithOpcUaGeneratedStack(), LanguageVersion.CSharp13);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new ModelSourceGenerator())
                .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.CSharp13))
                .AddAdditionalTexts([text]).WithUpdatedAnalyzerConfigOptions(options);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
            return (output, driver, diagnostics, new RoslynAnalyzerOptions([text], options));
        }

        /// <summary>
        /// Checks the final compilation, rather than only the input compilation seen by a generator.
        /// </summary>
        private static Task<ImmutableArray<Diagnostic>> Analyze(Compilation output, RoslynAnalyzerOptions options)
        {
            return output.WithAnalyzers([new EcmFieldCopyEligibilityAnalyzer()], options)
                .GetAnalyzerDiagnosticsAsync();
        }

        /// <summary>
        /// The two exact primitive structures required by the first-party ECM model.
        /// </summary>
        private const string Model = """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>http://opcfoundation.org/UA/ECM/</Uri></NamespaceUris>
              <Models><Model ModelUri="http://opcfoundation.org/UA/ECM/" Version="1.0.0"
                PublicationDate="2026-01-01T00:00:00Z">
                <RequiredModel ModelUri="http://opcfoundation.org/UA/" Version="1.05.07"
                  PublicationDate="2025-01-01T00:00:00Z" />
              </Model></Models>
              <Aliases>
                <Alias Alias="Float">i=10</Alias><Alias Alias="HasSubtype">i=45</Alias>
                <Alias Alias="HasEncoding">i=38</Alias><Alias Alias="HasTypeDefinition">i=40</Alias>
              </Aliases>
              <UADataType NodeId="ns=1;i=3005" BrowseName="1:AcPeDataType">
                <DisplayName>AcPeDataType</DisplayName>
                <References><Reference ReferenceType="HasSubtype" IsForward="false">i=22</Reference>
                  <Reference ReferenceType="HasEncoding">ns=1;i=5010</Reference></References>
                <Definition Name="1:AcPeDataType">
                  <Field Name="L1" DataType="Float" /><Field Name="L2" DataType="Float" />
                  <Field Name="L3" DataType="Float" />
                </Definition>
              </UADataType>
              <UAObject NodeId="ns=1;i=5010" BrowseName="Default Binary">
                <DisplayName>Default Binary</DisplayName>
                <References><Reference ReferenceType="HasTypeDefinition">i=76</Reference>
                  <Reference ReferenceType="HasEncoding" IsForward="false">ns=1;i=3005</Reference></References>
              </UAObject>
              <UADataType NodeId="ns=1;i=3006" BrowseName="1:AcPpDataType">
                <DisplayName>AcPpDataType</DisplayName>
                <References><Reference ReferenceType="HasSubtype" IsForward="false">i=22</Reference>
                  <Reference ReferenceType="HasEncoding">ns=1;i=5013</Reference></References>
                <Definition Name="1:AcPpDataType">
                  <Field Name="L1L2" DataType="Float" /><Field Name="L2L3" DataType="Float" />
                  <Field Name="L3L1" DataType="Float" />
                </Definition>
              </UADataType>
              <UAObject NodeId="ns=1;i=5013" BrowseName="Default Binary">
                <DisplayName>Default Binary</DisplayName>
                <References><Reference ReferenceType="HasTypeDefinition">i=76</Reference>
                  <Reference ReferenceType="HasEncoding" IsForward="false">ns=1;i=3006</Reference></References>
              </UAObject>
            </UANodeSet>
            """;
    }
}
