/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Xml;
using System.Xml.Serialization;

namespace Opc.Ua.Schema.Binary
{
    /// <summary>
    /// A description of type,
    /// </summary>
    public partial class TypeDescription
    {
        /// <summary>
        /// The qualifed name for the type.
        /// </summary>
        [XmlIgnore]
        public XmlQualifiedName? QName { get; set; }
    }
}
