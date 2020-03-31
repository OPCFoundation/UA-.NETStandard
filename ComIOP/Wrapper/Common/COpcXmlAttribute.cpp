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

#include "StdAfx.h"
#include "COpcXmlAttribute.h"
#include "COpcVariant.h"

//==============================================================================
// COpcXmlAttribute

// Constructor
COpcXmlAttribute::COpcXmlAttribute(IUnknown* ipUnknown)
{
    m_ipAttribute = NULL;
    *this = ipUnknown;
}

// Copy Constructor
COpcXmlAttribute::COpcXmlAttribute(const COpcXmlAttribute& cAttribute)
{
    m_ipAttribute = NULL;
    *this = cAttribute.m_ipAttribute;
}

// Destructor
COpcXmlAttribute::~COpcXmlAttribute()
{
    if (m_ipAttribute != NULL)
    {
        m_ipAttribute->Release();
        m_ipAttribute = NULL;
    }
}

// Assignment
COpcXmlAttribute& COpcXmlAttribute::operator=(IUnknown* ipUnknown)
{
    if (m_ipAttribute != NULL)
    {
        m_ipAttribute->Release();
        m_ipAttribute = NULL;
    }

    if (ipUnknown != NULL)
    {
        HRESULT hResult = ipUnknown->QueryInterface(__uuidof(IXMLDOMAttribute), (void**)&m_ipAttribute);

        if (FAILED(hResult))
        {
            m_ipAttribute = NULL;
        }
    }

    return *this;
}

// Assignment
COpcXmlAttribute& COpcXmlAttribute::operator=(const COpcXmlAttribute& cAttribute)
{
    if (this == &cAttribute)
    {
        return *this;
    }

    *this = cAttribute.m_ipAttribute;
    return *this;
}

// GetName
COpcString COpcXmlAttribute::GetName()
{
	if (m_ipAttribute != NULL)
	{
		BSTR bstrName = NULL;

		HRESULT hResult = m_ipAttribute->get_name(&bstrName);
		OPC_ASSERT(SUCCEEDED(hResult));

		COpcString cName = bstrName;
		SysFreeString(bstrName);

		return cName;
	}

	return (LPCWSTR)NULL;
}

// GetPrefix
COpcString COpcXmlAttribute::GetPrefix()
{
	if (m_ipAttribute != NULL)
	{
		BSTR bstrName = NULL;

		HRESULT hResult = m_ipAttribute->get_prefix(&bstrName);
		OPC_ASSERT(SUCCEEDED(hResult));

		COpcString cName = bstrName;
		SysFreeString(bstrName);

		return cName;
	}

	return (LPCWSTR)NULL;
}

// GetQualifiedName
OpcXml::QName COpcXmlAttribute::GetQualifiedName()
{
	OpcXml::QName cQName;

	if (m_ipAttribute != NULL)
	{
		BSTR bstrName = NULL;

		HRESULT hResult = m_ipAttribute->get_baseName(&bstrName);
		OPC_ASSERT(SUCCEEDED(hResult));

		cQName.SetName(bstrName);
		cQName.SetNamespace(GetNamespace());
		
		SysFreeString(bstrName);
	}

	return cQName;
}

// GetNamespace
COpcString COpcXmlAttribute::GetNamespace()
{
	if (m_ipAttribute != NULL)
	{
		BSTR bstrName = NULL;

		HRESULT hResult = m_ipAttribute->get_namespaceURI(&bstrName);
		OPC_ASSERT(SUCCEEDED(hResult));

		COpcString cName = bstrName;
		SysFreeString(bstrName);

		return cName;
	}

	return (LPCWSTR)NULL;
}

// GetValue
COpcString COpcXmlAttribute::GetValue()
{
    VARIANT cVariant; OpcVariantInit(&cVariant);

    if (FAILED(m_ipAttribute->get_value(&cVariant))) { OPC_ASSERT(false); }

    COpcString cValue = cVariant.bstrVal;
    OpcVariantClear(&cVariant);

    return cValue;
}
