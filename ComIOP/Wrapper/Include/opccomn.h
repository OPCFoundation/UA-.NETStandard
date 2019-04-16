/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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


/* verify that the <rpcndr.h> version is high enough to compile this file*/
#ifndef __REQUIRED_RPCNDR_H_VERSION__
#define __REQUIRED_RPCNDR_H_VERSION__ 440
#endif

#include "rpc.h"
#include "rpcndr.h"

#ifndef __RPCNDR_H_VERSION__
#error this stub requires an updated version of <rpcndr.h>
#endif // __RPCNDR_H_VERSION__

#ifndef COM_NO_WINDOWS_H
#include "windows.h"
#include "ole2.h"
#endif /*COM_NO_WINDOWS_H*/

#ifndef __opccomn_h__
#define __opccomn_h__

#if defined(_MSC_VER) && (_MSC_VER >= 1020)
#pragma once
#endif

/* Forward Declarations */ 

#ifndef __IOPCShutdown_FWD_DEFINED__
#define __IOPCShutdown_FWD_DEFINED__
typedef interface IOPCShutdown IOPCShutdown;
#endif 	/* __IOPCShutdown_FWD_DEFINED__ */


#ifndef __IOPCCommon_FWD_DEFINED__
#define __IOPCCommon_FWD_DEFINED__
typedef interface IOPCCommon IOPCCommon;
#endif 	/* __IOPCCommon_FWD_DEFINED__ */


#ifndef __IOPCServerList_FWD_DEFINED__
#define __IOPCServerList_FWD_DEFINED__
typedef interface IOPCServerList IOPCServerList;
#endif 	/* __IOPCServerList_FWD_DEFINED__ */


#ifndef __IOPCEnumGUID_FWD_DEFINED__
#define __IOPCEnumGUID_FWD_DEFINED__
typedef interface IOPCEnumGUID IOPCEnumGUID;
#endif 	/* __IOPCEnumGUID_FWD_DEFINED__ */


#ifndef __IOPCServerList2_FWD_DEFINED__
#define __IOPCServerList2_FWD_DEFINED__
typedef interface IOPCServerList2 IOPCServerList2;
#endif 	/* __IOPCServerList2_FWD_DEFINED__ */


#ifndef __IOPCCommon_FWD_DEFINED__
#define __IOPCCommon_FWD_DEFINED__
typedef interface IOPCCommon IOPCCommon;
#endif 	/* __IOPCCommon_FWD_DEFINED__ */


#ifndef __IOPCShutdown_FWD_DEFINED__
#define __IOPCShutdown_FWD_DEFINED__
typedef interface IOPCShutdown IOPCShutdown;
#endif 	/* __IOPCShutdown_FWD_DEFINED__ */


#ifndef __IOPCServerList_FWD_DEFINED__
#define __IOPCServerList_FWD_DEFINED__
typedef interface IOPCServerList IOPCServerList;
#endif 	/* __IOPCServerList_FWD_DEFINED__ */


#ifndef __IOPCServerList2_FWD_DEFINED__
#define __IOPCServerList2_FWD_DEFINED__
typedef interface IOPCServerList2 IOPCServerList2;
#endif 	/* __IOPCServerList2_FWD_DEFINED__ */


#ifndef __IOPCEnumGUID_FWD_DEFINED__
#define __IOPCEnumGUID_FWD_DEFINED__
typedef interface IOPCEnumGUID IOPCEnumGUID;
#endif 	/* __IOPCEnumGUID_FWD_DEFINED__ */


/* header files for imported files */
#include "comcat.h"
#include "oaidl.h"
#include "ocidl.h"

