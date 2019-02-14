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
#include "COpcXmlElement.h"
#include "COpcVariant.h"

//==============================================================================
// Local Declarations

#define TAG_TYPE       _T("xsi:type")
#define TAG_ARRAY_TYPE _T("ArrayOf")

//==============================================================================
// COpcXmlElement

// Constructor
COpcXmlElement::COpcXmlElement(IUnknown* ipUnknown)
{
    m_ipElement = NULL;
    *this = ipUnknown;
}

// Copy Constructor
COpcXmlElement::COpcXmlElement(const COpcXmlElement& cElement)
{
    m_ipElement = NULL;
    *this = cElement.m_ipElement;
}

// Destructor
COpcXmlElement::~COpcXmlElement()
{
    if (m_ipElement != NULL)
    {
        m_ipElement->Release();
        m_ipElement = NULL;
    }
}

// Assignment
COpcXmlElement& COpcXmlElement::operator=(IUnknown* ipUnknown)
{
    if (m_ipElement != NULL)
    {
        m_ipElement->Release();
        m_ipElement = NULL;
    }

    if (ipUnknown != NULL)
    {
        HRESULT hResult = ipUnknown->QueryInterface(__uuidof(IXMLDOMElement), (void**)&m_ipElement);

        if (FAILED(hResult))
        {
            m_ipElement = NULL;
        }
    }

    return *this;
}

// Assignment
COpcXmlElement& COpcXmlElement::operator=(const COpcXmlElement& cElement)
{
    if (this == &cElement)
    {
        return *this;
    }

    *this = cElement.m_ipElement;
    return *this;
}

// GetName
COpcString COpcXmlElement::GetName()
{
	if (m_ipElement != NULL)
	{
		BSTR bstrName = NULL;

		HRESULT hResult = m_ipElement->get_nodeName(&bstrName);
		OPC_ASSERT(SUCCEEDED(hResult));

		COpcString cName = bstrName;
		SysFreeString(bstrName);

		return cName;
	}

	return (LPCWSTR)NULL;
}

// GetPrefix
COpcString COpcXmlElement::GetPrefix()
{
	if (m_ipElement != NULL)
	{
		BSTR bstrName = NULL;

		HRESULT hResult = m_ipElement->get_prefix(&bstrName);
		OPC_ASSERT(SUCCEEDED(hResult));

		COpcString cName = bstrName;
		SysFreeString(bstrName);

		return cName;
	}

	return (LPCWSTR)NULL;
}

// GetQualifiedName
OpcXml::QName COpcXmlElement::GetQualifiedName()
{
	OpcXml::QName cQName;

	if (m_ipElement != NULL)
	{
		BSTR bstrName = NULL;

		HRESULT hResult = m_ipElement->get_baseName(&bstrName);
		OPC_ASSERT(SUCCEEDED(hResult));

		cQName.SetName(bstrName);
		cQName.SetNamespace(GetNamespace());
		
		SysFreeString(bstrName);
	}

	return cQName;
}

// GetValue
COpcString COpcXmlElement::GetValue()
{
    BSTR bstrValue = NULL;

    HRESULT hResult = m_ipElement->get_text(&bstrValue);
    OPC_ASSERT(SUCCEEDED(hResult));

    COpcString cValue = bstrValue;
    SysFreeString(bstrValue);

    return cValue;
}

// SetValue
void COpcXmlElement::SetValue(const COpcString& cValue)
{
    BSTR bstrValue = SysAllocString((LPCWSTR)cValue);

    HRESULT hResult = m_ipElement->put_text(bstrValue);
    OPC_ASSERT(SUCCEEDED(hResult));

    SysFreeString(bstrValue);
}

// GetAttribute
COpcXmlAttribute COpcXmlElement::GetAttribute(const COpcString& cName, const COpcString& cNamespace)
{
	COpcString cFullName = ResolveNamespace(cNamespace, m_ipElement);

	if (!cFullName.IsEmpty())
	{
		cFullName += _T(":");
	}

	cFullName += cName;

	return GetAttribute(cFullName);
}

