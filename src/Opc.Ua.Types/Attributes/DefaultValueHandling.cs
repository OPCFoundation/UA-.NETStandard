// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Controls default value handling during encode/decode.
    /// </summary>
    [Flags]
    public enum DefaultValueHandling
    {
        /// <summary>
        /// Omit type default values on write (only when the declared
        /// default is the type default too); on read a field absent
        /// from XML keeps the constructor default, a field absent from
        /// JSON is the type default (OPC 10000-6 5.4.1). This is the
        /// default and works well for XML configuration files.
        /// </summary>
        Exclude = 0,

        /// <summary>
        /// Always write the field, even if it holds the
        /// default value. This mimics the DataMemberAttribute
        /// EmitDefaultValue set to true.
        /// </summary>
        Emit = 1,

        /// <summary>
        /// Always set the property on read, even if the field
        /// is absent (overwrites the class defaults with decoder's
        /// zero/false/null).
        /// </summary>
        SetIfMissing = 2,

        /// <summary>
        /// Always write AND always read — equivalent to
        /// Emit | SetIfMissing.
        /// </summary>
        Include = Emit | SetIfMissing
    }
}
