using System;
using System.Linq;
using System.Threading.Tasks;
using Defend.Unity;
using Defend.Simulation;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// 手动短验收的单次操作入口，不驱动模拟循环、不改战斗数值，也不自动购买或推进整局。
public static class ConfirmedGameplayInteraction
{
    public static object KeepEditorUpdating() { Application.runInBackground=true; UnityEditor.EditorApplication.QueuePlayerLoopUpdate(); return new{Application.runInBackground,Application.isFocused}; }
    public static object SelectBuilding(int x,int z) { var s=UnityEngine.Object.FindAnyObjectByType<GameSessionRoot>(); s.Select(s.World.FindBuildingAt(x,z)?.Id??0); return Snapshot(); }
    public static object InterfaceState()
    {
        return new { editorPaused=UnityEditor.EditorApplication.isPaused,
            texts=UnityEngine.Object.FindObjectsByType<Text>().Where(t=>t.gameObject.activeInHierarchy&&t.transform.parent.name.StartsWith("结束")).Select(t=>new{parent=t.transform.parent.name,t.text,t.enabled,color=t.color.ToString(),rect=t.rectTransform.rect.ToString(),vertices=t.cachedTextGenerator.vertexCount}).ToArray() };
    }
    public static object Routes()
    {
        var session=UnityEngine.Object.FindAnyObjectByType<GameSessionRoot>();
        return session.Battle.Enemies.Select(e=>new { e.Id,e.X,e.Z,e.AttackPositionX,e.AttackPositionZ, fields=typeof(EnemyState).GetFields(System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Where(f=>new[]{"RouteCursor","BlockedSinceTick","YieldUntilTick","YieldX","YieldZ","NextCellX","NextCellZ"}.Contains(f.Name)).ToDictionary(f=>f.Name,f=>f.GetValue(e))}).ToArray();
    }
    public static object Snapshot()
    {
        var session=UnityEngine.Object.FindAnyObjectByType<GameSessionRoot>();
        return new { home=session.AtStartPage, paused=session.Paused, tick=session.World.CurrentTick, state=session.Battle.State.ToString(), wave=session.Battle.WaveNumber, credits=session.World.Credits,
            pending=session.Battle.RemainingToSpawn, kills=session.Battle.Kills, reward=session.Battle.PendingKillReward,
            buildings=session.World.Buildings.Where(x=>x.IsAlive&&!x.IsSold).Select(x=>new{id=x.Id,kind=x.Kind.ToString(),x=x.X,z=x.Z,hp=x.Health,energy=x.StoredEnergy,incoming=x.CommittedIncoming,received=x.ReceivedLastSecond,discharged=x.DischargedLastSecond}).ToArray(),
            enemies=session.Battle.Enemies.Select(x=>new{id=x.Id,kind=x.Kind.ToString(),hp=x.Health,x=x.X,z=x.Z,final=x.FinalTargetBuildingId,attack=x.CurrentAttackBuildingId,action=x.CurrentAction.ToString()}).ToArray(),
            selected=session.SelectedEnemy?.Id ?? session.SelectedBuildingId,feedback=session.Feedback,
            buttons=UnityEngine.Object.FindObjectsByType<Button>().Where(x=>x.gameObject.activeInHierarchy&&x.interactable).Select(x=>x.name).ToArray()};
    }
    public static object Click(string name)
    {
        var button=UnityEngine.Object.FindObjectsByType<Button>().Single(x=>x.name==name&&x.gameObject.activeInHierarchy);
        if(!button.interactable) throw new InvalidOperationException("按钮不可用："+name);
        var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left};
        ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
        return Snapshot();
    }
    public static async Task<object> Ground(float x,float z)
    {
        var session=UnityEngine.Object.FindAnyObjectByType<GameSessionRoot>();
        var camera=Camera.allCameras.First(c=>c.name.Contains("主摄像机"));
        Vector2 position=camera.WorldToScreenPoint(new Vector3(x,0,z));
        InputSystem.QueueStateEvent(Mouse.current,new MouseState{position=position}.WithButton(MouseButton.Left));
        await Task.Delay(100);
        InputSystem.QueueStateEvent(Mouse.current,new MouseState{position=position});
        await Task.Delay(100);
        return Snapshot();
    }
    public static async Task<object> Enemy(long id)
    {
        var session=UnityEngine.Object.FindAnyObjectByType<GameSessionRoot>();
        var view=UnityEngine.Object.FindObjectsByType<EnemyView>().Single(x=>x.EnemyId==id);
        var camera=Camera.allCameras.First(c=>c.name.Contains("主摄像机"));
        Vector2 position=camera.WorldToScreenPoint(view.GetComponent<Collider>().bounds.center);
        InputSystem.QueueStateEvent(Mouse.current,new MouseState{position=position}.WithButton(MouseButton.Left));
        await Task.Delay(30);
        InputSystem.QueueStateEvent(Mouse.current,new MouseState{position=position});
        await Task.Delay(30);
        return Snapshot();
    }
    public static object Sound(float music,float effects,bool muted)
    {
        UnityEngine.Object.FindObjectsByType<Slider>().Single(x=>x.name=="音乐音量滑块").value=music;
        UnityEngine.Object.FindObjectsByType<Slider>().Single(x=>x.name=="音效音量滑块").value=effects;
        UnityEngine.Object.FindObjectsByType<Toggle>().Single(x=>x.name=="静音开关").isOn=muted;
        var settings=UnityEngine.Object.FindAnyObjectByType<DefenseAudio>();
        return new{settings.MusicVolume,settings.EffectsVolume,settings.Muted,sources=settings.GetComponentsInChildren<AudioSource>().Select(x=>new{x.name,x.volume,clip=x.clip?.name,x.isPlaying}).ToArray()};
    }
    public static object Focus(float x,float z)
    { UnityEngine.Object.FindAnyObjectByType<WorldCameraViewport>().Focus(x,z); return Snapshot(); }
}
