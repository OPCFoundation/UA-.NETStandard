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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Apache.Arrow;
using Apache.Arrow.Arrays;
using Apache.Arrow.Ipc;
using Apache.Arrow.Memory;
using Apache.Arrow.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Pairs an Arrow field template with the single-column array produced for an encoded OPC UA value.
    /// </summary>
    /// <param name = "Template">The input required by this experimental codec helper.</param>
    /// <param name = "Array">The input required by this experimental codec helper.</param>
    internal sealed record Slot(Field Template, IArrowArray Array)
    {
        /// <summary>
        /// Creates an Arrow field with the supplied column name and this slot's template metadata.
        /// </summary>
        /// <param name = "name">The field or column name to assign.</param>
        /// <returns>The Arrow field with the supplied name.</returns>
        public Field Field(string name)
        {
            return new(name, Template.DataType, Template.IsNullable, Template.Metadata);
        }
    }
}
