//	WebHelp 5.10.002
var gaChunks=new Array();
var gaFakes=new Array();
var gaDataCon=null;
var gaData=new Array();

var gbFindCK=false;;
var gbNeedCalc=false;
var gbScrl=false;
var gbProcess=false;
var gbReady=false;

var gnCheck=0;
var gnNum=0;
var gnIns=-1;
var gnLoad=0;
var gnRef=-1;
var gnMaxItems=0;
var gnMaxMargin=32000;
var gnNeeded=0;
var gnNKI=-1;
var gnRE=0;
var gnScrlMgn=30;
var gnSE=0;
var gnVisible=0;
var gnItems=0;
var gnUHeight=1;

var gsBCK=null;
var gsChK=null;
var gsCK=null;
var gsLoadingDivID="LoadingDiv";
var gsLoadingMsg="Loading data, please wait...";
var gsSKA=null;
var gsSKB=null;

var gbLoadInfo=false;

function onLoadXMLError()
{
	if(gbLoadInfo)
	{
		var aChunk=new Array();
		projReady(aChunk);
	}
	else
	{
		var aData=new Array();
		putData(aData);
	}
}

function putDataXML(xmlDoc,sDocPath)
{
	if(gbLoadInfo)
	{
		var node=xmlDoc.lastChild;
		if(node)
		{
			var nTotal=0
			var aChunk=new Array();
			var oC=node.firstChild;
			while(oC)
			{
				if(oC.nodeName=="chunkinfo")
				{
					var item=new Object();
					item.sBK=oC.getAttribute("first");
					item.sEK=oC.getAttribute("last");
					item.sFileName=oC.getAttribute("url");
					item.nNum=parseInt(oC.getAttribute("num"));
					nTotal+=item.nNum;
					item.nTotal=nTotal;
					aChunk[aChunk.length]=item;
				}
				oC=oC.nextSibling;
			}
			projReady(aChunk);
		}
	}
	else
	{
		var node=xmlDoc.lastChild;
		if(node)
		{
			var aData=new Array();
			var nPrev=0;
			var nNext=0;
			var oC=node.firstChild;
			while(oC)
			{
				nPrev=nNext;
				if(oC.nodeName=="key")
				{
					var sName=oC.getAttribute("name");
					if(sName&&sName.length>0)
					{
						var sTarget=oC.getAttribute("target");
						var item=new Object();
						item.nType=2;
						item.sName=sName;
						if(sTarget)
							item.sTarget=sTarget;
						item.nPKOff=nPrev;
						aData[aData.length]=item;
						var nCurIndex=aData.length;
						processKey(oC,aData,item);
						nNext=aData.length-nCurIndex;
						item.nNKOff=nNext;
					}
				}
				else if(oC.nodeName=="letter")
				{
					var name=oC.getAttribute("name");
					if(name&&name.length>0)
					{
						var item=new Object();
						item.nType=1;
						item.sName=name;
						item.nPKOff=nPrev;
						nNext=0;
						item.nNKOff=nNext;
						aData[aData.length]=item;
					}
				}
				else if(oC.nodeName=="entry")
				{
					var name=oC.getAttribute("name");
					var def=oC.getAttribute("value");
					if(name&&name.length>0)
					{
						var item=new Object();
						item.sName=name;
						item.sDef=def;
						item.nPKOff=nPrev;
						nNext=0;
						item.nNKOff=nNext;
						aData[aData.length]=item;
					}
				}
				oC=oC.nextSibling;
			}
			putData(aData);
		}
	}
}

function processKey(element,aData,item)
{
	var i=0;
	var nPrev=0;
	var nNext=0;
	var oC=element.firstChild;
	while(oC)
	{
		if(oC.nodeName=="topic")
		{
			var name=oC.getAttribute("name");
			var url=oC.getAttribute("url");
			if(url&&url.length!=0)
			{
				if(!name||name.length==0)
					name=url;
				var topic=new Object();
				topic.sName=name;
				topic.sURL=url;
				if(!item.aTopics)
					item.aTopics=new Array();
				item.aTopics[item.aTopics.length]=topic;
			}
		}
		else if(oC.nodeName=="key")
		{
			nPrev=nNext;
			var name=oC.getAttribute("name");
			if(name&&name.length!=0)
			{
				var subItem=new Object();
				subItem.sName=name;
				subItem.nType=3;
				subItem.nPKOff=nPrev;
				aData[aData.length]=subItem;
				var nCurIndex=aData.length;
				processKey(oC,aData,subItem);
				nNext=aData.length-nCurIndex;
				subItem.nNKOff=nNext;
			}
		}
		oC=oC.nextSibling;
	}
}

