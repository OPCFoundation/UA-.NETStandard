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

#ifndef __opcda_h__
#define __opcda_h__

#if defined(_MSC_VER) && (_MSC_VER >= 1020)
#pragma once
#endif

/* Forward Declarations */ 

#ifndef __CATID_OPCDAServer10_FWD_DEFINED__
#define __CATID_OPCDAServer10_FWD_DEFINED__
typedef interface CATID_OPCDAServer10 CATID_OPCDAServer10;
#endif 	/* __CATID_OPCDAServer10_FWD_DEFINED__ */


#ifndef __CATID_OPCDAServer20_FWD_DEFINED__
#define __CATID_OPCDAServer20_FWD_DEFINED__
typedef interface CATID_OPCDAServer20 CATID_OPCDAServer20;
#endif 	/* __CATID_OPCDAServer20_FWD_DEFINED__ */


#ifndef __CATID_OPCDAServer30_FWD_DEFINED__
#define __CATID_OPCDAServer30_FWD_DEFINED__
typedef interface CATID_OPCDAServer30 CATID_OPCDAServer30;
#endif 	/* __CATID_OPCDAServer30_FWD_DEFINED__ */


#ifndef __CATID_XMLDAServer10_FWD_DEFINED__
#define __CATID_XMLDAServer10_FWD_DEFINED__
typedef interface CATID_XMLDAServer10 CATID_XMLDAServer10;
#endif 	/* __CATID_XMLDAServer10_FWD_DEFINED__ */


#ifndef __IOPCServer_FWD_DEFINED__
#define __IOPCServer_FWD_DEFINED__
typedef interface IOPCServer IOPCServer;
#endif 	/* __IOPCServer_FWD_DEFINED__ */


#ifndef __IOPCServerPublicGroups_FWD_DEFINED__
#define __IOPCServerPublicGroups_FWD_DEFINED__
typedef interface IOPCServerPublicGroups IOPCServerPublicGroups;
#endif 	/* __IOPCServerPublicGroups_FWD_DEFINED__ */


#ifndef __IOPCBrowseServerAddressSpace_FWD_DEFINED__
#define __IOPCBrowseServerAddressSpace_FWD_DEFINED__
typedef interface IOPCBrowseServerAddressSpace IOPCBrowseServerAddressSpace;
#endif 	/* __IOPCBrowseServerAddressSpace_FWD_DEFINED__ */


#ifndef __IOPCGroupStateMgt_FWD_DEFINED__
#define __IOPCGroupStateMgt_FWD_DEFINED__
typedef interface IOPCGroupStateMgt IOPCGroupStateMgt;
#endif 	/* __IOPCGroupStateMgt_FWD_DEFINED__ */


#ifndef __IOPCPublicGroupStateMgt_FWD_DEFINED__
#define __IOPCPublicGroupStateMgt_FWD_DEFINED__
typedef interface IOPCPublicGroupStateMgt IOPCPublicGroupStateMgt;
#endif 	/* __IOPCPublicGroupStateMgt_FWD_DEFINED__ */


#ifndef __IOPCSyncIO_FWD_DEFINED__
#define __IOPCSyncIO_FWD_DEFINED__
typedef interface IOPCSyncIO IOPCSyncIO;
#endif 	/* __IOPCSyncIO_FWD_DEFINED__ */


#ifndef __IOPCAsyncIO_FWD_DEFINED__
#define __IOPCAsyncIO_FWD_DEFINED__
typedef interface IOPCAsyncIO IOPCAsyncIO;
#endif 	/* __IOPCAsyncIO_FWD_DEFINED__ */


#ifndef __IOPCItemMgt_FWD_DEFINED__
#define __IOPCItemMgt_FWD_DEFINED__
typedef interface IOPCItemMgt IOPCItemMgt;
#endif 	/* __IOPCItemMgt_FWD_DEFINED__ */


#ifndef __IEnumOPCItemAttributes_FWD_DEFINED__
#define __IEnumOPCItemAttributes_FWD_DEFINED__
typedef interface IEnumOPCItemAttributes IEnumOPCItemAttributes;
#endif 	/* __IEnumOPCItemAttributes_FWD_DEFINED__ */


#ifndef __IOPCDataCallback_FWD_DEFINED__
#define __IOPCDataCallback_FWD_DEFINED__
typedef interface IOPCDataCallback IOPCDataCallback;
#endif 	/* __IOPCDataCallback_FWD_DEFINED__ */


#ifndef __IOPCAsyncIO2_FWD_DEFINED__
#define __IOPCAsyncIO2_FWD_DEFINED__
typedef interface IOPCAsyncIO2 IOPCAsyncIO2;
#endif 	/* __IOPCAsyncIO2_FWD_DEFINED__ */


#ifndef __IOPCItemProperties_FWD_DEFINED__
#define __IOPCItemProperties_FWD_DEFINED__
typedef interface IOPCItemProperties IOPCItemProperties;
#endif 	/* __IOPCItemProperties_FWD_DEFINED__ */


#ifndef __IOPCItemDeadbandMgt_FWD_DEFINED__
#define __IOPCItemDeadbandMgt_FWD_DEFINED__
typedef interface IOPCItemDeadbandMgt IOPCItemDeadbandMgt;
#endif 	/* __IOPCItemDeadbandMgt_FWD_DEFINED__ */


#ifndef __IOPCItemSamplingMgt_FWD_DEFINED__
#define __IOPCItemSamplingMgt_FWD_DEFINED__
typedef interface IOPCItemSamplingMgt IOPCItemSamplingMgt;
#endif 	/* __IOPCItemSamplingMgt_FWD_DEFINED__ */


#ifndef __IOPCBrowse_FWD_DEFINED__
#define __IOPCBrowse_FWD_DEFINED__
typedef interface IOPCBrowse IOPCBrowse;
#endif 	/* __IOPCBrowse_FWD_DEFINED__ */


#ifndef __IOPCItemIO_FWD_DEFINED__
#define __IOPCItemIO_FWD_DEFINED__
typedef interface IOPCItemIO IOPCItemIO;
#endif 	/* __IOPCItemIO_FWD_DEFINED__ */


#ifndef __IOPCSyncIO2_FWD_DEFINED__
#define __IOPCSyncIO2_FWD_DEFINED__
typedef interface IOPCSyncIO2 IOPCSyncIO2;
#endif 	/* __IOPCSyncIO2_FWD_DEFINED__ */


#ifndef __IOPCAsyncIO3_FWD_DEFINED__
#define __IOPCAsyncIO3_FWD_DEFINED__
typedef interface IOPCAsyncIO3 IOPCAsyncIO3;
#endif 	/* __IOPCAsyncIO3_FWD_DEFINED__ */


#ifndef __IOPCGroupStateMgt2_FWD_DEFINED__
#define __IOPCGroupStateMgt2_FWD_DEFINED__
typedef interface IOPCGroupStateMgt2 IOPCGroupStateMgt2;
#endif 	/* __IOPCGroupStateMgt2_FWD_DEFINED__ */


#ifndef __CATID_OPCDAServer10_FWD_DEFINED__
#define __CATID_OPCDAServer10_FWD_DEFINED__
typedef interface CATID_OPCDAServer10 CATID_OPCDAServer10;
#endif 	/* __CATID_OPCDAServer10_FWD_DEFINED__ */


#ifndef __CATID_OPCDAServer20_FWD_DEFINED__
#define __CATID_OPCDAServer20_FWD_DEFINED__
typedef interface CATID_OPCDAServer20 CATID_OPCDAServer20;
#endif 	/* __CATID_OPCDAServer20_FWD_DEFINED__ */


#ifndef __CATID_OPCDAServer30_FWD_DEFINED__
#define __CATID_OPCDAServer30_FWD_DEFINED__
typedef interface CATID_OPCDAServer30 CATID_OPCDAServer30;
#endif 	/* __CATID_OPCDAServer30_FWD_DEFINED__ */


#ifndef __CATID_XMLDAServer10_FWD_DEFINED__
#define __CATID_XMLDAServer10_FWD_DEFINED__
typedef interface CATID_XMLDAServer10 CATID_XMLDAServer10;
#endif 	/* __CATID_XMLDAServer10_FWD_DEFINED__ */


#ifndef __IOPCServer_FWD_DEFINED__
#define __IOPCServer_FWD_DEFINED__
typedef interface IOPCServer IOPCServer;
#endif 	/* __IOPCServer_FWD_DEFINED__ */


#ifndef __IOPCServerPublicGroups_FWD_DEFINED__
#define __IOPCServerPublicGroups_FWD_DEFINED__
typedef interface IOPCServerPublicGroups IOPCServerPublicGroups;
#endif 	/* __IOPCServerPublicGroups_FWD_DEFINED__ */


#ifndef __IOPCBrowseServerAddressSpace_FWD_DEFINED__
#define __IOPCBrowseServerAddressSpace_FWD_DEFINED__
typedef interface IOPCBrowseServerAddressSpace IOPCBrowseServerAddressSpace;
#endif 	/* __IOPCBrowseServerAddressSpace_FWD_DEFINED__ */


#ifndef __IOPCGroupStateMgt_FWD_DEFINED__
#define __IOPCGroupStateMgt_FWD_DEFINED__
typedef interface IOPCGroupStateMgt IOPCGroupStateMgt;
#endif 	/* __IOPCGroupStateMgt_FWD_DEFINED__ */


#ifndef __IOPCPublicGroupStateMgt_FWD_DEFINED__
#define __IOPCPublicGroupStateMgt_FWD_DEFINED__
typedef interface IOPCPublicGroupStateMgt IOPCPublicGroupStateMgt;
#endif 	/* __IOPCPublicGroupStateMgt_FWD_DEFINED__ */


#ifndef __IOPCSyncIO_FWD_DEFINED__
#define __IOPCSyncIO_FWD_DEFINED__
typedef interface IOPCSyncIO IOPCSyncIO;
#endif 	/* __IOPCSyncIO_FWD_DEFINED__ */


#ifndef __IOPCAsyncIO_FWD_DEFINED__
#define __IOPCAsyncIO_FWD_DEFINED__
typedef interface IOPCAsyncIO IOPCAsyncIO;
#endif 	/* __IOPCAsyncIO_FWD_DEFINED__ */


#ifndef __IOPCDataCallback_FWD_DEFINED__
#define __IOPCDataCallback_FWD_DEFINED__
typedef interface IOPCDataCallback IOPCDataCallback;
#endif 	/* __IOPCDataCallback_FWD_DEFINED__ */


#ifndef __IOPCItemMgt_FWD_DEFINED__
#define __IOPCItemMgt_FWD_DEFINED__
typedef interface IOPCItemMgt IOPCItemMgt;
#endif 	/* __IOPCItemMgt_FWD_DEFINED__ */


#ifndef __IEnumOPCItemAttributes_FWD_DEFINED__
#define __IEnumOPCItemAttributes_FWD_DEFINED__
typedef interface IEnumOPCItemAttributes IEnumOPCItemAttributes;
#endif 	/* __IEnumOPCItemAttributes_FWD_DEFINED__ */


#ifndef __IOPCAsyncIO2_FWD_DEFINED__
#define __IOPCAsyncIO2_FWD_DEFINED__
typedef interface IOPCAsyncIO2 IOPCAsyncIO2;
#endif 	/* __IOPCAsyncIO2_FWD_DEFINED__ */


#ifndef __IOPCItemProperties_FWD_DEFINED__
#define __IOPCItemProperties_FWD_DEFINED__
typedef interface IOPCItemProperties IOPCItemProperties;
#endif 	/* __IOPCItemProperties_FWD_DEFINED__ */


#ifndef __IOPCItemDeadbandMgt_FWD_DEFINED__
#define __IOPCItemDeadbandMgt_FWD_DEFINED__
typedef interface IOPCItemDeadbandMgt IOPCItemDeadbandMgt;
#endif 	/* __IOPCItemDeadbandMgt_FWD_DEFINED__ */


#ifndef __IOPCItemSamplingMgt_FWD_DEFINED__
#define __IOPCItemSamplingMgt_FWD_DEFINED__
typedef interface IOPCItemSamplingMgt IOPCItemSamplingMgt;
#endif 	/* __IOPCItemSamplingMgt_FWD_DEFINED__ */


#ifndef __IOPCBrowse_FWD_DEFINED__
#define __IOPCBrowse_FWD_DEFINED__
typedef interface IOPCBrowse IOPCBrowse;
#endif 	/* __IOPCBrowse_FWD_DEFINED__ */


#ifndef __IOPCItemIO_FWD_DEFINED__
#define __IOPCItemIO_FWD_DEFINED__
typedef interface IOPCItemIO IOPCItemIO;
#endif 	/* __IOPCItemIO_FWD_DEFINED__ */


#ifndef __IOPCSyncIO2_FWD_DEFINED__
#define __IOPCSyncIO2_FWD_DEFINED__
typedef interface IOPCSyncIO2 IOPCSyncIO2;
#endif 	/* __IOPCSyncIO2_FWD_DEFINED__ */


#ifndef __IOPCAsyncIO3_FWD_DEFINED__
#define __IOPCAsyncIO3_FWD_DEFINED__
typedef interface IOPCAsyncIO3 IOPCAsyncIO3;
#endif 	/* __IOPCAsyncIO3_FWD_DEFINED__ */


#ifndef __IOPCGroupStateMgt2_FWD_DEFINED__
#define __IOPCGroupStateMgt2_FWD_DEFINED__
typedef interface IOPCGroupStateMgt2 IOPCGroupStateMgt2;
#endif 	/* __IOPCGroupStateMgt2_FWD_DEFINED__ */


/* header files for imported files */
#include "oaidl.h"
#include "ocidl.h"

