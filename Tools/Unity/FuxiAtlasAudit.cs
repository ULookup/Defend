using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
public static class FuxiAtlasAudit
{
    public static object RefreshMeshBuffers()
    {
        const string root="Assets/Defend/Content/Art/FuxiTurret/";
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(root+"Prefabs/FuxiTurret.prefab");
        foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh=filter.sharedMesh;
            mesh.uv=mesh.uv;
            mesh.UploadMeshData(false);
        }
        return new {refreshed=prefab.GetComponentsInChildren<MeshFilter>(true).Length};
    }
    public static object Inspect()
    {
        const string root="Assets/Defend/Content/Art/FuxiTurret/";
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(root+"Models/FuxiTurret.fbx");
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(root+"Prefabs/FuxiTurret.prefab");
        var report=source.GetComponentsInChildren<MeshFilter>(true).Select(f=>
        {
            var counterpart=prefab.GetComponentsInChildren<MeshFilter>(true).Single(p=>p.name==f.name);
            var a=f.sharedMesh.uv;var b=counterpart.sharedMesh.uv;
            return new { name=f.name,sourceVertices=f.sharedMesh.vertexCount,targetVertices=counterpart.sharedMesh.vertexCount,
                sourceUvCount=a.Length,targetUvCount=b.Length,
                uvEqual=a.SequenceEqual(b),sourceUvPreview=a.Take(8).Select(p=>new[]{p.x,p.y}).ToArray(),
                targetUvPreview=b.Take(8).Select(p=>new[]{p.x,p.y}).ToArray() };
        }).ToArray();
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(root+"Textures/FuxiTurret_BaseColor.png");
        var rt=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var old=RenderTexture.active;
        try
        {
            Graphics.Blit(texture,rt);RenderTexture.active=rt;
            var copy=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false,false);
            copy.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);copy.Apply();
            File.WriteAllBytes("Docs/Art/FuxiTurret/Checks/Unity/ImportedAtlasGpu.png",copy.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(copy);
        }
        finally {RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);}
        File.WriteAllText("Docs/Art/FuxiTurret/Checks/Unity/AtlasMappingAudit.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
        return report;
    }
}
