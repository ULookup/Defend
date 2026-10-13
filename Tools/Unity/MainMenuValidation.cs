using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using Defend.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>通过真实按钮与设置控件检查开始页，不改写场景或用户的持久设置。</summary>
public static class MainMenuValidation
{
    public static async Task<object> Review()
    {
        var interactions = await Interactions();
        var captures = await Capture();
        return new { interactions, captures };
    }

    public static object Apply()
    {
        Defend.Editor.MainMenuArtAuthoring.Apply();
        return new { prefab = Defend.Editor.DefenseInterfaceAuthoring.PrefabPath, applied = true };
    }

    public static object Probe()
    {
        var ui = Object.FindAnyObjectByType<DefenseInterface>(FindObjectsInactive.Include);
        if (ui == null) throw new InvalidOperationException("当前场景没有正式界面。");
        var data = new SerializedObject(ui);
        var start = Ref<GameObject>(data, "startPage");
        return new { scene = ui.gameObject.scene.path, startPage = start.name,
            controls = start.GetComponentsInChildren<Button>(true).Select(x => x.name).ToArray(),
            playing = Application.isPlaying };
    }

    public static async Task<object> Interactions()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("请在播放模式验证。");
        var ui = Object.FindAnyObjectByType<DefenseInterface>();
        var session = Object.FindAnyObjectByType<GameSessionRoot>();
        var data = new SerializedObject(ui);
        ui.ShowStart();
        Check(session.AtStartPage, "进入场景即显示开始页");
        Check(Ref<GameObject>(data, "startPage").activeInHierarchy, "开始页可见");
        Check(Object.FindObjectsByType<EventSystem>().Length == 1, "唯一事件系统");
        Canvas.ForceUpdateCanvases();
        foreach (var rect in Ref<GameObject>(data, "startPage").GetComponentsInChildren<RectTransform>(true))
            Check(!float.IsNaN(rect.rect.width) && !float.IsNaN(rect.rect.height) && !float.IsInfinity(rect.rect.width), "开始页尺寸有效：" + rect.name);
        foreach (string field in new[] { "startGame", "openHelp", "openArchive", "openSettings" })
        {
            var button = Ref<Button>(data, field);
            var rect = (RectTransform)button.transform;
            var pointerAtButton = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointerAtButton, hits);
            Check(hits.Count > 0 && hits[0].gameObject == button.gameObject, "可点击且未被背景遮挡：" + field);
        }
        foreach (var entry in new[] { new[] { "openHelp", "helpPage", "closeHelp" }, new[] { "openArchive", "archivePage", "closeArchive" }, new[] { "openSettings", "settingsPage", "closeSettings" } })
        {
            Ref<Button>(data, entry[0]).onClick.Invoke();
            Check(Ref<GameObject>(data, entry[1]).activeInHierarchy, entry[1] + "打开");
            Check(ui.BlocksWorldInput, "弹窗阻止场景操作");
            Ref<Button>(data, entry[2]).onClick.Invoke();
            Check(!Ref<GameObject>(data, entry[1]).activeSelf, entry[1] + "关闭");
        }
        Ref<Button>(data, "openArchive").onClick.Invoke();
        var categories = data.FindProperty("archiveCategories");
        for (int i = 0; i < categories.arraySize; i++)
        {
            ((Button)categories.GetArrayElementAtIndex(i).objectReferenceValue).onClick.Invoke();
            Check(!string.IsNullOrEmpty(Ref<Text>(data, "archiveBody").text), "档案分类正文");
        }
        Ref<Button>(data, "closeArchive").onClick.Invoke();

        // 设置控件产生真实事件；finally 恢复原值，避免改变用户偏好。
        var audio = Ref<DefenseAudio>(data, "audioSettings");
        var music = Ref<Slider>(data, "musicVolume"); var effects = Ref<Slider>(data, "effectsVolume");
        bool styled = Ref<DefenseSettingsView>(data, "settingsView") != null;
        float originalMusic = music.value, originalEffects = effects.value;
        bool originalMusicMute = audio.MusicMuted, originalEffectsMute = audio.EffectsMuted;
        try
        {
            music.value = styled ? 21 : .21f; effects.value = styled ? 31 : .31f; audio.SetMuted(true);
            Check(Mathf.Approximately(audio.MusicVolume, .21f) && Mathf.Approximately(audio.EffectsVolume, .31f) && audio.Muted, "音量与静音生效");
            var channels = new SerializedObject(audio);
            Check(Ref<AudioSource>(channels, "musicSource").clip != null && Ref<AudioSource>(channels, "effectsSource").clip != null, "音频通道已绑定");
            Check(Ref<AudioSource>(channels, "musicSource").mute, "静音关闭音乐通道");
        }
        finally { music.value = originalMusic; effects.value = originalEffects; audio.SetMusicMuted(originalMusicMute); audio.SetEffectsMuted(originalEffectsMute); audio.Flush(); }

        var start = Ref<Button>(data, "startGame");
        var pointer = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(start.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
        ExecuteEvents.Execute(start.gameObject, pointer, ExecuteEvents.pointerDownHandler);
        Check(start.GetComponent<Image>().overrideSprite.name.EndsWith("Pressed"), "按下显示 Pressed 素材");
        ExecuteEvents.Execute(start.gameObject, pointer, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(start.gameObject, pointer, ExecuteEvents.pointerExitHandler);
        start.onClick.Invoke();
        Check(!session.AtStartPage && Ref<GameObject>(data, "hud").activeInHierarchy, "开始守望进入准备阶段");
        session.ReturnToStart();
        Check(session.AtStartPage && Ref<GameObject>(data, "startPage").activeInHierarchy, "可返回开始页");
        EventSystem.current.SetSelectedGameObject(null);
        Canvas.ForceUpdateCanvases(); await Task.Delay(200);
        return new { passed = true, checks = "初始显示、有效布局、三个档案分类与弹窗交互、音量与静音、按钮按下状态、进入准备及返回", state = session.Battle.State.ToString() };
    }

    public static async Task<object> Capture()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Capture requires play mode.");
        var ui = Object.FindAnyObjectByType<DefenseInterface>(); ui.ShowStart();
        EventSystem.current.SetSelectedGameObject(null);
        var assembly = typeof(Editor).Assembly;
        Type viewType = assembly.GetType("UnityEditor.GameView");
        var window = EditorWindow.GetWindow(viewType); window.Show(); window.Focus();
        var sizeIndex = viewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        int previous = (int)sizeIndex.GetValue(window);
        Type sizesType = assembly.GetType("UnityEditor.GameViewSizes");
        var singletonType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var sizes = singletonType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy).GetValue(null);
        var groupType = sizesType.GetProperty("currentGroupType").GetValue(sizes);
        var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { groupType });
        Type sizeType = assembly.GetType("UnityEditor.GameViewSize");
        Type modeType = assembly.GetType("UnityEditor.GameViewSizeType");
        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Docs/Art/MainMenu/Previews"); Directory.CreateDirectory(folder);
        try
        {
            foreach (var resolution in new[] { new[] { 1920, 1080 }, new[] { 1280, 800 } })
            {
                int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null), index = -1;
                for (int i = 0; i < count; i++)
                {
                    var candidate = group.GetType().GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
                    if ((int)sizeType.GetProperty("width").GetValue(candidate) == resolution[0] && (int)sizeType.GetProperty("height").GetValue(candidate) == resolution[1]) { index = i; break; }
                }
                if (index < 0)
                {
                    var size = Activator.CreateInstance(sizeType, new[] { Enum.ToObject(modeType, 1), (object)resolution[0], resolution[1], "Main menu " + resolution[0] + "x" + resolution[1] });
                    group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size }); index = count;
                }
                sizeIndex.SetValue(window, index); window.Repaint(); await Task.Delay(700); Canvas.ForceUpdateCanvases();
                Check(Screen.width == resolution[0] && Screen.height == resolution[1], "Game View 真实分辨率");
                CaptureFrame(Path.Combine(folder, "MainMenu-" + resolution[0] + "x" + resolution[1] + ".png"), resolution[0], resolution[1]);
            }
            Ref<Button>(new SerializedObject(ui), "openSettings").onClick.Invoke();
            await Task.Delay(300); CaptureFrame(Path.Combine(folder, "Settings-1280x800.png"), 1280, 800);
            Ref<Button>(new SerializedObject(ui), "closeSettings").onClick.Invoke();
            return new { captured = true, resolutions = "1920x1080,1280x800", folder };
        }
        finally { sizeIndex.SetValue(window, previous); window.Repaint(); }
    }

    private static void CaptureFrame(string path, int width, int height)
    {
        // 读取 Game View 的真实渲染目标，避免 Windows 150% 缩放裁切 ScreenCapture。
        Type capture = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("Unity.Pipeline.Editor.Commands.Capture.CaptureCommands")).First(x => x != null);
        var result = capture.GetMethod("CaptureGameView", BindingFlags.Static | BindingFlags.Public).Invoke(null,
            new object[] { width, height, null, null, false, 0, "screen" });
        string encoded = (string)result.GetType().GetProperty("Base64").GetValue(result);
        File.WriteAllBytes(path, Convert.FromBase64String(encoded));
    }

    private static T Ref<T>(SerializedObject data, string name) where T : Object { return (T)data.FindProperty(name).objectReferenceValue; }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("未通过：" + message); }
}
