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

#ifndef _COpcEnumStrings_H_
#define _COpcEnumStrings_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcDefs.h"
#include "COpcComObject.h"
#include "COpcList.h"
#include "COpcString.h"

//==============================================================================
// CLASS:   COpcEnumString
// PURPOSE: A class to implement the IEnumString interface.
// NOTES:

class OPCUTILS_API COpcEnumString 
:
    public COpcComObject,
    public IEnumString
{     
    OPC_BEGIN_INTERFACE_TABLE(COpcEnumString)
        OPC_INTERFACE_ENTRY(IEnumString)
    OPC_END_INTERFACE_TABLE()

    OPC_CLASS_NEW_DELETE()

public:

    //==========================================================================
    // Operators

    // Constructor
    COpcEnumString();

    // Constructor
    COpcEnumString(UINT uCount, LPWSTR*& pStrings);

    // Destructor 
    ~COpcEnumString();

    //==========================================================================
    // IEnumConnectionPoints
       
    // Next
    STDMETHODIMP Next(
        ULONG     celt,
        LPOLESTR* rgelt,
        ULONG*    pceltFetched);

    // Skip
    STDMETHODIMP Skip(ULONG celt);

    // Reset
    STDMETHODIMP Reset();

    // Clone
    STDMETHODIMP Clone(IEnumString** ppEnum);

private:

    //==========================================================================
    // Private Members

    UINT    m_uIndex;
    UINT    m_uCount;
    LPWSTR* m_pStrings;
};

#endif // _COpcEnumStrings_H_
