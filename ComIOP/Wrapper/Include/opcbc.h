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

#ifndef __opcbc_h__
#define __opcbc_h__

#if defined(_MSC_VER) && (_MSC_VER >= 1020)
#pragma once
#endif

/* Forward Declarations */ 

#ifndef __CATID_OPCBatchServer10_FWD_DEFINED__
#define __CATID_OPCBatchServer10_FWD_DEFINED__
typedef interface CATID_OPCBatchServer10 CATID_OPCBatchServer10;
#endif 	/* __CATID_OPCBatchServer10_FWD_DEFINED__ */


#ifndef __CATID_OPCBatchServer20_FWD_DEFINED__
#define __CATID_OPCBatchServer20_FWD_DEFINED__
typedef interface CATID_OPCBatchServer20 CATID_OPCBatchServer20;
#endif 	/* __CATID_OPCBatchServer20_FWD_DEFINED__ */


#ifndef __IOPCBatchServer_FWD_DEFINED__
#define __IOPCBatchServer_FWD_DEFINED__
typedef interface IOPCBatchServer IOPCBatchServer;
#endif 	/* __IOPCBatchServer_FWD_DEFINED__ */


#ifndef __IOPCBatchServer2_FWD_DEFINED__
#define __IOPCBatchServer2_FWD_DEFINED__
typedef interface IOPCBatchServer2 IOPCBatchServer2;
#endif 	/* __IOPCBatchServer2_FWD_DEFINED__ */


#ifndef __IEnumOPCBatchSummary_FWD_DEFINED__
#define __IEnumOPCBatchSummary_FWD_DEFINED__
typedef interface IEnumOPCBatchSummary IEnumOPCBatchSummary;
#endif 	/* __IEnumOPCBatchSummary_FWD_DEFINED__ */


#ifndef __IOPCEnumerationSets_FWD_DEFINED__
#define __IOPCEnumerationSets_FWD_DEFINED__
typedef interface IOPCEnumerationSets IOPCEnumerationSets;
#endif 	/* __IOPCEnumerationSets_FWD_DEFINED__ */


#ifndef __CATID_OPCBatchServer10_FWD_DEFINED__
#define __CATID_OPCBatchServer10_FWD_DEFINED__
typedef interface CATID_OPCBatchServer10 CATID_OPCBatchServer10;
#endif 	/* __CATID_OPCBatchServer10_FWD_DEFINED__ */


#ifndef __CATID_OPCBatchServer20_FWD_DEFINED__
#define __CATID_OPCBatchServer20_FWD_DEFINED__
typedef interface CATID_OPCBatchServer20 CATID_OPCBatchServer20;
#endif 	/* __CATID_OPCBatchServer20_FWD_DEFINED__ */


#ifndef __IOPCBatchServer_FWD_DEFINED__
#define __IOPCBatchServer_FWD_DEFINED__
typedef interface IOPCBatchServer IOPCBatchServer;
#endif 	/* __IOPCBatchServer_FWD_DEFINED__ */


#ifndef __IOPCBatchServer2_FWD_DEFINED__
#define __IOPCBatchServer2_FWD_DEFINED__
typedef interface IOPCBatchServer2 IOPCBatchServer2;
#endif 	/* __IOPCBatchServer2_FWD_DEFINED__ */


#ifndef __IEnumOPCBatchSummary_FWD_DEFINED__
#define __IEnumOPCBatchSummary_FWD_DEFINED__
typedef interface IEnumOPCBatchSummary IEnumOPCBatchSummary;
#endif 	/* __IEnumOPCBatchSummary_FWD_DEFINED__ */


#ifndef __IOPCEnumerationSets_FWD_DEFINED__
#define __IOPCEnumerationSets_FWD_DEFINED__
typedef interface IOPCEnumerationSets IOPCEnumerationSets;
#endif 	/* __IOPCEnumerationSets_FWD_DEFINED__ */


/* header files for imported files */
#include "oaidl.h"

