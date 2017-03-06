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

#include "COpcUaHdaProxyBrowser.h"
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
    COpcUaProxyUtils::TraceState("COpcUaHdaProxyBrowser", context, args);
	#endif
}

//==========================================================================
// COpcUaHdaProxyBrowser

// Constructor
COpcUaHdaProxyBrowser::COpcUaHdaProxyBrowser()
{
}

// Constructor
COpcUaHdaProxyBrowser::COpcUaHdaProxyBrowser(ComHdaBrowser^ browser)
{
	TraceState("COpcUaHdaProxyBrowser");

	GCHandle hInnerBrowser = GCHandle::Alloc(browser);
	m_pInnerBrowser = ((IntPtr)hInnerBrowser).ToPointer();
	browser->Handle = (IntPtr)this;
}

// Destructor 
COpcUaHdaProxyBrowser::~COpcUaHdaProxyBrowser()
{
	TraceState("~COpcUaHdaProxyBrowser");

	if (m_pInnerBrowser != NULL)
	{
		ComHdaBrowser^ browser = GetInnerBrowser();

		GCHandle hInnerBrowser = (GCHandle)IntPtr(m_pInnerBrowser);
		hInnerBrowser.Free();
		m_pInnerBrowser = NULL;
	}
}

// GetInnerBrowser
ComHdaBrowser^ COpcUaHdaProxyBrowser::GetInnerBrowser()
{
	if (m_pInnerBrowser == NULL)
	{
		return nullptr;
	}

	GCHandle hInnerBrowser = (GCHandle)IntPtr(m_pInnerBrowser);

	if (hInnerBrowser.IsAllocated)
	{
		return (ComHdaBrowser^)hInnerBrowser.Target;
	}

	return nullptr;
}

//=========================================================================
// IOPCHDA_Browser

// GetEnum
HRESULT COpcUaHdaProxyBrowser::GetEnum(
	OPCHDA_BROWSETYPE dwBrowseType,
	LPENUMSTRING*     ppIEnumString
)
{	
	TraceState("IOPCHDA_Browser.GetEnum");

	*ppIEnumString = NULL;

	// validate browse filters.
	if (dwBrowseType < OPCHDA_BRANCH || dwBrowseType > OPCHDA_ITEMS)
	{
		return E_INVALIDARG;
	}

	LPWSTR* pNames = NULL;
	DWORD dwCount = 0;

	try
	{	
		// get inner browser.
		ComHdaBrowser^ browser = GetInnerBrowser();

		// fetch the matching items.
		IList<String^>^ names = nullptr;
		
		if (dwBrowseType == OPCHDA_FLAT)
		{
			names = browser->BrowseForItems();
		}
		else
		{
			names = browser->Browse((int)dwBrowseType);
		}

		// create enumerator.
		return COpcUaProxyUtils::GetEnumerator(names, IID_IEnumString, (void**)ppIEnumString);
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}
}

// ChangeBrowsePosition
HRESULT COpcUaHdaProxyBrowser::ChangeBrowsePosition(
	OPCHDA_BROWSEDIRECTION dwBrowseDirection,
	LPCWSTR                szString
)
{
	TraceState("IOPCHDA_Browser.ChangeBrowsePosition" + Marshal::PtrToStringUni((IntPtr)(LPWSTR)szString));

	try
	{
		// get inner browser.
		ComHdaBrowser^ browser = GetInnerBrowser();

		if (browser == nullptr)
		{
			return E_FAIL;
		}

		// get target name.
		String^ targetName = Marshal::PtrToStringUni((IntPtr)(LPWSTR)szString);

		// dispatch operation.
		switch (dwBrowseDirection)
		{
			case OPCHDA_BROWSE_UP:		
			{
				if (szString != NULL && szString[0] != 0)
				{
					return E_INVALIDARG;
				}

				browser->BrowseUp();
				break;
			}

			case OPCHDA_BROWSE_DOWN:	
			{
				if (szString == NULL || szString[0] == 0)
				{
					return E_INVALIDARG;
				}

				browser->BrowseDown(targetName);
				break;
			}

			case OPCHDA_BROWSE_DIRECT:
			{
				browser->BrowseTo(targetName);
				break;
			}

			default:
			{
				return E_INVALIDARG;
			}
		}
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// GetItemID
HRESULT COpcUaHdaProxyBrowser::GetItemID(
	LPCWSTR szNode,
	LPWSTR* pszItemID
)
{
    TraceState("IOPCHDA_Browser.GetItemID");

    // check for invalid arguments
    if (szNode == NULL || wcslen(szNode) == 0)
    {
        return E_FAIL;
    }

	// initialize output parameters.
	*pszItemID = NULL;

	try
	{
		// get inner browser.
		ComHdaBrowser^ browser = GetInnerBrowser();

		// get item name.
		String^ itemName = Marshal::PtrToStringUni((IntPtr)(LPWSTR)szNode);

		// look up item name.
		String^ itemId = browser->GetItemId(itemName);

		if (itemId == nullptr)
		{			
			return E_INVALIDARG;
		}

		// return result.
		*pszItemID = (LPWSTR)Marshal::StringToCoTaskMemUni(itemId).ToPointer();
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// GetBranchPosition
HRESULT COpcUaHdaProxyBrowser::GetBranchPosition(
	LPWSTR* pszBranchPos
)
{
    TraceState("IOPCHDA_Browser.GetBranchPosition");

	// initialize output parameters.
	*pszBranchPos = NULL;

	try
	{
		// get inner browser.
		ComHdaBrowser^ browser = GetInnerBrowser();

		// look up item name.
		String^ itemId = browser->GetItemId(nullptr);

		if (itemId == nullptr)
		{			
			return E_INVALIDARG;
		}

		// return result.
		*pszBranchPos = (LPWSTR)Marshal::StringToCoTaskMemUni(itemId).ToPointer();
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}
