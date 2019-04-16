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

#ifndef _COpcMap_H_
#define _COpcMap_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcDefs.h"
#include "COpcString.h"

//==============================================================================
// TYPE:    OPC_POS
// PURPOSE: A position when enumerating the hash table.

#ifndef _OPC_POS
#define _OPC_POS
typedef struct TOpcPos{}* OPC_POS;
#endif //_OPC_POS

//==============================================================================
// CLASS:   COpcMap<KEY, VALUE>
// PURPOSE: Defines a hash table template class.

template<class KEY,class VALUE>
class COpcMap 
{
   OPC_CLASS_NEW_DELETE_ARRAY()

public:

    //==========================================================================
    // COpcEntry
    class COpcEntry
    {
        OPC_CLASS_NEW_DELETE_ARRAY()

    public:

        COpcEntry* pNext;
        KEY        cKey;
        VALUE      cValue;
        UINT       uHash;

        COpcEntry() : pNext(NULL), uHash(0) {}
    };

    //==========================================================================
    // COpcBlock
    class COpcBlock
    {
        OPC_CLASS_NEW_DELETE_ARRAY()

    public:

        COpcBlock(UINT uBlockSize)
        :
           pNext(NULL),
           pEntries(NULL)
        {
            pEntries = new COpcEntry[uBlockSize];
        }

        ~COpcBlock()
        {
            delete [] pEntries;
        }

        COpcBlock* pNext;
        COpcEntry* pEntries;
    };

public:

    //==========================================================================
    // Constructor
    COpcMap(int nBlockSize = 10, int uTableSize = 17)
    :
        m_pUnusedEntries(NULL),
        m_uCount(0),
        m_pBlocks(NULL),
        m_uBlockSize(nBlockSize),
        m_ppHashTable(NULL),
        m_uTableSize(0)
    {
        InitHashTable(uTableSize);
    }

    //==========================================================================
    // Copy Constructor
    COpcMap(const COpcMap& cMap)
    :
        m_pUnusedEntries(NULL),
        m_uCount(0),
        m_pBlocks(NULL),
        m_uBlockSize(cMap.m_uBlockSize),
        m_ppHashTable(NULL),
        m_uTableSize(0)
    {
        *this = cMap;
    }

    //==========================================================================
    // Destructor
    ~COpcMap()
    {
        RemoveAll();
        delete [] m_ppHashTable;
    }

    //==========================================================================
    // Assignment
    COpcMap& operator=(const COpcMap& cMap)
    {
        InitHashTable(cMap.m_uTableSize);

        KEY cKey;
        VALUE cValue;
        OPC_POS pos = cMap.GetStartPosition();

        while (pos != NULL)
        {
            cMap.GetNextAssoc(pos, cKey, cValue);
            SetAt(cKey, cValue);
        }

        return *this;
    }

    //==========================================================================
    // GetCount
    int GetCount() const
    {
        return m_uCount;
    }

    //==========================================================================
    // IsEmpty
    bool IsEmpty() const
    {
        return (m_uCount == 0);
    }

    //==========================================================================
    // Lookup - return false if not there
    bool Lookup(const KEY& cKey, VALUE** ppValue = NULL) const
    {
        COpcEntry* pEntry = Find(cKey);

        if (pEntry == NULL)
        {
            return false;
        }

        if (ppValue != NULL)
        {
            *ppValue = &(pEntry->cValue);
        }

        return true;
    }

    //==========================================================================
    // Lookup - return false if not there
    bool Lookup(const KEY& cKey, VALUE& cValue) const
    {
        COpcEntry* pEntry = Find(cKey);

        if (pEntry == NULL)
        {
            return false;
        }

        cValue = pEntry->cValue;
        return true;
    }

    //==========================================================================
    // Lookup - and add if not there
    VALUE& operator[](const KEY& cKey)
    {
        COpcEntry* pEntry = Find(cKey);

        if (pEntry == NULL)
        {
            pEntry = NewEntry(cKey);
        }

        return pEntry->cValue;
    }

    //==========================================================================
    // SetAt - add a new (key, value) pair
    void SetAt(const KEY& cKey, const VALUE& cValue)
    {
        (*this)[cKey] = cValue;
    }

    //==========================================================================
    // RemoveKey - removing existing (key, ?) pair
    bool RemoveKey(const KEY& cKey)
    {
        UINT uBin = HashKey(cKey)%m_uTableSize;
        COpcEntry* pEntry = m_ppHashTable[uBin];
        COpcEntry* pPrev  = NULL;

        while (pEntry != NULL)
        {
            if (pEntry->cKey == cKey)
            {
                if (pPrev == NULL)
                {
                    m_ppHashTable[uBin] = pEntry->pNext;
                }
                else
                {
                    pPrev->pNext = pEntry->pNext;
                }

                FreeEntry(pEntry);
                return true;
            }

            pPrev  = pEntry;
            pEntry = pEntry->pNext;
        }

        return false;
    }

