using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Defend.Unity;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Defend.Editor
{
    /// <summary>原生 Unity API 制作档案预制体；重复应用保持原页面和按钮引用。</summary>
    public static class ArchivePageArtAuthoring
    {
        private const string Art = "Assets/Defend/Content/Art/Archive/";
        private const string Settings = "Assets/Defend/Content/Art/Settings/";
        private static readonly Color Ink = new Color(.11f,.43f,.49f);
        private static readonly Color White = new Color(.89f,.94f,.96f);
        [MenuItem("不眠垣城/应用垣城档案美术")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出播放模式。");
            ImportArt();
            var root = PrefabUtility.LoadPrefabContents(DefenseInterfaceAuthoring.PrefabPath);
            try { ApplyToRoot(root); PrefabUtility.SaveAsPrefabAsset(root,DefenseInterfaceAuthoring.PrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }

        public static void ApplyToRoot(GameObject root)
        {
            if (!File.Exists(Art+"Textures/UI_Archive_Atlas_01.png")) return;
            var sprites = AssetDatabase.FindAssets("t:Sprite",new[]{Art}).Select(AssetDatabase.GUIDToAssetPath)
                .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<Sprite>().GroupBy(x=>x.name).ToDictionary(x=>x.Key,x=>x.First());
            var controller = new SerializedObject(root.GetComponent<DefenseInterface>());
            var overlay = Ref<GameObject>(controller,"archivePage").GetComponent<RectTransform>();
            var card = (RectTransform)(overlay.Find("页面内容") ?? overlay.Find("档案页构图/页面内容"));
            if (card == null) throw new InvalidOperationException("档案页缺少页面内容。");
            var font = AssetDatabase.LoadAssetAtPath<Font>(WhiteboxSceneBuilder.ChineseFontPath);
            var shade = overlay.GetComponent<UnityEngine.UI.Image>(); shade.color = new Color(.02f,.04f,.055f,.5f); shade.raycastTarget = true;
            var backdrop = Child(overlay,"模糊垣城背景"); Fill(backdrop);
            var bg = Picture(backdrop,AssetDatabase.LoadAssetAtPath<Sprite>(Settings+"Textures/BG_Settings_Backdrop_01.png"));
            bg.material = AssetDatabase.LoadAssetAtPath<Material>(Settings+"SettingsBackdrop.mat"); bg.color = new Color(.65f,.67f,.7f); bg.raycastTarget = true;
            Fit(backdrop,UnityEngine.UI.AspectRatioFitter.AspectMode.EnvelopeParent); backdrop.SetAsFirstSibling();
            var composition = Child(overlay,"档案页构图"); Fill(composition); Fit(composition,UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent);
            card.SetParent(composition,false); Box(card,55,174,1810,840,1920,1080);
            Picture(card,sprites["UI_Panel_Archive_Main_01"],true).pixelsPerUnitMultiplier = 2.5f;
            var heading = Child(composition,"档案标题牌"); Box(heading,55,52,550,102,1920,1080);
            Picture(heading,sprites["UI_Button_Archive_Entry_Normal"],true);
            var headingText = Label(heading,"标题",font,"垣城档案",42,new Color(.13f,.16f,.18f),TextAnchor.MiddleCenter); Fill(headingText.rectTransform);
            card.Find("页面标题").gameObject.SetActive(false); card.Find("页面引导").gameObject.SetActive(false);
            var line = Child(card,"UI_Frame_Archive_Title_01"); Box(line,30,80,1750,4,1810,840); Picture(line,sprites[line.name]);
            var count = Label(card,"条目计数",font,"背景故事  /  01",22,White,TextAnchor.MiddleLeft); Box(count.rectTransform,32,22,800,48,1810,840);
            var categories = Refs<UnityEngine.UI.Button>(controller,"archiveCategories");
            string[] categoryNames = {"背景故事","防御工事","守城者"}; string[] categoryIcons = {"Story","Build","Enemy"};
            for (int i=0;i<categories.Length;i++)
            {
                Box((RectTransform)categories[i].transform,32,122+i*104,245,86,1810,840);
                Style(categories[i],sprites["UI_Button_Archive_Tab_Normal"],sprites["UI_Button_Archive_Tab_Pressed"]);
                var label = categories[i].GetComponentInChildren<UnityEngine.UI.Text>(); label.text=categoryNames[i]; TextStyle(label,font,25,new Color(.15f,.19f,.22f),TextAnchor.MiddleCenter);
                Get<ArchiveButtonFeedback>(categories[i].gameObject).Configure(label,sprites["UI_Button_Archive_Tab_Pressed"],label.color,new Color(.85f,1f,1f));
                Anchors(label.rectTransform,new Vector2(.22f,0),Vector2.one);
                var icon=Child(categories[i].transform,"分类图标"); Box(icon,14,22,42,42,245,86); Picture(icon,sprites["UI_Icon_Archive_Tab_"+categoryIcons[i]+"_01"]).preserveAspect=true;
            }
            var quote=Label(card,"守望者档案铭文",font,"「城不靠人管。\n城自己管自己。」",22,new Color(.62f,.76f,.8f),TextAnchor.UpperLeft);
            Box(quote.rectTransform,45,510,220,140,1810,840);
            var listRect=Child(card,"档案条目列表"); Box(listRect,310,118,300,600,1810,840);
            var list=Get<UnityEngine.UI.ScrollRect>(listRect.gameObject); list.horizontal=false; list.vertical=true; list.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped; list.scrollSensitivity=35;
            var viewport=Child(listRect,"裁切区域"); Fill(viewport); Get<UnityEngine.UI.RectMask2D>(viewport.gameObject);
            var hit=Picture(viewport,null); hit.color=Color.clear; hit.raycastTarget=true;
            var content=Child(viewport,"条目内容"); content.anchorMin=new Vector2(0,1); content.anchorMax=Vector2.one; content.pivot=new Vector2(.5f,1); content.anchoredPosition=Vector2.zero; content.sizeDelta=Vector2.zero;
            var layout=Get<UnityEngine.UI.VerticalLayoutGroup>(content.gameObject); layout.spacing=12; layout.padding=new RectOffset(2,14,2,2); layout.childControlWidth=true; layout.childControlHeight=true; layout.childForceExpandHeight=false; layout.childForceExpandWidth=true;
            Get<UnityEngine.UI.ContentSizeFitter>(content.gameObject).verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            list.viewport=viewport; list.content=content;
            var entries=Refs<UnityEngine.UI.Button>(controller,"archiveEntries");
            foreach(var button in entries)
            {
                button.transform.SetParent(content,false); var rect=(RectTransform)button.transform; Fill(rect);
                Get<UnityEngine.UI.LayoutElement>(button.gameObject).preferredHeight=72;
                Style(button,sprites["UI_Button_Archive_Entry_Normal"],sprites["UI_Button_Archive_Entry_Pressed"]);
                var label=button.GetComponentInChildren<UnityEngine.UI.Text>(); Fill(label.rectTransform); TextStyle(label,font,25,new Color(.15f,.19f,.22f),TextAnchor.MiddleCenter);
                Get<ArchiveButtonFeedback>(button.gameObject).Configure(label,sprites["UI_Button_Archive_Entry_Pressed"],label.color,new Color(.85f,1f,1f));
            }
            var listBar=ScrollBar(listRect,"条目滚动条"); Anchors((RectTransform)listBar.transform,new Vector2(.97f,0),Vector2.one); list.verticalScrollbar=listBar; list.verticalScrollbarVisibility=UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
            var detail=(RectTransform)card.Find("档案资料"); Box(detail,640,110,1135,625,1810,840);
            var detailBg=detail.GetComponent<UnityEngine.UI.Image>(); detailBg.sprite=null; detailBg.color=new Color(.10f,.15f,.18f,.78f); detailBg.raycastTarget=false;
            var title=Ref<UnityEngine.UI.Text>(controller,"archiveTitle"); Box(title.rectTransform,28,16,800,62,1135,625); TextStyle(title,font,38,White,TextAnchor.MiddleLeft);
            var subtitle=Label(detail,"档案副标",font,"背景故事",22,new Color(.60f,.80f,.85f),TextAnchor.MiddleRight); Box(subtitle.rectTransform,845,23,260,45,1135,625);
            var portraitRect=Child(detail,"档案立绘"); Box(portraitRect,20,87,570,295,1135,625); var portrait=Picture(portraitRect,null); portrait.preserveAspect=true;
            var statsPanel=Child(detail,"属性面板"); Box(statsPanel,625,92,480,285,1135,625);
            var statLabels=new UnityEngine.UI.Text[6]; var statValues=new UnityEngine.UI.Text[6]; var statIcons=new UnityEngine.UI.Image[6]; var statFills=new UnityEngine.UI.Image[6];
            for (int i=0;i<6;i++)
            {
                var row=Child(statsPanel,"属性"+i); Box(row,0,i*46,480,43,480,285);
                var icon=Child(row,"图标"); Box(icon,0,5,27,27,480,43); statIcons[i]=Picture(icon,sprites["UI_Icon_Archive_Stat_HP_01"]); statIcons[i].preserveAspect=true;
                statLabels[i]=Label(row,"属性名",font,"耐久",20,White,TextAnchor.UpperLeft); Box(statLabels[i].rectTransform,40,0,145,32,480,43);
                statValues[i]=Label(row,"属性值",font,"600",21,new Color(.67f,.89f,.93f),TextAnchor.UpperRight); Box(statValues[i].rectTransform,160,0,315,32,480,43);
                var bar=Child(row,"属性槽"); Box(bar,40,31,435,7,480,43); Picture(bar,sprites["UI_Bar_Archive_Stat_BG_01"],true);
                var fill=Child(bar,"填充"); Fill(fill); statFills[i]=Picture(fill,sprites[i==0?"UI_Bar_Archive_Stat_Fill_Green_01":i==2?"UI_Bar_Archive_Stat_Fill_Orange_01":"UI_Bar_Archive_Stat_Fill_Cyan_01"]);
                statFills[i].type=UnityEngine.UI.Image.Type.Filled; statFills[i].fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal; statFills[i].fillOrigin=0;
            }
            var body=Ref<UnityEngine.UI.Text>(controller,"archiveBody"); var scroll=body.GetComponentInParent<UnityEngine.UI.ScrollRect>(true);
            var paper=Picture((RectTransform)scroll.transform,sprites["UI_Panel_Archive_Readout_01"],true); paper.raycastTarget=true;
            // 原有滚动正文保留引用，增加上下留白；只有文本高度由布局系统计算。
            var bodyViewport=scroll.viewport; Anchors(bodyViewport,new Vector2(.027f,.05f),new Vector2(.96f,.95f));
            body.rectTransform.anchorMin=new Vector2(0,1); body.rectTransform.anchorMax=Vector2.one; body.rectTransform.pivot=new Vector2(.5f,1); body.rectTransform.anchoredPosition=Vector2.zero; body.rectTransform.sizeDelta=Vector2.zero;
            TextStyle(body,font,24,Ink,TextAnchor.UpperLeft); body.lineSpacing=1.3f; body.supportRichText=false;
            Get<UnityEngine.UI.ContentSizeFitter>(body.gameObject).verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=40;
            if(scroll.verticalScrollbar!=null) { Anchors((RectTransform)scroll.verticalScrollbar.transform,new Vector2(.979f,.05f),new Vector2(.99f,.95f)); scroll.verticalScrollbar.GetComponent<UnityEngine.UI.Image>().color=new Color(.15f,.46f,.53f,.45f); ((UnityEngine.UI.Image)scroll.verticalScrollbar.targetGraphic).color=new Color(.15f,.54f,.62f); }
            var close=Ref<UnityEngine.UI.Button>(controller,"closeArchive"); Box((RectTransform)close.transform,1515,752,255,58,1810,840); Style(close,sprites["UI_Button_Archive_Entry_Normal"],sprites["UI_Button_Archive_Entry_Pressed"]);
            var closeLabel=close.GetComponentInChildren<UnityEngine.UI.Text>(); closeLabel.text="关闭"; TextStyle(closeLabel,font,25,new Color(.15f,.19f,.22f),TextAnchor.MiddleCenter); Fill(closeLabel.rectTransform);
            var topClose=Button(card,"关闭档案右上角",font,"×",sprites["UI_Button_Archive_Close_Normal"],sprites["UI_Button_Archive_Close_Pressed"]); Box((RectTransform)topClose.transform,1715,20,52,52,1810,840);
            var hint=Label(card,"档案阅读提示",font,"垣城档案  /  滚动阅读完整资料",18,new Color(.6f,.7f,.74f),TextAnchor.MiddleLeft); Box(hint.rectTransform,35,758,900,40,1810,840);
            for(int i=0;i<4;i++) { var corner=Child(card,"UI_Decal_Archive_Corner_01_"+i); Box(corner,i%2==0?14:1752,i<2?14:782,44,44,1810,840); Picture(corner,sprites["UI_Decal_Archive_Corner_01"]); corner.localScale=new Vector3(i%2==0?1:-1,i<2?1:-1,1); }
            var view=Get<DefenseArchiveView>(overlay.gameObject); var binding=new SerializedObject(view);
            Set(binding,"catalog",AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Defend/Content/Archive/ArchiveCatalog.json"));
            SetArray(binding,"categories",categories); SetArray(binding,"entries",entries); Set(binding,"topClose",topClose); Set(binding,"bottomClose",close);
            Set(binding,"title",title); Set(binding,"subtitle",subtitle); Set(binding,"body",body); Set(binding,"entryCount",count); Set(binding,"portrait",portrait);
            Set(binding,"bodyScroll",scroll.transform); Set(binding,"listScroll",list); Set(binding,"detailScroll",scroll); Set(binding,"statsPanel",statsPanel.gameObject);
            SetArray(binding,"statLabels",statLabels); SetArray(binding,"statValues",statValues); SetArray(binding,"statIcons",statIcons); SetArray(binding,"statFills",statFills);
            SetArray(binding,"illustrations",sprites.Values.Where(x=>x.name.StartsWith("UI_Art_Archive_")).ToArray()); SetArray(binding,"icons",sprites.Values.Where(x=>x.name.StartsWith("UI_Icon_Archive_Stat_")).ToArray());
            Set(binding,"normal",sprites["UI_Button_Archive_Entry_Normal"]); Set(binding,"pressed",sprites["UI_Button_Archive_Entry_Pressed"]); Set(binding,"selected",sprites["UI_Button_Archive_Entry_Selected"]);
            binding.ApplyModifiedPropertiesWithoutUndo(); Set(controller,"archiveView",view); controller.ApplyModifiedPropertiesWithoutUndo();
            ApplyReferenceLayout(card,composition,detail,controller,view,sprites,font);
            // 所有键盘导航都限制在当前弹窗内。
            var nav=categories.Concat(entries).Concat(new[]{topClose}).ToArray();
            for(int i=0;i<nav.Length;i++) { var n=nav[i].navigation; n.mode=UnityEngine.UI.Navigation.Mode.Explicit; n.selectOnUp=nav[(i+nav.Length-1)%nav.Length]; n.selectOnDown=nav[(i+1)%nav.Length]; n.selectOnLeft=categories[0]; n.selectOnRight=entries[0]; nav[i].navigation=n; }
            overlay.gameObject.SetActive(false);
        }

        private static void ApplyReferenceLayout(RectTransform card,RectTransform composition,RectTransform detail,SerializedObject controller,DefenseArchiveView view,Dictionary<string,Sprite> sprites,Font font)
        {
            // 参考图的左右分区：横向分类栏和竖排条目；右侧大立绘 / 大阅读屏。
            Fill(card); card.GetComponent<UnityEngine.UI.Image>().color=Color.clear;
            card.Find("UI_Frame_Archive_Title_01").gameObject.SetActive(false); card.Find("条目计数").gameObject.SetActive(false);
            card.Find("守望者档案铭文").gameObject.SetActive(false); card.Find("档案阅读提示").gameObject.SetActive(false);
            for(int i=0;i<4;i++) card.Find("UI_Decal_Archive_Corner_01_"+i).gameObject.SetActive(false);
            var heading=(RectTransform)composition.Find("档案标题牌"); Box(heading,106,58,480,110,1920,1080);
            heading.GetComponentInChildren<UnityEngine.UI.Text>().fontSize=44;
            var bar=Child(card,"横向分类栏"); Box(bar,30,230,565,140,1920,1080);
            Picture(bar,sprites["UI_Panel_Archive_Main_01"],true).pixelsPerUnitMultiplier=1.5f;
            foreach(var button in Refs<UnityEngine.UI.Button>(controller,"archiveCategories"))
            {
                button.transform.SetParent(bar,false);
                var image=(UnityEngine.UI.Image)button.targetGraphic; image.type=UnityEngine.UI.Image.Type.Simple;
                var icon=(RectTransform)button.transform.Find("分类图标"); Box(icon,10,20,65,70,330,110);
            }
            var list=(RectTransform)card.Find("档案条目列表"); Box(list,85,395,500,630,1920,1080);
            var layout=list.GetComponent<UnityEngine.UI.ScrollRect>().content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>(); layout.spacing=15; layout.padding=new RectOffset(0,15,0,0);
            var storyQuote=Label(Refs<UnityEngine.UI.Button>(controller,"archiveEntries")[0].transform,"故事铭文",font,"「城不靠人管。\n城自己管自己。」",25,new Color(.16f,.2f,.23f),TextAnchor.UpperCenter); Anchors(storyQuote.rectTransform,new Vector2(.08f,.18f),new Vector2(.92f,.55f));
            // 左侧装置支架由原生金属线与接头组成，持续贯穿条目列表。
            var pole=Child(card,"档案支架"); Box(pole,60,365,17,715,1920,1080); Picture(pole,null).color=new Color(.49f,.57f,.61f,.95f); pole.SetSiblingIndex(bar.GetSiblingIndex());
            var poleHighlight=Child(pole,"支架高光"); Anchors(poleHighlight,new Vector2(.2f,0),new Vector2(.4f,1)); Picture(poleHighlight,null).color=new Color(.8f,.87f,.89f);
            for(int i=0;i<7;i++) { var joint=Child(card,"支架接头"+i); Box(joint,52,429+i*106,70,13,1920,1080); Picture(joint,null).color=new Color(.56f,.65f,.69f); joint.SetSiblingIndex(list.GetSiblingIndex()); }
            Box(detail,625,100,1235,900,1920,1080); detail.GetComponent<UnityEngine.UI.Image>().color=Color.clear;
            var binding=new SerializedObject(view); var portrait=Ref<UnityEngine.UI.Image>(binding,"portrait"); Anchors(portrait.rectTransform,new Vector2(.035f,.34f),new Vector2(.985f,1.07f));
            var stats=Ref<GameObject>(binding,"statsPanel").GetComponent<RectTransform>(); Anchors(stats,new Vector2(.615f,.01f),new Vector2(.995f,.3f));
            Picture(stats,null).color=new Color(.12f,.17f,.20f,.93f);
            foreach(Transform row in stats)
            {
                row.Find("属性名").GetComponent<UnityEngine.UI.Text>().fontSize=17; row.Find("属性值").GetComponent<UnityEngine.UI.Text>().fontSize=18;
                Box((RectTransform)row.Find("属性槽"),40,37,435,5,480,43);
            }
            var panel=Ref<UnityEngine.UI.ScrollRect>(binding,"detailScroll").GetComponent<UnityEngine.UI.Image>(); panel.sprite=null; panel.type=UnityEngine.UI.Image.Type.Simple;
            string materialPath=Art+"ArchiveReadout.mat"; var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(material==null) { material=new Material(Shader.Find("Defend/UI/ArchiveReadout")); AssetDatabase.CreateAsset(material,materialPath); }
            panel.material=material;
            var frame=Child(panel.transform,"阅读屏金属边框"); Fill(frame); Picture(frame,sprites["UI_Panel_Archive_Main_01"],true).pixelsPerUnitMultiplier=2.5f;
            // 仅使用原面板边缘，中心被干净的青色阅读底遮盖。
            var readout=Child(frame,"阅读屏"); Anchors(readout,new Vector2(.018f,.025f),new Vector2(.982f,.975f)); var readoutImage=Picture(readout,null); readoutImage.material=material;
            frame.SetAsFirstSibling(); frame.gameObject.SetActive(true);
            var topClose=Ref<UnityEngine.UI.Button>(binding,"topClose"); Box((RectTransform)topClose.transform,24,53,80,120,1920,1080);
            topClose.transition=UnityEngine.UI.Selectable.Transition.ColorTint; var topImage=(UnityEngine.UI.Image)topClose.targetGraphic; topImage.sprite=null; topImage.overrideSprite=null; topImage.color=Color.clear;
            var arrow=topClose.GetComponentInChildren<UnityEngine.UI.Text>(); arrow.text="〈"; arrow.fontSize=76; arrow.color=new Color(.88f,.95f,.98f); arrow.alignment=TextAnchor.MiddleCenter;
            var close=Ref<UnityEngine.UI.Button>(controller,"closeArchive"); Box((RectTransform)close.transform,1690,1012,170,45,1920,1080); close.GetComponentInChildren<UnityEngine.UI.Text>().fontSize=19;
            close.gameObject.SetActive(false);
            Set(binding,"storyQuote",storyQuote.gameObject); Set(binding,"storyPlate",sprites["UI_Panel_Archive_Story_01"]); binding.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ImportArt()
        {
            Directory.CreateDirectory(Art+"Sprites"); AssetDatabase.Refresh();
            var source=AssetDatabase.LoadAllAssetsAtPath(Settings+"Textures/UI_Settings_Atlas_01.png").OfType<Sprite>().ToDictionary(x=>x.name);
            var main=AssetDatabase.LoadAllAssetsAtPath("Assets/Defend/Content/Art/MainMenu/Textures/UI_Main_ButtonStates_01.png").OfType<Sprite>().ToDictionary(x=>x.name);
            Alias(source["UI_Panel_Settings_Main_01"],"UI_Panel_Archive_Main_01"); Alias(source["UI_Frame_Settings_Line_01"],"UI_Frame_Archive_Title_01"); Alias(source["UI_Decal_Settings_Corner_01"],"UI_Decal_Archive_Corner_01");
            Alias(source["UI_Button_Settings_Close_Bottom_Normal"],"UI_Panel_Archive_Story_01");
            foreach(string state in new[]{"Normal","Pressed"}) Alias(source["UI_Button_Settings_Close_"+state],"UI_Button_Archive_Close_"+state);
            foreach(string type in new[]{"Tab","Entry"}) foreach(string state in new[]{"Normal","Pressed","Selected"}) Alias(main["UI_Button_Main_Archive_"+(state=="Pressed"?"Pressed":"Normal")],"UI_Button_Archive_"+type+"_"+state);
            foreach(string path in Directory.GetFiles(Art+"Illustrations","*.png"))
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(path.Replace('\\','/')); importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
                importer.alphaIsTransparency=true; importer.mipmapEnabled=false; importer.maxTextureSize=1024; importer.textureCompression=TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            }
            var atlas=(TextureImporter)AssetImporter.GetAtPath(Art+"Textures/UI_Archive_Atlas_01.png"); atlas.GetSourceTextureWidthAndHeight(out int w,out int h);
            string[] names={"UI_Icon_Archive_Tab_Story_01","UI_Icon_Archive_Tab_Build_01","UI_Icon_Archive_Tab_Enemy_01","UI_Icon_Archive_Stat_HP_01","UI_Icon_Archive_Stat_Speed_01","UI_Icon_Archive_Stat_Atk_01","UI_Icon_Archive_Stat_Target_01","UI_Bar_Archive_Stat_BG_01","UI_Bar_Archive_Stat_Fill_Green_01","UI_Bar_Archive_Stat_Fill_Cyan_01","UI_Bar_Archive_Stat_Fill_Orange_01","UI_Panel_Archive_Readout_01"};
            var metadata=new SpriteMetaData[names.Length];
            // 核对生成图的实际透明边界，避免图标或邻格进入裁切区域。
            Rect[] bounds={new Rect(18,208,309,220),new Rect(349,139,263,324),new Rect(620,142,311,317),new Rect(951,140,287,324),new Rect(22,536,308,285),new Rect(337,531,288,293),new Rect(629,531,292,294),new Rect(928,641,312,99),new Rect(18,973,302,87),new Rect(325,973,300,87),new Rect(628,973,296,87),new Rect(926,881,319,252)};
            for(int i=0;i<names.Length;i++) { var rect=bounds[i]; rect.y=h-rect.y-rect.height; metadata[i]=new SpriteMetaData{name=names[i],rect=rect,pivot=new Vector2(.5f,.5f),alignment=(int)SpriteAlignment.Center,border=i==11?new Vector4(34,34,34,34):Vector4.zero}; }
            atlas.textureType=TextureImporterType.Sprite; atlas.spriteImportMode=SpriteImportMode.Multiple; atlas.alphaIsTransparency=true; atlas.mipmapEnabled=false; atlas.textureCompression=TextureImporterCompression.Uncompressed; atlas.maxTextureSize=2048; atlas.npotScale=TextureImporterNPOTScale.None;
#pragma warning disable CS0618
            atlas.spritesheet=metadata;
#pragma warning restore CS0618
            atlas.SaveAndReimport();
        }
        private static void Alias(Sprite source,string name)
        {
            string path=Art+"Sprites/"+name+".asset";
            if(AssetDatabase.LoadAssetAtPath<Sprite>(path)!=null) return;
            var sprite=Sprite.Create(source.texture,source.rect,source.pivot/source.rect.size,source.pixelsPerUnit,0,SpriteMeshType.FullRect,source.border); sprite.name=name; AssetDatabase.CreateAsset(sprite,path);
        }
        private static T Ref<T>(SerializedObject data,string field) where T:Object => (T)data.FindProperty(field).objectReferenceValue;
        private static T[] Refs<T>(SerializedObject data,string field) where T:Object { var array=data.FindProperty(field); return Enumerable.Range(0,array.arraySize).Select(i=>(T)array.GetArrayElementAtIndex(i).objectReferenceValue).ToArray(); }
        private static void Set(SerializedObject data,string field,Object value) => data.FindProperty(field).objectReferenceValue=value;
        private static void SetArray<T>(SerializedObject data,string field,T[] values) where T:Object { var array=data.FindProperty(field); array.arraySize=values.Length; for(int i=0;i<values.Length;i++) array.GetArrayElementAtIndex(i).objectReferenceValue=values[i]; }
        private static T Get<T>(GameObject target) where T:Component => target.GetComponent<T>() ?? target.AddComponent<T>();
        private static RectTransform Child(Transform parent,string name) { var existing=parent.Find(name); if(existing!=null) return (RectTransform)existing; var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent,false); return rect; }
        private static void Anchors(RectTransform rect,Vector2 min,Vector2 max) { rect.anchorMin=min; rect.anchorMax=max; rect.pivot=new Vector2(.5f,.5f); rect.anchoredPosition=Vector2.zero; rect.sizeDelta=Vector2.zero; }
        private static void Fill(RectTransform rect) => Anchors(rect,Vector2.zero,Vector2.one);
        private static void Box(RectTransform rect,float x,float y,float w,float h,float pw,float ph) => Anchors(rect,new Vector2(x/pw,1-(y+h)/ph),new Vector2((x+w)/pw,1-y/ph));
        private static void Fit(RectTransform rect,UnityEngine.UI.AspectRatioFitter.AspectMode mode) { var fit=Get<UnityEngine.UI.AspectRatioFitter>(rect.gameObject); fit.enabled=false; fit.aspectMode=mode; fit.aspectRatio=16f/9f; Fill(rect); fit.enabled=true; }
        private static UnityEngine.UI.Image Picture(RectTransform rect,Sprite sprite,bool sliced=false) { var image=Get<UnityEngine.UI.Image>(rect.gameObject); image.sprite=sprite; image.color=Color.white; image.raycastTarget=false; image.type=sliced?UnityEngine.UI.Image.Type.Sliced:UnityEngine.UI.Image.Type.Simple; return image; }
        private static void TextStyle(UnityEngine.UI.Text label,Font font,int size,Color color,TextAnchor alignment) { label.font=font; label.fontSize=size; label.color=color; label.alignment=alignment; label.verticalOverflow=VerticalWrapMode.Overflow; label.horizontalOverflow=HorizontalWrapMode.Wrap; label.raycastTarget=false; }
        private static UnityEngine.UI.Text Label(Transform parent,string name,Font font,string text,int size,Color color,TextAnchor alignment) { var rect=Child(parent,name); var label=Get<UnityEngine.UI.Text>(rect.gameObject); TextStyle(label,font,size,color,alignment); label.text=text; return label; }
        private static void Style(UnityEngine.UI.Button button,Sprite normal,Sprite pressed) { var image=Picture((RectTransform)button.transform,normal,true); image.raycastTarget=true; button.targetGraphic=image; button.transition=UnityEngine.UI.Selectable.Transition.SpriteSwap; var state=button.spriteState; state.highlightedSprite=state.pressedSprite=state.selectedSprite=pressed; button.spriteState=state; }
        private static UnityEngine.UI.Button Button(Transform parent,string name,Font font,string text,Sprite normal,Sprite pressed) { var rect=Child(parent,name); var button=Get<UnityEngine.UI.Button>(rect.gameObject); Style(button,normal,pressed); var label=Label(rect,"按钮文字",font,text,32,White,TextAnchor.MiddleCenter); Fill(label.rectTransform); return button; }
        private static UnityEngine.UI.Scrollbar ScrollBar(Transform parent,string name)
        {
            var rect=Child(parent,name); Picture(rect,null).color=new Color(.13f,.23f,.27f); var bar=Get<UnityEngine.UI.Scrollbar>(rect.gameObject); bar.direction=UnityEngine.UI.Scrollbar.Direction.BottomToTop;
            var area=Child(rect,"滑动区"); Fill(area); var handle=Child(area,"手柄"); Fill(handle); var image=Picture(handle,null); image.color=new Color(.4f,.72f,.8f); image.raycastTarget=true; bar.handleRect=handle; bar.targetGraphic=image; return bar;
        }
    }
}
