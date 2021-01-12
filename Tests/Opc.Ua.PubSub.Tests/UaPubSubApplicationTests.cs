/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;

namespace Opc.Ua.PubSub.Tests
{
    [TestFixture(Description = "Tests for UaPubSubApplication class")]
    public class UaPubSubApplicationTests
    {
        private const string ConfigurationFileName = "PublisherConfiguration.xml";
        private PubSubConfigurationDataType m_pubSubConfiguration;

        [OneTimeSetUp()]
        public void MyTestInitialize()
        {
            m_pubSubConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(ConfigurationFileName);
        }

        [Test(Description = "Validate Create call with null path")]
        public void ValidateUaPubSubApplicationCreateNullFilePath()
        {
            Assert.Throws<ArgumentException>(() => UaPubSubApplication.Create((string)null), "Calling Create with null parameter shall throw error");
        }

        [Test(Description = "Validate Create call with null PubSubConfigurationDataType")]
        public void ValidateUaPubSubApplicationCreateNullPubSubConfigurationDataType()
        {
            Assert.DoesNotThrow(() => UaPubSubApplication.Create((PubSubConfigurationDataType)null), "Calling Create with null parameter shall not throw error");
        }

        [Test(Description = "Validate Create call")]
        public void ValidateUaPubSubApplicationCreate()
        {
            // Arrange
            UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(m_pubSubConfiguration);

            // Assert
            Assert.IsTrue(uaPubSubApplication.PubSubConnections != null, "uaPubSubApplication.PubSubConnections collection is null");
            Assert.AreEqual(2, uaPubSubApplication.PubSubConnections.Count, "uaPubSubApplication.PubSubConnections count");
            UaPubSubConnection connection = uaPubSubApplication.PubSubConnections[0] as UaPubSubConnection;
            Assert.IsTrue(connection.Publishers != null, "connection.Publishers is null");
            Assert.IsTrue(connection.Publishers.Count == 1, "connection.Publishers count is not 2");
            int index = 0;
            foreach(IUaPublisher publisher in connection.Publishers)
            {
                Assert.IsTrue(publisher!= null, "connection.Publishers[{0}] is null", index);
                Assert.IsTrue(publisher.PubSubConnection == connection, "connection.Publishers[{0}].PubSubConnection is not set correctly", index);
                Assert.IsTrue(publisher.WriterGroupConfiguration.WriterGroupId == m_pubSubConfiguration.Connections[0].WriterGroups[index].WriterGroupId, "connection.Publishers[{0}].WriterGroupConfiguration is not set correctly", index);
                index++;
            }
        }

    }
}
