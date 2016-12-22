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
#include "COpcUaAe2ProxySubscription.h"
#include "COpcUaAe2ProxyServer.h"
#include "COpcUaAe2ProxyEventCallback.h"
#include "COpcUaProxyUtils.h"

using namespace System;
using namespace System::Text;
using namespace System::Runtime::InteropServices;
using namespace System::Reflection;
using namespace Opc::Ua;
using namespace Opc::Ua::Com;
using namespace Opc::Ua::Com::Server;

//==========================================================================
// Local Functions

/// <summary>
/// Dumps the current state.
/// </summary>
static void TraceState(String^ context, ... array<Object^>^ args)
{
    #ifdef TRACESTATE
    COpcUaProxyUtils::TraceState("COpcUaAe2ProxySubscription", context, args);
	#endif
}

//==========================================================================
// COpcUaAe2ProxySubscription

// Constructor
COpcUaAe2ProxySubscription::COpcUaAe2ProxySubscription()
{
	RegisterInterface(IID_IOPCEventSink);

	m_pServer = NULL;
}

// Constructor
COpcUaAe2ProxySubscription::COpcUaAe2ProxySubscription(COpcUaAe2ProxyServer* pServer, ComAe2Subscription^ subscription)
{
	TraceState("COpcUaAe2ProxySubscription");

	RegisterInterface(IID_IOPCEventSink);

	m_pServer = pServer;

	GCHandle hInnerSubscription = GCHandle::Alloc(subscription);
	m_pInnerSubscription = ((IntPtr)hInnerSubscription).ToPointer();
	subscription->Handle = (IntPtr)this;
}

// Destructor 
COpcUaAe2ProxySubscription::~COpcUaAe2ProxySubscription()
{
	TraceState("~COpcUaAe2ProxySubscription");

	if (m_pInnerSubscription != NULL)
	{
		ComAe2Subscription^ subscription = GetInnerSubscription();
		delete subscription;

		GCHandle hInnerSubscription = (GCHandle)IntPtr(m_pInnerSubscription);
		hInnerSubscription.Free();
		m_pInnerSubscription = NULL;
	}
}

// GetInnerSubscription
ComAe2Subscription^ COpcUaAe2ProxySubscription::GetInnerSubscription()
{
	if (m_pInnerSubscription == NULL)
	{
		return nullptr;
	}

	GCHandle hInnerSubscription = (GCHandle)IntPtr(m_pInnerSubscription);

	if (hInnerSubscription.IsAllocated)
	{
		return (ComAe2Subscription^)hInnerSubscription.Target;
	}

	return nullptr;
}

// Delete
void COpcUaAe2ProxySubscription::Delete()
{
    COpcLock cLock(*this);
	m_pServer = NULL;
}

// OnAdvise
void COpcUaAe2ProxySubscription::OnAdvise(REFIID riid, DWORD dwCookie)
{
	COpcLock cLock(*this);

	IOPCEventSink* ipCallback = NULL;

    if (FAILED(GetCallback(IID_IOPCEventSink, (IUnknown**)&ipCallback)))
    {
        return;
    }

    try
    {
	    ComAe2Subscription^ subscription = GetInnerSubscription();
	    COpcUaAe2ProxyEventCallback^ callback = gcnew COpcUaAe2ProxyEventCallback(ipCallback);
	    subscription->SetCallback(callback);
	    ipCallback->Release();
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "Unexpected error setting subscription callback.");
    }

	cLock.Unlock();
}

// OnUnadvise
void COpcUaAe2ProxySubscription::OnUnadvise(REFIID riid, DWORD dwCookie)
{
	COpcLock cLock(*this);

    try
    {
	    ComAe2Subscription^ subscription = GetInnerSubscription();
	    subscription->SetCallback(nullptr);
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "Unexpected error releasing subscription callback.");
    }

	cLock.Unlock();
}

//=========================================================================
// IOPCEventSubscriptionMgt

