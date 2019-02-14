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

#ifndef _COpcEnumCPs_H_
#define _COpcEnumCPs_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcDefs.h"
#include "COpcComObject.h"
#include "COpcCPContainer.h"

//==============================================================================
// CLASS:   COpcEnumCPs
// PURPOSE: Implements the IEnumConnectionPoints interface.
// NOTES:

class OPCUTILS_API COpcEnumCPs 
:
    public COpcComObject,
    public IEnumConnectionPoints
{     
    OPC_BEGIN_INTERFACE_TABLE(COpcEnumCPs)
        OPC_INTERFACE_ENTRY(IEnumConnectionPoints)
    OPC_END_INTERFACE_TABLE()

    OPC_CLASS_NEW_DELETE()

public:

    //==========================================================================
    // Operators

    // Constructor
    COpcEnumCPs();
    
    // Constructor
    COpcEnumCPs(const COpcList<COpcConnectionPoint*>& cCPs);

    // Destructor 
    ~COpcEnumCPs();

    //==========================================================================
    // IEnumConnectionPoints

    // Next
    STDMETHODIMP Next(
        ULONG              cConnections,
        LPCONNECTIONPOINT* ppCP,
        ULONG*             pcFetched
    );

    // Skip
    STDMETHODIMP Skip(ULONG cConnections);

    // Reset
    STDMETHODIMP Reset();

    // Clone
    STDMETHODIMP Clone(IEnumConnectionPoints** ppEnum);

private:

    //==========================================================================
    // Private Members

    OPC_POS                 m_pos;
    COpcConnectionPointList m_cCPs;
};

#endif // _COpcEnumCPs_H_
