//	WebHelp 5.10.002
var gsDefaultTarget="bsscright";
var gsBgColor="#ffffff";
var gsBgImage="";
var goIdxFont=null;
var goIdxEmptyFont=null;
var goIdxHoverFont=null;
var gsIdxMargin="0pt";
var gsIdxIndent="8pt";
var gsIdxActiveBgColor="#cccccc";
var gsCK = null;
var gsBCK = null;
var gbCR = false;
var gbBCR = false;
var gbWhIHost=true;

function myEvent()
{
	this.pageX = 0;
	this.pageY = 0;
}
var _event=new myEvent();

function setBackgroundcolor(sBgColor)
{
	gsBgColor=sBgColor;
}

function setBackground(sBgImage)
{
	gsBgImage=sBgImage;
}

function setFont(sType,sFontName,sFontSize,sFontColor,sFontStyle,sFontWeight,sFontDecoration)
{
	var vFont=new whFont(sFontName,sFontSize,sFontColor,sFontStyle,sFontWeight,sFontDecoration);
	if(sType=="Normal")
		goIdxFont=vFont;
	else if(sType=="Empty")
		goIdxEmptyFont=vFont;
	else if(sType=="Hover")
		goIdxHoverFont=vFont;
}

function setActiveBgColor(sBgColor)
{
	gsIdxActiveBgColor=sBgColor;
}

function setMargin(sMargin)
{
	gsIdxMargin=sMargin;
}

function setIndent(sIndent)
{
	gsIdxIndent=sIndent;
}

function writeOneItem(oHTML,bDown,aDataCon,aCurIdxSet,nLength,aPos,nLevel)
{
	var sHTML="";
	var nIdxSet=aCurIdxSet[0];
	var nIIdx=aPos[nIdxSet];
	var sKOriName=getItemName(aDataCon,nIdxSet,nIIdx);
	var sKName=_textToHtml(sKOriName);

	var nIdxIndent=parseInt(gsIdxIndent);
	var sTopics="";
	if(nLevel==1){
		if(getItemType(aDataCon,nIdxSet,nIIdx)==1)
		{
			sHTML+="<H6><nobr>";
			sHTML+="<b>"+sKName+"</b></nobr></H6>";
		}
		else{
			for(var i=0;i<nLength;i++)
				sTopics+=getIdxTopics(aDataCon,aCurIdxSet[i],aPos[aCurIdxSet[i]]);
			sHTML+="<p style=\"margin-left:"+gsIdxIndent+"\"><nobr>";
			sHTML+="<a alt=\"" + sKName + "\" href=\"javascript:void(0);\" onfocus=\"clearHighLight();\" onclick=\"PopupMenu_Invoke(event,'"+excapeSingleQuotandSlash(getTargetName(aDataCon,nIdxSet,nIIdx))+"'";
			if(sTopics.length>0)
				sHTML+=sTopics+");return false;\">"+sKName+"</a></nobr></p>";
			else
				sHTML+=");return false;\" style=\""+getFontStyle(goIdxEmptyFont)+"\">"+sKName+"</a></nobr></p>";
		}
	}
	else if(nLevel>=2){
		var nIndent=nIdxIndent*nLevel;
		for(var i=0;i<nLength;i++)
			sTopics+=getIdxTopics(aDataCon,aCurIdxSet[i],aPos[aCurIdxSet[i]]);
		if (nLevel==2)
			sHTML+="<h6 class=\"firstsub\" style=\"margin-left:"+nIndent+"pt\"><nobr>";
		else
			sHTML+="<h6 style=\"margin-left:"+nIndent+"pt\"><nobr>";
		sHTML+="<a alt=\"" + sKName + "\" href=\"javascript:void(0);\" onfocus=\"clearHighLight();\" onclick=\"PopupMenu_Invoke(event,'"+excapeSingleQuotandSlash(getTargetName(aDataCon,nIdxSet,nIIdx))+"'";
		if(sTopics.length>0)
			sHTML+=sTopics+");return false;\">"+sKName+"</a></nobr></h6>";
		else
			sHTML+=");return false;\" style=\""+getFontStyle(goIdxEmptyFont)+"\">"+sKName+"</a></nobr></h6>";
	}
	oHTML.addHTML(sHTML,nLength,bDown,(nLevel==1),sKOriName);
}

function getTargetName(aDataCon,nIdxSet,nIIdx)
{
	if(nIdxSet<aDataCon.length&&aDataCon[nIdxSet].aKs.length>nIIdx)
		if(aDataCon[nIdxSet].aKs[nIIdx].sTarget)
			return aDataCon[nIdxSet].aKs[nIIdx].sTarget;
	return gsDefaultTarget;
}

