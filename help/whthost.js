//	WebHelp 5.10.007
var gaHSLoad=new Array();
var gnMinIdx=0;
var gnInsIdx=-1;
var gsLoadingDivID="LoadingDiv";
var gsLoadingMsg="Loading, click here to cancel...";
var gaProj=null;
var gaTocs=new Array();
var goChunk=null;
var gbReady=false;
var gbToc=false;
var gbXML=false;
var gaRoot=new Array();
var gnCC=-1;
var gsTP="";
var gaBTPs="";
var gsCTPath="";
var gnLT=-1;
var gsPathSplit="\n";
var gsBgColor="#ffffff";
var gsBgImage="";
var goFont=null;
var goHFont=null;

var gsMargin="0pt";
var gsIndent="15pt";
var gsABgColor="#cccccc";

var giBookClose="";
var giBookOpen="";
var giBookItem="";
var giURLItem="";
var giNewBookClose="";
var giNewBookOpen="";
var giNewBookItem="";
var giNewURLItem="";
var gnImages=0;
var gnLoadedImages=0;
var gaImgs=new Array();
var gbLoadData=false;
var gobj=null;
var gaTocsNs61Fix=null;
var gbWhTHost=false;
var gBookItems=new Array();
var gInSync=false;
var gbLData=false;
var gbNeedFillStub=false;
var gbLoadToc=false;

function chunkInfoQueue()
{
	this.aContent=new Array();
	this.inQueue=function(cInfo)
	{
		this.aContent[this.aContent.length]=cInfo;
	}
	this.deQueue=function()
	{
		var cInfo=null;
		if(this.aContent.length>0)
		{
			cInfo=this.aContent[0];
			for(var i=1;i<this.aContent.length;i++)
				this.aContent[i-1]=this.aContent[i];
			this.aContent.length--;
		}
		return cInfo;
	}
	this.length=function()
	{
		return this.aContent.length;
	}
}

var gchunkRequestQueue=new chunkInfoQueue();

function chunkInfo(nIdx, bLocal)
{
	this.nIdx=nIdx;
	this.bLocal=bLocal;
}

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
	if(sType=="Normal") goFont=vFont;
	else if(sType=="Hover") goHFont=vFont;
}

function setActiveBgColor(sBgColor){gsABgColor=sBgColor;}

function setMargin(sMargin){gsMargin=sMargin;}

function setIndent(sIndent){gsIndent=sIndent;}

function setIcon(sType,sURL)
{
	if(sType=="BookOpen")
		giBookOpen=sURL;
	else if(sType=="BookClose")
		giBookClose=sURL;
	else if(sType=="Item")
		giBookItem=sURL;
	else if(sType=="RemoteItem")
		giURLItem=sURL;
	else if(sType=="NewBookClose")
		giNewBookClose=sURL;
	else if(sType=="NewBookOpen")
		giNewBookOpen=sURL;
	else if(sType=="NewItem")
		giNewBookItem=sURL;
	else if(sType=="NewRemoteItem")
		giNewURLItem=sURL;		
}

function bookItem(sTarget,sURL)
{
	if(sTarget)
		this.sTarget=sTarget;
	else
		this.sTarget="bsscright";
	this.sURL=sURL;
}

function addBookItem(sBookId,sTarget,sURL)
{
	gBookItems[sBookId]=new bookItem(sTarget,sURL);		
}

function tocChunk(sPPath,sDPath)
{
	this.sPPath=sPPath;
	this.sDPath=sDPath;
	this.nMI=-1;
	this.aTocs=null;
}

function addTocChunk(sPPath,sDPath)
{
	var oChunk=new tocChunk(sPPath,sDPath);
	gaTocs[gaTocs.length]=oChunk;
	return oChunk;
}

function isHSLoad(nIdx)
{
	for(var i=0;i<gaHSLoad.length;i++)
		if(gaHSLoad[i]==nIdx)
			return true;
	return false;
}

function setHSLoad(nIdx)
{
	if(!isHSLoad(nIdx))
	{
		var len=gaHSLoad.length;
		for(var i=0;i<len;i++)
			if(gaHSLoad[i]==-1)
			{
				gaHSLoad[i]=nIdx;
				return;
			}
		gaHSLoad[len]=nIdx;
	}
}

function setHSUnLoad(nIdx)
{
	if(isHSLoad(nIdx))
	{
		for(var i=0;i<gaHSLoad.length;i++)
			if(gaHSLoad[i]==nIdx)
			{
				gaHSLoad[i]=-1;
				return;
			}
	}
}

function removeLoadingDiv()
{
	var eLoadingDiv=getElement(gsLoadingDivID);
	if(eLoadingDiv)
		removeThis(eLoadingDiv);
}

function checkBookItem(nIdx)
{
	if(!gInSync)
	{
		var sBookId=getBookId(nIdx);
		if(gBookItems[sBookId])
		{
			window.open(gBookItems[sBookId].sURL,gBookItems[sBookId].sTarget);
		}
	}
}

function insertBookItems(nIdx,num)
{
	checkBookItem(nIdx);
	var sChildBookId=getCBId(nIdx);
	var eChildDiv=getElement(sChildBookId);
	if(eChildDiv){
		if((eChildDiv.childNodes&&eChildDiv.childNodes.length==0)||
			(eChildDiv.all&&eChildDiv.all.length==0)){
			var sHTML=writeBookItems(nIdx,num);
			eChildDiv.innerHTML=sHTML;
			setTimeout("syncInit()",1);
		}
	}
	ExpandIt(nIdx);
}

