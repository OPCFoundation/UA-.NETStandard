/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community Binary License ("RCBL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community Binary License ("RCBL") Version 1.00, or subsequent versions 
 * as allowed by the RCBL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCBL.
 * 
 * All software distributed under the RCBL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCBL for specific 
 * language governing rights and limitations under the RCBL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCBL/1.00/
 * ======================================================================*/

/* link this file in with the server and any clients */


 /* File created by MIDL compiler version 6.00.0366 */
/* at Fri Feb 09 15:47:53 2008
 */
/* Compiler settings for .\opccomn.idl:
    Oicf, W1, Zp8, env=Win32 (32b run)
    protocol : dce , ms_ext, c_ext
    error checks: allocation ref bounds_check enum stub_data 
    VC __declspec() decoration level: 
         __declspec(uuid()), __declspec(selectany), __declspec(novtable)
         DECLSPEC_UUID(), MIDL_INTERFACE()
*/
//@@MIDL_FILE_HEADING(  )

#pragma warning( disable: 4049 )  /* more than 64k source lines */


#ifdef __cplusplus
extern "C"{
#endif 


#include <rpc.h>
#include <rpcndr.h>

#ifdef _MIDL_USE_GUIDDEF_

#ifndef INITGUID
#define INITGUID
#include <guiddef.h>
#undef INITGUID
#else
#include <guiddef.h>
#endif

#define MIDL_DEFINE_GUID(type,name,l,w1,w2,b1,b2,b3,b4,b5,b6,b7,b8) \
        DEFINE_GUID(name,l,w1,w2,b1,b2,b3,b4,b5,b6,b7,b8)

#else // !_MIDL_USE_GUIDDEF_

#ifndef __IID_DEFINED__
#define __IID_DEFINED__

typedef struct _IID
{
    unsigned long x;
    unsigned short s1;
    unsigned short s2;
    unsigned char  c[8];
} IID;

#endif // __IID_DEFINED__

#ifndef CLSID_DEFINED
#define CLSID_DEFINED
typedef IID CLSID;
#endif // CLSID_DEFINED

#define MIDL_DEFINE_GUID(type,name,l,w1,w2,b1,b2,b3,b4,b5,b6,b7,b8) \
        const type name = {l,w1,w2,{b1,b2,b3,b4,b5,b6,b7,b8}}

#endif !_MIDL_USE_GUIDDEF_

MIDL_DEFINE_GUID(IID, IID_IOPCShutdown,0xF31DFDE1,0x07B6,0x11d2,0xB2,0xD8,0x00,0x60,0x08,0x3B,0xA1,0xFB);


MIDL_DEFINE_GUID(IID, IID_IOPCCommon,0xF31DFDE2,0x07B6,0x11d2,0xB2,0xD8,0x00,0x60,0x08,0x3B,0xA1,0xFB);


MIDL_DEFINE_GUID(IID, IID_IOPCServerList,0x13486D50,0x4821,0x11D2,0xA4,0x94,0x3C,0xB3,0x06,0xC1,0x00,0x00);


MIDL_DEFINE_GUID(IID, IID_IOPCEnumGUID,0x55C382C8,0x21C7,0x4e88,0x96,0xC1,0xBE,0xCF,0xB1,0xE3,0xF4,0x83);


MIDL_DEFINE_GUID(IID, IID_IOPCServerList2,0x9DD0B56C,0xAD9E,0x43ee,0x83,0x05,0x48,0x7F,0x31,0x88,0xBF,0x7A);


MIDL_DEFINE_GUID(IID, LIBID_OPCCOMN,0xB28EEDB1,0xAC6F,0x11d1,0x84,0xD5,0x00,0x60,0x8C,0xB8,0xA7,0xE9);

#undef MIDL_DEFINE_GUID

#ifdef __cplusplus
}
#endif
