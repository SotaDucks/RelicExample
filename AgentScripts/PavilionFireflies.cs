using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class PavilionFireflies {
 const string Folder="Assets/SceneEffects/PavilionFireflies";
 const string EmitterName="\u4ead\u5185\u8424\u706b\u866b";
 public static string Build(){
  if(EditorApplication.isPlaying)throw new Exception("Scene edits require Edit Mode.");
  var all=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None);
  if(all.Any(t=>t.name==EmitterName))throw new Exception("Pavilion fireflies already exist.");
  var pavilion=all.Single(t=>t.name=="SM_Roof_05 (1)");
  var source=UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include,FindObjectsSortMode.None).First(p=>p.name.Contains("\u8424\u706b\u866b"));
  var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
  System.IO.Directory.CreateDirectory("artifacts/candle-flames");System.IO.File.Copy(scene.path,"artifacts/candle-flames/ZQY1-before-fireflies.unity",true);
  System.IO.Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
  var template=UnityEngine.Object.Instantiate(source.gameObject);template.name="PavilionFireflies";template.transform.SetParent(null,false);template.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);template.transform.localScale=Vector3.one;
  var ps=template.GetComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);ps.useAutoRandomSeed=false;ps.randomSeed=814237;
  var main=ps.main;main.duration=16;main.loop=true;main.prewarm=true;main.playOnAwake=true;main.maxParticles=30;main.startLifetime=new ParticleSystem.MinMaxCurve(8.5f,12f);main.startSpeed=new ParticleSystem.MinMaxCurve(0.012f,0.035f);main.startSize3D=false;main.startSize=new ParticleSystem.MinMaxCurve(0.15f,0.23f);main.startColor=new ParticleSystem.MinMaxGradient(new Color(0.90f,1.00f,0.38f,0.86f),new Color(0.72f,1.00f,0.30f,0.95f));main.gravityModifier=0;main.simulationSpace=ParticleSystemSimulationSpace.Local;main.scalingMode=ParticleSystemScalingMode.Local;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
  var shape=ps.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=1;shape.radiusThickness=1;shape.scale=new Vector3(3.0f,1.35f,2.9f);shape.position=Vector3.zero;shape.rotation=Vector3.zero;shape.alignToDirection=false;
  var em=ps.emission;em.enabled=true;em.rateOverTime=2.25f;em.rateOverDistance=0;em.SetBursts(new ParticleSystem.Burst[0]);
  var noise=ps.noise;noise.enabled=true;noise.separateAxes=true;noise.strengthX=new ParticleSystem.MinMaxCurve(0.10f,0.16f);noise.strengthY=new ParticleSystem.MinMaxCurve(0.07f,0.10f);noise.strengthZ=new ParticleSystem.MinMaxCurve(0.10f,0.16f);noise.frequency=0.28f;noise.scrollSpeed=0.06f;noise.damping=true;noise.octaveCount=2;noise.quality=ParticleSystemNoiseQuality.High;
  var velocity=ps.velocityOverLifetime;velocity.enabled=false;
  var limit=ps.limitVelocityOverLifetime;limit.enabled=true;limit.separateAxes=false;limit.limit=0.16f;limit.dampen=0.5f;
  var colors=ps.colorOverLifetime;colors.enabled=true;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(0.95f,0.10f),new GradientAlphaKey(0.18f,0.23f),new GradientAlphaKey(0.85f,0.38f),new GradientAlphaKey(0.12f,0.53f),new GradientAlphaKey(1,0.69f),new GradientAlphaKey(0.25f,0.83f),new GradientAlphaKey(0,1)});colors.color=g;
  var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.minParticleSize=0;renderer.maxParticleSize=0.035f;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
  var prefab=PrefabUtility.SaveAsPrefabAsset(template,Folder+"/PavilionFireflies.prefab");UnityEngine.Object.DestroyImmediate(template);
  var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);go.name=EmitterName;go.transform.position=new Vector3(57.6f,9.0f,101.8f);go.transform.rotation=Quaternion.identity;go.transform.localScale=Vector3.one;go.transform.SetParent(pavilion,true);
  Undo.RegisterCreatedObjectUndo(go,"Add pavilion fireflies");PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);PrefabUtility.RecordPrefabInstancePropertyModifications(go);
  ps=go.GetComponent<ParticleSystem>();ps.Simulate(12,true,true);ps.Play();
  AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  SceneView.RepaintAll();return Verify();
 }
 public static string Verify(){
  var ps=UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(p=>p.name==EmitterName);
  ps.Simulate(12,true,true);var before=new ParticleSystem.Particle[ps.main.maxParticles];int n=ps.GetParticles(before);
  ps.Simulate(1.0f,true,false);var after=new ParticleSystem.Particle[ps.main.maxParticles];int n2=ps.GetParticles(after);
  var world=after.Take(n2).Select(p=>ps.transform.TransformPoint(p.position)).ToArray();var min=world.Aggregate(Vector3.Min);var max=world.Aggregate(Vector3.Max);
  int moved=after.Take(n2).Count(p=>before.Take(n).Any(b=>b.randomSeed==p.randomSeed && Vector3.Distance(b.position,p.position)>0.001f));
  if(n2<15||n2>30||moved<10)throw new Exception("Firefly simulation failed.");
  if(min.y<7.2f || max.y>10.9f)throw new Exception("Fireflies outside pavilion height range.");
  var shader=ps.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader;if(ShaderUtil.ShaderHasError(shader))throw new Exception("Firefly shader error");
  ps.Play();var result=Newtonsoft.Json.JsonConvert.SerializeObject(new {fireflies=n2,moving=moved,worldMin=min.ToString("F2"),worldMax=max.ToString("F2"),parent=ps.transform.parent.name,shader=shader.name,scene=ps.gameObject.scene.path},Newtonsoft.Json.Formatting.Indented);
  System.IO.File.WriteAllText("artifacts/candle-flames/firefly-validation.json",result);return result;
 }
}