function isBookEmpty(nIdx)
{
	var num=getItemContentsNum(nIdx);
	if (num>0)
	{
		var nCIdx=0;
		do {
			nCIdx++;
			var i=nIdx+nCIdx;
			var nItemType=getItemType(i);
			if(nItemType==1){
				if (!isBookEmpty(i))
					return false;
			}
			else if(nItemType==4){
				var	sSrc=getRefURL(i);
				var nProj=getProject(sSrc);
				if(nProj!=-1){
					sSrc=gaRoot[nProj].sToc;
					if(sSrc)
						return false;
				}
			}
			else if(nItemType==2||nItemType==16||nItemType==8)
				return false;
		} while(nCIdx<num);
	}
	return true;
}

function writeBook(nIdx)
{
	var sIcon=getBookImage(nIdx,true);
	var sName=_textToHtml(getItemName(nIdx));
	sIcon=_textToHtml_nonbsp(sIcon);
	
	var nType=getItemType(nIdx);
	var bLocal=(nType==1);
	var bLocalProject=(nType!=4);
	
	var sHTML="<div id=\""+getPBId(nIdx)+"\" class=";
	if(bLocal)
	{
		if (!isBookEmpty(nIdx))
		{
			var sURL=_textToHtml_nonbsp(getItemURL(nIdx));
			var sBookRef = "javascript:void()"
			if(sURL!="")
				sBookRef = sURL;
			sHTML+="parent><p><nobr><a id=\""+getBookId(nIdx)+"\" href=\""+sBookRef+"\" onfocus=\"markBook("+nIdx+");\" onclick=\"";
			if(gbSafari3)
				sHTML+="markBook("+nIdx+");insertBookItems("+nIdx+", "+getItemContentsNum(nIdx);
			else
				sHTML+="insertBookItems("+nIdx+", "+getItemContentsNum(nIdx);
			sHTML+=");return false;\" title=\""+sName+"\"><img alt=\"Book\" name=\""+getBId(nIdx)+"\" src=\""+sIcon+"\" border=0 align=\"absmiddle\">";
			sHTML+="&nbsp;"+sName+"</a></nobr></p></div>";
			if(sURL!="")
				addBookItem(getBookId(nIdx),_textToHtml_nonbsp(getTopicTarget(nIdx)),sURL);
			sHTML+="<div id=\""+getCBId(nIdx)+"\" class=child></div>";
		}
		else
			sHTML="";
	}
	else
	{
		sHTML+="stub></div>";
		gbNeedFillStub=true;
		setTimeout("fillStub("+nIdx+","+bLocalProject+");",100);
	}
	return sHTML;
}

function checkFillStub()
{
	if(!gbLData)
	{
		if(gchunkRequestQueue.length()>0)
		{
			var cInfo=gchunkRequestQueue.deQueue();
			if(cInfo)
			{
				fillStub(cInfo.nIdx,cInfo.bLocal);
				return;
			}
		}
	}
	if(gbNeedFillStub)
	{
		gbNeedFillStub=false;
		setTimeout("syncInit()",1);
	}
}

function fillStub(nIdx,bLocalProject)
{
	if(!gbLData)
	{
		gbLData=true;
		var sObj=getElement(getPBId(nIdx));
		if(sObj!=null)
		{
			tocExpandHelpSet(nIdx,bLocalProject);
			gbNeedFillStub=false;
			setTimeout("syncInit()",1);
		}
		else
			setTimeout("fillStub("+nIdx+","+bLocalProject+");",100);
	}
	else
		gchunkRequestQueue.inQueue(new chunkInfo(nIdx,bLocalProject));
}

function getBookId(nIdx){return "B_"+nIdx;}

function getItemId(nIdx){return "I_"+nIdx;}

function markBook(nIdx)
{
	var obj=getElement(getItemId(nIdx));
	if(obj==null)
		obj=getElement(getBookId(nIdx));
	if(gbNav6)
	{
		gobj=obj;
		setTimeout("delayMarkObj();",1);
	}
	else
		markObj(obj);
}

function delayMarkObj()
{
	if(gobj)
	{
		markObj(gobj);
		gobj=null;
	}
}

function markObj(obj)
{
	if(obj!=null)
	{
		HighLightElement(obj,gsABgColor,"transparent");
		var sPath=calTocPath(obj);
		if(gsCTPath!=sPath)
			gsCTPath=sPath;
	}
}

function markItem(nIdx)
{
	var obj=getElement(getItemId(nIdx));
	if(gbNav6)
	{
		gobj=obj;
		setTimeout("delayMarkObj();",1);
	}
	else
		markObj(getElement(getItemId(nIdx)));
}

function calTocPath(obj)
{
	var sPath=getInnerText2(obj);
	var pObj=getParentNode(obj);
	do
	{
		while(pObj!=null&&!isCBId(pObj.id)) pObj=getParentNode(pObj);
		if(pObj!=null)
		{
			var nId=getIdByCBId(pObj.id);
			var sObj=getElement(getPBId(nId));
			if(sObj!=null)
			{
				var objs=getItemsByBook(sObj);
				for(var i=0;i<objs.length;i++)
				{
					var sText=getInnerText2(objs[i]);
					if(sText.length!=0)
					{
						sPath=sText+gsPathSplit+sPath;
						break;
					}
				}
			}
			pObj=getParentNode(pObj);
		}
	}while(pObj!=null);
	return sPath;
}

