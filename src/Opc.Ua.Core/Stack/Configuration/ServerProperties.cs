/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua
{
    /// <summary>
    /// The properties of the current server instance.
    /// </summary>
    public class ServerProperties
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ServerProperties()
        {
            ProductUri = string.Empty;
            ManufacturerName = string.Empty;
            ProductName = string.Empty;
            SoftwareVersion = string.Empty;
            BuildNumber = string.Empty;
            BuildDate = DateTime.MinValue;
            DatatypeAssemblies = [];
        }

        /// <summary>
        /// The unique identifier for the product.
        /// </summary>
        public string ProductUri { get; set; }

        /// <summary>
        /// The name of the product
        /// </summary>
        public string ProductName { get; set; }

        /// <summary>
        /// The name of the manufacturer
        /// </summary>
        public string ManufacturerName { get; set; }

        /// <summary>
        /// The software version for the application
        /// </summary>
        public string SoftwareVersion { get; set; }

        /// <summary>
        /// The build number for the application
        /// </summary>
        public string BuildNumber { get; set; }

        /// <summary>
        /// When the application was built.
        /// </summary>
        public DateTime BuildDate { get; set; }

        /// <summary>
        /// The assemblies that contain encodeable types that could be uses a variable values.
        /// </summary>
        public ArrayOf<string> DatatypeAssemblies { get; }
    }
}
