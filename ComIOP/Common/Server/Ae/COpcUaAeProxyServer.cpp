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

#include "StdAfx.h"

#include "OpcUaComProxyServer.h"

#include "COpcUaAeProxyServer.h"
#include "COpcUaAeProxySubscription.h"
#include "COpcUaAeProxyBrowser.h"
#include "COpcUaProxyUtils.h"

using namespace System;
using namespace System::Text;
using namespace System::Runtime::InteropServices;
using namespace System::Collections::Generic;
using namespace Opc::Ua;
using namespace Opc::Ua::Com;
using namespace Opc::Ua::Com::Server;

//==============================================================================
// Static Functions
/// <summary>
/// Writes a trace message.
/// </summary>
static void TraceState(String^ context, ... array<Object^>^ args)
{
    #ifdef TRACESTATE
	COpcUaProxyUtils::TraceState("COpcUaAeProxyServer", context, args);
	#endif
}

//============================================================================
// COpcUaDaProxyServer

// Constructor
COpcUaAeProxyServer::COpcUaAeProxyServer()
{
	TraceState("COpcUaAeProxyServer");

	m_pInnerServer  = NULL;
	m_ipUnknown     = NULL;
	m_dwConnection  = NULL;

    try
    {
		ComAeProxy^ server = gcnew ComAeProxy();
	    GCHandle hInnerServer = GCHandle::Alloc(server);
	    m_pInnerServer = ((IntPtr)hInnerServer).ToPointer();
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "COpcUaAeProxyServer: Unexpected error creating AE proxy.");
    }
}

// Destructor
COpcUaAeProxyServer::~COpcUaAeProxyServer()
{
	TraceState("~COpcUaAeProxyServer");

	Marshal::Release((IntPtr)m_ipUnknown);

	if (m_pInnerServer != NULL)
	{
		GCHandle hInnerServer = (GCHandle)IntPtr(m_pInnerServer);
		hInnerServer.Free();
		m_pInnerServer = NULL;
	}
}

// FinalConstruct
HRESULT COpcUaAeProxyServer::FinalConstruct()
{
	TraceState("FinalConstruct");

	COpcLock cLock(*this);

	HRESULT hResult = S_OK;

	ApplicationConfiguration^ configuration = nullptr;

	// load configuration.
	if (!COpcUaProxyUtils::Initialize(configuration))
	{
		return E_FAIL;
	}

	// get the CLSID being used.
	CLSID cClsid = GetCLSID();
	Guid clsid = (Guid)Marshal::PtrToStructure((IntPtr)&cClsid, Guid::typeid);

	// load the server.
    ComAeProxy^ server = nullptr;

    try
    {
	    server = GetInnerServer();
	    server->Load(clsid, configuration);
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "COpcUaAeProxyServer: Unexpected error loading proxy for CLSID={0}.", clsid);
    }

	try
	{
        // register shutdown interface.
        RegisterInterface(IID_IOPCShutdown);

        // get the interface pointer.
		m_ipUnknown = (IUnknown*)Marshal::GetComInterfaceForObject(server, OpcRcw::Ae::IOPCEventServer2::typeid).ToPointer();
	}
	catch (Exception^ e)
	{
        Utils::Trace(e, "COpcUaAeProxyServer: Unexpected getting COM interface for AE proxy.", clsid);
	}

    return S_OK;
}

// FinalRelease
bool COpcUaAeProxyServer::FinalRelease()
{
	TraceState("FinalRelease");

    try
    {
	    COpcLock cLock(*this);

	    ComAeProxy^ server = GetInnerServer();
	    server->Unload();

	    cLock.Unlock();
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "COpcUaAeProxyServer: Unexpected error unloading proxy.");
    }

	// decrement global reference count.
	COpcUaProxyUtils::Uninitialize();

	return true;
}

// GetInnerServer
ComAeProxy^ COpcUaAeProxyServer::GetInnerServer()
{
	if (m_pInnerServer == NULL)
	{
		return nullptr;
	}

	GCHandle hInnerServer = (GCHandle)IntPtr(m_pInnerServer);

	if (hInnerServer.IsAllocated)
	{
		return (ComAeProxy^)hInnerServer.Target;
	}

	return nullptr;
}

// OnAdvise
void COpcUaAeProxyServer::OnAdvise(REFIID riid,
								   DWORD dwCookie)
{
	COpcLock cLock(*this);

	if (riid == IID_IOPCShutdown)
	{
		if (FAILED(OpcConnect(m_ipUnknown, (IOPCShutdown*)this, riid, &m_dwConnection)))
		{
			m_dwConnection = NULL;
		}
	}
}

