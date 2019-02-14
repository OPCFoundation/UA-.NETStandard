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

#ifndef _COpcSortedArray_H
#define _COpcSortedArray_H

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "COpcString.h"

//==============================================================================
// CLASS:   COpcList<KEY, VALUE>
// PURPOSE: Defines a indexable array template class.

template<class KEY, class VALUE>
class COpcSortedArray 
{
    OPC_CLASS_NEW_DELETE_ARRAY()

public:

    //==========================================================================
    // Constructor
    COpcSortedArray(UINT uCapacity = 0, UINT uBlockSize = 16)
    :
        m_pKeys(NULL),
        m_pValues(NULL),
        m_uCapacity(0),
		m_uCount(0),
		m_uBlockSize(uBlockSize)
    {
        SetCapacity(uCapacity);
    }

    //==========================================================================
    // Copy Constructor
    COpcSortedArray(const COpcSortedArray& cArray)
    :
        m_pKeys(NULL),
        m_pValues(NULL),
        m_uCapacity(0),
		m_uCount(0)
    {
        *this = cArray;
    }  

    //==========================================================================
    // Destructor
    ~COpcSortedArray()
    {
        RemoveAll();
    }

    //==========================================================================
    // Assignment
    COpcSortedArray& operator=(const COpcSortedArray& cArray)
    {
		RemoveAll();

        SetCapacity(cArray.m_uCapacity);

        for (UINT ii = 0; ii < cArray.m_uCount; ii++)
        {
            m_pKeys[ii]   = cArray.m_pKeys[ii];
            m_pValues[ii] = cArray.m_pValues[ii];
        }

        return *this;
    }

    //==========================================================================
    // GetCapacity
    UINT GetCapacity() const
    {
        return m_uCapacity;
    }

	//==========================================================================
    // SetCapacity
    void SetCapacity(UINT uNewCapacity)
    {
        if (uNewCapacity == 0)
        {
            RemoveAll();
            return;
        }

        KEY*   pKeys   = new KEY[uNewCapacity];
        VALUE* pValues = new VALUE[uNewCapacity];

        for (UINT ii = 0; ii < uNewCapacity && ii < m_uCount; ii++)
        {
            pKeys[ii]   = m_pKeys[ii];
            pValues[ii] = m_pValues[ii];
        }

        if (m_pKeys != NULL)
        {
            delete [] m_pKeys;
        }

        if (m_pValues != NULL)
        {
            delete [] m_pValues;
        }

        m_pKeys     = pKeys;
        m_pValues   = pValues;
        m_uCapacity = uNewCapacity;

		if (m_uCount > uNewCapacity)
		{
			m_uCount = uNewCapacity;
		}
    }

    //==========================================================================
    // RemoveAll	
    void RemoveAll()
    {		
        if (m_pKeys != NULL)
        {
            delete [] m_pKeys;
			m_pKeys = NULL;
        }

        if (m_pValues != NULL)
        {
            delete [] m_pValues;
			m_pValues = NULL;
        }

        m_uCapacity = 0;
		m_uCount    = 0;
    }

    //==========================================================================
    // Append
    bool Append(const KEY& cKey, const VALUE& cValue)
    {
		// used to bulk load pre-sorted values into the array.
		if (m_uCount > 0)
		{
			if (m_pKeys[m_uCount-1] > cKey)
			{
				return false;
			}
		}

		// increase capacity as required.
		if (m_uCapacity <= m_uCount)
		{
			SetCapacity(m_uCapacity + m_uBlockSize);
		}

		// add to end of list.
		m_pKeys[m_uCount]   = cKey;
		m_pValues[m_uCount] = cValue;

		m_uCount++;

		// ok.
		return true;
    }

    //==========================================================================
    // GetCount
    UINT GetCount() const
    {
        return m_uCount;
    }

    //==========================================================================
    // operator[]	
    VALUE& operator[](UINT uIndex)
    {
        OPC_ASSERT(uIndex < m_uCount);
        return m_pValues[uIndex];
    }

    const VALUE& operator[](UINT uIndex) const
    {
        OPC_ASSERT(uIndex < m_uCount);
        return m_pValues[uIndex];
    }

    //==========================================================================
    // GetKey
    const KEY& GetKey(UINT uIndex) const
    {
        OPC_ASSERT(uIndex < m_uCount);
        return m_pKeys[uIndex];
    }
    
	//==========================================================================
    // GetLastValue
    const VALUE& GetLastValue() const
    {
        OPC_ASSERT(m_uCount > 0);
        return m_pValues[m_uCount-1];
    }

    //==========================================================================
    // IsValidIndex
    bool IsValidIndex(UINT uIndex) const
    {
        return (uIndex < m_uCount);
    }

    //==========================================================================
    // FindIndex
	UINT FindIndex(const KEY& cKey)
	{
		// check for empty list.
		if (m_uCount == 0 || cKey < m_pKeys[0] || cKey > m_pKeys[m_uCount-1])
		{
			return -1;
		}

		// find key.
		UINT uLower = 0;
		UINT uUpper = 0;
		
		if (FindBounds(cKey, uLower, uUpper))
		{
			return uLower;
		}

		return -1;
	}

    //==========================================================================
	// FindIndexBefore
	UINT FindIndexBefore(const KEY& cKey)
	{
		// check for empty list.
		if (m_uCount == 0 || cKey < m_pKeys[0])
		{
			return -1;
		}

		UINT uLower = 0;
		UINT uUpper = 0;

		if (FindBounds(cKey, uLower, uUpper))
		{
			return uUpper;
		}

		return uLower;
	}

