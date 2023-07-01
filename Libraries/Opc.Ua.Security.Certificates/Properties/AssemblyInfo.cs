/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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

using System.Runtime.CompilerServices;

#if SIGNASSEMBLY
[assembly: InternalsVisibleTo("Opc.Ua.Security.Certificates.Tests, PublicKey = " +
    // OPC Foundation Strong Name Public Key
    "0024000004800000940000000602000000240000525341310004000001000100d987b12f068b35" +
    "80429f3dde01397508880fc7e62621397618456ca1549aeacfbdb90c62adfe918f05ce3677b390" +
    "f78357b8745cb6e1334655afce1a9527ac92fc829ff585ea79f007e52ba0f83ead627e3edda40b" +
    "ec5ae574128fc9342cb57cb8285aa4e5b589c0ebef3be571b5c8f2ab1067f7c880e8f8882a73c8" +
    "0a12a1ef")]
[assembly: InternalsVisibleTo("Opc.Ua.Core.Tests, PublicKey = " +
    // OPC Foundation Strong Name Public Key
    "0024000004800000940000000602000000240000525341310004000001000100d987b12f068b35" +
    "80429f3dde01397508880fc7e62621397618456ca1549aeacfbdb90c62adfe918f05ce3677b390" +
    "f78357b8745cb6e1334655afce1a9527ac92fc829ff585ea79f007e52ba0f83ead627e3edda40b" +
    "ec5ae574128fc9342cb57cb8285aa4e5b589c0ebef3be571b5c8f2ab1067f7c880e8f8882a73c8" +
    "0a12a1ef")]
#else
[assembly: InternalsVisibleTo("Opc.Ua.Security.Certificates.Tests")]
[assembly: InternalsVisibleTo("Opc.Ua.Core.Tests")]
#endif
