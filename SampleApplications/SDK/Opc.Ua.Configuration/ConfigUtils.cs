/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Xml;
using System.Threading.Tasks;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// Utility functions used by COM applications.
    /// </summary>
    public static class ConfigUtils
    {
        /// <summary>
        /// Finds the first child element with the specified name.
        /// </summary>
        private static XmlElement FindFirstElement(XmlElement parent, string localName, string namespaceUri)
        {
            if (parent == null)
            {
                return null;
            }

            for (XmlNode child = parent.FirstChild; child != null; child = child.NextSibling)
            {
                XmlElement element = child as XmlElement;

                if (element != null)
                {
                    if (element.LocalName == localName && element.NamespaceURI == namespaceUri)
                    {
                        return element;
                    }

                    break;
                }
            }

            return null;
        }

        /// <summary>
        /// Updates the configuration location for the specified 
        /// </summary>
        public static void UpdateConfigurationLocation(string executablePath, string configurationPath)
        {
            string configFilePath = Utils.Format("{0}.config", executablePath);

            // not all apps have an app.config file.
            if (!File.Exists(configFilePath))
            {
                return;
            }

            // load from file.
            XmlDocument document = new XmlDocument();
            document.Load(new FileStream(configFilePath, FileMode.Open));

            for (XmlNode child = document.DocumentElement.FirstChild; child != null; child = child.NextSibling)
            {
                // ignore non-element.
                XmlElement element = child as XmlElement;

                if (element == null)
                {
                    continue;
                }

                // look for the configuration location.
                XmlElement location = FindFirstElement(element, "ConfigurationLocation", Namespaces.OpcUaConfig);

                if (location == null)
                {
                    continue;
                }
                
                // find the file path.
                XmlElement filePath = FindFirstElement(location, "FilePath", Namespaces.OpcUaConfig);

                if (filePath == null)
                {
                    filePath = location.OwnerDocument.CreateElement("FilePath", Namespaces.OpcUaConfig);
                    location.InsertBefore(filePath, location.FirstChild);
                }
                
                filePath.InnerText = configurationPath;
                break;
            }
            
            // save configuration file.
            Stream ostrm = File.Open(configFilePath, FileMode.Create, FileAccess.Write);
			StreamWriter writer = new StreamWriter(ostrm, System.Text.Encoding.UTF8);
            
            try
            {            
                document.Save(writer);
            }
            finally
            {
                writer.Flush();
                writer.Dispose();
            }
        }
      
        /// <summary>
        /// The category identifier for UA servers that are registered as COM servers on a machine.
        /// </summary>
        public static readonly Guid CATID_PseudoComServers = new Guid("899A3076-F94E-4695-9DF8-0ED25B02BDBA");

        /// <summary>
        /// The CLSID for the UA COM DA server host process (note: will be eventually replaced the proxy server).
        /// </summary>
        public static readonly Guid CLSID_UaComDaProxyServer = new Guid("B25384BD-D0DD-4d4d-805C-6E9F309F27C1");

        /// <summary>
        /// The CLSID for the UA COM AE server host process (note: will be eventually replaced the proxy server).
        /// </summary>
        public static readonly Guid CLSID_UaComAeProxyServer = new Guid("4DF1784C-085A-403d-AF8A-B140639B10B3");

        /// <summary>
        /// The CLSID for the UA COM HDA server host process (note: will be eventually replaced the proxy server).
        /// </summary>
        public static readonly Guid CLSID_UaComHdaProxyServer = new Guid("2DA58B69-2D85-4de0-A934-7751322132E2");
        
        /// <summary>
        /// COM servers that support the DA 2.0 specification.
        /// </summary>
        public static readonly Guid CATID_OPCDAServer20  = new Guid("63D5F432-CFE4-11d1-B2C8-0060083BA1FB");

        /// <summary>
        /// COM servers that support the DA 3.0 specification.
        /// </summary>
        public static readonly Guid CATID_OPCDAServer30  = new Guid("CC603642-66D7-48f1-B69A-B625E73652D7");

        /// <summary>
        /// COM servers that support the AE 1.0 specification.
        /// </summary>
        public static readonly Guid CATID_OPCAEServer10  = new Guid("58E13251-AC87-11d1-84D5-00608CB8A7E9");

        /// <summary>
        /// COM servers that support the HDA 1.0 specification.
        /// </summary>
        public static readonly Guid CATID_OPCHDAServer10 = new Guid("7DE5B060-E089-11d2-A5E6-000086339399");
		
		private const uint CLSCTX_INPROC_SERVER	 = 0x1;
		private const uint CLSCTX_INPROC_HANDLER = 0x2;
		private const uint CLSCTX_LOCAL_SERVER	 = 0x4;
		private const uint CLSCTX_REMOTE_SERVER	 = 0x10;

		private static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
		
        private static readonly Guid CLSID_StdComponentCategoriesMgr = new Guid("0002E005-0000-0000-C000-000000000046");

        private const string CATID_OPCDAServer20_Description  = "OPC Data Access Servers Version 2.0";
        private const string CATID_OPCDAServer30_Description  = "OPC Data Access Servers Version 3.0";
        private const string CATID_OPCAEServer10_Description  = "OPC Alarm & Event Server Version 1.0";
        private const string CATID_OPCHDAServer10_Description = "OPC History Data Access Servers Version 1.0";

        private const Int32 CRYPT_OID_INFO_OID_KEY = 1;
        private const Int32  CRYPT_INSTALL_OID_INFO_BEFORE_FLAG  = 1;
    }
}
