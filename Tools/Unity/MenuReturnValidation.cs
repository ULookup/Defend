using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Defend.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class MenuReturnValidation
{
    public static async Task<object> Baseline()
    {
        var ui=Object.FindAnyObjectByType<DefenseInterface>(); var data=new SerializedObject(ui);
        Object.FindAnyObjectByType<GameSessionRoot>().ReturnToStart(); var results=new List<object>();
        foreach(string name in new[]{"Settings","Archive"})
        {
            Button open=Ref<Button>(data,"open"+name); var view=new SerializedObject(Ref<Component>(data,name=="Settings"?"settingsView":"archiveView"));
            Click(open); await Task.Delay(100); Exit(open);
            bool top=Ref<Button>(view,"topClose").gameObject.activeInHierarchy, bottom=Ref<Button>(view,"bottomClose").gameObject.activeInHierarchy;
            Click(Ref<Button>(view,"topClose")); await Task.Delay(150);
            results.Add(new{name,activeReturnControls=(top?1:0)+(bottom?1:0),sprite=((Image)open.targetGraphic).overrideSprite.name,selected=EventSystem.current.currentSelectedGameObject==open.gameObject});
        }
        return results;
    }
    public static async Task<object> Review()
    {
        var ui=Object.FindAnyObjectByType<DefenseInterface>(); var data=new SerializedObject(ui);
        var session=Object.FindAnyObjectByType<GameSessionRoot>(); session.ReturnToStart(); var checks=new List<string>();
        foreach(string name in new[]{"Settings","Archive"})
        {
            Button open=Ref<Button>(data,"open"+name); var view=new SerializedObject(Ref<Component>(data,name=="Settings"?"settingsView":"archiveView"));
            for(int i=0;i<3;i++)
            {
                Click(open); await Task.Delay(100); Exit(open);
                Button close=Ref<Button>(view,"topClose"), hidden=Ref<Button>(view,"bottomClose");
                Check(close.gameObject.activeInHierarchy && !hidden.gameObject.activeSelf,name+" only one return",checks);
                Check(close.navigation.selectOnUp!=hidden && close.navigation.selectOnDown!=hidden,"hidden return excluded from navigation",null);
                Click(close); await Task.Delay(160); Canvas.ForceUpdateCanvases();
                Check(!Ref<GameObject>(data,name=="Settings"?"settingsPage":"archivePage").activeSelf && session.AtStartPage,"returns home",null);
                Check(((Image)open.targetGraphic).overrideSprite==((Image)open.targetGraphic).sprite,name+" idle sprite after return",checks);
                Check(EventSystem.current.currentSelectedGameObject==open.gameObject,"keyboard focus restored",null);
                Color idle=open.GetComponentInChildren<Text>().color;
                ExecuteEvents.Execute(open.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerEnterHandler);
                Check(((Image)open.targetGraphic).overrideSprite==open.spriteState.pressedSprite,"hover feedback still works",null);
                Exit(open); Check(((Image)open.targetGraphic).overrideSprite==((Image)open.targetGraphic).sprite && open.GetComponentInChildren<Text>().color==idle,"hover exit restores sprite and text",null);
                ExecuteEvents.Execute(open.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
                await Task.Delay(160); Exit(open); Click(close); await Task.Delay(160);
                Check(((Image)open.targetGraphic).overrideSprite==((Image)open.targetGraphic).sprite,"keyboard submit and return reset",null);
            }
        }
        Ref<Button>(data,"startGame").onClick.Invoke(); ui.TogglePause(); Ref<Button>(data,"pauseSettings").onClick.Invoke(); await Task.Delay(120);
        var settings=new SerializedObject(Ref<Component>(data,"settingsView")); Click(Ref<Button>(settings,"topClose"));
        Check(session.Paused && Ref<GameObject>(data,"pausePage").activeSelf,"settings still returns to pause",checks);
        session.ReturnToStart(); EventSystem.current.SetSelectedGameObject(null);
        return new {passed=true,checks};
    }
    private static void Click(Button button)
    {
        var rect=(RectTransform)button.transform; var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center))};
        var hits=new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer,hits);
        if(hits.Count==0 || !(hits[0].gameObject==button.gameObject || hits[0].gameObject.transform.IsChildOf(button.transform))) throw new InvalidOperationException("Cannot click "+button.name);
        EventSystem.current.SetSelectedGameObject(button.gameObject);
        ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerEnterHandler);
        ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
    }
    private static void Exit(Button button) => ExecuteEvents.Execute(button.gameObject,new PointerEventData(EventSystem.current){position=Vector2.zero},ExecuteEvents.pointerExitHandler);
    private static T Ref<T>(SerializedObject data,string field) where T:Object => (T)data.FindProperty(field).objectReferenceValue;
    private static void Check(bool value,string name,List<string> checks) {if(!value) throw new InvalidOperationException(name); checks?.Add(name);}
}
