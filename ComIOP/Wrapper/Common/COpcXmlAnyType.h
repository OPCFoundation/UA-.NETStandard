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

#ifndef _COpcXmlAnyType_H_
#define _COpcXmlAnyType_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "COpcString.h"
#include "COpcMap.h"
#include "COpcXmlElement.h"
#include "COpcVariant.h"

namespace OpcXml
{

//==============================================================================
// CLASS:   Schema
// PURPOSE: Describes the schema on XML element.

class Schema
{
	OPC_CLASS_NEW_DELETE_ARRAY();

public:

	//==========================================================================
	// Public Operators

	// Constructor
	Schema() {}
	Schema(const Schema& cSchema) { *this = cSchema; }

	// Destructor
	~Schema() {}

	// Assignment
	Schema& operator=(const Schema& cSchema)
	{
		m_cName       = cSchema.m_cName;
		m_cType       = cSchema.m_cType;
		m_cAttributes = cSchema.m_cAttributes;

		return *this;
	}

	//==========================================================================
	// Public Properties

	// Name
	const QName& GetName() const { return m_cName; }
	void SetName(const QName& cName) { m_cName = cName; }

	// Type
	const QName& GetType() const { return m_cType; }
	void SetType(const QName& cType) { m_cType = cType; }

	// Get
	template<class TYPE> 
	bool Get(const QName& cName, TYPE& value) const
	{
		COpcString cValue;

		if (m_cAttributes.Lookup(cName, cValue))
		{
			if (OpcXml::Read(cValue, value))
			{
				return true;
			}
		}

		Init(value);

		return false;
	}

	// Set
	template<class TYPE> 
	bool Set(const QName& cName, const TYPE& value)
	{
		COpcString cValue;

		if (OpcXml::Write(value, cValue))
		{
			m_cAttributes.SetAt(cName, cValue);
			return true;
		}

		return false;
	}

	//==========================================================================
	// Public Methods

	// Read
	bool Read(COpcXmlElement& cElement);

	// Write
	bool Write(COpcXmlElement& cParent) const;

private:

	//==========================================================================
	// Private Members

	QName                     m_cName;
	QName                     m_cType;
	COpcMap<QName,COpcString> m_cAttributes; 
};

//==============================================================================
// CLASS:   AnyType
// PURPOSE: Represents an arbitrary element of data.

class AnyType
{
	OPC_CLASS_NEW_DELETE_ARRAY();

public:
	
	//==========================================================================
	// Public Members

	Type   eType;
	Schema cSchema;
	Int    iLength;

	union
	{
		Boolean   boolValue;
	    SByte     sbyteValue;
		Byte      byteValue;
		Short     shortValue;
		UShort    ushortValue;
		Int       intValue;
		UInt      uintValue;
		Long      longValue;
		ULong     ulongValue;
		Float     floatValue;
		Double    doubleValue;
		Decimal   decimalValue;
		DateTime  dateTimeValue;
		String    stringValue;
		Boolean*  pboolValue;
	    SByte*    psbyteValue;
		Byte*     pbyteValue;
		Short*    pshortValue;
		UShort*   pushortValue;
		Int*      pintValue;
		UInt*     puintValue;
		Long*     plongValue;
		ULong*    pulongValue;
		Float*    pfloatValue;
		Double*   pdoubleValue;
		Decimal*  pdecimalValue;
		String*   pstringValue;
		DateTime* pdateTimeValue;
		AnyType*  panyTypeValue;
	};

	//==========================================================================
	// Public Operators

	// Constructor
	AnyType() {	Init(); }
	AnyType(Boolean value) { Init(); Set(value); }
	AnyType(SByte value) { Init(); Set(value); }
	AnyType(Byte value) { Init(); Set(value); }
	AnyType(Short value) { Init(); Set(value); }
	AnyType(UShort value) { Init(); Set(value); }
	AnyType(Int value) { Init(); Set(value); }
	AnyType(UInt value) { Init(); Set(value); };
	AnyType(long value) { Init(); Set(value); }
	AnyType(unsigned long value) { Init(); Set(value); }
	AnyType(Long value) { Init(); Set(value); }
	AnyType(ULong value) { Init(); Set(value); }
	AnyType(Float value) { Init(); Set(value); }
	AnyType(Double value) { Init(); Set(value); }
	AnyType(Decimal value) { Init(); Set(value); }
	AnyType(DateTime value) { Init(); Set(value); }
	AnyType(const String value) { Init(); Set(value); }
	AnyType(LPCSTR value) { Init(); Set(value); }
	AnyType(const COpcString& value) { Init(); Set(value); }
	AnyType(const COpcStringArray& value) { Init(); Set(value); }
	AnyType(const VARIANT& value) { Init(); Set(value); }
	AnyType(const AnyType& cValue) { Init(); *this = cValue; }

