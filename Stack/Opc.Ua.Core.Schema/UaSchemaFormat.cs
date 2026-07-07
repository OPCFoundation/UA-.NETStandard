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

namespace Opc.Ua.Schema
{
    /// <summary>
    /// The schema format (encoding) to generate for a data type.
    /// </summary>
    public enum UaSchemaFormat
    {
        /// <summary>
        /// XML schema (XSD) for the XML data encoding.
        /// </summary>
        Xsd,

        /// <summary>
        /// OPC Binary schema (BSD) for the binary data encoding.
        /// </summary>
        Bsd,

        /// <summary>
        /// JSON Schema for the compact (reversible) JSON data encoding.
        /// </summary>
        JsonCompact,

        /// <summary>
        /// JSON Schema for the verbose JSON data encoding.
        /// </summary>
        JsonVerbose
    }

    /// <summary>
    /// The scope of a generated schema document.
    /// </summary>
    public enum UaSchemaScope
    {
        /// <summary>
        /// A schema document for a single data type and the closure of the
        /// types it depends on.
        /// </summary>
        Type,

        /// <summary>
        /// A schema document (dictionary) for all data types in a namespace.
        /// </summary>
        Namespace
    }
}
