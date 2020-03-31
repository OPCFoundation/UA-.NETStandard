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
/* at Wed Sep 01 11:47:41 2004
 */
/* Compiler settings for .\OpcDx.idl:
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

#ifndef __OpcDx_h__
#define __OpcDx_h__

#if defined(_MSC_VER) && (_MSC_VER >= 1020)
#pragma once
#endif

/* Forward Declarations */ 

#ifndef __CATID_OPCDXServer10_FWD_DEFINED__
#define __CATID_OPCDXServer10_FWD_DEFINED__
typedef interface CATID_OPCDXServer10 CATID_OPCDXServer10;
#endif 	/* __CATID_OPCDXServer10_FWD_DEFINED__ */


#ifndef __IOPCConfiguration_FWD_DEFINED__
#define __IOPCConfiguration_FWD_DEFINED__
typedef interface IOPCConfiguration IOPCConfiguration;
#endif 	/* __IOPCConfiguration_FWD_DEFINED__ */


#ifndef __CATID_OPCDXServer10_FWD_DEFINED__
#define __CATID_OPCDXServer10_FWD_DEFINED__
typedef interface CATID_OPCDXServer10 CATID_OPCDXServer10;
#endif 	/* __CATID_OPCDXServer10_FWD_DEFINED__ */


#ifndef __IOPCConfiguration_FWD_DEFINED__
#define __IOPCConfiguration_FWD_DEFINED__
typedef interface IOPCConfiguration IOPCConfiguration;
#endif 	/* __IOPCConfiguration_FWD_DEFINED__ */


/* header files for imported files */
#include "oaidl.h"
#include "ocidl.h"

