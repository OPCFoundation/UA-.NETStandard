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
#include "COpcUaProxyUtils.h"
#include "COpcEnumString.h"
#include ".\Hda\COpcHdaTime.h"

using namespace System;
using namespace System::Text;
using namespace System::Runtime::InteropServices;
using namespace System::Security::Cryptography::X509Certificates;
using namespace System::Reflection;
using namespace System::Collections::Generic;

//==============================================================================
// Global Data

COpcCriticalSection g_cLock;
UINT g_uRefs = 0;
void* g_hConfiguration = 0;

//==============================================================================
// Member Functions

// COpcUaProxyUtils
COpcUaProxyUtils::COpcUaProxyUtils(void)
{
}

// ~COpcUaProxyUtils
COpcUaProxyUtils::~COpcUaProxyUtils(void)
{
}

// TraceState
void COpcUaProxyUtils::TraceState(String^ source, String^ context, ... array<Object^>^ args)
{
	if ((Utils::TraceMask & Utils::TraceMasks::Information) == 0)
    {
        //return;
    }

    StringBuilder^ buffer = gcnew StringBuilder();

    buffer->AppendFormat("{0}::{1}", source, context);

	if (args != nullptr && args->Length > 0)
    {
        buffer->Append(", { ");

        for (int ii = 0; ii < args->Length; ii++)
        {
            if (ii > 0)
            {
                buffer->Append(", ");
            }
         
            buffer->AppendFormat("{0}", Variant(args[ii]));
        }

        buffer->Append(" }");
    }

	Utils::Trace(Utils::TraceMasks::Error, "{0}", buffer->ToString());
}

// CheckApplicationInstanceCertificate
void COpcUaProxyUtils::CheckApplicationInstanceCertificate(ApplicationConfiguration^ configuration)
{
    // create a default certificate id none specified.
    CertificateIdentifier^ id = configuration->SecurityConfiguration->ApplicationCertificate;

    if (id == nullptr)
    {
        id = gcnew CertificateIdentifier();
        id->StoreType = Utils::DefaultStoreType;
        id->StorePath = Utils::DefaultStorePath;
        id->SubjectName = configuration->ApplicationName;
    }

    // check for certificate with a private key.
    X509Certificate2^ certificate = id->Find(true);

    if (certificate != nullptr)
    {
        return;
    }

    // construct the subject name from the 
    List<String^>^ hostNames = gcnew List<String^>();
    hostNames->Add(System::Net::Dns::GetHostName());

	String^ commonName = Opc::Ua::Utils::Format("CN={0}", configuration->ApplicationName);
    String^ domainName = Opc::Ua::Utils::Format("DC={0}", hostNames[0]);
    String^ subjectName = Opc::Ua::Utils::Format("{0}, {1}", commonName, domainName);

    // create a new certificate with a new public key pair.
	certificate = CertificateFactory::CreateCertificate(
        id->StoreType,
        id->StorePath,
        configuration->ApplicationUri,
        configuration->ApplicationName,
        subjectName,
        hostNames,
        1024,
        120);

    // update and save the configuration file.
    id->Certificate = certificate;
    configuration->SaveToFile(configuration->SourceFilePath);

    // add certificate to the trusted peer store so other applications will trust it.
	ICertificateStore^ store = configuration->SecurityConfiguration->TrustedPeerCertificates->OpenStore();

    try
    {
        X509Certificate2^ certificate2 = store->FindByThumbprint(certificate->Thumbprint);

        if (certificate2 == nullptr)
        {
            store->Add(certificate);
        }
    }
    finally
    {
        store->Close();
    }

    // tell the certificate validator about the new certificate.
    configuration->CertificateValidator->Update(configuration->SecurityConfiguration);
}

static void OnCertificateValidationFailed(CertificateValidator^ sender, CertificateValidationEventArgs^ e)
{
	// automatically accept untrusted certificates since an administrator had to create the COM server proxy.
	e->Accept = true;
}

