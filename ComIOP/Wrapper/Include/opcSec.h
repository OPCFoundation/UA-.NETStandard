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
/* at Wed Sep 01 11:47:38 2004
 */
/* Compiler settings for .\opcSec.idl:
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

#ifndef __opcSec_h__
#define __opcSec_h__

#if defined(_MSC_VER) && (_MSC_VER >= 1020)
#pragma once
#endif

/* Forward Declarations */ 

#ifndef __IOPCSecurityNT_FWD_DEFINED__
#define __IOPCSecurityNT_FWD_DEFINED__
typedef interface IOPCSecurityNT IOPCSecurityNT;
#endif 	/* __IOPCSecurityNT_FWD_DEFINED__ */


#ifndef __IOPCSecurityPrivate_FWD_DEFINED__
#define __IOPCSecurityPrivate_FWD_DEFINED__
typedef interface IOPCSecurityPrivate IOPCSecurityPrivate;
#endif 	/* __IOPCSecurityPrivate_FWD_DEFINED__ */


#ifndef __IOPCSecurityNT_FWD_DEFINED__
#define __IOPCSecurityNT_FWD_DEFINED__
typedef interface IOPCSecurityNT IOPCSecurityNT;
#endif 	/* __IOPCSecurityNT_FWD_DEFINED__ */


#ifndef __IOPCSecurityPrivate_FWD_DEFINED__
#define __IOPCSecurityPrivate_FWD_DEFINED__
typedef interface IOPCSecurityPrivate IOPCSecurityPrivate;
#endif 	/* __IOPCSecurityPrivate_FWD_DEFINED__ */


/* header files for imported files */
#include "oaidl.h"

#ifdef __cplusplus
extern "C"{
#endif 

void * __RPC_USER MIDL_user_allocate(size_t);
void __RPC_USER MIDL_user_free( void * ); 

#ifndef __IOPCSecurityNT_INTERFACE_DEFINED__
#define __IOPCSecurityNT_INTERFACE_DEFINED__

/* interface IOPCSecurityNT */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCSecurityNT;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("7AA83A01-6C77-11d3-84F9-00008630A38B")
    IOPCSecurityNT : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE IsAvailableNT( 
            /* [out] */ BOOL *pbAvailable) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE QueryMinImpersonationLevel( 
            /* [out] */ DWORD *pdwMinImpLevel) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE ChangeUser( void) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCSecurityNTVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCSecurityNT * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCSecurityNT * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCSecurityNT * This);
        
        HRESULT ( STDMETHODCALLTYPE *IsAvailableNT )( 
            IOPCSecurityNT * This,
            /* [out] */ BOOL *pbAvailable);
        
        HRESULT ( STDMETHODCALLTYPE *QueryMinImpersonationLevel )( 
            IOPCSecurityNT * This,
            /* [out] */ DWORD *pdwMinImpLevel);
        
        HRESULT ( STDMETHODCALLTYPE *ChangeUser )( 
            IOPCSecurityNT * This);
        
        END_INTERFACE
    } IOPCSecurityNTVtbl;

    interface IOPCSecurityNT
    {
        CONST_VTBL struct IOPCSecurityNTVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCSecurityNT_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCSecurityNT_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCSecurityNT_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCSecurityNT_IsAvailableNT(This,pbAvailable)	\
    (This)->lpVtbl -> IsAvailableNT(This,pbAvailable)

#define IOPCSecurityNT_QueryMinImpersonationLevel(This,pdwMinImpLevel)	\
    (This)->lpVtbl -> QueryMinImpersonationLevel(This,pdwMinImpLevel)

#define IOPCSecurityNT_ChangeUser(This)	\
    (This)->lpVtbl -> ChangeUser(This)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCSecurityNT_IsAvailableNT_Proxy( 
    IOPCSecurityNT * This,
    /* [out] */ BOOL *pbAvailable);


void __RPC_STUB IOPCSecurityNT_IsAvailableNT_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCSecurityNT_QueryMinImpersonationLevel_Proxy( 
    IOPCSecurityNT * This,
    /* [out] */ DWORD *pdwMinImpLevel);


void __RPC_STUB IOPCSecurityNT_QueryMinImpersonationLevel_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCSecurityNT_ChangeUser_Proxy( 
    IOPCSecurityNT * This);


void __RPC_STUB IOPCSecurityNT_ChangeUser_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCSecurityNT_INTERFACE_DEFINED__ */


#ifndef __IOPCSecurityPrivate_INTERFACE_DEFINED__
#define __IOPCSecurityPrivate_INTERFACE_DEFINED__

/* interface IOPCSecurityPrivate */
/* [unique][uuid][object] */ 


EXTERN_C const IID IID_IOPCSecurityPrivate;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("7AA83A02-6C77-11d3-84F9-00008630A38B")
    IOPCSecurityPrivate : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE IsAvailablePriv( 
            /* [out] */ BOOL *pbAvailable) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Logon( 
            /* [string][in] */ LPCWSTR szUserID,
            /* [string][in] */ LPCWSTR szPassword) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Logoff( void) = 0;
        
    };
    