function mergeItems(oHTML,bDown,aDataCon,aCurIdxSet,nLength,aPos,nLevel)
{
	var oLocalHTML=new indexHTMLPart();
	writeOneItem(oLocalHTML,bDown,aDataCon,aCurIdxSet,nLength,aPos,nLevel);
	
	var aLocalPos=new Array();
	var aMaxPos=new Array();
	for(var i=0;i<aPos.length;i++)
	{
		aLocalPos[i]=aPos[i];
		aMaxPos[i]=-1;
	}
	
	for(i=0;i<nLength;i++)
	{
		var nNKOff=getNKOff(aDataCon,aCurIdxSet[i],aLocalPos[aCurIdxSet[i]]);
		aLocalPos[aCurIdxSet[i]]++;
		if(nNKOff>0)
			aMaxPos[aCurIdxSet[i]]=aLocalPos[aCurIdxSet[i]]+nNKOff;
	}
	var oSubHTML=new indexHTMLPart();
	writeItems(oSubHTML,aDataCon,aLocalPos,null,aMaxPos,true,nLevel+1);
	oLocalHTML.addSubHTML(oSubHTML,true);
	oHTML.appendHTML(oLocalHTML,bDown);
}

function adjustPosition(bDown,aDataCon,aCurIdxSet,nLength,aPos)
{
	if(bDown)
	{
		for(var i=0;i<nLength;i++)
		{
			var nNKOff=getNKOff(aDataCon,aCurIdxSet[i],aPos[aCurIdxSet[i]]);
			aPos[aCurIdxSet[i]]+=(1+nNKOff);
		}
	}
	else{
		for(var i=0;i<nLength;i++)
		{
			var nPKOff=getPKOff(aDataCon,aCurIdxSet[i],aPos[aCurIdxSet[i]]);
			aPos[aCurIdxSet[i]]-=(1+nPKOff);
		}
	}
}

function getItemName(aDataCon,nIdxSet,nIIdx)
{
	if(nIdxSet<aDataCon.length&&aDataCon[nIdxSet].aKs.length>nIIdx)
		return aDataCon[nIdxSet].aKs[nIIdx].sName;
	else
		return null;
}

function getItemType(aDataCon,nIdxSet,nIIdx)
{
	if(nIdxSet<aDataCon.length&&aDataCon[nIdxSet].aKs.length>nIIdx)
		return aDataCon[nIdxSet].aKs[nIIdx].nType;
	else
		return 0;
}

function getNKOff(aDataCon,nIdxSet,nIIdx)
{
	if(nIdxSet<aDataCon.length&&aDataCon[nIdxSet].aKs.length>nIIdx)
		return aDataCon[nIdxSet].aKs[nIIdx].nNKOff;
	else
		return null;
}

function getPKOff(aDataCon,nIdxSet,nIIdx)
{
	if(nIdxSet<aDataCon.length&&aDataCon[nIdxSet].aKs.length>nIIdx)
		return aDataCon[nIdxSet].aKs[nIIdx].nPKOff;
	else
		return null;
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
	document.body.insertAdjacentHTML("beforeEnd",writeLoadingDiv());
	loadIdx();
	var oMsg=new whMessage(WH_MSG_SHOWIDX,this,1,null)
	SendMessage(oMsg);
}

function loadIdx()
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
					if(aProj[i].sIdx&&aProj[i].sLangId==sLangId)
					{
						addProjInfo(aProj[i].sPPath,aProj[i].sDPath,aProj[i].sIdx);
					}
				}
			}
			writeDataIFrame();
			enEvt();
		}		
	}
}

function getIdxTopics(aDataCon,nIdxSet,nIIdx)
{
	var sTopics="";
	if(nIdxSet<aDataCon.length&&aDataCon[nIdxSet].aKs.length>nIIdx)
	{
		if(aDataCon[nIdxSet].aKs[nIIdx].aTopics)
		{
			var nLen=aDataCon[nIdxSet].aKs[nIIdx].aTopics.length;
			var nProj=aDataCon[nIdxSet].nProjId;
			var sPath=gaData[nProj].sPPath;
			for(var i=0;i<nLen;i++)
			{
				var sURL=aDataCon[nIdxSet].aKs[nIIdx].aTopics[i].sURL;
				var sFullPath=_getFullPath(sPath,sURL);
				sTopics+=",'"+excapeSingleQuotandSlash(_textToHtml(aDataCon[nIdxSet].aKs[nIIdx].aTopics[i].sName))+"','"+excapeSingleQuotandSlash(_textToHtml_nonbsp(sFullPath))+"'";
			}
		}
	}
	return sTopics;		
}

