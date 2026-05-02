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
