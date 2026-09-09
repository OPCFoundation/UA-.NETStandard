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
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Reads bounded NuGet package metadata and extracts required identity and declared license information.
    /// </summary>
    internal static class NuspecMetadata
    {
        /// <summary>
        /// Parses a bounded nuspec without DTD resolution and requires exactly one metadata element.
        /// </summary>
        public static XElement Read(ReadOnlyMemory<byte> bytes)
        {
            using var stream = new MemoryStream(bytes.ToArray(), false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 16 * 1024 * 1024
            });
            var document = XDocument.Load(reader);
            XElement[] metadata = document.Root?.Elements().Where(e => e.Name.LocalName == "metadata").ToArray() ?? [];
            return metadata.Length == 1
                ? metadata[0]
                : throw new InvalidDataException("A package must contain exactly one nuspec metadata element.");
        }

        /// <summary>
        /// Returns a trimmed required metadata value or rejects a missing or blank value.
        /// </summary>
        public static string Required(XElement metadata, string name)
        {
            string? value = metadata.Elements().SingleOrDefault(e => e.Name.LocalName == name)?.Value;
            return string.IsNullOrWhiteSpace(value)
                ? throw new InvalidDataException($"Missing nuspec {name}.")
                : value.Trim();
        }

        /// <summary>
        /// Extracts the declared license expression, file, or legacy URL, or records that no license was declared.
        /// </summary>
        public static LicenseRecord[] Licenses(XElement metadata)
        {
            XElement? license = metadata.Elements().SingleOrDefault(e => e.Name.LocalName == "license");
            if (license != null)
            {
                string kind = license.Attribute("type")?.Value ?? "unknown";
                return [new LicenseRecord(kind, license.Value.Trim())];
            }
            XElement? url = metadata.Elements().SingleOrDefault(e => e.Name.LocalName == "licenseUrl");
            return url == null
                ? [new LicenseRecord("unknown", "not-declared")]
                : [new LicenseRecord("url", url.Value)];
        }
    }
}