// GetAttribute
COpcXmlAttribute COpcXmlElement::GetAttribute(const COpcString& cName)
{
    COpcXmlAttribute cAttribute;

    HRESULT hResult = S_OK;

    IXMLDOMNamedNodeMap* ipMap  = NULL;
    IXMLDOMNode*         ipNode = NULL;

    BSTR bstrName = SysAllocString((LPCWSTR)cName);

    TRY
    {
        if (m_ipElement == NULL)
        {
            THROW_(hResult, E_POINTER);
        }

        hResult = m_ipElement->get_attributes(&ipMap);

        if (FAILED(hResult))
        {
            THROW();
        }

        hResult = ipMap->getNamedItem(bstrName, &ipNode);

        if (FAILED(hResult))
        {
            THROW();
        }

        if (hResult == S_OK)
        {
            cAttribute = ipNode;
        }
    }
    CATCH_FINALLY
    {
        if (ipMap != NULL)    ipMap->Release();
        if (ipNode != NULL)   ipNode->Release();
        
		SysFreeString(bstrName);
    }

    return cAttribute;
}

// GetAttributes
UINT COpcXmlElement::GetAttributes(COpcStringMap& cAttributes)
{
    HRESULT hResult = S_OK;

    IXMLDOMNamedNodeMap* ipMap  = NULL;

    TRY
    {
		cAttributes.RemoveAll();

        if (m_ipElement == NULL)
        {
            THROW_(hResult, E_POINTER);
        }

        hResult = m_ipElement->get_attributes(&ipMap);

        if (FAILED(hResult))
        {
            THROW();
        }

		LONG lLength = 0;
		hResult = ipMap->get_length(&lLength);

		for (LONG ii = 0; ii < lLength; ii++)
		{
			IXMLDOMNode* ipEntry = NULL;
			ipMap->get_item(ii, &ipEntry);

			if (ipEntry != NULL)
			{
				IXMLDOMAttribute* ipAttribute = NULL;
				ipEntry->QueryInterface(__uuidof(IXMLDOMAttribute), (void**)&ipAttribute);

				if (ipAttribute != NULL)
				{
					// get the name.
					BSTR bsName = NULL;
					ipAttribute->get_name(&bsName);

					// get the value.
					VARIANT cVariant; OpcVariantInit(&cVariant);
				    if (FAILED(ipAttribute->get_value(&cVariant))) { OPC_ASSERT(false); }

					// add to table.
					cAttributes[(COpcString)bsName] = (COpcString)cVariant.bstrVal;

					// free memory.
					OpcVariantClear(&cVariant);
					SysFreeString(bsName);

					// release object.
					ipAttribute->Release();
				}
			}
		}       
    }
    CATCH_FINALLY
    {
        if (ipMap != NULL) ipMap->Release();
    }

    return cAttributes.GetCount();
}

// GetAttributes
UINT COpcXmlElement::GetAttributes(COpcXmlAttributeList& cAttributes)
{
    HRESULT hResult = S_OK;

    IXMLDOMNamedNodeMap* ipMap  = NULL;

    TRY
    {
		cAttributes.RemoveAll();

        if (m_ipElement == NULL)
        {
            THROW_(hResult, E_POINTER);
        }

        hResult = m_ipElement->get_attributes(&ipMap);

        if (FAILED(hResult))
        {
            THROW();
        }

		LONG lLength = 0;
		hResult = ipMap->get_length(&lLength);

		for (LONG ii = 0; ii < lLength; ii++)
		{
			IXMLDOMNode* ipEntry = NULL;
			ipMap->get_item(ii, &ipEntry);

			if (ipEntry != NULL)
			{
				IXMLDOMAttribute* ipAttribute = NULL;
				ipEntry->QueryInterface(__uuidof(IXMLDOMAttribute), (void**)&ipAttribute);

				if (ipAttribute != NULL)
				{
					cAttributes.Append((COpcXmlAttribute)(ipAttribute));
					ipAttribute->Release();
				}
			}
		}       
    }
    CATCH_FINALLY
    {
        if (ipMap != NULL) ipMap->Release();
    }

    return cAttributes.GetSize();
}

