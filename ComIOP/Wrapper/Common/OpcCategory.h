/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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

#ifndef _OpcCategory_H_
#define _OpcCategory_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcDefs.h"
#include "COpcComObject.h"
#include "COpcList.h"

//==============================================================================
// FUNCTION: OpcEnumServers
// PURPOSE:  Enumerates servers in the specified category on the host.
 
// OpcEnumServers
OPCUTILS_API HRESULT OpcEnumServersInCategory(
    LPCTSTR          tsHostName,
    const CATID&     tCategory,
    COpcList<CLSID>* pServers 
);

//==============================================================================
// FUNCTION: RegisterClsidInCategory
// PURPOSE:  Registers a CLSID as belonging to a component category. 
 
HRESULT RegisterClsidInCategory(REFCLSID clsid, CATID catid, LPCWSTR szDescription) ;

//==============================================================================
// FUNCTION: UnregisterClsidInCategory
// PURPOSE:  Unregisters a CLSID as belonging to a component category. 
HRESULT UnregisterClsidInCategory(REFCLSID clsid, CATID catid);

//==============================================================================
// STRUCT:  TClassCategories
// PURPOSE: Associates a clsid with a component category. 

struct TClassCategories 
{
    const CLSID* pClsid;
    const CATID* pCategory;
	const TCHAR* szDescription;
};

//==============================================================================
// MACRO:   OPC_BEGIN_CATEGORY_TABLE
// PURPOSE: Begins the module class category table.

#define OPC_BEGIN_CATEGORY_TABLE() static const TClassCategories g_pCategoryTable[] = {

//==============================================================================
// MACRO:   OPC_CATEGORY_TABLE_ENTRY
// PURPOSE: An entry in the module class category table.

#define OPC_CATEGORY_TABLE_ENTRY(xClsid, xCatid, xDescription) {&(__uuidof(xClsid)), &(xCatid), (xDescription)},

//==============================================================================
// MACRO:   OPC_END_CATEGORY_TABLE
// PURPOSE: Ends the module class category table.

#define OPC_END_CATEGORY_TABLE() {NULL, NULL, NULL}};

#endif // _OpcCategory_H_
