//	WebHelp 5.10.003
var gsNoTopics="No topics found";
var gsLoadXmlFailed="Error: Failed to load xml file.";
var gsInitDatabaseFailed="Error: Failed to initialize database.";
var gsNotAllDatabaseInited="Warning: Not all databases have been loaded. Some topics will not be found.";
var gsInvalidExpression_1="The words you typed is not a valid expression.";
var gsInvalidExpression_2="Click";
var gsInvalidExpression_3="Help";
var gsInvalidExpression_4="to learn how to use query expression.(To be finished)";
var gsSearching="Searching...";
var gsTitle="Title";
var gsRank="Rank";
var gsCancel="Cancel";
var gsCanceled="Canceled";

var gsSubstrSrch=1;
var gsHideRankColumn=0;

var g_RunesVowels="\x61\x65\x69\x6F\x75\x79";
var g_RunesWordBreaks="\x01\x02\x03\x04\x05\x06\x07\x08\x09\x0A\x0B\x0C\x0D\x0E\x0F\x10\x11\x12\x13\x14\x15\x16\x17\x18\x19\x1A\x1B\x1C\x1D\x1E\x1F\x22\x5C\x20\x2E\x2C\x21\x40\x23\x24\x25\x5E\x26\x2A\x28\x29\x7E\x27\x60\x3A\x3B\x3C\x3E\x3F\x2F\x7B\x7D\x5B\x5D\x7C\x2B\x2D\x3D\xC2\x81\xC2\x82\xC2\x83\xC2\x84\xC2\x85\xC2\x86\xC2\x87\xC2\x88\xC2\x89\xC2\x8A\xC2\x8B\xC2\x8C\xC2\x8D\xC2\x8E\xC2\x8F\xC2\x90\xC2\x91\xC2\x92\xC2\x93\xC2\x94\xC2\x95\xC2\x96\xC2\x97\xC2\x98\xC2\x99\xC2\x9A\xC2\x9B\xC2\x9C\xC2\x9D\xC2\x9E\xC2\x9F\xC2\xA1\xC2\xA9\xC2\xAB\xC2\xAE\xC2\xB7\xC2\xBB\xC2\xBF\xC2\xA0";
var g_RunesWhiteSpaces="\x20\x09\x0D\x0A\xC2\xA0";
var g_RunesSpecialBreaks = ",!@#$%^&*()~'`:;<>?/{}[]|+-=" ;
var g_RunesQuote='\x22';
var g_RunesHelSuffixes=new Array("ed,0","ingly,0","ings,0","ing,0","ly,1","s,1","e,1");

var gbHighLight=true;
var gsHLColorFront="#000000";
var gsHLColorBackground="#b2b4bf";
var gsAscendFlag = "&#9651;";
var gsDescendFlag = "&#9661;";

var gAIRSearchObject;
var gsResultDivID="ResultDiv";
var gbReady=false;
var gbXML=false;
var gsBgColor="#ffffff";
var gsBgImage="";
var gsMargin="0pt";
var gsIndent="0pt";
var goFont=null;
var goErrFont=null;
var goHoverFont=null;
var gsABgColor="#cccccc";
var gbWhFHost=false;
var gbAIRSearchResultDisplayDoneFn=dummySearchResultFn;
var gbAIRSearchResultDoneSteps=0;
var gbAIRSearchResultDone=false;

var g_nSortType = 0;
var g_nX0 = 0;
var g_nInitWidth = 0;
var g_bDraging = false;
var g_nRankWidthMin = 20;

var ECS_NONE			= 0;
var ECS_FTSREADY		= 1;
var ECS_SEARCHING		= 2;
var ECS_FATALERROR		= 3;
var ECS_CANCELED		= 4;
var ECS_SEARCHFAILED	= 5;
var ECS_FOUND			= 6;

var g_CurState = null;
var g_CurPage = 1;
var g_nMaxPages = 3 ;
var g_sQuestion='';
var g_bServerResult=false;
gbSearchPage=true;
var gbAIRSynonymString='';
var gbAIRSearchString='';

function setBackground(sBgImage)
{
	gsBgImage=sBgImage;
}

function setBackgroundcolor(sBgColor)
{
	gsBgColor=sBgColor;
}

function setFont(sType,sFontName,sFontSize,sFontColor,sFontStyle,sFontWeight,sFontDecoration)
{
	var vFont=new whFont(sFontName,sFontSize,sFontColor,sFontStyle,sFontWeight,sFontDecoration);
	if(sType=="Normal")
		goFont=vFont;
	else if(sType=="Error")
		goErrFont=vFont;
	else if(sType=="Hover")
		goHoverFont=vFont;
}

function setActiveBgColor(sBgColor)
{
	gsABgColor=sBgColor;
}

function setMargin(sMargin)
{
	gsMargin=sMargin;
}

function setIndent(sIndent)
{
	gsIndent=sIndent;
}

function window_OnLoad()
{
	if(gsBgImage&&gsBgImage.length>0)
	{
		document.body.background=gsBgImage;
	}
	if(gsBgColor&&gsBgColor.length>0)
	{
		document.body.bgColor=gsBgColor;
	}
	writeResultDiv();
	if(gbAIR)
		return;
	if(gbAIRSSL)
	{
		var strProjectDir=_getPath(document.location.href);
		SeeForSearch(strProjectDir);
		return;
	}
		
	loadFts();
	var oMsg=new whMessage(WH_MSG_SHOWFTS,this,1,null)
	SendMessage(oMsg);
	oMsg = new whMessage(WH_MSG_GETSEARCHSTR, this, 1, null);
	var strTerms = "";
	if (SendMessage(oMsg))
	{
		strTerms = oMsg.oParam;		
		if (strTerms != " " )
		{
			oMsg = new whMessage(WH_MSG_SEARCHTHIS, this, 1, strTerms);		
			SendMessage(oMsg);
		}		
	}
}

function writeResultDiv()
{
	var sHTML= "<div id='" + gsResultDivID+ "' style='position:absolute; width:100%'></div>";
	if(gbAIRSSL)
		document.body.innerHTML = sHTML;
	else	document.body.insertAdjacentHTML("beforeEnd",sHTML);
}

function registListener( a_Context, a_this )
{
	if(!gbAIRSSL)
		RegisterListener2( a_this, WH_MSG_SEARCHFTSKEY );
}

function FtsWriteClassStyle()
{
	var sStyle="<STYLE TYPE='text/css'>\n";
	if (gsBgImage)
		sStyle+="body {border-top:"+gsBgColor+" 1px solid;}\n";
	else if(gbAIR)
		sStyle+="body {border-top:white 1px solid;}\n";
	else 
	    sStyle+="body {border-top:black 1px solid;}\n";
	sStyle+="P.msg {"+getFontStyle(goErrFont)+"margin-left:"+gsIndent+";margin-top:"+gsMargin+";}\n";
	sStyle+="dl {"+getFontStyle(goFont)+"margin-left:"+gsIndent+";margin-top:"+gsMargin+";}\n";
	sStyle+="A.fr_tc:link {"+getFontStyle(goFont)+"font-weight:bold;}\n";
	sStyle+="A.fr_tc:visited {"+getFontStyle(goFont)+"font-weight:bold;}\n";
	sStyle+="A.fr_tc:hover {"+getFontStyle(goHoverFont)+"font-weight:bold;}\n";
	sStyle+="A.fr_act {background-color:"+gsABgColor+";}\n";
	sStyle+="A.fr_act:link {"+getFontStyle(goFont)+"}\n";
	sStyle+="A.fr_act:visited {"+getFontStyle(goFont)+"}\n";
	sStyle+="A.fr_act:hover {"+getFontStyle(goHoverFont)+"}\n";
	sStyle+="LI.sl_page{display:block;display:inline-block;float:left;cursor:pointer;text-decoration:none;border:1px solid #ccc;padding:.3em .7em;margin:0 .38em 0 0;text-align:center}\n";
	sStyle+="LI.sl_pageC{background:#f0f7e8;border:1px solid #83bc46;display:block;display:inline-block;float:left;cursor:pointer;text-decoration:none;padding:.3em .7em;margin:0 .38em 0 0;text-align:center}\n";
	sStyle+="LI.sl_pageN{cursor:pointer;padding:.2em .5em;margin:0 .38em 0 0;border:none;text-decoration:underline}\n";
	sStyle+="TD.fr_tt, TD.fr_tc, TD.fr_ts {"+getFontStyle(goFont)+"height:auto;}\n";
	if(gbAIRSSL)
	    sStyle+="TD.fr_ts {padding:0.2em 0em;border-style:solid solid solid none;border-color:"+gsABgColor+";border-width:1px;}\n";
	else
	    sStyle+="TD.fr_ts {padding:0.2em 0em;border-style:none solid solid none;border-color:"+gsABgColor+";border-width:1px;}\n";
	if(gbAIRSSL)
	    sStyle+="TD.fr_tt {padding:0.2em 0.5em;border-style:solid none solid none;border-color:"+gsABgColor+";border-width:1px;}\n";
	else
	    sStyle+="TD.fr_tt {padding:0.2em 0.5em;border-style:none none solid none;border-color:"+gsABgColor+";border-width:1px;}\n";
	sStyle+="TD.fr_tc {padding:0.2em 0.5em;}\n";
	sStyle+="P.pb_tt {"+getFontStyle(goErrFont)+"padding:0;margin:3px;}\n";
	sStyle+="P.pb_in {"+getFontStyle(goErrFont)+"position:relative;padding:0;margin:0;left:0%;top:0%;height:10px;border-style:none;background-color:blue;}\n";
	sStyle+="DIV.pb_out {width:100%;border-style:solid;border-width:1px;padding:0;margin:0;background-color:"+gsABgColor+";}\n";
	sStyle+="P.pb_cancel {"+getFontStyle(goHoverFont)+"cursor:pointer;padding:0;margin:3px}\n";
	sStyle+="</STYLE>";
	document.write(sStyle);
}

function window_Unload()
{
	if(!gbAIRSSL)
	{
		UnRegisterListener2(this,WH_MSG_PROJECTREADY);
		UnRegisterListener2(this, WH_MSG_SEARCHFTSKEY);
	}
}

function mrFindFTSKey()
{
	if( goOdinHunter.aProjPathes.length<=0 )
		return;

	// request the server
	var sUrl=goOdinHunter.aProjPathes[0].strOdbPath+'&question='+escape(g_sQuestion)+'&log=1';

	g_bServerResult=true;
	loadDataXML(sUrl,false);
};

function onSendMessage(oMsg)
{
	if(gbAIRSSL)
		return true;

	var nMsgId = oMsg.nMessageId;
	if ( nMsgId == WH_MSG_SEARCHFTSKEY )
	{
		if ( oMsg.oParam )
		{
			// redirect to roboengine search if gbEngine==true
			if( mrIsOnEngine()==true )
			{
				// this is server side fts
				g_sQuestion=oMsg.oParam;
				mrFindFTSKey();
			}else
			{
				// this is client side fts
				goOdinHunter.strQuery = oMsg.oParam;
				setTimeout( "Query()", 1 );
			};
		}
	}
	else if ( nMsgId == WH_MSG_PROJECTREADY )
	{
		loadFts();
	}
	return true;
}



////////////// ODIN FULL-TEXT SEARCH--------------------------------------

// Odin global variables------------------------

var context = new HuginContext();
var goOdinHunter = null;

// XmlUtility.js--------------------------------

var theXmlReader = new XmlReader();

function XmlNode()
{
	this.strTagName = null;
	this.aAttrs = new Array();
}

function XmlData()
{
	this.strFilePath = null;
	this.strRoot = null;
	this.aNodes = new Array();
}

