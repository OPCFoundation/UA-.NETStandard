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

using System.IO;
using System.Xml;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Checks XML element depth before loading untrusted XML into a DOM.
    /// </summary>
    internal static class XmlElementDepth
    {
        /// <summary>
        /// Rejects XML whose elements are nested deeper than the encoding limit.
        /// </summary>
        internal static void Check(
            string xml,
            IServiceMessageContext context,
            ConformanceLevel conformanceLevel)
        {
            if (string.IsNullOrEmpty(xml))
            {
                return;
            }

            int maxDepth = context.MaxEncodingNestingLevels > 0
                ? context.MaxEncodingNestingLevels
                : DefaultEncodingLimits.MaxEncodingNestingLevels;

            XmlReaderSettings settings = CoreUtils.DefaultXmlReaderSettings();
            settings.ConformanceLevel = conformanceLevel;

            using var stream = new StringReader(xml);
            using var reader = XmlReader.Create(stream, settings);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Depth > maxDepth)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "XML element nesting exceeds the maximum depth of {0}.",
                        maxDepth);
                }
            }
        }
    }
}
