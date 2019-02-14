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
/* at Wed Jul 07 21:26:03 2004
 */
/* Compiler settings for .\OpcCmd.idl:
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

#ifndef __OpcCmd_h__
#define __OpcCmd_h__

#if defined(_MSC_VER) && (_MSC_VER >= 1020)
#pragma once
#endif

/* Forward Declarations */ 

#ifndef __CATID_OPCCMDServer10_FWD_DEFINED__
#define __CATID_OPCCMDServer10_FWD_DEFINED__
typedef interface CATID_OPCCMDServer10 CATID_OPCCMDServer10;
#endif 	/* __CATID_OPCCMDServer10_FWD_DEFINED__ */


#ifndef __IOPCComandCallback_FWD_DEFINED__
#define __IOPCComandCallback_FWD_DEFINED__
typedef interface IOPCComandCallback IOPCComandCallback;
#endif 	/* __IOPCComandCallback_FWD_DEFINED__ */


#ifndef __IOPCCommandInformation_FWD_DEFINED__
#define __IOPCCommandInformation_FWD_DEFINED__
typedef interface IOPCCommandInformation IOPCCommandInformation;
#endif 	/* __IOPCCommandInformation_FWD_DEFINED__ */


#ifndef __IOPCCommandExecution_FWD_DEFINED__
#define __IOPCCommandExecution_FWD_DEFINED__
typedef interface IOPCCommandExecution IOPCCommandExecution;
#endif 	/* __IOPCCommandExecution_FWD_DEFINED__ */


#ifndef __CATID_OPCCMDServer10_FWD_DEFINED__
#define __CATID_OPCCMDServer10_FWD_DEFINED__
typedef interface CATID_OPCCMDServer10 CATID_OPCCMDServer10;
#endif 	/* __CATID_OPCCMDServer10_FWD_DEFINED__ */


#ifndef __IOPCCommandInformation_FWD_DEFINED__
#define __IOPCCommandInformation_FWD_DEFINED__
typedef interface IOPCCommandInformation IOPCCommandInformation;
#endif 	/* __IOPCCommandInformation_FWD_DEFINED__ */


#ifndef __IOPCCommandExecution_FWD_DEFINED__
#define __IOPCCommandExecution_FWD_DEFINED__
typedef interface IOPCCommandExecution IOPCCommandExecution;
#endif 	/* __IOPCCommandExecution_FWD_DEFINED__ */


#ifndef __IOPCComandCallback_FWD_DEFINED__
#define __IOPCComandCallback_FWD_DEFINED__
typedef interface IOPCComandCallback IOPCComandCallback;
#endif 	/* __IOPCComandCallback_FWD_DEFINED__ */


/* header files for imported files */
#include "oaidl.h"
#include "ocidl.h"