function XmlReader()
{
	this.strFilePath = null;			//in
	this.funcCallback = null;			//in

	this.bSucceed = false;				//out

	this.xmlDoc = null;
	this.aCache = new Array();
	this.bCache = false;
	this.curData = null;	

	this.loadFromCache = function()
	{
		for ( var i = 0; i < this.aCache.length; i++ )
		{
			if ( this.aCache[i].strFilePath == this.strFilePath )
			{
				this.curData = this.aCache[i];
				return true;
			}
		}
		return false;
	}

	this.loadFromFile = function( a_funcCallback, a_bCache )
	{
		this.funcCallback = a_funcCallback;
		this.bCache = ( a_bCache == true ) ? true : false;
		if ( this.loadFromCache() )
		{
			this.funcCallback();
			return;
		}
		loadDataXML( this.strFilePath,false );
	}

	this.receiveDom = function( a_XmlDoc )
	{
		this.curData = null;
		if ( a_XmlDoc.documentElement == null )
			return;

		this.curData = new XmlData();
		this.curData.strFilePath = this.strFilePath;
		with( a_XmlDoc.documentElement )
		{
			this.curData.strRoot = tagName;
			for ( var i = 0; i < childNodes.length; i++ )
			{
				with( a_XmlDoc.documentElement.childNodes.item( i ) )
				{
					if( nodeType == 3 )
						continue;
					var nLen = this.curData.aNodes.length;
					this.curData.aNodes[nLen] = new XmlNode();
					this.curData.aNodes[nLen].strTagName = tagName;
					for ( var j = 0; j < attributes.length; j++ )
					{
						this.curData.aNodes[nLen].aAttrs[attributes.item( j ).name] = attributes.item( j ).value;
					}
				}
			}
		}
		if ( this.bCache )
		{
			this.aCache[this.aCache.length] = this.curData;
		}
	}
	
	this.getNumOfNodes = function()
	{
	    return this.curData.aNodes.length ;
	}
	
	this.getNumOfTopics = function(i)
	{
	    with( this.curData )
		{
			if( i < aNodes.length)
			{
				var num ;
				try
				{
					num = parseInt(aNodes[i].aAttrs["num"]);
				}
				catch(e)
				{
				    return 0 ;
				}
				return num ;
			}
			else
				return 0 ;
		}
		
	}
	
	this.checkRoot = function( a_strRootName )
	{
		return this.curData.strRoot == a_strRootName;
	}
	
	this.getSynonyms = function ( a_strQuery )
	{
		if (gsSubstrSrch)
		{
			var synonyms = "" ;
			with( this.curData )
			{
				for ( var i = 0; i < aNodes.length; i++ )
				{
					if (aNodes[i].aAttrs["nm"].indexOf(a_strQuery) != -1)
						synonyms += "," + aNodes[i].aAttrs["sy"];
				}
			}
			return synonyms ;
		}
		else
		{
			return this.getAttr( "wd", "nm", a_strQuery, "sy" ) ;
		}
	}

	this.getWordRec = function ( a_strQuery , bPhraseSearch)
	{
		var begin = 0 ; 
		var end = this.curData.aNodes.length ; 
		var mid = Math.floor((end -begin ) / 2) ;
		while (mid > 0 )
		{
			mid = mid + begin ;			
			var term = this.curData.aNodes[mid].aAttrs["nm"] ;
			if (a_strQuery < term) 
				end = mid ;
			else if (a_strQuery > term)
				begin = mid ;
			else
				break ;
			mid = Math.floor((end -begin ) / 2) ;
		}
		if (((end-begin) == 1)&&(!this.matchPrefix(a_strQuery,this.curData.aNodes[mid].aAttrs["nm"])))
		{
			mid = end ;			
		}
		if (mid < this.curData.aNodes.length)
		{
			if (!bPhraseSearch && gsSubstrSrch)
			{
				//get all the records with matching prefix
				var arrTopicRecs = new Array();
				while( mid<this.curData.aNodes.length)
				{
					if (this.curData.aNodes[mid].aAttrs["sp"])
					{
						mid++ ;
						continue ;
					}
					else if (this.matchPrefix(a_strQuery,this.curData.aNodes[mid].aAttrs["nm"]))
					{
						arrTopicRecs[arrTopicRecs.length] = this.curData.aNodes[mid].aAttrs["rd"] ;
						mid++ ;
					}
					else
						break ;
				}
				if (arrTopicRecs.length == 0 )
					return "" ;
				var mergedRec = arrTopicRecs[0] ;
				for (var i = 1 ; i < arrTopicRecs.length ; i++)
				{
					mergedRec = mergeTopicRec(mergedRec , arrTopicRecs[i]);
				}
				return mergedRec ;									
			}
			else
			{
				//get the one with exact match
				if ((this.curData.aNodes[mid].aAttrs["nm"])==a_strQuery) 
				{
					if (bPhraseSearch || gsSubstrSrch)
						return this.curData.aNodes[mid].aAttrs["rd"] ;
					else
					{
						//do not return stop words
						if (this.curData.aNodes[mid].aAttrs["sp"])
							return "" ;
						else
							return this.curData.aNodes[mid].aAttrs["rd"] ;
					}		
				}
				else
					return "" ;
			}
		}			
	}
	
	this.getTopicRec = function ( a_nTopicId)
	{
		if ((a_nTopicId >= 0)&&(a_nTopicId < this.curData.aNodes.length ))
		{
			var objResult = new Object() ;
			objResult.rd = this.curData.aNodes[a_nTopicId].aAttrs["rd"];
			objResult.ct = this.curData.aNodes[a_nTopicId].aAttrs["ct"];
			return objResult ;
		}
		else
			return null ;	
	}
	
	this.getPackageIndex = function (a_strQuery)
	{
		var begin = 0 ; 
		var end = this.curData.aNodes.length - 1 ; 
		var mid;
		while (begin <= end)
		{
			mid = Math.floor((begin+end)/2);
			var startWord = this.curData.aNodes[mid].aAttrs["fm"] ;
			var endWord = this.curData.aNodes[mid].aAttrs["to"] ;
			if (end == begin)
			{
				if ((a_strQuery >= startWord )&&(a_strQuery <= endWord))
					return mid ;
				else 
					return -1 ;
			}
			if (a_strQuery < startWord )
				end = mid - 1 ;
			else if (a_strQuery > endWord )
				begin = mid + 1;
			else
				return mid ;		
		}		
		return -1 ;
	}
	
	this.matchPrefix = function (a_strQuery , a_strTerm )
	{
		if (a_strQuery.length > a_strTerm.length )
			return false ;
		var bPrefix = true ;
		var i ; 
		for (i=0; i< a_strQuery.length;i++)
		{
			if (a_strQuery.charAt(i) != a_strTerm.charAt(i))
			{
				bPrefix = false ;
				break ;
			}
		}
		return bPrefix ;
	}

	this.getAttr = function( a_strTagName )
	{
		var nArgsNum = this.getAttr.arguments.length;
		if ( nArgsNum < 2 || nArgsNum % 2 != 0 )
			return "";
			
		with( this.curData )
		{
			for ( var i = 0; i < aNodes.length; i++ )
			{
				if ( utf8Compare(aNodes[i].strTagName,a_strTagName) != 0 )
					continue;
				for ( var j = 1; j + 1 < nArgsNum - 1; j += 2 )
				{
					if ( utf8Compare(aNodes[i].aAttrs[this.getAttr.arguments[j]], this.getAttr.arguments[j + 1]) != 0 )
						break;
				}
				if ( j + 1 < nArgsNum - 1 )
					continue;
				if ( aNodes[i].aAttrs[this.getAttr.arguments[j]] )
					return aNodes[i].aAttrs[this.getAttr.arguments[j]];
				else
					continue;
			}
			return "";
		}
	}
	
	this.checkAttr = function( a_strTagName )
	{
		var nArgsNum = this.checkAttr.arguments.length;
		if ( nArgsNum < 1 || nArgsNum % 2 == 0 )
			return false;
			
		with( this.curData )
		{
			for ( var i = 0; i < aNodes.length; i++ )
			{
				if ( utf8Compare(aNodes[i].strTagName, a_strTagName) != 0 )
					continue;
				for ( var j = 1; j < nArgsNum - 1; j += 2 )
				{
					if ( utf8Compare(aNodes[i].aAttrs[this.checkAttr.arguments[j]], this.checkAttr.arguments[j + 1]) != 0 )
						break;
				}
				if ( j < nArgsNum - 1 )
					continue;
				return true;
			}
			return false;
		}
	}
}

function putDataXML( xmlDoc, sdocPath )
{
	if(g_bServerResult==true)
	{
		g_bServerResult=false;

		// received the search result
		var cRoot=xmlDoc.lastChild;
		var cResult=new HuginQueryResult();
		var nIndex=1;
		if(cRoot)
		{
			var cNode=cRoot.firstChild;
			while(cNode)
			{
				if(cNode.nodeName=="topic")
				{
					var cTopic=new Object();
					cTopic.nIndex=nIndex;
					cTopic.strTitle=cNode.getAttribute("name");
					cTopic.strUrl=cNode.getAttribute("url");
					cTopic.strSummary=cNode.getAttribute("summary");
					cTopic.nRank=cNode.getAttribute("rank");

					cResult.aTopics[cResult.aTopics.length]=cTopic;
				};
				cNode=cNode.nextSibling;
				nIndex=nIndex+1;
			};
		};

		displayTopics(cResult)
	}else
	{
		theXmlReader.receiveDom( xmlDoc );
		theXmlReader.bSucc = true;
		theXmlReader.funcCallback();
	};
}

function mergeTopicRec(a_strParentRec , a_strNewRec)
{
	var arrOldRecords = a_strParentRec.split("|");	
	var arrNewRecords = a_strNewRec.split("|");	
		
	var mergedRec = "" ;
	var i = 0 ;
	var j = 0 ;	
	var arrFinalRec = new Array();
	while ( i< arrOldRecords.length && j < arrNewRecords.length)
	{		
		var oldTopicRecord = getTopicDetails(arrOldRecords[i]);
		if (oldTopicRecord == null)
		{
			i++;
			continue ;
		}
		var newTopicRecord = getTopicDetails(arrNewRecords[j]);
		if (newTopicRecord == null)
		{
			j++;
			continue ;
		}
		if (oldTopicRecord.nTopicId < newTopicRecord.nTopicId)
		{
			arrFinalRec[arrFinalRec.length] = arrOldRecords[i] ;
			i++ ;
		}
		else if (oldTopicRecord.nTopicId > newTopicRecord.nTopicId)
		{
			arrFinalRec[arrFinalRec.length] = arrNewRecords[j] ;
			j++ ;
		}
		else
		{
			var temp = arrOldRecords[i].split(":");
			var uEmphasis =  (oldTopicRecord.uEmphasis > newTopicRecord.uEmphasis)?oldTopicRecord.uEmphasis:newTopicRecord.uEmphasis;
			var strRec = oldTopicRecord.nTopicId + "," + uEmphasis + ":" + temp [1] ; //since this will not be called in case of phrase search, we can ignore positions of other rec
			arrFinalRec[arrFinalRec.length] = strRec ;
			j++ ;
			i++ ;
		}
	}
	while (i< arrOldRecords.length)
	{
		arrFinalRec[arrFinalRec.length] = arrOldRecords[i] ;
		i++ ;
	}
	while (j < arrNewRecords.length)
	{
		arrFinalRec[arrFinalRec.length] = arrNewRecords[j] ;
		j++ ;
	}

	if (arrFinalRec.length == 0)
		return a_strParentRec ; 
	mergedRec = arrFinalRec[0];
	for ( i = 1 ; i < arrFinalRec.length ; i++)
		mergedRec += "|" + arrFinalRec[i] ;
	return mergedRec ;	
}

function getTopicDetails( a_strRecord )
{
	var index = a_strRecord.indexOf(",");
	if (index == -1)
		return null ;
	var nTopicId = a_strRecord.substring(0 , index);
	var strTopicDetails = a_strRecord.substring(index+1,a_strRecord.length);	
	var aShapes = strTopicDetails.split( ":" );
	if ( aShapes.length == 0 )
		return null;

	var record = new Object();
	record.nTopicId = nTopicId ;
	record.uEmphasis = parseInt( aShapes[0] );
	return record ;
}

function onLoadXMLError()
{
	//For Debug & Test------
	if ( window.gbTesting )
		return;
	//----------------------

	theXmlReader.bSucc = false;
	theXmlReader.funcCallback();
}

////////////////////////////////

function splitPathName( a_strPath )	//this utility function only fit this project.
{
	var rslt = new Object();
	rslt.strDir = "";
	rslt.strFile = "";
	rslt.strExt = "";

	var rg1 = /^(.*[\\\/])?([^\\\/]+)(\.[^\\\/\.]*)$/;
	var rg2 = /^(.*[\\\/])?([^\\\/.]+)$/;
	
	var v = a_strPath.match( rg1 );
	if ( v != null )
	{
		rslt.strDir = v[1];
		rslt.strFile = v[2];
		rslt.strExt = v[3];
	}
	else
	{
		v = a_strPath.match( rg2 );
		rslt.strDir = v[1];
		rslt.strFile = v[2];
		rslt.strExt = "";
	}
	return rslt;
}

function getAbsPath( a_strBasePath, a_strRelPath )
{
	var sf = splitPathName( a_strBasePath );
	return sf.strDir + a_strRelPath;
}

// HuginContext.js------------------------------

function HuginContext()
{
	this.aTasks = new Array();

	this.bExecuting = false;
	this.bCallBack = false;
	this.bCallBackReady = false;

	this.nLastTime = 0;

	this.bError = false;
	this.bCancel = false;
	this.strMsg = null;

	this.reset = function()
	{
		this.aTasks.length = 0;

		this.bExecuting = false;
		this.bCallBack = false;
		this.bCallBackReady = false;

		this.nLastTime = 0;

		this.bError = false;
		this.bCancel = false;
		this.strMsg = null;
	}

	this.push = function()
	{
		var i = 0;
		var ttasks = new Array();
		while( i < context.push.arguments.length )
		{
			var nLen = ttasks.length;
			ttasks[nLen] = new Object();
			ttasks[nLen].func = context.push.arguments[i];
			ttasks[nLen].owner = context.push.arguments[i + 1];
			i += 2;
			var bSuspend = false;
			if ( i < context.push.arguments.length &&
				 context.push.arguments[i].constructor.toString().search( /^\nfunction Boolean/ ) == 0 )
			{
				var bSuspend = context.push.arguments[i];
				++i;
			}
			ttasks[nLen].bSuspend = bSuspend;
		}
		for ( i = ttasks.length - 1; i >= 0; --i )
			context.aTasks[context.aTasks.length] = ttasks[i];
	}

	this.pop = function()
	{
		if ( context.aTasks.length == 0 )
			return null;
		
		var task = context.aTasks[context.aTasks.length - 1];
		context.aTasks.length--;	
		return task;
	}

	this.initTime = function()
	{
		context.nLastTime = ( new Date() ).getTime();
	}
	
	this.needBreathe = function()
	{
		var nCurTime = ( new Date() ).getTime();
		return nCurTime - context.nLastTime >= 100;
	}

	this.resume = function()
	{
		if ( context.bExecuting )
		{
			if ( context.bCallBack )
				context.bCallBackReady = true;
			return;
		}
	
		context.bExecuting = true;
		context.initTime();
		while ( true )
		{
			if ( context.bCancel )
			{
				context.bExecuting = false;
				g_CurState = ECS_CANCELED;
				updateResultView();
				return true;
			}
			
			var task = context.pop();
			if ( task == null )
			{	//All tasks were finished.
				context.bExecuting = false;
				return true;
			}

			context.bCallBack = task.bSuspend;
			task.func( context, task.owner, context.resume );	//If it is not a suspend task, the 3rd argument will be ignored.
			if ( context.bError )
			{	//Failed. Stop executing.
				context.bExecuting = false;
				g_CurState = ECS_FATALERROR;
				updateResultView();
				return;
			}

			if ( context.bCallBack && !context.bCallBackReady )
			{	//Callback function not finished. Give the resume right to it.
				context.bExecuting = false;
				return;
			}
			context.bCallBack = false;
			context.bCallBackReady = false;

			if ( context.needBreathe() )
			{	//Give GUI a chance to process messages.
				context.bExecuting = false;
				updateResultView();
				if(gbAIR)
					context.resume();
				else setTimeout( "context.resume();", 1 );
				return;
			}
		}
	}
	
	this.stop = function()
	{
		context.bCancel = true;
	}
}

// Base64.js------------------------------------

var XX = 127;		//mark for not used

