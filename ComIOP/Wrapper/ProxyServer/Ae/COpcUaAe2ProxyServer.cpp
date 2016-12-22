/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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

#include "COpcUaAe2ProxyServer.h"
#include "COpcUaAe2ProxyBrowser.h"
#include "COpcUaAe2ProxySubscription.h"
#include "COpcUaProxyUtils.h"

using namespace System;
using namespace System::Text;
using namespace System::Runtime::InteropServices;
using namespace System::Collections::Generic;
using namespace System::Reflection;
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
	COpcUaProxyUtils::TraceState("COpcUaAe2ProxyServer", context, args);
	#endif
}

//============================================================================
// COpcUaDaProxyServer

// Constructor
COpcUaAe2ProxyServer::COpcUaAe2ProxyServer()
{
	TraceState("COpcUaAe2ProxyServer");

	m_pInnerServer  = NULL;
	m_pClientName   = NULL;

	Version^ version = Assembly::GetExecutingAssembly()->GetName()->Version;

    m_ftStartTime = OpcUtcNow();
	m_wMajorVersion = (WORD)version->Major;
	m_wMinorVersion = (WORD)version->Minor;
	m_wBuildNumber  = (WORD)version->Build;
	m_szVendorInfo  = L"OPC UA COM AE Proxy Server";

    try
    {
	    ComAe2Proxy^ server = gcnew ComAe2Proxy();
	    GCHandle hInnerServer = GCHandle::Alloc(server);
	    m_pInnerServer = ((IntPtr)hInnerServer).ToPointer();
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "COpcUaHdaProxyServer: Unexpected error creating AE proxy.");
    }
}

// Destructor
COpcUaAe2ProxyServer::~COpcUaAe2ProxyServer()
{
	TraceState("~COpcUaAe2ProxyServer");

	if (m_pClientName != NULL)
	{
		CoTaskMemFree(m_pClientName);
		m_pClientName = NULL;
	}

	if (m_pInnerServer != NULL)
	{
		GCHandle hInnerServer = (GCHandle)IntPtr(m_pInnerServer);
		hInnerServer.Free();
		m_pInnerServer = NULL;
	}
}

// FinalConstruct
HRESULT COpcUaAe2ProxyServer::FinalConstruct()
{
	TraceState("FinalConstruct");

	COpcLock cLock(*this);

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
    try
    {
	    ComProxy^ server = GetInnerServer();
	    server->Load(clsid, configuration);
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "COpcUaAeProxyServer: Unexpected error loading proxy for CLSID={0}.", clsid);
    }

	// register callback interfaces.
    RegisterInterface(IID_IOPCShutdown);

    return S_OK;
}

// FinalRelease
bool COpcUaAe2ProxyServer::FinalRelease()
{
	TraceState("FinalRelease");

	COpcLock cLock(*this);

	ComAe2Proxy^ server = GetInnerServer();

    try
    {
	    server->Unload();
		delete server;
		UnregisterInterface(IID_IOPCShutdown);
	    cLock.Unlock();
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "COpcUaDaProxyServer: Unexpected error unloading proxy.");
    }

	// decrement global reference count.
	COpcUaProxyUtils::Uninitialize();

	return true;
}

// GetInnerServer
ComAe2Proxy^ COpcUaAe2ProxyServer::GetInnerServer()
{
	if (m_pInnerServer == NULL)
	{
		return nullptr;
	}

	GCHandle hInnerServer = (GCHandle)IntPtr(m_pInnerServer);

	if (hInnerServer.IsAllocated)
	{
		return (ComAe2Proxy^)hInnerServer.Target;
	}

	return nullptr;
}

//==============================================================================
// IOPCCommon

