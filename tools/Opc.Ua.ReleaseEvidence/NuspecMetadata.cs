// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Opc.Ua.ReleaseEvidence
{
    internal static class NuspecMetadata
    {
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

        public static string Required(XElement metadata, string name)
        {
            string? value = metadata.Elements().SingleOrDefault(e => e.Name.LocalName == name)?.Value;
            return string.IsNullOrWhiteSpace(value)
                ? throw new InvalidDataException($"Missing nuspec {name}.")
                : value.Trim();
        }

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