#else 	/* C style interface */

    typedef struct IOPCSecurityPrivateVtbl
    {
        BEGIN_INTERFACE
        
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            IOPCSecurityPrivate * This,
            /* [in] */ REFIID riid,
            /* [iid_is][out] */ void **ppvObject);
        
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            IOPCSecurityPrivate * This);
        
        ULONG ( STDMETHODCALLTYPE *Release )( 
            IOPCSecurityPrivate * This);
        
        HRESULT ( STDMETHODCALLTYPE *IsAvailablePriv )( 
            IOPCSecurityPrivate * This,
            /* [out] */ BOOL *pbAvailable);
        
        HRESULT ( STDMETHODCALLTYPE *Logon )( 
            IOPCSecurityPrivate * This,
            /* [string][in] */ LPCWSTR szUserID,
            /* [string][in] */ LPCWSTR szPassword);
        
        HRESULT ( STDMETHODCALLTYPE *Logoff )( 
            IOPCSecurityPrivate * This);
        
        END_INTERFACE
    } IOPCSecurityPrivateVtbl;

    interface IOPCSecurityPrivate
    {
        CONST_VTBL struct IOPCSecurityPrivateVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define IOPCSecurityPrivate_QueryInterface(This,riid,ppvObject)	\
    (This)->lpVtbl -> QueryInterface(This,riid,ppvObject)

#define IOPCSecurityPrivate_AddRef(This)	\
    (This)->lpVtbl -> AddRef(This)

#define IOPCSecurityPrivate_Release(This)	\
    (This)->lpVtbl -> Release(This)


#define IOPCSecurityPrivate_IsAvailablePriv(This,pbAvailable)	\
    (This)->lpVtbl -> IsAvailablePriv(This,pbAvailable)

#define IOPCSecurityPrivate_Logon(This,szUserID,szPassword)	\
    (This)->lpVtbl -> Logon(This,szUserID,szPassword)

#define IOPCSecurityPrivate_Logoff(This)	\
    (This)->lpVtbl -> Logoff(This)

#endif /* COBJMACROS */


#endif 	/* C style interface */



HRESULT STDMETHODCALLTYPE IOPCSecurityPrivate_IsAvailablePriv_Proxy( 
    IOPCSecurityPrivate * This,
    /* [out] */ BOOL *pbAvailable);


void __RPC_STUB IOPCSecurityPrivate_IsAvailablePriv_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCSecurityPrivate_Logon_Proxy( 
    IOPCSecurityPrivate * This,
    /* [string][in] */ LPCWSTR szUserID,
    /* [string][in] */ LPCWSTR szPassword);


void __RPC_STUB IOPCSecurityPrivate_Logon_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);


HRESULT STDMETHODCALLTYPE IOPCSecurityPrivate_Logoff_Proxy( 
    IOPCSecurityPrivate * This);


void __RPC_STUB IOPCSecurityPrivate_Logoff_Stub(
    IRpcStubBuffer *This,
    IRpcChannelBuffer *_pRpcChannelBuffer,
    PRPC_MESSAGE _pRpcMessage,
    DWORD *_pdwStubPhase);



#endif 	/* __IOPCSecurityPrivate_INTERFACE_DEFINED__ */



#ifndef __OPCSEC_LIBRARY_DEFINED__
#define __OPCSEC_LIBRARY_DEFINED__

/* library OPCSEC */
/* [helpstring][version][uuid] */ 




EXTERN_C const IID LIBID_OPCSEC;
#endif /* __OPCSEC_LIBRARY_DEFINED__ */

/* Additional Prototypes for ALL interfaces */

/* end of Additional Prototypes */

#ifdef __cplusplus
}
#endif

#endif