// OnUnadvise
void COpcUaAeProxyServer::OnUnadvise(REFIID riid,
									 DWORD dwCookie)
{
	if (riid == IID_IOPCShutdown)
	{
		OpcDisconnect(m_ipUnknown, riid, m_dwConnection);
		m_dwConnection = NULL;
	}
}

//==============================================================================
// IOPCCommon

// SetLocaleID
HRESULT COpcUaAeProxyServer::SetLocaleID(LCID dwLcid)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}
	
	// fetch required interface.
	IOPCCommon* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCCommon, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->SetLocaleID(
		dwLcid
		);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// GetLocaleID
HRESULT COpcUaAeProxyServer::GetLocaleID(LCID *pdwLcid)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCCommon* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCCommon, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->GetLocaleID(
		pdwLcid
		);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// QueryAvailableLocaleIDs
HRESULT COpcUaAeProxyServer::QueryAvailableLocaleIDs(DWORD* pdwCount,
													 LCID** pdwLcid)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCCommon* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCCommon, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->QueryAvailableLocaleIDs(
		pdwCount,
		pdwLcid
		);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// GetErrorString
HRESULT COpcUaAeProxyServer::GetErrorString(HRESULT dwError,
											LPWSTR* ppString)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCCommon* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCCommon, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->GetErrorString(
		dwError,
		ppString
		);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// SetClientName
HRESULT COpcUaAeProxyServer::SetClientName(LPCWSTR szName)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCCommon* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCCommon,
		(void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->SetClientName(
		szName
		);

	// release interface.
	ipInterface->Release();

	return hResult;
}


// WrapSubscription
HRESULT COpcUaAeProxyServer::WrapSubscription(REFIID riid,
											  IUnknown** ippUnknown)
{
	// wrap subscription.
	COpcUaAeProxySubscription* pSubscription = new COpcUaAeProxySubscription(this, *ippUnknown);

	// release reference.
	(*ippUnknown)->Release();
	*ippUnknown = NULL;

	// query for desired interface,
	HRESULT hResult = pSubscription->QueryInterface(riid, (void**)ippUnknown);

	if (FAILED(hResult))
	{
		pSubscription->Release();
		return hResult;
	}

	// save reference to subscription locally.
	COpcLock cLock(*this);
	m_cSubscriptions.AddTail(pSubscription);
	cLock.Unlock();

	return S_OK;
}

// WrapBrowser
HRESULT COpcUaAeProxyServer::WrapBrowser(REFIID riid,
										 IUnknown** ippUnknown)
{
	// wrap subscription.
	COpcUaAeProxyBrowser* pBrowser = new COpcUaAeProxyBrowser(this, *ippUnknown);

	// release reference.
	(*ippUnknown)->Release();
	*ippUnknown = NULL;

	// query for desired interface,
	HRESULT hResult = pBrowser->QueryInterface(riid, (void**)ippUnknown);

	if (FAILED(hResult))
	{
		pBrowser->Release();
		return hResult;
	}

	// save reference to subscription locally.
	COpcLock cLock(*this);
	m_cBrowsers.AddTail(pBrowser);
	cLock.Unlock();

	return S_OK;
}


//============================================================================
// IOPCEventServer


// GetStatus
HRESULT COpcUaAeProxyServer::GetStatus(OPCEVENTSERVERSTATUS **ppEventServerStatus)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->GetStatus(
		ppEventServerStatus
		);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// CreateEventSubscription
HRESULT COpcUaAeProxyServer::CreateEventSubscription(BOOL bActive,
													 DWORD dwBufferTime,
													 DWORD dwMaxSize,
													 OPCHANDLE hClientSubscription,
													 REFIID riid,
													 LPUNKNOWN *ppUnk,
													 DWORD *pdwRevisedBufferTime,
													 DWORD *pdwRevisedMaxSize)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->CreateEventSubscription(
		bActive,
		dwBufferTime,
		dwMaxSize,
		hClientSubscription,
		riid,
		ppUnk,
		pdwRevisedBufferTime,
		pdwRevisedMaxSize
		);

	// release interface.
	ipInterface->Release();
	ipInterface = NULL;

	// wrap subscription object.
	if (SUCCEEDED(hResult))
	{
		hResult = WrapSubscription(riid, ppUnk);

		if (FAILED(hResult))
		{
			// release unknown.
			if ((*ppUnk) != NULL)
			{
				(*ppUnk)->Release();
				*ppUnk = NULL;
			}

			// return failure.
			return hResult;
		}
	}

	// insert necessary success code.
	if (hResult == S_OK)
	{
		if(*pdwRevisedBufferTime != dwBufferTime)
				hResult = OPC_S_INVALIDBUFFERTIME;
		else if(*pdwRevisedMaxSize != dwMaxSize)
				hResult = OPC_S_INVALIDMAXSIZE;
	}

	return hResult;
}

