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
using System.Linq;

namespace Opc.Ua.Client.Tests
{
    public static class Extensions
    {
        public static bool HasArgsOfType(
            this CallMethodRequestCollection requests,
            params Type[] argTypes)
        {
            if (requests.Count != 1 || requests[0].InputArguments.Count != argTypes.Length)
            {
                return false;
            }
            for (int i = 0; i < argTypes.Length; i++)
            {
                if (requests[0].InputArguments[i].Value.GetType() != argTypes[i])
                {
                    return false;
                }
            }
            return true;
        }

        public static CallResponse ToResponse(
            this List<object> outputArguments, StatusCode response = default, StatusCode result = default)
        {
            return new CallResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    ServiceResult = response
                },
                Results =
                [
                    new CallMethodResult
                    {
                        StatusCode = result,
                        OutputArguments = outputArguments == null ?
                            null :
                            [.. outputArguments.Select(o => new Variant(o))]
                    }
                ]
            };
        }
    }
}