// Initialize
bool COpcUaProxyUtils::Initialize(ApplicationConfiguration^% configuration)
{
	configuration = nullptr;

    COpcLock cLock(g_cLock);

	try 
	{
		g_uRefs++;

		if (g_hConfiguration == 0)
		{
			configuration = ApplicationConfiguration::Load("Opc.Ua.ComProxyServer", Opc::Ua::ApplicationType::Client);
			CheckApplicationInstanceCertificate(configuration);
			configuration->CertificateValidator->CertificateValidation += gcnew Opc::Ua::CertificateValidationEventHandler(&OnCertificateValidationFailed);
			GCHandle hConfiguration = GCHandle::Alloc(configuration);
			g_hConfiguration = ((IntPtr)hConfiguration).ToPointer();
		}
		else
		{
			GCHandle hConfiguration = (GCHandle)IntPtr(g_hConfiguration);
			configuration = (ApplicationConfiguration^)hConfiguration.Target;
		}

		return true;
	}
	catch (Exception^ e)
	{
		Utils::Trace(e, "Could not load configuration for the COM UA Proxy Server.");
		return false;
	}	
}

// Uninitialize
void COpcUaProxyUtils::Uninitialize()
{
    COpcLock cLock(g_cLock);

    g_uRefs--;
    
    if (g_uRefs > 0)
    {
		return;
    }
			
	GCHandle hConfiguration = (GCHandle)IntPtr(g_hConfiguration);
	hConfiguration.Free();
	g_hConfiguration = 0;

    cLock.Unlock();

    COpcComModule::ExitProcess(S_OK);
}

// MarshalProperties
void COpcUaProxyUtils::FreeOPCITEMPROPERTIES(OPCITEMPROPERTIES& tItem)
{
	for (DWORD ii = 0; ii < tItem.dwNumProperties; ii++)
	{
		CoTaskMemFree(tItem.pItemProperties[ii].szDescription);
		CoTaskMemFree(tItem.pItemProperties[ii].szItemID);
		VariantClear(&(tItem.pItemProperties[ii].vValue));
	}

	CoTaskMemFree(tItem.pItemProperties);
}

// MarshalProperties
void COpcUaProxyUtils::MarshalProperties(
	OPCITEMPROPERTIES& tItem,
	array<int>^ propertyIds,
	bool returnPropertyValues,
	IList<DaProperty^>^ descriptions,
	array<DaValue^>^ values)
{
	int propertyCount = descriptions->Count;
	bool returnAllProperties = propertyIds == nullptr || propertyIds->Length == 0;

	if (returnAllProperties)
	{
		propertyCount = 0;

		// count the number of valid properties.
		for (int ii = 0; ii < values->Length; ii++)
		{
			if (values[ii]->Error != OPC_E_INVALID_PID && values[ii]->Value != nullptr)
			{
				propertyCount++;
				continue;
			}
		}
	}

	// allocate memory.
	OpcProxy_AllocArrayToReturn(tItem.pItemProperties, propertyCount, OPCITEMPROPERTY);
	tItem.dwNumProperties = propertyCount;

	propertyCount = 0;

	for (int ii = 0; ii < values->Length; ii++)
	{
		// skip invalid properties.
		if (returnAllProperties && (values[ii]->Error == OPC_E_INVALID_PID || values[ii]->Value == nullptr))
		{
			continue;
		}

		OPCITEMPROPERTY* pProperty = &(tItem.pItemProperties[propertyCount++]);

		// check for other errors.
		pProperty->hrErrorID = values[ii]->Error;

		if (FAILED(pProperty->hrErrorID))
		{
			tItem.hrErrorID = S_FALSE;
			continue;
		}

		// copy property id.
		pProperty->dwPropertyID = descriptions[ii]->PropertyId;

		// copy property info.
		pProperty->szDescription = (LPWSTR)Marshal::StringToCoTaskMemUni(descriptions[ii]->Name).ToPointer();
		pProperty->szItemID = NULL;
		pProperty->vtDataType = descriptions[ii]->DataType;

		if (returnPropertyValues)
		{				
			// need to watch for conversion errors for some values.
			if (!COpcUaProxyUtils::MarshalVARIANT(pProperty->vValue, values[ii]->Value, pProperty->hrErrorID))
			{
				tItem.hrErrorID = S_FALSE;
				continue;
			}
		}
	}

	// add done.
}

// MarshalVARIANT
bool COpcUaProxyUtils::MarshalVARIANT(VARIANT& tDst, Object^ src, HRESULT& hResult)
{
	try
	{
		Marshal::GetNativeVariantForObject(src, (IntPtr)&tDst);	
		return true;	
	}
	catch (Exception^ e)
	{
		hResult = Marshal::GetHRForException(e);
		return false;
	}
}