var s_strBase64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
var s_aAsciiToBase64 = new Array
(
	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,		//  00~0F
	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,		//  10~1F
	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	XX,	62,	XX,	XX,	XX,	63,		//  20~2F	'+'=62, '/'=63
	52,	53,	54,	55,	56,	57,	58,	59,	60,	61,	XX,	XX,	XX,	XX,	XX,	XX,		//  30~3F	'0'=52 to '9'=61
	XX,	0,	1,	2,	3,	4,	5,	6,	7,	8,	9,	10,	11,	12,	13,	14,		//  40~4F	'A'=0 to 'O'=14
	15,	16,	17,	18,	19,	20,	21,	22,	23,	24,	25,	XX,	XX,	XX,	XX,	XX,		//  50~5F	'P'=15 to 'Z'=25
	XX,	26,	27,	28,	29,	30,	31,	32,	33,	34,	35,	36,	37,	38,	39,	40,		//  60~6F	'a'=26 to 'o'=40
	41,	42,	43,	44,	45,	46,	47,	48,	49,	50,	51,	XX,	XX,	XX,	XX,	XX		//  70~7F	'p'=41 to 'z'=51
);
var s_aOdinToAscii = new Array
(	// the first the char is '\0', so that s_aOdinToAscii[0] means end of string
	'\0', '0', '1', '2', '3', '4', '5', '6',
	'7', '8', '9', '|', ':', ',', '\0', '\0'
);

var s_aBase64DecodeMap = new Array();

function _decordBase64ToStr( a_nA, a_nB )
{
	var uBuf = ( s_aAsciiToBase64[a_nA] << 6 ) + s_aAsciiToBase64[a_nB];
	var strRslt = "";
	var uCur = 0;
	for ( var i = 0; i <= 8; i += 4 )
	{	
		uCur = uBuf >> ( 8 - i ) & 0x000F;
		if ( uCur == 0 )
			return strRslt;
		strRslt += s_aOdinToAscii[uCur];
	}
	return strRslt;
}

// Decode a base64 formatted string into an odin formatted one (only contains charactors occur in s_strBase64
// [in]a_strBase64:	Base64 formatted string;
// RESULT:			Odin formatted string;
function decodeBase64ToOdin( a_strBase64 )
{
	var nLen = a_strBase64.length;
	var strRslt = "";
	var str = 0;
	for ( var i = 0; i + 1 < nLen; i += 2 )
	{
		str = s_aBase64DecodeMap[( a_strBase64.charCodeAt( i ) << 8 ) + a_strBase64.charCodeAt( i + 1 )]
		if ( !str )		return strRslt;
		strRslt += str;
	}
	if ( i < nLen )
	{
		str = s_aBase64DecodeMap[( a_strBase64.charCodeAt( i ) << 8 ) + s_strBase64.charCodeAt( 0 )]
		if ( !str )		return strRslt;
		strRslt += str;
	}
	return strRslt;
}

function initBase64DecodeMap()
{
	var i, j;
	for ( i = 0; i < 64; i++ )
		for ( j = 0; j < 64; j++ )
			s_aBase64DecodeMap[( s_strBase64.charCodeAt( i ) << 8 ) + s_strBase64.charCodeAt( j )] = _decordBase64ToStr( s_strBase64.charCodeAt( i ), s_strBase64.charCodeAt( j ) );
	for ( i = 0; i < 64; i++ )
		s_aBase64DecodeMap[s_strBase64.charCodeAt( i ) << 8] = _decordBase64ToStr( s_strBase64.charCodeAt( i ), s_strBase64.charCodeAt( 0 ) );
}

initBase64DecodeMap();

// HuginPackageReader.js------------------------

function HuginStemRecordTopicShape()
{
	this.aPositions = null;
}

function HuginStemRecordTopic()
{
	this.uEmphasis = null;
	this.aShapes = null;
}

function HuginStemRecord()
{
	this.aTopics = null;
}

function HuginPackageReaderResult()
{
	this.strNefStem = null;
	this.strRecord = null;
	this.bStopWord = null ;
}

function HuginPackageReader()
{
	this.strPackagePath = null;			//in
	this.strSynonymPath = null ;		//in
	this.recordResult = null;			//out
	this.strQueryWord = null;           //in
	this.bPhraseSearch = false ;
	this.bSucc = true;

	this.prepareQuery = function()
	{
		this.recordResult = null;			//out
		this.bSucc = true;
		this.strQueryWord = null;
		this.bPhraseSearch = false ;
	}

	this.loadFromFile = function( a_Context, a_this, a_funcCallback )
	{
		theXmlReader.loadFromFile( a_funcCallback, false );
	}

	this.pickSynonyms = function( a_strStem )
	{
		return theXmlReader.getSynonyms(a_strStem).split( "," );
	}
	
	this.setSynonymForHighlighting = function( a_Context, a_this )
	{
		if ( !theXmlReader.bSucc )
		{
			return;
		}
		var strSyn = "";
		var arySynonyms = a_this.pickSynonyms( a_this.strQueryWord );
		for ( var i = 0; i < arySynonyms.length; ++i )
		{
			strSyn += " " + arySynonyms[i];			
		}
		if ((strSyn == "")||(strSyn == " "))
			return ;
		if(!gbAIRSSL)
		{
			var oResMsg = new whMessage( WH_MSG_SETSYNSTR, a_this, 1, strSyn );
			SendMessage( oResMsg );
		}
		else
		{
		    gbAIRSynonymString = strSyn;
		}	
	}


	this.doQueryWordRecord = function( a_Context, a_this )
	{
		if ( !theXmlReader.bSucc )
		{
			a_Context.strMsg = gsLoadXmlFailed;
			a_Context.bError = true;
			return;
		}

		if ( !a_this.bSucc )
			return;
			
		//	By Lein 4:59 PM 7/15/2004
		a_this.recordResult = new HuginPackageReaderResult();
		var strRecord = theXmlReader.getWordRec(a_this.strQueryWord,a_this.bPhraseSearch);
		if ( strRecord == null )
			return;
		a_this.recordResult.bStopWord = false;
		a_this.recordResult.strNefStem = a_this.strQueryWord;
		a_this.recordResult.strRecord = strRecord;
		if (strRecord == "" )
		{
			a_this.bSucc = false;
			return;
		}
		a_this.bSucc = true;
		theXmlReader.strFilePath = a_this.strSynonymPath;
		a_Context.push( a_this.loadFromFile, a_this,
						a_this.setSynonymForHighlighting, a_this );		
	}

	this.query = function( a_Context, a_this )
	{
		a_this.bSucc = true;
		theXmlReader.strFilePath = a_this.strPackagePath;
		a_Context.push( a_this.loadFromFile, a_this,
						a_this.doQueryWordRecord, a_this );
	}
}

function HuginPackageIndexReader()
{
	this.queryWord = null;				//in
	this.strPackageIndexPath = null;	//in
	this.packageInfo = null;			//out
	this.bSucc = true;
	this.strCurQuery = null;
	this.strPackageInfo = null;

	this.prepareQuery = function()
	{
		this.packageInfo = null;			//out
		this.bSucc = true;
		this.strCurQuery = null;
		this.strPackageInfo = null;
	}

	this.getPackagePath = function( a_strSuffix )
	{
		var sf = splitPathName( this.strPackageIndexPath );
		return sf.strDir + "package_" + a_strSuffix + sf.strExt;
	}
	
	this.getTopicTablePath = function( a_strSuffix )
	{
		var sf = splitPathName( this.strPackageIndexPath );
		return sf.strDir + "topictable_" + a_strSuffix + sf.strExt;
	}

	this.parsePackageInfo = function( a_Context, a_this )
	{
		if ( !theXmlReader.bSucc ||
			 !theXmlReader.checkRoot( "cki" ) )
		{
			a_this.bSucc = false;			
			return;
		}
		var nPackageIndex = theXmlReader.getPackageIndex(a_this.strCurQuery);
		if ( nPackageIndex == null || nPackageIndex < 0 )
		{
			a_this.packageInfo = null;
			a_this.bSucc = false;			
			return;
		}

		a_this.packageInfo = a_this.getPackagePath( nPackageIndex );
		this.bSucc = true;
		return ;
	}
	
	this.parseTopicInfo = function( a_Context, a_this )
	{
		if ( !theXmlReader.bSucc ||
			 !theXmlReader.checkRoot( "cki" ) )
		{
			a_this.bSucc = false;			
			return;
		}
		var nPackageIndex = theXmlReader.getPackageIndex(a_this.strCurQuery);
		if ( nPackageIndex == null || nPackageIndex < 0 )
		{
			a_this.packageInfo = null;
			a_this.bSucc = false;			
			return;
		}

		a_this.packageInfo = a_this.getTopicTablePath( nPackageIndex );
		this.bSucc = true;
		return ;
	}
	
	this.loadFromFile = function( a_Context, a_this, a_funcCallback )
	{
		theXmlReader.loadFromFile( a_funcCallback , false);
	}

	this.queryPackageInfo = function( a_Context, a_this )
	{
		a_this.bSucc = true;
		theXmlReader.strFilePath = a_this.strPackageIndexPath;
		a_Context.push( a_this.loadFromFile, a_this,
						a_this.parsePackageInfo, a_this );
	}
	
	this.queryTopicInfo = function( a_Context, a_this )
	{
		a_this.bSucc = true;
		theXmlReader.strFilePath = a_this.strPackageIndexPath;
		a_Context.push( a_this.loadFromFile, a_this,
						a_this.parseTopicInfo, a_this );
	}
}

function HuginTopicTableReader()
{
	this.nQueryId = null;				//in
	this.strTopicTablePath = null;		//in
	this.lastIndex = -1;
	this.topicInfo = null;				//out
	this.bSucc = true;
	this.strTopicInfo = null;
	this.strTopicContext = null;
	this.topicMap = null ;
	this.curTopicIndex = null ;
	
	this.prepareQuery = function()
	{
		this.bSucc = true;
		this.strTopicInfo = null;		
	}
	
	this.loadFromFile = function( a_Context, a_this, a_funcCallback )
	{
		theXmlReader.loadFromFile( a_funcCallback );
	}

	this.processReaderResult = function( a_Context, a_this )
	{
		if ( !theXmlReader.bSucc ||
			 !theXmlReader.checkRoot( "ck" ) )
		{
			a_this.bSucc = false;
			a_this.bInited = false;
			return;
		}
		var queryId = a_this.nQueryId ;
		if (a_this.curTopicIndex > 0 )
		    queryId = queryId - a_this.topicMap[a_this.curTopicIndex -1 ] ;
		var topicInfo = theXmlReader.getTopicRec(queryId);
		a_this.strTopicInfo = topicInfo.rd;
		a_this.strTopicContext = topicInfo.ct;
	}

	this.parseTopicInfo = function( a_Context, a_this )
	{
		if ( !a_this.bSucc )
			return;

		a_this.topicInfo = new Object();
		var v = a_this.strTopicInfo.split( "|" );
		if ( v.length < 2 || v[0] == "" || v[1] == "" )
		{
			a_this.bSucc == false;
			return;
		}
		a_this.topicInfo.strUrl = v[0];
		a_this.topicInfo.strTitle = v[1];
	}

	this.queryTopicInfo = function( a_Context, a_this )
	{
		for (var i = 0 ; i < a_this.topicMap.length ; i++)
		{
			if (a_this.nQueryId < a_this.topicMap[i] )
				break ;			
		}
		if (i >= a_this.topicMap.length)
		{
			a_this.bSucc == false;
			return;
		}
		a_this.curTopicIndex = i ;
		var topictablefilename = "topictable_" + i + ".xml" ;
        if(i != a_this.lastIndex)
		{
			a_this.lastIndex = i;
			theXmlReader.strFilePath = getAbsPath( a_this.strTopicTablePath, topictablefilename );
			a_Context.push( a_this.loadFromFile, a_this,
						a_this.processReaderResult, a_this,
						a_this.parseTopicInfo, a_this );
		}
		else
		{
			a_Context.push( a_this.processReaderResult, a_this,
						a_this.parseTopicInfo, a_this );
		}
	}
	
	this.makeIndexMap = function( a_Context, a_this )
	{
		a_this.topicMap = new Array() ;
		var prev = 0 ;
		for (var i = 0; i < theXmlReader.getNumOfNodes() ; i++)
		{
			a_this.topicMap[i] = theXmlReader.getNumOfTopics(i) + prev;
			prev = a_this.topicMap[i] ;
		}		
	}
	
	this.prepareMap  = function( a_Context, a_this )
	{
		a_this.bSucc = true;
		theXmlReader.strFilePath =  a_this.strTopicTablePath;
		a_Context.push( a_this.loadFromFile, a_this,
						a_this.makeIndexMap, a_this);
	}
}

// HuginDatabase.js-----------------------------

