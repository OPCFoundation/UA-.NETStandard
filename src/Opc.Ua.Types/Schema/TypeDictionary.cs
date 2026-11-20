/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Xml;
using System.Xml.Serialization;

namespace Opc.Ua.Schema.Types
{
    /// <summary>
    /// A description of type,
    /// </summary>
    public partial class DataType
    {
        /// <summary>
        /// The qualifed name for the data type.
        /// </summary>
        [XmlIgnore]
        public XmlQualifiedName? QName { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            if (QName != null)
            {
                return QName.Name;
            }

            return base.ToString() ?? string.Empty;
        }
    }
}
