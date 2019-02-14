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

#ifndef __opchda_h__
#define __opchda_h__

#if defined(_MSC_VER) && (_MSC_VER >= 1020)
#pragma once
#endif

/* Forward Declarations */ 

#ifndef __CATID_OPCHDAServer10_FWD_DEFINED__
#define __CATID_OPCHDAServer10_FWD_DEFINED__
typedef interface CATID_OPCHDAServer10 CATID_OPCHDAServer10;
#endif 	/* __CATID_OPCHDAServer10_FWD_DEFINED__ */


#ifndef __IOPCHDA_Browser_FWD_DEFINED__
#define __IOPCHDA_Browser_FWD_DEFINED__
typedef interface IOPCHDA_Browser IOPCHDA_Browser;
#endif 	/* __IOPCHDA_Browser_FWD_DEFINED__ */


#ifndef __IOPCHDA_Server_FWD_DEFINED__
#define __IOPCHDA_Server_FWD_DEFINED__
typedef interface IOPCHDA_Server IOPCHDA_Server;
#endif 	/* __IOPCHDA_Server_FWD_DEFINED__ */


#ifndef __IOPCHDA_SyncRead_FWD_DEFINED__
#define __IOPCHDA_SyncRead_FWD_DEFINED__
typedef interface IOPCHDA_SyncRead IOPCHDA_SyncRead;
#endif 	/* __IOPCHDA_SyncRead_FWD_DEFINED__ */


#ifndef __IOPCHDA_SyncUpdate_FWD_DEFINED__
#define __IOPCHDA_SyncUpdate_FWD_DEFINED__
typedef interface IOPCHDA_SyncUpdate IOPCHDA_SyncUpdate;
#endif 	/* __IOPCHDA_SyncUpdate_FWD_DEFINED__ */


#ifndef __IOPCHDA_SyncAnnotations_FWD_DEFINED__
#define __IOPCHDA_SyncAnnotations_FWD_DEFINED__
typedef interface IOPCHDA_SyncAnnotations IOPCHDA_SyncAnnotations;
#endif 	/* __IOPCHDA_SyncAnnotations_FWD_DEFINED__ */


#ifndef __IOPCHDA_AsyncRead_FWD_DEFINED__
#define __IOPCHDA_AsyncRead_FWD_DEFINED__
typedef interface IOPCHDA_AsyncRead IOPCHDA_AsyncRead;
#endif 	/* __IOPCHDA_AsyncRead_FWD_DEFINED__ */


#ifndef __IOPCHDA_AsyncUpdate_FWD_DEFINED__
#define __IOPCHDA_AsyncUpdate_FWD_DEFINED__
typedef interface IOPCHDA_AsyncUpdate IOPCHDA_AsyncUpdate;
#endif 	/* __IOPCHDA_AsyncUpdate_FWD_DEFINED__ */


#ifndef __IOPCHDA_AsyncAnnotations_FWD_DEFINED__
#define __IOPCHDA_AsyncAnnotations_FWD_DEFINED__
typedef interface IOPCHDA_AsyncAnnotations IOPCHDA_AsyncAnnotations;
#endif 	/* __IOPCHDA_AsyncAnnotations_FWD_DEFINED__ */


#ifndef __IOPCHDA_Playback_FWD_DEFINED__
#define __IOPCHDA_Playback_FWD_DEFINED__
typedef interface IOPCHDA_Playback IOPCHDA_Playback;
#endif 	/* __IOPCHDA_Playback_FWD_DEFINED__ */


#ifndef __IOPCHDA_DataCallback_FWD_DEFINED__
#define __IOPCHDA_DataCallback_FWD_DEFINED__
typedef interface IOPCHDA_DataCallback IOPCHDA_DataCallback;
#endif 	/* __IOPCHDA_DataCallback_FWD_DEFINED__ */


#ifndef __CATID_OPCHDAServer10_FWD_DEFINED__
#define __CATID_OPCHDAServer10_FWD_DEFINED__
typedef interface CATID_OPCHDAServer10 CATID_OPCHDAServer10;
#endif 	/* __CATID_OPCHDAServer10_FWD_DEFINED__ */


#ifndef __IOPCHDA_Server_FWD_DEFINED__
#define __IOPCHDA_Server_FWD_DEFINED__
typedef interface IOPCHDA_Server IOPCHDA_Server;
#endif 	/* __IOPCHDA_Server_FWD_DEFINED__ */


#ifndef __IOPCHDA_Browser_FWD_DEFINED__
#define __IOPCHDA_Browser_FWD_DEFINED__
typedef interface IOPCHDA_Browser IOPCHDA_Browser;
#endif 	/* __IOPCHDA_Browser_FWD_DEFINED__ */


#ifndef __IOPCHDA_SyncRead_FWD_DEFINED__
#define __IOPCHDA_SyncRead_FWD_DEFINED__
typedef interface IOPCHDA_SyncRead IOPCHDA_SyncRead;
#endif 	/* __IOPCHDA_SyncRead_FWD_DEFINED__ */


#ifndef __IOPCHDA_SyncUpdate_FWD_DEFINED__
#define __IOPCHDA_SyncUpdate_FWD_DEFINED__
typedef interface IOPCHDA_SyncUpdate IOPCHDA_SyncUpdate;
#endif 	/* __IOPCHDA_SyncUpdate_FWD_DEFINED__ */


#ifndef __IOPCHDA_SyncAnnotations_FWD_DEFINED__
#define __IOPCHDA_SyncAnnotations_FWD_DEFINED__
typedef interface IOPCHDA_SyncAnnotations IOPCHDA_SyncAnnotations;
#endif 	/* __IOPCHDA_SyncAnnotations_FWD_DEFINED__ */


#ifndef __IOPCHDA_AsyncRead_FWD_DEFINED__
#define __IOPCHDA_AsyncRead_FWD_DEFINED__
typedef interface IOPCHDA_AsyncRead IOPCHDA_AsyncRead;
#endif 	/* __IOPCHDA_AsyncRead_FWD_DEFINED__ */


#ifndef __IOPCHDA_AsyncUpdate_FWD_DEFINED__
#define __IOPCHDA_AsyncUpdate_FWD_DEFINED__
typedef interface IOPCHDA_AsyncUpdate IOPCHDA_AsyncUpdate;
#endif 	/* __IOPCHDA_AsyncUpdate_FWD_DEFINED__ */


#ifndef __IOPCHDA_AsyncAnnotations_FWD_DEFINED__
#define __IOPCHDA_AsyncAnnotations_FWD_DEFINED__
typedef interface IOPCHDA_AsyncAnnotations IOPCHDA_AsyncAnnotations;
#endif 	/* __IOPCHDA_AsyncAnnotations_FWD_DEFINED__ */


#ifndef __IOPCHDA_Playback_FWD_DEFINED__
#define __IOPCHDA_Playback_FWD_DEFINED__
typedef interface IOPCHDA_Playback IOPCHDA_Playback;
#endif 	/* __IOPCHDA_Playback_FWD_DEFINED__ */


#ifndef __IOPCHDA_DataCallback_FWD_DEFINED__
#define __IOPCHDA_DataCallback_FWD_DEFINED__
typedef interface IOPCHDA_DataCallback IOPCHDA_DataCallback;
#endif 	/* __IOPCHDA_DataCallback_FWD_DEFINED__ */


/* header files for imported files */
#include "oaidl.h"

