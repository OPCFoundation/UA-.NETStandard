// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        /// <exception cref="ArgumentNullException"></exception>
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
        /// <exception cref="ArgumentNullException"></exception>
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
        /// <exception cref="ArgumentNullException"></exception>
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
