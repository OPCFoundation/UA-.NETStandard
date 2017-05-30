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
#include "COpcUaHdaProxyDataCallback.h"
#include "COpcUaProxyUtils.h"

using namespace System;
using namespace System::Text;
using namespace System::Runtime::InteropServices;
using namespace System::Reflection;
using namespace System::Threading;
using namespace Opc::Ua;
using namespace Opc::Ua::Com;

/// <summary>
/// Dumps the current state.
/// </summary>
static void TraceState(String^ context, ... array<Object^>^ args)
{
	#ifdef TRACESTATE
    COpcUaProxyUtils::TraceState("COpcUaHdaProxyDataCallback", context, args);
	#endif
}

// Copy(HRESULT)
extern HRESULT Copy(
	array<int>^ results,
	DWORD dwNumItems,
	HRESULT* pErrors);

// Copy(HdaUpdateRequest)
extern HRESULT Copy(
	List<HdaUpdateRequest^>^ results,
	DWORD dwNumItems,
    OPCHANDLE* phClient,
	HRESULT* pErrors);

// Copy(OPCHDA_ITEM)
extern HRESULT Copy(
	List<HdaReadRequest^>^ results,
	DWORD dwNumItems,
	OPCHDA_ITEM* pItemValues,
	HRESULT* pErrors);

// Free(OPCHDA_ITEM)
extern void Free(
    DWORD dwNumItems,
    OPCHDA_ITEM* pItemValues);

// Copy(OPCHDA_MODIFIEDITEM)
extern HRESULT Copy(
	List<HdaReadRequest^>^ results,
	DWORD dwNumItems,
	OPCHDA_MODIFIEDITEM* pItemValues,
	HRESULT* pErrors);

// Free(OPCHDA_MODIFIEDITEM)
extern void Free(
    DWORD dwNumItems,
    OPCHDA_MODIFIEDITEM* pItemValues);

// Copy(OPCHDA_ATTRIBUTE)
extern HRESULT Copy(
	List<HdaReadRequest^>^ results,
	DWORD dwItemAttributes,
	OPCHDA_ATTRIBUTE* pItemAttributes,
	HRESULT* pErrors);

// Free(OPCHDA_ATTRIBUTE)
extern void Free(
    DWORD dwNumAttributes,
    OPCHDA_ATTRIBUTE* pItemAttributes);

// Copy(OPCHDA_ANNOTATION)
extern HRESULT Copy(
	List<HdaReadRequest^>^ results,
	DWORD dwNumItems,
    OPCHDA_ANNOTATION* pItemAnnotations,
	HRESULT* pErrors);

// Free(OPCHDA_ANNOTATION)
extern void Free(
    DWORD dwNumItems,
    OPCHDA_ANNOTATION* pItemAnnotations);

// Constructor
COpcUaHdaProxyDataCallback::COpcUaHdaProxyDataCallback(IOPCHDA_DataCallback* ipCallback)
{
	TraceState("COpcUaHdaProxyDataCallback");

	m_lock = gcnew Object();
	m_ipCallback = ipCallback;
	m_ipCallback->AddRef();
}

// Destructor
COpcUaHdaProxyDataCallback::~COpcUaHdaProxyDataCallback(void)
{
	this->!COpcUaHdaProxyDataCallback();
}

// Finalizer
COpcUaHdaProxyDataCallback::!COpcUaHdaProxyDataCallback() 
{
	TraceState("!COpcUaHdaProxyDataCallback");

	Monitor::Enter(m_lock);

	if (m_ipCallback != NULL)
	{
		m_ipCallback->Release();
		m_ipCallback = NULL;
	}

	Monitor::Exit(m_lock);
}

