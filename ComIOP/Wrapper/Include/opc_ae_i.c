/* ========================================================================
 * Copyright (c) 2005-2011 The OPC Foundation, Inc. All rights reserved.
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
/* at Wed Jul 09 16:49:49 2008
 */
/* Compiler settings for .\opc_ae.idl:
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

MIDL_DEFINE_GUID(IID, IID_OPCEventServerCATID,0x58E13251,0xAC87,0x11d1,0x84,0xD5,0x00,0x60,0x8C,0xB8,0xA7,0xE9);


MIDL_DEFINE_GUID(IID, IID_IOPCEventServer,0x65168851,0x5783,0x11D1,0x84,0xA0,0x00,0x60,0x8C,0xB8,0xA7,0xE9);


MIDL_DEFINE_GUID(IID, IID_IOPCEventSubscriptionMgt,0x65168855,0x5783,0x11D1,0x84,0xA0,0x00,0x60,0x8C,0xB8,0xA7,0xE9);


MIDL_DEFINE_GUID(IID, IID_IOPCEventAreaBrowser,0x65168857,0x5783,0x11D1,0x84,0xA0,0x00,0x60,0x8C,0xB8,0xA7,0xE9);


MIDL_DEFINE_GUID(IID, IID_IOPCEventSink,0x6516885F,0x5783,0x11D1,0x84,0xA0,0x00,0x60,0x8C,0xB8,0xA7,0xE9);


MIDL_DEFINE_GUID(IID, IID_IOPCEventServer2,0x71BBE88E,0x9564,0x4bcd,0xBC,0xFC,0x71,0xC5,0x58,0xD9,0x4F,0x2D);


MIDL_DEFINE_GUID(IID, IID_IOPCEventSubscriptionMgt2,0x94C955DC,0x3684,0x4ccb,0xAF,0xAB,0xF8,0x98,0xCE,0x19,0xAA,0xC3);


MIDL_DEFINE_GUID(IID, LIBID_OPC_AE,0x65168844,0x5783,0x11D1,0x84,0xA0,0x00,0x60,0x8C,0xB8,0xA7,0xE9);

#undef MIDL_DEFINE_GUID

#ifdef __cplusplus
}
#endif