// SetAttribute
COpcXmlAttribute COpcXmlElement::SetAttribute(const COpcString& cName, const COpcString& cValue)
{
	 COpcXmlAttribute cAttribute;

    HRESULT hResult = S_OK;

    IXMLDOMDocument*     ipDocument  = NULL;
    IXMLDOMNamedNodeMap* ipMap       = NULL;
    IXMLDOMAttribute*    ipAttribute = NULL;
    IXMLDOMNode*         ipNode      = NULL;

    BSTR bstrName = SysAllocString((LPCWSTR)cName); 

    TRY
    {
        if (m_ipElement == NULL)
        {
            THROW_(hResult, E_POINTER);
        }

        hResult = m_ipElement->get_attributes(&ipMap);

        if (FAILED(hResult))
        {
            THROW();
        }

        hResult = m_ipElement->get_ownerDocument(&ipDocument);

        if (FAILED(hResult))
        {
            THROW();
        }
		
		/*
		VARIANT vNodeType;
		vNodeType.vt   = VT_I4;
		vNodeType.lVal = NODE_ATTRIBUTE;

        hResult = ipDocument->createNode(vNodeType, bstrName, bstrNamespace, (IXMLDOMNode**)&ipAttribute);
		*/

		hResult = ipDocument->createAttribute(bstrName, &ipAttribute);

        if (FAILED(hResult))
        {
            THROW();
        }

        COpcVariant cVariant(cValue);

        hResult = ipAttribute->put_value(cVariant);

        if (FAILED(hResult))
        {
            THROW();
        }

        hResult = ipMap->setNamedItem(ipAttribute, &ipNode);

        if (FAILED(hResult))
        {
            THROW();
        }

        cAttribute = ipAttribute;
    }
    CATCH_FINALLY
    {
        if (ipDocument != NULL)  ipDocument->Release();
        if (ipMap != NULL)       ipMap->Release();
        if (ipAttribute != NULL) ipAttribute->Release();
        if (ipNode != NULL)      ipNode->Release();
        
		SysFreeString(bstrName);
    }

    return cAttribute;
}

// GetChildren
UINT COpcXmlElement::GetChildren(COpcXmlElementList& cChildren)
{
    HRESULT hResult = S_OK;

    IXMLDOMNodeList* ipList = NULL;
    IXMLDOMNode*     ipNode = NULL;

    TRY
    {
        if (m_ipElement == NULL)
        {
            THROW_(hResult, E_POINTER);
        }

        hResult = m_ipElement->get_childNodes(&ipList);

        if (FAILED(hResult))
        {
            THROW();
        }

        long lLength = 0;

        hResult = ipList->get_length(&lLength);

        if (FAILED(hResult))
        {
            THROW();
        }

        for (long ii = 0; ii < lLength; ii++)
        {
            hResult = ipList->get_item(ii, &ipNode);

            if (FAILED(hResult))
            {
                THROW();
            }
            
            if (ipNode != NULL)
            {				
				COpcXmlElement cChild(ipNode);

                ipNode->Release();
                ipNode = NULL;

				if (cChild != NULL)
				{
					cChildren.Append(cChild);
				}
            }
        }
    }
    CATCH
    {
        cChildren.SetSize(0);
    }
    FINALLY
    {
        if (ipList != NULL) ipList->Release();
    }

    return cChildren.GetSize();
}

