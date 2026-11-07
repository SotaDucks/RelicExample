using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Collections;
public static class CandleGeometry {
 const string Folder="Assets/SceneEffects/CandleFlames";
 public static string Build() {
  if(EditorApplication.isPlaying)throw new Exception("Stop Play Mode before editing scene assets.");
  var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
  var filters=UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(m=>m.name.Contains("\u8721\u70db") && m.transform.position.x>48 && m.transform.position.x<67 && m.transform.position.z>94 && m.transform.position.z<111).ToArray();
  if(filters.Length!=12)throw new Exception("Unexpected candle group count: "+filters.Length);
  if(filters.Any(f=>f.transform.Find("CandleFlameFX")))throw new Exception("Flames already replaced.");
  System.IO.Directory.CreateDirectory("artifacts/candle-flames");
  System.IO.File.Copy(scene.path,"artifacts/candle-flames/ZQY1-before.unity",true);
  var shader=AssetDatabase.LoadAssetAtPath<Shader>(Folder+"/CandleFlame.shader");
  if(!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader))throw new Exception("Flame shader did not compile.");
  var mat=new Material(shader){name="Small Candle Flame"};
  mat.SetFloat("_Intensity",2.2f);mat.SetFloat("_Flicker",0.6f);
  AssetDatabase.CreateAsset(mat,Folder+"/SmallCandleFlame.mat");
  var originalSelection=Selection.objects;
  Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Replace pavilion candle flames");
  int count=0;var report=new List<object>();
  foreach(var group in filters.GroupBy(f=>f.sharedMesh)) {
   var source=group.Key;var first=group.First();var mats=first.GetComponent<Renderer>().sharedMaterials;
   int wax=Array.FindIndex(mats,m=>m.color.r>m.color.g*3f);
   if(wax<0 || mats.Length!=2)throw new Exception("Unexpected wax material.");
   int flame=1-wax;
   using(var data=MeshUtility.AcquireReadOnlyMeshData(source)) {
    var d=data[0];using(var nv=new NativeArray<Vector3>(d.vertexCount,Allocator.Temp))using(var nn=new NativeArray<Vector3>(d.vertexCount,Allocator.Temp)) {
     d.GetVertices(nv);d.GetNormals(nn);var vertices=nv.ToArray();var normals=nn.ToArray();
     var waxIndices=Indices(d,wax);var used=waxIndices.Distinct().OrderBy(i=>i).ToArray();var map=new int[vertices.Length];for(int i=0;i<used.Length;i++)map[used[i]]=i;
     string kind=group.Count()==4?"Cluster":group.Count()==5?"Single":"Triple";
     var body=new Mesh{name=kind+" Candle - Wax Only"};
     body.vertices=used.Select(i=>vertices[i]).ToArray();body.normals=used.Select(i=>normals[i]).ToArray();
     if(d.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))using(var uv=new NativeArray<Vector2>(d.vertexCount,Allocator.Temp)){d.GetUVs(0,uv);var a=uv.ToArray();body.uv=used.Select(i=>a[i]).ToArray();}
     body.triangles=waxIndices.Select(i=>map[i]).ToArray();body.RecalculateBounds();
     AssetDatabase.CreateAsset(body,Folder+"/"+kind+"WaxOnly.asset");
     var parts=Components(vertices,Indices(d,flame));
     if(parts.Count!=(kind=="Cluster"?5:kind=="Triple"?3:1))throw new Exception("Unexpected flame island count.");
     foreach(var f in group) {
      var rend=f.GetComponent<MeshRenderer>();Undo.RecordObject(f,"Remove solid flame mesh");Undo.RecordObject(rend,"Keep wax material");
      f.sharedMesh=body;rend.sharedMaterials=new[]{mats[wax]};PrefabUtility.RecordPrefabInstancePropertyModifications(f);PrefabUtility.RecordPrefabInstancePropertyModifications(rend);
      var root=new GameObject("CandleFlameFX");Undo.RegisterCreatedObjectUndo(root,"Create small candle flames");root.transform.SetParent(f.transform,false);
      foreach(var p in parts) {
       var world=p.Select(i=>f.transform.TransformPoint(vertices[i])).ToArray();var min=world.Aggregate(Vector3.Min);var max=world.Aggregate(Vector3.Max);
       float h=Mathf.Clamp((max.y-min.y)*0.85f,0.16f,0.24f);float width=h*0.78f;
       var wick=new Vector3((min.x+max.x)*0.5f,min.y+0.005f,(min.z+max.z)*0.5f);
       var go=new GameObject("SmallFlame_"+(++count).ToString("D2"));go.transform.SetParent(root.transform,false);go.transform.position=wick+Vector3.up*h*0.5f;go.transform.rotation=Quaternion.identity;
       var ps=go.AddComponent<ParticleSystem>();Configure(ps,mat,width,h,count);
       ps.Simulate(1.5f,true,true);ps.Play(true);
       report.Add(new {candle=f.name,flame=go.name,wick=new[]{wick.x,wick.y,wick.z},height=h});
      }
     }
    }
   }
  }
  var sample=new GameObject("SmallCandleFlame");var sps=sample.AddComponent<ParticleSystem>();Configure(sps,mat,0.17f,0.22f,1);
  PrefabUtility.SaveAsPrefabAsset(sample,Folder+"/SmallCandleFlame.prefab");UnityEngine.Object.DestroyImmediate(sample);
  Undo.CollapseUndoOperations(undo);EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();
  System.IO.File.WriteAllText("artifacts/candle-flames/replacement-report.json",Newtonsoft.Json.JsonConvert.SerializeObject(new {scene=scene.path,groups=filters.Length,flames=count,entries=report},Newtonsoft.Json.Formatting.Indented));
  Selection.objects=originalSelection;SceneView.RepaintAll();
  return Newtonsoft.Json.JsonConvert.SerializeObject(new {groups=filters.Length,flames=count,assets=Folder,scene=scene.path});
 }
 static void Configure(ParticleSystem ps,Material mat,float width,float height,int seed) {
  ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);ps.useAutoRandomSeed=false;ps.randomSeed=(uint)(1729+seed*41);
  var main=ps.main;main.duration=2f;main.loop=true;main.prewarm=true;main.startLifetime=new ParticleSystem.MinMaxCurve(0.55f,0.72f);main.startSpeed=0f;main.startSize3D=true;main.startSizeX=width;main.startSizeY=height;main.startSizeZ=width;main.startColor=new Color(1,1,1,0.60f);main.gravityModifier=0;main.maxParticles=12;main.simulationSpace=ParticleSystemSimulationSpace.World;main.scalingMode=ParticleSystemScalingMode.Shape;main.playOnAwake=true;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
  var em=ps.emission;em.enabled=true;em.rateOverTime=7f;
  var shape=ps.shape;shape.enabled=false;
  var col=ps.colorOverLifetime;col.enabled=true;var grad=new Gradient();grad.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,0.12f),new GradientAlphaKey(1,0.72f),new GradientAlphaKey(0,1)});col.color=grad;
  var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,0.93f),new Keyframe(0.35f,1.03f),new Keyframe(1,0.94f)));
  var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.VerticalBillboard;renderer.sharedMaterial=mat;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.lightProbeUsage=UnityEngine.Rendering.LightProbeUsage.Off;renderer.reflectionProbeUsage=UnityEngine.Rendering.ReflectionProbeUsage.Off;renderer.sortMode=ParticleSystemSortMode.Distance;renderer.minParticleSize=0;renderer.maxParticleSize=0.5f;
 }
 public static string Inspect() {
  var filters=UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(m=>m.name.Contains("\u8721\u70db") && m.transform.position.x<70).ToArray();
  var rows=new List<object>();
  foreach(var g in filters.GroupBy(f=>f.sharedMesh)) {
   var mesh=g.Key; var f=g.First(); var materials=f.GetComponent<Renderer>().sharedMaterials;
   using(var md=MeshUtility.AcquireReadOnlyMeshData(mesh)) {
    var d=md[0]; using(var verts=new NativeArray<Vector3>(d.vertexCount,Allocator.Temp)) {
     d.GetVertices(verts); var vv=verts.ToArray(); var subs=new List<object>();
     for(int s=0;s<d.subMeshCount;s++) {
      var ids=Indices(d,s); var components=Components(vv,ids);
      subs.Add(new {slot=s,mat=materials[s].name,color=materials[s].color.ToString(),triangles=ids.Length/3,parts=components.Select(p=>new {vertices=p.Length,localMin=p.Select(i=>vv[i]).Aggregate(Vector3.Min).ToString("F6"),localMax=p.Select(i=>vv[i]).Aggregate(Vector3.Max).ToString("F6"),worldMin=p.Select(i=>f.transform.TransformPoint(vv[i])).Aggregate(Vector3.Min).ToString("F3"),worldMax=p.Select(i=>f.transform.TransformPoint(vv[i])).Aggregate(Vector3.Max).ToString("F3")}).ToArray()});
     }
     rows.Add(new {mesh=mesh.name,source=AssetDatabase.GetAssetPath(mesh),instances=g.Select(m=>m.name).ToArray(),subs});
    }
   }
  }
  return Newtonsoft.Json.JsonConvert.SerializeObject(rows);
 }
 public static int[] Indices(Mesh.MeshData d,int s) {
  var sd=d.GetSubMesh(s); var ids=new int[sd.indexCount];
  if(d.indexFormat==UnityEngine.Rendering.IndexFormat.UInt16) {var raw=d.GetIndexData<ushort>();for(int i=0;i<ids.Length;i++)ids[i]=raw[sd.indexStart+i]+sd.baseVertex;}
  else {var raw=d.GetIndexData<uint>();for(int i=0;i<ids.Length;i++)ids[i]=(int)raw[sd.indexStart+i]+sd.baseVertex;}
  return ids;
 }
 public static List<int[]> Components(Vector3[] v,int[] ids) {
  var parent=Enumerable.Range(0,v.Length).ToArray();
  Func<int,int> root=null;root=i=>parent[i]==i?i:(parent[i]=root(parent[i]));
  Action<int,int> join=(a,b)=>{a=root(a);b=root(b);if(a!=b)parent[b]=a;};
  var weld=new Dictionary<string,int>();
  foreach(int i in ids.Distinct()) {var p=v[i];string key=$"{Mathf.RoundToInt(p.x*1000000)},{Mathf.RoundToInt(p.y*1000000)},{Mathf.RoundToInt(p.z*1000000)}";if(weld.TryGetValue(key,out int j))join(i,j);else weld[key]=i;}
  for(int i=0;i<ids.Length;i+=3){join(ids[i],ids[i+1]);join(ids[i],ids[i+2]);}
  return ids.Distinct().GroupBy(root).Select(g=>g.ToArray()).OrderByDescending(a=>a.Length).ToList();
 }
}