function HuginDatabase()
{
	this.strOdbPath = "";			//in

	this.queryWord = null;			//in
	this.bNeedStopWord = false;		//in
	this.recordResult = null;		//out
	this.eType = null ;             //in
	this.aQueryTopics = null;		//in out

	this.bSucc = false;
	this.bInited = false;
	
	this.strTopicTablePath = null;
	this.strPackageIndexPath = null;
	this.strSynonymPath = null ;
	this.packageInfo = null;
	
	this.iCurTopic = null;

	this.packageIndexReader = new HuginPackageIndexReader();
	this.packageReader = new HuginPackageReader();
	this.topicReader = new HuginTopicTableReader();

	this.prepareQuery = function()
	{
		this.recordResult = null;
		this.bSucc = true;
		
		this.packageInfo = null;
		
		this.iCurTopic = 0;

		this.packageIndexReader.prepareQuery();
		this.packageReader.prepareQuery();
		this.topicReader.prepareQuery();
		this.eType = ESNT_DEFAULT ;
	}

	this.getIndexUrl = function( a_strIndexType )
	{
		var strRelPath = theXmlReader.getAttr( "index", "type", a_strIndexType, "url" );
		if ( strRelPath == "" )
			return "";

		return getAbsPath( this.strOdbPath, strRelPath );
	}

	this.readOdbInfo = function( a_Context, a_this )
	{
		if ( !theXmlReader.bSucc ||
			 !theXmlReader.checkRoot( "odb" ) )
		{
			a_this.bSucc = false;
			a_this.bInited = false;
			return;
		}
					
		a_this.strTopicTablePath = a_this.getIndexUrl( "TopicIndex" );
		a_this.strPackageIndexPath = a_this.getIndexUrl( "PackageIndex" );
		a_this.strSynonymPath = a_this.getIndexUrl( "Synonym" );

		if ( a_this.strTopicTablePath == "" || a_this.strPackageIndexPath == ""  || a_this.strSynonymPath == "")
		{
			a_this.bSucc = false;
			a_this.bInited = false;
			return;
		}
		else
		{
			a_this.bSucc = true;
			a_this.bInited = true;
		}
	}

	this.loadFromFile = function( a_Context, a_this, a_funcCallback )
	{
		theXmlReader.loadFromFile( a_funcCallback );
	}

	this.queryRecordInPackage = function( a_Context, a_this )
	{
		if ( !a_this.packageIndexReader.bSucc )
			return;

		a_this.packageInfo = a_this.packageIndexReader.packageInfo;
		a_this.packageReader.strPackagePath = a_this.packageInfo;
		a_this.packageReader.strSynonymPath = a_this.strSynonymPath ;
		a_this.packageReader.bPhraseSearch = (a_this.eType == ESNT_PHRASE);
		if (gsSubstrSrch)
			a_this.packageReader.strQueryWord = a_this.queryWord.strNormalizedOrg;
		else
			a_this.packageReader.strQueryWord = a_this.queryWord.strHelStem;		//use stem in case substring search is off
		a_Context.push( a_this.packageReader.query, a_this.packageReader );
	}
	
	this.makeResult = function( a_Context, a_this )
	{
		if ( !a_this.packageIndexReader.bSucc ||
			 !a_this.packageReader.bSucc )
		{
			a_this.bSucc = false;
		}
		a_this.recordResult = a_this.packageReader.recordResult;
		if (a_this.eType == ESNT_NOT)
		{
            if ((typeof(a_this.recordResult)=='undefined')||(a_this.recordResult==null))
            {
                a_this.bSucc = true ;
                a_this.recordResult = a_this.makeDummyResultRec(a_this.queryWord.strNormalizedOrg,a_this.queryWord.strHelStem) ;
            }
            theXmlReader.strFilePath = a_this.strTopicTablePath;
            a_Context.push( a_this.loadFromFile, a_this,
						a_this.makeNotResult, a_this );            
		}    
	}
	
	this.makeDummyResultRec = function(strOrg, strStem)
	{
	    var recordResult = new Object();
	    recordResult.strRecord = "" ;
	    recordResult.strNefStem = strStem ;
	    recordResult.bStopWord = false ;
	    return recordResult ;
	}
	
	this.makeNotResult = function( a_Context, a_this )
	{
	    var topicRecs = a_this.recordResult.strRecord.split( "|" );
	    var bIncludeAll = (a_this.recordResult.strRecord == "" );
        var arrTopicIds = new Array();
        var j ;            
        for(j=0;j<topicRecs.length;j++)
        {
            var pos = topicRecs[j].indexOf(",");
            if(pos != -1)
                arrTopicIds[arrTopicIds.length] = topicRecs[j].substring(0,pos);
        }   
        var bCheck = false ;
        if(arrTopicIds.length > 0)
            bCheck = true ;
        var curIndex = 0 ;         
        var sDummyTopicRec = ",192:0,0,10" ;		
        var i = 0;
        var numTopics = 0 ;
		for (var k =0 ; k < theXmlReader.getNumOfNodes() ; k++)
		{
			numTopics += theXmlReader.getNumOfTopics(k);
		}
        a_this.recordResult.strRecord = '' ;
        while(i < numTopics)
        {
            var bIncludeTopic = true ;
            if (bCheck && (curIndex < arrTopicIds.length)&&(arrTopicIds[curIndex]==i) && (!bIncludeAll))
            {
                curIndex++ ;
                bIncludeTopic = false ;
            }
            if(bIncludeTopic)
            {
                a_this.bSucc = true ;
                var topicRec = i + sDummyTopicRec ;
                if (a_this.recordResult.strRecord == '')
                    a_this.recordResult.strRecord = topicRec ;
                else
                    a_this.recordResult.strRecord += "|" + topicRec ;
            }
            i++ ;
        }            
	}
	
	this.init = function( a_Context, a_this )
	{
		theXmlReader.strFilePath = a_this.strOdbPath;
		a_Context.push( a_this.loadFromFile, a_this,
						a_this.readOdbInfo, a_this );
	}

	this.queryRecord = function( a_Context, a_this )
	{
		if ( !a_this.bInited )
		{
			a_this.bSucc = false;
			return;
		}
		a_this.bSucc = true;

		a_this.packageIndexReader.strPackageIndexPath = a_this.strPackageIndexPath;
		if (gsSubstrSrch)
			a_this.packageIndexReader.strCurQuery = a_this.queryWord.strNormalizedOrg;
		else
			a_this.packageIndexReader.strCurQuery = a_this.queryWord.strHelStem;	//use stem in case substring search is off
		a_Context.push( a_this.packageIndexReader.queryPackageInfo, a_this.packageIndexReader,
						a_this.queryRecordInPackage, a_this,
						a_this.makeResult, a_this )
	}
	
	this.processTopicInfo = function( a_Context, a_this )
	{
		if ( !a_this.topicReader.bSucc )
		{
			a_this.bSucc = false;
			return;
		}
		a_this.aQueryTopics[a_this.iCurTopic].strUrl = a_this.topicReader.topicInfo.strUrl;
		a_this.aQueryTopics[a_this.iCurTopic].strTitle = a_this.topicReader.topicInfo.strTitle;
		a_this.aQueryTopics[a_this.iCurTopic].strSummary = a_this.topicReader.strTopicContext;
	}
	
	this.incCurTopic = function( a_Context, a_this )
	{
		a_this.iCurTopic++;
	}

	this.queryTopicInfo = function( a_Context, a_this )
	{
		if ( a_this.iCurTopic >= a_this.aQueryTopics.length )
			return;

		a_this.topicReader.nQueryId = a_this.aQueryTopics[a_this.iCurTopic].nTopicId;
		a_Context.push( a_this.topicReader.queryTopicInfo, a_this.topicReader,
						a_this.processTopicInfo, a_this,
						a_this.incCurTopic, a_this,
						a_this.queryTopicInfo, a_this );
	}

	this.queryTopicInfos = function( a_Context, a_this )
	{
		if ( !a_this.bInited )
		{
			a_this.bSucc = false;
			return;
		}
		a_this.bSucc = true;

		a_this.topicReader.strTopicTablePath = a_this.strTopicTablePath;
		a_this.iCurTopic = 0;						//this is an iterator of a "for" loop
		a_this.topicReader.lastIndex = -1;
		a_Context.push( a_this.topicReader.prepareMap , a_this.topicReader,
						a_this.queryTopicInfo, a_this );
	}
}

// LanguageService.js---------------------------

function LanguageService()
{
	this.getNormalizedOrg = function( a_strOrg, a_Result )
	{
		var strUpper = a_strOrg.toUpperCase();
		var strLower = a_strOrg.toLowerCase();

		if ( utf8Compare(strUpper, strLower) == 0 || utf8Compare(strUpper, a_strOrg) != 0 )
		{
			a_Result.strNormalizedOrg = strLower;
			a_Result.bUpperCase = false;
		}
		else
		{
			a_Result.strNormalizedOrg = strUpper;
			a_Result.bUpperCase = true;
		}
	}
	this.stemWith = function( a_strWord, a_strSuffix )
	{
		var s = a_strSuffix.split( "," );
		var strSuffix = s[0];
		var bRemoveOnly = ( s[1] == '1' );
		
		var ss = a_strWord.match( "^..+" + strSuffix + "$" );
		if ( ss == null )
			return null;

		var nLenRest = a_strWord.length - strSuffix.length;
		var bAddE = false;
		if ( !bRemoveOnly )
		{
			if ( !this.isVowel( a_strWord.charAt( nLenRest - 1 ) ) )
			{
				if ( a_strWord.charAt( nLenRest - 1 ) == a_strWord.charAt( nLenRest - 2 ) )
					nLenRest--;
				else
					bAddE = true;
			}
		}
		
		var strStem = a_strWord.substr( 0, nLenRest );
		
		if ( strStem.length < 2 || (( strStem.length == 2) && !bAddE ) )
			return null;

		//if ( strStem.length <= 2 )
			//return null;
			
		return strStem;
	}
	this.helStem = function( a_Result )
	{
		var strWord = a_Result.strNormalizedOrg.toLowerCase();

		var nSuffixNum = g_RunesHelSuffixes.length;
		var nStemFound = 0;
		var strStem = null;
		for ( var i = 0; i < nSuffixNum; i++ )
		{
			strStem = this.stemWith( strWord, g_RunesHelSuffixes[i] );
			if ( strStem != null )
			{
				nStemFound = i + 1;
				break;
			}
		}
		if ( strStem == null )
			strStem = strWord;

		a_Result.strHelStem = strStem;
		a_Result.nHelWordShape = a_Result.bUpperCase ? nStemFound * 2 + 1 : nStemFound * 2;
	}
	this.isVowel = function( a_ch )
	{
		return g_RunesVowels.indexOf( a_ch ) >= 0;
	}
	this.isWordBreak = function( a_ch )
	{
		return ( !this.isQuote( a_ch ) && g_RunesWordBreaks.indexOf( a_ch ) >= 0 );
	}
	this.isWhiteSpace = function( a_ch )
	{
		return ( g_RunesWhiteSpaces.indexOf( a_ch ) >= 0 );
	}
	this.isSpecialBreak = function( a_ch )
	{
		return ( g_RunesSpecialBreaks.indexOf( a_ch ) >= 0 );
	}
	this.isQuote = function( a_ch )
	{
		return ( a_ch == g_RunesQuote );
	}
	this.isAND = function( a_strOp )
	{	return ( a_strOp == "and" );	}
	this.isOR = function( a_strOp )
	{	return ( a_strOp == "or" );		}
	this.isNOT = function( a_strOp )
	{	return ( a_strOp == "not" );	}
	this.isOperator = function( strOp )
	{	if ( strOp == "and" ||
			 strOp == "or" ||
			 strOp == "not" )
			return true;
	}
}

// Runes.js----------------------------------

var	ESNT_AND		= 1;
var	ESNT_OR			= 2;
var	ESNT_NOT		= 3;
var	ESNT_DEFAULT	= 4;
var	ESNT_PHRASE		= 5;

function RunesContext( a_strSrc )
{
	this.strSrc = a_strSrc;
	this.nCur = 0;
	this.bFailed = false;
	this.bNot = false;
	this.nWordIndex = 0;
	
	this.getCurChar = function()
	{
		return this.strSrc.charAt( this.nCur );
	}
	this.getChar = function( i )
	{
		return this.strSrc.charAt( i );
	}
	this.reachEnd = function()
	{
		return this.nCur >= this.strSrc.length;
	}
}

function DolWord( a_strWord, a_nPosition )
{
	this.strWord		= a_strWord;
	this.nPosition		= a_nPosition;
}

function SolNode(){}

