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
using System.Runtime.Serialization;
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
        /// <param name="pubSubConfiguration">The configuration object that shall be saved in the file.</param>
        /// <param name="filePath">The file path from where the configuration shall be saved.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public static void SaveConfiguration(
            PubSubConfigurationDataType pubSubConfiguration,
            string filePath,
            ITelemetryContext telemetry)
        {
            Stream ostrm = File.Open(filePath, FileMode.Create, FileAccess.ReadWrite);

            using IDisposable scope = AmbientMessageContext.SetScopedContext(telemetry);
            DataContractSerializer serializer =
                CoreUtils.CreateDataContractSerializer<PubSubConfigurationDataType>();
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            settings.CloseOutput = true;
            using var writer = XmlWriter.Create(ostrm, settings);
            serializer.WriteObject(writer, pubSubConfiguration);
        }

        /// <summary>
        /// Load a <see cref="PubSubConfigurationDataType"/> instance from and XML File
        /// </summary>
        /// <param name="filePath">The file path from where the configuration shall be loaded.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <exception cref="ServiceResultException"></exception>
        public static PubSubConfigurationDataType LoadConfiguration(
            string filePath,
            ITelemetryContext telemetry)
        {
            try
            {
                using IDisposable scope = AmbientMessageContext.SetScopedContext(telemetry);
                DataContractSerializer serializer =
                    CoreUtils.CreateDataContractSerializer<PubSubConfigurationDataType>();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                using var reader = XmlReader.Create(stream, Utils.DefaultXmlReaderSettings());
                return (PubSubConfigurationDataType)serializer.ReadObject(reader);
            }
            catch (Exception e)
            {
                throw ServiceResultException.ConfigurationError(
                    e,
                    "Configuration file could not be loaded: {0}\nError: {1}",
                    filePath,
                    e.Message);
            }
        }
    }
}
