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

#pragma once

using namespace System;
using namespace System::Collections::Generic;
using namespace Opc::Ua;
using namespace Opc::Ua::Com;
using namespace Opc::Ua::Com::Server;

class COpcUaProxyUtils
{
public:
	COpcUaProxyUtils(void);
	~COpcUaProxyUtils(void);
			
	/// <summary>
	/// Outputs a trace message.
	/// </summary>
	/// <param name="source">The source of the trace.</param>
	/// <param name="context">The context to print with the source.</param>
	/// <param name="args">Any additional arguments.</param>
	static void TraceState(String^ source, String^ context, ... array<Object^>^ args);

	/// <summary>
	/// Creates an application instance certificate if one does not already exist.
	/// </summary>
	/// <param name="configuration">The configuration for the application.</param>
	static void CheckApplicationInstanceCertificate(ApplicationConfiguration^ configuration);

	/// <summary>
	/// Initializes the process and loads the application configuration.
	/// </summary>
	/// <param name="configuration">The configuration for the application.</param>
	static bool Initialize(ApplicationConfiguration^% configuration);

	/// <summary>
	/// Uninitializes the process (must be called once for every call to Initialize).
	/// </summary>
	static void Uninitialize();

	/// <summary>
	/// Frees the OPCITEMPROPERTIES structure.
	/// </summary>
	/// <param name="configuration">The structure to free.</param>
	static void FreeOPCITEMPROPERTIES(OPCITEMPROPERTIES& tItem);

	// MarshalProperties
	static void MarshalProperties(
		OPCITEMPROPERTIES& tItem, 
		array<int>^ propertyIds,
		bool returnPropertyValues,
		IList<DaProperty^>^ descriptions,
		array<DaValue^>^ values);

	// GetEnumerator
	static HRESULT GetEnumerator(
		IList<String^>^ strings, 
		REFIID          riid, 
		void**          ppUnknown);

	// MarshalVARIANT
	static bool MarshalVARIANT(VARIANT& tDst, Object^ src, HRESULT& hResult);

	// GetFILETIME
	static ::FILETIME GetFILETIME(DateTime time);

	// FixupOutputVariants
	static HRESULT FixupOutputVariants(DWORD dwCount, OPCITEMPROPERTIES* pItemProperties);

	// FixupOutputVariants
	static HRESULT FixupOutputVariants(DWORD dwCount, OPCBROWSEELEMENT* ppBrowseElements);

	// FixupDecimalArray
	static void FixupDecimalArray(VARIANT& vValue);

	// FixupOutputVariant
	static void FixupOutputVariant(VARIANT& vValue);

	// FixupOutputVariants
	static void FixupOutputVariants(DWORD dwCount, OPCITEMSTATE* pItemValues);

	// FixupOutputVariants
	static void FixupOutputVariants(DWORD dwCount, VARIANT* pItemValues);

	// FixupInputVariants
	static void FixupInputVariants(DWORD dwCount, VARIANT* pValues);

	// FixupInputVariants
	static void FixupInputVariants(DWORD dwCount, OPCITEMVQT* pValues);

    // ResolveTime
    static System::DateTime ResolveTime(OPCHDA_TIME* pTime);

    // ResolveTime
    static System::DateTime ResolveTime(::FILETIME* pTime);
};

// OpcProxy_AllocArrayToReturn
#define OpcProxy_AllocArrayToReturn(xArray, xCount, xType) \
if (xCount > 0) \
{ \
	xArray = (xType*)CoTaskMemAlloc((xCount)*sizeof(xType)); \
	\
	if (xArray == NULL) \
	{ \
		throw gcnew System::OutOfMemoryException(); \
	} \
	\
	memset(xArray, 0, (xCount)*sizeof(xType)); \
}