function putData(aData)
{
	endLoading();
	var oCData=goCData;
	if(oCData)
	{
		oCData.aKs=aData;
		if(gnNKI==-1)
			setTimeout("checkReady();",1);
		else
		{
			gsSKA=getKByIdx(oCData,gnNKI);
			gbNeedCalc=true;
			gbScrl=true;
			gnNKI=-1;
			if(gsSKA)
				setTimeout("checkReady();",1);
			else
			{
				markEnd();
				setTimeout("checkAgain();",50);
			}
		}
	}
}

function markEnd()
{
	if(gbProcess)
		gbProcess=false;
}

function endLoading()
{
	var oDiv=getElement(gsLoadingDivID);
	if(oDiv)
		oDiv.style.visibility="hidden";
}

function markBegin()
{
	gbProcess=true;
}

function beginLoading()
{
	var oDiv=getElement(gsLoadingDivID);
	if(oDiv)
	{
		oDiv.style.top=document.body.scrollTop;
		oDiv.style.visibility="visible";
	}
}

function indexHTMLPart()
{
	this.sHTML="";
	this.nNeeded=0;
	this.nCurrent=0;
	this.nConsumed=0;
	this.sFK=null;
	this.sLK=null;
	this.addHTML=function(sHTML,nConsumed,bDown,bK,sK){
		if(bDown)
			this.sHTML+=sHTML;
		else
			this.sHTML=sHTML+this.sHTML;
		this.nCurrent++;			
		this.nConsumed+=nConsumed;
		if(bK)
		{
			if(!this.sFK)
				this.sFK=sK;
			if(!this.sLK)
				this.sLK=sK;
			if(bDown)
				this.sLK=sK;
			else
				this.sFK=sK;
		}
	}
	this.appendHTML=function(oHTML,bDown){
		this.addSubHTML(oHTML,bDown);
		if(!this.sFK)
			this.sFK=oHTML.sFK;
		if(!this.sLK)
			this.sLK=oHTML.sLK;
		if(bDown)
			this.sLK=oHTML.sLK;
		else
			this.sFK=oHTML.sFK;
	}	
	
	this.addSubHTML=function(oHTML,bDown){
		if(bDown)
			this.sHTML+=oHTML.sHTML;
		else
			this.sHTML=oHTML.sHTML+this.sHTML;
		this.nCurrent+=oHTML.nCurrent;
		this.nConsumed+=oHTML.nConsumed;			
	}
}

function getMaxUnits()
{
	return Math.floor(gnMaxMargin/gnUHeight)+1;
}

function getFakeItemsHTMLbyCount(nB,nCount)
{
	var nMU=getMaxUnits();
	var nNum=Math.floor(nCount/nMU);
	var sHTML="";
	for(var i=0;i<nNum;i++)
		sHTML+=getFakeItemHTML(nB,nMU-1);
		
	var nRest=nCount%nMU;
	sHTML+=getFakeItemHTML(nB,nRest-1);
	return sHTML;
}

function getFakeItemHTML(nB,nNum)
{
	return "<h6 name=fk"+nB+" id=fk"+nB+" style=\"margin-top:"+gnUHeight*nNum+";margin-bottom:0\">&nbsp;</h6>";
}

