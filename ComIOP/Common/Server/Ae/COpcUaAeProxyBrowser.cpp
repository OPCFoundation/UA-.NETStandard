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
#include "COpcUaAeProxyBrowser.h"
#include "COpcUaAeProxyServer.h"
#include "COpcEnumStringWrapper.h"

//==========================================================================
// Local Functions



//==========================================================================
// COpcUaAeProxySubscription

// Constructor
COpcUaAeProxyBrowser::COpcUaAeProxyBrowser()
{
	m_pServer      = NULL;
	m_ipUnknown    = NULL;
}

// Constructor
COpcUaAeProxyBrowser::COpcUaAeProxyBrowser(COpcUaAeProxyServer* pServer, IUnknown* ipUnknown)
{
	m_pServer      = pServer;
	m_ipUnknown    = ipUnknown;

	if (ipUnknown != NULL)
	{
		ipUnknown->AddRef();
	}
}

// Destructor 
COpcUaAeProxyBrowser::~COpcUaAeProxyBrowser()
{
	if (m_ipUnknown != NULL)
	{
		m_ipUnknown->Release();
		m_ipUnknown = NULL;
	}
}

// Delete
void COpcUaAeProxyBrowser::Delete()
{
	COpcLock cLock(*this);
	m_pServer = NULL;
}



//=========================================================================
// IOPCEventAreaBrowser

// ChangeBrowsePosition
HRESULT COpcUaAeProxyBrowser::ChangeBrowsePosition(OPCAEBROWSEDIRECTION dwBrowseDirection,
												   LPCWSTR szString)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL || m_pServer == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventAreaBrowser* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventAreaBrowser, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->ChangeBrowsePosition(dwBrowseDirection,
		szString);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// BrowseOPCAreas
HRESULT COpcUaAeProxyBrowser::BrowseOPCAreas(OPCAEBROWSETYPE dwBrowseFilterType,
											 LPCWSTR szFilterCriteria,
											 LPENUMSTRING *ppIEnumString)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL || m_pServer == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventAreaBrowser* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventAreaBrowser, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	IEnumString* ipEnum = NULL;

	// invoke method.
	HRESULT hResult = ipInterface->BrowseOPCAreas(dwBrowseFilterType,
		szFilterCriteria,
		&ipEnum);

	if (SUCCEEDED(hResult) && ipEnum != NULL)
	{
		// create enumerator wrapper.
		IUnknown* ipWrapper = new COpcEnumStringWrapper(ipEnum);
	           
		// release local reference.
		ipEnum->Release();

		// query for desired interface.
		hResult = ipWrapper->QueryInterface(IID_IEnumString, (void**)ppIEnumString);
		if (SUCCEEDED(hResult))
		{
			// check if enumerator has any entries.
			ULONG  ulFetched = 0;
			LPWSTR szName    = NULL;

			hResult = (*ppIEnumString)->Next(1, &szName, &ulFetched);
			if (SUCCEEDED(hResult))
			{
				(*ppIEnumString)->Reset();
				OpcFree(szName);
			}
		}
		// release local reference.
		ipWrapper->Release();
	}

	// release interface.
	ipInterface->Release();

	return hResult;
}

// GetQualifiedAreaName
HRESULT COpcUaAeProxyBrowser::GetQualifiedAreaName(LPCWSTR szAreaName,
												   LPWSTR *pszQualifiedAreaName)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL || m_pServer == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventAreaBrowser* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventAreaBrowser, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->GetQualifiedAreaName(szAreaName,
		pszQualifiedAreaName);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// GetQualifiedSourceName
HRESULT COpcUaAeProxyBrowser::GetQualifiedSourceName(LPCWSTR szSourceName,
													 LPWSTR *pszQualifiedSourceName)
{
	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL || m_pServer == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IOPCEventAreaBrowser* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IOPCEventAreaBrowser, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->GetQualifiedSourceName(szSourceName,
		pszQualifiedSourceName);

	// release interface.
	ipInterface->Release();

	return hResult;
}
