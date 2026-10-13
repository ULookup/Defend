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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using Object = UnityEngine.Object;

/// <summary>局外按钮焦点和文字对比度回归；不启动对局或修改玩家设置。</summary>
public static class MenuFeedbackValidation
{
    public static async Task<object> Review()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Requires Play mode");
        var session = Object.FindAnyObjectByType<GameSessionRoot>();
        if (!session.AtStartPage) throw new InvalidOperationException("Requires start page; preserve the current game.");
        var ui = Object.FindAnyObjectByType<DefenseInterface>(); var bindings = new SerializedObject(ui);
        var checks = new List<string>(); var es = EventSystem.current;
        var start = Ref<Button>(bindings, "startGame"); var help = Ref<Button>(bindings, "openHelp");
        Check(es.firstSelectedGameObject == start.gameObject && es.currentSelectedGameObject == start.gameObject,
            "首次进入默认选中开始守望", checks);
        Check(Indicator(start).activeInHierarchy, "默认焦点有可见边角", checks);
        await ArrowDown();
        Check(es.currentSelectedGameObject == help.gameObject && Indicator(help).activeInHierarchy && !Indicator(start).activeSelf,
            "真实方向键移动焦点与可见提示", checks);
        foreach (string field in new[] { "startGame", "openHelp", "openArchive", "openSettings", "quit" })
        {
            var button = Ref<Button>(bindings, field); es.SetSelectedGameObject(button.gameObject);
            Check(Indicator(button).activeInHierarchy, "可见导航焦点：" + field, checks);
            Check(Indicator(button).GetComponentsInChildren<Graphic>().All(x => !x.raycastTarget), "焦点装饰不拦截点击", null);
            if (field != "quit") Check(((Image)button.targetGraphic).overrideSprite == ((Image)button.targetGraphic).sprite,
                "导航焦点保持默认按钮图片", null);
        }
        es.SetSelectedGameObject(start.gameObject); await Task.Delay(100); SaveFrame("Main-focus-fixed.png");
        var archive = Ref<DefenseArchiveView>(bindings, "archiveView"); var view = new SerializedObject(archive);
        Ref<Button>(bindings, "openArchive").onClick.Invoke();
        var categories = Refs<Button>(view, "categories"); var entries = Refs<Button>(view, "entries");
        for (int c = 0; c < 3; c++)
        {
            archive.SelectCategory(c);
            await Contrast(categories[c], true, false, checks);
            await Contrast(categories[(c + 1) % 3], false, false, checks);
            archive.SelectCategory(c);
            await Contrast(entries[0], true, c == 0, checks);
            if (c != 0) await Contrast(entries[1], false, false, checks);
        }
        archive.SelectCategory(1); es.SetSelectedGameObject(categories[1].gameObject);
        Enter(entries[1]); await Task.Delay(100); SaveFrame("Archive-hover-fixed.png"); Exit(entries[1]);
        Ref<Button>(view, "topClose").onClick.Invoke();
        foreach (string name in new[] { "Settings", "Archive" })
        {
            var open = Ref<Button>(bindings, "open" + name);
            var modal = new SerializedObject(Ref<Component>(bindings, name == "Settings" ? "settingsView" : "archiveView"));
            for (int i = 0; i < 3; i++)
            {
                es.SetSelectedGameObject(open.gameObject);
                ExecuteEvents.Execute(open.gameObject, new BaseEventData(es), ExecuteEvents.submitHandler);
                await Task.Delay(200); Exit(open);
                var close = Ref<Button>(modal, "topClose"); var hidden = Ref<Button>(modal, "bottomClose");
                Check(close.gameObject.activeInHierarchy && !hidden.gameObject.activeSelf, "只有一个返回入口：" + name, null);
                es.SetSelectedGameObject(close.gameObject);
                ExecuteEvents.Execute(close.gameObject, new BaseEventData(es), ExecuteEvents.submitHandler);
                await Task.Delay(200);
                Check(es.currentSelectedGameObject == open.gameObject && Indicator(open).activeInHierarchy,
                    "返回恢复入口焦点：" + name, null);
                Check(((Image)open.targetGraphic).overrideSprite == ((Image)open.targetGraphic).sprite,
                    "返回后没有持续按压：" + name, null);
            }
            checks.Add(name + " 三轮键盘打开及返回通过");
        }
        Check(!Ref<GameObject>(bindings, "settingsPage").GetComponentsInChildren<Text>(true).Any(x => x.name == "设置保存提示"),
            "设置底部两句提示保持删除", checks);
        ui.ShowStart(); await Task.Delay(100);
        Check(es.currentSelectedGameObject == start.gameObject && Indicator(start).activeInHierarchy, "再次进入首页恢复默认焦点", checks);
        return new { passed = true, checks };
    }

    private static async Task Contrast(Button button, bool selected, bool story, List<string> checks)
    {
        var es = EventSystem.current; var pointer = new PointerEventData(es) { button = PointerEventData.InputButton.Left };
        es.SetSelectedGameObject(null); Exit(button); await Task.Delay(60);
        var image = (Image)button.targetGraphic; var label = button.GetComponentInChildren<Text>();
        Check(label.color.grayscale < .4f, "浅色底正常深色文字", null);
        Enter(button); await Task.Delay(60);
        Check(selected || story ? label.color.grayscale < .4f : label.color.grayscale > .8f, "悬停文字可读", null);
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler); await Task.Delay(60);
        Check(story ? label.color.grayscale < .4f : label.color.grayscale > .8f, "按下文字可读", null);
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler); Exit(button); es.SetSelectedGameObject(null); await Task.Delay(60);
        Check(image.overrideSprite == image.sprite && label.color.grayscale < .4f, "离开后恢复默认文字", null);
        es.SetSelectedGameObject(button.gameObject); await Task.Delay(60);
        Check(selected || story ? label.color.grayscale < .4f : label.color.grayscale > .8f, "键盘焦点文字可读", null);
        ExecuteEvents.Execute(button.gameObject, new BaseEventData(es), ExecuteEvents.submitHandler); await Task.Delay(60);
        Check(story ? label.color.grayscale < .4f : label.color.grayscale > .8f, "键盘提交期间文字可读", null);
        await Task.Delay(200); es.SetSelectedGameObject(null); Exit(button); await Task.Delay(60);
        Check(image.overrideSprite == image.sprite && label.color.grayscale < .4f, "提交结束后恢复选中样式", null);
        checks.Add(button.name + " 悬停、按下、离开、导航和键盘提交对比度通过");
    }
    private static async Task ArrowDown()
    {
        var module = EventSystem.current.currentInputModule as InputSystemUIInputModule;
        var keyboard = Keyboard.current;
        if (module == null || keyboard == null) throw new InvalidOperationException("No keyboard input module");
        var window = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView")); window.Show(); window.Focus();
        await Task.Delay(150);
        var held = keyboard.allKeys.Where(x => x.isPressed).Select(x => x.keyCode).ToArray();
        bool observed = false;
        Action<InputAction.CallbackContext> callback = context => { if (context.ReadValue<Vector2>().y < 0) observed = true; };
        module.move.action.performed += callback;
        try { InputSystem.QueueStateEvent(keyboard, new KeyboardState(held.Concat(new[] { Key.DownArrow }).Distinct().ToArray())); await Task.Delay(170); }
        finally { InputSystem.QueueStateEvent(keyboard, new KeyboardState(held)); module.move.action.performed -= callback; }
        await Task.Delay(120); Check(observed, "Input System 收到真实方向键", null);
    }
    private static GameObject Indicator(Button button) => Ref<GameObject>(new SerializedObject(button.GetComponent<MainMenuButtonFeedback>()), "focusIndicator");
    private static void Enter(Button button) => ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
    private static void Exit(Button button) => ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
    private static void SaveFrame(string name)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("Unity.Pipeline.Editor.Commands.Capture.CaptureCommands")).First(x => x != null);
        var capture = type.GetMethod("CaptureGameView", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { Screen.width, Screen.height, null, null, false, 0, "screen" });
        var folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Docs/Validation/MenuPagesAudit"); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name), Convert.FromBase64String((string)capture.GetType().GetProperty("Base64").GetValue(capture)));
    }
    private static T Ref<T>(SerializedObject data, string field) where T : Object => (T)data.FindProperty(field).objectReferenceValue;
    private static T[] Refs<T>(SerializedObject data, string field) where T : Object { var p = data.FindProperty(field); return Enumerable.Range(0, p.arraySize).Select(i => (T)p.GetArrayElementAtIndex(i).objectReferenceValue).ToArray(); }
    private static void Check(bool condition, string name, List<string> checks) { if (!condition) throw new InvalidOperationException(name); checks?.Add(name); }
}
