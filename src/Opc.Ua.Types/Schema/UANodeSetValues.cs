// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Xml;
using Opc.Ua.Types;

namespace Opc.Ua.Export
{
    /// <summary>
    /// A set of nodes in an address space.
    /// </summary>
    public partial class UANodeSet
    {
        /// <summary>
        /// Rebases the namespace indexes of a value element onto the target node set.
        /// </summary>
        /// <param name="source">The XML element.</param>
        /// <param name="target">The UA node set.</param>
        /// <param name="context">The service message context.</param>
        /// <param name="rebase">The rebase flag supplied by the caller.</param>
        internal System.Xml.XmlElement RebaseValue(
            System.Xml.XmlElement source,
            UANodeSet target,
            IServiceMessageContext context,
            bool rebase)
        {
            using XmlDecoder decoder = CreateDecoder(context, source, preserveRemoteServerIdentity: true);
            decoder.RequireCompleteValue = true;
            decoder.AllowOpaqueValues = !rebase;
            Variant value = decoder.ReadVariant(null);
            if (!rebase || value.IsNull)
            {
                return source;
            }
            using XmlEncoder encoder = target.CreateEncoder(context);
            encoder.PreserveStringValues = true;
            encoder.WriteVariantValue(null, value);
            var document = new XmlDocument { XmlResolver = null };
            document.LoadInnerXml(encoder.CloseAndReturnText()!);
            System.Xml.XmlElement result = document.DocumentElement ?? throw new ServiceResultException(
                StatusCodes.BadEncodingError, "The value codec produced no XML element.");
            using XmlDecoder verification = target.CreateDecoder(context, result, preserveRemoteServerIdentity: true);
            verification.RequireCompleteValue = true;
            if (value != verification.ReadVariant(null))
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingError, "The value codec did not preserve the decoded value.");
            }
            return result;
        }

        /// <summary>
        /// Includes the implicit local server slot without assigning it a URI.
        /// </summary>
        internal StringTable CreateServerMappingTable()
        {
            return new StringTable([string.Empty, .. ServerUris ?? []]);
        }

        private XmlEncoder CreateEncoder(IServiceMessageContext context)
        {
            var encoder = new XmlEncoder(context);
            encoder.SetNodeSetMappingTables(
                new NamespaceTable([Namespaces.OpcUa, .. NamespaceUris ?? []]),
                CreateServerMappingTable());
            return encoder;
        }

        private XmlDecoder CreateDecoder(
            IServiceMessageContext context,
            System.Xml.XmlElement source,
            bool preserveRemoteServerIdentity = false)
        {
            var decoder = new XmlDecoder(WrapAsVariant(source), context);
            decoder.SetNodeSetMappingTables(
                new NamespaceTable([Namespaces.OpcUa, .. NamespaceUris ?? []]),
                CreateServerMappingTable(),
                preserveRemoteServerIdentity);
            return decoder;
        }
    }
}
