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
#include "COpcUaAeProxySubscription.h"
#include "COpcUaAeProxyServer.h"
#include "COpcUaProxyUtils.h"

//==========================================================================
// COpcUaAeProxySubscription

// Constructor
COpcUaAeProxySubscription::COpcUaAeProxySubscription()
{
	RegisterInterface(IID_IOPCEventSink);

	m_pServer      = NULL;
	m_ipUnknown    = NULL;
	m_dwConnection = NULL;
}

// Constructor
COpcUaAeProxySubscription::COpcUaAeProxySubscription(COpcUaAeProxyServer* pServer, IUnknown* ipUnknown)
{
	RegisterInterface(IID_IOPCEventSink);

	m_pServer      = pServer;
	m_ipUnknown    = ipUnknown;
	m_dwConnection = NULL;

	if (ipUnknown != NULL)
	{
		ipUnknown->AddRef();
	}
}

// Destructor 
COpcUaAeProxySubscription::~COpcUaAeProxySubscription()
{
	if (m_ipUnknown != NULL)
	{
		m_ipUnknown->Release();
		m_ipUnknown = NULL;
	}
}

// Delete
void COpcUaAeProxySubscription::Delete()
{
	COpcLock cLock(*this);
	m_pServer = NULL;
}

// OnAdvise
void COpcUaAeProxySubscription::OnAdvise(REFIID riid, DWORD dwCookie)
{
	COpcLock cLock(*this);

	if (riid == IID_IOPCEventSink)
	{
		if (FAILED(OpcConnect(m_ipUnknown, (IOPCEventSink*)this, riid, &m_dwConnection)))
		{
			m_dwConnection = NULL;
		}
	}
}

// OnUnadvise
void COpcUaAeProxySubscription::OnUnadvise(REFIID riid, DWORD dwCookie)
{
	if (riid == IID_IOPCEventSink)
	{
		OpcDisconnect(m_ipUnknown, riid, m_dwConnection);
		m_dwConnection = NULL;
	}
}

//=========================================================================
// IOPCEventSubscriptionMgt

// SetFilter
HRESULT COpcUaAeProxySubscription::SetFilter(DWORD dwEventType,
											 DWORD dwNumCategories,
											 DWORD *pdwEventCategories,
											 DWORD dwLowSeverity,
											 DWORD dwHighSeverity,
											 DWORD dwNumAreas,
											 LPWSTR *pszAreaList,
											 DWORD dwNumSources,
											 LPWSTR *pszSourceList)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL || m_pServer == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventSubscriptionMgt* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventSubscriptionMgt, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->SetFilter(dwEventType,
		dwNumCategories,
		pdwEventCategories,
		dwLowSeverity,
		dwHighSeverity,
		dwNumAreas,
		pszAreaList,
		dwNumSources,
		pszSourceList);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// GetFilter
HRESULT COpcUaAeProxySubscription::GetFilter(DWORD *pdwEventType,
											 DWORD *pdwNumCategories,
											 DWORD **ppdwEventCategories,
											 DWORD *pdwLowSeverity,
											 DWORD *pdwHighSeverity,
											 DWORD *pdwNumAreas,
											 LPWSTR **ppszAreaList,
											 DWORD *pdwNumSources,
											 LPWSTR **ppszSourceList)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL || m_pServer == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventSubscriptionMgt* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventSubscriptionMgt, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->GetFilter(pdwEventType,
		pdwNumCategories,
		ppdwEventCategories,
		pdwLowSeverity,
		pdwHighSeverity,
		pdwNumAreas,
		ppszAreaList,
		pdwNumSources,
		ppszSourceList);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// SelectReturnedAttributes
HRESULT COpcUaAeProxySubscription::SelectReturnedAttributes(DWORD dwEventCategory,
															DWORD dwCount,
															DWORD *dwAttributeIDs)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL || m_pServer == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventSubscriptionMgt* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventSubscriptionMgt, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->SelectReturnedAttributes(dwEventCategory,
		dwCount,
		dwAttributeIDs);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// GetReturnedAttributes
