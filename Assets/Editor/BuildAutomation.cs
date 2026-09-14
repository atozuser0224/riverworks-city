using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Riverworks.Editor
{
    public static class BuildAutomation
    {
        public const string ScenePath="Assets/Scenes/Riverworks.unity";
        [MenuItem("Riverworks/Prepare project and scene")]
        public static void Prepare()
        {
            PlayerSettings.companyName="Riverworks Studio";
            PlayerSettings.productName="Riverworks";
            PlayerSettings.bundleVersion="0.9.0";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone,"studio.riverworks.city");
            PlayerSettings.defaultScreenWidth=1600; PlayerSettings.defaultScreenHeight=900;
            PlayerSettings.defaultIsNativeResolution=false;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.resizableWindow=true; PlayerSettings.runInBackground=true;
            // The client restricts plain HTTP to loopback/private LAN gateways; cloud calls stay server-side HTTPS.
            PlayerSettings.insecureHttpOption=InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.SplashScreen.show=false;
            PlayerSettings.colorSpace=ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
            // The runtime uses Unity's legacy keyboard/mouse API and StandaloneInputModule.
            var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input=settings.FindProperty("activeInputHandler"); if(input!=null) {input.intValue=0; settings.ApplyModifiedPropertiesWithoutUndo();}
            IncludeShaders();
            Directory.CreateDirectory("Assets/Scenes");
            if(!File.Exists(ScenePath))
            {
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                new GameObject("RIVERWORKS — Play to build your city").AddComponent<GameController>();
                EditorSceneManager.SaveScene(scene,ScenePath);
            }
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            UiTextureImportSettings.ApplyAll();
            ResidentAssetBuild.Prepare();
        }
        static void IncludeShaders()
        {
            var asset=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if(asset.Length==0) return;
            var serialized=new SerializedObject(asset[0]);var list=serialized.FindProperty("m_AlwaysIncludedShaders");
            foreach(string name in new[]{"Standard","Unlit/Color","UI/Default","Sprites/Default"})
            {
                Shader shader=Shader.Find(name); if(shader==null) throw new Exception("Required shader not found: "+name);
                bool found=false; for(int i=0;i<list.arraySize;i++) if(list.GetArrayElementAtIndex(i).objectReferenceValue==shader) found=true;
                if(!found) {int index=list.arraySize;list.InsertArrayElementAtIndex(index);list.GetArrayElementAtIndex(index).objectReferenceValue=shader;}
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        [MenuItem("Riverworks/Build Windows game")]
        public static void Build()
        {
            Prepare();
            VerifySaveRoundTrip();
            SaveValidationChecks.Run();
            string[] args=Environment.GetCommandLineArgs();int outputIndex=Array.IndexOf(args,"-riverworks-build-output");
            string directory=Path.GetFullPath(outputIndex>=0&&outputIndex+1<args.Length?args[outputIndex+1]:"Builds/Windows");
            string builds=Path.GetFullPath("Builds").TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!directory.StartsWith(builds,StringComparison.OrdinalIgnoreCase))throw new Exception("Windows build output must be inside this project's Builds directory.");
            Directory.CreateDirectory(directory);
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=Path.Combine(directory,"Riverworks.exe"),target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            if(report.summary.result!=BuildResult.Succeeded) throw new Exception("Windows build failed: "+report.summary.result+" errors="+report.summary.totalErrors);
            Directory.CreateDirectory("Artifacts");
            File.WriteAllText("Artifacts/build-result.txt","SUCCESS\nUnity "+Application.unityVersion+"\nBytes "+report.summary.totalSize+"\nWarnings "+report.summary.totalWarnings+"\nErrors "+report.summary.totalErrors+"\n");
            Debug.Log("RIVERWORKS_BUILD_SUCCESS "+report.summary.totalSize+" bytes");
        }
        [MenuItem("Riverworks/Verify save format")]
        public static void VerifySaveRoundTrip()
        {
            Directory.CreateDirectory("Artifacts"); string path=Path.GetFullPath("Artifacts/editor-save-test.json");
            var state=GameState.CreateNew();var sim=new Simulation(state);sim.Tick();
            if(!SaveStore.TrySave(path,state,out var error)) throw new Exception("Save failed: "+error);
            if(!SaveStore.TryLoad(path,out var loaded,out error)) throw new Exception("Load failed: "+error);
            if(loaded.Day!=state.Day||loaded.Cells.Count!=441||loaded.Coins!=state.Coins||loaded.Stock[(int)Resource.Grain]!=state.Stock[(int)Resource.Grain]) throw new Exception("Save round-trip did not preserve state");
            File.WriteAllText(path,"{ corrupt JSON");
            if(SaveStore.TryLoad(path,out _,out _)) throw new Exception("Corrupt save accepted");
            if(!SaveStore.TrySave(path,state,out error)) throw new Exception("Atomic replacement failed: "+error);
            state.Stock[(int)Resource.Bread]=float.NaN;
            if(SaveStore.TrySave(path,state,out _)) throw new Exception("NaN save accepted");
            File.WriteAllText("Artifacts/save-tests.txt","PASS: JsonUtility round-trip, atomic replace and backup, corrupt rejection, non-finite rejection\n");
            Debug.Log("RIVERWORKS_SAVE_TESTS_PASS");
        }
    }
}
