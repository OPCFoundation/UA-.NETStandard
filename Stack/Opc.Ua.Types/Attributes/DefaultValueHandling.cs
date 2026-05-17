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
    /// Controls default value handling during encode/decode.
    /// </summary>
    [Flags]
    public enum DefaultValueHandling
    {
        /// <summary>
        /// Omit default values on write; preserve constructor
        /// defaults on read when field is absent. This is the
        /// default and works well for configuration.
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