function writeAnItem(nIdx)
{
	var sTarget=_textToHtml_nonbsp(getTopicTarget(nIdx));
	var sIcon=getItemIcon(nIdx,0);
	if(sIcon=="")
	{
		var nItemType=getItemType(nIdx);
		if(nItemType&2)
			sIcon=getItemImage(nIdx,false);
		else
			sIcon=getItemImage(nIdx,true);
	}
	sIcon=_textToHtml_nonbsp(sIcon);
	var sName=_textToHtml(getItemName(nIdx));
	var sHTML="<p><nobr><a id=\""+getItemId(nIdx)+"\" onfocus =\"markItem("+nIdx+");\" onclick=\"markItem("+nIdx+");\""
	var sAltString="";
	if(nItemType&2)
		sAltString="Page";
	else
		sAltString="Remote Page";
	if(sTarget!="")
		sHTML+="target=\""+sTarget+"\" ";
	sHTML+="href=\""+_textToHtml_nonbsp(getItemURL(nIdx))+"\" title=\""+sName+"\"><img alt=\""+sAltString+"\" src=\""+sIcon+"\" border=0 align=\"absmiddle\">&nbsp;"+sName+"</a></nobr></p>";
	return sHTML;
}

function writeBookItems(nIdx,num)
{
	var sHTML="";
	if(num>0){
		var nCIdx=0;
		do{
			nCIdx++;
			var i=nIdx+nCIdx;
			var nItemType=getItemType(i);
			if(nItemType==1||nItemType==4||nItemType==8){
				sHTML+=writeBook(i);	
				nCIdx+=getItemContentsNum(i);		
			}
			else if(nItemType==2||nItemType==16){
				sHTML+=writeAnItem(i);
			}
		}
		while(nCIdx<num);
	}
	return sHTML;
}

function tocExpandHelpSet(nIdx,bLocal)
{
	checkBookItem(nIdx);
	removeLoadingDiv();
	if(!isHSLoad(nIdx))
	{
		setHSLoad(nIdx);
		var sSrc="";
		if(bLocal){
			var oChunk=getChunk(nIdx);
			if(oChunk)
			{
				goChunk=addTocChunk(oChunk.sPPath,oChunk.sDPath);
				sSrc=oChunk.aTocs[nIdx-oChunk.nMI].sRefURL;
			}
		}
		else{
			sSrc=getRefURL(nIdx);
			var nProj=getProject(sSrc);
			if(nProj!=-1)
			{
				sSrc=gaRoot[nProj].sToc;
				if(sSrc)
					goChunk=addTocChunk(gaProj[nProj].sPPath,gaProj[nProj].sDPath);
				else
					goChunk=null;
			}
			else
				goChunk=null;
		}
		if(goChunk)
		{
			PrepareLoading(nIdx);
			gbToc=false;
			loadData2(goChunk.sPPath+goChunk.sDPath+sSrc);
		}
		else
		{
			gbLData=false;
			checkFillStub();
		}
	}
}

function getProject(sSrc)
{
	for(var i=0;i<gaProj.length;i++)
		if(isSamePath(getPath(sSrc),gaProj[i].sPPath))
			return i;
	return -1;
}

function getPath(sPath)
{
	if(sPath!="")
	{
		sPath=_replaceSlash(sPath);
		var nPosFile=sPath.lastIndexOf("/");
		sPath=sPath.substring(0,nPosFile+1);
	}
	return sPath;
}

function isSamePath(sPath1,sPath2)
{
	return (sPath1.toLowerCase()==sPath2.toLowerCase());
}

function PrepareLoading(nIdx)
{
	gnInsIdx=nIdx;
	if(!gsTP)
	{
		var oObj=getElement(getPBId(gnInsIdx));
		if(oObj)
			oObj.insertAdjacentHTML("afterEnd",writeLoadingDiv(nIdx));
	}
}

function writeLoadingDiv(nIdx)
{
	return"<div id=\""+gsLoadingDivID+"\" class=parent onclick=\"removeLoadingDiv();\" style=\"padding-left:4px;background-color:ivory;border-width:1;border-style:solid;border-color:black;width:150px;\">"+gsLoadingMsg+"</div>";
}

function getItemName(nIdx)
{
	var oChunk=getChunk(nIdx);
	if(oChunk)
		return oChunk.aTocs[nIdx-oChunk.nMI].sItemName;
	else
		return null;
}

function getItemContentsNum(nIdx)
{
	var oChunk=getChunk(nIdx);
	if(oChunk)
		return oChunk.aTocs[nIdx-oChunk.nMI].nContents;
	else
		return null;
}

function getItemType(nIdx)
{
	var oChunk=getChunk(nIdx);
	if(oChunk)
		return oChunk.aTocs[nIdx-oChunk.nMI].nType;
	else
		return 0;
}

function getItemURL(nIdx)
{
	var oChunk=getChunk(nIdx);
	if(oChunk)
	{
		var sPath=oChunk.aTocs[nIdx-oChunk.nMI].sItemURL;
		if(!(sPath==null||sPath==""))
		{
			return _getFullPath(oChunk.sPPath,sPath);
		}
	}
	return "";
}