#ifdef __cplusplus
extern "C"{
#endif 

void * __RPC_USER MIDL_user_allocate(size_t);
void __RPC_USER MIDL_user_free( void * ); 

#ifndef __CATID_OPCCMDServer10_INTERFACE_DEFINED__
#define __CATID_OPCCMDServer10_INTERFACE_DEFINED__

/* interface CATID_OPCCMDServer10 */
/* [object][uuid] */ 


EXTERN_C const IID IID_CATID_OPCCMDServer10;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("2D869D5C-3B05-41fb-851A-642FB2B801A0")
    CATID_OPCCMDServer10 : public IUnknown
    {
    public:
    };
    
#else 	/* C style interface */

    typedef struct CATID_OPCCMDServer10Vtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            CATID_OPCCMDServer10 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            CATID_OPCCMDServer10 * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            CATID_OPCCMDServer10 * This);
        
        END_INTERFACE
    } CATID_OPCCMDServer10Vtbl;

    interface CATID_OPCCMDServer10
    {
        CONST_VTBL struct CATID_OPCCMDServer10Vtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define CATID_OPCCMDServer10_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define CATID_OPCCMDServer10_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define CATID_OPCCMDServer10_Release(This)	\
    (This)->lpVtbl -> Release(This)


#endif /* COBJMACROS */


#endif 	/* C style interface */




#endif 	/* __CATID_OPCCMDServer10_INTERFACE_DEFINED__ */


/* interface __MIDL_itf_OpcCmd_0258 */
/* [local] */ 

#define CATID_OPCCMDServer10 IID_CATID_OPCCMDServer10
typedef struct tagOpcCmdNamespaceDefinition
    {
    LPWSTR szUri;
    LPWSTR szDescription;
    DWORD dwNoOfCommandNames;
    /* [size_is] */ LPWSTR *pszCommandNames;
    } 	OpcCmdNamespaceDefinition;

typedef 
enum tagOpcCmdBrowseFilter
    {	OpcCmdBrowseFilter_All	= 0,
	OpcCmdBrowseFilter_Branch	= OpcCmdBrowseFilter_All + 1,
	OpcCmdBrowseFilter_Target	= OpcCmdBrowseFilter_Branch + 1
    } 	OpcCmdBrowseFilter;

typedef struct tagOpcCmdTargetElement
    {
    LPWSTR szLabel;
    LPWSTR szTargetID;
    BOOL bIsTarget;
    BOOL bHasChildren;
    DWORD dwNoOfNamespaceUris;
    /* [size_is] */ LPWSTR *pszNamespaceUris;
    } 	OpcCmdTargetElement;

typedef struct tagOpcCmdEventDefinition
    {
    LPWSTR szName;
    LPWSTR szDescription;
    LPWSTR szDataTypeDefinition;
    DWORD dwReserved;
    } 	OpcCmdEventDefinition;

typedef struct tagOpcCmdStateDefinition
    {
    LPWSTR szName;
    LPWSTR szDescription;
    LPWSTR szDataTypeDefinition;
    DWORD dwReserved;
    } 	OpcCmdStateDefinition;

typedef struct tagOpcCmdActionDefinition
    {
    LPWSTR szName;
    LPWSTR szDescription;
    LPWSTR szEventName;
    LPWSTR szInArguments;
    LPWSTR szOutArguments;
    DWORD dwReserved;
    } 	OpcCmdActionDefinition;

typedef struct tagOpcCmdStateTransition
    {
    LPWSTR szTransitionID;
    LPWSTR szStartState;
    LPWSTR szEndState;
    LPWSTR szTriggerEvent;
    LPWSTR szAction;
    DWORD dwReserved;
    } 	OpcCmdStateTransition;

typedef struct tagOpcCmdArgumentDefinition
    {
    LPWSTR szName;
    VARTYPE vtValueType;
    WORD wReserved;
    BOOL bOptional;
    LPWSTR szDescription;
    VARIANT vDefaultValue;
    LPWSTR szUnitType;
    DWORD dwReserved;
    VARIANT vLowLimit;
    VARIANT vHighLimit;
    } 	OpcCmdArgumentDefinition;

typedef struct tagOpcCmdArgument
    {
    LPWSTR szName;
    VARIANT vValue;
    } 	OpcCmdArgument;

typedef struct tagOpcCmdCommandDescription
    {
    LPWSTR szDescription;
    BOOL bIsGlobal;
    DOUBLE dblExecutionTime;
    DWORD dwNoOfEventDefinitions;
    /* [size_is] */ OpcCmdEventDefinition *pEventDefinitions;
    DWORD dwNoOfStateDefinitions;
    /* [size_is] */ OpcCmdStateDefinition *pStateDefinitions;
    DWORD dwNoOfActionDefinitions;
    /* [size_is] */ OpcCmdActionDefinition *pActionDefinitions;
    DWORD dwNoOfTransitions;
    /* [size_is] */ OpcCmdStateTransition *pTransitions;
    DWORD dwNoOfInArguments;
    /* [size_is] */ OpcCmdArgumentDefinition *pInArguments;
    DWORD dwNoOfOutArguments;
    /* [size_is] */ OpcCmdArgumentDefinition *pOutArguments;
    DWORD dwNoOfSupportedControls;
    /* [size_is] */ LPWSTR *pszSupportedControls;
    DWORD dwNoOfAndDependencies;
    /* [size_is] */ LPWSTR *pszAndDependencies;
    DWORD dwNoOfOrDependencies;
    /* [size_is] */ LPWSTR *pszOrDependencies;
    DWORD dwNoOfNotDependencies;
    /* [size_is] */ LPWSTR *pszNotDependencies;
    } 	OpcCmdCommandDescription;

typedef struct tagOpcCmdStateChangeEvent
    {
    LPWSTR szEventName;
    DWORD dwReserved;
    FILETIME ftEventTime;
    LPWSTR szEventData;
    LPWSTR szOldState;
    LPWSTR szNewState;
    LPWSTR szStateData;
    DWORD dwNoOfInArguments;
    /* [size_is] */ OpcCmdArgument *pInArguments;
    DWORD dwNoOfOutArguments;
    /* [size_is] */ OpcCmdArgument *pOutArguments;
    } 	OpcCmdStateChangeEvent;



extern RPC_IF_HANDLE __MIDL_itf_OpcCmd_0258_v0_0_c_ifspec;
extern RPC_IF_HANDLE __MIDL_itf_OpcCmd_0258_v0_0_s_ifspec;

#ifndef __IOPCComandCallback_INTERFACE_DEFINED__
#define __IOPCComandCallback_INTERFACE_DEFINED__

/* interface IOPCComandCallback */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCComandCallback;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("3104B527-2016-442d-9696-1275DE978778")
    IOPCComandCallback : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE OnStateChange( 
            /* [in] */ DWORD dwNoOfEvents,
            /* [size_is][in] */ OpcCmdStateChangeEvent *pEvents,
            /* [in] */ DWORD dwNoOfPermittedControls,
            /* [size_is][in] */ LPWSTR *pszPermittedControls,
            /* [in] */ BOOL bNoStateChange) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCComandCallbackVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCComandCallback * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCComandCallback * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCComandCallback * This);
        
        HRESULT ( STDMETHODCALLTYPE *OnStateChange )( 
            IOPCComandCallback * This,
            /* [in] */ DWORD dwNoOfEvents,
            /* [size_is][in] */ OpcCmdStateChangeEvent *pEvents,
            /* [in] */ DWORD dwNoOfPermittedControls,
            /* [size_is][in] */ LPWSTR *pszPermittedControls,
            /* [in] */ BOOL bNoStateChange);
        
        END_INTERFACE
    } IOPCComandCallbackVtbl;

    interface IOPCComandCallback
    {
        CONST_VTBL struct IOPCComandCallbackVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCComandCallback_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCComandCallback_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCComandCallback_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCComandCallback_OnStateChange(This,dwNoOfEvents,pEvents,dwNoOfPermittedControls,pszPermittedControls,bNoStateChange)	\
    (This)->lpVtbl -> OnStateChange(This,dwNoOfEvents,pEvents,dwNoOfPermittedControls,pszPermittedControls,bNoStateChange)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCComandCallback_OnStateChange_Proxy( 
    IOPCComandCallback * This,
    /* [in] */ DWORD dwNoOfEvents,
    /* [size_is][in] */ OpcCmdStateChangeEvent *pEvents,
    /* [in] */ DWORD dwNoOfPermittedControls,
    /* [size_is][in] */ LPWSTR *pszPermittedControls,
    /* [in] */ BOOL bNoStateChange);


void __RPC_STUB IOPCComandCallback_OnStateChange_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCComandCallback_INTERFACE_DEFINED__ */


#ifndef __IOPCCommandInformation_INTERFACE_DEFINED__
#define __IOPCCommandInformation_INTERFACE_DEFINED__

/* interface IOPCCommandInformation */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCCommandInformation;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("3104B525-2016-442d-9696-1275DE978778")
    IOPCCommandInformation : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE QueryCapabilities( 
            /* [out] */ DOUBLE *pdblMaxStorageTime,
            /* [out] */ BOOL *pbSupportsEventFilter) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE QueryComands( 
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ OpcCmdNamespaceDefinition **ppNamespaces) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE BrowseCommandTargets( 
            /* [in] */ LPWSTR szTargetID,
            /* [in] */ LPWSTR szNamespaceUri,
            /* [in] */ OpcCmdBrowseFilter eBrowseFilter,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ OpcCmdTargetElement **ppTargets) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetCommandDescription( 
            /* [in] */ LPWSTR szCommandName,
            /* [in] */ LPWSTR szNamespaceUri,
            /* [out] */ OpcCmdCommandDescription *pDescription) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCCommandInformationVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCCommandInformation * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCCommandInformation * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCCommandInformation * This);
        
        HRESULT ( STDMETHODCALLTYPE *QueryCapabilities )( 
            IOPCCommandInformation * This,
            /* [out] */ DOUBLE *pdblMaxStorageTime,
            /* [out] */ BOOL *pbSupportsEventFilter);
        
        HRESULT ( STDMETHODCALLTYPE *QueryComands )( 
            IOPCCommandInformation * This,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ OpcCmdNamespaceDefinition **ppNamespaces);
        
        HRESULT ( STDMETHODCALLTYPE *BrowseCommandTargets )( 
            IOPCCommandInformation * This,
            /* [in] */ LPWSTR szTargetID,
            /* [in] */ LPWSTR szNamespaceUri,
            /* [in] */ OpcCmdBrowseFilter eBrowseFilter,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ OpcCmdTargetElement **ppTargets);
        
        HRESULT ( STDMETHODCALLTYPE *GetCommandDescription )( 
            IOPCCommandInformation * This,
            /* [in] */ LPWSTR szCommandName,
            /* [in] */ LPWSTR szNamespaceUri,
            /* [out] */ OpcCmdCommandDescription *pDescription);
        
        END_INTERFACE
    } IOPCCommandInformationVtbl;

    interface IOPCCommandInformation
    {
        CONST_VTBL struct IOPCCommandInformationVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCCommandInformation_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCCommandInformation_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCCommandInformation_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCCommandInformation_QueryCapabilities(This,pdblMaxStorageTime,pbSupportsEventFilter)	\
    (This)->lpVtbl -> QueryCapabilities(This,pdblMaxStorageTime,pbSupportsEventFilter)

#define IOPCCommandInformation_QueryComands(This,pdwCount,ppNamespaces)	\
    (This)->lpVtbl -> QueryComands(This,pdwCount,ppNamespaces)

#define IOPCCommandInformation_BrowseCommandTargets(This,szTargetID,szNamespaceUri,eBrowseFilter,pdwCount,ppTargets)	\
    (This)->lpVtbl -> BrowseCommandTargets(This,szTargetID,szNamespaceUri,eBrowseFilter,pdwCount,ppTargets)

#define IOPCCommandInformation_GetCommandDescription(This,szCommandName,szNamespaceUri,pDescription)	\
    (This)->lpVtbl -> GetCommandDescription(This,szCommandName,szNamespaceUri,pDescription)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCCommandInformation_QueryCapabilities_Proxy( 
    IOPCCommandInformation * This,
    /* [out] */ DOUBLE *pdblMaxStorageTime,
    /* [out] */ BOOL *pbSupportsEventFilter);


void __RPC_STUB IOPCCommandInformation_QueryCapabilities_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCCommandInformation_QueryComands_Proxy( 
    IOPCCommandInformation * This,
    /* [out] */ DWORD *pdwCount,
    /* [size_is][size_is][out] */ OpcCmdNamespaceDefinition **ppNamespaces);


void __RPC_STUB IOPCCommandInformation_QueryComands_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCCommandInformation_BrowseCommandTargets_Proxy( 
    IOPCCommandInformation * This,
    /* [in] */ LPWSTR szTargetID,
    /* [in] */ LPWSTR szNamespaceUri,
    /* [in] */ OpcCmdBrowseFilter eBrowseFilter,
    /* [out] */ DWORD *pdwCount,
    /* [size_is][size_is][out] */ OpcCmdTargetElement **ppTargets);


void __RPC_STUB IOPCCommandInformation_BrowseCommandTargets_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCCommandInformation_GetCommandDescription_Proxy( 
    IOPCCommandInformation * This,
    /* [in] */ LPWSTR szCommandName,
    /* [in] */ LPWSTR szNamespaceUri,
    /* [out] */ OpcCmdCommandDescription *pDescription);


void __RPC_STUB IOPCCommandInformation_GetCommandDescription_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCCommandInformation_INTERFACE_DEFINED__ */


#ifndef __IOPCCommandExecution_INTERFACE_DEFINED__
#define __IOPCCommandExecution_INTERFACE_DEFINED__

/* interface IOPCCommandExecution */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCCommandExecution;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("3104B526-2016-442d-9696-1275DE978778")
    IOPCCommandExecution : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE SyncInvoke( 
            /* [in] */ LPWSTR szCommandName,
            /* [in] */ LPWSTR szNamespaceUri,
            /* [unique][in] */ LPWSTR szTargetID,
            /* [in] */ DWORD dwNoOfArguments,
            /* [size_is][in] */ OpcCmdArgument *pArguments,
            /* [in] */ DWORD dwNoOfFilters,
            /* [size_is][in] */ LPWSTR *pszFilters,
            /* [out] */ DWORD *pdwNoOfEvents,
            /* [size_is][size_is][out] */ OpcCmdStateChangeEvent **ppEvents) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE AsyncInvoke( 
            /* [in] */ LPWSTR szCommandName,
            /* [in] */ LPWSTR szNamespaceUri,
            /* [unique][in] */ LPWSTR szTargetID,
            /* [in] */ DWORD dwNoOfArguments,
            /* [size_is][in] */ OpcCmdArgument *pArguments,
            /* [in] */ DWORD dwNoOfFilters,
            /* [size_is][in] */ LPWSTR *pszFilters,
            /* [unique][in] */ IOPCComandCallback *ipCallback,
            /* [in] */ DWORD dwUpdateFrequency,
            /* [in] */ DWORD dwKeepAliveTime,
            /* [out] */ LPWSTR *pszInvokeUUID,
            /* [out] */ DWORD *pdwRevisedUpdateFrequency) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Connect( 
            /* [in] */ LPWSTR szInvokeUUID,
            /* [unique][in] */ IOPCComandCallback *ipCallback,
            /* [in] */ DWORD dwUpdateFrequency,
            /* [in] */ DWORD dwKeepAliveTime,
            /* [out] */ DWORD *pdwRevisedUpdateFrequency) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Disconnect( 
            /* [in] */ LPWSTR szInvokeUUID) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE QueryState( 
            /* [in] */ LPWSTR szInvokeUUID,
            /* [in] */ DWORD dwWaitTime,
            /* [out] */ DWORD *pdwNoOfEvents,
            /* [size_is][size_is][out] */ OpcCmdStateChangeEvent **ppEvents,
            /* [out] */ DWORD *pdwNoOfPermittedControls,
            /* [size_is][size_is][out] */ LPWSTR **ppszPermittedControls,
            /* [out] */ BOOL *pbNoStateChange) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Control( 
            /* [in] */ LPWSTR szInvokeUUID,
            /* [in] */ LPWSTR szControl) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCCommandExecutionVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCCommandExecution * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCCommandExecution * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCCommandExecution * This);
        
        HRESULT ( STDMETHODCALLTYPE *SyncInvoke )( 
            IOPCCommandExecution * This,
            /* [in] */ LPWSTR szCommandName,
            /* [in] */ LPWSTR szNamespaceUri,
            /* [unique][in] */ LPWSTR szTargetID,
            /* [in] */ DWORD dwNoOfArguments,
            /* [size_is][in] */ OpcCmdArgument *pArguments,
            /* [in] */ DWORD dwNoOfFilters,
            /* [size_is][in] */ LPWSTR *pszFilters,
            /* [out] */ DWORD *pdwNoOfEvents,
            /* [size_is][size_is][out] */ OpcCmdStateChangeEvent **ppEvents);
        
        HRESULT ( STDMETHODCALLTYPE *AsyncInvoke )( 
            IOPCCommandExecution * This,
            /* [in] */ LPWSTR szCommandName,
            /* [in] */ LPWSTR szNamespaceUri,
            /* [unique][in] */ LPWSTR szTargetID,
            /* [in] */ DWORD dwNoOfArguments,
            /* [size_is][in] */ OpcCmdArgument *pArguments,
            /* [in] */ DWORD dwNoOfFilters,
            /* [size_is][in] */ LPWSTR *pszFilters,
            /* [unique][in] */ IOPCComandCallback *ipCallback,
            /* [in] */ DWORD dwUpdateFrequency,
            /* [in] */ DWORD dwKeepAliveTime,
            /* [out] */ LPWSTR *pszInvokeUUID,
            /* [out] */ DWORD *pdwRevisedUpdateFrequency);
        
        HRESULT ( STDMETHODCALLTYPE *Connect )( 
            IOPCCommandExecution * This,
            /* [in] */ LPWSTR szInvokeUUID,
            /* [unique][in] */ IOPCComandCallback *ipCallback,
            /* [in] */ DWORD dwUpdateFrequency,
            /* [in] */ DWORD dwKeepAliveTime,
            /* [out] */ DWORD *pdwRevisedUpdateFrequency);
        
        HRESULT ( STDMETHODCALLTYPE *Disconnect )( 
            IOPCCommandExecution * This,
            /* [in] */ LPWSTR szInvokeUUID);
        
        HRESULT ( STDMETHODCALLTYPE *QueryState )( 
            IOPCCommandExecution * This,
            /* [in] */ LPWSTR szInvokeUUID,
            /* [in] */ DWORD dwWaitTime,
            /* [out] */ DWORD *pdwNoOfEvents,
            /* [size_is][size_is][out] */ OpcCmdStateChangeEvent **ppEvents,
            /* [out] */ DWORD *pdwNoOfPermittedControls,
            /* [size_is][size_is][out] */ LPWSTR **ppszPermittedControls,
            /* [out] */ BOOL *pbNoStateChange);
        
        HRESULT ( STDMETHODCALLTYPE *Control )( 
            IOPCCommandExecution * This,
            /* [in] */ LPWSTR szInvokeUUID,
            /* [in] */ LPWSTR szControl);
        
        END_INTERFACE
    } IOPCCommandExecutionVtbl;

    interface IOPCCommandExecution
    {
        CONST_VTBL struct IOPCCommandExecutionVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCCommandExecution_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCCommandExecution_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCCommandExecution_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCCommandExecution_SyncInvoke(This,szCommandName,szNamespaceUri,szTargetID,dwNoOfArguments,pArguments,dwNoOfFilters,pszFilters,pdwNoOfEvents,ppEvents)	\
    (This)->lpVtbl -> SyncInvoke(This,szCommandName,szNamespaceUri,szTargetID,dwNoOfArguments,pArguments,dwNoOfFilters,pszFilters,pdwNoOfEvents,ppEvents)

#define IOPCCommandExecution_AsyncInvoke(This,szCommandName,szNamespaceUri,szTargetID,dwNoOfArguments,pArguments,dwNoOfFilters,pszFilters,ipCallback,dwUpdateFrequency,dwKeepAliveTime,pszInvokeUUID,pdwRevisedUpdateFrequency)	\
    (This)->lpVtbl -> AsyncInvoke(This,szCommandName,szNamespaceUri,szTargetID,dwNoOfArguments,pArguments,dwNoOfFilters,pszFilters,ipCallback,dwUpdateFrequency,dwKeepAliveTime,pszInvokeUUID,pdwRevisedUpdateFrequency)

#define IOPCCommandExecution_Connect(This,szInvokeUUID,ipCallback,dwUpdateFrequency,dwKeepAliveTime,pdwRevisedUpdateFrequency)	\
    (This)->lpVtbl -> Connect(This,szInvokeUUID,ipCallback,dwUpdateFrequency,dwKeepAliveTime,pdwRevisedUpdateFrequency)

#define IOPCCommandExecution_Disconnect(This,szInvokeUUID)	\
    (This)->lpVtbl -> Disconnect(This,szInvokeUUID)

#define IOPCCommandExecution_QueryState(This,szInvokeUUID,dwWaitTime,pdwNoOfEvents,ppEvents,pdwNoOfPermittedControls,ppszPermittedControls,pbNoStateChange)	\
    (This)->lpVtbl -> QueryState(This,szInvokeUUID,dwWaitTime,pdwNoOfEvents,ppEvents,pdwNoOfPermittedControls,ppszPermittedControls,pbNoStateChange)

#define IOPCCommandExecution_Control(This,szInvokeUUID,szControl)	\
    (This)->lpVtbl -> Control(This,szInvokeUUID,szControl)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCCommandExecution_SyncInvoke_Proxy( 
    IOPCCommandExecution * This,
    /* [in] */ LPWSTR szCommandName,
    /* [in] */ LPWSTR szNamespaceUri,
    /* [unique][in] */ LPWSTR szTargetID,
    /* [in] */ DWORD dwNoOfArguments,
    /* [size_is][in] */ OpcCmdArgument *pArguments,
    /* [in] */ DWORD dwNoOfFilters,
    /* [size_is][in] */ LPWSTR *pszFilters,
    /* [out] */ DWORD *pdwNoOfEvents,
    /* [size_is][size_is][out] */ OpcCmdStateChangeEvent **ppEvents);


void __RPC_STUB IOPCCommandExecution_SyncInvoke_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCCommandExecution_AsyncInvoke_Proxy( 
    IOPCCommandExecution * This,
    /* [in] */ LPWSTR szCommandName,
    /* [in] */ LPWSTR szNamespaceUri,
    /* [unique][in] */ LPWSTR szTargetID,
    /* [in] */ DWORD dwNoOfArguments,
    /* [size_is][in] */ OpcCmdArgument *pArguments,
    /* [in] */ DWORD dwNoOfFilters,
    /* [size_is][in] */ LPWSTR *pszFilters,
    /* [unique][in] */ IOPCComandCallback *ipCallback,
    /* [in] */ DWORD dwUpdateFrequency,
    /* [in] */ DWORD dwKeepAliveTime,
    /* [out] */ LPWSTR *pszInvokeUUID,
    /* [out] */ DWORD *pdwRevisedUpdateFrequency);


void __RPC_STUB IOPCCommandExecution_AsyncInvoke_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCCommandExecution_Connect_Proxy( 
    IOPCCommandExecution * This,
    /* [in] */ LPWSTR szInvokeUUID,
    /* [unique][in] */ IOPCComandCallback *ipCallback,
    /* [in] */ DWORD dwUpdateFrequency,
    /* [in] */ DWORD dwKeepAliveTime,
    /* [out] */ DWORD *pdwRevisedUpdateFrequency);


void __RPC_STUB IOPCCommandExecution_Connect_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCCommandExecution_Disconnect_Proxy( 
    IOPCCommandExecution * This,
    /* [in] */ LPWSTR szInvokeUUID);


void __RPC_STUB IOPCCommandExecution_Disconnect_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCCommandExecution_QueryState_Proxy( 
    IOPCCommandExecution * This,
    /* [in] */ LPWSTR szInvokeUUID,
    /* [in] */ DWORD dwWaitTime,
    /* [out] */ DWORD *pdwNoOfEvents,
    /* [size_is][size_is][out] */ OpcCmdStateChangeEvent **ppEvents,
    /* [out] */ DWORD *pdwNoOfPermittedControls,
    /* [size_is][size_is][out] */ LPWSTR **ppszPermittedControls,
    /* [out] */ BOOL *pbNoStateChange);


void __RPC_STUB IOPCCommandExecution_QueryState_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCCommandExecution_Control_Proxy( 
    IOPCCommandExecution * This,
    /* [in] */ LPWSTR szInvokeUUID,
    /* [in] */ LPWSTR szControl);


void __RPC_STUB IOPCCommandExecution_Control_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCCommandExecution_INTERFACE_DEFINED__ */



#ifndef __OpcCmdLib_LIBRARY_DEFINED__
#define __OpcCmdLib_LIBRARY_DEFINED__

/* library OpcCmdLib */
/* [helpstring][version][uuid] */ 






EXTERN_C const IID LIBID_OpcCmdLib;


#ifndef __Constants_MODULE_DEFINED__
#define __Constants_MODULE_DEFINED__


/* module Constants */


const LPCWSTR OPC_CATEGORY_DESCRIPTION_CMD10	=	L"OPC Command Execution Servers Version 1.0";

const LPCWSTR OPCCMD_NAMESPACE_V10	=	L"http://opcfoundation.org/webservices/OPCCMD/10";

#endif /* __Constants_MODULE_DEFINED__ */


#ifndef __EventName_MODULE_DEFINED__
#define __EventName_MODULE_DEFINED__


/* module EventName */


const LPCWSTR OPCCMD_EVENT_NAME_INVOKE	=	L"Invoke";

const LPCWSTR OPCCMD_EVENT_NAME_FINISHED	=	L"Finished";

const LPCWSTR OPCCMD_EVENT_NAME_ABORTED	=	L"Aborted";

const LPCWSTR OPCCMD_EVENT_NAME_RESET	=	L"Reset";

const LPCWSTR OPCCMD_EVENT_NAME_HALTED	=	L"Halted";

const LPCWSTR OPCCMD_EVENT_NAME_RESUMED	=	L"Resumed";

const LPCWSTR OPCCMD_EVENT_NAME_CANCELLED	=	L"Cancelled";

#endif /* __EventName_MODULE_DEFINED__ */


#ifndef __StateName_MODULE_DEFINED__
#define __StateName_MODULE_DEFINED__


/* module StateName */


const LPCWSTR OPCCMD_STATE_NAME_IDLE	=	L"Idle";

const LPCWSTR OPCCMD_STATE_NAME_EXECUTING	=	L"Executing";

const LPCWSTR OPCCMD_STATE_NAME_COMPLETE	=	L"Complete";

const LPCWSTR OPCCMD_STATE_NAME_ABNORMAL_FAILURE	=	L"AbnormalFailure";

const LPCWSTR OPCCMD_STATE_NAME_HALTED	=	L"Halted";

#endif /* __StateName_MODULE_DEFINED__ */


#ifndef __ControlCommand_MODULE_DEFINED__
#define __ControlCommand_MODULE_DEFINED__


/* module ControlCommand */


const LPCWSTR OPCCMD_CONTROL_SUSPEND	=	L"Suspend";

const LPCWSTR OPCCMD_CONTROL_RESUME	=	L"Resume";

const LPCWSTR OPCCMD_CONTROL_CANCEL	=	L"Cancel";

#endif /* __ControlCommand_MODULE_DEFINED__ */
#endif /* __OpcCmdLib_LIBRARY_DEFINED__ */

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
