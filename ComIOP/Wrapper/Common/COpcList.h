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

#ifndef _COpcList_H_
#define _COpcList_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcDefs.h"
#include "COpcString.h"

//==============================================================================
// TYPE:    OPC_POS
// PURPOSE: A position when enumerating the list.

#ifndef _OPC_POS
#define _OPC_POS
typedef struct TOpcPos{}* OPC_POS;
#endif //_OPC_POS

//==============================================================================
// CLASS:   COpcList<TYPE>
// PURPOSE: Defines a linked list template class.

template<class TYPE>
class COpcList
{
   OPC_CLASS_NEW_DELETE_ARRAY()

public:

    //==========================================================================
    // Constructor
    COpcList(UINT uBlockSize = 10)
    {
        m_uCount = 0;
        m_pNodeHead = m_pNodeTail = m_pNodeFree = NULL;
        m_pBlocks = NULL;
        m_uBlockSize = uBlockSize;
    }

    //==========================================================================  
    // Copy Constructor
    COpcList(const COpcList& cList)
    {
        m_uCount = 0;
        m_pNodeHead = m_pNodeTail = m_pNodeFree = NULL;
        m_pBlocks = NULL;
        m_uBlockSize = cList.m_uBlockSize;

        *this = cList;
    }

    //========================================================================== 
    // Destructor
    ~COpcList()
    {
        RemoveAll();
    }

    //==========================================================================  
    // Assignment
    COpcList& operator=(const COpcList& cList)
    {
        RemoveAll();

        m_uBlockSize = cList.m_uBlockSize;

        OPC_POS pos = cList.GetHeadPosition();

        while (pos != NULL)
        {
            AddTail(cList.GetNext(pos));       
        }

        return *this;
    }

    //==========================================================================
    // GetCount
    UINT GetCount() const
    {
        return m_uCount;
    }

    //==========================================================================
    // IsEmpty
    BOOL IsEmpty() const
    {
        return (m_uCount == 0);
    }

    //==========================================================================
    // GetBlockSize
    UINT GetBlockSize() const
    {
        return m_uBlockSize;
    }

    //==========================================================================
    // GetHead
    TYPE& GetHead()
    {
        OPC_ASSERT(m_pNodeHead != NULL);
        return m_pNodeHead->data;
    }

    TYPE GetHead() const
    {
        OPC_ASSERT(m_pNodeHead != NULL);
        return m_pNodeHead->data;
    }

    //==========================================================================
    // GetTail
    TYPE& GetTail()
    {
        OPC_ASSERT(m_pNodeTail != NULL);
        return m_pNodeTail->data;
    }

    TYPE GetTail() const
    {
        OPC_ASSERT(m_pNodeTail != NULL);
        return m_pNodeTail->data;
    }

    //==========================================================================
    // RemoveHead
    TYPE RemoveHead()
    {
        OPC_ASSERT(m_pNodeHead != NULL);  // don't call on empty list !!!

        CVsNode* pOldNode = m_pNodeHead;
        TYPE returnValue = pOldNode->data;

        m_pNodeHead = pOldNode->pNext;
        
        if (m_pNodeHead != NULL)
        {
            m_pNodeHead->pPrev = NULL;
        }
        else
        {
            m_pNodeTail = NULL;
        }

        FreeNode(pOldNode);
        return returnValue;
    }

    //==========================================================================
    // RemoveTail
    TYPE RemoveTail()
    {
        OPC_ASSERT(m_pNodeTail != NULL);  // don't call on empty list !!!

        CVsNode* pOldNode = m_pNodeTail;
        TYPE returnValue = pOldNode->data;

        m_pNodeTail = pOldNode->pPrev;
        
        if (m_pNodeTail != NULL)
        {
            m_pNodeTail->pNext = NULL;
        }
        else
        {
            m_pNodeHead = NULL;
        }

        FreeNode(pOldNode);
        return returnValue;
    }

    //==========================================================================
    // AddHead
    OPC_POS AddHead(const TYPE& newElement)
    {
        CVsNode* pNewNode = NewNode(NULL, m_pNodeHead);
        pNewNode->data = newElement;

        if (m_pNodeHead != NULL)
        {
            m_pNodeHead->pPrev = pNewNode;
        }
        else
        {
            m_pNodeTail = pNewNode;
        }

        m_pNodeHead = pNewNode;
        return (OPC_POS) pNewNode;
    }

    void AddHead(COpcList* pNewList)
    {
        OPC_ASSERT(pNewList != NULL);

        // add a list of same elements to head (maintain order)
        OPC_POS pos = pNewList->GetTailPosition();
        
        while (pos != NULL)
        {
            AddHead(pNewList->GetPrev(pos));
        }
    }