function fakeItemsArea(nB,n,sKA,sKB,obj)
{
	this.nB=nB;
	this.nNum=n;
	this.sKA=sKA;
	this.sKB=sKB;
	this.obj=obj;
	this.nMargin=(n-1)*gnUHeight;

	this.setNum=function(n)
	{
		var nLastobj=-1;
		var nDelta=this.nMargin;
		this.nMargin=(n-1)*gnUHeight;
		nDelta=nDelta-this.nMargin;
		if(n>0)
		{
			this.nNum=n;
			var nMU=getMaxUnits();
			nLastobj=Math.floor((n-1)/nMU);
			if(this.obj.length)
				this.obj[nLastobj].style.marginTop=((n-1)%nMU)*gnUHeight;
			else
				this.obj.style.marginTop=((n-1)%nMU)*gnUHeight;
		}
		if(this.obj.length)
		{
			for(var i=this.obj.length-1;i>nLastobj;i--)
				removeThis(this.obj[i]);
		}
		else
		{
			if(nLastobj==-1)
				removeThis(this.obj);
		}
		return nDelta;
	}
	this.insertAdjacentHTML=function(sWhere,sHTML)
	{
		if(sWhere=="beforeBegin")
		{
			if(this.obj.length)
				this.obj[0].insertAdjacentHTML(sWhere,sHTML);
			else
				this.obj.insertAdjacentHTML(sWhere,sHTML);
		}
		else if(sWhere=="afterEnd")
		{
			if(this.obj.length)
			{
				if(gbMac&&gbIE5&&this.obj[this.obj.length-1].nextSibling)
					this.obj[this.obj.length-1].nextSibling.insertAdjacentHTML("beforeBegin",sHTML);
				else
					this.obj[this.obj.length-1].insertAdjacentHTML(sWhere,sHTML);
			}
			else
			{
				if(gbMac&&gbIE5&&this.obj.nextSibling)
					this.obj.nextSibling.insertAdjacentHTML("beforeBegin",sHTML);
				else
					this.obj.insertAdjacentHTML(sWhere,sHTML);
			}
		}
	}
	this.getBtm=function()
	{
	if(gbSafari3)
	{
		if(this.obj.length)
			return findPosition(this.obj[this.obj.length-1]);
		else
			return findPosition(this.obj);
	}
		if(this.obj.length)
			return this.obj[this.obj.length-1].offsetTop;
		else
			return this.obj.offsetTop;
	}
	this.getTop=function()
	{
		return this.getBtm()-this.nMargin;
	}
}

function usedItems(nB,nE)
{
	this.nB=nB;
	this.nE=nE;
	this.oN=null;
}

function checkReady()
{
	var len=gaChunks.length;
	var bNeedLoad=false;
	var aDataCon;
	var s=0;
	var bDown=(gsSKB==null);
	var sK=bDown?gsSKA:gsSKB;
	if(sK==null)
	{
		markEnd();
		setTimeout("checkAgain();",50);
		return;
	}
	if(!gsChK||sK!=gsChK||gnNum==0)
	{
		gnCheck=0;
		gsChK=sK;
		aDataCon=new Array();
	}
	else{
		s=gnNum;
		aDataCon=gaDataCon;
	}
	for(var i=gnCheck;i<len;i++)
	{
		var oCData=getChunkedData(i,bDown,sK);
		if(oCData)
		{
			if(!oCData.aKs&&oCData.sFileName!=null)
			{
				bNeedLoad=true;
				goCData=oCData;
				gnNum=s;
				gnCheck=i;
				gaDataCon=aDataCon;
				oCData.nProjId=i;
				gbLoadInfo=false;
				beginLoading();
				loadData2(gaData[i].sPPath+gaData[i].sDPath+oCData.sFileName);
				return;
			}
			else{
				aDataCon[s++]=oCData;
			}
		}
	}
	if(!bNeedLoad)
	{
		gnNum=0;
		gsSKA=gsSKB=gsShowK=null;
		
		var oHTML=new indexHTMLPart();
		var aPos=new Array();
		var aOriPos=new Array();
		var aMaxPos=new Array();
		var aMinPos=new Array();
		
		if(gbNeedCalc||gbFindCK) gnIns=0;
		for(i=0;i<aDataCon.length;i++)
		{
			aPos[i]=getIdxPos(aDataCon[i],bDown,sK);
			if(gbNeedCalc||gbFindCK)
			{
				gnIns+=aPos[i]+aDataCon[i].nTotal-aDataCon[i].nNum;
				if(!bDown)
				{
					if(aPos[i]!=-1)
					{
						if(aDataCon[i].aKs)
							gnIns+=aDataCon[i].aKs[aPos[i]].nNKOff;
						else
						{
							var n=aPos[i]+1;
							while(n<aDataCon[i].aKsOnly.length&&!aDataCon[i].aKsOnly[n])
								n++;
							n=n-aPos[i]-1;
							gnIns+=n;
						}
					}
				}
			}
			aOriPos[i]=aPos[i];
			getLimit(aDataCon,aPos,aMaxPos,aMinPos,i);
		}
		if((gbNeedCalc||gbFindCK)&&!bDown&&gnIns!=-1)
		{
			gnIns+=(aDataCon.length-1)
		}
		if(gnIns!=-1||gbFindCK)
		{
			oHTML.nNeeded=gnNeeded;
			var bDone=writeItems(oHTML,aDataCon,aPos,aMinPos,aMaxPos,bDown,1);
			if(oHTML.nConsumed!=0)
			{
				var nB;
				if(!bDown)
					nB=gnIns-oHTML.nConsumed+1;
				else
					nB=gnIns;
					
				var oldScrollPos=document.body.scrollTop;
				if(insertIdxKs(nB,oHTML,gbScrl))
				{
					updateUsedK(aDataCon,aOriPos,aPos,bDown);
					if(!gbScrl&&gbMac)
					{
						while(document.body.scrollTop!=oldScrollPos)
							document.body.scrollTop=oldScrollPos;
					}
					gbScrl=false;
				}
				if(gbFindCK)
				{
					gbFindCK=false;
					gbNeedCalc=true;
					gsSKB=oHTML.sFK;
					gnIns=-1;
					setTimeout("checkReady();",50);
					return;
				}
			}
			else if(gbFindCK)
			{
				gbFindCK=false;
				gbNeedCalc=true;
				gsSKB=getFirstKeyFromPos(aDataCon,aPos);
				gnIns=-1;
				setTimeout("checkReady();",50);
				return;
			}
			if(!findCKInDom()) return;

			gnNeeded=gnNeeded-oHTML.nCurrent;
			gnIns=-1;
			markEnd();
			setTimeout("checkAgain();",50);
			gbNeedCalc=false;
		}
		else
		{
			if(!findCKInDom()) return;
			markEnd();
			setTimeout("checkAgain();",50);
		}
	}
}

