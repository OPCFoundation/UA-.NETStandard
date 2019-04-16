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

#ifndef __opc_ae_h__
#define __opc_ae_h__

#if defined(_MSC_VER) && (_MSC_VER >= 1020)
#pragma once
#endif

/* Forward Declarations */ 

#ifndef __OPCEventServerCATID_FWD_DEFINED__
#define __OPCEventServerCATID_FWD_DEFINED__
typedef interface OPCEventServerCATID OPCEventServerCATID;
#endif 	/* __OPCEventServerCATID_FWD_DEFINED__ */


#ifndef __IOPCEventServer_FWD_DEFINED__
#define __IOPCEventServer_FWD_DEFINED__
typedef interface IOPCEventServer IOPCEventServer;
#endif 	/* __IOPCEventServer_FWD_DEFINED__ */


#ifndef __IOPCEventSubscriptionMgt_FWD_DEFINED__
#define __IOPCEventSubscriptionMgt_FWD_DEFINED__
typedef interface IOPCEventSubscriptionMgt IOPCEventSubscriptionMgt;
#endif 	/* __IOPCEventSubscriptionMgt_FWD_DEFINED__ */


#ifndef __IOPCEventAreaBrowser_FWD_DEFINED__
#define __IOPCEventAreaBrowser_FWD_DEFINED__
typedef interface IOPCEventAreaBrowser IOPCEventAreaBrowser;
#endif 	/* __IOPCEventAreaBrowser_FWD_DEFINED__ */


#ifndef __IOPCEventSink_FWD_DEFINED__
#define __IOPCEventSink_FWD_DEFINED__
typedef interface IOPCEventSink IOPCEventSink;
#endif 	/* __IOPCEventSink_FWD_DEFINED__ */


#ifndef __IOPCEventServer2_FWD_DEFINED__
#define __IOPCEventServer2_FWD_DEFINED__
typedef interface IOPCEventServer2 IOPCEventServer2;
#endif 	/* __IOPCEventServer2_FWD_DEFINED__ */


#ifndef __IOPCEventSubscriptionMgt2_FWD_DEFINED__
#define __IOPCEventSubscriptionMgt2_FWD_DEFINED__
typedef interface IOPCEventSubscriptionMgt2 IOPCEventSubscriptionMgt2;
#endif 	/* __IOPCEventSubscriptionMgt2_FWD_DEFINED__ */


#ifndef __OPCEventServerCATID_FWD_DEFINED__
#define __OPCEventServerCATID_FWD_DEFINED__
typedef interface OPCEventServerCATID OPCEventServerCATID;
#endif 	/* __OPCEventServerCATID_FWD_DEFINED__ */


#ifndef __IOPCEventServer_FWD_DEFINED__
#define __IOPCEventServer_FWD_DEFINED__
typedef interface IOPCEventServer IOPCEventServer;
#endif 	/* __IOPCEventServer_FWD_DEFINED__ */


#ifndef __IOPCEventSubscriptionMgt_FWD_DEFINED__
#define __IOPCEventSubscriptionMgt_FWD_DEFINED__
typedef interface IOPCEventSubscriptionMgt IOPCEventSubscriptionMgt;
#endif 	/* __IOPCEventSubscriptionMgt_FWD_DEFINED__ */


#ifndef __IOPCEventAreaBrowser_FWD_DEFINED__
#define __IOPCEventAreaBrowser_FWD_DEFINED__
typedef interface IOPCEventAreaBrowser IOPCEventAreaBrowser;
#endif 	/* __IOPCEventAreaBrowser_FWD_DEFINED__ */


#ifndef __IOPCEventSink_FWD_DEFINED__
#define __IOPCEventSink_FWD_DEFINED__
typedef interface IOPCEventSink IOPCEventSink;
#endif 	/* __IOPCEventSink_FWD_DEFINED__ */


#ifndef __IOPCEventServer2_FWD_DEFINED__
#define __IOPCEventServer2_FWD_DEFINED__
typedef interface IOPCEventServer2 IOPCEventServer2;
#endif 	/* __IOPCEventServer2_FWD_DEFINED__ */


#ifndef __IOPCEventSubscriptionMgt2_FWD_DEFINED__
#define __IOPCEventSubscriptionMgt2_FWD_DEFINED__
typedef interface IOPCEventSubscriptionMgt2 IOPCEventSubscriptionMgt2;
#endif 	/* __IOPCEventSubscriptionMgt2_FWD_DEFINED__ */


/* header files for imported files */
#include "oaidl.h"
#include "ocidl.h"