function RunesService()
{
	this.langSev = new LanguageService();
	
	this.isOperator = function( a_str, a_nFrom )
	{
		var strOp = this.getWord( a_str, a_nFrom ).toLowerCase();

		if ( this.langSev.isOperator( strOp ) )
			return true;

		return false;
	}
	
	this.getLengthOfWordBreak = function( a_str, a_nFrom )
	{
		var i = a_nFrom, nLen = a_str.length;
		while ( i < nLen && this.langSev.isWordBreak( a_str.charAt( i ) ) )
			i++;
		return i - a_nFrom;
	}

	this.getLengthOfWord = function( a_str, a_nFrom )
	{
		var i = a_nFrom, nLen = a_str.length;
		while ( i < nLen &&
				!this.langSev.isWordBreak( a_str.charAt( i ) ) &&
				!this.langSev.isQuote( a_str.charAt( i ) ) )
			++i;
		return i - a_nFrom;
	}

	this.getWord = function( a_str, a_nFrom )
	{
		var nLen = this.getLengthOfWord( a_str, a_nFrom );
		return a_str.substr( a_nFrom, nLen );
	}
	
	this.getPositionInc = function( a_str, a_nFrom )
	{
		var i = a_nFrom, nLen = a_str.length, nInc = 1;
		while ( i < nLen && this.langSev.isWordBreak( a_str.charAt( i ) ) )
		{
			if ( !this.langSev.isWhiteSpace( a_str.charAt( i ) ) )
				nInc++;

			i++;
		}
		return nInc;
	}

	this.getTerm = function( a_Context, a_Rslt )
	{
		if ( this.langSev.isQuote( a_Context.getCurChar() ) )
		{
			a_Context.nCur++;

			var nLen = this.getLengthOfPhrase( a_Context.strSrc, a_Context.nCur );
			if ( nLen <= 0 )
				return false;

			a_Rslt.eType = ESNT_PHRASE;
			a_Rslt.strTerm = a_Context.strSrc.substr( a_Context.nCur, nLen );
			a_Context.nCur += nLen + 1;
		}
		else
		{
			var nLen = this.getLengthOfDefault( a_Context.strSrc, a_Context.nCur );
			if ( nLen <= 0 )
				return false;

			a_Rslt.eType = ESNT_DEFAULT;
			a_Rslt.strTerm = a_Context.strSrc.substr( a_Context.nCur, nLen );
			a_Context.nCur += nLen;
		}

		return true;
	}

	this.getOperator = function( a_Context, a_Rslt )
	{
		if ( a_Context.reachEnd() )
			return false;

		var strOp = this.getWord( a_Context.strSrc, a_Context.nCur ).toLowerCase();

		if ( this.langSev.isAND( strOp ) )
		{
			a_Rslt.eType = ESNT_AND;
			a_Context.nCur += strOp.length;
		}
		else if ( this.langSev.isOR( strOp ) )
		{
			a_Rslt.eType = ESNT_OR;
			a_Context.nCur += strOp.length;
		}
		else if ( this.langSev.isNOT( strOp ) )
		{
			a_Rslt.eType = ESNT_NOT;
			a_Context.nCur += strOp.length;
		}
		else
		{
			a_Rslt.eType = ESNT_OR;
		}
		
		return true;
	}

	this.getLengthOfPhrase = function( a_str, a_nFrom )
	{
		var i = a_nFrom, nLen = a_str.length;
		while ( i < nLen )
		{
			if ( this.langSev.isQuote( a_str.charAt( i ) ) )
				return i - a_nFrom;
			++i;
		}
		return -1;
	}

	this.getLengthOfDefault = function( a_str, a_nFrom )
	{
		var i = a_nFrom, nLen = a_str.length;
		while ( i < nLen &&
				!this.isOperator( a_str, i ) &&
				!this.langSev.isQuote( a_str.charAt( i ) ) )
		{
			i += this.getLengthOfWord( a_str, i );
			i += this.getLengthOfWordBreak( a_str, i );
		}
		return i - a_nFrom;
	}

	this.parseOperator = function( a_Context, a_Result, a_bNotAllowed )
	{
		a_Context.nCur += this.getLengthOfWordBreak( a_Context.strSrc, a_Context.nCur );

		var rslt = new Object;
		if ( !this.getOperator( a_Context, rslt ) )
			return false;

		if ( rslt.eType == ESNT_NOT )
		{
			if (a_bNotAllowed)
			{
			    if ( a_Context.bNOT )
			    {
				    rslt.eType = ESNT_OR;
			    }
			    else
			    {
				    a_Context.bNOT = true;
			    }
			}
			else
			{
			    a_Context.bFailed = true;
			    return false ;			
			}
		}
		a_Result.eType = rslt.eType;
		a_Result.right = new SolNode();
		if ( !this.parseTerm( a_Context, a_Result.right ) )
			return false;

		return true;
	}

	this.parseTerm = function( a_Context, a_Result )
	{
		a_Context.nCur += this.getLengthOfWordBreak( a_Context.strSrc, a_Context.nCur );

		var rslt = new Object;
		if ( !this.getTerm( a_Context, rslt ) )
		{
			if (( this.parseOperator( a_Context, rslt, true ) )&&(rslt.eType == ESNT_NOT))
		    {
		        a_Result.eType = rslt.eType;
		        if (rslt.right.eType == ESNT_DEFAULT)
		        {
			        a_Result.strTerm = rslt.right.strTerm;	
			        return true ;		    
			    }
			    else
			    {
			        a_Context.bFailed = true;
			        return false;
			    }
		    }
		    else
		    {
		        a_Context.bFailed = true;
			    return false;
		    }			
		}

		if ( this.parseOperator( a_Context, a_Result, false ) )
		{
			a_Result.left = new SolNode();
			a_Result.left.eType = rslt.eType;
			a_Result.left.strTerm = rslt.strTerm;
		}
		else
		{
			a_Result.eType = rslt.eType;
			a_Result.strTerm = rslt.strTerm;
		}
		
		return true;
	}

	this.extractTerm = function( a_Context, a_Term )
	{
		a_Term.aWords = new Array();
		this.dolSegment( a_Term.strTerm, a_Term.aWords );

		if ( a_Term.aWords.length == 0 )
			return false;

		for ( var i = 0; i < a_Term.aWords.length; i++ )
		{
			a_Term.aWords[i].nWordId = a_Term.aWords[i].nPosition + a_Context.nWordIndex;
		}
		a_Context.nWordIndex = a_Term.aWords[a_Term.aWords.length - 1].nWordId + 1;
		return true;
	}

	this.parsePhraseAndDefault = function( a_Context, a_Node )
	{
		if ( a_Node.eType == ESNT_PHRASE || a_Node.eType == ESNT_DEFAULT || a_Node.eType == ESNT_NOT)
		{
			if ( !this.extractTerm( a_Context, a_Node ) &&
				 a_Node.eType == ESNT_PHRASE )
				a_Context.bFailed = true;
		}
		else
		{
			this.parsePhraseAndDefault( a_Context, a_Node.left );
			this.parsePhraseAndDefault( a_Context, a_Node.right );
		}
	}

	this.helStem = function( a_strOrg, a_Result )
	{
		this.langSev.getNormalizedOrg( a_strOrg, a_Result );
		this.langSev.helStem( a_Result );
	}
	
	this.dolSegment = function( a_strSrc, a_Result )
	{
		var nLen = a_strSrc.length;
		var nCur = 0;
		var nPosition = 1;
		var strWord = "";

		nCur += this.getLengthOfWordBreak( a_strSrc, nCur );
		if ( nCur > 0 )
		{
			//search query begins with break chars; check for special break chars
			var nTemp = 0 ;
			while ( nTemp < nCur )
			{
				if ( this.langSev.isSpecialBreak( a_strSrc.charAt( nTemp ) ) )
				{
					//it's a special word break, include it in search
					a_Result[a_Result.length] = new DolWord( a_strSrc.charAt( nTemp ), nPosition );
					nPosition++;
				}
				nTemp++ ;
			}
		}
		while ( nCur < nLen )
		{
			strWord = this.getWord( a_strSrc, nCur );
			a_Result[a_Result.length] = new DolWord( strWord, nPosition );
			
			nCur += strWord.length;
			nPosition++ ;
			
			//check if we can find some special break characters in between this and next word
			while ( nCur < nLen && this.langSev.isWordBreak( a_strSrc.charAt( nCur ) ) )
			{
				if ( this.langSev.isSpecialBreak( a_strSrc.charAt( nCur ) ) )
				{
					//it's a special word break, include it in search
					a_Result[a_Result.length] = new DolWord( a_strSrc.charAt( nCur ), nPosition );
					nPosition++;
				}
				nCur++;
			}
			
			//nPosition += this.getPositionInc( a_strSrc, nCur );
			//nCur += this.getLengthOfWordBreak( a_strSrc, nCur );
		}
	}
	
	this.solParse = function( a_strSrc, a_Result )
	{
		var context = new RunesContext( a_strSrc );
		this.parseTerm( context, a_Result );

		if ( context.bFailed )
			return false;
			
		this.parsePhraseAndDefault( context, a_Result );
		if ( context.bFailed )
			return false;

		return true;
	}
}

// HuginInput.js--------------------------------

function _helStemNode( a_Runes, a_Node )
{
	with ( a_Node )
	{
		if ( eType == ESNT_PHRASE || eType == ESNT_DEFAULT || eType == ESNT_NOT)
		{
			for ( var i = 0; i < aWords.length; i++ )
			{
				a_Runes.helStem( aWords[i].strWord, aWords[i] )
			}
		}
		else
		{
			_helStemNode( a_Runes, left );
			_helStemNode( a_Runes, right );
		}
	}
}

function parseQueryExpression( a_strQuery )
{
	var runes = new RunesService();
	var expression = new SolNode();
	if ( !runes.solParse( a_strQuery, expression ) )
		return null;
		
	_helStemNode( runes, expression )
	
	return expression;
}


// RankingCalculator.js-------------------------

var	EWMT_NotMatch			= 0;
var	EWMT_SynonymMatch		= 1;
var	EWMT_WordMatch			= 2;
var	EWMT_ShapeMatch			= 3;

var WEIGHT_OF_SHAPE_MATCH			= 0.5;
var WEIGHT_OF_SINGLE_WORD_SCORE		= 0.5;
var HUGIN_KEYWORD_FLAG				= 0x0040;
var HUGIN_TITLE_FLAG				= 0x0080;
var WORDSHAPE_SYNONYM				= -2;

function _rank_ULaw( a_fX )
{
	if ( a_fX < 0.0 )
		return 0.0;

	return 1.0 - 1.0 / ( a_fX + 1.0 );
}

function _rank_Weaken( a_fWeight, a_fPercent )
{
	var fPercent = ( a_fPercent < 0.0 ) ? 0.0 : 
				   ( a_fPercent > 1.0 ) ? 1.0 : a_fPercent;

	return 1 - fPercent + a_fWeight * fPercent;
}

function _isKeyWord( a_uEmphasis )
{
	return ( a_uEmphasis & HUGIN_KEYWORD_FLAG ) != 0;
}

function _isTitle( a_uEmphasis )
{
	return ( a_uEmphasis & HUGIN_TITLE_FLAG ) != 0;
}

function _isUpperCaseShape( a_nWordShape )
{
	return a_nWordShape % 2 != 0;
}

function _emphasisToScore( a_uEmphasis )
{
	var nScore = 0;

	//H1 = 64, H2 = 32, H3 = 16, H4 = 8, H5 = 4, H6 = 2
	for ( var i = 5, nInc = 2; i >= 0; i--, nInc *= 2 )
		nScore += nInc * ( ( a_uEmphasis >> i ) & 1 );

	return nScore;
}

function _getWordMatchType( a_Word, a_Tile, a_nPosition, a_nOffset )
{
	var eRslt = EWMT_NotMatch;

	// The term must be consecutive.
	if ( a_nPosition - a_nOffset != a_Word.nWordId )
		return eRslt;

	for ( var i = 0; i < a_Tile.aWords.length; i++ )
	{
		var eCur = EWMT_NotMatch;
		if ( a_Tile.aWords[i].nWordId == a_Word.nWordId )
		{
			if ( a_Tile.aWords[i].nWordForm == WORDSHAPE_SYNONYM )
				eCur = EWMT_SynonymMatch;
			else
			{
				if ( _isUpperCaseShape( a_Word.nWordShape ) )
				{
					if ( a_Word.nWordShape == a_Tile.aWords[i].nWordForm )
						eCur = EWMT_ShapeMatch;
					else
						eCur = EWMT_NotMatch;
				}
				else
				{
					if ( a_Word.nWordShape == a_Tile.aWords[i].nWordForm ||
						 a_Word.nWordShape == a_Tile.aWords[i].nWordForm - 1 )
						eCur = EWMT_ShapeMatch;
					else
						eCur = EWMT_WordMatch;
				}
			}
		}
		if ( eRslt < eCur )
			eRslt = eCur;
	}
	return eRslt;
}

function _getTermMatchType( a_aTiles, a_nTileFrom, a_aWords, a_nFrom, a_nLen )
{
	var eRslt = EWMT_ShapeMatch;

	if ( a_nFrom < 0 || a_nLen <= 0 || a_aWords.length < a_nFrom + a_nLen )
		return EWMT_NotMatch;

	var nOffset = a_nTileFrom ;

	var j = a_nTileFrom;
	for ( var i = a_nFrom; i < a_nFrom + a_nLen; i++  )
	{		
		nOffset = nOffset - a_aWords[i].nWordId
		j = nOffset + a_aWords[i].nWordId;	//seek j to the tile that should be matched
		
		if ( !a_aTiles[j] )
			return EWMT_NotMatch;
	
		var eCur = _getWordMatchType( a_aWords[i], a_aTiles[j], j, nOffset );
		if ( eCur < eRslt )
			eRslt = eCur;
		if ( eRslt == EWMT_NotMatch )
			return eRslt;
	    nOffset = nOffset + a_aWords[i].strWord.length + a_aWords[i].nWordId ;
	}
	return eRslt;
}

function _matchTypeToScore( a_eMatchType )
{
	switch( a_eMatchType )
	{
	case EWMT_NotMatch:			return 0;
	case EWMT_SynonymMatch:		return 1;
	case EWMT_WordMatch:		return 2;
	case EWMT_ShapeMatch:		return 4;
	default:					return 0;
	}
}

function _computeSingleWordScore( a_TopicImage, a_nWordId )
{
	if ( !a_TopicImage.aWords[a_nWordId] )
		return 0.0;

	var emphasis = a_TopicImage.aWords[a_nWordId].uEmphasis ;
	if (emphasis != 0 )
	    emphasis = _emphasisToScore(emphasis ) ;
		
	emphasis += a_TopicImage.aWords[a_nWordId].uFreq; 	
	    
	var fWeightScore = _rank_Weaken( _rank_ULaw( emphasis ), WEIGHT_OF_SINGLE_WORD_SCORE );

	if ( _isTitle( a_TopicImage.aWords[a_nWordId].uEmphasis ) )		// Words in title are important than key words and the rest.
	{
		return fWeightScore / 3.0 + 2.0 / 3.0;
	}
	if ( _isKeyWord( a_TopicImage.aWords[a_nWordId].uEmphasis ) )	// Key words are always more important than non-keywords.
	{
		return fWeightScore / 3.0 + 1.0 / 3.0;
	}	
	else
	{
		return fWeightScore / 3.0;
	}
}

function _computeTermWeight( a_TopicImage, a_Term )
{
	var nTermLen = a_Term.aWords.length;		// empty term means ignored or all stop words
	if ( nTermLen == 0 )
		return -1.0;

	var fTermScore = 0.0;						// position independent score
	for ( i = 0; i < nTermLen; i++ )
		fTermScore += _computeSingleWordScore( a_TopicImage, a_Term.aWords[i].nWordId );
	if ( a_Term.eType == ESNT_PHRASE )
	{
		//check if its a phrase
		var bPhrase = false ;
		var iPosition = 0;			
		for ( var strPosition in a_TopicImage.aTiles )
		{
			iPosition = parseInt( strPosition );
			bPhrase = _getPhraseMatch( a_TopicImage.aTiles, iPosition, a_Term.aWords, 0 );
			if ( bPhrase )
				break ;
		}
		
		if (bPhrase)
			return fTermScore;
		else
			return 0.0 ;
	}
	else
	{		
		return fTermScore;
	}
}

function _getPhraseMatch(a_aTiles, iPosition, a_aWords, a_nCurIdx )
{
	var nOffset = iPosition ;
	if (a_nCurIdx >= a_aWords.length)
		return false ;
	if (iPosition >= a_aTiles.length)
		return false ;
	var nCurWordId = a_aWords[a_nCurIdx].nWordId ;
	if(!a_aTiles[iPosition])
		return false ;
		
	var i  ;
	for ( i = 0 ; i < a_aTiles[iPosition].aWords.length ; i++)
	{
		var wordAtPos = a_aTiles[iPosition].aWords[i].nWordId	;
	if (wordAtPos == nCurWordId)
	{
		if (a_nCurIdx == (a_aWords.length -1 ))
		{
			//last word matches, then return true
			return true ;
		}
		else
		{
			var wordLen = a_aWords[a_nCurIdx].strWord.length ;
			return _getPhraseMatch(a_aTiles , iPosition + wordLen , a_aWords , a_nCurIdx+1);
		}
	}
	}
		return false ;
}

function _removeNegativeWeight( a_fWeight, a_eOpType )
{
	if ( a_fWeight >= 0.0 )
		return a_fWeight;

	switch ( a_eOpType )
	{
	case ESNT_OR:	return 0.0;
	case ESNT_AND:	return 1.0;
	case ESNT_NOT:	return 0.0;
	}
	return 0.0;
}

