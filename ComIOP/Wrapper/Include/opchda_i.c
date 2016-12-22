/* ========================================================================
 * Copyright (c) 2005-2011 The OPC Foundation, Inc. All rights reserved.
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
/* at Wed Sep 01 11:47:35 2004
 */
/* Compiler settings for .\opchda.idl:
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

MIDL_DEFINE_GUID(IID, IID_CATID_OPCHDAServer10,0x7DE5B060,0xE089,0x11d2,0xA5,0xE6,0x00,0x00,0x86,0x33,0x93,0x99);


MIDL_DEFINE_GUID(IID, IID_IOPCHDA_Browser,0x1F1217B1,0xDEE0,0x11d2,0xA5,0xE5,0x00,0x00,0x86,0x33,0x93,0x99);


MIDL_DEFINE_GUID(IID, IID_IOPCHDA_Server,0x1F1217B0,0xDEE0,0x11d2,0xA5,0xE5,0x00,0x00,0x86,0x33,0x93,0x99);


MIDL_DEFINE_GUID(IID, IID_IOPCHDA_SyncRead,0x1F1217B2,0xDEE0,0x11d2,0xA5,0xE5,0x00,0x00,0x86,0x33,0x93,0x99);


MIDL_DEFINE_GUID(IID, IID_IOPCHDA_SyncUpdate,0x1F1217B3,0xDEE0,0x11d2,0xA5,0xE5,0x00,0x00,0x86,0x33,0x93,0x99);


MIDL_DEFINE_GUID(IID, IID_IOPCHDA_SyncAnnotations,0x1F1217B4,0xDEE0,0x11d2,0xA5,0xE5,0x00,0x00,0x86,0x33,0x93,0x99);


MIDL_DEFINE_GUID(IID, IID_IOPCHDA_AsyncRead,0x1F1217B5,0xDEE0,0x11d2,0xA5,0xE5,0x00,0x00,0x86,0x33,0x93,0x99);


MIDL_DEFINE_GUID(IID, IID_IOPCHDA_AsyncUpdate,0x1F1217B6,0xDEE0,0x11d2,0xA5,0xE5,0x00,0x00,0x86,0x33,0x93,0x99);


MIDL_DEFINE_GUID(IID, IID_IOPCHDA_AsyncAnnotations,0x1F1217B7,0xDEE0,0x11d2,0xA5,0xE5,0x00,0x00,0x86,0x33,0x93,0x99);


MIDL_DEFINE_GUID(IID, IID_IOPCHDA_Playback,0x1F1217B8,0xDEE0,0x11d2,0xA5,0xE5,0x00,0x00,0x86,0x33,0x93,0x99);


MIDL_DEFINE_GUID(IID, IID_IOPCHDA_DataCallback,0x1F1217B9,0xDEE0,0x11d2,0xA5,0xE5,0x00,0x00,0x86,0x33,0x93,0x99);


MIDL_DEFINE_GUID(IID, LIBID_OPCHDA,0x1F1217BA,0xDEE0,0x11d2,0xA5,0xE5,0x00,0x00,0x86,0x33,0x93,0x99);

#undef MIDL_DEFINE_GUID

#ifdef __cplusplus
}
#endif



#endif /* !defined(_M_IA64) && !defined(_M_AMD64)*/