    //==========================================================================
    // AddTail
    OPC_POS AddTail(const TYPE& newElement)
    {
        CVsNode* pNewNode = NewNode(m_pNodeTail, NULL);
        pNewNode->data = newElement;
        
        if (m_pNodeTail != NULL)
        {
            m_pNodeTail->pNext = pNewNode;
        }
        else
        {
            m_pNodeHead = pNewNode;
        }
        
        m_pNodeTail = pNewNode;
        return (OPC_POS) pNewNode;
    }

    void AddTail(COpcList* pNewList)
    {
        OPC_ASSERT(pNewList != NULL);

        // add a list of same elements
        OPC_POS pos = pNewList->GetHeadPosition();
        
        while (pos != NULL)
        {
            AddTail(pNewList->GetNext(pos));
        }
    }

    //==========================================================================
    // RemoveAll
    void RemoveAll()
    {
        CVsBlock* pBlock = m_pBlocks;
        CVsBlock* pNext  = NULL;

        while (pBlock != NULL)
        {
            pNext = pBlock->pNext;
            delete pBlock;
            pBlock = pNext;
        }

        m_uCount = 0;
        m_pNodeHead = m_pNodeTail = m_pNodeFree = NULL;
        m_pBlocks = NULL;
    }

    //==========================================================================
    // GetHeadPosition
    OPC_POS GetHeadPosition() const
    {
        return (OPC_POS)m_pNodeHead;
    }

    //==========================================================================
    // GetTailPosition
    OPC_POS GetTailPosition() const
    {
        return (OPC_POS)m_pNodeTail;
    }

    //==========================================================================
    // GetNext
    TYPE& GetNext(OPC_POS& rPosition) // return *Position++
    { 
        OPC_ASSERT(rPosition != NULL);
        CVsNode* pNode = (CVsNode*) rPosition;
        rPosition = (OPC_POS) pNode->pNext;
        return pNode->data; 
    }

    TYPE GetNext(OPC_POS& rPosition) const // return *Position++
    { 
        OPC_ASSERT(rPosition != NULL);
        CVsNode* pNode = (CVsNode*) rPosition;
        rPosition = (OPC_POS) pNode->pNext;
        return pNode->data; 
    }

    //==========================================================================
    // GetPrev
    TYPE& GetPrev(OPC_POS& rPosition) // return *Position--
    { 
        CVsNode* pNode = (CVsNode*) rPosition;
        rPosition = (OPC_POS) pNode->pPrev;
        return pNode->data; 
    }

    TYPE GetPrev(OPC_POS& rPosition) const // return *Position--
    { 
        CVsNode* pNode = (CVsNode*) rPosition;
        rPosition = (OPC_POS) pNode->pPrev;
        return pNode->data; 
    }

    //==========================================================================
    // GetAt
    TYPE& GetAt(OPC_POS position)
    { 
        CVsNode* pNode = (CVsNode*) position;
        return pNode->data; 
    }

    TYPE GetAt(OPC_POS position) const
    { 
        CVsNode* pNode = (CVsNode*) position;
        return pNode->data; 
    }

    //==========================================================================
    // operator[]
    TYPE& operator[](OPC_POS position) 
    {
        return GetAt(position);
    }

    TYPE operator[](OPC_POS position) const 
    {
        return GetAt(position);
    }

    //==========================================================================
    // SetAt
    void SetAt(OPC_POS pos, const TYPE& newElement)
    { 
        CVsNode* pNode = (CVsNode*)pos;
        pNode->data = newElement; 
    }

    //==========================================================================
    // RemoveAt
    void RemoveAt(OPC_POS position)
    {
        CVsNode* pOldNode = (CVsNode*) position;

        // remove pOldNode from list
        if (pOldNode == m_pNodeHead)
        {
            m_pNodeHead = pOldNode->pNext;
        }
        else
        {
            pOldNode->pPrev->pNext = pOldNode->pNext;
        }
        if (pOldNode == m_pNodeTail)
        {
            m_pNodeTail = pOldNode->pPrev;
        }
        else
        {
            pOldNode->pNext->pPrev = pOldNode->pPrev;
        }

        FreeNode(pOldNode);
    }

    //==========================================================================
    // InsertBefore
    OPC_POS InsertBefore(OPC_POS position, const TYPE& newElement)
    {
        if (position == NULL)
        return AddHead(newElement); // insert before nothing -> head of the list

        // Insert it before position
        CVsNode* pOldNode = (CVsNode*) position;
        CVsNode* pNewNode = NewNode(pOldNode->pPrev, pOldNode);
        pNewNode->data = newElement;

        if (pOldNode->pPrev != NULL)
        {
            pOldNode->pPrev->pNext = pNewNode;
        }
        else
        {
            OPC_ASSERT(pOldNode == m_pNodeHead);
            m_pNodeHead = pNewNode;
        }

        pOldNode->pPrev = pNewNode;
        return (OPC_POS) pNewNode;
    }

