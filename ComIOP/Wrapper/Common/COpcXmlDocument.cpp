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
#include "COpcXmlDocument.h"
#include "COpcXmlElement.h"
#include "COpcXmlAttribute.h"
#include "COpcVariant.h"

//==============================================================================
// Local Declarations

#define TAG_DOCTYPE L"xml"
#define TAG_FORMAT  L"version=\"1.0\" encoding=\"utf-16\""
#define TAG_XSD     L"xsd"
#define TAG_XSI     L"xsi"

//==============================================================================
// COpcXmlDocument

// Constructor
COpcXmlDocument::COpcXmlDocument(IXMLDOMDocument* ipUnknown)
{
    m_ipDocument = NULL;
    *this = ipUnknown;
}

// Copy Constructor
COpcXmlDocument::COpcXmlDocument(const COpcXmlDocument& cDocument)
{
    m_ipDocument = NULL;
    *this = cDocument.m_ipDocument;
}

// Destructor
COpcXmlDocument::~COpcXmlDocument()
{
    if (m_ipDocument != NULL)
    {
        m_ipDocument->Release();
        m_ipDocument = NULL;
    }
}

// Assignment
COpcXmlDocument& COpcXmlDocument::operator=(IUnknown* ipUnknown)
{
    if (m_ipDocument != NULL)
    {
        m_ipDocument->Release();
        m_ipDocument = NULL;
    }

    if (ipUnknown != NULL)
    {
        HRESULT hResult = ipUnknown->QueryInterface(__uuidof(IXMLDOMDocument), (void**)&m_ipDocument);

        if (FAILED(hResult))
        {
            m_ipDocument = NULL;
        }
    }

    return *this;
}

// Assignment
COpcXmlDocument& COpcXmlDocument::operator=(const COpcXmlDocument& cDocument)
{
    if (this == &cDocument)
    {
        return *this;
    }

    *this = cDocument.m_ipDocument;
    return *this;
}

// GetRoot
COpcXmlElement COpcXmlDocument::GetRoot() const
{
    COpcXmlElement cElement;

    // check if document exists.
    if (m_ipDocument == NULL)
    {
        return cElement;
    }

    IXMLDOMElement* ipRoot = NULL;

    HRESULT hResult = m_ipDocument->get_documentElement(&ipRoot);

    if (FAILED(hResult))
    {
        return cElement;
    }

    cElement = ipRoot;

	if (ipRoot != NULL)
	{
	    ipRoot->Release();
	}

    return cElement;
}

// FindElement
COpcXmlElement COpcXmlDocument::FindElement(const COpcString& cXPath)
{
    COpcXmlElement cElement;

    // check if document exists.
    if (m_ipDocument == NULL)
    {
        return cElement;
    }

	// serach for single node.
    IXMLDOMElement* ipElement = NULL;
	BSTR bstrQuery = SysAllocString(cXPath);
	HRESULT hResult = m_ipDocument->selectSingleNode(bstrQuery, (IXMLDOMNode**)&ipElement);
	SysFreeString(bstrQuery);
   
	if (FAILED(hResult))
    {
        return cElement;
    }

	// return result.
    cElement = ipElement;

	if (ipElement != NULL)
	{
		ipElement->Release();
	}

    return cElement;
}

// FindElements
UINT COpcXmlDocument::FindElements(const COpcString& cXPath, COpcXmlElementList& cElements)
{
	cElements.RemoveAll();

    // check if document exists.
    if (m_ipDocument == NULL)
    {
        return 0;
    }

	// search for matching nodes.
	IXMLDOMNodeList* ipNodes = NULL;

	BSTR bstrQuery = SysAllocString(cXPath);
	HRESULT hResult = m_ipDocument->selectNodes(bstrQuery, &ipNodes);
	SysFreeString(bstrQuery);
   
	if (FAILED(hResult))
    {
        return 0;
    }

	// add found nodes to element list.
	do
	{
		IXMLDOMNode* ipNode = NULL;

		hResult = ipNodes->nextNode(&ipNode);

		if (hResult != S_OK || ipNode == NULL)
		{
			break;
		}

		COpcXmlElement cElement(ipNode);
		ipNode->Release();

		if (cElement != NULL)
		{
			cElements.Append(cElement);
		}
	}
	while (SUCCEEDED(hResult));

	if (ipNodes != NULL)
	{
		ipNodes->Release();
	}

	// return the number of elements found.
	return cElements.GetSize();
}

