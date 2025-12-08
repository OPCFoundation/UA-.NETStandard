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
using System.Configuration;
using System.Xml;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Configuration.Tests
{
    /// <summary>
    /// Tests for the ApplicationConfigurationSection class.
    /// </summary>
    [TestFixture]
    [Category("ApplicationConfigurationSection")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ApplicationConfigurationSectionTests
    {
        /// <summary>
        /// Test that ApplicationConfigurationSection implements IConfigurationSectionHandler.
        /// </summary>
        [Test]
        public void ApplicationConfigurationSectionImplementsInterface()
        {
            var section = new ApplicationConfigurationSection();
            Assert.IsNotNull(section);
            Assert.IsInstanceOf<IConfigurationSectionHandler>(section);
        }

        /// <summary>
        /// Test that the Create method works correctly with valid XML.
        /// </summary>
        [Test]
        public void CreateMethodReturnsConfigurationLocation()
        {
            var section = new ApplicationConfigurationSection();
            var xmlDoc = new XmlDocument();
            
            string xmlContent = @"
                <section>
                    <ConfigurationLocation xmlns='http://opcfoundation.org/UA/SDK/Configuration.xsd'>
                        <FilePath>test.xml</FilePath>
                    </ConfigurationLocation>
                </section>";
            
            using (var reader = XmlReader.Create(new System.IO.StringReader(xmlContent), Utils.DefaultXmlReaderSettings()))
            {
                xmlDoc.Load(reader);
            }

            var result = section.Create(null, null, xmlDoc.DocumentElement);
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<ConfigurationLocation>(result);
            
            var configLocation = result as ConfigurationLocation;
            Assert.AreEqual("test.xml", configLocation.FilePath);
        }

        /// <summary>
        /// Test that the Create method throws ArgumentNullException when section is null.
        /// </summary>
        [Test]
        public void CreateMethodThrowsOnNullSection()
        {
            var section = new ApplicationConfigurationSection();
            Assert.Throws<ArgumentNullException>(() => section.Create(null, null, null));
        }

        /// <summary>
        /// Test that IConfigurationSectionHandler.Create can be called through the interface.
        /// </summary>
        [Test]
        public void InterfaceCreateMethodWorks()
        {
            ApplicationConfigurationSection handler = new ApplicationConfigurationSection();
            var xmlDoc = new XmlDocument();
            
            string xmlContent = @"
                <section>
                    <ConfigurationLocation xmlns='http://opcfoundation.org/UA/SDK/Configuration.xsd'>
                        <FilePath>interface-test.xml</FilePath>
                    </ConfigurationLocation>
                </section>";
            
            using (var reader = XmlReader.Create(new System.IO.StringReader(xmlContent), Utils.DefaultXmlReaderSettings()))
            {
                xmlDoc.Load(reader);
            }

            var result = handler.Create(null, null, xmlDoc.DocumentElement);
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<ConfigurationLocation>(result);
            
            var configLocation = result as ConfigurationLocation;
            Assert.AreEqual("interface-test.xml", configLocation.FilePath);
        }
    }
}