// GetChild
COpcXmlElement COpcXmlElement::GetChild(const COpcString& cName)
{
    COpcXmlElement cElement;

    HRESULT hResult = S_OK;

    IXMLDOMNodeList* ipList = NULL;
    IXMLDOMNode*     ipNode = NULL;

    TRY
    {
        if (m_ipElement == NULL)
        {
            THROW_(hResult, E_POINTER);
        }

        hResult = m_ipElement->get_childNodes(&ipList);

        if (FAILED(hResult))
        {
            THROW();
        }

        long lLength = 0;

        hResult = ipList->get_length(&lLength);

        if (FAILED(hResult))
        {
            THROW();
        }

        for (long ii = 0; ii < lLength; ii++)
        {
            hResult = ipList->get_item(ii, &ipNode);

            if (FAILED(hResult))
            {
                THROW();
            }
            
            if (ipNode != NULL)
            {
                cElement = ipNode;
                
                ipNode->Release();
                ipNode = NULL;

                if (cElement.GetName() == cName)
                {
                    break;
                }
            }

            cElement = NULL;
        }
    }
    CATCH_FINALLY
    {
        if (ipList != NULL) ipList->Release();
        if (ipNode != NULL) ipNode->Release();
    }

    return cElement;
}

// GetChild
COpcXmlElement COpcXmlElement::GetChild(UINT uIndex)
{
    COpcXmlElement cElement;

    HRESULT hResult = S_OK;

    IXMLDOMNodeList* ipList = NULL;
    IXMLDOMNode*     ipNode = NULL;

    TRY
    {
        if (m_ipElement == NULL)
        {
            THROW_(hResult, E_POINTER);
        }

        hResult = m_ipElement->get_childNodes(&ipList);

        if (FAILED(hResult))
        {
            THROW();
        }

        long lLength = 0;

        hResult = ipList->get_length(&lLength);

        if (FAILED(hResult))
        {
            THROW();
        }

        if (lLength <= (long)uIndex)
        {
            THROW_(hResult, E_FAIL);
        }

        hResult = ipList->get_item((long)uIndex, &ipNode);

        if (FAILED(hResult))
        {
            THROW();
        }

        cElement = ipNode;
    }
    CATCH_FINALLY
    {
        if (ipList != NULL) ipList->Release();
        if (ipNode != NULL) ipNode->Release();
    }

    return cElement;
}

// GetNamespace
COpcString COpcXmlElement::GetNamespace(IXMLDOMElement* ipElement)
{
	COpcXmlElement cElement;

	BSTR bstrNamespace = NULL;
    IXMLDOMElement* ipParent = NULL;

	COpcString cNamespace;

	TRY
    {
        if (ipElement == NULL)
        {
            THROW();
        }

		HRESULT hResult = ipElement->get_namespaceURI(&bstrNamespace);

        if (FAILED(hResult))
        {
            THROW();
        }

		cNamespace = bstrNamespace;

		if (cNamespace.IsEmpty())
		{
			hResult = ipElement->get_parentNode((IXMLDOMNode**)&ipParent);

			if (FAILED(hResult))
			{
				THROW();
			}

			cNamespace = GetNamespace(ipParent);
		}
    }
    CATCH_FINALLY
    {
		SysFreeString(bstrNamespace);
        if (ipParent != NULL) ipParent->Release();
    }

    return cNamespace;
}

// GetNamespace
COpcString COpcXmlElement::GetNamespace()
{
	return GetNamespace(m_ipElement);
}

// AddChild
COpcXmlElement COpcXmlElement::AddChild(const COpcString& cName)
{
	OpcXml::QName cQName(cName, ResolvePrefix(""));
	return AddChild(cQName);
}