// GetXml
bool COpcXmlDocument::GetXml(COpcString& cXml) const
{
    // check if document exists.
    if (m_ipDocument == NULL)
    {
        return false;
    }

	// get the xml.
	BSTR bstrXml = NULL;

    HRESULT hResult = m_ipDocument->get_xml(&bstrXml);

    if (FAILED(hResult))
    {
        return false;
    }

	// copy the text.
	cXml = bstrXml;
	SysFreeString(bstrXml);
	return true;
}

// Init
bool COpcXmlDocument::Init()
{
    HRESULT hResult = S_OK;

    TRY
    {
        // clear existing document.
        Clear();

        // create new document.
        hResult = CoCreateInstance(
            __uuidof(DOMDocument),
            NULL,
            CLSCTX_INPROC_SERVER,
            __uuidof(IXMLDOMDocument),
            (void**)&m_ipDocument
        );

        if (FAILED(hResult))
        {
            THROW();
        }
    }
    CATCH
    {
        Clear();
    }

    FINALLY

    return SUCCEEDED(hResult);
}

// Clear
void COpcXmlDocument::Clear()
{
    // release the document.
    if (m_ipDocument != NULL)
    {
        m_ipDocument->Release();
        m_ipDocument = NULL;
    }
}

// New
bool COpcXmlDocument::New()
{
	HRESULT hResult = S_OK;

    IXMLDOMProcessingInstruction* ipHeader = NULL;
    IXMLDOMNode*                  ipResult = NULL;

    BSTR bstrDocType = SysAllocString(TAG_DOCTYPE);
    BSTR bstrFormat  = SysAllocString(TAG_FORMAT);

    TRY
    {
        // create new document instance.
        if (!Init())
        {
            THROW_(hResult, E_FAIL);
        }

        // add document header.
        hResult = m_ipDocument->createProcessingInstruction(bstrDocType, bstrFormat, &ipHeader);

        if (FAILED(hResult))
        {
            THROW();
        }

        hResult = m_ipDocument->appendChild(ipHeader, &ipResult);

        if (FAILED(hResult))
        {
            THROW();
        }
    }
    
    CATCH
    {
        Clear();
    }

    FINALLY
    {
        // release memory.
        if (ipHeader != NULL) ipHeader->Release();
        if (ipResult != NULL) ipResult->Release();

        SysFreeString(bstrDocType);
        SysFreeString(bstrFormat);
    }

    return SUCCEEDED(hResult);
}