#ifdef __cplusplus
extern "C"{
#endif 

void * __RPC_USER MIDL_user_allocate(size_t);
void __RPC_USER MIDL_user_free( void * ); 

#ifndef __IOPCShutdown_INTERFACE_DEFINED__
#define __IOPCShutdown_INTERFACE_DEFINED__

/* interface IOPCShutdown */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCShutdown;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("F31DFDE1-07B6-11d2-B2D8-0060083BA1FB")
    IOPCShutdown : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE ShutdownRequest( 
            /* [string][in] */ LPCWSTR szReason) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCShutdownVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCShutdown * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCShutdown * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCShutdown * This);
        
        HRESULT ( STDMETHODCALLTYPE *ShutdownRequest )( 
            IOPCShutdown * This,
            /* [string][in] */ LPCWSTR szReason);
        
        END_INTERFACE
    } IOPCShutdownVtbl;

    interface IOPCShutdown
    {
        CONST_VTBL struct IOPCShutdownVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCShutdown_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCShutdown_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCShutdown_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCShutdown_ShutdownRequest(This,szReason)	\
    (This)->lpVtbl -> ShutdownRequest(This,szReason)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCShutdown_ShutdownRequest_Proxy( 
    IOPCShutdown * This,
    /* [string][in] */ LPCWSTR szReason);


void __RPC_STUB IOPCShutdown_ShutdownRequest_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCShutdown_INTERFACE_DEFINED__ */


#ifndef __IOPCCommon_INTERFACE_DEFINED__
#define __IOPCCommon_INTERFACE_DEFINED__

/* interface IOPCCommon */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCCommon;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("F31DFDE2-07B6-11d2-B2D8-0060083BA1FB")
    IOPCCommon : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE SetLocaleID( 
            /* [in] */ LCID dwLcid) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetLocaleID( 
            /* [out] */ LCID *pdwLcid) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE QueryAvailableLocaleIDs( 
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ LCID **pdwLcid) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetErrorString( 
            /* [in] */ HRESULT dwError,
            /* [string][out] */ LPWSTR *ppString) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE SetClientName( 
            /* [string][in] */ LPCWSTR szName) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCCommonVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCCommon * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCCommon * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCCommon * This);
        
        HRESULT ( STDMETHODCALLTYPE *SetLocaleID )( 
            IOPCCommon * This,
            /* [in] */ LCID dwLcid);
        
        HRESULT ( STDMETHODCALLTYPE *GetLocaleID )( 
            IOPCCommon * This,
            /* [out] */ LCID *pdwLcid);
        
        HRESULT ( STDMETHODCALLTYPE *QueryAvailableLocaleIDs )( 
            IOPCCommon * This,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ LCID **pdwLcid);
        
        HRESULT ( STDMETHODCALLTYPE *GetErrorString )( 
            IOPCCommon * This,
            /* [in] */ HRESULT dwError,
            /* [string][out] */ LPWSTR *ppString);
        
        HRESULT ( STDMETHODCALLTYPE *SetClientName )( 
            IOPCCommon * This,
            /* [string][in] */ LPCWSTR szName);
        
        END_INTERFACE
    } IOPCCommonVtbl;

    interface IOPCCommon
    {
        CONST_VTBL struct IOPCCommonVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCCommon_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCCommon_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCCommon_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCCommon_SetLocaleID(This,dwLcid)	\
    (This)->lpVtbl -> SetLocaleID(This,dwLcid)

#define IOPCCommon_GetLocaleID(This,pdwLcid)	\
    (This)->lpVtbl -> GetLocaleID(This,pdwLcid)

#define IOPCCommon_QueryAvailableLocaleIDs(This,pdwCount,pdwLcid)	\
    (This)->lpVtbl -> QueryAvailableLocaleIDs(This,pdwCount,pdwLcid)

#define IOPCCommon_GetErrorString(This,dwError,ppString)	\
    (This)->lpVtbl -> GetErrorString(This,dwError,ppString)

#define IOPCCommon_SetClientName(This,szName)	\
    (This)->lpVtbl -> SetClientName(This,szName)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCCommon_SetLocaleID_Proxy( 
    IOPCCommon * This,
    /* [in] */ LCID dwLcid);


void __RPC_STUB IOPCCommon_SetLocaleID_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCCommon_GetLocaleID_Proxy( 
    IOPCCommon * This,
    /* [out] */ LCID *pdwLcid);


void __RPC_STUB IOPCCommon_GetLocaleID_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCCommon_QueryAvailableLocaleIDs_Proxy( 
    IOPCCommon * This,
    /* [out] */ DWORD *pdwCount,
    /* [size_is][size_is][out] */ LCID **pdwLcid);


void __RPC_STUB IOPCCommon_QueryAvailableLocaleIDs_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCCommon_GetErrorString_Proxy( 
    IOPCCommon * This,
    /* [in] */ HRESULT dwError,
    /* [string][out] */ LPWSTR *ppString);


void __RPC_STUB IOPCCommon_GetErrorString_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCCommon_SetClientName_Proxy( 
    IOPCCommon * This,
    /* [string][in] */ LPCWSTR szName);


void __RPC_STUB IOPCCommon_SetClientName_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCCommon_INTERFACE_DEFINED__ */


#ifndef __IOPCServerList_INTERFACE_DEFINED__
#define __IOPCServerList_INTERFACE_DEFINED__

/* interface IOPCServerList */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCServerList;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("13486D50-4821-11D2-A494-3CB306C10000")
    IOPCServerList : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE EnumClassesOfCategories( 
            /* [in] */ ULONG cImplemented,
            /* [size_is][in] */ CATID rgcatidImpl[  ],
            /* [in] */ ULONG cRequired,
            /* [size_is][in] */ CATID rgcatidReq[  ],
            /* [out] */ IEnumGUID **ppenumClsid) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetClassDetails( 
            /* [in] */ REFCLSID clsid,
            /* [out] */ LPOLESTR *ppszProgID,
            /* [out] */ LPOLESTR *ppszUserType) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE CLSIDFromProgID( 
            /* [in] */ LPCOLESTR szProgId,
            /* [out] */ LPCLSID clsid) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCServerListVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCServerList * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCServerList * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCServerList * This);
        
        HRESULT ( STDMETHODCALLTYPE *EnumClassesOfCategories )( 
            IOPCServerList * This,
            /* [in] */ ULONG cImplemented,
            /* [size_is][in] */ CATID rgcatidImpl[  ],
            /* [in] */ ULONG cRequired,
            /* [size_is][in] */ CATID rgcatidReq[  ],
            /* [out] */ IEnumGUID **ppenumClsid);
        
        HRESULT ( STDMETHODCALLTYPE *GetClassDetails )( 
            IOPCServerList * This,
            /* [in] */ REFCLSID clsid,
            /* [out] */ LPOLESTR *ppszProgID,
            /* [out] */ LPOLESTR *ppszUserType);
        
        HRESULT ( STDMETHODCALLTYPE *CLSIDFromProgID )( 
            IOPCServerList * This,
            /* [in] */ LPCOLESTR szProgId,
            /* [out] */ LPCLSID clsid);
        
        END_INTERFACE
    } IOPCServerListVtbl;

    interface IOPCServerList
    {
        CONST_VTBL struct IOPCServerListVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCServerList_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCServerList_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCServerList_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCServerList_EnumClassesOfCategories(This,cImplemented,rgcatidImpl,cRequired,rgcatidReq,ppenumClsid)	\
    (This)->lpVtbl -> EnumClassesOfCategories(This,cImplemented,rgcatidImpl,cRequired,rgcatidReq,ppenumClsid)

#define IOPCServerList_GetClassDetails(This,clsid,ppszProgID,ppszUserType)	\
    (This)->lpVtbl -> GetClassDetails(This,clsid,ppszProgID,ppszUserType)

#define IOPCServerList_CLSIDFromProgID(This,szProgId,clsid)	\
    (This)->lpVtbl -> CLSIDFromProgID(This,szProgId,clsid)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCServerList_EnumClassesOfCategories_Proxy( 
    IOPCServerList * This,
    /* [in] */ ULONG cImplemented,
    /* [size_is][in] */ CATID rgcatidImpl[  ],
    /* [in] */ ULONG cRequired,
    /* [size_is][in] */ CATID rgcatidReq[  ],
    /* [out] */ IEnumGUID **ppenumClsid);


void __RPC_STUB IOPCServerList_EnumClassesOfCategories_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCServerList_GetClassDetails_Proxy( 
    IOPCServerList * This,
    /* [in] */ REFCLSID clsid,
    /* [out] */ LPOLESTR *ppszProgID,
    /* [out] */ LPOLESTR *ppszUserType);


void __RPC_STUB IOPCServerList_GetClassDetails_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCServerList_CLSIDFromProgID_Proxy( 
    IOPCServerList * This,
    /* [in] */ LPCOLESTR szProgId,
    /* [out] */ LPCLSID clsid);


void __RPC_STUB IOPCServerList_CLSIDFromProgID_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCServerList_INTERFACE_DEFINED__ */


#ifndef __IOPCEnumGUID_INTERFACE_DEFINED__
#define __IOPCEnumGUID_INTERFACE_DEFINED__

/* interface IOPCEnumGUID */
/* [unique][uuid][object] */ 

typedef /* [unique] */ IOPCEnumGUID *LPOPCENUMGUID;


EXTERN_C const IID IID_IOPCEnumGUID;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("55C382C8-21C7-4e88-96C1-BECFB1E3F483")
    IOPCEnumGUID : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE Next( 
            /* [in] */ ULONG celt,
            /* [length_is][size_is][out] */ GUID *rgelt,
            /* [out] */ ULONG *pceltFetched) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Skip( 
            /* [in] */ ULONG celt) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Reset( void) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Clone( 
            /* [out] */ IOPCEnumGUID **ppenum) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCEnumGUIDVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCEnumGUID * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCEnumGUID * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCEnumGUID * This);
        
        HRESULT ( STDMETHODCALLTYPE *Next )( 
            IOPCEnumGUID * This,
            /* [in] */ ULONG celt,
            /* [length_is][size_is][out] */ GUID *rgelt,
            /* [out] */ ULONG *pceltFetched);
        
        HRESULT ( STDMETHODCALLTYPE *Skip )( 
            IOPCEnumGUID * This,
            /* [in] */ ULONG celt);
        
        HRESULT ( STDMETHODCALLTYPE *Reset )( 
            IOPCEnumGUID * This);
        
        HRESULT ( STDMETHODCALLTYPE *Clone )( 
            IOPCEnumGUID * This,
            /* [out] */ IOPCEnumGUID **ppenum);
        
        END_INTERFACE
    } IOPCEnumGUIDVtbl;

    interface IOPCEnumGUID
    {
        CONST_VTBL struct IOPCEnumGUIDVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCEnumGUID_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCEnumGUID_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCEnumGUID_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCEnumGUID_Next(This,celt,rgelt,pceltFetched)	\
    (This)->lpVtbl -> Next(This,celt,rgelt,pceltFetched)

#define IOPCEnumGUID_Skip(This,celt)	\
    (This)->lpVtbl -> Skip(This,celt)

#define IOPCEnumGUID_Reset(This)	\
    (This)->lpVtbl -> Reset(This)

#define IOPCEnumGUID_Clone(This,ppenum)	\
    (This)->lpVtbl -> Clone(This,ppenum)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCEnumGUID_Next_Proxy( 
    IOPCEnumGUID * This,
    /* [in] */ ULONG celt,
    /* [length_is][size_is][out] */ GUID *rgelt,
    /* [out] */ ULONG *pceltFetched);


void __RPC_STUB IOPCEnumGUID_Next_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEnumGUID_Skip_Proxy( 
    IOPCEnumGUID * This,
    /* [in] */ ULONG celt);


void __RPC_STUB IOPCEnumGUID_Skip_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEnumGUID_Reset_Proxy( 
    IOPCEnumGUID * This);


void __RPC_STUB IOPCEnumGUID_Reset_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEnumGUID_Clone_Proxy( 
    IOPCEnumGUID * This,
    /* [out] */ IOPCEnumGUID **ppenum);


void __RPC_STUB IOPCEnumGUID_Clone_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCEnumGUID_INTERFACE_DEFINED__ */


#ifndef __IOPCServerList2_INTERFACE_DEFINED__
#define __IOPCServerList2_INTERFACE_DEFINED__

/* interface IOPCServerList2 */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCServerList2;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("9DD0B56C-AD9E-43ee-8305-487F3188BF7A")
    IOPCServerList2 : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE EnumClassesOfCategories( 
            /* [in] */ ULONG cImplemented,
            /* [size_is][in] */ CATID rgcatidImpl[  ],
            /* [in] */ ULONG cRequired,
            /* [size_is][in] */ CATID rgcatidReq[  ],
            /* [out] */ IOPCEnumGUID **ppenumClsid) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetClassDetails( 
            /* [in] */ REFCLSID clsid,
            /* [out] */ LPOLESTR *ppszProgID,
            /* [out] */ LPOLESTR *ppszUserType,
            /* [out] */ LPOLESTR *ppszVerIndProgID) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE CLSIDFromProgID( 
            /* [in] */ LPCOLESTR szProgId,
            /* [out] */ LPCLSID clsid) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCServerList2Vtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCServerList2 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCServerList2 * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCServerList2 * This);
        
        HRESULT ( STDMETHODCALLTYPE *EnumClassesOfCategories )( 
            IOPCServerList2 * This,
            /* [in] */ ULONG cImplemented,
            /* [size_is][in] */ CATID rgcatidImpl[  ],
            /* [in] */ ULONG cRequired,
            /* [size_is][in] */ CATID rgcatidReq[  ],
            /* [out] */ IOPCEnumGUID **ppenumClsid);
        
        HRESULT ( STDMETHODCALLTYPE *GetClassDetails )( 
            IOPCServerList2 * This,
            /* [in] */ REFCLSID clsid,
            /* [out] */ LPOLESTR *ppszProgID,
            /* [out] */ LPOLESTR *ppszUserType,
            /* [out] */ LPOLESTR *ppszVerIndProgID);
        
        HRESULT ( STDMETHODCALLTYPE *CLSIDFromProgID )( 
            IOPCServerList2 * This,
            /* [in] */ LPCOLESTR szProgId,
            /* [out] */ LPCLSID clsid);
        
        END_INTERFACE
    } IOPCServerList2Vtbl;

    interface IOPCServerList2
    {
        CONST_VTBL struct IOPCServerList2Vtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCServerList2_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCServerList2_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCServerList2_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCServerList2_EnumClassesOfCategories(This,cImplemented,rgcatidImpl,cRequired,rgcatidReq,ppenumClsid)	\
    (This)->lpVtbl -> EnumClassesOfCategories(This,cImplemented,rgcatidImpl,cRequired,rgcatidReq,ppenumClsid)

#define IOPCServerList2_GetClassDetails(This,clsid,ppszProgID,ppszUserType,ppszVerIndProgID)	\
    (This)->lpVtbl -> GetClassDetails(This,clsid,ppszProgID,ppszUserType,ppszVerIndProgID)

#define IOPCServerList2_CLSIDFromProgID(This,szProgId,clsid)	\
    (This)->lpVtbl -> CLSIDFromProgID(This,szProgId,clsid)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCServerList2_EnumClassesOfCategories_Proxy( 
    IOPCServerList2 * This,
    /* [in] */ ULONG cImplemented,
    /* [size_is][in] */ CATID rgcatidImpl[  ],
    /* [in] */ ULONG cRequired,
    /* [size_is][in] */ CATID rgcatidReq[  ],
    /* [out] */ IOPCEnumGUID **ppenumClsid);


void __RPC_STUB IOPCServerList2_EnumClassesOfCategories_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCServerList2_GetClassDetails_Proxy( 
    IOPCServerList2 * This,
    /* [in] */ REFCLSID clsid,
    /* [out] */ LPOLESTR *ppszProgID,
    /* [out] */ LPOLESTR *ppszUserType,
    /* [out] */ LPOLESTR *ppszVerIndProgID);


void __RPC_STUB IOPCServerList2_GetClassDetails_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCServerList2_CLSIDFromProgID_Proxy( 
    IOPCServerList2 * This,
    /* [in] */ LPCOLESTR szProgId,
    /* [out] */ LPCLSID clsid);


void __RPC_STUB IOPCServerList2_CLSIDFromProgID_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCServerList2_INTERFACE_DEFINED__ */



#ifndef __OPCCOMN_LIBRARY_DEFINED__
#define __OPCCOMN_LIBRARY_DEFINED__

/* library OPCCOMN */
/* [helpstring][version][uuid] */ 












EXTERN_C const IID LIBID_OPCCOMN;
#endif /* __OPCCOMN_LIBRARY_DEFINED__ */

/* Additional Prototypes for ALL interfaces */

/* end of Additional Prototypes */

#ifdef __cplusplus
}
#endif

#endif
