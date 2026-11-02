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
using System.IO;
using System.Linq;
using System.Reflection;
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

        /// <summary>
        /// Verifies that adding an optional subtree rebases a retained type reference to its mandatory child.
        /// </summary>
        [Test]
        public void AddingOptionalSubtreeRebasesTheRetainedReference()
        {
            GeneratorDriver driver = GenerateModel(out Compilation output);
            output.GetDiagnostics().Check(TestContext.Out, out int errors, out _);
            Assert.That(errors, Is.Zero);

            output = output.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                """
                using System.Collections.Generic;
                using Opc.Ua;
                using Test.InstanceReferences;

                public static class OptionalChildProbe
                {
                    public static bool Run()
                    {
                        var context = new SystemContext(null);
                        context.NamespaceUris = new NamespaceTable();
                        ushort namespaceIndex = context.NamespaceUris.GetIndexOrAppend(
                            "http://test.org/UA/InstanceReferences/");
                        context.NodeIdFactory = new ProbeNodeIdFactory(namespaceIndex);
                        var state = context.CreateInstanceOfDeviceType();
                        state.Create(context, new NodeId("Device", namespaceIndex),
                            new QualifiedName("Device", namespaceIndex), new LocalizedText("Device"), false);
                        state.AddBlocks(context);
                        FolderState blocks = state.Blocks;
                        var children = new List<BaseInstanceState>();
                        blocks.GetChildren(context, children);
                        NodeState block = null;
                        foreach (BaseInstanceState child in children)
                        {
                            if (child.BrowseName.Name == "Block")
                            {
                                block = child;
                                break;
                            }
                        }
                        if (block == null)
                        {
                            return false;
                        }
                        var references = new List<IReference>();
                        state.GetReferences(context, references);
                        foreach (IReference reference in references)
                        {
                            if (reference.ReferenceTypeId == new NodeId(17604) &&
                                ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris) == block.NodeId)
                            {
                                return true;
                            }
                        }
                        return false;
                    }

                    private sealed class ProbeNodeIdFactory : INodeIdFactory
                    {
                        private uint m_next = 1000;
                        private readonly ushort m_namespaceIndex;

                        public ProbeNodeIdFactory(ushort namespaceIndex)
                        {
                            m_namespaceIndex = namespaceIndex;
                        }

                        public NodeId New(ISystemContext context, NodeState node)
                        {
                            return new NodeId(m_next++, m_namespaceIndex);
                        }
                    }
                }
                """,
                new CSharpParseOptions(LanguageVersion.CSharp11)));

            using var stream = new MemoryStream();
            var emitted = output.Emit(stream);
            Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
            Assembly assembly = Assembly.Load(stream.ToArray());
            bool preserved = (bool)assembly.GetType("OptionalChildProbe")!.GetMethod("Run")!
                .Invoke(null, null)!;
            Assert.That(preserved, Is.True);
        }

        /// <summary>
        /// Semantic references accumulate through type inheritance and declaration
        /// overrides without copying dictionary objects or inheriting inverse references.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void InheritedSemanticReferencesSurviveDeclarationOverridesAndNamespaceRemapping(bool initialize)
        {
            GenerateModel(out Compilation output, kSemanticModel);
            output.GetDiagnostics().Check(TestContext.Out, out int errors, out _);
            Assert.That(errors, Is.Zero);
            output = output.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                """
                using System.Collections.Generic;
                using System.Linq;
                using Opc.Ua;
                using Test.InstanceReferences;

                public static class SemanticReferenceProbe
                {
                    public static string Run(bool initialize)
                    {
                        var context = new SystemContext(null);
                        context.NamespaceUris = new NamespaceTable();
                        context.NamespaceUris.GetIndexOrAppend("urn:unrelated:first");
                        context.NamespaceUris.GetIndexOrAppend("urn:unrelated:second");
                        ushort ns = context.NamespaceUris.GetIndexOrAppend(
                            "http://test.org/UA/InstanceReferences/");
                        var state = context.CreateInstanceOfDerivedDeviceType();
                        if (initialize)
                        {
                            state.Create(context, new NodeId("Device", ns),
                                new QualifiedName("Device", ns), new LocalizedText("Device"), false);
                        }
                        string error = Check(context, state, ns, "RootBase", "RootDerived");
                        if (error.Length != 0)
                        {
                            return "Root: " + error;
                        }
                        var value = state.FindChild(context, new QualifiedName("Value", ns));
                        if (value == null)
                        {
                            return "Mandatory inherited child missing";
                        }
                        error = Check(context, value, ns, "VariableType", "DeclarationBase", "DeclarationOverride");
                        if (error.Length != 0)
                        {
                            return "Override: " + error;
                        }
                        if (state.FindChild(context, new QualifiedName("Optional", ns)) != null)
                        {
                            return "Unrequested optional declaration instantiated";
                        }
                        state.AddOptional(context);
                        var optional = state.FindChild(context, new QualifiedName("Optional", ns));
                        if (optional == null)
                        {
                            return "Requested optional declaration missing";
                        }
                        error = Check(context, optional, ns, "VariableType", "Optional");
                        if (error.Length != 0)
                        {
                            return "Optional: " + error;
                        }
                        var entry = context.CreateRootBase();
                        if (entry.NodeId != new NodeId("0112/2///61987#RootBase", ns) ||
                            entry.DisplayName.Text != "Preserved IRDI display name")
                        {
                            return "Dictionary entry identity or display name changed";
                        }
                        var children = new List<BaseInstanceState>();
                        state.GetChildren(context, children);
                        if (children.Count != 2)
                        {
                            return "Semantic targets were copied into the instance";
                        }
                        return "";
                    }

                    private static string Check(ISystemContext context, NodeState node, ushort ns,
                        params string[] expectedEntries)
                    {
                        var references = new List<IReference>();
                        node.GetReferences(context, references);
                        var entries = references.Where(r => r.ReferenceTypeId == new NodeId(17597)).ToArray();
                        var expected = expectedEntries.Select(s => new NodeId("0112/2///61987#" + s, ns));
                        if (entries.Any(r => r.IsInverse) ||
                            !entries.Select(r => ExpandedNodeId.ToNodeId(r.TargetId, context.NamespaceUris))
                                .OrderBy(id => id.ToString()).SequenceEqual(expected.OrderBy(id => id.ToString())))
                        {
                            return "Dictionary entries: " + string.Join(",",
                                entries.Select(r => r.TargetId.ToString()));
                        }
                        var interfaces = references.Where(r => r.ReferenceTypeId == new NodeId(17603)).ToArray();
                        if (interfaces.Length != 1 || interfaces[0].IsInverse ||
                            ExpandedNodeId.ToNodeId(interfaces[0].TargetId, context.NamespaceUris) !=
                                new NodeId(17602))
                        {
                            return "Inherited interface missing or duplicated";
                        }
                        if (references.Any(r => r.ReferenceTypeId == new NodeId(41)))
                        {
                            return "Unrelated non-hierarchical reference inherited";
                        }
                        return "";
                    }
                }
                """,
                new CSharpParseOptions(LanguageVersion.CSharp11)));

            using var stream = new MemoryStream();
            var emitted = output.Emit(stream);
            Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
            Assembly assembly = Assembly.Load(stream.ToArray());
            string result = (string)assembly.GetType("SemanticReferenceProbe")!.GetMethod("Run")!
                .Invoke(null, [initialize])!;
            Assert.That(result, Is.Empty);
        }

        private static GeneratorDriver GenerateModel(out Compilation output, string model = kModel)
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
                .AddAdditionalTexts([EmbeddedText.Create("InstanceReferences.xml", model)])
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

        private const string kSemanticModel = """
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
              <opc:Object SymbolicName="RootBase" StringId="0112/2///61987#RootBase"
                  TypeDefinition="ua:IrdiDictionaryEntryType">
                <opc:DisplayName>Preserved IRDI display name</opc:DisplayName>
              </opc:Object>
              <opc:Object SymbolicName="RootDerived" StringId="0112/2///61987#RootDerived"
                  TypeDefinition="ua:IrdiDictionaryEntryType" />
              <opc:Object SymbolicName="VariableType" StringId="0112/2///61987#VariableType"
                  TypeDefinition="ua:IrdiDictionaryEntryType" />
              <opc:Object SymbolicName="DeclarationBase" StringId="0112/2///61987#DeclarationBase"
                  TypeDefinition="ua:IrdiDictionaryEntryType" />
              <opc:Object SymbolicName="DeclarationOverride" StringId="0112/2///61987#DeclarationOverride"
                  TypeDefinition="ua:IrdiDictionaryEntryType" />
              <opc:Object SymbolicName="OptionalEntry" StringId="0112/2///61987#Optional"
                  TypeDefinition="ua:IrdiDictionaryEntryType" />
              <opc:VariableType SymbolicName="SemanticVariableType" BaseType="ua:BaseDataVariableType"
                  DataType="ua:Double">
                <opc:References>
                  <opc:Reference>
                    <opc:ReferenceType>ua:HasDictionaryEntry</opc:ReferenceType>
                    <opc:TargetId>VariableType</opc:TargetId>
                  </opc:Reference>
                  <opc:Reference>
                    <opc:ReferenceType>ua:HasInterface</opc:ReferenceType>
                    <opc:TargetId>ua:BaseInterfaceType</opc:TargetId>
                  </opc:Reference>
                </opc:References>
              </opc:VariableType>
              <opc:ObjectType SymbolicName="BaseDeviceType" BaseType="ua:BaseObjectType">
                <opc:Children>
                  <opc:Variable SymbolicName="Value" TypeDefinition="SemanticVariableType"
                      ModellingRule="Mandatory">
                    <opc:References>
                      <opc:Reference>
                        <opc:ReferenceType>ua:HasDictionaryEntry</opc:ReferenceType>
                        <opc:TargetId>DeclarationBase</opc:TargetId>
                      </opc:Reference>
                    </opc:References>
                  </opc:Variable>
                  <opc:Variable SymbolicName="Optional" TypeDefinition="SemanticVariableType"
                      ModellingRule="Optional">
                    <opc:References>
                      <opc:Reference>
                        <opc:ReferenceType>ua:HasDictionaryEntry</opc:ReferenceType>
                        <opc:TargetId>OptionalEntry</opc:TargetId>
                      </opc:Reference>
                    </opc:References>
                  </opc:Variable>
                </opc:Children>
                <opc:References>
                  <opc:Reference>
                    <opc:ReferenceType>ua:HasInterface</opc:ReferenceType>
                    <opc:TargetId>ua:BaseInterfaceType</opc:TargetId>
                  </opc:Reference>
                  <opc:Reference>
                    <opc:ReferenceType>ua:HasDictionaryEntry</opc:ReferenceType>
                    <opc:TargetId>RootBase</opc:TargetId>
                  </opc:Reference>
                  <opc:Reference IsInverse="true">
                    <opc:ReferenceType>ua:HasDictionaryEntry</opc:ReferenceType>
                    <opc:TargetId>RootDerived</opc:TargetId>
                  </opc:Reference>
                  <opc:Reference>
                    <opc:ReferenceType>ua:GeneratesEvent</opc:ReferenceType>
                    <opc:TargetId>ua:BaseEventType</opc:TargetId>
                  </opc:Reference>
                </opc:References>
              </opc:ObjectType>
              <opc:ObjectType SymbolicName="DerivedDeviceType" BaseType="BaseDeviceType">
                <opc:Children>
                  <opc:Variable SymbolicName="Value" TypeDefinition="SemanticVariableType"
                      ModellingRule="Mandatory">
                    <opc:References>
                      <opc:Reference>
                        <opc:ReferenceType>ua:HasDictionaryEntry</opc:ReferenceType>
                        <opc:TargetId>DeclarationOverride</opc:TargetId>
                      </opc:Reference>
                    </opc:References>
                  </opc:Variable>
                </opc:Children>
                <opc:References>
                  <opc:Reference>
                    <opc:ReferenceType>ua:HasDictionaryEntry</opc:ReferenceType>
                    <opc:TargetId>RootDerived</opc:TargetId>
                  </opc:Reference>
                </opc:References>
              </opc:ObjectType>
            </opc:ModelDesign>
            """;

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
