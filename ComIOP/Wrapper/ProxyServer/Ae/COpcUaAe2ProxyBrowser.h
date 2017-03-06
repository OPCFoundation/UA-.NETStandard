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

#ifndef _COpcUaAe2ProxyBrowser_H_
#define _COpcUaAe2ProxyBrowser_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

using namespace Opc::Ua;
using namespace Opc::Ua::Com;
using namespace Opc::Ua::Com::Server;

class COpcUaAe2ProxyBrowser :
    public COpcComObject,
    public IOPCEventAreaBrowser,
    public COpcSynchObject
{
    OPC_CLASS_NEW_DELETE()

    OPC_BEGIN_INTERFACE_TABLE(COpcUaAe2ProxyBrowser)
        OPC_INTERFACE_ENTRY(IOPCEventAreaBrowser)
    OPC_END_INTERFACE_TABLE()

public:

	//=========================================================================
    // Operators

    // Constructor
    COpcUaAe2ProxyBrowser();
    COpcUaAe2ProxyBrowser(ComAe2Browser^ browser);

    // Destructor 
    ~COpcUaAe2ProxyBrowser();

	//=========================================================================
	// IOPCEventAreaBrowser

	STDMETHODIMP ChangeBrowsePosition( 
		/* [in] */ OPCAEBROWSEDIRECTION dwBrowseDirection,
		/* [string][in] */ LPCWSTR szString);

	STDMETHODIMP BrowseOPCAreas( 
		/* [in] */ OPCAEBROWSETYPE dwBrowseFilterType,
		/* [string][in] */ LPCWSTR szFilterCriteria,
		/* [out] */ LPENUMSTRING __RPC_FAR *ppIEnumString);

	STDMETHODIMP GetQualifiedAreaName( 
		/* [in] */ LPCWSTR szAreaName,
		/* [string][out] */ LPWSTR __RPC_FAR *pszQualifiedAreaName);

	STDMETHODIMP GetQualifiedSourceName( 
		/* [in] */ LPCWSTR szSourceName,
		/* [string][out] */ LPWSTR __RPC_FAR *pszQualifiedSourceName);

private:

	// GetInnerBrowser
	ComAe2Browser^ GetInnerBrowser();
	void* m_pInnerBrowser;
};

#endif // _COpcUaAe2ProxyBrowser_H_
