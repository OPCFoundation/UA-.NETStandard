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

#ifndef _COpcBrowseElement_H_
#define _COpcBrowseElement_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "COpcString.h"
#include "COpcList.h"

//============================================================================
// TYPE:    COpcBrowseElementList
// PURPOSE: A ordered list of server namespace elements.

class COpcBrowseElement;
typedef COpcList<COpcBrowseElement*> COpcBrowseElementList;

//============================================================================
// CLASS:   COpcBrowseElement
// PURPOSE: Describes an element in the server namespace.

class COpcBrowseElement
{
    OPC_CLASS_NEW_DELETE()

public:

    //========================================================================
    // Public Operators

    // Constructor
    COpcBrowseElement(COpcBrowseElement* pParent);

    // Destructor
    ~COpcBrowseElement() { Clear(); }

    //========================================================================
    // Public Methods
    
    // Init
    void Init();

    // Clear
    void Clear();

    // GetName
    COpcString GetName() const;

    // GetItemID
    COpcString GetItemID() const;

    // GetBrowsePath
    COpcString GetBrowsePath() const;

    // GetSeparator
    COpcString GetSeparator() const;

    // GetParent
    COpcBrowseElement* GetParent() const { return m_pParent; }

    // GetChild
    COpcBrowseElement* GetChild(UINT uIndex) const;

	// Browse
	void Browse(
		const COpcString& cPath,
		bool              bFlat, 
		COpcStringList&   cNodes
	);

    // Find
    COpcBrowseElement* Find(const COpcString& cPath);
    
    // Insert
    COpcBrowseElement* Insert(const COpcString& cPath);

    // Insert
    COpcBrowseElement* Insert(
        const COpcString& cPath,
        const COpcString& cItemID
    );

    // Remove
    void Remove();

    // Remove
    bool Remove(const COpcString& cName);

protected:
    
	//========================================================================
    // Protected Methods

    // CreateInstance
    virtual COpcBrowseElement* CreateInstance();

    //========================================================================
    // Protected Members

    COpcBrowseElement* m_pParent;
    COpcString         m_cItemID;
    COpcString         m_cName;
    COpcString         m_cSeparator;

    COpcBrowseElementList m_cChildren;
};

#endif // _COpcBrowseElement_H_