function findCKInDom()
{
	if(gsCK!=null)
	{
		var sK=gsCK;
		var oP=getElementsByTag(document,"P");
		if(!oP) return false;
		var len=oP.length;
		var nB=0;
		var nE=len-1;
		var nM=0;
		var sItem="";
		var bF=false;
		while(nB<nE){
			nM=(nB+nE+1)>>1;
			sItem=getInnerText(oP[nM]);
			
			if(compare(sItem,sK)==0)
			{
				bF=true;
				break;
			}
			else if(compare(sItem,sK)>0)
				nE=nM-1;
			else if(compare(sItem,sK)<0)
				nB=nM;
		}
		if(!bF)
		{
			if(nB==nE) nM=nB;
		
			if(nM+1<len)
			{
				sItem=getInnerText(oP[nM+1]);
				if(compare(sItem,sK)<=0) nM++;
			}			
			if(nM+1<len)
			{
				sItem=getInnerText(oP[nM+1]);
				if(compare(sItem.substring(0,sK.length),sK)==0) nM++;
			}			
		}
	
		var oMatch=oP[nM];
		if(oMatch)
		{
			window.scrollTo(0,oMatch.offsetTop);
			var tempColl=getElementsByTag(oMatch,"A");
			if(tempColl&&tempColl.length>0){
				var nbTag=getElementsByTag(oMatch,"NOBR");
				if(nbTag&&nbTag.length>0)
					HighLightElement(nbTag[0], gsIdxActiveBgColor, "transparent");
				if (gbCR)
				{
					if (gbIE4)
						tempColl(0).click();
					else
					{
						var strCommand = tempColl[0].getAttribute("onClick");
						var nstrCommand = strCommand.indexOf(";");
						strCommand = strCommand.substring(0, nstrCommand);
						strCommand = strCommand.replace("event", "_event");
						window._event.pageX = oMatch.offsetLeft ;
						window._event.pageY = oMatch.offsetTop + 20;
						window.setTimeout(strCommand, 100);
					}
				}
			}
		}
		gsCK=gsBCK;
		gbCR=gbBCR;
		if(gsBCK!=null)
		{
			gsBCK=null;
			gbBCR=false;
			findCK();
			return false;
		}
		
	}
	return true;
}

function clearHighLight()
{
	resetHighLight(gsBgColor);
}

function IndexWriteClassStyle()
{
	var sStyle="";
	sStyle+="<STYLE TYPE='text/css'>";
	if (gsBgImage)
		sStyle+="body {border-top:"+gsBgColor+" 1px solid;}\n";
	else
		sStyle+="body {border-top:black 1px solid;}\n";
	if(gbIE4&&gbMac&&!gbIE5)
	{
		var nMargin=parseInt(gsIdxMargin);
		nMargin-=10;
		sStyle+="P {"+getFontStyle(goIdxFont)+"margin-top:"+gsIdxMargin+";margin-bottom:"+gsIdxMargin+";}\n";
		sStyle+="H6 {"+getFontStyle(goIdxFont)+"margin-top:"+gsIdxMargin+";margin-bottom:"+gsIdxMargin+";}\n";
		sStyle+="H6.firstsub {"+getFontStyle(goIdxFont)+"margin-top:"+nMargin+"pt;margin-bottom:"+gsIdxMargin+";}\n";
	}
	else
	{
		sStyle+="P {"+getFontStyle(goIdxFont)+"margin-top:"+gsIdxMargin+";margin-bottom:"+gsIdxMargin+";}\n";
		sStyle+="H6 {"+getFontStyle(goIdxFont)+"margin-top:"+gsIdxMargin+";margin-bottom:"+gsIdxMargin+";}\n";
	}
	sStyle+="DIV {margin-top:"+gsIdxMargin+";margin-bottom:"+gsIdxMargin+";}\n";
	sStyle+="A:link {"+getFontStyle(goIdxFont)+"}\n";
	sStyle+="A:visited {"+getFontStyle(goIdxFont)+"}\n";
	sStyle+="A:active {background-color:"+gsIdxActiveBgColor+";}\n";
	sStyle+="A:hover {"+getFontStyle(goIdxHoverFont)+"}\n";
	sStyle+="</STYLE>";	
	document.write(sStyle);
	return;
}

function window_Unload()
{
	UnRegisterListener2(this,WH_MSG_PROJECTREADY);
	UnRegisterListener2(this,WH_MSG_SEARCHINDEXKEY);
}

function onSendMessage(oMsg)
{
	if(oMsg)
	{
		var nMsgId=oMsg.nMessageId;
		if(nMsgId==WH_MSG_SEARCHINDEXKEY)
		{
			if(oMsg.oParam && oMsg.oParam.sInput)
			{
				if(gsCK==null)
				{
					gsCK=oMsg.oParam.sInput;
					gbCR = oMsg.oParam.bCR;
					findCK();
				}
				else
				{
					gsBCK=oMsg.oParam.sInput;
					gbBCR = oMsg.oParam.bCR;
				}
			}
		}
		else if(nMsgId==WH_MSG_PROJECTREADY)
		{
			loadIdx();
		}
	}
	return true;
}

if(window.gbWhVer&&window.gbWhLang&&window.gbWhMsg&&window.gbWhUtil&&window.gbWhHost&&window.gbWhProxy)
{
	RegisterListener2(this,WH_MSG_PROJECTREADY);
	RegisterListener2(this,WH_MSG_SEARCHINDEXKEY);
	goIdxFont=new whFont("Verdana","8pt","#000000","normal","normal","none");
	goIdxEmptyFont=new whFont("Verdana","8pt","#666666","normal","normal","none");
	goIdxHoverFont=new whFont("Verdana","8pt","#007f00","normal","normal","underline");

	window.onload=window_OnLoad;
	window.onbeforeunload=window_BUnload;
	window.onunload=window_Unload;
	gbWhIHost=true;
}
else
	document.location.reload();

