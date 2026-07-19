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

using System.IO;
using MemoryBuffer;
using NUnit.Framework;
using Opc.Ua.Tests;
using TestData;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// XML round-trip tests for source-generated IEncodeable config types:
    /// <see cref="MemoryBufferConfiguration"/> and
    /// <see cref="TestDataNodeManagerConfiguration"/>.
    /// </summary>
    [TestFixture]
    [Category("Configuration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ConfigurationRoundTripTests
    {
        [Test]
        public void MemoryBufferConfigurationXmlRoundTrip()
        {
            var original = new MemoryBufferConfiguration
            {
                Buffers =
                [
                    new MemoryBufferInstance
                    {
                        Name = "UInt32",
                        TagCount = 100,
                        DataType = "UInt32"
                    },
                    new MemoryBufferInstance
                    {
                        Name = "Double",
                        TagCount = 200,
                        DataType = "Double"
                    }
                ]
            };

            MemoryBufferConfiguration decoded = RoundTripExtension(
                original,
                out string xml);

            Assert.That(xml, Does.Contain("http://samples.org/UA/MemoryBuffer"));
            Assert.That(xml, Does.Not.Contain("urn:memorybuffer"));
            VerifyMemoryBufferConfiguration(decoded, original);
        }

        [Test]
        public void MemoryBufferConfigurationParseExtensionFromFixture()
        {
            MemoryBufferConfiguration first =
                ParseExtensionFromFixture<MemoryBufferConfiguration>(
                    "test-memorybuffer-config.xml");

            Assert.That(first.Buffers, Has.Count.GreaterThan(0));
            Assert.That(first.Buffers.Count, Is.EqualTo(2));
            Assert.That(first.Buffers[0].Name, Is.EqualTo("UInt32"));
            Assert.That(first.Buffers[0].TagCount, Is.EqualTo(100));
            Assert.That(first.Buffers[0].DataType, Is.EqualTo("UInt32"));
            Assert.That(first.Buffers[1].Name, Is.EqualTo("Double"));
            Assert.That(first.Buffers[1].TagCount, Is.EqualTo(200));
            Assert.That(first.Buffers[1].DataType, Is.EqualTo("Double"));

            MemoryBufferConfiguration second = RoundTripExtension(
                first,
                out string xml);

            Assert.That(xml, Does.Not.Contain("urn:memorybuffer"));
            VerifyMemoryBufferConfiguration(second, first);
        }

        [Test]
        public void TestDataNodeManagerConfigurationXmlRoundTrip()
        {
            var original = new TestDataNodeManagerConfiguration
            {
                SaveFilePath = "C:\\TestData\\state.bin",
                MaxQueueSize = 250,
                NextUnusedId = 42
            };

            TestDataNodeManagerConfiguration decoded = RoundTripExtension(
                original,
                out string xml);

            Assert.That(xml, Does.Contain("http://test.org/UA/Data/"));
            Assert.That(xml, Does.Not.Contain("urn:testdata"));
            VerifyTestDataConfig(decoded, original);
        }

        [Test]
        public void TestDataNodeManagerConfigurationParseExtensionFromFixture()
        {
            TestDataNodeManagerConfiguration first =
                ParseExtensionFromFixture<TestDataNodeManagerConfiguration>(
                    "test-testdata-config.xml");

            Assert.That(first.SaveFilePath,
                Is.EqualTo("C:\\TestData\\state.bin"));
            Assert.That(first.MaxQueueSize, Is.EqualTo(250u));
            Assert.That(first.NextUnusedId, Is.EqualTo(42u));

            TestDataNodeManagerConfiguration second = RoundTripExtension(
                first,
                out string xml);

            Assert.That(xml, Does.Not.Contain("urn:testdata"));
            VerifyTestDataConfig(second, first);
        }

        private static T ParseExtensionFromFixture<T>(string fileName)
            where T : class, IEncodeable, new()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var configuration = new ApplicationConfiguration(telemetry)
            {
                Extensions =
                [
                    Opc.Ua.XmlElement.From(File.ReadAllText(Path.Combine(
                        TestContext.CurrentContext.WorkDirectory,
                        fileName)))
                ]
            };

            T result = configuration.ParseExtension<T>();
            Assert.That(result, Is.Not.Null);
            return result;
        }

        private static T RoundTripExtension<T>(T value, out string xml)
            where T : class, IEncodeable, new()
        {
            var configuration = new ApplicationConfiguration(
                NUnitTelemetryContext.Create());
            configuration.UpdateExtension<T>(null, value);

            Assert.That(configuration.Extensions, Has.Count.EqualTo(1));
            xml = configuration.Extensions[0].OuterXml;

            T result = configuration.ParseExtension<T>();
            Assert.That(result, Is.Not.Null);
            return result;
        }

        private static void VerifyMemoryBufferConfiguration(
            MemoryBufferConfiguration actual,
            MemoryBufferConfiguration expected)
        {
            Assert.That(actual.Buffers, Has.Count.GreaterThan(0));
            Assert.That(actual.Buffers.Count,
                Is.EqualTo(expected.Buffers.Count));

            for (int i = 0; i < expected.Buffers.Count; i++)
            {
                Assert.That(actual.Buffers[i].Name,
                    Is.EqualTo(expected.Buffers[i].Name));
                Assert.That(actual.Buffers[i].TagCount,
                    Is.EqualTo(expected.Buffers[i].TagCount));
                Assert.That(actual.Buffers[i].DataType,
                    Is.EqualTo(expected.Buffers[i].DataType));
            }
        }

        private static void VerifyTestDataConfig(
            TestDataNodeManagerConfiguration actual,
            TestDataNodeManagerConfiguration expected)
        {
            Assert.That(actual.SaveFilePath,
                Is.EqualTo(expected.SaveFilePath));
            Assert.That(actual.MaxQueueSize,
                Is.EqualTo(expected.MaxQueueSize));
            Assert.That(actual.NextUnusedId,
                Is.EqualTo(expected.NextUnusedId));
        }
    }
}