// AddChild
COpcXmlElement COpcXmlElement::AddChild(const OpcXml::QName& cName)
{
    COpcXmlElement cElement;

    HRESULT hResult = S_OK;

    IXMLDOMDocument* ipDocument = NULL;
    IXMLDOMNode*     ipOldChild = NULL;
    IXMLDOMElement*  ipNewChild = NULL;
    IXMLDOMNode*     ipResult   = NULL;

	COpcString cFullName = ResolveNamespace(cName.GetNamespace());

	if (!cFullName.IsEmpty())
	{
		cFullName += _T(":");
	}

	cFullName += cName.GetName();

    BSTR bstrName = SysAllocString((LPCWSTR)cFullName);
    BSTR bstrNamespace = SysAllocString((LPCWSTR)cName.GetNamespace()); 

    TRY
    {
        if (m_ipElement == NULL)
        {
            THROW_(hResult, E_POINTER);
        }

        hResult = m_ipElement->get_ownerDocument(&ipDocument);

        if (FAILED(hResult))
        {
            THROW();
        }
		
		VARIANT vNodeType;
		vNodeType.vt   = VT_I4;
		vNodeType.lVal = NODE_ELEMENT;

        hResult = ipDocument->createNode(vNodeType, bstrName, bstrNamespace, (IXMLDOMNode**)&ipNewChild);

        if (FAILED(hResult))
        {
            THROW();
        }
        
        COpcXmlElement cOldChild = GetChild(cFullName);

        if (cOldChild == NULL)
        {
            hResult = m_ipElement->appendChild(ipNewChild, &ipResult);

            if (FAILED(hResult))
            {
                THROW();
            }
        }

        else
        {
            hResult = ((IXMLDOMElement*)cOldChild)->QueryInterface(__uuidof(IXMLDOMNode), (void**)&ipOldChild);

            if (FAILED(hResult))
            {
                THROW();
            }

            hResult = m_ipElement->replaceChild(ipNewChild, ipOldChild, &ipResult);

            if (FAILED(hResult))
            {
                THROW();
            }
        }

        cElement = ipNewChild;
    }
    CATCH_FINALLY
    {
        if (ipDocument != NULL) ipDocument->Release();
        if (ipOldChild != NULL) ipOldChild->Release();
        if (ipNewChild != NULL) ipNewChild->Release();
        if (ipResult != NULL)   ipResult->Release();

        SysFreeString(bstrName);
        SysFreeString(bstrNamespace);
    }

    return cElement;
}

// AppendChild
COpcXmlElement COpcXmlElement::AppendChild(const COpcString& cName)
{
	OpcXml::QName cQName(cName, ResolvePrefix(""));
	return AppendChild(cQName);
}

// AppendChild
COpcXmlElement COpcXmlElement::AppendChild(const OpcXml::QName& cName)
{
    COpcXmlElement cElement;

    HRESULT hResult = S_OK;

    IXMLDOMDocument* ipDocument = NULL;
    IXMLDOMElement*  ipNewChild = NULL;
    IXMLDOMNode*     ipResult   = NULL;

	COpcString cFullName = ResolveNamespace(cName.GetNamespace());

	if (!cFullName.IsEmpty())
	{
		cFullName += _T(":");
	}

	cFullName += cName.GetName();

    BSTR bstrName = SysAllocString((LPCWSTR)cFullName); 
    BSTR bstrNamespace = SysAllocString((LPCWSTR)cName.GetNamespace()); 

    TRY
    {
        if (m_ipElement == NULL)
        {
            THROW_(hResult, E_POINTER);
        }

        hResult = m_ipElement->get_ownerDocument(&ipDocument);

        if (FAILED(hResult))
        {
            THROW();
        }
		
		VARIANT vNodeType;
		vNodeType.vt   = VT_I4;
		vNodeType.lVal = NODE_ELEMENT;

        hResult = ipDocument->createNode(vNodeType, bstrName, bstrNamespace, (IXMLDOMNode**)&ipNewChild);

        if (FAILED(hResult))
        {
            THROW();
        }

        hResult = m_ipElement->appendChild(ipNewChild, &ipResult);

        if (FAILED(hResult))
        {
            THROW();
        }

        cElement = ipNewChild;
    }
    CATCH_FINALLY
    {
        if (ipDocument != NULL) ipDocument->Release();
        if (ipNewChild != NULL) ipNewChild->Release();
        if (ipResult != NULL)   ipResult->Release();

        SysFreeString(bstrName); 
        SysFreeString(bstrNamespace); 
    }

    return cElement;
}     