function getFirstKeyFromPos(aDataCon,aPos)
{
	var sCurrentK=getBiggestChar();
	for(var i=0;i<aPos.length;i++)
	{
		if(aDataCon[i].aKs&&aDataCon[i].aKs.length>0&&aPos[i]>=0&&aPos[i]<aDataCon[i].aKs.length)
		{
			if(sCurrentK==""||
				compare(sCurrentK,aDataCon[i].aKs[aPos[i]].sName)>0)
			{
				sCurrentK=aDataCon[i].aKs[aPos[i]].sName;
			}
		}
	}
	return sCurrentK;
}

function checkAgain()
{
	if(!gbProcess)
	{
		if(gsBCK!=null)
		{
			gsCK=gsBCK;
			gsBCK=null;
			findCK();
		}
		else
		{
			markBegin();
		    getUnitIdx(document.body.scrollTop,getClientHeight());
		}
	}
	else
		setTimeout("checkAgain()",50);
}

function getLimit(aDataCon,aPos,aMaxPos,aMinPos,i)
{
	aMaxPos[i]=aDataCon[i].nNum;
	aMinPos[i]=-1;
	var oPNode=null;
	if(aDataCon[i].oUsedItems)
	{
		var oUsedItems=aDataCon[i].oUsedItems;
		do{
			if(oUsedItems.nB>aPos[i])
			{
				aMaxPos[i]=oUsedItems.nB;
				break;
			}
			oPNode=oUsedItems;
			oUsedItems=oUsedItems.oN;
		}while(oUsedItems!=null);
		if(oPNode)
			aMinPos[i]=oPNode.nE;
	}
	else if(aDataCon[i].aKs==null)
	{
		aMaxPos[i]=aMinPos[i]=aPos[i];
	}
	if(aMinPos[i]>=aPos[i]||aMaxPos[i]<=aPos[i])
	{
		aMaxPos[i]=aMinPos[i]=aPos[i];
	}
}

function getIdxPos(oIdx,bDown,sK)
{
	var aKs=oIdx.aKs;
	var nIdx;
	if(bDown)
		nIdx=oIdx.nNum;
	else
		nIdx=-1;
	if(aKs!=null)
	{
		for(var i=0;i<aKs.length;i++)
		{	
			if(bDown)
			{
				if(compare(aKs[i].sName,sK)>0)
				{
					nIdx=i;
					break;
				}
			}
			else
			{
				if(compare(aKs[i].sName,sK)<0)
					nIdx=i;
				else
					break;
			}
			i+=aKs[i].nNKOff;
		}
	}
	else if(oIdx.aKsOnly)
	{
		var aKsOnly=oIdx.aKsOnly;
		for(var i=0;i<aKsOnly.length;i++)
		{	
			if(aKsOnly[i])
			{
				if(bDown)
				{
					if(compare(aKsOnly[i],sK)>0)
					{
						nIdx=i;
						break;
					}
				}
				else
				{
					if(compare(aKsOnly[i],sK)<0)
						nIdx=i;
					else
						break;
				}
			}
		}
	}
	return nIdx;
}

