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

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Stores the configuration for the Global Discovery Client.
    /// Serialized via source-generated IEncodeable implementation.
    /// </summary>
    [DataType(Namespace = Namespaces.OpcUaGds + "Configuration.xsd")]
    public sealed partial class GlobalDiscoveryClientConfiguration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public GlobalDiscoveryClientConfiguration()
        {
        }

        /// <summary>
        /// The URL of the Global Discovery Server.
        /// </summary>
        [DataTypeField(Order = 0)]
        public string? GlobalDiscoveryServerUrl { get; set; }

        /// <summary>
        /// The path to the external editor application.
        /// </summary>
        [DataTypeField(Order = 1)]
        public string? ExternalEditor { get; set; }
    }
}
