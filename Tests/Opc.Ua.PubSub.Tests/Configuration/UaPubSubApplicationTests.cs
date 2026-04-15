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
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    [TestFixture(Description = "Tests for UaPubSubApplication class")]
    public class UaPubSubApplicationTests
    {
        private readonly string m_configurationFileName = Path.Combine(
            "Configuration",
            "PublisherConfiguration.xml");

        private PubSubConfigurationDataType m_pubSubConfiguration;

        [OneTimeSetUp]
        public void MyTestInitialize()
        {
            string configurationFile = Utils.GetAbsoluteFilePath(
                m_configurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_pubSubConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(
                configurationFile,
                telemetry);
        }

        [Test(Description = "Validate Create call with null path")]
        public void ValidateUaPubSubApplicationCreateNullFilePath()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Assert.Throws<ArgumentNullException>(
                () => UaPubSubApplication.Create((string)null, telemetry),
                "Calling Create with null parameter shall throw error");
        }

        [Test(Description = "Validate Create call with null PubSubConfigurationDataType")]
        public void ValidateUaPubSubApplicationCreateNullPubSubConfigurationDataType()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Assert.DoesNotThrow(
                () => UaPubSubApplication.Create((PubSubConfigurationDataType)null, telemetry),
                "Calling Create with null parameter shall not throw error");
        }

        [Test(Description = "Validate Create call")]
        public void ValidateUaPubSubApplicationCreate()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            // Arrange
            using UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(m_pubSubConfiguration, telemetry);

            // Assert
            Assert.That(
                !uaPubSubApplication.PubSubConnections.IsNull,
                Is.True,
                "uaPubSubApplication.PubSubConnections collection is null");
            Assert.That(
                uaPubSubApplication.PubSubConnections.Count,
                Is.EqualTo(3),
                "uaPubSubApplication.PubSubConnections count");
            var connection = uaPubSubApplication.PubSubConnections[0] as UaPubSubConnection;
            Assert.That(connection.Publishers, Is.Not.Null, "connection.Publishers is null");
            Assert.That(connection.Publishers, Has.Count.EqualTo(1), "connection.Publishers count is not 2");
            int index = 0;
            foreach (IUaPublisher publisher in connection.Publishers)
            {
                Assert.That(publisher, Is.Not.Null, CoreUtils.Format("connection.Publishers[{0}] is null", index));
                Assert.That(
                    publisher.PubSubConnection,
                    Is.EqualTo(connection),
                    CoreUtils.Format("connection.Publishers[{0}].PubSubConnection is not set correctly", index));
                Assert.That(
                    publisher.WriterGroupConfiguration.WriterGroupId,
                    Is.EqualTo(m_pubSubConfiguration.Connections[0].WriterGroups[index].WriterGroupId),
                    CoreUtils.Format("connection.Publishers[{0}].WriterGroupConfiguration is not set correctly", index));
                index++;
            }
        }
    }
}
