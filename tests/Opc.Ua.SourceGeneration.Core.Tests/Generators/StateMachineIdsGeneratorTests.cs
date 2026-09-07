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
