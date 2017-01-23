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

#include "COpcUaHdaProxyServer.h"
#include "COpcUaProxyUtils.h"
#include "COpcUaHdaProxyBrowser.h"
#include "COpcUaHdaProxyDataCallback.h"

using namespace System;
using namespace System::Text;
using namespace System::Runtime::InteropServices;
using namespace System::Reflection;
using namespace System::Collections::Generic;
using namespace System::Security::Cryptography::X509Certificates;
using namespace Opc::Ua;
using namespace Opc::Ua::Com;
using namespace Opc::Ua::Com::Server;

//==============================================================================
// Static Functions

/// <summary>
/// Writes a trace message.
/// </summary>
static void TraceState(String^ context, ... array<Object^>^ args)
{
    #ifdef TRACESTATE
	COpcUaProxyUtils::TraceState("COpcUaHdaProxyServer", context, args);
	#endif
}

struct OpcHdaAttributeDesc
{
    DWORD   dwID;
    VARTYPE vtDataType;
    LPCWSTR szName;
    LPCWSTR szDescription;
};

OpcHdaAttributeDesc g_AttributeTable[] = 
{
    { OPCHDA_DATA_TYPE,          VT_I2,   OPCHDA_ATTRNAME_DATA_TYPE,          L"Specifies the data type for the item." },
    { OPCHDA_DESCRIPTION,        VT_BSTR, OPCHDA_ATTRNAME_DESCRIPTION,        L"Describes the item." },
    { OPCHDA_ENG_UNITS,          VT_BSTR, OPCHDA_ATTRNAME_ENG_UNITS,          L"The label to use in displays to define the units for the item (e.g., kg/sec)." },
    { OPCHDA_STEPPED,            VT_BOOL, OPCHDA_ATTRNAME_STEPPED,            L"Whether data from the history repository should be displayed as interpolated or stepped."  },
	{ OPCHDA_ARCHIVING,          VT_BOOL, OPCHDA_ATTRNAME_ARCHIVING,          L"Indicates whether historian is recording data for this item."  },
    { OPCHDA_DERIVE_EQUATION,    VT_BSTR, OPCHDA_ATTRNAME_DERIVE_EQUATION,    L"Specifies the equation to be used by a derived item to calculate its value."  },
    { OPCHDA_NORMAL_MAXIMUM,     VT_R8,   OPCHDA_ATTRNAME_NORMAL_MAXIMUM,     L"Specifies the upper limit for the normal value range for the item."  },
    { OPCHDA_NORMAL_MINIMUM,     VT_R8,   OPCHDA_ATTRNAME_NORMAL_MINIMUM,     L"Specifies the lower limit for the normal value range for the item."  },
    { OPCHDA_ITEMID,             VT_BSTR, OPCHDA_ATTRNAME_ITEMID,             L"Specifies the leaf name portion the item id."  },
    { OPCHDA_MAX_TIME_INT,       VT_CY,   OPCHDA_ATTRNAME_MAX_TIME_INT,       L"Specifies the maximum interval between data points in the history repository."  },
    { OPCHDA_MIN_TIME_INT,       VT_CY,   OPCHDA_ATTRNAME_MIN_TIME_INT,       L"Specifies the minimum interval between data points in the history repository."  },
    { OPCHDA_EXCEPTION_DEV,      VT_R8,   OPCHDA_ATTRNAME_EXCEPTION_DEV,      L"Specifies the minimum amount that the data for the item must change in order for the change to be reported to the history database."  },
	{ OPCHDA_EXCEPTION_DEV_TYPE, VT_I2,   OPCHDA_ATTRNAME_EXCEPTION_DEV_TYPE, L"Specifies whether the exception deviation is given as an absolute value, percent of span, or percent of value." },
    { OPCHDA_HIGH_ENTRY_LIMIT,   VT_R8,   OPCHDA_ATTRNAME_HIGH_ENTRY_LIMIT,   L"Specifies the highest valid value for the item." },
    { OPCHDA_LOW_ENTRY_LIMIT,    VT_R8,   OPCHDA_ATTRNAME_LOW_ENTRY_LIMIT,    L"Specifies the lowest valid value for the item." },
	{ 0, VT_EMPTY, NULL, NULL }
};

static void* AllocArray(int size, int count)
{
    // allocate return array.
    void* pValues  = CoTaskMemAlloc(size*count);

    // check allocations.
    if (pValues == NULL)
    {
	    throw gcnew System::OutOfMemoryException();
    }

    // initialize results.
    memset(pValues, 0, size*count);

	return pValues;
}

#define ALLOC_ARRAY(xType, xCount) (xType*)AllocArray(sizeof(xType),xCount)

// Copy(HRESULT)
HRESULT Copy(
	array<int>^ results,
	DWORD dwNumItems,
	HRESULT* pErrors)
{
	HRESULT hResult = S_OK;

    for (DWORD ii = 0; ii < dwNumItems; ii++)
    {
        pErrors[ii] = results[ii];
            
        if (pErrors[ii] != S_OK)
        {
            hResult = S_FALSE;
        }
    }

	return hResult;
}

// Copy(HdaUpdateRequest)
HRESULT Copy(
	List<HdaUpdateRequest^>^ results,
	DWORD dwNumItems,
    OPCHANDLE* phClient,
	HRESULT* pErrors)
{
	HRESULT hResult = S_OK;

    for (DWORD ii = 0; ii < dwNumItems; ii++)
    {
        phClient[ii] = results[ii]->ClientHandle;
        pErrors[ii] = results[ii]->Error;
            
        if (pErrors[ii] != S_OK)
        {
            hResult = S_FALSE;
        }
    }

	return hResult;
}

