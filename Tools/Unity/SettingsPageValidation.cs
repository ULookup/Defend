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
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>设置页行为与画面验证，保存并恢复全部音频偏好。</summary>
public static class SettingsPageValidation
{
    private static readonly string[] Keys = { "Defend.MusicVolume", "Defend.EffectsVolume", "Defend.MusicMuted", "Defend.EffectsMuted", "Defend.AudioSettingsVersion", "Defend.Muted" };
    public static async Task<object> Review()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Review requires play mode.");
        var ui = Object.FindAnyObjectByType<DefenseInterface>(); var controller = new SerializedObject(ui);
        var audio = Ref<DefenseAudio>(controller,"audioSettings");
        var session = Object.FindAnyObjectByType<GameSessionRoot>();
        session.ReturnToStart(); Ref<Button>(controller,"openSettings").onClick.Invoke();
        var page = Ref<GameObject>(controller,"settingsPage");
        var view = new SerializedObject(Ref<DefenseSettingsView>(controller,"settingsView"));
        Slider music = Ref<Slider>(view,"musicSlider"), effects = Ref<Slider>(view,"effectsSlider");
        var channels = new SerializedObject(audio); var musicSource = Ref<AudioSource>(channels,"musicSource"); var effectSource = Ref<AudioSource>(channels,"effectsSource");
        var existed = Keys.Select(PlayerPrefs.HasKey).ToArray();
        var oldFloats = Keys.Select(x => PlayerPrefs.GetFloat(x)).ToArray(); var oldInts = Keys.Select(x => PlayerPrefs.GetInt(x)).ToArray();
        var passed = new List<string>();
        try
        {
            foreach (string key in Keys) PlayerPrefs.DeleteKey(key);
            audio.Load(); Check(audio.MusicVolume == .7f && audio.EffectsVolume == .7f && !audio.MusicMuted && !audio.EffectsMuted,"首次两路70且不静音",passed);
            await Task.Delay(150); Canvas.ForceUpdateCanvases();
            foreach (var rect in page.GetComponentsInChildren<RectTransform>()) Check(!float.IsNaN(rect.rect.width) && !float.IsInfinity(rect.rect.width) && rect.rect.width > 0,"布局有效",null);
            foreach (string field in new[] { "topClose", "musicMute", "effectsMute", "musicSlider", "effectsSlider" })
            {
                var component = Ref<Component>(view,field); var rect = (RectTransform)component.transform;
                var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center)) };
                var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer,hits);
                Check(hits.Count > 0 && (hits[0].gameObject == component.gameObject || hits[0].gameObject.transform.IsChildOf(component.transform)),"设置控件可点击："+field+"，命中="+(hits.Count>0?hits[0].gameObject.name:"无"),null);
            }
            passed.Add("有效布局与真实点击区域");
            music.value = 27; effects.value = 83;
            await Task.Delay(100); Canvas.ForceUpdateCanvases();
            Check(Ref<Text>(view,"musicValue").cachedTextGenerator.vertexCount >= 4 && Ref<Text>(view,"effectsValue").cachedTextGenerator.vertexCount >= 4 && Ref<Button>(view,"topClose").GetComponentInChildren<Text>().cachedTextGenerator.vertexCount >= 4,"音量数字和×有可见文字几何",passed);
            Check(Mathf.Approximately(musicSource.volume,.27f) && Mathf.Approximately(effectSource.volume,.83f) && Ref<Text>(view,"musicValue").text == "27" && Ref<Text>(view,"effectsValue").text == "83","滑杆实时改变声音与0–100数值",passed);
            Ref<Button>(view,"musicMute").onClick.Invoke();
            Check(audio.MusicMuted && !audio.EffectsMuted && musicSource.mute && !effectSource.mute && music.value == 27 && effects.value == 83,"音乐独立静音且数值保留",passed);
            Ref<Button>(view,"effectsMute").onClick.Invoke();
            Check(audio.MusicMuted && audio.EffectsMuted && effectSource.mute && effectSource.volume == .83f,"音效独立静音且数值保留",passed);
            Check(Ref<Button>(view,"musicMute").GetComponent<Image>().overrideSprite.name == "UI_Button_Settings_Mute_Off","静音显示红斜杠",passed);
            Drag(music,.61f);
            Check(!audio.MusicMuted && audio.EffectsMuted && Mathf.Abs(music.value-61) <= 1,"真实拖动音乐仅解除音乐静音",passed);
            Drag(effects,.41f);
            Check(!audio.EffectsMuted && Mathf.Abs(effects.value-41) <= 1,"真实拖动音效仅解除音效静音",passed);
            music.value = 0; effects.value = 100;
            Check(audio.MusicVolume == 0 && audio.EffectsVolume == 1,"0与100端点",passed);
            music.value = 61; effects.value = 41; audio.SetMusicMuted(true); audio.SetEffectsMuted(false); audio.Flush();
            var readerRoot = new GameObject("设置持久化验收临时对象");
            try
            {
                var reader = readerRoot.AddComponent<DefenseAudio>(); reader.Load();
                Check(reader.MusicVolume == .61f && reader.EffectsVolume == .41f && reader.MusicMuted && !reader.EffectsMuted,"新的音频组件从本地偏好恢复两音量及两静音状态",passed);
            }
            finally { Object.DestroyImmediate(readerRoot); }
            PlayerPrefs.DeleteKey(Keys[2]); PlayerPrefs.DeleteKey(Keys[3]); PlayerPrefs.DeleteKey(Keys[4]);
            PlayerPrefs.SetInt("Defend.Muted",1); PlayerPrefs.SetFloat(Keys[0],.22f); PlayerPrefs.SetFloat(Keys[1],.44f); audio.Load();
            Check(audio.MusicMuted && audio.EffectsMuted && audio.MusicVolume == .22f && audio.EffectsVolume == .44f,"旧总静音及旧音量迁移",passed);
            var outside = new PointerEventData(EventSystem.current) { position = new Vector2(8,8) }; var outsideHits = new List<RaycastResult>(); EventSystem.current.RaycastAll(outside,outsideHits);
            Check(outsideHits.Count > 0,"背景拦截",null); ExecuteEvents.Execute(outsideHits[0].gameObject,outside,ExecuteEvents.pointerClickHandler);
            Check(page.activeSelf && session.AtStartPage,"点击弹窗外不关闭也不穿透",passed);
            Ref<Button>(view,"topClose").onClick.Invoke(); Check(!page.activeSelf && session.AtStartPage,"右上角×关闭",passed);
            Check(!Ref<Button>(view,"bottomClose").gameObject.activeSelf,"设置仅保留右上角退出",passed);
            Ref<Button>(controller,"startGame").onClick.Invoke(); ui.TogglePause(); Ref<Button>(controller,"pauseSettings").onClick.Invoke();
            Ref<Button>(view,"topClose").onClick.Invoke(); Check(Ref<GameObject>(controller,"pausePage").activeSelf && session.Paused,"从暂停打开后返回暂停",passed);
        }
        finally
        {
            audio.Flush();
            for (int i = 0; i < Keys.Length; i++) { if (!existed[i]) PlayerPrefs.DeleteKey(Keys[i]); else if (i < 2) PlayerPrefs.SetFloat(Keys[i],oldFloats[i]); else PlayerPrefs.SetInt(Keys[i],oldInts[i]); }
            PlayerPrefs.Save(); audio.Load(); audio.Flush(); session.ReturnToStart(); Ref<Button>(controller,"openSettings").onClick.Invoke();
        }
        await Capture();
        return new { passed=true, checks=passed, preferencesRestored=true, screenshots="1920x1080,1280x800" };
    }

    private static void Drag(Slider slider,float fraction)
    {
        var handle = slider.handleRect; var area = (RectTransform)handle.parent;
        var pointer = new PointerEventData(EventSystem.current) { button=PointerEventData.InputButton.Left,position=RectTransformUtility.WorldToScreenPoint(null,handle.TransformPoint(handle.rect.center)) };
        ExecuteEvents.Execute(slider.gameObject,pointer,ExecuteEvents.initializePotentialDrag);
        ExecuteEvents.Execute(slider.gameObject,pointer,ExecuteEvents.pointerDownHandler);
        pointer.position=RectTransformUtility.WorldToScreenPoint(null,area.TransformPoint(new Vector3(Mathf.Lerp(area.rect.xMin,area.rect.xMax,fraction),area.rect.center.y)));
        ExecuteEvents.Execute(slider.gameObject,pointer,ExecuteEvents.dragHandler); ExecuteEvents.Execute(slider.gameObject,pointer,ExecuteEvents.pointerUpHandler);
        EventSystem.current.SetSelectedGameObject(null);
    }

    private static async Task Capture()
    {
        Type viewType=typeof(Editor).Assembly.GetType("UnityEditor.GameView"); var window=EditorWindow.GetWindow(viewType); window.Show(); window.Focus();
        var sizeIndex=viewType.GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic); int previous=(int)sizeIndex.GetValue(window);
        Type sizesType=typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
        var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var sizes=singleton.GetProperty("instance",BindingFlags.Static|BindingFlags.Public|BindingFlags.FlattenHierarchy).GetValue(null);
        var group=sizesType.GetMethod("GetGroup").Invoke(sizes,new[]{sizesType.GetProperty("currentGroupType").GetValue(sizes)});
        Type sizeType=typeof(Editor).Assembly.GetType("UnityEditor.GameViewSize"), modeType=typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizeType");
        Type capture=AppDomain.CurrentDomain.GetAssemblies().Select(x=>x.GetType("Unity.Pipeline.Editor.Commands.Capture.CaptureCommands")).First(x=>x!=null);
        string folder=Path.Combine(Path.GetDirectoryName(Application.dataPath),"Docs/Art/Settings/Previews"); Directory.CreateDirectory(folder);
        try
        {
            foreach(var resolution in new[]{new[]{1920,1080},new[]{1280,800}})
            {
                int count=(int)group.GetType().GetMethod("GetTotalCount").Invoke(group,null), index=-1;
                for(int i=0;i<count;i++) { var candidate=group.GetType().GetMethod("GetGameViewSize").Invoke(group,new object[]{i}); if((int)sizeType.GetProperty("width").GetValue(candidate)==resolution[0] && (int)sizeType.GetProperty("height").GetValue(candidate)==resolution[1]) {index=i;break;} }
                if(index<0) { var size=Activator.CreateInstance(sizeType,new[]{Enum.ToObject(modeType,1),(object)resolution[0],resolution[1],"Settings review"}); group.GetType().GetMethod("AddCustomSize").Invoke(group,new[]{size}); index=count; }
                sizeIndex.SetValue(window,index); window.Repaint(); await Task.Delay(650); Canvas.ForceUpdateCanvases();
                Check(Screen.width==resolution[0] && Screen.height==resolution[1],"真实分辨率",null);
                var result=capture.GetMethod("CaptureGameView",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{resolution[0],resolution[1],null,null,false,0,"screen"});
                File.WriteAllBytes(Path.Combine(folder,"Settings-"+resolution[0]+"x"+resolution[1]+".png"),Convert.FromBase64String((string)result.GetType().GetProperty("Base64").GetValue(result)));
            }
        }
        finally { sizeIndex.SetValue(window,previous); window.Repaint(); }
    }
    private static T Ref<T>(SerializedObject data,string field) where T:Object { return (T)data.FindProperty(field).objectReferenceValue; }
    private static void Check(bool condition,string name,List<string> passed) { if(!condition) throw new InvalidOperationException("未通过："+name); passed?.Add(name); }
}