    //==========================================================================
    // RemoveAll
    void RemoveAll()
    {
        COpcBlock* pBlock = m_pBlocks;
        COpcBlock* pNext  = NULL;

        while (pBlock != NULL)
        {
            pNext = pBlock->pNext;
            delete pBlock;
            pBlock = pNext;
        }

        m_uCount   = 0;
        m_pUnusedEntries = NULL;
        m_pBlocks  = NULL;
        memset(m_ppHashTable, 0, m_uTableSize*sizeof(COpcEntry*));
    }

    //==========================================================================
    // GetStartPosition
    OPC_POS GetStartPosition() const
    {
        UINT uBin = 0;

        while (uBin < m_uTableSize && m_ppHashTable[uBin] == NULL) 
        {
            uBin++;
        }

        if (uBin == m_uTableSize)
        {
            return (OPC_POS)NULL;
        }

        return (OPC_POS)m_ppHashTable[uBin];
    }

    //==========================================================================
    // GetNextAssoc
    void GetNextAssoc(OPC_POS& pos, KEY& cKey) const
    {
        COpcEntry* pEntry = (COpcEntry*)pos;
        OPC_ASSERT(pos != NULL);

        cKey = pEntry->cKey;

        pos = GetNextAssoc(pos);
    }

    //==========================================================================
    // GetNextAssoc
    void GetNextAssoc(OPC_POS& pos, KEY& cKey, VALUE& cValue) const
    {
        COpcEntry* pEntry = (COpcEntry*)pos;
        OPC_ASSERT(pos != NULL);

        cKey = pEntry->cKey;
        cValue = pEntry->cValue;

        pos = GetNextAssoc(pos);
    }

    //==========================================================================
    // GetNextAssoc
    void GetNextAssoc(OPC_POS& pos, KEY& cKey, VALUE*& pValue) const
    {
        COpcEntry* pEntry = (COpcEntry*)pos;
        OPC_ASSERT(pos != NULL);

        cKey = pEntry->cKey;
        pValue = &(pEntry->cValue);

        pos = GetNextAssoc(pos);
    }
    
	//==========================================================================
    // IsValid
    bool IsValid(OPC_POS pos) const
    {
        COpcBlock* pBlock = m_pBlocks;

        while (pBlock != NULL)
        {			
			for (UINT ii = 0; ii < m_uBlockSize; ii++)
            {
				if (pos == (OPC_POS)&(pBlock->pEntries[ii]))
				{
					return true;
				}
            }

            pBlock = pBlock->pNext;
        }

		return false;
    }
    
	//==========================================================================
    // GetPosition
    OPC_POS GetPosition(const KEY& cKey) const
    {
        return (OPC_POS)Find(cKey);
    }

    //==========================================================================
    // GetKey
    const KEY& GetKey(OPC_POS pos) const
    {
        OPC_ASSERT(IsValid(pos));
        return ((COpcEntry*)pos)->cKey;
    }
    
	//==========================================================================
    // GetValue
    VALUE& GetValue(OPC_POS pos)
    {
        OPC_ASSERT(IsValid(pos));
        return ((COpcEntry*)pos)->cValue;
    }

    //==========================================================================
    // GetValue
    const VALUE& GetValue(OPC_POS pos) const
    {
        OPC_ASSERT(IsValid(pos));
        return ((COpcEntry*)pos)->cValue;
    }

    //==========================================================================
    // GetBlockSize
    UINT GetBlockSize() const
    {
        return m_uBlockSize;
    }

    //==========================================================================
    // GetHashTableSize
    UINT GetHashTableSize() const
    {
        return m_uTableSize;
    }

    //==========================================================================
    // InitHashTable
    void InitHashTable(UINT uTableSize)
    {
        COpcEntry* pEntries = NULL;

        for (UINT ii = 0; ii < m_uTableSize; ii++)
        {
            COpcEntry* pEntry = m_ppHashTable[ii];
            COpcEntry* pNext  = NULL;

            while (pEntry != NULL)
            {
                pNext         = pEntry->pNext;
                pEntry->pNext = pEntries;
                pEntries      = pEntry;
                pEntry        = pNext;
            }
        }

        delete [] m_ppHashTable;

        m_uTableSize = uTableSize;
        m_ppHashTable = new COpcEntry*[m_uTableSize];
        memset(m_ppHashTable, 0, m_uTableSize*sizeof(COpcEntry*));

        COpcEntry* pEntry = pEntries;
        COpcEntry* pNext  = NULL;

        while (pEntry != NULL)
        {
            pNext = pEntry->pNext;

            UINT uBin = (pEntry->uHash)%m_uTableSize;
            pEntry->pNext       = m_ppHashTable[uBin];
            m_ppHashTable[uBin] = pEntry;

            pEntry = pNext;
        }
    }

private:

    //==========================================================================
    // Find
    COpcEntry* Find(const KEY& cKey) const
    {
        UINT uBin = HashKey(cKey)%m_uTableSize;
        COpcEntry* pEntry = m_ppHashTable[uBin];

        while (pEntry != NULL)
        {
            if (pEntry->cKey == cKey)
            {
                return pEntry;
            }

            pEntry = pEntry->pNext;
        }

        return NULL;
    }

