/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Interface implemented by complex data type instances.
    /// </summary>
    public interface IComplexTypeInstance
    {
        /// <summary>
        /// Gets or sets the UA type identifier for the encodeable type.
        /// </summary>
        /// <value>
        /// The UA type identifier.
        /// </value>
        ExpandedNodeId TypeId { get; set; }
    }

    /// <summary>
    /// Interface implemented by complex data type instances that discover and provide type information dynamically
    /// </summary>
    public interface IDynamicComplexTypeInstance : IComplexTypeInstance
    {
        /// <summary>
        /// Returns the xml qualified name for the type instance
        /// </summary>
        /// <returns>The xml qualified name for the type instance</returns>
        XmlQualifiedName GetXmlName(IServiceMessageContext context);
    }
}
