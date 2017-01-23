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

#include "COpcUaAe2ProxyBrowser.h"
#include "COpcUaProxyUtils.h"

using namespace System;
using namespace System::Text;
using namespace System::Runtime::InteropServices;
using namespace System::Reflection;
using namespace System::Collections::Generic;
using namespace System::Security::Cryptography::X509Certificates;
using namespace Opc::Ua;
using namespace Opc::Ua::Com;
using namespace Opc::Ua::Com::Server;

/// <summary>
/// Dumps the current state.
/// </summary>
static void TraceState(String^ context, ... array<Object^>^ args)
{
    #ifdef TRACESTATE
    COpcUaProxyUtils::TraceState("COpcUaAe2ProxyBrowser", context, args);
	#endif
}

//==========================================================================
// COpcUaAe2ProxyBrowser

// Constructor
COpcUaAe2ProxyBrowser::COpcUaAe2ProxyBrowser()
{
}

// Constructor
COpcUaAe2ProxyBrowser::COpcUaAe2ProxyBrowser(ComAe2Browser^ browser)
{
	TraceState("COpcUaAe2ProxyBrowser");

	GCHandle hInnerBrowser = GCHandle::Alloc(browser);
	m_pInnerBrowser = ((IntPtr)hInnerBrowser).ToPointer();
	browser->Handle = (IntPtr)this;
}

// Destructor 
COpcUaAe2ProxyBrowser::~COpcUaAe2ProxyBrowser()
{
	TraceState("~COpcUaAe2ProxyBrowser");

	if (m_pInnerBrowser != NULL)
	{
		ComAe2Browser^ browser = GetInnerBrowser();
		delete browser;

		GCHandle hInnerBrowser = (GCHandle)IntPtr(m_pInnerBrowser);
		hInnerBrowser.Free();
		m_pInnerBrowser = NULL;
	}
}

// GetInnerBrowser
ComAe2Browser^ COpcUaAe2ProxyBrowser::GetInnerBrowser()
{
	if (m_pInnerBrowser == NULL)
	{
		return nullptr;
	}

	GCHandle hInnerBrowser = (GCHandle)IntPtr(m_pInnerBrowser);

	if (hInnerBrowser.IsAllocated)
	{
		return (ComAe2Browser^)hInnerBrowser.Target;
	}

	return nullptr;
}

//=========================================================================
// IOPCEventAreaBrowser

// ChangeBrowsePosition
HRESULT COpcUaAe2ProxyBrowser::ChangeBrowsePosition(
	OPCAEBROWSEDIRECTION dwBrowseDirection,
	LPCWSTR szString)
{
    TraceState("IOPCEventAreaBrowser.ChangeBrowsePosition");

	try
	{
		// get inner browser.
		ComAe2Browser^ browser = GetInnerBrowser();

		String^ target = Marshal::PtrToStringUni((IntPtr)(void*)szString);

		switch (dwBrowseDirection)
		{
			case OPCAE_BROWSE_TO:
			{
				browser->BrowseTo(target);
				break;
			}

			case OPCAE_BROWSE_UP:
			{
				if (!String::IsNullOrEmpty(target))
				{
					return E_INVALIDARG;
				}

				browser->BrowseUp();
				break;
			}

			case OPCAE_BROWSE_DOWN:
			{
				if (String::IsNullOrEmpty(target))
				{
					return E_INVALIDARG;
				}

				browser->BrowseDown(target);
				break;
			}
		}
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// BrowseOPCAreas
HRESULT COpcUaAe2ProxyBrowser::BrowseOPCAreas(
	OPCAEBROWSETYPE dwBrowseFilterType,
	LPCWSTR szFilterCriteria,
	LPENUMSTRING *ppIEnumString)
{
	TraceState("IOPCEventAreaBrowser.BrowseOPCAreas");

	*ppIEnumString = NULL;

	// validate browse filters.

	LPWSTR* pNames = NULL;
	DWORD dwCount = 0;

	try
	{	
		// get inner browser.
		ComAe2Browser^ browser = GetInnerBrowser();

		String^ filter = Marshal::PtrToStringUni((IntPtr)(void*)szFilterCriteria);

		// fetch the matching items.
		IList<String^>^ names = browser->Browse(dwBrowseFilterType == OPC_AREA, filter);

		// create enumerator.
		return COpcUaProxyUtils::GetEnumerator(names, IID_IEnumString, (void**)ppIEnumString);
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}
}

// GetQualifiedAreaName
HRESULT COpcUaAe2ProxyBrowser::GetQualifiedAreaName(
	LPCWSTR szAreaName,
	LPWSTR* pszQualifiedAreaName)
{
    TraceState("IOPCEventAreaBrowser.GetQualifiedAreaName");

	if (szAreaName == NULL || pszQualifiedAreaName == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize output parameters.
	*pszQualifiedAreaName = NULL;

	try
	{
		ComAe2Browser^ browser = GetInnerBrowser();

		String^ itemName = Marshal::PtrToStringUni((IntPtr)(void*)szAreaName);
		String^ itemId = browser->GetQualifiedName(itemName, true);

		if (itemId != nullptr)
		{
			*pszQualifiedAreaName = (LPWSTR)Marshal::StringToCoTaskMemUni(itemId).ToPointer();
		}

		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// GetQualifiedSourceName
HRESULT COpcUaAe2ProxyBrowser::GetQualifiedSourceName(
	LPCWSTR szSourceName,
	LPWSTR *pszQualifiedSourceName)
{
    TraceState("IOPCEventAreaBrowser.GetQualifiedSourceName");

	if (szSourceName == NULL || pszQualifiedSourceName == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize output parameters.
	*pszQualifiedSourceName = NULL;

	try
	{
		ComAe2Browser^ browser = GetInnerBrowser();

		String^ itemName = Marshal::PtrToStringUni((IntPtr)(void*)szSourceName);
		String^ itemId = browser->GetQualifiedName(itemName, false);

		if (itemId != nullptr)
		{
			*pszQualifiedSourceName = (LPWSTR)Marshal::StringToCoTaskMemUni(itemId).ToPointer();
		}

		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}
