using System;
using System.Linq;
using Defend.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

public static class PerspectiveCameraValidation
{
    const string ScenePath = "Assets/Defend/Content/Scenes/Dev/IndustrialDefense.unity";
    public static object Inspect()
    {
        var scene = SceneManager.GetActiveScene();
        return new { scene.path, scene.isDirty, playing = Application.isPlaying,
            cameras = UnityEngine.Object.FindObjectsByType<Camera>().Select(c => new { c.name, c.orthographic, c.fieldOfView, position = c.transform.position.ToString() }).ToArray() };
    }
    public static object Apply()
    {
        if (Application.isPlaying) throw new Exception("请先退出播放模式。");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || scene.isDirty) throw new Exception("只迁移已保存的工业防线场景。");
        var controller = UnityEngine.Object.FindAnyObjectByType<WorldCameraViewport>();
        var data = new SerializedObject(controller);
        var camera = (Camera)data.FindProperty("worldCamera").objectReferenceValue;
        Undo.RecordObjects(new UnityEngine.Object[] { controller, camera, camera.transform, camera.gameObject }, "改为近距离透视俯视");
        data.FindProperty("zoomFactor").floatValue = .4f;
        data.ApplyModifiedProperties();
        camera.name = "主摄像机 · 60度透视俯视";
        camera.orthographic = false;
        camera.fieldOfView = 50;
        camera.transform.rotation = Quaternion.Euler(60, 0, 0);
        camera.transform.position = new Vector3(26, 0, 20) - camera.transform.forward * (8.4f / Mathf.Tan(25 * Mathf.Deg2Rad));
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("场景保存失败。");
        return Inspect();
    }
    public static object ValidatePan()
    {
        if (!Application.isPlaying) throw new Exception("需要播放模式。");
        var camera = Camera.main;
        var controller = UnityEngine.Object.FindAnyObjectByType<WorldCameraViewport>();
        var original = camera.transform.position;
        var background = InputSystem.settings.backgroundBehavior;
        var session = UnityEngine.Object.FindAnyObjectByType<GameSessionRoot>();
        bool paused = session.Paused;
        try
        {
            if (!paused) session.TogglePause();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Require(!camera.orthographic && Mathf.Abs(camera.fieldOfView - 50) < .01f, "透视投影参数错误");
            Require(Mathf.Abs(camera.transform.eulerAngles.x - 60) < .01f, "俯视角度错误");
            var start = camera.pixelRect.center;
            var end = start + new Vector2(75, 35);
            var anchor = Ground(camera, start);
            Send(controller, start, false);
            Send(controller, start, true);
            Send(controller, end, true);
            float error = Vector3.Distance(anchor, Ground(camera, end));
            Require(error < .001f && Vector3.Distance(original, camera.transform.position) > .1f, "中键拖动没有跟随地面: " + error + " moved=" + Vector3.Distance(original, camera.transform.position));
            Send(controller, end, false);
            // 多帧更新及宽窄视口切换不应移动地面焦点。
            var focus = GroundCenter(camera);
            for (int i = 0; i < 100; i++) controller.SendMessage("LateUpdate");
            float drift = Vector3.Distance(focus, GroundCenter(camera));
            Require(drift < .001f, "连续更新导致镜头漂移");
            var zoom = typeof(WorldCameraViewport).GetMethod("ApplyZoom", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var size in new[] { new Vector2(500, 900), new Vector2(1400, 700) })
            {
                zoom.Invoke(controller, new object[] { size.x, size.y });
                Require(Vector3.Distance(focus, GroundCenter(camera)) < .001f, "视口变化导致焦点移动");
            }
            return new { success = true, dragAnchorError = error, drift, perspective = true, pitch = 60, fieldOfView = 50 };
        }
        finally
        {
            Send(controller, camera.pixelRect.center, false);
            camera.transform.position = original;
            InputSystem.settings.backgroundBehavior = background;
            if (session.Paused != paused) session.TogglePause();
        }
    }
    static void Send(WorldCameraViewport controller, Vector2 position, bool pressed)
    {
        InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = position }.WithButton(MouseButton.Middle, pressed));
        InputSystem.Update();
        controller.SendMessage("LateUpdate");
    }
    static Vector3 Ground(Camera camera, Vector2 screen)
    {
        var ray = camera.ScreenPointToRay(screen);
        if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance)) throw new Exception("未命中地面。");
        return ray.GetPoint(distance);
    }
    static Vector3 GroundCenter(Camera camera)
    {
        var ray = new Ray(camera.transform.position, camera.transform.forward);
        if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance)) throw new Exception("未命中地面。");
        return ray.GetPoint(distance);
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