// QueryAvailableFilters
HRESULT COpcUaAeProxyServer::QueryAvailableFilters(DWORD *pdwFilterMask)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->QueryAvailableFilters(pdwFilterMask);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// QueryEventCategories
HRESULT COpcUaAeProxyServer::QueryEventCategories(DWORD dwEventType,
												  DWORD *pdwCount,
												  DWORD **ppdwEventCategories,
												  LPWSTR **ppszEventCategoryDescs)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->QueryEventCategories(dwEventType,
		pdwCount,
		ppdwEventCategories,
		ppszEventCategoryDescs);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// QueryConditionNames
HRESULT COpcUaAeProxyServer::QueryConditionNames(DWORD dwEventCategory,
												 DWORD *pdwCount,
												 LPWSTR **ppszConditionNames)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->QueryConditionNames(dwEventCategory,
		pdwCount,
		ppszConditionNames);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// QuerySubConditionNames
HRESULT COpcUaAeProxyServer::QuerySubConditionNames(LPWSTR szConditionName,
													DWORD *pdwCount,
													LPWSTR **ppszSubConditionNames)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->QuerySubConditionNames(szConditionName,
		pdwCount,
		ppszSubConditionNames);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// QuerySourceConditions
HRESULT COpcUaAeProxyServer::QuerySourceConditions(LPWSTR szSource,
												   DWORD *pdwCount,
												   LPWSTR **ppszConditionNames)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->QuerySourceConditions(szSource,
		pdwCount,
		ppszConditionNames);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// QueryEventAttributes
HRESULT COpcUaAeProxyServer::QueryEventAttributes(DWORD dwEventCategory,
												  DWORD *pdwCount,
												  DWORD **ppdwAttrIDs,
												  LPWSTR **ppszAttrDescs,
												  VARTYPE **ppvtAttrTypes)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->QueryEventAttributes(dwEventCategory,
		pdwCount,
		ppdwAttrIDs,
		ppszAttrDescs,
		ppvtAttrTypes);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// TranslateToItemIDs
HRESULT COpcUaAeProxyServer::TranslateToItemIDs(LPWSTR szSource,
												DWORD dwEventCategory,
												LPWSTR szConditionName,
												LPWSTR szSubconditionName,
												DWORD dwCount,
												DWORD *pdwAssocAttrIDs,
												LPWSTR **ppszAttrItemIDs,
												LPWSTR **ppszNodeNames,
												CLSID **ppCLSIDs)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->TranslateToItemIDs(szSource,
		dwEventCategory,
		szConditionName,
		szSubconditionName,
		dwCount,
		pdwAssocAttrIDs,
		ppszAttrItemIDs,
		ppszNodeNames,
		ppCLSIDs);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// GetConditionState
HRESULT COpcUaAeProxyServer::GetConditionState(LPWSTR szSource,
											   LPWSTR szConditionName,
											   DWORD dwNumEventAttrs,
											   DWORD *dwAttributeIDs,
											   OPCCONDITIONSTATE **ppConditionState)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->GetConditionState(szSource,
		szConditionName,
		dwNumEventAttrs,
		dwAttributeIDs,
		ppConditionState);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// EnableConditionByArea
HRESULT COpcUaAeProxyServer::EnableConditionByArea(DWORD dwNumAreas,
												   LPWSTR *pszAreas)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->EnableConditionByArea(dwNumAreas,
		pszAreas);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// EnableConditionBySource
HRESULT COpcUaAeProxyServer::EnableConditionBySource(DWORD dwNumSources,
													 LPWSTR *pszSources)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->EnableConditionBySource(dwNumSources,
		pszSources);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// DisableConditionByArea
HRESULT COpcUaAeProxyServer::DisableConditionByArea(DWORD dwNumAreas,
													LPWSTR *pszAreas)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->DisableConditionByArea(dwNumAreas,
		pszAreas);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// DisableConditionBySource
HRESULT COpcUaAeProxyServer::DisableConditionBySource(DWORD dwNumSources,
													  LPWSTR *pszSources)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->DisableConditionBySource(dwNumSources,
		pszSources);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// AckCondition
HRESULT COpcUaAeProxyServer::AckCondition(DWORD dwCount,
										  LPWSTR szAcknowledgerID,
										  LPWSTR szComment,
										  LPWSTR *pszSource,
										  LPWSTR *pszConditionName,
										  ::FILETIME *pftActiveTime,
										  DWORD *pdwCookie,
										  HRESULT **ppErrors)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->AckCondition(dwCount,
		szAcknowledgerID,
		szComment,
		pszSource,
		pszConditionName,
		pftActiveTime,
		pdwCookie,
		ppErrors);

	// release interface.
	ipInterface->Release();

	RETURN_SFALSE(hResult, dwCount, ppErrors);
}