void COpcUaHdaProxyDataCallback::OnDataChange(
	int transactionId, 
	List<HdaReadRequest^>^ results)
{
	TraceState("OnDataChange", "transactionId", transactionId);

	DWORD dwNumItems = results->Count;
	OPCHDA_ITEM* pItemValues = NULL;
    HRESULT* pErrors = NULL;

	IOPCHDA_DataCallback* ipCallback = NULL;

	Monitor::Enter(m_lock);

	// check if callback has been released.
	if (m_ipCallback == NULL)
	{
		TraceState("OnDataChange NO CALLBACK");
		return;
	}

	ipCallback = m_ipCallback;
	ipCallback->AddRef();
	
	Monitor::Exit(m_lock);

	try
	{
		// allocate return parameters.
		OpcProxy_AllocArrayToReturn(pItemValues, dwNumItems, OPCHDA_ITEM);
		OpcProxy_AllocArrayToReturn(pErrors, dwNumItems, HRESULT);

		// marshal output parameters.
		HRESULT hrStatus = Copy(results, dwNumItems, pItemValues, pErrors);

		// send the datachange callback.
		HRESULT hResult = ipCallback->OnDataChange(
			transactionId,
			hrStatus,
			dwNumItems,
			pItemValues,
			pErrors);

        if (FAILED(hResult))
        {
	        TraceState("OnDataChange ERROR", hResult);
        }

		TraceState("OnDataChange Exiting");
	}
    catch (Exception^ e)
    {
        TraceState("OnDataChange Error", e->Message);
    }
	finally
	{
		ipCallback->Release();

        Free(dwNumItems, pItemValues);
		CoTaskMemFree(pErrors);
	}
}

void COpcUaHdaProxyDataCallback::OnReadComplete(
	int transactionId, 
	List<HdaReadRequest^>^ results)
{
	TraceState("OnReadComplete", "transactionId", transactionId);

	DWORD dwNumItems = results->Count;
	OPCHDA_ITEM* pItemValues = NULL;
    HRESULT* pErrors = NULL;

	IOPCHDA_DataCallback* ipCallback = NULL;

	Monitor::Enter(m_lock);

	// check if callback has been released.
	if (m_ipCallback == NULL)
	{
		TraceState("OnReadComplete NO CALLBACK");
		return;
	}

	ipCallback = m_ipCallback;
	ipCallback->AddRef();
	
	Monitor::Exit(m_lock);

	try
	{
		// allocate return parameters.
		OpcProxy_AllocArrayToReturn(pItemValues, dwNumItems, OPCHDA_ITEM);
		OpcProxy_AllocArrayToReturn(pErrors, dwNumItems, HRESULT);

		// marshal output parameters.
		HRESULT hrStatus = Copy(results, dwNumItems, pItemValues, pErrors);

		// send the datachange callback.
		HRESULT hResult = ipCallback->OnReadComplete(
			transactionId,
			hrStatus,
			dwNumItems,
			pItemValues,
			pErrors);

        if (FAILED(hResult))
        {
	        TraceState("OnReadComplete ERROR", hResult);
        }

		TraceState("OnReadComplete Exiting");
	}
    catch (Exception^ e)
    {
        TraceState("OnReadComplete Error", e->Message);
    }
	finally
	{
		ipCallback->Release();

        Free(dwNumItems, pItemValues);
		CoTaskMemFree(pErrors);
	}
}

void COpcUaHdaProxyDataCallback::OnReadModifiedComplete(
	int transactionId, 
	List<HdaReadRequest^>^ results)
{
	TraceState("OnReadModifiedComplete", "transactionId", transactionId);

	DWORD dwNumItems = results->Count;
	OPCHDA_MODIFIEDITEM* pItemValues = NULL;
    HRESULT* pErrors = NULL;

	IOPCHDA_DataCallback* ipCallback = NULL;

	Monitor::Enter(m_lock);

	// check if callback has been released.
	if (m_ipCallback == NULL)
	{
		TraceState("OnReadModifiedComplete NO CALLBACK");
		return;
	}

	ipCallback = m_ipCallback;
	ipCallback->AddRef();
	
	Monitor::Exit(m_lock);

	try
	{
		// allocate return parameters.
		OpcProxy_AllocArrayToReturn(pItemValues, dwNumItems, OPCHDA_MODIFIEDITEM);
		OpcProxy_AllocArrayToReturn(pErrors, dwNumItems, HRESULT);

		// marshal output parameters.
		HRESULT hrStatus = Copy(results, dwNumItems, pItemValues, pErrors);

		// send the datachange callback.
		HRESULT hResult = ipCallback->OnReadModifiedComplete(
			transactionId,
			hrStatus,
			dwNumItems,
			pItemValues,
			pErrors);

        if (FAILED(hResult))
        {
	        TraceState("OnReadModifiedComplete ERROR", hResult);
        }

		TraceState("OnReadModifiedComplete Exiting");
	}
    catch (Exception^ e)
    {
        TraceState("OnReadModifiedComplete Error", e->Message);
    }
	finally
	{
		ipCallback->Release();

        Free(dwNumItems, pItemValues);
		CoTaskMemFree(pErrors);
	}
}

