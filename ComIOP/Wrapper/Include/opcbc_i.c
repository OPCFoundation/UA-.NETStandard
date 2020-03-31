/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions as 
 * allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

/* link this file in with the server and any clients */


 /* File created by MIDL compiler version 6.00.0361 */
/* at Wed Sep 01 11:47:33 2004
 */
/* Compiler settings for .\opcbc.idl:
    Oicf, W1, Zp8, env=Win32 (32b run)
    protocol : dce , ms_ext, c_ext
    error checks: allocation ref bounds_check enum stub_data 
    VC __declspec() decoration level: 
         __declspec(uuid()), __declspec(selectany), __declspec(novtable)
         DECLSPEC_UUID(), MIDL_INTERFACE()
*/
//@@MIDL_FILE_HEADING(  )

#if !defined(_M_IA64) && !defined(_M_AMD64)


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

MIDL_DEFINE_GUID(IID, IID_CATID_OPCBatchServer10,0xA8080DA0,0xE23E,0x11D2,0xAF,0xA7,0x00,0xC0,0x4F,0x53,0x94,0x21);


MIDL_DEFINE_GUID(IID, IID_CATID_OPCBatchServer20,0x843DE67B,0xB0C9,0x11d4,0xA0,0xB7,0x00,0x01,0x02,0xA9,0x80,0xB1);


MIDL_DEFINE_GUID(IID, IID_IOPCBatchServer,0x8BB4ED50,0xB314,0x11d3,0xB3,0xEA,0x00,0xC0,0x4F,0x8E,0xCE,0xAA);


MIDL_DEFINE_GUID(IID, IID_IOPCBatchServer2,0x895A78CF,0xB0C5,0x11d4,0xA0,0xB7,0x00,0x01,0x02,0xA9,0x80,0xB1);


MIDL_DEFINE_GUID(IID, IID_IEnumOPCBatchSummary,0xa8080da2,0xe23e,0x11d2,0xaf,0xa7,0x00,0xc0,0x4f,0x53,0x94,0x21);


MIDL_DEFINE_GUID(IID, IID_IOPCEnumerationSets,0xa8080da3,0xe23e,0x11d2,0xaf,0xa7,0x00,0xc0,0x4f,0x53,0x94,0x21);


MIDL_DEFINE_GUID(IID, LIBID_OPC_BATCH,0xa8080da4,0xe23e,0x11d2,0xaf,0xa7,0x00,0xc0,0x4f,0x53,0x94,0x21);

#undef MIDL_DEFINE_GUID

#ifdef __cplusplus
}
#endif



#endif /* !defined(_M_IA64) && !defined(_M_AMD64)*/