// GetEnumerator
 HRESULT COpcUaProxyUtils::GetEnumerator(
	IList<String^>^ strings, 
	REFIID          riid, 
	void**          ppUnknown)
{
	*ppUnknown = NULL;

	COpcEnumString* ipEnum = NULL;
	DWORD dwCount = 0;

	if (strings != nullptr && strings->Count > 0)
	{
		dwCount = strings->Count;
		LPWSTR* ppStrings = (LPWSTR*)CoTaskMemAlloc(dwCount*sizeof(LPWSTR));

		if (ppStrings == NULL)
		{
			return E_OUTOFMEMORY;
		}

		memset(ppStrings, 0, dwCount*sizeof(LPWSTR));

		try
		{
			for (int ii = 0; ii < strings->Count; ii++)
			{
				if (strings[ii] == nullptr)
				{
					ppStrings[ii] = NULL;
					continue;
				}

				ppStrings[ii] = (LPWSTR)Marshal::StringToCoTaskMemUni(strings[ii]).ToPointer();
			}
		}
		catch (Exception^ e)
		{
			if (ppStrings != NULL)
			{
				for (DWORD ii = 0; ii < dwCount; ii++)
				{
					CoTaskMemFree(ppStrings[ii]);
				}
					
				CoTaskMemFree(ppStrings);
			}

			return Marshal::GetHRForException(e);
		}

		ipEnum = new COpcEnumString(dwCount, ppStrings);
	}
	
	// create an empty enumerator if nothing found.
	if (ipEnum == NULL)
	{
		ipEnum = new COpcEnumString();
	}
	
	// return requested interface.
	HRESULT hResult = ipEnum->QueryInterface(riid, (void**)ppUnknown);

	// release local reference.
	ipEnum->Release();

	if (FAILED(hResult))
	{
		return hResult;
	}

	// return correct error code.
	return (dwCount > 0)?S_OK:S_FALSE;
}

// GetFILETIME
::FILETIME COpcUaProxyUtils::GetFILETIME(DateTime dateTime)
{
	System::Runtime::InteropServices::ComTypes::FILETIME ft1 = ComUtils::GetFILETIME(dateTime);

	::FILETIME ft2;
	ft2.dwHighDateTime = ft1.dwHighDateTime;
	ft2.dwLowDateTime = ft1.dwLowDateTime;

	return ft2;
}

// FixupOutputVariants
HRESULT COpcUaProxyUtils::FixupOutputVariants(DWORD dwCount, OPCITEMPROPERTIES* pItemProperties)
{
	HRESULT hResult = S_OK;

	if (pItemProperties != NULL)
	{
		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			for (DWORD jj = 0; jj < pItemProperties[ii].dwNumProperties; jj++)
			{
				FixupOutputVariant(pItemProperties[ii].pItemProperties[jj].vValue);
			}            
			
			// check if individual item has an issue 
			if (pItemProperties[ii].hrErrorID != S_OK) hResult = S_FALSE; 
		}
	}

	return hResult;
}

// FixupOutputVariants
HRESULT COpcUaProxyUtils::FixupOutputVariants(DWORD dwCount, OPCBROWSEELEMENT* ppBrowseElements)
{
	HRESULT hResult = S_OK;

	if (ppBrowseElements != NULL)
	{
		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			if (FixupOutputVariants(1, &(ppBrowseElements[ii].ItemProperties)) != S_OK)
			{
				hResult = S_FALSE; 
			}
		}
	}

	return hResult;
}

// FixupDecimalArray
void COpcUaProxyUtils::FixupDecimalArray(VARIANT& vValue)
{
	VARIANT vDst;
	VariantInit(&vDst);

	COpcSafeArray cSrc(vValue);

	UINT uLength = cSrc.GetLength();

	COpcSafeArray cDst(vDst);
	cDst.Alloc(VT_CY, cSrc.GetLength());

	for (UINT jj = 0; jj < uLength; jj++)
	{
		DECIMAL decVal;
		
		if (SUCCEEDED(SafeArrayGetElement(vValue.parray, (LONG*)&jj, (void*)&decVal)))
		{		
			CY cyVal;

			if (FAILED(VarCyFromDec(&decVal, &cyVal)))
			{
				cyVal.int64 = 0;
			}

			SafeArrayPutElement(vDst.parray, (LONG*)&jj, (void*)&cyVal);
		}
	}

	VariantClear(&vValue);
	vValue = vDst;
}