function writeItems(oHTML,aDataCon,aPos,aMinPos,aMaxPos,bDown,nLevel)
{
	var aOldPos=new Array();
	for(var i=0;i<aPos.length;i++)
	{
		aOldPos[i]=aPos[i];
	}
	var p;
	do{
		var sCurrentK="";
		var aCurIdxSet=new Array();
		p=0;
		for(i=0;i<aDataCon.length;i++)
		{
			if(aDataCon[i].aKs&&aDataCon[i].aKs.length&&aPos[i]!=-1&&
				(bDown&&aPos[i]<aMaxPos[i])||(!bDown&&aPos[i]>aMinPos[i]))
			{
				if(sCurrentK==""||
					(bDown&&compare(sCurrentK,aDataCon[i].aKs[aPos[i]].sName)>0)||
					(!bDown&&compare(sCurrentK,aDataCon[i].aKs[aPos[i]].sName)<0))
				{
					sCurrentK=aDataCon[i].aKs[aPos[i]].sName;
					p=0;
					aCurIdxSet[p++]=i;
				}
				else if(compare(sCurrentK,aDataCon[i].aKs[aPos[i]].sName)==0){
					aCurIdxSet[p++]=i;
				}
			}
			else if(nLevel==1&&aMaxPos[i]!=aMinPos[i]){
				if(bDown&&aPos[i]==aMaxPos[i])
				{
					if(aDataCon[i].aKs)
					{
						gsSKA=aDataCon[i].aKs[aOldPos[i]].sName;
						return false;
					}
				}
				else if(!bDown&&aPos[i]==aMinPos[i])
				{
					if(aDataCon[i].aKs)
					{
						gsSKB=aDataCon[i].aKs[aOldPos[i]].sName;
						return false;
					}
				}
			}
		}
		if(p>=1){
			for(var s=0;s<p;s++)
			{
				aOldPos[aCurIdxSet[s]]=aPos[aCurIdxSet[s]];
			}
			mergeItems(oHTML,bDown,aDataCon,aCurIdxSet,p,aPos,nLevel);
			adjustPosition(bDown,aDataCon,aCurIdxSet,p,aPos);
			
			if(nLevel==1&&oHTML.nNeeded<=oHTML.nCurrent){
				return true;
			}
		}
	}while(p>0);
	return true;
}

function updateUsedK(aDataCon,aOriPos,aOldPos,bDown)
{
	for(var i=0;i<aDataCon.length;i++)
	{
		if (aOldPos[i]!=aOriPos[i])
		{
			var nBP=0;
			var nEP=0;
			if(bDown)
			{
				nBP=aOriPos[i];
				nEP=aOldPos[i]-1;
			}
			else
			{
				if (aOldPos[i]!=-1)
					nBP=aOldPos[i]+aDataCon[i].aKs[aOldPos[i]].nNKOff+1;
				else
					nBP=0;
				nEP=aOriPos[i]+aDataCon[i].aKs[aOriPos[i]].nNKOff;
			}
			if(nBP<=nEP)
			{
				setContentsUsed(aDataCon[i],nBP,nEP);
				
				var oFirstPair=aDataCon[i].oUsedItems;
				if(oFirstPair.oN==null&&oFirstPair.nB==0&&oFirstPair.nE==aDataCon[i].nNum-1)
				{
					storeKeysOnly(aDataCon[i]);
					aDataCon[i].oUsedItems=aDataCon[i].aKs=aDataCon[i].sFileName=null;
				}
			}
		}
	}
}

function storeKeysOnly(oCData)
{
	oCData.aKsOnly=new Array();
	for(var i=0;i<oCData.aKs.length;i++)
	{
		oCData.aKsOnly[i]=oCData.aKs[i].sName;
		i+=oCData.aKs[i].nNKOff;
	}
}

function setContentsUsed(oIdx,nB,nE)
{
	if(!oIdx.oUsedItems)
		oIdx.oUsedItems=new usedItems(nB,nE);
	else
	{
		var oUsedItems=oIdx.oUsedItems;
		var oPNode=null;
		do{
			if(oUsedItems.nB>nB)
			{
				if(oUsedItems.nB==nE+1)
				{
					oUsedItems.nB=nB;
				}
				else{
					var oNewNode=new usedItems(oUsedItems.nB,oUsedItems.nE);
					oNewNode.oN=oUsedItems.oN;
					oUsedItems.nB=nB;
					oUsedItems.nE=nE;
					oUsedItems.oN=oNewNode;
				}
				break;
			}
			oPNode=oUsedItems;
			oUsedItems=oUsedItems.oN;
		}while(oUsedItems);
		if(!oUsedItems)
		{
			if(oPNode!=null)
				oPNode.oN=new usedItems(nB,nE);
		}
		if(oPNode!=null){
			if(oPNode.nE==oPNode.oN.nB-1)
			{
				oPNode.nE=oPNode.oN.nE;
				oPNode.oN=oPNode.oN.oN;
			}
		}
	}
}

