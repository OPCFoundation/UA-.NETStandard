// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.IO;
using System.Xml;

namespace Opc.Ua.Schema
{
    internal static class Extensions
    {
        /// <summary>
        /// Safe version for assignment of InnerXml.
        /// </summary>
        /// <param name="doc">The XmlDocument.</param>
        /// <param name="xml">The Xml document string.</param>
        internal static void LoadInnerXml(this XmlDocument doc, string xml)
        {
            using var sreader = new StringReader(xml);
            using var reader = XmlReader.Create(sreader, CoreUtils.DefaultXmlReaderSettings());
            doc.XmlResolver = null;
            doc.Load(reader);
        }
    }
}
