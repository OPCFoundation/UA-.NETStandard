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

#ifndef _COpcUaAe2ProxySubscription_H_
#define _COpcUaAe2ProxySubscription_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcUaComProxyServer.h"

using namespace System;
using namespace System::Collections::Generic;
using namespace Opc::Ua;
using namespace Opc::Ua::Com;
using namespace Opc::Ua::Com::Server;

class COpcUaAe2ProxyServer;

//============================================================================
// CLASS:   COpcUaAe2ProxySubscription

class COpcUaAe2ProxySubscription :
    public COpcComObject,
    public COpcCPContainer,
	public IOPCEventSubscriptionMgt2,
    public COpcSynchObject
{
    OPC_CLASS_NEW_DELETE()

    OPC_BEGIN_INTERFACE_TABLE(COpcUaAe2ProxySubscription)
        OPC_INTERFACE_ENTRY(IConnectionPointContainer)
        OPC_INTERFACE_ENTRY(IOPCEventSubscriptionMgt)
        OPC_INTERFACE_ENTRY(IOPCEventSubscriptionMgt2)
    OPC_END_INTERFACE_TABLE()

public:

    // Constructor
    COpcUaAe2ProxySubscription();
    COpcUaAe2ProxySubscription(COpcUaAe2ProxyServer* pServer, ComAe2Subscription^ subscription);

    // Destructor 
    ~COpcUaAe2ProxySubscription();

    //=========================================================================
    // Public Methods

    virtual ULONG InternalAddRef() 
    { 
		return COpcComObject::InternalAddRef(); 
    } 

    // InternalRelease
    // 
    // Description
    //
    // Removes a reference to the COM server. If the reference reaches zero
    // it calls FinalRelease() and deletes the instance.
    //
    // Return Codes
    //
    // The current number of references.

    virtual ULONG InternalRelease() 
    { 
		return COpcComObject::InternalRelease(); 
    } 

	// Delete
	void Delete();

	// OnAdvise
	virtual void OnAdvise(REFIID riid, DWORD dwCookie);

	// OnUnadvise
	virtual void OnUnadvise(REFIID riid, DWORD dwCookie);

	//=========================================================================
	// IOPCEventSubscriptionMgt

	STDMETHODIMP SetFilter( 
		/* [in] */ DWORD dwEventType,
		/* [in] */ DWORD dwNumCategories,
		/* [size_is][in] */ DWORD __RPC_FAR *pdwEventCategories,
		/* [in] */ DWORD dwLowSeverity,
		/* [in] */ DWORD dwHighSeverity,
		/* [in] */ DWORD dwNumAreas,
		/* [size_is][in] */ LPWSTR __RPC_FAR *pszAreaList,
		/* [in] */ DWORD dwNumSources,
		/* [size_is][in] */ LPWSTR __RPC_FAR *pszSourceList);

	STDMETHODIMP GetFilter( 
		/* [out] */ DWORD __RPC_FAR *pdwEventType,
		/* [out] */ DWORD __RPC_FAR *pdwNumCategories,
		/* [size_is][size_is][out] */ DWORD __RPC_FAR *__RPC_FAR *ppdwEventCategories,
		/* [out] */ DWORD __RPC_FAR *pdwLowSeverity,
		/* [out] */ DWORD __RPC_FAR *pdwHighSeverity,
		/* [out] */ DWORD __RPC_FAR *pdwNumAreas,
		/* [size_is][size_is][out] */ LPWSTR __RPC_FAR *__RPC_FAR *ppszAreaList,
		/* [out] */ DWORD __RPC_FAR *pdwNumSources,
		/* [size_is][size_is][out] */ LPWSTR __RPC_FAR *__RPC_FAR *ppszSourceList);

	STDMETHODIMP SelectReturnedAttributes( 
		/* [in] */ DWORD dwEventCategory,
		/* [in] */ DWORD dwCount,
		/* [size_is][in] */ DWORD __RPC_FAR *dwAttributeIDs);

	STDMETHODIMP GetReturnedAttributes( 
		/* [in] */ DWORD dwEventCategory,
		/* [out] */ DWORD __RPC_FAR *pdwCount,
		/* [size_is][size_is][out] */ DWORD __RPC_FAR *__RPC_FAR *ppdwAttributeIDs);

	STDMETHODIMP Refresh( 
		/* [in] */ DWORD dwConnection);

	STDMETHODIMP CancelRefresh( 
		/* [in] */ DWORD dwConnection);

	STDMETHODIMP GetState( 
		/* [out] */ BOOL __RPC_FAR *pbActive,
		/* [out] */ DWORD __RPC_FAR *pdwBufferTime,
		/* [out] */ DWORD __RPC_FAR *pdwMaxSize,
		/* [out] */ OPCHANDLE __RPC_FAR *phClientSubscription);

	STDMETHODIMP SetState( 
		/* [in][unique] */ BOOL __RPC_FAR *pbActive,
		/* [in][unique] */ DWORD __RPC_FAR *pdwBufferTime,
		/* [in][unique] */ DWORD __RPC_FAR *pdwMaxSize,
		/* [in] */ OPCHANDLE hClientSubscription,
		/* [out] */ DWORD __RPC_FAR *pdwRevisedBufferTime,
		/* [out] */ DWORD __RPC_FAR *pdwRevisedMaxSize);


	//=========================================================================
	// IOPCEventSubscriptionMgt2

	STDMETHODIMP SetKeepAlive( 
		/* [in] */ DWORD dwKeepAliveTime,
		/* [out] */ DWORD __RPC_FAR *pdwRevisedKeepAliveTime);

	STDMETHODIMP GetKeepAlive( 
		/* [out] */ DWORD __RPC_FAR *pdwKeepAliveTime);

private:

	// GetInnerGroup
	ComAe2Subscription^ GetInnerSubscription();
	COpcUaAe2ProxyServer* m_pServer;
	void* m_pInnerSubscription;

};

#endif // _COpcUaAe2ProxySubscription_H_
