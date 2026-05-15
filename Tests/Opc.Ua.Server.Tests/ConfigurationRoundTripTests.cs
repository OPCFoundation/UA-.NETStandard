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
using System.Text;
using System.Xml;
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

            IServiceMessageContext ctx = CreateMessageContext();

            string xml = EncodeMemoryBufferConfiguration(original, ctx);
            MemoryBufferConfiguration decoded =
                DecodeMemoryBufferConfiguration(xml, ctx);

            VerifyMemoryBufferConfiguration(decoded, original);
        }

        [Test]
        public void MemoryBufferConfigurationXmlRoundTripFromFixture()
        {
            IServiceMessageContext ctx = CreateMessageContext();

            string filePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "test-memorybuffer-config.xml");

            MemoryBufferConfiguration first;
            using (var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read))
            {
                first = DecodeMemoryBufferConfigurationFromStream(
                    stream, ctx);
            }

            Assert.That(first.Buffers, Has.Count.GreaterThan(0));
            Assert.That(first.Buffers.Count, Is.EqualTo(2));
            Assert.That(first.Buffers[0].Name, Is.EqualTo("UInt32"));
            Assert.That(first.Buffers[0].TagCount, Is.EqualTo(100));
            Assert.That(first.Buffers[0].DataType, Is.EqualTo("UInt32"));
            Assert.That(first.Buffers[1].Name, Is.EqualTo("Double"));
            Assert.That(first.Buffers[1].TagCount, Is.EqualTo(200));
            Assert.That(first.Buffers[1].DataType, Is.EqualTo("Double"));

            string xml = EncodeMemoryBufferConfiguration(first, ctx);
            MemoryBufferConfiguration second =
                DecodeMemoryBufferConfiguration(xml, ctx);

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

            IServiceMessageContext ctx = CreateMessageContext();

            string xml = EncodeTestDataConfig(original, ctx);
            TestDataNodeManagerConfiguration decoded =
                DecodeTestDataConfig(xml, ctx);

            VerifyTestDataConfig(decoded, original);
        }

        [Test]
        public void TestDataNodeManagerConfigurationXmlRoundTripFromFixture()
        {
            IServiceMessageContext ctx = CreateMessageContext();

            string filePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "test-testdata-config.xml");

            TestDataNodeManagerConfiguration first;
            using (var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read))
            {
                first = DecodeTestDataConfigFromStream(stream, ctx);
            }

            Assert.That(first.SaveFilePath,
                Is.EqualTo("C:\\TestData\\state.bin"));
            Assert.That(first.MaxQueueSize, Is.EqualTo(250u));
            Assert.That(first.NextUnusedId, Is.EqualTo(42u));

            string xml = EncodeTestDataConfig(first, ctx);
            TestDataNodeManagerConfiguration second =
                DecodeTestDataConfig(xml, ctx);

            VerifyTestDataConfig(second, first);
        }

        private static ServiceMessageContext CreateMessageContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return ServiceMessageContext.CreateEmpty(telemetry);
        }

        private static string EncodeMemoryBufferConfiguration(
            MemoryBufferConfiguration config,
            IServiceMessageContext ctx)
        {
            using var stream = new MemoryStream();
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            settings.Encoding = new UTF8Encoding(false);
            using (var writer = XmlWriter.Create(stream, settings))
            {
                using var encoder = new XmlEncoder(
                    typeof(MemoryBufferConfiguration), writer, ctx);
                config.Encode(encoder);
                encoder.Close();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static MemoryBufferConfiguration
            DecodeMemoryBufferConfiguration(
                string xml,
                IServiceMessageContext ctx)
        {
            using var stream = new MemoryStream(
                Encoding.UTF8.GetBytes(xml));
            return DecodeMemoryBufferConfigurationFromStream(stream, ctx);
        }

        private static MemoryBufferConfiguration
            DecodeMemoryBufferConfigurationFromStream(
                Stream stream,
                IServiceMessageContext ctx)
        {
            using var parser = new XmlParser(
                typeof(MemoryBufferConfiguration), stream, ctx);
            var result = new MemoryBufferConfiguration();
            result.Decode(parser);
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

        private static string EncodeTestDataConfig(
            TestDataNodeManagerConfiguration config,
            IServiceMessageContext ctx)
        {
            using var stream = new MemoryStream();
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            settings.Encoding = new UTF8Encoding(false);
            using (var writer = XmlWriter.Create(stream, settings))
            {
                using var encoder = new XmlEncoder(
                    typeof(TestDataNodeManagerConfiguration), writer, ctx);
                config.Encode(encoder);
                encoder.Close();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static TestDataNodeManagerConfiguration
            DecodeTestDataConfig(
                string xml,
                IServiceMessageContext ctx)
        {
            using var stream = new MemoryStream(
                Encoding.UTF8.GetBytes(xml));
            return DecodeTestDataConfigFromStream(stream, ctx);
        }

        private static TestDataNodeManagerConfiguration
            DecodeTestDataConfigFromStream(
                Stream stream,
                IServiceMessageContext ctx)
        {
            using var parser = new XmlParser(
                typeof(TestDataNodeManagerConfiguration), stream, ctx);
            var result = new TestDataNodeManagerConfiguration();
            result.Decode(parser);
            return result;
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
