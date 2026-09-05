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
    /// Tests event-record generation across companion-model boundaries.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class CrossModelEventRecordGeneratorTests
    {
        [Test]
        public void InheritedXRegistrySourceUrlUsesStringReaderAcrossModels()
        {
            CSharpCompilation compilation = OptimizationLevel.Release
                .CreateCompilation()
                .AddCode(
                    new Dictionary<string, string>().WithOpcUaGeneratedStack(),
                    LanguageVersion.CSharp13);
            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true",
                    ["build_property.ModelSourceGeneratorOmitObjectTypeProxies"] = "true"
                });
            var generator = new ModelSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(
                    new CSharpParseOptions()
                        .WithKind(SourceCodeKind.Regular)
                        .WithLanguageVersion(LanguageVersion.CSharp13))
                .AddAdditionalTexts(
                [
                    EmbeddedText.Create(
                        "XRegistry.ModelDesign.xml",
                        XRegistryModelDesign),
                    EmbeddedText.Create(
                        "Downstream.ModelDesign.xml",
                        DownstreamModelDesign)
                ])
                .WithUpdatedAnalyzerConfigOptions(options);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out _,
                out ImmutableArray<Diagnostic> diagnostics);

            Assert.That(
                diagnostics,
                Is.Empty,
                string.Join("\n", diagnostics.Select(diagnostic => diagnostic.ToString())));

            GeneratorRunResult result = driver.GetRunResult().Results[0];
            string xRegistryRecords = result.GeneratedSources
                .Select(source => source.SourceText.ToString())
                .Single(source => source.Contains(
                    "public partial record XRegistryEventTypeRecord",
                    StringComparison.Ordinal));
            string downstreamRecords = result.GeneratedSources
                .Select(source => source.SourceText.ToString())
                .Single(source => source.Contains(
                    "public partial record DownstreamEventTypeRecord",
                    StringComparison.Ordinal));

            Assert.That(
                xRegistryRecords,
                Does.Contain("public string? SourceUrl"));
            Assert.That(
                downstreamRecords,
                Does.Contain(
                    "DownstreamEventTypeRecord : " +
                    "global::Test.XRegistry.XRegistryEventTypeRecord"));

            AssignmentExpressionSyntax sourceUrlAssignment = CSharpSyntaxTree
                .ParseText(downstreamRecords)
                .GetRoot()
                .DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Single(assignment => assignment.Left.ToString() == "SourceUrl");
            var getString = (InvocationExpressionSyntax)sourceUrlAssignment.Right;
            Assert.Multiple(() =>
            {
                Assert.That(
                    getString.Expression.ToString(),
                    Is.EqualTo("global::Opc.Ua.EventRecordFieldReaders.GetString"));
                Assert.That(
                    getString.ArgumentList.Arguments[1].Expression.ToString(),
                    Is.EqualTo("13"));
            });

            // The shared stack fixture uses classes for C# 8 tests. Restore the
            // production record shape for this C# 13 compilation.
            string recordCompatibleStack = CompilerUtils.OpcUa
                .Replace(
                    "public abstract class EventRecord",
                    "public abstract record EventRecord",
                    StringComparison.Ordinal)
                .Replace(
                    "public partial class BaseEventTypeRecord : EventRecord",
                    "public partial record BaseEventTypeRecord : EventRecord",
                    StringComparison.Ordinal);
            CSharpCompilation eventRecordCompilation = OptimizationLevel.Release
                .CreateCompilation("CrossModelEventRecords")
                .AddCode(
                    new Dictionary<string, string>
                    {
                        ["OpcUa.cs"] = recordCompatibleStack,
                        ["EventModelStubs.cs"] = EventModelStubs,
                        ["Test.XRegistry.EventRecords.g.cs"] = xRegistryRecords,
                        ["Test.Downstream.EventRecords.g.cs"] = downstreamRecords
                    },
                    LanguageVersion.CSharp13);

            INamedTypeSymbol downstreamRecord = eventRecordCompilation.GetTypeByMetadataName(
                "Test.Downstream.DownstreamEventTypeRecord");
            IPropertySymbol sourceUrl = FindProperty(downstreamRecord, "SourceUrl");
            Assert.That(sourceUrl, Is.Not.Null);
            Assert.That(sourceUrl!.Type.SpecialType, Is.EqualTo(SpecialType.System_String));

            Diagnostic[] compilationErrors = eventRecordCompilation
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();
            Assert.That(
                compilationErrors,
                Is.Empty,
                string.Join(
                    "\n",
                    compilationErrors.Select(diagnostic => diagnostic.ToString())));
        }

        private static IPropertySymbol FindProperty(INamedTypeSymbol type, string name)
        {
            for (INamedTypeSymbol current = type;
                current != null;
                current = current.BaseType)
            {
                IPropertySymbol property = current
                    .GetMembers(name)
                    .OfType<IPropertySymbol>()
                    .SingleOrDefault();
                if (property != null)
                {
                    return property;
                }
            }
            return null;
        }

        private const string EventModelStubs =
            """
            namespace Test.XRegistry
            {
                public static partial class BrowseNames
                {
                    public const string SourceUrl = nameof(SourceUrl);
                }

                public static partial class ObjectTypeIds
                {
                    public static global::Opc.Ua.ExpandedNodeId XRegistryEventType => default;
                }
            }

            namespace Test.Downstream
            {
                public static partial class BrowseNames
                {
                    public const string SourceUrl = nameof(SourceUrl);
                    public const string CompanionField = nameof(CompanionField);
                }

                public static partial class ObjectTypeIds
                {
                    public static global::Opc.Ua.ExpandedNodeId DownstreamEventType => default;
                }
            }
            """;

        private const string XRegistryModelDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns="http://opcfoundation.org/UA/xRegistry/"
              TargetNamespace="http://opcfoundation.org/UA/xRegistry/">
              <opc:Namespaces>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua"
                  XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd"
                  >http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="XRegistry"
                  Prefix="Test.XRegistry">http://opcfoundation.org/UA/xRegistry/</opc:Namespace>
              </opc:Namespaces>
              <opc:ObjectType SymbolicName="XRegistryEventType"
                BaseType="ua:BaseEventType" IsAbstract="true">
                <opc:Children>
                  <opc:Property SymbolicName="SourceUrl" DataType="ua:UriString"
                    ValueRank="Scalar" ModellingRule="Mandatory" />
                </opc:Children>
              </opc:ObjectType>
            </opc:ModelDesign>
            """;

        private const string DownstreamModelDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns:xreg="http://opcfoundation.org/UA/xRegistry/"
              xmlns="http://example.org/UA/Downstream/"
              TargetNamespace="http://example.org/UA/Downstream/">
              <opc:Namespaces>
                <opc:Namespace Name="Downstream"
                  Prefix="Test.Downstream">http://example.org/UA/Downstream/</opc:Namespace>
                <opc:Namespace Name="XRegistry"
                  Prefix="Test.XRegistry">http://opcfoundation.org/UA/xRegistry/</opc:Namespace>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua"
                  XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd"
                  >http://opcfoundation.org/UA/</opc:Namespace>
              </opc:Namespaces>
              <opc:ObjectType SymbolicName="DownstreamEventType"
                BaseType="xreg:XRegistryEventType">
                <opc:Children>
                  <opc:Property SymbolicName="CompanionField" DataType="ua:String"
                    ValueRank="Scalar" ModellingRule="Mandatory" />
                </opc:Children>
              </opc:ObjectType>
            </opc:ModelDesign>
            """;
    }
}
