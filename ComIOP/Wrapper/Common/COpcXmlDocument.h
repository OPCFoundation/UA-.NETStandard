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

#ifndef _COpcXmlDocument_H_
#define _COpcXmlDocument_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "COpcMap.h"
#include "OpcXmlType.h"
#include "COpcXmlElement.h"

//==============================================================================
// CLASS:   COpcXmlDocument
// PURPOSE  Facilitiates manipulation of XML documents,

class OPCUTILS_API COpcXmlDocument 
{
    OPC_CLASS_NEW_DELETE()

public:

    //==========================================================================
    // Public Operators

    // Constructor
    COpcXmlDocument(IXMLDOMDocument* ipUnknown = NULL);

    // Copy Constructor
    COpcXmlDocument(const COpcXmlDocument& cDocument);
            
    // Destructor
    ~COpcXmlDocument();

    // Assignment
    COpcXmlDocument& operator=(IUnknown* ipUnknown);
    COpcXmlDocument& operator=(const COpcXmlDocument& cDocument);

    // Accessor
    operator IXMLDOMDocument*() const { return m_ipDocument; }

    //==========================================================================
    // Public Methods
    
    // Init
    virtual bool Init();

    // Clear
    virtual void Clear();

	// New
    virtual bool New();

    // New
    virtual bool New(const COpcString& cRoot, const COpcString& cDefaultNamespace);

	// New
    virtual bool New(IXMLDOMElement* ipElement);

    // Init
    virtual bool LoadXml(LPCWSTR szXml);

    // Load
    virtual bool Load(const COpcString& cFilePath = OPC_EMPTY_STRING);

    // Save
    virtual bool Save(const COpcString& cFilePath = OPC_EMPTY_STRING);

    // GetRoot
    COpcXmlElement GetRoot() const;

	// GetXml
	bool GetXml(COpcString& cXml) const;

    // GetDefaultNamespace
    COpcString GetDefaultNamespace();

    // AddNamespace
    bool AddNamespace(const COpcString& cPrefix, const COpcString& cNamespace);

	// GetNamespaces
	void GetNamespaces(COpcStringMap& cNamespaces);

	// GetNamespacePrefix
	COpcString GetNamespacePrefix(const COpcString& cNamespace);

	// FindElement
	COpcXmlElement FindElement(const COpcString& cXPath);
	
	// FindElements
	UINT FindElements(const COpcString& cXPath, COpcXmlElementList& cElements);

protected:
    
    //==========================================================================
    // Protected Methods

    // GetFilePath
    const COpcString& GetFilePath() const { return m_cFilePath; }

    // SetFilePath
    void SetFilePath(const COpcString& cFilePath) { m_cFilePath = cFilePath; }

private:

    //==========================================================================
    // Private Members

    COpcString       m_cFilePath;
    IXMLDOMDocument* m_ipDocument;
};

#endif // _COpcXmlDocument_H_
