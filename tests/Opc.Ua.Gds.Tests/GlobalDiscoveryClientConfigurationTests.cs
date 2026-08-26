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
using System.Text;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Gds.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class GlobalDiscoveryClientConfigurationTests
    {
        [Test]
        public void ConstructorCreatesInstance()
        {
            var config = new GlobalDiscoveryClientConfiguration();
            Assert.That(config, Is.Not.Null);
        }

        [Test]
        public void GlobalDiscoveryServerUrlDefaultsToNull()
        {
            var config = new GlobalDiscoveryClientConfiguration();
            Assert.That(config.GlobalDiscoveryServerUrl, Is.Null);
        }

        [Test]
        public void GlobalDiscoveryServerUrlRoundTrip()
        {
            var config = new GlobalDiscoveryClientConfiguration
            {
                GlobalDiscoveryServerUrl = "opc.tcp://gds.example.com:4840"
            };
            Assert.That(config.GlobalDiscoveryServerUrl, Is.EqualTo("opc.tcp://gds.example.com:4840"));
        }

        [Test]
        public void ExternalEditorDefaultsToNull()
        {
            var config = new GlobalDiscoveryClientConfiguration();
            Assert.That(config.ExternalEditor, Is.Null);
        }

        [Test]
        public void ExternalEditorRoundTrip()
        {
            var config = new GlobalDiscoveryClientConfiguration
            {
                ExternalEditor = "notepad.exe"
            };
            Assert.That(config.ExternalEditor, Is.EqualTo("notepad.exe"));
        }

        [Test]
        public void GlobalDiscoveryClientConfigurationXmlRoundTrip()
        {
            string filePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "test-gds-client-config.xml");

            // Step 1: Decode from XML fixture
            GlobalDiscoveryClientConfiguration original;
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                original = DecodeConfiguration(stream);
            }

            // Step 2: Verify decoded fields
            Assert.That(original, Is.Not.Null);
            Assert.That(original.GlobalDiscoveryServerUrl, Is.EqualTo("opc.tcp://gds.example.com:4840"));
            Assert.That(original.ExternalEditor, Is.EqualTo("notepad.exe"));

            // Step 3: Encode back to XML
            string xml = EncodeConfiguration(original);
            Assert.That(xml, Is.Not.Null.And.Not.Empty);

            // Step 4: Re-parse encoded XML
            GlobalDiscoveryClientConfiguration roundTripped;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
            {
                roundTripped = DecodeConfiguration(stream);
            }

            // Step 5: Verify re-parsed matches original
            Assert.That(roundTripped.GlobalDiscoveryServerUrl, Is.EqualTo(original.GlobalDiscoveryServerUrl));
            Assert.That(roundTripped.ExternalEditor, Is.EqualTo(original.ExternalEditor));
        }

        private static IServiceMessageContext CreateMessageContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using IDisposable scope = AmbientMessageContext.SetScopedContext(telemetry);
            return AmbientMessageContext.CurrentContext
                ?? ServiceMessageContext.CreateEmpty(telemetry);
        }

        private static GlobalDiscoveryClientConfiguration DecodeConfiguration(Stream stream)
        {
            IServiceMessageContext ctx = CreateMessageContext();
            using var parser = new XmlParser(
                typeof(GlobalDiscoveryClientConfiguration), stream, ctx);
            var config = new GlobalDiscoveryClientConfiguration();
            config.Decode(parser);
            return config;
        }

        private static string EncodeConfiguration(GlobalDiscoveryClientConfiguration configuration)
        {
            IServiceMessageContext ctx = CreateMessageContext();
            using var stream = new MemoryStream();
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            settings.Encoding = new UTF8Encoding(false);
            using (var writer = XmlWriter.Create(stream, settings))
            {
                using var encoder = new XmlEncoder(
                    typeof(GlobalDiscoveryClientConfiguration), writer, ctx);
                configuration.Encode(encoder);
                encoder.Close();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