// MarshalRequests
static HRESULT MarshalRequests(
    array<DaValue^>^ values,
    VARIANT*         pvValues,
    DWORD*           pdwQualities,
	::FILETIME*      pftTimeStamps)
{
	// check arguments.
	if (pvValues == NULL || pdwQualities == NULL || pftTimeStamps == NULL)
	{
		return E_INVALIDARG;
	}

    DWORD dwCount = values->Length;

	try
	{
		// fix any marshalling issues.
        COpcUaProxyUtils::FixupInputVariants(dwCount, pvValues);

		// copy data.
		for (int ii = 0; ii < values->Length; ii++)
		{
            values[ii] = gcnew DaValue();
            values[ii]->Value = Marshal::GetObjectForNativeVariant((IntPtr)&(pvValues[ii]));
            values[ii]->Timestamp = COpcUaProxyUtils::ResolveTime(&(pftTimeStamps[ii]));
			values[ii]->HdaQuality = pdwQualities[ii];
		}

		return S_OK;
	}
	catch (Exception^ e)
    {
		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// MarshalRequests
static HRESULT MarshalRequests(
    array<int>^                 serverHandles,
    array<DateTime>^            timestamps,
    array<array<Annotation^>^>^ annotations,
	OPCHANDLE*                  phServer,
	::FILETIME*                 ftTimeStamps,
	OPCHDA_ANNOTATION*          pAnnotationValues)
{

	// check arguments.
	if (pAnnotationValues == NULL || ftTimeStamps == NULL)
	{
		return E_INVALIDARG;
	}

    DWORD dwCount = serverHandles->Length;

	try
	{
		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            // marshal handle/timestamps.
            serverHandles[ii] = phServer[ii];
            timestamps[ii] = COpcUaProxyUtils::ResolveTime(&(ftTimeStamps[ii]));

			if (pAnnotationValues[ii].dwNumValues == 0)
			{
                continue;
            }

            // marhsal annotations.
			List<Annotation^>^ list = gcnew List<Annotation^>(pAnnotationValues[ii].dwNumValues);

			for (DWORD jj = 0; jj < pAnnotationValues[ii].dwNumValues; jj++)
			{
				if (pAnnotationValues[ii].ftTimeStamps[jj] != ftTimeStamps[ii])
				{
					continue;
				}

                if (pAnnotationValues[ii].szAnnotation == NULL)
                {
					continue;
                }

				Annotation^ annotation = gcnew Annotation();
				annotation->AnnotationTime = COpcUaProxyUtils::ResolveTime(&(pAnnotationValues[ii].ftAnnotationTime[jj]));
				annotation->Message = Marshal::PtrToStringUni((IntPtr)(pAnnotationValues[ii].szAnnotation[jj]));

                if (pAnnotationValues[ii].szUser[jj] != NULL)
                {
				    annotation->UserName = Marshal::PtrToStringUni((IntPtr)(pAnnotationValues[ii].szUser[jj]));
                }

				list->Add(annotation);
			}

			annotations[ii] = list->ToArray();
		}

        return S_OK;
	}
	catch (Exception^ e)
    {
		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// MarshalResults
static HRESULT MarshalResults(
    List<DaValue^>^ values,
    VARIANT**       ppvValues,
    DWORD**         ppdwQualities,
	::FILETIME**    ppftTimeStamps)
{
	// check arguments.
	if (ppvValues == NULL || ppdwQualities == NULL || ppftTimeStamps == NULL)
	{
		return E_INVALIDARG;
	}

	*ppvValues = NULL;
	*ppdwQualities = NULL;
	*ppftTimeStamps = NULL;

    DWORD dwCount = values->Count;

	VARIANT* pvValues =  NULL;
	DWORD* pdwQualities = NULL;
	::FILETIME* pftTimeStamps = NULL;

	try
	{
		// allocate memory for results.
		pvValues = ALLOC_ARRAY(VARIANT, dwCount);
		pdwQualities = ALLOC_ARRAY(DWORD, dwCount);
		pftTimeStamps = ALLOC_ARRAY(::FILETIME, dwCount);

		// copy results.
		for (int ii = 0; ii < values->Count; ii++)
		{
            // assume bad quality on error.
			pdwQualities[ii] = OPC_QUALITY_BAD;
			pftTimeStamps[ii] = COpcUaProxyUtils::GetFILETIME(values[ii]->Timestamp);

			if (FAILED(values[ii]->Error))
			{
				continue;
			}

			// need to watch for conversion errors for some values.
            HRESULT hResult = S_OK;

			if (!COpcUaProxyUtils::MarshalVARIANT(pvValues[ii], values[ii]->Value, hResult))
			{
				continue;
			}

			if (FAILED(hResult))
			{
				continue;
			}

			pdwQualities[ii] = values[ii]->HdaQuality;
		}

		// fix any marshalling issues.
		COpcUaProxyUtils::FixupOutputVariants(dwCount, pvValues);
	
		*ppvValues = pvValues;
		*ppdwQualities = pdwQualities;
		*ppftTimeStamps = pftTimeStamps;

		return S_OK;
	}
	catch (Exception^ e)
	{
		// free variants on error.
		if (pvValues != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				VariantClear(&(pvValues[ii]));
			}
		}

		// free allocated results.
		CoTaskMemFree(pvValues);
		CoTaskMemFree(pdwQualities);
		CoTaskMemFree(pftTimeStamps);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// MarshalResults
static HRESULT MarshalResults(
    List<DaValue^>^ values,
    VARIANT**       ppvValues,
	::FILETIME**    ppftTimeStamps)
{
	// check arguments.
	if (ppvValues == NULL || ppftTimeStamps == NULL)
	{
		return E_INVALIDARG;
	}

	*ppvValues = NULL;
	*ppftTimeStamps = NULL;

    DWORD dwCount = values->Count;

	VARIANT* pvValues =  NULL;
	::FILETIME* pftTimeStamps = NULL;

	try
	{
		// allocate memory for results.
		pvValues = ALLOC_ARRAY(VARIANT, dwCount);
		pftTimeStamps = ALLOC_ARRAY(::FILETIME, dwCount);

		// copy results.
		for (int ii = 0; ii < values->Count; ii++)
		{
            // assume bad quality on error.
			pftTimeStamps[ii] = COpcUaProxyUtils::GetFILETIME(values[ii]->Timestamp);

			if (FAILED(values[ii]->Error))
			{
				continue;
			}

			// need to watch for conversion errors for some values.
            HRESULT hResult = S_OK;

			if (!COpcUaProxyUtils::MarshalVARIANT(pvValues[ii], values[ii]->Value, hResult))
			{
				continue;
			}
		}

		// fix any marshalling issues.
		COpcUaProxyUtils::FixupOutputVariants(dwCount, pvValues);
	
		*ppvValues = pvValues;
		*ppftTimeStamps = pftTimeStamps;

		return S_OK;
	}
	catch (Exception^ e)
	{
		// free variants on error.
		if (pvValues != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				VariantClear(&(pvValues[ii]));
			}
		}

		// free allocated results.
		CoTaskMemFree(pvValues);
		CoTaskMemFree(pftTimeStamps);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// MarshalResults
static HRESULT MarshalResults(
    List<ModificationInfo^>^ values,
    DWORD                    dwCount,
    LPWSTR**                 ppszUsers,
    OPCHDA_EDITTYPE**        ppEditTypes,
	::FILETIME**             ppftModificationTimes)
{
	// check arguments.
	if (ppszUsers == NULL || ppEditTypes == NULL || ppftModificationTimes == NULL)
	{
		return E_INVALIDARG;
	}

	*ppszUsers = NULL;
	*ppEditTypes = NULL;
	*ppftModificationTimes = NULL;

	LPWSTR* pszUsers =  NULL;
	OPCHDA_EDITTYPE* pEditTypes =  NULL;
	::FILETIME* pftModificationTimes = NULL;

	try
	{
		// allocate memory for results.
		pszUsers = ALLOC_ARRAY(LPWSTR, dwCount);
		pEditTypes = ALLOC_ARRAY(OPCHDA_EDITTYPE, dwCount);
		pftModificationTimes = ALLOC_ARRAY(::FILETIME, dwCount);

		// copy results.
        if (values != nullptr)
        {
		    for (int ii = 0; ii < (int)dwCount && ii < values->Count; ii++)
		    {
                pszUsers[ii] = (LPWSTR)Marshal::StringToCoTaskMemUni(values[ii]->UserName).ToPointer();
                pEditTypes[ii] = (OPCHDA_EDITTYPE)(int)values[ii]->UpdateType;
                pftModificationTimes[ii] = COpcUaProxyUtils::GetFILETIME(values[ii]->ModificationTime);
		    }
        }
	
		*ppszUsers = pszUsers;
		*ppEditTypes = pEditTypes;
		*ppftModificationTimes = pftModificationTimes;

		return S_OK;
	}
	catch (Exception^ e)
	{
		// free variants on error.
		if (pszUsers != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				CoTaskMemFree(pszUsers[ii]);
			}
		}

		// free allocated results.
		CoTaskMemFree(pszUsers);
		CoTaskMemFree(pEditTypes);
		CoTaskMemFree(pftModificationTimes);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// MarshalResults
static HRESULT MarshalResults(
    List<DaValue^>^   values,
	::FILETIME**      ppftTimestamps,
	::FILETIME**      ppftAnnotationTimes,
    LPWSTR**          ppszAnnotations,
    LPWSTR**          ppszUsers)
{
	// check arguments.
	if (ppftTimestamps == NULL || ppftAnnotationTimes == NULL || ppszAnnotations == NULL || ppszUsers == NULL)
	{
		return E_INVALIDARG;
	}

	*ppftTimestamps = NULL;
	*ppftAnnotationTimes = NULL;
	*ppszAnnotations = NULL;
	*ppszUsers = NULL;

    DWORD dwCount = values->Count;

	::FILETIME* pftTimestamps = NULL;
	::FILETIME* pftAnnotationTimes = NULL;
	LPWSTR* pszAnnotations = NULL;
	LPWSTR* pszUsers = NULL;

	try
	{
		// allocate memory for results.
		pftTimestamps = ALLOC_ARRAY(::FILETIME, dwCount);
		pftAnnotationTimes = ALLOC_ARRAY(::FILETIME, dwCount);
		pszAnnotations = ALLOC_ARRAY(LPWSTR, dwCount);
		pszUsers = ALLOC_ARRAY(LPWSTR, dwCount);

		// copy results.
		for (int ii = 0; ii < values->Count; ii++)
		{
			pftTimestamps[ii] = COpcUaProxyUtils::GetFILETIME(values[ii]->Timestamp);
			
			if (Annotation::typeid->IsInstanceOfType(values[ii]->Value))
			{
				Annotation^ annnotation = (Annotation^)values[ii]->Value;
				pszAnnotations[ii] = (LPWSTR)Marshal::StringToCoTaskMemUni(annnotation->Message).ToPointer();
				pszUsers[ii] = (LPWSTR)Marshal::StringToCoTaskMemUni(annnotation->UserName).ToPointer();
				pftAnnotationTimes[ii] = COpcUaProxyUtils::GetFILETIME(annnotation->AnnotationTime);
			}
		}
	
		*ppftTimestamps = pftTimestamps;
		*ppftAnnotationTimes = pftAnnotationTimes;
		*ppszAnnotations = pszAnnotations;
		*ppszUsers = pszUsers;

		return S_OK;
	}
	catch (Exception^ e)
	{
		if (pszAnnotations != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				CoTaskMemFree(pszAnnotations[ii]);
			}
		}

		if (pszUsers != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				CoTaskMemFree(pszUsers[ii]);
			}
		}

		// free allocated results.
		CoTaskMemFree(pftTimestamps);
		CoTaskMemFree(pftAnnotationTimes);
		CoTaskMemFree(pszAnnotations);
		CoTaskMemFree(pszUsers);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// Copy(OPCHDA_ITEM)
HRESULT Copy(
	List<HdaReadRequest^>^ results,
	DWORD dwNumItems,
	OPCHDA_ITEM* pItemValues,
	HRESULT* pErrors)
{
	HRESULT hResult = S_OK;

    for (DWORD ii = 0; ii < dwNumItems; ii++)
    {
        pErrors[ii] = results[ii]->Error;
        pItemValues[ii].hClient = results[ii]->ClientHandle;
        pItemValues[ii].haAggregate = results[ii]->AggregateId;

        if (results[ii]->Values != nullptr)
        {
            pItemValues[ii].dwCount = results[ii]->Values->Count;

            if (pItemValues[ii].dwCount > 0)
            {
                MarshalResults(
                    results[ii]->Values,
                    &(pItemValues[ii].pvDataValues),
                    &(pItemValues[ii].pdwQualities),
                    &(pItemValues[ii].pftTimeStamps));
            }
        }
            
        if (pErrors[ii] != S_OK)
        {
            hResult = S_FALSE;
        }
    }

	return hResult;
}

// Free(OPCHDA_ITEM)
void Free(
    DWORD dwNumItems,
    OPCHDA_ITEM* pItemValues)
{
    if (pItemValues != NULL)
    {
        for (DWORD ii = 0; ii < dwNumItems; ii++)
        {
            // free variants on error.
            if (pItemValues[ii].pvDataValues != NULL)
            {
                for (DWORD jj = 0; jj < pItemValues[ii].dwCount; jj++)
                {
	                VariantClear(&(pItemValues[ii].pvDataValues[jj]));
                }
            }

            // free allocated results.
            CoTaskMemFree(pItemValues[ii].pvDataValues);
            CoTaskMemFree(pItemValues[ii].pdwQualities);
            CoTaskMemFree(pItemValues[ii].pftTimeStamps);
        }
            
        CoTaskMemFree(pItemValues);
    }
}

// Copy(OPCHDA_MODIFIEDITEM)
HRESULT Copy(
	List<HdaReadRequest^>^ results,
	DWORD dwNumItems,
	OPCHDA_MODIFIEDITEM* pItemValues,
	HRESULT* pErrors)
{
	HRESULT hResult = S_OK;

    for (DWORD ii = 0; ii < dwNumItems; ii++)
    {
        pErrors[ii] = results[ii]->Error;
        pItemValues[ii].hClient = results[ii]->ClientHandle;

        if (results[ii]->Values != nullptr)
        {
            pItemValues[ii].dwCount = results[ii]->Values->Count;

            if (pItemValues[ii].dwCount > 0)
            {
                MarshalResults(
                    results[ii]->Values,
                    &(pItemValues[ii].pvDataValues),
                    &(pItemValues[ii].pdwQualities),
                    &(pItemValues[ii].pftTimeStamps));

                MarshalResults(
                    results[ii]->ModificationInfos,
                    results[ii]->Values->Count,
                    &(pItemValues[ii].szUser),
                    &(pItemValues[ii].pEditType),
                    &(pItemValues[ii].pftModificationTime));
            }
        }
            
        if (pErrors[ii] != S_OK)
        {
            hResult = S_FALSE;
        }
    }

	return hResult;
}

// Free(OPCHDA_MODIFIEDITEM)
void Free(
    DWORD dwNumItems,
    OPCHDA_MODIFIEDITEM* pItemValues)
{
    if (pItemValues != NULL)
    {
        for (DWORD ii = 0; ii < dwNumItems; ii++)
        {
            // free variants on error.
            if (pItemValues[ii].pvDataValues != NULL)
            {
                for (DWORD jj = 0; jj < pItemValues[ii].dwCount; jj++)
                {
	                VariantClear(&(pItemValues[ii].pvDataValues[jj]));
                }
            }

            // free strings on error.
            if (pItemValues[ii].szUser != NULL)
            {
                for (DWORD jj = 0; jj < pItemValues[ii].dwCount; jj++)
                {
	                CoTaskMemFree(pItemValues[ii].szUser[jj]);
                }
            }

            // free allocated results.
            CoTaskMemFree(pItemValues[ii].pvDataValues);
            CoTaskMemFree(pItemValues[ii].pdwQualities);
            CoTaskMemFree(pItemValues[ii].pftTimeStamps);
            CoTaskMemFree(pItemValues[ii].szUser);
            CoTaskMemFree(pItemValues[ii].pEditType);
            CoTaskMemFree(pItemValues[ii].pftModificationTime);
        }

        CoTaskMemFree(pItemValues);
    }
}

// Copy(OPCHDA_ATTRIBUTE)
HRESULT Copy(
	List<HdaReadRequest^>^ results,
	DWORD dwItemAttributes,
	OPCHDA_ATTRIBUTE* pItemAttributes,
	HRESULT* pErrors)
{
	HRESULT hResult = S_OK;

    for (DWORD ii = 0; ii < dwItemAttributes; ii++)
    {
        pErrors[ii] = results[ii]->Error;
        pItemAttributes[ii].hClient = results[ii]->ClientHandle;
        pItemAttributes[ii].dwAttributeID = results[ii]->AttributeId;

        if (results[ii]->Values != nullptr)
        {
			pItemAttributes[ii].dwNumValues = results[ii]->Values->Count;

            if (pItemAttributes[ii].dwNumValues > 0)
            {
                MarshalResults(
                    results[ii]->Values,
					&(pItemAttributes[ii].vAttributeValues),
					&(pItemAttributes[ii].ftTimeStamps));
            }
        }
            
        if (pErrors[ii] != S_OK)
        {
            hResult = S_FALSE;
        }
    }

	return hResult;
}

// Free(OPCHDA_ATTRIBUTE)
void Free(
    DWORD dwNumAttributes,
    OPCHDA_ATTRIBUTE* pItemAttributes)
{
    if (pItemAttributes != NULL)
    {
        for (DWORD ii = 0; ii < dwNumAttributes; ii++)
        {
			if (pItemAttributes[ii].vAttributeValues != NULL)
            {
				for (DWORD jj = 0; jj < pItemAttributes[ii].dwNumValues; jj++)
                {
	                VariantClear(&(pItemAttributes[ii].vAttributeValues[jj]));
                }
            }

            CoTaskMemFree(pItemAttributes[ii].vAttributeValues);
			CoTaskMemFree(pItemAttributes[ii].ftTimeStamps);
        }
			
        CoTaskMemFree(pItemAttributes);
    }
}

// Copy(OPCHDA_ANNOTATION)
HRESULT Copy(
	List<HdaReadRequest^>^ results,
	DWORD dwNumItems,
    OPCHDA_ANNOTATION* pItemAnnotations,
	HRESULT* pErrors)
{
	HRESULT hResult = S_OK;

    for (DWORD ii = 0; ii < dwNumItems; ii++)
    {
        pErrors[ii] = results[ii]->Error;
        pItemAnnotations[ii].hClient = results[ii]->ClientHandle;

        if (results[ii]->Values != nullptr)
        {
			pItemAnnotations[ii].dwNumValues = results[ii]->Values->Count;

            if (pItemAnnotations[ii].dwNumValues > 0)
            {
                MarshalResults(
                    results[ii]->Values,
					&(pItemAnnotations[ii].ftTimeStamps),
					&(pItemAnnotations[ii].ftAnnotationTime),
					&(pItemAnnotations[ii].szAnnotation),
					&(pItemAnnotations[ii].szUser));
            }
        }
            
        if (pErrors[ii] != S_OK)
        {
            hResult = S_FALSE;
        }
    }

	return hResult;
}

// Free(OPCHDA_ANNOTATION)
void Free(
    DWORD dwNumItems,
    OPCHDA_ANNOTATION* pItemAnnotations)
{
    if (pItemAnnotations != NULL)
    {
        for (DWORD ii = 0; ii < dwNumItems; ii++)
        {
			if (pItemAnnotations[ii].szAnnotation != NULL)
            {
                for (DWORD jj = 0; jj < pItemAnnotations[ii].dwNumValues; jj++)
                {
	                CoTaskMemFree(pItemAnnotations[ii].szAnnotation[jj]);
                }
            }

			if (pItemAnnotations[ii].szUser != NULL)
            {
                for (DWORD jj = 0; jj < pItemAnnotations[ii].dwNumValues; jj++)
                {
	                CoTaskMemFree(pItemAnnotations[ii].szUser[jj]);
                }
            }

            // free allocated results.
			CoTaskMemFree(pItemAnnotations[ii].ftAnnotationTime);
			CoTaskMemFree(pItemAnnotations[ii].ftTimeStamps);
			CoTaskMemFree(pItemAnnotations[ii].szAnnotation);
			CoTaskMemFree(pItemAnnotations[ii].szUser);
        }
			
        CoTaskMemFree(pItemAnnotations);
    }
}


//============================================================================
// COpcUaHdaProxyServer

// Constructor
COpcUaHdaProxyServer::COpcUaHdaProxyServer()
{
	TraceState("COpcUaHdaProxyServer");

	m_pInnerServer  = NULL;
	m_pClientName   = NULL;

	Version^ version = Assembly::GetExecutingAssembly()->GetName()->Version;

    m_ftStartTime = OpcUtcNow();
	m_wMajorVersion = (WORD)version->Major;
	m_wMinorVersion = (WORD)version->Minor;
	m_wBuildNumber  = (WORD)version->Build;
	m_szVendorInfo  = L"OPC UA COM HDA Proxy Server";

    try
    {
	    ComHdaProxy^ server = gcnew ComHdaProxy();
	    GCHandle hInnerServer = GCHandle::Alloc(server);
	    m_pInnerServer = ((IntPtr)hInnerServer).ToPointer();
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "COpcUaHdaProxyServer: Unexpected error creating AE proxy.");
    }
}

// Destructor
COpcUaHdaProxyServer::~COpcUaHdaProxyServer()
{
	TraceState("~COpcUaHdaProxyServer");

	if (m_pClientName != NULL)
	{
		CoTaskMemFree(m_pClientName);
		m_pClientName = NULL;
	}

	if (m_pInnerServer != NULL)
	{
		GCHandle hInnerServer = (GCHandle)IntPtr(m_pInnerServer);
		hInnerServer.Free();
		m_pInnerServer = NULL;
	}
}

// FinalConstruct
HRESULT COpcUaHdaProxyServer::FinalConstruct()
{
	TraceState("FinalConstruct");

	COpcLock cLock(*this);

	ApplicationConfiguration^ configuration = nullptr;

	// load configuration.
	if (!COpcUaProxyUtils::Initialize(configuration))
	{
		return E_FAIL;
	}

	// get the CLSID being used.
	CLSID cClsid = GetCLSID();
	Guid clsid = (Guid)Marshal::PtrToStructure((IntPtr)&cClsid, Guid::typeid);

	// load the server.
    try
    {
	    ComProxy^ server = GetInnerServer();
	    server->Load(clsid, configuration);
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "COpcUaHdaProxyServer: Unexpected error loading proxy for CLSID={0}.", clsid);
    }

	// register callback interfaces.
    RegisterInterface(IID_IOPCShutdown);
    RegisterInterface(IID_IOPCHDA_DataCallback);

    return S_OK;
}

// FinalRelease
bool COpcUaHdaProxyServer::FinalRelease()
{
	TraceState("FinalRelease");

	COpcLock cLock(*this);

	ComHdaProxy^ server = GetInnerServer();

    try
    {
	    server->Unload();
		UnregisterInterface(IID_IOPCShutdown);
	    cLock.Unlock();
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "COpcUaDaProxyServer: Unexpected error unloading proxy.");
    }

	// decrement global reference count.
	COpcUaProxyUtils::Uninitialize();

	return true;
}

// GetInnerServer
ComHdaProxy^ COpcUaHdaProxyServer::GetInnerServer()
{
	if (m_pInnerServer == NULL)
	{
		return nullptr;
	}

	GCHandle hInnerServer = (GCHandle)IntPtr(m_pInnerServer);

	if (hInnerServer.IsAllocated)
	{
		return (ComHdaProxy^)hInnerServer.Target;
	}

	return nullptr;
}

// OnAdvise
void COpcUaHdaProxyServer::OnAdvise(REFIID riid, DWORD dwCookie)
{
	COpcLock cLock(*this);

	IOPCHDA_DataCallback* ipCallback = NULL;

    if (FAILED(GetCallback(IID_IOPCHDA_DataCallback, (IUnknown**)&ipCallback)))
    {
        return;
    }

    try
    {
	    ComHdaProxy^ server = GetInnerServer();
	    COpcUaHdaProxyDataCallback^ callback = gcnew COpcUaHdaProxyDataCallback(ipCallback);
	    server->SetCallback(callback);
	    ipCallback->Release();
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "Unexpected error setting data callback.");
    }

	cLock.Unlock();
}

// OnUnadvise
void COpcUaHdaProxyServer::OnUnadvise(REFIID riid, DWORD dwCookie)
{
	COpcLock cLock(*this);

    try
    {
	    ComHdaProxy^ server = GetInnerServer();
	    server->SetCallback(nullptr);
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "Unexpected error releasing data callback.");
    }

	cLock.Unlock();
}

//==============================================================================
// IOPCCommon

// SetLocaleID
HRESULT COpcUaHdaProxyServer::SetLocaleID(LCID dwLcid)
{
	TraceState("IOPCCommon.SetLocaleID");

	try
	{	
		ComHdaProxy^ server = GetInnerServer();
		server->SetLocaleId(dwLcid);
		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// GetLocaleID
HRESULT COpcUaHdaProxyServer::GetLocaleID(LCID *pdwLcid)
{
	TraceState("IOPCCommon.GetLocaleID");

	if (pdwLcid == 0)
	{
		return E_INVALIDARG;
	}

	*pdwLcid = 0;

	try
	{	
		ComHdaProxy^ server = GetInnerServer();
		*pdwLcid = server->GetLocaleId();
		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// QueryAvailableLocaleIDs
HRESULT COpcUaHdaProxyServer::QueryAvailableLocaleIDs(
	DWORD* pdwCount,
	LCID** pdwLcid)
{
	TraceState("IOPCCommon.QueryAvailableLocaleIDs");

	if (pdwCount == 0 || pdwLcid == 0)
	{
		return E_INVALIDARG;
	}

	*pdwCount = 0;
	*pdwLcid = NULL;

	LCID* pLcids = NULL;

	try
	{	
		ComHdaProxy^ server = GetInnerServer();
		array<int>^ localeIds = server->GetAvailableLocaleIds();
		OpcProxy_AllocArrayToReturn(pLcids, localeIds->Length, LCID);

		for (int ii = 0; ii < localeIds->Length; ii++)
		{
			pLcids[ii] = localeIds[ii];
		}

		*pdwCount = localeIds->Length;
		*pdwLcid = pLcids;

		return S_OK;
	}
	catch (Exception^ e)
	{
		CoTaskMemFree(pLcids);
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// GetErrorString
HRESULT COpcUaHdaProxyServer::GetErrorString(
	HRESULT dwError,
	LPWSTR* ppString)
{
	TraceState("IOPCCommon.GetErrorString");

	if (ppString == 0)
	{
		return E_INVALIDARG;
	}

	return COpcCommon::GetErrorString(OPC_MESSAGE_MODULE_NAME_HDA, dwError, LOCALE_SYSTEM_DEFAULT, ppString);
}

// SetClientName
HRESULT COpcUaHdaProxyServer::SetClientName(LPCWSTR szName)
{
	TraceState("SetClientName");

	// check arguments.
	if (szName == 0)
	{
		return E_INVALIDARG;
	}

	COpcLock cLock(*this);

	int length = wcslen(szName);

	// allocate memory.
	m_pClientName = (LPWSTR)CoTaskMemAlloc((sizeof(WCHAR)+1)*length);
	
	if (m_pClientName == 0)
	{
		return E_OUTOFMEMORY;
	}

	// copy the string.
	wcscpy_s(m_pClientName, length+1, szName);

	return S_OK;
}

//=========================================================================
// IOPCHDA_Server

// GetItemAttributes
HRESULT COpcUaHdaProxyServer::GetItemAttributes( 
	DWORD*    pdwCount,
	DWORD**   ppdwAttrID,
	LPWSTR**  ppszAttrName,
	LPWSTR**  ppszAttrDesc,
	VARTYPE** ppvtAttrDataType
)
{
	TraceState("GetItemAttributes");

	// check arguments.
	if (pdwCount == NULL || ppdwAttrID == NULL || ppszAttrName == NULL || ppszAttrDesc == NULL ||  ppvtAttrDataType == NULL)
	{
		return E_INVALIDARG;
	}
    
    *pdwCount = 0;
	*ppdwAttrID = NULL;
	*ppszAttrName = NULL;
	*ppszAttrDesc = NULL;
	*ppvtAttrDataType = NULL;

	// determine the number of known attributes.
    DWORD dwCount = 0;

	while (g_AttributeTable[dwCount].dwID != 0) dwCount++;

	// allocate memory for results.
	DWORD* pdwAttrID = NULL;
	LPWSTR* pszAttrName = NULL;
	LPWSTR* pszAttrDesc = NULL;
	VARTYPE* pvtAttrDataType = NULL;

	try
	{
		// allocate memory for results.
		pdwAttrID = ALLOC_ARRAY(DWORD, dwCount);
		pszAttrName = ALLOC_ARRAY(LPWSTR, dwCount);
		pszAttrDesc = ALLOC_ARRAY(LPWSTR, dwCount);
		pvtAttrDataType = ALLOC_ARRAY(VARTYPE, dwCount);

	    for (DWORD ii = 0; ii < dwCount; ii++)
	    {
		    pdwAttrID[ii]       = g_AttributeTable[ii].dwID;
		    pszAttrName[ii]     = OpcStrDup(g_AttributeTable[ii].szName);
		    pszAttrDesc[ii]     = OpcStrDup(g_AttributeTable[ii].szDescription);
		    pvtAttrDataType[ii] = g_AttributeTable[ii].vtDataType;
	    }

        *pdwCount = dwCount;
		*ppdwAttrID = pdwAttrID;
		*ppszAttrName = pszAttrName;
		*ppszAttrDesc = pszAttrDesc;
		*ppvtAttrDataType = pvtAttrDataType;

		return S_OK;
	}
	catch (Exception^ e)
	{
		// free strings on error.
		if (pszAttrName != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				CoTaskMemFree(pszAttrName[ii]);
			}
		}

		// free strings on error.
		if (pszAttrDesc != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				CoTaskMemFree(pszAttrDesc[ii]);
			}
		}

		// free allocated results.
		CoTaskMemFree(pdwAttrID);
		CoTaskMemFree(pszAttrName);
		CoTaskMemFree(pszAttrDesc);
		CoTaskMemFree(pvtAttrDataType);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// GetAggregates
HRESULT COpcUaHdaProxyServer::GetAggregates(
	DWORD*   pdwCount,
	DWORD**  ppdwAggrID,
	LPWSTR** ppszAggrName,
	LPWSTR** ppszAggrDesc
)
{
    TraceState("GetAggregates");

	// check arguments.
	if (pdwCount == NULL || ppdwAggrID == NULL || ppszAggrName == NULL || ppszAggrDesc == NULL)
	{
		return E_INVALIDARG;
	}
    
    *pdwCount = 0;
	*ppdwAggrID = NULL;
	*ppszAggrName = NULL;
	*ppszAggrDesc = NULL;

    DWORD dwCount = 0;
    DWORD* pdwAggrID = NULL;
    LPWSTR* pszAggrName = NULL;
    LPWSTR* pszAggrDesc = NULL;

	try
	{
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

        // fetch aggregates.
		List<HdaAggregate^>^ values = server->GetSupportedAggregates();

        if (values == nullptr || values->Count == 0)
        {
            return S_OK;
        }

        dwCount = values->Count;

		// allocate memory for results.
		pdwAggrID = ALLOC_ARRAY(DWORD, dwCount);
		pszAggrName = ALLOC_ARRAY(LPWSTR, dwCount);
		pszAggrDesc = ALLOC_ARRAY(LPWSTR, dwCount);

	    for (DWORD ii = 0; ii < dwCount; ii++)
	    {
            pdwAggrID[ii]   = values[ii]->LocalId;
            pszAggrName[ii] = (LPWSTR)Marshal::StringToCoTaskMemUni(values[ii]->Name).ToPointer();

            if (values[ii]->Description != nullptr)
            {
                pszAggrDesc[ii] = (LPWSTR)Marshal::StringToCoTaskMemUni(values[ii]->Description).ToPointer();;
            }
	    }

        *pdwCount = dwCount;
		*ppdwAggrID = pdwAggrID;
		*ppszAggrName = pszAggrName;
		*ppszAggrDesc = pszAggrDesc;

		return S_OK;
	}
	catch (Exception^ e)
	{
		// free strings on error.
		if (pszAggrName != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				CoTaskMemFree(pszAggrName[ii]);
			}
		}

		// free strings on error.
		if (pszAggrDesc != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				CoTaskMemFree(pszAggrDesc[ii]);
			}
		}

		// free allocated results.
		CoTaskMemFree(pdwAggrID);
		CoTaskMemFree(pszAggrName);
		CoTaskMemFree(pszAggrDesc);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// GetHistorianStatus
HRESULT COpcUaHdaProxyServer::GetHistorianStatus(
	OPCHDA_SERVERSTATUS* pwStatus,
    ::FILETIME**         pftCurrentTime,
	::FILETIME**         pftStartTime,
	WORD*                pwMajorVersion,
	WORD*                pwMinorVersion,
	WORD*                pwBuildNumber,
	DWORD*               pdwMaxReturnValues,
	LPWSTR*              ppszStatusString,
	LPWSTR*              ppszVendorInfo
)
{
	TraceState("GetHistorianStatus");

	// check arguments.
	if (pwStatus == NULL || pftCurrentTime == NULL || pftStartTime == NULL || pwMajorVersion == NULL || pwMinorVersion == NULL || pwBuildNumber == NULL || pdwMaxReturnValues == NULL || ppszStatusString == NULL || ppszVendorInfo == NULL)
	{
		return E_INVALIDARG;
	}

    COpcLock cLock(*this);

    *pftCurrentTime = (::FILETIME*)CoTaskMemAlloc(sizeof(::FILETIME));
    *pftStartTime = (::FILETIME*)CoTaskMemAlloc(sizeof(::FILETIME));

	*pwStatus = OPCHDA_DOWN;
	**pftCurrentTime = OpcUtcNow();
    **pftStartTime = this->m_ftStartTime;
    *pwMajorVersion = this->m_wMajorVersion;
    *pwMinorVersion = this->m_wMinorVersion;
    *pwBuildNumber = this->m_wBuildNumber;
    *pdwMaxReturnValues = 0;
    *ppszStatusString = NULL;
    *ppszVendorInfo = NULL;

	// get inner server.
	ComHdaProxy^ server = GetInnerServer();

	if (server != nullptr)
	{
		*pdwMaxReturnValues = server->MaxReturnValues;

		if (server->Connected)
		{
			*pwStatus = OPCHDA_UP;
		}

		ConfiguredEndpoint^ endpoint = server->Endpoint;

		if (endpoint != nullptr)
		{
			*ppszVendorInfo = (LPWSTR)Marshal::StringToCoTaskMemUni(endpoint->ToString()).ToPointer();
		}
	}

	return S_OK;
}

// GetItemHandles
HRESULT COpcUaHdaProxyServer::GetItemHandles(
	DWORD		dwCount,
	LPWSTR*     pszItemID,
	OPCHANDLE*  phClient,
	OPCHANDLE** pphServer,
	HRESULT**   ppErrors
)
{	
    TraceState("GetItemHandles");

	// check arguments.
	if (dwCount == 0 || pszItemID == NULL || phClient == NULL || pphServer == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}
    
	*pphServer = NULL;
	*ppErrors = NULL;

	OPCHANDLE* phServer = NULL;
	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		phServer = ALLOC_ARRAY(OPCHANDLE, dwCount);
		pErrors = ALLOC_ARRAY(HRESULT, dwCount);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert item ids.
		array<String^>^ itemIds = gcnew array<String^>(dwCount);
		array<int>^ clientHandles = gcnew array<int>(dwCount);

		for (int ii = 0; ii < itemIds->Length; ii++)
		{
            if (pszItemID[ii] != NULL)
            {
			    itemIds[ii] = Marshal::PtrToStringUni((IntPtr)(LPWSTR)pszItemID[ii]);
            }

            clientHandles[ii] = phClient[ii];
		}

		// read values.
        array<HdaItemHandle^>^ handles = server->GetItemHandles(itemIds, clientHandles);

        HRESULT hResult = S_OK;

	    for (DWORD ii = 0; ii < dwCount; ii++)
	    {
            phServer[ii] = handles[ii]->ServerHandle;
            pErrors[ii] = handles[ii]->Error;

            if (pErrors[ii] != S_OK)
            {
                hResult = S_FALSE;
            }
	    }

		*pphServer = phServer;
		*ppErrors = pErrors;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(phServer);
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// ReleaseItemHandles
HRESULT COpcUaHdaProxyServer::ReleaseItemHandles(
	DWORD		dwCount,
	OPCHANDLE* phServer,
	HRESULT**  ppErrors
)
{	
    TraceState("ReleaseItemHandles");

	// check arguments.
	if (dwCount == 0 || phServer == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}
    
	*ppErrors = NULL;

	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pErrors = ALLOC_ARRAY(HRESULT, dwCount);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert item ids.
		array<int>^ serverHandles = gcnew array<int>(dwCount);

		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            serverHandles[ii] = phServer[ii];
		}

		// read values.
        array<int>^ errors = server->ReleaseItemHandles(serverHandles);

        HRESULT hResult = Copy(errors, dwCount, pErrors);

		*ppErrors = pErrors;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// ValidateItemIDs
HRESULT COpcUaHdaProxyServer::ValidateItemIDs(
	DWORD	  dwCount,
	LPWSTR*   pszItemID,
	HRESULT** ppErrors
)
{
    TraceState("ValidateItemIDs");

	// check arguments.
	if (dwCount == 0 || pszItemID == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}
    
	*ppErrors = NULL;

	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pErrors = ALLOC_ARRAY(HRESULT, dwCount);
               
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert item ids.
		array<String^>^ itemIds = gcnew array<String^>(dwCount);

		for (int ii = 0; ii < itemIds->Length; ii++)
		{
            if (pszItemID[ii] != NULL)
            {
			    itemIds[ii] = Marshal::PtrToStringUni((IntPtr)(LPWSTR)pszItemID[ii]);
            }
		}

		// validate items.
        array<int>^ errors = server->ValidateItemIds(itemIds);

        HRESULT hResult = Copy(errors, dwCount, pErrors);

		*ppErrors = pErrors;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// CreateBrowse
HRESULT COpcUaHdaProxyServer::CreateBrowse(
	DWORD			      dwCount,
	DWORD*                pdwAttrID,
	OPCHDA_OPERATORCODES* pOperator,
	VARIANT*              vFilter,
	IOPCHDA_Browser**     pphBrowser,
	HRESULT**             ppErrors
)
{
    TraceState("CreateBrowse");

    HRESULT hResult = S_OK;

	// check arguments.
	if (pphBrowser == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

    *pphBrowser = NULL;
    *ppErrors = NULL;

	// allocate memory for results.
	HRESULT* pErrors = NULL;      

	try
	{
        // get the server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// create the browser.
        ComHdaBrowser^ browser = server->CreateBrowser();

		// wrap the browser.
		COpcUaHdaProxyBrowser* pBrowser = new COpcUaHdaProxyBrowser(browser);

		// fetch required interface.
		if (FAILED(pBrowser->QueryInterface(IID_IOPCHDA_Browser, (void**)pphBrowser)))
		{
            pBrowser->Release();
			return E_NOINTERFACE;
		}

		// unmarshal attribute filters.
        if (dwCount != 0)
        {
			// allocate memory for results.
			pErrors = ALLOC_ARRAY(HRESULT, dwCount);

            // marshal filter paramters.
		    array<unsigned int>^ attributeIds = gcnew array<unsigned int>(dwCount);
		    array<int>^ operators = gcnew array<int>(dwCount);
		    array<Object^>^ values = gcnew array<Object^>(dwCount);

		    for (int ii = 0; ii < attributeIds->Length; ii++)
		    {
			    attributeIds[ii] = pdwAttrID[ii];
			    operators[ii] = (int)pOperator[ii];
			    values[ii] = Marshal::GetObjectForNativeVariant((IntPtr)&(vFilter[ii]));
		    }

            // create the filter.
            array<int>^ errors = browser->SetAttributeFilter(attributeIds, operators, values);

            // check for errors.
	        for (DWORD ii = 0; ii < dwCount; ii++)
	        {
                pErrors[ii] = errors[ii];

                if (pErrors[ii] != S_OK)
                {
                    hResult = S_FALSE;
                }
	        }
        }

		*ppErrors = pErrors;

        return hResult;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}
}

//=========================================================================
// IOPCHDA_SyncRead

// ReadRaw
HRESULT COpcUaHdaProxyServer::ReadRaw(
	OPCHDA_TIME*  htStartTime,
	OPCHDA_TIME*  htEndTime,
	DWORD	      dwNumValues,
	BOOL	      bBounds,
	DWORD	      dwNumItems,
	OPCHANDLE*    phServer, 
	OPCHDA_ITEM** ppItemValues,
	HRESULT**     ppErrors
)
{
    TraceState("IOPCHDA_SyncRead.ReadRaw");

	// check arguments.
	if (htStartTime == NULL || htEndTime == NULL || dwNumItems == 0 || phServer == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppItemValues = NULL;
	*ppErrors     = NULL;

    DateTime startTime = COpcUaProxyUtils::ResolveTime(htStartTime);
	DateTime endTime   = COpcUaProxyUtils::ResolveTime(htEndTime);

	OPCHDA_ITEM* pItemValues = NULL;
	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pItemValues = ALLOC_ARRAY(OPCHDA_ITEM, dwNumItems);
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert item ids.
		array<int>^ serverHandles = gcnew array<int>(dwNumItems);

		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            serverHandles[ii] = phServer[ii];
		}

		// read values.
        List<HdaReadRequest^>^ results = server->ReadRaw(
            startTime,
            endTime,
            dwNumValues,
            bBounds != 0,
            serverHandles);

        HRESULT hResult = Copy(results, dwNumItems, pItemValues, pErrors);

		*ppItemValues = pItemValues;
		*ppErrors = pErrors;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
        Free(dwNumItems, pItemValues);
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// ReadProcessed
HRESULT COpcUaHdaProxyServer::ReadProcessed(
	OPCHDA_TIME*  htStartTime,
	OPCHDA_TIME*  htEndTime,
	::FILETIME    ftResampleInterval,
	DWORD         dwNumItems,
	OPCHANDLE*    phServer, 
	DWORD*        haAggregate, 
	OPCHDA_ITEM** ppItemValues,
	HRESULT**     ppErrors
)
{
    TraceState("IOPCHDA_SyncRead.ReadProcessed");

	// check arguments.
	if (htStartTime == NULL || htEndTime == NULL || dwNumItems == 0 || phServer == NULL || haAggregate == NULL || ppItemValues == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppItemValues = NULL;
	*ppErrors     = NULL;

    DateTime startTime = COpcUaProxyUtils::ResolveTime(htStartTime);
	DateTime endTime   = COpcUaProxyUtils::ResolveTime(htEndTime);

	// check arguments.
	if (endTime == startTime)
	{
		return E_INVALIDARG;
	}

	OPCHDA_ITEM* pItemValues = NULL;
	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pItemValues = ALLOC_ARRAY(OPCHDA_ITEM, dwNumItems);
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert item ids.
		array<int>^ serverHandles = gcnew array<int>(dwNumItems);
		array<unsigned int>^ aggregateIds = gcnew array<unsigned int>(dwNumItems);

		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            serverHandles[ii] = phServer[ii];
            aggregateIds[ii] = haAggregate[ii];
		}

        LONGLONG resampleInterval = ::OpcToInt64(ftResampleInterval)/TimeSpan::TicksPerMillisecond;

		// read values.
        List<HdaReadRequest^>^ results = server->ReadProcessed(
            startTime,
            endTime,
            resampleInterval,
            serverHandles,
            aggregateIds);

        HRESULT hResult = Copy(results, dwNumItems, pItemValues, pErrors);

		*ppItemValues = pItemValues;
		*ppErrors = pErrors;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
        Free(dwNumItems, pItemValues);
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// ReadAtTime
HRESULT COpcUaHdaProxyServer::ReadAtTime(
	DWORD         dwNumTimeStamps,
	::FILETIME*   ftTimeStamps,
	DWORD         dwNumItems,
	OPCHANDLE*    phServer, 
	OPCHDA_ITEM** ppItemValues,
	HRESULT**     ppErrors
)
{
    TraceState("IOPCHDA_SyncRead.ReadAtTime");

	// check arguments.
	if (dwNumTimeStamps == 0 || ftTimeStamps == NULL || dwNumItems == 0 || phServer == NULL || ppItemValues == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppItemValues = NULL;
	*ppErrors     = NULL;

	OPCHDA_ITEM* pItemValues = NULL;
	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pItemValues = ALLOC_ARRAY(OPCHDA_ITEM, dwNumItems);
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert server handles
		array<int>^ serverHandles = gcnew array<int>(dwNumItems);

		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            serverHandles[ii] = phServer[ii];
		}

		// convert server handles
		array<DateTime>^ timestamps = gcnew array<DateTime>(dwNumTimeStamps);

		for (int ii = 0; ii < timestamps->Length; ii++)
		{
            timestamps[ii] = COpcUaProxyUtils::ResolveTime(&(ftTimeStamps[ii]));
		}

		// read values.
        List<HdaReadRequest^>^ results = server->ReadAtTime(timestamps, serverHandles);

        HRESULT hResult = Copy(results, dwNumItems, pItemValues, pErrors);

		*ppItemValues = pItemValues;
		*ppErrors = pErrors;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
        Free(dwNumItems, pItemValues);
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// ReadModified
HRESULT COpcUaHdaProxyServer::ReadModified(
	OPCHDA_TIME*          htStartTime,
	OPCHDA_TIME*          htEndTime,
	DWORD                 dwNumValues,
	DWORD                 dwNumItems,
	OPCHANDLE*            phServer, 
	OPCHDA_MODIFIEDITEM** ppItemValues,
	HRESULT**             ppErrors
)
{
    TraceState("IOPCHDA_SyncRead.ReadModified");

	// check arguments.
	if (htStartTime == NULL || htEndTime == NULL || dwNumItems == 0 || phServer == NULL || ppItemValues == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppItemValues = NULL;
	*ppErrors     = NULL;

    DateTime startTime = COpcUaProxyUtils::ResolveTime(htStartTime);
	DateTime endTime   = COpcUaProxyUtils::ResolveTime(htEndTime);

	OPCHDA_MODIFIEDITEM* pItemValues = NULL;
	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pItemValues = ALLOC_ARRAY(OPCHDA_MODIFIEDITEM, dwNumItems);
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert item ids.
		array<int>^ serverHandles = gcnew array<int>(dwNumItems);

		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            serverHandles[ii] = phServer[ii];
		}

		// read values.
        List<HdaReadRequest^>^ results = server->ReadModified(
            startTime,
            endTime,
            dwNumValues,
            serverHandles);

        HRESULT hResult = Copy(results, dwNumItems, pItemValues, pErrors);

		*ppItemValues = pItemValues;
		*ppErrors = pErrors;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
        Free(dwNumItems, pItemValues);
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// ReadAttribute
HRESULT COpcUaHdaProxyServer::ReadAttribute(
	OPCHDA_TIME*       htStartTime,
	OPCHDA_TIME*       htEndTime,
	OPCHANDLE          hServer, 
	DWORD              dwNumAttributes,
	DWORD*             pdwAttributeIDs, 
	OPCHDA_ATTRIBUTE** ppAttributeValues,
	HRESULT**          ppErrors
)
{
    TraceState("IOPCHDA_SyncRead.ReadAttribute");

	// check arguments.
	if (htStartTime == NULL || htEndTime == NULL || dwNumAttributes == 0 || pdwAttributeIDs == NULL || ppAttributeValues == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppAttributeValues = NULL;
	*ppErrors = NULL;

    DateTime startTime = COpcUaProxyUtils::ResolveTime(htStartTime);
	DateTime endTime   = COpcUaProxyUtils::ResolveTime(htEndTime);

	OPCHDA_ATTRIBUTE* pAttributeValues = NULL;
	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pAttributeValues = ALLOC_ARRAY(OPCHDA_ATTRIBUTE, dwNumAttributes);
		pErrors = ALLOC_ARRAY(HRESULT, dwNumAttributes);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert item ids.
		array<unsigned int>^ attributeIds = gcnew array<unsigned int>(dwNumAttributes);

		for (int ii = 0; ii < attributeIds->Length; ii++)
		{
            attributeIds[ii] = pdwAttributeIDs[ii];
		}

		// read values.
        List<HdaReadRequest^>^ results = server->ReadAttributes(
            startTime,
            endTime,
            hServer,
            attributeIds);

        HRESULT hResult = Copy(results, dwNumAttributes, pAttributeValues, pErrors);

		*ppAttributeValues = pAttributeValues;
		*ppErrors = pErrors;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
        Free(dwNumAttributes, pAttributeValues);
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

//=========================================================================
// IOPCHDA_SyncUpdate

// QueryCapabilities
HRESULT COpcUaHdaProxyServer::QueryCapabilities(
	OPCHDA_UPDATECAPABILITIES* pCapabilities
)
{
    TraceState("IOPCHDA_SyncUpdate.QueryCapabilities");
	*pCapabilities = (OPCHDA_UPDATECAPABILITIES)(OPCHDA_INSERTCAP | OPCHDA_REPLACECAP | OPCHDA_INSERTREPLACECAP | OPCHDA_DELETERAWCAP | OPCHDA_DELETEATTIMECAP);
	return S_OK;
}

// UpdateRaw
HRESULT COpcUaHdaProxyServer::UpdateRaw(
	PerformUpdateType        updateType,
	DWORD                    dwNumItems, 
	OPCHANDLE*               phServer, 
	::FILETIME*              ftTimeStamps,
	VARIANT*                 vDataValues,
	DWORD*                   pdwQualities,
	HRESULT**                ppErrors
)
{
	// check arguments.
	if (dwNumItems == 0 || phServer == NULL || ftTimeStamps == NULL || vDataValues == NULL || pdwQualities == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppErrors = NULL;

	HRESULT* pErrors = NULL;

	try
	{
		// convert server handles.
		array<int>^ serverHandles = gcnew array<int>(dwNumItems);

		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            serverHandles[ii] = phServer[ii];
		}

        // convert values.
		array<DaValue^>^ values = gcnew array<DaValue^>(dwNumItems);

        HRESULT hResult = MarshalRequests(values, vDataValues, pdwQualities, ftTimeStamps);
        
        if (FAILED(hResult))
        {
            return hResult;
        }

		// allocate memory for results.
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// update values.
        array<int>^ errors = server->UpdateRaw(
			updateType,
            serverHandles,
			values);

        hResult = Copy(errors, dwNumItems, pErrors);

		*ppErrors = pErrors;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// Insert
HRESULT COpcUaHdaProxyServer::Insert(
	DWORD      dwNumItems, 
	OPCHANDLE* phServer, 
	::FILETIME*ftTimeStamps,
	VARIANT*   vDataValues,
	DWORD*     pdwQualities,
	HRESULT**  ppErrors
)
{
    TraceState("IOPCHDA_SyncUpdate.Insert");

	return UpdateRaw(
		PerformUpdateType::Insert,
		dwNumItems,
		phServer,
		ftTimeStamps,
		vDataValues,
		pdwQualities,
		ppErrors);
}

// Replace
HRESULT COpcUaHdaProxyServer::Replace(
	DWORD       dwNumItems, 
	OPCHANDLE*  phServer, 
	::FILETIME* ftTimeStamps,
	VARIANT*    vDataValues,
	DWORD*      pdwQualities,
	HRESULT**   ppErrors
)
{
    TraceState("IOPCHDA_SyncUpdate.Replace");

	return UpdateRaw(
		PerformUpdateType::Replace,
		dwNumItems,
		phServer,
		ftTimeStamps,
		vDataValues,
		pdwQualities,
		ppErrors);
}

// InsertReplace
HRESULT COpcUaHdaProxyServer::InsertReplace(
	DWORD       dwNumItems, 
	OPCHANDLE*  phServer, 
	::FILETIME* ftTimeStamps,
	VARIANT*    vDataValues,
	DWORD*      pdwQualities,
	HRESULT**   ppErrors
)
{
    TraceState("IOPCHDA_SyncUpdate.InsertReplace");

	return UpdateRaw(
		PerformUpdateType::Update,
		dwNumItems,
		phServer,
		ftTimeStamps,
		vDataValues,
		pdwQualities,
		ppErrors);
}

// DeleteRaw
HRESULT COpcUaHdaProxyServer::DeleteRaw(
	OPCHDA_TIME* htStartTime,
	OPCHDA_TIME* htEndTime,
	DWORD        dwNumItems,
	OPCHANDLE*   phServer,
	HRESULT**    ppErrors
)
{
    TraceState("IOPCHDA_SyncUpdate.DeleteRaw");

	// check arguments.
	if (htStartTime == NULL || htEndTime == NULL || dwNumItems == 0 || phServer == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppErrors = NULL;

    DateTime startTime = COpcUaProxyUtils::ResolveTime(htStartTime);
	DateTime endTime   = COpcUaProxyUtils::ResolveTime(htEndTime);

	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert item ids.
		array<int>^ serverHandles = gcnew array<int>(dwNumItems);

		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            serverHandles[ii] = phServer[ii];
		}

		// read values.
        array<int>^ errors = server->DeleteRaw(
            startTime,
            endTime,
            serverHandles);

        HRESULT hResult = Copy(errors, dwNumItems, pErrors);

		*ppErrors = pErrors;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// DeleteAtTime
HRESULT COpcUaHdaProxyServer::DeleteAtTime(
	DWORD      dwNumItems,
	OPCHANDLE* phServer,
	::FILETIME*ftTimeStamps,
	HRESULT**  ppErrors
)
{
    TraceState("IOPCHDA_SyncUpdate.DeleteAtTime");

	// check arguments.
	if (ftTimeStamps == NULL || dwNumItems == 0 || phServer == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppErrors = NULL;

	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		array<int>^ serverHandles = gcnew array<int>(dwNumItems);
		array<DateTime>^ timestamps = gcnew array<DateTime>(dwNumItems);

		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            serverHandles[ii] = phServer[ii];
            timestamps[ii] = COpcUaProxyUtils::ResolveTime(&(ftTimeStamps[ii]));
		}

        array<int>^ errors = server->DeleteAtTime(timestamps, serverHandles);

        HRESULT hResult = Copy(errors, dwNumItems, pErrors);

		*ppErrors = pErrors;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

//=========================================================================
// IOPCHDA_SyncAnnotations

// QueryCapabilities
HRESULT COpcUaHdaProxyServer::QueryCapabilities(
	OPCHDA_ANNOTATIONCAPABILITIES* pCapabilities
)
{
	*pCapabilities = (OPCHDA_ANNOTATIONCAPABILITIES)(OPCHDA_READANNOTATIONCAP | OPCHDA_INSERTANNOTATIONCAP);
    return S_OK;
}

// Read
HRESULT COpcUaHdaProxyServer::Read(
	OPCHDA_TIME*        htStartTime,
	OPCHDA_TIME*        htEndTime,
	DWORD	            dwNumItems,
	OPCHANDLE*          phServer, 
	OPCHDA_ANNOTATION** ppAnnotationValues,
	HRESULT**           ppErrors
)
{ 
	TraceState("IOPCHDA_SyncAnnotations.Read");

	// check arguments.
	if (htStartTime == NULL || htEndTime == NULL || dwNumItems == 0 || phServer == NULL || ppAnnotationValues == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppAnnotationValues = NULL;
	*ppErrors = NULL;

    DateTime startTime = COpcUaProxyUtils::ResolveTime(htStartTime);
	DateTime endTime   = COpcUaProxyUtils::ResolveTime(htEndTime);

	OPCHDA_ANNOTATION* pAnnotationValues = NULL;
	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pAnnotationValues = ALLOC_ARRAY(OPCHDA_ANNOTATION, dwNumItems);
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);;
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert item ids.
		array<int>^ serverHandles = gcnew array<int>(dwNumItems);

		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            serverHandles[ii] = phServer[ii];
		}

		// read values.
        List<HdaReadRequest^>^ results = server->ReadAnnotations(
            startTime,
            endTime,
            serverHandles);

        HRESULT hResult = Copy(results, dwNumItems, pAnnotationValues, pErrors);

		*ppAnnotationValues = pAnnotationValues;
		*ppErrors = pErrors;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
        Free(dwNumItems, pAnnotationValues);
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// Insert
HRESULT COpcUaHdaProxyServer::Insert(
	DWORD              dwNumItems, 
	OPCHANDLE*         phServer, 
	::FILETIME*        ftTimeStamps,
	OPCHDA_ANNOTATION* pAnnotationValues,
	HRESULT**          ppErrors
)
{
    TraceState("IOPCHDA_SyncAnnotations.Insert");

	// check arguments.
	if (dwNumItems == 0 || phServer == NULL || ftTimeStamps == NULL || pAnnotationValues == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppErrors = NULL;

	HRESULT* pErrors = NULL;

	try
	{
		// marshal inputs.
		array<int>^ serverHandles = gcnew array<int>(dwNumItems);
		array<DateTime>^ timestamps = gcnew array<DateTime>(dwNumItems);
		array<array<Annotation^>^>^ annotations = gcnew array<array<Annotation^>^>(dwNumItems);

        HRESULT hResult = MarshalRequests(
            serverHandles,
            timestamps,
            annotations,
            phServer,
            ftTimeStamps,
            pAnnotationValues);

        if (FAILED(hResult))
        {
            return hResult;
        }

		// allocate memory for results.
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// read values.
        array<int>^ errors = server->InsertAnnotations(
            serverHandles,
			timestamps,
			annotations);

        hResult = Copy(errors, dwNumItems, pErrors);

		*ppErrors = pErrors;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

//=========================================================================
// IOPCHDA_AsyncRead

// ReadRaw
HRESULT COpcUaHdaProxyServer::ReadRaw(
	DWORD        dwTransactionID,
	OPCHDA_TIME* htStartTime,
	OPCHDA_TIME* htEndTime,
	DWORD        dwNumValues,
	BOOL         bBounds,
	DWORD        dwNumItems,
	OPCHANDLE*   phServer,
	DWORD*       pdwCancelID,
	HRESULT**    ppErrors
)
{
    TraceState("IOPCHDA_AsyncRead.ReadRaw");

	// check arguments.
	if (htStartTime == NULL || htEndTime == NULL || dwNumItems == 0 || phServer == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppErrors = NULL;

    DateTime startTime = COpcUaProxyUtils::ResolveTime(htStartTime);
	DateTime endTime   = COpcUaProxyUtils::ResolveTime(htEndTime);

	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert item ids.
		array<int>^ serverHandles = gcnew array<int>(dwNumItems);

		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            serverHandles[ii] = phServer[ii];
		}

		int cancelId = 0;

        array<int>^ errors = server->ReadRaw(
            dwTransactionID,
			startTime,
            endTime,
            dwNumValues,
            bBounds != 0,
            serverHandles,
			cancelId);

        HRESULT hResult = Copy(errors, dwNumItems, pErrors);

		*ppErrors = pErrors;
		*pdwCancelID = cancelId;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// AdviseRaw
HRESULT COpcUaHdaProxyServer::AdviseRaw(
	DWORD        dwTransactionID,
	OPCHDA_TIME* htStartTime,
	::FILETIME   ftUpdateInterval,
	DWORD        dwNumItems,
	OPCHANDLE*   phServer,
	DWORD*       pdwCancelID,
	HRESULT**    ppErrors
)
{
    TraceState("IOPCHDA_AsyncRead.AdviseRaw");
    return E_NOTIMPL;
}

// ReadProcessed
HRESULT COpcUaHdaProxyServer::ReadProcessed(
	DWORD        dwTransactionID,
	OPCHDA_TIME* htStartTime,
	OPCHDA_TIME* htEndTime,
	::FILETIME   ftResampleInterval,
	DWORD        dwNumItems,
	OPCHANDLE*   phServer,
	DWORD*       haAggregate,
	DWORD*       pdwCancelID,
	HRESULT**    ppErrors
)
{
    TraceState("IOPCHDA_AsyncRead.ReadProcessed");

	// check arguments.
	if (htStartTime == NULL || htEndTime == NULL || dwNumItems == 0 || phServer == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppErrors = NULL;

    DateTime startTime = COpcUaProxyUtils::ResolveTime(htStartTime);
	DateTime endTime   = COpcUaProxyUtils::ResolveTime(htEndTime);

	// check arguments.
	if (endTime == startTime)
	{
		return E_INVALIDARG;
	}

	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert item ids.
		array<int>^ serverHandles = gcnew array<int>(dwNumItems);
		array<unsigned int>^ aggregateIds = gcnew array<unsigned int>(dwNumItems);

		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            serverHandles[ii] = phServer[ii];
            aggregateIds[ii] = haAggregate[ii];
		}

        LONGLONG resampleInterval = ::OpcToInt64(ftResampleInterval)/TimeSpan::TicksPerMillisecond;

		int cancelId = 0;

        array<int>^ errors = server->ReadProcessed(
			dwTransactionID,
            startTime,
            endTime,
            resampleInterval,
            serverHandles,
            aggregateIds,
			cancelId);

        HRESULT hResult = Copy(errors, dwNumItems, pErrors);

		*ppErrors = pErrors;
		*pdwCancelID = cancelId;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// AdviseProcessed
HRESULT COpcUaHdaProxyServer::AdviseProcessed(
	DWORD        dwTransactionID,
	OPCHDA_TIME* htStartTime,
	::FILETIME   ftResampleInterval,
	DWORD        dwNumItems,
	OPCHANDLE*   phServer,
	DWORD*       haAggregate,
	DWORD        dwNumIntervals,
	DWORD*       pdwCancelID,
	HRESULT**    ppErrors
)
{
    TraceState("IOPCHDA_AsyncRead.AdviseProcessed");
    return E_NOTIMPL;
}

// ReadAtTime
HRESULT COpcUaHdaProxyServer::ReadAtTime(
	DWORD      dwTransactionID,
	DWORD      dwNumTimeStamps,
	::FILETIME*ftTimeStamps,
	DWORD      dwNumItems,
	OPCHANDLE* phServer, 
	DWORD*     pdwCancelID,
	HRESULT**  ppErrors
)
{
    TraceState("IOPCHDA_AsyncRead.ReadAtTime");

	// check arguments.
	if (dwNumTimeStamps == 0 || ftTimeStamps == NULL || dwNumItems == 0 || phServer == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppErrors = NULL;

	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert server handles
		array<int>^ serverHandles = gcnew array<int>(dwNumItems);

		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            serverHandles[ii] = phServer[ii];
		}

		// convert server handles
		array<DateTime>^ timestamps = gcnew array<DateTime>(dwNumTimeStamps);

		for (int ii = 0; ii < timestamps->Length; ii++)
		{
            timestamps[ii] = COpcUaProxyUtils::ResolveTime(&(ftTimeStamps[ii]));
		}
	
		int cancelId = 0;

        array<int>^ errors = server->ReadAtTime(
			dwTransactionID, 
			timestamps, 
			serverHandles,
			cancelId);

        HRESULT hResult = Copy(errors, dwNumItems, pErrors);

		*ppErrors = pErrors;
		*pdwCancelID = cancelId;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// ReadModified
HRESULT COpcUaHdaProxyServer::ReadModified(
	DWORD        dwTransactionID,
	OPCHDA_TIME* htStartTime,
	OPCHDA_TIME* htEndTime,
	DWORD        dwNumValues,
	DWORD        dwNumItems,
	OPCHANDLE*   phServer, 
	DWORD*       pdwCancelID,
	HRESULT**    ppErrors
)
{
    TraceState("IOPCHDA_AsyncRead.ReadModified");

	// check arguments.
	if (htStartTime == NULL || htEndTime == NULL || dwNumItems == 0 || phServer == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppErrors = NULL;

    DateTime startTime = COpcUaProxyUtils::ResolveTime(htStartTime);
	DateTime endTime   = COpcUaProxyUtils::ResolveTime(htEndTime);

	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert item ids.
		array<int>^ serverHandles = gcnew array<int>(dwNumItems);

		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            serverHandles[ii] = phServer[ii];
		}

		int cancelId = 0;

        array<int>^ errors = server->ReadModified(
            dwTransactionID,
			startTime,
            endTime,
            dwNumValues,
            serverHandles,
			cancelId);

        HRESULT hResult = Copy(errors, dwNumItems, pErrors);

		*ppErrors = pErrors;
		*pdwCancelID = cancelId;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// ReadAttribute
HRESULT COpcUaHdaProxyServer::ReadAttribute(
	DWORD        dwTransactionID,
	OPCHDA_TIME* htStartTime,
	OPCHDA_TIME* htEndTime,
	OPCHANDLE    hServer, 
	DWORD        dwNumAttributes,
	DWORD*       dwAttributeIDs, 
	DWORD*       pdwCancelID,
	HRESULT**    ppErrors
)
{
    TraceState("IOPCHDA_AsyncRead.ReadAttribute");

	// check arguments.
	if (htStartTime == NULL || htEndTime == NULL || dwNumAttributes == 0 || dwAttributeIDs == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppErrors = NULL;

    DateTime startTime = COpcUaProxyUtils::ResolveTime(htStartTime);
	DateTime endTime   = COpcUaProxyUtils::ResolveTime(htEndTime);

	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pErrors = ALLOC_ARRAY(HRESULT, dwNumAttributes);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert item ids.
		array<unsigned int>^ attributeIds = gcnew array<unsigned int>(dwNumAttributes);

		for (int ii = 0; ii < attributeIds->Length; ii++)
		{
            attributeIds[ii] = dwAttributeIDs[ii];
		}

		int cancelId = 0;

        array<int>^ errors = server->ReadAttributes(
            dwTransactionID,
			startTime,
            endTime,
            hServer,
            attributeIds,
			cancelId);

        HRESULT hResult = Copy(errors, dwNumAttributes, pErrors);

		*ppErrors = pErrors;
		*pdwCancelID = cancelId;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// Cancel
HRESULT COpcUaHdaProxyServer::Cancel(DWORD dwCancelID)
{
    TraceState("IOPCHDA_AsyncRead.Cancel");

	try
	{
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

        return server->Cancel(dwCancelID);
	}
	catch (Exception^ e)
	{
		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

//=========================================================================
// IOPCHDA_AsyncUpdate

// UpdateRaw
HRESULT COpcUaHdaProxyServer::UpdateRaw(
    PerformUpdateType        updateType,
	DWORD                    dwTransactionID,
	DWORD                    dwNumItems,
	OPCHANDLE*               phServer,
	::FILETIME*              ftTimeStamps,
	VARIANT*                 vDataValues,
	DWORD*                   pdwQualities,
	DWORD*                   pdwCancelID,
	HRESULT**                ppErrors
)
{
	// check arguments.
	if (ftTimeStamps == NULL || vDataValues == NULL || pdwQualities == NULL || dwNumItems == 0 || phServer == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppErrors = NULL;

	HRESULT* pErrors = NULL;

	try
	{
		// convert server handles.
		array<int>^ serverHandles = gcnew array<int>(dwNumItems);

		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            serverHandles[ii] = phServer[ii];
		}

        // convert values.
		array<DaValue^>^ values = gcnew array<DaValue^>(dwNumItems);

        HRESULT hResult = MarshalRequests(values, vDataValues, pdwQualities, ftTimeStamps);
        
        if (FAILED(hResult))
        {
            return hResult;
        }

		// allocate memory for results.
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		int cancelId = 0;

        array<int>^ errors = server->UpdateRaw(
            dwTransactionID,
            updateType,
            serverHandles,
            values,
			cancelId);

        hResult = Copy(errors, dwNumItems, pErrors);

		*ppErrors = pErrors;
		*pdwCancelID = cancelId;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}


// Insert
HRESULT COpcUaHdaProxyServer::Insert(
	DWORD      dwTransactionID,
	DWORD      dwNumItems,
	OPCHANDLE* phServer,
	::FILETIME*ftTimeStamps,
	VARIANT*   vDataValues,
	DWORD*     pdwQualities,
	DWORD*     pdwCancelID,
	HRESULT**  ppErrors
)
{
    TraceState("IOPCHDA_AsyncUpdate.Insert");

    return UpdateRaw(
        PerformUpdateType::Insert,
        dwTransactionID,
        dwNumItems,
        phServer,
        ftTimeStamps,
        vDataValues,
        pdwQualities,
        pdwCancelID,
        ppErrors);
}

// Replace
HRESULT COpcUaHdaProxyServer::Replace(
	DWORD      dwTransactionID,
	DWORD      dwNumItems,
	OPCHANDLE* phServer,
	::FILETIME*ftTimeStamps,
	VARIANT*   vDataValues,
	DWORD*     pdwQualities,
	DWORD*     pdwCancelID,
	HRESULT**  ppErrors
)
{
    TraceState("IOPCHDA_AsyncUpdate.Replace");

    return UpdateRaw(
        PerformUpdateType::Replace,
        dwTransactionID,
        dwNumItems,
        phServer,
        ftTimeStamps,
        vDataValues,
        pdwQualities,
        pdwCancelID,
        ppErrors);
}

// InsertReplace
HRESULT COpcUaHdaProxyServer::InsertReplace(
	DWORD      dwTransactionID,
	DWORD      dwNumItems,
	OPCHANDLE* phServer,
	::FILETIME*ftTimeStamps,
	VARIANT*   vDataValues,
	DWORD*     pdwQualities,
	DWORD*     pdwCancelID,
	HRESULT**  ppErrors
)
{
    TraceState("IOPCHDA_AsyncUpdate.InsertReplace");

    return UpdateRaw(
        PerformUpdateType::Update,
        dwTransactionID,
        dwNumItems,
        phServer,
        ftTimeStamps,
        vDataValues,
        pdwQualities,
        pdwCancelID,
        ppErrors);
}

// DeleteRaw
HRESULT COpcUaHdaProxyServer::DeleteRaw(
	DWORD        dwTransactionID,
	OPCHDA_TIME* htStartTime,
	OPCHDA_TIME* htEndTime,
	DWORD        dwNumItems,
	OPCHANDLE*   phServer,
	DWORD*       pdwCancelID,
	HRESULT**    ppErrors
)
{
    TraceState("IOPCHDA_AsyncUpdate.DeleteRaw");

	// check arguments.
	if (htStartTime == NULL || htEndTime == NULL || dwNumItems == 0 || phServer == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppErrors = NULL;

    DateTime startTime = COpcUaProxyUtils::ResolveTime(htStartTime);
	DateTime endTime   = COpcUaProxyUtils::ResolveTime(htEndTime);

	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert item ids.
		array<int>^ serverHandles = gcnew array<int>(dwNumItems);

		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            serverHandles[ii] = phServer[ii];
		}

		int cancelId = 0;

        array<int>^ errors = server->DeleteRaw(
            dwTransactionID,
			startTime,
            endTime,
            serverHandles,
			cancelId);

        HRESULT hResult = Copy(errors, dwNumItems, pErrors);

		*ppErrors = pErrors;
		*pdwCancelID = cancelId;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// DeleteAtTime
HRESULT COpcUaHdaProxyServer::DeleteAtTime(
	DWORD      dwTransactionID,
	DWORD      dwNumItems,
	OPCHANDLE* phServer,
	::FILETIME*ftTimeStamps,
	DWORD*     pdwCancelID,
	HRESULT**  ppErrors
)
{
    TraceState("IOPCHDA_AsyncUpdate.DeleteAtTime");

	// check arguments.
	if (ftTimeStamps == NULL || dwNumItems == 0 || phServer == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppErrors = NULL;

	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert server handles
		array<int>^ serverHandles = gcnew array<int>(dwNumItems);
		array<DateTime>^ timestamps = gcnew array<DateTime>(dwNumItems);

		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            serverHandles[ii] = phServer[ii];
            timestamps[ii] = COpcUaProxyUtils::ResolveTime(&(ftTimeStamps[ii]));
		}
	
		int cancelId = 0;

        array<int>^ errors = server->DeleteAtTime(
			dwTransactionID, 
			timestamps, 
			serverHandles,
			cancelId);

        HRESULT hResult = Copy(errors, dwNumItems, pErrors);

		*ppErrors = pErrors;
		*pdwCancelID = cancelId;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

//=========================================================================
// IOPCHDA_AsyncAnnotations

HRESULT COpcUaHdaProxyServer::Read(
	DWORD        dwTransactionID,
	OPCHDA_TIME* htStartTime,
	OPCHDA_TIME* htEndTime,
	DWORD        dwNumItems,
	OPCHANDLE*   phServer, 
	DWORD*       pdwCancelID,
	HRESULT**    ppErrors
)
{
    TraceState("IOPCHDA_AsyncAnnotations.Read");

	// check arguments.
	if (htStartTime == NULL || htEndTime == NULL || dwNumItems == 0 || phServer == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppErrors = NULL;

    DateTime startTime = COpcUaProxyUtils::ResolveTime(htStartTime);
	DateTime endTime   = COpcUaProxyUtils::ResolveTime(htEndTime);

	HRESULT* pErrors = NULL;

	try
	{
		// allocate memory for results.
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert item ids.
		array<int>^ serverHandles = gcnew array<int>(dwNumItems);

		for (int ii = 0; ii < serverHandles->Length; ii++)
		{
            serverHandles[ii] = phServer[ii];
		}

		int cancelId = 0;

        array<int>^ errors = server->ReadAnnotations(
            dwTransactionID,
			startTime,
            endTime,
            serverHandles,
			cancelId);

        HRESULT hResult = Copy(errors, dwNumItems, pErrors);

		*ppErrors = pErrors;
		*pdwCancelID = cancelId;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// Insert
HRESULT COpcUaHdaProxyServer::Insert(
	DWORD              dwTransactionID,
	DWORD              dwNumItems, 
	OPCHANDLE*         phServer, 
	::FILETIME*        ftTimeStamps,
	OPCHDA_ANNOTATION* pAnnotationValues,
	DWORD*             pdwCancelID,
	HRESULT**          ppErrors
)
{
    TraceState("IOPCHDA_AsyncAnnotations.Insert");

	// check arguments.
	if (ftTimeStamps == NULL || pAnnotationValues == NULL || dwNumItems == 0 || phServer == NULL)
	{
		return E_INVALIDARG;
	}

	// initialize return parameters.
	*ppErrors = NULL;

	HRESULT* pErrors = NULL;

	try
	{
		// marshal inputs.
		array<int>^ serverHandles = gcnew array<int>(dwNumItems);
		array<DateTime>^ timestamps = gcnew array<DateTime>(dwNumItems);
		array<array<Annotation^>^>^ annotations = gcnew array<array<Annotation^>^>(dwNumItems);

        HRESULT hResult = MarshalRequests(
            serverHandles,
            timestamps,
            annotations,
            phServer,
            ftTimeStamps,
            pAnnotationValues);

        if (FAILED(hResult))
        {
            return hResult;
        }

		// allocate memory for results.
		pErrors = ALLOC_ARRAY(HRESULT, dwNumItems);
        
		// get inner server.
		ComHdaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		int cancelId = 0;

        array<int>^ errors = server->InsertAnnotations(
            dwTransactionID,
            serverHandles,
            timestamps,
            annotations,
			cancelId);

        hResult = Copy(errors, dwNumItems, pErrors);

		*ppErrors = pErrors;
		*pdwCancelID = cancelId;

        return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

//=========================================================================
// IOPCHDA_Playback

// ReadRawWithUpdate
HRESULT COpcUaHdaProxyServer::ReadRawWithUpdate(
	DWORD        dwTransactionID,
	OPCHDA_TIME* htStartTime,
	OPCHDA_TIME* htEndTime,
	DWORD        dwNumValues,
	::FILETIME   ftUpdateDuration,
	::FILETIME   ftUpdateInterval,
	DWORD        dwNumItems,
	OPCHANDLE*   phServer,
	DWORD*       pdwCancelID,
	HRESULT**    ppErrors
)
{
    TraceState("IOPCHDA_Playback.ReadRawWithUpdate");
    return E_NOTIMPL;
}

// ReadProcessedWithUpdate
HRESULT COpcUaHdaProxyServer::ReadProcessedWithUpdate(
	DWORD        dwTransactionID,
	OPCHDA_TIME* htStartTime,
	OPCHDA_TIME* htEndTime,
	::FILETIME   ftResampleInterval,
	DWORD        dwNumIntervals,
	::FILETIME   ftUpdateInterval,
	DWORD        dwNumItems,
	OPCHANDLE*   phServer,
	DWORD*       haAggregate,
	DWORD*       pdwCancelID,
	HRESULT**    ppErrors
)
{
    TraceState("IOPCHDA_Playback.ReadProcessedWithUpdate");
    return E_NOTIMPL;
}
