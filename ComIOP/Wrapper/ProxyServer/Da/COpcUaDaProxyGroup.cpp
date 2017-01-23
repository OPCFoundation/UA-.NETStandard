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
#include "COpcUaDaProxyGroup.h"
#include "COpcUaDaProxyEnumItem.h"
#include "COpcUaDaProxyServer.h"
#include "COpcUaDaProxyGroup.h"
#include "COpcUaProxyUtils.h"
#include "COpcUaDaProxyGroupCallback.h"

using namespace System;
using namespace System::Text;
using namespace System::Runtime::InteropServices;
using namespace System::Reflection;
using namespace Opc::Ua;
using namespace Opc::Ua::Com;
using namespace Opc::Ua::Com::Server;

//==========================================================================
// Local Functions

/// <summary>
/// Dumps the current state.
/// </summary>
static void TraceState(String^ context, ... array<Object^>^ args)
{
    #ifdef TRACESTATE
    COpcUaProxyUtils::TraceState("COpcUaDaProxyGroup", context, args);
	#endif
}
        
// MarshalAndReturnErrors
static HRESULT MarshalAndReturnErrors(array<int>^ errors, HRESULT** ppErrors)
{
	// allocate buffer.
	*ppErrors = OpcArrayAlloc(HRESULT, errors->Length);

	if (*ppErrors == NULL)
	{
		return E_OUTOFMEMORY;
	}
	
	// copy errors from managed array.
	HRESULT hResult = S_OK;

	for (int ii = 0; ii < errors->Length; ii++)
	{
		(*ppErrors)[ii] = errors[ii];

		if ((*ppErrors)[ii] != S_OK)
		{
			hResult = S_FALSE;
		}
	}

	// return correct status code.
	return hResult;
}

//==========================================================================
// COpcUaDaProxyGroup

// Constructor
COpcUaDaProxyGroup::COpcUaDaProxyGroup()
{
	RegisterInterface(IID_IOPCDataCallback);
	m_pServer = NULL;
}

// Constructor
COpcUaDaProxyGroup::COpcUaDaProxyGroup(COpcUaDaProxyServer* pServer, ComDaGroup^ group)
{
	TraceState("COpcUaDaProxyGroup");

	RegisterInterface(IID_IOPCDataCallback);

	m_pServer = pServer;

	GCHandle hInnerGroup = GCHandle::Alloc(group);
	m_pInnerGroup = ((IntPtr)hInnerGroup).ToPointer();
	group->Handle = (IntPtr)this;
}

// Destructor 
COpcUaDaProxyGroup::~COpcUaDaProxyGroup()
{
	TraceState("~COpcUaDaProxyGroup");

	if (m_pInnerGroup != NULL)
	{
		ComDaGroup^ group = GetInnerGroup();

		GCHandle hInnerGroup = (GCHandle)IntPtr(m_pInnerGroup);
		hInnerGroup.Free();
		m_pInnerGroup = NULL;
	}
}

// GetInnerGroup
ComDaGroup^ COpcUaDaProxyGroup::GetInnerGroup()
{
	if (m_pInnerGroup == NULL)
	{
		return nullptr;
	}

	GCHandle hInnerGroup = (GCHandle)IntPtr(m_pInnerGroup);

	if (hInnerGroup.IsAllocated)
	{
		return (ComDaGroup^)hInnerGroup.Target;
	}

	return nullptr;
}

// Delete
void COpcUaDaProxyGroup::Delete()
{
    COpcLock cLock(*this);
	m_pServer = NULL;
	UnregisterInterface(IID_IOPCDataCallback);
}

// OnAdvise
void COpcUaDaProxyGroup::OnAdvise(REFIID riid, DWORD dwCookie)
{
	COpcLock cLock(*this);

	IOPCDataCallback* ipCallback = NULL;

    if (FAILED(GetCallback(IID_IOPCDataCallback, (IUnknown**)&ipCallback)))
    {
        return;
    }

    try
    {
	    ComDaGroup^ group = GetInnerGroup();
	    COpcUaDaProxyGroupCallback^ callback = gcnew COpcUaDaProxyGroupCallback(ipCallback);
	    group->SetCallback(callback);
	    ipCallback->Release();
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "Unexpected error setting group callback.");
    }

	cLock.Unlock();
}

// OnUnadvise
void COpcUaDaProxyGroup::OnUnadvise(REFIID riid, DWORD dwCookie)
{
	COpcLock cLock(*this);

    try
    {
	    ComDaGroup^ group = GetInnerGroup();
	    group->SetCallback(nullptr);
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "Unexpected error releasing group callback.");
    }

	cLock.Unlock();
}

//=========================================================================
// IOPCGroupStateMgt

