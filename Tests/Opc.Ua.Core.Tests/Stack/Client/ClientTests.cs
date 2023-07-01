/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    /// <summary>
    /// Tests for the UANodeSet helper.
    /// </summary>
    [TestFixture, Category("Client")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class ClientTests
    {
        #region Test Setup
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
        }

        [SetUp]
        protected void SetUp()
        {

        }

        [TearDown]
        protected void TearDown()
        {
        }
        #endregion

        #region Test Methods
        /// <summary>
        /// Ensure that use of OriginalString preserves a scope id of a IPv6 address.
        /// </summary>
        [Test]
        [TestCase("opc.tcp://another.server.com:4840/CustomEndpoint")]
        [TestCase("opc.tcp://10.11.222.12:62541/ReferenceServer")]
        [TestCase("opc.tcp://[2003:d9:1f40:bc00:a115:d9c7:6134:f347]:4840/AnEndpoint")]
        [TestCase("opc.tcp://[fe80::280:deff:fa02:c63e%eth0]:4840/")]
        [TestCase("opc.tcp://[fe80::de39:6fff:feae:c78%12]:4840/Endpoint1")]
        public void DiscoveryEndPointUrls(string urlString)
        {
            Uri uri = new Uri(urlString);
            Assert.True(uri.IsWellFormedOriginalString());

            UriBuilder uriBuilder = new UriBuilder {
                Scheme = uri.Scheme,
                Host = uri.DnsSafeHost,
                Port = uri.Port,
                Path = uri.AbsolutePath
            };

            Assert.AreEqual(uri.OriginalString, uriBuilder.Uri.OriginalString);
        }
        #endregion
    }
}