	// Destructor
	~AnyType()
	{
		Clear();
	}

	// Assignment
	AnyType& operator=(const AnyType& cValue);
	AnyType& operator=(const VARIANT& cValue) { Set(cValue); }
	AnyType& operator=(const COpcVariant& cValue) { Set(((COpcVariant&)cValue).GetRef()); }
	
	// Comparison
	bool operator==(const AnyType& cValue) { return (Compare(cValue) == 0); }
	bool operator<=(const AnyType& cValue) { return (Compare(cValue) <= 0); }
	bool operator>=(const AnyType& cValue) { return (Compare(cValue) >= 0); }
	bool operator< (const AnyType& cValue) { return (Compare(cValue) <  0); }
	bool operator> (const AnyType& cValue) { return (Compare(cValue) <  0); }
	bool operator!=(const AnyType& cValue) { return (Compare(cValue) != 0); }

	//==========================================================================
	// Public Methods

	// Init
	void Init()
	{
		eType   = XML_EMPTY;
		iLength = -1;
		memset(&dateTimeValue, 0, sizeof(dateTimeValue));
	}

	// Clear
	void Clear();

	// Compare
	int Compare(const AnyType& cValue);

	// Alloc
	void Alloc(Type eElement, UINT uLength);
	
	// MoveTo
	void MoveTo(AnyType& cTarget);

	// CopyTo
	bool CopyTo(AnyType& cValue, Type eNewType);

	// GetElement
	bool GetElement(int iIndex, AnyType& cElement) const;

	// SetElement
	bool SetElement(int iIndex, const AnyType& cElement);

	// Get
	bool Get(Boolean& value) const;
	bool Get(SByte& value) const;
	bool Get(Byte& value) const;
	bool Get(Short& value) const;
	bool Get(UShort& value) const;
	bool Get(Int& value) const;
	bool Get(UInt& value) const;
	bool Get(long& value) const;
	bool Get(unsigned long& value) const;
	bool Get(Long& value) const;
	bool Get(ULong& value) const;
	bool Get(Float& value) const;
	bool Get(Double& value) const;
	bool Get(Decimal& value) const;
	bool Get(DateTime& value) const;
	bool Get(String& value) const;
	bool Get(LPSTR& value) const;
	bool Get(COpcString& value) const;
	bool Get(COpcStringArray& value) const;
	bool Get(VARIANT& cVariant) const;

	// Set
	void Set(Boolean value);
	void Set(SByte value);
	void Set(Byte value);
	void Set(Short value);
	void Set(UShort value);
	void Set(Int value);
	void Set(UInt value);
	void Set(long value);
	void Set(unsigned long value);
	void Set(Long value);
	void Set(ULong value);
	void Set(Float value);
	void Set(Double value);
	void Set(Decimal value);
	void Set(DateTime value);
	void Set(const String value);
	void Set(LPCSTR value);
	void Set(const COpcString& value);
	void Set(const COpcStringArray& value);
	void Set(const VARIANT& cVariant);
};

//==============================================================================
// FUNCTION: XXX<AnyType>
// PURPOSE   Defines conversion functions for COpcVariants.

template<> OPCUTILS_API void Init<AnyType>(AnyType& cValue);
template<> OPCUTILS_API void Clear<AnyType>(AnyType& cValue);
template<> OPCUTILS_API bool ReadXml<AnyType>(IXMLDOMNode* ipNode, AnyType& cValue);
template<> OPCUTILS_API bool WriteXml<AnyType>(IXMLDOMNode* ipNode, const AnyType& cValue);

}; // OpcXml

//==============================================================================
// FUNCTION: HashKey<OpcXml::QName>
// PURPOSE:  QName hash key generator.

template<> 
inline UINT HashKey<OpcXml::QName>(const OpcXml::QName& cName)
{
    LPCTSTR key = cName.GetName();
    if (key == NULL) return -1;

	UINT nHash = 0;
	while (*key)
		nHash = (nHash<<5) + nHash + *key++;
	return nHash;
}

#endif // _COpcXmlAnyType_H_