// New
bool COpcXmlDocument::New(IXMLDOMElement* ipElement)
{
    HRESULT hResult = S_OK;

    IXMLDOMElement* ipClone  = NULL;
    IXMLDOMNode*    ipParent = NULL;
    IXMLDOMNode*    ipResult = NULL;

    TRY
    {
        // create new document instance.
        if (!New())
        {
            THROW_(hResult, E_FAIL);
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
        hResult = m_ipDocument->appendChild(ipClone, &ipResult);

        if (FAILED(hResult))
        {
            THROW();
        }

        if (ipResult != NULL)
        {
            ipResult->Release();
            ipResult = NULL;
        }

        // declare element as the document element.
        hResult = m_ipDocument->putref_documentElement(ipClone);

        if (FAILED(hResult))
        {
            THROW();
        }

        // add predefined namespaces
        AddNamespace(TAG_XSD, OPCXML_NS_SCHEMA);
        AddNamespace(TAG_XSI, OPCXML_NS_SCHEMA_INSTANCE);
    }
    
    CATCH
    {
        Clear();
    }

    FINALLY
    {
        // release memory.
        if (ipClone != NULL)  ipClone->Release();
        if (ipParent != NULL) ipParent->Release();
        if (ipResult != NULL) ipResult->Release();
    }

    return SUCCEEDED(hResult);
}

// New
bool COpcXmlDocument::New(const COpcString& cRoot, const COpcString& cDefaultNamespace)
{
    HRESULT hResult = S_OK;

    IXMLDOMElement* ipRoot   = NULL;
    IXMLDOMNode*    ipResult = NULL;

    BSTR bstrRoot = SysAllocString((LPCWSTR)cRoot);

    TRY
    {
        // create new document instance.
        if (!New())
        {
            THROW_(hResult, E_FAIL);
        }

        // create root element.
		VARIANT vNodeType;
		vNodeType.vt   = VT_I4;
		vNodeType.lVal = NODE_ELEMENT;

		BSTR bstrNamespace = SysAllocString(cDefaultNamespace);
        hResult = m_ipDocument->createNode(vNodeType, bstrRoot, bstrNamespace, (IXMLDOMNode**)&ipRoot);
		SysFreeString(bstrNamespace);

        if (FAILED(hResult))
        {
            THROW();
        }

        // add root element to document.
        hResult = m_ipDocument->appendChild(ipRoot, &ipResult);

        if (FAILED(hResult))
        {
            THROW();
        }

        if (ipResult != NULL)
        {
            ipResult->Release();
            ipResult = NULL;
        }

        // declare element as the document element.
        hResult = m_ipDocument->putref_documentElement(ipRoot);

        if (FAILED(hResult))
        {
            THROW();
        }

        // add predefined namespaces
        AddNamespace(TAG_XSD, OPCXML_NS_SCHEMA);
        AddNamespace(TAG_XSI, OPCXML_NS_SCHEMA_INSTANCE);
    }
    
    CATCH
    {
        Clear();
    }

    FINALLY
    {
        // release memory. 
        if (ipRoot != NULL) ipRoot->Release();
        if (ipResult != NULL) ipResult->Release();
 
        SysFreeString(bstrRoot);
    }

    return SUCCEEDED(hResult);
}

// Init
bool COpcXmlDocument::LoadXml(LPCWSTR szXml)
{
    HRESULT hResult = S_OK;
    
    BSTR bstrXml = SysAllocString(szXml);

    TRY
    {
        // create new document instance.
        if (!Init())
        {
            THROW_(hResult, E_FAIL);
        }

        // parse the XML.
        VARIANT_BOOL bResult = VARIANT_FALSE;

        hResult = m_ipDocument->loadXML(bstrXml, &bResult);
       
        if (FAILED(hResult))
        {
            THROW();
        }

        if (!bResult)
        {
            THROW_(hResult, E_FAIL);
        }

        // add predefined namespaces
        AddNamespace(TAG_XSD,  OPCXML_NS_SCHEMA);
        AddNamespace(TAG_XSI,  OPCXML_NS_SCHEMA_INSTANCE);
    }
    
    CATCH
    {
        Clear();
    }
    
    FINALLY
    {
        SysFreeString(bstrXml);
    }

    return SUCCEEDED(hResult);
}


// Load
bool COpcXmlDocument::Load(const COpcString& cFilePath)
{
    HRESULT hResult = S_OK;
    
    VARIANT varFile;

    varFile.vt      = VT_BSTR;
    varFile.bstrVal = SysAllocString((LPCWSTR)cFilePath);

    TRY
    {
        // create new document instance.
        if (!Init())
        {
            THROW_(hResult, E_FAIL);
        }

        // load the file.
        VARIANT_BOOL bResult = VARIANT_FALSE;

        hResult = m_ipDocument->load(varFile, &bResult);
       
        if (FAILED(hResult))
        {
            THROW();
        }

        if (!bResult)
        {
			IXMLDOMParseError* ipError = NULL;

			hResult = m_ipDocument->get_parseError(&ipError);

			if (FAILED(hResult))
			{
				THROW_(hResult, E_FAIL);
			}

			BSTR bstrReason = NULL;

			hResult = ipError->get_reason(&bstrReason);

            if (SUCCEEDED(hResult))
			{
				SysFreeString(bstrReason);
			}

			ipError->Release();
        }

        // update default path.
        if (!cFilePath.IsEmpty()) m_cFilePath = cFilePath;
    }   
    
    CATCH
    {
        Clear();
    }
    
    FINALLY
    {
        OpcVariantClear(&varFile);
    }

    return SUCCEEDED(hResult);   
}

// Save
bool COpcXmlDocument::Save(const COpcString& cFilePath)
{
    HRESULT hResult = S_OK;
    
    VARIANT varFile;

    varFile.vt      = VT_BSTR;
    varFile.bstrVal = SysAllocString((LPCWSTR)((cFilePath.IsEmpty())?m_cFilePath:cFilePath));

    TRY
    {
        // save the file.
        hResult = m_ipDocument->save(varFile);
       
        if (FAILED(hResult))
        {
            THROW();
        }
    }   
  
    CATCH_FINALLY
    {
        OpcVariantClear(&varFile);
    }

    return SUCCEEDED(hResult);   
}

// GetDefaultNamespace
COpcString COpcXmlDocument::GetDefaultNamespace()
{
	// check for a valid root element.
	COpcXmlElement cElement(GetRoot());

    if (cElement == NULL)
    {
        return (LPCWSTR)NULL;
    }

	return cElement.GetNamespace();
}

// GetNamespaces
void COpcXmlDocument::GetNamespaces(COpcStringMap& cNamespaces)
{
	// clear the current set.
	cNamespaces.RemoveAll();

	// check for a valid root element.
	COpcXmlElement cElement(GetRoot());

    if (cElement == NULL)
    {
        return;
    }

	// add the namespace for the root element.
	COpcString cPrefix = cElement.GetPrefix();
	
	if (!cPrefix.IsEmpty())
	{
		cNamespaces[cPrefix] = cElement.GetNamespace();
	}

	// fetch the attributes from the root element.
	COpcXmlAttributeList cAttributes;

	if (cElement.GetAttributes(cAttributes) > 0)
	{
		for (UINT ii = 0; ii < cAttributes.GetSize(); ii++)
		{
			if (cAttributes[ii].GetPrefix() == OPCXML_NAMESPACE_ATTRIBUTE)
			{
				COpcString cName = cAttributes[ii].GetQualifiedName().GetName();

				// don't add the default namespace.
				if (!cName.IsEmpty())
				{
					cNamespaces[cName] = cAttributes[ii].GetValue();
				}
			}
		}
	}
}

// GetNamespacePrefix
COpcString COpcXmlDocument::GetNamespacePrefix(const COpcString& cNamespace)
{
	COpcStringMap cNamespaces;
	GetNamespaces(cNamespaces);

	OPC_POS pos = cNamespaces.GetStartPosition();

	while (pos != NULL)
	{
		COpcString cPrefix;
		COpcString cValue;
		cNamespaces.GetNextAssoc(pos, cPrefix, cValue);

		if (cValue == cNamespace)
		{
			return cPrefix;
		}
	}

	return (LPCWSTR)NULL;
}

// AddNamespace
bool COpcXmlDocument::AddNamespace(const COpcString& cPrefix, const COpcString& cNamespace)
{
    // check for a valid root element.
	COpcXmlElement cElement(GetRoot());

    if (cElement == NULL)
    {
        return false;
    }

	// check for an invalid prefix
	if (cPrefix.IsEmpty())
	{
        return false;
	}

	// construct the new namespace attribute name.
	COpcString cAttributeName;

	cAttributeName += OPCXML_NAMESPACE_ATTRIBUTE;
	cAttributeName += ":";
	cAttributeName += cPrefix;

	// add or update the namespace string.
    cElement.SetAttribute(cAttributeName, cNamespace);

	return true;
}
