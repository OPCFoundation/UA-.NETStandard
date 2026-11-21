// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Compliance.Classification;

namespace Opc.Ua.Redaction
{
    /// <summary>
    /// Marks a value as <see cref="OpcUaDataClassifications.NetworkAddress"/>.
    /// </summary>
    public sealed class NetworkAddressAttribute : DataClassificationAttribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkAddressAttribute"/> class.
        /// </summary>
        public NetworkAddressAttribute()
            : base(OpcUaDataClassifications.NetworkAddress)
        {
        }
    }
}