function getRefURL(nIdx)
{
	var oChunk=getChunk(nIdx);
	if(oChunk)
	{
		var sPath=oChunk.aTocs[nIdx-oChunk.nMI].sRefURL;
		if(!(sPath==null||sPath==""))
		{
			return _getFullPath(oChunk.sPPath,sPath)
		}
	}
	return "";
}

function getTopicTarget(nIdx)
{
	var oChunk=getChunk(nIdx);
	if(oChunk)
	{
		if(typeof(oChunk.aTocs[nIdx-oChunk.nMI].sTarget)!="undefined")
			return oChunk.aTocs[nIdx-oChunk.nMI].sTarget;
	}
	return "";
}

function getItemIcon(nIdx,nIconIdx)
{
	var oChunk=getChunk(nIdx);
	if(oChunk)
	{
		if(typeof(oChunk.aTocs[nIdx-oChunk.nMI].sIconRef)!="undefined")
		{
			var sIconRef=oChunk.aTocs[nIdx-oChunk.nMI].sIconRef;
			var nIndex=sIconRef.indexOf(";");
			while(nIconIdx-->0&&nIndex!=-1)
			{
				sIconRef=sIconRef.substring(nIndex+1);
				nIndex=sIconRef.indexOf(";");
			}
			if(nIconIdx<0)
			{
				if(nIndex!=-1)
					sIconRef=sIconRef.substring(0,nIndex);
				return _getFullPath(oChunk.sPPath,sIconRef)
			}
		}
	}
	return "";
}

function TocWriteClassStyle()
{
	var sStyle="<STYLE TYPE='text/css'>\n";
	if(gsBgImage)
		sStyle+="body {border-top:"+gsBgColor+" 1px solid;}\n";
	else
		sStyle+="body {border-top:black 1px solid;}\n";
	sStyle+="P {"+getFontStyle(goFont)+"margin-top:"+gsMargin+";margin-bottom:"+gsMargin+";}\n";
	sStyle+="DIV {margin-top:"+gsMargin+";margin-bottom:"+gsMargin+";}\n";
	sStyle+=".parent {margin-left:0pt;}\n";
	sStyle+=".stub {margin-left:0pt;display:none}\n";
	sStyle+=".child {display:none;margin-left:"+gsIndent+";}\n";
	sStyle+="A:link {"+getFontStyle(goFont)+"}\n";
	sStyle+="A:visited {"+getFontStyle(goFont)+"}\n";
	sStyle+="A:active {background-color:"+gsABgColor+";}\n";
	sStyle +="A:hover {"+getFontStyle(goHFont)+"}\n";
	sStyle+="</STYLE>";
	document.write(sStyle);
}

function TocWriteFixedWidth(bBegin,nWidth)
{
	if((gbIE4)&&(gbMac)&&(!gbIE5)){
		if(bBegin)
			document.write("<table width="+nWidth+" border=0><tr><td>");
		else
			document.write("</td></tr></table>");
	}
}

function TocInitPage()
{
	var tempColl=getItemsByBook(document.body);
	if(tempColl.length>0)
		tempColl[0].focus();
}

function getItemsFromObj(obj)
{
	var aAnchor=new Array();
	var tempColl=getChildrenByTag(obj,"P");
	if(tempColl&&tempColl.length>0)
	{
		var anobr=new Array();
		for(var i=0;i<tempColl.length;i++)
		{
			var tempNobr=getChildrenByTag(tempColl[i],"NOBR");
			if(tempNobr&&tempNobr.length>0)
				for(var j=0;j<tempNobr.length;j++)
					anobr[anobr.length]=tempNobr[j];
		}
		for(var s=0;s<anobr.length;s++)
		{
			var tempAnchor=getChildrenByTag(anobr[s],"A");
			if(tempAnchor&&tempAnchor.length>0)
				for(var u=0;u<tempAnchor.length;u++)
					aAnchor[aAnchor.length]=tempAnchor[u];
		}
	}
	return aAnchor;
}

function getItemsByBook(obj)
{
	var aAnchor=new Array();
	var aTAnchor=getItemsFromObj(obj);
	for(var i=0;i<aTAnchor.length;i++)
		aAnchor[aAnchor.length]=aTAnchor[i];
	var tempBook=getChildrenByTag(obj,"DIV");
	if(tempBook&&tempBook.length>0)
		for(var j=0;j<tempBook.length;j++)
		{
			var aTAnchorDiv=getItemsFromObj(tempBook[j]);
			for(var s=0;s<aTAnchorDiv.length;s++)
				aAnchor[aAnchor.length]=aTAnchorDiv[s];
		}
	return aAnchor;
}

function ExpandIt(nId)
{
	if(!gsTP)
		ExpandIt2(nId,false);
}

function ExpandIt2(nId,bForceOpen)
{
	var oC=TocExpand(nId,true,bForceOpen);
	var nNewScroll=document.body.scrollTop;
	if(oC.style.display=="block"){
		var nTop=oC.offsetTop;
		var nBottom=nTop+oC.offsetHeight;
		if(document.body.scrollTop+getClientHeight()<nBottom){
			nNewScroll=nBottom-getClientHeight();
		}
		if(nBottom-nTop>getClientHeight()){
			nNewScroll=nTop-20;
		}
	}
	document.body.scrollTop=nNewScroll;
}