function projInfo(sPPath,sDPath,sFile)
{
	this.sPPath=sPPath;
	this.sDPath=sDPath;
	this.sFile=sFile;
}

function addProjInfo(sPPath,sDPath,sFile)
{
	var oIdxInfo=new projInfo(sPPath,sDPath,sFile);
	gaData[gaData.length]=oIdxInfo;
	return oIdxInfo;
}

function writeDataIFrame()
{
	if(gnLoad<gaData.length)
	{
		gbLoadInfo=true;
		loadData2(gaData[gnLoad].sPPath+gaData[gnLoad].sDPath+gaData[gnLoad].sFile);
	}
	else{
		if(gnItems!=0)
		{
			markBegin();
			writeFakeItems();
			gsSKA="";
			gnNeeded=gnVisible;
			gnIns=0;
			checkReady();
		}
	}
}

function getH6ById(nPos)
{
	if(document.all)
		return document.all("fk"+nPos);
	else if(document.getElementsByName)
		return document.getElementsByName("fk"+nPos);
	return null;		
}

function showItemsInEvaluation(nBP)
{
	var bRtn=true;
	var fPer=nBP/gnItems;
	var nB=Math.floor(fPer*gnMaxItems);
	var oCData=getChunkByIdx(gnRef,nB);
	if(oCData)
	{
		if(!oCData.aKs&&oCData.sFileName!=null)
		{
			gnNKI=nB;
			goCData=oCData;
			oCData.nProjId=gnRef;
			gbLoadInfo=false;
			beginLoading();
			loadData2(gaData[gnRef].sPPath+gaData[gnRef].sDPath+oCData.sFileName);
		}
		else{
			gsSKA=getKByIdx(oCData,nB);
			if(gsSKA)
			{
				gsSKB=null;
				gbNeedCalc=true;
				gbScrl=true;
				checkReady()
			}
			else
			{
				markEnd();
				bRtn=false;
			}
		}
	}
	else
	{
		markEnd();
		bRtn=false;
	}
	return bRtn;
}

function isUsed(oCData,nPos)
{
	var oUsed=oCData.oUsedItems;
	while(oUsed&&oUsed.nB<=nPos)
	{
		if(oUsed.nE>=nPos) return true;
		oUsed=oUsed.oN;
	}
	return false;
}

function getKByIdx(oCData,nB)
{
	var nRelPos=nB-(oCData.nTotal-oCData.nNum);
	var aIKs=oCData.aKs;
	if(nRelPos>=0&&aIKs&&nRelPos<aIKs.length)
	{
		var oK=null;
		do{
			oK=aIKs[nRelPos++];
		}	
		while((oK.nType==3||isUsed(oCData,nRelPos-1))&&nRelPos<aIKs.length);
		if(oK.nType!=3)
		{
			return oK.sName;
		}
		else{
			nRelPos=nB-(oCData.nTotal-oCData.nNum)-1;
			if(nRelPos>=0)
			{
				do{
					oK=aIKs[nRelPos--];
				}
				while((oK.nType==3||isUsed(oCData,nRelPos+1))&&nRelPos>=0);
			}
			if(oK.nType!=3)
			{
				return oK.sName;
			}
		}
	}
	return null;
}

function loadData2(sFileName)
{
	disEvt();
	if(gbXML)
		loadDataXML(sFileName);
	else
		loadData(sFileName);
	enEvt();
}

function projReady(aChunk)
{
	gaChunks[gnLoad++]=aChunk;
	var len=aChunk.length;
	var nTotal=0;
	if(len>0)
		nTotal=aChunk[len-1].nTotal;
	gnItems+=nTotal;
	if(nTotal>gnMaxItems)
	{
		gnMaxItems=nTotal;
		gnRef=gnLoad-1;
	}
	setTimeout("writeDataIFrame();",1);
}