function _getWeightOfNode( a_TopicImage, a_Node )
{
	if ( a_Node == null )
		return 0.0;

	if ( a_Node.eType == ESNT_DEFAULT || a_Node.eType == ESNT_PHRASE || a_Node.eType == ESNT_NOT)
	{
		return _computeTermWeight( a_TopicImage, a_Node );
	}
	else
	{
		// Right has only 1/2 weight of left
		var fWeightRight = _getWeightOfNode( a_TopicImage, a_Node.right ) / 2.0;
		var fWeightLeft = _getWeightOfNode( a_TopicImage, a_Node.left );
		
		// To both negativeWeight return negative
		if ( fWeightRight < 0.0 && fWeightLeft < 0.0 )
			return -1.0;

		// Convert NegativeWeight to 1.0 or 0.0 according to operator type
		fWeightRight = _removeNegativeWeight( fWeightRight, a_Node.eType );
		fWeightLeft = _removeNegativeWeight( fWeightLeft, a_Node.eType );

		// Boolean operation
		switch ( a_Node.eType )
		{
		case ESNT_OR:
			return ( fWeightLeft + fWeightRight ) / 2.0;
		case ESNT_AND:
			return ( fWeightLeft * fWeightRight );
		case ESNT_NOT:
			fWeightRight = ( fWeightRight == 0.0 ) ? 1.0 : 0.0;
			return fWeightLeft * fWeightRight;
		}
		
		// Uncoverd cases (inexistent).
		return 0.0;
	}
}

function calculateRanking( a_TopicImage, a_Expression )
{
	return _getWeightOfNode( a_TopicImage, a_Expression );
}

// HuginHunter.js-------------------------------

function arrayRemoveAt( a_ary, a_nIndex )
{
	var nLen = a_ary.length;
	for ( var i = a_nIndex; i < nLen - 1; i++ )
		a_ary[i] = a_ary[i + 1];
	a_ary.length--;
}

function HuginQueryResult()
{
	this.aTopics = new Array();	
}

function HuginImageWord()
{
	this.uEmphasis = 0;	
	this.uFreq     = 0;
	
};

function HuginImageTileWord( a_nWordId, a_nWordForm )
{
	this.nWordId = a_nWordId;
	this.nWordForm = a_nWordForm;
}

function HuginImageTile()
{
	this.aWords = new Array();
}

function HuginTopicImage( a_nTopicId )
{
	this.aWords = new Array();
	this.aTiles = new Array();;
}

function HuginHunter()
{
	this.aOdbPathes = null;				//in
	this.strOdbPath = null;				//in
	
	this.strQuery = null;				//in
	this.queryResult = null;			//out
	
	this.bInited = false;
	this.bSucc = true;
	
	this.aDatabases = null;

	this.iCurProj = null;
	
	this.queryExpression = null;
	this.queryWord = null;
	this.curTermNode = null;

	this.aRecordTable = null;	
	this.aSuspendTopics = null;
	this.aTopics = null;
	this.iCurTopic = null;
	
	this.aTopicImages = null;
	this.aNodeStack = null;
	this.iCurTermNodeWord = null;

	this.aPossibleOrgs = null;
	this.aRankedTopics = null;
	
	this.nWordLoaded = 0;
	this.nWordNum = 0;
	this.nState = 0;
	this.nProgress = 0;

	this.prepareQuery = function()
	{
		this.queryResult = null;
		this.bSucc = true;
		
		this.iCurProj = 0;
		this.queryExpression = null;
		this.queryWord = null;
		this.curTermNode = null;

		this.aRecordTable = null;	
		this.aSuspendTopics = null;
		this.aTopics = null;
		this.iCurTopic = 0;

		this.aTopicImages = null;
		this.aNodeStack = null;
		this.iCurTermNodeWord = 0;

		this.aPossibleOrgs = null;
		this.aRankedTopics = null;

		this.nWordLoaded = 0;
		this.nWordNum = 0;
		this.nState = 0;
		this.nProgress = 0;

		for ( var i in this.aDatabases )
			this.aDatabases[i].prepareQuery();
	}
	
	this.updateProgress = function()
	{
		var fProgress = 100 * this.iCurProj / this.aDatabases.length;
		var fBase = 100 / this.aDatabases.length;

		if ( this.nState == 1 )
			this.nProgress = Math.round( fProgress + ( this.nWordLoaded / this.nWordNum ) * ( fBase / 3 ) );
		else if ( this.nState == 2 )
			this.nProgress = Math.round( fProgress + fBase / 3 +
										 ( this.iCurTopic / this.aTopics.length ) * ( fBase * 2 / 3 ) );
	}

	this.incCurProjForInit = function( a_Context, a_this )
	{
		a_this.iCurProj++;
		if ( a_this.iCurProj < a_this.aDatabases.length )
		{
			a_Context.push( a_this.aDatabases[a_this.iCurProj].init, a_this.aDatabases[a_this.iCurProj],
							a_this.incCurProjForInit, a_this );
		}
	}
	
	this.incCurProjForEvaluate = function( a_Context, a_this )
	{
		a_this.iCurProj++;
		if ( a_this.iCurProj < a_this.aDatabases.length )
		{
			a_this.aRankedTopics[a_this.iCurProj] = new Array();
			a_Context.push( a_this.getRecords, a_this,
							a_this.evaluateTopics, a_this,
							a_this.getTopicInfo, a_this,
							a_this.incCurProjForEvaluate, a_this );
		}
	}
	
	this.incCurTermNodeWord = function( a_Context, a_this )
	{
		a_this.nState = 1;

		a_this.iCurTermNodeWord++;
		a_this.nWordLoaded++;
		a_this.updateProgress();
	}

	this.incCurTopic = function( a_Context, a_this )
	{
		a_this.nState = 2;

		a_this.iCurTopic++;
		a_this.updateProgress();
	}
	
	this.checkInitSucc = function( a_Context, a_this )
	{
		var bAllFailed = true;
		var bNotAllDatabaseInited = false;
		a_this.bInited = false;
		for ( var i = 0; i < a_this.aDatabases.length; i++ )
		{
			if ( !a_this.aDatabases[i].bSucc || !a_this.aDatabases[i].bInited )
				bNotAllDatabaseInited = true;
			else
				bAllFailed = false;
		}
		if ( bAllFailed )
		{
			a_Context.strMsg = gsInitDatabaseFailed;
			g_CurState = ECS_FATALERROR;
			updateResultView();
			return;
		}
		if ( bNotAllDatabaseInited )
		{
			a_Context.strMsg = gsInitDatabaseFailed;
		}
		a_Context.strMsg = "";
		g_CurState = ECS_FTSREADY;
		a_this.bInited = true;
	}

	this.getWordImageToAdd = function( a_nWordId, a_aWords )
	{
		return a_aWords[a_nWordId] ? a_aWords[a_nWordId] : ( a_aWords[a_nWordId] = new HuginImageWord() );
	}
	
	this.getTileImageToAdd = function( a_nPosition, a_aTiles )
	{
		return a_aTiles[a_nPosition] ? a_aTiles[a_nPosition] : ( a_aTiles[a_nPosition] = new HuginImageTile() );
	}

	this.addRecordToRecordTable = function( a_nWordId, a_strRecord )
	{
		if ( a_strRecord == "" )
			return;

		if(typeof(this.aRecordTable[a_nWordId]) == "undefined")
			this.aRecordTable[a_nWordId] = new Array();
		
		var aTopics = a_strRecord.split( "|" );
		for ( var iTopic = 0; iTopic < aTopics.length; iTopic++ )
		{
			var topicHead = aTopics[iTopic].match( "^(\\d+),(.*)$" );
			if ( topicHead == null )
				continue;
			this.aRecordTable[a_nWordId][topicHead[1]] = topicHead[2];
			this.aSuspendTopics[topicHead[1]] = true;
		}
	}
	
	this.processRecordResult = function( a_Context, a_this )
	{
		if ( !a_this.aDatabases[a_this.iCurProj].bSucc )			// Go on searching for other words while one not found
			return;

		a_this.wordRecord = a_this.aDatabases[a_this.iCurProj].recordResult;
		a_this.addRecordToRecordTable( a_this.queryWord.nWordId, a_this.wordRecord.strRecord );		
	}
	
	this.getRecordOfTermWord = function( a_Context, a_this )
	{
		if ( a_this.iCurTermNodeWord >= a_this.curTermNode.aWords.length )
			return;

		a_this.queryWord = a_this.curTermNode.aWords[a_this.iCurTermNodeWord];
		a_this.aDatabases[a_this.iCurProj].queryWord = a_this.queryWord;
		a_this.aDatabases[a_this.iCurProj].eType = a_this.curTermNode.eType ;
		a_this.aDatabases[a_this.iCurProj].bNeedStopWord = a_this.bNeedStopWord;
		a_Context.push( a_this.aDatabases[a_this.iCurProj].queryRecord, a_this.aDatabases[a_this.iCurProj],
						a_this.processRecordResult, a_this,
						a_this.incCurTermNodeWord, a_this,
						a_this.getRecordOfTermWord, a_this );
	}
	
	this.getRecordOfTermNode = function( a_Context, a_this )
	{
		a_this.aPossibleOrgs = new Array();
		a_this.bNeedStopWord = ( a_this.curTermNode.eType == ESNT_PHRASE );
		a_this.iCurTermNodeWord = 0;					//Init the iterator of a "for loop"
		a_Context.push( a_this.getRecordOfTermWord, a_this);
	}
	
	this.getRecordOfNode = function( a_Context, a_this )
	{
		if ( a_this.aNodeStack.length == 0 )
			return;
			
		var curNode = a_this.aNodeStack[a_this.aNodeStack.length - 1];
		a_this.aNodeStack.length--;

		if ( curNode != null )
		{
			if ( curNode.eType == ESNT_PHRASE || curNode.eType == ESNT_DEFAULT || curNode.eType == ESNT_NOT)
			{
				a_this.curTermNode = curNode;
				a_Context.push( a_this.getRecordOfTermNode, a_this );
			}
			else
			{
				a_this.aNodeStack[a_this.aNodeStack.length] = curNode.right;
				a_this.aNodeStack[a_this.aNodeStack.length] = curNode.left;

				a_Context.push( a_this.getRecordOfNode, a_this,
								a_this.getRecordOfNode, a_this );
			}
		}
	}
	
	this.addToTopicImage = function( a_Image, a_nWordId, a_Record )
	{
		var wordImage = this.getWordImageToAdd( a_nWordId, a_Image.aWords );

		wordImage.uEmphasis |= a_Record.uEmphasis;
		wordImage.uFreq = a_Record.aPositions.length ;
		for ( var strPosition in a_Record.aPositions )
		{
			var tileImage = this.getTileImageToAdd( a_Record.aPositions[strPosition],
														a_Image.aTiles );
			tileImage.aWords[tileImage.aWords.length] = new HuginImageTileWord( a_nWordId, 0 /*nWordShape*/ );			
		}
	}
	
	this.unpackTopicRecord = function( a_strRecord )
	{
		var aShapes = a_strRecord.split( ":" );
		if ( aShapes.length == 0 )
			return null;

		var record = new Object();
		record.uEmphasis = parseInt( aShapes[0] );
		record.aPositions = aShapes[1].split( "," );
		if ( record.aPositions.length < 1 )
			return null;		
		return record;
	}
	
	this.makeTopicImage = function( a_nTopicId )
	{
		var topicImage = new HuginTopicImage();
		for ( var strWordId in this.aRecordTable )
		{
			if ( !this.aRecordTable[strWordId][a_nTopicId] )
				continue;

			var record = this.unpackTopicRecord( this.aRecordTable[strWordId][a_nTopicId] )
			this.addToTopicImage( topicImage, parseInt( strWordId ), record );
		}
		return topicImage;
	}
	
	this.calculateRanking = function( a_TopicImage )
	{
		return calculateRanking( a_TopicImage, this.queryExpression );
	}
	
	this.calculateWordNum = function( a_Node )
	{
		if ( a_Node.eType == ESNT_PHRASE || a_Node.eType == ESNT_DEFAULT || a_Node.eType == ESNT_NOT)
		{
			this.nWordNum += a_Node.aWords.length;
		}
		else
		{
			this.calculateWordNum( a_Node.right );
			this.calculateWordNum( a_Node.left );
		}
	}
	
	this.getRecords = function( a_Context, a_this )
	{
		a_this.aNodeStack = new Array();
		a_this.aNodeStack[a_this.aNodeStack.length] = a_this.queryExpression;
		a_this.aRecordTable = new Array();
		a_this.aSuspendTopics = new Array();
		
		a_this.nWordLoaded = 0;
		a_this.calculateWordNum( a_this.queryExpression );
		
		a_Context.push( a_this.getRecordOfNode, a_this );
	}
	
	this.evaluateTopic = function( a_Context, a_this )
	{
		if ( a_this.iCurTopic >= a_this.aTopics.length )
			return;
		
		var rankedTopic = new Object();
		rankedTopic.fRanking = a_this.calculateRanking( a_this.makeTopicImage( a_this.aTopics[a_this.iCurTopic] ) );
		if ( rankedTopic.fRanking > 0 )
		{
			rankedTopic.nTopicId = parseInt( a_this.aTopics[a_this.iCurTopic] );
			a_this.aRankedTopics[a_this.iCurProj][a_this.aRankedTopics[a_this.iCurProj].length] = rankedTopic;
		}
		a_Context.push( a_this.incCurTopic, a_this,
						a_this.evaluateTopic, a_this );
	}

	this.evaluateTopics = function( a_Context, a_this )
	{
		a_this.aTopics = new Array();
		for ( var iTopic in a_this.aSuspendTopics )
			a_this.aTopics[a_this.aTopics.length] = iTopic;
		
		a_this.iCurTopic = 0;
		a_Context.push( a_this.evaluateTopic, a_this );
	}

	this.createTopicImages = function( a_Context, a_this )
	{
		a_this.aTopicImages = new Array();
		a_this.aPossibleOrgs = new Array();

		a_this.aNodeStack = new Array();
		a_this.aNodeStack[a_this.aNodeStack.length] = a_this.queryExpression;

		a_Context.push( a_this.addNodeToTopicImages, a_this );
	}

	this.calculateRankings = function( a_Context, a_this )
	{
		a_this.aRankedTopics = new Array();
		for ( var strTopicId in a_this.aTopicImages )
		{
			var rankedTopic = new Object();
			rankedTopic.fRanking = calculateRanking( a_this.aTopicImages[strTopicId], a_this.queryExpression );
			if ( rankedTopic.fRanking <= 0 )
				continue;
			rankedTopic.nTopicId = parseInt( strTopicId );
			document.writeln( "<br>" + rankedTopic.nTopicId + "<br>" );
			a_this.aRankedTopics[a_this.aRankedTopics.length] = rankedTopic;
		}
	}
	
	this.compRankedTopics = function( a_itemA, a_itemB )
	{
		if ( a_itemA.fRanking > a_itemB.fRanking )
			return true;
		else if ( a_itemA.fRanking == a_itemB.fRanking )
		{
			var k = compare( a_itemA.strTitle, a_itemB.strTitle );
			if ( k < 0 )
				return true;
		}
		return false;
	}
	
	this.swapRankedTopics = function( a_aTopics, a_nIdx, a_itemNew )
	{
		a_aTopics[a_nIdx] = a_itemNew;
	}
	
	this.quickSortRankedTopics = function( a_nLow, a_nHigh )
	{
		quickSort( this.queryResult.aTopics,
				   0, this.queryResult.aTopics.length - 1,
				   this.compRankedTopics,
				   this.swapRankedTopics );
	}
	
	this.compPossibleOrgs = function( a_itemA, a_itemB )
	{
		if ( utf8Compare(a_itemA, a_itemB) <= 0 )
			return true;
		return false;
	}
	
	this.swapPossibleOrgs = function( a_aOrgs, a_nIdx, a_itemNew )
	{
		a_aOrgs[a_nIdx] = a_itemNew;
	}

	this.getTopicInfo = function( a_Context, a_this )
	{	// the database will fill the query result in the same mapRankedTopics
		a_this.aDatabases[a_this.iCurProj].aQueryTopics = a_this.aRankedTopics[a_this.iCurProj];
		a_Context.push( a_this.aDatabases[a_this.iCurProj].queryTopicInfos, a_this.aDatabases[a_this.iCurProj] );
	}

	this.evaluateExpression = function( a_Context, a_this )
	{
		a_this.queryExpression = parseQueryExpression( a_this.strQuery );
		if ( a_this.queryExpression == null )
		{
			a_Context.strMsg = gsInvalidExpression_1 ;
			a_this.bSucc = false;
			return;
		}

		a_this.aRankedTopics = new Array();
		a_this.iCurProj = -1;
		a_Context.push( a_this.incCurProjForEvaluate, a_this );
	}

	this.makeResult = function( a_Context, a_this )
	{
		a_this.queryResult.aTopics = new Array();
		if ( !a_this.bSucc )
			return;
		var nLen = 0;
		for ( var i = 0; i < a_this.aRankedTopics.length; i++ )
		{
			for ( var j = 0; j < a_this.aRankedTopics[i].length; j++ )
			{
				nLen = a_this.queryResult.aTopics.length;
				a_this.queryResult.aTopics[nLen] = a_this.aRankedTopics[i][j];
				strTopicUrl = a_this.queryResult.aTopics[nLen].strUrl;
				if(!(_isAbsPath(strTopicUrl) || _isRemoteUrl(strTopicUrl)))
				    a_this.queryResult.aTopics[nLen].strUrl = a_this.aProjPathes[i].strProjDir
														  + a_this.queryResult.aTopics[nLen].strUrl;
				a_this.queryResult.aTopics[nLen].fRanking = a_this.aRankedTopics[i][j].fRanking;
			}
		}
		a_this.quickSortRankedTopics( 0, a_this.queryResult.aTopics.length - 1 );
		for ( var i = 0; i < a_this.queryResult.aTopics.length; i++ )
		{
			a_this.queryResult.aTopics[i].nIndex = i + 1;
		}		
	}

	//interface
	this.init = function( a_Context, a_this )
	{
		a_this.bInited = false;
		
		a_this.aDatabases = new Array();
		for ( var i in a_this.aProjPathes )
		{
			var nLen = a_this.aDatabases.length;
			a_this.aDatabases[nLen] = new HuginDatabase();
			a_this.aDatabases[nLen].strOdbPath = a_this.aProjPathes[i].strOdbPath;
		}
		if ( a_this.aDatabases.length == 0 )
			return;

		// for the roboengine server we don't need database inits
		if( gbAIRSSL || mrIsOnEngine()==false )
		{
			a_this.iCurProj = 0;
			a_Context.push( a_this.aDatabases[a_this.iCurProj].init, a_this.aDatabases[a_this.iCurProj],
						a_this.incCurProjForInit, a_this,
						a_this.checkInitSucc, a_this );
		};
	}

	this.query = function( a_Context, a_this )
	{
		if ( !a_this.bInited )
		{
			a_this.bSucc = false;
			return;
		}

		a_this.prepareQuery();
		g_CurState = ECS_SEARCHING;
		updateResultView();
		
		a_this.queryResult = new HuginQueryResult();
		a_Context.push( a_this.evaluateExpression, a_this,
						a_this.makeResult, a_this );
	}
}

