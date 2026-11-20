/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Stores information about engineering units.
    /// </summary>
    public partial class EUInformation
    {
        /// <summary>
        /// Initializes the object with the unitName and namespaceUri.
        /// </summary>
        public EUInformation(string unitName, string namespaceUri)
        {
            Initialize();

            m_displayName = new LocalizedText(unitName);
            m_description = new LocalizedText(unitName);
            m_namespaceUri = namespaceUri;
        }

        /// <summary>
        /// Initializes the object with the unitName and namespaceUri.
        /// </summary>
        public EUInformation(string shortName, string longName, string namespaceUri)
        {
            Initialize();

            m_displayName = new LocalizedText(shortName);
            m_description = new LocalizedText(longName);
            m_namespaceUri = namespaceUri;
        }
    }
}
