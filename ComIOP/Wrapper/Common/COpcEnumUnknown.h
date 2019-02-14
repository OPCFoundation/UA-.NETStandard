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

#ifndef _COpcEnumUnknowns_H_
#define _COpcEnumUnknowns_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcDefs.h"
#include "COpcComObject.h"
#include "COpcList.h"
#include "COpcString.h"

//==============================================================================
// CLASS:   COpcEnumUnknown
// PURPOSE: A class to implement the IEnumUnknown interface.
// NOTES:

class OPCUTILS_API COpcEnumUnknown 
:
    public COpcComObject,
    public IEnumUnknown
{     
    OPC_BEGIN_INTERFACE_TABLE(COpcEnumUnknown)
        OPC_INTERFACE_ENTRY(IEnumUnknown)
    OPC_END_INTERFACE_TABLE()

    OPC_CLASS_NEW_DELETE()

public:

    //==========================================================================
    // Operators

    // Constructor
    COpcEnumUnknown();

    // Constructor
    COpcEnumUnknown(UINT uCount, IUnknown**& pUnknowns);

    // Destructor 
    ~COpcEnumUnknown();

    //==========================================================================
    // IEnumConnectionPoints
       
    // Next
    STDMETHODIMP Next(
        ULONG      celt,          
        IUnknown** rgelt,   
        ULONG*     pceltFetched
    );

    // Skip
    STDMETHODIMP Skip(ULONG celt);

    // Reset
    STDMETHODIMP Reset();

    // Clone
    STDMETHODIMP Clone(IEnumUnknown** ppEnum);

private:

    //==========================================================================
    // Private Members

    UINT       m_uIndex;
    UINT       m_uCount;
    IUnknown** m_pUnknowns;
};

#endif // _COpcEnumUnknowns_H_
