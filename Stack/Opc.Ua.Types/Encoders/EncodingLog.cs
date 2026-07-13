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
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Shared source-generated log messages for the binary, JSON and XML encoders, decoders
    /// and parsers. These codecs emit the same diagnostics, so the messages are defined once
    /// here to avoid duplicate extension method definitions.
    /// </summary>
    internal static partial class EncodingLog
    {
        [LoggerMessage(EventId = EventIds.Encoding + 0, Level = LogLevel.Warning,
            Message = "InnerDiagnosticInfo dropped because nesting exceeds maximum of {MaxInnerDepth}.")]
        public static partial void InnerDiagnosticInfoDropped(this ILogger logger, int maxInnerDepth);

        [LoggerMessage(EventId = EventIds.Encoding + 1, Level = LogLevel.Warning,
            Message = "Cannot deserialize extension objects if the NamespaceUri is not in the " +
                "NamespaceTable: Type = {Type}")]
        public static partial void CannotDeserializeExtensionObject(this ILogger logger, NodeId type);

        [LoggerMessage(EventId = EventIds.Encoding + 2, Level = LogLevel.Warning,
            Message = "Cannot deserialize extension objects if the NamespaceUri is not in the " +
                "NamespaceTable: Type = {Type}")]
        public static partial void CannotDeserializeExtensionObject(this ILogger logger, ExpandedNodeId type);

        [LoggerMessage(EventId = EventIds.Encoding + 3, Level = LogLevel.Information,
            Message = "Cannot deserialize extension object from body.")]
        public static partial void CannotDeserializeExtensionObjectBody(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = EventIds.Encoding + 4, Level = LogLevel.Debug,
            Message = "Failed to retrieve activator for extension object.")]
        public static partial void ActivatorNotFound(this ILogger logger);

        [LoggerMessage(EventId = EventIds.Encoding + 5, Level = LogLevel.Error,
            Message = "Could not decode known type {Name} encoded as Xml. Error={Message}, Value={OuterXml}")]
        public static partial void CouldNotDecodeKnownTypeXml(
            this ILogger logger,
            XmlQualifiedName name,
            string message,
            string? outerXml);

        [LoggerMessage(EventId = EventIds.Encoding + 6, Level = LogLevel.Warning,
            Message = "{Message}, failed to decode encodeable type '{Name}', NodeId='{NodeId}'. " +
                "BinaryDecoder recovered.")]
        public static partial void DecodeEncodeableRecovered(
            this ILogger logger,
            Exception? exception,
            string message,
            XmlQualifiedName name,
            ExpandedNodeId nodeId);

        [LoggerMessage(EventId = EventIds.Encoding + 7, Level = LogLevel.Error,
            Message = "Error reading variant.")]
        public static partial void ErrorReadingVariant(this ILogger logger, Exception ex);
    }
}
