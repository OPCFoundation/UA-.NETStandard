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
 *
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

using System.Xml;
using Opc.Ua.Types;

namespace Opc.Ua.Export
{
    public partial class UANodeSet
    {
        internal System.Xml.XmlElement RebaseValue(
            System.Xml.XmlElement source,
            UANodeSet target,
            IServiceMessageContext context,
            bool rebase)
        {
            using XmlDecoder decoder = CreateDecoder(context, source);
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
            using XmlDecoder verification = target.CreateDecoder(context, result);
            verification.RequireCompleteValue = true;
            if (value != verification.ReadVariant(null))
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingError, "The value codec did not preserve the decoded value.");
            }
            return result;
        }

        private XmlEncoder CreateEncoder(IServiceMessageContext context)
        {
            var encoder = new XmlEncoder(context);
            encoder.SetNodeSetMappingTables(
                new NamespaceTable([Namespaces.OpcUa, .. NamespaceUris ?? []]),
                CreateServerMappingTable(context));
            return encoder;
        }

        private XmlDecoder CreateDecoder(IServiceMessageContext context, System.Xml.XmlElement source)
        {
            var decoder = new XmlDecoder(WrapAsVariant(source), context);
            decoder.SetNodeSetMappingTables(
                new NamespaceTable([Namespaces.OpcUa, .. NamespaceUris ?? []]),
                CreateServerMappingTable(context));
            return decoder;
        }

        private StringTable CreateServerMappingTable(IServiceMessageContext context)
        {
            return new StringTable([context.ServerUris.GetString(0) ?? string.Empty, .. ServerUris ?? []]);
        }
    }
}
