/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema.Model;
using Opc.Ua.SourceGeneration.Api.Tests;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the NodeStateGenerator class.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeStateGeneratorTests
    {
        [SetUp]
        public void SetUp()
        {
            m_mockFileSystem = new Mock<IFileSystem>();
            m_mockModelDesign = new Mock<IModelDesign>();
            m_mockTelemetry = new Mock<ITelemetryContext>();

            // Setup default namespace
            var targetNamespace = new Namespace
            {
                Value = "http://test.org/UA/",
                Prefix = "Test",
                Name = "TestNamespace"
            };
            m_mockModelDesign.Setup(m => m.TargetNamespace).Returns(targetNamespace);
            m_mockModelDesign.Setup(m => m.Namespaces).Returns([targetNamespace]);
        }

        /// <summary>
        /// Tests that the constructor throws ArgumentNullException when context is null.
        /// </summary>
        [Test]
        public void Constructor_NullContext_ThrowsArgumentNullException()
        {
            // Arrange
            GeneratorContext context = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new NodeStateGenerator(context));
        }

        /// <summary>
        /// Tests that constructor creates instance with valid context.
        /// </summary>
        [Test]
        public void Constructor_ValidContext_CreatesInstance()
        {
            // Arrange
            m_context = new GeneratorContext
            {
                FileSystem = m_mockFileSystem.Object,
                OutputFolder = "TestOutput",
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            // Act
            var generator = new NodeStateGenerator(m_context);

            // Assert
            Assert.That(generator, Is.Not.Null);
        }

        [TestCase(ModellingRule.Mandatory, true)]
        [TestCase(ModellingRule.Optional, true)]
        [TestCase(ModellingRule.None, false)]
        [TestCase(ModellingRule.OptionalPlaceholder, false)]
        public void HasFixedChildSlotRequiresFixedModellingRule(
            ModellingRule modellingRule,
            bool expected)
        {
            var type = new ObjectTypeDesign
            {
                Children = new ListOfChildren
                {
                    Items =
                    [
                        new VariableDesign
                        {
                            SymbolicName = new System.Xml.XmlQualifiedName(
                                "DefaultInstanceBrowseName",
                                Types.Namespaces.OpcUa),
                            ModellingRule = modellingRule
                        }
                    ]
                }
            };

            Assert.That(
                NodeStateGenerator.HasFixedChildSlot(
                    type,
                    "DefaultInstanceBrowseName"),
                Is.EqualTo(expected));
        }

        /// <summary>
        /// A subtype that declares its own state elements reports the
        /// namespace of its declaring model.
        /// </summary>
        [TestCase("StateType")]
        [TestCase("InitialStateType")]
        [TestCase("TransitionType")]
        public void FindElementNamespaceUriUsesTheDeclaringTypesNamespace(
            string elementTypeName)
        {
            const string vendorNs = "http://vendor.test/UA/";
            ObjectTypeDesign machine = CreateFsmSubtype(
                vendorNs, DeclareElement(elementTypeName));

            Assert.Multiple(() =>
            {
                Assert.That(
                    NodeStateGenerator.IsFiniteStateMachineSubtype(machine),
                    Is.True);
                Assert.That(
                    NodeStateGenerator.FindElementNamespaceUri(machine),
                    Is.EqualTo(vendorNs));
            });
        }

        /// <summary>
        /// A behaviour-only subtype inherits its elements — and their
        /// namespace — from the nearest base that declares them.
        /// </summary>
        [Test]
        public void FindElementNamespaceUriWalksUpToTheDeclaringBase()
        {
            const string baseNs = "http://base.test/UA/";
            const string derivedNs = "http://derived.test/UA/";
            ObjectTypeDesign baseMachine = CreateFsmSubtype(
                baseNs, DeclareElement("StateType"));
            var derived = new ObjectTypeDesign
            {
                SymbolicName = new System.Xml.XmlQualifiedName(
                    "DerivedMachineType", derivedNs),
                BaseTypeNode = baseMachine
            };

            Assert.Multiple(() =>
            {
                Assert.That(
                    NodeStateGenerator.IsFiniteStateMachineSubtype(derived),
                    Is.True);
                Assert.That(
                    NodeStateGenerator.FindElementNamespaceUri(derived),
                    Is.EqualTo(baseNs));
            });
        }

        /// <summary>
        /// A subtype whose elements are declared by the standard model
        /// resolves to the OPC UA namespace — for which no override is
        /// emitted, because the base class already returns it.
        /// </summary>
        [Test]
        public void FindElementNamespaceUriResolvesInheritedStandardElements()
        {
            ObjectTypeDesign standardMachine = CreateFsmSubtype(
                Types.Namespaces.OpcUa, DeclareElement("StateType"));
            var vendorSubtype = new ObjectTypeDesign
            {
                SymbolicName = new System.Xml.XmlQualifiedName(
                    "VendorProgramType", "http://vendor.test/UA/"),
                BaseTypeNode = standardMachine
            };

            Assert.That(
                NodeStateGenerator.FindElementNamespaceUri(vendorSubtype),
                Is.EqualTo(Types.Namespaces.OpcUa));
        }

        /// <summary>
        /// Types outside the FiniteStateMachineType hierarchy get no
        /// override, and a hierarchy with no declared elements resolves
        /// to nothing.
        /// </summary>
        [Test]
        public void FindElementNamespaceUriIgnoresNonStateMachineTypes()
        {
            var plainType = new ObjectTypeDesign
            {
                SymbolicName = new System.Xml.XmlQualifiedName(
                    "PlainType", "http://vendor.test/UA/")
            };
            ObjectTypeDesign elementFreeMachine = CreateFsmSubtype(
                "http://vendor.test/UA/", children: null);

            Assert.Multiple(() =>
            {
                Assert.That(
                    NodeStateGenerator.IsFiniteStateMachineSubtype(plainType),
                    Is.False);
                Assert.That(
                    NodeStateGenerator.IsFiniteStateMachineSubtype(elementFreeMachine),
                    Is.True);
                Assert.That(
                    NodeStateGenerator.FindElementNamespaceUri(elementFreeMachine),
                    Is.Null);
            });
        }

        /// <summary>
        /// A subtype that merely re-declares an inherited state (to
        /// attach a description or modelling rule) is not the type that
        /// declares the elements — the namespace walk must continue to
        /// its base.
        /// </summary>
        [Test]
        public void FindElementNamespaceUriSkipsOverriddenElements()
        {
            const string baseNs = "http://base.test/UA/";
            ObjectTypeDesign baseMachine = CreateFsmSubtype(
                baseNs, DeclareElement("StateType"));
            ObjectDesign overriddenState = DeclareElement("StateType");
            overriddenState.OveriddenNode = DeclareElement("StateType");
            var derived = new ObjectTypeDesign
            {
                SymbolicName = new System.Xml.XmlQualifiedName(
                    "OverridingMachineType", "http://derived.test/UA/"),
                BaseTypeNode = baseMachine,
                Children = new ListOfChildren { Items = [overriddenState] }
            };

            Assert.That(
                NodeStateGenerator.FindElementNamespaceUri(derived),
                Is.EqualTo(baseNs));
        }

        /// <summary>
        /// Builds a subtype of the standard FiniteStateMachineType in
        /// <paramref name="namespaceUri"/>, optionally declaring
        /// state-machine element children.
        /// </summary>
        private static ObjectTypeDesign CreateFsmSubtype(
            string namespaceUri,
            params InstanceDesign[] children)
        {
            var fsmRoot = new ObjectTypeDesign
            {
                SymbolicName = new System.Xml.XmlQualifiedName(
                    "FiniteStateMachineType", Types.Namespaces.OpcUa)
            };
            return new ObjectTypeDesign
            {
                SymbolicName = new System.Xml.XmlQualifiedName(
                    "TestMachineType", namespaceUri),
                BaseTypeNode = fsmRoot,
                Children = children == null || children.Length == 0
                    ? null
                    : new ListOfChildren { Items = children }
            };
        }

        private static ObjectDesign DeclareElement(string elementTypeName)
        {
            return new ObjectDesign
            {
                SymbolicName = new System.Xml.XmlQualifiedName(
                    "SomeElement", "http://vendor.test/UA/"),
                TypeDefinition = new System.Xml.XmlQualifiedName(
                    elementTypeName, Types.Namespaces.OpcUa)
            };
        }

        /// <summary>
        /// Tests that Emit returns early without creating files when no node state classes exist.
        /// </summary>
        [Test]
        public void Emit_NoNodeStateClasses_ReturnsEarlyWithoutCreatingFiles()
        {
            // Arrange
            m_mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([]);

            m_context = new GeneratorContext
            {
                FileSystem = m_mockFileSystem.Object,
                OutputFolder = "TestOutput",
                ModelDesign = m_mockModelDesign.Object,
                Telemetry = m_mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            var generator = new NodeStateGenerator(m_context);

            // Act
            generator.Emit();

            // Assert - OpenWrite should not be called when there are no node state classes
            m_mockFileSystem.Verify(
                fs => fs.OpenWrite(It.IsAny<string>()),
                Times.Never,
                "OpenWrite should not be called when there are no node state classes");
        }

        [Test]
        public void GenerateNodeStateGeneratorCodeTest()
        {
            // Arrange
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var fileSystem = new VirtualFileSystem();

            // Act - Generate full stack
            Generators.GenerateStack(StackGenerationType.All, fileSystem, string.Empty, telemetry);

            // Assert - NodeState file should be created
            var generatedFiles = fileSystem.CreatedFiles
                .Where(c => c.EndsWith(".NodeStates.ex.g.cs", StringComparison.Ordinal))
                .ToList();

            Assert.That(generatedFiles, Is.Not.Empty,
                "NodeStates.ex.g.cs file should be generated");

            foreach (string file in generatedFiles)
            {
                string content = Encoding.UTF8.GetString(fileSystem.Get(file));
                TestContext.Out.WriteLine("Generated file: {0} ({1} bytes)", file, content.Length);

                // Verify basic structure
                Assert.That(content, Does.Contain("// <auto-generated />"),
                    "Generated code should have auto-generated header");
                Assert.That(content, Does.Contain("public static partial class OpcUaExtensions"),
                    "Generated code should contain OpcUaExtensions class");
                Assert.That(content, Does.Contain("public static global::Opc.Ua.NodeStateCollection AddOpcUa"),
                    "Generated code should contain AddOpcUa method");
            }
        }

        [Test]
        public void GeneratedNodeStateGeneratorCodeCompilesTest()
        {
            // Arrange
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var fileSystem = new VirtualFileSystem();

            // Act - Generate stack. The test compilation only provides
            // Core stubs via WithOpcUaCoreStubs(), no Opc.Ua.Server
            // reference, so suppress fluent-builder emission.
            Generators.GenerateStack(StackGenerationType.All, fileSystem, string.Empty, telemetry,
                new GeneratorOptions
                {
                    OmitFluentApi = true,
                    OmitEventRecords = true
                });

            // Get all generated C# files
            var generatedText = fileSystem.CreatedFiles
                .Where(c => Path.GetExtension(c) == ".cs")
                .ToDictionary(c => c, c => Encoding.UTF8.GetString(fileSystem.Get(c)));

            // Verify generated code compiles
            using var peStream = new MemoryStream();
            bool success = OptimizationLevel.Debug
                .CreateCompilation()
                .AddCode(generatedText.WithOpcUaCoreStubs(), LanguageVersion.Latest)
                .Emit(peStream)
                .Check(TestContext.Out, out int errorCount, out int warnCount);

            // Assert
            Assert.That(success, Is.True,
                $"Generated NodeStates should compile without errors. Errors: {errorCount}, Warnings: {warnCount}");
        }

        [Test]
        public void NodeSetMethodArgumentsGenerateTypedDelegatesResultsAndPlumbing()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            IModelDesign model = OpenNodeSetModel(
                "TypedMethodArguments.NodeSet2.xml",
                telemetry);
            ObjectTypeDesign controllerDesign = model.Nodes
                .OfType<ObjectTypeDesign>()
                .Single(type => type.SymbolicId.Name == "ControllerType");
            MethodDesign executeDesign = controllerDesign.Children.Items
                .OfType<MethodDesign>()
                .Single(method => method.BrowseName == "Execute");
            MethodDesign calibrateDesign = controllerDesign.Children.Items
                .OfType<MethodDesign>()
                .Single(method => method.BrowseName == "Calibrate");
            Assert.Multiple(() =>
            {
                Assert.That(
                    MethodDesignArgumentResolver.ResolveMethodInputs(executeDesign),
                    Has.Length.EqualTo(4));
                Assert.That(
                    MethodDesignArgumentResolver.ResolveMethodOutputs(executeDesign),
                    Has.Length.EqualTo(4));
                Assert.That(
                    MethodDesignArgumentResolver.ResolveMethodInputs(calibrateDesign),
                    Has.Length.EqualTo(1));
                Assert.That(
                    MethodDesignArgumentResolver.ResolveMethodOutputs(calibrateDesign),
                    Has.Length.EqualTo(1));
            });

            Dictionary<string, string> files = GenerateFromNodeSet(
                "TypedMethodArguments.NodeSet2.xml",
                telemetry);
            string nodeStates = files.Single(
                kv => kv.Key.EndsWith(".NodeStates.g.cs", StringComparison.Ordinal)).Value;

            Assert.Multiple(() =>
            {
                Assert.That(nodeStates, Does.Contain(
                    "_inputArguments[0].TryGetValue(out string name);"));
                Assert.That(nodeStates, Does.Contain(
                    "_inputArguments[1].TryGetValue(out global::Opc.Ua.NodeId targetId);"));
                Assert.That(nodeStates, Does.Contain(
                    "_inputArguments[2].TryGetValue(out short mode);"));
                Assert.That(nodeStates, Does.Contain(
                    "_inputArguments[3].TryGetValue(out " +
                    "global::Opc.Ua.ArrayOf<global::Opc.Ua.NodeId> targets);"));
                Assert.That(nodeStates, Does.Contain(
                    "_outputArguments[0] = global::Opc.Ua.Variant.From(_result.Status);"));
                Assert.That(nodeStates, Does.Contain(
                    "_outputArguments[3] = global::Opc.Ua.Variant.From(_result.RelatedIds);"));
                Assert.That(nodeStates, Does.Not.Contain("class PingMethodState"));
            });

            Assembly assembly = CompileGeneratedAssembly(files);
            Type executeDelegate = GetGeneratedType(
                assembly,
                "ExecuteMethodStateMethodCallHandler");
            ParameterInfo[] executeParameters =
                executeDelegate.GetMethod("Invoke")!.GetParameters();

            Assert.Multiple(() =>
            {
                Assert.That(executeParameters, Has.Length.EqualTo(11));
                Assert.That(executeParameters[3].ParameterType, Is.EqualTo(typeof(string)));
                Assert.That(executeParameters[4].ParameterType.FullName, Is.EqualTo("Opc.Ua.NodeId"));
                Assert.That(executeParameters[5].ParameterType, Is.EqualTo(typeof(short)));
                AssertArrayOfNodeIds(executeParameters[6].ParameterType);
                Assert.That(executeParameters[7].ParameterType, Is.EqualTo(typeof(short).MakeByRefType()));
                Assert.That(
                    executeParameters[8].ParameterType.GetElementType()?.FullName,
                    Is.EqualTo("Opc.Ua.NodeId"));
                Assert.That(
                    executeParameters[9].ParameterType,
                    Is.EqualTo(typeof(string).MakeByRefType()));
                AssertArrayOfNodeIds(executeParameters[10].ParameterType.GetElementType());
            });

            Type asyncDelegate = GetGeneratedType(
                assembly,
                "ExecuteMethodStateMethodAsyncCallHandler");
            MethodInfo asyncInvoke = asyncDelegate.GetMethod("Invoke")!;
            Assert.Multiple(() =>
            {
                Assert.That(asyncInvoke.GetParameters(), Has.Length.EqualTo(8));
                Assert.That(asyncInvoke.ReturnType.IsGenericType, Is.True);
                Assert.That(
                    asyncInvoke.ReturnType.GetGenericTypeDefinition(),
                    Is.EqualTo(typeof(ValueTask<>)));
                Assert.That(
                    asyncInvoke.ReturnType.GetGenericArguments()[0].Name,
                    Is.EqualTo("ExecuteMethodStateResult"));
            });

            Type result = GetGeneratedType(assembly, "ExecuteMethodStateResult");
            Assert.Multiple(() =>
            {
                Assert.That(result.GetProperty("ServiceResult"), Is.Not.Null);
                Assert.That(result.GetProperty("Status")?.PropertyType, Is.EqualTo(typeof(short)));
                Assert.That(
                    result.GetProperty("SelectedId")?.PropertyType.FullName,
                    Is.EqualTo("Opc.Ua.NodeId"));
                Assert.That(result.GetProperty("Message")?.PropertyType, Is.EqualTo(typeof(string)));
                AssertArrayOfNodeIds(result.GetProperty("RelatedIds")?.PropertyType);
            });

            Type controller = GetGeneratedType(assembly, "ControllerState");
            Assert.Multiple(() =>
            {
                Assert.That(
                    controller.GetProperty("Calibrate")?.PropertyType.Name,
                    Is.EqualTo("SharedCalibrationMethodState"));
                Assert.That(
                    controller.GetProperty("Ping")?.PropertyType.FullName,
                    Is.EqualTo("Opc.Ua.MethodState"));
            });
        }

        [Test]
        public void DuplicateMethodNamesReuseOnlyMatchingSignatures()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            IModelDesign focusedModel = OpenNodeSetModel(
                "DuplicateMethodNames.NodeSet2.xml",
                telemetry);
            MethodDesign alpha = GetMethod(focusedModel, "AlphaType", "Execute");
            MethodDesign beta = GetMethod(focusedModel, "BetaType", "Execute");
            MethodDesign gamma = GetMethod(focusedModel, "GammaType", "Execute");

            Assert.Multiple(() =>
            {
                Assert.That(alpha.MethodDeclarationNode, Is.SameAs(beta.MethodDeclarationNode));
                Assert.That(gamma.MethodDeclarationNode, Is.Not.SameAs(alpha.MethodDeclarationNode));
                Assert.That(
                    alpha.MethodDeclarationNode.SymbolicId.Name,
                    Is.EqualTo("ExecuteAlphaTypeMethodType"));
                Assert.That(
                    gamma.MethodDeclarationNode.SymbolicId.Name,
                    Is.EqualTo("ExecuteGammaTypeMethodType"));
            });

            Dictionary<string, string> files = GenerateFromNodeSet(
                "DuplicateMethodNames.NodeSet2.xml",
                telemetry);
            Assembly assembly = CompileGeneratedAssembly(files);
            Type sharedDelegate = GetGeneratedType(
                assembly,
                "ExecuteAlphaTypeMethodStateMethodCallHandler");
            Type stringDelegate = GetGeneratedType(
                assembly,
                "ExecuteGammaTypeMethodStateMethodCallHandler");
            Type alphaState = GetGeneratedType(assembly, "AlphaState");
            Type betaState = GetGeneratedType(assembly, "BetaState");
            Type gammaState = GetGeneratedType(assembly, "GammaState");

            Assert.Multiple(() =>
            {
                Assert.That(
                    sharedDelegate.GetMethod("Invoke")!.GetParameters()[3].ParameterType,
                    Is.EqualTo(typeof(int)));
                Assert.That(
                    stringDelegate.GetMethod("Invoke")!.GetParameters()[3].ParameterType,
                    Is.EqualTo(typeof(string)));
                Assert.That(
                    alphaState.GetProperty("Execute")?.PropertyType.Name,
                    Is.EqualTo("ExecuteAlphaTypeMethodState"));
                Assert.That(
                    betaState.GetProperty("Execute")?.PropertyType.Name,
                    Is.EqualTo("ExecuteAlphaTypeMethodState"));
                Assert.That(
                    gammaState.GetProperty("Execute")?.PropertyType.Name,
                    Is.EqualTo("ExecuteGammaTypeMethodState"));
            });
        }

        [Test]
        public void DiGetUpdateBehaviorMethodsUseDistinctDeclarations()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            IModelDesign model = OpenNodeSetModel("Opc.Ua.Di.NodeSet2.xml", telemetry);
            MethodDesign cached = GetMethod(model, "CachedLoadingType", "GetUpdateBehavior");
            MethodDesign fileSystem = GetMethod(
                model,
                "FileSystemLoadingType",
                "GetUpdateBehavior");

            Assert.Multiple(() =>
            {
                Assert.That(cached.MethodDeclarationNode, Is.Not.Null);
                Assert.That(fileSystem.MethodDeclarationNode, Is.Not.Null);
                Assert.That(
                    cached.MethodDeclarationNode,
                    Is.Not.SameAs(fileSystem.MethodDeclarationNode));
                Assert.That(
                    cached.MethodDeclarationNode.SymbolicId.Name,
                    Is.EqualTo("GetUpdateBehaviorCachedLoadingTypeMethodType"));
                Assert.That(
                    fileSystem.MethodDeclarationNode.SymbolicId.Name,
                    Is.EqualTo("GetUpdateBehaviorFileSystemLoadingTypeMethodType"));
                Assert.That(
                    MethodDesignArgumentResolver.ResolveMethodInputs(cached),
                    Has.Length.EqualTo(3));
                Assert.That(
                    MethodDesignArgumentResolver.ResolveMethodInputs(fileSystem),
                    Has.Length.EqualTo(1));
            });
        }

        [Test]
        public void DerivedMethodTypeOverrideUsesStringDefinitionEverywhere()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            IModelDesign model = OpenModelDesign(
                "MethodTypeOverride.ModelDesign.xml",
                telemetry);
            ObjectTypeDesign derivedType = model.Nodes
                .OfType<ObjectTypeDesign>()
                .Single(type => type.SymbolicId.Name == "StringControllerType");
            MethodDesign stringMethodType = model.Nodes
                .OfType<MethodDesign>()
                .Single(method => method.SymbolicId.Name == "StringConvertMethodType");
            Assert.That(stringMethodType.InputArguments, Has.Length.EqualTo(1));
            Assert.That(stringMethodType.OutputArguments, Has.Length.EqualTo(1));
            MethodDesign mergedMethod = (MethodDesign)derivedType.Hierarchy.Nodes["Convert"].Instance;
            Parameter[] inputs =
                MethodDesignArgumentResolver.ResolveMethodInputs(mergedMethod);
            Parameter[] outputs =
                MethodDesignArgumentResolver.ResolveMethodOutputs(mergedMethod);
            Assert.That(inputs, Has.Length.EqualTo(1));
            Assert.That(outputs, Has.Length.EqualTo(1));

            Assert.Multiple(() =>
            {
                Assert.That(
                    mergedMethod.TypeDefinition.Name,
                    Is.EqualTo("StringConvertMethodType"));
                Assert.That(
                    mergedMethod.MethodType.SymbolicId.Name,
                    Is.EqualTo("StringConvertMethodType"));
                Assert.That(
                    mergedMethod.MethodDeclarationNode.SymbolicId.Name,
                    Is.EqualTo("StringControllerType_Convert"));
                Assert.That(
                    mergedMethod.MethodDeclarationNode,
                    Is.SameAs(mergedMethod));
                Assert.That(mergedMethod.NumericIdSpecified, Is.True);
                Assert.That(
                    inputs[0].DataType.Name,
                    Is.EqualTo("String"));
                Assert.That(
                    outputs[0].DataType.Name,
                    Is.EqualTo("String"));
            });

            Dictionary<string, string> files = GenerateFromModelDesign(
                "MethodTypeOverride.ModelDesign.xml",
                telemetry);
            string extensions = files.Single(
                file => file.Key.EndsWith(".NodeStates.ex.g.cs", StringComparison.Ordinal)).Value;
            string factory = ExtractMethodBody(
                extensions,
                "CreateStringControllerType_Convert");
            Assert.Multiple(() =>
            {
                Assert.That(
                    factory,
                    Does.Contain("global::MethodTypeOverride.StringConvertMethodState"));
                Assert.That(
                    factory,
                    Does.Contain(
                        $"state.MethodDeclarationId = global::Opc.Ua.NodeId.Create({mergedMethod.NumericId}u"));
                Assert.That(
                    factory,
                    Does.Not.Contain("global::MethodTypeOverride.IntegerConvertMethodState"));
            });

            Assembly assembly = CompileGeneratedAssembly(files);
            Type derivedState = GetGeneratedType(assembly, "StringControllerState");
            Type handler = GetGeneratedType(
                assembly,
                "StringConvertMethodStateMethodCallHandler");
            ParameterInfo[] parameters = handler.GetMethod("Invoke")!.GetParameters();
            Assert.Multiple(() =>
            {
                Assert.That(
                    derivedState.GetProperty(
                        "Convert",
                        BindingFlags.Public |
                        BindingFlags.Instance |
                        BindingFlags.DeclaredOnly)?.PropertyType.Name,
                    Is.EqualTo("StringConvertMethodState"));
                Assert.That(parameters[3].ParameterType, Is.EqualTo(typeof(string)));
                Assert.That(
                    parameters[4].ParameterType,
                    Is.EqualTo(typeof(string).MakeByRefType()));
            });
        }

        [Test]
        public void NodeStateGeneratorCodeGeneratesCorrectMethodSignatures()
        {
            // Arrange
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var fileSystem = new VirtualFileSystem();

            // Act
            Generators.GenerateStack(StackGenerationType.All, fileSystem, string.Empty, telemetry);

            // Find NodeStateGenerator files
            var predefinedNodesFiles = fileSystem.CreatedFiles
                .Where(c => c.EndsWith(".NodeStates.ex.g.cs", StringComparison.Ordinal))
                .ToList();
            string generatedCode = string.Join("\n", fileSystem.CreatedFiles
                .Where(c => Path.GetExtension(c) == ".cs")
                .Select(c => Encoding.UTF8.GetString(fileSystem.Get(c))));

            Assert.That(predefinedNodesFiles, Is.Not.Empty);
            Assert.That(generatedCode, Does.Contain(
                "global::Opc.Ua.NodeId previousNodeId = nodeState.NodeId;"));
            Assert.That(generatedCode, Does.Contain(
                "nodeState.NodeId.Equals("));
            Assert.That(generatedCode, Does.Not.Contain(
                "((global::Opc.Ua.NodeState)state)"));

            foreach (string file in predefinedNodesFiles)
            {
                string content = Encoding.UTF8.GetString(fileSystem.Get(file));

                // Check for proper method signatures
                Assert.That(content, Does.Contain("global::Opc.Ua.ISystemContext context"),
                    "Methods should use ISystemContext parameter");
                Assert.That(content, Does.Contain("global::Opc.Ua.NodeState nodeState = state;"),
                    "Code should use a base-typed NodeState local");
                Assert.That(content, Does.Contain("nodeState.NodeId ="),
                    "Code should set NodeId property");
                Assert.That(content, Does.Contain("nodeState.BrowseName ="),
                    "Code should set BrowseName property");
            }
        }

        /// <summary>
        /// Verifies the standard-model source generator declares typed
        /// <c>MatrixOf&lt;T&gt;</c> State classes for the matrix-rank
        /// VariableType (<c>XYArrayItemType</c>) and matrix-rank
        /// Property / Variable instances
        /// (<c>EnumDictionaryEntries</c>,
        /// <c>FailureSystemIdentifier</c>).
        /// </summary>
        [Test]
        public void NodeStateGeneratorEmitsMatrixOfTemplateParameterForStandardModel()
        {
            // Arrange
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var fileSystem = new VirtualFileSystem();

            // Act
            Generators.GenerateStack(StackGenerationType.All, fileSystem, string.Empty, telemetry,
                new GeneratorOptions
                {
                    OmitFluentApi = true
                });

            // Concatenate every generated source file so the snippet match
            // is resilient to whichever file each declaration lives in.
            string code = string.Join("\n", fileSystem.CreatedFiles
                .Where(c => Path.GetExtension(c) == ".cs")
                .Select(c => Encoding.UTF8.GetString(fileSystem.Get(c))));

            Assert.Multiple(() =>
            {
                // VariableType template specialization: XYArrayItemType
                // inherits the generic ArrayItemState chain with a
                // MatrixOf<XVType> template parameter.
                Assert.That(code, Does.Contain(
                    "global::Opc.Ua.ArrayItemState<global::Opc.Ua.MatrixOf<global::Opc.Ua.XVType>>"),
                    "XYArrayItemState should inherit ArrayItemState<MatrixOf<XVType>>.");

                // Property-state instance matrix branch.
                Assert.That(code, Does.Contain(
                    "global::Opc.Ua.PropertyState<global::Opc.Ua.MatrixOf<global::Opc.Ua.NodeId>>"),
                    "EnumDictionaryEntries should declare PropertyState<MatrixOf<NodeId>>.");

                // BaseDataVariableState-instance matrix branch.
                Assert.That(code, Does.Contain(
                    "global::Opc.Ua.BaseDataVariableState<global::Opc.Ua.MatrixOf<byte>>"),
                    "FailureSystemIdentifier should declare BaseDataVariableState<MatrixOf<byte>>.");
            });
        }

        /// <summary>
        /// Verifies Mandatory declarations on standard alarm ObjectTypes retain their
        /// ModellingRule when the type definition is materialized.
        /// </summary>
        [Test]
        public void StandardAlarmTypeDeclarationsRetainModellingRules()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var fileSystem = new VirtualFileSystem();

            Generators.GenerateStack(
                StackGenerationType.All,
                fileSystem,
                string.Empty,
                telemetry,
                new GeneratorOptions
                {
                    OmitFluentApi = true
                });

            string code = string.Join("\n", fileSystem.CreatedFiles
                .Where(c => Path.GetExtension(c) == ".cs")
                .Select(c => Encoding.UTF8.GetString(fileSystem.Get(c))));
            string certificateAlarmFactory = ExtractMethodBody(
                code,
                "CreateCertificateExpirationAlarmType");
            string expirationDateFactory = ExtractMethodBody(
                code,
                "CreateCertificateExpirationAlarmType_ExpirationDate");
            string trustListIdFactory = ExtractMethodBody(
                code,
                "CreateTrustListOutOfDateAlarmType_TrustListId");

            Assert.Multiple(() =>
            {
                Assert.That(
                    certificateAlarmFactory,
                    Does.Contain(
                        "CreateCertificateExpirationAlarmType_ExpirationDate(" +
                        "context, state, forInstance: forInstance)"),
                    "The ObjectType factory must pass its declaration/instance mode to child factories.");
                Assert.That(
                    expirationDateFactory,
                    Does.Contain("state.ModellingRuleId ="),
                    "CertificateExpirationAlarmType.ExpirationDate must retain its Mandatory rule.");
                Assert.That(
                    trustListIdFactory,
                    Does.Contain("state.ModellingRuleId ="),
                    "TrustListOutOfDateAlarmType.TrustListId must retain its Mandatory rule.");
            });
        }

        /// <summary>
        /// Verifies the source generator emits the singleton-instance
        /// dispatch inside type-level child factories for synthesized
        /// method arguments. Both the top-level method NodeId and its
        /// Mandatory <c>InputArguments</c> / <c>OutputArguments</c>
        /// descendants must rebind to their well-known singleton-instance
        /// NodeIds when the type-level factory is called with
        /// <c>forInstance: true</c> for a known singleton owner. Without
        /// this dispatch, lazy-added methods on the
        /// <c>Server</c>/<c>WellKnownRole_*</c> singletons silently keep
        /// the type-level child NodeIds (e.g. 11490 instead of 11493 for
        /// <c>Server_GetMonitoredItems_InputArguments</c>), so reads
        /// against the spec-reserved well-known instance NodeIds return
        /// <c>BadNodeIdUnknown</c>.
        /// </summary>
        [Test]
        public void NodeStateGeneratorEmitsSingletonInstanceDispatchInTypeLevelFactories()
        {
            // Arrange
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var fileSystem = new VirtualFileSystem();

            // Act
            Generators.GenerateStack(StackGenerationType.All, fileSystem, string.Empty, telemetry,
                new GeneratorOptions
                {
                    OmitFluentApi = true
                });

            string code = string.Join("\n", fileSystem.CreatedFiles
                .Where(c => Path.GetExtension(c) == ".cs")
                .Select(c => Encoding.UTF8.GetString(fileSystem.Get(c))));

            Assert.Multiple(() =>
            {
                // ServerType (single singleton: Server, NodeId 2253).
                // The type-level factory must dispatch the synthesized
                // InputArguments / OutputArguments children through the
                // singleton-instance child factories (Server_*).
                Assert.That(code, Does.Contain(
                    "if (parent.NodeId.Equals(global::Opc.Ua.NodeId.Create(" +
                    "2253u, global::Opc.Ua.Namespaces.OpcUa, context.NamespaceUris)))"),
                    "CreateServerType_GetMonitoredItems should dispatch on the Server singleton NodeId.");
                Assert.That(code, Does.Contain(
                    "state.CreateOrReplaceInputArguments(context, " +
                    "CreateServer_GetMonitoredItems_InputArguments(" +
                    "context, state, forInstance: true), assignInstanceNodeIds: false);"),
                    "The Server singleton branch should call the singleton-instance InputArguments factory.");
                Assert.That(code, Does.Contain(
                    "state.CreateOrReplaceOutputArguments(context, " +
                    "CreateServer_GetMonitoredItems_OutputArguments(" +
                    "context, state, forInstance: true), assignInstanceNodeIds: false);"),
                    "The Server singleton branch should call the singleton-instance OutputArguments factory.");

                // RoleType (multi-singleton: WellKnownRole_Observer = 15668,
                // WellKnownRole_Operator = 15680, …). The type-level factory
                // must dispatch on parent.NodeId across every singleton
                // whose corresponding child factory has been collected.
                Assert.That(code, Does.Contain(
                    "state.CreateOrReplaceInputArguments(context, " +
                    "CreateWellKnownRole_Observer_AddIdentity_InputArguments(" +
                    "context, state, forInstance: true), assignInstanceNodeIds: false);"),
                    "RoleType_AddIdentity should dispatch to WellKnownRole_Observer's InputArguments factory.");
                Assert.That(code, Does.Contain(
                    "state.CreateOrReplaceInputArguments(context, " +
                    "CreateWellKnownRole_SecurityAdmin_AddIdentity_InputArguments(" +
                    "context, state, forInstance: true), assignInstanceNodeIds: false);"),
                    "RoleType_AddIdentity should dispatch to WellKnownRole_SecurityAdmin's InputArguments factory.");

                // The top-level NodeId override re-binds nodeState.NodeId from
                // the type-level constant to the singleton-instance NodeId
                // when the dispatch matches. For GetMonitoredItems the
                // override rewrites 11489 → 11492 under the Server
                // singleton.
                Assert.That(code, Does.Contain(
                    "nodeState.NodeId = global::Opc.Ua.NodeId.Create(" +
                    "11492u, global::Opc.Ua.Namespaces.OpcUa, context.NamespaceUris);"),
                    "The Server singleton dispatch should override nodeState.NodeId " +
                    "to Server_GetMonitoredItems (11492).");
            });
        }

        /// <summary>
        /// Regression test for issue #3964. A concrete predefined instance
        /// (<c>AnalogDevice</c>) declares a <c>BaseAnalogType</c> variable
        /// (<c>Measurement</c>) whose <c>EURange</c> / <c>EngineeringUnits</c>
        /// property children are explicitly present on the instance but only
        /// resolve to <c>Optional</c> (inherited from <c>BaseAnalogType</c>,
        /// not restated as Mandatory on the instance). Before the fix the
        /// generated instance-level factory emitted the creation of those
        /// children inside the <c>if (!forInstance)</c> type-template block,
        /// so — because the factory is always invoked with
        /// <c>forInstance: true</c> — the properties were dropped from the
        /// actual instance. They must instead be created unconditionally,
        /// exactly like the type-level factory does.
        /// </summary>
        [Test]
        public void ConcreteInstanceAnalogChildren_AreMaterializedUnconditionally()
        {
            // Arrange
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);

            // Act
            Dictionary<string, string> files = GenerateFromNodeSet(
                "AnalogInstance.NodeSet2.xml", telemetry);

            // Assert
            string ex = files.Single(
                kv => kv.Key.EndsWith(".NodeStates.ex.g.cs", StringComparison.Ordinal)).Value;

            // The concrete-instance factory for the analog variable
            // (Create<Instance>_<Var>) is distinct from the type-level
            // factory (Create<Type>_<Var>). Only the instance one regressed.
            string instanceFactory = ExtractMethodBody(ex, "CreateAnalogDevice_Measurement");

            // The variable must be recognised as an analog state so that the
            // typed EngineeringUnits / EURange children (and the fluent
            // WithEURange / WithEngineeringUnits helpers) are available.
            Assert.That(instanceFactory,
                Does.Contain("global::Opc.Ua.BaseAnalogState"),
                "Measurement should be generated as a BaseAnalogState.");

            Assert.Multiple(() =>
            {
                Assert.That(instanceFactory, Does.Contain("CreateOrReplaceEURange"),
                    "Instance factory must create the EURange child.");
                Assert.That(instanceFactory, Does.Contain("CreateOrReplaceEngineeringUnits"),
                    "Instance factory must create the EngineeringUnits child.");
            });

            int idxGate = instanceFactory.IndexOf(
                "if (!forInstance)", StringComparison.Ordinal);
            int idxEuRange = instanceFactory.IndexOf(
                "CreateOrReplaceEURange", StringComparison.Ordinal);
            int idxEngineeringUnits = instanceFactory.IndexOf(
                "CreateOrReplaceEngineeringUnits", StringComparison.Ordinal);

            if (idxGate >= 0)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(idxEuRange, Is.LessThan(idxGate),
                        "EURange must be created before the `if (!forInstance)` gate " +
                        "so it materialises on the actual instance (issue #3964).");
                    Assert.That(idxEngineeringUnits, Is.LessThan(idxGate),
                        "EngineeringUnits must be created before the `if (!forInstance)` " +
                        "gate so it materialises on the actual instance (issue #3964).");
                });
            }
        }

        /// <summary>
        /// Verifies DataTypeEncoding objects remain independent predefined
        /// nodes when a NodeSet exporter authors a same-namespace
        /// <c>ParentNodeId</c> pointing at the owning DataType. Absorbing the
        /// encoding into the DataType child collection drops it from the
        /// generated NodeManager and leaves the server TypeTree without the
        /// encoding-to-DataType relationship required for structured values.
        /// </summary>
        [Test]
        public void SameNamespaceEncodingNodesAreEmittedAsPredefinedNodes()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);

            Dictionary<string, string> files = GenerateFromNodeSet(
                "SameNamespaceEncoding.NodeSet2.xml",
                telemetry);

            string code = files.Single(
                kv => kv.Key.EndsWith(".NodeStates.ex.g.cs", StringComparison.Ordinal)).Value;

            Assert.Multiple(() =>
            {
                Assert.That(
                    code,
                    Does.Contain("CreateMyDataType_Encoding_DefaultBinary"),
                    "The Default Binary encoding factory must be emitted.");
                Assert.That(
                    code,
                    Does.Contain("CreateMyDataType_Encoding_Default_JSON"),
                    "The Default JSON encoding factory must be emitted.");
                Assert.That(
                    code,
                    Does.Contain(
                        "NodeState state = CreateMyDataType_Encoding_DefaultBinary(context);"),
                    "The Default Binary encoding must be registered as a predefined node.");
                Assert.That(
                    code,
                    Does.Contain(
                        "NodeState state = CreateMyDataType_Encoding_Default_JSON(context);"),
                    "The Default JSON encoding must be registered as a predefined node.");
            });
        }

        [Test]
        public void MethodArgumentDataTypesUseRuntimeNamespaceTable()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);

            Dictionary<string, string> files = GenerateFromNodeSet(
                "MethodArgumentNamespace.NodeSet2.xml",
                telemetry);

            string code = files.Single(
                kv => kv.Key.EndsWith(".NodeStates.ex.g.cs", StringComparison.Ordinal)).Value;
            string factory = ExtractMethodBody(
                code,
                "CreateMyObjectType_DoWork_InputArguments");

            Assert.Multiple(() =>
            {
                Assert.That(
                    factory,
                    Does.Contain(
                        "DataType = global::Opc.Ua.NodeId.Create(100u, " +
                        "global::MethodArguments.Namespaces.MethodArguments, " +
                        "context.NamespaceUris)"));
                Assert.That(factory, Does.Not.Contain("Variant.FromXml"));
            });
        }

        [Test]
        public void SameNamedMethodArgumentsUseDistinctSymbolsAndPreserveRuntimeNames()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);

            Dictionary<string, string> files = GenerateFromNodeSet(
                "SameNamedMethodArguments.NodeSet2.xml",
                telemetry);
            Dictionary<string, string> repeatedFiles = GenerateFromNodeSet(
                "SameNamedMethodArguments.NodeSet2.xml",
                telemetry);

            string nodeStates = files.Single(
                kv => kv.Key.EndsWith(".NodeStates.ex.g.cs", StringComparison.Ordinal)).Value;
            string nodeStateClasses = files.Single(
                kv => kv.Key.EndsWith(".NodeStates.g.cs", StringComparison.Ordinal)).Value;
            string proxies = files.Single(
                kv => kv.Key.EndsWith(".TypeProxies.g.cs", StringComparison.Ordinal)).Value;
            string generatedCode = string.Join("\n", files.Values);
            string inputArgumentsFactory = ExtractMethodBody(
                nodeStates,
                "CreateRoundTripMethodType_InputArguments");
            string outputArgumentsFactory = ExtractMethodBody(
                nodeStates,
                "CreateRoundTripMethodType_OutputArguments");
            string localCollisionInputFactory = ExtractMethodBody(
                nodeStates,
                "CreateLocalCollisionMethodType_InputArguments");
            string localCollisionOutputFactory = ExtractMethodBody(
                nodeStates,
                "CreateLocalCollisionMethodType_OutputArguments");
            string[] inputRuntimeNames = ExtractStringLiteralValues(inputArgumentsFactory);
            string[] outputRuntimeNames = ExtractStringLiteralValues(outputArgumentsFactory);
            string[] localCollisionInputNames =
                ExtractStringLiteralValues(localCollisionInputFactory);
            string[] localCollisionOutputNames =
                ExtractStringLiteralValues(localCollisionOutputFactory);
            string[] proxyRuntimeStrings = ExtractStringLiteralValues(proxies);
            string[] expectedInputNames =
            [
                "Foo",
                "foo",
                "Class",
                "VersionId",
                "Await",
                "Ct",
                "cT",
                "CancellationToken",
                "Context",
                "ObjectId",
                "Method",
                "InputArguments",
                "Results",
                "_result",
                "_foo",
                "_",
                "Nameof",
                "__arglist",
                "__makeref",
                "__reftype",
                "__refvalue"
            ];
            string[] expectedOutputNames =
            [
                "VersionId",
                "class",
                "Changed",
                "OutputArguments",
                "ServiceResult",
                "Quote\"Name",
                "Back\\Slash",
                "Line\nBreak",
                "Δelta雪",
                "Foo",
                "RoundTripMethodStateResult",
                "Next\u0085Line",
                "Line\u2028Separator",
                "Paragraph\u2029Separator"
            ];

            Assert.Multiple(() =>
            {
                Assert.That(generatedCode, Does.Contain("string foo,"));
                Assert.That(generatedCode, Does.Contain("string foo2,"));
                Assert.That(generatedCode, Does.Contain("string @class,"));
                Assert.That(generatedCode, Does.Contain("string versionId,"));
                Assert.That(generatedCode, Does.Contain("string await2,"));
                Assert.That(generatedCode, Does.Contain("string ct2,"));
                Assert.That(generatedCode, Does.Contain("string cT3,"));
                Assert.That(generatedCode, Does.Contain("string cancellationToken2,"));
                Assert.That(generatedCode, Does.Contain("string context2,"));
                Assert.That(generatedCode, Does.Contain("string objectId2,"));
                Assert.That(generatedCode, Does.Contain("string method2,"));
                Assert.That(generatedCode, Does.Contain("string inputArguments2,"));
                Assert.That(generatedCode, Does.Contain("string results2,"));
                Assert.That(nodeStateClasses, Does.Contain("string _result2,"));
                Assert.That(nodeStateClasses, Does.Contain("string _foo,"));
                Assert.That(nodeStateClasses, Does.Contain("string _2,"));
                Assert.That(nodeStateClasses, Does.Contain("string nameof,"));
                Assert.That(nodeStateClasses, Does.Contain("string @__arglist,"));
                Assert.That(nodeStateClasses, Does.Contain("string @__makeref,"));
                Assert.That(nodeStateClasses, Does.Contain("string @__reftype,"));
                Assert.That(nodeStateClasses, Does.Contain("string @__refvalue,"));
                Assert.That(generatedCode, Does.Contain("ref string versionIdOut"));
                Assert.That(generatedCode, Does.Contain("ref string classOut"));
                Assert.That(generatedCode, Does.Contain("ref string outputArgumentsOut"));
                Assert.That(generatedCode, Does.Contain("ref string serviceResultOut"));
                Assert.That(nodeStateClasses, Does.Contain("ref string fooOut"));
                Assert.That(nodeStateClasses,
                    Does.Contain("ref string roundTripMethodStateResultOut"));
                Assert.That(generatedCode, Does.Contain(
                    "public string VersionIdOut { get; set; }"));
                Assert.That(generatedCode, Does.Contain(
                    "public string ClassOut { get; set; }"));
                Assert.That(generatedCode, Does.Contain(
                    "public string OutputArgumentsOut { get; set; }"));
                Assert.That(generatedCode, Does.Contain(
                    "public string ServiceResultOut { get; set; }"));
                Assert.That(nodeStateClasses, Does.Contain(
                    "public string RoundTripMethodStateResultOut { get; set; }"));
                Assert.That(generatedCode, Does.Contain("\\u0085"));
                Assert.That(generatedCode, Does.Contain("\\u2028"));
                Assert.That(generatedCode, Does.Contain("\\u2029"));
                Assert.That(generatedCode, Does.Not.Contain("string await,"));
                Assert.That(generatedCode, Does.Not.Contain("string cancellationToken,"));
                Assert.That(inputArgumentsFactory,
                    Does.Not.Contain("Name = \"foo2\""));
                Assert.That(inputArgumentsFactory,
                    Does.Not.Contain("Name = \"@class\""));
                Assert.That(outputArgumentsFactory,
                    Does.Not.Contain("Name = \"VersionIdOut\""));
                Assert.That(outputArgumentsFactory,
                    Does.Not.Contain("Name = \"classOut\""));
                foreach (string expectedName in expectedInputNames)
                {
                    Assert.That(inputRuntimeNames, Does.Contain(expectedName));
                }
                foreach (string expectedName in expectedOutputNames)
                {
                    Assert.That(outputRuntimeNames, Does.Contain(expectedName));
                }
                Assert.That(localCollisionInputNames, Does.Contain("_foo"));
                Assert.That(localCollisionOutputNames, Does.Contain("Foo"));

                Assert.That(proxies, Does.Contain("ValueTask<("));
                Assert.That(proxies, Does.Contain(
                    "ValueTask<string> LocalCollisionAsync("));
                Assert.That(proxies, Does.Contain(
                    "string _fooOut;"));
                Assert.That(proxies, Does.Contain(
                    "TryGetValue(out _fooOut)"));
                Assert.That(proxies, Does.Contain("return _fooOut;"));
                Assert.That(proxies, Does.Contain("string versionIdOut"));
                Assert.That(proxies, Does.Contain("string classOut"));
                Assert.That(proxies, Does.Contain("bool changed"));
                Assert.That(proxies, Does.Contain("string outputArgumentsOut"));
                Assert.That(proxies, Does.Contain("string serviceResultOut"));
                Assert.That(proxies, Does.Contain("string quote_Name"));
                Assert.That(proxies, Does.Contain("string back_Slash"));
                Assert.That(proxies, Does.Contain("string lineBreak"));
                Assert.That(proxies, Does.Contain("string δelta雪"));
                Assert.That(proxies, Does.Contain("string fooOut"));
                Assert.That(proxies,
                    Does.Contain("string roundTripMethodStateResult"));
                Assert.That(proxies, Does.Contain("string nextLine"));
                Assert.That(proxies, Does.Contain("string lineSeparator"));
                Assert.That(proxies, Does.Contain("string paragraphSeparator"));
                Assert.That(proxies, Does.Contain("string foo,"));
                Assert.That(proxies, Does.Contain("string foo2,"));
                Assert.That(proxies, Does.Contain("string @class,"));
                Assert.That(proxies, Does.Contain("string versionId,"));
                Assert.That(proxies, Does.Contain("string await2,"));
                Assert.That(proxies, Does.Contain("string ct2,"));
                Assert.That(proxies, Does.Contain("string cT3,"));
                Assert.That(proxies, Does.Contain("string cancellationToken2,"));
                Assert.That(proxies, Does.Contain("string context2,"));
                Assert.That(proxies, Does.Contain("string objectId2,"));
                Assert.That(proxies, Does.Contain("string method2,"));
                Assert.That(proxies, Does.Contain("string inputArguments2,"));
                Assert.That(proxies, Does.Contain("string results2,"));
                Assert.That(proxies, Does.Contain("string _result,"));
                Assert.That(proxies, Does.Contain("string _foo,"));
                Assert.That(proxies, Does.Contain("string _2,"));
                Assert.That(proxies, Does.Contain("string nameof2,"));
                Assert.That(proxies, Does.Contain("string @__arglist,"));
                Assert.That(proxies, Does.Contain("string @__makeref,"));
                Assert.That(proxies, Does.Contain("string @__reftype,"));
                Assert.That(proxies, Does.Contain("string @__refvalue,"));
                Assert.That(proxies, Does.Contain("<param name=\"class\">"));
                Assert.That(proxies, Does.Not.Contain("<param name=\"@class\">"));
                Assert.That(proxies, Does.Contain("output 'VersionId'."));
                Assert.That(proxies, Does.Contain("output 'class'."));
                foreach (string expectedName in expectedOutputNames)
                {
                    Assert.That(
                        proxyRuntimeStrings,
                        Does.Contain(
                            $"Method 'RoundTrip' returned an unexpected value " +
                            $"for output '{expectedName}'."));
                }

                Assert.That(repeatedFiles.Keys, Is.EquivalentTo(files.Keys));
                foreach (string key in files.Keys)
                {
                    Assert.That(repeatedFiles[key], Is.EqualTo(files[key]), key);
                }
            });

            using var peStream = new MemoryStream();
            bool success = OptimizationLevel.Debug
                .CreateCompilation()
                .AddCode(
                    files.WithOpcUaGeneratedStack(),
                    LanguageVersion.Latest)
                .Emit(peStream)
                .Check(TestContext.Out, out int errorCount, out int warnCount);

            Assert.That(success, Is.True,
                $"Generated code should compile. Errors: {errorCount}, Warnings: {warnCount}");
        }

        private static string[] ExtractStringLiteralValues(string source)
        {
            return
            [
                .. CSharpSyntaxTree.ParseText(source)
                    .GetRoot()
                    .DescendantNodes()
                    .OfType<LiteralExpressionSyntax>()
                    .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                    .Select(literal => literal.Token.ValueText)
            ];
        }

        private static Dictionary<string, string> GenerateFromNodeSet(
            string nodeSetResource,
            ITelemetryContext telemetry)
        {
            using var fileSystem = new VirtualFileSystem();
            string path = Path.Combine(
                Directory.GetCurrentDirectory(), "Resources", nodeSetResource);

            var nodesets = new NodesetFileCollection(
                [(path, new NodesetFileOptions())],
                [],
                fileSystem,
                telemetry);

            // The test compilation has no Opc.Ua.Server reference, so
            // suppress fluent-builder emission (mirrors model-only projects).
            nodesets.GenerateCode(
                fileSystem,
                string.Empty,
                telemetry,
                new GeneratorOptions { OmitFluentApi = true });

            return fileSystem.CreatedFiles
                .Where(c => Path.GetExtension(c) == ".cs")
                .ToDictionary(c => c, c => Encoding.UTF8.GetString(fileSystem.Get(c)));
        }

        private static Dictionary<string, string> GenerateFromModelDesign(
            string modelDesignResource,
            ITelemetryContext telemetry)
        {
            using var fileSystem = new VirtualFileSystem();
            string path = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Resources",
                modelDesignResource);
            Generators.GenerateCode(
                new DesignFileCollection
                {
                    Targets = [path],
                    Options = new DesignFileOptions()
                },
                fileSystem,
                string.Empty,
                telemetry,
                new GeneratorOptions { OmitFluentApi = true });

            return fileSystem.CreatedFiles
                .Where(c => Path.GetExtension(c) == ".cs")
                .ToDictionary(c => c, c => Encoding.UTF8.GetString(fileSystem.Get(c)));
        }

        private static IModelDesign OpenNodeSetModel(
            string nodeSetResource,
            ITelemetryContext telemetry)
        {
            using var fileSystem = new VirtualFileSystem();
            string path = Path.Combine(
                Directory.GetCurrentDirectory(), "Resources", nodeSetResource);
            var nodesets = new NodesetFileCollection(
                [(path, new NodesetFileOptions())],
                [],
                fileSystem,
                telemetry);
            string modelUri = nodesets.ModelUris.Single();
            List<string> designFiles = nodesets.GetDesignFileListForModel(
                modelUri,
                out _);
            IFileSystem sourceFileSystem = typeof(Generators).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design")
                .WithFallback(fileSystem);
            return sourceFileSystem.OpenModelDesign(
                new DesignFileCollection { Targets = designFiles },
                [],
                telemetry,
                useAllowSubtypes: false);
        }

        private static IModelDesign OpenModelDesign(
            string modelDesignResource,
            ITelemetryContext telemetry)
        {
            using var fileSystem = new VirtualFileSystem();
            string path = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Resources",
                modelDesignResource);
            IFileSystem sourceFileSystem = typeof(Generators).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design")
                .WithFallback(fileSystem);
            return sourceFileSystem.OpenModelDesign(
                new DesignFileCollection { Targets = [path] },
                [],
                telemetry,
                useAllowSubtypes: false);
        }

        private static MethodDesign GetMethod(
            IModelDesign model,
            string ownerName,
            string browseName)
        {
            ObjectTypeDesign owner = model.Nodes
                .OfType<ObjectTypeDesign>()
                .Single(type => type.SymbolicId.Name == ownerName);
            return owner.Children.Items
                .OfType<MethodDesign>()
                .Single(method => method.BrowseName == browseName);
        }

        private static Assembly CompileGeneratedAssembly(
            Dictionary<string, string> files)
        {
            using var peStream = new MemoryStream();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            Dictionary<string, string> stackFiles = GenerateStackTests.GenerateStack(
                StackGenerationType.Models,
                telemetry,
                out _);
            IEnumerable<KeyValuePair<string, string>> generatedNodeStateFiles = stackFiles
                .Concat(files)
                .Where(file => !file.Key.EndsWith(
                    ".TypeProxies.g.cs",
                    StringComparison.Ordinal));
            bool success = OptimizationLevel.Debug
                .CreateCompilation("TypedMethodArguments.Generated")
                .AddCode(
                    generatedNodeStateFiles.WithOpcUaCoreStubs(),
                    LanguageVersion.Latest)
                .Emit(peStream)
                .Check(TestContext.Out, out int errorCount, out int warningCount);

            Assert.That(
                success,
                Is.True,
                $"Generated code failed with {errorCount} errors and {warningCount} warnings.");
            return Assembly.Load(peStream.ToArray());
        }

        private static Type GetGeneratedType(Assembly assembly, string name)
        {
            Type type = assembly.GetTypes().SingleOrDefault(candidate => candidate.Name == name);
            Assert.That(type, Is.Not.Null, $"Generated type '{name}' was not found.");
            return type;
        }

        private static void AssertArrayOfNodeIds(Type type)
        {
            Assert.That(type, Is.Not.Null);
            if (type == null)
            {
                return;
            }
            Assert.That(type.IsGenericType, Is.True);
            if (!type.IsGenericType)
            {
                return;
            }
            Assert.Multiple(() =>
            {
                Assert.That(
                    type.GetGenericTypeDefinition().FullName,
                    Is.EqualTo("Opc.Ua.ArrayOf`1"));
                Assert.That(
                    type.GetGenericArguments()[0].FullName,
                    Is.EqualTo("Opc.Ua.NodeId"));
            });
        }

        /// <summary>
        /// Extracts the source of the factory method whose signature begins
        /// with <c>internal static … {methodName}(</c>. Returns the body from
        /// the method definition up to (but not including) the next
        /// static-method definition, so callers can assert on ordering within
        /// a single method without matching call sites elsewhere in the file.
        /// </summary>
        private static string ExtractMethodBody(string code, string methodName)
        {
            System.Text.RegularExpressions.Match match = Regex.Match(
                code,
                @"internal static [^\r\n]*\b" + Regex.Escape(methodName) + @"\(");
            Assert.That(match.Success, Is.True,
                $"Method definition '{methodName}' not found in generated code.");

            int start = match.Index;
            int end = code.Length;
            foreach (string marker in new[]
            {
                "\n        internal static",
                "\n        public static",
                "\n        private static"
            })
            {
                int idx = code.IndexOf(
                    marker, start + match.Length, StringComparison.Ordinal);
                if (idx >= 0 && idx < end)
                {
                    end = idx;
                }
            }
            return code[start..end];
        }

        private Mock<IFileSystem> m_mockFileSystem;
        private Mock<IModelDesign> m_mockModelDesign;
        private Mock<ITelemetryContext> m_mockTelemetry;
        private GeneratorContext m_context;
    }
}