void COpcUaHdaProxyDataCallback::OnReadAttributeComplete(
	int transactionId, 
	List<HdaReadRequest^>^ results)
{
	TraceState("OnReadAttributeComplete", "transactionId", transactionId);

	DWORD dwNumItems = results->Count;
	OPCHDA_ATTRIBUTE* pAttributeValues = NULL;
    HRESULT* pErrors = NULL;

	IOPCHDA_DataCallback* ipCallback = NULL;

	Monitor::Enter(m_lock);

	// check if callback has been released.
	if (m_ipCallback == NULL)
	{
		TraceState("OnReadAttributeComplete NO CALLBACK");
		return;
	}

	ipCallback = m_ipCallback;
	ipCallback->AddRef();
	
	Monitor::Exit(m_lock);

	try
	{
		// allocate return parameters.
		OpcProxy_AllocArrayToReturn(pAttributeValues, dwNumItems, OPCHDA_ATTRIBUTE);
		OpcProxy_AllocArrayToReturn(pErrors, dwNumItems, HRESULT);

		// marshal output parameters.
		HRESULT hrStatus = Copy(results, dwNumItems, pAttributeValues, pErrors);

        // get the client handle.
        OPCHANDLE hClient = 0;

        if (results->Count > 0)
        {
            hClient = results[0]->ClientHandle;
        }

		// send the datachange callback.
		HRESULT hResult = ipCallback->OnReadAttributeComplete(
			transactionId,
			hrStatus,
            hClient,
			dwNumItems,
			pAttributeValues,
			pErrors);

        if (FAILED(hResult))
        {
	        TraceState("OnReadAttributeComplete ERROR", hResult);
        }

		TraceState("OnReadAttributeComplete Exiting");
	}
    catch (Exception^ e)
    {
        TraceState("OnReadAttributeComplete Error", e->Message);
    }
	finally
	{
		ipCallback->Release();

        Free(dwNumItems, pAttributeValues);
		CoTaskMemFree(pErrors);
	}
}

void COpcUaHdaProxyDataCallback::OnReadAnnotations(
	int transactionId, 
	List<HdaReadRequest^>^ results)
{
	TraceState("OnReadAnnotations", "transactionId", transactionId);

	DWORD dwNumItems = results->Count;
	OPCHDA_ANNOTATION* pAnnotationValues = NULL;
    HRESULT* pErrors = NULL;

	IOPCHDA_DataCallback* ipCallback = NULL;

	Monitor::Enter(m_lock);

	// check if callback has been released.
	if (m_ipCallback == NULL)
	{
		TraceState("OnReadAnnotations NO CALLBACK");
		return;
	}

	ipCallback = m_ipCallback;
	ipCallback->AddRef();
	
	Monitor::Exit(m_lock);

	try
	{
		// allocate return parameters.
		OpcProxy_AllocArrayToReturn(pAnnotationValues, dwNumItems, OPCHDA_ANNOTATION);
		OpcProxy_AllocArrayToReturn(pErrors, dwNumItems, HRESULT);

		// marshal output parameters.
		HRESULT hrStatus = Copy(results, dwNumItems, pAnnotationValues, pErrors);

		// send the datachange callback.
		HRESULT hResult = ipCallback->OnReadAnnotations(
			transactionId,
			hrStatus,
			dwNumItems,
			pAnnotationValues,
			pErrors);

        if (FAILED(hResult))
        {
	        TraceState("OnReadAnnotations ERROR", hResult);
        }

		TraceState("OnReadAnnotations Exiting");
	}
    catch (Exception^ e)
    {
        TraceState("OnReadAnnotations Error", e->Message);
    }
	finally
	{
		ipCallback->Release();

        Free(dwNumItems, pAnnotationValues);
		CoTaskMemFree(pErrors);
	}
}