// CreateAreaBrowser
HRESULT COpcUaAeProxyServer::CreateAreaBrowser(REFIID riid,
											   LPUNKNOWN *ppUnk)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->CreateAreaBrowser(riid, ppUnk);

	// release interface.
	ipInterface->Release();
	ipInterface = NULL;


	// wrap subscription object.
	if (SUCCEEDED(hResult))
	{
		hResult = WrapBrowser(riid, ppUnk);

		if (FAILED(hResult))
		{
			// release unknown.
			if ((*ppUnk) != NULL)
			{
				(*ppUnk)->Release();
				*ppUnk = NULL;
			}

			// return failure.
			return hResult;
		}
	}

	return hResult;
}


//==========================================================================
// IOPCEventServer2


// EnableConditionByArea2
HRESULT COpcUaAeProxyServer::EnableConditionByArea2(DWORD dwNumAreas,
													LPWSTR *pszAreas,
													HRESULT **ppErrors)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer2* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer2, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->EnableConditionByArea2(dwNumAreas,
		pszAreas,
		ppErrors);

	// release interface.
	ipInterface->Release();

	RETURN_SFALSE(hResult, dwNumAreas, ppErrors);
}

// EnableConditionBySource2
HRESULT COpcUaAeProxyServer::EnableConditionBySource2(DWORD dwNumSources,
													  LPWSTR *pszSources,
													  HRESULT **ppErrors)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer2* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer2, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->EnableConditionBySource2(dwNumSources,
		pszSources,
		ppErrors);

	// release interface.
	ipInterface->Release();

	RETURN_SFALSE(hResult, dwNumSources, ppErrors);
}

// DisableConditionByArea2
HRESULT COpcUaAeProxyServer::DisableConditionByArea2(DWORD dwNumAreas,
													 LPWSTR *pszAreas,
													 HRESULT **ppErrors)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer2* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer2, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->DisableConditionByArea2(dwNumAreas,
		pszAreas,
		ppErrors);

	// release interface.
	ipInterface->Release();

	RETURN_SFALSE(hResult, dwNumAreas, ppErrors);
}

// DisableConditionBySource2
HRESULT COpcUaAeProxyServer::DisableConditionBySource2(DWORD dwNumSources,
													   LPWSTR *pszSources,
													   HRESULT **ppErrors)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer2* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer2, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->DisableConditionBySource2(dwNumSources,
		pszSources,
		ppErrors);

	// release interface.
	ipInterface->Release();

	RETURN_SFALSE(hResult, dwNumSources, ppErrors);
}

// GetEnableStateByArea
HRESULT COpcUaAeProxyServer::GetEnableStateByArea(DWORD dwNumAreas,
												  LPWSTR *pszAreas,
												  BOOL **pbEnabled,
												  BOOL **pbEffectivelyEnabled,
												  HRESULT **ppErrors)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer2* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer2, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->GetEnableStateByArea(dwNumAreas,
		pszAreas,
		pbEnabled,
		pbEffectivelyEnabled,
		ppErrors);

	// release interface.
	ipInterface->Release();

	RETURN_SFALSE(hResult, dwNumAreas, ppErrors);
}

// GetEnableStateBySource
HRESULT COpcUaAeProxyServer::GetEnableStateBySource(DWORD dwNumSources,
													LPWSTR *pszSources,
													BOOL **pbEnabled,
													BOOL **pbEffectivelyEnabled,
													HRESULT **ppErrors)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventServer2* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventServer2, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	HRESULT hResult = ipInterface->GetEnableStateBySource(dwNumSources,
		pszSources,
		pbEnabled,
		pbEffectivelyEnabled,
		ppErrors);

	// release interface.
	ipInterface->Release();

	RETURN_SFALSE(hResult, dwNumSources, ppErrors);
}


//==============================================================================
// IOPCShutdown

// ShutdownRequest
HRESULT COpcUaAeProxyServer::ShutdownRequest(
	LPCWSTR szReason
	)
{
	// get callback object.
	IOPCShutdown* ipCallback = NULL;

	HRESULT hResult = GetCallback(IID_IOPCShutdown, (IUnknown**)&ipCallback);

	if (FAILED(hResult) || ipCallback == NULL)
	{
		return S_OK;
	}

	// invoke callback.
	ipCallback->ShutdownRequest(szReason);

	// release callback.
	ipCallback->Release();

	return S_OK;
}
