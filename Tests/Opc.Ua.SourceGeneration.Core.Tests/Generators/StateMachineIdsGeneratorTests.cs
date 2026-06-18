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
    }
}
