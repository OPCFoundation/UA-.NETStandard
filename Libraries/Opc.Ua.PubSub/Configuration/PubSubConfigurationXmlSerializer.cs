/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Xml;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Shared encode / decode primitives for the on-disk XML
    /// representation of a <see cref="PubSubConfigurationDataType"/>.
    /// Both <see cref="XmlPubSubConfigurationStore"/> and any future
    /// tooling reuse these helpers so the wire format remains
    /// identical to the one produced by the legacy
    /// <c>UaPubSubConfigurationHelper</c>.
    /// </summary>
    internal static class PubSubConfigurationXmlSerializer
    {
        /// <summary>
        /// Encodes <paramref name="configuration"/> as XML using
        /// <see cref="XmlEncoder"/>. The returned byte array contains
        /// a UTF-8 XML document ready to be written to disk.
        /// </summary>
        /// <param name="configuration">Configuration to encode.</param>
        /// <param name="context">Service message context.</param>
        /// <returns>UTF-8 XML bytes.</returns>
        public static byte[] EncodeXml(
            PubSubConfigurationDataType configuration,
            IServiceMessageContext context)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            using var stream = new MemoryStream();
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            settings.CloseOutput = false;
            using (var writer = XmlWriter.Create(stream, settings))
            {
                using var encoder = new XmlEncoder(
                    typeof(PubSubConfigurationDataType),
                    writer,
                    context);
                configuration.Encode(encoder);
                encoder.Close();
            }
            return stream.ToArray();
        }

        /// <summary>
        /// Decodes a <see cref="PubSubConfigurationDataType"/> from
        /// the UTF-8 XML payload in <paramref name="xml"/>.
        /// </summary>
        /// <param name="xml">UTF-8 XML bytes.</param>
        /// <param name="context">Service message context.</param>
        /// <returns>Decoded configuration.</returns>
        public static PubSubConfigurationDataType DecodeXml(
            ReadOnlySpan<byte> xml,
            IServiceMessageContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            byte[] buffer = xml.ToArray();
            using var stream = new MemoryStream(buffer, writable: false);
            return DecodeXmlCore(stream, context);
        }

        /// <summary>
        /// Decodes a <see cref="PubSubConfigurationDataType"/> from
        /// the supplied stream. The stream is read in-place; callers
        /// retain ownership.
        /// </summary>
        /// <param name="stream">Source stream.</param>
        /// <param name="context">Service message context.</param>
        /// <returns>Decoded configuration.</returns>
        public static PubSubConfigurationDataType DecodeXml(
            Stream stream,
            IServiceMessageContext context)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            return DecodeXmlCore(stream, context);
        }

        private static PubSubConfigurationDataType DecodeXmlCore(
            Stream stream,
            IServiceMessageContext context)
        {
            using var parser = new XmlParser(
                typeof(PubSubConfigurationDataType),
                stream,
                context);
            var configuration = new PubSubConfigurationDataType();
            configuration.Decode(parser);
            return configuration;
        }
    }
}
