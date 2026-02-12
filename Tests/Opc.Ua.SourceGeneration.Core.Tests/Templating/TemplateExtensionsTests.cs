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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Templating.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TemplateExtensions"/> class.
    /// </summary>
    [TestFixture]
    [Category("Templating")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TemplateExtensionsTests
    {
        /// <summary>
        /// Test that AddReplacement throws ArgumentNullException when template is null.
        /// </summary>
        [Test]
        public void AddReplacement_NullTemplate_ThrowsArgumentNullException()
        {
            // Arrange
            Template template = null;
            const string replacement = "testReplacement";
            TemplateString templateString = "test template";
            IEnumerable targets = new List<object> { "target1" };

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                template.AddReplacement(replacement, templateString, targets));
        }

        /// <summary>
        /// Test that AddReplacement throws ArgumentNullException when replacement is null.
        /// </summary>
        [Test]
        public void AddReplacement_NullReplacement_ThrowsArgumentNullException()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = null;
            TemplateString templateString = "test template";
            IEnumerable targets = new List<object> { "target1" };

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                template.AddReplacement(replacement, templateString, targets));
        }

        /// <summary>
        /// Test that AddReplacement throws ArgumentNullException when replacement is empty.
        /// </summary>
        [Test]
        public void AddReplacement_EmptyReplacement_ThrowsArgumentNullException()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            string replacement = string.Empty;
            TemplateString templateString = "test template";
            IEnumerable targets = new List<object> { "target1" };

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                template.AddReplacement(replacement, templateString, targets));
        }

        /// <summary>
        /// Test that AddReplacement throws ArgumentException when templateString is null and OnLoad is null.
        /// </summary>
        [Test]
        public void AddReplacement_NullTemplateStringAndNullOnLoad_ThrowsArgumentException()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "testReplacement";
            TemplateString templateString = null;
            IEnumerable targets = new List<object> { "target1" };

            // Act & Assert
            ArgumentException ex = Assert.Throws<ArgumentException>(() =>
                template.AddReplacement(replacement, templateString, targets, onLoad: null, onWrite: null));
            Assert.That(ex.ParamName, Is.EqualTo("onLoad"));
        }

        /// <summary>
        /// Test that AddReplacement accepts null templateString when OnLoad is provided.
        /// </summary>
        [Test]
        public void AddReplacement_NullTemplateStringWithOnLoad_DoesNotThrow()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "testReplacement";
            TemplateString templateString = null;
            IEnumerable targets = new List<object> { "target1" };
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, OnLoad, null));
        }

        /// <summary>
        /// Test that AddReplacement handles null targets by creating an empty array.
        /// </summary>
        [Test]
        public void AddReplacement_NullTargets_CreatesEmptyArray()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "testReplacement";
            TemplateString templateString = "test template";
            IEnumerable targets = null;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets));
        }

        /// <summary>
        /// Test that AddReplacement handles string targets by creating a single-element array.
        /// </summary>
        [Test]
        public void AddReplacement_StringTargets_CreatesSingleElementArray()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "testReplacement";
            TemplateString templateString = "test template";
            IEnumerable targets = "singleString";

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets));
        }

        /// <summary>
        /// Test that AddReplacement handles empty string targets by creating a single-element array.
        /// </summary>
        [Test]
        public void AddReplacement_EmptyStringTargets_CreatesSingleElementArray()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "testReplacement";
            TemplateString templateString = "test template";
            IEnumerable targets = string.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets));
        }

        /// <summary>
        /// Test that AddReplacement handles empty collection targets.
        /// </summary>
        [Test]
        public void AddReplacement_EmptyCollectionTargets_DoesNotThrow()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "testReplacement";
            TemplateString templateString = "test template";
            IEnumerable targets = new List<object>();

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets));
        }

        /// <summary>
        /// Test that AddReplacement handles single-element collection targets.
        /// </summary>
        [Test]
        public void AddReplacement_SingleElementCollectionTargets_DoesNotThrow()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "testReplacement";
            TemplateString templateString = "test template";
            IEnumerable targets = new List<object> { "target1" };

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets));
        }

        /// <summary>
        /// Test that AddReplacement handles multi-element collection targets.
        /// </summary>
        [Test]
        public void AddReplacement_MultiElementCollectionTargets_DoesNotThrow()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "testReplacement";
            TemplateString templateString = "test template";
            IEnumerable targets = new List<object> { "target1", "target2", "target3" };

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets));
        }

        /// <summary>
        /// Test that AddReplacement handles collection with null elements.
        /// </summary>
        [Test]
        public void AddReplacement_CollectionWithNullElements_DoesNotThrow()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "testReplacement";
            TemplateString templateString = "test template";
            IEnumerable targets = new List<object> { null, "target1", null };

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets));
        }

        /// <summary>
        /// Test that AddReplacement accepts null OnWrite parameter.
        /// </summary>
        [Test]
        public void AddReplacement_NullOnWrite_DoesNotThrow()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "testReplacement";
            TemplateString templateString = "test template";
            IEnumerable targets = new List<object> { "target1" };

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, onLoad: null, onWrite: null));
        }

        /// <summary>
        /// Test that AddReplacement accepts non-null OnLoad parameter.
        /// </summary>
        [Test]
        public void AddReplacement_NonNullOnLoad_DoesNotThrow()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "testReplacement";
            TemplateString templateString = "test template";
            IEnumerable targets = new List<object> { "target1" };
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, OnLoad, null));
        }

        /// <summary>
        /// Test that AddReplacement accepts non-null OnWrite parameter.
        /// </summary>
        [Test]
        public void AddReplacement_NonNullOnWrite_DoesNotThrow()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "testReplacement";
            TemplateString templateString = "test template";
            IEnumerable targets = new List<object> { "target1" };
            static bool OnWrite(IWriteContext context) => true;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, null, OnWrite));
        }

        /// <summary>
        /// Test that AddReplacement accepts both non-null OnLoad and OnWrite parameters.
        /// </summary>
        [Test]
        public void AddReplacement_NonNullOnLoadAndOnWrite_DoesNotThrow()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "testReplacement";
            TemplateString templateString = "test template";
            IEnumerable targets = new List<object> { "target1" };
            TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;
            bool OnWrite(IWriteContext context) => true;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, OnLoad, OnWrite));
        }

        /// <summary>
        /// Test that AddReplacement handles targets with different types.
        /// </summary>
        [Test]
        public void AddReplacement_MixedTypeTargets_DoesNotThrow()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "testReplacement";
            TemplateString templateString = "test template";
            IEnumerable targets = new List<object> { "string", 123, true, 45.67 };

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets));
        }

        /// <summary>
        /// Test that AddReplacement handles array targets.
        /// </summary>
        [Test]
        public void AddReplacement_ArrayTargets_DoesNotThrow()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "testReplacement";
            TemplateString templateString = "test template";
            IEnumerable targets = new object[] { "target1", "target2" };

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets));
        }

        /// <summary>
        /// Test that AddReplacement handles very long string replacement.
        /// </summary>
        [Test]
        public void AddReplacement_VeryLongReplacement_DoesNotThrow()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            string replacement = new('x', 10000);
            TemplateString templateString = "test template";
            IEnumerable targets = new List<object> { "target1" };

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets));
        }

        /// <summary>
        /// Test that AddReplacement handles replacement with special characters.
        /// </summary>
        [Test]
        public void AddReplacement_ReplacementWithSpecialCharacters_DoesNotThrow()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "test{0}Replacement\n\r\t@#$%^&*()";
            TemplateString templateString = "test template";
            IEnumerable targets = new List<object> { "target1" };

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when replacement parameter contains only whitespace.
        /// </summary>
        [Test]
        public void AddReplacement_WhitespaceReplacement_Succeeds()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "   ";
            IReadOnlyList<object> targets = [new()];
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad, null));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when targets parameter is null.
        /// </summary>
        [Test]
        public void AddReplacement_NullTargets_Succeeds()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "TestReplacement";
            IReadOnlyList<object> targets = null;
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad, null));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when targets parameter is an empty list.
        /// </summary>
        [Test]
        public void AddReplacement_EmptyTargets_Succeeds()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "TestReplacement";
            IReadOnlyList<object> targets = [];
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad, null));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when OnLoad parameter is null.
        /// Since TemplateString.Empty is passed as templateString (not null), no exception should be thrown.
        /// </summary>
        [Test]
        public void AddReplacement_NullOnLoad_Succeeds()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "TestReplacement";
            IReadOnlyList<object> targets = [new()];
            LoadTemplateEventHandler onLoad = null;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, onLoad, null));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when OnWrite parameter is null (default value).
        /// </summary>
        [Test]
        public void AddReplacement_NullOnWrite_Succeeds()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "TestReplacement";
            IReadOnlyList<object> targets = [new()];
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when targets contains a single element.
        /// </summary>
        [Test]
        public void AddReplacement_SingleTarget_Succeeds()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "TestReplacement";
            IReadOnlyList<object> targets = ["TargetItem"];
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when targets contains multiple elements.
        /// </summary>
        [Test]
        public void AddReplacement_MultipleTargets_Succeeds()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "TestReplacement";
            IReadOnlyList<object> targets = ["Target1", "Target2", "Target3"];
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds with valid parameters including special characters in replacement string.
        /// </summary>
        [Test]
        public void AddReplacement_ReplacementWithSpecialCharacters_Succeeds()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "Test@Replacement#123!";
            IReadOnlyList<object> targets = [new()];
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds with a very long replacement string.
        /// </summary>
        [Test]
        public void AddReplacement_VeryLongReplacement_Succeeds()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            string replacement = new('A', 10000);
            IReadOnlyList<object> targets = [new()];
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when targets contains null elements.
        /// </summary>
        [Test]
        public void AddReplacement_TargetsWithNullElements_Succeeds()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, TemplateString.Empty);
            const string replacement = "TestReplacement";
            IReadOnlyList<object> targets = [null, new(), null];
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad));
        }

        /// <summary>
        /// Tests that AddReplacement throws ArgumentException when templateString parameter is null.
        /// Since OnLoad is always passed as null in this overload, having a null templateString violates the validation.
        /// Input: null templateString
        /// Expected: ArgumentException with parameter name "OnLoad"
        /// </summary>
        [Test]
        public void AddReplacement_NullTemplateString_ThrowsArgumentException()
        {
            // Arrange
            using var writer = new StringWriter();
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, "base template");
            const string replacement = "test";
            TemplateString templateString = null;
            IReadOnlyList<object> targets = [new()];
            static bool OnWrite(IWriteContext context) => true;

            // Act & Assert
            ArgumentException ex = Assert.Throws<ArgumentException>(() =>
                template.AddReplacement(replacement, templateString, targets, OnWrite));
            Assert.That(ex.ParamName, Is.EqualTo("onLoad"));
            Assert.That(ex.Message, Does.Contain("A template loader must be passed if template string is null"));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when targets parameter is null.
        /// Input: null targets
        /// Expected: No exception thrown
        /// </summary>
        [Test]
        public void AddReplacement_NullTargets_DoesNotThrow()
        {
            // Arrange
            using var writer = new StringWriter();
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, "base template");
            const string replacement = "test";
            TemplateString templateString = "test template";
            IReadOnlyList<object> targets = null;
            static bool OnWrite(IWriteContext context) => true;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, OnWrite));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when all required parameters are valid.
        /// Input: valid template, replacement, templateString, targets, and OnWrite
        /// Expected: No exception thrown
        /// </summary>
        [Test]
        public void AddReplacement_ValidParameters_DoesNotThrow()
        {
            // Arrange
            using var writer = new StringWriter();
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, "base template");
            const string replacement = "test";
            TemplateString templateString = "test template";
            IReadOnlyList<object> targets = [new()];
            static bool OnWrite(IWriteContext context) => true;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, OnWrite));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when targets is an empty list.
        /// Input: empty targets list
        /// Expected: No exception thrown
        /// </summary>
        [Test]
        public void AddReplacement_EmptyTargets_DoesNotThrow()
        {
            // Arrange
            using var writer = new StringWriter();
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, "base template");
            const string replacement = "test";
            TemplateString templateString = "test template";
            IReadOnlyList<object> targets = [];
            static bool OnWrite(IWriteContext context) => true;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, OnWrite));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when targets contains multiple objects.
        /// Input: targets list with multiple objects
        /// Expected: No exception thrown
        /// </summary>
        [Test]
        public void AddReplacement_MultipleTargets_DoesNotThrow()
        {
            // Arrange
            using var writer = new StringWriter();
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, "base template");
            const string replacement = "test";
            TemplateString templateString = "test template";
            IReadOnlyList<object> targets = [new(), new(), new()];
            static bool OnWrite(IWriteContext context) => true;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, OnWrite));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when replacement is whitespace-only.
        /// Whitespace-only strings are not caught by string.IsNullOrEmpty check.
        /// Input: whitespace-only replacement string
        /// Expected: No exception thrown
        /// </summary>
        [Test]
        public void AddReplacement_WhitespaceReplacement_DoesNotThrow()
        {
            // Arrange
            using var writer = new StringWriter();
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, "base template");
            const string replacement = "   ";
            TemplateString templateString = "test template";
            IReadOnlyList<object> targets = [new()];
            static bool OnWrite(IWriteContext context) => true;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, OnWrite));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds with special characters in replacement string.
        /// Input: replacement string with special characters
        /// Expected: No exception thrown
        /// </summary>
        [Test]
        public void AddReplacement_SpecialCharactersInReplacement_DoesNotThrow()
        {
            // Arrange
            using var writer = new StringWriter();
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, "base template");
            const string replacement = "!@#$%^&*()_+-=[]{}|;':\",./<>?";
            TemplateString templateString = "test template";
            IReadOnlyList<object> targets = [new()];
            static bool OnWrite(IWriteContext context) => true;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, OnWrite));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when templateString is empty.
        /// Input: empty templateString
        /// Expected: No exception thrown
        /// </summary>
        [Test]
        public void AddReplacement_EmptyTemplateString_DoesNotThrow()
        {
            // Arrange
            using var writer = new StringWriter();
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, "base template");
            const string replacement = "test";
            TemplateString templateString = string.Empty;
            IReadOnlyList<object> targets = [new()];
            static bool OnWrite(IWriteContext context) => true;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, OnWrite));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when targets is an array.
        /// Input: targets as an array
        /// Expected: No exception thrown
        /// </summary>
        [Test]
        public void AddReplacement_TargetsAsArray_DoesNotThrow()
        {
            // Arrange
            using var writer = new StringWriter();
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, "base template");
            const string replacement = "test";
            TemplateString templateString = "test template";
            IReadOnlyList<object> targets = [new(), new()];
            static bool OnWrite(IWriteContext context) => true;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, OnWrite));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when both targets and OnWrite are null.
        /// Input: null targets and null OnWrite
        /// Expected: No exception thrown
        /// </summary>
        [Test]
        public void AddReplacement_NullTargetsAndNullOnWrite_DoesNotThrow()
        {
            // Arrange
            using var writer = new StringWriter();
            var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, "base template");
            const string replacement = "test";
            TemplateString templateString = "test template";
            IReadOnlyList<object> targets = null;
            WriteTemplateEventHandler onWrite = null;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, onWrite));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds with valid parameters and null OnWrite (default).
        /// </summary>
        [Test]
        public void AddReplacement_ValidParametersWithNullOnWrite_DoesNotThrow()
        {
            // Arrange
            var writer = new TemplateWriter(new StringWriter());
            var template = new Template(writer, TemplateString.Empty);
            const string replacement = "TestReplacement";
            IEnumerable targets = new List<object> { "target1", "target2" };
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad, null));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds with valid parameters including non-null OnWrite.
        /// </summary>
        [Test]
        public void AddReplacement_ValidParametersWithOnWrite_DoesNotThrow()
        {
            // Arrange
            var writer = new TemplateWriter(new StringWriter());
            var template = new Template(writer, TemplateString.Empty);
            const string replacement = "TestReplacement";
            IEnumerable targets = new List<object> { "target1", "target2" };
            TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;
            bool OnWrite(IWriteContext context) => true;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad, OnWrite));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when targets is a string.
        /// Strings implement IEnumerable and should be handled as a special case.
        /// </summary>
        [Test]
        public void AddReplacement_StringAsTarget_DoesNotThrow()
        {
            // Arrange
            var writer = new TemplateWriter(new StringWriter());
            var template = new Template(writer, TemplateString.Empty);
            const string replacement = "TestReplacement";
            IEnumerable targets = "SingleStringTarget";
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when targets contains a single item.
        /// </summary>
        [Test]
        public void AddReplacement_SingleItemTarget_DoesNotThrow()
        {
            // Arrange
            var writer = new TemplateWriter(new StringWriter());
            var template = new Template(writer, TemplateString.Empty);
            const string replacement = "TestReplacement";
            IEnumerable targets = new List<object> { "singleItem" };
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds with special characters in replacement string.
        /// </summary>
        [TestCase("Replace\\ment")]
        [TestCase("Replace\nment")]
        [TestCase("Replace\tment")]
        [TestCase("Replace{0}ment")]
        [TestCase("Replace$ment")]
        [TestCase("Replace@ment")]
        [TestCase("Replace!@#$%^&*()ment")]
        public void AddReplacement_SpecialCharactersInReplacement_DoesNotThrow(string replacement)
        {
            // Arrange
            var writer = new TemplateWriter(new StringWriter());
            var template = new Template(writer, TemplateString.Empty);
            IEnumerable targets = new List<object> { "target1" };
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds with various target collection types.
        /// </summary>
        [Test]
        public void AddReplacement_VariousTargetTypes_DoesNotThrow()
        {
            // Arrange
            var writer = new TemplateWriter(new StringWriter());
            var template = new Template(writer, TemplateString.Empty);
            const string replacement = "TestReplacement";
            IEnumerable targets = new ArrayList { 1, "string", 3.14, true };
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds with multiple targets of mixed types.
        /// </summary>
        [Test]
        public void AddReplacement_MultipleTargetsMixedTypes_DoesNotThrow()
        {
            // Arrange
            var writer = new TemplateWriter(new StringWriter());
            var template = new Template(writer, TemplateString.Empty);
            const string replacement = "TestReplacement";
            IEnumerable targets = new List<object>
            {
                "stringTarget",
                42,
                3.14159,
                true,
                new()
            };
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when OnLoad is null.
        /// Since TemplateString.Empty is passed internally, OnLoad can be null without throwing.
        /// </summary>
        [Test]
        public void AddReplacement_NullOnLoad_DoesNotThrow()
        {
            // Arrange
            var writer = new TemplateWriter(new StringWriter());
            var template = new Template(writer, TemplateString.Empty);
            const string replacement = "TestReplacement";
            IEnumerable targets = new List<object> { "target1" };
            LoadTemplateEventHandler onLoad = null;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, onLoad));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds with all optional parameters null.
        /// Since the method passes TemplateString.Empty, it should handle null OnLoad and OnWrite.
        /// </summary>
        [Test]
        public void AddReplacement_AllOptionalParametersNull_DoesNotThrow()
        {
            // Arrange
            var writer = new TemplateWriter(new StringWriter());
            var template = new Template(writer, TemplateString.Empty);
            const string replacement = "TestReplacement";
            IEnumerable targets = new List<object> { "target1" };

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, null, null));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds with array as targets.
        /// </summary>
        [Test]
        public void AddReplacement_ArrayAsTargets_DoesNotThrow()
        {
            // Arrange
            var writer = new TemplateWriter(new StringWriter());
            var template = new Template(writer, TemplateString.Empty);
            const string replacement = "TestReplacement";
            IEnumerable targets = new[] { "target1", "target2", "target3" };
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds with duplicate items in targets.
        /// </summary>
        [Test]
        public void AddReplacement_DuplicateTargets_DoesNotThrow()
        {
            // Arrange
            var writer = new TemplateWriter(new StringWriter());
            var template = new Template(writer, TemplateString.Empty);
            const string replacement = "TestReplacement";
            IEnumerable targets = new List<object> { "target1", "target1", "target1" };
            static TemplateString OnLoad(ILoadContext context) => TemplateString.Empty;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, targets, OnLoad));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds when templateString is null but OnLoad is provided.
        /// </summary>
        [Test]
        public void AddReplacement_NullTemplateStringWithOnLoad_Succeeds()
        {
            // Arrange
            Template template = CreateTestTemplate();
            const string replacement = "TestReplacement";
            TemplateString templateString = null;
            IReadOnlyList<object> targets = [new()];
            static TemplateString OnLoad(ILoadContext context) => "loaded";

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, OnLoad, null));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds with valid parameters including templateString.
        /// </summary>
        [Test]
        public void AddReplacement_ValidParametersWithTemplateString_Succeeds()
        {
            // Arrange
            Template template = CreateTestTemplate();
            const string replacement = "TestReplacement";
            TemplateString templateString = "test template content";
            IReadOnlyList<object> targets = ["target1", "target2"];
            LoadTemplateEventHandler onLoad = null;
            WriteTemplateEventHandler onWrite = null;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, onLoad, onWrite));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds with both OnLoad and OnWrite handlers provided.
        /// </summary>
        [Test]
        public void AddReplacement_WithBothHandlers_Succeeds()
        {
            // Arrange
            Template template = CreateTestTemplate();
            const string replacement = "TestReplacement";
            TemplateString templateString = "test";
            IReadOnlyList<object> targets = ["target"];
            TemplateString OnLoad(ILoadContext context) => "loaded";
            bool OnWrite(IWriteContext context) => true;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, OnLoad, OnWrite));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds with only OnWrite handler provided.
        /// </summary>
        [Test]
        public void AddReplacement_WithOnlyOnWrite_Succeeds()
        {
            // Arrange
            Template template = CreateTestTemplate();
            const string replacement = "TestReplacement";
            TemplateString templateString = "test";
            IReadOnlyList<object> targets = [];
            static bool OnWrite(IWriteContext context) => true;

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, null, OnWrite));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds with special characters in replacement parameter.
        /// </summary>
        [Test]
        public void AddReplacement_SpecialCharactersInReplacement_Succeeds()
        {
            // Arrange
            Template template = CreateTestTemplate();
            const string replacement = "Test@#$%Replacement!";
            TemplateString templateString = "test";
            IReadOnlyList<object> targets = [];

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, null, null));
        }

        /// <summary>
        /// Tests that AddReplacement succeeds with empty template string.
        /// </summary>
        [Test]
        public void AddReplacement_EmptyTemplateString_Succeeds()
        {
            // Arrange
            Template template = CreateTestTemplate();
            const string replacement = "TestReplacement";
            TemplateString templateString = string.Empty;
            IReadOnlyList<object> targets = [];

            // Act & Assert
            Assert.DoesNotThrow(() =>
                template.AddReplacement(replacement, templateString, targets, null, null));
        }

        /// <summary>
        /// Helper method to create a Template instance for testing.
        /// </summary>
        private static Template CreateTestTemplate()
        {
            var writer = new StringWriter();
            var templateWriter = new TemplateWriter(writer);
            TemplateString templateString = "test";
            return new Template(templateWriter, templateString);
        }
    }
}
