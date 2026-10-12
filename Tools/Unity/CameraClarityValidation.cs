using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Defend.Unity;

public static class CameraClarityValidation
{
    const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static EditorWindow GameView() => Resources.FindObjectsOfTypeAll<EditorWindow>().First(w => w.GetType().FullName == "UnityEditor.GameView");
    public static object Inspect()
    {
        var view = GameView();
        var data = new SerializedObject(view);
        var values = new System.Collections.Generic.List<string>();
        var property = data.GetIterator();
        while (property.Next(true))
            if (property.propertyPath.Contains("Resolution") || property.propertyPath.Contains("TargetSize") || property.propertyPath.Contains("Scale") || property.propertyPath.Contains("SelectedSize"))
                values.Add(property.propertyPath + "=" + (property.propertyType == SerializedPropertyType.Boolean ? property.boolValue.ToString() : property.propertyType == SerializedPropertyType.Float ? property.floatValue.ToString() : property.propertyType == SerializedPropertyType.Integer ? property.intValue.ToString() : property.propertyType.ToString()));
        var camera = Camera.main;
        var extra = camera != null ? camera.GetComponent<UniversalAdditionalCameraData>() : null;
        return new {
            playing = Application.isPlaying,
            editorPaused = EditorApplication.isPaused,
            frame = Time.frameCount,
            canvas = UnityEngine.Object.FindObjectsByType<Canvas>().Select(c=>new {c.name,c.scaleFactor,rect=c.pixelRect.ToString()}).ToArray(),
            screen = new { Screen.width, Screen.height, Screen.dpi },
            pixelsPerPoint = EditorGUIUtility.pixelsPerPoint,
            position = view.position.ToString(),
            values,
            properties = view.GetType().GetProperties(Members).Where(p=>p.Name.ToLowerInvariant().Contains("resolution") || p.Name.Contains("Size") || p.Name.Contains("Scale")).Select(p=>new {p.Name,type=p.PropertyType.FullName,p.CanWrite}).ToArray(),
            scrollBehavior = InputSystem.settings.scrollDeltaBehavior.ToString(),
            camera = camera == null ? null : new { camera.pixelWidth, camera.pixelHeight, camera.orthographicSize, camera.allowDynamicResolution, postProcessing = extra != null && extra.renderPostProcessing },
            renderScale = (GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset)?.renderScale
        };
    }
    public static object MeasureScroll()
    {
        if (!Application.isPlaying) throw new Exception("Play mode required");
        var camera=Camera.main;
        var controller=UnityEngine.Object.FindAnyObjectByType<WorldCameraViewport>();
        var data=new SerializedObject(controller);
        float originalZoom=data.FindProperty("zoomFactor").floatValue;
        var position=camera.transform.position;
        float size=ViewSize(camera);
        var behavior=InputSystem.settings.backgroundBehavior;
        try {
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.QueueStateEvent(Mouse.current,new MouseState {position=camera.pixelRect.center,scroll=new Vector2(0,1)});
            InputSystem.Update();controller.SendMessage("LateUpdate");
            return new { before=size,after=ViewSize(camera),reductionPercent=100*(1-ViewSize(camera)/size),scroll=Mouse.current.scroll.ReadValue().y,setting=InputSystem.settings.scrollDeltaBehavior.ToString() };
        } finally {
            InputSystem.QueueStateEvent(Mouse.current,new MouseState {position=camera.pixelRect.center});InputSystem.Update();
            InputSystem.settings.backgroundBehavior=behavior;
            data.Update();data.FindProperty("zoomFactor").floatValue=originalZoom;data.ApplyModifiedPropertiesWithoutUndo();
            camera.transform.position=position;if(camera.orthographic)camera.orthographicSize=size;
        }
    }
    public static object DisableLowResolutionPreview()
    {
        var view = GameView();
        var property = view.GetType().GetProperty("lowResolutionForAspectRatios", Members);
        if (property == null || !property.CanWrite) throw new Exception("Game view resolution setting unavailable");
        property.SetValue(view, false);
        view.Repaint();
        return new { lowResolution = property.GetValue(view) };
    }
    public static object EnableLowResolutionBaseline()
    {
        var view = GameView();
        view.GetType().GetProperty("lowResolutionForAspectRatios", Members).SetValue(view, true);
        view.Focus();view.Repaint();
        return new { lowResolution = true };
    }
    public static object FocusPreview() { var view=GameView();view.Focus();view.Repaint();return Inspect(); }
    public static object BeginCapture()
    {
        bool previous=Application.runInBackground;
        Application.runInBackground=true;
        var session=UnityEngine.Object.FindAnyObjectByType<GameSessionRoot>();
        if(!session.Paused)session.TogglePause();
        return new {previousRunInBackground=previous,frame=Time.frameCount};
    }
    public static object EndCapture() { Application.runInBackground=false;return new { frame=Time.frameCount }; }
    public static object CaptureScreenBefore()
    {
        ScreenCapture.CaptureScreenshot("Logs/CameraClarity/before-screen.png");return Inspect();
    }
    public static object CaptureScreenAfter()
    {
        var view=GameView();view.Focus();view.Repaint();EditorApplication.QueuePlayerLoopUpdate();
        ScreenCapture.CaptureScreenshot("Logs/CameraClarity/after-screen.png");
        return Inspect();
    }
    public static object ValidateZoom()
    {
        if (!Application.isPlaying) throw new Exception("Play mode required");
        var camera=Camera.main;
        var controller=UnityEngine.Object.FindAnyObjectByType<WorldCameraViewport>();
        var session=UnityEngine.Object.FindAnyObjectByType<GameSessionRoot>();
        var data=new SerializedObject(controller);
        float originalZoom=data.FindProperty("zoomFactor").floatValue;
        var originalPosition=camera.transform.position;
        var originalRotation=camera.transform.rotation;
        float originalSize=ViewSize(camera);
        var originalBackground=InputSystem.settings.backgroundBehavior;
        var originalScroll=InputSystem.settings.scrollDeltaBehavior;
        bool originalPaused=session.Paused;
        long originalTick=session.World.CurrentTick;
        int originalBuildings=session.World.Buildings.Count;
        var point=camera.pixelRect.center+new Vector2(50,25);
        try {
            if(!session.Paused)session.TogglePause();
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.scrollDeltaBehavior=InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms;
            Vector3 anchored=Ground(camera,point);
            SendScroll(controller,point,1);
            float normalizedSize=ViewSize(camera);
            Require(normalizedSize/originalSize > .75f && normalizedSize/originalSize < .85f,"单格缩放应约为20%");
            float anchorError=Vector3.Distance(anchored,Ground(camera,point));
            Require(anchorError < .001f,"缩放没有保持鼠标指向: " + anchorError + " before=" + anchored + " after=" + Ground(camera,point));
            SendScroll(controller,point,-1);
            Require(Mathf.Abs(ViewSize(camera)-originalSize)<.001f,"反向缩放没有恢复视野");
            for(int i=0;i<4;i++)SendScroll(controller,point,.25f);
            Require(Mathf.Abs(ViewSize(camera)-normalizedSize)<.001f,"细分滚轮输入不连续");
            SendScroll(controller,point,-1);
            if(Application.platform==RuntimePlatform.WindowsEditor) {
                InputSystem.settings.scrollDeltaBehavior=InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange;
                SendScroll(controller,point,120);
                Require(Mathf.Abs(ViewSize(camera)-normalizedSize)<.001f,"Windows原始滚轮单位换算错误");
                SendScroll(controller,point,-120);
            }
            InputSystem.settings.scrollDeltaBehavior=InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms;
            SendScroll(controller,new Vector2(camera.pixelRect.xMin-10,point.y),1);
            Require(Mathf.Abs(ViewSize(camera)-originalSize)<.001f,"界面区域滚轮影响了地图");
            SendScroll(controller,camera.pixelRect.center,30);
            data.Update();float minimumZoom=data.FindProperty("zoomFactor").floatValue;
            Require(Mathf.Abs(minimumZoom-.28f)<.001f,"近景范围限位错误");
            SendScroll(controller,camera.pixelRect.center,-30);
            data.Update();float maximumZoom=data.FindProperty("zoomFactor").floatValue;
            Require(Mathf.Abs(maximumZoom-1.4f)<.001f,"远景范围限位错误");
            Require(Quaternion.Angle(camera.transform.rotation,originalRotation)<.001f,"镜头角度被修改");
            Require(session.World.CurrentTick==originalTick && session.World.Buildings.Count==originalBuildings,"镜头输入修改了模拟状态");
            return new {success=true,originalSize,normalizedSize,reductionPercent=100*(1-normalizedSize/originalSize),anchorError,minimumZoom,maximumZoom,
                checkedBehaviors="归一化输入、Windows原始单位、反向恢复、连续细分输入、指针保持、界面防穿透、缩放限位、暂停时操作、模拟状态不变"};
        } finally {
            InputSystem.QueueStateEvent(Mouse.current,new MouseState {position=camera.pixelRect.center});InputSystem.Update();
            InputSystem.settings.backgroundBehavior=originalBackground;
            InputSystem.settings.scrollDeltaBehavior=originalScroll;
            data.Update();data.FindProperty("zoomFactor").floatValue=originalZoom;data.ApplyModifiedPropertiesWithoutUndo();
            camera.transform.SetPositionAndRotation(originalPosition,originalRotation);if(camera.orthographic)camera.orthographicSize=originalSize;
            if(session.Paused!=originalPaused)session.TogglePause();
        }
    }
    static float ViewSize(Camera camera) => camera.orthographic ? camera.orthographicSize : Vector3.Distance(camera.transform.position, Ground(camera, camera.pixelRect.center)) * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
    static void SendScroll(WorldCameraViewport controller,Vector2 position,float amount)
    {
        InputSystem.QueueStateEvent(Mouse.current,new MouseState {position=position,scroll=new Vector2(0,amount)});
        InputSystem.Update();controller.SendMessage("LateUpdate");
    }
    static Vector3 Ground(Camera camera,Vector2 point)
    {
        var ray=camera.ScreenPointToRay(point);
        if(!new Plane(Vector3.up,Vector3.zero).Raycast(ray,out float distance))throw new Exception("No ground intersection");
        return ray.GetPoint(distance);
    }
    static void Require(bool condition,string message) { if(!condition)throw new Exception(message); }
}
