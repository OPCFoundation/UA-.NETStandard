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

#ifndef _COpcArray_H
#define _COpcArray_H

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "COpcString.h"

//==============================================================================
// CLASS:   COpcList<TYPE>
// PURPOSE: Defines a indexable array template class.

template<class TYPE>
class COpcArray 
{
    OPC_CLASS_NEW_DELETE_ARRAY()

    public:

    //==========================================================================
    // Constructor
    COpcArray(UINT uSize = 0)
    :
        m_pData(NULL),
        m_uSize(0)
    {
        SetSize(uSize);
    }

    //==========================================================================
    // Copy Constructor
    COpcArray(const COpcArray& cArray)
    :
        m_pData(NULL),
        m_uSize(0)
    {
        *this = cArray;
    }  

    //==========================================================================
    // Destructor
    ~COpcArray()
    {
        RemoveAll();
    }

    //==========================================================================
    // Assignment
    COpcArray& operator=(const COpcArray& cArray)
    {
        SetSize(cArray.m_uSize);

        for (UINT ii = 0; ii < cArray.m_uSize; ii++)
        {
            m_pData[ii] = cArray[ii];
        }

        return *this;
    }

    //==========================================================================
    // GetSize
    UINT GetSize() const
    {
        return m_uSize;
    }

    //==========================================================================
    // GetData
    TYPE* GetData() const
    {
        return m_pData;
    }

    //==========================================================================
    // SetSize
    void SetSize(UINT uNewSize)
    {
        if (uNewSize == 0)
        {
            RemoveAll();
            return;
        }

        TYPE* pData = new TYPE[uNewSize];

        for (UINT ii = 0; ii < uNewSize && ii < m_uSize; ii++)
        {
            pData[ii] = m_pData[ii];
        }

        if (m_pData != NULL)
        {
            delete [] m_pData;
        }

        m_pData = pData;
        m_uSize = uNewSize;
    }

    //==========================================================================
    // RemoveAll	
    void RemoveAll()
    {
        if (m_pData != NULL)
        {
            delete [] m_pData;
        }

        m_uSize = 0;
        m_pData = NULL;
    }

    //==========================================================================
    // operator[]	
    TYPE& operator[](UINT uIndex)
    {
        OPC_ASSERT(uIndex < m_uSize);
        return m_pData[uIndex];
    }

    const TYPE& operator[](UINT uIndex) const
    {
        OPC_ASSERT(uIndex < m_uSize);
        return m_pData[uIndex];
    }

    //==========================================================================
    // SetAtGrow
    void SetAtGrow(UINT uIndex, const TYPE& newElement)
    {
        if (uIndex+1 > m_uSize)
        {
            SetSize(uIndex+1);
        }

        m_pData[uIndex] = newElement;
    }

    //==========================================================================
    // Append
    void Append(const TYPE& newElement)
    {
        SetAtGrow(m_uSize, newElement);
    }

    //==========================================================================
    // InsertAt
    void InsertAt(UINT uIndex, const TYPE& newElement, UINT uCount = 1)
    {
        OPC_ASSERT(uIndex < m_uSize);

        UINT uNewSize = m_uSize+uCount;
        TYPE* pData = new TYPE[uNewSize];

		UINT ii = 0;

        for (ii = 0; ii < uIndex; ii++)
        {
            pData[ii] = m_pData[ii];
        }

        for (ii = uIndex; ii < uCount; ii++)
        {
            pData[ii] = newElement;
        }

        for (ii = uIndex+uCount; ii < uNewSize; ii++)
        {
            pData[ii] = m_pData[ii-uCount];
        }

        delete [] m_pData;
        m_pData = pData;
        m_uSize = uNewSize;
    }

    //==========================================================================
    // RemoveAt
    void RemoveAt(UINT uIndex, UINT uCount = 1)
    {
        OPC_ASSERT(uIndex < m_uSize);

        UINT uNewSize = m_uSize-uCount;
        TYPE* pData = new TYPE[uNewSize];

		UINT ii = 0;

        for (ii = 0; ii < uIndex; ii++)
        {
            pData[ii] = m_pData[ii];
        }

        for (ii = uIndex+uCount; ii < m_uSize; ii++)
        {
            pData[ii-uCount] = m_pData[ii];
        }

        delete [] m_pData;
        m_pData = pData;
        m_uSize = uNewSize;
    }

private:

    TYPE* m_pData;
    UINT  m_uSize;
};

//==============================================================================
// TYPE:    COpcStringArray
// PURPOSE: An array of strings.

typedef COpcArray<COpcString> COpcStringArray;

#ifndef OPCUTILS_EXPORTS
template class OPCUTILS_API COpcArray<COpcString>;
#endif

#endif //ndef _COpcArray_H