// AppendChild
bool COpcXmlElement::AppendChild(IXMLDOMElement* ipElement)
{
    HRESULT hResult = S_OK;

    IXMLDOMElement* ipClone  = NULL;
    IXMLDOMNode*    ipParent = NULL;
    IXMLDOMNode*    ipResult = NULL;

    TRY
    {
        if (m_ipElement == NULL)
        {
            THROW_(hResult, E_POINTER);
        }

        // clone the element.
		hResult = ipElement->cloneNode(VARIANT_TRUE, (IXMLDOMNode**)&ipClone);
        
		if (FAILED(hResult))
        {
            THROW();
        }

		// remove clone from parent.
		hResult = ipClone->get_parentNode(&ipParent);
        
		if (FAILED(hResult))
        {
            THROW();
        }

		if (ipParent != NULL)
		{
			hResult = ipParent->removeChild(ipClone, &ipResult);

			if (FAILED(hResult))
			{
				THROW();
			}

			if (ipResult != NULL)
			{
				ipResult->Release();
				ipResult = NULL;
			}
		}

        // add root element to document.
        hResult = m_ipElement->appendChild(ipClone, &ipResult);

        if (FAILED(hResult))
        {
            THROW();
        }

        if (ipResult != NULL)
        {
            ipResult->Release();
            ipResult = NULL;
        }
    }
    CATCH_FINALLY
    {
        // release memory.
        if (ipClone != NULL)  ipClone->Release();
        if (ipParent != NULL) ipParent->Release();
        if (ipResult != NULL) ipResult->Release();
    }

    return SUCCEEDED(hResult);
}


// AppendText
void COpcXmlElement::AppendText(const COpcString& cText)
{
    HRESULT hResult = S_OK;

    IXMLDOMDocument* ipDocument = NULL;
    IXMLDOMText*     ipNewChild = NULL;
    IXMLDOMNode*     ipResult   = NULL;

    BSTR bstrText = SysAllocString((LPCWSTR)cText);

    TRY
    {
        if (m_ipElement == NULL)
        {
            THROW_(hResult, E_POINTER);
        }

        hResult = m_ipElement->get_ownerDocument(&ipDocument);

        if (FAILED(hResult))
        {
            THROW();
        }

        hResult = ipDocument->createTextNode(bstrText, &ipNewChild);

        if (FAILED(hResult))
        {
            THROW();
        }

        hResult = m_ipElement->appendChild(ipNewChild, &ipResult);

        if (FAILED(hResult))
        {
            THROW();
        }
    }
    CATCH_FINALLY
    {
        if (ipDocument != NULL) ipDocument->Release();
        if (ipNewChild != NULL) ipNewChild->Release();
        if (ipResult != NULL)   ipResult->Release();

        SysFreeString(bstrText);
    }
}     