    //==========================================================================
    // NewEntry
    COpcEntry* NewEntry(const KEY& cKey)
    {
        // optimize hash table size.
        if (m_uTableSize < 1.2*m_uCount)
        {
            InitHashTable(2*m_uCount);
        }

        // create a new block if necessary.
        if (m_pUnusedEntries == NULL)
        {
            COpcBlock* pBlock = new COpcBlock(m_uBlockSize);

            for (UINT ii = 0; ii < m_uBlockSize; ii++)
            {
                pBlock->pEntries[ii].pNext = m_pUnusedEntries;
                m_pUnusedEntries           = &(pBlock->pEntries[ii]);
            }

            pBlock->pNext = m_pBlocks;
            m_pBlocks     = pBlock;
        }

        OPC_ASSERT(m_pUnusedEntries != NULL); 

        // remove entry from unused entry list.
        COpcEntry* pEntry   = m_pUnusedEntries;
        m_pUnusedEntries   = m_pUnusedEntries->pNext;

        // insert entry into hash table.
        pEntry->cKey  = cKey;
        pEntry->uHash = HashKey(cKey);

        UINT uBin           = pEntry->uHash%m_uTableSize;
        pEntry->pNext       = m_ppHashTable[uBin];
        m_ppHashTable[uBin] = pEntry;

        m_uCount++;

        return pEntry;
    }

    //==========================================================================
    // FreeEntry
    void FreeEntry(COpcEntry* pEntry)
    {
        // return to unused entries list
        pEntry->pNext    = m_pUnusedEntries;
        m_pUnusedEntries = pEntry;
        m_uCount--;
        OPC_ASSERT(m_uCount >= 0);  // make sure we don't underflow

        // if no more elements, cleanup completely
        if (m_uCount == 0)
        {
            RemoveAll();
        }
    }

    //==========================================================================
    // GetNextAssoc
    OPC_POS GetNextAssoc(OPC_POS pos) const
    {
        COpcEntry* pEntry = (COpcEntry*)pos;
        OPC_ASSERT(pos != NULL);

        if (pEntry->pNext == NULL)
        {
            UINT uBin = pEntry->uHash%m_uTableSize;

            do
            {
                uBin++;
            }
            while (uBin < m_uTableSize && m_ppHashTable[uBin] == NULL);

            if (uBin == m_uTableSize)
            {
                return (OPC_POS)NULL;
            }

            return (OPC_POS)m_ppHashTable[uBin];
        }

        return (OPC_POS)pEntry->pNext;
    }

    //==========================================================================
    // Members
    COpcEntry** m_ppHashTable;
    UINT        m_uTableSize;
    UINT        m_uCount;
    COpcEntry*  m_pUnusedEntries;
    COpcBlock*  m_pBlocks;
    UINT        m_uBlockSize;
};


//==============================================================================
// FUNCTION: HashKey<KEY>
// PURPOSE:  Default hash key generator.
template<class KEY>
inline UINT HashKey(const KEY& cKey)
{
	return ((UINT)(void*)(DWORD)cKey) >> 4;
}

//==============================================================================
// FUNCTION: HashKey<LPCTSTR>
// PURPOSE:  String hash key generator.
template<> 
inline UINT HashKey<LPCTSTR> (const LPCTSTR& tsKey)
{
    LPCTSTR key = tsKey;
    if (key == NULL) return -1;

	UINT nHash = 0;
	while (*key)
		nHash = (nHash<<5) + nHash + *key++;
	return nHash;
}

//==============================================================================
// FUNCTION: HashKey<COpcString>
// PURPOSE:  String object hash key generator.
template<> 
inline UINT HashKey<COpcString> (const COpcString& cKey)
{
    LPCTSTR key = cKey;
    if (key == NULL) return -1;

	UINT nHash = 0;
	while (*key)
		nHash = (nHash<<5) + nHash + *key++;
	return nHash;
}

//==============================================================================
// FUNCTION: HashKey<GUID>
// PURPOSE:  GUID hash key generator.
template<> 
inline UINT HashKey<GUID> (const GUID& cKey)
{
	UINT nHash = 0;

	nHash ^= cKey.Data1;
	nHash ^= (cKey.Data2<<16 | cKey.Data3);
	nHash ^= ((cKey.Data4[0]<<24) | (cKey.Data4[1]<<16) |(cKey.Data4[2]<<8) | (cKey.Data4[3]));
	nHash ^= ((cKey.Data4[4]<<24) | (cKey.Data4[5]<<16) |(cKey.Data4[6]<<8) | (cKey.Data4[7]));

	return nHash;
}

//==============================================================================
// TYPE:    COpcStringMap
// PURPOSE: A string to string map.

typedef COpcMap<COpcString,COpcString> COpcStringMap;

//#ifndef OPCUTILS_EXPORTS
template class OPCUTILS_API COpcMap<COpcString,COpcString>;
//#endif

#endif //ndef _COpcMap_H_
