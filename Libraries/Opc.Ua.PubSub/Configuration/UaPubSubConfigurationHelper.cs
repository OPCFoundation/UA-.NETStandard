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
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Helper class for load/save configuration
    /// </summary>
    public static class UaPubSubConfigurationHelper
    {
        /// <summary>
        /// Save a <see cref="PubSubConfigurationDataType"/> instance as XML
        /// </summary>
        /// <param name="pubSubConfiguration"></param>
        /// <param name="filePath"></param>
        public static void SaveConfiguration(PubSubConfigurationDataType pubSubConfiguration, string filePath)
        {

            Stream ostrm = File.Open(filePath, FileMode.Create, FileAccess.ReadWrite);

            XmlWriterSettings settings = new XmlWriterSettings();

            settings.Encoding = Encoding.UTF8;
            settings.Indent = true;
            settings.CloseOutput = true;

            using (XmlWriter writer = XmlDictionaryWriter.Create(ostrm, settings))
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(PubSubConfigurationDataType));
                serializer.WriteObject(writer, pubSubConfiguration);
            }
        }

        /// <summary>
        /// Load a <see cref="PubSubConfigurationDataType"/> instance from and XML File
        /// </summary>
        /// <param name="filePath"></param>
        public static PubSubConfigurationDataType LoadConfiguration(string filePath)
        {
            try
            {
                using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    DataContractSerializer serializer = new DataContractSerializer(typeof(PubSubConfigurationDataType));
                    return (PubSubConfigurationDataType)serializer.ReadObject(stream);
                }
            }
            catch (Exception e)
            {
                StringBuilder buffer = new StringBuilder();
                buffer.AppendFormat("Configuration file could not be loaded: {0}\r\n", filePath);
                buffer.AppendFormat("Error: {0}", e.Message);

                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    e,
                    buffer.ToString());
            }
        }
    }
}