//Display/////////////////////////////////////////////////////////////////////

function getEventX( e )
{
	if ( gbIE5 )
		return event.x;
	if ( gbNav7 ||gbAIR)
		return e.pageX;
	return 0;
}

function getEventY( e )
{
	if ( gbIE5 )
		return event.y;
	if ( gbNav7 ||gbAIR)
		return e.pageY;
	return 0;
}

function setInnerHTML( a_Node, a_strText )
{
	a_Node.innerHTML = a_strText;
}

function compByTitleDescend( a_itemA, a_itemB )
{
	var k = compare( getInnerText( a_itemA.cells[0] ), getInnerText( a_itemB.cells[0] ) );
	if ( k > 0 )
		return true;
	else if ( k == 0 )
		return compByRank( a_itemA, a_itemB );
	else
		return false;
}

function compByTitleAscend( a_itemA, a_itemB )
{
	var k = compare( getInnerText( a_itemA.cells[0] ), getInnerText( a_itemB.cells[0] ) );
	if ( k < 0 )
		return true;
	else if ( k == 0 )
		return compByRank( a_itemA, a_itemB );
	else
		return false;

	return getInnerText( a_itemA.cells[0] ) <= getInnerText( a_itemB.cells[0] );
}

function compByRank( a_itemA, a_itemB )
{
	return parseInt( getInnerText( a_itemA.cells[2] ) ) <= parseInt( getInnerText( a_itemB.cells[2] ) );
}

function swapTableNode( a_aItems, a_nIdx, a_itemB )
{
	a_aItems[a_nIdx].swapNode( a_itemB );
}

function getPartition( a_aRows, i, j, a_funcComp, a_funcSwap )
{
	var pivot = a_aRows[i];
	while( i < j )
	{
		while( i < j && a_funcComp( pivot, a_aRows[j] ) )
			j--;
		if( i < j )
			a_funcSwap( a_aRows, i++, a_aRows[j] );
		while( i < j && a_funcComp( a_aRows[i], pivot ) )
			i++;
		if( i < j )
			a_funcSwap( a_aRows, j--, a_aRows[i] );
	}
	a_funcSwap( a_aRows, i, pivot );
	return i;
}

function quickSort( a_aRows, a_nLow, a_nHigh, a_funcComp, a_funcSwap )
{
	if ( a_nLow < a_nHigh )
	{
		var nPivotpos = getPartition( a_aRows, a_nLow, a_nHigh, a_funcComp, a_funcSwap );
		quickSort( a_aRows, a_nLow, nPivotpos - 1, a_funcComp, a_funcSwap );
		quickSort( a_aRows, nPivotpos + 1, a_nHigh, a_funcComp, a_funcSwap );
	}
}

function updateHeader()
{
	var tdTitle = getElement( "OdinFtsTitle" );
	var tdRank = getElement( "OdinFtsRank" );
	if ( tdTitle == null || tdRank == null )
		return;
	switch( g_nSortType )
	{
		case 0:	setInnerHTML( tdTitle, gsTitle );
				setInnerHTML( tdRank, gsRank + ' ' + gsAscendFlag );
				break;
		case 1:	setInnerHTML( tdTitle, gsTitle + ' ' + gsAscendFlag );
				setInnerHTML( tdRank, gsRank );
				break;
		case 2:	setInnerHTML( tdTitle, gsTitle + ' ' + gsDescendFlag );
				setInnerHTML( tdRank, gsRank );
				break;
	}
}

function quickSortResult()
{
	var tbl = getElement( "OdinFtsRslt" );
	if ( tbl == null )
		return;
	
	var tdRank = getElement( "OdinFtsRank" );
		if ( tdRank == null )
			return;

	var comp = g_nSortType == 0 ? compByRank
								: g_nSortType == 1 ? compByTitleAscend
													: compByTitleDescend;
	quickSort( tbl.rows, 0, tbl.rows.length - 1, comp, swapTableNode );
	updateHeader();
}

function getStylePropertyValue(elementTag,styleProp)
{
    var y=null;
	if (elementTag.currentStyle)
		y = elementTag.currentStyle[styleProp];
	else if (window.getComputedStyle)
		y = document.defaultView.getComputedStyle(elementTag,null).getPropertyValue(styleProp);
	return y;
}

function uiMouseDown( e )
{
	var myEvent = gbNav7 || gbAIR ? e : event;
	var clickedElement = gbNav7 || gbAIR ? myEvent.target : myEvent.srcElement;
	if ( clickedElement.id == 'DragSep' )
	{
		var tdRank = getElement( "OdinFtsRank" );
		if ( tdRank == null )
			return;
			
		var elementWidth = tdRank.style.width;
		if(elementWidth==null || elementWidth.length==0)
		{
	        elementWidth = getStylePropertyValue(tdRank,"width");
		}
		if(elementWidth != null && elementWidth.length>0)
		{
		    //in case of every thing fail try the offsetwidth as well
		    if(tdRank.offsetWidth)
		        elementWidth = '' +tdRank.offsetWidth+'px';
		}
		
		if(elementWidth != null && elementWidth.length>0)
		{
		    var matchResult = elementWidth.match( "^(.+)px$" );
		    if(matchResult!=null)
		    {
        		g_nInitWidth = parseInt( elementWidth.match( "^(.+)px$" )[1] );
		        if ( g_nInitWidth.toString() != "NaN" )
			    {
		            g_nX0 = getEventX( e );
		            g_bDraging = true;
		        }
		    }
		}
	}
	else if ( clickedElement.id == 'OdinFtsTitle' )
	{
		g_nSortType = g_nSortType != 1 ? 1 : 2;
		quickSortResult();	
	}
	else if ( clickedElement.id == 'OdinFtsRank' )
	{
		g_nSortType = 0;
		quickSortResult();	
	}
}

function uiMouseMove( e )
{
	var tdRank = getElement( "OdinFtsRank" );
	if ( tdRank == null )
		return;
	if ( g_bDraging == true )
	{
		var d = getEventX( e ) - g_nX0;
		if ( g_nInitWidth - d > g_nRankWidthMin )
			tdRank.style.width = g_nInitWidth - d;
	}
}

function uiMouseUp( e )
{
	g_bDraging = false;
}

function uiClick( e )
{
	var myEvent = gbNav7 ? e : event;
	var clickedElement = gbNav7 ? myEvent.target : myEvent.srcElement;
	if ( clickedElement.className == "fr_tc" && clickedElement.tagName == "A" )
	{
		var lnkAct = getElement( "ActiveLink" );
		if ( lnkAct != null )
		{
			lnkAct.id = "";
			lnkAct.className = "fr_tc";
		}

		clickedElement.id = "ActiveLink";
		clickedElement.className = "fr_act";
	}
	else if (gbAIR && clickedElement.className == "sl_page" && clickedElement.tagName == "LI" )
	{
	    var id = clickedElement.id;
	    if(id!=null && id.length>0)
	    {
		    var pageNum =parseInt(clickedElement.id);
		    onClickPage(pageNum);
		}
	}	
}

if ( gbNav7 || gbAIR ||gbSafari)
{
	Node.prototype.swapNode = function( a_Node )
	{
		var nextSibling = this.nextSibling;
		var parentNode = this.parentNode;
		a_Node.parentNode.insertBefore( this, a_Node );
		parentNode.insertBefore( a_Node, nextSibling );  
	}

	document.captureEvents( Event.MOUSEMOVE | Event.MOUSEDOWN | Event.MOUSEUP | Event.CLICK );
}

///////////////////////////////////////////////////////////////////////////

function loadFts()
{
	if( !gbReady )
	{
		var oResMsg = new whMessage( WH_MSG_GETPROJINFO, this, 1, null );
		if( SendMessage( oResMsg ) && oResMsg.oParam )
		{
			gbReady = true;
			var oProj = oResMsg.oParam;
			var aProj = oProj.aProj;
			gbXML = oProj.bXML;
			if( aProj && aProj.length > 0 )
			{
				context = new HuginContext();
				goOdinHunter = new HuginHunter();
				goOdinHunter.aProjPathes = new Array();
				for ( var i in aProj )
				{
					goOdinHunter.aProjPathes[i] = new Object();
					goOdinHunter.aProjPathes[i].strProjDir = aProj[i].sPPath;
					goOdinHunter.aProjPathes[i].strOdbPath = aProj[i].sPPath + aProj[i].sDPath + aProj[i].sFts;
				}
				
				context.reset();
				context.push( goOdinHunter.init, goOdinHunter,
							  registListener, this );
				context.resume();
			}
		}
	}
}

var gbAIRMergeArray;
function GetMergeProjectArray()
{
	try
	{
		if(gbAIR)
		{
			return gbAIRMergeArray; 
		}
		else if(gbAIRSSL)
		{
			if(this.window.parent.parent.gbAIRmergeProjectArray!=null)
				return this.window.parent.parent.gbAIRmergeProjectArray;
		}
	}catch(e){}
	return null;
}


function loadFts_context(strProjDir , strFtsFile)
{
	var mergeArray = GetMergeProjectArray();
	
	goOdinHunter = new HuginHunter();
	goOdinHunter.aProjPathes = new Array();
	goOdinHunter.aProjPathes[0] = new Object();
	goOdinHunter.aProjPathes[0].strProjDir = strProjDir ; 
	goOdinHunter.aProjPathes[0].strOdbPath = goOdinHunter.aProjPathes[0].strProjDir + "whxdata/whfts.xml";
	if(mergeArray!=null)
	{
		var index=0;
		while(index<mergeArray.length)
		{
			goOdinHunter.aProjPathes[index+1] = new Object();
			goOdinHunter.aProjPathes[index+1].strProjDir = mergeArray[index];
			goOdinHunter.aProjPathes[index+1].strOdbPath = mergeArray[index] + "whxdata/whfts.xml";
			index++;
		}
			
	}

	context = new HuginContext();
	context.reset();
	context.push( goOdinHunter.init, goOdinHunter,
				  registListener, this );
	context.resume();
}

