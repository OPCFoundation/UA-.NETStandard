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
#include "COpcConnectionPoint.h"
#include "COpcCPContainer.h"

#include "opcda.h"

//==============================================================================
// COpcConnectionPoint

// Constructor
COpcConnectionPoint::COpcConnectionPoint()
{
    m_tInterface = GUID_NULL;
    m_pContainer = NULL;
    m_ipCallback = NULL;
    m_dwCookie   = NULL;
    m_bFetched   = false;
}

// Constructor
COpcConnectionPoint::COpcConnectionPoint(const IID& tIid, COpcCPContainer* pContainer)
{
    m_tInterface = tIid;
    m_pContainer = pContainer;
    m_ipCallback = NULL;
    m_dwCookie   = NULL;
    m_bFetched   = false;
}

// Destructor 
COpcConnectionPoint::~COpcConnectionPoint()
{
    if (m_ipCallback != NULL) m_ipCallback->Release();
}

// Delete
bool COpcConnectionPoint::Delete()
{	
	COpcLock cLock(*this);

    if (m_ipCallback != NULL) 
    {
        m_ipCallback->Release();
        m_ipCallback = NULL;
    }

	m_pContainer = NULL;

	cLock.Unlock();
    
    // return false if reference still exist.
    return (Release() == 0);
}

//==============================================================================
// IConnectionPoint

// GetConnectionInterface
HRESULT COpcConnectionPoint::GetConnectionInterface(IID* pIID)
{
	COpcLock cLock(*this);

    if (pIID == NULL)
    {
        return E_INVALIDARG;
    }

    *pIID = m_tInterface;
    return S_OK;
}

// GetConnectionPointContainer
HRESULT COpcConnectionPoint::GetConnectionPointContainer(IConnectionPointContainer** ppCPC)
{
	COpcLock cLock(*this);

    if (ppCPC == NULL)
    {
        return E_INVALIDARG;
    }

    if (m_pContainer != NULL)
    {
        return m_pContainer->QueryInterface(IID_IConnectionPointContainer, (void**)ppCPC);
    }

    return E_FAIL;
}

// Advise
HRESULT COpcConnectionPoint::Advise(IUnknown* pUnkSink, DWORD* pdwCookie)
{
	COpcLock cLock(*this);

    // invalid arguments.
    if (pUnkSink == NULL || pdwCookie == NULL)
    {
        return E_POINTER;
    }

    *pdwCookie = NULL;

    // check if an callback already exists.
    if (m_ipCallback != NULL)
    {
        return CONNECT_E_ADVISELIMIT;
    }

	// get the connection callback interface.
	HRESULT hResult = pUnkSink->QueryInterface(m_tInterface, (void**)&m_ipCallback);

	if (FAILED(hResult))
	{
		return hResult;
	}

    // use interface as a unique id.
    m_dwCookie = (DWORD)m_ipCallback;
    *pdwCookie = m_dwCookie;

	// notify the container.
	COpcCPContainer* pContainer = m_pContainer;
	
	if (pContainer != NULL)
	{
		pContainer->AddRef();
	}

	DWORD dwCookie = m_dwCookie;

	cLock.Unlock();

	if (pContainer != NULL)
	{
		pContainer->OnAdvise(m_tInterface, dwCookie);
		pContainer->Release();
	}

	return S_OK;
}

// Unadvise
HRESULT COpcConnectionPoint::Unadvise(DWORD dwCookie)
{
	COpcLock cLock(*this);

    // not a valid connection id.
    if (m_dwCookie != dwCookie || m_ipCallback == NULL)
    {
        return CONNECT_E_NOCONNECTION;
    }

    m_ipCallback->Release();
	
	// notify the container.
	COpcCPContainer* pContainer = m_pContainer;
	
	if (pContainer != NULL)
	{
		pContainer->AddRef();
	}

    m_ipCallback = NULL;
    m_dwCookie   = NULL;

	cLock.Unlock();

	if (pContainer != NULL)
	{
		pContainer->OnUnadvise(m_tInterface, dwCookie);
		pContainer->Release();
	}

    return S_OK;
}

// EnumConnections
HRESULT COpcConnectionPoint::EnumConnections(IEnumConnections** ppEnum)
{
	COpcLock cLock(*this);

    // invalid arguments.
    if (ppEnum == NULL)
    {
        return E_POINTER;
    }

    // no separate object required since one and only one callback exists.
    return QueryInterface(IID_IEnumConnections, (void**)ppEnum);
}

//==============================================================================
// FUNCTION: OpcConnect
// PURPOSE:  Establishes a connection to the server.

OPCUTILS_API HRESULT OpcConnect(
    IUnknown* ipSource, 
    IUnknown* ipSink, 
    REFIID    riid, 
    DWORD*    pdwConnection)
{
    HRESULT hResult = S_OK;

    IConnectionPoint* pCP = NULL;
    IConnectionPointContainer* ipCPC = NULL;
    IUnknown* ipCallback = NULL;

    TRY
    {
        // check for valid arguments.
        if (ipSource == NULL || ipSink == NULL || pdwConnection == NULL)
        {
            THROW_(hResult, E_POINTER);
        }

        // query for container.
        hResult = ipSource->QueryInterface(IID_IConnectionPointContainer, (void**)&ipCPC);

        if (FAILED(hResult))
        {
            THROW();
        }

        // find connection point.
        hResult = ipCPC->FindConnectionPoint(riid, &pCP);

        if (FAILED(hResult))
        {
            THROW();
        }

        // query for interface.
        hResult = ipSink->QueryInterface(riid, (void**)&ipCallback);

        if (FAILED(hResult))
        {
            THROW();
        }

        // connect 
        hResult = pCP->Advise(ipCallback, pdwConnection);
        
        if (FAILED(hResult))
        {
            THROW();
        }
    }
    CATCH_FINALLY
    {
        if (pCP != NULL) pCP->Release();
        if (ipCPC != NULL) ipCPC->Release();
		if (ipCallback != NULL) ipCallback->Release();
    }

    return hResult;
}

//==============================================================================
// FUNCTION: OpcDisconnect
// PURPOSE:  Closes a connection to the server.

OPCUTILS_API HRESULT OpcDisconnect(
    IUnknown* ipSource, 
    REFIID    riid, 
    DWORD     dwConnection)
{
    HRESULT hResult = S_OK;

    IConnectionPoint* pCP = NULL;
    IConnectionPointContainer* ipCPC = NULL;

    TRY
    {
        // check for valid arguments.
        if (ipSource == NULL)
        {
            THROW_(hResult, E_POINTER);
        }

        // query for container.
        hResult = ipSource->QueryInterface(IID_IConnectionPointContainer, (void**)&ipCPC);

        if (FAILED(hResult))
        {
            THROW();
        }

        // find connection point.
        hResult = ipCPC->FindConnectionPoint(riid, &pCP);

        if (FAILED(hResult))
        {
            THROW();
        }

        // disconnect 
        hResult = pCP->Unadvise(dwConnection);
        
        if (FAILED(hResult))
        {
            THROW();
        }
    }
    CATCH_FINALLY
    {
        if (pCP != NULL) pCP->Release();
        if (ipCPC != NULL) ipCPC->Release();
    }

    return hResult;
}
