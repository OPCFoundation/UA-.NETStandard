/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Controls how IEncodeable fields are encoded.
    /// </summary>
    public enum StructureHandling
    {
        /// <summary>
        /// Generator decides automatically based on sealed/base
        /// type analysis. If the type is sealed and not deriving
        /// it uses Inline mode, otherwise it uses ExtensionObject.
        /// This is the default.
        /// </summary>
        Auto = 0,

        /// <summary>
        /// Force encoding as encodeable object (using the defined
        /// data encoding) via WriteEncodeable/ReadEncodeable. This
        /// produces the exact type but expects the receiver to
        /// know the type already.
        /// </summary>
        Inline = 1,

        /// <summary>
        /// Force encoding and decoding the structure as an
        /// ExtensionObject via WriteEncodeableAsExtensionObject and
        /// ReadEncodeableAsExtensionObject. This adds the type
        /// information to the encoded data and allows the receiver
        /// to decode the structure without prior knowledge of the
        /// type, including support for polymorphic types.
        /// </summary>
        ExtensionObject = 2
    }
}
