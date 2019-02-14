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
/* Compiler settings for .\opcda.idl:
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

MIDL_DEFINE_GUID(IID, IID_CATID_OPCDAServer10,0x63D5F430,0xCFE4,0x11d1,0xB2,0xC8,0x00,0x60,0x08,0x3B,0xA1,0xFB);


MIDL_DEFINE_GUID(IID, IID_CATID_OPCDAServer20,0x63D5F432,0xCFE4,0x11d1,0xB2,0xC8,0x00,0x60,0x08,0x3B,0xA1,0xFB);


MIDL_DEFINE_GUID(IID, IID_CATID_OPCDAServer30,0xCC603642,0x66D7,0x48f1,0xB6,0x9A,0xB6,0x25,0xE7,0x36,0x52,0xD7);


MIDL_DEFINE_GUID(IID, IID_CATID_XMLDAServer10,0x3098EDA4,0xA006,0x48b2,0xA2,0x7F,0x24,0x74,0x53,0x95,0x94,0x08);


MIDL_DEFINE_GUID(IID, IID_IOPCServer,0x39c13a4d,0x011e,0x11d0,0x96,0x75,0x00,0x20,0xaf,0xd8,0xad,0xb3);


MIDL_DEFINE_GUID(IID, IID_IOPCServerPublicGroups,0x39c13a4e,0x011e,0x11d0,0x96,0x75,0x00,0x20,0xaf,0xd8,0xad,0xb3);


MIDL_DEFINE_GUID(IID, IID_IOPCBrowseServerAddressSpace,0x39c13a4f,0x011e,0x11d0,0x96,0x75,0x00,0x20,0xaf,0xd8,0xad,0xb3);


MIDL_DEFINE_GUID(IID, IID_IOPCGroupStateMgt,0x39c13a50,0x011e,0x11d0,0x96,0x75,0x00,0x20,0xaf,0xd8,0xad,0xb3);


MIDL_DEFINE_GUID(IID, IID_IOPCPublicGroupStateMgt,0x39c13a51,0x011e,0x11d0,0x96,0x75,0x00,0x20,0xaf,0xd8,0xad,0xb3);


MIDL_DEFINE_GUID(IID, IID_IOPCSyncIO,0x39c13a52,0x011e,0x11d0,0x96,0x75,0x00,0x20,0xaf,0xd8,0xad,0xb3);


MIDL_DEFINE_GUID(IID, IID_IOPCAsyncIO,0x39c13a53,0x011e,0x11d0,0x96,0x75,0x00,0x20,0xaf,0xd8,0xad,0xb3);


MIDL_DEFINE_GUID(IID, IID_IOPCItemMgt,0x39c13a54,0x011e,0x11d0,0x96,0x75,0x00,0x20,0xaf,0xd8,0xad,0xb3);


MIDL_DEFINE_GUID(IID, IID_IEnumOPCItemAttributes,0x39c13a55,0x011e,0x11d0,0x96,0x75,0x00,0x20,0xaf,0xd8,0xad,0xb3);


MIDL_DEFINE_GUID(IID, IID_IOPCDataCallback,0x39c13a70,0x011e,0x11d0,0x96,0x75,0x00,0x20,0xaf,0xd8,0xad,0xb3);


MIDL_DEFINE_GUID(IID, IID_IOPCAsyncIO2,0x39c13a71,0x011e,0x11d0,0x96,0x75,0x00,0x20,0xaf,0xd8,0xad,0xb3);


MIDL_DEFINE_GUID(IID, IID_IOPCItemProperties,0x39c13a72,0x011e,0x11d0,0x96,0x75,0x00,0x20,0xaf,0xd8,0xad,0xb3);


MIDL_DEFINE_GUID(IID, IID_IOPCItemDeadbandMgt,0x5946DA93,0x8B39,0x4ec8,0xAB,0x3D,0xAA,0x73,0xDF,0x5B,0xC8,0x6F);


MIDL_DEFINE_GUID(IID, IID_IOPCItemSamplingMgt,0x3E22D313,0xF08B,0x41a5,0x86,0xC8,0x95,0xE9,0x5C,0xB4,0x9F,0xFC);


MIDL_DEFINE_GUID(IID, IID_IOPCBrowse,0x39227004,0xA18F,0x4b57,0x8B,0x0A,0x52,0x35,0x67,0x0F,0x44,0x68);


MIDL_DEFINE_GUID(IID, IID_IOPCItemIO,0x85C0B427,0x2893,0x4cbc,0xBD,0x78,0xE5,0xFC,0x51,0x46,0xF0,0x8F);


MIDL_DEFINE_GUID(IID, IID_IOPCSyncIO2,0x730F5F0F,0x55B1,0x4c81,0x9E,0x18,0xFF,0x8A,0x09,0x04,0xE1,0xFA);


MIDL_DEFINE_GUID(IID, IID_IOPCAsyncIO3,0x0967B97B,0x36EF,0x423e,0xB6,0xF8,0x6B,0xFF,0x1E,0x40,0xD3,0x9D);


MIDL_DEFINE_GUID(IID, IID_IOPCGroupStateMgt2,0x8E368666,0xD72E,0x4f78,0x87,0xED,0x64,0x76,0x11,0xC6,0x1C,0x9F);


MIDL_DEFINE_GUID(IID, LIBID_OPCDA,0x3B540B51,0x0378,0x4551,0xAD,0xCC,0xEA,0x9B,0x10,0x43,0x02,0xBF);

#undef MIDL_DEFINE_GUID

#ifdef __cplusplus
}
#endif