#ifdef __cplusplus
extern "C"{
#endif 

void * __RPC_USER MIDL_user_allocate(size_t);
void __RPC_USER MIDL_user_free( void * ); 

#ifndef __CATID_OPCBatchServer10_INTERFACE_DEFINED__
#define __CATID_OPCBatchServer10_INTERFACE_DEFINED__

/* interface CATID_OPCBatchServer10 */
/* [object][uuid] */ 


EXTERN_C const IID IID_CATID_OPCBatchServer10;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("A8080DA0-E23E-11D2-AFA7-00C04F539421")
    CATID_OPCBatchServer10 : public IUnknown
    {
    public:
    };
    
#else 	/* C style interface */

    typedef struct CATID_OPCBatchServer10Vtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            CATID_OPCBatchServer10 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            CATID_OPCBatchServer10 * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            CATID_OPCBatchServer10 * This);
        
        END_INTERFACE
    } CATID_OPCBatchServer10Vtbl;

    interface CATID_OPCBatchServer10
    {
        CONST_VTBL struct CATID_OPCBatchServer10Vtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define CATID_OPCBatchServer10_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define CATID_OPCBatchServer10_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define CATID_OPCBatchServer10_Release(This)	\
    (This)->lpVtbl -> Release(This)


#endif /* COBJMACROS */


#endif 	/* C style interface */




#endif 	/* __CATID_OPCBatchServer10_INTERFACE_DEFINED__ */


#ifndef __CATID_OPCBatchServer20_INTERFACE_DEFINED__
#define __CATID_OPCBatchServer20_INTERFACE_DEFINED__

/* interface CATID_OPCBatchServer20 */
/* [object][uuid] */ 


EXTERN_C const IID IID_CATID_OPCBatchServer20;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("843DE67B-B0C9-11d4-A0B7-000102A980B1")
    CATID_OPCBatchServer20 : public IUnknown
    {
    public:
    };
    
#else 	/* C style interface */

    typedef struct CATID_OPCBatchServer20Vtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            CATID_OPCBatchServer20 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            CATID_OPCBatchServer20 * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            CATID_OPCBatchServer20 * This);
        
        END_INTERFACE
    } CATID_OPCBatchServer20Vtbl;

    interface CATID_OPCBatchServer20
    {
        CONST_VTBL struct CATID_OPCBatchServer20Vtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define CATID_OPCBatchServer20_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define CATID_OPCBatchServer20_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define CATID_OPCBatchServer20_Release(This)	\
    (This)->lpVtbl -> Release(This)


#endif /* COBJMACROS */


#endif 	/* C style interface */




#endif 	/* __CATID_OPCBatchServer20_INTERFACE_DEFINED__ */


/* interface __MIDL_itf_opcbc_0116 */
/* [local] */ 

#define CATID_OPCBatchServer10 IID_CATID_OPCBatchServer10
#define CATID_OPCBatchServer20 IID_CATID_OPCBatchServer20
typedef struct tagOPCBATCHSUMMARY
    {
    /* [string] */ LPWSTR szID;
    /* [string] */ LPWSTR szDescription;
    /* [string] */ LPWSTR szOPCItemID;
    /* [string] */ LPWSTR szMasterRecipeID;
    FLOAT fBatchSize;
    /* [string] */ LPWSTR szEU;
    /* [string] */ LPWSTR szExecutionState;
    /* [string] */ LPWSTR szExecutionMode;
    FILETIME ftActualStartTime;
    FILETIME ftActualEndTime;
    } 	OPCBATCHSUMMARY;

typedef struct tagOPCBATCHSUMMARYFILTER
    {
    /* [string] */ LPWSTR szID;
    /* [string] */ LPWSTR szDescription;
    /* [string] */ LPWSTR szOPCItemID;
    /* [string] */ LPWSTR szMasterRecipeID;
    FLOAT fMinBatchSize;
    FLOAT fMaxBatchSize;
    /* [string] */ LPWSTR szEU;
    /* [string] */ LPWSTR szExecutionState;
    /* [string] */ LPWSTR szExecutionMode;
    FILETIME ftMinStartTime;
    FILETIME ftMaxStartTime;
    FILETIME ftMinEndTime;
    FILETIME ftMaxEndTime;
    } 	OPCBATCHSUMMARYFILTER;



extern RPC_IF_HANDLE __MIDL_itf_opcbc_0116_v0_0_c_ifspec;
extern RPC_IF_HANDLE __MIDL_itf_opcbc_0116_v0_0_s_ifspec;

#ifndef __IOPCBatchServer_INTERFACE_DEFINED__
#define __IOPCBatchServer_INTERFACE_DEFINED__

/* interface IOPCBatchServer */
/* [object][unique][uuid] */ 


EXTERN_C const IID IID_IOPCBatchServer;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("8BB4ED50-B314-11d3-B3EA-00C04F8ECEAA")
    IOPCBatchServer : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE GetDelimiter( 
            /* [string][out] */ LPWSTR *pszDelimiter) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE CreateEnumerator( 
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCBatchServerVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCBatchServer * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCBatchServer * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCBatchServer * This);
        
        HRESULT ( STDMETHODCALLTYPE *GetDelimiter )( 
            IOPCBatchServer * This,
            /* [string][out] */ LPWSTR *pszDelimiter);
        
        HRESULT ( STDMETHODCALLTYPE *CreateEnumerator )( 
            IOPCBatchServer * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk);
        
        END_INTERFACE
    } IOPCBatchServerVtbl;

    interface IOPCBatchServer
    {
        CONST_VTBL struct IOPCBatchServerVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCBatchServer_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCBatchServer_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCBatchServer_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCBatchServer_GetDelimiter(This,pszDelimiter)	\
    (This)->lpVtbl -> GetDelimiter(This,pszDelimiter)

#define IOPCBatchServer_CreateEnumerator(This,riid,ppUnk)	\
    (This)->lpVtbl -> CreateEnumerator(This,riid,ppUnk)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCBatchServer_GetDelimiter_Proxy( 
    IOPCBatchServer * This,
    /* [string][out] */ LPWSTR *pszDelimiter);


void __RPC_STUB IOPCBatchServer_GetDelimiter_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCBatchServer_CreateEnumerator_Proxy( 
    IOPCBatchServer * This,
    /* [in] */ REFIID riid,
    /* [iid_is][out] */ LPUNKNOWN *ppUnk);


void __RPC_STUB IOPCBatchServer_CreateEnumerator_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCBatchServer_INTERFACE_DEFINED__ */


#ifndef __IOPCBatchServer2_INTERFACE_DEFINED__
#define __IOPCBatchServer2_INTERFACE_DEFINED__

/* interface IOPCBatchServer2 */
/* [object][unique][uuid] */ 


EXTERN_C const IID IID_IOPCBatchServer2;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("895A78CF-B0C5-11d4-A0B7-000102A980B1")
    IOPCBatchServer2 : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE CreateFilteredEnumerator( 
            /* [in] */ REFIID riid,
            /* [full][in] */ OPCBATCHSUMMARYFILTER *pFilter,
            /* [string][in] */ LPCWSTR szModel,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCBatchServer2Vtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCBatchServer2 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCBatchServer2 * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCBatchServer2 * This);
        
        HRESULT ( STDMETHODCALLTYPE *CreateFilteredEnumerator )( 
            IOPCBatchServer2 * This,
            /* [in] */ REFIID riid,
            /* [full][in] */ OPCBATCHSUMMARYFILTER *pFilter,
            /* [string][in] */ LPCWSTR szModel,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk);
        
        END_INTERFACE
    } IOPCBatchServer2Vtbl;

    interface IOPCBatchServer2
    {
        CONST_VTBL struct IOPCBatchServer2Vtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCBatchServer2_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCBatchServer2_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCBatchServer2_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCBatchServer2_CreateFilteredEnumerator(This,riid,pFilter,szModel,ppUnk)	\
    (This)->lpVtbl -> CreateFilteredEnumerator(This,riid,pFilter,szModel,ppUnk)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCBatchServer2_CreateFilteredEnumerator_Proxy( 
    IOPCBatchServer2 * This,
    /* [in] */ REFIID riid,
    /* [full][in] */ OPCBATCHSUMMARYFILTER *pFilter,
    /* [string][in] */ LPCWSTR szModel,
    /* [iid_is][out] */ LPUNKNOWN *ppUnk);


void __RPC_STUB IOPCBatchServer2_CreateFilteredEnumerator_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCBatchServer2_INTERFACE_DEFINED__ */


#ifndef __IEnumOPCBatchSummary_INTERFACE_DEFINED__
#define __IEnumOPCBatchSummary_INTERFACE_DEFINED__

/* interface IEnumOPCBatchSummary */
/* [object][unique][uuid] */ 


EXTERN_C const IID IID_IEnumOPCBatchSummary;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("a8080da2-e23e-11d2-afa7-00c04f539421")
    IEnumOPCBatchSummary : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE Next( 
            /* [in] */ ULONG celt,
            /* [size_is][size_is][out] */ OPCBATCHSUMMARY **ppSummaryArray,
            /* [out] */ ULONG *pceltFetched) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Skip( 
            /* [in] */ ULONG celt) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Reset( void) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Clone( 
            /* [out] */ IEnumOPCBatchSummary **ppEnumBatchSummary) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Count( 
            /* [out] */ ULONG *pcelt) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IEnumOPCBatchSummaryVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IEnumOPCBatchSummary * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IEnumOPCBatchSummary * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IEnumOPCBatchSummary * This);
        
        HRESULT ( STDMETHODCALLTYPE *Next )( 
            IEnumOPCBatchSummary * This,
            /* [in] */ ULONG celt,
            /* [size_is][size_is][out] */ OPCBATCHSUMMARY **ppSummaryArray,
            /* [out] */ ULONG *pceltFetched);
        
        HRESULT ( STDMETHODCALLTYPE *Skip )( 
            IEnumOPCBatchSummary * This,
            /* [in] */ ULONG celt);
        
        HRESULT ( STDMETHODCALLTYPE *Reset )( 
            IEnumOPCBatchSummary * This);
        
        HRESULT ( STDMETHODCALLTYPE *Clone )( 
            IEnumOPCBatchSummary * This,
            /* [out] */ IEnumOPCBatchSummary **ppEnumBatchSummary);
        
        HRESULT ( STDMETHODCALLTYPE *Count )( 
            IEnumOPCBatchSummary * This,
            /* [out] */ ULONG *pcelt);
        
        END_INTERFACE
    } IEnumOPCBatchSummaryVtbl;

    interface IEnumOPCBatchSummary
    {
        CONST_VTBL struct IEnumOPCBatchSummaryVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IEnumOPCBatchSummary_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IEnumOPCBatchSummary_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IEnumOPCBatchSummary_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IEnumOPCBatchSummary_Next(This,celt,ppSummaryArray,pceltFetched)	\
    (This)->lpVtbl -> Next(This,celt,ppSummaryArray,pceltFetched)

#define IEnumOPCBatchSummary_Skip(This,celt)	\
    (This)->lpVtbl -> Skip(This,celt)

#define IEnumOPCBatchSummary_Reset(This)	\
    (This)->lpVtbl -> Reset(This)

#define IEnumOPCBatchSummary_Clone(This,ppEnumBatchSummary)	\
    (This)->lpVtbl -> Clone(This,ppEnumBatchSummary)

#define IEnumOPCBatchSummary_Count(This,pcelt)	\
    (This)->lpVtbl -> Count(This,pcelt)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IEnumOPCBatchSummary_Next_Proxy( 
    IEnumOPCBatchSummary * This,
    /* [in] */ ULONG celt,
    /* [size_is][size_is][out] */ OPCBATCHSUMMARY **ppSummaryArray,
    /* [out] */ ULONG *pceltFetched);


void __RPC_STUB IEnumOPCBatchSummary_Next_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IEnumOPCBatchSummary_Skip_Proxy( 
    IEnumOPCBatchSummary * This,
    /* [in] */ ULONG celt);


void __RPC_STUB IEnumOPCBatchSummary_Skip_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IEnumOPCBatchSummary_Reset_Proxy( 
    IEnumOPCBatchSummary * This);


void __RPC_STUB IEnumOPCBatchSummary_Reset_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IEnumOPCBatchSummary_Clone_Proxy( 
    IEnumOPCBatchSummary * This,
    /* [out] */ IEnumOPCBatchSummary **ppEnumBatchSummary);


void __RPC_STUB IEnumOPCBatchSummary_Clone_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IEnumOPCBatchSummary_Count_Proxy( 
    IEnumOPCBatchSummary * This,
    /* [out] */ ULONG *pcelt);


void __RPC_STUB IEnumOPCBatchSummary_Count_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IEnumOPCBatchSummary_INTERFACE_DEFINED__ */


#ifndef __IOPCEnumerationSets_INTERFACE_DEFINED__
#define __IOPCEnumerationSets_INTERFACE_DEFINED__

/* interface IOPCEnumerationSets */
/* [object][unique][uuid] */ 


EXTERN_C const IID IID_IOPCEnumerationSets;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("a8080da3-e23e-11d2-afa7-00c04f539421")
    IOPCEnumerationSets : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE QueryEnumerationSets( 
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppdwEnumSetId,
            /* [size_is][size_is][string][out] */ LPWSTR **ppszEnumSetName) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE QueryEnumeration( 
            /* [in] */ DWORD dwEnumSetId,
            /* [in] */ DWORD dwEnumValue,
            /* [string][out] */ LPWSTR *pszEnumName) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE QueryEnumerationList( 
            /* [in] */ DWORD dwEnumSetId,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppdwEnumValue,
            /* [size_is][size_is][string][out] */ LPWSTR **ppszEnumName) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCEnumerationSetsVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCEnumerationSets * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCEnumerationSets * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCEnumerationSets * This);
        
        HRESULT ( STDMETHODCALLTYPE *QueryEnumerationSets )( 
            IOPCEnumerationSets * This,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppdwEnumSetId,
            /* [size_is][size_is][string][out] */ LPWSTR **ppszEnumSetName);
        
        HRESULT ( STDMETHODCALLTYPE *QueryEnumeration )( 
            IOPCEnumerationSets * This,
            /* [in] */ DWORD dwEnumSetId,
            /* [in] */ DWORD dwEnumValue,
            /* [string][out] */ LPWSTR *pszEnumName);
        
        HRESULT ( STDMETHODCALLTYPE *QueryEnumerationList )( 
            IOPCEnumerationSets * This,
            /* [in] */ DWORD dwEnumSetId,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppdwEnumValue,
            /* [size_is][size_is][string][out] */ LPWSTR **ppszEnumName);
        
        END_INTERFACE
    } IOPCEnumerationSetsVtbl;

    interface IOPCEnumerationSets
    {
        CONST_VTBL struct IOPCEnumerationSetsVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCEnumerationSets_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCEnumerationSets_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCEnumerationSets_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCEnumerationSets_QueryEnumerationSets(This,pdwCount,ppdwEnumSetId,ppszEnumSetName)	\
    (This)->lpVtbl -> QueryEnumerationSets(This,pdwCount,ppdwEnumSetId,ppszEnumSetName)

#define IOPCEnumerationSets_QueryEnumeration(This,dwEnumSetId,dwEnumValue,pszEnumName)	\
    (This)->lpVtbl -> QueryEnumeration(This,dwEnumSetId,dwEnumValue,pszEnumName)

#define IOPCEnumerationSets_QueryEnumerationList(This,dwEnumSetId,pdwCount,ppdwEnumValue,ppszEnumName)	\
    (This)->lpVtbl -> QueryEnumerationList(This,dwEnumSetId,pdwCount,ppdwEnumValue,ppszEnumName)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCEnumerationSets_QueryEnumerationSets_Proxy( 
    IOPCEnumerationSets * This,
    /* [out] */ DWORD *pdwCount,
    /* [size_is][size_is][out] */ DWORD **ppdwEnumSetId,
    /* [size_is][size_is][string][out] */ LPWSTR **ppszEnumSetName);


void __RPC_STUB IOPCEnumerationSets_QueryEnumerationSets_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEnumerationSets_QueryEnumeration_Proxy( 
    IOPCEnumerationSets * This,
    /* [in] */ DWORD dwEnumSetId,
    /* [in] */ DWORD dwEnumValue,
    /* [string][out] */ LPWSTR *pszEnumName);


void __RPC_STUB IOPCEnumerationSets_QueryEnumeration_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEnumerationSets_QueryEnumerationList_Proxy( 
    IOPCEnumerationSets * This,
    /* [in] */ DWORD dwEnumSetId,
    /* [out] */ DWORD *pdwCount,
    /* [size_is][size_is][out] */ DWORD **ppdwEnumValue,
    /* [size_is][size_is][string][out] */ LPWSTR **ppszEnumName);


void __RPC_STUB IOPCEnumerationSets_QueryEnumerationList_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCEnumerationSets_INTERFACE_DEFINED__ */



#ifndef __OPC_BATCH_LIBRARY_DEFINED__
#define __OPC_BATCH_LIBRARY_DEFINED__

/* library OPC_BATCH */
/* [helpstring][version][uuid] */ 








EXTERN_C const IID LIBID_OPC_BATCH;


#ifndef __OPCBC_Constants_MODULE_DEFINED__
#define __OPCBC_Constants_MODULE_DEFINED__


/* module OPCBC_Constants */


const LPCWSTR OPC_CATEGORY_DESCRIPTION_BATCH10	=	L"OPC Batch Server Version 1.0";

const LPCWSTR OPC_CATEGORY_DESCRIPTION_BATCH20	=	L"OPC Batch Server Version 2.0";

#endif /* __OPCBC_Constants_MODULE_DEFINED__ */


#ifndef __OPCBC_EnumSets_MODULE_DEFINED__
#define __OPCBC_EnumSets_MODULE_DEFINED__


/* module OPCBC_EnumSets */


const DWORD OPCB_ENUM_PHYS	=	0;

const DWORD OPCB_ENUM_PROC	=	1;

const DWORD OPCB_ENUM_STATE	=	2;

const DWORD OPCB_ENUM_MODE	=	3;

const DWORD OPCB_ENUM_PARAM	=	4;

const DWORD OPCB_ENUM_MR_PROC	=	5;

const DWORD OPCB_ENUM_RE_USE	=	6;

const DWORD OPCB_PHYS_ENTERPRISE	=	0;

const DWORD OPCB_PHYS_SITE	=	1;

const DWORD OPCB_PHYS_AREA	=	2;

const DWORD OPCB_PHYS_PROCESSCELL	=	3;

const DWORD OPCB_PHYS_UNIT	=	4;

const DWORD OPCB_PHYS_EQUIPMENTMODULE	=	5;

const DWORD OPCB_PHYS_CONTROLMODULE	=	6;

const DWORD OPCB_PHYS_EPE	=	7;

const DWORD OPCB_PROC_PROCEDURE	=	0;

const DWORD OPCB_PROC_UNITPROCEDURE	=	1;

const DWORD OPCB_PROC_OPERATION	=	2;

const DWORD OPCB_PROC_PHASE	=	3;

const DWORD OPCB_PROC_PARAMETER_COLLECTION	=	4;

const DWORD OPCB_PROC_PARAMETER	=	5;

const DWORD OPCB_PROC_RESULT_COLLECTION	=	6;

const DWORD OPCB_PROC_RESULT	=	7;

const DWORD OPCB_PROC_BATCH	=	8;

const DWORD OPCB_PROC_CAMPAIGN	=	9;

const DWORD OPCB_STATE_IDLE	=	0;

const DWORD OPCB_STATE_RUNNING	=	1;

const DWORD OPCB_STATE_COMPLETE	=	2;

const DWORD OPCB_STATE_PAUSING	=	3;

const DWORD OPCB_STATE_PAUSED	=	4;

const DWORD OPCB_STATE_HOLDING	=	5;

const DWORD OPCB_STATE_HELD	=	6;

const DWORD OPCB_STATE_RESTARTING	=	7;

const DWORD OPCB_STATE_STOPPING	=	8;

const DWORD OPCB_STATE_STOPPED	=	9;

const DWORD OPCB_STATE_ABORTING	=	10;

const DWORD OPCB_STATE_ABORTED	=	11;

const DWORD OPCB_STATE_UNKNOWN	=	12;

const DWORD OPCB_MODE_AUTOMATIC	=	0;

const DWORD OPCB_MODE_SEMIAUTOMATIC	=	1;

const DWORD OPCB_MODE_MANUAL	=	2;

const DWORD OPCB_MODE_UNKNOWN	=	3;

const DWORD OPCB_PARAM_PROCESSINPUT	=	0;

const DWORD OPCB_PARAM_PROCESSPARAMETER	=	1;

const DWORD OPCB_PARAM_PROCESSOUTPUT	=	2;

const DWORD OPCB_MR_PROC_PROCEDURE	=	0;

const DWORD OPCB_MR_PROC_UNITPROCEDURE	=	1;

const DWORD OPCB_MR_PROC_OPERATION	=	2;

const DWORD OPCB_MR_PROC_PHASE	=	3;

const DWORD OPCB_MR_PARAMETER_COLLECTION	=	4;

const DWORD OPCB_MR_PARAMETER	=	5;

const DWORD OPCB_MR_RESULT_COLLECTION	=	6;

const DWORD OPCB_MR_RESULT	=	7;

const DWORD OPCB_RE_USE_INVALID	=	0;

const DWORD OPCB_RE_USE_LINKED	=	1;

const DWORD OPCB_RE_USE_EMBEDDED	=	2;

const DWORD OPCB_RE_USE_COPIED	=	3;

#endif /* __OPCBC_EnumSets_MODULE_DEFINED__ */


#ifndef __OPCBC_Properties_MODULE_DEFINED__
#define __OPCBC_Properties_MODULE_DEFINED__


/* module OPCBC_Properties */


const DWORD OPCB_PROPERTY_ID	=	400;

const DWORD OPCB_PROPERTY_VALUE	=	401;

const DWORD OPCB_PROPERTY_RIGHTS	=	402;

const DWORD OPCB_PROPERTY_EU	=	403;

const DWORD OPCB_PROPERTY_DESC	=	404;

const DWORD OPCB_PROPERTY_HIGH_VALUE_LIMIT	=	405;

const DWORD OPCB_PROPERTY_LOW_VALUE_LIMIT	=	406;

const DWORD OPCB_PROPERTY_TIME_ZONE	=	407;

const DWORD OPCB_PROPERTY_CONDITION_STATUS	=	408;

const DWORD OPCB_PROPERTY_PHYSICAL_MODEL_LEVEL	=	409;

const DWORD OPCB_PROPERTY_BATCH_MODEL_LEVEL	=	410;

const DWORD OPCB_PROPERTY_RELATED_BATCH_IDS	=	411;

const DWORD OPCB_PROPERTY_VERSION	=	412;

const DWORD OPCB_PROPERTY_EQUIPMENT_CLASS	=	413;

const DWORD OPCB_PROPERTY_LOCATION	=	414;

const DWORD OPCB_PROPERTY_MAXIMUM_USER_COUNT	=	415;

const DWORD OPCB_PROPERTY_CURRENT_USER_COUNT	=	416;

const DWORD OPCB_PROPERTY_CURRENT_USER_LIST	=	417;

const DWORD OPCB_PROPERTY_ALLOCATED_EQUIPMENT_LIST	=	418;

const DWORD OPCB_PROPERTY_REQUESTER_LIST	=	419;

const DWORD OPCB_PROPERTY_REQUESTED_LIST	=	420;

const DWORD OPCB_PROPERTY_SHARED_BY_LIST	=	421;

const DWORD OPCB_PROPERTY_EQUIPMENT_STATE	=	422;

const DWORD OPCB_PROPERTY_EQUIPMENT_MODE	=	423;

const DWORD OPCB_PROPERTY_UPSTREAM_EQUIPMENT_LIST	=	424;

const DWORD OPCB_PROPERTY_DOWNSTREAM_EQUIPMENT_LIST	=	425;

const DWORD OPCB_PROPERTY_EQUIPMENT_PROCEDURAL_ELEMENT_LIST	=	426;

const DWORD OPCB_PROPERTY_CURRENT_PROCEDURE_LIST	=	427;

const DWORD OPCB_PROPERTY_TRAIN_LIST	=	428;

const DWORD OPCB_PROPERTY_DEVICE_DATA_SOURCE	=	429;

const DWORD OPCB_PROPERTY_DEVICE_DATA_SERVER	=	430;

const DWORD OPCB_PROPERTY_CAMPAIGN_ID	=	431;

const DWORD OPCB_PROPERTY_LOT_ID_LIST	=	432;

const DWORD OPCB_PROPERTY_CONTROL_RECIPE_ID	=	433;

const DWORD OPCB_PROPERTY_CONTROL_RECIPE_VERSION	=	434;

const DWORD OPCB_PROPERTY_MASTER_RECIPE_ID	=	435;

const DWORD OPCB_PROPERTY_MASTER_RECIPE_VERSION	=	436;

const DWORD OPCB_PROPERTY_PRODUCT_ID	=	437;

const DWORD OPCB_PROPERTY_GRADE	=	438;

const DWORD OPCB_PROPERTY_BATCH_SIZE	=	439;

const DWORD OPCB_PROPERTY_PRIORITY	=	440;

const DWORD OPCB_PROPERTY_EXECUTION_STATE	=	441;

const DWORD OPCB_PROPERTY_IEC61512_1_STATE	=	442;

const DWORD OPCB_PROPERTY_EXECUTION_MODE	=	443;

const DWORD OPCB_PROPERTY_IEC61512_1_MODE	=	444;

const DWORD OPCB_PROPERTY_SCHEDULED_START_TIME	=	445;

const DWORD OPCB_PROPERTY_ACTUAL_START_TIME	=	446;

const DWORD OPCB_PROPERTY_ESTIMATED_END_TIME	=	447;

const DWORD OPCB_PROPERTY_ACTUAL_END_TIME	=	448;

const DWORD OPCB_PROPERTY_PHYSICAL_MODEL_REFERENCE	=	449;

const DWORD OPCB_PROPERTY_EQUIPMENT_PROCEDURAL_ELEMENT	=	450;

const DWORD OPCB_PROPERTY_PARAMETER_COUNT	=	451;

const DWORD OPCB_PROPERTY_PARAMETER_TYPE	=	452;

const DWORD OPCB_PROPERTY_VALID_VALUES	=	453;

const DWORD OPCB_PROPERTY_SCALING_RULE	=	454;

const DWORD OPCB_PROPERTY_EXPRESSION_RULE	=	455;

const DWORD OPCB_PROPERTY_RESULT_COUNT	=	456;

const DWORD OPCB_PROPERTY_ENUMERATION_SET_ID	=	457;

const DWORD OPCB_PROPERTY_MASTER_RECIPE_MODEL_LEVEL	=	458;

const DWORD OPCB_PROPERTY_PROCEDURE_LOGIC	=	459;

const DWORD OPCB_PROPERTY_PROCEDURE_LOGIC_SCHEMA	=	460;

const DWORD OPCB_PROPERTY_EQUIPMENT_CANDIDATE_LIST	=	461;

const DWORD OPCB_PROPERTY_EQUIPMENT_CLASS_CANDIDATE_LIST	=	462;

const DWORD OPCB_PROPERTY_VERSION_DATE	=	463;

const DWORD OPCB_PROPERTY_APPROVAL_DATE	=	464;

const DWORD OPCB_PROPERTY_EFFECTIVE_DATE	=	465;

const DWORD OPCB_PROPERTY_EXPIRATION_DATE	=	466;

const DWORD OPCB_PROPERTY_AUTHOR	=	467;

const DWORD OPCB_PROPERTY_APPROVED_BY	=	468;

const DWORD OPCB_PROPERTY_USAGE_CONSTRAINT	=	469;

const DWORD OPCB_PROPERTY_RECIPE_STATUS	=	470;

const DWORD OPCB_PROPERTY_RE_USE	=	471;

const DWORD OPCB_PROPERTY_DERIVED_RE	=	472;

const DWORD OPCB_PROPERTY_DERIVED_VERSION	=	473;

const DWORD OPCB_PROPERTY_SCALABLE	=	474;

const DWORD OPCB_PROPERTY_EXPECTED_DURATION	=	475;

const DWORD OPCB_PROPERTY_ACTUAL_DURATION	=	476;

const DWORD OPCB_PROPERTY_TRAIN_LIST2	=	477;

const DWORD OPCB_PROPERTY_TRAIN_LIST2_SCHEMA	=	478;

#endif /* __OPCBC_Properties_MODULE_DEFINED__ */
#endif /* __OPC_BATCH_LIBRARY_DEFINED__ */

/* Additional Prototypes for ALL interfaces */

/* end of Additional Prototypes */

#ifdef __cplusplus
}
#endif

#endif
