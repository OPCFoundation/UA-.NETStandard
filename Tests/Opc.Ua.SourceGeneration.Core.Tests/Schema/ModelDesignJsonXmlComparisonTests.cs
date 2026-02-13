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
#nullable enable

using System.IO;
using System.Text.Json;
using System.Xml;
using System.Xml.Serialization;
using NUnit.Framework;

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// Integration tests comparing JSON and XML deserialization of OPC UA model files.
    /// These tests verify that both deserialization paths produce equivalent ModelDesign objects.
    /// </summary>
    [TestFixture]
    public class ModelDesignJsonXmlComparisonTests
    {
        /// <summary>
        /// Tests that deserializing DemoModel.json and converting via ToModelDesign() produces
        /// equivalent results to deserializing DemoModel.xml.
        /// </summary>
        [Test]
        [Explicit]
        public void DeserializeJsonAndXmlComparison_DemoModel_ProducesEquivalentResults()
        {
            // Arrange
            string jsonFile = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "DemoModel.json");
            string xmlFile = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "DemoModel.xml");

            Assert.That(File.Exists(jsonFile), Is.True, $"DemoModel.json not found at {jsonFile}");
            Assert.That(File.Exists(xmlFile), Is.True, $"DemoModel.xml not found at {xmlFile}");

            // Act
            var jsonModelDesign = DeserializeJsonToModelDesignJson(jsonFile).ToModelDesign();
            ModelDesign xmlModelDesign = DeserializeXmlToModelDesign(xmlFile);

            // Assert
            Assert.That(jsonModelDesign, Is.Not.Null, "JSON deserialization produced null ModelDesign");
            Assert.That(xmlModelDesign, Is.Not.Null, "XML deserialization produced null ModelDesign");

            // Compare key properties
            Assert.That(jsonModelDesign.TargetNamespace, Is.EqualTo(xmlModelDesign.TargetNamespace),
                "TargetNamespace mismatch between JSON and XML deserialization");

            Assert.That(jsonModelDesign.TargetVersion, Is.EqualTo(xmlModelDesign.TargetVersion),
                "TargetVersion mismatch between JSON and XML deserialization");

            Assert.That(jsonModelDesign.TargetXmlNamespace, Is.EqualTo(xmlModelDesign.TargetXmlNamespace),
                "TargetXmlNamespace mismatch between JSON and XML deserialization");

            Assert.That(jsonModelDesign.DefaultLocale, Is.EqualTo(xmlModelDesign.DefaultLocale),
                "DefaultLocale mismatch between JSON and XML deserialization");

            // Compare collection counts
            int jsonNamespacesCount = jsonModelDesign.Namespaces?.Length ?? 0;
            int xmlNamespacesCount = xmlModelDesign.Namespaces?.Length ?? 0;
            Assert.That(jsonNamespacesCount, Is.EqualTo(xmlNamespacesCount),
                "Namespaces array length mismatch between JSON and XML deserialization");

            int jsonItemsCount = jsonModelDesign.Items?.Length ?? 0;
            int xmlItemsCount = xmlModelDesign.Items?.Length ?? 0;
            Assert.That(jsonItemsCount, Is.EqualTo(xmlItemsCount),
                "Items array length mismatch between JSON and XML deserialization");

            // Compare namespace values if populated
            if (jsonNamespacesCount > 0)
            {
                for (int i = 0; i < jsonNamespacesCount; i++)
                {
                    Namespace jsonNs = jsonModelDesign.Namespaces![i];
                    Namespace xmlNs = xmlModelDesign.Namespaces![i];

                    Assert.That(jsonNs.Value, Is.EqualTo(xmlNs.Value),
                        $"Namespace[{i}].Value mismatch between JSON and XML deserialization");

                    Assert.That(jsonNs.Prefix, Is.EqualTo(xmlNs.Prefix),
                        $"Namespace[{i}].Prefix mismatch between JSON and XML deserialization");
                }
            }
        }

        /// <summary>
        /// Tests that deserializing TestDataDesign.json and converting via ToModelDesign() produces
        /// equivalent results to deserializing TestDataDesign.xml.
        /// </summary>
        [Test]
        [Explicit]
        public void DeserializeJsonAndXmlComparison_TestDataDesign_ProducesEquivalentResults()
        {
            // Arrange
            string jsonFile = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "TestDataDesign.json");
            string xmlFile = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "TestDataDesign.xml");

            Assert.That(File.Exists(jsonFile), Is.True, $"TestDataDesign.json not found at {jsonFile}");
            Assert.That(File.Exists(xmlFile), Is.True, $"TestDataDesign.xml not found at {xmlFile}");

            // Act
            var jsonModelDesign = DeserializeJsonToModelDesignJson(jsonFile).ToModelDesign();
            ModelDesign xmlModelDesign = DeserializeXmlToModelDesign(xmlFile);

            // Assert
            Assert.That(jsonModelDesign, Is.Not.Null, "JSON deserialization produced null ModelDesign");
            Assert.That(xmlModelDesign, Is.Not.Null, "XML deserialization produced null ModelDesign");

            // Compare key properties
            Assert.That(jsonModelDesign.TargetNamespace, Is.EqualTo(xmlModelDesign.TargetNamespace),
                "TargetNamespace mismatch between JSON and XML deserialization");

            Assert.That(jsonModelDesign.TargetVersion, Is.EqualTo(xmlModelDesign.TargetVersion),
                "TargetVersion mismatch between JSON and XML deserialization");

            Assert.That(jsonModelDesign.TargetXmlNamespace, Is.EqualTo(xmlModelDesign.TargetXmlNamespace),
                "TargetXmlNamespace mismatch between JSON and XML deserialization");

            Assert.That(jsonModelDesign.DefaultLocale, Is.EqualTo(xmlModelDesign.DefaultLocale),
                "DefaultLocale mismatch between JSON and XML deserialization");

            // Compare collection counts
            int jsonNamespacesCount = jsonModelDesign.Namespaces?.Length ?? 0;
            int xmlNamespacesCount = xmlModelDesign.Namespaces?.Length ?? 0;
            Assert.That(jsonNamespacesCount, Is.EqualTo(xmlNamespacesCount),
                "Namespaces array length mismatch between JSON and XML deserialization");

            int jsonItemsCount = jsonModelDesign.Items?.Length ?? 0;
            int xmlItemsCount = xmlModelDesign.Items?.Length ?? 0;
            Assert.That(jsonItemsCount, Is.EqualTo(xmlItemsCount),
                "Items array length mismatch between JSON and XML deserialization");

            // Compare namespace values if populated
            if (jsonNamespacesCount > 0)
            {
                for (int i = 0; i < jsonNamespacesCount; i++)
                {
                    Namespace jsonNs = jsonModelDesign.Namespaces![i];
                    Namespace xmlNs = xmlModelDesign.Namespaces![i];

                    Assert.That(jsonNs.Value, Is.EqualTo(xmlNs.Value),
                        $"Namespace[{i}].Value mismatch between JSON and XML deserialization");

                    Assert.That(jsonNs.Prefix, Is.EqualTo(xmlNs.Prefix),
                        $"Namespace[{i}].Prefix mismatch between JSON and XML deserialization");
                }
            }
        }

        /// <summary>
        /// Deserializes a JSON model file to a ModelDesignJson record.
        /// </summary>
        /// <param name="jsonFilePath">Full path to the JSON file.</param>
        /// <returns>ModelDesignJson record or null if deserialization fails.</returns>
        private static ModelDesignJson DeserializeJsonToModelDesignJson(string jsonFilePath)
        {
            string json = File.ReadAllText(jsonFilePath);
            return JsonSerializer.Deserialize<ModelDesignJson>(json, kOptions)!;
        }

        /// <summary>
        /// Deserializes an XML model file to a ModelDesign object.
        /// </summary>
        /// <param name="xmlFilePath">Full path to the XML file.</param>
        /// <returns>ModelDesign object or null if deserialization fails.</returns>
        private static ModelDesign DeserializeXmlToModelDesign(string xmlFilePath)
        {
            using var reader = XmlReader.Create(xmlFilePath, CoreUtils.DefaultXmlReaderSettings());
            var serializer = new XmlSerializer(typeof(ModelDesign));
            var modelDesign = serializer.Deserialize(reader) as ModelDesign;
            return modelDesign!;
        }

        private static readonly JsonSerializerOptions kOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }
}
