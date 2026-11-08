using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
public static class TransferSpawnScenery {
 const string Work="C:/Users/18125/Documents/Codex/2026-11-07/new-chat-3/work/";
 const string RootName="ZQY\u51fa\u751f\u70b9\u666f\u89c2";
 static readonly Vector3 Offset=new Vector3(-0.4f,0.17f,-8.3f);
 static string Hash(string p){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(p)));}
 static string PathOf(Transform t)=>AnimationUtility.CalculateTransformPath(t,null);
 static IEnumerable<Transform> All(Scene s)=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true));
 static string Protected(Scene s){
  return Newtonsoft.Json.JsonConvert.SerializeObject(All(s).Where(t=>t.GetComponent<Camera>()||t.GetComponent<Terrain>()||t.name=="PixelCameraSystem").OrderBy(PathOf).Select(t=>new{path=PathOf(t),transform=EditorJsonUtility.ToJson(t),components=t.GetComponents<Component>().Where(c=>c is Camera||c is Terrain||c is TerrainCollider).Select(c=>EditorJsonUtility.ToJson(c)).ToArray()}).ToArray());
 }
 static Bounds BoundsOf(GameObject g){var rs=g.GetComponentsInChildren<Renderer>(true).Where(r=>!(r is ParticleSystemRenderer)&&r.gameObject.activeSelf).ToArray();if(rs.Length==0)return new Bounds(g.transform.position,Vector3.zero);var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;}
 static GameObject Group(string name,Transform parent){var g=new GameObject(name);g.transform.SetParent(parent,false);return g;}
 static GameObject Clone(GameObject src,Transform parent){if(src.GetComponentsInChildren<Camera>(true).Any()||src.GetComponentsInChildren<Terrain>(true).Any())throw new Exception("Protected component in selection: "+src.name);var g=UnityEngine.Object.Instantiate(src,parent);g.name=src.name;g.transform.SetPositionAndRotation(src.transform.position+Offset,src.transform.rotation);g.transform.localScale=src.transform.lossyScale;g.SetActive(true);return g;}
 static float Ground(Terrain t,Vector3 p)=>t.SampleHeight(p)+t.transform.position.y;
 static void Plant(GameObject g,Terrain terrain,Vector3 center,float embed=0.04f){var b=BoundsOf(g);g.transform.position+=new Vector3(center.x-b.center.x,Ground(terrain,center)-b.min.y-embed,center.z-b.center.z);}
 public static string Build(){
  if(EditorApplication.isPlaying)throw new Exception("Must edit outside Play Mode.");var target=SceneManager.GetActiveScene();if(target.name!="demo2.0")throw new Exception("Target is not demo2.0");
  if(All(target).Any(t=>t.name==RootName))throw new Exception("Imported scenery already exists.");
  var sourcePath=AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath).Single(p=>p.EndsWith("/ZQY 1.unity"));
  var sourceHash=Hash(sourcePath);var protectedBefore=Protected(target);var terrain=All(target).Select(t=>t.GetComponent<Terrain>()).First(t=>t);var terrainPaths=new[]{AssetDatabase.GetAssetPath(terrain.terrainData),"Assets/\u573a\u666f\u7684\u5730\u5f62\u6570\u636e.asset"}.Distinct().ToArray();var terrainHashes=terrainPaths.ToDictionary(p=>p,Hash);
  File.Copy(target.path,Work+"demo2-before-transfer.unity",true);File.WriteAllText(Work+"protected-cameras-terrain-before.json",protectedBefore);
  var source=EditorSceneManager.OpenPreviewScene(sourcePath);
  Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Transfer ZQY spawn scenery to demo2.0");
  var disabled=new List<string>();var cloned=new List<string>();
  try{
   var lookup=All(source).GroupBy(PathOf).ToDictionary(g=>g.Key,g=>g.Select(t=>t.gameObject).ToArray());
   Func<string,GameObject> find=p=>lookup.TryGetValue(p,out var g)&&g.Length==1?g[0]:throw new Exception("Missing or ambiguous source object: "+p);
   var root=new GameObject(RootName);Undo.RegisterCreatedObjectUndo(root,"Add spawn scenery");
   var architecture=Group("\u4ead\u5b50\u4e0e\u70db\u53f0",root.transform);var rocks=Group("\u77f3\u666f",root.transform);var trees=Group("\u6797\u6728",root.transform);var plants=Group("\u8349\u4e1b\u4e0e\u704c\u6728",root.transform);var localLights=Group("\u5c40\u90e8\u706f\u5149",root.transform);
   Func<string,Transform,GameObject> copy=(p,parent)=>{var g=Clone(find(p),parent);cloned.Add(p);return g;};
   var oldTreeRoot=target.GetRootGameObjects().Single(g=>g.name=="\u6811");
   foreach(var name in new[]{"SM_Tree_012_002 (1)","SM_Tree_012_002 (2)","SM_Tree_012_002 (3)","SM_Tree_012_002 (47)","SM_Tree_012_002 (60)","SM_Tree_012_002 (61)","SM_Tree_012_002 (62)"}){
    var t=oldTreeRoot.transform.Find(name);if(t){disabled.Add(PathOf(t));Undo.RecordObject(t.gameObject,"Replace obstructing tree");t.gameObject.SetActive(false);}
   }
   var pavilion=copy("wuti/SM_Roof_05 (1)",architecture.transform);
   foreach(var lod in pavilion.GetComponentsInChildren<LODGroup>(true))lod.enabled=false;
   foreach(var t in pavilion.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("SM_Roof_05_LOD")))t.gameObject.SetActive(t.name.EndsWith("LOD0"));
   foreach(var p in new[]{"floor/SM_road_preset_02 (2)","floor/SM_road_preset_01 (1)","floor/SM_treeslot (1)","floor/SM_Stairs_02"})copy(p,architecture.transform);
   foreach(var p in lookup.Keys.Where(p=>p.StartsWith("wuti/") && p.Count(c=>c=='/')==1 && p.Contains("\u8721\u70db")).Where(p=>find(p).transform.position.x<67 && find(p).transform.position.z<109))copy(p,architecture.transform);
   foreach(var p in new[]{"wuti/SM_Cliff_003","wuti/SM_Cliff_001 (50)","wuti/SM_Cliff_003 (1)","wuti/SM_Rock_016 (9)","wuti/SM_Rock_016 (10)","wuti/SM_Rock_016 (1)","wuti/SM_Rock_016 (3)","wuti/SM_Rock_016 (2)","wuti/SM_Rock_007","wuti/SM_Rock_011","wuti/SM_Rock_010 (6)","floor/Rocks_Round_m_03 (26)","floor/Rocks_Round_m_03 (28)","floor/Rocks_Round_m_03 (27)"})copy(p,rocks.transform);
   foreach(var p in new[]{"wuti/\u5c0f\u68111 (8)","wuti/\u5c0f\u68112 (2)","wuti/\u5c0f\u68112 (3)","wuti/SM_Tree_005_001","wuti/SM_Tree_013_006","wuti/SM_Tree_010_001 (1)","wuti/SM_Tree_010_001 (5)"})copy(p,trees.transform);
   // Frame the far edge with a smaller tree, rather than another large foreground canopy.
   var backTree=copy("wuti/SM_Tree_002_002 (1)",trees.transform);backTree.transform.localScale*=0.65f;Plant(backTree,terrain,new Vector3(63.6f,0,83.2f),0.08f);
   var flowerGroup=Group("\u82b1\u6811\u4e0e\u6302\u706f",trees.transform);var flowerSource=find("wuti/\u5de8\u5927\u7684\u6811\u6539\u826f\u7248");flowerGroup.transform.position=flowerSource.transform.position+Offset;
   var flower=copy("wuti/\u5de8\u5927\u7684\u6811\u6539\u826f\u7248",flowerGroup.transform);
   foreach(var p in new[]{"floor/SM_lantern_paper_04 (4)","floor/SM_lantern_paper_04 (5)","floor/SM_lantern_paper_04 (7)","floor/SM_lantern_paper_04 (8)"})copy(p,flowerGroup.transform);
   flowerGroup.transform.localScale=Vector3.one*0.78f;var fb=BoundsOf(flower);var flowerCenter=new Vector3(65.2f,0,96.6f);flowerGroup.transform.position+=new Vector3(flowerCenter.x-fb.center.x,Ground(terrain,flowerCenter)-fb.min.y-0.07f,flowerCenter.z-fb.center.z);
   int lamp=0;foreach(var t in flowerGroup.transform.Cast<Transform>().Where(t=>t!=flower.transform)){
    var glow=Group("LanternGlow_"+(++lamp),localLights.transform);glow.transform.position=BoundsOf(t.gameObject).center;var l=glow.AddComponent<Light>();l.type=LightType.Point;l.color=new Color(1f,0.46f,0.22f);l.intensity=0.75f;l.range=2.5f;l.shadows=LightShadows.None;
   }
   foreach(var p in new[]{"floor/Point Light","floor/Point Light (2)","floor/Point Light (4)","floor/Point Light (5)"}){
    var original=lookup[p].OrderBy(g=>Vector3.Distance(g.transform.position,new Vector3(57.5f,8.5f,102f))).First();if(Vector3.Distance(original.transform.position,new Vector3(57.5f,8.5f,102f))>8)throw new Exception("Light outside source patch");
    var g=Clone(original,localLights.transform);cloned.Add(p);var l=g.GetComponent<Light>();l.range=Mathf.Min(l.range,5.5f);l.shadows=LightShadows.None;
   }
   // Copy the source's grass/shrub materials; scatter separate mesh objects, leaving terrain data alone.
   var grassSources=new[]{find("floor/SM_Grass_004 (13)"),find("floor/SM_Grass_004 (14)"),find("floor/SM_Grass_008 (16)")};
   var shrubSources=new[]{find("floor/SM_Bush_002_002 (17)"),find("floor/SM_Bush_006_001 (5)"),find("floor/SM_Bush_005_001 (3)")};
   var random=new System.Random(20261107);int grassCount=0;
   for(int i=0;i<52;i++){
    float a=(float)random.NextDouble()*Mathf.PI*2;float r=6f+(float)random.NextDouble()*3.5f;var pos=new Vector3(57f+Mathf.Cos(a)*r,0,93.4f+Mathf.Sin(a)*r);
    if(pos.z>97f&&pos.x>51f&&pos.x<60f)continue;
    var g=Clone(grassSources[i%3],plants.transform);g.name="Grass_"+(++grassCount).ToString("D2");g.transform.localScale*=1.6f+(float)random.NextDouble()*1.2f;g.transform.rotation=Quaternion.Euler(0,(float)random.NextDouble()*360,0);Plant(g,terrain,pos,0.02f);
   }
   var shrubPositions=new[]{new Vector3(49,0,89),new Vector3(50.4f,0,87.4f),new Vector3(53.2f,0,85.5f),new Vector3(57.5f,0,85),new Vector3(63.2f,0,87.3f),new Vector3(65f,0,90),new Vector3(64.8f,0,96),new Vector3(48.2f,0,94.4f),new Vector3(62,0,99)};
   for(int i=0;i<shrubPositions.Length;i++){var g=Clone(shrubSources[i%3],plants.transform);g.name="Shrub_"+(i+1).ToString("D2");g.transform.localScale*=0.65f+(float)random.NextDouble()*0.35f;g.transform.rotation=Quaternion.Euler(0,i*137.5f,0);Plant(g,terrain,shrubPositions[i],0.05f);}
   // Keep this copy of the firefly volume in the gazebo; the source keeps its user-adjusted scale.
   foreach(var ps in pavilion.GetComponentsInChildren<ParticleSystem>(true)){
    if(ps.name.Contains("\u8424\u706b\u866b")){ps.transform.localScale*=1f/ps.transform.lossyScale.x;ps.transform.position=new Vector3(57.2f,9.8f,93.5f);var main=ps.main;main.scalingMode=ParticleSystemScalingMode.Hierarchy;}
   }
   foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true)){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);ps.Simulate(12f,true,true);ps.Play(true);}
   if(Protected(target)!=protectedBefore)throw new Exception("Protected camera/terrain object changed");
   if(Hash(sourcePath)!=sourceHash||terrainHashes.Any(k=>Hash(k.Key)!=k.Value))throw new Exception("Protected asset changed");
   Undo.CollapseUndoOperations(undo);EditorSceneManager.MarkSceneDirty(target);EditorSceneManager.SaveScene(target);
   var report=Newtonsoft.Json.JsonConvert.SerializeObject(new{source=sourcePath,target=target.path,sourceSceneHash=sourceHash,sourceSceneUnchanged=true,camerasAndTerrainUnchanged=true,terrainHashes,disabledOriginalTrees=disabled,copiedSourcePaths=cloned,grassClumps=grassCount,shrubs=shrubPositions.Length,particleSystems=root.GetComponentsInChildren<ParticleSystem>(true).Length,lights=root.GetComponentsInChildren<Light>(true).Length},Newtonsoft.Json.Formatting.Indented);
   File.WriteAllText(Work+"scenery-transfer-report.json",report);SceneView.RepaintAll();return Refine();
  }catch{Undo.RevertAllDownToGroup(undo);throw;}finally{EditorSceneManager.ClosePreviewScene(source);}
 }
 public static string Refine(){
  var s=SceneManager.GetActiveScene();var root=s.GetRootGameObjects().Single(g=>g.name==RootName);var terrain=All(s).Select(t=>t.GetComponent<Terrain>()).First(t=>t);
  Undo.IncrementCurrentGroup();int u=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Refine spawn scenery composition");Undo.RecordObject(root.transform,"Fit scenery to original frame");root.transform.position=new Vector3(57f,5.12f,93.5f)*(1-0.67f)+new Vector3(-3.5f,-0.55f,0);root.transform.localScale=Vector3.one*0.67f;
  var originals=s.GetRootGameObjects().Single(g=>g.name=="\u6811");foreach(var name in new[]{"SM_Tree_012_002 (10)","SM_Tree_012_002 (12)","SM_Tree_012_002 (13)","SM_Tree_012_002 (14)"}){var t=originals.transform.Find(name);if(t){Undo.RecordObject(t.gameObject,"Clear marked foreground canopy");t.gameObject.SetActive(false);}}
  var small=root.GetComponentsInChildren<Transform>(true).First(t=>t.name=="\u5c0f\u68112 (3)");Undo.RecordObject(small,"Frame pavilion with side tree");small.position+=new Vector3(1f,0,-2.5f);
  var covers=root.transform.Find("\u8349\u4e1b\u4e0e\u704c\u6728");foreach(Transform t in covers){Undo.RecordObject(t,"Seat ground cover");Plant(t.gameObject,terrain,t.position,0.025f);}
  var treeRoot=root.transform.Find("\u6797\u6728");foreach(Transform t in treeRoot){
   Undo.RecordObject(t,"Seat scenery trees");if(t.name=="\u82b1\u6811\u4e0e\u6302\u706f"){var flower=t.Find("\u5de8\u5927\u7684\u6811\u6539\u826f\u7248");var b=BoundsOf(flower.gameObject);t.position+=Vector3.up*(Ground(terrain,b.center)-b.min.y-0.08f);}else Plant(t.gameObject,terrain,t.position,0.07f);
  }
  var fill=Group("PavilionWarmFill",root.transform.Find("\u5c40\u90e8\u706f\u5149"));Undo.RegisterCreatedObjectUndo(fill,"Add pavilion warm fill");fill.transform.position=new Vector3(49.5f,9.0f,94.0f);var light=fill.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1f,0.65f,0.38f);light.range=6.2f;light.intensity=0.75f;light.shadows=LightShadows.None;
  foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true)){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);ps.Simulate(12f,true,true);ps.Play();}Undo.CollapseUndoOperations(u);SceneView.RepaintAll();var verification=Verify();EditorSceneManager.SaveScene(s);return verification;
 }
 public static string Verify(){var s=SceneManager.GetActiveScene();var root=s.GetRootGameObjects().Single(g=>g.name==RootName);var r=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Work+"scenery-transfer-report.json"));bool source=Hash((string)r["source"])==(string)r["sourceSceneHash"];bool cameras=Protected(s)==File.ReadAllText(Work+"protected-cameras-terrain-before.json");var terrain=(Newtonsoft.Json.Linq.JObject)r["terrainHashes"];bool terrainOk=terrain.Properties().All(p=>Hash(p.Name)==(string)p.Value);if(!source||!cameras||!terrainOk)throw new Exception("Protected scene/camera/terrain verification failed");return Newtonsoft.Json.JsonConvert.SerializeObject(new{sourceUnchanged=source,camerasUnchanged=cameras,terrainUnchanged=terrainOk,root=root.name,objects=root.GetComponentsInChildren<Transform>(true).Length,particles=root.GetComponentsInChildren<ParticleSystem>(true).Length,lights=root.GetComponentsInChildren<Light>(true).Length});}
}