// FixupOutputVariant
void COpcUaProxyUtils::FixupOutputVariant(VARIANT& vValue)
{
	switch (vValue.vt)
	{
		case VT_ARRAY | VT_DECIMAL:
		{
			FixupDecimalArray(vValue);
			break;
		}

		case VT_DECIMAL:
		{
			VARIANT cyVal; 
			VariantInit(&cyVal);

			if (SUCCEEDED(VariantChangeType(&cyVal, &vValue, NULL, VT_CY)))
			{
				vValue.vt    = VT_CY;
				vValue.cyVal = cyVal.cyVal;
			}

			break;
		}
	}
}

// FixupOutputVariants
void COpcUaProxyUtils::FixupOutputVariants(DWORD dwCount, OPCITEMSTATE* pItemValues)
{
	if (pItemValues != NULL)
	{
		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			FixupOutputVariant(pItemValues[ii].vDataValue);
		}
	}
}

// FixupOutputVariants
void COpcUaProxyUtils::FixupOutputVariants(DWORD dwCount, VARIANT* pItemValues)
{
	if (pItemValues != NULL)
	{
		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			FixupOutputVariant(pItemValues[ii]);
		}
	}
}

// FixupInputVariants
void COpcUaProxyUtils::FixupInputVariants(DWORD dwCount, VARIANT* pValues)
{
	for (DWORD ii = 0; ii < dwCount; ii++)
	{
		if (pValues[ii].vt == VT_DATE)
		{
			if (pValues[ii].dblVal > 2e6)
			{
				pValues[ii].vt = VT_R8;
			}
		}
	}
}

// FixupInputVariants
void COpcUaProxyUtils::FixupInputVariants(DWORD dwCount, OPCITEMVQT* pValues)
{
	for (DWORD ii = 0; ii < dwCount; ii++)
	{
		if (pValues[ii].vDataValue.vt == VT_DATE)
		{
			if (pValues[ii].vDataValue.dblVal > 2e6)
			{
				pValues[ii].vDataValue.vt = VT_R8;
			}
		}
	}
}

// ResolveTime
System::DateTime COpcUaProxyUtils::ResolveTime(OPCHDA_TIME* pTime)
{
    // check for unspecified values.
    if (pTime == NULL)
    {
        return DateTime::MinValue;
    }

    if (pTime->bString)
    {
        if (pTime->szTime == NULL || wcslen(pTime->szTime) == 0)
        {
            return DateTime::MinValue;
        }
    }
    else
    {
        if (pTime->ftTime.dwHighDateTime == 0 && pTime->ftTime.dwLowDateTime == 0)
        {
            return System::DateTime(1601,1,1,0,0,1);
        }
    }

    // convert to WIN32 ticks.
    LONGLONG llComTime = OpcHdaResolveTime(*pTime);
	LONGLONG llMaxTime = Int64::MaxValue - llComTime;

    // convert to a .NET time.
    System::DateTime basetime = System::DateTime(1601,1,1);

	if (llMaxTime < basetime.Ticks)
	{
		return DateTime::MaxValue;
	}

	return basetime.AddTicks(llComTime);
}

// ResolveTime
System::DateTime COpcUaProxyUtils::ResolveTime(::FILETIME* pTime)
{
    // check for unspecified values.
    if (pTime == NULL)
    {
        return DateTime::MinValue;
    }

    if (pTime->dwHighDateTime == 0 && pTime->dwLowDateTime == 0)
    {
        return DateTime::MinValue;
    }

    // convert to WIN32 ticks.
	LONGLONG llComTime = ::OpcToInt64(*pTime);
	LONGLONG llMaxTime = Int64::MaxValue - llComTime;

    // convert to a .NET time.
    System::DateTime basetime = System::DateTime(1601,1,1);

	if (llMaxTime < basetime.Ticks)
	{
		return DateTime::MaxValue;
	}

	return basetime.AddTicks(llComTime);
}
