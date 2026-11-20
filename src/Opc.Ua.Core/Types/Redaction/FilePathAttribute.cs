/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Microsoft.Extensions.Compliance.Classification;

namespace Opc.Ua.Redaction
{
    /// <summary>
    /// Marks a value as <see cref="OpcUaDataClassifications.FilePath"/>.
    /// </summary>
    public sealed class FilePathAttribute : DataClassificationAttribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FilePathAttribute"/> class.
        /// </summary>
        public FilePathAttribute()
            : base(OpcUaDataClassifications.FilePath)
        {
        }
    }
}
