

/* this ALWAYS GENERATED file contains the definitions for the interfaces */


 /* File created by MIDL compiler version 8.00.0603 */
/* at Mon Dec 12 11:24:10 2016
 */
/* Compiler settings for OpcUaComProxyServer.idl:
    Oicf, W1, Zp8, env=Win32 (32b run), target_arch=X86 8.00.0603 
    protocol : dce , ms_ext, c_ext, robust
    error checks: allocation ref bounds_check enum stub_data 
    VC __declspec() decoration level: 
         __declspec(uuid()), __declspec(selectany), __declspec(novtable)
         DECLSPEC_UUID(), MIDL_INTERFACE()
*/
/* @@MIDL_FILE_HEADING(  ) */

#pragma warning( disable: 4049 )  /* more than 64k source lines */


/* verify that the <rpcndr.h> version is high enough to compile this file*/
#ifndef __REQUIRED_RPCNDR_H_VERSION__
#define __REQUIRED_RPCNDR_H_VERSION__ 475
#endif

#include "rpc.h"
#include "rpcndr.h"

#ifndef __RPCNDR_H_VERSION__
#error this stub requires an updated version of <rpcndr.h>
#endif // __RPCNDR_H_VERSION__


#ifndef __OpcUaComProxyServer_h__
#define __OpcUaComProxyServer_h__

#if defined(_MSC_VER) && (_MSC_VER >= 1020)
#pragma once
#endif

/* Forward Declarations */ 

#ifndef __ComDaProxyServer_FWD_DEFINED__
#define __ComDaProxyServer_FWD_DEFINED__

#ifdef __cplusplus
typedef class ComDaProxyServer ComDaProxyServer;
#else
typedef struct ComDaProxyServer ComDaProxyServer;
#endif /* __cplusplus */

#endif 	/* __ComDaProxyServer_FWD_DEFINED__ */


#ifndef __ComAeProxyServer_FWD_DEFINED__
#define __ComAeProxyServer_FWD_DEFINED__

#ifdef __cplusplus
typedef class ComAeProxyServer ComAeProxyServer;
#else
typedef struct ComAeProxyServer ComAeProxyServer;
#endif /* __cplusplus */

#endif 	/* __ComAeProxyServer_FWD_DEFINED__ */


#ifndef __ComAe2ProxyServer_FWD_DEFINED__
#define __ComAe2ProxyServer_FWD_DEFINED__

#ifdef __cplusplus
typedef class ComAe2ProxyServer ComAe2ProxyServer;
#else
typedef struct ComAe2ProxyServer ComAe2ProxyServer;
#endif /* __cplusplus */

#endif 	/* __ComAe2ProxyServer_FWD_DEFINED__ */


#ifndef __ComHdaProxyServer_FWD_DEFINED__
#define __ComHdaProxyServer_FWD_DEFINED__

#ifdef __cplusplus
typedef class ComHdaProxyServer ComHdaProxyServer;
#else
typedef struct ComHdaProxyServer ComHdaProxyServer;
#endif /* __cplusplus */

#endif 	/* __ComHdaProxyServer_FWD_DEFINED__ */


/* header files for imported files */
#include "opccomn.h"
#include "opcda.h"
#include "opc_ae.h"
#include "opchda.h"

#ifdef __cplusplus
extern "C"{
#endif 



#ifndef __OpcUaComProxyServerLib_LIBRARY_DEFINED__
#define __OpcUaComProxyServerLib_LIBRARY_DEFINED__

/* library OpcUaComProxyServerLib */
/* [version][uuid] */ 


EXTERN_C const IID LIBID_OpcUaComProxyServerLib;

EXTERN_C const CLSID CLSID_ComDaProxyServer;

#ifdef __cplusplus

class DECLSPEC_UUID("B25384BD-D0DD-4d4d-805C-6E9F309F27C1")
ComDaProxyServer;
#endif

EXTERN_C const CLSID CLSID_ComAeProxyServer;

#ifdef __cplusplus

class DECLSPEC_UUID("4DF1784D-085A-403d-AF8A-B140639B10B3")
ComAeProxyServer;
#endif

EXTERN_C const CLSID CLSID_ComAe2ProxyServer;

#ifdef __cplusplus

class DECLSPEC_UUID("4DF1784C-085A-403d-AF8A-B140639B10B3")
ComAe2ProxyServer;
#endif

EXTERN_C const CLSID CLSID_ComHdaProxyServer;

#ifdef __cplusplus

class DECLSPEC_UUID("2DA58B69-2D85-4de0-A934-7751322132E2")
ComHdaProxyServer;
#endif
#endif /* __OpcUaComProxyServerLib_LIBRARY_DEFINED__ */

/* Additional Prototypes for ALL interfaces */

/* end of Additional Prototypes */

#ifdef __cplusplus
}
#endif

#endif