function TocExpand(nId,bChangeImg,bForceOpen)
{
	var oDiv=getElement(getCBId(nId));
	if(oDiv==null) return null;

	var whichIm=document.images[getBId(nId)];
	if((oDiv.style.display!="block")||bForceOpen){
		oDiv.style.display="block";
		if(bChangeImg){
			var sPath=getPath(whichIm.src);
			sPath=_getFullPath(sPath,getBookImage(nId,false));
			whichIm.src=sPath;		
		}
	}else{
		oDiv.style.display="none";
		if(bChangeImg){
			var sPath=getPath(whichIm.src);
			sPath=_getFullPath(sPath,getBookImage(nId,true));
			whichIm.src=sPath;
		}
		if(gbMac&&gbIE5){
			this.parent.document.getElementById("tocIFrame").style.width="101%";
			this.parent.document.getElementById("tocIFrame").style.width="100%";
		}
	}
	return oDiv;
}

function getChunkId(n)
{
	var nCan=-1;
	for(var i=0;i<gaTocs.length;i++)
		if(gaTocs[i].nMI<=n&&gaTocs[i].nMI!=-1)
		{
			if(nCan==-1)
				nCan=i;
			else
				if(gaTocs[i].nMI>=gaTocs[nCan].nMI)
					nCan=i;
		}
	if(nCan!=-1)
		return nCan;
	else
		return -1;
}

function getChunk(n)
{
	if(gnCC!=-1&&gaTocs[gnCC].nMI<=n&&(gnCC==gaTocs.length-1||
		gaTocs[gnCC+1].nMI>n))
	{	
		return gaTocs[gnCC];
	}
	else{
		gnCC=getChunkId(n);
		if(gnCC!=-1)
			return gaTocs[gnCC];
		else
			return null;
	}
}

function getBookImage(nIdx,bClosed)
{
	var nIdx=bClosed?0:1;
	var sIcon=getItemIcon(nIdx,nIdx);
	if(sIcon=="")
		if(bClosed)
			sIcon=giBookClose;
		else
			sIcon=giBookOpen;
	return _getFullPath(gaProj[0].sPPath,sIcon);
}

function getItemImage(nIdx,bRemote)
{
	var sIcon=getItemIcon(nIdx,0);
	if(sIcon=="")
		if(bRemote)
			sIcon=giURLItem;
		else
			sIcon=giBookItem;
	return _getFullPath(gaProj[0].sPPath,sIcon);
}

function getInnerText2(obj)
{
	var sText=getInnerText(obj);
	if(sText.length>0&&!gbOpera7)
		sText=sText.substring(1);
	return sText;
}

function expandToc(oObj,sRest,aIdList)
{
	var len=aIdList.length;
	var nPos=sRest.indexOf(gsPathSplit);
	if(nPos!=-1)
	{
		sPart=sRest.substring(0,nPos);
		sRest=sRest.substring(nPos+1);
	}
	else
	{
		sPart=sRest;
		var aTagAs=getItemsByBook(oObj);
		for(var s=0;s<aTagAs.length;s++)
		{
			var sText=getInnerText2(aTagAs[s]);
			if(sText==sPart)
			{
				aIdList[len]=aTagAs[s];
				return 1;
			}
		}
		return 0;
	}
		
	var aChildren=getChildrenByTag(oObj,"DIV");
	for(var i=0;i<aChildren.length;i++)
	{
		var sPId=aChildren[i].id;
		if(!isPBId(sPId))
			continue;
		var sText=getInnerText2(aChildren[i]);
		sText = sText.replace("\n", "");
		if(sText!=sPart)
			continue;
		aIdList[len]=getIdByPBId(sPId);
		var sCId=getCBId(aIdList[len]);
		var oCObj=getElement(sCId);
		if(oCObj)
		{
			if(oCObj.innerHTML=="")
			{
				var obj=getItemsByBook(aChildren[i]);
				if(obj.length>0)
				{
				  	if(gbNav6 || gbSafari3)
					{
					    if(gbNav6 )
					    {
						    var sCommand=obj[0].getAttribute("onClick");
						    var nCommand=sCommand.indexOf(";");
						    sCommand=sCommand.substring(0,nCommand);
					    }
					    else if(gbSafari3)
					    {
						    var sCommand=obj[0].getAttribute("onClick");
						    var nCommand1=sCommand.indexOf(";");
						    var nCommand2=sCommand.indexOf(";", nCommand1+1);
						    sCommand=sCommand.substring(nCommand1+1, nCommand2);
					    }
					    var indx1 = sCommand.indexOf("(");
						var indx2 = sCommand.indexOf(",", indx1);
						var arg1 = sCommand.substring(indx1+1, indx2);
						indx1 = indx2;
						indx2 = sCommand.indexOf(")", indx1);
						var arg2 = sCommand.substring(indx1+1, indx2);
						n1 = parseInt(arg1);
						n2 = parseInt(arg2);
						insertBookItems(n1, n2);
					}
					else
						obj[0].click();
				}
				return -1;
			}
			var nRet=expandToc(oCObj,sRest,aIdList);
			if(nRet)
				return nRet;
		}
	}
	aIdList.length=len;
	return 0;
}

function getIdByPBId(sPId)
{
	return parseInt(sPId.substring(2,sPId.length-1));
}