/*
// GetType
bool COpcXmlElement::GetType(OpcXml::Type& eType)
{
    eType = OpcXml::XML_EMPTY;

    if (m_ipElement == NULL) 
    {
        return false;
    }

    COpcXmlAttribute cAttribute = GetAttribute(TAG_TYPE);

    if (cAttribute == NULL)
    {
        return false;
    }

    return OpcXml::Read(cAttribute.GetValue(), eType);
}

// SetType
bool COpcXmlElement::SetType(OpcXml::Type eType)
{
    if (m_ipElement == NULL || eType == OpcXml::XML_EMPTY) 
    {
        return false;
    }

    COpcString cText;

    if (!OpcXml::Write(eType, cText))
    {
        return false;
    }

    COpcXmlAttribute cAttribute = SetAttribute(TAG_TYPE, cText);

    if (cAttribute == NULL)
    {
        return false;
    }

    return true;
}

// GetArrayType
bool COpcXmlElement::GetArrayType(OpcXml::Type& eType)
{
    eType = OpcXml::XML_EMPTY;

    if (m_ipElement == NULL) 
    {
        return false;
    }

    COpcXmlAttribute cAttribute = GetAttribute(TAG_TYPE);

    if (cAttribute == NULL)
    {
        return false;
    }

    COpcString cText = cAttribute.GetValue();

    UINT uIndex = cText.Find(TAG_ARRAY_TYPE);

    if (uIndex != 0)
    {
        return false;
    }

    // remove prefix and set first letter to lower case.
	cText = cText.SubStr(_tcslen(TAG_ARRAY_TYPE)).ToLower(0);

    return OpcXml::Read(cText, eType);
}

// SetArrayType
bool COpcXmlElement::SetArrayType(OpcXml::Type eType)
{
    if (m_ipElement == NULL || eType == OpcXml::XML_EMPTY) 
    {
        return false;
    }

    COpcString cText;

    if (!OpcXml::Write(eType, cText))
    {
        return false;
    }

	// remove the xsd prefix.
	int iIndex = cText.Find(_T("xsd:"));

	if (iIndex == 0)
	{
		cText = cText.SubStr(4);
	}

    // set first letter to upper case and prepend prefix.
    cText = cText.ToUpper(0);
    cText = TAG_ARRAY_TYPE + cText;

    COpcXmlAttribute cAttribute = SetAttribute(TAG_TYPE, cText);

    if (cAttribute == NULL)
    {
        return false;
    }

    return true;
}
*/

// ResolvePrefix
COpcString COpcXmlElement::ResolvePrefix(const COpcString& cPrefix)
{
	return ResolvePrefix(cPrefix, m_ipElement);
}

// ResolvePrefix
COpcString COpcXmlElement::ResolvePrefix(const COpcString& cPrefix, IXMLDOMNode* ipNode)
{
	if (ipNode == NULL)
	{
		return (LPCWSTR)NULL;
	}

	COpcXmlElement cElement(ipNode);

	// compare the search prefix to the prefix for the current element.
	if (cElement.GetPrefix() == cPrefix)
	{
		return cElement.GetNamespace();
	}

	COpcXmlAttributeList cAttributes;

	if (cElement.GetAttributes(cAttributes) > 0)
	{
		for (UINT ii = 0; ii < cAttributes.GetSize(); ii++)
		{
			if (cAttributes[ii].GetPrefix() == OPCXML_NAMESPACE_ATTRIBUTE)
			{
				if (cAttributes[ii].GetQualifiedName().GetName() == cPrefix)
				{
					return cAttributes[ii].GetValue();
				}
			}
		}
	}

	IXMLDOMNode* ipParent = NULL;

	HRESULT hResult = ipNode->get_parentNode(&ipParent);

	if (FAILED(hResult))
	{
		return (LPCWSTR)NULL;
	}

	DOMNodeType nodeType = NODE_ELEMENT;
	
	if (ipParent != NULL)
	{
		hResult = ipParent->get_nodeType(&nodeType);

		if (FAILED(hResult))
		{
			return (LPCWSTR)NULL;
		}
	}

	// search in parent element for namespace.
	if (ipParent != NULL && nodeType == NODE_ELEMENT)
	{	
		COpcString cNamespace = ResolvePrefix(cPrefix, ipParent);
		ipParent->Release();
		return cNamespace;
	}
			
	return (LPCWSTR)NULL;
}

// ResolveNamespace
COpcString COpcXmlElement::ResolveNamespace(const COpcString& cNamespace)
{
	return ResolveNamespace(cNamespace, m_ipElement);
}

