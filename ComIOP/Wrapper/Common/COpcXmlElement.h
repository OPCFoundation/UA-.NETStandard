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

#ifndef _COpcXmlElement_H_
#define _COpcXmlElement_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "COpcString.h"
#include "COpcMap.h"
#include "COpcArray.h"
#include "OpcXmlType.h"
#include "COpcXmlAttribute.h"

class COpcXmlElement;

//==============================================================================
// TYPE:    COpcXmlElementList
// PURPOSE: A list of elements.

typedef COpcArray<COpcXmlElement> COpcXmlElementList;

//==============================================================================
// CLASS:   COpcXmlElement
// PURPOSE  Facilitiates manipulation of XML Elements,

class OPCUTILS_API COpcXmlElement 
{
    OPC_CLASS_NEW_DELETE_ARRAY();

public:

    //==========================================================================
    // Public Operators

    // Constructor
    COpcXmlElement(IUnknown* ipUnknown = NULL);

    // Copy Constructor
    COpcXmlElement(const COpcXmlElement& cElement);
            
    // Destructor
    ~COpcXmlElement();

    // Assignment
    COpcXmlElement& operator=(IUnknown* ipUnknown);
    COpcXmlElement& operator=(const COpcXmlElement& cElement);

    // Accessor
    operator IXMLDOMElement*() const { return m_ipElement; }

    //==========================================================================
    // Public Methods
       
    // GetName
    COpcString GetName();
    	
	// GetPrefix
    COpcString GetPrefix();
                
	// GetNamespace
	COpcString GetNamespace();

	// GetQualifiedName
	OpcXml::QName GetQualifiedName();

    // Value
    COpcString GetValue();
    void SetValue(const COpcString& cValue);
    
	// Type
	bool GetType(OpcXml::QName& cType);
    void SetType(OpcXml::QName cType);
	
	// GetAttribute
    COpcXmlAttribute GetAttribute(const COpcString& cName);
	
	// GetAttribute
	COpcXmlAttribute GetAttribute(const COpcString& cName, const COpcString& cNamespace);

	// GetAttributes
	UINT GetAttributes(COpcStringMap& cAttributes);

	// GetAttributes
	UINT GetAttributes(COpcXmlAttributeList& cAttributes);

    // SetAttribute
    COpcXmlAttribute SetAttribute(const COpcString& cName, const COpcString& cValue);

    // GetChild
    COpcXmlElement GetChild(const COpcString& cName);

    // GetChild
    COpcXmlElement GetChild(UINT uIndex);
    
    // GetChildren
    UINT GetChildren(COpcXmlElementList& cElements);

    // AddChild
    COpcXmlElement AddChild(const COpcString& cName);
	COpcXmlElement AddChild(const OpcXml::QName& cName);

    // AppendChild
    COpcXmlElement AppendChild(const COpcString& cName);
    COpcXmlElement AppendChild(const OpcXml::QName& cName);
   
    // AppendChild
	bool AppendChild(IXMLDOMElement* ipElement);

	// AppendText
	void AppendText(const COpcString& cText);
	
	// ResolvePrefix
	COpcString ResolvePrefix(const COpcString& cPrefix);

	// ResolveNamespace
	COpcString ResolveNamespace(const COpcString& cNamespace);

private:

    //==========================================================================
    // Private Methods

	// GetNamespace
	COpcString GetNamespace(IXMLDOMElement* ipElement);
	
	// ResolvePrefix
	COpcString ResolvePrefix(const COpcString& cPrefix, IXMLDOMNode* ipNode);
	
	// ResolveNamespace
	COpcString ResolveNamespace(const COpcString& cNamespace, IXMLDOMNode* ipNode);

    //==========================================================================
    // Private Members

    IXMLDOMElement* m_ipElement;
};

//==============================================================================
// INTERFACE: IOpcXmlSerialize
// PURPOSE    Defines standard XML serialization methods for an object.

interface IOpcXmlSerialize 
{
public:

    // Init
    virtual void Init() = 0;

    // Clear
    virtual void Clear() = 0;

    // Read
    virtual bool Read(COpcXmlElement& cElement) = 0;

    // Write
    virtual bool Write(COpcXmlElement& cElement) = 0;
};

//==============================================================================
// MACRO:   READ_XXX/WRITE_XXX
// PURPOSE  Facilitiate implementation of XML serialization code,

#define READ_ATTRIBUTE(xName, xValue) OpcXml::ReadXml(cElement.GetAttribute(xName), xValue);

#define READ_DEFAULT_ATTRIBUTE(xName, xValue, xDefault) \
if (!OpcXml::ReadXml(cElement.GetAttribute(xName), xValue)) \
{ \
	xValue = xDefault; \
}

#define READ_OPTIONAL_ATTRIBUTE(xName, xValue) \
if (!OpcXml::ReadXml(cElement.GetAttribute(xName), xValue)) \
{ \
	OpcXml::Init(xValue); \
	xValue##Specified = false; \
} \
else \
{ \
	xValue##Specified = true; \
}

#define WRITE_ATTRIBUTE(xName, xValue) {COpcString cText; if (OpcXml::Write(xValue, cText)) {cElement.SetAttribute(xName, cText);}}

#define READ_ELEMENT(xName, xValue) OpcXml::ReadXml(cElement.GetChild(xName), xValue);
#define WRITE_ELEMENT(xName, xValue) OpcXml::WriteXml(cElement.AddChild(xName), xValue);

#define READ_ATTRIBUTE_EX(xName, xValue, xAction) if (!OpcXml::ReadXml(cElement.GetAttribute(xName), xValue)) {	xAction; }
#define WRITE_ATTRIBUTE_EX(xName, xValue, xAction) { COpcString cText; if (!OpcXml::Write(xValue, cText)) {xAction;} if (cElement.SetAttribute(xName, cText) == NULL) {xAction;}}

#define READ_ELEMENT_EX(xName, xValue, xAction) if (!OpcXml::ReadXml(cElement.GetChild(xName), xValue)) {xAction;}
#define WRITE_ELEMENT_EX(xName, xValue, xAction) if (!OpcXml::WriteXml(cElement.AddChild(xName), xValue)) {xAction;}

#endif // _COpcXmlElement_H_