function getIdByCBId(sCId)
{
	return parseInt(sCId.substring(2,sCId.length-1));
}

function isPBId(sId)
{
	return (sId&&sId.indexOf("B_")==0&&sId.lastIndexOf("P")==sId.length-1);
}

function isCBId(sId)
{
	return (sId&&sId.indexOf("B_")==0&&sId.lastIndexOf("C")==sId.length-1);
}

function getBId(nIdx)
{
	return "B_"+nIdx;
}

function getPBId(nIdx)
{
	return getBId(nIdx)+"P";
}

function getCBId(nIdx)
{
	return getBId(nIdx)+"C";
}

function getClosestTocPath(aPaths)
{
	var nMaxSimilarity=0;
	var nThatIndex=-1;
	var sPath=null;
	if(aPaths.length==0) return sPath;
	for(var i=0;i<aPaths.length;i++)
	{
		var nSimilarity=comparePath(gsCTPath,aPaths[i]);
		if(nSimilarity>nMaxSimilarity)
		{
			nMaxSimilarity=nSimilarity;
			nThatIndex=i;
		}
	}
	if(nThatIndex!=-1)
		sPath=aPaths[nThatIndex];
	else
		sPath=aPaths[0];
	return sPath;
}

function comparePath(sPath1,sPath2)
{
	var nMaxSimilarity=0;
	var nStartPos1=0;
	var nPos1=-1;
	var nStartPos2=0;
	var nPos2=-1;
	do{
		var sCheck1=null;
		var sCheck2=null;
		nPos1=sPath1.indexOf(gsPathSplit,nStartPos1);
		if(nPos1!=-1)
		{
			sCheck1=sPath1.substring(nStartPos1,nPos1);
			nStartPos1=nPos1+1;
		}
		else
		{
			sCheck1=sPath1.substring(nStartPos1);
			nStartPos1=-1;
		}
		nPos2=sPath2.indexOf(gsPathSplit,nStartPos2);
		if(nPos1!=-1)
		{
			sCheck2=sPath2.substring(nStartPos2,nPos2);
			nStartPos2=nPos2+1;
		}
		else
		{
			sCheck2=sPath2.substring(nStartPos2);
			nStartPos2=-1;
		}
		if(sCheck1==sCheck2)
			nMaxSimilarity++;
		else
			break;
	}while(nStartPos1!=-1&&nStartPos2!=-1);
	return nMaxSimilarity;
}

function getTocPaths(oTopicParam)
{
	var aRelTocPaths=oTopicParam.aPaths;
	var aPaths=new Array();
	for(var i=0;i<gaProj.length;i++)
		if(isSamePath(gaProj[i].sPPath,oTopicParam.sPPath))
		{
			for(var j=0;j<aRelTocPaths.length;j++)
				for (var k=0;k<gaRoot[i].aRPath.length;k++)
				{
					var sPath=gaRoot[i].aRPath[k]+aRelTocPaths[j];
					aPaths[aPaths.length]=sPath.substring(1);
				}
			break;
		}
	return aPaths;
}

function syncInit()
{
	if(gsTP&&!gbNeedFillStub)
	{
		gInSync=true;
		var obj=document.body;
		var aIdList=new Array();
		var nRet=expandToc(obj,gsTP,aIdList);
		if(nRet!=-1)
		{
			if(nRet==1)
			{
				if(aIdList.length)
					for(var i=0;i<aIdList.length-1;i++)
						ExpandIt2(aIdList[i],true);
				gsCTPath=gsTP;
				HighLightElement(aIdList[aIdList.length-1],gsABgColor,"transparent");
				aIdList[aIdList.length-1].focus();
				gsTP=null;
			}
			if(gaBTPs!=""&&gaBTPs!=null)
			{
				var aPaths=gaBTPs;
				gsTP=null;
				gaBTPs=null;
				if(aPaths!=null)
				{
					var sPath=getClosestTocPath(aPaths);
					if(sPath!=null)
					{	
						gsTP=sPath;		
						setTimeout("syncInit()",1);
					}
				}
			}
		}
		gInSync=false;
	}
}

function loadToc()
{
	if(!gbLoadToc)
	{
		var oResMsg=new whMessage(WH_MSG_GETPROJINFO,this,1,null);
		if(SendMessage(oResMsg)&&oResMsg.oParam)
		{
			gbLoadToc=true;
			var oProj=oResMsg.oParam;
			gaProj=oProj.aProj;
			gbXML=oProj.bXML;
			load1B1();
		}
	}
}

function load1B1()
{
	if(gnLT+1<gaProj.length)
		for(var i=gnLT+1;i<gaProj.length;i++)
			if(gaProj[i].sToc)
			{
				gbToc=true;
				gnLT=i;
				setTimeout("loadTocInfo()",1);
				return true;
			}
	return false;
}

function loadTocInfo()
{
	loadData2(gaProj[gnLT].sPPath+gaProj[gnLT].sDPath+gaProj[gnLT].sToc);
}

function loadData2(sFile)
{
	if(gbXML)
		loadDataXML(sFile);
	else
		loadData(sFile);
}