function Query()
{
	gbAIRSearchString  = goOdinHunter.strQuery;
	g_CurPage = 1;
	context = new HuginContext();
	context.reset();
	context.push( goOdinHunter.query, goOdinHunter,
				  processHunterResult, null );
	context.resume();
}

function changeResultView( a_strHTML )
{
	var resultDiv = getElement( gsResultDivID );
	if(resultDiv )
	{
		var  resultDivParent = getParentNode( resultDiv );
		if (!resultDivParent )
			return;
		resultDiv.innerHTML = a_strHTML;
	}
	else document.body.innerHTML = a_strHTML;
}

function displayMsg( a_strMsg )
{
	if(!gbAIRSSL)
	{
		//set num of results
		var oMsg = new whMessage(WH_MSG_SETNUMRSLT, this, 1, 0);
		SendMessage(oMsg);
	}
	
	changeResultView( "<P CLASS='msg'>" + a_strMsg + "</P>" );
}

function writeTableHead()
{
	if(!gsHideRankColumn)
	{
		if(gbAIRSSL)
		{
				return	"<TABLE CELLSPACING=0 CEllPADDING=0 STYLE='table-layout:fixed; width:100%'>" +
						"<THEAD>" + 
							"<TR STYLE='font-weight:bold'>" +
								"<TD ID='OdinFtsTitle'	CLASS='fr_tt' STYLE='cursor:pointer'>"+gsTitle+"</TD>" +
								"<TD ID='DragSep'		CLASS='fr_ts' STYLE='cursor:w-resize; width:4px'>&nbsp;</TD>" +
								"<TD ID='OdinFtsRank'	CLASS='fr_tt' STYLE='cursor:pointer; width:60px'>"+gsRank+"^</TD>" +
							"</TR>" +
						"</THEAD>" +
						"<TBODY ID='OdinFtsRslt'>";
		}
		else
		{
				return	"<TABLE CELLSPACING=0 CEllPADDING=0 STYLE='table-layout:fixed; width:110%'>" +
						"<THEAD>" + 
							"<TR STYLE='font-weight:bold'>" +
								"<TD ID='OdinFtsTitle'	CLASS='fr_tt' STYLE='cursor:pointer'>"+gsTitle+"</TD>" +
								"<TD ID='DragSep'		CLASS='fr_ts' STYLE='cursor:w-resize; width:4px'>&nbsp;</TD>" +
								"<TD ID='OdinFtsRank'	CLASS='fr_tt' STYLE='cursor:pointer'>"+gsRank+"^</TD>" +
							"</TR>" +
						"</THEAD>" +
						"<TBODY ID='OdinFtsRslt'>";
		}
	}
	else
		return "<TABLE CELLSPACING=0 CEllPADDING=0 STYLE='table-layout:fixed; width:110%'>" +
				"<TBODY ID='OdinFtsRslt'>";
}

function writeResult( a_strUrl, a_strHighlight, a_strTitle, a_strFont, a_nIndex, a_sSummary )
{
	var strOutput = "";
	if(a_sSummary.length > 0)
	{
		strOutput = "<TR>" +
				"<TD CLASS='fr_tc'><A CLASS='fr_tc'" +
				" HREF='" + a_strUrl + "'" + ">" + _textToHtml_nonbsp(a_strTitle) + "</A><div>" + _textToHtml_nonbsp(a_sSummary) + "</div><br></TD>";
		if(!gsHideRankColumn)
		{
				strOutput += "<TD CLASS='fr_tc'>&nbsp;</TD>" +
				"<TD CLASS='fr_tc' STYLE='text-align:center'>" + a_nIndex + "</TD>";
		}
		strOutput += "</TR>";
	}
	else
	{
		strOutput = "<TR>" +
				"<TD CLASS='fr_tc'><A CLASS='fr_tc' STYLE='font-weight:normal;'" +
				" HREF='" + a_strUrl + "'" + ">" + _textToHtml_nonbsp(a_strTitle) + "</A></TD>";
		if(!gsHideRankColumn)
		{
				strOutput += "<TD CLASS='fr_tc'>&nbsp;</TD>" +
				"<TD CLASS='fr_tc' STYLE='text-align:center'>" + a_nIndex + "</TD>";
		}
		strOutput += "</TR>";
	}

	return strOutput;
}

function writeTableTail()
{
	return	"</TBODY>" +
			"</TABLE>";
}

function HandleAIRSearchFunction()
{
	if(gAIRSearchObject != undefined && gAIRSearchObject.displayFunction != undefined)
	{
		gAIRSearchObject.displayFunction();
		return;
	}
}

function displayTopics( a_QueryResult )
{
    HandleAIRSearchFunction();
	var sHTML = "";
	var sLine = "";
	var szSearchStrings= gbAIRSearchString;
	var sHighlight = "CLRF=" + gsHLColorFront +
					 ",CLRB=" + gsHLColorBackground + ",HL=";
	
	var nMaxResult = 10 ;
	if(gbAIRSSL)
		nMaxResult = 25; 
	if(!gbAIRSSL)
	{
		var oMsg = new whMessage(WH_MSG_GETMAXRSLT, this, 1, null);
		if (SendMessage(oMsg))
			nMaxResult = oMsg.oParam;
	}
	
	var i = 0;
	if (g_CurPage < 1)
	    g_CurPage = 1 ;
	
	if ( a_QueryResult != null )
	{
		if(!gbAIRSSL)
		{
			//set num of results
			var oMsg = new whMessage(WH_MSG_SETNUMRSLT, this, 1, a_QueryResult.aTopics.length);
			SendMessage(oMsg);
		}
		
		sHTML += writeTableHead();
		if ( gbHighLight )
		{
			var strAddForHighlight;
			strAddForHighlight = "?" ;
			if(gbAIRSSL)
			{
				strAddForHighlight += "searchhighlightTextStringRoboHelp=\"";	
				//in case of AIR, we need to add synonyms as well
				if(gbAIRSynonymString!=null && gbAIRSynonymString.length>0)
				    szSearchStrings+= gbAIRSynonymString;
			}
			sHighlight = strAddForHighlight + escape( szSearchStrings ) + "\"";
		}
		else
			sHighlight = "";
		
		for( i = (g_CurPage-1)*nMaxResult; (i < a_QueryResult.aTopics.length)&&(i<(g_CurPage*nMaxResult)); i++ )
		{
			var szTopicURL = a_QueryResult.aTopics[i].strUrl;
			if(gbAIRSSL && !_isRemoteUrl(szTopicURL))
			{
				//if it is AIR, we need to add the highlight option now
				szTopicURL += sHighlight;
			}
			sLine += writeResult( szTopicURL,
								  sHighlight,
								  a_QueryResult.aTopics[i].strTitle,
								  getFontStyle(goFont),
								  a_QueryResult.aTopics[i].nIndex,
								  a_QueryResult.aTopics[i].strSummary );
			if( i & 0xF == 0 )
			{
				sHTML += sLine;
				sLine = "";
			}
		}
		if( sLine.length > 0 )
			sHTML += sLine;
					
		sHTML += writeTableTail();
		
		var nNumPages = Math.ceil(a_QueryResult.aTopics.length / nMaxResult );
		var sPageHTML = "" ;
		var j ;
		if (nNumPages > 1)
		{
			sHTML += "<div><ul style=\"list-style:none; margin:0; padding:0\" >" ;
			if (g_CurPage > 1)
				sPageHTML += "<li class='sl_page' id='"+(g_CurPage-1).toString()+"' onMouseOver=\"this.className='sl_pageC'\" onMouseOut=\"this.className='sl_page'\" onclick=\"onClickPage(" + (g_CurPage-1).toString() + ")\">&lt;&lt;</li>" ;	
			var sClass = "sl_page" ;
			for (j=g_CurPage;j<=nNumPages && j<=(g_CurPage+g_nMaxPages) ;j++)
			{
				if (j == g_CurPage )
					sClass = "sl_pageC" ;
				else
					sClass = "sl_page" ;
				sPageHTML += "<li class='" +sClass + "' id='"+j.toString()+"' onMouseOver=\"this.className='sl_pageC'\" onMouseOut=\"this.className='" + sClass + "'\" onclick=\"onClickPage(" + j + ")\">" + j.toString() + "</li>" ;
			}
			
			if(g_CurPage != nNumPages)
				sPageHTML += "<li class='sl_page' id='"+(g_CurPage+1).toString()+"' onMouseOver=\"this.className='sl_pageC'\" onMouseOut=\"this.className='sl_page'\" onclick=\"onClickPage(" + (g_CurPage+1).toString() + ")\">&gt;&gt;</li>" ;			
			sHTML += sPageHTML ;
			sHTML += "</ul></div>" ;
		}		
	}
	
	if( a_QueryResult.aTopics.length == 0 )
		sHTML = "<P CLASS='msg'>" + gsNoTopics + "</P>"
	else
		sHTML= "<dl>" + sHTML + "</dl>";

	changeResultView( sHTML );
	if(gbAIR)
	{
		quickSortResult();
		if(gbAIRSearchResultDisplayDoneFn!=null)
		{
			gbAIRSearchResultDoneSteps++;
			if(gbAIRSearchResultDoneSteps>0)
			{
				gbAIRSearchResultDone = true;
				gbAIRSearchResultDoneSteps = 0;
				gbAIRSearchResultDisplayDoneFn();
			}
		}
	}	
	else	setTimeout( "quickSortResult();", 1 );
}

function dummySearchResultFn()
{
	//don no
}

function onClickPage( a_nPageNumber )
{
    g_CurPage = a_nPageNumber ;
    g_CurState = ECS_FOUND ;
    updateResultView();
}

function displaySearchProgressBar( a_nProgress )
{
	var pb = getElement( 'SearchProgressBar' );
	var pt = getElement( 'SearchProgress' );
	if( !pb || !pt )
	{
		var sHTML = "<P ID='SearchProgress' CLASS='pb_tt'>" + gsSearching + " " + a_nProgress + "%</P>\n" +
					"<DIV CLASS='pb_out'>\n" +
					"<P ID='SearchProgressBar' CLASS='pb_in' STYLE='width:" + a_nProgress + "%'></P>\n" +
					"</DIV>\n" +
					"<CENTER><P CLASS='pb_cancel' ONMOUSEDOWN='context.stop()'>"+gsCancel+"</P></CENTER>\n";
		changeResultView( sHTML );
	}
	else
	{
		setInnerHTML( pt, gsSearching + " " + a_nProgress + "%" );
		pb.style.width = a_nProgress + "%";
	}
}

function updateResultView()
{
	if ( g_CurState == ECS_SEARCHING )
		displaySearchProgressBar( goOdinHunter.nProgress );
		
	else if ( g_CurState == ECS_FOUND )
		displayTopics( goOdinHunter.queryResult );
		
	else if ( g_CurState == ECS_SEARCHFAILED )
		displayMsg( context.strMsg );
		
	else if ( g_CurState == ECS_FATALERROR )
		displayMsg( context.strMsg );
		
	else if ( g_CurState == ECS_CANCELED )
		displayMsg( gsCanceled );
		
	if(g_CurState == ECS_SEARCHFAILED ||
	    g_CurState == ECS_FATALERROR ||
	    g_CurState == ECS_CANCELED 
	    )
	{
	    HandleAIRSearchFunction();
	}
}

function processHunterResult( a_Context )
{
	if ( a_Context )
	{
		updateResultView();
		if(gbAIR)
			processHunterResult();
		else setTimeout( "processHunterResult();", 1 );
		return;
	}
	
	if ( goOdinHunter == null )
		return;

	if ( !goOdinHunter.bSucc )
	{
		g_CurState = ECS_SEARCHFAILED;
		updateResultView();
		return;
	}

	g_CurState = ECS_FOUND;
	updateResultView();
}

function GetSearchTextFromURL()
{
	var strLocation = document.location.href;
	var queryStartPos = strLocation.indexOf('%22');
	var queryEndPos = strLocation.lastIndexOf('%22');
	var strQuery = "";
	if(queryStartPos!=-1 && queryEndPos!=-1 )
	{
		strQuery = strLocation.substring(queryStartPos + 3  , queryEndPos);
	}
	if(queryStartPos==-1 && queryEndPos==-1 )
	{
	    //try to see if " are there
	    queryStartPos = strLocation.indexOf('\"');
	    queryEndPos = strLocation.lastIndexOf('\"');
	    if(queryStartPos!=-1 && queryEndPos!=-1 )
	    {
		    strQuery = strLocation.substring(queryStartPos + 1  , queryEndPos);
	    }
	}
	strQuery = decodeURIComponent(strQuery);
	strQuery= unescape(strQuery);
 	//strQuery = strQuery.toUpperCase();
	return strQuery;
}

function setSearchDisplayFunction(funcName)
{
	gAIRSearchObject = new Object();
	gAIRSearchObject.displayFunction = funcName;
}

if ( gbAIRSSL || !window.gbTesting )
{
	if ( window.gbWhUtil && window.gbWhLang && window.gbWhVer &&
	 	 (window.gbAIRSSL || 
	 	 	(
				 window.gbWhProxy &&
				 window.gbWhMsg 
			)
		)
	)
	{
		goFont=new whFont("Verdana","8pt","#000000","normal","normal","none");
		goErrFont=new whFont("Verdana","8pt","#000000","normal","bold","none");
		goHoverFont=new whFont("Verdana","8pt","#007f00","normal","normal","underline");
		if(!gbAIR)
			window.onload=window_OnLoad;
		window.onbeforeunload=window_BUnload;
		window.onunload=window_Unload;
		if(!gbAIRSSL)
			RegisterListener2(this,WH_MSG_PROJECTREADY);
		gbWhFHost=true;
	}
	else
	{
		document.location.reload();
	}
}

function utf8Compare(strText1,strText2)
{
	var strt1=strText1;
	var strt2=strText2;

	try {
		strt1=strText1.toLowerCase();
	}
	catch(er) {
	}

	try {
		strt2=strText2.toLowerCase();
	}
	catch(er) {
	}

	if(strt1<strt2) return -1;
	if(strt1>strt2) return 1;
	return 0;
}


document.onmousedown = uiMouseDown;
document.onmousemove = uiMouseMove;
document.onmouseup = uiMouseUp;
document.onclick = uiClick;
