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

#ifndef _COpcUaAeProxyServer_H_
#define _COpcUaAeProxyServer_H_


#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcUaComProxyServer.h"

class COpcUaAeProxySubscription;
class COpcUaAeProxyBrowser;

using namespace Opc::Ua::Com::Server;
using namespace Opc::Ua::Com::Server::Ae;

/////////////////////////////////////////////////////////////////////////////
// COPCEventServer
class COpcUaAeProxyServer : 
	public COpcComObject,
    public COpcCPContainer,
	public IOPCEventServer2,
	public IOPCCommon,
	public IOPCShutdown,
	public COpcSynchObject
{
private:
	OPC_CLASS_NEW_DELETE()

	OPC_BEGIN_INTERFACE_TABLE(COpcUaAeProxyServer)
        OPC_INTERFACE_ENTRY(IOPCCommon)
		OPC_INTERFACE_ENTRY(IConnectionPointContainer)
		OPC_INTERFACE_ENTRY(IOPCEventServer)
//		OPC_INTERFACE_ENTRY(IOPCEventServer2)
	OPC_END_INTERFACE_TABLE()

public:
	//=========================================================================
	// Operators

	// Constructor
	COpcUaAeProxyServer();

	// Destructor 
	~COpcUaAeProxyServer();

	//=========================================================================
	// Public Methods

	// FinalConstruct
	virtual HRESULT FinalConstruct();

	// FinalRelease
	virtual bool FinalRelease();

	// WrapSubscription
	HRESULT WrapSubscription(REFIID riid, IUnknown** ippUnknown);

	// WrapBrowser
	HRESULT WrapBrowser(REFIID riid, IUnknown** ippUnknown);

	// OnAdvise
	virtual void OnAdvise(REFIID riid, DWORD dwCookie);

	// OnUnadvise
	virtual void OnUnadvise(REFIID riid, DWORD dwCookie);

	//=========================================================================
	// IOPCShutdown

	// ShutdownRequest
	STDMETHODIMP ShutdownRequest(
		LPCWSTR szReason
		);

	//==========================================================================
	// IOPCCommon

	// SetLocaleID
	STDMETHODIMP SetLocaleID(LCID dwLcid);

	// GetLocaleID
	STDMETHODIMP GetLocaleID(LCID *pdwLcid);

	// QueryAvailableLocaleIDs
	STDMETHODIMP QueryAvailableLocaleIDs(DWORD* pdwCount, LCID** pdwLcid);

	// GetErrorString
	STDMETHODIMP GetErrorString(HRESULT dwError, LPWSTR* ppString);

	// SetClientName
	STDMETHODIMP SetClientName(LPCWSTR szName);

	//==========================================================================
	// IOPCEventServer

	STDMETHODIMP GetStatus( 
		/* [out] */ OPCEVENTSERVERSTATUS __RPC_FAR **ppEventServerStatus);

	STDMETHODIMP CreateEventSubscription( 
		/* [in] */ BOOL bActive,
		/* [in] */ DWORD dwBufferTime,
		/* [in] */ DWORD dwMaxSize,
		/* [in] */ OPCHANDLE hClientSubscription,
		/* [in] */ REFIID riid,
		/* [iid_is][out] */ LPUNKNOWN __RPC_FAR *ppUnk,
		/* [out] */ DWORD __RPC_FAR *pdwRevisedBufferTime,
		/* [out] */ DWORD __RPC_FAR *pdwRevisedMaxSize);

	STDMETHODIMP QueryAvailableFilters( 
		/* [out] */ DWORD __RPC_FAR *pdwFilterMask);

	STDMETHODIMP QueryEventCategories( 
		/* [in] */ DWORD dwEventType,
		/* [out] */ DWORD __RPC_FAR *pdwCount,
		/* [size_is][size_is][out] */ DWORD __RPC_FAR *__RPC_FAR *ppdwEventCategories,
		/* [size_is][size_is][out] */ LPWSTR __RPC_FAR *__RPC_FAR *ppszEventCategoryDescs);

	STDMETHODIMP QueryConditionNames( 
		/* [in] */ DWORD dwEventCategory,
		/* [out] */ DWORD __RPC_FAR *pdwCount,
		/* [size_is][size_is][out] */ LPWSTR __RPC_FAR *__RPC_FAR *ppszConditionNames);

	STDMETHODIMP QuerySubConditionNames( 
		/* [in] */ LPWSTR szConditionName,
		/* [out] */ DWORD __RPC_FAR *pdwCount,
		/* [size_is][size_is][out] */ LPWSTR __RPC_FAR *__RPC_FAR *ppszSubConditionNames);

	STDMETHODIMP QuerySourceConditions( 
		/* [in] */ LPWSTR szSource,
		/* [out] */ DWORD __RPC_FAR *pdwCount,
		/* [size_is][size_is][out] */ LPWSTR __RPC_FAR *__RPC_FAR *ppszConditionNames);

	STDMETHODIMP QueryEventAttributes( 
		/* [in] */ DWORD dwEventCategory,
		/* [out] */ DWORD __RPC_FAR *pdwCount,
		/* [size_is][size_is][out] */ DWORD __RPC_FAR *__RPC_FAR *ppdwAttrIDs,
		/* [size_is][size_is][out] */ LPWSTR __RPC_FAR *__RPC_FAR *ppszAttrDescs,
		/* [size_is][size_is][out] */ VARTYPE __RPC_FAR *__RPC_FAR *ppvtAttrTypes);

	STDMETHODIMP TranslateToItemIDs( 
		/* [in] */ LPWSTR szSource,
		/* [in] */ DWORD dwEventCategory,
		/* [in] */ LPWSTR szConditionName,
		/* [in] */ LPWSTR szSubconditionName,
		/* [in] */ DWORD dwCount,
		/* [size_is][in] */ DWORD __RPC_FAR *pdwAssocAttrIDs,
		/* [size_is][size_is][out] */ LPWSTR __RPC_FAR *__RPC_FAR *ppszAttrItemIDs,
		/* [size_is][size_is][out] */ LPWSTR __RPC_FAR *__RPC_FAR *ppszNodeNames,
		/* [size_is][out] */ CLSID __RPC_FAR **ppCLSIDs);

	STDMETHODIMP GetConditionState( 
		/* [in] */ LPWSTR szSource,
		/* [in] */ LPWSTR szConditionName,
		/* [in] */ DWORD dwNumEventAttrs,
		/* [size_is][in] */ DWORD __RPC_FAR *dwAttributeIDs,
		/* [out] */ OPCCONDITIONSTATE __RPC_FAR *__RPC_FAR *ppConditionState);

	STDMETHODIMP EnableConditionByArea( 
		/* [in] */ DWORD dwNumAreas,
		/* [size_is][in] */ LPWSTR __RPC_FAR *pszAreas);

	STDMETHODIMP EnableConditionBySource( 
		/* [in] */ DWORD dwNumSources,
		/* [size_is][in] */ LPWSTR __RPC_FAR *pszSources);

	STDMETHODIMP DisableConditionByArea( 
		/* [in] */ DWORD dwNumAreas,
		/* [size_is][in] */ LPWSTR __RPC_FAR *pszAreas);

	STDMETHODIMP DisableConditionBySource( 
		/* [in] */ DWORD dwNumSources,
		/* [size_is][in] */ LPWSTR __RPC_FAR *pszSources);

	STDMETHODIMP AckCondition( 
		/* [in] */ DWORD dwCount,
		/* [string][in] */ LPWSTR szAcknowledgerID,
		/* [string][in] */ LPWSTR szComment,
		/* [size_is][in] */ LPWSTR __RPC_FAR *pszSource,
		/* [size_is][in] */ LPWSTR __RPC_FAR *pszConditionName,
		/* [size_is][in] */ FILETIME __RPC_FAR *pftActiveTime,
		/* [size_is][in] */ DWORD __RPC_FAR *pdwCookie,
		/* [size_is][out] */ HRESULT __RPC_FAR *__RPC_FAR *ppErrors);

	STDMETHODIMP CreateAreaBrowser( 
		/* [in] */ REFIID riid,
		/* [iid_is][out] */ LPUNKNOWN __RPC_FAR *ppUnk);


	//==========================================================================
	// IOPCEventServer2

	STDMETHODIMP EnableConditionByArea2( 
		/* [in] */ DWORD dwNumAreas,
		/* [size_is][string][in] */ LPWSTR __RPC_FAR *pszAreas,
		/* [size_is][size_is][out] */ HRESULT __RPC_FAR *__RPC_FAR *ppErrors);

	STDMETHODIMP EnableConditionBySource2( 
		/* [in] */ DWORD dwNumSources,
		/* [size_is][string][in] */ LPWSTR __RPC_FAR *pszSources,
		/* [size_is][size_is][out] */ HRESULT __RPC_FAR *__RPC_FAR *ppErrors);

	STDMETHODIMP DisableConditionByArea2( 
		/* [in] */ DWORD dwNumAreas,
		/* [size_is][string][in] */ LPWSTR __RPC_FAR *pszAreas,
		/* [size_is][size_is][out] */ HRESULT __RPC_FAR *__RPC_FAR *ppErrors);

	STDMETHODIMP DisableConditionBySource2( 
		/* [in] */ DWORD dwNumSources,
		/* [size_is][string][in] */ LPWSTR __RPC_FAR *pszSources,
		/* [size_is][size_is][out] */ HRESULT __RPC_FAR *__RPC_FAR *ppErrors);

	STDMETHODIMP GetEnableStateByArea( 
		/* [in] */ DWORD dwNumAreas,
		/* [size_is][string][in] */ LPWSTR __RPC_FAR *pszAreas,
		/* [size_is][size_is][out] */ BOOL __RPC_FAR *__RPC_FAR *pbEnabled,
		/* [size_is][size_is][out] */ BOOL __RPC_FAR *__RPC_FAR *pbEffectivelyEnabled,
		/* [size_is][size_is][out] */ HRESULT __RPC_FAR *__RPC_FAR *ppErrors);

	STDMETHODIMP GetEnableStateBySource( 
		/* [in] */ DWORD dwNumSources,
		/* [size_is][string][in] */ LPWSTR __RPC_FAR *pszSources,
		/* [size_is][size_is][out] */ BOOL __RPC_FAR *__RPC_FAR *pbEnabled,
		/* [size_is][size_is][out] */ BOOL __RPC_FAR *__RPC_FAR *pbEffectivelyEnabled,
		/* [size_is][size_is][out] */ HRESULT __RPC_FAR *__RPC_FAR *ppErrors);


private:

	// GetInnerServer
	ComAeProxy^ GetInnerServer();

	//==========================================================================
	// Private Members

	IUnknown* m_ipUnknown;
	void* m_pInnerServer;
	DWORD m_dwConnection;
	COpcList<COpcUaAeProxySubscription*> m_cSubscriptions;
	COpcList<COpcUaAeProxyBrowser*> m_cBrowsers;

};

//============================================================================
// MACRO:   RETURN_SFALSE
// PURPOSE: Returns SFALSE if any elements in the array are not S_OK

#define RETURN_SFALSE(xResult, xCount, xErrors) \
if ((xResult) == S_OK && (*(xErrors)) != NULL)\
{ \
	for (DWORD xx = 0; xx < (DWORD)(xCount); xx++) \
	{ \
		if ((*(xErrors))[xx] != S_OK) \
		{ \
			return S_FALSE; \
		} \
	} \
} \
return xResult;

#endif // _COpcUaAeProxyServer_H_