function projReady(sRoot,aProj)
{
	if(gaRoot.length<=gnLT||!gaRoot[gnLT])
		gaRoot[gnLT]=new Object();
	gaRoot[gnLT].sToc=sRoot;
	
	if(gnLT==0)
	{
		gaRoot[gnLT].aRPath=new Array();
		gaRoot[gnLT].aRPath[0]=gsPathSplit;
	}

	updatePTPath(gnLT,aProj);

	if(!((gnLT+1<gaProj.length)&&load1B1()))
	{
		gbReady=true;
		if(gbIE4)
			setTimeout("loadImages();",1);
		else
			setTimeout("loadTData();",1);
	}
}

function loadTData()
{
	if(gaProj[0].sToc!="")
	{
		gbLData=true;
		goChunk=addTocChunk(gaProj[0].sPPath,gaProj[0].sDPath);
		gbToc=false;
		loadData2(gaProj[0].sPPath+gaProj[0].sDPath+gaRoot[0].sToc);
	}
}

function updatePTPath(n,aProj)
{
	if(aProj)
		for(var i=0;i<aProj.length;i++)
		{
			var sFullPath=_getFullPath(gaProj[n].sPPath,aProj[i].sPPath);
			for(var j=0;j<gaProj.length;j++)
				if(isSamePath(sFullPath,gaProj[j].sPPath))
				{
					if(gaRoot.length<=j||!gaRoot[j])
						gaRoot[j]=new Object();
					if(!gaRoot[j].aRPath)
						gaRoot[j].aRPath=new Array();

					if(gaRoot[n].aRPath)
						for(var k=0;k<gaRoot[n].aRPath.length;k++)
						{
							var bDup=false;
							var sTFPath=gaRoot[n].aRPath[k]+aProj[i].sRPath;
							for(var l=0;l<gaRoot[j].aRPath.length;l++)
								if(gaRoot[j].aRPath[l]==sTFPath)
								{
									bDup=true;
									break;
								}
							if(!bDup)
								gaRoot[j].aRPath[gaRoot[j].aRPath.length]=sTFPath;
						}
					else
						gaRoot[j].aRPath[gaRoot[j].aRPath.length]=aProj[i].sRPath;
					break;
				}
		}
}

function onLoadXMLError()
{
	if(gbToc)
	{
		var sRoot="";
		var aRProj=new Array();
		projReady(sRoot,aRProj);
	}
	else
	{
		var aToc=new Array();
		putData(aToc)
	}
}

function putDataXML(xmlDoc,sDocPath)
{
	if(gbToc)
	{
		var tocNode=xmlDoc.getElementsByTagName("toc")[0];
		if(tocNode)
		{
			var sRoot=tocNode.getAttribute("root");
			var rmtProject=tocNode.getElementsByTagName("project");
			var aRProj=new Array();
			if(rmtProject.length>0)
			{
				for(var i=0;i<rmtProject.length;i++)
				{
					aRProj[i]=new Object();
					var sURL=rmtProject[i].getAttribute("url");
					if(sURL)
					{
						if(sURL.lastIndexOf("/")!=sURL.length-1)
							sURL+="/";						
					}
					aRProj[i].sPPath=sURL;
					aRProj[i].sRPath = "";
					var oSubPath = rmtProject[i].getElementsByTagName("subpath")[0];
					if (oSubPath)
					{
						while (oSubPath)
						{
							aRProj[i].sRPath += oSubPath.getAttribute("name") + "\n";
							oSubPath = oSubPath.getElementsByTagName("subpath")[0];
						}
					}
					else
						aRProj[i].sRPath=rmtProject[i].getAttribute("path");
				}
			}
			projReady(sRoot,aRProj);
		}
	}
	else
	{
		var chunkNode=xmlDoc.getElementsByTagName("tocdata")[0];
		if(chunkNode)
		{
			var aToc=new Array();
			processBook(chunkNode,aToc);
			putData(aToc);
		}
	}
}

function processBook(node,aToc)
{
	var i=0;
	var entry=null;
	var prevEntry=null;
	var oChild=node.firstChild;
	do{
		if(oChild)
		{
			if(oChild.nodeName.indexOf("#")!=0)
			{
				var sName=oChild.getAttribute("name");
				var sURL=oChild.getAttribute("url");
				var sRef=oChild.getAttribute("ref");
				var sTarget=oChild.getAttribute("target");
				var sIcons=oChild.getAttribute("images");
				var item=new Object();
				item.sItemName=sName;
				if(sTarget)
					item.sTarget=sTarget;
				if(sIcons)
					item.sIconRef=sIcons;
				if(sURL==null) sURL="";

				item.sItemURL=sURL;
				
				if(oChild.nodeName=="book")
				{
					item.nType=1;
					aToc[aToc.length]=item;
					var nCurrPos=aToc.length;
					processBook(oChild,aToc);
					item.nContents=aToc.length-nCurrPos;
				}
				else if(oChild.nodeName=="item")
				{
					item.nType=2;
					item.nContents=0;
					aToc[aToc.length]=item;
				}
				else if(oChild.nodeName=="remoteitem")
				{
					item.nType=16;
					item.nContents=0;
					aToc[aToc.length]=item;
				}
				else if(oChild.nodeName=="project")
				{
					if(sRef)
					{
						if(sRef.lastIndexOf("/")!=sRef.length-1)
							sRef+="/";						
					}
					item.nType=4;
					item.sRefURL=sRef;
					item.nContents=0;
					aToc[aToc.length]=item;
				}
				else if(oChild.nodeName=="chunk")
				{
					item.nType=8;
					item.sRefURL=sRef;
					item.nContents=0;
					aToc[aToc.length]=item;
				}
			}
		}
		else
			break;
		oChild=oChild.nextSibling;
	}while(true);
}

