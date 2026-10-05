using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Quota.EditorTools
{
    public static class WebGlExport
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        public static void Build()
        {
            HardenWebGlPlayer();
            Directory.CreateDirectory("Assets/Scenes");
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                cameraObject.AddComponent<AudioListener>();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            var dest = System.Environment.GetEnvironmentVariable("QUOTA_WEBGL_OUT");
            if (string.IsNullOrEmpty(dest)) dest = "Builds/WebGL";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = dest,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("WebGL export failed: " + report.summary.result);
                EditorApplication.Exit(1);
            }
        }

        static void HardenWebGlPlayer()
        {
            // Keep Safari / iOS from compiling URP post-process shaders and from
            // serving a stale IndexedDB copy of an older build.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = false;
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            PlayerSettings.WebGL.powerPreference = WebGLPowerPreference.Default;
            PlayerSettings.WebGL.initialMemorySize = 128;
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, Il2CppCodeGeneration.OptimizeSize);
            Debug.Log("WebGL player hardened: gzip fallback on, IndexedDB cache off, hashed names on.");
        }
    }
}