// SetFilter
HRESULT COpcUaAe2ProxySubscription::SetFilter(
	DWORD dwEventType,
	DWORD dwNumCategories,
	DWORD *pdwEventCategories,
	DWORD dwLowSeverity,
	DWORD dwHighSeverity,
	DWORD dwNumAreas,
	LPWSTR *pszAreaList,
	DWORD dwNumSources,
	LPWSTR *pszSourceList)
{
    TraceState("IOPCEventSubscriptionMgt.SetFilter");

	if (pdwEventCategories == NULL || pszAreaList == NULL || pszSourceList == NULL)
	{
		return E_INVALIDARG;
	}

	try
	{
		ComAe2Subscription^ subscription = GetInnerSubscription();

		array<unsigned int>^ categoryIds = gcnew array<unsigned int>(dwNumCategories);

		for (DWORD ii = 0; ii < dwNumCategories; ii++)
		{
			categoryIds[ii] = pdwEventCategories[ii];
		}

		array<String^>^ areas = gcnew array<String^>(dwNumAreas);

		for (DWORD ii = 0; ii < dwNumAreas; ii++)
		{
			areas[ii] = Marshal::PtrToStringUni((IntPtr)(void*)pszAreaList[ii]);
		}

		array<String^>^ sources = gcnew array<String^>(dwNumSources);

		for (DWORD ii = 0; ii < dwNumSources; ii++)
		{
			sources[ii] = Marshal::PtrToStringUni((IntPtr)(void*)pszSourceList[ii]);
		}

		subscription->SetFilter(
			dwEventType,
			(WORD)dwLowSeverity,
			(WORD)dwHighSeverity,
			categoryIds,
			areas,
			sources);

		subscription->ApplyChanges();

		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// GetFilter
HRESULT COpcUaAe2ProxySubscription::GetFilter(
	DWORD *pdwEventType,
	DWORD *pdwNumCategories,
	DWORD **ppdwEventCategories,
	DWORD *pdwLowSeverity,
	DWORD *pdwHighSeverity,
	DWORD *pdwNumAreas,
	LPWSTR **ppszAreaList,
	DWORD *pdwNumSources,
	LPWSTR **ppszSourceList)
{
    TraceState("IOPCEventSubscriptionMgt.GetFilter");

	if  (
			pdwEventType == NULL || pdwNumCategories == NULL || ppdwEventCategories == NULL ||
			pdwLowSeverity == NULL || pdwHighSeverity == NULL || pdwNumAreas == NULL ||
			ppszAreaList == NULL || pdwNumSources == NULL || ppszSourceList == NULL
		)
	{
		return E_INVALIDARG;
	}

	*pdwEventType = 0;
	*pdwNumCategories = 0;
	*ppdwEventCategories = NULL;
	*pdwLowSeverity = 0;
	*pdwHighSeverity = 0;
	*pdwNumAreas = 0;
	*ppszAreaList = NULL;
	*pdwNumSources = 0;
	*ppszSourceList = NULL;
	
	DWORD dwNumCategories = 0;
	DWORD* pdwEventCategories = NULL;
	DWORD dwNumAreas = 0;
	LPWSTR* pszAreaList = NULL;
	DWORD dwNumSources = 0;
	LPWSTR* pszSourceList = NULL;

	try
	{
		ComAe2Subscription^ subscription = GetInnerSubscription();

		int eventType = 0;
		unsigned short lowSeverity = 0;
		unsigned short highSeverity = 0;
		array<unsigned int>^ categoryIds = nullptr;
		array<String^>^ areas = nullptr;
		array<String^>^ sources = nullptr;

		subscription->GetFilter(
			eventType,
			lowSeverity,
			highSeverity,
			categoryIds,
			areas,
			sources);

		dwNumCategories = categoryIds->Length;
		dwNumAreas = areas->Length;
		dwNumSources = sources->Length;

		// allocate memory for results.
		if (categoryIds != nullptr && categoryIds->Length > 0)
		{
			OpcProxy_AllocArrayToReturn(pdwEventCategories, categoryIds->Length, DWORD);

			for (int ii = 0; ii < categoryIds->Length; ii++)
			{
				pdwEventCategories[ii] = categoryIds[ii];
			}
		}

		if (areas != nullptr && areas->Length > 0)
		{
			OpcProxy_AllocArrayToReturn(pszAreaList, areas->Length, LPWSTR);

			for (int ii = 0; ii < areas->Length; ii++)
			{
				if (areas[ii] != nullptr)
				{
					pszAreaList[ii] = (LPWSTR)Marshal::StringToCoTaskMemUni(areas[ii]).ToPointer();;
				}
			}
		}

		if (sources != nullptr && sources->Length > 0)
		{
			OpcProxy_AllocArrayToReturn(pszSourceList, sources->Length, LPWSTR);

			for (int ii = 0; ii < sources->Length; ii++)
			{
				if (sources[ii] != nullptr)
				{
					pszSourceList[ii] = (LPWSTR)Marshal::StringToCoTaskMemUni(sources[ii]).ToPointer();;
				}
			}
		}

		*pdwEventType = eventType;
		*pdwNumCategories = dwNumCategories;
		*ppdwEventCategories = pdwEventCategories;
		*pdwLowSeverity = lowSeverity;
		*pdwHighSeverity = highSeverity;
		*pdwNumAreas = dwNumAreas;
		*ppszAreaList = pszAreaList;
		*pdwNumSources = dwNumSources;
		*ppszSourceList = pszSourceList;

		return S_OK;
	}
	catch (Exception^ e)
	{
		if (pszAreaList != NULL)
		{
			for (DWORD ii = 0; ii < dwNumAreas; ii++)
			{
				CoTaskMemFree(pszAreaList[ii]);
			}
		}

		if (pszSourceList != NULL)
		{
			for (DWORD ii = 0; ii < dwNumSources; ii++)
			{
				CoTaskMemFree(pszSourceList[ii]);
			}
		}

		CoTaskMemFree(pdwEventCategories);
		CoTaskMemFree(pszAreaList);
		CoTaskMemFree(pszSourceList);

		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// SelectReturnedAttributes
HRESULT COpcUaAe2ProxySubscription::SelectReturnedAttributes(
	DWORD dwEventCategory,
	DWORD dwCount,
	DWORD *dwAttributeIDs)
{
    TraceState("IOPCEventSubscriptionMgt.SelectReturnedAttributes");

	try
	{
		ComAe2Subscription^ subscription = GetInnerSubscription();

		array<unsigned int>^ attributeIds = gcnew array<unsigned int>(dwCount);

		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			attributeIds[ii] = dwAttributeIDs[ii];
		}

		subscription->SelectAttributes(dwEventCategory, attributeIds);
		subscription->ApplyChanges();

		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// GetReturnedAttributes
HRESULT COpcUaAe2ProxySubscription::GetReturnedAttributes(
	DWORD dwEventCategory,
	DWORD *pdwCount,
	DWORD **ppdwAttributeIDs)
{
    TraceState("IOPCEventSubscriptionMgt.GetReturnedAttributes");

	if (pdwCount == NULL || ppdwAttributeIDs == NULL)
	{
		return E_INVALIDARG;
	}

	*pdwCount = 0;
	*ppdwAttributeIDs = NULL;
	
	DWORD* pdwAttributeIDs = NULL;

	try
	{
		ComAe2Subscription^ subscription = GetInnerSubscription();

		array<unsigned int>^ attributesIds = subscription->GetSelectedAttributes(dwEventCategory);

		// allocate memory for results.
		if (attributesIds != nullptr && attributesIds->Length > 0)
		{
			OpcProxy_AllocArrayToReturn(pdwAttributeIDs, attributesIds->Length, DWORD);

			for (int ii = 0; ii < attributesIds->Length; ii++)
			{
				pdwAttributeIDs[ii] = attributesIds[ii];
			}

			*pdwCount = attributesIds->Length;
			*ppdwAttributeIDs = pdwAttributeIDs;
		}

		return S_OK;
	}
	catch (Exception^ e)
	{
		CoTaskMemFree(pdwAttributeIDs);

		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// Refresh
HRESULT COpcUaAe2ProxySubscription::Refresh(DWORD dwConnection)
{
	TraceState("IOPCEventSubscriptionMgt.Refresh");
	try
	{
		ComAe2Subscription^ subscription = GetInnerSubscription();
		subscription->Refresh();
		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// CancelRefresh
HRESULT COpcUaAe2ProxySubscription::CancelRefresh(DWORD dwConnection)
{
	TraceState("IOPCEventSubscriptionMgt.CancelRefresh");

	try
	{
		ComAe2Subscription^ subscription = GetInnerSubscription();
		subscription->CancelRefresh();
		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// GetState
HRESULT COpcUaAe2ProxySubscription::GetState(
	BOOL *pbActive,
	DWORD *pdwBufferTime,
	DWORD *pdwMaxSize,
	OPCHANDLE *phClientSubscription)
{
	TraceState("IOPCEventSubscriptionMgt.GetState");

	if (pbActive == NULL || pdwBufferTime == NULL || pdwMaxSize == NULL || phClientSubscription == NULL)
	{
		return E_INVALIDARG;
	}

	*pbActive = 0;
	*pdwBufferTime = 0;
	*pdwMaxSize = 0;
	*phClientSubscription = 0;

	try
	{
		ComAe2Subscription^ subscription = GetInnerSubscription();

		*pbActive = (subscription->Active)?1:0;
		*pdwBufferTime = (DWORD)subscription->ActualBufferTime;
		*pdwMaxSize = (DWORD)subscription->ActualMaxSize;
		*phClientSubscription = subscription->ClientHandle;

		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// SetState
HRESULT COpcUaAe2ProxySubscription::SetState(
	BOOL *pbActive,
	DWORD *pdwBufferTime,
	DWORD *pdwMaxSize,
	OPCHANDLE hClientSubscription,
	DWORD *pdwRevisedBufferTime,
	DWORD *pdwRevisedMaxSize)
{
	TraceState("IOPCEventSubscriptionMgt.SetState");

	if (pdwRevisedBufferTime == NULL || pdwRevisedMaxSize == NULL)
	{
		return E_INVALIDARG;
	}

	*pdwRevisedBufferTime = 0;
	*pdwRevisedMaxSize = 0;

	try
	{
		ComAe2Subscription^ subscription = GetInnerSubscription();

		subscription->ClientHandle = hClientSubscription;

		if (pdwBufferTime != NULL)
		{
			subscription->BufferTime = *pdwBufferTime;
		}

		if (pdwMaxSize != NULL)
		{
			subscription->MaxSize = *pdwMaxSize;
		}

		if (pbActive != NULL)
		{
			subscription->Active = *pbActive != 0;
		}

		subscription->ApplyChanges();

		*pdwRevisedBufferTime = (DWORD)subscription->ActualBufferTime;
		*pdwRevisedMaxSize = (DWORD)subscription->ActualMaxSize;

		// check if buffer time was revised.
		if (pdwBufferTime != NULL && *pdwRevisedBufferTime != *pdwBufferTime)
		{
			return OPC_S_INVALIDBUFFERTIME;
		}

		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

//=========================================================================
// IOPCEventSubscriptionMgt2

// SetKeepAlive
HRESULT COpcUaAe2ProxySubscription::SetKeepAlive(
	DWORD dwKeepAliveTime,
	DWORD *pdwRevisedKeepAliveTime)
{
	TraceState("IOPCEventSubscriptionMgt2.SetKeepAlive");

	if (pdwRevisedKeepAliveTime == NULL)
	{
		return E_INVALIDARG;
	}

	*pdwRevisedKeepAliveTime = 0;

	try
	{
		ComAe2Subscription^ subscription = GetInnerSubscription();
		*pdwRevisedKeepAliveTime = (DWORD)subscription->SetKeepAlive(dwKeepAliveTime);

		if (*pdwRevisedKeepAliveTime != dwKeepAliveTime)
		{
			return OPC_S_INVALIDKEEPALIVETIME;
		}

		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// GetKeepAlive
HRESULT COpcUaAe2ProxySubscription::GetKeepAlive(DWORD *pdwKeepAliveTime)
{
	TraceState("IOPCEventSubscriptionMgt2.GetKeepAlive");

	if (pdwKeepAliveTime == NULL)
	{
		return E_INVALIDARG;
	}

	*pdwKeepAliveTime = 0;

	try
	{
		ComAe2Subscription^ subscription = GetInnerSubscription();
		*pdwKeepAliveTime = (DWORD)subscription->KeepAlive;
		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}