function putData(aTocs)
{
	gaTocsNs61Fix=aTocs;
	setTimeout("realPutData();",1);
}

function realPutData()
{
	removeLoadingDiv();
	var aTocs=gaTocsNs61Fix;
	if(!aTocs) return;
	if(goChunk)
	{
		var n=gnMinIdx;
		goChunk.nMI=gnMinIdx;
		goChunk.aTocs=aTocs;
		gnMinIdx+=aTocs.length;
		if(gnInsIdx!=-1)
		{
			var oObj=getElement(getPBId(gnInsIdx));
			if(oObj)
			{
				oObj.insertAdjacentHTML("afterEnd",writeBookItems(n-1,aTocs.length));
				setTimeout("syncInit()",1);
			}
		}
		else{
			document.body.insertAdjacentHTML("beforeEnd",writeBookItems(n-1,aTocs.length));
			var oParam=new Object();
			oParam.oTocInfo=null;
			var oMsg=new whMessage(WH_MSG_GETTOCPATHS,this,1,oParam);
			if(SendMessage(oMsg))
			{
				if(oMsg.oParam.oTocInfo)
					syncWithPaths(oMsg.oParam.oTocInfo);
			}
		}	
	}
	gbLData=false;
	checkFillStub();
}

function syncWithPaths(oTopicParam)
{
	var aPaths=getTocPaths(oTopicParam);
	if(gsTP)
		gaBTPs=aPaths;
	else{
		var sPath=getClosestTocPath(aPaths);
		if(sPath!=null)
		{
			gsTP=sPath;
			setTimeout("syncInit()",1);
		}
	}
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
	loadToc();
	var oMsg=new whMessage(WH_MSG_SHOWTOC,this,1,null)
	SendMessage(oMsg);
}

function loadImages()
{
	if(giBookClose)
	{
		gaImgs[gnImages]=giBookClose;
		gnImages++;
	}		
	if(giBookOpen)
	{
		gaImgs[gnImages]=giBookOpen;
		gnImages++;
	}		
	if(giBookItem)
	{
		gaImgs[gnImages]=giBookItem;
		gnImages++;
	}		
	if(giURLItem)
	{
		gaImgs[gnImages]=giURLItem;
		gnImages++;
	}		
	if(giNewBookClose)
	{
		gaImgs[gnImages]=giNewBookClose;
		gnImages++;
	}		
	if(giNewBookOpen)
	{
		gaImgs[gnImages]=giNewBookOpen;
		gnImages++;
	}		
	if(giNewBookItem)
	{
		gaImgs[gnImages]=giNewBookItem;
		gnImages++;
	}		
	if(giNewURLItem)
	{
		gaImgs[gnImages]=giNewURLItem;
		gnImages++;
	}
	if(gnImages>0)
	{
		setTimeout("loadDataAfter();",1000);
		loadImage(gaImgs[0]);
	}
	else
		loadDataAfter();
}

function loadImage(sURL)
{
	var oImg=new Image();
	oImg.onload=checkImageLoading;
	oImg.onerror=errorImageLoading;
	oImg.src=_getFullPath(gaProj[0].sPPath,sURL);
}

function loadDataAfter()
{
	if(!gbLoadData)
	{
		gbLoadData=true;
		loadTData();
	}
}

function errorImageLoading()
{
	gnLoadedImages++;
	if(gnImages==gnLoadedImages)
		loadDataAfter();
	else
		loadImage(gaImgs[gnLoadedImages]);	
}

function checkImageLoading()
{
	gnLoadedImages++;
	if(gnImages==gnLoadedImages)
		loadDataAfter();
	else
		loadImage(gaImgs[gnLoadedImages]);	
}

function window_unload()
{
	UnRegisterListener2(this,WH_MSG_PROJECTREADY);
	UnRegisterListener2(this,WH_MSG_SYNCTOC);
	UnRegisterListener2(this,WH_MSG_SHOWTOC);
}

function onSendMessage(oMsg)
{
	if(oMsg)
	{
		var nMsgId=oMsg.nMessageId;
		if(nMsgId==WH_MSG_PROJECTREADY)
		{
			loadToc();
		}
		else if(nMsgId==WH_MSG_SYNCTOC)
		{
			if(gbReady)
			{
				syncWithPaths(oMsg.oParam);
			}
		}
		else if(nMsgId==WH_MSG_SHOWTOC)
		{
			if(!gbNav6)
				document.body.focus();
		}
	}
	return true;
}

if(window.gbWhUtil&&window.gbWhVer&&window.gbWhMsg&&window.gbWhProxy)
{
	RegisterListener2(this,WH_MSG_PROJECTREADY);
	RegisterListener2(this,WH_MSG_SYNCTOC);
	RegisterListener2(this,WH_MSG_SHOWTOC);
	goFont=new whFont("Verdana","8pt","#000000","normal","normal","none");
	goHFont=new whFont("Verdana","8pt","#007f00","normal","normal","underline");

	window.onload=window_OnLoad;
	window.onbeforeunload=window_BUnload;
	window.onunload=window_unload;
	gbWhTHost=true;
}
else
	document.location.reload();
