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

#ifndef _COpcXmlAttribute_H_
#define _COpcXmlAttribute_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "COpcString.h"
#include "COpcArray.h"
#include "OpcXmlType.h"

//==============================================================================
// CLASS:   COpcXmlAttribute
// PURPOSE  Represents an XML attribute.

class OPCUTILS_API COpcXmlAttribute 
{
    OPC_CLASS_NEW_DELETE_ARRAY();

public:

    //==========================================================================
    // Public Operators

    // Constructor
    COpcXmlAttribute(IUnknown* ipUnknown = NULL);

    // Copy Constructor
    COpcXmlAttribute(const COpcXmlAttribute& cAttribute);
            
    // Destructor
    ~COpcXmlAttribute();

    // Assignment
    COpcXmlAttribute& operator=(IUnknown* ipUnknown);
    COpcXmlAttribute& operator=(const COpcXmlAttribute& cAttribute);

    // Accessor
    operator IXMLDOMAttribute*() const { return m_ipAttribute; }

    //==========================================================================
    // Public Methods
    
    // GetName
    COpcString GetName();
    	
	// Prefix
    COpcString GetPrefix();   
        
	// Namespace
	COpcString GetNamespace();

	// GetQualifiedName
	OpcXml::QName GetQualifiedName();

    // GetValue
    COpcString GetValue();
   
protected:

    //==========================================================================
    // Private Members

    IXMLDOMAttribute* m_ipAttribute;
};

//==============================================================================
// TYPE:    COpcXmlAttributeList
// PURPOSE: A list of elements.

typedef COpcArray<COpcXmlAttribute> COpcXmlAttributeList;

#endif // _COpcXmlAttribute_H_