void COpcUaHdaProxyDataCallback::OnInsertAnnotations(
	int transactionId, 
	List<HdaUpdateRequest^>^ results)
{
    TraceState("OnInsertAnnotations", "transactionId", transactionId);

	DWORD dwNumItems = results->Count;
    OPCHANDLE* phClient = NULL;
    HRESULT* pErrors = NULL;

	IOPCHDA_DataCallback* ipCallback = NULL;

	Monitor::Enter(m_lock);

	// check if callback has been released.
	if (m_ipCallback == NULL)
	{
		TraceState("OnInsertAnnotations NO CALLBACK");
		return;
	}

	ipCallback = m_ipCallback;
	ipCallback->AddRef();
	
	Monitor::Exit(m_lock);

	try
	{
		// allocate return parameters.
		OpcProxy_AllocArrayToReturn(phClient, dwNumItems, OPCHANDLE);
		OpcProxy_AllocArrayToReturn(pErrors, dwNumItems, HRESULT);

		// marshal output parameters.
		HRESULT hrStatus = Copy(results, dwNumItems, phClient, pErrors);

		// send the datachange callback.
		HRESULT hResult = ipCallback->OnInsertAnnotations(
			transactionId,
			hrStatus,
			dwNumItems,
            phClient,
			pErrors);

        if (FAILED(hResult))
        {
	        TraceState("OnInsertAnnotations ERROR", hResult);
        }

		TraceState("OnInsertAnnotations Exiting");
	}
    catch (Exception^ e)
    {
        TraceState("OnInsertAnnotations Error", e->Message);
    }
	finally
	{
		ipCallback->Release();

		CoTaskMemFree(phClient);
		CoTaskMemFree(pErrors);
	}
}

void COpcUaHdaProxyDataCallback::OnUpdateComplete(
	int transactionId, 
	List<HdaUpdateRequest^>^ results)
{
    TraceState("OnUpdateComplete", "transactionId", transactionId);

	DWORD dwNumItems = results->Count;
    OPCHANDLE* phClient = NULL;
    HRESULT* pErrors = NULL;

	IOPCHDA_DataCallback* ipCallback = NULL;

	Monitor::Enter(m_lock);

	// check if callback has been released.
	if (m_ipCallback == NULL)
	{
		TraceState("OnUpdateComplete NO CALLBACK");
		return;
	}

	ipCallback = m_ipCallback;
	ipCallback->AddRef();
	
	Monitor::Exit(m_lock);

	try
	{
		// allocate return parameters.
		OpcProxy_AllocArrayToReturn(phClient, dwNumItems, OPCHANDLE);
		OpcProxy_AllocArrayToReturn(pErrors, dwNumItems, HRESULT);

		// marshal output parameters.
		HRESULT hrStatus = Copy(results, dwNumItems, phClient, pErrors);

		// send the datachange callback.
		HRESULT hResult = ipCallback->OnUpdateComplete(
			transactionId,
			hrStatus,
			dwNumItems,
            phClient,
			pErrors);

        if (FAILED(hResult))
        {
	        TraceState("OnUpdateComplete ERROR", hResult);
        }

		TraceState("OnUpdateComplete Exiting");
	}
    catch (Exception^ e)
    {
        TraceState("OnUpdateComplete Error", e->Message);
    }
	finally
	{
		ipCallback->Release();

		CoTaskMemFree(phClient);
		CoTaskMemFree(pErrors);
	}
}

void COpcUaHdaProxyDataCallback::OnCancelComplete(int transactionId)
{
	TraceState("OnCancelComplete");

	IOPCHDA_DataCallback* ipCallback = NULL;

	Monitor::Enter(m_lock);

	// check if callback has been released.
	if (m_ipCallback == NULL)
	{
		return;
	}

	ipCallback = m_ipCallback;
	ipCallback->AddRef();
	
	Monitor::Exit(m_lock);

	// send callback.
	try
	{
		ipCallback->OnCancelComplete(transactionId);
	}
	finally
	{
		ipCallback->Release();
	}
}
