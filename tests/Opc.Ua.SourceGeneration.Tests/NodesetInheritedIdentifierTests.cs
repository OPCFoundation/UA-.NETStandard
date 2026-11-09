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
using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Inherited declarations cannot reuse a dependency's identifiers in the consuming model.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class NodesetInheritedIdentifierTests
    {
        /// <summary>
        /// Executes both compiled loaders and preserves explicit overrides and predefined-instance symbols.
        /// </summary>
        [TestCase("i=6001", false)]
        [TestCase("i=6001", true)]
        [TestCase("s=shared-value", false)]
        [TestCase("s=shared-value", true)]
        [TestCase("g=09087e75-8e5e-499b-954f-f2a9603db28a", false)]
        [TestCase("g=09087e75-8e5e-499b-954f-f2a9603db28a", true)]
        [TestCase("b=AQIDBA==", false)]
        [TestCase("b=AQIDBA==", true)]
        [TestCase("s=base-value", false, "s=DerivedDeviceType_InheritedValue")]
        [TestCase("s=base-value", true, "s=DerivedDeviceType_InheritedValue")]
        [TestCase("s=base-value", false, "s=InstalledDevice_InheritedValue")]
        [TestCase("s=base-value", true, "s=InstalledDevice_InheritedValue")]
        public void InheritedChildrenDoNotBorrowPublishedIdentifiers(
            string identifier,
            bool derivedFirst,
            string? ownIdentifier = null)
        {
            ownIdentifier ??= identifier;
            CSharpCompilation compilation = OptimizationLevel.Release.CreateCompilation()
                .AddCode(new Dictionary<string, string>().WithOpcUaGeneratedStack(), LanguageVersion.CSharp13)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(kProbe, new CSharpParseOptions(LanguageVersion.CSharp13)));
            var options = new AnalyzerOptionsProvider(new Dictionary<string, string>
            {
                ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true",
                ["build_property.ModelSourceGeneratorOmitEventRecords"] = "true",
                ["build_property.ModelSourceGeneratorOmitObjectTypeProxies"] = "true"
            });
            foreach (string model in new[] { "IdentifierBase", "IdentifierDerived" })
            {
                options.TextOptions[model + ".NodeSet2.xml"] = new Dictionary<string, string>
                {
                    ["build_metadata.AdditionalFiles.ModelSourceGeneratorModelUri"] = "urn:test:" + model,
                    ["build_metadata.AdditionalFiles.ModelSourceGeneratorName"] = model,
                    ["build_metadata.AdditionalFiles.ModelSourceGeneratorPrefix"] = "Opc.Ua." + model
                };
            }
            AdditionalText baseModel = EmbeddedText.Create("IdentifierBase.NodeSet2.xml",
                kBaseModel.Replace("__Id__", identifier, StringComparison.Ordinal));
            AdditionalText derivedModel = EmbeddedText.Create("IdentifierDerived.NodeSet2.xml",
                kDerivedModel.Replace("__Id__", ownIdentifier, StringComparison.Ordinal));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new ModelSourceGenerator())
                .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.CSharp13))
                .WithUpdatedAnalyzerConfigOptions(options)
                .AddAdditionalTexts(derivedFirst ? [derivedModel, baseModel] : [baseModel, derivedModel]);
            driver.RunGeneratorsAndUpdateCompilation(
                compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
            Assert.That(diagnostics, Is.Empty, string.Join("\n", diagnostics));
            using var stream = new MemoryStream();
            var emitted = output.Emit(stream);
            Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
            Assembly assembly = Assembly.Load(stream.ToArray());
            MethodInfo probe = assembly.GetType("IdentifierProbe")!.GetMethod("Run")!;

            string result = (string)probe.Invoke(null, [identifier, ownIdentifier])!;

            Assert.That(result, Is.Empty);
        }

        /// <summary>The compiled loader probe uses shifted namespace indexes.</summary>
        private const string kProbe =
            """
            using System.Linq;
            using Opc.Ua;
            using Opc.Ua.IdentifierBase;
            using Opc.Ua.IdentifierDerived;

            public static class IdentifierProbe
            {
                public static string Run(string identifier, string ownIdentifier)
                {
                    var context = new SystemContext(null);
                    context.NamespaceUris = new NamespaceTable();
                    context.NamespaceUris.GetIndexOrAppend("urn:test:application");
                    ushort baseIndex = context.NamespaceUris.GetIndexOrAppend("urn:test:IdentifierBase");
                    ushort derivedIndex = context.NamespaceUris.GetIndexOrAppend("urn:test:IdentifierDerived");
                    var nodes = new NodeStateCollection();
                    nodes.AddOpcUaIdentifierBase(context);
                    nodes.AddOpcUaIdentifierDerived(context);
                    NodeState baseType = nodes.Single(node => node.NodeId == new NodeId(1001, baseIndex));
                    NodeState derivedType = nodes.Single(node => node.NodeId == new NodeId(1001, derivedIndex));
                    var inheritedName = new QualifiedName("InheritedValue", baseIndex);
                    NodeState original = baseType.FindChild(context, inheritedName);
                    NodeState inherited = derivedType.FindChild(context, inheritedName);
                    NodeState own = derivedType.FindChild(context, new QualifiedName("OwnValue", derivedIndex));
                    NodeState overridden = derivedType.FindChild(
                        context, new QualifiedName("OverrideValue", baseIndex));
                    if (original == null || inherited == null || own == null || overridden == null)
                    {
                        return "A required declaration is missing.";
                    }
                    if (original.NodeId != NodeId.Parse("ns=" + baseIndex + ";" + identifier) ||
                        own.NodeId != NodeId.Parse("ns=" + derivedIndex + ";" + ownIdentifier) ||
                        overridden.NodeId != new NodeId(6101, derivedIndex))
                    {
                        return "A published identifier changed: " +
                            original.NodeId + "/" + own.NodeId + "/" + overridden.NodeId;
                    }
                    if (inherited.NodeId.IsNull || inherited.NodeId.NamespaceIndex != derivedIndex ||
                        inherited.NodeId == own.NodeId || inherited.NodeId == overridden.NodeId ||
                        inherited.NodeId == new NodeId("DerivedDeviceType_InheritedValue_1", derivedIndex))
                    {
                        return "Inherited declaration borrowed a published identifier: " + inherited.NodeId;
                    }
                    NodeState installed = nodes.Single(node => node.NodeId == new NodeId(7001, derivedIndex));
                    NodeState installedValue = installed.FindChild(context, inheritedName);
                    if (installedValue == null ||
                        installedValue.NodeId != new NodeId(
                            Opc.Ua.IdentifierDerived.Variables.InstalledDevice_InheritedValue, derivedIndex) ||
                        installedValue.NodeId == inherited.NodeId ||
                        installedValue.NodeId == own.NodeId ||
                        installedValue.NodeId == overridden.NodeId ||
                        installedValue.NodeId == new NodeId("InstalledDevice_InheritedValue_1", derivedIndex))
                    {
                        return "The predefined instance lost its distinct generated identifier.";
                    }
                    return "";
                }
            }
            """;

        /// <summary>The source type declares two inherited variables with published identifiers.</summary>
        private const string kBaseModel =
            """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>urn:test:IdentifierBase</Uri></NamespaceUris>
              <Models>
                <Model ModelUri="urn:test:IdentifierBase" Version="1.0.0"
                    PublicationDate="2026-01-01T00:00:00Z">
                  <RequiredModel ModelUri="http://opcfoundation.org/UA/" />
                </Model>
              </Models>
              <UAObjectType NodeId="ns=1;i=1001" BrowseName="1:BaseDeviceType">
                <References>
                  <Reference ReferenceType="i=45" IsForward="false">i=58</Reference>
                  <Reference ReferenceType="i=47">ns=1;__Id__</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=6100</Reference>
                </References>
              </UAObjectType>
              <UAVariable NodeId="ns=1;__Id__" ParentNodeId="ns=1;i=1001"
                  BrowseName="1:InheritedValue" DataType="i=11">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1001</Reference>
                  <Reference ReferenceType="i=40">i=63</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                </References>
              </UAVariable>
              <UAVariable NodeId="ns=1;i=6100" ParentNodeId="ns=1;i=1001"
                  BrowseName="1:OverrideValue" DataType="i=11">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1001</Reference>
                  <Reference ReferenceType="i=40">i=63</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                </References>
              </UAVariable>
            </UANodeSet>
            """;

        /// <summary>The derived model legally reuses an identifier for an unrelated local member.</summary>
        private const string kDerivedModel =
            """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris>
                <Uri>urn:test:IdentifierDerived</Uri>
                <Uri>urn:test:IdentifierBase</Uri>
              </NamespaceUris>
              <Models>
                <Model ModelUri="urn:test:IdentifierDerived" Version="1.0.0"
                    PublicationDate="2026-01-01T00:00:00Z">
                  <RequiredModel ModelUri="http://opcfoundation.org/UA/" />
                  <RequiredModel ModelUri="urn:test:IdentifierBase" Version="1.0.0"
                      PublicationDate="2026-01-01T00:00:00Z" />
                </Model>
              </Models>
              <UAObjectType NodeId="ns=1;i=1001" BrowseName="1:DerivedDeviceType">
                <References>
                  <Reference ReferenceType="i=45" IsForward="false">ns=2;i=1001</Reference>
                  <Reference ReferenceType="i=47">ns=1;__Id__</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=6101</Reference>
                </References>
              </UAObjectType>
              <UAVariable NodeId="ns=1;__Id__" ParentNodeId="ns=1;i=1001"
                  BrowseName="1:OwnValue" DataType="i=11">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1001</Reference>
                  <Reference ReferenceType="i=40">i=63</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                </References>
              </UAVariable>
              <UAVariable NodeId="ns=1;i=6101" ParentNodeId="ns=1;i=1001"
                  BrowseName="2:OverrideValue" DataType="i=11">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1001</Reference>
                  <Reference ReferenceType="i=40">i=63</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                </References>
              </UAVariable>
              <UAObject NodeId="ns=1;i=7001" BrowseName="1:InstalledDevice">
                <References>
                  <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                  <Reference ReferenceType="i=40">ns=1;i=1001</Reference>
                </References>
              </UAObject>
              <UAObject NodeId="ns=1;s=DerivedDeviceType_InheritedValue_1" BrowseName="1:ReservedTypeSuffix">
                <References>
                  <Reference ReferenceType="i=40">i=58</Reference>
                </References>
              </UAObject>
              <UAObject NodeId="ns=1;s=InstalledDevice_InheritedValue_1" BrowseName="1:ReservedInstanceSuffix">
                <References>
                  <Reference ReferenceType="i=40">i=58</Reference>
                </References>
              </UAObject>
            </UANodeSet>
            """;
    }
}
