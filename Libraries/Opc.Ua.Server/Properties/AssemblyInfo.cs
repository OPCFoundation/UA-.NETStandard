/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/
using System.Runtime.CompilerServices;

#if HAVE_SERVER_TESTS  // TODO: uncomment when server tests become available
#if SIGNASSEMBLY
[assembly: InternalsVisibleTo("Opc.Ua.Server.Tests, PublicKey = " +
    // OPC Foundation Strong Name Public Key
    "0024000004800000940000000602000000240000525341310004000001000100d987b12f068b35" +
    "80429f3dde01397508880fc7e62621397618456ca1549aeacfbdb90c62adfe918f05ce3677b390" +
    "f78357b8745cb6e1334655afce1a9527ac92fc829ff585ea79f007e52ba0f83ead627e3edda40b" +
    "ec5ae574128fc9342cb57cb8285aa4e5b589c0ebef3be571b5c8f2ab1067f7c880e8f8882a73c8" +
    "0a12a1ef")]
#else
[assembly: InternalsVisibleTo("Opc.Ua.Server.Tests")]
#endif
#endif
