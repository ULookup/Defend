using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using Defend.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

public static class ArchivePageValidation
{
    [Serializable] private sealed class Report { public bool passed=true; public string[] checks; public int entries=13, illustrations=12; public string screenshots="1920x1080 / 1280x800"; }
    public static async Task<object> Review()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Review requires play mode.");
        var ui=Object.FindAnyObjectByType<DefenseInterface>(); var controller=new SerializedObject(ui);
        var session=Object.FindAnyObjectByType<GameSessionRoot>(); session.ReturnToStart();
        Ref<UnityEngine.UI.Button>(controller,"openArchive").onClick.Invoke();
        var page=Ref<GameObject>(controller,"archivePage"); var component=Ref<DefenseArchiveView>(controller,"archiveView"); var view=new SerializedObject(component);
        var data=JsonUtility.FromJson<ArchiveCatalog>(Ref<TextAsset>(view,"catalog").text);
        var categories=Refs<UnityEngine.UI.Button>(view,"categories"); var entries=Refs<UnityEngine.UI.Button>(view,"entries");
        var body=Ref<UnityEngine.UI.Text>(view,"body"); var scroll=Ref<UnityEngine.UI.ScrollRect>(view,"detailScroll");
        var checks=new List<string>();
        Check(page.activeSelf && session.AtStartPage && ui.BlocksWorldInput,"开始页打开档案并阻止战场输入",checks);
        await Task.Delay(200); Canvas.ForceUpdateCanvases();
        Check(Object.FindObjectsByType<EventSystem>().Length==1,"单一事件系统",checks);
        for(int c=0;c<3;c++)
        {
            Click(categories[c]); await Task.Delay(100); Canvas.ForceUpdateCanvases();
            var expected=data.entries.Where(x=>x.category==c).ToArray();
            Check(entries.Count(x=>x.gameObject.activeSelf)==expected.Length,"分类条目数量："+data.categories[c],checks);
            Check(((UnityEngine.UI.Image)categories[c].targetGraphic).color.b>.9f,"分类选中高亮",null);
            for(int e=0;e<expected.Length;e++)
            {
                Ref<UnityEngine.UI.ScrollRect>(view,"listScroll").verticalNormalizedPosition = expected.Length>1 ? 1-e/(float)(expected.Length-1) : 1;
                await Task.Delay(70); Canvas.ForceUpdateCanvases();
                Click(entries[e]); await Task.Delay(70); Canvas.ForceUpdateCanvases();
                Check(component.SelectedEntry.id==expected[e].id && body.text.Replace('\u00a0',' ')==expected[e].body,"完整配置正文："+expected[e].title,checks);
                Check(Ref<UnityEngine.UI.Text>(view,"title").text==(c==0?data.categories[0]:expected[e].title),"对应标题",null);
                Check(body.cachedTextGenerator.vertexCount>=4,"正文文字可见",null);
                var portrait=Ref<UnityEngine.UI.Image>(view,"portrait");
                Check(c==0?!portrait.gameObject.activeSelf:portrait.sprite!=null && portrait.sprite.name==expected[e].art,"对应立绘",null);
                Check(((UnityEngine.UI.Image)entries[e].targetGraphic).color.b>.9f,"条目高亮",null);
                var labels=Refs<UnityEngine.UI.Text>(view,"statLabels"); var values=Refs<UnityEngine.UI.Text>(view,"statValues");
                for(int s=0;s<expected[e].stats.Length;s++) Check(labels[s].text==expected[e].stats[s].label && values[s].text==expected[e].stats[s].value && values[s].cachedTextGenerator.vertexCount>=4,"属性来自配置且可见",null);
                foreach(var rect in page.GetComponentsInChildren<RectTransform>()) Check(!float.IsNaN(rect.rect.width) && !float.IsInfinity(rect.rect.width),"有限布局",null);
            }
        }
        Click(categories[0]); await Task.Delay(150); Canvas.ForceUpdateCanvases();
        Check(scroll.content.rect.height>scroll.viewport.rect.height,"长故事实际溢出并可滚动",checks);
        var pointer=new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,-10)};
        ExecuteEvents.Execute(scroll.gameObject,pointer,ExecuteEvents.scrollHandler); await Task.Delay(100);
        Check(scroll.verticalNormalizedPosition<.999f,"滚轮移动故事正文",checks);
        scroll.verticalNormalizedPosition=0; Canvas.ForceUpdateCanvases(); await Task.Delay(100);
        Vector3[] corners=new Vector3[4]; scroll.content.GetWorldCorners(corners);
        var bottom=scroll.viewport.InverseTransformPoint(corners[0]);
        Check(Mathf.Abs(bottom.y-scroll.viewport.rect.yMin)<3,"故事最后一段可以滚到视口底部",checks);
        Click(categories[1]); Check(Mathf.Abs(scroll.verticalNormalizedPosition-1)<.02f || scroll.content.rect.height<=scroll.viewport.rect.height,"切换条目正文回到顶部",checks);
        var outside=new PointerEventData(EventSystem.current){position=new Vector2(8,8)}; var hits=new List<RaycastResult>(); EventSystem.current.RaycastAll(outside,hits);
        Check(hits.Count>0,"背景拦截",null); ExecuteEvents.Execute(hits[0].gameObject,outside,ExecuteEvents.pointerClickHandler);
        Check(page.activeSelf && session.AtStartPage,"点击弹窗外不穿透开始页",checks);
        Click(Ref<UnityEngine.UI.Button>(view,"topClose")); Check(!page.activeSelf && session.AtStartPage,"左上返回箭头关闭到开始页",checks);
        Check(!Ref<UnityEngine.UI.Button>(view,"bottomClose").gameObject.activeSelf,"档案仅保留左上角返回",checks);
        Ref<UnityEngine.UI.Button>(controller,"openSettings").onClick.Invoke(); await Task.Delay(100); var settings=new SerializedObject(Ref<DefenseSettingsView>(controller,"settingsView")); Ref<UnityEngine.UI.Button>(settings,"topClose").onClick.Invoke();
        Check(!Ref<GameObject>(controller,"settingsPage").activeSelf,"设置页仍能打开及关闭",checks);
        Ref<UnityEngine.UI.Button>(controller,"openArchive").onClick.Invoke();
        await Capture(component);
        var result=new Report{checks=checks.ToArray()};
        string folder=Path.Combine(Path.GetDirectoryName(Application.dataPath),"Docs/Art/Archive"); File.WriteAllText(Path.Combine(folder,"Validation.json"),JsonUtility.ToJson(result,true));
        return result;
    }

    private static void Click(UnityEngine.UI.Button button)
    {
        var rect=(RectTransform)button.transform;
        var pointer=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center)),button=PointerEventData.InputButton.Left};
        var hits=new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer,hits);
        Check(hits.Count>0 && (hits[0].gameObject==button.gameObject || hits[0].gameObject.transform.IsChildOf(button.transform)),"真实点击命中："+button.name+"/"+(hits.Count>0?hits[0].gameObject.name:"无"),null);
        ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
    }
    private static async Task Capture(DefenseArchiveView component)
    {
        Type viewType=typeof(Editor).Assembly.GetType("UnityEditor.GameView"); var window=EditorWindow.GetWindow(viewType); window.Show(); window.Focus();
        var sizeIndex=viewType.GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic); int previous=(int)sizeIndex.GetValue(window);
        Type sizesType=typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
        var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var sizes=singleton.GetProperty("instance",BindingFlags.Static|BindingFlags.Public|BindingFlags.FlattenHierarchy).GetValue(null);
        var group=sizesType.GetMethod("GetGroup").Invoke(sizes,new[]{sizesType.GetProperty("currentGroupType").GetValue(sizes)});
        Type sizeType=typeof(Editor).Assembly.GetType("UnityEditor.GameViewSize"), modeType=typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizeType");
        Type capture=AppDomain.CurrentDomain.GetAssemblies().Select(x=>x.GetType("Unity.Pipeline.Editor.Commands.Capture.CaptureCommands")).First(x=>x!=null);
        string folder=Path.Combine(Path.GetDirectoryName(Application.dataPath),"Docs/Art/Archive/Previews"); Directory.CreateDirectory(folder);
        try
        {
            foreach(var resolution in new[]{new[]{1920,1080},new[]{1280,800}})
            {
                int count=(int)group.GetType().GetMethod("GetTotalCount").Invoke(group,null), index=-1;
                for(int i=0;i<count;i++) { var candidate=group.GetType().GetMethod("GetGameViewSize").Invoke(group,new object[]{i}); if((int)sizeType.GetProperty("width").GetValue(candidate)==resolution[0] && (int)sizeType.GetProperty("height").GetValue(candidate)==resolution[1]) {index=i;break;} }
                if(index<0) { var size=Activator.CreateInstance(sizeType,new[]{Enum.ToObject(modeType,1),(object)resolution[0],resolution[1],"Archive review"}); group.GetType().GetMethod("AddCustomSize").Invoke(group,new[]{size}); index=count; }
                sizeIndex.SetValue(window,index); window.Repaint(); await Task.Delay(650);
                Check(Screen.width==resolution[0] && Screen.height==resolution[1],"真实分辨率",null);
                for(int c=0;c<3;c++)
                {
                    component.SelectCategory(c); if(c==1) component.SelectEntry(1); await Task.Delay(200); Canvas.ForceUpdateCanvases();
                    var result=capture.GetMethod("CaptureGameView",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{resolution[0],resolution[1],null,null,false,0,"screen"});
                    File.WriteAllBytes(Path.Combine(folder,"Archive-"+c+"-"+resolution[0]+"x"+resolution[1]+".png"),Convert.FromBase64String((string)result.GetType().GetProperty("Base64").GetValue(result)));
                }
            }
        }
        finally { sizeIndex.SetValue(window,previous); window.Repaint(); component.SelectCategory(1); component.SelectEntry(1); }
    }
    private static T Ref<T>(SerializedObject data,string field) where T:Object => (T)data.FindProperty(field).objectReferenceValue;
    private static T[] Refs<T>(SerializedObject data,string field) where T:Object { var array=data.FindProperty(field); return Enumerable.Range(0,array.arraySize).Select(i=>(T)array.GetArrayElementAtIndex(i).objectReferenceValue).ToArray(); }
    private static void Check(bool condition,string name,List<string> passed) { if(!condition) throw new InvalidOperationException("未通过："+name); passed?.Add(name); }
}