    //==========================================================================
	// FindIndexAfter
	UINT FindIndexAfter(const KEY& cKey)
	{
		// check for empty list.
		if (m_uCount == 0 || cKey > m_pKeys[m_uCount-1])
		{
			return -1;
		}

		UINT uLower = 0;
		UINT uUpper = 0;

		if (FindBounds(cKey, uLower, uUpper))
		{
			return uLower;
		}

		return uUpper;
	}

    //==========================================================================
    // Insert	
	UINT Insert(const KEY& cKey, const VALUE& cValue)
	{
		// increase capacity as required.
		if (m_uCapacity <= m_uCount)
		{
			SetCapacity(m_uCapacity + m_uBlockSize);
		}

		// find location in array.
		UINT uLower = 0;
		UINT uUpper = 0;

		bool bKeyExists = FindBounds(cKey, uLower, uUpper);
		
		// insert at end of array.
		if (uUpper == -1)
		{
			m_pKeys[m_uCount] = cKey;
			m_pValues[m_uCount] = cValue;

			// increment count.
			m_uCount++;
			
			// return new index.
			return m_uCount-1;
		}

		// determine index of new value.
		UINT uIndex = (bKeyExists)?uUpper+1:uUpper;

		// shift values after the value up one index.
		for (UINT ii = m_uCount; ii > uIndex; ii--)
		{
			m_pKeys[ii]   = m_pKeys[ii-1];
			m_pValues[ii] = m_pValues[ii-1];
		}

		// add key/value to array.
		m_pKeys[uIndex]   = cKey;
		m_pValues[uIndex] = cValue;
		
		// increment count.
		m_uCount++;

		// return new index.
		return uIndex;
	}

	//==========================================================================
    // RemoveAt	
	void RemoveAt(UINT uIndex, UINT uCount = 1)
	{
		if (uIndex < m_uCount && uCount > 0)
		{
			// adjust the count.
			if (uCount > m_uCount-uIndex)
			{
				uCount = m_uCount-uIndex;
			}

			// shift values after the value down one index.
			for (UINT ii = uIndex; ii < m_uCount-uCount; ii++)
			{
				m_pKeys[ii]   = m_pKeys[ii+uCount];
				m_pValues[ii] = m_pValues[ii+uCount];
			}

			// decrement count.
			m_uCount -= uCount;
		}
	}

    //==========================================================================
	// FindBounds
	bool FindBounds(const KEY& cTarget, UINT& uLower, UINT& uUpper)
	{
		uLower = -1;
		uUpper = -1;

		// check if no values exists.
		if (m_uCount == 0)
		{
			return false;
		}

		// check if target is before first value.
		if (cTarget < m_pKeys[0])
		{
			uLower = -1;
			uUpper = 0;
			return false;
		}

		// check if target is after last value.
		if (cTarget > m_pKeys[m_uCount-1])
		{
			uLower = m_uCount-1;
			uUpper = -1;
			return false;
		}

		// check if target is the last value.
		if (cTarget == m_pKeys[m_uCount-1])
		{
			uLower = uUpper = m_uCount-1;

			// adjust lower bound to beginning duplicate keys.
			while (uLower > 0 && m_pKeys[uLower-1] == cTarget)
			{
				uLower--;
			}

			return true;
		}

		// start with full range of data.
		uLower = 0;
		uUpper = m_uCount-1;

		// narrow the bounds as much as possible.
		while (uUpper - uLower > 1 && cTarget != m_pKeys[uLower])
		{	
			// split the range in half and discard the half that the key does not belong to. 
			UINT uBound = uUpper - (uUpper-uLower)/2;

			// keep the lower range.
			if (KeyInRange(uLower, uBound, cTarget))
			{
				uUpper = uBound;
			}

			// keep the upper range.
			else
			{
				uLower = uBound;
			}
		}

		// a value at the key does not exist.
		if (cTarget != m_pKeys[uLower])
		{
			return false;
		}

		// adjust upper bound end of range of duplicate keys.
		uUpper = uLower;

		while (uUpper < m_uCount-1 && m_pKeys[uUpper+1] == cTarget)
		{
			uUpper++;
		}

		// adjust lower bound to beginning duplicate timestamps.
		while (uLower > 0 && m_pKeys[uLower-1] == cTarget)
		{
			uLower--;
		}

		// a value at the time does exist.
		return true;
	}

private:

	//==========================================================================
	// KeyInRange
	bool KeyInRange(UINT uLower, UINT uUpper, const KEY& cTarget)
	{
		// ensure upper bound is a valid index.
		if (uUpper > m_uCount)
		{
			uUpper = m_uCount-1;
		}

		// check if lower bound is a valid index.
		if (uLower >= m_uCount || uUpper < uLower)
		{
			return false;
		}

		// check if target is within range.
		if (m_pKeys[uLower] <= cTarget)
		{
			if (m_pKeys[uUpper] > cTarget || (m_pKeys[uLower] == cTarget && m_pKeys[uLower] == m_pKeys[uUpper]))
			{
				return true;
			}
		}

		// target is not within range.
		return false;
	}

	KEY*   m_pKeys;
	VALUE* m_pValues;
	UINT   m_uCapacity;
	UINT   m_uCount;
	UINT   m_uBlockSize;
};

//==============================================================================
// VALUE:    COpcStringArray
// PURPOSE: An array of strings.

typedef COpcSortedArray<COpcString,COpcString> COpcSortedStringArray;

#ifndef OPCUTILS_EXPORTS
template class OPCUTILS_API COpcSortedArray<COpcString,COpcString>;
#endif

#endif //ndef _COpcSortedArray_H