#ifdef __cplusplus
extern "C"{
#endif 

void * __RPC_USER MIDL_user_allocate(size_t);
void __RPC_USER MIDL_user_free( void * ); 

#ifndef __CATID_OPCHDAServer10_INTERFACE_DEFINED__
#define __CATID_OPCHDAServer10_INTERFACE_DEFINED__

/* interface CATID_OPCHDAServer10 */
/* [object][uuid] */ 


EXTERN_C const IID IID_CATID_OPCHDAServer10;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("7DE5B060-E089-11d2-A5E6-000086339399")
    CATID_OPCHDAServer10 : public IUnknown
    {
    public:
    };
    
#else 	/* C style interface */

    typedef struct CATID_OPCHDAServer10Vtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            CATID_OPCHDAServer10 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            CATID_OPCHDAServer10 * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            CATID_OPCHDAServer10 * This);
        
        END_INTERFACE
    } CATID_OPCHDAServer10Vtbl;

    interface CATID_OPCHDAServer10
    {
        CONST_VTBL struct CATID_OPCHDAServer10Vtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define CATID_OPCHDAServer10_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define CATID_OPCHDAServer10_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define CATID_OPCHDAServer10_Release(This)	\
    (This)->lpVtbl -> Release(This)


#endif /* COBJMACROS */


#endif 	/* C style interface */




#endif 	/* __CATID_OPCHDAServer10_INTERFACE_DEFINED__ */


/* interface __MIDL_itf_opchda_0115 */
/* [local] */ 

#define CATID_OPCHDAServer10 IID_CATID_OPCHDAServer10
typedef 
enum tagOPCHDA_SERVERSTATUS
    {	OPCHDA_UP	= 1,
	OPCHDA_DOWN	= OPCHDA_UP + 1,
	OPCHDA_INDETERMINATE	= OPCHDA_DOWN + 1
    } 	OPCHDA_SERVERSTATUS;

typedef 
enum tagOPCHDA_BROWSEDIRECTION
    {	OPCHDA_BROWSE_UP	= 1,
	OPCHDA_BROWSE_DOWN	= OPCHDA_BROWSE_UP + 1,
	OPCHDA_BROWSE_DIRECT	= OPCHDA_BROWSE_DOWN + 1
    } 	OPCHDA_BROWSEDIRECTION;

typedef 
enum tagOPCHDA_BROWSETYPE
    {	OPCHDA_BRANCH	= 1,
	OPCHDA_LEAF	= OPCHDA_BRANCH + 1,
	OPCHDA_FLAT	= OPCHDA_LEAF + 1,
	OPCHDA_ITEMS	= OPCHDA_FLAT + 1
    } 	OPCHDA_BROWSETYPE;

typedef 
enum tagOPCHDA_ANNOTATIONCAPABILITIES
    {	OPCHDA_READANNOTATIONCAP	= 0x1,
	OPCHDA_INSERTANNOTATIONCAP	= 0x2
    } 	OPCHDA_ANNOTATIONCAPABILITIES;

typedef 
enum tagOPCHDA_UPDATECAPABILITIES
    {	OPCHDA_INSERTCAP	= 0x1,
	OPCHDA_REPLACECAP	= 0x2,
	OPCHDA_INSERTREPLACECAP	= 0x4,
	OPCHDA_DELETERAWCAP	= 0x8,
	OPCHDA_DELETEATTIMECAP	= 0x10
    } 	OPCHDA_UPDATECAPABILITIES;

typedef 
enum tagOPCHDA_OPERATORCODES
    {	OPCHDA_EQUAL	= 1,
	OPCHDA_LESS	= OPCHDA_EQUAL + 1,
	OPCHDA_LESSEQUAL	= OPCHDA_LESS + 1,
	OPCHDA_GREATER	= OPCHDA_LESSEQUAL + 1,
	OPCHDA_GREATEREQUAL	= OPCHDA_GREATER + 1,
	OPCHDA_NOTEQUAL	= OPCHDA_GREATEREQUAL + 1
    } 	OPCHDA_OPERATORCODES;

typedef 
enum tagOPCHDA_EDITTYPE
    {	OPCHDA_INSERT	= 1,
	OPCHDA_REPLACE	= OPCHDA_INSERT + 1,
	OPCHDA_INSERTREPLACE	= OPCHDA_REPLACE + 1,
	OPCHDA_DELETE	= OPCHDA_INSERTREPLACE + 1
    } 	OPCHDA_EDITTYPE;

typedef 
enum tagOPCHDA_AGGREGATE
    {	OPCHDA_NOAGGREGATE	= 0,
	OPCHDA_INTERPOLATIVE	= OPCHDA_NOAGGREGATE + 1,
	OPCHDA_TOTAL	= OPCHDA_INTERPOLATIVE + 1,
	OPCHDA_AVERAGE	= OPCHDA_TOTAL + 1,
	OPCHDA_TIMEAVERAGE	= OPCHDA_AVERAGE + 1,
	OPCHDA_COUNT	= OPCHDA_TIMEAVERAGE + 1,
	OPCHDA_STDEV	= OPCHDA_COUNT + 1,
	OPCHDA_MINIMUMACTUALTIME	= OPCHDA_STDEV + 1,
	OPCHDA_MINIMUM	= OPCHDA_MINIMUMACTUALTIME + 1,
	OPCHDA_MAXIMUMACTUALTIME	= OPCHDA_MINIMUM + 1,
	OPCHDA_MAXIMUM	= OPCHDA_MAXIMUMACTUALTIME + 1,
	OPCHDA_START	= OPCHDA_MAXIMUM + 1,
	OPCHDA_END	= OPCHDA_START + 1,
	OPCHDA_DELTA	= OPCHDA_END + 1,
	OPCHDA_REGSLOPE	= OPCHDA_DELTA + 1,
	OPCHDA_REGCONST	= OPCHDA_REGSLOPE + 1,
	OPCHDA_REGDEV	= OPCHDA_REGCONST + 1,
	OPCHDA_VARIANCE	= OPCHDA_REGDEV + 1,
	OPCHDA_RANGE	= OPCHDA_VARIANCE + 1,
	OPCHDA_DURATIONGOOD	= OPCHDA_RANGE + 1,
	OPCHDA_DURATIONBAD	= OPCHDA_DURATIONGOOD + 1,
	OPCHDA_PERCENTGOOD	= OPCHDA_DURATIONBAD + 1,
	OPCHDA_PERCENTBAD	= OPCHDA_PERCENTGOOD + 1,
	OPCHDA_WORSTQUALITY	= OPCHDA_PERCENTBAD + 1,
	OPCHDA_ANNOTATIONS	= OPCHDA_WORSTQUALITY + 1
    } 	OPCHDA_AGGREGATE;

typedef DWORD OPCHANDLE;

typedef struct tagOPCHDA_ANNOTATION
    {
    OPCHANDLE hClient;
    DWORD dwNumValues;
    /* [size_is] */ FILETIME *ftTimeStamps;
    /* [string][size_is] */ LPWSTR *szAnnotation;
    /* [size_is] */ FILETIME *ftAnnotationTime;
    /* [string][size_is] */ LPWSTR *szUser;
    } 	OPCHDA_ANNOTATION;

typedef struct tagOPCHDA_MODIFIEDITEM
    {
    OPCHANDLE hClient;
    DWORD dwCount;
    /* [size_is] */ FILETIME *pftTimeStamps;
    /* [size_is] */ DWORD *pdwQualities;
    /* [size_is] */ VARIANT *pvDataValues;
    /* [size_is] */ FILETIME *pftModificationTime;
    /* [size_is] */ OPCHDA_EDITTYPE *pEditType;
    /* [size_is] */ LPWSTR *szUser;
    } 	OPCHDA_MODIFIEDITEM;

typedef struct tagOPCHDA_ATTRIBUTE
    {
    OPCHANDLE hClient;
    DWORD dwNumValues;
    DWORD dwAttributeID;
    /* [size_is] */ FILETIME *ftTimeStamps;
    /* [size_is] */ VARIANT *vAttributeValues;
    } 	OPCHDA_ATTRIBUTE;

typedef struct tagOPCHDA_TIME
    {
    BOOL bString;
    /* [string] */ LPWSTR szTime;
    FILETIME ftTime;
    } 	OPCHDA_TIME;

typedef struct tagOPCHDA_ITEM
    {
    OPCHANDLE hClient;
    DWORD haAggregate;
    DWORD dwCount;
    /* [size_is] */ FILETIME *pftTimeStamps;
    /* [size_is] */ DWORD *pdwQualities;
    /* [size_is] */ VARIANT *pvDataValues;
    } 	OPCHDA_ITEM;



extern RPC_IF_HANDLE __MIDL_itf_opchda_0115_v0_0_c_ifspec;
extern RPC_IF_HANDLE __MIDL_itf_opchda_0115_v0_0_s_ifspec;

#ifndef __IOPCHDA_Browser_INTERFACE_DEFINED__
#define __IOPCHDA_Browser_INTERFACE_DEFINED__

/* interface IOPCHDA_Browser */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCHDA_Browser;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("1F1217B1-DEE0-11d2-A5E5-000086339399")
    IOPCHDA_Browser : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE GetEnum( 
            /* [in] */ OPCHDA_BROWSETYPE dwBrowseType,
            /* [out] */ LPENUMSTRING *ppIEnumString) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ChangeBrowsePosition( 
            /* [in] */ OPCHDA_BROWSEDIRECTION dwBrowseDirection,
            /* [string][in] */ LPCWSTR szString) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetItemID( 
            /* [string][in] */ LPCWSTR szNode,
            /* [string][out] */ LPWSTR *pszItemID) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetBranchPosition( 
            /* [string][out] */ LPWSTR *pszBranchPos) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCHDA_BrowserVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCHDA_Browser * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCHDA_Browser * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCHDA_Browser * This);
        
        HRESULT ( STDMETHODCALLTYPE *GetEnum )( 
            IOPCHDA_Browser * This,
            /* [in] */ OPCHDA_BROWSETYPE dwBrowseType,
            /* [out] */ LPENUMSTRING *ppIEnumString);
        
        HRESULT ( STDMETHODCALLTYPE *ChangeBrowsePosition )( 
            IOPCHDA_Browser * This,
            /* [in] */ OPCHDA_BROWSEDIRECTION dwBrowseDirection,
            /* [string][in] */ LPCWSTR szString);
        
        HRESULT ( STDMETHODCALLTYPE *GetItemID )( 
            IOPCHDA_Browser * This,
            /* [string][in] */ LPCWSTR szNode,
            /* [string][out] */ LPWSTR *pszItemID);
        
        HRESULT ( STDMETHODCALLTYPE *GetBranchPosition )( 
            IOPCHDA_Browser * This,
            /* [string][out] */ LPWSTR *pszBranchPos);
        
        END_INTERFACE
    } IOPCHDA_BrowserVtbl;

    interface IOPCHDA_Browser
    {
        CONST_VTBL struct IOPCHDA_BrowserVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCHDA_Browser_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCHDA_Browser_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCHDA_Browser_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCHDA_Browser_GetEnum(This,dwBrowseType,ppIEnumString)	\
    (This)->lpVtbl -> GetEnum(This,dwBrowseType,ppIEnumString)

#define IOPCHDA_Browser_ChangeBrowsePosition(This,dwBrowseDirection,szString)	\
    (This)->lpVtbl -> ChangeBrowsePosition(This,dwBrowseDirection,szString)

#define IOPCHDA_Browser_GetItemID(This,szNode,pszItemID)	\
    (This)->lpVtbl -> GetItemID(This,szNode,pszItemID)

#define IOPCHDA_Browser_GetBranchPosition(This,pszBranchPos)	\
    (This)->lpVtbl -> GetBranchPosition(This,pszBranchPos)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCHDA_Browser_GetEnum_Proxy( 
    IOPCHDA_Browser * This,
    /* [in] */ OPCHDA_BROWSETYPE dwBrowseType,
    /* [out] */ LPENUMSTRING *ppIEnumString);


void __RPC_STUB IOPCHDA_Browser_GetEnum_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_Browser_ChangeBrowsePosition_Proxy( 
    IOPCHDA_Browser * This,
    /* [in] */ OPCHDA_BROWSEDIRECTION dwBrowseDirection,
    /* [string][in] */ LPCWSTR szString);


void __RPC_STUB IOPCHDA_Browser_ChangeBrowsePosition_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_Browser_GetItemID_Proxy( 
    IOPCHDA_Browser * This,
    /* [string][in] */ LPCWSTR szNode,
    /* [string][out] */ LPWSTR *pszItemID);


void __RPC_STUB IOPCHDA_Browser_GetItemID_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_Browser_GetBranchPosition_Proxy( 
    IOPCHDA_Browser * This,
    /* [string][out] */ LPWSTR *pszBranchPos);


void __RPC_STUB IOPCHDA_Browser_GetBranchPosition_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCHDA_Browser_INTERFACE_DEFINED__ */


#ifndef __IOPCHDA_Server_INTERFACE_DEFINED__
#define __IOPCHDA_Server_INTERFACE_DEFINED__

/* interface IOPCHDA_Server */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCHDA_Server;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("1F1217B0-DEE0-11d2-A5E5-000086339399")
    IOPCHDA_Server : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE GetItemAttributes( 
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppdwAttrID,
            /* [string][size_is][size_is][out] */ LPWSTR **ppszAttrName,
            /* [string][size_is][size_is][out] */ LPWSTR **ppszAttrDesc,
            /* [size_is][size_is][out] */ VARTYPE **ppvtAttrDataType) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetAggregates( 
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppdwAggrID,
            /* [string][size_is][size_is][out] */ LPWSTR **ppszAggrName,
            /* [string][size_is][size_is][out] */ LPWSTR **ppszAggrDesc) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetHistorianStatus( 
            /* [out] */ OPCHDA_SERVERSTATUS *pwStatus,
            /* [out] */ FILETIME **pftCurrentTime,
            /* [out] */ FILETIME **pftStartTime,
            /* [out] */ WORD *pwMajorVersion,
            /* [out] */ WORD *pwMinorVersion,
            /* [out] */ WORD *pwBuildNumber,
            /* [out] */ DWORD *pdwMaxReturnValues,
            /* [string][out] */ LPWSTR *ppszStatusString,
            /* [string][out] */ LPWSTR *ppszVendorInfo) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetItemHandles( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ LPWSTR *pszItemID,
            /* [size_is][in] */ OPCHANDLE *phClient,
            /* [size_is][size_is][out] */ OPCHANDLE **pphServer,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ReleaseItemHandles( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ValidateItemIDs( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ LPWSTR *pszItemID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE CreateBrowse( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ DWORD *pdwAttrID,
            /* [size_is][in] */ OPCHDA_OPERATORCODES *pOperator,
            /* [size_is][in] */ VARIANT *vFilter,
            /* [out] */ IOPCHDA_Browser **pphBrowser,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCHDA_ServerVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCHDA_Server * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCHDA_Server * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCHDA_Server * This);
        
        HRESULT ( STDMETHODCALLTYPE *GetItemAttributes )( 
            IOPCHDA_Server * This,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppdwAttrID,
            /* [string][size_is][size_is][out] */ LPWSTR **ppszAttrName,
            /* [string][size_is][size_is][out] */ LPWSTR **ppszAttrDesc,
            /* [size_is][size_is][out] */ VARTYPE **ppvtAttrDataType);
        
        HRESULT ( STDMETHODCALLTYPE *GetAggregates )( 
            IOPCHDA_Server * This,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppdwAggrID,
            /* [string][size_is][size_is][out] */ LPWSTR **ppszAggrName,
            /* [string][size_is][size_is][out] */ LPWSTR **ppszAggrDesc);
        
        HRESULT ( STDMETHODCALLTYPE *GetHistorianStatus )( 
            IOPCHDA_Server * This,
            /* [out] */ OPCHDA_SERVERSTATUS *pwStatus,
            /* [out] */ FILETIME **pftCurrentTime,
            /* [out] */ FILETIME **pftStartTime,
            /* [out] */ WORD *pwMajorVersion,
            /* [out] */ WORD *pwMinorVersion,
            /* [out] */ WORD *pwBuildNumber,
            /* [out] */ DWORD *pdwMaxReturnValues,
            /* [string][out] */ LPWSTR *ppszStatusString,
            /* [string][out] */ LPWSTR *ppszVendorInfo);
        
        HRESULT ( STDMETHODCALLTYPE *GetItemHandles )( 
            IOPCHDA_Server * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ LPWSTR *pszItemID,
            /* [size_is][in] */ OPCHANDLE *phClient,
            /* [size_is][size_is][out] */ OPCHANDLE **pphServer,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *ReleaseItemHandles )( 
            IOPCHDA_Server * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *ValidateItemIDs )( 
            IOPCHDA_Server * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ LPWSTR *pszItemID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *CreateBrowse )( 
            IOPCHDA_Server * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ DWORD *pdwAttrID,
            /* [size_is][in] */ OPCHDA_OPERATORCODES *pOperator,
            /* [size_is][in] */ VARIANT *vFilter,
            /* [out] */ IOPCHDA_Browser **pphBrowser,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        END_INTERFACE
    } IOPCHDA_ServerVtbl;

    interface IOPCHDA_Server
    {
        CONST_VTBL struct IOPCHDA_ServerVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCHDA_Server_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCHDA_Server_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCHDA_Server_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCHDA_Server_GetItemAttributes(This,pdwCount,ppdwAttrID,ppszAttrName,ppszAttrDesc,ppvtAttrDataType)	\
    (This)->lpVtbl -> GetItemAttributes(This,pdwCount,ppdwAttrID,ppszAttrName,ppszAttrDesc,ppvtAttrDataType)

#define IOPCHDA_Server_GetAggregates(This,pdwCount,ppdwAggrID,ppszAggrName,ppszAggrDesc)	\
    (This)->lpVtbl -> GetAggregates(This,pdwCount,ppdwAggrID,ppszAggrName,ppszAggrDesc)

#define IOPCHDA_Server_GetHistorianStatus(This,pwStatus,pftCurrentTime,pftStartTime,pwMajorVersion,pwMinorVersion,pwBuildNumber,pdwMaxReturnValues,ppszStatusString,ppszVendorInfo)	\
    (This)->lpVtbl -> GetHistorianStatus(This,pwStatus,pftCurrentTime,pftStartTime,pwMajorVersion,pwMinorVersion,pwBuildNumber,pdwMaxReturnValues,ppszStatusString,ppszVendorInfo)

#define IOPCHDA_Server_GetItemHandles(This,dwCount,pszItemID,phClient,pphServer,ppErrors)	\
    (This)->lpVtbl -> GetItemHandles(This,dwCount,pszItemID,phClient,pphServer,ppErrors)

#define IOPCHDA_Server_ReleaseItemHandles(This,dwCount,phServer,ppErrors)	\
    (This)->lpVtbl -> ReleaseItemHandles(This,dwCount,phServer,ppErrors)

#define IOPCHDA_Server_ValidateItemIDs(This,dwCount,pszItemID,ppErrors)	\
    (This)->lpVtbl -> ValidateItemIDs(This,dwCount,pszItemID,ppErrors)

#define IOPCHDA_Server_CreateBrowse(This,dwCount,pdwAttrID,pOperator,vFilter,pphBrowser,ppErrors)	\
    (This)->lpVtbl -> CreateBrowse(This,dwCount,pdwAttrID,pOperator,vFilter,pphBrowser,ppErrors)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCHDA_Server_GetItemAttributes_Proxy( 
    IOPCHDA_Server * This,
    /* [out] */ DWORD *pdwCount,
    /* [size_is][size_is][out] */ DWORD **ppdwAttrID,
    /* [string][size_is][size_is][out] */ LPWSTR **ppszAttrName,
    /* [string][size_is][size_is][out] */ LPWSTR **ppszAttrDesc,
    /* [size_is][size_is][out] */ VARTYPE **ppvtAttrDataType);


void __RPC_STUB IOPCHDA_Server_GetItemAttributes_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_Server_GetAggregates_Proxy( 
    IOPCHDA_Server * This,
    /* [out] */ DWORD *pdwCount,
    /* [size_is][size_is][out] */ DWORD **ppdwAggrID,
    /* [string][size_is][size_is][out] */ LPWSTR **ppszAggrName,
    /* [string][size_is][size_is][out] */ LPWSTR **ppszAggrDesc);


void __RPC_STUB IOPCHDA_Server_GetAggregates_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_Server_GetHistorianStatus_Proxy( 
    IOPCHDA_Server * This,
    /* [out] */ OPCHDA_SERVERSTATUS *pwStatus,
    /* [out] */ FILETIME **pftCurrentTime,
    /* [out] */ FILETIME **pftStartTime,
    /* [out] */ WORD *pwMajorVersion,
    /* [out] */ WORD *pwMinorVersion,
    /* [out] */ WORD *pwBuildNumber,
    /* [out] */ DWORD *pdwMaxReturnValues,
    /* [string][out] */ LPWSTR *ppszStatusString,
    /* [string][out] */ LPWSTR *ppszVendorInfo);


void __RPC_STUB IOPCHDA_Server_GetHistorianStatus_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_Server_GetItemHandles_Proxy( 
    IOPCHDA_Server * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ LPWSTR *pszItemID,
    /* [size_is][in] */ OPCHANDLE *phClient,
    /* [size_is][size_is][out] */ OPCHANDLE **pphServer,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_Server_GetItemHandles_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_Server_ReleaseItemHandles_Proxy( 
    IOPCHDA_Server * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_Server_ReleaseItemHandles_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_Server_ValidateItemIDs_Proxy( 
    IOPCHDA_Server * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ LPWSTR *pszItemID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_Server_ValidateItemIDs_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_Server_CreateBrowse_Proxy( 
    IOPCHDA_Server * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ DWORD *pdwAttrID,
    /* [size_is][in] */ OPCHDA_OPERATORCODES *pOperator,
    /* [size_is][in] */ VARIANT *vFilter,
    /* [out] */ IOPCHDA_Browser **pphBrowser,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_Server_CreateBrowse_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCHDA_Server_INTERFACE_DEFINED__ */


#ifndef __IOPCHDA_SyncRead_INTERFACE_DEFINED__
#define __IOPCHDA_SyncRead_INTERFACE_DEFINED__

/* interface IOPCHDA_SyncRead */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCHDA_SyncRead;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("1F1217B2-DEE0-11d2-A5E5-000086339399")
    IOPCHDA_SyncRead : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE ReadRaw( 
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumValues,
            /* [in] */ BOOL bBounds,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ OPCHDA_ITEM **ppItemValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ReadProcessed( 
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ FILETIME ftResampleInterval,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ DWORD *haAggregate,
            /* [size_is][size_is][out] */ OPCHDA_ITEM **ppItemValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ReadAtTime( 
            /* [in] */ DWORD dwNumTimeStamps,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ OPCHDA_ITEM **ppItemValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ReadModified( 
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumValues,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ OPCHDA_MODIFIEDITEM **ppItemValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ReadAttribute( 
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ OPCHANDLE hServer,
            /* [in] */ DWORD dwNumAttributes,
            /* [size_is][in] */ DWORD *pdwAttributeIDs,
            /* [size_is][size_is][out] */ OPCHDA_ATTRIBUTE **ppAttributeValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCHDA_SyncReadVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCHDA_SyncRead * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCHDA_SyncRead * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCHDA_SyncRead * This);
        
        HRESULT ( STDMETHODCALLTYPE *ReadRaw )( 
            IOPCHDA_SyncRead * This,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumValues,
            /* [in] */ BOOL bBounds,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ OPCHDA_ITEM **ppItemValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *ReadProcessed )( 
            IOPCHDA_SyncRead * This,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ FILETIME ftResampleInterval,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ DWORD *haAggregate,
            /* [size_is][size_is][out] */ OPCHDA_ITEM **ppItemValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *ReadAtTime )( 
            IOPCHDA_SyncRead * This,
            /* [in] */ DWORD dwNumTimeStamps,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ OPCHDA_ITEM **ppItemValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *ReadModified )( 
            IOPCHDA_SyncRead * This,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumValues,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ OPCHDA_MODIFIEDITEM **ppItemValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *ReadAttribute )( 
            IOPCHDA_SyncRead * This,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ OPCHANDLE hServer,
            /* [in] */ DWORD dwNumAttributes,
            /* [size_is][in] */ DWORD *pdwAttributeIDs,
            /* [size_is][size_is][out] */ OPCHDA_ATTRIBUTE **ppAttributeValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        END_INTERFACE
    } IOPCHDA_SyncReadVtbl;

    interface IOPCHDA_SyncRead
    {
        CONST_VTBL struct IOPCHDA_SyncReadVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCHDA_SyncRead_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCHDA_SyncRead_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCHDA_SyncRead_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCHDA_SyncRead_ReadRaw(This,htStartTime,htEndTime,dwNumValues,bBounds,dwNumItems,phServer,ppItemValues,ppErrors)	\
    (This)->lpVtbl -> ReadRaw(This,htStartTime,htEndTime,dwNumValues,bBounds,dwNumItems,phServer,ppItemValues,ppErrors)

#define IOPCHDA_SyncRead_ReadProcessed(This,htStartTime,htEndTime,ftResampleInterval,dwNumItems,phServer,haAggregate,ppItemValues,ppErrors)	\
    (This)->lpVtbl -> ReadProcessed(This,htStartTime,htEndTime,ftResampleInterval,dwNumItems,phServer,haAggregate,ppItemValues,ppErrors)

#define IOPCHDA_SyncRead_ReadAtTime(This,dwNumTimeStamps,ftTimeStamps,dwNumItems,phServer,ppItemValues,ppErrors)	\
    (This)->lpVtbl -> ReadAtTime(This,dwNumTimeStamps,ftTimeStamps,dwNumItems,phServer,ppItemValues,ppErrors)

#define IOPCHDA_SyncRead_ReadModified(This,htStartTime,htEndTime,dwNumValues,dwNumItems,phServer,ppItemValues,ppErrors)	\
    (This)->lpVtbl -> ReadModified(This,htStartTime,htEndTime,dwNumValues,dwNumItems,phServer,ppItemValues,ppErrors)

#define IOPCHDA_SyncRead_ReadAttribute(This,htStartTime,htEndTime,hServer,dwNumAttributes,pdwAttributeIDs,ppAttributeValues,ppErrors)	\
    (This)->lpVtbl -> ReadAttribute(This,htStartTime,htEndTime,hServer,dwNumAttributes,pdwAttributeIDs,ppAttributeValues,ppErrors)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCHDA_SyncRead_ReadRaw_Proxy( 
    IOPCHDA_SyncRead * This,
    /* [out][in] */ OPCHDA_TIME *htStartTime,
    /* [out][in] */ OPCHDA_TIME *htEndTime,
    /* [in] */ DWORD dwNumValues,
    /* [in] */ BOOL bBounds,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][size_is][out] */ OPCHDA_ITEM **ppItemValues,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_SyncRead_ReadRaw_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_SyncRead_ReadProcessed_Proxy( 
    IOPCHDA_SyncRead * This,
    /* [out][in] */ OPCHDA_TIME *htStartTime,
    /* [out][in] */ OPCHDA_TIME *htEndTime,
    /* [in] */ FILETIME ftResampleInterval,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ DWORD *haAggregate,
    /* [size_is][size_is][out] */ OPCHDA_ITEM **ppItemValues,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_SyncRead_ReadProcessed_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_SyncRead_ReadAtTime_Proxy( 
    IOPCHDA_SyncRead * This,
    /* [in] */ DWORD dwNumTimeStamps,
    /* [size_is][in] */ FILETIME *ftTimeStamps,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][size_is][out] */ OPCHDA_ITEM **ppItemValues,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_SyncRead_ReadAtTime_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_SyncRead_ReadModified_Proxy( 
    IOPCHDA_SyncRead * This,
    /* [out][in] */ OPCHDA_TIME *htStartTime,
    /* [out][in] */ OPCHDA_TIME *htEndTime,
    /* [in] */ DWORD dwNumValues,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][size_is][out] */ OPCHDA_MODIFIEDITEM **ppItemValues,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_SyncRead_ReadModified_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_SyncRead_ReadAttribute_Proxy( 
    IOPCHDA_SyncRead * This,
    /* [out][in] */ OPCHDA_TIME *htStartTime,
    /* [out][in] */ OPCHDA_TIME *htEndTime,
    /* [in] */ OPCHANDLE hServer,
    /* [in] */ DWORD dwNumAttributes,
    /* [size_is][in] */ DWORD *pdwAttributeIDs,
    /* [size_is][size_is][out] */ OPCHDA_ATTRIBUTE **ppAttributeValues,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_SyncRead_ReadAttribute_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCHDA_SyncRead_INTERFACE_DEFINED__ */


#ifndef __IOPCHDA_SyncUpdate_INTERFACE_DEFINED__
#define __IOPCHDA_SyncUpdate_INTERFACE_DEFINED__

/* interface IOPCHDA_SyncUpdate */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCHDA_SyncUpdate;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("1F1217B3-DEE0-11d2-A5E5-000086339399")
    IOPCHDA_SyncUpdate : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE QueryCapabilities( 
            /* [out] */ OPCHDA_UPDATECAPABILITIES *pCapabilities) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Insert( 
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][in] */ VARIANT *vDataValues,
            /* [size_is][in] */ DWORD *pdwQualities,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Replace( 
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][in] */ VARIANT *vDataValues,
            /* [size_is][in] */ DWORD *pdwQualities,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE InsertReplace( 
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][in] */ VARIANT *vDataValues,
            /* [size_is][in] */ DWORD *pdwQualities,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE DeleteRaw( 
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE DeleteAtTime( 
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCHDA_SyncUpdateVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCHDA_SyncUpdate * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCHDA_SyncUpdate * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCHDA_SyncUpdate * This);
        
        HRESULT ( STDMETHODCALLTYPE *QueryCapabilities )( 
            IOPCHDA_SyncUpdate * This,
            /* [out] */ OPCHDA_UPDATECAPABILITIES *pCapabilities);
        
        HRESULT ( STDMETHODCALLTYPE *Insert )( 
            IOPCHDA_SyncUpdate * This,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][in] */ VARIANT *vDataValues,
            /* [size_is][in] */ DWORD *pdwQualities,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *Replace )( 
            IOPCHDA_SyncUpdate * This,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][in] */ VARIANT *vDataValues,
            /* [size_is][in] */ DWORD *pdwQualities,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *InsertReplace )( 
            IOPCHDA_SyncUpdate * This,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][in] */ VARIANT *vDataValues,
            /* [size_is][in] */ DWORD *pdwQualities,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *DeleteRaw )( 
            IOPCHDA_SyncUpdate * This,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *DeleteAtTime )( 
            IOPCHDA_SyncUpdate * This,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        END_INTERFACE
    } IOPCHDA_SyncUpdateVtbl;

    interface IOPCHDA_SyncUpdate
    {
        CONST_VTBL struct IOPCHDA_SyncUpdateVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCHDA_SyncUpdate_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCHDA_SyncUpdate_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCHDA_SyncUpdate_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCHDA_SyncUpdate_QueryCapabilities(This,pCapabilities)	\
    (This)->lpVtbl -> QueryCapabilities(This,pCapabilities)

#define IOPCHDA_SyncUpdate_Insert(This,dwNumItems,phServer,ftTimeStamps,vDataValues,pdwQualities,ppErrors)	\
    (This)->lpVtbl -> Insert(This,dwNumItems,phServer,ftTimeStamps,vDataValues,pdwQualities,ppErrors)

#define IOPCHDA_SyncUpdate_Replace(This,dwNumItems,phServer,ftTimeStamps,vDataValues,pdwQualities,ppErrors)	\
    (This)->lpVtbl -> Replace(This,dwNumItems,phServer,ftTimeStamps,vDataValues,pdwQualities,ppErrors)

#define IOPCHDA_SyncUpdate_InsertReplace(This,dwNumItems,phServer,ftTimeStamps,vDataValues,pdwQualities,ppErrors)	\
    (This)->lpVtbl -> InsertReplace(This,dwNumItems,phServer,ftTimeStamps,vDataValues,pdwQualities,ppErrors)

#define IOPCHDA_SyncUpdate_DeleteRaw(This,htStartTime,htEndTime,dwNumItems,phServer,ppErrors)	\
    (This)->lpVtbl -> DeleteRaw(This,htStartTime,htEndTime,dwNumItems,phServer,ppErrors)

#define IOPCHDA_SyncUpdate_DeleteAtTime(This,dwNumItems,phServer,ftTimeStamps,ppErrors)	\
    (This)->lpVtbl -> DeleteAtTime(This,dwNumItems,phServer,ftTimeStamps,ppErrors)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCHDA_SyncUpdate_QueryCapabilities_Proxy( 
    IOPCHDA_SyncUpdate * This,
    /* [out] */ OPCHDA_UPDATECAPABILITIES *pCapabilities);


void __RPC_STUB IOPCHDA_SyncUpdate_QueryCapabilities_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_SyncUpdate_Insert_Proxy( 
    IOPCHDA_SyncUpdate * This,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ FILETIME *ftTimeStamps,
    /* [size_is][in] */ VARIANT *vDataValues,
    /* [size_is][in] */ DWORD *pdwQualities,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_SyncUpdate_Insert_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_SyncUpdate_Replace_Proxy( 
    IOPCHDA_SyncUpdate * This,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ FILETIME *ftTimeStamps,
    /* [size_is][in] */ VARIANT *vDataValues,
    /* [size_is][in] */ DWORD *pdwQualities,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_SyncUpdate_Replace_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_SyncUpdate_InsertReplace_Proxy( 
    IOPCHDA_SyncUpdate * This,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ FILETIME *ftTimeStamps,
    /* [size_is][in] */ VARIANT *vDataValues,
    /* [size_is][in] */ DWORD *pdwQualities,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_SyncUpdate_InsertReplace_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_SyncUpdate_DeleteRaw_Proxy( 
    IOPCHDA_SyncUpdate * This,
    /* [out][in] */ OPCHDA_TIME *htStartTime,
    /* [out][in] */ OPCHDA_TIME *htEndTime,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_SyncUpdate_DeleteRaw_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_SyncUpdate_DeleteAtTime_Proxy( 
    IOPCHDA_SyncUpdate * This,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ FILETIME *ftTimeStamps,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_SyncUpdate_DeleteAtTime_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCHDA_SyncUpdate_INTERFACE_DEFINED__ */


#ifndef __IOPCHDA_SyncAnnotations_INTERFACE_DEFINED__
#define __IOPCHDA_SyncAnnotations_INTERFACE_DEFINED__

/* interface IOPCHDA_SyncAnnotations */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCHDA_SyncAnnotations;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("1F1217B4-DEE0-11d2-A5E5-000086339399")
    IOPCHDA_SyncAnnotations : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE QueryCapabilities( 
            /* [out] */ OPCHDA_ANNOTATIONCAPABILITIES *pCapabilities) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Read( 
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ OPCHDA_ANNOTATION **ppAnnotationValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Insert( 
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][in] */ OPCHDA_ANNOTATION *pAnnotationValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCHDA_SyncAnnotationsVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCHDA_SyncAnnotations * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCHDA_SyncAnnotations * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCHDA_SyncAnnotations * This);
        
        HRESULT ( STDMETHODCALLTYPE *QueryCapabilities )( 
            IOPCHDA_SyncAnnotations * This,
            /* [out] */ OPCHDA_ANNOTATIONCAPABILITIES *pCapabilities);
        
        HRESULT ( STDMETHODCALLTYPE *Read )( 
            IOPCHDA_SyncAnnotations * This,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ OPCHDA_ANNOTATION **ppAnnotationValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *Insert )( 
            IOPCHDA_SyncAnnotations * This,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][in] */ OPCHDA_ANNOTATION *pAnnotationValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        END_INTERFACE
    } IOPCHDA_SyncAnnotationsVtbl;

    interface IOPCHDA_SyncAnnotations
    {
        CONST_VTBL struct IOPCHDA_SyncAnnotationsVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCHDA_SyncAnnotations_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCHDA_SyncAnnotations_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCHDA_SyncAnnotations_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCHDA_SyncAnnotations_QueryCapabilities(This,pCapabilities)	\
    (This)->lpVtbl -> QueryCapabilities(This,pCapabilities)

#define IOPCHDA_SyncAnnotations_Read(This,htStartTime,htEndTime,dwNumItems,phServer,ppAnnotationValues,ppErrors)	\
    (This)->lpVtbl -> Read(This,htStartTime,htEndTime,dwNumItems,phServer,ppAnnotationValues,ppErrors)

#define IOPCHDA_SyncAnnotations_Insert(This,dwNumItems,phServer,ftTimeStamps,pAnnotationValues,ppErrors)	\
    (This)->lpVtbl -> Insert(This,dwNumItems,phServer,ftTimeStamps,pAnnotationValues,ppErrors)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCHDA_SyncAnnotations_QueryCapabilities_Proxy( 
    IOPCHDA_SyncAnnotations * This,
    /* [out] */ OPCHDA_ANNOTATIONCAPABILITIES *pCapabilities);


void __RPC_STUB IOPCHDA_SyncAnnotations_QueryCapabilities_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_SyncAnnotations_Read_Proxy( 
    IOPCHDA_SyncAnnotations * This,
    /* [out][in] */ OPCHDA_TIME *htStartTime,
    /* [out][in] */ OPCHDA_TIME *htEndTime,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][size_is][out] */ OPCHDA_ANNOTATION **ppAnnotationValues,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_SyncAnnotations_Read_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_SyncAnnotations_Insert_Proxy( 
    IOPCHDA_SyncAnnotations * This,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ FILETIME *ftTimeStamps,
    /* [size_is][in] */ OPCHDA_ANNOTATION *pAnnotationValues,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_SyncAnnotations_Insert_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCHDA_SyncAnnotations_INTERFACE_DEFINED__ */


#ifndef __IOPCHDA_AsyncRead_INTERFACE_DEFINED__
#define __IOPCHDA_AsyncRead_INTERFACE_DEFINED__

/* interface IOPCHDA_AsyncRead */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCHDA_AsyncRead;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("1F1217B5-DEE0-11d2-A5E5-000086339399")
    IOPCHDA_AsyncRead : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE ReadRaw( 
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumValues,
            /* [in] */ BOOL bBounds,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE AdviseRaw( 
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [in] */ FILETIME ftUpdateInterval,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ReadProcessed( 
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ FILETIME ftResampleInterval,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ DWORD *haAggregate,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE AdviseProcessed( 
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [in] */ FILETIME ftResampleInterval,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ DWORD *haAggregate,
            /* [in] */ DWORD dwNumIntervals,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ReadAtTime( 
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ DWORD dwNumTimeStamps,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ReadModified( 
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumValues,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ReadAttribute( 
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ OPCHANDLE hServer,
            /* [in] */ DWORD dwNumAttributes,
            /* [size_is][in] */ DWORD *dwAttributeIDs,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Cancel( 
            /* [in] */ DWORD dwCancelID) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCHDA_AsyncReadVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCHDA_AsyncRead * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCHDA_AsyncRead * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCHDA_AsyncRead * This);
        
        HRESULT ( STDMETHODCALLTYPE *ReadRaw )( 
            IOPCHDA_AsyncRead * This,
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumValues,
            /* [in] */ BOOL bBounds,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *AdviseRaw )( 
            IOPCHDA_AsyncRead * This,
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [in] */ FILETIME ftUpdateInterval,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *ReadProcessed )( 
            IOPCHDA_AsyncRead * This,
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ FILETIME ftResampleInterval,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ DWORD *haAggregate,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *AdviseProcessed )( 
            IOPCHDA_AsyncRead * This,
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [in] */ FILETIME ftResampleInterval,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ DWORD *haAggregate,
            /* [in] */ DWORD dwNumIntervals,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *ReadAtTime )( 
            IOPCHDA_AsyncRead * This,
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ DWORD dwNumTimeStamps,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *ReadModified )( 
            IOPCHDA_AsyncRead * This,
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumValues,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *ReadAttribute )( 
            IOPCHDA_AsyncRead * This,
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ OPCHANDLE hServer,
            /* [in] */ DWORD dwNumAttributes,
            /* [size_is][in] */ DWORD *dwAttributeIDs,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *Cancel )( 
            IOPCHDA_AsyncRead * This,
            /* [in] */ DWORD dwCancelID);
        
        END_INTERFACE
    } IOPCHDA_AsyncReadVtbl;

    interface IOPCHDA_AsyncRead
    {
        CONST_VTBL struct IOPCHDA_AsyncReadVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCHDA_AsyncRead_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCHDA_AsyncRead_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCHDA_AsyncRead_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCHDA_AsyncRead_ReadRaw(This,dwTransactionID,htStartTime,htEndTime,dwNumValues,bBounds,dwNumItems,phServer,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> ReadRaw(This,dwTransactionID,htStartTime,htEndTime,dwNumValues,bBounds,dwNumItems,phServer,pdwCancelID,ppErrors)

#define IOPCHDA_AsyncRead_AdviseRaw(This,dwTransactionID,htStartTime,ftUpdateInterval,dwNumItems,phServer,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> AdviseRaw(This,dwTransactionID,htStartTime,ftUpdateInterval,dwNumItems,phServer,pdwCancelID,ppErrors)

#define IOPCHDA_AsyncRead_ReadProcessed(This,dwTransactionID,htStartTime,htEndTime,ftResampleInterval,dwNumItems,phServer,haAggregate,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> ReadProcessed(This,dwTransactionID,htStartTime,htEndTime,ftResampleInterval,dwNumItems,phServer,haAggregate,pdwCancelID,ppErrors)

#define IOPCHDA_AsyncRead_AdviseProcessed(This,dwTransactionID,htStartTime,ftResampleInterval,dwNumItems,phServer,haAggregate,dwNumIntervals,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> AdviseProcessed(This,dwTransactionID,htStartTime,ftResampleInterval,dwNumItems,phServer,haAggregate,dwNumIntervals,pdwCancelID,ppErrors)

#define IOPCHDA_AsyncRead_ReadAtTime(This,dwTransactionID,dwNumTimeStamps,ftTimeStamps,dwNumItems,phServer,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> ReadAtTime(This,dwTransactionID,dwNumTimeStamps,ftTimeStamps,dwNumItems,phServer,pdwCancelID,ppErrors)

#define IOPCHDA_AsyncRead_ReadModified(This,dwTransactionID,htStartTime,htEndTime,dwNumValues,dwNumItems,phServer,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> ReadModified(This,dwTransactionID,htStartTime,htEndTime,dwNumValues,dwNumItems,phServer,pdwCancelID,ppErrors)

#define IOPCHDA_AsyncRead_ReadAttribute(This,dwTransactionID,htStartTime,htEndTime,hServer,dwNumAttributes,dwAttributeIDs,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> ReadAttribute(This,dwTransactionID,htStartTime,htEndTime,hServer,dwNumAttributes,dwAttributeIDs,pdwCancelID,ppErrors)

#define IOPCHDA_AsyncRead_Cancel(This,dwCancelID)	\
    (This)->lpVtbl -> Cancel(This,dwCancelID)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncRead_ReadRaw_Proxy( 
    IOPCHDA_AsyncRead * This,
    /* [in] */ DWORD dwTransactionID,
    /* [out][in] */ OPCHDA_TIME *htStartTime,
    /* [out][in] */ OPCHDA_TIME *htEndTime,
    /* [in] */ DWORD dwNumValues,
    /* [in] */ BOOL bBounds,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_AsyncRead_ReadRaw_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncRead_AdviseRaw_Proxy( 
    IOPCHDA_AsyncRead * This,
    /* [in] */ DWORD dwTransactionID,
    /* [out][in] */ OPCHDA_TIME *htStartTime,
    /* [in] */ FILETIME ftUpdateInterval,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_AsyncRead_AdviseRaw_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncRead_ReadProcessed_Proxy( 
    IOPCHDA_AsyncRead * This,
    /* [in] */ DWORD dwTransactionID,
    /* [out][in] */ OPCHDA_TIME *htStartTime,
    /* [out][in] */ OPCHDA_TIME *htEndTime,
    /* [in] */ FILETIME ftResampleInterval,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ DWORD *haAggregate,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_AsyncRead_ReadProcessed_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncRead_AdviseProcessed_Proxy( 
    IOPCHDA_AsyncRead * This,
    /* [in] */ DWORD dwTransactionID,
    /* [out][in] */ OPCHDA_TIME *htStartTime,
    /* [in] */ FILETIME ftResampleInterval,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ DWORD *haAggregate,
    /* [in] */ DWORD dwNumIntervals,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_AsyncRead_AdviseProcessed_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncRead_ReadAtTime_Proxy( 
    IOPCHDA_AsyncRead * This,
    /* [in] */ DWORD dwTransactionID,
    /* [in] */ DWORD dwNumTimeStamps,
    /* [size_is][in] */ FILETIME *ftTimeStamps,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_AsyncRead_ReadAtTime_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncRead_ReadModified_Proxy( 
    IOPCHDA_AsyncRead * This,
    /* [in] */ DWORD dwTransactionID,
    /* [out][in] */ OPCHDA_TIME *htStartTime,
    /* [out][in] */ OPCHDA_TIME *htEndTime,
    /* [in] */ DWORD dwNumValues,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_AsyncRead_ReadModified_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncRead_ReadAttribute_Proxy( 
    IOPCHDA_AsyncRead * This,
    /* [in] */ DWORD dwTransactionID,
    /* [out][in] */ OPCHDA_TIME *htStartTime,
    /* [out][in] */ OPCHDA_TIME *htEndTime,
    /* [in] */ OPCHANDLE hServer,
    /* [in] */ DWORD dwNumAttributes,
    /* [size_is][in] */ DWORD *dwAttributeIDs,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_AsyncRead_ReadAttribute_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncRead_Cancel_Proxy( 
    IOPCHDA_AsyncRead * This,
    /* [in] */ DWORD dwCancelID);


void __RPC_STUB IOPCHDA_AsyncRead_Cancel_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCHDA_AsyncRead_INTERFACE_DEFINED__ */


#ifndef __IOPCHDA_AsyncUpdate_INTERFACE_DEFINED__
#define __IOPCHDA_AsyncUpdate_INTERFACE_DEFINED__

/* interface IOPCHDA_AsyncUpdate */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCHDA_AsyncUpdate;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("1F1217B6-DEE0-11d2-A5E5-000086339399")
    IOPCHDA_AsyncUpdate : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE QueryCapabilities( 
            /* [out] */ OPCHDA_UPDATECAPABILITIES *pCapabilities) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Insert( 
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][in] */ VARIANT *vDataValues,
            /* [size_is][in] */ DWORD *pdwQualities,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Replace( 
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][in] */ VARIANT *vDataValues,
            /* [size_is][in] */ DWORD *pdwQualities,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE InsertReplace( 
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][in] */ VARIANT *vDataValues,
            /* [size_is][in] */ DWORD *pdwQualities,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE DeleteRaw( 
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE DeleteAtTime( 
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Cancel( 
            /* [in] */ DWORD dwCancelID) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCHDA_AsyncUpdateVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCHDA_AsyncUpdate * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCHDA_AsyncUpdate * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCHDA_AsyncUpdate * This);
        
        HRESULT ( STDMETHODCALLTYPE *QueryCapabilities )( 
            IOPCHDA_AsyncUpdate * This,
            /* [out] */ OPCHDA_UPDATECAPABILITIES *pCapabilities);
        
        HRESULT ( STDMETHODCALLTYPE *Insert )( 
            IOPCHDA_AsyncUpdate * This,
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][in] */ VARIANT *vDataValues,
            /* [size_is][in] */ DWORD *pdwQualities,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *Replace )( 
            IOPCHDA_AsyncUpdate * This,
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][in] */ VARIANT *vDataValues,
            /* [size_is][in] */ DWORD *pdwQualities,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *InsertReplace )( 
            IOPCHDA_AsyncUpdate * This,
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][in] */ VARIANT *vDataValues,
            /* [size_is][in] */ DWORD *pdwQualities,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *DeleteRaw )( 
            IOPCHDA_AsyncUpdate * This,
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *DeleteAtTime )( 
            IOPCHDA_AsyncUpdate * This,
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *Cancel )( 
            IOPCHDA_AsyncUpdate * This,
            /* [in] */ DWORD dwCancelID);
        
        END_INTERFACE
    } IOPCHDA_AsyncUpdateVtbl;

    interface IOPCHDA_AsyncUpdate
    {
        CONST_VTBL struct IOPCHDA_AsyncUpdateVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCHDA_AsyncUpdate_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCHDA_AsyncUpdate_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCHDA_AsyncUpdate_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCHDA_AsyncUpdate_QueryCapabilities(This,pCapabilities)	\
    (This)->lpVtbl -> QueryCapabilities(This,pCapabilities)

#define IOPCHDA_AsyncUpdate_Insert(This,dwTransactionID,dwNumItems,phServer,ftTimeStamps,vDataValues,pdwQualities,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> Insert(This,dwTransactionID,dwNumItems,phServer,ftTimeStamps,vDataValues,pdwQualities,pdwCancelID,ppErrors)

#define IOPCHDA_AsyncUpdate_Replace(This,dwTransactionID,dwNumItems,phServer,ftTimeStamps,vDataValues,pdwQualities,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> Replace(This,dwTransactionID,dwNumItems,phServer,ftTimeStamps,vDataValues,pdwQualities,pdwCancelID,ppErrors)

#define IOPCHDA_AsyncUpdate_InsertReplace(This,dwTransactionID,dwNumItems,phServer,ftTimeStamps,vDataValues,pdwQualities,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> InsertReplace(This,dwTransactionID,dwNumItems,phServer,ftTimeStamps,vDataValues,pdwQualities,pdwCancelID,ppErrors)

#define IOPCHDA_AsyncUpdate_DeleteRaw(This,dwTransactionID,htStartTime,htEndTime,dwNumItems,phServer,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> DeleteRaw(This,dwTransactionID,htStartTime,htEndTime,dwNumItems,phServer,pdwCancelID,ppErrors)

#define IOPCHDA_AsyncUpdate_DeleteAtTime(This,dwTransactionID,dwNumItems,phServer,ftTimeStamps,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> DeleteAtTime(This,dwTransactionID,dwNumItems,phServer,ftTimeStamps,pdwCancelID,ppErrors)

#define IOPCHDA_AsyncUpdate_Cancel(This,dwCancelID)	\
    (This)->lpVtbl -> Cancel(This,dwCancelID)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncUpdate_QueryCapabilities_Proxy( 
    IOPCHDA_AsyncUpdate * This,
    /* [out] */ OPCHDA_UPDATECAPABILITIES *pCapabilities);


void __RPC_STUB IOPCHDA_AsyncUpdate_QueryCapabilities_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncUpdate_Insert_Proxy( 
    IOPCHDA_AsyncUpdate * This,
    /* [in] */ DWORD dwTransactionID,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ FILETIME *ftTimeStamps,
    /* [size_is][in] */ VARIANT *vDataValues,
    /* [size_is][in] */ DWORD *pdwQualities,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_AsyncUpdate_Insert_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncUpdate_Replace_Proxy( 
    IOPCHDA_AsyncUpdate * This,
    /* [in] */ DWORD dwTransactionID,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ FILETIME *ftTimeStamps,
    /* [size_is][in] */ VARIANT *vDataValues,
    /* [size_is][in] */ DWORD *pdwQualities,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_AsyncUpdate_Replace_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncUpdate_InsertReplace_Proxy( 
    IOPCHDA_AsyncUpdate * This,
    /* [in] */ DWORD dwTransactionID,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ FILETIME *ftTimeStamps,
    /* [size_is][in] */ VARIANT *vDataValues,
    /* [size_is][in] */ DWORD *pdwQualities,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_AsyncUpdate_InsertReplace_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncUpdate_DeleteRaw_Proxy( 
    IOPCHDA_AsyncUpdate * This,
    /* [in] */ DWORD dwTransactionID,
    /* [out][in] */ OPCHDA_TIME *htStartTime,
    /* [out][in] */ OPCHDA_TIME *htEndTime,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_AsyncUpdate_DeleteRaw_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncUpdate_DeleteAtTime_Proxy( 
    IOPCHDA_AsyncUpdate * This,
    /* [in] */ DWORD dwTransactionID,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ FILETIME *ftTimeStamps,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_AsyncUpdate_DeleteAtTime_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncUpdate_Cancel_Proxy( 
    IOPCHDA_AsyncUpdate * This,
    /* [in] */ DWORD dwCancelID);


void __RPC_STUB IOPCHDA_AsyncUpdate_Cancel_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCHDA_AsyncUpdate_INTERFACE_DEFINED__ */


#ifndef __IOPCHDA_AsyncAnnotations_INTERFACE_DEFINED__
#define __IOPCHDA_AsyncAnnotations_INTERFACE_DEFINED__

/* interface IOPCHDA_AsyncAnnotations */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCHDA_AsyncAnnotations;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("1F1217B7-DEE0-11d2-A5E5-000086339399")
    IOPCHDA_AsyncAnnotations : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE QueryCapabilities( 
            /* [out] */ OPCHDA_ANNOTATIONCAPABILITIES *pCapabilities) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Read( 
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Insert( 
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][in] */ OPCHDA_ANNOTATION *pAnnotationValues,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Cancel( 
            /* [in] */ DWORD dwCancelID) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCHDA_AsyncAnnotationsVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCHDA_AsyncAnnotations * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCHDA_AsyncAnnotations * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCHDA_AsyncAnnotations * This);
        
        HRESULT ( STDMETHODCALLTYPE *QueryCapabilities )( 
            IOPCHDA_AsyncAnnotations * This,
            /* [out] */ OPCHDA_ANNOTATIONCAPABILITIES *pCapabilities);
        
        HRESULT ( STDMETHODCALLTYPE *Read )( 
            IOPCHDA_AsyncAnnotations * This,
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *Insert )( 
            IOPCHDA_AsyncAnnotations * This,
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FILETIME *ftTimeStamps,
            /* [size_is][in] */ OPCHDA_ANNOTATION *pAnnotationValues,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *Cancel )( 
            IOPCHDA_AsyncAnnotations * This,
            /* [in] */ DWORD dwCancelID);
        
        END_INTERFACE
    } IOPCHDA_AsyncAnnotationsVtbl;

    interface IOPCHDA_AsyncAnnotations
    {
        CONST_VTBL struct IOPCHDA_AsyncAnnotationsVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCHDA_AsyncAnnotations_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCHDA_AsyncAnnotations_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCHDA_AsyncAnnotations_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCHDA_AsyncAnnotations_QueryCapabilities(This,pCapabilities)	\
    (This)->lpVtbl -> QueryCapabilities(This,pCapabilities)

#define IOPCHDA_AsyncAnnotations_Read(This,dwTransactionID,htStartTime,htEndTime,dwNumItems,phServer,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> Read(This,dwTransactionID,htStartTime,htEndTime,dwNumItems,phServer,pdwCancelID,ppErrors)

#define IOPCHDA_AsyncAnnotations_Insert(This,dwTransactionID,dwNumItems,phServer,ftTimeStamps,pAnnotationValues,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> Insert(This,dwTransactionID,dwNumItems,phServer,ftTimeStamps,pAnnotationValues,pdwCancelID,ppErrors)

#define IOPCHDA_AsyncAnnotations_Cancel(This,dwCancelID)	\
    (This)->lpVtbl -> Cancel(This,dwCancelID)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncAnnotations_QueryCapabilities_Proxy( 
    IOPCHDA_AsyncAnnotations * This,
    /* [out] */ OPCHDA_ANNOTATIONCAPABILITIES *pCapabilities);


void __RPC_STUB IOPCHDA_AsyncAnnotations_QueryCapabilities_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncAnnotations_Read_Proxy( 
    IOPCHDA_AsyncAnnotations * This,
    /* [in] */ DWORD dwTransactionID,
    /* [out][in] */ OPCHDA_TIME *htStartTime,
    /* [out][in] */ OPCHDA_TIME *htEndTime,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_AsyncAnnotations_Read_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncAnnotations_Insert_Proxy( 
    IOPCHDA_AsyncAnnotations * This,
    /* [in] */ DWORD dwTransactionID,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ FILETIME *ftTimeStamps,
    /* [size_is][in] */ OPCHDA_ANNOTATION *pAnnotationValues,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_AsyncAnnotations_Insert_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_AsyncAnnotations_Cancel_Proxy( 
    IOPCHDA_AsyncAnnotations * This,
    /* [in] */ DWORD dwCancelID);


void __RPC_STUB IOPCHDA_AsyncAnnotations_Cancel_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCHDA_AsyncAnnotations_INTERFACE_DEFINED__ */


#ifndef __IOPCHDA_Playback_INTERFACE_DEFINED__
#define __IOPCHDA_Playback_INTERFACE_DEFINED__

/* interface IOPCHDA_Playback */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCHDA_Playback;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("1F1217B8-DEE0-11d2-A5E5-000086339399")
    IOPCHDA_Playback : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE ReadRawWithUpdate( 
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumValues,
            /* [in] */ FILETIME ftUpdateDuration,
            /* [in] */ FILETIME ftUpdateInterval,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ReadProcessedWithUpdate( 
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ FILETIME ftResampleInterval,
            /* [in] */ DWORD dwNumIntervals,
            /* [in] */ FILETIME ftUpdateInterval,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ DWORD *haAggregate,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Cancel( 
            /* [in] */ DWORD dwCancelID) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCHDA_PlaybackVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCHDA_Playback * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCHDA_Playback * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCHDA_Playback * This);
        
        HRESULT ( STDMETHODCALLTYPE *ReadRawWithUpdate )( 
            IOPCHDA_Playback * This,
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ DWORD dwNumValues,
            /* [in] */ FILETIME ftUpdateDuration,
            /* [in] */ FILETIME ftUpdateInterval,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *ReadProcessedWithUpdate )( 
            IOPCHDA_Playback * This,
            /* [in] */ DWORD dwTransactionID,
            /* [out][in] */ OPCHDA_TIME *htStartTime,
            /* [out][in] */ OPCHDA_TIME *htEndTime,
            /* [in] */ FILETIME ftResampleInterval,
            /* [in] */ DWORD dwNumIntervals,
            /* [in] */ FILETIME ftUpdateInterval,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ DWORD *haAggregate,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *Cancel )( 
            IOPCHDA_Playback * This,
            /* [in] */ DWORD dwCancelID);
        
        END_INTERFACE
    } IOPCHDA_PlaybackVtbl;

    interface IOPCHDA_Playback
    {
        CONST_VTBL struct IOPCHDA_PlaybackVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCHDA_Playback_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCHDA_Playback_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCHDA_Playback_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCHDA_Playback_ReadRawWithUpdate(This,dwTransactionID,htStartTime,htEndTime,dwNumValues,ftUpdateDuration,ftUpdateInterval,dwNumItems,phServer,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> ReadRawWithUpdate(This,dwTransactionID,htStartTime,htEndTime,dwNumValues,ftUpdateDuration,ftUpdateInterval,dwNumItems,phServer,pdwCancelID,ppErrors)

#define IOPCHDA_Playback_ReadProcessedWithUpdate(This,dwTransactionID,htStartTime,htEndTime,ftResampleInterval,dwNumIntervals,ftUpdateInterval,dwNumItems,phServer,haAggregate,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> ReadProcessedWithUpdate(This,dwTransactionID,htStartTime,htEndTime,ftResampleInterval,dwNumIntervals,ftUpdateInterval,dwNumItems,phServer,haAggregate,pdwCancelID,ppErrors)

#define IOPCHDA_Playback_Cancel(This,dwCancelID)	\
    (This)->lpVtbl -> Cancel(This,dwCancelID)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCHDA_Playback_ReadRawWithUpdate_Proxy( 
    IOPCHDA_Playback * This,
    /* [in] */ DWORD dwTransactionID,
    /* [out][in] */ OPCHDA_TIME *htStartTime,
    /* [out][in] */ OPCHDA_TIME *htEndTime,
    /* [in] */ DWORD dwNumValues,
    /* [in] */ FILETIME ftUpdateDuration,
    /* [in] */ FILETIME ftUpdateInterval,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_Playback_ReadRawWithUpdate_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_Playback_ReadProcessedWithUpdate_Proxy( 
    IOPCHDA_Playback * This,
    /* [in] */ DWORD dwTransactionID,
    /* [out][in] */ OPCHDA_TIME *htStartTime,
    /* [out][in] */ OPCHDA_TIME *htEndTime,
    /* [in] */ FILETIME ftResampleInterval,
    /* [in] */ DWORD dwNumIntervals,
    /* [in] */ FILETIME ftUpdateInterval,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ DWORD *haAggregate,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCHDA_Playback_ReadProcessedWithUpdate_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_Playback_Cancel_Proxy( 
    IOPCHDA_Playback * This,
    /* [in] */ DWORD dwCancelID);


void __RPC_STUB IOPCHDA_Playback_Cancel_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCHDA_Playback_INTERFACE_DEFINED__ */


#ifndef __IOPCHDA_DataCallback_INTERFACE_DEFINED__
#define __IOPCHDA_DataCallback_INTERFACE_DEFINED__

/* interface IOPCHDA_DataCallback */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCHDA_DataCallback;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("1F1217B9-DEE0-11d2-A5E5-000086339399")
    IOPCHDA_DataCallback : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE OnDataChange( 
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ HRESULT hrStatus,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHDA_ITEM *pItemValues,
            /* [size_is][in] */ HRESULT *phrErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE OnReadComplete( 
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ HRESULT hrStatus,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHDA_ITEM *pItemValues,
            /* [size_is][in] */ HRESULT *phrErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE OnReadModifiedComplete( 
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ HRESULT hrStatus,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHDA_MODIFIEDITEM *pItemValues,
            /* [size_is][in] */ HRESULT *phrErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE OnReadAttributeComplete( 
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ HRESULT hrStatus,
            /* [in] */ OPCHANDLE hClient,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHDA_ATTRIBUTE *pAttributeValues,
            /* [size_is][in] */ HRESULT *phrErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE OnReadAnnotations( 
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ HRESULT hrStatus,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHDA_ANNOTATION *pAnnotationValues,
            /* [size_is][in] */ HRESULT *phrErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE OnInsertAnnotations( 
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ HRESULT hrStatus,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phClients,
            /* [size_is][in] */ HRESULT *phrErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE OnPlayback( 
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ HRESULT hrStatus,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHDA_ITEM **ppItemValues,
            /* [size_is][in] */ HRESULT *phrErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE OnUpdateComplete( 
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ HRESULT hrStatus,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phClients,
            /* [size_is][in] */ HRESULT *phrErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE OnCancelComplete( 
            /* [in] */ DWORD dwCancelID) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCHDA_DataCallbackVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCHDA_DataCallback * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCHDA_DataCallback * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCHDA_DataCallback * This);
        
        HRESULT ( STDMETHODCALLTYPE *OnDataChange )( 
            IOPCHDA_DataCallback * This,
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ HRESULT hrStatus,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHDA_ITEM *pItemValues,
            /* [size_is][in] */ HRESULT *phrErrors);
        
        HRESULT ( STDMETHODCALLTYPE *OnReadComplete )( 
            IOPCHDA_DataCallback * This,
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ HRESULT hrStatus,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHDA_ITEM *pItemValues,
            /* [size_is][in] */ HRESULT *phrErrors);
        
        HRESULT ( STDMETHODCALLTYPE *OnReadModifiedComplete )( 
            IOPCHDA_DataCallback * This,
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ HRESULT hrStatus,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHDA_MODIFIEDITEM *pItemValues,
            /* [size_is][in] */ HRESULT *phrErrors);
        
        HRESULT ( STDMETHODCALLTYPE *OnReadAttributeComplete )( 
            IOPCHDA_DataCallback * This,
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ HRESULT hrStatus,
            /* [in] */ OPCHANDLE hClient,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHDA_ATTRIBUTE *pAttributeValues,
            /* [size_is][in] */ HRESULT *phrErrors);
        
        HRESULT ( STDMETHODCALLTYPE *OnReadAnnotations )( 
            IOPCHDA_DataCallback * This,
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ HRESULT hrStatus,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHDA_ANNOTATION *pAnnotationValues,
            /* [size_is][in] */ HRESULT *phrErrors);
        
        HRESULT ( STDMETHODCALLTYPE *OnInsertAnnotations )( 
            IOPCHDA_DataCallback * This,
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ HRESULT hrStatus,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phClients,
            /* [size_is][in] */ HRESULT *phrErrors);
        
        HRESULT ( STDMETHODCALLTYPE *OnPlayback )( 
            IOPCHDA_DataCallback * This,
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ HRESULT hrStatus,
            /* [in] */ DWORD dwNumItems,
            /* [size_is][in] */ OPCHDA_ITEM **ppItemValues,
            /* [size_is][in] */ HRESULT *phrErrors);
        
        HRESULT ( STDMETHODCALLTYPE *OnUpdateComplete )( 
            IOPCHDA_DataCallback * This,
            /* [in] */ DWORD dwTransactionID,
            /* [in] */ HRESULT hrStatus,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phClients,
            /* [size_is][in] */ HRESULT *phrErrors);
        
        HRESULT ( STDMETHODCALLTYPE *OnCancelComplete )( 
            IOPCHDA_DataCallback * This,
            /* [in] */ DWORD dwCancelID);
        
        END_INTERFACE
    } IOPCHDA_DataCallbackVtbl;

    interface IOPCHDA_DataCallback
    {
        CONST_VTBL struct IOPCHDA_DataCallbackVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCHDA_DataCallback_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCHDA_DataCallback_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCHDA_DataCallback_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCHDA_DataCallback_OnDataChange(This,dwTransactionID,hrStatus,dwNumItems,pItemValues,phrErrors)	\
    (This)->lpVtbl -> OnDataChange(This,dwTransactionID,hrStatus,dwNumItems,pItemValues,phrErrors)

#define IOPCHDA_DataCallback_OnReadComplete(This,dwTransactionID,hrStatus,dwNumItems,pItemValues,phrErrors)	\
    (This)->lpVtbl -> OnReadComplete(This,dwTransactionID,hrStatus,dwNumItems,pItemValues,phrErrors)

#define IOPCHDA_DataCallback_OnReadModifiedComplete(This,dwTransactionID,hrStatus,dwNumItems,pItemValues,phrErrors)	\
    (This)->lpVtbl -> OnReadModifiedComplete(This,dwTransactionID,hrStatus,dwNumItems,pItemValues,phrErrors)

#define IOPCHDA_DataCallback_OnReadAttributeComplete(This,dwTransactionID,hrStatus,hClient,dwNumItems,pAttributeValues,phrErrors)	\
    (This)->lpVtbl -> OnReadAttributeComplete(This,dwTransactionID,hrStatus,hClient,dwNumItems,pAttributeValues,phrErrors)

#define IOPCHDA_DataCallback_OnReadAnnotations(This,dwTransactionID,hrStatus,dwNumItems,pAnnotationValues,phrErrors)	\
    (This)->lpVtbl -> OnReadAnnotations(This,dwTransactionID,hrStatus,dwNumItems,pAnnotationValues,phrErrors)

#define IOPCHDA_DataCallback_OnInsertAnnotations(This,dwTransactionID,hrStatus,dwCount,phClients,phrErrors)	\
    (This)->lpVtbl -> OnInsertAnnotations(This,dwTransactionID,hrStatus,dwCount,phClients,phrErrors)

#define IOPCHDA_DataCallback_OnPlayback(This,dwTransactionID,hrStatus,dwNumItems,ppItemValues,phrErrors)	\
    (This)->lpVtbl -> OnPlayback(This,dwTransactionID,hrStatus,dwNumItems,ppItemValues,phrErrors)

#define IOPCHDA_DataCallback_OnUpdateComplete(This,dwTransactionID,hrStatus,dwCount,phClients,phrErrors)	\
    (This)->lpVtbl -> OnUpdateComplete(This,dwTransactionID,hrStatus,dwCount,phClients,phrErrors)

#define IOPCHDA_DataCallback_OnCancelComplete(This,dwCancelID)	\
    (This)->lpVtbl -> OnCancelComplete(This,dwCancelID)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCHDA_DataCallback_OnDataChange_Proxy( 
    IOPCHDA_DataCallback * This,
    /* [in] */ DWORD dwTransactionID,
    /* [in] */ HRESULT hrStatus,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHDA_ITEM *pItemValues,
    /* [size_is][in] */ HRESULT *phrErrors);


void __RPC_STUB IOPCHDA_DataCallback_OnDataChange_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_DataCallback_OnReadComplete_Proxy( 
    IOPCHDA_DataCallback * This,
    /* [in] */ DWORD dwTransactionID,
    /* [in] */ HRESULT hrStatus,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHDA_ITEM *pItemValues,
    /* [size_is][in] */ HRESULT *phrErrors);


void __RPC_STUB IOPCHDA_DataCallback_OnReadComplete_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_DataCallback_OnReadModifiedComplete_Proxy( 
    IOPCHDA_DataCallback * This,
    /* [in] */ DWORD dwTransactionID,
    /* [in] */ HRESULT hrStatus,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHDA_MODIFIEDITEM *pItemValues,
    /* [size_is][in] */ HRESULT *phrErrors);


void __RPC_STUB IOPCHDA_DataCallback_OnReadModifiedComplete_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_DataCallback_OnReadAttributeComplete_Proxy( 
    IOPCHDA_DataCallback * This,
    /* [in] */ DWORD dwTransactionID,
    /* [in] */ HRESULT hrStatus,
    /* [in] */ OPCHANDLE hClient,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHDA_ATTRIBUTE *pAttributeValues,
    /* [size_is][in] */ HRESULT *phrErrors);


void __RPC_STUB IOPCHDA_DataCallback_OnReadAttributeComplete_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_DataCallback_OnReadAnnotations_Proxy( 
    IOPCHDA_DataCallback * This,
    /* [in] */ DWORD dwTransactionID,
    /* [in] */ HRESULT hrStatus,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHDA_ANNOTATION *pAnnotationValues,
    /* [size_is][in] */ HRESULT *phrErrors);


void __RPC_STUB IOPCHDA_DataCallback_OnReadAnnotations_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_DataCallback_OnInsertAnnotations_Proxy( 
    IOPCHDA_DataCallback * This,
    /* [in] */ DWORD dwTransactionID,
    /* [in] */ HRESULT hrStatus,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phClients,
    /* [size_is][in] */ HRESULT *phrErrors);


void __RPC_STUB IOPCHDA_DataCallback_OnInsertAnnotations_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_DataCallback_OnPlayback_Proxy( 
    IOPCHDA_DataCallback * This,
    /* [in] */ DWORD dwTransactionID,
    /* [in] */ HRESULT hrStatus,
    /* [in] */ DWORD dwNumItems,
    /* [size_is][in] */ OPCHDA_ITEM **ppItemValues,
    /* [size_is][in] */ HRESULT *phrErrors);


void __RPC_STUB IOPCHDA_DataCallback_OnPlayback_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_DataCallback_OnUpdateComplete_Proxy( 
    IOPCHDA_DataCallback * This,
    /* [in] */ DWORD dwTransactionID,
    /* [in] */ HRESULT hrStatus,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phClients,
    /* [size_is][in] */ HRESULT *phrErrors);


void __RPC_STUB IOPCHDA_DataCallback_OnUpdateComplete_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCHDA_DataCallback_OnCancelComplete_Proxy( 
    IOPCHDA_DataCallback * This,
    /* [in] */ DWORD dwCancelID);


void __RPC_STUB IOPCHDA_DataCallback_OnCancelComplete_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCHDA_DataCallback_INTERFACE_DEFINED__ */



#ifndef __OPCHDA_LIBRARY_DEFINED__
#define __OPCHDA_LIBRARY_DEFINED__

/* library OPCHDA */
/* [helpstring][version][uuid] */ 













EXTERN_C const IID LIBID_OPCHDA;


#ifndef __OPCHDA_Constants_MODULE_DEFINED__
#define __OPCHDA_Constants_MODULE_DEFINED__


/* module OPCHDA_Constants */


const LPCWSTR OPC_CATEGORY_DESCRIPTION_HDA10	=	L"OPC History Data Access Servers Version 1.0";

const DWORD OPCHDA_DATA_TYPE	=	0x1;

const DWORD OPCHDA_DESCRIPTION	=	0x2;

const DWORD OPCHDA_ENG_UNITS	=	0x3;

const DWORD OPCHDA_STEPPED	=	0x4;

const DWORD OPCHDA_ARCHIVING	=	0x5;

const DWORD OPCHDA_DERIVE_EQUATION	=	0x6;

const DWORD OPCHDA_NODE_NAME	=	0x7;

const DWORD OPCHDA_PROCESS_NAME	=	0x8;

const DWORD OPCHDA_SOURCE_NAME	=	0x9;

const DWORD OPCHDA_SOURCE_TYPE	=	0xa;

const DWORD OPCHDA_NORMAL_MAXIMUM	=	0xb;

const DWORD OPCHDA_NORMAL_MINIMUM	=	0xc;

const DWORD OPCHDA_ITEMID	=	0xd;

const DWORD OPCHDA_MAX_TIME_INT	=	0xe;

const DWORD OPCHDA_MIN_TIME_INT	=	0xf;

const DWORD OPCHDA_EXCEPTION_DEV	=	0x10;

const DWORD OPCHDA_EXCEPTION_DEV_TYPE	=	0x11;

const DWORD OPCHDA_HIGH_ENTRY_LIMIT	=	0x12;

const DWORD OPCHDA_LOW_ENTRY_LIMIT	=	0x13;

const LPCWSTR OPCHDA_ATTRNAME_DATA_TYPE	=	L"Data Type";

const LPCWSTR OPCHDA_ATTRNAME_DESCRIPTION	=	L"Description";

const LPCWSTR OPCHDA_ATTRNAME_ENG_UNITS	=	L"Eng Units";

const LPCWSTR OPCHDA_ATTRNAME_STEPPED	=	L"Stepped";

const LPCWSTR OPCHDA_ATTRNAME_ARCHIVING	=	L"Archiving";

const LPCWSTR OPCHDA_ATTRNAME_DERIVE_EQUATION	=	L"Derive Equation";

const LPCWSTR OPCHDA_ATTRNAME_NODE_NAME	=	L"Node Name";

const LPCWSTR OPCHDA_ATTRNAME_PROCESS_NAME	=	L"Process Name";

const LPCWSTR OPCHDA_ATTRNAME_SOURCE_NAME	=	L"Source Name";

const LPCWSTR OPCHDA_ATTRNAME_SOURCE_TYPE	=	L"Source Type";

const LPCWSTR OPCHDA_ATTRNAME_NORMAL_MAXIMUM	=	L"Normal Maximum";

const LPCWSTR OPCHDA_ATTRNAME_NORMAL_MINIMUM	=	L"Normal Minimum";

const LPCWSTR OPCHDA_ATTRNAME_ITEMID	=	L"ItemID";

const LPCWSTR OPCHDA_ATTRNAME_MAX_TIME_INT	=	L"Max Time Interval";

const LPCWSTR OPCHDA_ATTRNAME_MIN_TIME_INT	=	L"Min Time Interval";

const LPCWSTR OPCHDA_ATTRNAME_EXCEPTION_DEV	=	L"Exception Deviation";

const LPCWSTR OPCHDA_ATTRNAME_EXCEPTION_DEV_TYPE	=	L"Exception Dev Type";

const LPCWSTR OPCHDA_ATTRNAME_HIGH_ENTRY_LIMIT	=	L"High Entry Limit";

const LPCWSTR OPCHDA_ATTRNAME_LOW_ENTRY_LIMIT	=	L"Low Entry Limit";

const LPCWSTR OPCHDA_AGGRNAME_INTERPOLATIVE	=	L"Interpolative";

const LPCWSTR OPCHDA_AGGRNAME_TOTAL	=	L"Total";

const LPCWSTR OPCHDA_AGGRNAME_AVERAGE	=	L"Average";

const LPCWSTR OPCHDA_AGGRNAME_TIMEAVERAGE	=	L"Time Average";

const LPCWSTR OPCHDA_AGGRNAME_COUNT	=	L"Count";

const LPCWSTR OPCHDA_AGGRNAME_STDEV	=	L"Standard Deviation";

const LPCWSTR OPCHDA_AGGRNAME_MINIMUMACTUALTIME	=	L"Minimum Actual Time";

const LPCWSTR OPCHDA_AGGRNAME_MINIMUM	=	L"Minimum";

const LPCWSTR OPCHDA_AGGRNAME_MAXIMUMACTUALTIME	=	L"Maximum Actual Time";

const LPCWSTR OPCHDA_AGGRNAME_MAXIMUM	=	L"Maximum";

const LPCWSTR OPCHDA_AGGRNAME_START	=	L"Start";

const LPCWSTR OPCHDA_AGGRNAME_END	=	L"End";

const LPCWSTR OPCHDA_AGGRNAME_DELTA	=	L"Delta";

const LPCWSTR OPCHDA_AGGRNAME_REGSLOPE	=	L"Regression Line Slope";

const LPCWSTR OPCHDA_AGGRNAME_REGCONST	=	L"Regression Line Constant";

const LPCWSTR OPCHDA_AGGRNAME_REGDEV	=	L"Regression Line Error";

const LPCWSTR OPCHDA_AGGRNAME_VARIANCE	=	L"Variance";

const LPCWSTR OPCHDA_AGGRNAME_RANGE	=	L"Range";

const LPCWSTR OPCHDA_AGGRNAME_DURATIONGOOD	=	L"Duration Good";

const LPCWSTR OPCHDA_AGGRNAME_DURATIONBAD	=	L"Duration Bad";

const LPCWSTR OPCHDA_AGGRNAME_PERCENTGOOD	=	L"Percent Good";

const LPCWSTR OPCHDA_AGGRNAME_PERCENTBAD	=	L"Percent Bad";

const LPCWSTR OPCHDA_AGGRNAME_WORSTQUALITY	=	L"Worst Quality";

const LPCWSTR OPCHDA_AGGRNAME_ANNOTATIONS	=	L"Annotations";

const DWORD OPCHDA_EXTRADATA	=	0x10000;

const DWORD OPCHDA_INTERPOLATED	=	0x20000;

const DWORD OPCHDA_RAW	=	0x40000;

const DWORD OPCHDA_CALCULATED	=	0x80000;

const DWORD OPCHDA_NOBOUND	=	0x100000;

const DWORD OPCHDA_NODATA	=	0x200000;

const DWORD OPCHDA_DATALOST	=	0x400000;

const DWORD OPCHDA_CONVERSION	=	0x800000;

const DWORD OPCHDA_PARTIAL	=	0x1000000;

#endif /* __OPCHDA_Constants_MODULE_DEFINED__ */
#endif /* __OPCHDA_LIBRARY_DEFINED__ */

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
