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
using System.Globalization;
using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Keeps published nested placeholder declarations without creating concrete placeholder instances.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class NestedPlaceholderDeclarationTests
    {
        /// <summary>
        /// Executes the generated loader and factory, rather than inspecting emitted implementation text.
        /// </summary>
        [TestCase(11508u)]
        [TestCase(11510u)]
        public void NestedPlaceholderDeclarationsRetainPublishedIdentifiers(uint modellingRule)
        {
            const string path = "NestedPlaceholders.NodeSet2.xml";
            CSharpCompilation compilation = OptimizationLevel.Release.CreateCompilation()
                .AddCode(new Dictionary<string, string>().WithOpcUaGeneratedStack(), LanguageVersion.CSharp13)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(kProbe, new CSharpParseOptions(LanguageVersion.CSharp13)));
            var options = new AnalyzerOptionsProvider(new Dictionary<string, string>
            {
                ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true",
                ["build_property.ModelSourceGeneratorOmitEventRecords"] = "true",
                ["build_property.ModelSourceGeneratorOmitObjectTypeProxies"] = "true"
            });
            options.TextOptions[path] = new Dictionary<string, string>
            {
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorModelUri"] = "urn:test:nested-placeholders",
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorName"] = "PlaceholderTest",
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorPrefix"] = "Opc.Ua.PlaceholderTest"
            };
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new ModelSourceGenerator())
                .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.CSharp13))
                .WithUpdatedAnalyzerConfigOptions(options)
                .AddAdditionalTexts(
                [
                    EmbeddedText.Create(
                        path,
                        kNodeSet.Replace(
                            "__RuleId__",
                            modellingRule.ToString(CultureInfo.InvariantCulture),
                            StringComparison.Ordinal))
                ]);
            driver.RunGeneratorsAndUpdateCompilation(
                compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
            Assert.That(diagnostics, Is.Empty, string.Join("\n", diagnostics));
            using var stream = new MemoryStream();
            var emitted = output.Emit(stream);
            Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
            Assembly assembly = Assembly.Load(stream.ToArray());
            MethodInfo probe = assembly.GetType("PlaceholderProbe")!.GetMethod("Run")!;
            string result = (string)probe.Invoke(null, null)!;
            Assert.That(result, Is.EqualTo("declaration=1302/1303;factory=none;predefined=none"));
        }

        /// <summary>The compiled public-API probe for declaration and concrete-instance behavior.</summary>
        private const string kProbe =
            """
            using System.Linq;
            using Opc.Ua;
            using Opc.Ua.PlaceholderTest;

            public static class PlaceholderProbe
            {
                public static string Run()
                {
                    var context = new SystemContext(null);
                    context.NamespaceUris = new NamespaceTable();
                    context.NamespaceUris.GetIndexOrAppend("urn:test:application");
                    ushort index = context.NamespaceUris.GetIndexOrAppend("urn:test:nested-placeholders");
                    var nodes = new NodeStateCollection();
                    nodes.AddOpcUaPlaceholderTest(context);
                    NodeState declaration = nodes.Single(node => node.NodeId == new NodeId(1002, index));
                    NodeState group = declaration.FindChild(context, new QualifiedName("Stage", index))
                        ?.FindChild(context, new QualifiedName("Group", index));
                    NodeState placeholder = group?.FindChild(context, new QualifiedName("<Item>", index));
                    NodeState reading = placeholder?.FindChild(context, new QualifiedName("Reading", index));
                    string declared = placeholder == null ? "placeholder missing" : "reading missing";
                    if (placeholder != null && reading != null &&
                        placeholder.NodeId.TryGetValue(out uint placeholderId) &&
                        reading.NodeId.TryGetValue(out uint readingId))
                    {
                        declared = placeholderId + "/" + readingId;
                    }

                    DeviceState instance = context.CreateInstanceOfDeviceType();
                    NodeState predefined = nodes.Single(node => node.NodeId == new NodeId(2000, index));
                    return "declaration=" + declared +
                        ";factory=" + GetPlaceholder(context, instance, index) +
                        ";predefined=" + GetPlaceholder(context, predefined, index);
                }

                private static string GetPlaceholder(ISystemContext context, NodeState instance, ushort index)
                {
                    NodeState instanceGroup = instance.FindChild(context, new QualifiedName("Stage", index))
                        ?.FindChild(context, new QualifiedName("Group", index));
                    return instanceGroup == null ? "group missing" :
                        instanceGroup.FindChild(context, new QualifiedName("<Item>", index)) == null
                            ? "none" : "placeholder";
                }
            }
            """;

        /// <summary>A small synthetic namespace reproducing the nested GDD declaration shape.</summary>
        private const string kNodeSet =
            """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>urn:test:nested-placeholders</Uri></NamespaceUris>
              <Models>
                <Model ModelUri="urn:test:nested-placeholders" Version="1.0.0"
                    PublicationDate="2026-01-01T00:00:00Z">
                  <RequiredModel ModelUri="http://opcfoundation.org/UA/" />
                </Model>
              </Models>
              <UAObjectType NodeId="ns=1;i=1002" BrowseName="1:DeviceType">
                <References>
                  <Reference ReferenceType="i=45" IsForward="false">i=58</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=1300</Reference>
                </References>
              </UAObjectType>
              <UAObject NodeId="ns=1;i=1300" ParentNodeId="ns=1;i=1002" BrowseName="1:Stage">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1002</Reference>
                  <Reference ReferenceType="i=40">i=58</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=1301</Reference>
                </References>
              </UAObject>
              <UAObject NodeId="ns=1;i=1301" ParentNodeId="ns=1;i=1300" BrowseName="1:Group">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1300</Reference>
                  <Reference ReferenceType="i=40">i=58</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=1302</Reference>
                </References>
              </UAObject>
              <UAObject NodeId="ns=1;i=1302" ParentNodeId="ns=1;i=1301"
                  BrowseName="1:&lt;Item&gt;" SymbolicName="Item">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1301</Reference>
                  <Reference ReferenceType="i=40">i=58</Reference>
                  <Reference ReferenceType="i=37">i=__RuleId__</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=1303</Reference>
                </References>
              </UAObject>
              <UAVariable NodeId="ns=1;i=1303" ParentNodeId="ns=1;i=1302"
                  BrowseName="1:Reading" DataType="i=7">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1302</Reference>
                  <Reference ReferenceType="i=40">i=63</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                </References>
              </UAVariable>
              <UAObject NodeId="ns=1;i=2000" BrowseName="1:InstalledDevice">
                <References>
                  <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                  <Reference ReferenceType="i=40">ns=1;i=1002</Reference>
                </References>
              </UAObject>
            </UANodeSet>
            """;
    }
}
