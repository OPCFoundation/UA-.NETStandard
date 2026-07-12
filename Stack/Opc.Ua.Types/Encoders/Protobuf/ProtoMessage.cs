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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// Stores the decoded fields of one Protobuf message for positional lookup.
    /// </summary>
    internal sealed class ProtoMessage
    {
        /// <summary>
        /// Stores the decoded values of one Protobuf field.
        /// </summary>
        /// <returns>The result produced by this codec helper.</returns>
        public List<ProtoField> Fields { get; } = new();

        /// <summary>
        /// Returns the first decoded Protobuf field with the requested field number.
        /// </summary>
        /// <param name = "number">The Protobuf field number to locate.</param>
        /// <returns>The first matching field, or null when the field is absent.</returns>
        public ProtoField? First(int number)
        {
            foreach (ProtoField field in Fields)
            {
                if (field.Number == number)
                {
                    return field;
                }
            }

            return null;
        }

        /// <summary>
        /// Enumerates all decoded Protobuf fields with the requested field number.
        /// </summary>
        /// <param name = "number">The Protobuf field number to locate.</param>
        /// <returns>The matching decoded fields.</returns>
        public IEnumerable<ProtoField> All(int number)
        {
            return Fields.Where(f => f.Number == number);
        }

        /// <summary>
        /// Returns whether the decoded Protobuf message contains the requested field number.
        /// </summary>
        /// <param name = "number">The Protobuf field number to locate.</param>
        /// <returns>True when the message contains the field number.</returns>
        public bool Has(int number)
        {
            return Fields.Any(f => f.Number == number);
        }
    }
}
