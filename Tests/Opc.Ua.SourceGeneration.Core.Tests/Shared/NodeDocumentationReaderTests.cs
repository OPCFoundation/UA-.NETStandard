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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Shared.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="NodeDocumentationMap"/> class.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeDocumentationMapTests
    {
        /// <summary>
        /// Tests that the constructor creates a valid instance without throwing exceptions.
        /// Input: None (parameterless constructor).
        /// Expected: Instance is successfully created and not null.
        /// </summary>
        [Test]
        public void Constructor_WhenCalled_CreatesInstanceSuccessfully()
        {
            // Arrange & Act
            var map = new NodeDocumentationMap();

            // Assert
            Assert.That(map, Is.Not.Null);
        }

        /// <summary>
        /// Tests that the constructor creates a ClassMap instance that can be registered with CsvHelper.
        /// Input: None (parameterless constructor).
        /// Expected: Instance can be registered with CsvContext without throwing exceptions.
        /// </summary>
        [Test]
        public void Constructor_WhenCalled_CreatesInstanceThatCanBeRegisteredWithCsvHelper()
        {
            // Arrange
            const string csvData = "Id,Name,Link,ConformanceUnits\n1,TestName,TestLink,Unit1";
            var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                MissingFieldFound = (args) => { }
            };

            // Act & Assert
            using var reader = new StringReader(csvData);
            using var csv = new CsvReader(reader, configuration);
            Assert.DoesNotThrow(() => csv.Context.RegisterClassMap<NodeDocumentationMap>());
        }

        /// <summary>
        /// Tests that the constructor configures mappings that allow CsvHelper to read NodeDocumentationRow records.
        /// Input: CSV data with all columns (Id, Name, Link, ConformanceUnits).
        /// Expected: CsvHelper can successfully read records using the configured mappings.
        /// </summary>
        [Test]
        public void Constructor_WhenCalled_ConfiguresMappingsThatAllowCsvReading()
        {
            // Arrange
            const string csvData = "Id,Name,Link,ConformanceUnits\n42,TestNode,http://test.com,Unit1";
            var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                MissingFieldFound = (args) => { }
            };

            // Act
            NodeDocumentationRow result = null;
            using (var reader = new StringReader(csvData))
            using (var csv = new CsvReader(reader, configuration))
            {
                csv.Context.RegisterClassMap<NodeDocumentationMap>();
                result = csv.GetRecords<NodeDocumentationRow>().FirstOrDefault();
            }

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Id, Is.EqualTo(42));
            Assert.That(result.Name, Is.EqualTo("TestNode"));
            Assert.That(result.Link, Is.EqualTo("http://test.com"));
        }

        /// <summary>
        /// Tests that the constructor configures the Id property mapping correctly.
        /// Input: CSV with Id column containing uint value.
        /// Expected: Id property is correctly mapped and parsed as uint.
        /// </summary>
        [Test]
        public void Constructor_WhenCalled_ConfiguresIdMappingCorrectly()
        {
            // Arrange
            const string csvData = "Id,Name,Link,ConformanceUnits\n999,Test,Link,";
            var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                MissingFieldFound = (args) => { }
            };

            // Act
            NodeDocumentationRow result = null;
            using (var reader = new StringReader(csvData))
            using (var csv = new CsvReader(reader, configuration))
            {
                csv.Context.RegisterClassMap<NodeDocumentationMap>();
                result = csv.GetRecords<NodeDocumentationRow>().FirstOrDefault();
            }

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Id, Is.EqualTo(999u));
        }

        /// <summary>
        /// Tests that the constructor configures the Name property mapping correctly.
        /// Input: CSV with Name column containing string value.
        /// Expected: Name property is correctly mapped and assigned.
        /// </summary>
        [Test]
        public void Constructor_WhenCalled_ConfiguresNameMappingCorrectly()
        {
            // Arrange
            const string csvData = "Id,Name,Link,ConformanceUnits\n1,TestNodeName,Link,";
            var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                MissingFieldFound = (args) => { }
            };

            // Act
            NodeDocumentationRow result = null;
            using (var reader = new StringReader(csvData))
            using (var csv = new CsvReader(reader, configuration))
            {
                csv.Context.RegisterClassMap<NodeDocumentationMap>();
                result = csv.GetRecords<NodeDocumentationRow>().FirstOrDefault();
            }

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo("TestNodeName"));
        }

        /// <summary>
        /// Tests that the constructor configures the Link property mapping correctly.
        /// Input: CSV with Link column containing string value.
        /// Expected: Link property is correctly mapped and assigned.
        /// </summary>
        [Test]
        public void Constructor_WhenCalled_ConfiguresLinkMappingCorrectly()
        {
            // Arrange
            const string csvData = "Id,Name,Link,ConformanceUnits\n1,Name,http://example.com,";
            var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                MissingFieldFound = (args) => { }
            };

            // Act
            NodeDocumentationRow result = null;
            using (var reader = new StringReader(csvData))
            using (var csv = new CsvReader(reader, configuration))
            {
                csv.Context.RegisterClassMap<NodeDocumentationMap>();
                result = csv.GetRecords<NodeDocumentationRow>().FirstOrDefault();
            }

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Link, Is.EqualTo("http://example.com"));
        }

        /// <summary>
        /// Tests that the constructor configures the ConformanceUnits property mapping with ArrayConverter.
        /// Input: CSV with ConformanceUnits column containing array-formatted string.
        /// Expected: ConformanceUnits property is correctly mapped using the custom ArrayConverter.
        /// </summary>
        [Test]
        public void Constructor_WhenCalled_ConfiguresConformanceUnitsMappingWithArrayConverter()
        {
            // Arrange
            const string csvData = "Id,Name,Link,ConformanceUnits\n1,Name,Link,\"[\"\"Unit1\"\",\"\"Unit2\"\"]\"";
            var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                MissingFieldFound = (args) => { }
            };

            // Act
            NodeDocumentationRow result = null;
            using (var reader = new StringReader(csvData))
            using (var csv = new CsvReader(reader, configuration))
            {
                csv.Context.RegisterClassMap<NodeDocumentationMap>();
                result = csv.GetRecords<NodeDocumentationRow>().FirstOrDefault();
            }

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.ConformanceUnits, Is.Not.Null);
        }
    }

    /// <summary>
    /// Unit tests for the <see cref="ArrayConverter{T}"/> class.
    /// </summary>
    [TestFixture]
    public class ArrayConverterTests
    {
        /// <summary>
        /// Tests that ConvertToString returns an empty string when value parameter is null.
        /// </summary>
        [Test]
        public void ConvertToString_NullValue_ReturnsEmptyString()
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);

            // Act
            string result = converter.ConvertToString(null, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that ConvertToString returns an empty string when value is not an IList of strings.
        /// </summary>
        [TestCase(123)]
        [TestCase("plain string")]
        [TestCase(3.14)]
        public void ConvertToString_NonListValue_ReturnsEmptyString(object value)
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);

            // Act
            string result = converter.ConvertToString(value, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that ConvertToString returns an empty string when value is an empty list.
        /// </summary>
        [Test]
        public void ConvertToString_EmptyList_ReturnsEmptyString()
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);
            var emptyList = new List<string>();

            // Act
            string result = converter.ConvertToString(emptyList, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that ConvertToString correctly converts a list with a single element.
        /// </summary>
        [Test]
        public void ConvertToString_SingleElement_ReturnsSingleElement()
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);
            var list = new List<string> { "Element1" };

            // Act
            string result = converter.ConvertToString(list, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.EqualTo("Element1"));
        }

        /// <summary>
        /// Tests that ConvertToString correctly converts a list with multiple elements separated by semicolons.
        /// </summary>
        [Test]
        public void ConvertToString_MultipleElements_ReturnsSemicolonSeparatedString()
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);
            var list = new List<string> { "Element1", "Element2", "Element3" };

            // Act
            string result = converter.ConvertToString(list, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.EqualTo("Element1;Element2;Element3"));
        }

        /// <summary>
        /// Tests that ConvertToString trims whitespace from elements before adding them to the result.
        /// </summary>
        [Test]
        public void ConvertToString_ElementsWithWhitespace_TrimmedElementsInResult()
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);
            var list = new List<string> { "  Element1  ", "\tElement2\t", " Element3 " };

            // Act
            string result = converter.ConvertToString(list, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.EqualTo("Element1;Element2;Element3"));
        }

        /// <summary>
        /// Tests that ConvertToString skips null elements in the list.
        /// </summary>
        [Test]
        public void ConvertToString_ListWithNullElements_SkipsNullElements()
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);
            var list = new List<string> { "Element1", null, "Element2" };

            // Act
            string result = converter.ConvertToString(list, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.EqualTo("Element1;Element2"));
        }

        /// <summary>
        /// Tests that ConvertToString skips empty string elements in the list.
        /// </summary>
        [Test]
        public void ConvertToString_ListWithEmptyStrings_SkipsEmptyStrings()
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);
            var list = new List<string> { "Element1", string.Empty, "Element2" };

            // Act
            string result = converter.ConvertToString(list, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.EqualTo("Element1;Element2"));
        }

        /// <summary>
        /// Tests that ConvertToString skips whitespace-only elements after trimming.
        /// </summary>
        [Test]
        public void ConvertToString_ListWithWhitespaceOnlyElements_SkipsWhitespaceElements()
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);
            var list = new List<string> { "Element1", "   ", "\t\t", "Element2" };

            // Act
            string result = converter.ConvertToString(list, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.EqualTo("Element1;Element2"));
        }

        /// <summary>
        /// Tests that ConvertToString correctly handles a list with mixed valid and invalid elements.
        /// </summary>
        [Test]
        public void ConvertToString_MixedValidAndInvalidElements_OnlyIncludesValidElements()
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);
            var list = new List<string> { "Element1", null, string.Empty, "  ", "Element2", "\t", "Element3" };

            // Act
            string result = converter.ConvertToString(list, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.EqualTo("Element1;Element2;Element3"));
        }

        /// <summary>
        /// Tests that ConvertToString returns an empty string when all elements are null, empty, or whitespace.
        /// </summary>
        [Test]
        public void ConvertToString_AllInvalidElements_ReturnsEmptyString()
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);
            var list = new List<string> { null, string.Empty, "   ", "\t\t" };

            // Act
            string result = converter.ConvertToString(list, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that ConvertToString correctly handles elements with special characters.
        /// </summary>
        [Test]
        public void ConvertToString_ElementsWithSpecialCharacters_PreservesSpecialCharacters()
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);
            var list = new List<string> { "Element@1", "Element#2", "Element$3" };

            // Act
            string result = converter.ConvertToString(list, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.EqualTo("Element@1;Element#2;Element$3"));
        }

        /// <summary>
        /// Tests that ConvertToString correctly handles elements containing semicolons.
        /// </summary>
        [Test]
        public void ConvertToString_ElementsWithSemicolons_PreservesSemicolonsInElements()
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);
            var list = new List<string> { "Element;1", "Element2", "Element;3" };

            // Act
            string result = converter.ConvertToString(list, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.EqualTo("Element;1;Element2;Element;3"));
        }

        /// <summary>
        /// Tests that ConvertToString correctly handles a very long list of elements.
        /// </summary>
        [Test]
        public void ConvertToString_VeryLongList_ReturnsAllElementsSeparated()
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);
            var list = new List<string>();
            for (int i = 0; i < 1000; i++)
            {
                list.Add($"Element{i}");
            }

            // Act
            string result = converter.ConvertToString(list, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Does.StartWith("Element0;Element1;Element2"));
            Assert.That(result, Does.EndWith("Element997;Element998;Element999"));
            Assert.That(result.Split(';').Length, Is.EqualTo(1000));
        }

        /// <summary>
        /// Tests that ConvertToString correctly handles elements with very long strings.
        /// </summary>
        [Test]
        public void ConvertToString_VeryLongStringElements_PreservesLongStrings()
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);
            string longString = new('A', 10000);
            var list = new List<string> { longString, "Element2" };

            // Act
            string result = converter.ConvertToString(list, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Does.StartWith(longString));
            Assert.That(result, Does.EndWith(";Element2"));
            Assert.That(result.Length, Is.EqualTo(10000 + 1 + 8));
        }

        /// <summary>
        /// Tests that ConvertToString correctly handles a list with only one valid element among many invalid ones.
        /// </summary>
        [Test]
        public void ConvertToString_SingleValidElementAmongInvalid_ReturnsSingleElement()
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);
            var list = new List<string> { null, string.Empty, "  ", "ValidElement", "\t", string.Empty };

            // Act
            string result = converter.ConvertToString(list, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.EqualTo("ValidElement"));
        }

        /// <summary>
        /// Tests that ConvertToString does not add a trailing semicolon to the result.
        /// </summary>
        [Test]
        public void ConvertToString_MultipleElements_NoTrailingSemicolon()
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);
            var list = new List<string> { "Element1", "Element2" };

            // Act
            string result = converter.ConvertToString(list, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Does.Not.EndWith(";"));
            Assert.That(result, Is.EqualTo("Element1;Element2"));
        }

        /// <summary>
        /// Tests that ConvertToString does not add a leading semicolon to the result.
        /// </summary>
        [Test]
        public void ConvertToString_MultipleElements_NoLeadingSemicolon()
        {
            // Arrange
            var converter = new ArrayConverter<NodeDocumentationRow>();
            var mockRow = new Mock<IWriterRow>();
            var mockMemberMapData = new Mock<MemberMapData>(null);
            var list = new List<string> { "Element1", "Element2" };

            // Act
            string result = converter.ConvertToString(list, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Does.Not.StartWith(";"));
            Assert.That(result, Is.EqualTo("Element1;Element2"));
        }

        /// <summary>
        /// Tests that ConvertFromString returns an empty list when the input text is null.
        /// </summary>
        [Test]
        public void ConvertFromString_NullText_ReturnsEmptyList()
        {
            // Arrange
            var converter = new ArrayConverter<string>();
            var mockRow = new Mock<IReaderRow>();
            var mockMemberMapData = new Mock<MemberMapData>(MockBehavior.Loose, null);

            // Act
            object result = converter.ConvertFromString(null, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<List<string>>());
            var list = (List<string>)result;
            Assert.That(list, Is.Empty);
        }

        /// <summary>
        /// Tests that ConvertFromString returns an empty list when the input text is an empty string.
        /// </summary>
        [Test]
        public void ConvertFromString_EmptyString_ReturnsEmptyList()
        {
            // Arrange
            var converter = new ArrayConverter<string>();
            var mockRow = new Mock<IReaderRow>();
            var mockMemberMapData = new Mock<MemberMapData>(MockBehavior.Loose, null);

            // Act
            object result = converter.ConvertFromString(string.Empty, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<List<string>>());
            var list = (List<string>)result;
            Assert.That(list, Is.Empty);
        }

        /// <summary>
        /// Tests that ConvertFromString returns an empty list when the input text contains only whitespace.
        /// </summary>
        [Test]
        public void ConvertFromString_WhitespaceOnlyString_ReturnsEmptyList()
        {
            // Arrange
            var converter = new ArrayConverter<string>();
            var mockRow = new Mock<IReaderRow>();
            var mockMemberMapData = new Mock<MemberMapData>(MockBehavior.Loose, null);

            // Act
            object result = converter.ConvertFromString("   ", mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<List<string>>());
            var list = (List<string>)result;
            Assert.That(list, Is.Empty);
        }

        /// <summary>
        /// Tests that ConvertFromString correctly parses a single element without semicolons.
        /// </summary>
        [Test]
        public void ConvertFromString_SingleElement_ReturnsSingleElementList()
        {
            // Arrange
            var converter = new ArrayConverter<string>();
            var mockRow = new Mock<IReaderRow>();
            var mockMemberMapData = new Mock<MemberMapData>(MockBehavior.Loose, null);
            const string input = "item1";

            // Act
            object result = converter.ConvertFromString(input, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<List<string>>());
            var list = (List<string>)result;
            Assert.That(list, Has.Count.EqualTo(1));
            Assert.That(list[0], Is.EqualTo("item1"));
        }

        /// <summary>
        /// Tests that ConvertFromString correctly parses multiple semicolon-separated elements.
        /// </summary>
        [Test]
        public void ConvertFromString_MultipleSemicolonSeparatedElements_ReturnsListWithAllElements()
        {
            // Arrange
            var converter = new ArrayConverter<string>();
            var mockRow = new Mock<IReaderRow>();
            var mockMemberMapData = new Mock<MemberMapData>(MockBehavior.Loose, null);
            const string input = "item1;item2;item3";

            // Act
            object result = converter.ConvertFromString(input, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<List<string>>());
            var list = (List<string>)result;
            Assert.That(list, Has.Count.EqualTo(3));
            Assert.That(list[0], Is.EqualTo("item1"));
            Assert.That(list[1], Is.EqualTo("item2"));
            Assert.That(list[2], Is.EqualTo("item3"));
        }

        /// <summary>
        /// Tests that ConvertFromString trims leading and trailing whitespace from elements.
        /// </summary>
        [Test]
        public void ConvertFromString_ElementsWithWhitespace_TrimsWhitespace()
        {
            // Arrange
            var converter = new ArrayConverter<string>();
            var mockRow = new Mock<IReaderRow>();
            var mockMemberMapData = new Mock<MemberMapData>(MockBehavior.Loose, null);
            const string input = "  item1  ;  item2  ;  item3  ";

            // Act
            object result = converter.ConvertFromString(input, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<List<string>>());
            var list = (List<string>)result;
            Assert.That(list, Has.Count.EqualTo(3));
            Assert.That(list[0], Is.EqualTo("item1"));
            Assert.That(list[1], Is.EqualTo("item2"));
            Assert.That(list[2], Is.EqualTo("item3"));
        }

        /// <summary>
        /// Tests that ConvertFromString filters out empty elements between semicolons.
        /// </summary>
        [Test]
        public void ConvertFromString_EmptyElementsBetweenSemicolons_FiltersOutEmptyElements()
        {
            // Arrange
            var converter = new ArrayConverter<string>();
            var mockRow = new Mock<IReaderRow>();
            var mockMemberMapData = new Mock<MemberMapData>(MockBehavior.Loose, null);
            const string input = "item1;;item2";

            // Act
            object result = converter.ConvertFromString(input, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<List<string>>());
            var list = (List<string>)result;
            Assert.That(list, Has.Count.EqualTo(2));
            Assert.That(list[0], Is.EqualTo("item1"));
            Assert.That(list[1], Is.EqualTo("item2"));
        }

        /// <summary>
        /// Tests that ConvertFromString filters out leading semicolons and parses remaining elements.
        /// </summary>
        [Test]
        public void ConvertFromString_LeadingSemicolon_FiltersOutLeadingEmpty()
        {
            // Arrange
            var converter = new ArrayConverter<string>();
            var mockRow = new Mock<IReaderRow>();
            var mockMemberMapData = new Mock<MemberMapData>(MockBehavior.Loose, null);
            const string input = ";item1;item2";

            // Act
            object result = converter.ConvertFromString(input, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<List<string>>());
            var list = (List<string>)result;
            Assert.That(list, Has.Count.EqualTo(2));
            Assert.That(list[0], Is.EqualTo("item1"));
            Assert.That(list[1], Is.EqualTo("item2"));
        }

        /// <summary>
        /// Tests that ConvertFromString filters out trailing semicolons and parses remaining elements.
        /// </summary>
        [Test]
        public void ConvertFromString_TrailingSemicolon_FiltersOutTrailingEmpty()
        {
            // Arrange
            var converter = new ArrayConverter<string>();
            var mockRow = new Mock<IReaderRow>();
            var mockMemberMapData = new Mock<MemberMapData>(MockBehavior.Loose, null);
            const string input = "item1;item2;";

            // Act
            object result = converter.ConvertFromString(input, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<List<string>>());
            var list = (List<string>)result;
            Assert.That(list, Has.Count.EqualTo(2));
            Assert.That(list[0], Is.EqualTo("item1"));
            Assert.That(list[1], Is.EqualTo("item2"));
        }

        /// <summary>
        /// Tests that ConvertFromString returns an empty list when the input contains only semicolons.
        /// </summary>
        [Test]
        public void ConvertFromString_OnlySemicolons_ReturnsEmptyList()
        {
            // Arrange
            var converter = new ArrayConverter<string>();
            var mockRow = new Mock<IReaderRow>();
            var mockMemberMapData = new Mock<MemberMapData>(MockBehavior.Loose, null);
            const string input = ";;;";

            // Act
            object result = converter.ConvertFromString(input, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<List<string>>());
            var list = (List<string>)result;
            Assert.That(list, Is.Empty);
        }

        /// <summary>
        /// Tests that ConvertFromString filters out whitespace-only elements between semicolons.
        /// </summary>
        [Test]
        public void ConvertFromString_WhitespaceOnlyElements_FiltersOutWhitespaceElements()
        {
            // Arrange
            var converter = new ArrayConverter<string>();
            var mockRow = new Mock<IReaderRow>();
            var mockMemberMapData = new Mock<MemberMapData>(MockBehavior.Loose, null);
            const string input = "item1;   ;item2";

            // Act
            object result = converter.ConvertFromString(input, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<List<string>>());
            var list = (List<string>)result;
            Assert.That(list, Has.Count.EqualTo(2));
            Assert.That(list[0], Is.EqualTo("item1"));
            Assert.That(list[1], Is.EqualTo("item2"));
        }

        /// <summary>
        /// Tests that ConvertFromString correctly handles elements with special characters.
        /// </summary>
        [Test]
        public void ConvertFromString_ElementsWithSpecialCharacters_PreservesSpecialCharacters()
        {
            // Arrange
            var converter = new ArrayConverter<string>();
            var mockRow = new Mock<IReaderRow>();
            var mockMemberMapData = new Mock<MemberMapData>(MockBehavior.Loose, null);
            const string input = "item@1;item#2;item$3";

            // Act
            object result = converter.ConvertFromString(input, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<List<string>>());
            var list = (List<string>)result;
            Assert.That(list, Has.Count.EqualTo(3));
            Assert.That(list[0], Is.EqualTo("item@1"));
            Assert.That(list[1], Is.EqualTo("item#2"));
            Assert.That(list[2], Is.EqualTo("item$3"));
        }

        /// <summary>
        /// Tests that ConvertFromString correctly handles a very long input string.
        /// </summary>
        [Test]
        public void ConvertFromString_VeryLongString_ParsesCorrectly()
        {
            // Arrange
            var converter = new ArrayConverter<string>();
            var mockRow = new Mock<IReaderRow>();
            var mockMemberMapData = new Mock<MemberMapData>(MockBehavior.Loose, null);
            var elements = Enumerable.Range(1, 1000).Select(i => $"item{i}").ToList();
            string input = string.Join(";", elements);

            // Act
            object result = converter.ConvertFromString(input, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<List<string>>());
            var list = (List<string>)result;
            Assert.That(list, Has.Count.EqualTo(1000));
            Assert.That(list, Is.EqualTo(elements));
        }

        /// <summary>
        /// Tests that ConvertFromString correctly handles mixed valid and empty elements.
        /// </summary>
        [Test]
        public void ConvertFromString_MixedValidAndEmptyElements_FiltersOutEmptyElements()
        {
            // Arrange
            var converter = new ArrayConverter<string>();
            var mockRow = new Mock<IReaderRow>();
            var mockMemberMapData = new Mock<MemberMapData>(MockBehavior.Loose, null);
            const string input = ";item1;;item2; ;item3;";

            // Act
            object result = converter.ConvertFromString(input, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<List<string>>());
            var list = (List<string>)result;
            Assert.That(list, Has.Count.EqualTo(3));
            Assert.That(list[0], Is.EqualTo("item1"));
            Assert.That(list[1], Is.EqualTo("item2"));
            Assert.That(list[2], Is.EqualTo("item3"));
        }

        /// <summary>
        /// Tests that ConvertFromString correctly handles a single semicolon.
        /// </summary>
        [Test]
        public void ConvertFromString_SingleSemicolon_ReturnsEmptyList()
        {
            // Arrange
            var converter = new ArrayConverter<string>();
            var mockRow = new Mock<IReaderRow>();
            var mockMemberMapData = new Mock<MemberMapData>(MockBehavior.Loose, null);
            const string input = ";";

            // Act
            object result = converter.ConvertFromString(input, mockRow.Object, mockMemberMapData.Object);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<List<string>>());
            var list = (List<string>)result;
            Assert.That(list, Is.Empty);
        }
    }

    /// <summary>
    /// Unit tests for the <see cref="NodeDocumentationReader"/> class.
    /// </summary>
    [TestFixture]
    public class NodeDocumentationReaderTests
    {
        /// <summary>
        /// Tests that Load with no filepaths returns an empty list.
        /// Input: No arguments provided.
        /// Expected: Returns an empty list of NodeDocumentationRow.
        /// </summary>
        [Test]
        public void Load_NoFilepaths_ReturnsEmptyList()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            var reader = new NodeDocumentationReader(mockFileSystem.Object);

            // Act
            IList<NodeDocumentationRow> result = reader.Load();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
            mockFileSystem.Verify(fs => fs.OpenRead(It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// Tests that Load with an empty array returns an empty list.
        /// Input: Empty string array.
        /// Expected: Returns an empty list of NodeDocumentationRow.
        /// </summary>
        [Test]
        public void Load_EmptyArray_ReturnsEmptyList()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            var reader = new NodeDocumentationReader(mockFileSystem.Object);
            string[] emptyArray = [];

            // Act
            IList<NodeDocumentationRow> result = reader.Load(emptyArray);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
            mockFileSystem.Verify(fs => fs.OpenRead(It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// Tests that Load with a single valid filepath returns records from that file.
        /// Input: One valid filepath with CSV content.
        /// Expected: Returns a list containing the records from the CSV file.
        /// </summary>
        [Test]
        public void Load_SingleValidFilepath_ReturnsRecordsFromFile()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            const string csvContent = "Id,Name,Link,ConformanceUnits\n1,TestNode,\" http://test.com \",\"Unit1,Unit2\"";
            mockFileSystem.Setup(fs => fs.OpenRead("file1.csv")).Returns(StreamFromString(csvContent));
            var reader = new NodeDocumentationReader(mockFileSystem.Object);

            // Act
            IList<NodeDocumentationRow> result = reader.Load("file1.csv");

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Id, Is.EqualTo(1u));
            Assert.That(result[0].Name, Is.EqualTo("TestNode"));
            Assert.That(result[0].Link, Is.EqualTo("http://test.com"));
            mockFileSystem.Verify(fs => fs.OpenRead("file1.csv"), Times.Once);
        }

        /// <summary>
        /// Tests that Load with multiple valid filepaths returns combined records from all files.
        /// Input: Multiple valid filepaths with CSV content.
        /// Expected: Returns a list containing records from all CSV files in order.
        /// </summary>
        [Test]
        public void Load_MultipleValidFilepaths_ReturnsCombinedRecords()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            const string csvContent1 = "Id,Name,Link,ConformanceUnits\n1,Node1,\" http://node1.com \",\"Unit1\"";
            const string csvContent2 = "Id,Name,Link,ConformanceUnits\n2,Node2,\" http://node2.com \",\"Unit2\"";
            const string csvContent3 = "Id,Name,Link,ConformanceUnits\n3,Node3,\" http://node3.com \",\"Unit3\"";
            mockFileSystem.Setup(fs => fs.OpenRead("file1.csv")).Returns(StreamFromString(csvContent1));
            mockFileSystem.Setup(fs => fs.OpenRead("file2.csv")).Returns(StreamFromString(csvContent2));
            mockFileSystem.Setup(fs => fs.OpenRead("file3.csv")).Returns(StreamFromString(csvContent3));
            var reader = new NodeDocumentationReader(mockFileSystem.Object);

            // Act
            IList<NodeDocumentationRow> result = reader.Load("file1.csv", "file2.csv", "file3.csv");

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0].Id, Is.EqualTo(1u));
            Assert.That(result[0].Name, Is.EqualTo("Node1"));
            Assert.That(result[1].Id, Is.EqualTo(2u));
            Assert.That(result[1].Name, Is.EqualTo("Node2"));
            Assert.That(result[2].Id, Is.EqualTo(3u));
            Assert.That(result[2].Name, Is.EqualTo("Node3"));
            mockFileSystem.Verify(fs => fs.OpenRead(It.IsAny<string>()), Times.Exactly(3));
        }

        /// <summary>
        /// Tests that Load with a file containing multiple records returns all records.
        /// Input: Single filepath with CSV containing multiple rows.
        /// Expected: Returns a list containing all records from the CSV file.
        /// </summary>
        [Test]
        public void Load_FileWithMultipleRecords_ReturnsAllRecords()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            const string csvContent = "Id,Name,Link,ConformanceUnits\n" +
                "10,Node10,\" http://node10.com \",\"UnitA\"\n" +
                "20,Node20,\" http://node20.com \",\"UnitB\"\n" +
                "30,Node30,\" http://node30.com \",\"UnitC\"";
            mockFileSystem.Setup(fs => fs.OpenRead("file.csv")).Returns(StreamFromString(csvContent));
            var reader = new NodeDocumentationReader(mockFileSystem.Object);

            // Act
            IList<NodeDocumentationRow> result = reader.Load("file.csv");

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0].Id, Is.EqualTo(10u));
            Assert.That(result[1].Id, Is.EqualTo(20u));
            Assert.That(result[2].Id, Is.EqualTo(30u));
        }

        /// <summary>
        /// Tests that Load throws exception when file system throws on CreateTextReader.
        /// Input: Filepath that causes CreateTextReader to throw FileNotFoundException.
        /// Expected: FileNotFoundException is propagated to caller.
        /// </summary>
        [Test]
        public void Load_FileNotFound_ThrowsFileNotFoundException()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.OpenRead("nonexistent.csv"))
                .Throws(new FileNotFoundException("File not found", "nonexistent.csv"));
            var reader = new NodeDocumentationReader(mockFileSystem.Object);

            // Act & Assert
            Assert.Throws<FileNotFoundException>(() => reader.Load("nonexistent.csv"));
        }

        /// <summary>
        /// Tests that Load throws exception when CreateTextReader is called with null filepath.
        /// Input: Null string in the filepaths array.
        /// Expected: ArgumentNullException or other exception from file system.
        /// </summary>
        [Test]
        public void Load_NullFilepath_ThrowsException()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.OpenRead(null))
                .Throws(new ArgumentNullException("filepath"));
            var reader = new NodeDocumentationReader(mockFileSystem.Object);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => reader.Load((string)null));
        }

        /// <summary>
        /// Tests that Load throws exception when CreateTextReader is called with empty string.
        /// Input: Empty string in the filepaths array.
        /// Expected: ArgumentException from file system.
        /// </summary>
        [Test]
        public void Load_EmptyStringFilepath_ThrowsException()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.OpenRead(string.Empty))
                .Throws(new ArgumentException("filepath cannot be empty", "filepath"));
            var reader = new NodeDocumentationReader(mockFileSystem.Object);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => reader.Load(string.Empty));
        }

        /// <summary>
        /// Tests that Load throws exception when CreateTextReader is called with whitespace-only string.
        /// Input: Whitespace-only string in the filepaths array.
        /// Expected: ArgumentException from file system.
        /// </summary>
        [Test]
        public void Load_WhitespaceFilepath_ThrowsException()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.OpenRead("   "))
                .Throws(new ArgumentException("filepath cannot be whitespace", "filepath"));
            var reader = new NodeDocumentationReader(mockFileSystem.Object);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => reader.Load("   "));
        }

        /// <summary>
        /// Tests that Load processes valid filepaths before throwing on invalid one.
        /// Input: Multiple filepaths where first is valid and second throws exception.
        /// Expected: Exception is thrown but valid records from first file are processed.
        /// </summary>
        [Test]
        public void Load_ValidThenInvalidFilepath_ThrowsButProcessesValidFirst()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            const string csvContent = "Id,Name,Link,ConformanceUnits\n1,Node1,\" http://node1.com \",\"Unit1\"";
            mockFileSystem.Setup(fs => fs.OpenRead("valid.csv")).Returns(StreamFromString(csvContent));
            mockFileSystem.Setup(fs => fs.OpenRead("invalid.csv"))
                .Throws(new FileNotFoundException("File not found", "invalid.csv"));
            var reader = new NodeDocumentationReader(mockFileSystem.Object);

            // Act & Assert
            Assert.Throws<FileNotFoundException>(() => reader.Load("valid.csv", "invalid.csv"));
            mockFileSystem.Verify(fs => fs.OpenRead("valid.csv"), Times.Once);
        }

        /// <summary>
        /// Tests that Load with very long filepath string is handled.
        /// Input: Very long filepath string.
        /// Expected: CreateTextReader is called with the long filepath.
        /// </summary>
        [Test]
        public void Load_VeryLongFilepath_CallsCreateTextReader()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            string longPath = new('a', 10000);
            const string csvContent = "Id,Name,Link,ConformanceUnits\n1,Node1,\" http://node1.com \",\"Unit1\"";
            mockFileSystem.Setup(fs => fs.OpenRead(longPath)).Returns(StreamFromString(csvContent));
            var reader = new NodeDocumentationReader(mockFileSystem.Object);

            // Act
            IList<NodeDocumentationRow> result = reader.Load(longPath);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(1));
            mockFileSystem.Verify(fs => fs.OpenRead(longPath), Times.Once);
        }

        /// <summary>
        /// Tests that Load with filepath containing special characters is handled.
        /// Input: Filepath with special characters like spaces, unicode, etc.
        /// Expected: CreateTextReader is called with the special filepath.
        /// </summary>
        [Test]
        public void Load_FilepathWithSpecialCharacters_CallsCreateTextReader()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            const string specialPath = "file with spaces & symbols @#$.csv";
            const string csvContent = "Id,Name,Link,ConformanceUnits\n1,Node1,\" http://node1.com \",\"Unit1\"";
            mockFileSystem.Setup(fs => fs.OpenRead(specialPath)).Returns(StreamFromString(csvContent));
            var reader = new NodeDocumentationReader(mockFileSystem.Object);

            // Act
            IList<NodeDocumentationRow> result = reader.Load(specialPath);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(1));
            mockFileSystem.Verify(fs => fs.OpenRead(specialPath), Times.Once);
        }

        /// <summary>
        /// Tests that Load trims whitespace from Link property.
        /// Input: CSV with Link values containing leading/trailing whitespace.
        /// Expected: Link property is trimmed in the returned records.
        /// </summary>
        [Test]
        public void Load_LinkWithWhitespace_TrimsLink()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            const string csvContent = "Id,Name,Link,ConformanceUnits\n1,Node1,\"  http://test.com  \",\"Unit1\"";
            mockFileSystem.Setup(fs => fs.OpenRead("file.csv")).Returns(StreamFromString(csvContent));
            var reader = new NodeDocumentationReader(mockFileSystem.Object);

            // Act
            IList<NodeDocumentationRow> result = reader.Load("file.csv");

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Link, Is.EqualTo("http://test.com"));
        }

        /// <summary>
        /// Tests that Load handles empty CSV file (header only).
        /// Input: CSV file with only header row and no data rows.
        /// Expected: Returns empty list.
        /// </summary>
        [Test]
        public void Load_EmptyCsvFile_ReturnsEmptyList()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            const string csvContent = "Id,Name,Link,ConformanceUnits";
            mockFileSystem.Setup(fs => fs.OpenRead("empty.csv")).Returns(StreamFromString(csvContent));
            var reader = new NodeDocumentationReader(mockFileSystem.Object);

            // Act
            IList<NodeDocumentationRow> result = reader.Load("empty.csv");

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Tests that Load handles uint boundary values in Id field.
        /// Input: CSV with Id values at uint boundaries (0, uint.MaxValue).
        /// Expected: Returns records with correct Id values.
        /// </summary>
        [Test]
        public void Load_UintBoundaryValues_ReturnsCorrectIds()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            const string csvContent = "Id,Name,Link,ConformanceUnits\n" +
                "0,Node0,\" http://node0.com \",\"Unit0\"\n" +
                "4294967295,NodeMax,\" http://nodemax.com \",\"UnitMax\"";
            mockFileSystem.Setup(fs => fs.OpenRead("file.csv")).Returns(StreamFromString(csvContent));
            var reader = new NodeDocumentationReader(mockFileSystem.Object);

            // Act
            IList<NodeDocumentationRow> result = reader.Load("file.csv");

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Id, Is.EqualTo(0u));
            Assert.That(result[1].Id, Is.EqualTo(uint.MaxValue));
        }

        /// <summary>
        /// Tests that the constructor successfully creates an instance when provided with a valid IFileSystem implementation.
        /// Input: A mocked IFileSystem instance.
        /// Expected: Constructor completes successfully without throwing an exception.
        /// </summary>
        [Test]
        public void Constructor_ValidFileSystem_CompletesSuccessfully()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();

            // Act
            var reader = new NodeDocumentationReader(mockFileSystem.Object);

            // Assert
            Assert.That(reader, Is.Not.Null);
        }

        /// <summary>
        /// Tests that the constructor successfully creates an instance when provided with null.
        /// The constructor should fall back to LocalFileSystem.Instance.
        /// Input: null IFileSystem.
        /// Expected: Constructor completes successfully without throwing an exception.
        /// </summary>
        [Test]
        public void Constructor_NullFileSystem_CompletesSuccessfully()
        {
            // Arrange
            IFileSystem nullFileSystem = null;

            // Act
            var reader = new NodeDocumentationReader(nullFileSystem);

            // Assert
            Assert.That(reader, Is.Not.Null);
        }

        /// <summary>
        /// Tests that the constructor can be called multiple times with different IFileSystem instances.
        /// Input: Multiple different mocked IFileSystem instances.
        /// Expected: Each constructor call completes successfully and creates a new instance.
        /// </summary>
        [Test]
        public void Constructor_MultipleCalls_EachCompletesSuccessfully()
        {
            // Arrange
            var mockFileSystem1 = new Mock<IFileSystem>();
            var mockFileSystem2 = new Mock<IFileSystem>();

            // Act
            var reader1 = new NodeDocumentationReader(mockFileSystem1.Object);
            var reader2 = new NodeDocumentationReader(mockFileSystem2.Object);
            var reader3 = new NodeDocumentationReader(null);

            // Assert
            Assert.That(reader1, Is.Not.Null);
            Assert.That(reader2, Is.Not.Null);
            Assert.That(reader3, Is.Not.Null);
            Assert.That(reader1, Is.Not.SameAs(reader2));
            Assert.That(reader2, Is.Not.SameAs(reader3));
        }

        private static MemoryStream StreamFromString(string str)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(str ?? string.Empty));
        }
    }
}