function writeFakeItems()
{
	disEvt();
	gnUHeight=15;
	if(gbSafari3 && !gbMac)
	    gnUHeight=1;
	var sHTML=getFakeItemsHTMLbyCount(0,gnItems);
	document.body.insertAdjacentHTML("beforeEnd",sHTML);
	var obj=getH6ById(0);
	if (document.body != null)
	{
		gnVisible=Math.ceil(getClientHeight()/gnUHeight);
	}
	gaFakes[0]=new fakeItemsArea(0,gnItems,"",getEndString(),obj);
	enEvt();
}

function getEndString()
{
	var sBC=getBiggestChar();
	return sBC+sBC+sBC+sBC+sBC+sBC+sBC+sBC;
}

function getUnitIdx(nScrl,nHeight)
{
	if(gaFakes.length==0)
	{
		markEnd();
		return;
	}
	var nB=0;
	var nE=gaFakes.length-1;
	var nM=-1;
	var nTop=0;
	var nBtm=0;
	var bF=false;
	do{
		nM=(nB+nE)>>1;
		nBtm=gaFakes[nM].getBtm();
		nTop=gaFakes[nM].getTop();
			
		if(nTop>=nScrl+nHeight)
			nE=nM-1;
		else if(nBtm<nScrl)
			nB=nM+1;
		else{
			bF=true;
			break;
		}
	}while(nE>=nB);
	if(bF)
	{
		if(nTop>=nScrl){
			gsSKA=gaFakes[nM].sKA;
			gsSKB=null;
			gnNeeded=Math.ceil((nHeight-nTop+nScrl)/gnUHeight);
			gnIns=gaFakes[nM].nB;
			checkReady();
		}
		else if(nBtm<=nScrl+nHeight){
			gsSKB=gaFakes[nM].sKB;
			gsSKA=null;
			gnNeeded=Math.ceil((nBtm-nScrl+gnScrlMgn)/gnUHeight);
			gbNeedCalc=true;
			checkReady();
		}
		else{
			gnNeeded=gnVisible;
			var nUnitIdx=gaFakes[nM].nB+Math.floor((nScrl-nTop)/gnUHeight);
			if (!showItemsInEvaluation(nUnitIdx))
			{
				gsSKA=gaFakes[nM].sKA;
				gsSKB=null;
				gnNeeded=Math.ceil(nHeight/gnUHeight);
				gnIns=gaFakes[nM].nB;
				checkReady();
			}
		}
	}
	else
		markEnd();
}

function disEvt()
{
	window.onscroll=null;
	window.onresize=null;
}

function enEvt()
{
	window.onscroll=window_OnScroll;
	window.onresize=window_OnResize;
}

function insertIdxKs(nIns,oHTML,bScrl)
{
	var bRtn=true;
	disEvt();
	var nCount=oHTML.nConsumed;
	var nB=0;
	var nE=gaFakes.length-1;
	var nM=-1;
	var bF=false;
	do{
		nM=(nB+nE)>>1;
		if(gaFakes[nM].nB>nIns)
			nE=nM-1;
		else if(gaFakes[nM].nB+gaFakes[nM].nNum<=nIns)
			nB=nM+1;
		else{
			bF=true;
			break;
		}
	}while(nE>=nB);
	if(bF)
	{
		var oFIA=gaFakes[nM];
		var nOffsetTop=oFIA.getTop();
		var nOffsetBottom=oFIA.getBtm();
		var nDelta=0;
		var nHDiff=nIns-oFIA.nB;
		var nTDiff=oFIA.nNum+oFIA.nB-(nIns+nCount);
		if(nHDiff>0)
		{
			nDelta=oFIA.setNum(nHDiff);
			var sOldKBefore=oFIA.sKB;
			oFIA.sKB=oHTML.sFK;
			if(nTDiff>0)
			{
				var sHTML=getFakeItemsHTMLbyCount(nIns,nTDiff);
				oFIA.insertAdjacentHTML("afterEnd",sHTML);
				var obj=getH6ById(nIns);
				insertItemIntoArray(gaFakes,nM+1,new fakeItemsArea(nIns+nCount,nTDiff,oHTML.sLK,sOldKBefore,obj));
			}
			oFIA.insertAdjacentHTML("afterEnd",oHTML.sHTML);	
			if(bScrl)
			{
				if(gbMac&&gbIE4)
				{
					var nScrollPos=nOffsetBottom-nDelta;
					while(document.body.scrollTop!=nScrollPos)
						document.body.scrollTop=nScrollPos;
				}
				else
					window.scrollTo(0,nOffsetBottom-nDelta);
			}
		}
		else{
			oFIA.insertAdjacentHTML("beforeBegin",oHTML.sHTML);
			if(bScrl){
				if(gbMac&&gbIE4)
				{
					var nScrollPos=nOffsetTop;
					while(document.body.scrollTop!=nScrollPos)
						document.body.scrollTop=nScrollPos;
				}
				else
					window.scrollTo(0,nOffsetTop);
			}
					
			if(nTDiff>0)
			{
				oFIA.nB=nIns+nCount;
				nDelta=oFIA.setNum(nTDiff);
				oFIA.sKA=oHTML.sLK;
			}	
			else{
				gaFakes[nM].setNum(0);
				removeItemFromArray(gaFakes,nM);
			}
		}
	}
	else
		bRtn=false;
	enEvt();
	return bRtn;
}