#ifdef __cplusplus
extern "C"{
#endif 

void * __RPC_USER MIDL_user_allocate(size_t);
void __RPC_USER MIDL_user_free( void * ); 

#ifndef __CATID_OPCDAServer10_INTERFACE_DEFINED__
#define __CATID_OPCDAServer10_INTERFACE_DEFINED__

/* interface CATID_OPCDAServer10 */
/* [object][uuid] */ 


EXTERN_C const IID IID_CATID_OPCDAServer10;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("63D5F430-CFE4-11d1-B2C8-0060083BA1FB")
    CATID_OPCDAServer10 : public IUnknown
    {
    public:
    };
    
#else 	/* C style interface */

    typedef struct CATID_OPCDAServer10Vtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            CATID_OPCDAServer10 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            CATID_OPCDAServer10 * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            CATID_OPCDAServer10 * This);
        
        END_INTERFACE
    } CATID_OPCDAServer10Vtbl;

    interface CATID_OPCDAServer10
    {
        CONST_VTBL struct CATID_OPCDAServer10Vtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define CATID_OPCDAServer10_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define CATID_OPCDAServer10_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define CATID_OPCDAServer10_Release(This)	\
    (This)->lpVtbl -> Release(This)


#endif /* COBJMACROS */


#endif 	/* C style interface */




#endif 	/* __CATID_OPCDAServer10_INTERFACE_DEFINED__ */


#ifndef __CATID_OPCDAServer20_INTERFACE_DEFINED__
#define __CATID_OPCDAServer20_INTERFACE_DEFINED__

/* interface CATID_OPCDAServer20 */
/* [object][uuid] */ 


EXTERN_C const IID IID_CATID_OPCDAServer20;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("63D5F432-CFE4-11d1-B2C8-0060083BA1FB")
    CATID_OPCDAServer20 : public IUnknown
    {
    public:
    };
    
#else 	/* C style interface */

    typedef struct CATID_OPCDAServer20Vtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            CATID_OPCDAServer20 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            CATID_OPCDAServer20 * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            CATID_OPCDAServer20 * This);
        
        END_INTERFACE
    } CATID_OPCDAServer20Vtbl;

    interface CATID_OPCDAServer20
    {
        CONST_VTBL struct CATID_OPCDAServer20Vtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define CATID_OPCDAServer20_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define CATID_OPCDAServer20_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define CATID_OPCDAServer20_Release(This)	\
    (This)->lpVtbl -> Release(This)


#endif /* COBJMACROS */


#endif 	/* C style interface */




#endif 	/* __CATID_OPCDAServer20_INTERFACE_DEFINED__ */


#ifndef __CATID_OPCDAServer30_INTERFACE_DEFINED__
#define __CATID_OPCDAServer30_INTERFACE_DEFINED__

/* interface CATID_OPCDAServer30 */
/* [object][uuid] */ 


EXTERN_C const IID IID_CATID_OPCDAServer30;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("CC603642-66D7-48f1-B69A-B625E73652D7")
    CATID_OPCDAServer30 : public IUnknown
    {
    public:
    };
    
#else 	/* C style interface */

    typedef struct CATID_OPCDAServer30Vtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            CATID_OPCDAServer30 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            CATID_OPCDAServer30 * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            CATID_OPCDAServer30 * This);
        
        END_INTERFACE
    } CATID_OPCDAServer30Vtbl;

    interface CATID_OPCDAServer30
    {
        CONST_VTBL struct CATID_OPCDAServer30Vtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define CATID_OPCDAServer30_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define CATID_OPCDAServer30_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define CATID_OPCDAServer30_Release(This)	\
    (This)->lpVtbl -> Release(This)


#endif /* COBJMACROS */


#endif 	/* C style interface */




#endif 	/* __CATID_OPCDAServer30_INTERFACE_DEFINED__ */


#ifndef __CATID_XMLDAServer10_INTERFACE_DEFINED__
#define __CATID_XMLDAServer10_INTERFACE_DEFINED__

/* interface CATID_XMLDAServer10 */
/* [object][uuid] */ 


EXTERN_C const IID IID_CATID_XMLDAServer10;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("3098EDA4-A006-48b2-A27F-247453959408")
    CATID_XMLDAServer10 : public IUnknown
    {
    public:
    };
    
#else 	/* C style interface */

    typedef struct CATID_XMLDAServer10Vtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            CATID_XMLDAServer10 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            CATID_XMLDAServer10 * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            CATID_XMLDAServer10 * This);
        
        END_INTERFACE
    } CATID_XMLDAServer10Vtbl;

    interface CATID_XMLDAServer10
    {
        CONST_VTBL struct CATID_XMLDAServer10Vtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define CATID_XMLDAServer10_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define CATID_XMLDAServer10_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define CATID_XMLDAServer10_Release(This)	\
    (This)->lpVtbl -> Release(This)


#endif /* COBJMACROS */


#endif 	/* C style interface */




#endif 	/* __CATID_XMLDAServer10_INTERFACE_DEFINED__ */


/* interface __MIDL_itf_opcda_0266 */
/* [local] */ 

#define CATID_OPCDAServer10 IID_CATID_OPCDAServer10
#define CATID_OPCDAServer20 IID_CATID_OPCDAServer20
#define CATID_OPCDAServer30 IID_CATID_OPCDAServer30
#define CATID_XMLDAServer10 IID_CATID_XMLDAServer10
typedef DWORD OPCHANDLE;

typedef 
enum tagOPCDATASOURCE
    {	OPC_DS_CACHE	= 1,
	OPC_DS_DEVICE	= OPC_DS_CACHE + 1
    } 	OPCDATASOURCE;

typedef 
enum tagOPCBROWSETYPE
    {	OPC_BRANCH	= 1,
	OPC_LEAF	= OPC_BRANCH + 1,
	OPC_FLAT	= OPC_LEAF + 1
    } 	OPCBROWSETYPE;

typedef 
enum tagOPCNAMESPACETYPE
    {	OPC_NS_HIERARCHIAL	= 1,
	OPC_NS_FLAT	= OPC_NS_HIERARCHIAL + 1
    } 	OPCNAMESPACETYPE;

typedef 
enum tagOPCBROWSEDIRECTION
    {	OPC_BROWSE_UP	= 1,
	OPC_BROWSE_DOWN	= OPC_BROWSE_UP + 1,
	OPC_BROWSE_TO	= OPC_BROWSE_DOWN + 1
    } 	OPCBROWSEDIRECTION;

typedef 
enum tagOPCEUTYPE
    {	OPC_NOENUM	= 0,
	OPC_ANALOG	= OPC_NOENUM + 1,
	OPC_ENUMERATED	= OPC_ANALOG + 1
    } 	OPCEUTYPE;

typedef 
enum tagOPCSERVERSTATE
    {	OPC_STATUS_RUNNING	= 1,
	OPC_STATUS_FAILED	= OPC_STATUS_RUNNING + 1,
	OPC_STATUS_NOCONFIG	= OPC_STATUS_FAILED + 1,
	OPC_STATUS_SUSPENDED	= OPC_STATUS_NOCONFIG + 1,
	OPC_STATUS_TEST	= OPC_STATUS_SUSPENDED + 1,
	OPC_STATUS_COMM_FAULT	= OPC_STATUS_TEST + 1
    } 	OPCSERVERSTATE;

typedef 
enum tagOPCENUMSCOPE
    {	OPC_ENUM_PRIVATE_CONNECTIONS	= 1,
	OPC_ENUM_PUBLIC_CONNECTIONS	= OPC_ENUM_PRIVATE_CONNECTIONS + 1,
	OPC_ENUM_ALL_CONNECTIONS	= OPC_ENUM_PUBLIC_CONNECTIONS + 1,
	OPC_ENUM_PRIVATE	= OPC_ENUM_ALL_CONNECTIONS + 1,
	OPC_ENUM_PUBLIC	= OPC_ENUM_PRIVATE + 1,
	OPC_ENUM_ALL	= OPC_ENUM_PUBLIC + 1
    } 	OPCENUMSCOPE;

typedef struct tagOPCGROUPHEADER
    {
    DWORD dwSize;
    DWORD dwItemCount;
    OPCHANDLE hClientGroup;
    DWORD dwTransactionID;
    HRESULT hrStatus;
    } 	OPCGROUPHEADER;

typedef struct tagOPCITEMHEADER1
    {
    OPCHANDLE hClient;
    DWORD dwValueOffset;
    WORD wQuality;
    WORD wReserved;
    FILETIME ftTimeStampItem;
    } 	OPCITEMHEADER1;

typedef struct tagOPCITEMHEADER2
    {
    OPCHANDLE hClient;
    DWORD dwValueOffset;
    WORD wQuality;
    WORD wReserved;
    } 	OPCITEMHEADER2;

typedef struct tagOPCGROUPHEADERWRITE
    {
    DWORD dwItemCount;
    OPCHANDLE hClientGroup;
    DWORD dwTransactionID;
    HRESULT hrStatus;
    } 	OPCGROUPHEADERWRITE;

typedef struct tagOPCITEMHEADERWRITE
    {
    OPCHANDLE hClient;
    HRESULT dwError;
    } 	OPCITEMHEADERWRITE;

typedef struct tagOPCITEMSTATE
    {
    OPCHANDLE hClient;
    FILETIME ftTimeStamp;
    WORD wQuality;
    WORD wReserved;
    VARIANT vDataValue;
    } 	OPCITEMSTATE;

typedef struct tagOPCSERVERSTATUS
    {
    FILETIME ftStartTime;
    FILETIME ftCurrentTime;
    FILETIME ftLastUpdateTime;
    OPCSERVERSTATE dwServerState;
    DWORD dwGroupCount;
    DWORD dwBandWidth;
    WORD wMajorVersion;
    WORD wMinorVersion;
    WORD wBuildNumber;
    WORD wReserved;
    /* [string] */ LPWSTR szVendorInfo;
    } 	OPCSERVERSTATUS;

typedef struct tagOPCITEMDEF
    {
    /* [string] */ LPWSTR szAccessPath;
    /* [string] */ LPWSTR szItemID;
    BOOL bActive;
    OPCHANDLE hClient;
    DWORD dwBlobSize;
    /* [size_is] */ BYTE *pBlob;
    VARTYPE vtRequestedDataType;
    WORD wReserved;
    } 	OPCITEMDEF;

typedef struct tagOPCITEMATTRIBUTES
    {
    /* [string] */ LPWSTR szAccessPath;
    /* [string] */ LPWSTR szItemID;
    BOOL bActive;
    OPCHANDLE hClient;
    OPCHANDLE hServer;
    DWORD dwAccessRights;
    DWORD dwBlobSize;
    /* [size_is] */ BYTE *pBlob;
    VARTYPE vtRequestedDataType;
    VARTYPE vtCanonicalDataType;
    OPCEUTYPE dwEUType;
    VARIANT vEUInfo;
    } 	OPCITEMATTRIBUTES;

typedef struct tagOPCITEMRESULT
    {
    OPCHANDLE hServer;
    VARTYPE vtCanonicalDataType;
    WORD wReserved;
    DWORD dwAccessRights;
    DWORD dwBlobSize;
    /* [size_is] */ BYTE *pBlob;
    } 	OPCITEMRESULT;

typedef struct tagOPCITEMPROPERTY
    {
    VARTYPE vtDataType;
    WORD wReserved;
    DWORD dwPropertyID;
    /* [string] */ LPWSTR szItemID;
    /* [string] */ LPWSTR szDescription;
    VARIANT vValue;
    HRESULT hrErrorID;
    DWORD dwReserved;
    } 	OPCITEMPROPERTY;

typedef struct tagOPCITEMPROPERTIES
    {
    HRESULT hrErrorID;
    DWORD dwNumProperties;
    /* [size_is] */ OPCITEMPROPERTY *pItemProperties;
    DWORD dwReserved;
    } 	OPCITEMPROPERTIES;

typedef struct tagOPCBROWSEELEMENT
    {
    /* [string] */ LPWSTR szName;
    /* [string] */ LPWSTR szItemID;
    DWORD dwFlagValue;
    DWORD dwReserved;
    OPCITEMPROPERTIES ItemProperties;
    } 	OPCBROWSEELEMENT;

typedef struct tagOPCITEMVQT
    {
    VARIANT vDataValue;
    BOOL bQualitySpecified;
    WORD wQuality;
    WORD wReserved;
    BOOL bTimeStampSpecified;
    DWORD dwReserved;
    FILETIME ftTimeStamp;
    } 	OPCITEMVQT;

typedef 
enum tagOPCBROWSEFILTER
    {	OPC_BROWSE_FILTER_ALL	= 1,
	OPC_BROWSE_FILTER_BRANCHES	= OPC_BROWSE_FILTER_ALL + 1,
	OPC_BROWSE_FILTER_ITEMS	= OPC_BROWSE_FILTER_BRANCHES + 1
    } 	OPCBROWSEFILTER;



extern RPC_IF_HANDLE __MIDL_itf_opcda_0266_v0_0_c_ifspec;
extern RPC_IF_HANDLE __MIDL_itf_opcda_0266_v0_0_s_ifspec;

#ifndef __IOPCServer_INTERFACE_DEFINED__
#define __IOPCServer_INTERFACE_DEFINED__

/* interface IOPCServer */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCServer;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("39c13a4d-011e-11d0-9675-0020afd8adb3")
    IOPCServer : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE AddGroup( 
            /* [string][in] */ LPCWSTR szName,
            /* [in] */ BOOL bActive,
            /* [in] */ DWORD dwRequestedUpdateRate,
            /* [in] */ OPCHANDLE hClientGroup,
            /* [in][unique] */ LONG *pTimeBias,
            /* [in][unique] */ FLOAT *pPercentDeadband,
            /* [in] */ DWORD dwLCID,
            /* [out] */ OPCHANDLE *phServerGroup,
            /* [out] */ DWORD *pRevisedUpdateRate,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetErrorString( 
            /* [in] */ HRESULT dwError,
            /* [in] */ LCID dwLocale,
            /* [string][out] */ LPWSTR *ppString) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetGroupByName( 
            /* [string][in] */ LPCWSTR szName,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetStatus( 
            /* [out] */ OPCSERVERSTATUS **ppServerStatus) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE RemoveGroup( 
            /* [in] */ OPCHANDLE hServerGroup,
            /* [in] */ BOOL bForce) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE CreateGroupEnumerator( 
            /* [in] */ OPCENUMSCOPE dwScope,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCServerVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCServer * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCServer * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCServer * This);
        
        HRESULT ( STDMETHODCALLTYPE *AddGroup )( 
            IOPCServer * This,
            /* [string][in] */ LPCWSTR szName,
            /* [in] */ BOOL bActive,
            /* [in] */ DWORD dwRequestedUpdateRate,
            /* [in] */ OPCHANDLE hClientGroup,
            /* [in][unique] */ LONG *pTimeBias,
            /* [in][unique] */ FLOAT *pPercentDeadband,
            /* [in] */ DWORD dwLCID,
            /* [out] */ OPCHANDLE *phServerGroup,
            /* [out] */ DWORD *pRevisedUpdateRate,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk);
        
        HRESULT ( STDMETHODCALLTYPE *GetErrorString )( 
            IOPCServer * This,
            /* [in] */ HRESULT dwError,
            /* [in] */ LCID dwLocale,
            /* [string][out] */ LPWSTR *ppString);
        
        HRESULT ( STDMETHODCALLTYPE *GetGroupByName )( 
            IOPCServer * This,
            /* [string][in] */ LPCWSTR szName,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk);
        
        HRESULT ( STDMETHODCALLTYPE *GetStatus )( 
            IOPCServer * This,
            /* [out] */ OPCSERVERSTATUS **ppServerStatus);
        
        HRESULT ( STDMETHODCALLTYPE *RemoveGroup )( 
            IOPCServer * This,
            /* [in] */ OPCHANDLE hServerGroup,
            /* [in] */ BOOL bForce);
        
        HRESULT ( STDMETHODCALLTYPE *CreateGroupEnumerator )( 
            IOPCServer * This,
            /* [in] */ OPCENUMSCOPE dwScope,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk);
        
        END_INTERFACE
    } IOPCServerVtbl;

    interface IOPCServer
    {
        CONST_VTBL struct IOPCServerVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCServer_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCServer_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCServer_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCServer_AddGroup(This,szName,bActive,dwRequestedUpdateRate,hClientGroup,pTimeBias,pPercentDeadband,dwLCID,phServerGroup,pRevisedUpdateRate,riid,ppUnk)	\
    (This)->lpVtbl -> AddGroup(This,szName,bActive,dwRequestedUpdateRate,hClientGroup,pTimeBias,pPercentDeadband,dwLCID,phServerGroup,pRevisedUpdateRate,riid,ppUnk)

#define IOPCServer_GetErrorString(This,dwError,dwLocale,ppString)	\
    (This)->lpVtbl -> GetErrorString(This,dwError,dwLocale,ppString)

#define IOPCServer_GetGroupByName(This,szName,riid,ppUnk)	\
    (This)->lpVtbl -> GetGroupByName(This,szName,riid,ppUnk)

#define IOPCServer_GetStatus(This,ppServerStatus)	\
    (This)->lpVtbl -> GetStatus(This,ppServerStatus)

#define IOPCServer_RemoveGroup(This,hServerGroup,bForce)	\
    (This)->lpVtbl -> RemoveGroup(This,hServerGroup,bForce)

#define IOPCServer_CreateGroupEnumerator(This,dwScope,riid,ppUnk)	\
    (This)->lpVtbl -> CreateGroupEnumerator(This,dwScope,riid,ppUnk)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCServer_AddGroup_Proxy( 
    IOPCServer * This,
    /* [string][in] */ LPCWSTR szName,
    /* [in] */ BOOL bActive,
    /* [in] */ DWORD dwRequestedUpdateRate,
    /* [in] */ OPCHANDLE hClientGroup,
    /* [in][unique] */ LONG *pTimeBias,
    /* [in][unique] */ FLOAT *pPercentDeadband,
    /* [in] */ DWORD dwLCID,
    /* [out] */ OPCHANDLE *phServerGroup,
    /* [out] */ DWORD *pRevisedUpdateRate,
    /* [in] */ REFIID riid,
    /* [iid_is][out] */ LPUNKNOWN *ppUnk);


void __RPC_STUB IOPCServer_AddGroup_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCServer_GetErrorString_Proxy( 
    IOPCServer * This,
    /* [in] */ HRESULT dwError,
    /* [in] */ LCID dwLocale,
    /* [string][out] */ LPWSTR *ppString);


void __RPC_STUB IOPCServer_GetErrorString_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCServer_GetGroupByName_Proxy( 
    IOPCServer * This,
    /* [string][in] */ LPCWSTR szName,
    /* [in] */ REFIID riid,
    /* [iid_is][out] */ LPUNKNOWN *ppUnk);


void __RPC_STUB IOPCServer_GetGroupByName_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCServer_GetStatus_Proxy( 
    IOPCServer * This,
    /* [out] */ OPCSERVERSTATUS **ppServerStatus);


void __RPC_STUB IOPCServer_GetStatus_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCServer_RemoveGroup_Proxy( 
    IOPCServer * This,
    /* [in] */ OPCHANDLE hServerGroup,
    /* [in] */ BOOL bForce);


void __RPC_STUB IOPCServer_RemoveGroup_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCServer_CreateGroupEnumerator_Proxy( 
    IOPCServer * This,
    /* [in] */ OPCENUMSCOPE dwScope,
    /* [in] */ REFIID riid,
    /* [iid_is][out] */ LPUNKNOWN *ppUnk);


void __RPC_STUB IOPCServer_CreateGroupEnumerator_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCServer_INTERFACE_DEFINED__ */


#ifndef __IOPCServerPublicGroups_INTERFACE_DEFINED__
#define __IOPCServerPublicGroups_INTERFACE_DEFINED__

/* interface IOPCServerPublicGroups */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCServerPublicGroups;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("39c13a4e-011e-11d0-9675-0020afd8adb3")
    IOPCServerPublicGroups : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE GetPublicGroupByName( 
            /* [string][in] */ LPCWSTR szName,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE RemovePublicGroup( 
            /* [in] */ OPCHANDLE hServerGroup,
            /* [in] */ BOOL bForce) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCServerPublicGroupsVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCServerPublicGroups * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCServerPublicGroups * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCServerPublicGroups * This);
        
        HRESULT ( STDMETHODCALLTYPE *GetPublicGroupByName )( 
            IOPCServerPublicGroups * This,
            /* [string][in] */ LPCWSTR szName,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk);
        
        HRESULT ( STDMETHODCALLTYPE *RemovePublicGroup )( 
            IOPCServerPublicGroups * This,
            /* [in] */ OPCHANDLE hServerGroup,
            /* [in] */ BOOL bForce);
        
        END_INTERFACE
    } IOPCServerPublicGroupsVtbl;

    interface IOPCServerPublicGroups
    {
        CONST_VTBL struct IOPCServerPublicGroupsVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCServerPublicGroups_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCServerPublicGroups_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCServerPublicGroups_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCServerPublicGroups_GetPublicGroupByName(This,szName,riid,ppUnk)	\
    (This)->lpVtbl -> GetPublicGroupByName(This,szName,riid,ppUnk)

#define IOPCServerPublicGroups_RemovePublicGroup(This,hServerGroup,bForce)	\
    (This)->lpVtbl -> RemovePublicGroup(This,hServerGroup,bForce)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCServerPublicGroups_GetPublicGroupByName_Proxy( 
    IOPCServerPublicGroups * This,
    /* [string][in] */ LPCWSTR szName,
    /* [in] */ REFIID riid,
    /* [iid_is][out] */ LPUNKNOWN *ppUnk);


void __RPC_STUB IOPCServerPublicGroups_GetPublicGroupByName_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCServerPublicGroups_RemovePublicGroup_Proxy( 
    IOPCServerPublicGroups * This,
    /* [in] */ OPCHANDLE hServerGroup,
    /* [in] */ BOOL bForce);


void __RPC_STUB IOPCServerPublicGroups_RemovePublicGroup_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCServerPublicGroups_INTERFACE_DEFINED__ */


#ifndef __IOPCBrowseServerAddressSpace_INTERFACE_DEFINED__
#define __IOPCBrowseServerAddressSpace_INTERFACE_DEFINED__

/* interface IOPCBrowseServerAddressSpace */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCBrowseServerAddressSpace;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("39c13a4f-011e-11d0-9675-0020afd8adb3")
    IOPCBrowseServerAddressSpace : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE QueryOrganization( 
            /* [out] */ OPCNAMESPACETYPE *pNameSpaceType) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ChangeBrowsePosition( 
            /* [in] */ OPCBROWSEDIRECTION dwBrowseDirection,
            /* [string][in] */ LPCWSTR szString) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE BrowseOPCItemIDs( 
            /* [in] */ OPCBROWSETYPE dwBrowseFilterType,
            /* [string][in] */ LPCWSTR szFilterCriteria,
            /* [in] */ VARTYPE vtDataTypeFilter,
            /* [in] */ DWORD dwAccessRightsFilter,
            /* [out] */ LPENUMSTRING *ppIEnumString) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetItemID( 
            /* [in] */ LPWSTR szItemDataID,
            /* [string][out] */ LPWSTR *szItemID) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE BrowseAccessPaths( 
            /* [string][in] */ LPCWSTR szItemID,
            /* [out] */ LPENUMSTRING *ppIEnumString) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCBrowseServerAddressSpaceVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCBrowseServerAddressSpace * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCBrowseServerAddressSpace * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCBrowseServerAddressSpace * This);
        
        HRESULT ( STDMETHODCALLTYPE *QueryOrganization )( 
            IOPCBrowseServerAddressSpace * This,
            /* [out] */ OPCNAMESPACETYPE *pNameSpaceType);
        
        HRESULT ( STDMETHODCALLTYPE *ChangeBrowsePosition )( 
            IOPCBrowseServerAddressSpace * This,
            /* [in] */ OPCBROWSEDIRECTION dwBrowseDirection,
            /* [string][in] */ LPCWSTR szString);
        
        HRESULT ( STDMETHODCALLTYPE *BrowseOPCItemIDs )( 
            IOPCBrowseServerAddressSpace * This,
            /* [in] */ OPCBROWSETYPE dwBrowseFilterType,
            /* [string][in] */ LPCWSTR szFilterCriteria,
            /* [in] */ VARTYPE vtDataTypeFilter,
            /* [in] */ DWORD dwAccessRightsFilter,
            /* [out] */ LPENUMSTRING *ppIEnumString);
        
        HRESULT ( STDMETHODCALLTYPE *GetItemID )( 
            IOPCBrowseServerAddressSpace * This,
            /* [in] */ LPWSTR szItemDataID,
            /* [string][out] */ LPWSTR *szItemID);
        
        HRESULT ( STDMETHODCALLTYPE *BrowseAccessPaths )( 
            IOPCBrowseServerAddressSpace * This,
            /* [string][in] */ LPCWSTR szItemID,
            /* [out] */ LPENUMSTRING *ppIEnumString);
        
        END_INTERFACE
    } IOPCBrowseServerAddressSpaceVtbl;

    interface IOPCBrowseServerAddressSpace
    {
        CONST_VTBL struct IOPCBrowseServerAddressSpaceVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCBrowseServerAddressSpace_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCBrowseServerAddressSpace_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCBrowseServerAddressSpace_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCBrowseServerAddressSpace_QueryOrganization(This,pNameSpaceType)	\
    (This)->lpVtbl -> QueryOrganization(This,pNameSpaceType)

#define IOPCBrowseServerAddressSpace_ChangeBrowsePosition(This,dwBrowseDirection,szString)	\
    (This)->lpVtbl -> ChangeBrowsePosition(This,dwBrowseDirection,szString)

#define IOPCBrowseServerAddressSpace_BrowseOPCItemIDs(This,dwBrowseFilterType,szFilterCriteria,vtDataTypeFilter,dwAccessRightsFilter,ppIEnumString)	\
    (This)->lpVtbl -> BrowseOPCItemIDs(This,dwBrowseFilterType,szFilterCriteria,vtDataTypeFilter,dwAccessRightsFilter,ppIEnumString)

#define IOPCBrowseServerAddressSpace_GetItemID(This,szItemDataID,szItemID)	\
    (This)->lpVtbl -> GetItemID(This,szItemDataID,szItemID)

#define IOPCBrowseServerAddressSpace_BrowseAccessPaths(This,szItemID,ppIEnumString)	\
    (This)->lpVtbl -> BrowseAccessPaths(This,szItemID,ppIEnumString)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCBrowseServerAddressSpace_QueryOrganization_Proxy( 
    IOPCBrowseServerAddressSpace * This,
    /* [out] */ OPCNAMESPACETYPE *pNameSpaceType);


void __RPC_STUB IOPCBrowseServerAddressSpace_QueryOrganization_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCBrowseServerAddressSpace_ChangeBrowsePosition_Proxy( 
    IOPCBrowseServerAddressSpace * This,
    /* [in] */ OPCBROWSEDIRECTION dwBrowseDirection,
    /* [string][in] */ LPCWSTR szString);


void __RPC_STUB IOPCBrowseServerAddressSpace_ChangeBrowsePosition_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCBrowseServerAddressSpace_BrowseOPCItemIDs_Proxy( 
    IOPCBrowseServerAddressSpace * This,
    /* [in] */ OPCBROWSETYPE dwBrowseFilterType,
    /* [string][in] */ LPCWSTR szFilterCriteria,
    /* [in] */ VARTYPE vtDataTypeFilter,
    /* [in] */ DWORD dwAccessRightsFilter,
    /* [out] */ LPENUMSTRING *ppIEnumString);


void __RPC_STUB IOPCBrowseServerAddressSpace_BrowseOPCItemIDs_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCBrowseServerAddressSpace_GetItemID_Proxy( 
    IOPCBrowseServerAddressSpace * This,
    /* [in] */ LPWSTR szItemDataID,
    /* [string][out] */ LPWSTR *szItemID);


void __RPC_STUB IOPCBrowseServerAddressSpace_GetItemID_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCBrowseServerAddressSpace_BrowseAccessPaths_Proxy( 
    IOPCBrowseServerAddressSpace * This,
    /* [string][in] */ LPCWSTR szItemID,
    /* [out] */ LPENUMSTRING *ppIEnumString);


void __RPC_STUB IOPCBrowseServerAddressSpace_BrowseAccessPaths_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCBrowseServerAddressSpace_INTERFACE_DEFINED__ */


#ifndef __IOPCGroupStateMgt_INTERFACE_DEFINED__
#define __IOPCGroupStateMgt_INTERFACE_DEFINED__

/* interface IOPCGroupStateMgt */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCGroupStateMgt;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("39c13a50-011e-11d0-9675-0020afd8adb3")
    IOPCGroupStateMgt : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE GetState( 
            /* [out] */ DWORD *pUpdateRate,
            /* [out] */ BOOL *pActive,
            /* [string][out] */ LPWSTR *ppName,
            /* [out] */ LONG *pTimeBias,
            /* [out] */ FLOAT *pPercentDeadband,
            /* [out] */ DWORD *pLCID,
            /* [out] */ OPCHANDLE *phClientGroup,
            /* [out] */ OPCHANDLE *phServerGroup) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE SetState( 
            /* [in][unique] */ DWORD *pRequestedUpdateRate,
            /* [out] */ DWORD *pRevisedUpdateRate,
            /* [in][unique] */ BOOL *pActive,
            /* [in][unique] */ LONG *pTimeBias,
            /* [in][unique] */ FLOAT *pPercentDeadband,
            /* [in][unique] */ DWORD *pLCID,
            /* [in][unique] */ OPCHANDLE *phClientGroup) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE SetName( 
            /* [string][in] */ LPCWSTR szName) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE CloneGroup( 
            /* [string][in] */ LPCWSTR szName,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCGroupStateMgtVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCGroupStateMgt * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCGroupStateMgt * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCGroupStateMgt * This);
        
        HRESULT ( STDMETHODCALLTYPE *GetState )( 
            IOPCGroupStateMgt * This,
            /* [out] */ DWORD *pUpdateRate,
            /* [out] */ BOOL *pActive,
            /* [string][out] */ LPWSTR *ppName,
            /* [out] */ LONG *pTimeBias,
            /* [out] */ FLOAT *pPercentDeadband,
            /* [out] */ DWORD *pLCID,
            /* [out] */ OPCHANDLE *phClientGroup,
            /* [out] */ OPCHANDLE *phServerGroup);
        
        HRESULT ( STDMETHODCALLTYPE *SetState )( 
            IOPCGroupStateMgt * This,
            /* [in][unique] */ DWORD *pRequestedUpdateRate,
            /* [out] */ DWORD *pRevisedUpdateRate,
            /* [in][unique] */ BOOL *pActive,
            /* [in][unique] */ LONG *pTimeBias,
            /* [in][unique] */ FLOAT *pPercentDeadband,
            /* [in][unique] */ DWORD *pLCID,
            /* [in][unique] */ OPCHANDLE *phClientGroup);
        
        HRESULT ( STDMETHODCALLTYPE *SetName )( 
            IOPCGroupStateMgt * This,
            /* [string][in] */ LPCWSTR szName);
        
        HRESULT ( STDMETHODCALLTYPE *CloneGroup )( 
            IOPCGroupStateMgt * This,
            /* [string][in] */ LPCWSTR szName,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk);
        
        END_INTERFACE
    } IOPCGroupStateMgtVtbl;

    interface IOPCGroupStateMgt
    {
        CONST_VTBL struct IOPCGroupStateMgtVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCGroupStateMgt_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCGroupStateMgt_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCGroupStateMgt_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCGroupStateMgt_GetState(This,pUpdateRate,pActive,ppName,pTimeBias,pPercentDeadband,pLCID,phClientGroup,phServerGroup)	\
    (This)->lpVtbl -> GetState(This,pUpdateRate,pActive,ppName,pTimeBias,pPercentDeadband,pLCID,phClientGroup,phServerGroup)

#define IOPCGroupStateMgt_SetState(This,pRequestedUpdateRate,pRevisedUpdateRate,pActive,pTimeBias,pPercentDeadband,pLCID,phClientGroup)	\
    (This)->lpVtbl -> SetState(This,pRequestedUpdateRate,pRevisedUpdateRate,pActive,pTimeBias,pPercentDeadband,pLCID,phClientGroup)

#define IOPCGroupStateMgt_SetName(This,szName)	\
    (This)->lpVtbl -> SetName(This,szName)

#define IOPCGroupStateMgt_CloneGroup(This,szName,riid,ppUnk)	\
    (This)->lpVtbl -> CloneGroup(This,szName,riid,ppUnk)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCGroupStateMgt_GetState_Proxy( 
    IOPCGroupStateMgt * This,
    /* [out] */ DWORD *pUpdateRate,
    /* [out] */ BOOL *pActive,
    /* [string][out] */ LPWSTR *ppName,
    /* [out] */ LONG *pTimeBias,
    /* [out] */ FLOAT *pPercentDeadband,
    /* [out] */ DWORD *pLCID,
    /* [out] */ OPCHANDLE *phClientGroup,
    /* [out] */ OPCHANDLE *phServerGroup);


void __RPC_STUB IOPCGroupStateMgt_GetState_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCGroupStateMgt_SetState_Proxy( 
    IOPCGroupStateMgt * This,
    /* [in][unique] */ DWORD *pRequestedUpdateRate,
    /* [out] */ DWORD *pRevisedUpdateRate,
    /* [in][unique] */ BOOL *pActive,
    /* [in][unique] */ LONG *pTimeBias,
    /* [in][unique] */ FLOAT *pPercentDeadband,
    /* [in][unique] */ DWORD *pLCID,
    /* [in][unique] */ OPCHANDLE *phClientGroup);


void __RPC_STUB IOPCGroupStateMgt_SetState_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCGroupStateMgt_SetName_Proxy( 
    IOPCGroupStateMgt * This,
    /* [string][in] */ LPCWSTR szName);


void __RPC_STUB IOPCGroupStateMgt_SetName_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCGroupStateMgt_CloneGroup_Proxy( 
    IOPCGroupStateMgt * This,
    /* [string][in] */ LPCWSTR szName,
    /* [in] */ REFIID riid,
    /* [iid_is][out] */ LPUNKNOWN *ppUnk);


void __RPC_STUB IOPCGroupStateMgt_CloneGroup_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCGroupStateMgt_INTERFACE_DEFINED__ */


#ifndef __IOPCPublicGroupStateMgt_INTERFACE_DEFINED__
#define __IOPCPublicGroupStateMgt_INTERFACE_DEFINED__

/* interface IOPCPublicGroupStateMgt */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCPublicGroupStateMgt;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("39c13a51-011e-11d0-9675-0020afd8adb3")
    IOPCPublicGroupStateMgt : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE GetState( 
            /* [out] */ BOOL *pPublic) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE MoveToPublic( void) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCPublicGroupStateMgtVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCPublicGroupStateMgt * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCPublicGroupStateMgt * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCPublicGroupStateMgt * This);
        
        HRESULT ( STDMETHODCALLTYPE *GetState )( 
            IOPCPublicGroupStateMgt * This,
            /* [out] */ BOOL *pPublic);
        
        HRESULT ( STDMETHODCALLTYPE *MoveToPublic )( 
            IOPCPublicGroupStateMgt * This);
        
        END_INTERFACE
    } IOPCPublicGroupStateMgtVtbl;

    interface IOPCPublicGroupStateMgt
    {
        CONST_VTBL struct IOPCPublicGroupStateMgtVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCPublicGroupStateMgt_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCPublicGroupStateMgt_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCPublicGroupStateMgt_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCPublicGroupStateMgt_GetState(This,pPublic)	\
    (This)->lpVtbl -> GetState(This,pPublic)

#define IOPCPublicGroupStateMgt_MoveToPublic(This)	\
    (This)->lpVtbl -> MoveToPublic(This)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCPublicGroupStateMgt_GetState_Proxy( 
    IOPCPublicGroupStateMgt * This,
    /* [out] */ BOOL *pPublic);


void __RPC_STUB IOPCPublicGroupStateMgt_GetState_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCPublicGroupStateMgt_MoveToPublic_Proxy( 
    IOPCPublicGroupStateMgt * This);


void __RPC_STUB IOPCPublicGroupStateMgt_MoveToPublic_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCPublicGroupStateMgt_INTERFACE_DEFINED__ */


#ifndef __IOPCSyncIO_INTERFACE_DEFINED__
#define __IOPCSyncIO_INTERFACE_DEFINED__

/* interface IOPCSyncIO */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCSyncIO;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("39c13a52-011e-11d0-9675-0020afd8adb3")
    IOPCSyncIO : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE Read( 
            /* [in] */ OPCDATASOURCE dwSource,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ OPCITEMSTATE **ppItemValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Write( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ VARIANT *pItemValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCSyncIOVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCSyncIO * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCSyncIO * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCSyncIO * This);
        
        HRESULT ( STDMETHODCALLTYPE *Read )( 
            IOPCSyncIO * This,
            /* [in] */ OPCDATASOURCE dwSource,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ OPCITEMSTATE **ppItemValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *Write )( 
            IOPCSyncIO * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ VARIANT *pItemValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        END_INTERFACE
    } IOPCSyncIOVtbl;

    interface IOPCSyncIO
    {
        CONST_VTBL struct IOPCSyncIOVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCSyncIO_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCSyncIO_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCSyncIO_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCSyncIO_Read(This,dwSource,dwCount,phServer,ppItemValues,ppErrors)	\
    (This)->lpVtbl -> Read(This,dwSource,dwCount,phServer,ppItemValues,ppErrors)

#define IOPCSyncIO_Write(This,dwCount,phServer,pItemValues,ppErrors)	\
    (This)->lpVtbl -> Write(This,dwCount,phServer,pItemValues,ppErrors)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCSyncIO_Read_Proxy( 
    IOPCSyncIO * This,
    /* [in] */ OPCDATASOURCE dwSource,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][size_is][out] */ OPCITEMSTATE **ppItemValues,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCSyncIO_Read_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCSyncIO_Write_Proxy( 
    IOPCSyncIO * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ VARIANT *pItemValues,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCSyncIO_Write_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCSyncIO_INTERFACE_DEFINED__ */


#ifndef __IOPCAsyncIO_INTERFACE_DEFINED__
#define __IOPCAsyncIO_INTERFACE_DEFINED__

/* interface IOPCAsyncIO */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCAsyncIO;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("39c13a53-011e-11d0-9675-0020afd8adb3")
    IOPCAsyncIO : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE Read( 
            /* [in] */ DWORD dwConnection,
            /* [in] */ OPCDATASOURCE dwSource,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [out] */ DWORD *pTransactionID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Write( 
            /* [in] */ DWORD dwConnection,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ VARIANT *pItemValues,
            /* [out] */ DWORD *pTransactionID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Refresh( 
            /* [in] */ DWORD dwConnection,
            /* [in] */ OPCDATASOURCE dwSource,
            /* [out] */ DWORD *pTransactionID) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Cancel( 
            /* [in] */ DWORD dwTransactionID) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCAsyncIOVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCAsyncIO * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCAsyncIO * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCAsyncIO * This);
        
        HRESULT ( STDMETHODCALLTYPE *Read )( 
            IOPCAsyncIO * This,
            /* [in] */ DWORD dwConnection,
            /* [in] */ OPCDATASOURCE dwSource,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [out] */ DWORD *pTransactionID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *Write )( 
            IOPCAsyncIO * This,
            /* [in] */ DWORD dwConnection,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ VARIANT *pItemValues,
            /* [out] */ DWORD *pTransactionID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *Refresh )( 
            IOPCAsyncIO * This,
            /* [in] */ DWORD dwConnection,
            /* [in] */ OPCDATASOURCE dwSource,
            /* [out] */ DWORD *pTransactionID);
        
        HRESULT ( STDMETHODCALLTYPE *Cancel )( 
            IOPCAsyncIO * This,
            /* [in] */ DWORD dwTransactionID);
        
        END_INTERFACE
    } IOPCAsyncIOVtbl;

    interface IOPCAsyncIO
    {
        CONST_VTBL struct IOPCAsyncIOVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCAsyncIO_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCAsyncIO_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCAsyncIO_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCAsyncIO_Read(This,dwConnection,dwSource,dwCount,phServer,pTransactionID,ppErrors)	\
    (This)->lpVtbl -> Read(This,dwConnection,dwSource,dwCount,phServer,pTransactionID,ppErrors)

#define IOPCAsyncIO_Write(This,dwConnection,dwCount,phServer,pItemValues,pTransactionID,ppErrors)	\
    (This)->lpVtbl -> Write(This,dwConnection,dwCount,phServer,pItemValues,pTransactionID,ppErrors)

#define IOPCAsyncIO_Refresh(This,dwConnection,dwSource,pTransactionID)	\
    (This)->lpVtbl -> Refresh(This,dwConnection,dwSource,pTransactionID)

#define IOPCAsyncIO_Cancel(This,dwTransactionID)	\
    (This)->lpVtbl -> Cancel(This,dwTransactionID)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCAsyncIO_Read_Proxy( 
    IOPCAsyncIO * This,
    /* [in] */ DWORD dwConnection,
    /* [in] */ OPCDATASOURCE dwSource,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [out] */ DWORD *pTransactionID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCAsyncIO_Read_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCAsyncIO_Write_Proxy( 
    IOPCAsyncIO * This,
    /* [in] */ DWORD dwConnection,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ VARIANT *pItemValues,
    /* [out] */ DWORD *pTransactionID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCAsyncIO_Write_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCAsyncIO_Refresh_Proxy( 
    IOPCAsyncIO * This,
    /* [in] */ DWORD dwConnection,
    /* [in] */ OPCDATASOURCE dwSource,
    /* [out] */ DWORD *pTransactionID);


void __RPC_STUB IOPCAsyncIO_Refresh_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCAsyncIO_Cancel_Proxy( 
    IOPCAsyncIO * This,
    /* [in] */ DWORD dwTransactionID);


void __RPC_STUB IOPCAsyncIO_Cancel_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCAsyncIO_INTERFACE_DEFINED__ */


#ifndef __IOPCItemMgt_INTERFACE_DEFINED__
#define __IOPCItemMgt_INTERFACE_DEFINED__

/* interface IOPCItemMgt */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCItemMgt;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("39c13a54-011e-11d0-9675-0020afd8adb3")
    IOPCItemMgt : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE AddItems( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCITEMDEF *pItemArray,
            /* [size_is][size_is][out] */ OPCITEMRESULT **ppAddResults,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ValidateItems( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCITEMDEF *pItemArray,
            /* [in] */ BOOL bBlobUpdate,
            /* [size_is][size_is][out] */ OPCITEMRESULT **ppValidationResults,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE RemoveItems( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE SetActiveState( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [in] */ BOOL bActive,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE SetClientHandles( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ OPCHANDLE *phClient,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE SetDatatypes( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ VARTYPE *pRequestedDatatypes,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE CreateEnumerator( 
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCItemMgtVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCItemMgt * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCItemMgt * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCItemMgt * This);
        
        HRESULT ( STDMETHODCALLTYPE *AddItems )( 
            IOPCItemMgt * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCITEMDEF *pItemArray,
            /* [size_is][size_is][out] */ OPCITEMRESULT **ppAddResults,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *ValidateItems )( 
            IOPCItemMgt * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCITEMDEF *pItemArray,
            /* [in] */ BOOL bBlobUpdate,
            /* [size_is][size_is][out] */ OPCITEMRESULT **ppValidationResults,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *RemoveItems )( 
            IOPCItemMgt * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *SetActiveState )( 
            IOPCItemMgt * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [in] */ BOOL bActive,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *SetClientHandles )( 
            IOPCItemMgt * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ OPCHANDLE *phClient,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *SetDatatypes )( 
            IOPCItemMgt * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ VARTYPE *pRequestedDatatypes,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *CreateEnumerator )( 
            IOPCItemMgt * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk);
        
        END_INTERFACE
    } IOPCItemMgtVtbl;

    interface IOPCItemMgt
    {
        CONST_VTBL struct IOPCItemMgtVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCItemMgt_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCItemMgt_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCItemMgt_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCItemMgt_AddItems(This,dwCount,pItemArray,ppAddResults,ppErrors)	\
    (This)->lpVtbl -> AddItems(This,dwCount,pItemArray,ppAddResults,ppErrors)

#define IOPCItemMgt_ValidateItems(This,dwCount,pItemArray,bBlobUpdate,ppValidationResults,ppErrors)	\
    (This)->lpVtbl -> ValidateItems(This,dwCount,pItemArray,bBlobUpdate,ppValidationResults,ppErrors)

#define IOPCItemMgt_RemoveItems(This,dwCount,phServer,ppErrors)	\
    (This)->lpVtbl -> RemoveItems(This,dwCount,phServer,ppErrors)

#define IOPCItemMgt_SetActiveState(This,dwCount,phServer,bActive,ppErrors)	\
    (This)->lpVtbl -> SetActiveState(This,dwCount,phServer,bActive,ppErrors)

#define IOPCItemMgt_SetClientHandles(This,dwCount,phServer,phClient,ppErrors)	\
    (This)->lpVtbl -> SetClientHandles(This,dwCount,phServer,phClient,ppErrors)

#define IOPCItemMgt_SetDatatypes(This,dwCount,phServer,pRequestedDatatypes,ppErrors)	\
    (This)->lpVtbl -> SetDatatypes(This,dwCount,phServer,pRequestedDatatypes,ppErrors)

#define IOPCItemMgt_CreateEnumerator(This,riid,ppUnk)	\
    (This)->lpVtbl -> CreateEnumerator(This,riid,ppUnk)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCItemMgt_AddItems_Proxy( 
    IOPCItemMgt * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCITEMDEF *pItemArray,
    /* [size_is][size_is][out] */ OPCITEMRESULT **ppAddResults,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemMgt_AddItems_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCItemMgt_ValidateItems_Proxy( 
    IOPCItemMgt * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCITEMDEF *pItemArray,
    /* [in] */ BOOL bBlobUpdate,
    /* [size_is][size_is][out] */ OPCITEMRESULT **ppValidationResults,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemMgt_ValidateItems_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCItemMgt_RemoveItems_Proxy( 
    IOPCItemMgt * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemMgt_RemoveItems_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCItemMgt_SetActiveState_Proxy( 
    IOPCItemMgt * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [in] */ BOOL bActive,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemMgt_SetActiveState_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCItemMgt_SetClientHandles_Proxy( 
    IOPCItemMgt * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ OPCHANDLE *phClient,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemMgt_SetClientHandles_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCItemMgt_SetDatatypes_Proxy( 
    IOPCItemMgt * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ VARTYPE *pRequestedDatatypes,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemMgt_SetDatatypes_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCItemMgt_CreateEnumerator_Proxy( 
    IOPCItemMgt * This,
    /* [in] */ REFIID riid,
    /* [iid_is][out] */ LPUNKNOWN *ppUnk);


void __RPC_STUB IOPCItemMgt_CreateEnumerator_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCItemMgt_INTERFACE_DEFINED__ */


#ifndef __IEnumOPCItemAttributes_INTERFACE_DEFINED__
#define __IEnumOPCItemAttributes_INTERFACE_DEFINED__

/* interface IEnumOPCItemAttributes */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IEnumOPCItemAttributes;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("39c13a55-011e-11d0-9675-0020afd8adb3")
    IEnumOPCItemAttributes : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE Next( 
            /* [in] */ ULONG celt,
            /* [size_is][size_is][out] */ OPCITEMATTRIBUTES **ppItemArray,
            /* [out] */ ULONG *pceltFetched) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Skip( 
            /* [in] */ ULONG celt) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Reset( void) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Clone( 
            /* [out] */ IEnumOPCItemAttributes **ppEnumItemAttributes) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IEnumOPCItemAttributesVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IEnumOPCItemAttributes * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IEnumOPCItemAttributes * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IEnumOPCItemAttributes * This);
        
        HRESULT ( STDMETHODCALLTYPE *Next )( 
            IEnumOPCItemAttributes * This,
            /* [in] */ ULONG celt,
            /* [size_is][size_is][out] */ OPCITEMATTRIBUTES **ppItemArray,
            /* [out] */ ULONG *pceltFetched);
        
        HRESULT ( STDMETHODCALLTYPE *Skip )( 
            IEnumOPCItemAttributes * This,
            /* [in] */ ULONG celt);
        
        HRESULT ( STDMETHODCALLTYPE *Reset )( 
            IEnumOPCItemAttributes * This);
        
        HRESULT ( STDMETHODCALLTYPE *Clone )( 
            IEnumOPCItemAttributes * This,
            /* [out] */ IEnumOPCItemAttributes **ppEnumItemAttributes);
        
        END_INTERFACE
    } IEnumOPCItemAttributesVtbl;

    interface IEnumOPCItemAttributes
    {
        CONST_VTBL struct IEnumOPCItemAttributesVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IEnumOPCItemAttributes_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IEnumOPCItemAttributes_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IEnumOPCItemAttributes_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IEnumOPCItemAttributes_Next(This,celt,ppItemArray,pceltFetched)	\
    (This)->lpVtbl -> Next(This,celt,ppItemArray,pceltFetched)

#define IEnumOPCItemAttributes_Skip(This,celt)	\
    (This)->lpVtbl -> Skip(This,celt)

#define IEnumOPCItemAttributes_Reset(This)	\
    (This)->lpVtbl -> Reset(This)

#define IEnumOPCItemAttributes_Clone(This,ppEnumItemAttributes)	\
    (This)->lpVtbl -> Clone(This,ppEnumItemAttributes)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IEnumOPCItemAttributes_Next_Proxy( 
    IEnumOPCItemAttributes * This,
    /* [in] */ ULONG celt,
    /* [size_is][size_is][out] */ OPCITEMATTRIBUTES **ppItemArray,
    /* [out] */ ULONG *pceltFetched);


void __RPC_STUB IEnumOPCItemAttributes_Next_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IEnumOPCItemAttributes_Skip_Proxy( 
    IEnumOPCItemAttributes * This,
    /* [in] */ ULONG celt);


void __RPC_STUB IEnumOPCItemAttributes_Skip_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IEnumOPCItemAttributes_Reset_Proxy( 
    IEnumOPCItemAttributes * This);


void __RPC_STUB IEnumOPCItemAttributes_Reset_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IEnumOPCItemAttributes_Clone_Proxy( 
    IEnumOPCItemAttributes * This,
    /* [out] */ IEnumOPCItemAttributes **ppEnumItemAttributes);


void __RPC_STUB IEnumOPCItemAttributes_Clone_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IEnumOPCItemAttributes_INTERFACE_DEFINED__ */


#ifndef __IOPCDataCallback_INTERFACE_DEFINED__
#define __IOPCDataCallback_INTERFACE_DEFINED__

/* interface IOPCDataCallback */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCDataCallback;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("39c13a70-011e-11d0-9675-0020afd8adb3")
    IOPCDataCallback : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE OnDataChange( 
            /* [in] */ DWORD dwTransid,
            /* [in] */ OPCHANDLE hGroup,
            /* [in] */ HRESULT hrMasterquality,
            /* [in] */ HRESULT hrMastererror,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phClientItems,
            /* [size_is][in] */ VARIANT *pvValues,
            /* [size_is][in] */ WORD *pwQualities,
            /* [size_is][in] */ FILETIME *pftTimeStamps,
            /* [size_is][in] */ HRESULT *pErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE OnReadComplete( 
            /* [in] */ DWORD dwTransid,
            /* [in] */ OPCHANDLE hGroup,
            /* [in] */ HRESULT hrMasterquality,
            /* [in] */ HRESULT hrMastererror,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phClientItems,
            /* [size_is][in] */ VARIANT *pvValues,
            /* [size_is][in] */ WORD *pwQualities,
            /* [size_is][in] */ FILETIME *pftTimeStamps,
            /* [size_is][in] */ HRESULT *pErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE OnWriteComplete( 
            /* [in] */ DWORD dwTransid,
            /* [in] */ OPCHANDLE hGroup,
            /* [in] */ HRESULT hrMastererr,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *pClienthandles,
            /* [size_is][in] */ HRESULT *pErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE OnCancelComplete( 
            /* [in] */ DWORD dwTransid,
            /* [in] */ OPCHANDLE hGroup) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCDataCallbackVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCDataCallback * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCDataCallback * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCDataCallback * This);
        
        HRESULT ( STDMETHODCALLTYPE *OnDataChange )( 
            IOPCDataCallback * This,
            /* [in] */ DWORD dwTransid,
            /* [in] */ OPCHANDLE hGroup,
            /* [in] */ HRESULT hrMasterquality,
            /* [in] */ HRESULT hrMastererror,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phClientItems,
            /* [size_is][in] */ VARIANT *pvValues,
            /* [size_is][in] */ WORD *pwQualities,
            /* [size_is][in] */ FILETIME *pftTimeStamps,
            /* [size_is][in] */ HRESULT *pErrors);
        
        HRESULT ( STDMETHODCALLTYPE *OnReadComplete )( 
            IOPCDataCallback * This,
            /* [in] */ DWORD dwTransid,
            /* [in] */ OPCHANDLE hGroup,
            /* [in] */ HRESULT hrMasterquality,
            /* [in] */ HRESULT hrMastererror,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phClientItems,
            /* [size_is][in] */ VARIANT *pvValues,
            /* [size_is][in] */ WORD *pwQualities,
            /* [size_is][in] */ FILETIME *pftTimeStamps,
            /* [size_is][in] */ HRESULT *pErrors);
        
        HRESULT ( STDMETHODCALLTYPE *OnWriteComplete )( 
            IOPCDataCallback * This,
            /* [in] */ DWORD dwTransid,
            /* [in] */ OPCHANDLE hGroup,
            /* [in] */ HRESULT hrMastererr,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *pClienthandles,
            /* [size_is][in] */ HRESULT *pErrors);
        
        HRESULT ( STDMETHODCALLTYPE *OnCancelComplete )( 
            IOPCDataCallback * This,
            /* [in] */ DWORD dwTransid,
            /* [in] */ OPCHANDLE hGroup);
        
        END_INTERFACE
    } IOPCDataCallbackVtbl;

    interface IOPCDataCallback
    {
        CONST_VTBL struct IOPCDataCallbackVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCDataCallback_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCDataCallback_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCDataCallback_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCDataCallback_OnDataChange(This,dwTransid,hGroup,hrMasterquality,hrMastererror,dwCount,phClientItems,pvValues,pwQualities,pftTimeStamps,pErrors)	\
    (This)->lpVtbl -> OnDataChange(This,dwTransid,hGroup,hrMasterquality,hrMastererror,dwCount,phClientItems,pvValues,pwQualities,pftTimeStamps,pErrors)

#define IOPCDataCallback_OnReadComplete(This,dwTransid,hGroup,hrMasterquality,hrMastererror,dwCount,phClientItems,pvValues,pwQualities,pftTimeStamps,pErrors)	\
    (This)->lpVtbl -> OnReadComplete(This,dwTransid,hGroup,hrMasterquality,hrMastererror,dwCount,phClientItems,pvValues,pwQualities,pftTimeStamps,pErrors)

#define IOPCDataCallback_OnWriteComplete(This,dwTransid,hGroup,hrMastererr,dwCount,pClienthandles,pErrors)	\
    (This)->lpVtbl -> OnWriteComplete(This,dwTransid,hGroup,hrMastererr,dwCount,pClienthandles,pErrors)

#define IOPCDataCallback_OnCancelComplete(This,dwTransid,hGroup)	\
    (This)->lpVtbl -> OnCancelComplete(This,dwTransid,hGroup)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCDataCallback_OnDataChange_Proxy( 
    IOPCDataCallback * This,
    /* [in] */ DWORD dwTransid,
    /* [in] */ OPCHANDLE hGroup,
    /* [in] */ HRESULT hrMasterquality,
    /* [in] */ HRESULT hrMastererror,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phClientItems,
    /* [size_is][in] */ VARIANT *pvValues,
    /* [size_is][in] */ WORD *pwQualities,
    /* [size_is][in] */ FILETIME *pftTimeStamps,
    /* [size_is][in] */ HRESULT *pErrors);


void __RPC_STUB IOPCDataCallback_OnDataChange_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCDataCallback_OnReadComplete_Proxy( 
    IOPCDataCallback * This,
    /* [in] */ DWORD dwTransid,
    /* [in] */ OPCHANDLE hGroup,
    /* [in] */ HRESULT hrMasterquality,
    /* [in] */ HRESULT hrMastererror,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phClientItems,
    /* [size_is][in] */ VARIANT *pvValues,
    /* [size_is][in] */ WORD *pwQualities,
    /* [size_is][in] */ FILETIME *pftTimeStamps,
    /* [size_is][in] */ HRESULT *pErrors);


void __RPC_STUB IOPCDataCallback_OnReadComplete_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCDataCallback_OnWriteComplete_Proxy( 
    IOPCDataCallback * This,
    /* [in] */ DWORD dwTransid,
    /* [in] */ OPCHANDLE hGroup,
    /* [in] */ HRESULT hrMastererr,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *pClienthandles,
    /* [size_is][in] */ HRESULT *pErrors);


void __RPC_STUB IOPCDataCallback_OnWriteComplete_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCDataCallback_OnCancelComplete_Proxy( 
    IOPCDataCallback * This,
    /* [in] */ DWORD dwTransid,
    /* [in] */ OPCHANDLE hGroup);


void __RPC_STUB IOPCDataCallback_OnCancelComplete_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCDataCallback_INTERFACE_DEFINED__ */


#ifndef __IOPCAsyncIO2_INTERFACE_DEFINED__
#define __IOPCAsyncIO2_INTERFACE_DEFINED__

/* interface IOPCAsyncIO2 */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCAsyncIO2;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("39c13a71-011e-11d0-9675-0020afd8adb3")
    IOPCAsyncIO2 : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE Read( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [in] */ DWORD dwTransactionID,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Write( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ VARIANT *pItemValues,
            /* [in] */ DWORD dwTransactionID,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Refresh2( 
            /* [in] */ OPCDATASOURCE dwSource,
            /* [in] */ DWORD dwTransactionID,
            /* [out] */ DWORD *pdwCancelID) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Cancel2( 
            /* [in] */ DWORD dwCancelID) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE SetEnable( 
            /* [in] */ BOOL bEnable) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetEnable( 
            /* [out] */ BOOL *pbEnable) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCAsyncIO2Vtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCAsyncIO2 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCAsyncIO2 * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCAsyncIO2 * This);
        
        HRESULT ( STDMETHODCALLTYPE *Read )( 
            IOPCAsyncIO2 * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [in] */ DWORD dwTransactionID,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *Write )( 
            IOPCAsyncIO2 * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ VARIANT *pItemValues,
            /* [in] */ DWORD dwTransactionID,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *Refresh2 )( 
            IOPCAsyncIO2 * This,
            /* [in] */ OPCDATASOURCE dwSource,
            /* [in] */ DWORD dwTransactionID,
            /* [out] */ DWORD *pdwCancelID);
        
        HRESULT ( STDMETHODCALLTYPE *Cancel2 )( 
            IOPCAsyncIO2 * This,
            /* [in] */ DWORD dwCancelID);
        
        HRESULT ( STDMETHODCALLTYPE *SetEnable )( 
            IOPCAsyncIO2 * This,
            /* [in] */ BOOL bEnable);
        
        HRESULT ( STDMETHODCALLTYPE *GetEnable )( 
            IOPCAsyncIO2 * This,
            /* [out] */ BOOL *pbEnable);
        
        END_INTERFACE
    } IOPCAsyncIO2Vtbl;

    interface IOPCAsyncIO2
    {
        CONST_VTBL struct IOPCAsyncIO2Vtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCAsyncIO2_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCAsyncIO2_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCAsyncIO2_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCAsyncIO2_Read(This,dwCount,phServer,dwTransactionID,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> Read(This,dwCount,phServer,dwTransactionID,pdwCancelID,ppErrors)

#define IOPCAsyncIO2_Write(This,dwCount,phServer,pItemValues,dwTransactionID,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> Write(This,dwCount,phServer,pItemValues,dwTransactionID,pdwCancelID,ppErrors)

#define IOPCAsyncIO2_Refresh2(This,dwSource,dwTransactionID,pdwCancelID)	\
    (This)->lpVtbl -> Refresh2(This,dwSource,dwTransactionID,pdwCancelID)

#define IOPCAsyncIO2_Cancel2(This,dwCancelID)	\
    (This)->lpVtbl -> Cancel2(This,dwCancelID)

#define IOPCAsyncIO2_SetEnable(This,bEnable)	\
    (This)->lpVtbl -> SetEnable(This,bEnable)

#define IOPCAsyncIO2_GetEnable(This,pbEnable)	\
    (This)->lpVtbl -> GetEnable(This,pbEnable)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCAsyncIO2_Read_Proxy( 
    IOPCAsyncIO2 * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [in] */ DWORD dwTransactionID,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCAsyncIO2_Read_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCAsyncIO2_Write_Proxy( 
    IOPCAsyncIO2 * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ VARIANT *pItemValues,
    /* [in] */ DWORD dwTransactionID,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCAsyncIO2_Write_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCAsyncIO2_Refresh2_Proxy( 
    IOPCAsyncIO2 * This,
    /* [in] */ OPCDATASOURCE dwSource,
    /* [in] */ DWORD dwTransactionID,
    /* [out] */ DWORD *pdwCancelID);


void __RPC_STUB IOPCAsyncIO2_Refresh2_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCAsyncIO2_Cancel2_Proxy( 
    IOPCAsyncIO2 * This,
    /* [in] */ DWORD dwCancelID);


void __RPC_STUB IOPCAsyncIO2_Cancel2_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCAsyncIO2_SetEnable_Proxy( 
    IOPCAsyncIO2 * This,
    /* [in] */ BOOL bEnable);


void __RPC_STUB IOPCAsyncIO2_SetEnable_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCAsyncIO2_GetEnable_Proxy( 
    IOPCAsyncIO2 * This,
    /* [out] */ BOOL *pbEnable);


void __RPC_STUB IOPCAsyncIO2_GetEnable_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCAsyncIO2_INTERFACE_DEFINED__ */


#ifndef __IOPCItemProperties_INTERFACE_DEFINED__
#define __IOPCItemProperties_INTERFACE_DEFINED__

/* interface IOPCItemProperties */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCItemProperties;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("39c13a72-011e-11d0-9675-0020afd8adb3")
    IOPCItemProperties : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE QueryAvailableProperties( 
            /* [in] */ LPWSTR szItemID,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppPropertyIDs,
            /* [size_is][size_is][out] */ LPWSTR **ppDescriptions,
            /* [size_is][size_is][out] */ VARTYPE **ppvtDataTypes) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetItemProperties( 
            /* [in] */ LPWSTR szItemID,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ DWORD *pdwPropertyIDs,
            /* [size_is][size_is][out] */ VARIANT **ppvData,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE LookupItemIDs( 
            /* [in] */ LPWSTR szItemID,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ DWORD *pdwPropertyIDs,
            /* [size_is][size_is][string][out] */ LPWSTR **ppszNewItemIDs,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCItemPropertiesVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCItemProperties * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCItemProperties * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCItemProperties * This);
        
        HRESULT ( STDMETHODCALLTYPE *QueryAvailableProperties )( 
            IOPCItemProperties * This,
            /* [in] */ LPWSTR szItemID,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ DWORD **ppPropertyIDs,
            /* [size_is][size_is][out] */ LPWSTR **ppDescriptions,
            /* [size_is][size_is][out] */ VARTYPE **ppvtDataTypes);
        
        HRESULT ( STDMETHODCALLTYPE *GetItemProperties )( 
            IOPCItemProperties * This,
            /* [in] */ LPWSTR szItemID,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ DWORD *pdwPropertyIDs,
            /* [size_is][size_is][out] */ VARIANT **ppvData,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *LookupItemIDs )( 
            IOPCItemProperties * This,
            /* [in] */ LPWSTR szItemID,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ DWORD *pdwPropertyIDs,
            /* [size_is][size_is][string][out] */ LPWSTR **ppszNewItemIDs,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        END_INTERFACE
    } IOPCItemPropertiesVtbl;

    interface IOPCItemProperties
    {
        CONST_VTBL struct IOPCItemPropertiesVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCItemProperties_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCItemProperties_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCItemProperties_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCItemProperties_QueryAvailableProperties(This,szItemID,pdwCount,ppPropertyIDs,ppDescriptions,ppvtDataTypes)	\
    (This)->lpVtbl -> QueryAvailableProperties(This,szItemID,pdwCount,ppPropertyIDs,ppDescriptions,ppvtDataTypes)

#define IOPCItemProperties_GetItemProperties(This,szItemID,dwCount,pdwPropertyIDs,ppvData,ppErrors)	\
    (This)->lpVtbl -> GetItemProperties(This,szItemID,dwCount,pdwPropertyIDs,ppvData,ppErrors)

#define IOPCItemProperties_LookupItemIDs(This,szItemID,dwCount,pdwPropertyIDs,ppszNewItemIDs,ppErrors)	\
    (This)->lpVtbl -> LookupItemIDs(This,szItemID,dwCount,pdwPropertyIDs,ppszNewItemIDs,ppErrors)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCItemProperties_QueryAvailableProperties_Proxy( 
    IOPCItemProperties * This,
    /* [in] */ LPWSTR szItemID,
    /* [out] */ DWORD *pdwCount,
    /* [size_is][size_is][out] */ DWORD **ppPropertyIDs,
    /* [size_is][size_is][out] */ LPWSTR **ppDescriptions,
    /* [size_is][size_is][out] */ VARTYPE **ppvtDataTypes);


void __RPC_STUB IOPCItemProperties_QueryAvailableProperties_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCItemProperties_GetItemProperties_Proxy( 
    IOPCItemProperties * This,
    /* [in] */ LPWSTR szItemID,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ DWORD *pdwPropertyIDs,
    /* [size_is][size_is][out] */ VARIANT **ppvData,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemProperties_GetItemProperties_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCItemProperties_LookupItemIDs_Proxy( 
    IOPCItemProperties * This,
    /* [in] */ LPWSTR szItemID,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ DWORD *pdwPropertyIDs,
    /* [size_is][size_is][string][out] */ LPWSTR **ppszNewItemIDs,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemProperties_LookupItemIDs_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCItemProperties_INTERFACE_DEFINED__ */


#ifndef __IOPCItemDeadbandMgt_INTERFACE_DEFINED__
#define __IOPCItemDeadbandMgt_INTERFACE_DEFINED__

/* interface IOPCItemDeadbandMgt */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCItemDeadbandMgt;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("5946DA93-8B39-4ec8-AB3D-AA73DF5BC86F")
    IOPCItemDeadbandMgt : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE SetItemDeadband( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FLOAT *pPercentDeadband,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetItemDeadband( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ FLOAT **ppPercentDeadband,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ClearItemDeadband( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCItemDeadbandMgtVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCItemDeadbandMgt * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCItemDeadbandMgt * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCItemDeadbandMgt * This);
        
        HRESULT ( STDMETHODCALLTYPE *SetItemDeadband )( 
            IOPCItemDeadbandMgt * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ FLOAT *pPercentDeadband,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *GetItemDeadband )( 
            IOPCItemDeadbandMgt * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ FLOAT **ppPercentDeadband,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *ClearItemDeadband )( 
            IOPCItemDeadbandMgt * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        END_INTERFACE
    } IOPCItemDeadbandMgtVtbl;

    interface IOPCItemDeadbandMgt
    {
        CONST_VTBL struct IOPCItemDeadbandMgtVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCItemDeadbandMgt_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCItemDeadbandMgt_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCItemDeadbandMgt_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCItemDeadbandMgt_SetItemDeadband(This,dwCount,phServer,pPercentDeadband,ppErrors)	\
    (This)->lpVtbl -> SetItemDeadband(This,dwCount,phServer,pPercentDeadband,ppErrors)

#define IOPCItemDeadbandMgt_GetItemDeadband(This,dwCount,phServer,ppPercentDeadband,ppErrors)	\
    (This)->lpVtbl -> GetItemDeadband(This,dwCount,phServer,ppPercentDeadband,ppErrors)

#define IOPCItemDeadbandMgt_ClearItemDeadband(This,dwCount,phServer,ppErrors)	\
    (This)->lpVtbl -> ClearItemDeadband(This,dwCount,phServer,ppErrors)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCItemDeadbandMgt_SetItemDeadband_Proxy( 
    IOPCItemDeadbandMgt * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ FLOAT *pPercentDeadband,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemDeadbandMgt_SetItemDeadband_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCItemDeadbandMgt_GetItemDeadband_Proxy( 
    IOPCItemDeadbandMgt * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][size_is][out] */ FLOAT **ppPercentDeadband,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemDeadbandMgt_GetItemDeadband_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCItemDeadbandMgt_ClearItemDeadband_Proxy( 
    IOPCItemDeadbandMgt * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemDeadbandMgt_ClearItemDeadband_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCItemDeadbandMgt_INTERFACE_DEFINED__ */


#ifndef __IOPCItemSamplingMgt_INTERFACE_DEFINED__
#define __IOPCItemSamplingMgt_INTERFACE_DEFINED__

/* interface IOPCItemSamplingMgt */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCItemSamplingMgt;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("3E22D313-F08B-41a5-86C8-95E95CB49FFC")
    IOPCItemSamplingMgt : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE SetItemSamplingRate( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ DWORD *pdwRequestedSamplingRate,
            /* [size_is][size_is][out] */ DWORD **ppdwRevisedSamplingRate,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetItemSamplingRate( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ DWORD **ppdwSamplingRate,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ClearItemSamplingRate( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE SetItemBufferEnable( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ BOOL *pbEnable,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetItemBufferEnable( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ BOOL **ppbEnable,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCItemSamplingMgtVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCItemSamplingMgt * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCItemSamplingMgt * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCItemSamplingMgt * This);
        
        HRESULT ( STDMETHODCALLTYPE *SetItemSamplingRate )( 
            IOPCItemSamplingMgt * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ DWORD *pdwRequestedSamplingRate,
            /* [size_is][size_is][out] */ DWORD **ppdwRevisedSamplingRate,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *GetItemSamplingRate )( 
            IOPCItemSamplingMgt * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ DWORD **ppdwSamplingRate,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *ClearItemSamplingRate )( 
            IOPCItemSamplingMgt * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *SetItemBufferEnable )( 
            IOPCItemSamplingMgt * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ BOOL *pbEnable,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *GetItemBufferEnable )( 
            IOPCItemSamplingMgt * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ BOOL **ppbEnable,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        END_INTERFACE
    } IOPCItemSamplingMgtVtbl;

    interface IOPCItemSamplingMgt
    {
        CONST_VTBL struct IOPCItemSamplingMgtVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCItemSamplingMgt_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCItemSamplingMgt_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCItemSamplingMgt_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCItemSamplingMgt_SetItemSamplingRate(This,dwCount,phServer,pdwRequestedSamplingRate,ppdwRevisedSamplingRate,ppErrors)	\
    (This)->lpVtbl -> SetItemSamplingRate(This,dwCount,phServer,pdwRequestedSamplingRate,ppdwRevisedSamplingRate,ppErrors)

#define IOPCItemSamplingMgt_GetItemSamplingRate(This,dwCount,phServer,ppdwSamplingRate,ppErrors)	\
    (This)->lpVtbl -> GetItemSamplingRate(This,dwCount,phServer,ppdwSamplingRate,ppErrors)

#define IOPCItemSamplingMgt_ClearItemSamplingRate(This,dwCount,phServer,ppErrors)	\
    (This)->lpVtbl -> ClearItemSamplingRate(This,dwCount,phServer,ppErrors)

#define IOPCItemSamplingMgt_SetItemBufferEnable(This,dwCount,phServer,pbEnable,ppErrors)	\
    (This)->lpVtbl -> SetItemBufferEnable(This,dwCount,phServer,pbEnable,ppErrors)

#define IOPCItemSamplingMgt_GetItemBufferEnable(This,dwCount,phServer,ppbEnable,ppErrors)	\
    (This)->lpVtbl -> GetItemBufferEnable(This,dwCount,phServer,ppbEnable,ppErrors)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCItemSamplingMgt_SetItemSamplingRate_Proxy( 
    IOPCItemSamplingMgt * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ DWORD *pdwRequestedSamplingRate,
    /* [size_is][size_is][out] */ DWORD **ppdwRevisedSamplingRate,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemSamplingMgt_SetItemSamplingRate_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCItemSamplingMgt_GetItemSamplingRate_Proxy( 
    IOPCItemSamplingMgt * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][size_is][out] */ DWORD **ppdwSamplingRate,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemSamplingMgt_GetItemSamplingRate_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCItemSamplingMgt_ClearItemSamplingRate_Proxy( 
    IOPCItemSamplingMgt * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemSamplingMgt_ClearItemSamplingRate_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCItemSamplingMgt_SetItemBufferEnable_Proxy( 
    IOPCItemSamplingMgt * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ BOOL *pbEnable,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemSamplingMgt_SetItemBufferEnable_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCItemSamplingMgt_GetItemBufferEnable_Proxy( 
    IOPCItemSamplingMgt * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][size_is][out] */ BOOL **ppbEnable,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemSamplingMgt_GetItemBufferEnable_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCItemSamplingMgt_INTERFACE_DEFINED__ */


#ifndef __IOPCBrowse_INTERFACE_DEFINED__
#define __IOPCBrowse_INTERFACE_DEFINED__

/* interface IOPCBrowse */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCBrowse;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("39227004-A18F-4b57-8B0A-5235670F4468")
    IOPCBrowse : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE GetProperties( 
            /* [in] */ DWORD dwItemCount,
            /* [size_is][string][in] */ LPWSTR *pszItemIDs,
            /* [in] */ BOOL bReturnPropertyValues,
            /* [in] */ DWORD dwPropertyCount,
            /* [size_is][in] */ DWORD *pdwPropertyIDs,
            /* [size_is][size_is][out] */ OPCITEMPROPERTIES **ppItemProperties) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Browse( 
            /* [string][in] */ LPWSTR szItemID,
            /* [string][out][in] */ LPWSTR *pszContinuationPoint,
            /* [in] */ DWORD dwMaxElementsReturned,
            /* [in] */ OPCBROWSEFILTER dwBrowseFilter,
            /* [string][in] */ LPWSTR szElementNameFilter,
            /* [string][in] */ LPWSTR szVendorFilter,
            /* [in] */ BOOL bReturnAllProperties,
            /* [in] */ BOOL bReturnPropertyValues,
            /* [in] */ DWORD dwPropertyCount,
            /* [size_is][in] */ DWORD *pdwPropertyIDs,
            /* [out] */ BOOL *pbMoreElements,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ OPCBROWSEELEMENT **ppBrowseElements) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCBrowseVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCBrowse * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCBrowse * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCBrowse * This);
        
        HRESULT ( STDMETHODCALLTYPE *GetProperties )( 
            IOPCBrowse * This,
            /* [in] */ DWORD dwItemCount,
            /* [size_is][string][in] */ LPWSTR *pszItemIDs,
            /* [in] */ BOOL bReturnPropertyValues,
            /* [in] */ DWORD dwPropertyCount,
            /* [size_is][in] */ DWORD *pdwPropertyIDs,
            /* [size_is][size_is][out] */ OPCITEMPROPERTIES **ppItemProperties);
        
        HRESULT ( STDMETHODCALLTYPE *Browse )( 
            IOPCBrowse * This,
            /* [string][in] */ LPWSTR szItemID,
            /* [string][out][in] */ LPWSTR *pszContinuationPoint,
            /* [in] */ DWORD dwMaxElementsReturned,
            /* [in] */ OPCBROWSEFILTER dwBrowseFilter,
            /* [string][in] */ LPWSTR szElementNameFilter,
            /* [string][in] */ LPWSTR szVendorFilter,
            /* [in] */ BOOL bReturnAllProperties,
            /* [in] */ BOOL bReturnPropertyValues,
            /* [in] */ DWORD dwPropertyCount,
            /* [size_is][in] */ DWORD *pdwPropertyIDs,
            /* [out] */ BOOL *pbMoreElements,
            /* [out] */ DWORD *pdwCount,
            /* [size_is][size_is][out] */ OPCBROWSEELEMENT **ppBrowseElements);
        
        END_INTERFACE
    } IOPCBrowseVtbl;

    interface IOPCBrowse
    {
        CONST_VTBL struct IOPCBrowseVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCBrowse_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCBrowse_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCBrowse_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCBrowse_GetProperties(This,dwItemCount,pszItemIDs,bReturnPropertyValues,dwPropertyCount,pdwPropertyIDs,ppItemProperties)	\
    (This)->lpVtbl -> GetProperties(This,dwItemCount,pszItemIDs,bReturnPropertyValues,dwPropertyCount,pdwPropertyIDs,ppItemProperties)

#define IOPCBrowse_Browse(This,szItemID,pszContinuationPoint,dwMaxElementsReturned,dwBrowseFilter,szElementNameFilter,szVendorFilter,bReturnAllProperties,bReturnPropertyValues,dwPropertyCount,pdwPropertyIDs,pbMoreElements,pdwCount,ppBrowseElements)	\
    (This)->lpVtbl -> Browse(This,szItemID,pszContinuationPoint,dwMaxElementsReturned,dwBrowseFilter,szElementNameFilter,szVendorFilter,bReturnAllProperties,bReturnPropertyValues,dwPropertyCount,pdwPropertyIDs,pbMoreElements,pdwCount,ppBrowseElements)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCBrowse_GetProperties_Proxy( 
    IOPCBrowse * This,
    /* [in] */ DWORD dwItemCount,
    /* [size_is][string][in] */ LPWSTR *pszItemIDs,
    /* [in] */ BOOL bReturnPropertyValues,
    /* [in] */ DWORD dwPropertyCount,
    /* [size_is][in] */ DWORD *pdwPropertyIDs,
    /* [size_is][size_is][out] */ OPCITEMPROPERTIES **ppItemProperties);


void __RPC_STUB IOPCBrowse_GetProperties_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCBrowse_Browse_Proxy( 
    IOPCBrowse * This,
    /* [string][in] */ LPWSTR szItemID,
    /* [string][out][in] */ LPWSTR *pszContinuationPoint,
    /* [in] */ DWORD dwMaxElementsReturned,
    /* [in] */ OPCBROWSEFILTER dwBrowseFilter,
    /* [string][in] */ LPWSTR szElementNameFilter,
    /* [string][in] */ LPWSTR szVendorFilter,
    /* [in] */ BOOL bReturnAllProperties,
    /* [in] */ BOOL bReturnPropertyValues,
    /* [in] */ DWORD dwPropertyCount,
    /* [size_is][in] */ DWORD *pdwPropertyIDs,
    /* [out] */ BOOL *pbMoreElements,
    /* [out] */ DWORD *pdwCount,
    /* [size_is][size_is][out] */ OPCBROWSEELEMENT **ppBrowseElements);


void __RPC_STUB IOPCBrowse_Browse_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCBrowse_INTERFACE_DEFINED__ */


#ifndef __IOPCItemIO_INTERFACE_DEFINED__
#define __IOPCItemIO_INTERFACE_DEFINED__

/* interface IOPCItemIO */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCItemIO;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("85C0B427-2893-4cbc-BD78-E5FC5146F08F")
    IOPCItemIO : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE Read( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ LPCWSTR *pszItemIDs,
            /* [size_is][in] */ DWORD *pdwMaxAge,
            /* [size_is][size_is][out] */ VARIANT **ppvValues,
            /* [size_is][size_is][out] */ WORD **ppwQualities,
            /* [size_is][size_is][out] */ FILETIME **ppftTimeStamps,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE WriteVQT( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ LPCWSTR *pszItemIDs,
            /* [size_is][in] */ OPCITEMVQT *pItemVQT,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCItemIOVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCItemIO * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCItemIO * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCItemIO * This);
        
        HRESULT ( STDMETHODCALLTYPE *Read )( 
            IOPCItemIO * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ LPCWSTR *pszItemIDs,
            /* [size_is][in] */ DWORD *pdwMaxAge,
            /* [size_is][size_is][out] */ VARIANT **ppvValues,
            /* [size_is][size_is][out] */ WORD **ppwQualities,
            /* [size_is][size_is][out] */ FILETIME **ppftTimeStamps,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *WriteVQT )( 
            IOPCItemIO * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ LPCWSTR *pszItemIDs,
            /* [size_is][in] */ OPCITEMVQT *pItemVQT,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        END_INTERFACE
    } IOPCItemIOVtbl;

    interface IOPCItemIO
    {
        CONST_VTBL struct IOPCItemIOVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCItemIO_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCItemIO_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCItemIO_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCItemIO_Read(This,dwCount,pszItemIDs,pdwMaxAge,ppvValues,ppwQualities,ppftTimeStamps,ppErrors)	\
    (This)->lpVtbl -> Read(This,dwCount,pszItemIDs,pdwMaxAge,ppvValues,ppwQualities,ppftTimeStamps,ppErrors)

#define IOPCItemIO_WriteVQT(This,dwCount,pszItemIDs,pItemVQT,ppErrors)	\
    (This)->lpVtbl -> WriteVQT(This,dwCount,pszItemIDs,pItemVQT,ppErrors)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCItemIO_Read_Proxy( 
    IOPCItemIO * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ LPCWSTR *pszItemIDs,
    /* [size_is][in] */ DWORD *pdwMaxAge,
    /* [size_is][size_is][out] */ VARIANT **ppvValues,
    /* [size_is][size_is][out] */ WORD **ppwQualities,
    /* [size_is][size_is][out] */ FILETIME **ppftTimeStamps,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemIO_Read_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCItemIO_WriteVQT_Proxy( 
    IOPCItemIO * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ LPCWSTR *pszItemIDs,
    /* [size_is][in] */ OPCITEMVQT *pItemVQT,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCItemIO_WriteVQT_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCItemIO_INTERFACE_DEFINED__ */


#ifndef __IOPCSyncIO2_INTERFACE_DEFINED__
#define __IOPCSyncIO2_INTERFACE_DEFINED__

/* interface IOPCSyncIO2 */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCSyncIO2;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("730F5F0F-55B1-4c81-9E18-FF8A0904E1FA")
    IOPCSyncIO2 : public IOPCSyncIO
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE ReadMaxAge( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ DWORD *pdwMaxAge,
            /* [size_is][size_is][out] */ VARIANT **ppvValues,
            /* [size_is][size_is][out] */ WORD **ppwQualities,
            /* [size_is][size_is][out] */ FILETIME **ppftTimeStamps,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE WriteVQT( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ OPCITEMVQT *pItemVQT,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCSyncIO2Vtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCSyncIO2 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCSyncIO2 * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCSyncIO2 * This);
        
        HRESULT ( STDMETHODCALLTYPE *Read )( 
            IOPCSyncIO2 * This,
            /* [in] */ OPCDATASOURCE dwSource,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][size_is][out] */ OPCITEMSTATE **ppItemValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *Write )( 
            IOPCSyncIO2 * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ VARIANT *pItemValues,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *ReadMaxAge )( 
            IOPCSyncIO2 * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ DWORD *pdwMaxAge,
            /* [size_is][size_is][out] */ VARIANT **ppvValues,
            /* [size_is][size_is][out] */ WORD **ppwQualities,
            /* [size_is][size_is][out] */ FILETIME **ppftTimeStamps,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *WriteVQT )( 
            IOPCSyncIO2 * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ OPCITEMVQT *pItemVQT,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        END_INTERFACE
    } IOPCSyncIO2Vtbl;

    interface IOPCSyncIO2
    {
        CONST_VTBL struct IOPCSyncIO2Vtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCSyncIO2_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCSyncIO2_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCSyncIO2_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCSyncIO2_Read(This,dwSource,dwCount,phServer,ppItemValues,ppErrors)	\
    (This)->lpVtbl -> Read(This,dwSource,dwCount,phServer,ppItemValues,ppErrors)

#define IOPCSyncIO2_Write(This,dwCount,phServer,pItemValues,ppErrors)	\
    (This)->lpVtbl -> Write(This,dwCount,phServer,pItemValues,ppErrors)


#define IOPCSyncIO2_ReadMaxAge(This,dwCount,phServer,pdwMaxAge,ppvValues,ppwQualities,ppftTimeStamps,ppErrors)	\
    (This)->lpVtbl -> ReadMaxAge(This,dwCount,phServer,pdwMaxAge,ppvValues,ppwQualities,ppftTimeStamps,ppErrors)

#define IOPCSyncIO2_WriteVQT(This,dwCount,phServer,pItemVQT,ppErrors)	\
    (This)->lpVtbl -> WriteVQT(This,dwCount,phServer,pItemVQT,ppErrors)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCSyncIO2_ReadMaxAge_Proxy( 
    IOPCSyncIO2 * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ DWORD *pdwMaxAge,
    /* [size_is][size_is][out] */ VARIANT **ppvValues,
    /* [size_is][size_is][out] */ WORD **ppwQualities,
    /* [size_is][size_is][out] */ FILETIME **ppftTimeStamps,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCSyncIO2_ReadMaxAge_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCSyncIO2_WriteVQT_Proxy( 
    IOPCSyncIO2 * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ OPCITEMVQT *pItemVQT,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCSyncIO2_WriteVQT_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCSyncIO2_INTERFACE_DEFINED__ */


#ifndef __IOPCAsyncIO3_INTERFACE_DEFINED__
#define __IOPCAsyncIO3_INTERFACE_DEFINED__

/* interface IOPCAsyncIO3 */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCAsyncIO3;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("0967B97B-36EF-423e-B6F8-6BFF1E40D39D")
    IOPCAsyncIO3 : public IOPCAsyncIO2
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE ReadMaxAge( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ DWORD *pdwMaxAge,
            /* [in] */ DWORD dwTransactionID,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE WriteVQT( 
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ OPCITEMVQT *pItemVQT,
            /* [in] */ DWORD dwTransactionID,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE RefreshMaxAge( 
            /* [in] */ DWORD dwMaxAge,
            /* [in] */ DWORD dwTransactionID,
            /* [out] */ DWORD *pdwCancelID) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCAsyncIO3Vtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCAsyncIO3 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCAsyncIO3 * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCAsyncIO3 * This);
        
        HRESULT ( STDMETHODCALLTYPE *Read )( 
            IOPCAsyncIO3 * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [in] */ DWORD dwTransactionID,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *Write )( 
            IOPCAsyncIO3 * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ VARIANT *pItemValues,
            /* [in] */ DWORD dwTransactionID,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *Refresh2 )( 
            IOPCAsyncIO3 * This,
            /* [in] */ OPCDATASOURCE dwSource,
            /* [in] */ DWORD dwTransactionID,
            /* [out] */ DWORD *pdwCancelID);
        
        HRESULT ( STDMETHODCALLTYPE *Cancel2 )( 
            IOPCAsyncIO3 * This,
            /* [in] */ DWORD dwCancelID);
        
        HRESULT ( STDMETHODCALLTYPE *SetEnable )( 
            IOPCAsyncIO3 * This,
            /* [in] */ BOOL bEnable);
        
        HRESULT ( STDMETHODCALLTYPE *GetEnable )( 
            IOPCAsyncIO3 * This,
            /* [out] */ BOOL *pbEnable);
        
        HRESULT ( STDMETHODCALLTYPE *ReadMaxAge )( 
            IOPCAsyncIO3 * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ DWORD *pdwMaxAge,
            /* [in] */ DWORD dwTransactionID,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *WriteVQT )( 
            IOPCAsyncIO3 * This,
            /* [in] */ DWORD dwCount,
            /* [size_is][in] */ OPCHANDLE *phServer,
            /* [size_is][in] */ OPCITEMVQT *pItemVQT,
            /* [in] */ DWORD dwTransactionID,
            /* [out] */ DWORD *pdwCancelID,
            /* [size_is][size_is][out] */ HRESULT **ppErrors);
        
        HRESULT ( STDMETHODCALLTYPE *RefreshMaxAge )( 
            IOPCAsyncIO3 * This,
            /* [in] */ DWORD dwMaxAge,
            /* [in] */ DWORD dwTransactionID,
            /* [out] */ DWORD *pdwCancelID);
        
        END_INTERFACE
    } IOPCAsyncIO3Vtbl;

    interface IOPCAsyncIO3
    {
        CONST_VTBL struct IOPCAsyncIO3Vtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCAsyncIO3_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCAsyncIO3_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCAsyncIO3_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCAsyncIO3_Read(This,dwCount,phServer,dwTransactionID,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> Read(This,dwCount,phServer,dwTransactionID,pdwCancelID,ppErrors)

#define IOPCAsyncIO3_Write(This,dwCount,phServer,pItemValues,dwTransactionID,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> Write(This,dwCount,phServer,pItemValues,dwTransactionID,pdwCancelID,ppErrors)

#define IOPCAsyncIO3_Refresh2(This,dwSource,dwTransactionID,pdwCancelID)	\
    (This)->lpVtbl -> Refresh2(This,dwSource,dwTransactionID,pdwCancelID)

#define IOPCAsyncIO3_Cancel2(This,dwCancelID)	\
    (This)->lpVtbl -> Cancel2(This,dwCancelID)

#define IOPCAsyncIO3_SetEnable(This,bEnable)	\
    (This)->lpVtbl -> SetEnable(This,bEnable)

#define IOPCAsyncIO3_GetEnable(This,pbEnable)	\
    (This)->lpVtbl -> GetEnable(This,pbEnable)


#define IOPCAsyncIO3_ReadMaxAge(This,dwCount,phServer,pdwMaxAge,dwTransactionID,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> ReadMaxAge(This,dwCount,phServer,pdwMaxAge,dwTransactionID,pdwCancelID,ppErrors)

#define IOPCAsyncIO3_WriteVQT(This,dwCount,phServer,pItemVQT,dwTransactionID,pdwCancelID,ppErrors)	\
    (This)->lpVtbl -> WriteVQT(This,dwCount,phServer,pItemVQT,dwTransactionID,pdwCancelID,ppErrors)

#define IOPCAsyncIO3_RefreshMaxAge(This,dwMaxAge,dwTransactionID,pdwCancelID)	\
    (This)->lpVtbl -> RefreshMaxAge(This,dwMaxAge,dwTransactionID,pdwCancelID)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCAsyncIO3_ReadMaxAge_Proxy( 
    IOPCAsyncIO3 * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ DWORD *pdwMaxAge,
    /* [in] */ DWORD dwTransactionID,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCAsyncIO3_ReadMaxAge_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCAsyncIO3_WriteVQT_Proxy( 
    IOPCAsyncIO3 * This,
    /* [in] */ DWORD dwCount,
    /* [size_is][in] */ OPCHANDLE *phServer,
    /* [size_is][in] */ OPCITEMVQT *pItemVQT,
    /* [in] */ DWORD dwTransactionID,
    /* [out] */ DWORD *pdwCancelID,
    /* [size_is][size_is][out] */ HRESULT **ppErrors);


void __RPC_STUB IOPCAsyncIO3_WriteVQT_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCAsyncIO3_RefreshMaxAge_Proxy( 
    IOPCAsyncIO3 * This,
    /* [in] */ DWORD dwMaxAge,
    /* [in] */ DWORD dwTransactionID,
    /* [out] */ DWORD *pdwCancelID);


void __RPC_STUB IOPCAsyncIO3_RefreshMaxAge_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCAsyncIO3_INTERFACE_DEFINED__ */


#ifndef __IOPCGroupStateMgt2_INTERFACE_DEFINED__
#define __IOPCGroupStateMgt2_INTERFACE_DEFINED__

/* interface IOPCGroupStateMgt2 */
/* [object][unique][uuid] */ 


EXTERN_C const IID IID_IOPCGroupStateMgt2;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("8E368666-D72E-4f78-87ED-647611C61C9F")
    IOPCGroupStateMgt2 : public IOPCGroupStateMgt
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE SetKeepAlive( 
            /* [in] */ DWORD dwKeepAliveTime,
            /* [out] */ DWORD *pdwRevisedKeepAliveTime) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetKeepAlive( 
            /* [out] */ DWORD *pdwKeepAliveTime) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCGroupStateMgt2Vtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCGroupStateMgt2 * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCGroupStateMgt2 * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCGroupStateMgt2 * This);
        
        HRESULT ( STDMETHODCALLTYPE *GetState )( 
            IOPCGroupStateMgt2 * This,
            /* [out] */ DWORD *pUpdateRate,
            /* [out] */ BOOL *pActive,
            /* [string][out] */ LPWSTR *ppName,
            /* [out] */ LONG *pTimeBias,
            /* [out] */ FLOAT *pPercentDeadband,
            /* [out] */ DWORD *pLCID,
            /* [out] */ OPCHANDLE *phClientGroup,
            /* [out] */ OPCHANDLE *phServerGroup);
        
        HRESULT ( STDMETHODCALLTYPE *SetState )( 
            IOPCGroupStateMgt2 * This,
            /* [in][unique] */ DWORD *pRequestedUpdateRate,
            /* [out] */ DWORD *pRevisedUpdateRate,
            /* [in][unique] */ BOOL *pActive,
            /* [in][unique] */ LONG *pTimeBias,
            /* [in][unique] */ FLOAT *pPercentDeadband,
            /* [in][unique] */ DWORD *pLCID,
            /* [in][unique] */ OPCHANDLE *phClientGroup);
        
        HRESULT ( STDMETHODCALLTYPE *SetName )( 
            IOPCGroupStateMgt2 * This,
            /* [string][in] */ LPCWSTR szName);
        
        HRESULT ( STDMETHODCALLTYPE *CloneGroup )( 
            IOPCGroupStateMgt2 * This,
            /* [string][in] */ LPCWSTR szName,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ LPUNKNOWN *ppUnk);
        
        HRESULT ( STDMETHODCALLTYPE *SetKeepAlive )( 
            IOPCGroupStateMgt2 * This,
            /* [in] */ DWORD dwKeepAliveTime,
            /* [out] */ DWORD *pdwRevisedKeepAliveTime);
        
        HRESULT ( STDMETHODCALLTYPE *GetKeepAlive )( 
            IOPCGroupStateMgt2 * This,
            /* [out] */ DWORD *pdwKeepAliveTime);
        
        END_INTERFACE
    } IOPCGroupStateMgt2Vtbl;

    interface IOPCGroupStateMgt2
    {
        CONST_VTBL struct IOPCGroupStateMgt2Vtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCGroupStateMgt2_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCGroupStateMgt2_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCGroupStateMgt2_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCGroupStateMgt2_GetState(This,pUpdateRate,pActive,ppName,pTimeBias,pPercentDeadband,pLCID,phClientGroup,phServerGroup)	\
    (This)->lpVtbl -> GetState(This,pUpdateRate,pActive,ppName,pTimeBias,pPercentDeadband,pLCID,phClientGroup,phServerGroup)

#define IOPCGroupStateMgt2_SetState(This,pRequestedUpdateRate,pRevisedUpdateRate,pActive,pTimeBias,pPercentDeadband,pLCID,phClientGroup)	\
    (This)->lpVtbl -> SetState(This,pRequestedUpdateRate,pRevisedUpdateRate,pActive,pTimeBias,pPercentDeadband,pLCID,phClientGroup)

#define IOPCGroupStateMgt2_SetName(This,szName)	\
    (This)->lpVtbl -> SetName(This,szName)

#define IOPCGroupStateMgt2_CloneGroup(This,szName,riid,ppUnk)	\
    (This)->lpVtbl -> CloneGroup(This,szName,riid,ppUnk)


#define IOPCGroupStateMgt2_SetKeepAlive(This,dwKeepAliveTime,pdwRevisedKeepAliveTime)	\
    (This)->lpVtbl -> SetKeepAlive(This,dwKeepAliveTime,pdwRevisedKeepAliveTime)

#define IOPCGroupStateMgt2_GetKeepAlive(This,pdwKeepAliveTime)	\
    (This)->lpVtbl -> GetKeepAlive(This,pdwKeepAliveTime)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCGroupStateMgt2_SetKeepAlive_Proxy( 
    IOPCGroupStateMgt2 * This,
    /* [in] */ DWORD dwKeepAliveTime,
    /* [out] */ DWORD *pdwRevisedKeepAliveTime);


void __RPC_STUB IOPCGroupStateMgt2_SetKeepAlive_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCGroupStateMgt2_GetKeepAlive_Proxy( 
    IOPCGroupStateMgt2 * This,
    /* [out] */ DWORD *pdwKeepAliveTime);


void __RPC_STUB IOPCGroupStateMgt2_GetKeepAlive_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCGroupStateMgt2_INTERFACE_DEFINED__ */



#ifndef __OPCDA_LIBRARY_DEFINED__
#define __OPCDA_LIBRARY_DEFINED__

/* library OPCDA */
/* [helpstring][version][uuid] */ 

























EXTERN_C const IID LIBID_OPCDA;


#ifndef __OPCDA_Constants_MODULE_DEFINED__
#define __OPCDA_Constants_MODULE_DEFINED__


/* module OPCDA_Constants */


const LPCWSTR OPC_CATEGORY_DESCRIPTION_DA10	=	L"OPC Data Access Servers Version 1.0";

const LPCWSTR OPC_CATEGORY_DESCRIPTION_DA20	=	L"OPC Data Access Servers Version 2.0";

const LPCWSTR OPC_CATEGORY_DESCRIPTION_DA30	=	L"OPC Data Access Servers Version 3.0";

const LPCWSTR OPC_CATEGORY_DESCRIPTION_XMLDA10	=	L"OPC XML Data Access Servers Version 1.0";

const DWORD OPC_READABLE	=	0x1;

const DWORD OPC_WRITEABLE	=	0x2;

const DWORD OPC_BROWSE_HASCHILDREN	=	0x1;

const DWORD OPC_BROWSE_ISITEM	=	0x2;

const LPCWSTR OPC_TYPE_SYSTEM_OPCBINARY	=	L"OPCBinary";

const LPCWSTR OPC_TYPE_SYSTEM_XMLSCHEMA	=	L"XMLSchema";

const LPCWSTR OPC_CONSISTENCY_WINDOW_UNKNOWN	=	L"Unknown";

const LPCWSTR OPC_CONSISTENCY_WINDOW_NOT_CONSISTENT	=	L"Not Consistent";

const LPCWSTR OPC_WRITE_BEHAVIOR_BEST_EFFORT	=	L"Best Effort";

const LPCWSTR OPC_WRITE_BEHAVIOR_ALL_OR_NOTHING	=	L"All or Nothing";

#endif /* __OPCDA_Constants_MODULE_DEFINED__ */


#ifndef __OPCDA_Qualities_MODULE_DEFINED__
#define __OPCDA_Qualities_MODULE_DEFINED__


/* module OPCDA_Qualities */


const WORD OPC_QUALITY_MASK	=	0xc0;

const WORD OPC_STATUS_MASK	=	0xfc;

const WORD OPC_LIMIT_MASK	=	0x3;

const WORD OPC_QUALITY_BAD	=	0;

const WORD OPC_QUALITY_UNCERTAIN	=	0x40;

const WORD OPC_QUALITY_GOOD	=	0xc0;

const WORD OPC_QUALITY_CONFIG_ERROR	=	0x4;

const WORD OPC_QUALITY_NOT_CONNECTED	=	0x8;

const WORD OPC_QUALITY_DEVICE_FAILURE	=	0xc;

const WORD OPC_QUALITY_SENSOR_FAILURE	=	0x10;

const WORD OPC_QUALITY_LAST_KNOWN	=	0x14;

const WORD OPC_QUALITY_COMM_FAILURE	=	0x18;

const WORD OPC_QUALITY_OUT_OF_SERVICE	=	0x1c;

const WORD OPC_QUALITY_WAITING_FOR_INITIAL_DATA	=	0x20;

const WORD OPC_QUALITY_LAST_USABLE	=	0x44;

const WORD OPC_QUALITY_SENSOR_CAL	=	0x50;

const WORD OPC_QUALITY_EGU_EXCEEDED	=	0x54;

const WORD OPC_QUALITY_SUB_NORMAL	=	0x58;

const WORD OPC_QUALITY_LOCAL_OVERRIDE	=	0xd8;

const WORD OPC_LIMIT_OK	=	0;

const WORD OPC_LIMIT_LOW	=	0x1;

const WORD OPC_LIMIT_HIGH	=	0x2;

const WORD OPC_LIMIT_CONST	=	0x3;

#endif /* __OPCDA_Qualities_MODULE_DEFINED__ */


#ifndef __OPCDA_Properties_MODULE_DEFINED__
#define __OPCDA_Properties_MODULE_DEFINED__


/* module OPCDA_Properties */


const DWORD OPC_PROPERTY_DATATYPE	=	1;

const DWORD OPC_PROPERTY_VALUE	=	2;

const DWORD OPC_PROPERTY_QUALITY	=	3;

const DWORD OPC_PROPERTY_TIMESTAMP	=	4;

const DWORD OPC_PROPERTY_ACCESS_RIGHTS	=	5;

const DWORD OPC_PROPERTY_SCAN_RATE	=	6;

const DWORD OPC_PROPERTY_EU_TYPE	=	7;

const DWORD OPC_PROPERTY_EU_INFO	=	8;

const DWORD OPC_PROPERTY_EU_UNITS	=	100;

const DWORD OPC_PROPERTY_DESCRIPTION	=	101;

const DWORD OPC_PROPERTY_HIGH_EU	=	102;

const DWORD OPC_PROPERTY_LOW_EU	=	103;

const DWORD OPC_PROPERTY_HIGH_IR	=	104;

const DWORD OPC_PROPERTY_LOW_IR	=	105;

const DWORD OPC_PROPERTY_CLOSE_LABEL	=	106;

const DWORD OPC_PROPERTY_OPEN_LABEL	=	107;

const DWORD OPC_PROPERTY_TIMEZONE	=	108;

const DWORD OPC_PROPERTY_CONDITION_STATUS	=	300;

const DWORD OPC_PROPERTY_ALARM_QUICK_HELP	=	301;

const DWORD OPC_PROPERTY_ALARM_AREA_LIST	=	302;

const DWORD OPC_PROPERTY_PRIMARY_ALARM_AREA	=	303;

const DWORD OPC_PROPERTY_CONDITION_LOGIC	=	304;

const DWORD OPC_PROPERTY_LIMIT_EXCEEDED	=	305;

const DWORD OPC_PROPERTY_DEADBAND	=	306;

const DWORD OPC_PROPERTY_HIHI_LIMIT	=	307;

const DWORD OPC_PROPERTY_HI_LIMIT	=	308;

const DWORD OPC_PROPERTY_LO_LIMIT	=	309;

const DWORD OPC_PROPERTY_LOLO_LIMIT	=	310;

const DWORD OPC_PROPERTY_CHANGE_RATE_LIMIT	=	311;

const DWORD OPC_PROPERTY_DEVIATION_LIMIT	=	312;

const DWORD OPC_PROPERTY_SOUND_FILE	=	313;

const DWORD OPC_PROPERTY_TYPE_SYSTEM_ID	=	600;

const DWORD OPC_PROPERTY_DICTIONARY_ID	=	601;

const DWORD OPC_PROPERTY_TYPE_ID	=	602;

const DWORD OPC_PROPERTY_DICTIONARY	=	603;

const DWORD OPC_PROPERTY_TYPE_DESCRIPTION	=	604;

const DWORD OPC_PROPERTY_CONSISTENCY_WINDOW	=	605;

const DWORD OPC_PROPERTY_WRITE_BEHAVIOR	=	606;

const DWORD OPC_PROPERTY_UNCONVERTED_ITEM_ID	=	607;

const DWORD OPC_PROPERTY_UNFILTERED_ITEM_ID	=	608;

const DWORD OPC_PROPERTY_DATA_FILTER_VALUE	=	609;

const LPCWSTR OPC_PROPERTY_DESC_DATATYPE	=	L"Item Canonical Data Type";

const LPCWSTR OPC_PROPERTY_DESC_VALUE	=	L"Item Value";

const LPCWSTR OPC_PROPERTY_DESC_QUALITY	=	L"Item Quality";

const LPCWSTR OPC_PROPERTY_DESC_TIMESTAMP	=	L"Item Timestamp";

const LPCWSTR OPC_PROPERTY_DESC_ACCESS_RIGHTS	=	L"Item Access Rights";

const LPCWSTR OPC_PROPERTY_DESC_SCAN_RATE	=	L"Server Scan Rate";

const LPCWSTR OPC_PROPERTY_DESC_EU_TYPE	=	L"Item EU Type";

const LPCWSTR OPC_PROPERTY_DESC_EU_INFO	=	L"Item EU Info";

const LPCWSTR OPC_PROPERTY_DESC_EU_UNITS	=	L"EU Units";

const LPCWSTR OPC_PROPERTY_DESC_DESCRIPTION	=	L"Item Description";

const LPCWSTR OPC_PROPERTY_DESC_HIGH_EU	=	L"High EU";

const LPCWSTR OPC_PROPERTY_DESC_LOW_EU	=	L"Low EU";

const LPCWSTR OPC_PROPERTY_DESC_HIGH_IR	=	L"High Instrument Range";

const LPCWSTR OPC_PROPERTY_DESC_LOW_IR	=	L"Low Instrument Range";

const LPCWSTR OPC_PROPERTY_DESC_CLOSE_LABEL	=	L"Contact Close Label";

const LPCWSTR OPC_PROPERTY_DESC_OPEN_LABEL	=	L"Contact Open Label";

const LPCWSTR OPC_PROPERTY_DESC_TIMEZONE	=	L"Item Timezone";

const LPCWSTR OPC_PROPERTY_DESC_CONDITION_STATUS	=	L"Condition Status";

const LPCWSTR OPC_PROPERTY_DESC_ALARM_QUICK_HELP	=	L"Alarm Quick Help";

const LPCWSTR OPC_PROPERTY_DESC_ALARM_AREA_LIST	=	L"Alarm Area List";

const LPCWSTR OPC_PROPERTY_DESC_PRIMARY_ALARM_AREA	=	L"Primary Alarm Area";

const LPCWSTR OPC_PROPERTY_DESC_CONDITION_LOGIC	=	L"Condition Logic";

const LPCWSTR OPC_PROPERTY_DESC_LIMIT_EXCEEDED	=	L"Limit Exceeded";

const LPCWSTR OPC_PROPERTY_DESC_DEADBAND	=	L"Deadband";

const LPCWSTR OPC_PROPERTY_DESC_HIHI_LIMIT	=	L"HiHi Limit";

const LPCWSTR OPC_PROPERTY_DESC_HI_LIMIT	=	L"Hi Limit";

const LPCWSTR OPC_PROPERTY_DESC_LO_LIMIT	=	L"Lo Limit";

const LPCWSTR OPC_PROPERTY_DESC_LOLO_LIMIT	=	L"LoLo Limit";

const LPCWSTR OPC_PROPERTY_DESC_CHANGE_RATE_LIMIT	=	L"Rate of Change Limit";

const LPCWSTR OPC_PROPERTY_DESC_DEVIATION_LIMIT	=	L"Deviation Limit";

const LPCWSTR OPC_PROPERTY_DESC_SOUND_FILE	=	L"Sound File";

const LPCWSTR OPC_PROPERTY_DESC_TYPE_SYSTEM_ID	=	L"Type System ID";

const LPCWSTR OPC_PROPERTY_DESC_DICTIONARY_ID	=	L"Dictionary ID";

const LPCWSTR OPC_PROPERTY_DESC_TYPE_ID	=	L"Type ID";

const LPCWSTR OPC_PROPERTY_DESC_DICTIONARY	=	L"Dictionary";

const LPCWSTR OPC_PROPERTY_DESC_TYPE_DESCRIPTION	=	L"Type Description";

const LPCWSTR OPC_PROPERTY_DESC_CONSISTENCY_WINDOW	=	L"Consistency Window";

const LPCWSTR OPC_PROPERTY_DESC_WRITE_BEHAVIOR	=	L"Write Behavior";

const LPCWSTR OPC_PROPERTY_DESC_UNCONVERTED_ITEM_ID	=	L"Unconverted Item ID";

const LPCWSTR OPC_PROPERTY_DESC_UNFILTERED_ITEM_ID	=	L"Unfiltered Item ID";

const LPCWSTR OPC_PROPERTY_DESC_DATA_FILTER_VALUE	=	L"Data Filter Value";

#endif /* __OPCDA_Properties_MODULE_DEFINED__ */
#endif /* __OPCDA_LIBRARY_DEFINED__ */

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