#ifdef __cplusplus
extern "C"{
#endif 

void * __RPC_USER MIDL_user_allocate(size_t);
void __RPC_USER MIDL_user_free( void * ); 

#ifndef __OPCEventServerCATID_INTERFACE_DEFINED__
#define __OPCEventServerCATID_INTERFACE_DEFINED__

/* interface OPCEventServerCATID */
/* [object][uuid] */ 


EXTERN_C const IID IID_OPCEventServerCATID;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("58E13251-AC87-11d1-84D5-00608CB8A7E9")
    OPCEventServerCATID : public IUnknown
    {
    public:
    };
    
#else 	/* C style interface */

    typedef struct OPCEventServerCATIDVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            OPCEventServerCATID * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            OPCEventServerCATID * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            OPCEventServerCATID * This);
        
        END_INTERFACE
    } OPCEventServerCATIDVtbl;

    interface OPCEventServerCATID
    {
        CONST_VTBL struct OPCEventServerCATIDVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define OPCEventServerCATID_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define OPCEventServerCATID_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define OPCEventServerCATID_Release(This)	\
    (This)->lpVtbl -> Release(This)


#endif /* COBJMACROS */


#endif 	/* C style interface */




#endif 	/* __OPCEventServerCATID_INTERFACE_DEFINED__ */


/* interface __MIDL_itf_opc_ae_0262 */
/* [local] */ 

#define CATID_OPCAEServer10 IID_OPCEventServerCATID
typedef DWORD OPCHANDLE;

typedef /* [public][public] */ 
enum __MIDL___MIDL_itf_opc_ae_0262_0001
    {	OPCAE_BROWSE_UP	= 1,
	OPCAE_BROWSE_DOWN	= OPCAE_BROWSE_UP + 1,
	OPCAE_BROWSE_TO	= OPCAE_BROWSE_DOWN + 1
    } 	OPCAEBROWSEDIRECTION;

typedef /* [public][public] */ 
enum __MIDL___MIDL_itf_opc_ae_0262_0002
    {	OPC_AREA	= 1,
	OPC_SOURCE	= OPC_AREA + 1
    } 	OPCAEBROWSETYPE;

typedef /* [public][public][public] */ 
enum __MIDL___MIDL_itf_opc_ae_0262_0003
    {	OPCAE_STATUS_RUNNING	= 1,
	OPCAE_STATUS_FAILED	= OPCAE_STATUS_RUNNING + 1,
	OPCAE_STATUS_NOCONFIG	= OPCAE_STATUS_FAILED + 1,
	OPCAE_STATUS_SUSPENDED	= OPCAE_STATUS_NOCONFIG + 1,
	OPCAE_STATUS_TEST	= OPCAE_STATUS_SUSPENDED + 1,
	OPCAE_STATUS_COMM_FAULT	= OPCAE_STATUS_TEST + 1
    } 	OPCEVENTSERVERSTATE;

typedef /* [public][public] */ struct __MIDL___MIDL_itf_opc_ae_0262_0004
    {
    WORD wChangeMask;
    WORD wNewState;
    /* [string] */ LPWSTR szSource;
    FILETIME ftTime;
    /* [string] */ LPWSTR szMessage;
    DWORD dwEventType;
    DWORD dwEventCategory;
    DWORD dwSeverity;
    /* [string] */ LPWSTR szConditionName;
    /* [string] */ LPWSTR szSubconditionName;
    WORD wQuality;
    WORD wReserved;
    BOOL bAckRequired;
    FILETIME ftActiveTime;
    DWORD dwCookie;
    DWORD dwNumEventAttrs;
    /* [size_is] */ VARIANT *pEventAttributes;
    /* [string] */ LPWSTR szActorID;
    } 	ONEVENTSTRUCT;

typedef /* [public][public] */ struct __MIDL___MIDL_itf_opc_ae_0262_0005
    {
    FILETIME ftStartTime;
    FILETIME ftCurrentTime;
    FILETIME ftLastUpdateTime;
    OPCEVENTSERVERSTATE dwServerState;
    WORD wMajorVersion;
    WORD wMinorVersion;
    WORD wBuildNumber;
    WORD wReserved;
    /* [string] */ LPWSTR szVendorInfo;
    } 	OPCEVENTSERVERSTATUS;

typedef /* [public][public] */ struct __MIDL___MIDL_itf_opc_ae_0262_0006
    {
    WORD wState;
    WORD wReserved1;
    LPWSTR szActiveSubCondition;
    LPWSTR szASCDefinition;
    DWORD dwASCSeverity;
    LPWSTR szASCDescription;
    WORD wQuality;
    WORD wReserved2;
    FILETIME ftLastAckTime;
    FILETIME ftSubCondLastActive;
    FILETIME ftCondLastActive;
    FILETIME ftCondLastInactive;
    LPWSTR szAcknowledgerID;
    LPWSTR szComment;
    DWORD dwNumSCs;
    /* [size_is] */ LPWSTR *pszSCNames;
    /* [size_is] */ LPWSTR *pszSCDefinitions;
    /* [size_is] */ DWORD *pdwSCSeverities;
    /* [size_is] */ LPWSTR *pszSCDescriptions;
    DWORD dwNumEventAttrs;
    /* [size_is] */ VARIANT *pEventAttributes;
    /* [size_is] */ HRESULT *pErrors;
    } 	OPCCONDITIONSTATE;



extern RPC_IF_HANDLE __MIDL_itf_opc_ae_0262_v0_0_c_ifspec;
extern RPC_IF_HANDLE __MIDL_itf_opc_ae_0262_v0_0_s_ifspec;

#ifndef __IOPCEventServer_INTERFACE_DEFINED__
#define __IOPCEventServer_INTERFACE_DEFINED__

/* interface IOPCEventServer */
/* [object][unique][uuid] */ 


EXTERN_C const IID IID_IOPCEventServer;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("65168851-5783-11D1-84A0-00608CB8A7E9")
    IOPCEventServer : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE GetStatus( 
            /* [out] */ OPCEVENTSERVERSTATUS **ppEventServerStatus) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE CreateEventSubscription( 
            /* [in] */ BOOL bActive,
            /* [in] */ DWORD dwBufferTime,
            /* [in] */ DWORD dwMaxSize,
            /* [in] */ OPCHANDLE hClientSubscription,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk,
            /* [out] */ DWORD *pdwRevisedBufferTime,
            /* [out] */ DWORD *pdwRevisedMaxSize) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE QueryAvailableFilters( 
            /* [out] */ DWORD *pdwFilterMask) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE QueryEventCategories( 
            /* [in] */ DWORD dwEventType,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppdwEventCategories,
            /* [size_is][size_is][out] */ LPWSTR **ppszEventCategoryDescs) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE QueryConditionNames( 
            /* [in] */ DWORD dwEventCategory,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ LPWSTR **ppszConditionNames) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE QuerySubConditionNames( 
            /* [in] */ LPWSTR szConditionName,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ LPWSTR **ppszSubConditionNames) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE QuerySourceConditions( 
            /* [in] */ LPWSTR szSource,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ LPWSTR **ppszConditionNames) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE QueryEventAttributes( 
            /* [in] */ DWORD dwEventCategory,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppdwAttrIDs,
            /* [size_is][size_is][out] */ LPWSTR **ppszAttrDescs,
            /* [size_is][size_is][out] */ VARTYPE **ppvtAttrTypes) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE TranslateToItemIDs( 
            /* [in] */ LPWSTR szSource,
            /* [in] */ DWORD dwEventCategory,
            /* [in] */ LPWSTR szConditionName,
            /* [in] */ LPWSTR szSubconditionName,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ DWORD *pdwAssocAttrIDs,
            /* [size_is][size_is][out] */ LPWSTR **ppszAttrItemIDs,
            /* [size_is][size_is][out] */ LPWSTR **ppszNodeNames,
            /* [size_is][size_is][out] */ CLSID **ppCLSIDs) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetConditionState( 
            /* [in] */ LPWSTR szSource,
            /* [in] */ LPWSTR szConditionName,
            /* [in] */ DWORD dwNumEventAttrs,
            /* [size_is][in] */ DWORD *pdwAttributeIDs,
            /* [out] */ OPCCONDITIONSTATE **ppConditionState) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE EnableConditionByArea( 
            /* [in] */ DWORD dwNumAreas,
            /* [size_is][in] */ LPWSTR *pszAreas) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE EnableConditionBySource( 
            /* [in] */ DWORD dwNumSources,
            /* [size_is][in] */ LPWSTR *pszSources) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE DisableConditionByArea( 
            /* [in] */ DWORD dwNumAreas,
            /* [size_is][in] */ LPWSTR *pszAreas) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE DisableConditionBySource( 
            /* [in] */ DWORD dwNumSources,
            /* [size_is][in] */ LPWSTR *pszSources) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE AckCondition( 
            /* [in] */ DWORD dwCount,
            /* [string][in] */ LPWSTR szAcknowledgerID,
            /* [string][in] */ LPWSTR szComment,
            /* [size_is][in] */ LPWSTR *pszSource,
            /* [size_is][in] */ LPWSTR *pszConditionName,
            /* [size_is][in] */ FILETIME *pftActiveTime,
            /* [size_is][in] */ DWORD *pdwCookie,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE CreateAreaBrowser( 
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCEventServerVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCEventServer * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCEventServer * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCEventServer * This);
        
        HRESULT ( STDMETHODCALLTYPE *GetStatus )( 
            IOPCEventServer * This,
            /* [out] */ OPCEVENTSERVERSTATUS **ppEventServerStatus);
        
        HRESULT ( STDMETHODCALLTYPE *CreateEventSubscription )( 
            IOPCEventServer * This,
            /* [in] */ BOOL bActive,
            /* [in] */ DWORD dwBufferTime,
            /* [in] */ DWORD dwMaxSize,
            /* [in] */ OPCHANDLE hClientSubscription,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk,
            /* [out] */ DWORD *pdwRevisedBufferTime,
            /* [out] */ DWORD *pdwRevisedMaxSize);
        
        HRESULT ( STDMETHODCALLTYPE *QueryAvailableFilters )( 
            IOPCEventServer * This,
            /* [out] */ DWORD *pdwFilterMask);
        
        HRESULT ( STDMETHODCALLTYPE *QueryEventCategories )( 
            IOPCEventServer * This,
            /* [in] */ DWORD dwEventType,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppdwEventCategories,
            /* [size_is][size_is][out] */ LPWSTR **ppszEventCategoryDescs);
        
        HRESULT ( STDMETHODCALLTYPE *QueryConditionNames )( 
            IOPCEventServer * This,
            /* [in] */ DWORD dwEventCategory,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ LPWSTR **ppszConditionNames);
        
        HRESULT ( STDMETHODCALLTYPE *QuerySubConditionNames )( 
            IOPCEventServer * This,
            /* [in] */ LPWSTR szConditionName,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ LPWSTR **ppszSubConditionNames);
        
        HRESULT ( STDMETHODCALLTYPE *QuerySourceConditions )( 
            IOPCEventServer * This,
            /* [in] */ LPWSTR szSource,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ LPWSTR **ppszConditionNames);
        
        HRESULT ( STDMETHODCALLTYPE *QueryEventAttributes )( 
            IOPCEventServer * This,
            /* [in] */ DWORD dwEventCategory,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppdwAttrIDs,
            /* [size_is][size_is][out] */ LPWSTR **ppszAttrDescs,
            /* [size_is][size_is][out] */ VARTYPE **ppvtAttrTypes);
        
        HRESULT ( STDMETHODCALLTYPE *TranslateToItemIDs )( 
            IOPCEventServer * This,
            /* [in] */ LPWSTR szSource,
            /* [in] */ DWORD dwEventCategory,
            /* [in] */ LPWSTR szConditionName,
            /* [in] */ LPWSTR szSubconditionName,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ DWORD *pdwAssocAttrIDs,
            /* [size_is][size_is][out] */ LPWSTR **ppszAttrItemIDs,
            /* [size_is][size_is][out] */ LPWSTR **ppszNodeNames,
            /* [size_is][size_is][out] */ CLSID **ppCLSIDs);
        
        HRESULT ( STDMETHODCALLTYPE *GetConditionState )( 
            IOPCEventServer * This,
            /* [in] */ LPWSTR szSource,
            /* [in] */ LPWSTR szConditionName,
            /* [in] */ DWORD dwNumEventAttrs,
            /* [size_is][in] */ DWORD *pdwAttributeIDs,
            /* [out] */ OPCCONDITIONSTATE **ppConditionState);
        
        HRESULT ( STDMETHODCALLTYPE *EnableConditionByArea )( 
            IOPCEventServer * This,
            /* [in] */ DWORD dwNumAreas,
            /* [size_is][in] */ LPWSTR *pszAreas);
        
        HRESULT ( STDMETHODCALLTYPE *EnableConditionBySource )( 
            IOPCEventServer * This,
            /* [in] */ DWORD dwNumSources,
            /* [size_is][in] */ LPWSTR *pszSources);
        
        HRESULT ( STDMETHODCALLTYPE *DisableConditionByArea )( 
            IOPCEventServer * This,
            /* [in] */ DWORD dwNumAreas,
            /* [size_is][in] */ LPWSTR *pszAreas);
        
        HRESULT ( STDMETHODCALLTYPE *DisableConditionBySource )( 
            IOPCEventServer * This,
            /* [in] */ DWORD dwNumSources,
            /* [size_is][in] */ LPWSTR *pszSources);
        
        HRESULT ( STDMETHODCALLTYPE *AckCondition )( 
            IOPCEventServer * This,
            /* [in] */ DWORD dwCount,
            /* [string][in] */ LPWSTR szAcknowledgerID,
            /* [string][in] */ LPWSTR szComment,
            /* [size_is][in] */ LPWSTR *pszSource,
            /* [size_is][in] */ LPWSTR *pszConditionName,
            /* [size_is][in] */ FILETIME *pftActiveTime,
            /* [size_is][in] */ DWORD *pdwCookie,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *CreateAreaBrowser )( 
            IOPCEventServer * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk);
        
        END_INTERFACE
    } IOPCEventServerVtbl;

    interface IOPCEventServer
    {
        CONST_VTBL struct IOPCEventServerVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCEventServer_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCEventServer_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCEventServer_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCEventServer_GetStatus(This,ppEventServerStatus)	\
    (This)->lpVtbl -> GetStatus(This,ppEventServerStatus)

#define IOPCEventServer_CreateEventSubscription(This,bActive,dwBufferTime,dwMaxSize,hClientSubscription,riid,ppUnk,pdwRevisedBufferTime,pdwRevisedMaxSize)	\
    (This)->lpVtbl -> CreateEventSubscription(This,bActive,dwBufferTime,dwMaxSize,hClientSubscription,riid,ppUnk,pdwRevisedBufferTime,pdwRevisedMaxSize)

#define IOPCEventServer_QueryAvailableFilters(This,pdwFilterMask)	\
    (This)->lpVtbl -> QueryAvailableFilters(This,pdwFilterMask)

#define IOPCEventServer_QueryEventCategories(This,dwEventType,pdwCount,ppdwEventCategories,ppszEventCategoryDescs)	\
    (This)->lpVtbl -> QueryEventCategories(This,dwEventType,pdwCount,ppdwEventCategories,ppszEventCategoryDescs)

#define IOPCEventServer_QueryConditionNames(This,dwEventCategory,pdwCount,ppszConditionNames)	\
    (This)->lpVtbl -> QueryConditionNames(This,dwEventCategory,pdwCount,ppszConditionNames)

#define IOPCEventServer_QuerySubConditionNames(This,szConditionName,pdwCount,ppszSubConditionNames)	\
    (This)->lpVtbl -> QuerySubConditionNames(This,szConditionName,pdwCount,ppszSubConditionNames)

#define IOPCEventServer_QuerySourceConditions(This,szSource,pdwCount,ppszConditionNames)	\
    (This)->lpVtbl -> QuerySourceConditions(This,szSource,pdwCount,ppszConditionNames)

#define IOPCEventServer_QueryEventAttributes(This,dwEventCategory,pdwCount,ppdwAttrIDs,ppszAttrDescs,ppvtAttrTypes)	\
    (This)->lpVtbl -> QueryEventAttributes(This,dwEventCategory,pdwCount,ppdwAttrIDs,ppszAttrDescs,ppvtAttrTypes)

#define IOPCEventServer_TranslateToItemIDs(This,szSource,dwEventCategory,szConditionName,szSubconditionName,dwCount,pdwAssocAttrIDs,ppszAttrItemIDs,ppszNodeNames,ppCLSIDs)	\
    (This)->lpVtbl -> TranslateToItemIDs(This,szSource,dwEventCategory,szConditionName,szSubconditionName,dwCount,pdwAssocAttrIDs,ppszAttrItemIDs,ppszNodeNames,ppCLSIDs)

#define IOPCEventServer_GetConditionState(This,szSource,szConditionName,dwNumEventAttrs,pdwAttributeIDs,ppConditionState)	\
    (This)->lpVtbl -> GetConditionState(This,szSource,szConditionName,dwNumEventAttrs,pdwAttributeIDs,ppConditionState)

#define IOPCEventServer_EnableConditionByArea(This,dwNumAreas,pszAreas)	\
    (This)->lpVtbl -> EnableConditionByArea(This,dwNumAreas,pszAreas)

#define IOPCEventServer_EnableConditionBySource(This,dwNumSources,pszSources)	\
    (This)->lpVtbl -> EnableConditionBySource(This,dwNumSources,pszSources)

#define IOPCEventServer_DisableConditionByArea(This,dwNumAreas,pszAreas)	\
    (This)->lpVtbl -> DisableConditionByArea(This,dwNumAreas,pszAreas)

#define IOPCEventServer_DisableConditionBySource(This,dwNumSources,pszSources)	\
    (This)->lpVtbl -> DisableConditionBySource(This,dwNumSources,pszSources)

#define IOPCEventServer_AckCondition(This,dwCount,szAcknowledgerID,szComment,pszSource,pszConditionName,pftActiveTime,pdwCookie,ppErrors)	\
    (This)->lpVtbl -> AckCondition(This,dwCount,szAcknowledgerID,szComment,pszSource,pszConditionName,pftActiveTime,pdwCookie,ppErrors)

#define IOPCEventServer_CreateAreaBrowser(This,riid,ppUnk)	\
    (This)->lpVtbl -> CreateAreaBrowser(This,riid,ppUnk)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCEventServer_GetStatus_Proxy( 
    IOPCEventServer * This,
    /* [out] */ OPCEVENTSERVERSTATUS **ppEventServerStatus);


void __RPC_STUB IOPCEventServer_GetStatus_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer_CreateEventSubscription_Proxy( 
    IOPCEventServer * This,
    /* [in] */ BOOL bActive,
    /* [in] */ DWORD dwBufferTime,
    /* [in] */ DWORD dwMaxSize,
    /* [in] */ OPCHANDLE hClientSubscription,
    /* [in] */ REFIID riid,
    /* [iid_is][out] */ LPUNKNOWN *ppUnk,
    /* [out] */ DWORD *pdwRevisedBufferTime,
    /* [out] */ DWORD *pdwRevisedMaxSize);


void __RPC_STUB IOPCEventServer_CreateEventSubscription_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer_QueryAvailableFilters_Proxy( 
    IOPCEventServer * This,
    /* [out] */ DWORD *pdwFilterMask);


void __RPC_STUB IOPCEventServer_QueryAvailableFilters_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer_QueryEventCategories_Proxy( 
    IOPCEventServer * This,
    /* [in] */ DWORD dwEventType,
    /* [out] */ DWORD *pdwCount,
    /* [size_is][size_is][out] */ DWORD **ppdwEventCategories,
    /* [size_is][size_is][out] */ LPWSTR **ppszEventCategoryDescs);


void __RPC_STUB IOPCEventServer_QueryEventCategories_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer_QueryConditionNames_Proxy( 
    IOPCEventServer * This,
    /* [in] */ DWORD dwEventCategory,
    /* [out] */ DWORD *pdwCount,
    /* [size_is][size_is][out] */ LPWSTR **ppszConditionNames);


void __RPC_STUB IOPCEventServer_QueryConditionNames_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer_QuerySubConditionNames_Proxy( 
    IOPCEventServer * This,
    /* [in] */ LPWSTR szConditionName,
    /* [out] */ DWORD *pdwCount,
    /* [size_is][size_is][out] */ LPWSTR **ppszSubConditionNames);


void __RPC_STUB IOPCEventServer_QuerySubConditionNames_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer_QuerySourceConditions_Proxy( 
    IOPCEventServer * This,
    /* [in] */ LPWSTR szSource,
    /* [out] */ DWORD *pdwCount,
    /* [size_is][size_is][out] */ LPWSTR **ppszConditionNames);


void __RPC_STUB IOPCEventServer_QuerySourceConditions_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer_QueryEventAttributes_Proxy( 
    IOPCEventServer * This,
    /* [in] */ DWORD dwEventCategory,
    /* [out] */ DWORD *pdwCount,
    /* [size_is][size_is][out] */ DWORD **ppdwAttrIDs,
    /* [size_is][size_is][out] */ LPWSTR **ppszAttrDescs,
    /* [size_is][size_is][out] */ VARTYPE **ppvtAttrTypes);


void __RPC_STUB IOPCEventServer_QueryEventAttributes_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer_TranslateToItemIDs_Proxy( 
    IOPCEventServer * This,
    /* [in] */ LPWSTR szSource,
    /* [in] */ DWORD dwEventCategory,
    /* [in] */ LPWSTR szConditionName,
    /* [in] */ LPWSTR szSubconditionName,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ DWORD *pdwAssocAttrIDs,
    /* [size_is][size_is][out] */ LPWSTR **ppszAttrItemIDs,
    /* [size_is][size_is][out] */ LPWSTR **ppszNodeNames,
    /* [size_is][size_is][out] */ CLSID **ppCLSIDs);


void __RPC_STUB IOPCEventServer_TranslateToItemIDs_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer_GetConditionState_Proxy( 
    IOPCEventServer * This,
    /* [in] */ LPWSTR szSource,
    /* [in] */ LPWSTR szConditionName,
    /* [in] */ DWORD dwNumEventAttrs,
    /* [size_is][in] */ DWORD *pdwAttributeIDs,
    /* [out] */ OPCCONDITIONSTATE **ppConditionState);


void __RPC_STUB IOPCEventServer_GetConditionState_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer_EnableConditionByArea_Proxy( 
    IOPCEventServer * This,
    /* [in] */ DWORD dwNumAreas,
    /* [size_is][in] */ LPWSTR *pszAreas);


void __RPC_STUB IOPCEventServer_EnableConditionByArea_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer_EnableConditionBySource_Proxy( 
    IOPCEventServer * This,
    /* [in] */ DWORD dwNumSources,
    /* [size_is][in] */ LPWSTR *pszSources);


void __RPC_STUB IOPCEventServer_EnableConditionBySource_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer_DisableConditionByArea_Proxy( 
    IOPCEventServer * This,
    /* [in] */ DWORD dwNumAreas,
    /* [size_is][in] */ LPWSTR *pszAreas);


void __RPC_STUB IOPCEventServer_DisableConditionByArea_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer_DisableConditionBySource_Proxy( 
    IOPCEventServer * This,
    /* [in] */ DWORD dwNumSources,
    /* [size_is][in] */ LPWSTR *pszSources);


void __RPC_STUB IOPCEventServer_DisableConditionBySource_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer_AckCondition_Proxy( 
    IOPCEventServer * This,
    /* [in] */ DWORD dwCount,
    /* [string][in] */ LPWSTR szAcknowledgerID,
    /* [string][in] */ LPWSTR szComment,
    /* [size_is][in] */ LPWSTR *pszSource,
    /* [size_is][in] */ LPWSTR *pszConditionName,
    /* [size_is][in] */ FILETIME *pftActiveTime,
    /* [size_is][in] */ DWORD *pdwCookie,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCEventServer_AckCondition_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer_CreateAreaBrowser_Proxy( 
    IOPCEventServer * This,
    /* [in] */ REFIID riid,
    /* [iid_is][out] */ LPUNKNOWN *ppUnk);


void __RPC_STUB IOPCEventServer_CreateAreaBrowser_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCEventServer_INTERFACE_DEFINED__ */


#ifndef __IOPCEventSubscriptionMgt_INTERFACE_DEFINED__
#define __IOPCEventSubscriptionMgt_INTERFACE_DEFINED__

/* interface IOPCEventSubscriptionMgt */
/* [object][unique][uuid] */ 


EXTERN_C const IID IID_IOPCEventSubscriptionMgt;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("65168855-5783-11D1-84A0-00608CB8A7E9")
    IOPCEventSubscriptionMgt : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE SetFilter( 
            /* [in] */ DWORD dwEventType,
            /* [in] */ DWORD dwNumCategories,
            /* [size_is][in] */ DWORD *pdwEventCategories,
            /* [in] */ DWORD dwLowSeverity,
            /* [in] */ DWORD dwHighSeverity,
            /* [in] */ DWORD dwNumAreas,
            /* [size_is][in] */ LPWSTR *pszAreaList,
            /* [in] */ DWORD dwNumSources,
            /* [size_is][in] */ LPWSTR *pszSourceList) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetFilter( 
            /* [out] */ DWORD *pdwEventType,
            /* [out] */ DWORD *pdwNumCategories,
            /* [size_is][size_is][out] */ DWORD **ppdwEventCategories,
            /* [out] */ DWORD *pdwLowSeverity,
            /* [out] */ DWORD *pdwHighSeverity,
            /* [out] */ DWORD *pdwNumAreas,
            /* [size_is][size_is][out] */ LPWSTR **ppszAreaList,
            /* [out] */ DWORD *pdwNumSources,
            /* [size_is][size_is][out] */ LPWSTR **ppszSourceList) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE SelectReturnedAttributes( 
            /* [in] */ DWORD dwEventCategory,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ DWORD *dwAttributeIDs) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetReturnedAttributes( 
            /* [in] */ DWORD dwEventCategory,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppdwAttributeIDs) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Refresh( 
            /* [in] */ DWORD dwConnection) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE CancelRefresh( 
            /* [in] */ DWORD dwConnection) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetState( 
            /* [out] */ BOOL *pbActive,
            /* [out] */ DWORD *pdwBufferTime,
            /* [out] */ DWORD *pdwMaxSize,
            /* [out] */ OPCHANDLE *phClientSubscription) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE SetState( 
            /* [in][unique] */ BOOL *pbActive,
            /* [in][unique] */ DWORD *pdwBufferTime,
            /* [in][unique] */ DWORD *pdwMaxSize,
            /* [in] */ OPCHANDLE hClientSubscription,
            /* [out] */ DWORD *pdwRevisedBufferTime,
            /* [out] */ DWORD *pdwRevisedMaxSize) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCEventSubscriptionMgtVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCEventSubscriptionMgt * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCEventSubscriptionMgt * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCEventSubscriptionMgt * This);
        
        HRESULT ( STDMETHODCALLTYPE *SetFilter )( 
            IOPCEventSubscriptionMgt * This,
            /* [in] */ DWORD dwEventType,
            /* [in] */ DWORD dwNumCategories,
            /* [size_is][in] */ DWORD *pdwEventCategories,
            /* [in] */ DWORD dwLowSeverity,
            /* [in] */ DWORD dwHighSeverity,
            /* [in] */ DWORD dwNumAreas,
            /* [size_is][in] */ LPWSTR *pszAreaList,
            /* [in] */ DWORD dwNumSources,
            /* [size_is][in] */ LPWSTR *pszSourceList);
        
        HRESULT ( STDMETHODCALLTYPE *GetFilter )( 
            IOPCEventSubscriptionMgt * This,
            /* [out] */ DWORD *pdwEventType,
            /* [out] */ DWORD *pdwNumCategories,
            /* [size_is][size_is][out] */ DWORD **ppdwEventCategories,
            /* [out] */ DWORD *pdwLowSeverity,
            /* [out] */ DWORD *pdwHighSeverity,
            /* [out] */ DWORD *pdwNumAreas,
            /* [size_is][size_is][out] */ LPWSTR **ppszAreaList,
            /* [out] */ DWORD *pdwNumSources,
            /* [size_is][size_is][out] */ LPWSTR **ppszSourceList);
        
        HRESULT ( STDMETHODCALLTYPE *SelectReturnedAttributes )( 
            IOPCEventSubscriptionMgt * This,
            /* [in] */ DWORD dwEventCategory,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ DWORD *dwAttributeIDs);
        
        HRESULT ( STDMETHODCALLTYPE *GetReturnedAttributes )( 
            IOPCEventSubscriptionMgt * This,
            /* [in] */ DWORD dwEventCategory,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppdwAttributeIDs);
        
        HRESULT ( STDMETHODCALLTYPE *Refresh )( 
            IOPCEventSubscriptionMgt * This,
            /* [in] */ DWORD dwConnection);
        
        HRESULT ( STDMETHODCALLTYPE *CancelRefresh )( 
            IOPCEventSubscriptionMgt * This,
            /* [in] */ DWORD dwConnection);
        
        HRESULT ( STDMETHODCALLTYPE *GetState )( 
            IOPCEventSubscriptionMgt * This,
            /* [out] */ BOOL *pbActive,
            /* [out] */ DWORD *pdwBufferTime,
            /* [out] */ DWORD *pdwMaxSize,
            /* [out] */ OPCHANDLE *phClientSubscription);
        
        HRESULT ( STDMETHODCALLTYPE *SetState )( 
            IOPCEventSubscriptionMgt * This,
            /* [in][unique] */ BOOL *pbActive,
            /* [in][unique] */ DWORD *pdwBufferTime,
            /* [in][unique] */ DWORD *pdwMaxSize,
            /* [in] */ OPCHANDLE hClientSubscription,
            /* [out] */ DWORD *pdwRevisedBufferTime,
            /* [out] */ DWORD *pdwRevisedMaxSize);
        
        END_INTERFACE
    } IOPCEventSubscriptionMgtVtbl;

    interface IOPCEventSubscriptionMgt
    {
        CONST_VTBL struct IOPCEventSubscriptionMgtVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCEventSubscriptionMgt_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCEventSubscriptionMgt_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCEventSubscriptionMgt_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCEventSubscriptionMgt_SetFilter(This,dwEventType,dwNumCategories,pdwEventCategories,dwLowSeverity,dwHighSeverity,dwNumAreas,pszAreaList,dwNumSources,pszSourceList)	\
    (This)->lpVtbl -> SetFilter(This,dwEventType,dwNumCategories,pdwEventCategories,dwLowSeverity,dwHighSeverity,dwNumAreas,pszAreaList,dwNumSources,pszSourceList)

#define IOPCEventSubscriptionMgt_GetFilter(This,pdwEventType,pdwNumCategories,ppdwEventCategories,pdwLowSeverity,pdwHighSeverity,pdwNumAreas,ppszAreaList,pdwNumSources,ppszSourceList)	\
    (This)->lpVtbl -> GetFilter(This,pdwEventType,pdwNumCategories,ppdwEventCategories,pdwLowSeverity,pdwHighSeverity,pdwNumAreas,ppszAreaList,pdwNumSources,ppszSourceList)

#define IOPCEventSubscriptionMgt_SelectReturnedAttributes(This,dwEventCategory,dwCount,dwAttributeIDs)	\
    (This)->lpVtbl -> SelectReturnedAttributes(This,dwEventCategory,dwCount,dwAttributeIDs)

#define IOPCEventSubscriptionMgt_GetReturnedAttributes(This,dwEventCategory,pdwCount,ppdwAttributeIDs)	\
    (This)->lpVtbl -> GetReturnedAttributes(This,dwEventCategory,pdwCount,ppdwAttributeIDs)

#define IOPCEventSubscriptionMgt_Refresh(This,dwConnection)	\
    (This)->lpVtbl -> Refresh(This,dwConnection)

#define IOPCEventSubscriptionMgt_CancelRefresh(This,dwConnection)	\
    (This)->lpVtbl -> CancelRefresh(This,dwConnection)

#define IOPCEventSubscriptionMgt_GetState(This,pbActive,pdwBufferTime,pdwMaxSize,phClientSubscription)	\
    (This)->lpVtbl -> GetState(This,pbActive,pdwBufferTime,pdwMaxSize,phClientSubscription)

#define IOPCEventSubscriptionMgt_SetState(This,pbActive,pdwBufferTime,pdwMaxSize,hClientSubscription,pdwRevisedBufferTime,pdwRevisedMaxSize)	\
    (This)->lpVtbl -> SetState(This,pbActive,pdwBufferTime,pdwMaxSize,hClientSubscription,pdwRevisedBufferTime,pdwRevisedMaxSize)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCEventSubscriptionMgt_SetFilter_Proxy( 
    IOPCEventSubscriptionMgt * This,
    /* [in] */ DWORD dwEventType,
    /* [in] */ DWORD dwNumCategories,
    /* [size_is][in] */ DWORD *pdwEventCategories,
    /* [in] */ DWORD dwLowSeverity,
    /* [in] */ DWORD dwHighSeverity,
    /* [in] */ DWORD dwNumAreas,
    /* [size_is][in] */ LPWSTR *pszAreaList,
    /* [in] */ DWORD dwNumSources,
    /* [size_is][in] */ LPWSTR *pszSourceList);


void __RPC_STUB IOPCEventSubscriptionMgt_SetFilter_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventSubscriptionMgt_GetFilter_Proxy( 
    IOPCEventSubscriptionMgt * This,
    /* [out] */ DWORD *pdwEventType,
    /* [out] */ DWORD *pdwNumCategories,
    /* [size_is][size_is][out] */ DWORD **ppdwEventCategories,
    /* [out] */ DWORD *pdwLowSeverity,
    /* [out] */ DWORD *pdwHighSeverity,
    /* [out] */ DWORD *pdwNumAreas,
    /* [size_is][size_is][out] */ LPWSTR **ppszAreaList,
    /* [out] */ DWORD *pdwNumSources,
    /* [size_is][size_is][out] */ LPWSTR **ppszSourceList);


void __RPC_STUB IOPCEventSubscriptionMgt_GetFilter_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventSubscriptionMgt_SelectReturnedAttributes_Proxy( 
    IOPCEventSubscriptionMgt * This,
    /* [in] */ DWORD dwEventCategory,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ DWORD *dwAttributeIDs);


void __RPC_STUB IOPCEventSubscriptionMgt_SelectReturnedAttributes_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventSubscriptionMgt_GetReturnedAttributes_Proxy( 
    IOPCEventSubscriptionMgt * This,
    /* [in] */ DWORD dwEventCategory,
    /* [out] */ DWORD *pdwCount,
    /* [size_is][size_is][out] */ DWORD **ppdwAttributeIDs);


void __RPC_STUB IOPCEventSubscriptionMgt_GetReturnedAttributes_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventSubscriptionMgt_Refresh_Proxy( 
    IOPCEventSubscriptionMgt * This,
    /* [in] */ DWORD dwConnection);


void __RPC_STUB IOPCEventSubscriptionMgt_Refresh_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventSubscriptionMgt_CancelRefresh_Proxy( 
    IOPCEventSubscriptionMgt * This,
    /* [in] */ DWORD dwConnection);


void __RPC_STUB IOPCEventSubscriptionMgt_CancelRefresh_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventSubscriptionMgt_GetState_Proxy( 
    IOPCEventSubscriptionMgt * This,
    /* [out] */ BOOL *pbActive,
    /* [out] */ DWORD *pdwBufferTime,
    /* [out] */ DWORD *pdwMaxSize,
    /* [out] */ OPCHANDLE *phClientSubscription);


void __RPC_STUB IOPCEventSubscriptionMgt_GetState_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventSubscriptionMgt_SetState_Proxy( 
    IOPCEventSubscriptionMgt * This,
    /* [in][unique] */ BOOL *pbActive,
    /* [in][unique] */ DWORD *pdwBufferTime,
    /* [in][unique] */ DWORD *pdwMaxSize,
    /* [in] */ OPCHANDLE hClientSubscription,
    /* [out] */ DWORD *pdwRevisedBufferTime,
    /* [out] */ DWORD *pdwRevisedMaxSize);


void __RPC_STUB IOPCEventSubscriptionMgt_SetState_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCEventSubscriptionMgt_INTERFACE_DEFINED__ */


#ifndef __IOPCEventAreaBrowser_INTERFACE_DEFINED__
#define __IOPCEventAreaBrowser_INTERFACE_DEFINED__

/* interface IOPCEventAreaBrowser */
/* [object][unique][uuid] */ 


EXTERN_C const IID IID_IOPCEventAreaBrowser;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("65168857-5783-11D1-84A0-00608CB8A7E9")
    IOPCEventAreaBrowser : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE ChangeBrowsePosition( 
            /* [in] */ OPCAEBROWSEDIRECTION dwBrowseDirection,
            /* [string][in] */ LPCWSTR szString) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE BrowseOPCAreas( 
            /* [in] */ OPCAEBROWSETYPE dwBrowseFilterType,
            /* [string][in] */ LPCWSTR szFilterCriteria,
            /* [out] */ LPENUMSTRING *ppIEnumString) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetQualifiedAreaName( 
            /* [in] */ LPCWSTR szAreaName,
            /* [string][out] */ LPWSTR *pszQualifiedAreaName) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetQualifiedSourceName( 
            /* [in] */ LPCWSTR szSourceName,
            /* [string][out] */ LPWSTR *pszQualifiedSourceName) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCEventAreaBrowserVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCEventAreaBrowser * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCEventAreaBrowser * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCEventAreaBrowser * This);
        
        HRESULT ( STDMETHODCALLTYPE *ChangeBrowsePosition )( 
            IOPCEventAreaBrowser * This,
            /* [in] */ OPCAEBROWSEDIRECTION dwBrowseDirection,
            /* [string][in] */ LPCWSTR szString);
        
        HRESULT ( STDMETHODCALLTYPE *BrowseOPCAreas )( 
            IOPCEventAreaBrowser * This,
            /* [in] */ OPCAEBROWSETYPE dwBrowseFilterType,
            /* [string][in] */ LPCWSTR szFilterCriteria,
            /* [out] */ LPENUMSTRING *ppIEnumString);
        
        HRESULT ( STDMETHODCALLTYPE *GetQualifiedAreaName )( 
            IOPCEventAreaBrowser * This,
            /* [in] */ LPCWSTR szAreaName,
            /* [string][out] */ LPWSTR *pszQualifiedAreaName);
        
        HRESULT ( STDMETHODCALLTYPE *GetQualifiedSourceName )( 
            IOPCEventAreaBrowser * This,
            /* [in] */ LPCWSTR szSourceName,
            /* [string][out] */ LPWSTR *pszQualifiedSourceName);
        
        END_INTERFACE
    } IOPCEventAreaBrowserVtbl;

    interface IOPCEventAreaBrowser
    {
        CONST_VTBL struct IOPCEventAreaBrowserVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCEventAreaBrowser_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCEventAreaBrowser_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCEventAreaBrowser_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCEventAreaBrowser_ChangeBrowsePosition(This,dwBrowseDirection,szString)	\
    (This)->lpVtbl -> ChangeBrowsePosition(This,dwBrowseDirection,szString)

#define IOPCEventAreaBrowser_BrowseOPCAreas(This,dwBrowseFilterType,szFilterCriteria,ppIEnumString)	\
    (This)->lpVtbl -> BrowseOPCAreas(This,dwBrowseFilterType,szFilterCriteria,ppIEnumString)

#define IOPCEventAreaBrowser_GetQualifiedAreaName(This,szAreaName,pszQualifiedAreaName)	\
    (This)->lpVtbl -> GetQualifiedAreaName(This,szAreaName,pszQualifiedAreaName)

#define IOPCEventAreaBrowser_GetQualifiedSourceName(This,szSourceName,pszQualifiedSourceName)	\
    (This)->lpVtbl -> GetQualifiedSourceName(This,szSourceName,pszQualifiedSourceName)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCEventAreaBrowser_ChangeBrowsePosition_Proxy( 
    IOPCEventAreaBrowser * This,
    /* [in] */ OPCAEBROWSEDIRECTION dwBrowseDirection,
    /* [string][in] */ LPCWSTR szString);


void __RPC_STUB IOPCEventAreaBrowser_ChangeBrowsePosition_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventAreaBrowser_BrowseOPCAreas_Proxy( 
    IOPCEventAreaBrowser * This,
    /* [in] */ OPCAEBROWSETYPE dwBrowseFilterType,
    /* [string][in] */ LPCWSTR szFilterCriteria,
    /* [out] */ LPENUMSTRING *ppIEnumString);


void __RPC_STUB IOPCEventAreaBrowser_BrowseOPCAreas_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventAreaBrowser_GetQualifiedAreaName_Proxy( 
    IOPCEventAreaBrowser * This,
    /* [in] */ LPCWSTR szAreaName,
    /* [string][out] */ LPWSTR *pszQualifiedAreaName);


void __RPC_STUB IOPCEventAreaBrowser_GetQualifiedAreaName_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventAreaBrowser_GetQualifiedSourceName_Proxy( 
    IOPCEventAreaBrowser * This,
    /* [in] */ LPCWSTR szSourceName,
    /* [string][out] */ LPWSTR *pszQualifiedSourceName);


void __RPC_STUB IOPCEventAreaBrowser_GetQualifiedSourceName_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCEventAreaBrowser_INTERFACE_DEFINED__ */


#ifndef __IOPCEventSink_INTERFACE_DEFINED__
#define __IOPCEventSink_INTERFACE_DEFINED__

/* interface IOPCEventSink */
/* [object][unique][uuid] */ 


EXTERN_C const IID IID_IOPCEventSink;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("6516885F-5783-11D1-84A0-00608CB8A7E9")
    IOPCEventSink : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE OnEvent( 
            /* [in] */ OPCHANDLE hClientSubscription,
            /* [in] */ BOOL bRefresh,
            /* [in] */ BOOL bLastRefresh,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ ONEVENTSTRUCT *pEvents) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCEventSinkVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCEventSink * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCEventSink * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCEventSink * This);
        
        HRESULT ( STDMETHODCALLTYPE *OnEvent )( 
            IOPCEventSink * This,
            /* [in] */ OPCHANDLE hClientSubscription,
            /* [in] */ BOOL bRefresh,
            /* [in] */ BOOL bLastRefresh,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ ONEVENTSTRUCT *pEvents);
        
        END_INTERFACE
    } IOPCEventSinkVtbl;

    interface IOPCEventSink
    {
        CONST_VTBL struct IOPCEventSinkVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCEventSink_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCEventSink_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCEventSink_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCEventSink_OnEvent(This,hClientSubscription,bRefresh,bLastRefresh,dwCount,pEvents)	\
    (This)->lpVtbl -> OnEvent(This,hClientSubscription,bRefresh,bLastRefresh,dwCount,pEvents)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCEventSink_OnEvent_Proxy( 
    IOPCEventSink * This,
    /* [in] */ OPCHANDLE hClientSubscription,
    /* [in] */ BOOL bRefresh,
    /* [in] */ BOOL bLastRefresh,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ ONEVENTSTRUCT *pEvents);


void __RPC_STUB IOPCEventSink_OnEvent_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCEventSink_INTERFACE_DEFINED__ */


#ifndef __IOPCEventServer2_INTERFACE_DEFINED__
#define __IOPCEventServer2_INTERFACE_DEFINED__

/* interface IOPCEventServer2 */
/* [object][unique][uuid] */ 


EXTERN_C const IID IID_IOPCEventServer2;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("71BBE88E-9564-4bcd-BCFC-71C558D94F2D")
    IOPCEventServer2 : public IOPCEventServer
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE EnableConditionByArea2( 
            /* [in] */ DWORD dwNumAreas,
            /* [size_is][string][in] */ LPWSTR *pszAreas,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE EnableConditionBySource2( 
            /* [in] */ DWORD dwNumSources,
            /* [size_is][string][in] */ LPWSTR *pszSources,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE DisableConditionByArea2( 
            /* [in] */ DWORD dwNumAreas,
            /* [size_is][string][in] */ LPWSTR *pszAreas,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE DisableConditionBySource2( 
            /* [in] */ DWORD dwNumSources,
            /* [size_is][string][in] */ LPWSTR *pszSources,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetEnableStateByArea( 
            /* [in] */ DWORD dwNumAreas,
            /* [size_is][string][in] */ LPWSTR *pszAreas,
            /* [size_is][size_is][out] */ BOOL **pbEnabled,
            /* [size_is][size_is][out] */ BOOL **pbEffectivelyEnabled,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetEnableStateBySource( 
            /* [in] */ DWORD dwNumSources,
            /* [size_is][string][in] */ LPWSTR *pszSources,
            /* [size_is][size_is][out] */ BOOL **pbEnabled,
            /* [size_is][size_is][out] */ BOOL **pbEffectivelyEnabled,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCEventServer2Vtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCEventServer2 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCEventServer2 * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCEventServer2 * This);
        
        HRESULT ( STDMETHODCALLTYPE *GetStatus )( 
            IOPCEventServer2 * This,
            /* [out] */ OPCEVENTSERVERSTATUS **ppEventServerStatus);
        
        HRESULT ( STDMETHODCALLTYPE *CreateEventSubscription )( 
            IOPCEventServer2 * This,
            /* [in] */ BOOL bActive,
            /* [in] */ DWORD dwBufferTime,
            /* [in] */ DWORD dwMaxSize,
            /* [in] */ OPCHANDLE hClientSubscription,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk,
            /* [out] */ DWORD *pdwRevisedBufferTime,
            /* [out] */ DWORD *pdwRevisedMaxSize);
        
        HRESULT ( STDMETHODCALLTYPE *QueryAvailableFilters )( 
            IOPCEventServer2 * This,
            /* [out] */ DWORD *pdwFilterMask);
        
        HRESULT ( STDMETHODCALLTYPE *QueryEventCategories )( 
            IOPCEventServer2 * This,
            /* [in] */ DWORD dwEventType,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppdwEventCategories,
            /* [size_is][size_is][out] */ LPWSTR **ppszEventCategoryDescs);
        
        HRESULT ( STDMETHODCALLTYPE *QueryConditionNames )( 
            IOPCEventServer2 * This,
            /* [in] */ DWORD dwEventCategory,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ LPWSTR **ppszConditionNames);
        
        HRESULT ( STDMETHODCALLTYPE *QuerySubConditionNames )( 
            IOPCEventServer2 * This,
            /* [in] */ LPWSTR szConditionName,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ LPWSTR **ppszSubConditionNames);
        
        HRESULT ( STDMETHODCALLTYPE *QuerySourceConditions )( 
            IOPCEventServer2 * This,
            /* [in] */ LPWSTR szSource,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ LPWSTR **ppszConditionNames);
        
        HRESULT ( STDMETHODCALLTYPE *QueryEventAttributes )( 
            IOPCEventServer2 * This,
            /* [in] */ DWORD dwEventCategory,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppdwAttrIDs,
            /* [size_is][size_is][out] */ LPWSTR **ppszAttrDescs,
            /* [size_is][size_is][out] */ VARTYPE **ppvtAttrTypes);
        
        HRESULT ( STDMETHODCALLTYPE *TranslateToItemIDs )( 
            IOPCEventServer2 * This,
            /* [in] */ LPWSTR szSource,
            /* [in] */ DWORD dwEventCategory,
            /* [in] */ LPWSTR szConditionName,
            /* [in] */ LPWSTR szSubconditionName,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ DWORD *pdwAssocAttrIDs,
            /* [size_is][size_is][out] */ LPWSTR **ppszAttrItemIDs,
            /* [size_is][size_is][out] */ LPWSTR **ppszNodeNames,
            /* [size_is][size_is][out] */ CLSID **ppCLSIDs);
        
        HRESULT ( STDMETHODCALLTYPE *GetConditionState )( 
            IOPCEventServer2 * This,
            /* [in] */ LPWSTR szSource,
            /* [in] */ LPWSTR szConditionName,
            /* [in] */ DWORD dwNumEventAttrs,
            /* [size_is][in] */ DWORD *pdwAttributeIDs,
            /* [out] */ OPCCONDITIONSTATE **ppConditionState);
        
        HRESULT ( STDMETHODCALLTYPE *EnableConditionByArea )( 
            IOPCEventServer2 * This,
            /* [in] */ DWORD dwNumAreas,
            /* [size_is][in] */ LPWSTR *pszAreas);
        
        HRESULT ( STDMETHODCALLTYPE *EnableConditionBySource )( 
            IOPCEventServer2 * This,
            /* [in] */ DWORD dwNumSources,
            /* [size_is][in] */ LPWSTR *pszSources);
        
        HRESULT ( STDMETHODCALLTYPE *DisableConditionByArea )( 
            IOPCEventServer2 * This,
            /* [in] */ DWORD dwNumAreas,
            /* [size_is][in] */ LPWSTR *pszAreas);
        
        HRESULT ( STDMETHODCALLTYPE *DisableConditionBySource )( 
            IOPCEventServer2 * This,
            /* [in] */ DWORD dwNumSources,
            /* [size_is][in] */ LPWSTR *pszSources);
        
        HRESULT ( STDMETHODCALLTYPE *AckCondition )( 
            IOPCEventServer2 * This,
            /* [in] */ DWORD dwCount,
            /* [string][in] */ LPWSTR szAcknowledgerID,
            /* [string][in] */ LPWSTR szComment,
            /* [size_is][in] */ LPWSTR *pszSource,
            /* [size_is][in] */ LPWSTR *pszConditionName,
            /* [size_is][in] */ FILETIME *pftActiveTime,
            /* [size_is][in] */ DWORD *pdwCookie,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *CreateAreaBrowser )( 
            IOPCEventServer2 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk);
        
        HRESULT ( STDMETHODCALLTYPE *EnableConditionByArea2 )( 
            IOPCEventServer2 * This,
            /* [in] */ DWORD dwNumAreas,
            /* [size_is][string][in] */ LPWSTR *pszAreas,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *EnableConditionBySource2 )( 
            IOPCEventServer2 * This,
            /* [in] */ DWORD dwNumSources,
            /* [size_is][string][in] */ LPWSTR *pszSources,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *DisableConditionByArea2 )( 
            IOPCEventServer2 * This,
            /* [in] */ DWORD dwNumAreas,
            /* [size_is][string][in] */ LPWSTR *pszAreas,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *DisableConditionBySource2 )( 
            IOPCEventServer2 * This,
            /* [in] */ DWORD dwNumSources,
            /* [size_is][string][in] */ LPWSTR *pszSources,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *GetEnableStateByArea )( 
            IOPCEventServer2 * This,
            /* [in] */ DWORD dwNumAreas,
            /* [size_is][string][in] */ LPWSTR *pszAreas,
            /* [size_is][size_is][out] */ BOOL **pbEnabled,
            /* [size_is][size_is][out] */ BOOL **pbEffectivelyEnabled,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *GetEnableStateBySource )( 
            IOPCEventServer2 * This,
            /* [in] */ DWORD dwNumSources,
            /* [size_is][string][in] */ LPWSTR *pszSources,
            /* [size_is][size_is][out] */ BOOL **pbEnabled,
            /* [size_is][size_is][out] */ BOOL **pbEffectivelyEnabled,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        END_INTERFACE
    } IOPCEventServer2Vtbl;

    interface IOPCEventServer2
    {
        CONST_VTBL struct IOPCEventServer2Vtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCEventServer2_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCEventServer2_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCEventServer2_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCEventServer2_GetStatus(This,ppEventServerStatus)	\
    (This)->lpVtbl -> GetStatus(This,ppEventServerStatus)

#define IOPCEventServer2_CreateEventSubscription(This,bActive,dwBufferTime,dwMaxSize,hClientSubscription,riid,ppUnk,pdwRevisedBufferTime,pdwRevisedMaxSize)	\
    (This)->lpVtbl -> CreateEventSubscription(This,bActive,dwBufferTime,dwMaxSize,hClientSubscription,riid,ppUnk,pdwRevisedBufferTime,pdwRevisedMaxSize)

#define IOPCEventServer2_QueryAvailableFilters(This,pdwFilterMask)	\
    (This)->lpVtbl -> QueryAvailableFilters(This,pdwFilterMask)

#define IOPCEventServer2_QueryEventCategories(This,dwEventType,pdwCount,ppdwEventCategories,ppszEventCategoryDescs)	\
    (This)->lpVtbl -> QueryEventCategories(This,dwEventType,pdwCount,ppdwEventCategories,ppszEventCategoryDescs)

#define IOPCEventServer2_QueryConditionNames(This,dwEventCategory,pdwCount,ppszConditionNames)	\
    (This)->lpVtbl -> QueryConditionNames(This,dwEventCategory,pdwCount,ppszConditionNames)

#define IOPCEventServer2_QuerySubConditionNames(This,szConditionName,pdwCount,ppszSubConditionNames)	\
    (This)->lpVtbl -> QuerySubConditionNames(This,szConditionName,pdwCount,ppszSubConditionNames)

#define IOPCEventServer2_QuerySourceConditions(This,szSource,pdwCount,ppszConditionNames)	\
    (This)->lpVtbl -> QuerySourceConditions(This,szSource,pdwCount,ppszConditionNames)

#define IOPCEventServer2_QueryEventAttributes(This,dwEventCategory,pdwCount,ppdwAttrIDs,ppszAttrDescs,ppvtAttrTypes)	\
    (This)->lpVtbl -> QueryEventAttributes(This,dwEventCategory,pdwCount,ppdwAttrIDs,ppszAttrDescs,ppvtAttrTypes)

#define IOPCEventServer2_TranslateToItemIDs(This,szSource,dwEventCategory,szConditionName,szSubconditionName,dwCount,pdwAssocAttrIDs,ppszAttrItemIDs,ppszNodeNames,ppCLSIDs)	\
    (This)->lpVtbl -> TranslateToItemIDs(This,szSource,dwEventCategory,szConditionName,szSubconditionName,dwCount,pdwAssocAttrIDs,ppszAttrItemIDs,ppszNodeNames,ppCLSIDs)

#define IOPCEventServer2_GetConditionState(This,szSource,szConditionName,dwNumEventAttrs,pdwAttributeIDs,ppConditionState)	\
    (This)->lpVtbl -> GetConditionState(This,szSource,szConditionName,dwNumEventAttrs,pdwAttributeIDs,ppConditionState)

#define IOPCEventServer2_EnableConditionByArea(This,dwNumAreas,pszAreas)	\
    (This)->lpVtbl -> EnableConditionByArea(This,dwNumAreas,pszAreas)

#define IOPCEventServer2_EnableConditionBySource(This,dwNumSources,pszSources)	\
    (This)->lpVtbl -> EnableConditionBySource(This,dwNumSources,pszSources)

#define IOPCEventServer2_DisableConditionByArea(This,dwNumAreas,pszAreas)	\
    (This)->lpVtbl -> DisableConditionByArea(This,dwNumAreas,pszAreas)

#define IOPCEventServer2_DisableConditionBySource(This,dwNumSources,pszSources)	\
    (This)->lpVtbl -> DisableConditionBySource(This,dwNumSources,pszSources)

#define IOPCEventServer2_AckCondition(This,dwCount,szAcknowledgerID,szComment,pszSource,pszConditionName,pftActiveTime,pdwCookie,ppErrors)	\
    (This)->lpVtbl -> AckCondition(This,dwCount,szAcknowledgerID,szComment,pszSource,pszConditionName,pftActiveTime,pdwCookie,ppErrors)

#define IOPCEventServer2_CreateAreaBrowser(This,riid,ppUnk)	\
    (This)->lpVtbl -> CreateAreaBrowser(This,riid,ppUnk)


#define IOPCEventServer2_EnableConditionByArea2(This,dwNumAreas,pszAreas,ppErrors)	\
    (This)->lpVtbl -> EnableConditionByArea2(This,dwNumAreas,pszAreas,ppErrors)

#define IOPCEventServer2_EnableConditionBySource2(This,dwNumSources,pszSources,ppErrors)	\
    (This)->lpVtbl -> EnableConditionBySource2(This,dwNumSources,pszSources,ppErrors)

#define IOPCEventServer2_DisableConditionByArea2(This,dwNumAreas,pszAreas,ppErrors)	\
    (This)->lpVtbl -> DisableConditionByArea2(This,dwNumAreas,pszAreas,ppErrors)

#define IOPCEventServer2_DisableConditionBySource2(This,dwNumSources,pszSources,ppErrors)	\
    (This)->lpVtbl -> DisableConditionBySource2(This,dwNumSources,pszSources,ppErrors)

#define IOPCEventServer2_GetEnableStateByArea(This,dwNumAreas,pszAreas,pbEnabled,pbEffectivelyEnabled,ppErrors)	\
    (This)->lpVtbl -> GetEnableStateByArea(This,dwNumAreas,pszAreas,pbEnabled,pbEffectivelyEnabled,ppErrors)

#define IOPCEventServer2_GetEnableStateBySource(This,dwNumSources,pszSources,pbEnabled,pbEffectivelyEnabled,ppErrors)	\
    (This)->lpVtbl -> GetEnableStateBySource(This,dwNumSources,pszSources,pbEnabled,pbEffectivelyEnabled,ppErrors)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCEventServer2_EnableConditionByArea2_Proxy( 
    IOPCEventServer2 * This,
    /* [in] */ DWORD dwNumAreas,
    /* [size_is][string][in] */ LPWSTR *pszAreas,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCEventServer2_EnableConditionByArea2_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer2_EnableConditionBySource2_Proxy( 
    IOPCEventServer2 * This,
    /* [in] */ DWORD dwNumSources,
    /* [size_is][string][in] */ LPWSTR *pszSources,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCEventServer2_EnableConditionBySource2_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer2_DisableConditionByArea2_Proxy( 
    IOPCEventServer2 * This,
    /* [in] */ DWORD dwNumAreas,
    /* [size_is][string][in] */ LPWSTR *pszAreas,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCEventServer2_DisableConditionByArea2_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer2_DisableConditionBySource2_Proxy( 
    IOPCEventServer2 * This,
    /* [in] */ DWORD dwNumSources,
    /* [size_is][string][in] */ LPWSTR *pszSources,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCEventServer2_DisableConditionBySource2_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer2_GetEnableStateByArea_Proxy( 
    IOPCEventServer2 * This,
    /* [in] */ DWORD dwNumAreas,
    /* [size_is][string][in] */ LPWSTR *pszAreas,
    /* [size_is][size_is][out] */ BOOL **pbEnabled,
    /* [size_is][size_is][out] */ BOOL **pbEffectivelyEnabled,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCEventServer2_GetEnableStateByArea_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventServer2_GetEnableStateBySource_Proxy( 
    IOPCEventServer2 * This,
    /* [in] */ DWORD dwNumSources,
    /* [size_is][string][in] */ LPWSTR *pszSources,
    /* [size_is][size_is][out] */ BOOL **pbEnabled,
    /* [size_is][size_is][out] */ BOOL **pbEffectivelyEnabled,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCEventServer2_GetEnableStateBySource_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCEventServer2_INTERFACE_DEFINED__ */


#ifndef __IOPCEventSubscriptionMgt2_INTERFACE_DEFINED__
#define __IOPCEventSubscriptionMgt2_INTERFACE_DEFINED__

/* interface IOPCEventSubscriptionMgt2 */
/* [object][unique][uuid] */ 


EXTERN_C const IID IID_IOPCEventSubscriptionMgt2;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("94C955DC-3684-4ccb-AFAB-F898CE19AAC3")
    IOPCEventSubscriptionMgt2 : public IOPCEventSubscriptionMgt
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE SetKeepAlive( 
            /* [in] */ DWORD dwKeepAliveTime,
            /* [out] */ DWORD *pdwRevisedKeepAliveTime) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetKeepAlive( 
            /* [out] */ DWORD *pdwKeepAliveTime) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCEventSubscriptionMgt2Vtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCEventSubscriptionMgt2 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCEventSubscriptionMgt2 * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCEventSubscriptionMgt2 * This);
        
        HRESULT ( STDMETHODCALLTYPE *SetFilter )( 
            IOPCEventSubscriptionMgt2 * This,
            /* [in] */ DWORD dwEventType,
            /* [in] */ DWORD dwNumCategories,
            /* [size_is][in] */ DWORD *pdwEventCategories,
            /* [in] */ DWORD dwLowSeverity,
            /* [in] */ DWORD dwHighSeverity,
            /* [in] */ DWORD dwNumAreas,
            /* [size_is][in] */ LPWSTR *pszAreaList,
            /* [in] */ DWORD dwNumSources,
            /* [size_is][in] */ LPWSTR *pszSourceList);
        
        HRESULT ( STDMETHODCALLTYPE *GetFilter )( 
            IOPCEventSubscriptionMgt2 * This,
            /* [out] */ DWORD *pdwEventType,
            /* [out] */ DWORD *pdwNumCategories,
            /* [size_is][size_is][out] */ DWORD **ppdwEventCategories,
            /* [out] */ DWORD *pdwLowSeverity,
            /* [out] */ DWORD *pdwHighSeverity,
            /* [out] */ DWORD *pdwNumAreas,
            /* [size_is][size_is][out] */ LPWSTR **ppszAreaList,
            /* [out] */ DWORD *pdwNumSources,
            /* [size_is][size_is][out] */ LPWSTR **ppszSourceList);
        
        HRESULT ( STDMETHODCALLTYPE *SelectReturnedAttributes )( 
            IOPCEventSubscriptionMgt2 * This,
            /* [in] */ DWORD dwEventCategory,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ DWORD *dwAttributeIDs);
        
        HRESULT ( STDMETHODCALLTYPE *GetReturnedAttributes )( 
            IOPCEventSubscriptionMgt2 * This,
            /* [in] */ DWORD dwEventCategory,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppdwAttributeIDs);
        
        HRESULT ( STDMETHODCALLTYPE *Refresh )( 
            IOPCEventSubscriptionMgt2 * This,
            /* [in] */ DWORD dwConnection);
        
        HRESULT ( STDMETHODCALLTYPE *CancelRefresh )( 
            IOPCEventSubscriptionMgt2 * This,
            /* [in] */ DWORD dwConnection);
        
        HRESULT ( STDMETHODCALLTYPE *GetState )( 
            IOPCEventSubscriptionMgt2 * This,
            /* [out] */ BOOL *pbActive,
            /* [out] */ DWORD *pdwBufferTime,
            /* [out] */ DWORD *pdwMaxSize,
            /* [out] */ OPCHANDLE *phClientSubscription);
        
        HRESULT ( STDMETHODCALLTYPE *SetState )( 
            IOPCEventSubscriptionMgt2 * This,
            /* [in][unique] */ BOOL *pbActive,
            /* [in][unique] */ DWORD *pdwBufferTime,
            /* [in][unique] */ DWORD *pdwMaxSize,
            /* [in] */ OPCHANDLE hClientSubscription,
            /* [out] */ DWORD *pdwRevisedBufferTime,
            /* [out] */ DWORD *pdwRevisedMaxSize);
        
        HRESULT ( STDMETHODCALLTYPE *SetKeepAlive )( 
            IOPCEventSubscriptionMgt2 * This,
            /* [in] */ DWORD dwKeepAliveTime,
            /* [out] */ DWORD *pdwRevisedKeepAliveTime);
        
        HRESULT ( STDMETHODCALLTYPE *GetKeepAlive )( 
            IOPCEventSubscriptionMgt2 * This,
            /* [out] */ DWORD *pdwKeepAliveTime);
        
        END_INTERFACE
    } IOPCEventSubscriptionMgt2Vtbl;

    interface IOPCEventSubscriptionMgt2
    {
        CONST_VTBL struct IOPCEventSubscriptionMgt2Vtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCEventSubscriptionMgt2_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCEventSubscriptionMgt2_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCEventSubscriptionMgt2_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCEventSubscriptionMgt2_SetFilter(This,dwEventType,dwNumCategories,pdwEventCategories,dwLowSeverity,dwHighSeverity,dwNumAreas,pszAreaList,dwNumSources,pszSourceList)	\
    (This)->lpVtbl -> SetFilter(This,dwEventType,dwNumCategories,pdwEventCategories,dwLowSeverity,dwHighSeverity,dwNumAreas,pszAreaList,dwNumSources,pszSourceList)

#define IOPCEventSubscriptionMgt2_GetFilter(This,pdwEventType,pdwNumCategories,ppdwEventCategories,pdwLowSeverity,pdwHighSeverity,pdwNumAreas,ppszAreaList,pdwNumSources,ppszSourceList)	\
    (This)->lpVtbl -> GetFilter(This,pdwEventType,pdwNumCategories,ppdwEventCategories,pdwLowSeverity,pdwHighSeverity,pdwNumAreas,ppszAreaList,pdwNumSources,ppszSourceList)

#define IOPCEventSubscriptionMgt2_SelectReturnedAttributes(This,dwEventCategory,dwCount,dwAttributeIDs)	\
    (This)->lpVtbl -> SelectReturnedAttributes(This,dwEventCategory,dwCount,dwAttributeIDs)

#define IOPCEventSubscriptionMgt2_GetReturnedAttributes(This,dwEventCategory,pdwCount,ppdwAttributeIDs)	\
    (This)->lpVtbl -> GetReturnedAttributes(This,dwEventCategory,pdwCount,ppdwAttributeIDs)

#define IOPCEventSubscriptionMgt2_Refresh(This,dwConnection)	\
    (This)->lpVtbl -> Refresh(This,dwConnection)

#define IOPCEventSubscriptionMgt2_CancelRefresh(This,dwConnection)	\
    (This)->lpVtbl -> CancelRefresh(This,dwConnection)

#define IOPCEventSubscriptionMgt2_GetState(This,pbActive,pdwBufferTime,pdwMaxSize,phClientSubscription)	\
    (This)->lpVtbl -> GetState(This,pbActive,pdwBufferTime,pdwMaxSize,phClientSubscription)

#define IOPCEventSubscriptionMgt2_SetState(This,pbActive,pdwBufferTime,pdwMaxSize,hClientSubscription,pdwRevisedBufferTime,pdwRevisedMaxSize)	\
    (This)->lpVtbl -> SetState(This,pbActive,pdwBufferTime,pdwMaxSize,hClientSubscription,pdwRevisedBufferTime,pdwRevisedMaxSize)


#define IOPCEventSubscriptionMgt2_SetKeepAlive(This,dwKeepAliveTime,pdwRevisedKeepAliveTime)	\
    (This)->lpVtbl -> SetKeepAlive(This,dwKeepAliveTime,pdwRevisedKeepAliveTime)

#define IOPCEventSubscriptionMgt2_GetKeepAlive(This,pdwKeepAliveTime)	\
    (This)->lpVtbl -> GetKeepAlive(This,pdwKeepAliveTime)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCEventSubscriptionMgt2_SetKeepAlive_Proxy( 
    IOPCEventSubscriptionMgt2 * This,
    /* [in] */ DWORD dwKeepAliveTime,
    /* [out] */ DWORD *pdwRevisedKeepAliveTime);


void __RPC_STUB IOPCEventSubscriptionMgt2_SetKeepAlive_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCEventSubscriptionMgt2_GetKeepAlive_Proxy( 
    IOPCEventSubscriptionMgt2 * This,
    /* [out] */ DWORD *pdwKeepAliveTime);


void __RPC_STUB IOPCEventSubscriptionMgt2_GetKeepAlive_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCEventSubscriptionMgt2_INTERFACE_DEFINED__ */



#ifndef __OPC_AE_LIBRARY_DEFINED__
#define __OPC_AE_LIBRARY_DEFINED__

/* library OPC_AE */
/* [helpstring][version][uuid] */ 










EXTERN_C const IID LIBID_OPC_AE;


#ifndef __OPCAE_Constants_MODULE_DEFINED__
#define __OPCAE_Constants_MODULE_DEFINED__


/* module OPCAE_Constants */


const LPCWSTR OPC_CATEGORY_DESCRIPTION_AE10	=	L"OPC Alarm & Event Server Version 1.0";

const DWORD OPC_CONDITION_ENABLED	=	0x1;

const DWORD OPC_CONDITION_ACTIVE	=	0x2;

const DWORD OPC_CONDITION_ACKED	=	0x4;

const DWORD OPC_CHANGE_ACTIVE_STATE	=	0x1;

const DWORD OPC_CHANGE_ACK_STATE	=	0x2;

const DWORD OPC_CHANGE_ENABLE_STATE	=	0x4;

const DWORD OPC_CHANGE_QUALITY	=	0x8;

const DWORD OPC_CHANGE_SEVERITY	=	0x10;

const DWORD OPC_CHANGE_SUBCONDITION	=	0x20;

const DWORD OPC_CHANGE_MESSAGE	=	0x40;

const DWORD OPC_CHANGE_ATTRIBUTE	=	0x80;

const DWORD OPC_SIMPLE_EVENT	=	0x1;

const DWORD OPC_TRACKING_EVENT	=	0x2;

const DWORD OPC_CONDITION_EVENT	=	0x4;

const DWORD OPC_ALL_EVENTS	=	0x7;

const DWORD OPC_FILTER_BY_EVENT	=	0x1;

const DWORD OPC_FILTER_BY_CATEGORY	=	0x2;

const DWORD OPC_FILTER_BY_SEVERITY	=	0x4;

const DWORD OPC_FILTER_BY_AREA	=	0x8;

const DWORD OPC_FILTER_BY_SOURCE	=	0x10;

#endif /* __OPCAE_Constants_MODULE_DEFINED__ */
#endif /* __OPC_AE_LIBRARY_DEFINED__ */

/* Additional Prototypes for ALL interfaces */

unsigned long             __RPC_USER  VARIANT_UserSize(     unsigned long *, unsigned long            , VARIANT * ); 
unsigned char * __RPC_USER  VARIANT_UserMarshal(  unsigned long *, unsigned char *, VARIANT * ); 
unsigned char * __RPC_USER  VARIANT_UserUnmarshal(unsigned long *, unsigned char *, VARIANT * ); 
void                      __RPC_USER  VARIANT_UserFree(     unsigned long *, VARIANT * ); 

/* end of Additional Prototypes */

#ifdef __cplusplus
}
#endif

#endif