function window_OnScroll()
{
	gnSE++;
	setTimeout("procScroll();",50);	
}

function procScroll()
{
	if(gnSE==1&&!gbProcess)
	{
		markBegin();
		getUnitIdx(document.body.scrollTop,getClientHeight());
	}
	gnSE--;
}

function window_OnResize()
{
	gnRE++;
	setTimeout("procResize();",50);
}

function procResize()
{
	if(gnRE==1&&!gbProcess)
	{
		markBegin();
		gnVisible=Math.ceil(getClientHeight()/gnUHeight);
		if(gnIns==-1)
			getUnitIdx(document.body.scrollTop,getClientHeight());
	}
	gnRE--;
}

function getChunkByIdx(nIdx,nPosition)
{
	var oCData=null;
	if(nIdx<gaChunks.length)
	{
		var len=gaChunks[nIdx].length;
		if(len>0)
		{
			var nB=0;
			var nE=len-1;
			var bF=false;
			do{
				var nM=(nB+nE)>>1;
				if(nPosition<gaChunks[nIdx][nM].nTotal)
				{
					bF=true;
					nE=nM;
				}
				else
					nB=nM+1;
			}while(nE>nB);
			if(bF)
				oCData=gaChunks[nIdx][nE];
			else if(nPosition<gaChunks[nIdx][nB].nTotal)
				oCData=gaChunks[nIdx][nB];
		}
	}
	return oCData;
}

function getChunkedData(nIdx,bDown,sK)
{
	var oCData=null;
	var nCandId=-1;
	if(nIdx<gaChunks.length)
	{
		var len=gaChunks[nIdx].length;
		if(len>0)
		{
			var nB=0;
			var nE=len-1;
			var bF=false;
			do{
				var nM=(nB+nE+(bDown?0:1))>>1;
				if(bDown)
				{
					if(compare(sK,gaChunks[nIdx][nM].sEK)<0)
					{
						bF=true;
						nE=nM;
					}
					else
						nB=nM+1;
				}
				else
				{
					if(compare(sK,gaChunks[nIdx][nM].sBK)>0)
					{
						bF=true;
						nB=nM;
					}
					else
						nE=nM-1;
				}
			}while(nE>nB);
			if(bF)
			{
				if(bDown)
					nCandId=nE;
				else
					nCandId=nB;
			}
			else
			{
				if(bDown)
				{
					if(gaChunks[nIdx].length>nB&&compare(sK,gaChunks[nIdx][nB].sEK)<0)
						nCandId=nB;
					else
						nCandId=gaChunks[nIdx].length-1;
				}
				else
				{
					if(0<=nE&&compare(sK,gaChunks[nIdx][nE].sBK)>0)
						nCandId=nE;
					else
						nCandId=0;
				}
			}
			return gaChunks[nIdx][nCandId];
		}
	}
	return null;
}

function findCK()
{
	if(gsCK!=null)
	{
		gsSKA=gsCK;
		gbFindCK=true;
		gnNeeded=1;
		markBegin();
		checkReady();
	}
}

function writeLoadingDiv(nIIdx)
{
	return "<div id=\""+gsLoadingDivID+"\" style=\"position:absolute;top:0;left:0;z-index:600;visibility:hidden;padding-left:4px;background-color:ivory;border-width:1;border-style:solid;border-color:black;width:150px;\">"+gsLoadingMsg+"</div>";
}

function findPosition( oElement ) {
  if( typeof( oElement.offsetParent ) != 'undefined' ) 
  {
    for( var posY = 0; oElement; oElement = oElement.offsetParent ) 
    {
      posY += oElement.offsetTop;
    }
    return posY ;
  } 
  else 
  {
    return oElement.y ;
  }
}

var gbWhHost=true;