// SetLocaleID
HRESULT COpcUaAe2ProxyServer::SetLocaleID(LCID dwLcid)
{
	TraceState("IOPCCommon.SetLocaleID");

	try
	{	
		ComAe2Proxy^ server = GetInnerServer();
		server->SetLocaleId(dwLcid);
		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// GetLocaleID
HRESULT COpcUaAe2ProxyServer::GetLocaleID(LCID *pdwLcid)
{
	TraceState("IOPCCommon.GetLocaleID");

	if (pdwLcid == 0)
	{
		return E_INVALIDARG;
	}

	*pdwLcid = 0;

	try
	{	
		ComAe2Proxy^ server = GetInnerServer();
		*pdwLcid = server->GetLocaleId();
		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// QueryAvailableLocaleIDs
HRESULT COpcUaAe2ProxyServer::QueryAvailableLocaleIDs(
	DWORD* pdwCount,
	LCID** pdwLcid)
{
	TraceState("IOPCCommon.QueryAvailableLocaleIDs");

	if (pdwCount == 0 || pdwLcid == 0)
	{
		return E_INVALIDARG;
	}

	*pdwCount = 0;
	*pdwLcid = NULL;

	LCID* pLcids = NULL;

	try
	{	
		ComAe2Proxy^ server = GetInnerServer();
		array<int>^ localeIds = server->GetAvailableLocaleIds();
		OpcProxy_AllocArrayToReturn(pLcids, localeIds->Length, LCID);

		for (int ii = 0; ii < localeIds->Length; ii++)
		{
			pLcids[ii] = localeIds[ii];
		}

		*pdwCount = localeIds->Length;
		*pdwLcid = pLcids;

		return S_OK;
	}
	catch (Exception^ e)
	{
		CoTaskMemFree(pLcids);
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// GetErrorString
HRESULT COpcUaAe2ProxyServer::GetErrorString(
	HRESULT dwError,
	LPWSTR* ppString)
{
	TraceState("IOPCCommon.GetErrorString");

	if (ppString == 0)
	{
		return E_INVALIDARG;
	}

	return COpcCommon::GetErrorString(OPC_MESSAGE_MODULE_NAME_AE, dwError, LOCALE_SYSTEM_DEFAULT, ppString);
}

// SetClientName
HRESULT COpcUaAe2ProxyServer::SetClientName(LPCWSTR szName)
{
	TraceState("IOPCCommon.SetClientName");

	// check arguments.
	if (szName == 0)
	{
		return E_INVALIDARG;
	}

	COpcLock cLock(*this);

	int length = wcslen(szName);

	// allocate memory.
	m_pClientName = (LPWSTR)CoTaskMemAlloc((sizeof(WCHAR)+1)*length);
	
	if (m_pClientName == 0)
	{
		return E_OUTOFMEMORY;
	}

	// copy the string.
	wcscpy_s(m_pClientName, length+1, szName);

	return S_OK;
}

//============================================================================
// IOPCEventServer

// GetStatus
HRESULT COpcUaAe2ProxyServer::GetStatus(OPCEVENTSERVERSTATUS **ppEventServerStatus)
{
	TraceState("IOPCEventServer.GetStatus");

	// check arguments.
	if (ppEventServerStatus == NULL)
	{
		return E_INVALIDARG;
	}

    COpcLock cLock(*this);

    *ppEventServerStatus = (::OPCEVENTSERVERSTATUS*)CoTaskMemAlloc(sizeof(::OPCEVENTSERVERSTATUS));
	memset(*ppEventServerStatus, 0, sizeof(OPCEVENTSERVERSTATUS));

	(*ppEventServerStatus)->ftStartTime = this->m_ftStartTime;
	(*ppEventServerStatus)->ftCurrentTime = OpcUtcNow();
	(*ppEventServerStatus)->dwServerState = OPCAE_STATUS_FAILED;
	(*ppEventServerStatus)->wMajorVersion = this->m_wMajorVersion;
	(*ppEventServerStatus)->wMinorVersion = this->m_wMinorVersion;
	(*ppEventServerStatus)->wBuildNumber = this->m_wBuildNumber;

	// get inner server.
	ComAe2Proxy^ server = GetInnerServer();

	if (server != nullptr)
	{
		(*ppEventServerStatus)->ftLastUpdateTime = COpcUaProxyUtils::GetFILETIME(server->LastUpdateTime);

		if (server->Connected)
		{
			(*ppEventServerStatus)->dwServerState = OPCAE_STATUS_RUNNING;
		}

		ConfiguredEndpoint^ endpoint = server->Endpoint;

		if (endpoint != nullptr)
		{
			(*ppEventServerStatus)->szVendorInfo = (LPWSTR)Marshal::StringToCoTaskMemUni(endpoint->ToString()).ToPointer();
		}
	}

	return S_OK;
}

// CreateEventSubscription
HRESULT COpcUaAe2ProxyServer::CreateEventSubscription(
	BOOL bActive,
	DWORD dwBufferTime,
	DWORD dwMaxSize,
	OPCHANDLE hClientSubscription,
	REFIID riid,
	LPUNKNOWN *ppUnk,
	DWORD *pdwRevisedBufferTime,
	DWORD *pdwRevisedMaxSize)
{
    TraceState("IOPCEventServer.CreateEventSubscription");

    HRESULT hResult = S_OK;

	// check ppUnk.
	if (ppUnk == NULL || pdwRevisedBufferTime == NULL || pdwRevisedMaxSize == NULL)
	{
		return E_INVALIDARG;
	}

	*ppUnk = NULL;
    *pdwRevisedBufferTime = 0;
    *pdwRevisedMaxSize = 0;

	try
	{
        // get the server.
		ComAe2Proxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// create the subscription.
		ComAe2Subscription^ subscription = server->CreateSubscription();

		subscription->Active = (bActive != 0);
		subscription->BufferTime = dwBufferTime;
		subscription->MaxSize = dwMaxSize;
		subscription->ClientHandle = hClientSubscription;

		subscription->ApplyChanges();

		*pdwRevisedBufferTime = (DWORD)subscription->ActualBufferTime;
		*pdwRevisedMaxSize = (DWORD)subscription->ActualMaxSize;

		// wrap the browser.
		COpcUaAe2ProxySubscription* pSubscription = new COpcUaAe2ProxySubscription(this, subscription);

		// fetch required interface.
		if (FAILED(pSubscription->QueryInterface(riid, (void**)ppUnk)))
		{
            pSubscription->Release();
			return E_NOINTERFACE;
		}

        pSubscription->Release();

		if (*pdwRevisedBufferTime != dwBufferTime)
		{
			return OPC_S_INVALIDBUFFERTIME;
		}

		if (*pdwRevisedMaxSize != dwMaxSize)
		{
			return OPC_S_INVALIDMAXSIZE;
		}

		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}
}

// QueryAvailableFilters
HRESULT COpcUaAe2ProxyServer::QueryAvailableFilters(DWORD *pdwFilterMask)
{
    TraceState("IOPCEventServer.QueryAvailableFilters");

	// check arguments.
	if (pdwFilterMask == NULL)
	{
		return E_INVALIDARG;
	}
    
    *pdwFilterMask = OPC_FILTER_BY_EVENT | OPC_FILTER_BY_CATEGORY | OPC_FILTER_BY_SEVERITY | OPC_FILTER_BY_AREA | OPC_FILTER_BY_SOURCE;

	return S_OK;
}

// QueryEventCategories
HRESULT COpcUaAe2ProxyServer::QueryEventCategories(
	DWORD dwEventType,
	DWORD *pdwCount,
	DWORD **ppdwEventCategories,
	LPWSTR **ppszEventCategoryDescs)
{
    TraceState("IOPCEventServer.QueryEventCategories");

	// check arguments.
	if (pdwCount == NULL || ppdwEventCategories == NULL || ppszEventCategoryDescs == NULL)
	{
		return E_INVALIDARG;
	}

	if (dwEventType <= 0 || dwEventType > 7)
	{
		return E_INVALIDARG;
	}
    
    *pdwCount = 0;
	*ppdwEventCategories = NULL;
	*ppszEventCategoryDescs = NULL;

    DWORD dwCount = 0;
    DWORD* pdwEventCategories = NULL;
    LPWSTR* pszEventCategoryDescs = NULL;

	try
	{
		// get inner server.
		ComAe2Proxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

        // fetch categories.
		List<AeEventCategory^>^ values = server->QueryEventCategories(dwEventType);

        if (values == nullptr || values->Count == 0)
        {
            return S_OK;
        }

        dwCount = values->Count;

		// allocate memory for results.
		OpcProxy_AllocArrayToReturn(pdwEventCategories, dwCount, DWORD);
		OpcProxy_AllocArrayToReturn(pszEventCategoryDescs, dwCount, LPWSTR);

	    for (DWORD ii = 0; ii < dwCount; ii++)
	    {
            pdwEventCategories[ii] = values[ii]->LocalId;

            if (values[ii]->Description != nullptr)
            {
                pszEventCategoryDescs[ii] = (LPWSTR)Marshal::StringToCoTaskMemUni(values[ii]->Description).ToPointer();;
            }
	    }

        *pdwCount = dwCount;
		*ppdwEventCategories = pdwEventCategories;
		*ppszEventCategoryDescs = pszEventCategoryDescs;

		return S_OK;
	}
	catch (Exception^ e)
	{
		// free strings on error.
		if (pszEventCategoryDescs != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				CoTaskMemFree(pszEventCategoryDescs[ii]);
			}
		}

		// free allocated results.
		CoTaskMemFree(pdwEventCategories);
		CoTaskMemFree(pszEventCategoryDescs);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// QueryConditionNames
HRESULT COpcUaAe2ProxyServer::QueryConditionNames(
	DWORD dwEventCategory,
	DWORD* pdwCount,
	LPWSTR** ppszConditionNames)
{
    TraceState("IOPCEventServer.QueryConditionNames");
	return E_NOTIMPL;
}

// QuerySubConditionNames
HRESULT COpcUaAe2ProxyServer::QuerySubConditionNames(
	LPWSTR szConditionName,
	DWORD* pdwCount,
	LPWSTR** ppszSubConditionNames)
{
    TraceState("IOPCEventServer.QuerySubConditionNames");
	return E_NOTIMPL;
}

// QuerySourceConditions
HRESULT COpcUaAe2ProxyServer::QuerySourceConditions(
	LPWSTR szSource,
	DWORD *pdwCount,
	LPWSTR **ppszConditionNames)
{
    TraceState("IOPCEventServer.QuerySourceConditions");

	// check arguments.
	if (pdwCount == NULL || ppszConditionNames == NULL)
	{
		return E_INVALIDARG;
	}

	*pdwCount = NULL;
	*ppszConditionNames = NULL;

	try
	{	
		// get inner server.
		ComAe2Proxy^ server = GetInnerServer();

		// check that the source is valid.
		String^ sourceId = Marshal::PtrToStringUni((IntPtr)(void*)szSource);

		if (!server->IsSourceValid(sourceId))
		{
			return E_INVALIDARG;
		}

		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}
}

// QueryEventAttributes
HRESULT COpcUaAe2ProxyServer::QueryEventAttributes(
	DWORD dwEventCategory,
	DWORD *pdwCount,
	DWORD **ppdwAttrIDs,
	LPWSTR **ppszAttrDescs,
	VARTYPE **ppvtAttrTypes)
{
    TraceState("IOPCEventServer.QueryEventAttributes");

	// check arguments.
	if (pdwCount == NULL || ppdwAttrIDs == NULL || ppszAttrDescs == NULL|| ppvtAttrTypes == NULL)
	{
		return E_INVALIDARG;
	}
    
    *pdwCount = 0;
	*ppdwAttrIDs = NULL;
	*ppszAttrDescs = NULL;
	*ppvtAttrTypes = NULL;

    DWORD dwCount = 0;
	DWORD* pdwAttrIDs = NULL;
	LPWSTR* pszAttrDescs = NULL;
	VARTYPE* pvtAttrTypes = NULL;

	try
	{
		// get inner server.
		ComAe2Proxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

        // fetch categories.
		List<AeEventAttribute^>^ values = server->QueryEventAttributes(dwEventCategory);

        if (values == nullptr)
        {
            return E_INVALIDARG;
        }

		if (values->Count == 0)
        {
            return S_OK;
        }

        dwCount = values->Count;

		// allocate memory for results.
		OpcProxy_AllocArrayToReturn(pdwAttrIDs, dwCount, DWORD);
		OpcProxy_AllocArrayToReturn(pszAttrDescs, dwCount, LPWSTR);
		OpcProxy_AllocArrayToReturn(pvtAttrTypes, dwCount, VARTYPE);

	    for (DWORD ii = 0; ii < dwCount; ii++)
	    {
            pdwAttrIDs[ii] = values[ii]->LocalId;
            pvtAttrTypes[ii] = (VARTYPE)ComUtils::GetVarType(gcnew Opc::Ua::TypeInfo(values[ii]->BuiltInType, values[ii]->ValueRank));

            if (values[ii]->DisplayPath != nullptr)
            {
                pszAttrDescs[ii] = (LPWSTR)Marshal::StringToCoTaskMemUni(values[ii]->DisplayPath).ToPointer();;
            }
	    }

        *pdwCount = dwCount;
		*ppdwAttrIDs = pdwAttrIDs;
		*ppszAttrDescs = pszAttrDescs;
		*ppvtAttrTypes = pvtAttrTypes;

		return S_OK;
	}
	catch (Exception^ e)
	{
		// free strings on error.
		if (pszAttrDescs != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				CoTaskMemFree(pszAttrDescs[ii]);
			}
		}

		// free allocated results.
		CoTaskMemFree(pdwAttrIDs);
		CoTaskMemFree(pszAttrDescs);
		CoTaskMemFree(pvtAttrTypes);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// TranslateToItemIDs
HRESULT COpcUaAe2ProxyServer::TranslateToItemIDs(
	LPWSTR szSource,
	DWORD dwEventCategory,
	LPWSTR szConditionName,
	LPWSTR szSubconditionName,
	DWORD dwCount,
	DWORD *pdwAssocAttrIDs,
	LPWSTR **ppszAttrItemIDs,
	LPWSTR **ppszNodeNames,
	CLSID **ppCLSIDs)
{
    TraceState("IOPCEventServer.TranslateToItemIDs");
	return E_NOTIMPL;
}

// GetConditionState
HRESULT COpcUaAe2ProxyServer::GetConditionState(
	LPWSTR szSource,
	LPWSTR szConditionName,
	DWORD dwNumEventAttrs,
	DWORD *dwAttributeIDs,
	OPCCONDITIONSTATE **ppConditionState)
{
    TraceState("IOPCEventServer.GetConditionState");
	return E_NOTIMPL;
}

// EnableConditionByArea
HRESULT COpcUaAe2ProxyServer::EnableConditionByArea(
	DWORD dwNumAreas,
	LPWSTR *pszAreas)
{
    TraceState("IOPCEventServer.EnableConditionByArea");
	return E_NOTIMPL;
}

// EnableConditionBySource
HRESULT COpcUaAe2ProxyServer::EnableConditionBySource(
	DWORD dwNumSources,
	LPWSTR *pszSources)
{
    TraceState("IOPCEventServer.EnableConditionBySource");
	return E_NOTIMPL;
}

// DisableConditionByArea
HRESULT COpcUaAe2ProxyServer::DisableConditionByArea(
	DWORD dwNumAreas,
	LPWSTR *pszAreas)
{
    TraceState("IOPCEventServer.DisableConditionByArea");
	return E_NOTIMPL;
}

// DisableConditionBySource
HRESULT COpcUaAe2ProxyServer::DisableConditionBySource(
	DWORD dwNumSources,
	LPWSTR *pszSources)
{
    TraceState("IOPCEventServer.DisableConditionBySource");
	return E_NOTIMPL;
}

// AckCondition
HRESULT COpcUaAe2ProxyServer::AckCondition(
	DWORD dwCount,
	LPWSTR szAcknowledgerID,
	LPWSTR szComment,
	LPWSTR *pszSource,
	LPWSTR *pszConditionName,
	::FILETIME *pftActiveTime,
	DWORD *pdwCookie,
	HRESULT **ppErrors)
{
    TraceState("IOPCEventServer.AckCondition");

	if (dwCount == 0 || pszSource == NULL || pszConditionName == NULL || pftActiveTime == NULL || pdwCookie == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	if (szAcknowledgerID == NULL || szAcknowledgerID[0] == 0)
	{
		return E_INVALIDARG;
	}

	*ppErrors = NULL;

	HRESULT* pErrors = NULL;

	try
	{
		OpcProxy_AllocArrayToReturn(pErrors, dwCount, HRESULT);

		ComAe2Proxy^ server = GetInnerServer();

		array<AeAcknowledgeRequest^>^ requests = gcnew array<AeAcknowledgeRequest^>(dwCount);

		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			requests[ii] =  gcnew AeAcknowledgeRequest();
			requests[ii]->SourceName = Marshal::PtrToStringUni((IntPtr)(void*)pszSource[ii]);
			requests[ii]->ConditionName = Marshal::PtrToStringUni((IntPtr)(void*)pszConditionName[ii]);
			requests[ii]->ActiveTime = ComUtils::GetDateTime((IntPtr)&(pftActiveTime[ii]));
			requests[ii]->Cookie = pdwCookie[ii];
		}

		String^ comment =  Marshal::PtrToStringUni((IntPtr)(void*)szComment);
		String^ acknowledgerId =  Marshal::PtrToStringUni((IntPtr)(void*)szAcknowledgerID);

		array<int>^ results = server->AcknowledgeEvents(comment, acknowledgerId, requests);

		HRESULT hResult = S_OK;

		for (int ii = 0; ii < results->Length; ii++)
		{
			pErrors[ii] = results[ii];

			if (pErrors[ii] != S_OK)
			{
				hResult = S_FALSE;
			}
		}

		*ppErrors = pErrors;

		return hResult;
	}
	catch (Exception^ e)
	{
		CoTaskMemFree(pErrors);
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// CreateAreaBrowser
HRESULT COpcUaAe2ProxyServer::CreateAreaBrowser(
	REFIID riid,
	LPUNKNOWN* ppUnk)
{
    TraceState("IOPCEventServer.CreateAreaBrowser");

    HRESULT hResult = S_OK;

	// check ppUnk.
	if (ppUnk == NULL)
	{
		return E_INVALIDARG;
	}

    *ppUnk = NULL;

	try
	{
        // get the server.
		ComAe2Proxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// create the browser.
        ComAe2Browser^ browser = server->CreateBrowser();

		// wrap the browser.
		COpcUaAe2ProxyBrowser* pBrowser = new COpcUaAe2ProxyBrowser(browser);

		// fetch required interface.
		if (FAILED(pBrowser->QueryInterface(IID_IOPCEventAreaBrowser, (void**)ppUnk)))
		{
            pBrowser->Release();
			return E_NOINTERFACE;
		}

		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}
}
