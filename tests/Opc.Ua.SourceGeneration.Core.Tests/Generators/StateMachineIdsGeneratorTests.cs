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
using System.Linq;
using System.Text;
using System.Xml;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for the template-based
    /// <see cref="StateMachineIdsGenerator"/>: covers the no-FSM short
    /// circuit and the constructor contract. Output-shape validation is
    /// performed by the Opc.Ua.Di tests, which compile + execute against
    /// the emitted <c>*.StateMachineIds.g.cs</c> file.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class StateMachineIdsGeneratorTests
    {
        [Test]
        public void Constructor_NullContext_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new StateMachineIdsGenerator(null));
        }

        [Test]
        public void Emit_ModelWithoutFsmSubtypes_ReturnsEmpty()
        {
            // Arrange — a single non-FSM ObjectType with a base of OpcUa
            // BaseObjectType. The IsFiniteStateMachineSubtype walk
            // returns false, so the generator emits nothing.
            var nonFsm = new ObjectTypeDesign
            {
                SymbolicId = new System.Xml.XmlQualifiedName(
                    "NotAnFsm", "http://test.org/UA/"),
                SymbolicName = new System.Xml.XmlQualifiedName(
                    "NotAnFsm", "http://test.org/UA/"),
                BrowseName = "NotAnFsm"
            };

            var targetNamespace = new Namespace
            {
                Value = "http://test.org/UA/",
                Prefix = "Test",
                Name = "TestNamespace"
            };

            var mockModelDesign = new Mock<IModelDesign>();
            mockModelDesign.Setup(m => m.TargetNamespace).Returns(targetNamespace);
            mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([nonFsm]);
            mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<NodeDesign>())).Returns(false);

            var mockFileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            var mockTelemetry = new Mock<ITelemetryContext>();

            var context = new GeneratorContext
            {
                FileSystem = mockFileSystem.Object,
                OutputFolder = "out",
                ModelDesign = mockModelDesign.Object,
                Telemetry = mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            var generator = new StateMachineIdsGenerator(context);

            // Act
            var resources = generator.Emit().ToList();

            // Assert — no file opened, no resource produced.
            Assert.That(resources, Is.Empty);
            mockFileSystem.VerifyNoOtherCalls();
        }

        [Test]
        public void Emit_NodeSetStyleNumberVariables_EmitsStateAndTransitionNumbers()
        {
            const string testNamespaceUri = "http://test.org/UA/";
            const string uaNamespaceUri = "http://opcfoundation.org/UA/";

            var finiteStateMachineType = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("FiniteStateMachineType", uaNamespaceUri)
            };
            var machineType = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestStateMachineType", testNamespaceUri),
                SymbolicName = new XmlQualifiedName("TestStateMachineType", testNamespaceUri),
                BrowseName = "TestStateMachineType",
                BaseTypeNode = finiteStateMachineType,
                Children = new ListOfChildren
                {
                    Items =
                    [
                        CreateState("Idle", 1),
                        CreateTransition("IdleToReady", 101)
                    ]
                },
                HasChildren = true
            };

            var targetNamespace = new Namespace
            {
                Value = testNamespaceUri,
                Prefix = "Test",
                Name = "TestNamespace"
            };

            var mockModelDesign = new Mock<IModelDesign>();
            mockModelDesign.Setup(m => m.TargetNamespace).Returns(targetNamespace);
            mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([machineType]);
            mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<NodeDesign>())).Returns(false);

            using var fileSystem = new VirtualFileSystem();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var context = new GeneratorContext
            {
                FileSystem = fileSystem,
                OutputFolder = "out",
                ModelDesign = mockModelDesign.Object,
                Telemetry = mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            var generator = new StateMachineIdsGenerator(context);

            var resources = generator.Emit().ToList();

            Assert.That(resources, Is.Not.Empty);
            string output = Encoding.UTF8.GetString(fileSystem.Get(
                System.IO.Path.Combine("out", "Test.StateMachineIds.g.cs")));
            Assert.That(output, Does.Contain("public const uint Idle = 1u;"));
            Assert.That(output, Does.Contain("public const uint IdleToReady = 101u;"));
        }

        /// <summary>
        /// Regression: the state / transition NodeId constants alias the
        /// <c>Objects</c> constants NodeIdGenerator emits, and those are
        /// <c>string</c> for a node with a string identifier. Emitting them as
        /// <c>const uint</c> regardless made every vendor model that uses string
        /// NodeIds and has a FiniteStateMachineType subtype fail with CS0029.
        /// </summary>
        [Test]
        public void Emit_ModelWithStringNodeIds_EmitsStringIdConstants()
        {
            const string testNamespaceUri = "http://test.org/UA/";
            const string uaNamespaceUri = "http://opcfoundation.org/UA/";

            var finiteStateMachineType = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("FiniteStateMachineType", uaNamespaceUri)
            };
            var machineType = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestStateMachineType", testNamespaceUri),
                SymbolicName = new XmlQualifiedName("TestStateMachineType", testNamespaceUri),
                BrowseName = "TestStateMachineType",
                BaseTypeNode = finiteStateMachineType,
                Children = new ListOfChildren
                {
                    Items =
                    [
                        CreateState("Idle", 1),
                        CreateTransition("IdleToReady", 101)
                    ]
                },
                HasChildren = true
            };

            // The flattened nodes the Objects constants are emitted from. These
            // carry a string identifier, so their constants are const string.
            var idleNode = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName(
                    "TestStateMachineType_Idle", testNamespaceUri),
                SymbolicName = new XmlQualifiedName("Idle", testNamespaceUri),
                BrowseName = "Idle",
                StringId = "TestStateMachineType_Idle"
            };
            var transitionNode = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName(
                    "TestStateMachineType_IdleToReady", testNamespaceUri),
                SymbolicName = new XmlQualifiedName("IdleToReady", testNamespaceUri),
                BrowseName = "IdleToReady",
                StringId = "TestStateMachineType_IdleToReady"
            };

            var targetNamespace = new Namespace
            {
                Value = testNamespaceUri,
                Prefix = "Test",
                Name = "TestNamespace"
            };

            var mockModelDesign = new Mock<IModelDesign>();
            mockModelDesign.Setup(m => m.TargetNamespace).Returns(targetNamespace);
            mockModelDesign
                .Setup(m => m.GetNodeDesigns())
                .Returns([machineType, idleNode, transitionNode]);
            mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<NodeDesign>())).Returns(false);

            using var fileSystem = new VirtualFileSystem();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var context = new GeneratorContext
            {
                FileSystem = fileSystem,
                OutputFolder = "out",
                ModelDesign = mockModelDesign.Object,
                Telemetry = mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            var generator = new StateMachineIdsGenerator(context);
            var resources = generator.Emit().ToList();

            Assert.That(resources, Is.Not.Empty);
            string output = Encoding.UTF8.GetString(fileSystem.Get(
                System.IO.Path.Combine("out", "Test.StateMachineIds.g.cs")));

            Assert.That(
                output,
                Does.Contain(
                    "public const string Idle = global::Test.Objects.TestStateMachineType_Idle;"),
                "a string identified state must alias a const string");
            Assert.That(
                output,
                Does.Contain(
                    "public const string IdleToReady = " +
                    "global::Test.Objects.TestStateMachineType_IdleToReady;"),
                "a string identified transition must alias a const string");
            Assert.That(
                output,
                Does.Not.Contain(
                    "public const uint Idle = global::Test.Objects."),
                "the uint alias would not compile against a const string");

            // The StateNumber / TransitionNumber values stay uint - they are
            // property values, not identifiers.
            Assert.That(output, Does.Contain("public const uint Idle = 1u;"));
        }

        /// <summary>
        /// Regression: the string/uint lookup was built from the top-level node
        /// list, but a state or transition of a FiniteStateMachineType exists
        /// only as an entry in its owner's instance hierarchy - which is where
        /// NodeIdGenerator emits its Objects constant from. The lookup could
        /// therefore never match and every alias fell back to uint, so the
        /// CS0029 this was meant to fix survived on every real model.
        /// </summary>
        [Test]
        public void Emit_StringNodeIdsReachableOnlyViaHierarchy_EmitsStringIdConstants()
        {
            const string testNamespaceUri = "http://test.org/UA/";
            const string uaNamespaceUri = "http://opcfoundation.org/UA/";

            var finiteStateMachineType = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("FiniteStateMachineType", uaNamespaceUri)
            };

            // The hierarchy entry NodeIdGenerator emits the Objects constant
            // from. It carries a string identifier, so the constant is a string.
            var idleInstance = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName(
                    "TestStateMachineType_Idle", testNamespaceUri),
                SymbolicName = new XmlQualifiedName("Idle", testNamespaceUri),
                BrowseName = "Idle",
                StringId = "TestStateMachineType_Idle"
            };

            var machineType = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestStateMachineType", testNamespaceUri),
                SymbolicName = new XmlQualifiedName("TestStateMachineType", testNamespaceUri),
                BrowseName = "TestStateMachineType",
                BaseTypeNode = finiteStateMachineType,
                Children = new ListOfChildren { Items = [CreateState("Idle", 1)] },
                HasChildren = true,
                Hierarchy = new Hierarchy()
            };
            machineType.Hierarchy.Nodes["Idle"] = new HierarchyNode
            {
                RelativePath = "Idle",
                Instance = idleInstance
            };

            var targetNamespace = new Namespace
            {
                Value = testNamespaceUri,
                Prefix = "Test",
                Name = "TestNamespace"
            };

            var mockModelDesign = new Mock<IModelDesign>();
            mockModelDesign.Setup(m => m.TargetNamespace).Returns(targetNamespace);
            // Only the type is a top-level node - exactly what the real
            // GetNodeDesigns() returns.
            mockModelDesign.Setup(m => m.GetNodeDesigns()).Returns([machineType]);
            mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<NodeDesign>())).Returns(false);

            using var fileSystem = new VirtualFileSystem();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var context = new GeneratorContext
            {
                FileSystem = fileSystem,
                OutputFolder = "out",
                ModelDesign = mockModelDesign.Object,
                Telemetry = mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            var resources = new StateMachineIdsGenerator(context).Emit().ToList();
            Assert.That(resources, Is.Not.Empty);

            string output = Encoding.UTF8.GetString(fileSystem.Get(
                System.IO.Path.Combine("out", "Test.StateMachineIds.g.cs")));

            Assert.That(
                output,
                Does.Contain(
                    "public const string Idle = global::Test.Objects.TestStateMachineType_Idle;"),
                "the constant type has to follow the hierarchy node's identifier");
            Assert.That(
                output,
                Does.Not.Contain("public const uint Idle = global::Test.Objects."),
                "the uint alias would not compile against a const string");
        }

        /// <summary>
        /// A model with numeric identifiers keeps the uint alias.
        /// </summary>
        [Test]
        public void Emit_ModelWithNumericNodeIds_EmitsUIntIdConstants()
        {
            const string testNamespaceUri = "http://test.org/UA/";
            const string uaNamespaceUri = "http://opcfoundation.org/UA/";

            var finiteStateMachineType = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("FiniteStateMachineType", uaNamespaceUri)
            };
            var machineType = new ObjectTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestStateMachineType", testNamespaceUri),
                SymbolicName = new XmlQualifiedName("TestStateMachineType", testNamespaceUri),
                BrowseName = "TestStateMachineType",
                BaseTypeNode = finiteStateMachineType,
                Children = new ListOfChildren
                {
                    Items = [CreateState("Idle", 1)]
                },
                HasChildren = true
            };
            var idleNode = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName(
                    "TestStateMachineType_Idle", testNamespaceUri),
                SymbolicName = new XmlQualifiedName("Idle", testNamespaceUri),
                BrowseName = "Idle",
                NumericId = 5001,
                NumericIdSpecified = true
            };

            var targetNamespace = new Namespace
            {
                Value = testNamespaceUri,
                Prefix = "Test",
                Name = "TestNamespace"
            };

            var mockModelDesign = new Mock<IModelDesign>();
            mockModelDesign.Setup(m => m.TargetNamespace).Returns(targetNamespace);
            mockModelDesign
                .Setup(m => m.GetNodeDesigns())
                .Returns([machineType, idleNode]);
            mockModelDesign.Setup(m => m.IsExcluded(It.IsAny<NodeDesign>())).Returns(false);

            using var fileSystem = new VirtualFileSystem();
            var mockTelemetry = new Mock<ITelemetryContext>();
            var context = new GeneratorContext
            {
                FileSystem = fileSystem,
                OutputFolder = "out",
                ModelDesign = mockModelDesign.Object,
                Telemetry = mockTelemetry.Object,
                Options = new GeneratorOptions()
            };

            string output = Encoding.UTF8.GetString(
                new StateMachineIdsGenerator(context).Emit().ToList() is { Count: > 0 }
                    ? fileSystem.Get(System.IO.Path.Combine("out", "Test.StateMachineIds.g.cs"))
                    : []);

            Assert.That(
                output,
                Does.Contain(
                    "public const uint Idle = global::Test.Objects.TestStateMachineType_Idle;"));
        }

        private static ObjectDesign CreateState(string name, uint number)
        {
            return CreateStateMachineChild(name, "StateType", "StateNumber", number);
        }

        private static ObjectDesign CreateTransition(string name, uint number)
        {
            return CreateStateMachineChild(name, "TransitionType", "TransitionNumber", number);
        }

        private static ObjectDesign CreateStateMachineChild(
            string name,
            string typeDefinition,
            string numberPropertyName,
            uint number)
        {
            const string uaNamespaceUri = "http://opcfoundation.org/UA/";
            const string testNamespaceUri = "http://test.org/UA/";

            return new ObjectDesign
            {
                SymbolicName = new XmlQualifiedName(name, testNamespaceUri),
                BrowseName = name,
                TypeDefinition = new XmlQualifiedName(typeDefinition, uaNamespaceUri),
                Children = new ListOfChildren
                {
                    Items =
                    [
                        new VariableDesign
                        {
                            SymbolicName = new XmlQualifiedName(numberPropertyName, testNamespaceUri),
                            BrowseName = numberPropertyName,
                            DecodedValue = number,
                            DefaultValue = CreateUInt32Value(number)
                        }
                    ]
                },
                HasChildren = true
            };
        }

        private static System.Xml.XmlElement CreateUInt32Value(uint number)
        {
            var document = new XmlDocument();
            System.Xml.XmlElement value = document.CreateElement("Value");
            System.Xml.XmlElement child = document.CreateElement(
                "uax",
                "UInt32",
                "http://opcfoundation.org/UA/2008/02/Types.xsd");
            child.InnerText = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            value.AppendChild(child);
            return value;
        }
    }
}