    //==========================================================================
    // InsertAfter
    OPC_POS InsertAfter(OPC_POS position, const TYPE& newElement)
    {
        if (position == NULL)
        return AddTail(newElement); // insert after nothing -> tail of the list

        // Insert it before position
        CVsNode* pOldNode = (CVsNode*) position;
        CVsNode* pNewNode = NewNode(pOldNode, pOldNode->pNext);
        pNewNode->data = newElement;

        if (pOldNode->pNext != NULL)
        {
            pOldNode->pNext->pPrev = pNewNode;
        }
        else
        {
            OPC_ASSERT(pOldNode == m_pNodeTail);
            m_pNodeTail = pNewNode;
        }

        pOldNode->pNext = pNewNode;
        return (OPC_POS) pNewNode;
    }

    //==========================================================================
    // Find - helper functions (note: O(n) speed)
    OPC_POS Find(const TYPE& searchValue, OPC_POS startAfter = NULL) const
    {
        CVsNode* pNode = (CVsNode*)startAfter;

        if (pNode == NULL)
        {
            pNode = m_pNodeHead;  // start at head
        }
        else
        {
            pNode = pNode->pNext;  // start after the one specified
        }

        for (; pNode != NULL; pNode = pNode->pNext)
        {
            if (pNode->data == searchValue)
            {
               return (OPC_POS)pNode;
            }
        }

        return NULL;
    }

    //==========================================================================
    // FindIndex - defaults to starting at the HEAD, return NULL if not found
    OPC_POS FindIndex(UINT nIndex) const
    {
        if (nIndex >= m_uCount || nIndex < 0)
        {
            return NULL;  // went too far
        }

        CVsNode* pNode = m_pNodeHead;

        while (nIndex--)
        {
            pNode = pNode->pNext;
        }

        return (OPC_POS)pNode;
    }

    protected:

    //==========================================================================
    // CVsNode
    struct CVsNode
    {
        OPC_CLASS_NEW_DELETE_ARRAY()

        CVsNode* pNext;
        CVsNode* pPrev;
        TYPE     data;
    };

    //==========================================================================
    // CVsBlock
    class CVsBlock
    {
        OPC_CLASS_NEW_DELETE_ARRAY()

        public:

        CVsBlock(UINT uBlockSize)
        {
            pNodes = new CVsNode[uBlockSize];
        }

        ~CVsBlock()
        {
            delete [] pNodes;
        }

        CVsBlock* pNext;
        CVsNode*  pNodes;
    };

    //==========================================================================
    // NewNode
    CVsNode* NewNode(CVsNode* pPrev, CVsNode* pNext)
    {
        if (m_pNodeFree == NULL)
        {
            CVsBlock* pBlock = new CVsBlock(m_uBlockSize);

            for (UINT ii = 0; ii < m_uBlockSize; ii++)
            {
                pBlock->pNodes[ii].pNext = m_pNodeFree;
                m_pNodeFree               = &(pBlock->pNodes[ii]);
            }

            pBlock->pNext = m_pBlocks;
            m_pBlocks     = pBlock;
        }

        OPC_ASSERT(m_pNodeFree != NULL); 

        CVsNode* pNode = m_pNodeFree;
        m_pNodeFree    = m_pNodeFree->pNext;
        pNode->pPrev   = pPrev;
        pNode->pNext   = pNext;

        m_uCount++;
        OPC_ASSERT(m_uCount > 0);  // make sure we don't overflow

        return pNode;
    }

    //==========================================================================
    // FreeNode
    void FreeNode(CVsNode* pNode)
    {
        pNode->pNext = m_pNodeFree;
        m_pNodeFree  = pNode;
        m_uCount--;
        OPC_ASSERT(m_uCount >= 0);  // make sure we don't underflow

        // if no more elements, cleanup completely
        if (m_uCount == 0)
        RemoveAll();
    }

    //==========================================================================
    // Members
    CVsNode*  m_pNodeHead;
    CVsNode*  m_pNodeTail;
    UINT      m_uCount;
    CVsNode*  m_pNodeFree;
    CVsBlock* m_pBlocks;
    UINT      m_uBlockSize;
};

//==============================================================================
// TYPE:    COpcStringList
// PURPOSE: A list of strings.

typedef COpcList<COpcString> COpcStringList;

#ifndef OPCUTILS_EXPORTS
template class OPCUTILS_API COpcList<COpcString>;
#endif

#endif //ndef _COpcList_H_