#ifdef __cplusplus
extern "C"{
#endif 

void * __RPC_USER MIDL_user_allocate(size_t);
void __RPC_USER MIDL_user_free( void * ); 

#ifndef __CATID_OPCDXServer10_INTERFACE_DEFINED__
#define __CATID_OPCDXServer10_INTERFACE_DEFINED__

/* interface CATID_OPCDXServer10 */
/* [object][uuid] */ 


EXTERN_C const IID IID_CATID_OPCDXServer10;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("A0C85BB8-4161-4fd6-8655-BB584601C9E0")
    CATID_OPCDXServer10 : public IUnknown
    {
    public:
    };
    
#else 	/* C style interface */

    typedef struct CATID_OPCDXServer10Vtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            CATID_OPCDXServer10 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            CATID_OPCDXServer10 * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            CATID_OPCDXServer10 * This);
        
        END_INTERFACE
    } CATID_OPCDXServer10Vtbl;

    interface CATID_OPCDXServer10
    {
        CONST_VTBL struct CATID_OPCDXServer10Vtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define CATID_OPCDXServer10_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define CATID_OPCDXServer10_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define CATID_OPCDXServer10_Release(This)	\
    (This)->lpVtbl -> Release(This)


#endif /* COBJMACROS */


#endif 	/* C style interface */




#endif 	/* __CATID_OPCDXServer10_INTERFACE_DEFINED__ */


/* interface __MIDL_itf_OpcDx_0258 */
/* [local] */ 

#define CATID_OPCDXServer10 IID_CATID_OPCDXServer10
typedef struct tagOpcDxItemIdentifier
    {
    LPWSTR szItemPath;
    LPWSTR szItemName;
    LPWSTR szVersion;
    DWORD dwReserved;
    } 	OpcDxItemIdentifier;

typedef struct tagOpcDxIdentifiedResult
    {
    LPWSTR szItemPath;
    LPWSTR szItemName;
    LPWSTR szVersion;
    HRESULT hResultCode;
    } 	OpcDxIdentifiedResult;

typedef struct tagOpcDxGeneralResponse
    {
    LPWSTR szConfigurationVersion;
    DWORD dwCount;
    /* [size_is] */ OpcDxIdentifiedResult *pIdentifiedResults;
    DWORD dwReserved;
    } 	OpcDxGeneralResponse;

typedef struct tagOpcDxSourceServer
    {
    DWORD dwMask;
    LPWSTR szItemPath;
    LPWSTR szItemName;
    LPWSTR szVersion;
    LPWSTR szName;
    LPWSTR szDescription;
    LPWSTR szServerType;
    LPWSTR szServerURL;
    BOOL bDefaultSourceServerConnected;
    DWORD dwReserved;
    } 	OpcDxSourceServer;

typedef struct tagOpcDxConnection
    {
    DWORD dwMask;
    LPWSTR szItemPath;
    LPWSTR szItemName;
    LPWSTR szVersion;
    DWORD dwBrowsePathCount;
    /* [size_is] */ LPWSTR *pszBrowsePaths;
    LPWSTR szName;
    LPWSTR szDescription;
    LPWSTR szKeyword;
    BOOL bDefaultSourceItemConnected;
    BOOL bDefaultTargetItemConnected;
    BOOL bDefaultOverridden;
    VARIANT vDefaultOverrideValue;
    VARIANT vSubstituteValue;
    BOOL bEnableSubstituteValue;
    LPWSTR szTargetItemPath;
    LPWSTR szTargetItemName;
    LPWSTR szSourceServerName;
    LPWSTR szSourceItemPath;
    LPWSTR szSourceItemName;
    DWORD dwSourceItemQueueSize;
    DWORD dwUpdateRate;
    FLOAT fltDeadBand;
    LPWSTR szVendorData;
    } 	OpcDxConnection;



extern RPC_IF_HANDLE __MIDL_itf_OpcDx_0258_v0_0_c_ifspec;
extern RPC_IF_HANDLE __MIDL_itf_OpcDx_0258_v0_0_s_ifspec;

#ifndef __IOPCConfiguration_INTERFACE_DEFINED__
#define __IOPCConfiguration_INTERFACE_DEFINED__

/* interface IOPCConfiguration */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCConfiguration;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("C130D281-F4AA-4779-8846-C2C4CB444F2A")
    IOPCConfiguration : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE GetServers( 
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ OpcDxSourceServer **ppServers) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE AddServers( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OpcDxSourceServer *pServers,
            /* [out] */ OpcDxGeneralResponse *pResponse) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ModifyServers( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OpcDxSourceServer *pServers,
            /* [out] */ OpcDxGeneralResponse *pResponse) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE DeleteServers( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OpcDxItemIdentifier *pServers,
            /* [out] */ OpcDxGeneralResponse *pResponse) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE CopyDefaultServerAttributes( 
            /* [in] */ BOOL bConfigToStatus,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OpcDxItemIdentifier *pServers,
            /* [out] */ OpcDxGeneralResponse *pResponse) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE QueryDXConnections( 
            /* [in] */ LPWSTR szBrowsePath,
            /* [in] */ DWORD dwNoOfMasks,
            /* [size_is][in] */ OpcDxConnection *pDXConnectionMasks,
            /* [in] */ BOOL bRecursive,
            /* [size_is][size_is][out] */ HRESULT **ppErrors,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ OpcDxConnection **ppConnections) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE AddDXConnections( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OpcDxConnection *pConnections,
            /* [out] */ OpcDxGeneralResponse *pResponse) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE UpdateDXConnections( 
            /* [in] */ LPWSTR szBrowsePath,
            /* [in] */ DWORD dwNoOfMasks,
            /* [size_is][in] */ OpcDxConnection *pDXConnectionMasks,
            /* [in] */ BOOL bRecursive,
            /* [in] */ OpcDxConnection *pDXConnectionDefinition,
            /* [size_is][size_is][out] */ HRESULT **ppErrors,
            /* [out] */ OpcDxGeneralResponse *pResponse) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ModifyDXConnections( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OpcDxConnection *pDXConnectionDefinitions,
            /* [out] */ OpcDxGeneralResponse *pResponse) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE DeleteDXConnections( 
            /* [in] */ LPWSTR szBrowsePath,
            /* [in] */ DWORD dwNoOfMasks,
            /* [size_is][in] */ OpcDxConnection *pDXConnectionMasks,
            /* [in] */ BOOL bRecursive,
            /* [size_is][size_is][out] */ HRESULT **ppErrors,
            /* [out] */ OpcDxGeneralResponse *pResponse) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE CopyDXConnectionDefaultAttributes( 
            /* [in] */ BOOL bConfigToStatus,
            /* [in] */ LPWSTR szBrowsePath,
            /* [in] */ DWORD dwNoOfMasks,
            /* [size_is][in] */ OpcDxConnection *pDXConnectionMasks,
            /* [in] */ BOOL bRecursive,
            /* [size_is][size_is][out] */ HRESULT **ppErrors,
            /* [out] */ OpcDxGeneralResponse *pResponse) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ResetConfiguration( 
            /* [in] */ LPWSTR szConfigurationVersion,
            /* [out] */ LPWSTR *pszConfigurationVersion) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCConfigurationVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCConfiguration * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCConfiguration * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCConfiguration * This);
        
        HRESULT ( STDMETHODCALLTYPE *GetServers )( 
            IOPCConfiguration * This,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ OpcDxSourceServer **ppServers);
        
        HRESULT ( STDMETHODCALLTYPE *AddServers )( 
            IOPCConfiguration * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OpcDxSourceServer *pServers,
            /* [out] */ OpcDxGeneralResponse *pResponse);
        
        HRESULT ( STDMETHODCALLTYPE *ModifyServers )( 
            IOPCConfiguration * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OpcDxSourceServer *pServers,
            /* [out] */ OpcDxGeneralResponse *pResponse);
        
        HRESULT ( STDMETHODCALLTYPE *DeleteServers )( 
            IOPCConfiguration * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OpcDxItemIdentifier *pServers,
            /* [out] */ OpcDxGeneralResponse *pResponse);
        
        HRESULT ( STDMETHODCALLTYPE *CopyDefaultServerAttributes )( 
            IOPCConfiguration * This,
            /* [in] */ BOOL bConfigToStatus,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OpcDxItemIdentifier *pServers,
            /* [out] */ OpcDxGeneralResponse *pResponse);
        
        HRESULT ( STDMETHODCALLTYPE *QueryDXConnections )( 
            IOPCConfiguration * This,
            /* [in] */ LPWSTR szBrowsePath,
            /* [in] */ DWORD dwNoOfMasks,
            /* [size_is][in] */ OpcDxConnection *pDXConnectionMasks,
            /* [in] */ BOOL bRecursive,
            /* [size_is][size_is][out] */ HRESULT **ppErrors,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ OpcDxConnection **ppConnections);
        
        HRESULT ( STDMETHODCALLTYPE *AddDXConnections )( 
            IOPCConfiguration * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OpcDxConnection *pConnections,
            /* [out] */ OpcDxGeneralResponse *pResponse);
        
        HRESULT ( STDMETHODCALLTYPE *UpdateDXConnections )( 
            IOPCConfiguration * This,
            /* [in] */ LPWSTR szBrowsePath,
            /* [in] */ DWORD dwNoOfMasks,
            /* [size_is][in] */ OpcDxConnection *pDXConnectionMasks,
            /* [in] */ BOOL bRecursive,
            /* [in] */ OpcDxConnection *pDXConnectionDefinition,
            /* [size_is][size_is][out] */ HRESULT **ppErrors,
            /* [out] */ OpcDxGeneralResponse *pResponse);
        
        HRESULT ( STDMETHODCALLTYPE *ModifyDXConnections )( 
            IOPCConfiguration * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OpcDxConnection *pDXConnectionDefinitions,
            /* [out] */ OpcDxGeneralResponse *pResponse);
        
        HRESULT ( STDMETHODCALLTYPE *DeleteDXConnections )( 
            IOPCConfiguration * This,
            /* [in] */ LPWSTR szBrowsePath,
            /* [in] */ DWORD dwNoOfMasks,
            /* [size_is][in] */ OpcDxConnection *pDXConnectionMasks,
            /* [in] */ BOOL bRecursive,
            /* [size_is][size_is][out] */ HRESULT **ppErrors,
            /* [out] */ OpcDxGeneralResponse *pResponse);
        
        HRESULT ( STDMETHODCALLTYPE *CopyDXConnectionDefaultAttributes )( 
            IOPCConfiguration * This,
            /* [in] */ BOOL bConfigToStatus,
            /* [in] */ LPWSTR szBrowsePath,
            /* [in] */ DWORD dwNoOfMasks,
            /* [size_is][in] */ OpcDxConnection *pDXConnectionMasks,
            /* [in] */ BOOL bRecursive,
            /* [size_is][size_is][out] */ HRESULT **ppErrors,
            /* [out] */ OpcDxGeneralResponse *pResponse);
        
        HRESULT ( STDMETHODCALLTYPE *ResetConfiguration )( 
            IOPCConfiguration * This,
            /* [in] */ LPWSTR szConfigurationVersion,
            /* [out] */ LPWSTR *pszConfigurationVersion);
        
        END_INTERFACE
    } IOPCConfigurationVtbl;

    interface IOPCConfiguration
    {
        CONST_VTBL struct IOPCConfigurationVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCConfiguration_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCConfiguration_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCConfiguration_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCConfiguration_GetServers(This,pdwCount,ppServers)	\
    (This)->lpVtbl -> GetServers(This,pdwCount,ppServers)

#define IOPCConfiguration_AddServers(This,dwCount,pServers,pResponse)	\
    (This)->lpVtbl -> AddServers(This,dwCount,pServers,pResponse)

#define IOPCConfiguration_ModifyServers(This,dwCount,pServers,pResponse)	\
    (This)->lpVtbl -> ModifyServers(This,dwCount,pServers,pResponse)

#define IOPCConfiguration_DeleteServers(This,dwCount,pServers,pResponse)	\
    (This)->lpVtbl -> DeleteServers(This,dwCount,pServers,pResponse)

#define IOPCConfiguration_CopyDefaultServerAttributes(This,bConfigToStatus,dwCount,pServers,pResponse)	\
    (This)->lpVtbl -> CopyDefaultServerAttributes(This,bConfigToStatus,dwCount,pServers,pResponse)

#define IOPCConfiguration_QueryDXConnections(This,szBrowsePath,dwNoOfMasks,pDXConnectionMasks,bRecursive,ppErrors,pdwCount,ppConnections)	\
    (This)->lpVtbl -> QueryDXConnections(This,szBrowsePath,dwNoOfMasks,pDXConnectionMasks,bRecursive,ppErrors,pdwCount,ppConnections)

#define IOPCConfiguration_AddDXConnections(This,dwCount,pConnections,pResponse)	\
    (This)->lpVtbl -> AddDXConnections(This,dwCount,pConnections,pResponse)

#define IOPCConfiguration_UpdateDXConnections(This,szBrowsePath,dwNoOfMasks,pDXConnectionMasks,bRecursive,pDXConnectionDefinition,ppErrors,pResponse)	\
    (This)->lpVtbl -> UpdateDXConnections(This,szBrowsePath,dwNoOfMasks,pDXConnectionMasks,bRecursive,pDXConnectionDefinition,ppErrors,pResponse)

#define IOPCConfiguration_ModifyDXConnections(This,dwCount,pDXConnectionDefinitions,pResponse)	\
    (This)->lpVtbl -> ModifyDXConnections(This,dwCount,pDXConnectionDefinitions,pResponse)

#define IOPCConfiguration_DeleteDXConnections(This,szBrowsePath,dwNoOfMasks,pDXConnectionMasks,bRecursive,ppErrors,pResponse)	\
    (This)->lpVtbl -> DeleteDXConnections(This,szBrowsePath,dwNoOfMasks,pDXConnectionMasks,bRecursive,ppErrors,pResponse)

#define IOPCConfiguration_CopyDXConnectionDefaultAttributes(This,bConfigToStatus,szBrowsePath,dwNoOfMasks,pDXConnectionMasks,bRecursive,ppErrors,pResponse)	\
    (This)->lpVtbl -> CopyDXConnectionDefaultAttributes(This,bConfigToStatus,szBrowsePath,dwNoOfMasks,pDXConnectionMasks,bRecursive,ppErrors,pResponse)

#define IOPCConfiguration_ResetConfiguration(This,szConfigurationVersion,pszConfigurationVersion)	\
    (This)->lpVtbl -> ResetConfiguration(This,szConfigurationVersion,pszConfigurationVersion)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCConfiguration_GetServers_Proxy( 
    IOPCConfiguration * This,
    /* [out] */ DWORD *pdwCount,
    /* [size_is][size_is][out] */ OpcDxSourceServer **ppServers);


void __RPC_STUB IOPCConfiguration_GetServers_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCConfiguration_AddServers_Proxy( 
    IOPCConfiguration * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OpcDxSourceServer *pServers,
    /* [out] */ OpcDxGeneralResponse *pResponse);


void __RPC_STUB IOPCConfiguration_AddServers_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCConfiguration_ModifyServers_Proxy( 
    IOPCConfiguration * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OpcDxSourceServer *pServers,
    /* [out] */ OpcDxGeneralResponse *pResponse);


void __RPC_STUB IOPCConfiguration_ModifyServers_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCConfiguration_DeleteServers_Proxy( 
    IOPCConfiguration * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OpcDxItemIdentifier *pServers,
    /* [out] */ OpcDxGeneralResponse *pResponse);


void __RPC_STUB IOPCConfiguration_DeleteServers_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCConfiguration_CopyDefaultServerAttributes_Proxy( 
    IOPCConfiguration * This,
    /* [in] */ BOOL bConfigToStatus,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OpcDxItemIdentifier *pServers,
    /* [out] */ OpcDxGeneralResponse *pResponse);


void __RPC_STUB IOPCConfiguration_CopyDefaultServerAttributes_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCConfiguration_QueryDXConnections_Proxy( 
    IOPCConfiguration * This,
    /* [in] */ LPWSTR szBrowsePath,
    /* [in] */ DWORD dwNoOfMasks,
    /* [size_is][in] */ OpcDxConnection *pDXConnectionMasks,
    /* [in] */ BOOL bRecursive,
    /* [size_is][size_is][out] */ HRESULT **ppErrors,
    /* [out] */ DWORD *pdwCount,
    /* [size_is][size_is][out] */ OpcDxConnection **ppConnections);


void __RPC_STUB IOPCConfiguration_QueryDXConnections_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCConfiguration_AddDXConnections_Proxy( 
    IOPCConfiguration * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OpcDxConnection *pConnections,
    /* [out] */ OpcDxGeneralResponse *pResponse);


void __RPC_STUB IOPCConfiguration_AddDXConnections_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCConfiguration_UpdateDXConnections_Proxy( 
    IOPCConfiguration * This,
    /* [in] */ LPWSTR szBrowsePath,
    /* [in] */ DWORD dwNoOfMasks,
    /* [size_is][in] */ OpcDxConnection *pDXConnectionMasks,
    /* [in] */ BOOL bRecursive,
    /* [in] */ OpcDxConnection *pDXConnectionDefinition,
    /* [size_is][size_is][out] */ HRESULT **ppErrors,
    /* [out] */ OpcDxGeneralResponse *pResponse);


void __RPC_STUB IOPCConfiguration_UpdateDXConnections_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCConfiguration_ModifyDXConnections_Proxy( 
    IOPCConfiguration * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OpcDxConnection *pDXConnectionDefinitions,
    /* [out] */ OpcDxGeneralResponse *pResponse);


void __RPC_STUB IOPCConfiguration_ModifyDXConnections_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCConfiguration_DeleteDXConnections_Proxy( 
    IOPCConfiguration * This,
    /* [in] */ LPWSTR szBrowsePath,
    /* [in] */ DWORD dwNoOfMasks,
    /* [size_is][in] */ OpcDxConnection *pDXConnectionMasks,
    /* [in] */ BOOL bRecursive,
    /* [size_is][size_is][out] */ HRESULT **ppErrors,
    /* [out] */ OpcDxGeneralResponse *pResponse);


void __RPC_STUB IOPCConfiguration_DeleteDXConnections_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCConfiguration_CopyDXConnectionDefaultAttributes_Proxy( 
    IOPCConfiguration * This,
    /* [in] */ BOOL bConfigToStatus,
    /* [in] */ LPWSTR szBrowsePath,
    /* [in] */ DWORD dwNoOfMasks,
    /* [size_is][in] */ OpcDxConnection *pDXConnectionMasks,
    /* [in] */ BOOL bRecursive,
    /* [size_is][size_is][out] */ HRESULT **ppErrors,
    /* [out] */ OpcDxGeneralResponse *pResponse);


void __RPC_STUB IOPCConfiguration_CopyDXConnectionDefaultAttributes_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCConfiguration_ResetConfiguration_Proxy( 
    IOPCConfiguration * This,
    /* [in] */ LPWSTR szConfigurationVersion,
    /* [out] */ LPWSTR *pszConfigurationVersion);


void __RPC_STUB IOPCConfiguration_ResetConfiguration_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCConfiguration_INTERFACE_DEFINED__ */



#ifndef __OpcDxLib_LIBRARY_DEFINED__
#define __OpcDxLib_LIBRARY_DEFINED__

/* library OpcDxLib */
/* [helpstring][version][uuid] */ 



typedef 
enum tagOpcDxServerType
    {	OpcDxServerType_COM_DA10	= 1,
	OpcDxServerType_COM_DA204	= OpcDxServerType_COM_DA10 + 1,
	OpcDxServerType_COM_DA205	= OpcDxServerType_COM_DA204 + 1,
	OpcDxServerType_COM_DA30	= OpcDxServerType_COM_DA205 + 1,
	OpcDxServerType_XML_DA10	= OpcDxServerType_COM_DA30 + 1
    } 	OpcDxServerType;

typedef 
enum tagOpcDxServerState
    {	OpcDxServerState_RUNNING	= 1,
	OpcDxServerState_FAILED	= OpcDxServerState_RUNNING + 1,
	OpcDxServerState_NOCONFIG	= OpcDxServerState_FAILED + 1,
	OpcDxServerState_SUSPENDED	= OpcDxServerState_NOCONFIG + 1,
	OpcDxServerState_TEST	= OpcDxServerState_SUSPENDED + 1,
	OpcDxServerState_COMM_FAULT	= OpcDxServerState_TEST + 1,
	OpcDxServerState_UNKNOWN	= OpcDxServerState_COMM_FAULT + 1
    } 	OpcDxServerState;

typedef 
enum tagOpcDxConnectionState
    {	OpcDxConnectionState_INITIALIZING	= 1,
	OpcDxConnectionState_OPERATIONAL	= OpcDxConnectionState_INITIALIZING + 1,
	OpcDxConnectionState_DEACTIVATED	= OpcDxConnectionState_OPERATIONAL + 1,
	OpcDxConnectionState_SOURCE_SERVER_NOT_CONNECTED	= OpcDxConnectionState_DEACTIVATED + 1,
	OpcDxConnectionState_SUBSCRIPTION_FAILED	= OpcDxConnectionState_SOURCE_SERVER_NOT_CONNECTED + 1,
	OpcDxConnectionState_TARGET_ITEM_NOT_FOUND	= OpcDxConnectionState_SUBSCRIPTION_FAILED + 1
    } 	OpcDxConnectionState;

typedef 
enum tagOpcDxConnectStatus
    {	OpcDxConnectStatus_CONNECTED	= 1,
	OpcDxConnectStatus_DISCONNECTED	= OpcDxConnectStatus_CONNECTED + 1,
	OpcDxConnectStatus_CONNECTING	= OpcDxConnectStatus_DISCONNECTED + 1,
	OpcDxConnectStatus_FAILED	= OpcDxConnectStatus_CONNECTING + 1
    } 	OpcDxConnectStatus;

typedef 
enum tagOpcDxMask
    {	OpcDxMask_None	= 0,
	OpcDxMask_ItemPath	= 0x1,
	OpcDxMask_ItemName	= 0x2,
	OpcDxMask_Version	= 0x4,
	OpcDxMask_BrowsePaths	= 0x8,
	OpcDxMask_Name	= 0x10,
	OpcDxMask_Description	= 0x20,
	OpcDxMask_Keyword	= 0x40,
	OpcDxMask_DefaultSourceItemConnected	= 0x80,
	OpcDxMask_DefaultTargetItemConnected	= 0x100,
	OpcDxMask_DefaultOverridden	= 0x200,
	OpcDxMask_DefaultOverrideValue	= 0x400,
	OpcDxMask_SubstituteValue	= 0x800,
	OpcDxMask_EnableSubstituteValue	= 0x1000,
	OpcDxMask_TargetItemPath	= 0x2000,
	OpcDxMask_TargetItemName	= 0x4000,
	OpcDxMask_SourceServerName	= 0x8000,
	OpcDxMask_SourceItemPath	= 0x10000,
	OpcDxMask_SourceItemName	= 0x20000,
	OpcDxMask_SourceItemQueueSize	= 0x40000,
	OpcDxMask_UpdateRate	= 0x80000,
	OpcDxMask_DeadBand	= 0x100000,
	OpcDxMask_VendorData	= 0x200000,
	OpcDxMask_ServerType	= 0x400000,
	OpcDxMask_ServerURL	= 0x800000,
	OpcDxMask_DefaultSourceServerConnected	= 0x1000000,
	OpcDxMask_All	= 0x7fffffff
    } 	OpcDxMask;


EXTERN_C const IID LIBID_OpcDxLib;


#ifndef __OPCDX_Names_MODULE_DEFINED__
#define __OPCDX_Names_MODULE_DEFINED__


/* module OPCDX_Names */


const LPCWSTR OPC_CATEGORY_DESCRIPTION_DX10	=	L"OPC Data Exchange Servers Version 1.0";

const LPCWSTR OPCDX_NAMESPACE_V10	=	L"http://opcfoundation.org/webservices/OPCDX/10";

const LPCWSTR OPCDX_DATABASE_ROOT	=	L"DX";

const LPCWSTR OPCDX_SEPARATOR	=	L"/";

const LPCWSTR OPCDX_ITEM_PATH	=	L"ItemPath";

const LPCWSTR OPCDX_ITEM_NAME	=	L"ItemName";

const LPCWSTR OPCDX_VERSION	=	L"Version";

const LPCWSTR OPCDX_SERVER_STATUS_TYPE	=	L"DXServerStatus";

const LPCWSTR OPCDX_SERVER_STATUS	=	L"ServerStatus";

const LPCWSTR OPCDX_CONFIGURATION_VERSION	=	L"ConfigurationVersion";

const LPCWSTR OPCDX_SERVER_STATE	=	L"ServerState";

const LPCWSTR OPCDX_CONNECTION_COUNT	=	L"DXConnectionCount";

const LPCWSTR OPCDX_MAX_CONNECTIONS	=	L"MaxDXConnections";

const LPCWSTR OPCDX_SERVER_ERROR_ID	=	L"ErrorID";

const LPCWSTR OPCDX_SERVER_ERROR_DIAGNOSTIC	=	L"ErrorDiagnostic";

const LPCWSTR OPCDX_DIRTY_FLAG	=	L"DirtyFlag";

const LPCWSTR OPCDX_SOURCE_SERVER_TYPES	=	L"SourceServerTypes";

const LPCWSTR OPCDX_MAX_QUEUE_SIZE	=	L"MaxQueueSize";

const LPCWSTR OPCDX_CONNECTIONS_ROOT	=	L"DXConnectionsRoot";

const LPCWSTR OPCDX_CONNECTION_TYPE	=	L"DXConnection";

const LPCWSTR OPCDX_CONNECTION_NAME	=	L"Name";

const LPCWSTR OPCDX_CONNECTION_BROWSE_PATHS	=	L"BrowsePath";

const LPCWSTR OPCDX_CONNECTION_VERSION	=	L"Version";

const LPCWSTR OPCDX_CONNECTION_DESCRIPTION	=	L"Description";

const LPCWSTR OPCDX_CONNECTION_KEYWORD	=	L"Keyword";

const LPCWSTR OPCDX_DEFAULT_SOURCE_ITEM_CONNECTED	=	L"DefaultSourceItemConnected";

const LPCWSTR OPCDX_DEFAULT_TARGET_ITEM_CONNECTED	=	L"DefaultTargetItemConnected";

const LPCWSTR OPCDX_DEFAULT_OVERRIDDEN	=	L"DefaultOverridden";

const LPCWSTR OPCDX_DEFAULT_OVERRIDE_VALUE	=	L"DefaultOverrideValue";

const LPCWSTR OPCDX_ENABLE_SUBSTITUTE_VALUE	=	L"EnableSubstituteValue";

const LPCWSTR OPCDX_SUBSTITUTE_VALUE	=	L"SubstituteValue";

const LPCWSTR OPCDX_TARGET_ITEM_PATH	=	L"TargetItemPath";

const LPCWSTR OPCDX_TARGET_ITEM_NAME	=	L"TargetItemName";

const LPCWSTR OPCDX_CONNECTION_SOURCE_SERVER_NAME	=	L"SourceServerName";

const LPCWSTR OPCDX_SOURCE_ITEM_PATH	=	L"SourceItemPath";

const LPCWSTR OPCDX_SOURCE_ITEM_NAME	=	L"SourceItemName";

const LPCWSTR OPCDX_SOURCE_ITEM_QUEUE_SIZE	=	L"QueueSize";

const LPCWSTR OPCDX_UPDATE_RATE	=	L"UpdateRate";

const LPCWSTR OPCDX_DEADBAND	=	L"Deadband";

const LPCWSTR OPCDX_VENDOR_DATA	=	L"VendorData";

const LPCWSTR OPCDX_CONNECTION_STATUS	=	L"Status";

const LPCWSTR OPCDX_CONNECTION_STATUS_TYPE	=	L"DXConnectionStatus";

const LPCWSTR OPCDX_CONNECTION_STATE	=	L"DXConnectionState";

const LPCWSTR OPCDX_WRITE_VALUE	=	L"WriteValue";

const LPCWSTR OPCDX_WRITE_TIMESTAMP	=	L"WriteTimestamp";

const LPCWSTR OPCDX_WRITE_QUALITY	=	L"WriteQuality";

const LPCWSTR OPCDX_WRITE_ERROR_ID	=	L"WriteErrorID";

const LPCWSTR OPCDX_WRITE_ERROR_DIAGNOSTIC	=	L"WriteErrorDiagnostic";

const LPCWSTR OPCDX_SOURCE_VALUE	=	L"SourceValue";

const LPCWSTR OPCDX_SOURCE_TIMESTAMP	=	L"SourceTimestamp";

const LPCWSTR OPCDX_SOURCE_QUALITY	=	L"SourceQuality";

const LPCWSTR OPCDX_SOURCE_ERROR_ID	=	L"SourceErrorID";

const LPCWSTR OPCDX_SOURCE_ERROR_DIAGNOSTIC	=	L"SourceErrorDiagnostic";

const LPCWSTR OPCDX_ACTUAL_UPDATE_RATE	=	L"ActualUpdateRate";

const LPCWSTR OPCDX_QUEUE_HIGH_WATER_MARK	=	L"QueueHighWaterMark";

const LPCWSTR OPCDX_QUEUE_FLUSH_COUNT	=	L"QueueFlushCount";

const LPCWSTR OPCDX_SOURCE_ITEM_CONNECTED	=	L"SourceItemConnected";

const LPCWSTR OPCDX_TARGET_ITEM_CONNECTED	=	L"TargetItemConnected";

const LPCWSTR OPCDX_OVERRIDDEN	=	L"Overridden";

const LPCWSTR OPCDX_OVERRIDE_VALUE	=	L"OverrideValue";

const LPCWSTR OPCDX_SOURCE_SERVERS_ROOT	=	L"SourceServers";

const LPCWSTR OPCDX_SOURCE_SERVER_TYPE	=	L"SourceServer";

const LPCWSTR OPCDX_SOURCE_SERVER_NAME	=	L"Name";

const LPCWSTR OPCDX_SOURCE_SERVER_VERSION	=	L"Version";

const LPCWSTR OPCDX_SOURCE_SERVER_DESCRIPTION	=	L"Description";

const LPCWSTR OPCDX_SERVER_URL	=	L"ServerURL";

const LPCWSTR OPCDX_SERVER_TYPE	=	L"ServerType";

const LPCWSTR OPCDX_DEFAULT_SOURCE_SERVER_CONNECTED	=	L"DefaultSourceServerConnected";

const LPCWSTR OPCDX_SOURCE_SERVER_STATUS_TYPE	=	L"DXSourceServerStatus";

const LPCWSTR OPCDX_SOURCE_SERVER_STATUS	=	L"Status";

const LPCWSTR OPCDX_SERVER_CONNECT_STATUS	=	L"ConnectStatus";

const LPCWSTR OPCDX_SOURCE_SERVER_ERROR_ID	=	L"ErrorID";

const LPCWSTR OPCDX_SOURCE_SERVER_ERROR_DIAGNOSTIC	=	L"ErrorDiagnostic";

const LPCWSTR OPCDX_LAST_CONNECT_TIMESTAMP	=	L"LastConnectTimestamp";

const LPCWSTR OPCDX_LAST_CONNECT_FAIL_TIMESTAMP	=	L"LastConnectFailTimestamp";

const LPCWSTR OPCDX_CONNECT_FAIL_COUNT	=	L"ConnectFailCount";

const LPCWSTR OPCDX_PING_TIME	=	L"PingTime";

const LPCWSTR OPCDX_LAST_DATA_CHANGE_TIMESTAMP	=	L"LastDataChangeTimestamp";

const LPCWSTR OPCDX_SOURCE_SERVER_CONNECTED	=	L"SourceServerConnected";

const LPCWSTR OPCDX_QUALITY	=	L"DXQuality";

const LPCWSTR OPCDX_QUALITY_STATUS	=	L"Quality";

const LPCWSTR OPCDX_LIMIT_BITS	=	L"LimitBits";

const LPCWSTR OPCDX_VENDOR_BITS	=	L"VendorBits";

const LPCWSTR OPCDX_ERROR	=	L"OPCError";

const LPCWSTR OPCDX_ERROR_ID	=	L"ID";

const LPCWSTR OPCDX_ERROR_TEXT	=	L"Text";

const LPCWSTR OPCDX_SOURCE_SERVER_URL_SCHEME_OPCDA	=	L"opcda";

const LPCWSTR OPCDX_SOURCE_SERVER_URL_SCHEME_XMLDA	=	L"http";

#endif /* __OPCDX_Names_MODULE_DEFINED__ */


#ifndef __OPCDX_QualityStatusName_MODULE_DEFINED__
#define __OPCDX_QualityStatusName_MODULE_DEFINED__


/* module OPCDX_QualityStatusName */


const LPCWSTR OPCDX_QUALITY_BAD	=	L"bad";

const LPCWSTR OPCDX_QUALITY_BAD_CONFIG_ERROR	=	L"badConfigurationError";

const LPCWSTR OPCDX_QUALITY_BAD_NOT_CONNECTED	=	L"badNotConnected";

const LPCWSTR OPCDX_QUALITY_BAD_DEVICE_FAILURE	=	L"badDeviceFailure";

const LPCWSTR OPCDX_QUALITY_BAD_SENSOR_FAILURE	=	L"badSensorFailure";

const LPCWSTR OPCDX_QUALITY_BAD_LAST_KNOWN_VALUE	=	L"badLastKnownValue";

const LPCWSTR OPCDX_QUALITY_BAD_COMM_FAILURE	=	L"badCommFailure";

const LPCWSTR OPCDX_QUALITY_BAD_OUT_OF_SERVICE	=	L"badOutOfService";

const LPCWSTR OPCDX_QUALITY_UNCERTAIN	=	L"uncertain";

const LPCWSTR OPCDX_QUALITY_UNCERTAIN_LAST_USABLE_VALUE	=	L"uncertainLastUsableValue";

const LPCWSTR OPCDX_QUALITY_UNCERTAIN_SENSOR_NOT_ACCURATE	=	L"uncertainSensorNotAccurate";

const LPCWSTR OPCDX_QUALITY_UNCERTAIN_EU_EXCEEDED	=	L"uncertainEUExceeded";

const LPCWSTR OPCDX_QUALITY_UNCERTAIN_SUB_NORMAL	=	L"uncertainSubNormal";

const LPCWSTR OPCDX_QUALITY_GOOD	=	L"good";

const LPCWSTR OPCDX_QUALITY_GOOD_LOCAL_OVERRIDE	=	L"goodLocalOverride";

#endif /* __OPCDX_QualityStatusName_MODULE_DEFINED__ */


#ifndef __OPCDX_LimitStatusName_MODULE_DEFINED__
#define __OPCDX_LimitStatusName_MODULE_DEFINED__


/* module OPCDX_LimitStatusName */


const LPCWSTR OPCDX_LIMIT_NONE	=	L"none";

const LPCWSTR OPCDX_LIMIT_LOW	=	L"low";

const LPCWSTR OPCDX_LIMIT_HIGH	=	L"high";

const LPCWSTR OPCDX_LIMIT_CONSTANT	=	L"constant";

#endif /* __OPCDX_LimitStatusName_MODULE_DEFINED__ */


#ifndef __OPCDX_ServerTypeName_MODULE_DEFINED__
#define __OPCDX_ServerTypeName_MODULE_DEFINED__


/* module OPCDX_ServerTypeName */


const LPCWSTR OPCDX_SERVER_TYPE_COM_DA10	=	L"COM-DA1.0";

const LPCWSTR OPCDX_SERVER_TYPE_COM_DA204	=	L"COM-DA2.04";

const LPCWSTR OPCDX_SERVER_TYPE_COM_DA205	=	L"COM-DA2.05";

const LPCWSTR OPCDX_SERVER_TYPE_COM_DA30	=	L"COM-DA3.0";

const LPCWSTR OPCDX_SERVER_TYPE_XML_DA10	=	L"XML-DA1.0";

#endif /* __OPCDX_ServerTypeName_MODULE_DEFINED__ */


#ifndef __OPCDX_ServerStateName_MODULE_DEFINED__
#define __OPCDX_ServerStateName_MODULE_DEFINED__


/* module OPCDX_ServerStateName */


const LPCWSTR OPCDX_SERVER_STATE_RUNNING	=	L"running";

const LPCWSTR OPCDX_SERVER_STATE_FAILED	=	L"failed";

const LPCWSTR OPCDX_SERVER_STATE_NOCONFIG	=	L"noConfig";

const LPCWSTR OPCDX_SERVER_STATE_SUSPENDED	=	L"suspended";

const LPCWSTR OPCDX_SERVER_STATE_TEST	=	L"test";

const LPCWSTR OPCDX_SERVER_STATE_COMM_FAULT	=	L"commFault";

const LPCWSTR OPCDX_SERVER_STATE_UNKNOWN	=	L"unknown";

#endif /* __OPCDX_ServerStateName_MODULE_DEFINED__ */


#ifndef __OPCDX_ConnectStatusName_MODULE_DEFINED__
#define __OPCDX_ConnectStatusName_MODULE_DEFINED__


/* module OPCDX_ConnectStatusName */


const LPCWSTR OPCDX_CONNECT_STATUS_CONNECTED	=	L"connected";

const LPCWSTR OPCDX_CONNECT_STATUS_DISCONNECTED	=	L"disconnected";

const LPCWSTR OPCDX_CONNECT_STATUS_CONNECTING	=	L"connecting";

const LPCWSTR OPCDX_CONNECT_STATUS_FAILED	=	L"failed";

#endif /* __OPCDX_ConnectStatusName_MODULE_DEFINED__ */


#ifndef __OPCDX_ConnectionStateName_MODULE_DEFINED__
#define __OPCDX_ConnectionStateName_MODULE_DEFINED__


/* module OPCDX_ConnectionStateName */


const LPCWSTR OPCDX_CONNECTION_STATE_INITIALIZING	=	L"initializing";

const LPCWSTR OPCDX_CONNECTION_STATE_OPERATIONAL	=	L"operational";

const LPCWSTR OPCDX_CONNECTION_STATE_DEACTIVATED	=	L"deactivated";

const LPCWSTR OPCDX_CONNECTION_STATE_SOURCE_SERVER_NOT_CONNECTED	=	L"sourceServerNotConnected";

const LPCWSTR OPCDX_CONNECTION_STATE_SUBSCRIPTION_FAILED	=	L"subscriptionFailed";

const LPCWSTR OPCDX_CONNECTION_STATE_TARGET_ITEM_NOT_FOUND	=	L"targetItemNotFound";

#endif /* __OPCDX_ConnectionStateName_MODULE_DEFINED__ */
#endif /* __OpcDxLib_LIBRARY_DEFINED__ */

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
