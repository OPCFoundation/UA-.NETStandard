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
#include "COpcUaDaProxyGroupCallback.h"
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
    COpcUaProxyUtils::TraceState("COpcUaDaProxyGroupCallback", context, args);
	#endif
}

// Constructor
COpcUaDaProxyGroupCallback::COpcUaDaProxyGroupCallback(IOPCDataCallback* ipCallback)
{
	TraceState("COpcUaDaProxyGroupCallback");

	m_lock = gcnew Object();
	m_ipCallback = ipCallback;
	m_ipCallback->AddRef();
}

// Destructor
COpcUaDaProxyGroupCallback::~COpcUaDaProxyGroupCallback(void)
{
	this->!COpcUaDaProxyGroupCallback();
}

// Finalizer
COpcUaDaProxyGroupCallback::!COpcUaDaProxyGroupCallback() 
{
	TraceState("!COpcUaDaProxyGroupCallback");

	Monitor::Enter(m_lock);

	if (m_ipCallback != NULL)
	{
		m_ipCallback->Release();
		m_ipCallback = NULL;
	}

	Monitor::Exit(m_lock);
}

// ReadComplete
void COpcUaDaProxyGroupCallback::ReadCompleted(
    int groupHandle,
	bool isRefresh,
    int cancelId,
    int transactionId,
    array<int>^ clientHandles,
    array<DaValue^>^ values)
{	
	TraceState("ReadCompleted", "groupHandle", groupHandle, "transactionId", transactionId, "isRefresh", isRefresh);

	DWORD dwCount = clientHandles->Length;
    OPCHANDLE* phClient = NULL;
    VARIANT* pvValues = NULL;
    WORD* pwQualities = NULL;
    ::FILETIME* pftTimeStamps = NULL;
    HRESULT* pErrors = NULL;

	IOPCDataCallback* ipCallback = NULL;

	Monitor::Enter(m_lock);

	// check if callback has been released.
	if (m_ipCallback == NULL)
	{
		TraceState("ReadCompleted NO CALLBACK");
		return;
	}

	ipCallback = m_ipCallback;
	ipCallback->AddRef();
	
	Monitor::Exit(m_lock);

	try
	{
        bool isKeepAlive = false;

        // need to ensure a non-null pointer for keep alive.
        if (dwCount == 0)
        {
            dwCount = 1;
            isKeepAlive = true;
        }

		// allocate return parameters.
		OpcProxy_AllocArrayToReturn(phClient, dwCount, OPCHANDLE);
		OpcProxy_AllocArrayToReturn(pvValues, dwCount, VARIANT);
		OpcProxy_AllocArrayToReturn(pwQualities, dwCount, WORD);
		OpcProxy_AllocArrayToReturn(pftTimeStamps, dwCount, ::FILETIME);
		OpcProxy_AllocArrayToReturn(pErrors, dwCount, HRESULT);

        if (isKeepAlive)
        {
            dwCount = 0;
        }

		// marshal output parameters.
		HRESULT hMasterError = S_OK;
		HRESULT hMasterQuality = S_OK;

		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			phClient[ii] = clientHandles[ii];
			pErrors[ii] = values[ii]->Error;

			if (pErrors[ii] != S_OK)
			{
				hMasterError = S_FALSE;
				continue;
			}

			// need to watch for conversion errors for some values.
			if (!COpcUaProxyUtils::MarshalVARIANT(pvValues[ii], values[ii]->Value, pErrors[ii]))
			{
				hMasterError = S_FALSE;
				continue;
			}

			pwQualities[ii] = values[ii]->Quality;
			pftTimeStamps[ii] = COpcUaProxyUtils::GetFILETIME(values[ii]->Timestamp);

			if (pwQualities[ii] != OPC_QUALITY_GOOD)
			{
				hMasterQuality = S_FALSE;
			}
		}

		// fix any conversion issues in variants.
		COpcUaProxyUtils::FixupOutputVariants(dwCount, pvValues);

		// send the datachange callback.
		if (isRefresh || cancelId == 0)
		{
			HRESULT hResult = ipCallback->OnDataChange(
				transactionId,
				groupHandle,
				hMasterQuality,
				hMasterError,
				dwCount,
				phClient,
				pvValues,
				pwQualities,
				pftTimeStamps,
				pErrors);

            if (FAILED(hResult))
            {
		        TraceState("OnDataChange ERROR", hResult);
            }
		}

		// send the read complete callback.
		else
		{
			HRESULT hResult = ipCallback->OnReadComplete(
				transactionId,
				groupHandle,
				hMasterQuality,
				hMasterError,
				dwCount,
				phClient,
				pvValues,
				pwQualities,
				pftTimeStamps,
				pErrors);

            if (FAILED(hResult))
            {
		        TraceState("OnReadComplete ERROR", hResult);
            }
		}

		TraceState("ReadCompleted Exiting");
	}
    catch (Exception^ e)
    {
        TraceState("ReadCompleted Error", e->Message);
    }
	finally
	{
		ipCallback->Release();

		if (pvValues != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				VariantClear(&(pvValues[ii]));
			}
		}

		CoTaskMemFree(phClient);
		CoTaskMemFree(pvValues);
		CoTaskMemFree(pwQualities);
		CoTaskMemFree(pftTimeStamps);
		CoTaskMemFree(pErrors);
	}
}

// WriteComplete
void COpcUaDaProxyGroupCallback::WriteCompleted(
    int groupHandle,
    int transactionId,
    array<int>^ clientHandles,
    array<int>^ errors)
{	
	TraceState("WriteCompleted");

	DWORD dwCount = clientHandles->Length;
    OPCHANDLE* phClient = NULL;
    HRESULT* pErrors = NULL;

	IOPCDataCallback* ipCallback = NULL;

	Monitor::Enter(m_lock);

	// check if callback has been released.
	if (m_ipCallback == NULL)
	{
		return;
	}

	ipCallback = m_ipCallback;
	ipCallback->AddRef();
	
	Monitor::Exit(m_lock);

	try
	{
		// allocate return parameters.
		OpcProxy_AllocArrayToReturn(phClient, dwCount, OPCHANDLE);
		OpcProxy_AllocArrayToReturn(pErrors, dwCount, HRESULT);

		// marshal output parameters.
		HRESULT hMasterError = S_OK;

		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			phClient[ii] = clientHandles[ii];
			pErrors[ii] = errors[ii];

			if (pErrors[ii] != S_OK)
			{
				hMasterError = S_FALSE;
				continue;
			}
		}

		// send the write callback.
		ipCallback->OnWriteComplete(
			transactionId,
			groupHandle,
			hMasterError,
			dwCount,
			phClient,
			pErrors);
	}
	finally
	{
		ipCallback->Release();
		CoTaskMemFree(phClient);
		CoTaskMemFree(pErrors);
	}
}

// CancelSucceeded
void COpcUaDaProxyGroupCallback::CancelSucceeded(
    int groupHandle,
    int transactionId)
{	
	TraceState("CancelSucceeded");

	IOPCDataCallback* ipCallback = NULL;

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
		ipCallback->OnCancelComplete(groupHandle, transactionId);
	}
	finally
	{
		ipCallback->Release();
	}
}
