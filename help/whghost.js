//	WebHelp 5.10.004
var gbXML=false;
var gaDef=new Array();
var gsBgColor="#ffffff";
var gsBgImage="";
var goFont=null;
var goHoverFont=null;
var gsMargin="2pt";
var gsIndent="2pt";
var gsActiveBgColor="#cccccc";
var gbWhGHost=false;
var gbShowDef=true;
var gsGloId="Glo_"

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
	else if(sType=="Hover")
		goHoverFont=vFont;
}

function setActiveBgColor(sBgColor)
{
	gsActiveBgColor=sBgColor;
}

function setMargin(sMargin)
{
	gsMargin=sMargin;
}

function setIndent(sIndent)
{
	gsIndent=sIndent;
}

function insertDef(sKName,sDef)
{
	var nLength=gaDef.length
	var oGlo=new nameDefPair(sKName,sDef);
	gaDef[nLength]=oGlo;
	return nLength;
}

function showDef(nDef)
{
	if(nDef<gaDef.length)
	{	
		HighLight(nDef);
		var oParam=gaDef[nDef];
		var oMsg=new whMessage(WH_MSG_SHOWGLODEF,this,1,oParam);
		SendMessage(oMsg);
	}
}

function nameDefPair(sName,sDef)
{
	this.sName=sName;
	this.sDef=sDef;
}

function mergeItems(oHTML,bDown,aDataCon,aCurIdxSet,nLength,aPos,nLevel)
{
	var sHTML="";
	var nIdxSet=aCurIdxSet[0];
	var nIIdx=aPos[nIdxSet];
	var sRawKName = getItemName(aDataCon,nIdxSet,nIIdx);
	var sKName=_textToHtml(sRawKName);
	var sDText=_textToHtml_nonbsp(getDef(aDataCon,nIdxSet,nIIdx));
	var sDef=sDText;
	for(var i=1;i<nLength;i++)
	{
		var nIdxSeti=aCurIdxSet[i];
		var nIIdxi=aPos[nIdxSeti];
		var sRawKNamei = getItemName(aDataCon,nIdxSeti,nIIdxi);
		var sKNamei=_textToHtml(sRawKNamei);
		var sDTexti=_textToHtml_nonbsp(getDef(aDataCon,nIdxSeti,nIIdxi));
		if (compare(sKNamei,sKName)==0)
		{
			if (sDText!=sDTexti)
				sDef+="<br>"+sDTexti;
		}
	}
	var nIndex=insertDef(sKName,sDef);
	if(nLevel==1)
	{
		sHTML+="<p><nobr id=\""+getGloId(nIndex)+"\">";
		sHTML+="<a alt=\""+sKName+"\" href=\"javascript:void(0);\" onfocus=\"HighLight("+nIndex+");\" onclick=\"showDef("+nIndex+");return false;\">"+sKName+"</a></nobr></p>";
		oHTML.addHTML(sHTML,nLength,bDown,true,sRawKName);
		if(gbShowDef)
		{
			setTimeout("showDef(0);",100);
			gbShowDef = false;
		}
	}
}

function HighLight(nIndex)
{
	var oObj=getElement(getGloId(nIndex));
	if(oObj)
		HighLightElement(oObj,gsActiveBgColor,"transparent");
}

function getGloId(nIndex)
{
	return gsGloId+nIndex;
}

function adjustPosition(bDown,aDataCon,aCurIdxSet,nLength,aPos)
{
	if(bDown)
	{
		for(var i=0;i<nLength;i++)
			aPos[aCurIdxSet[i]]++;
	}
	else{
		for(var i=0;i<nLength;i++)
			aPos[aCurIdxSet[i]]--;
	}
}

function getDef(aDataCon,nIdxSet,nIIdx)
{
	if(nIdxSet<aDataCon.length&&aDataCon[nIdxSet].aKs.length>nIIdx)
		return aDataCon[nIdxSet].aKs[nIIdx].sDef;
	else
		return null;
}

function getItemName(aDataCon,nIdxSet,nIIdx)
{
	if(nIdxSet<aDataCon.length&&aDataCon[nIdxSet].aKs.length>nIIdx)
		return aDataCon[nIdxSet].aKs[nIIdx].sName;
	else
		return null;
}

function window_OnLoad()
{
	if(gsBgImage&&gsBgImage.length>0)
		document.body.background=gsBgImage;
	if(gsBgColor&&gsBgColor.length>0)
		document.body.bgColor=gsBgColor;
	document.body.insertAdjacentHTML("beforeEnd",writeLoadingDiv());
	loadGlo();
	var oMsg=new whMessage(WH_MSG_SHOWGLO,this,1,null)
	SendMessage(oMsg);
}

function loadGlo()
{
	if(!gbReady)
	{
		var oResMsg=new whMessage(WH_MSG_GETPROJINFO,this,1,null);
		if(SendMessage(oResMsg)&&oResMsg.oParam)
		{
			gbReady=true;
			var oProj=oResMsg.oParam;
			var aProj=oProj.aProj;
			gbXML=oProj.bXML;
			if(aProj.length>0)
			{
				var sLangId=aProj[0].sLangId;
				for(var i=0;i<aProj.length;i++)
				{
					if(aProj[i].sGlo&&aProj[i].sLangId==sLangId)
						addProjInfo(aProj[i].sPPath,aProj[i].sDPath,aProj[i].sGlo);
				}
			}
			writeDataIFrame();
			enEvt();
		}		
	}
}

function findCKInDom()
{
	return true;
}

function GloWriteClassStyle()
{
	var sStyle="<STYLE TYPE='text/css'>\n";
	if (gsBgImage)
		sStyle+="body {border-top:"+gsBgColor+" 1px solid;}\n";
	else
		sStyle+="body {border-top:black 1px solid;}\n";
	sStyle+="P {"+getFontStyle(goFont)+"margin-top:"+gsMargin+";margin-bottom:"+gsMargin+";margin-left:"+gsIndent+";}\n";
	sStyle+="A:link {"+getFontStyle(goFont)+"}\n";
	sStyle+="A:visited {"+getFontStyle(goFont)+"}\n";
	sStyle+="A:active {background-color:"+gsActiveBgColor+";}\n";
	sStyle+="A:hover {"+getFontStyle(goHoverFont)+"}\n";
	sStyle+="</STYLE>";	
	document.write(sStyle);
}

function window_Unload()
{
	UnRegisterListener2(this,WH_MSG_PROJECTREADY);
	UnRegisterListener2(this,WH_MSG_SHOWGLO);
}

function onSendMessage(oMsg)
{
	if(oMsg)
	{
		var nMsgId=oMsg.nMessageId;
		if(nMsgId==WH_MSG_SHOWGLO)
		{
			if(!gbNav6)
				document.body.focus();
		}
		else if(nMsgId==WH_MSG_PROJECTREADY)
		{
			loadGlo();
		}
	}
	return true;
}

if (window.gbWhUtil&&window.gbWhProxy&&window.gbWhVer&&window.gbWhLang&&window.gbWhMsg&&window.gbWhHost)
{
	RegisterListener2(this,WH_MSG_PROJECTREADY);
	RegisterListener2(this,WH_MSG_SHOWGLO);
	goFont=new whFont("Verdana","8pt","#000000","normal","normal","none");
	goHoverFont=new whFont("Verdana","8pt","#007f00","normal","normal","underline");
	window.onload=window_OnLoad;
	window.onbeforeunload=window_BUnload;
	window.onunload=window_Unload;
	gbWhGHost=true;
}
else
	document.location.reload();