// GetState
HRESULT COpcUaDaProxyGroup::GetState(
    DWORD     * pUpdateRate, 
    BOOL      * pActive, 
    LPWSTR    * ppName,
    LONG      * pTimeBias,
    FLOAT     * pPercentDeadband,
    DWORD     * pLCID,
    OPCHANDLE * phClientGroup,
    OPCHANDLE * phServerGroup
)
{
	TraceState("GetState");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// check arguments.
	if (pUpdateRate == NULL || pActive == NULL || ppName == NULL || pTimeBias == NULL || pPercentDeadband == NULL || pLCID == NULL  || phClientGroup == NULL  || phServerGroup == NULL)
	{
		return E_INVALIDARG;
	}

	ComDaGroup^ group = GetInnerGroup();

	try
	{
		*pUpdateRate = group->ActualUpdateRate;
		*pActive = (group->Active)?1:0;
		*ppName = (LPWSTR)Marshal::StringToCoTaskMemUni(group->Name).ToPointer();
		*pTimeBias = group->TimeBias;
		*pPercentDeadband = group->Deadband;
		*pLCID = group->Lcid;
		*phClientGroup = group->ClientHandle;
		*phServerGroup = group->ServerHandle;

		return S_OK;
	}
	catch (Exception^ e)
	{
        TraceState("GetState", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// SetState
HRESULT COpcUaDaProxyGroup::SetState( 
    DWORD     * pRequestedUpdateRate, 
    DWORD     * pRevisedUpdateRate, 
    BOOL      * pActive, 
    LONG      * pTimeBias,
    FLOAT     * pPercentDeadband,
    DWORD     * pLCID,
    OPCHANDLE * phClientGroup
)
{
	TraceState("SetState");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// check arguments.
	if (pRevisedUpdateRate == NULL)
	{
		return E_INVALIDARG;
	}

	// only support default locale.
	if (pLCID != NULL && *pLCID != 0 && *pLCID != LOCALE_SYSTEM_DEFAULT)
	{
		return E_INVALIDARG;
	}

	*pRevisedUpdateRate = 0;

	ComDaGroup^ group = GetInnerGroup();

	try
	{
		HRESULT hResult = S_OK;

		// set the client handle.
        if (phClientGroup != NULL)
        {
			group->ClientHandle = *phClientGroup;
        }

		// set the time bias.
        if (pTimeBias != NULL)
        {
			group->TimeBias = *pTimeBias;
		}

		// set the update rate.
		if (pRequestedUpdateRate != NULL)
		{
			*pRevisedUpdateRate = group->SetUpdateRate(*pRequestedUpdateRate);

			if (*pRevisedUpdateRate != *pRequestedUpdateRate)
			{
				hResult = OPC_S_UNSUPPORTEDRATE;
			}
		}

		// set the deadband.
		if (pPercentDeadband != NULL)
		{
			group->SetDeadband(*pPercentDeadband);
		}

		// set the locale id.
		if (pLCID != NULL)
		{
			group->Lcid = *pLCID;
		}

		// set the deadband.
		if (pActive != NULL)
		{
			group->SetActive((*pActive) != 0);
		}

		// return necessary success code.
		return hResult;
	}
	catch (Exception^ e)
	{
        TraceState("SetState", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// SetName
HRESULT COpcUaDaProxyGroup::SetName( 
    LPCWSTR szName
)
{
	TraceState("SetName");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// check arguments.
	if (szName == NULL)
	{
		return E_INVALIDARG;
	}

	ComDaGroup^ group = GetInnerGroup();

	try
	{
		// unmarshal the the name.
		String^ groupName = Marshal::PtrToStringUni((IntPtr)(LPWSTR)szName);

		// update the name.
		group->SetName(groupName);

		// all done.
		return S_OK;
	}
	catch (Exception^ e)
	{
        TraceState("SetName", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// CloneGroup
HRESULT COpcUaDaProxyGroup::CloneGroup(
    LPCWSTR     szName,
    REFIID      riid,
    LPUNKNOWN * ppUnk
)
{    
	TraceState("CloneGroup");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// check arguments.
	if (szName == NULL)
	{
		return E_INVALIDARG;
	}

	try
	{
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal the the name.
		String^ groupName = Marshal::PtrToStringUni((IntPtr)(LPWSTR)szName);

		// update the name.
		ComDaGroup^ clone = group->Clone(groupName);

		// wrap the group.
		COpcUaDaProxyGroup* pGroup = new COpcUaDaProxyGroup(m_pServer, clone);

		// fetch required interface.
		if (FAILED(pGroup->QueryInterface(riid, (void**)ppUnk)))
		{
			clone->Remove();
			return E_NOINTERFACE;
		}

		// all done.
		return S_OK;
	}
	catch (Exception^ e)
	{
        TraceState("CloneGroup", e->Message);
		return Marshal::GetHRForException(e);
	}
}

//=========================================================================
// IOPCGroupStateMgt2

// SetKeepAlive
HRESULT COpcUaDaProxyGroup::SetKeepAlive( 
    DWORD   dwKeepAliveTime,
    DWORD * pdwRevisedKeepAliveTime 
)
{
	TraceState("SetKeepAlive");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// check arguments.
	if (pdwRevisedKeepAliveTime == NULL)
	{
		return E_INVALIDARG;
	}

	ComDaGroup^ group = GetInnerGroup();

	try
	{
		// update the name.
		*pdwRevisedKeepAliveTime = group->SetKeepAliveTime(dwKeepAliveTime);

		if (*pdwRevisedKeepAliveTime != dwKeepAliveTime)
		{
			return OPC_S_UNSUPPORTEDRATE;
		}

		// all done.
		return S_OK;
	}
	catch (Exception^ e)
	{
        TraceState("SetKeepAlive", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// GetKeepAlive
HRESULT COpcUaDaProxyGroup::GetKeepAlive( 
    DWORD * pdwKeepAliveTime 
)
{
	TraceState("GetKeepAlive");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// check arguments.
	if (pdwKeepAliveTime == NULL)
	{
		return E_INVALIDARG;
	}

	ComDaGroup^ group = GetInnerGroup();

	try
	{
		// get the value.
		*pdwKeepAliveTime = group->KeepAliveTime;

		// all done.
		return S_OK;
	}
	catch (Exception^ e)
	{
        TraceState("GetKeepAlive", e->Message);
		return Marshal::GetHRForException(e);
	}
}

//=========================================================================
// IOPCItemMgt

// AddItems
HRESULT COpcUaDaProxyGroup::AddItems( 
    DWORD            dwCount,
    OPCITEMDEF     * pItemArray,
    OPCITEMRESULT ** ppAddResults,
    HRESULT       ** ppErrors
)
{
	TraceState("AddItems");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || pItemArray == NULL || ppAddResults == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppAddResults = NULL;
	*ppErrors = NULL;

    OPCITEMRESULT* pAddResults = NULL;
    HRESULT* pErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<ComDaCreateItemRequest^>^ requests = gcnew array<ComDaCreateItemRequest^>(dwCount);

		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			requests[ii] = gcnew ComDaCreateItemRequest();
			requests[ii]->ItemId = Marshal::PtrToStringUni((IntPtr)pItemArray[ii].szItemID);
			requests[ii]->AccessPath = Marshal::PtrToStringUni((IntPtr)pItemArray[ii].szAccessPath);
			requests[ii]->RequestedDataType = pItemArray[ii].vtRequestedDataType;
			requests[ii]->Active = pItemArray[ii].bActive != 0;
			requests[ii]->ClientHandle = pItemArray[ii].hClient;
		}

		// create the items.
		group->CreateItems(requests, false);
		
		// allocate return parameters.
		OpcProxy_AllocArrayToReturn(pAddResults, dwCount, OPCITEMRESULT);
		OpcProxy_AllocArrayToReturn(pErrors, dwCount, HRESULT);

		// marshal output paramaters
		HRESULT hResult = S_OK;
			
		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			pErrors[ii] = requests[ii]->Error;

			if (FAILED(pErrors[ii]))
			{
				hResult = S_FALSE;
				continue;
			}

			pAddResults[ii].hServer = requests[ii]->ServerHandle;
			pAddResults[ii].vtCanonicalDataType = requests[ii]->CanonicalDataType;
			pAddResults[ii].dwAccessRights = requests[ii]->AccessRights;
			pAddResults[ii].dwBlobSize = 0;
			pAddResults[ii].pBlob = NULL;
		}

		*ppAddResults = pAddResults;
		*ppErrors = pErrors;

		return hResult;
	}
	catch (Exception^ e)
	{
		CoTaskMemFree(pAddResults);
		CoTaskMemFree(pErrors);

        TraceState("AddItems", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// ValidateItems
HRESULT COpcUaDaProxyGroup::ValidateItems( 
    DWORD             dwCount,
    OPCITEMDEF      * pItemArray,
    BOOL              bBlobUpdate,
    OPCITEMRESULT  ** ppValidationResults,
    HRESULT        ** ppErrors
)
{
	TraceState("ValidateItems");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || pItemArray == NULL || ppValidationResults == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppValidationResults = NULL;
	*ppErrors = NULL;

    OPCITEMRESULT* pValidationResults = NULL;
    HRESULT* pErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<ComDaCreateItemRequest^>^ requests = gcnew array<ComDaCreateItemRequest^>(dwCount);

		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			requests[ii] = gcnew ComDaCreateItemRequest();
			requests[ii]->ItemId = Marshal::PtrToStringUni((IntPtr)pItemArray[ii].szItemID);
			requests[ii]->AccessPath = Marshal::PtrToStringUni((IntPtr)pItemArray[ii].szAccessPath);
			requests[ii]->RequestedDataType = pItemArray[ii].vtRequestedDataType;
			requests[ii]->Active = pItemArray[ii].bActive != 0;
			requests[ii]->ClientHandle = pItemArray[ii].hClient;
		}

		// create the items.
		group->CreateItems(requests, true);
		
		// allocate return parameters.
		OpcProxy_AllocArrayToReturn(pValidationResults, dwCount, OPCITEMRESULT);
		OpcProxy_AllocArrayToReturn(pErrors, dwCount, HRESULT);

		// marshal output paramaters
		HRESULT hResult = S_OK;
			
		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			pErrors[ii] = requests[ii]->Error;

			if (FAILED(pErrors[ii]))
			{
				hResult = S_FALSE;
				continue;
			}

			pValidationResults[ii].hServer = requests[ii]->ServerHandle;
			pValidationResults[ii].vtCanonicalDataType = requests[ii]->CanonicalDataType;
			pValidationResults[ii].dwAccessRights = requests[ii]->AccessRights;
			pValidationResults[ii].dwBlobSize = 0;
			pValidationResults[ii].pBlob = NULL;
		}

		*ppValidationResults = pValidationResults;
		*ppErrors = pErrors;

		return hResult;
	}
	catch (Exception^ e)
	{
		CoTaskMemFree(pValidationResults);
		CoTaskMemFree(pErrors);

        TraceState("ValidateItems", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// RemoveItems
HRESULT COpcUaDaProxyGroup::RemoveItems( 
    DWORD        dwCount,
    OPCHANDLE  * phServer,
    HRESULT   ** ppErrors
)
{
	TraceState("RemoveItems");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		// create the items.
		array<int>^ results = group->DeleteItems(serverHandles);
		
		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("RemoveItems", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// SetActiveState
HRESULT COpcUaDaProxyGroup::SetActiveState(
    DWORD        dwCount,
    OPCHANDLE  * phServer,
    BOOL         bActive, 
    HRESULT   ** ppErrors
)
{
	TraceState("SetActiveState");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		// create the items.
		array<int>^ results = group->SetActive(serverHandles, bActive != 0);
				
		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("SetActiveState", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// SetClientHandles
HRESULT COpcUaDaProxyGroup::SetClientHandles(
    DWORD        dwCount,
    OPCHANDLE  * phServer,
    OPCHANDLE  * phClient,
    HRESULT   ** ppErrors
)
{
	TraceState("SetClientHandles");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || phClient == NULL  || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		array<int>^ clientHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phClient, clientHandles, 0, dwCount);

		// create the items.
		array<int>^ results = group->SetClientHandles(serverHandles, clientHandles);
						
		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("SetClientHandles", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// SetDatatypes
HRESULT COpcUaDaProxyGroup::SetDatatypes(
    DWORD        dwCount,
    OPCHANDLE  * phServer,
    VARTYPE    * pRequestedDatatypes,
    HRESULT   ** ppErrors
)
{
	TraceState("SetDatatypes");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || pRequestedDatatypes == NULL  || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);
		
		array<short>^ dataTypes = gcnew array<short>(dwCount);
		Marshal::Copy((IntPtr)pRequestedDatatypes, dataTypes, 0, dwCount);

		// create the items.
		array<int>^ results = group->SetDataTypes(serverHandles, dataTypes);
						
		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("SetDatatypes", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// CreateEnumerator
HRESULT COpcUaDaProxyGroup::CreateEnumerator(
    REFIID      riid,
    LPUNKNOWN * ppUnk
)
{	
	TraceState("CreateEnumerator");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (ppUnk == 0)
	{
		return E_INVALIDARG;
	}

	*ppUnk = NULL;
	OPCITEMATTRIBUTES* pAttributes = NULL;

	HRESULT hResult = S_OK;
	DWORD dwCount = 0;
	COpcUaDaProxyEnumItem* pEnum = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// fetch the attributes.
		array<OpcRcw::Da::OPCITEMATTRIBUTES>^ attributes = group->GetItemAttributes();
		dwCount = attributes->Length;

		if (dwCount > 0)
		{
			// marhsal results.
			OpcProxy_AllocArrayToReturn(pAttributes, dwCount, OPCITEMATTRIBUTES);

			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				pAttributes[ii].szItemID = (LPWSTR)Marshal::StringToCoTaskMemUni(attributes[ii].szItemID).ToPointer();
				pAttributes[ii].szAccessPath = (LPWSTR)Marshal::StringToCoTaskMemUni(attributes[ii].szAccessPath).ToPointer();
				pAttributes[ii].hServer = attributes[ii].hServer;
				pAttributes[ii].hClient = attributes[ii].hClient;
				pAttributes[ii].bActive = attributes[ii].bActive;
				pAttributes[ii].vtRequestedDataType = attributes[ii].vtRequestedDataType;
				pAttributes[ii].vtCanonicalDataType = attributes[ii].vtCanonicalDataType;
				pAttributes[ii].dwAccessRights = attributes[ii].dwAccessRights;
				pAttributes[ii].dwEUType = (OPCEUTYPE)(int)attributes[ii].dwEUType;
				pAttributes[ii].dwBlobSize = 0;
				pAttributes[ii].pBlob = NULL;			
				
				VariantInit(&(pAttributes[ii].vEUInfo));
				COpcUaProxyUtils::MarshalVARIANT(pAttributes[ii].vEUInfo, attributes[ii].vEUInfo, hResult);
			}

			// create enumerator.
			pEnum = new COpcUaDaProxyEnumItem(dwCount, pAttributes);
			pAttributes = NULL;
		}
		else
		{
			// create empty enumerator.
			pEnum = new COpcUaDaProxyEnumItem();
		}

		// query for interface.
		hResult = pEnum->QueryInterface(riid, (void**)ppUnk);

		// release local reference.
		pEnum->Release();

		if (FAILED(hResult))
		{
			return hResult;
		}

		return (dwCount > 0)?S_OK:S_FALSE;
	}
	catch (Exception^ e)
	{
		if (pAttributes != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				VariantClear(&(pAttributes[ii].vEUInfo));
				OpcFree(pAttributes[ii].szItemID);
				OpcFree(pAttributes[ii].szAccessPath);
			}
		}

		CoTaskMemFree(pAttributes);

        TraceState("CreateEnumerator", e->Message);
		return Marshal::GetHRForException(e);
	}
}

//=========================================================================
// IOPCAsyncIO2

// Read
HRESULT COpcUaDaProxyGroup::Read(
    DWORD           dwCount,
    OPCHANDLE     * phServer,
    DWORD           dwTransactionID,
    DWORD         * pdwCancelID,
    HRESULT      ** ppErrors
)
{
	TraceState("Read");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || pdwCancelID == NULL  || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	COpcLock cLock(*this);

    // check for an active connection.
    if (!IsConnected(IID_IOPCDataCallback))
    {
        return CONNECT_E_NOCONNECTION;
    }

	cLock.Unlock();

	*pdwCancelID = NULL;
	*ppErrors = NULL;

	int cancelId = 0;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		// create the items.
		array<int>^ results = group->AsyncRead(0, dwTransactionID, serverHandles, cancelId);
						
		*pdwCancelID = cancelId;

		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("Read", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// Write
HRESULT COpcUaDaProxyGroup::Write(
    DWORD           dwCount, 
    OPCHANDLE     * phServer,
    VARIANT       * pItemValues, 
    DWORD           dwTransactionID,
    DWORD         * pdwCancelID,
    HRESULT      ** ppErrors
)
{
	TraceState("Write");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || pItemValues == NULL || pdwCancelID == NULL  || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	COpcLock cLock(*this);

    // check for an active connection.
    if (!IsConnected(IID_IOPCDataCallback))
    {
        return CONNECT_E_NOCONNECTION;
    }

	cLock.Unlock();

	*pdwCancelID = NULL;
	*ppErrors = NULL;

	int cancelId = 0;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// fixup conversion problems with input variants.
		COpcUaProxyUtils::FixupInputVariants(dwCount, pItemValues);

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		array<DaValue^>^ values = gcnew array<DaValue^>(dwCount);
		
		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			values[ii] = gcnew DaValue();
			values[ii]->Value = Marshal::GetObjectForNativeVariant((IntPtr)&(pItemValues[ii]));
			values[ii]->Quality = OPC_QUALITY_GOOD;
			values[ii]->Timestamp = DateTime::MinValue;
		}

		// create the items.
		array<int>^ results = group->AsyncWrite(dwTransactionID, serverHandles, values, cancelId);
						
		*pdwCancelID = cancelId;

		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("Write", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// Refresh2
HRESULT COpcUaDaProxyGroup::Refresh2(
    OPCDATASOURCE   dwSource,
    DWORD           dwTransactionID,
    DWORD         * pdwCancelID
)
{
	TraceState("Refresh2");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwSource == NULL || pdwCancelID == NULL)
	{
		return E_INVALIDARG;
	}

	COpcLock cLock(*this);

    // check for an active connection.
    if (!IsConnected(IID_IOPCDataCallback))
    {
        return CONNECT_E_NOCONNECTION;
    }

	cLock.Unlock();

	*pdwCancelID = NULL;

	int cancelId = 0;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		unsigned int maxAge = (dwSource == OPC_DS_DEVICE)?0:UInt32::MaxValue;

		// create the items.
		group->Refresh(maxAge, dwTransactionID, cancelId);

		*pdwCancelID = cancelId;

		return S_OK;
	}
	catch (Exception^ e)
	{
        TraceState("Refresh2", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// Cancel2
HRESULT COpcUaDaProxyGroup::Cancel2(DWORD dwCancelID)
{
	TraceState("Cancel2");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	COpcLock cLock(*this);

    // check for an active connection.
    if (!IsConnected(IID_IOPCDataCallback))
    {
        return CONNECT_E_NOCONNECTION;
    }

	cLock.Unlock();

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// cancel the request.
		if (!group->Cancel(dwCancelID))
		{
			return E_FAIL;
		}

		return S_OK;
	}
	catch (Exception^ e)
	{
        TraceState("Cancel2", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// SetEnable
HRESULT COpcUaDaProxyGroup::SetEnable(BOOL bEnable)
{
	TraceState("SetEnable");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	COpcLock cLock(*this);

    // check for an active connection.
    if (!IsConnected(IID_IOPCDataCallback))
    {
        return CONNECT_E_NOCONNECTION;
    }

	cLock.Unlock();

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// set the enable state.
		group->SetEnabled(bEnable != 0);

		// all done.
		return S_OK;
	}
	catch (Exception^ e)
	{
        TraceState("SetEnable", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// GetEnable
HRESULT COpcUaDaProxyGroup::GetEnable(BOOL* pbEnable)
{
	TraceState("GetEnable");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	if (pbEnable == NULL)
	{
		return E_INVALIDARG;
	}

	COpcLock cLock(*this);

    // check for an active connection.
    if (!IsConnected(IID_IOPCDataCallback))
    {
        return CONNECT_E_NOCONNECTION;
    }

	cLock.Unlock();
	
	*pbEnable = 0;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// get the enable state.
		*pbEnable = (group->Enabled)?1:0;

		// all done.
		return S_OK;
	}
	catch (Exception^ e)
	{
        TraceState("GetEnable", e->Message);
		return Marshal::GetHRForException(e);
	}
}

//=========================================================================
// IOPCItemDeadbandMgt

// SetItemDeadband
HRESULT COpcUaDaProxyGroup::SetItemDeadband( 
    DWORD 	    dwCount,
    OPCHANDLE * phServer,
    FLOAT     * pPercentDeadband,
    HRESULT  ** ppErrors
)
{
	TraceState("SetItemDeadband");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || pPercentDeadband == NULL  || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		array<float>^ deadbands = gcnew array<float>(dwCount);
		Marshal::Copy((IntPtr)pPercentDeadband, deadbands, 0, dwCount);

		// create the items.
		array<int>^ results = group->SetItemDeadbands(serverHandles, deadbands);
						
		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("SetItemDeadband", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// GetItemDeadband
HRESULT COpcUaDaProxyGroup::GetItemDeadband( 
    DWORD 	    dwCount,
    OPCHANDLE * phServer,
    FLOAT    ** ppPercentDeadband,
    HRESULT  ** ppErrors
)
{
	TraceState("GetItemDeadband");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || ppPercentDeadband == NULL  || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppPercentDeadband = NULL;
	*ppErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		// get the deadbands
		array<float>^ deadbands = gcnew array<float>(dwCount);
		array<int>^ results = group->GetItemDeadbands(serverHandles, deadbands);

		// copy results.
		*ppPercentDeadband = OpcArrayAlloc(FLOAT, dwCount);

		if (*ppPercentDeadband == NULL)
		{
			return E_OUTOFMEMORY;
		}

		Marshal::Copy(deadbands, 0, (IntPtr)*ppPercentDeadband, dwCount);

		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("GetItemDeadband", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// ClearItemDeadband
HRESULT COpcUaDaProxyGroup::ClearItemDeadband(
    DWORD       dwCount,
    OPCHANDLE * phServer,
    HRESULT  ** ppErrors
)
{
	TraceState("ClearItemDeadband");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		// create the items.
		array<int>^ results = group->ClearItemDeadbands(serverHandles);
						
		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("ClearItemDeadband", e->Message);
		return Marshal::GetHRForException(e);
	}
}

//=========================================================================
// IOPCItemSamplingMgt

// SetItemSamplingRate
HRESULT COpcUaDaProxyGroup::SetItemSamplingRate(
    DWORD 	    dwCount,
    OPCHANDLE * phServer,
    DWORD     * pdwRequestedSamplingRate,
    DWORD    ** ppdwRevisedSamplingRate,
    HRESULT  ** ppErrors
)
{
	TraceState("SetItemSamplingRate");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || pdwRequestedSamplingRate == NULL || ppdwRevisedSamplingRate == NULL  || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppdwRevisedSamplingRate = 0;
	*ppErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		array<int>^ samplingRates = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)pdwRequestedSamplingRate, samplingRates, 0, dwCount);

		// set the sampling rates.
		array<int>^ revisedSamplingRates = gcnew array<int>(dwCount);
		array<int>^ results = group->SetItemSamplingRates(serverHandles, samplingRates, revisedSamplingRates);
						
		// copy results.
		*ppdwRevisedSamplingRate = OpcArrayAlloc(DWORD, dwCount);

		if (*ppdwRevisedSamplingRate == NULL)
		{
			return E_OUTOFMEMORY;
		}

		Marshal::Copy(revisedSamplingRates, 0, (IntPtr)*ppdwRevisedSamplingRate, dwCount);

		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("SetItemSamplingRate", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// GetItemSamplingRate
HRESULT COpcUaDaProxyGroup::GetItemSamplingRate(
    DWORD 	    dwCount,
    OPCHANDLE * phServer,
    DWORD    ** ppdwSamplingRate,
    HRESULT  ** ppErrors
)
{
	TraceState("GetItemSamplingRate");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || ppdwSamplingRate == NULL  || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppdwSamplingRate = 0;
	*ppErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		// get the sampling rates.
		array<int>^ samplingRates = gcnew array<int>(dwCount);
		array<int>^ results = group->GetItemSamplingRates(serverHandles, samplingRates);
						
		// copy results.
		*ppdwSamplingRate = OpcArrayAlloc(DWORD, dwCount);

		if (*ppdwSamplingRate == NULL)
		{
			return E_OUTOFMEMORY;
		}

		Marshal::Copy(samplingRates, 0, (IntPtr)*ppdwSamplingRate, dwCount);

		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("GetItemSamplingRate", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// ClearItemSamplingRate
HRESULT COpcUaDaProxyGroup::ClearItemSamplingRate(
    DWORD 	    dwCount,
    OPCHANDLE * phServer,
    HRESULT  ** ppErrors
)
{
	TraceState("ClearItemSamplingRate");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		// get the sampling rates.
		array<int>^ results = group->ClearItemSamplingRates(serverHandles);
						
		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("ClearItemSamplingRate", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// SetItemBufferEnable
HRESULT COpcUaDaProxyGroup::SetItemBufferEnable(
    DWORD       dwCount, 
    OPCHANDLE * phServer, 
    BOOL      * pbEnable,
    HRESULT  ** ppErrors
)
{
	TraceState("SetItemBufferEnable");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || pbEnable == NULL  || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		array<int>^ bufferEnabled = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)pbEnable, bufferEnabled, 0, dwCount);

		// set the buffer enabled state.
		array<int>^ results = group->SetItemBufferEnabled(serverHandles, bufferEnabled);
						
		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("SetItemBufferEnable", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// GetItemBufferEnable
HRESULT COpcUaDaProxyGroup::GetItemBufferEnable(
    DWORD       dwCount, 
    OPCHANDLE * phServer, 
    BOOL     ** ppbEnable,
    HRESULT  ** ppErrors
)
{
	TraceState("GetItemBufferEnable");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || ppbEnable == NULL  || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppbEnable = NULL;
	*ppErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		// get the buffer enabled state.
		array<int>^ bufferEnabled = gcnew array<int>(dwCount);
		array<int>^ results = group->GetItemBufferEnabled(serverHandles, bufferEnabled);
						
		// copy results.
		*ppbEnable = OpcArrayAlloc(BOOL, dwCount);

		if (*ppbEnable == NULL)
		{
			return E_OUTOFMEMORY;
		}

		Marshal::Copy(bufferEnabled, 0, (IntPtr)*ppbEnable, dwCount);

		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("GetItemBufferEnable", e->Message);
		return Marshal::GetHRForException(e);
	}
}

//=========================================================================
// IOPCSyncIO2

// Read
HRESULT COpcUaDaProxyGroup::Read(
    OPCDATASOURCE   dwSource,
    DWORD           dwCount, 
    OPCHANDLE     * phServer, 
    OPCITEMSTATE ** ppItemValues,
    HRESULT      ** ppErrors
)
{
	TraceState("Read");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || ppItemValues == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppItemValues = NULL;
	*ppErrors = NULL;

    OPCITEMSTATE* pItemValues = NULL;
    HRESULT* pErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		unsigned int maxAge = (dwSource == OPC_DS_DEVICE)?0:UInt32::MaxValue;

		// create the items.
		array<int>^ clientHandles = gcnew array<int>(dwCount);
		array<DaValue^>^ values = group->SyncRead(maxAge, serverHandles, false, clientHandles);
		
		// allocate return parameters.
		OpcProxy_AllocArrayToReturn(pItemValues, dwCount, OPCITEMSTATE);
		OpcProxy_AllocArrayToReturn(pErrors, dwCount, HRESULT);

		// marshal output paramaters
		HRESULT hResult = S_OK;

		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			pErrors[ii] = values[ii]->Error;

			if (values[ii]->Error < 0)
			{
				hResult = S_FALSE;
				continue;
			}

			// need to watch for conversion errors for some values.
			if (!COpcUaProxyUtils::MarshalVARIANT(pItemValues[ii].vDataValue, values[ii]->Value, pErrors[ii]))
			{
				hResult = S_FALSE;
				continue;
			}

			pItemValues[ii].hClient = clientHandles[ii];
			pItemValues[ii].wQuality = values[ii]->Quality;
			pItemValues[ii].ftTimeStamp = COpcUaProxyUtils::GetFILETIME(values[ii]->Timestamp);
		}

		*ppItemValues = pItemValues;
		*ppErrors = pErrors;

		// fix any conversion issues in variants.
		COpcUaProxyUtils::FixupOutputVariants(dwCount, *ppItemValues);

		// return results.
		return hResult;
	}
	catch (Exception^ e)
	{
		if (pItemValues != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				VariantClear(&(pItemValues[ii].vDataValue));
			}
		}

		CoTaskMemFree(pItemValues);
		CoTaskMemFree(pErrors);

        TraceState("Read", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// Write
HRESULT COpcUaDaProxyGroup::Write(
    DWORD        dwCount, 
    OPCHANDLE  * phServer, 
    VARIANT    * pItemValues, 
    HRESULT   ** ppErrors
)
{
	TraceState("Write");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || pItemValues == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppErrors = NULL;

    HRESULT* pErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// fix any conversion issues in variants.
		COpcUaProxyUtils::FixupInputVariants(dwCount, pItemValues);

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		array<DaValue^>^ values = gcnew array<DaValue^>(dwCount);

		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			values[ii] = gcnew DaValue();
			values[ii]->Value = Marshal::GetObjectForNativeVariant((IntPtr)&(pItemValues[ii]));
			values[ii]->Quality = OPC_QUALITY_GOOD;
			values[ii]->Timestamp = DateTime::MinValue;
		}

		// write values.
		array<int>^ results = group->SyncWrite(serverHandles, values);
						
		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("Write", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// ReadMaxAge
HRESULT COpcUaDaProxyGroup::ReadMaxAge(
    DWORD         dwCount, 
    OPCHANDLE   * phServer, 
    DWORD       * pdwMaxAge,
    VARIANT    ** ppvValues,
    WORD       ** ppwQualities,
    ::FILETIME ** ppftTimeStamps,
    HRESULT    ** ppErrors
)
{
	TraceState("ReadMaxAge");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

    // validate arguments.
	if (dwCount == 0 || phServer == NULL || pdwMaxAge == NULL || ppvValues == NULL || ppwQualities == NULL || ppftTimeStamps == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppvValues = NULL;
	*ppwQualities = NULL;
	*ppftTimeStamps = NULL;
	*ppErrors = NULL;

    VARIANT* pvValues = NULL;
    WORD* pwQualities = NULL;
    ::FILETIME* pftTimeStamps = NULL;
    HRESULT* pErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		// select the shortest max age.
		unsigned int maxAge = UInt32::MaxValue;

		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			if (maxAge > pdwMaxAge[ii])
			{
				maxAge = pdwMaxAge[ii];
			}
		}

		// create the items.
		array<DaValue^>^ values = group->SyncRead(maxAge, serverHandles, false, nullptr);
		
		// allocate return parameters.
		OpcProxy_AllocArrayToReturn(pvValues, dwCount, VARIANT);
		OpcProxy_AllocArrayToReturn(pwQualities, dwCount, WORD);
		OpcProxy_AllocArrayToReturn(pftTimeStamps, dwCount, ::FILETIME);
		OpcProxy_AllocArrayToReturn(pErrors, dwCount, HRESULT);

		// marshal output paramaters
		HRESULT hResult = S_OK;

		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			pErrors[ii] = values[ii]->Error;

			if (values[ii]->Error < 0)
			{
				hResult = S_FALSE;
				continue;
			}

			// need to watch for conversion errors for some values.
			if (!COpcUaProxyUtils::MarshalVARIANT(pvValues[ii], values[ii]->Value, pErrors[ii]))
			{
				hResult = S_FALSE;
				continue;
			}
		 
			pwQualities[ii] = values[ii]->Quality;
			pftTimeStamps[ii] = COpcUaProxyUtils::GetFILETIME(values[ii]->Timestamp);
		}

		*ppvValues = pvValues;
		*ppwQualities = pwQualities;
		*ppftTimeStamps = pftTimeStamps;
		*ppErrors = pErrors;

		// fix any conversion issues in variants.
		COpcUaProxyUtils::FixupOutputVariants(dwCount, *ppvValues);

		// return results.
		return hResult;
	}
	catch (Exception^ e)
	{
		if (pvValues != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				VariantClear(&(pvValues[ii]));
			}
		}

		CoTaskMemFree(pvValues);
		CoTaskMemFree(pwQualities);
		CoTaskMemFree(pftTimeStamps);
		CoTaskMemFree(pErrors);

        TraceState("ReadMaxAge", e->Message);
		return Marshal::GetHRForException(e);
	}
}

// WriteVQT
HRESULT COpcUaDaProxyGroup::WriteVQT(
    DWORD         dwCount, 
    OPCHANDLE  *  phServer, 
    OPCITEMVQT *  pItemVQT,
    HRESULT    ** ppErrors
)
{
	TraceState("WriteVQT");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || pItemVQT == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppErrors = NULL;

    HRESULT* pErrors = NULL;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// fix any conversion issues in variants.
		COpcUaProxyUtils::FixupInputVariants(dwCount, pItemVQT);

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		array<DaValue^>^ values = gcnew array<DaValue^>(dwCount);

		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			values[ii] = gcnew DaValue();
			values[ii]->Value = Marshal::GetObjectForNativeVariant((IntPtr)&(pItemVQT[ii].vDataValue));
			values[ii]->Quality = OPC_QUALITY_GOOD;
			values[ii]->Timestamp = DateTime::MinValue;

			if (pItemVQT[ii].bQualitySpecified != 0)
			{
				values[ii]->Quality = pItemVQT[ii].wQuality;
			}

			if (pItemVQT[ii].bTimeStampSpecified != 0)
			{
				values[ii]->Timestamp = ComUtils::GetDateTime((IntPtr)&(pItemVQT[ii].ftTimeStamp));
			}
		}

		// write values.
		array<int>^ results = group->SyncWrite(serverHandles, values);
						
		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("WriteVQT", e->Message);
		return Marshal::GetHRForException(e);
	};
}

//=========================================================================
// IOPCAsyncIO3

HRESULT COpcUaDaProxyGroup::ReadMaxAge(
    DWORD       dwCount, 
    OPCHANDLE * phServer,
    DWORD     * pdwMaxAge,
    DWORD       dwTransactionID,
    DWORD     * pdwCancelID,
    HRESULT  ** ppErrors
)
{
	TraceState("ReadMaxAge");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || pdwCancelID == NULL || pdwMaxAge == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	COpcLock cLock(*this);

    // check for an active connection.
    if (!IsConnected(IID_IOPCDataCallback))
    {
        return CONNECT_E_NOCONNECTION;
    }

	cLock.Unlock();

	*pdwCancelID = NULL;
	*ppErrors = NULL;

	int cancelId = 0;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		// select the shortest max age.
		unsigned int maxAge = UInt32::MaxValue;

		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			if (maxAge > pdwMaxAge[ii])
			{
				maxAge = pdwMaxAge[ii];
			}
		}

		// create the items.
		array<int>^ results = group->AsyncRead(maxAge, dwTransactionID, serverHandles, cancelId);
						
		*pdwCancelID = cancelId;

		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("ReadMaxAge", e->Message);
		return Marshal::GetHRForException(e);
	}
}

HRESULT COpcUaDaProxyGroup::WriteVQT(
    DWORD        dwCount, 
    OPCHANDLE  * phServer,
    OPCITEMVQT * pItemVQT,
    DWORD        dwTransactionID,
    DWORD      * pdwCancelID,
    HRESULT   ** ppErrors
)
{
	TraceState("WriteVQT");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (dwCount == 0 || phServer == NULL || pItemVQT == NULL || pdwCancelID == NULL  || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	COpcLock cLock(*this);

    // check for an active connection.
    if (!IsConnected(IID_IOPCDataCallback))
    {
        return CONNECT_E_NOCONNECTION;
    }

	cLock.Unlock();

	*pdwCancelID = NULL;
	*ppErrors = NULL;

	int cancelId = 0;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// fixup conversion problems with input variants.
		COpcUaProxyUtils::FixupInputVariants(dwCount, pItemVQT);

		// unmarshal input parameters.
		array<int>^ serverHandles = gcnew array<int>(dwCount);
		Marshal::Copy((IntPtr)phServer, serverHandles, 0, dwCount);

		array<DaValue^>^ values = gcnew array<DaValue^>(dwCount);
		
		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			values[ii] = gcnew DaValue();
			values[ii]->Value = Marshal::GetObjectForNativeVariant((IntPtr)&(pItemVQT[ii].vDataValue));
			values[ii]->Quality = OPC_QUALITY_GOOD;
			values[ii]->Timestamp = DateTime::MinValue;

			if (pItemVQT[ii].bQualitySpecified != 0)
			{
				values[ii]->Quality = pItemVQT[ii].wQuality;
			}

			if (pItemVQT[ii].bTimeStampSpecified != 0)
			{
				values[ii]->Timestamp = ComUtils::GetDateTime((IntPtr)&(pItemVQT[ii].ftTimeStamp));
			}
		}

		// create the items.
		array<int>^ results = group->AsyncWrite(dwTransactionID, serverHandles, values, cancelId);
						
		*pdwCancelID = cancelId;

		// return results.
		return MarshalAndReturnErrors(results, ppErrors);
	}
	catch (Exception^ e)
	{
        TraceState("WriteVQT", e->Message);
		return Marshal::GetHRForException(e);
	}
}

HRESULT COpcUaDaProxyGroup::RefreshMaxAge(
    DWORD   dwMaxAge,
    DWORD   dwTransactionID,
    DWORD * pdwCancelID
)
{
	TraceState("RefreshMaxAge");

	// check if group has been deleted.
	if (m_pServer == NULL)
	{
		return E_FAIL;
	}

	// validate arguments.
	if (pdwCancelID == NULL)
	{
		return E_INVALIDARG;
	}

	COpcLock cLock(*this);

    // check for an active connection.
    if (!IsConnected(IID_IOPCDataCallback))
    {
        return CONNECT_E_NOCONNECTION;
    }

	cLock.Unlock();

	*pdwCancelID = NULL;

	int cancelId = 0;

	try
	{
		// get inner group.
		ComDaGroup^ group = GetInnerGroup();

		// create the items.
		group->Refresh(dwMaxAge, dwTransactionID, cancelId);

		*pdwCancelID = cancelId;

		return S_OK;
	}
	catch (Exception^ e)
	{
        TraceState("RefreshMaxAge", e->Message);
		return Marshal::GetHRForException(e);
	}
}