HRESULT COpcUaAeProxySubscription::GetReturnedAttributes(DWORD dwEventCategory,
														 DWORD *pdwCount,
														 DWORD **ppdwAttributeIDs)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL || m_pServer == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventSubscriptionMgt* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventSubscriptionMgt, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->GetReturnedAttributes(dwEventCategory,
		pdwCount,
		ppdwAttributeIDs);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// Refresh
HRESULT COpcUaAeProxySubscription::Refresh(DWORD dwConnection)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL || m_pServer == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventSubscriptionMgt* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventSubscriptionMgt, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->Refresh(dwConnection);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// CancelRefresh
HRESULT COpcUaAeProxySubscription::CancelRefresh(DWORD dwConnection)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL || m_pServer == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventSubscriptionMgt* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventSubscriptionMgt, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->CancelRefresh(dwConnection);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// GetState
HRESULT COpcUaAeProxySubscription::GetState(BOOL *pbActive,
											DWORD *pdwBufferTime,
											DWORD *pdwMaxSize,
											OPCHANDLE *phClientSubscription)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL || m_pServer == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventSubscriptionMgt* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventSubscriptionMgt, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->GetState(pbActive,
		pdwBufferTime,
		pdwMaxSize,
		phClientSubscription);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// SetState
HRESULT COpcUaAeProxySubscription::SetState(BOOL *pbActive,
											DWORD *pdwBufferTime,
											DWORD *pdwMaxSize,
											OPCHANDLE hClientSubscription,
											DWORD *pdwRevisedBufferTime,
											DWORD *pdwRevisedMaxSize)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL || m_pServer == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventSubscriptionMgt* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventSubscriptionMgt, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->SetState(pbActive,
		pdwBufferTime,
		pdwMaxSize,
		hClientSubscription,
		pdwRevisedBufferTime,
		pdwRevisedMaxSize);

	if (SUCCEEDED(hResult))
	{
	    if (pdwMaxSize)
		{
			if(*pdwRevisedMaxSize != *pdwMaxSize)
				hResult = OPC_S_INVALIDMAXSIZE;
		}

		if (pdwBufferTime)
		{
			if(*pdwRevisedBufferTime != *pdwBufferTime)
				hResult = OPC_S_INVALIDBUFFERTIME;
		}
	}
	// release interface.
	ipInterface->Release();

	return hResult;
}

//=========================================================================
// IOPCEventSubscriptionMgt2

// SetKeepAlive
HRESULT COpcUaAeProxySubscription::SetKeepAlive(DWORD dwKeepAliveTime,
												DWORD *pdwRevisedKeepAliveTime)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL || m_pServer == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventSubscriptionMgt2* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventSubscriptionMgt2, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->SetKeepAlive(
		dwKeepAliveTime,
		pdwRevisedKeepAliveTime);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// GetKeepAlive
HRESULT COpcUaAeProxySubscription::GetKeepAlive(DWORD *pdwKeepAliveTime)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL || m_pServer == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventSubscriptionMgt2* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventSubscriptionMgt2, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->GetKeepAlive(pdwKeepAliveTime);

	// release interface.
	ipInterface->Release();

	return hResult;
}

//==============================================================================
// IOPCEventSink

// OnEvent
HRESULT COpcUaAeProxySubscription::OnEvent(OPCHANDLE hClientSubscription,
            BOOL bRefresh,
            BOOL bLastRefresh,
            DWORD dwCount,
            ONEVENTSTRUCT *pEvents)
{
	// get callback object.
	IOPCEventSink* ipCallback = NULL;

	HRESULT hResult = GetCallback(IID_IOPCEventSink, (IUnknown**)&ipCallback);

	if (FAILED(hResult) || ipCallback == NULL)
	{
		return S_OK;
	}

	// fix any conversion issues in variants.
	COpcUaProxyUtils::FixupOutputVariants(pEvents->dwNumEventAttrs, pEvents->pEventAttributes);

	// invoke callback.
	ipCallback->OnEvent(hClientSubscription,
            bRefresh,
            bLastRefresh,
            dwCount,
            pEvents);

	// release callback.
	ipCallback->Release();

	return S_OK;
}
