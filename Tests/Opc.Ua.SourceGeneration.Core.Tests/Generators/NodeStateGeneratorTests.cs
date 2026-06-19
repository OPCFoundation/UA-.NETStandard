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
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema.Model;
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
                    OmitFluentApi = true
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

            Assert.That(predefinedNodesFiles, Is.Not.Empty);

            foreach (string file in predefinedNodesFiles)
            {
                string content = Encoding.UTF8.GetString(fileSystem.Get(file));

                // Check for proper method signatures
                Assert.That(content, Does.Contain("global::Opc.Ua.ISystemContext context"),
                    "Methods should use ISystemContext parameter");
                Assert.That(content, Does.Contain("state.NodeId ="),
                    "Code should set NodeId property");
                Assert.That(content, Does.Contain("state.BrowseName ="),
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
                    "if (parent.NodeId.Equals(global::Opc.Ua.NodeId.Create(2253u, global::Opc.Ua.Namespaces.OpcUa, context.NamespaceUris)))"),
                    "CreateServerType_GetMonitoredItems should dispatch on the Server singleton NodeId.");
                Assert.That(code, Does.Contain(
                    "state.CreateOrReplaceInputArguments(context, CreateServer_GetMonitoredItems_InputArguments(context, state, forInstance: true));"),
                    "The Server singleton branch should call the singleton-instance InputArguments factory.");
                Assert.That(code, Does.Contain(
                    "state.CreateOrReplaceOutputArguments(context, CreateServer_GetMonitoredItems_OutputArguments(context, state, forInstance: true));"),
                    "The Server singleton branch should call the singleton-instance OutputArguments factory.");

                // RoleType (multi-singleton: WellKnownRole_Observer = 15668,
                // WellKnownRole_Operator = 15680, …). The type-level factory
                // must dispatch on parent.NodeId across every singleton
                // whose corresponding child factory has been collected.
                Assert.That(code, Does.Contain(
                    "state.CreateOrReplaceInputArguments(context, CreateWellKnownRole_Observer_AddIdentity_InputArguments(context, state, forInstance: true));"),
                    "RoleType_AddIdentity should dispatch to WellKnownRole_Observer's InputArguments factory.");
                Assert.That(code, Does.Contain(
                    "state.CreateOrReplaceInputArguments(context, CreateWellKnownRole_SecurityAdmin_AddIdentity_InputArguments(context, state, forInstance: true));"),
                    "RoleType_AddIdentity should dispatch to WellKnownRole_SecurityAdmin's InputArguments factory.");

                // The top-level NodeId override re-binds state.NodeId from
                // the type-level constant to the singleton-instance NodeId
                // when the dispatch matches. For GetMonitoredItems the
                // override rewrites 11489 → 11492 under the Server
                // singleton.
                Assert.That(code, Does.Contain(
                    "state.NodeId = global::Opc.Ua.NodeId.Create(11492u, global::Opc.Ua.Namespaces.OpcUa, context.NamespaceUris);"),
                    "The Server singleton dispatch should override state.NodeId to Server_GetMonitoredItems (11492).");
            });
        }

        private Mock<IFileSystem> m_mockFileSystem;
        private Mock<IModelDesign> m_mockModelDesign;
        private Mock<ITelemetryContext> m_mockTelemetry;
        private GeneratorContext m_context;
    }
}
