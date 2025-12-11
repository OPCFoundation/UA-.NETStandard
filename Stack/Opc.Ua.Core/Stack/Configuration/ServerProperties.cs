/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
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
        public StringCollection DatatypeAssemblies { get; }
    }
}
