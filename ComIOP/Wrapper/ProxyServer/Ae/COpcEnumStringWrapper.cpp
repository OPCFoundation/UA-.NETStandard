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
#include "COpcEnumStringWrapper.h"
#include "COpcUaProxyUtils.h"

using namespace System;

/// <summary>
/// Dumps the current state.
/// </summary>
static void TraceState(String^ context, ... array<Object^>^ args)
{
    #ifdef TRACESTATE
    COpcUaProxyUtils::TraceState("COpcEnumStringWrapper", context, args);
	#endif
}

//============================================================================
// COpcEnumStringWrapper

COpcEnumStringWrapper::COpcEnumStringWrapper()
{
	TraceState("COpcEnumStringWrapper");
	
    m_ipUnknown = NULL;
}

// Constructor
COpcEnumStringWrapper::COpcEnumStringWrapper(IUnknown* ipUnknown)
{
	TraceState("COpcEnumStringWrapper");
	
    m_ipUnknown = ipUnknown;
	m_ipUnknown->AddRef();
}

// Destructor 
COpcEnumStringWrapper::~COpcEnumStringWrapper()
{
	TraceState("~COpcEnumStringWrapper");

	if (m_ipUnknown != NULL)
	{
		m_ipUnknown->Release();
		m_ipUnknown = NULL;
	}
}

//============================================================================
// IEnumString
   
// Next
HRESULT COpcEnumStringWrapper::Next(
	ULONG     celt,
	LPOLESTR* rgelt,
	ULONG*    pceltFetched
)
{
	TraceState("Next");

	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IEnumString* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IEnumString, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->Next(
		celt,
		rgelt,
		pceltFetched
	);

	// release interface.
	ipInterface->Release();

	if (hResult == S_OK)
	{
		if (celt > 0 && *pceltFetched == 0)
		{
			return S_FALSE;
		}
	}

	return hResult;
}

// Skip
HRESULT COpcEnumStringWrapper::Skip(ULONG celt)
{
	TraceState("Skip");

	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IEnumString* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IEnumString, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->Skip(
		celt
	);

	// release interface.
	ipInterface->Release();

	return hResult;
}

// Reset
HRESULT COpcEnumStringWrapper::Reset()
{
	TraceState("Reset");

	COpcLock cLock(*this);

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IEnumString* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IEnumString, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	HRESULT hResult = ipInterface->Reset();

	// release interface.
	ipInterface->Release();

	return hResult;
}

// Clone
HRESULT COpcEnumStringWrapper::Clone(IEnumString** ppEnum)
{
	TraceState("Clone");

	COpcLock cLock(*this);

    // check for invalid arguments.
    if (ppEnum == NULL)
    {
        return E_INVALIDARG;
    }

	// check inner server.
	if (m_ipUnknown == NULL)
	{
		return E_FAIL;
	}

	// fetch required interface.
	IEnumString* ipInterface = NULL;

	if (FAILED(m_ipUnknown->QueryInterface(IID_IEnumString, (void**)&ipInterface)))
	{
		return E_NOTIMPL;
	}

	// invoke method.
	IEnumString* ipEnum = NULL;

	HRESULT hResult = ipInterface->Clone(&ipEnum);

	// release interface.
	ipInterface->Release();

	// create wrapper.
	COpcEnumStringWrapper* pEnum = new COpcEnumStringWrapper(ipEnum);

	// release local reference.
	ipEnum->Release();

	// query for interface.
    hResult = pEnum->QueryInterface(IID_IEnumString, (void**)ppEnum);

    // release local reference.
    pEnum->Release();

    return hResult;
}