// ResolveNamespace
COpcString COpcXmlElement::ResolveNamespace(const COpcString& cNamespace, IXMLDOMNode* ipNode)
{
	if (ipNode == NULL)
	{
		return (LPCWSTR)NULL;
	}

	COpcXmlElement cElement(ipNode);

	// check element namespace.
	if (cElement.GetNamespace() == cNamespace)
	{
		return cElement.GetPrefix();
	}

	// check for additional namespace declarations on the element.
	COpcXmlAttributeList cAttributes;

	if (cElement.GetAttributes(cAttributes) > 0)
	{
		for (UINT ii = 0; ii < cAttributes.GetSize(); ii++)
		{
			if (cAttributes[ii].GetPrefix() == OPCXML_NAMESPACE_ATTRIBUTE)
			{
				if (cAttributes[ii].GetValue() == cNamespace)
				{
					return cAttributes[ii].GetQualifiedName().GetName();
				}
			}
		}
	}

	IXMLDOMNode* ipParent = NULL;

	HRESULT hResult = ipNode->get_parentNode(&ipParent);

	if (FAILED(hResult))
	{
		return (LPCWSTR)NULL;
	}

	DOMNodeType nodeType = NODE_ELEMENT;
	
	if (ipParent != NULL)
	{
		hResult = ipParent->get_nodeType(&nodeType);

		if (FAILED(hResult))
		{
			return (LPCWSTR)NULL;
		}
	}

	// search in parent element for namespace.
	if (ipParent != NULL && nodeType == NODE_ELEMENT)
	{	
		COpcString cPrefix = ResolveNamespace(cNamespace, ipParent);
		ipParent->Release();
		return cPrefix;
	}

	// find unused prefix.
	COpcString cPrefix;
	
	for (int ii = 0; cPrefix.IsEmpty(); ii++)
	{
		TCHAR tsBuf[16];
		_stprintf(tsBuf, _T("s%d"), ii); 
		cPrefix = tsBuf;
			
		if (!ResolvePrefix(cPrefix, ipNode).IsEmpty())
		{
			cPrefix.Empty();
		}
	}

	// add namespace to current element.
	COpcString cAttributeName;

	cAttributeName += OPCXML_NAMESPACE_ATTRIBUTE;
	cAttributeName += _T(":");
	cAttributeName += cPrefix;

	cElement.SetAttribute(cAttributeName, cNamespace);

	return cPrefix;
}

// GetType
bool COpcXmlElement::GetType(OpcXml::QName& cType)
{	
	// construct the name of the XML schema type attribute.
	COpcString cFullName = ResolveNamespace(OPCXML_NS_SCHEMA_INSTANCE);

	if (!cFullName.IsEmpty())
	{
		cFullName += _T(":");
	}

	cFullName += OPCXML_TYPE_ATTRIBUTE;

	// get the type attribute.
	COpcXmlAttribute cAttribute = GetAttribute(cFullName);

	if (cAttribute == NULL)
	{
		return false;
	}
	
	// parse attribute value to find the namespace of the type.
	COpcString cPrefix;
	COpcString cTypeName = cAttribute.GetValue();

	if (cTypeName.IsEmpty())
	{
		return false;
	}
	
	int iIndex = cTypeName.Find(_T(":"));

	if (iIndex != -1)
	{
		cPrefix   = cTypeName.SubStr(0, iIndex);
		cTypeName = cTypeName.SubStr(iIndex+1);
	}

	// set the type name and namespace.
	cType.SetName(cTypeName);
	cType.SetNamespace(ResolvePrefix(cPrefix));

	return true;
}

// SetType
void COpcXmlElement::SetType(OpcXml::QName cType)
{
	// check for trivial case.
	if (cType.GetName().IsEmpty())
	{
		return;
	}

	// construct the name of the XML schema type attribute.
	COpcString cFullName = ResolveNamespace(OPCXML_NS_SCHEMA_INSTANCE);

	if (!cFullName.IsEmpty())
	{
		cFullName += _T(":");
	}

	cFullName += OPCXML_TYPE_ATTRIBUTE;

	// construct the name of the type.
	COpcString cTypeName = ResolveNamespace(cType.GetNamespace());

	if (!cTypeName.IsEmpty())
	{
		cTypeName += _T(":");
	}

	cTypeName += cType.GetName();

	// update the type attribute.
	SetAttribute(cFullName, cTypeName);
}
