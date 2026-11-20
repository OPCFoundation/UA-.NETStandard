/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
        [LoggerMessage(EventId = TypesEventIds.Encoding + 0, Level = LogLevel.Warning,
            Message = "InnerDiagnosticInfo dropped because nesting exceeds maximum of {MaxInnerDepth}.")]
        public static partial void InnerDiagnosticInfoDropped(this ILogger logger, int maxInnerDepth);

        [LoggerMessage(EventId = TypesEventIds.Encoding + 1, Level = LogLevel.Warning,
            Message = "Cannot deserialize extension objects if the NamespaceUri is not in the " +
                "NamespaceTable: Type = {Type}")]
        public static partial void CannotDeserializeExtensionObject(this ILogger logger, NodeId type);

        [LoggerMessage(EventId = TypesEventIds.Encoding + 2, Level = LogLevel.Warning,
            Message = "Cannot deserialize extension objects if the NamespaceUri is not in the " +
                "NamespaceTable: Type = {Type}")]
        public static partial void CannotDeserializeExtensionObject(this ILogger logger, ExpandedNodeId type);

        [LoggerMessage(EventId = TypesEventIds.Encoding + 3, Level = LogLevel.Information,
            Message = "Cannot deserialize extension object from body.")]
        public static partial void CannotDeserializeExtensionObjectBody(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = TypesEventIds.Encoding + 4, Level = LogLevel.Debug,
            Message = "Failed to retrieve activator for extension object.")]
        public static partial void ActivatorNotFound(this ILogger logger);

        [LoggerMessage(EventId = TypesEventIds.Encoding + 5, Level = LogLevel.Warning,
            Message = "Could not decode known type {Name} encoded as Xml. Error={Message}. " +
                "BinaryDecoder recovered.")]
        public static partial void CouldNotDecodeKnownTypeXml(
            this ILogger logger,
            XmlQualifiedName name,
            string message);

        [LoggerMessage(EventId = TypesEventIds.Encoding + 6, Level = LogLevel.Warning,
            Message = "{Message}, failed to decode encodeable type '{Name}', NodeId='{NodeId}'. " +
                "BinaryDecoder recovered.")]
        public static partial void DecodeEncodeableRecovered(
            this ILogger logger,
            Exception? exception,
            string message,
            XmlQualifiedName name,
            ExpandedNodeId nodeId);

        [LoggerMessage(EventId = TypesEventIds.Encoding + 7, Level = LogLevel.Error,
            Message = "Error reading variant.")]
        public static partial void ErrorReadingVariant(this ILogger logger, Exception ex);
    }
}
