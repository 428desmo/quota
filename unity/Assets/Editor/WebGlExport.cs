using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Quota.EditorTools
{
    public static class WebGlExport
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string StreamingRoot = "Assets/StreamingAssets";
        const string ResourceRoot = "Assets/Resources/QuotaWebGenerated";

        public static void Build()
        {
            HardenWebGlPlayer();
            StageWebResources();
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
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/UniversalRenderer.asset");
            var oldPost = renderer != null ? renderer.postProcessData : null;
            var pipelineObject = new SerializedObject(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/UniversalRenderPipeline.asset"));
            var hdr = pipelineObject.FindProperty("m_SupportsHDR");
            var oldHdr = hdr != null && hdr.boolValue;
            try
            {
                if (renderer != null)
                {
                    renderer.postProcessData = null;
                    EditorUtility.SetDirty(renderer);
                }
                if (hdr != null)
                {
                    hdr.boolValue = false;
                    pipelineObject.ApplyModifiedPropertiesWithoutUndo();
                }
                AssetDatabase.SaveAssets();
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
            finally
            {
                if (renderer != null)
                {
                    renderer.postProcessData = oldPost;
                    EditorUtility.SetDirty(renderer);
                }
                if (hdr != null)
                {
                    hdr.boolValue = oldHdr;
                    pipelineObject.ApplyModifiedPropertiesWithoutUndo();
                }
                AssetDatabase.DeleteAsset(ResourceRoot);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.SaveAssets();
            }
        }

        static void StageWebResources()
        {
            if (AssetDatabase.IsValidFolder(ResourceRoot))
                AssetDatabase.DeleteAsset(ResourceRoot);
            Directory.CreateDirectory(ResourceRoot);
            CopyResource("vertical_base.jpg");
            CopyResource("horizontal_base.jpg");
            CopyResource("title1.png");
            CopyResource("title2.png");
            AssetDatabase.DeleteAsset(StreamingRoot + "/play_bgm_01.mp3");
            File.Copy(Path.GetFullPath("../sound/play_bgm_02.mp3"), Path.Combine(StreamingRoot, "play_bgm_02.mp3"), true);
            File.Copy(Path.GetFullPath("../visual/card_back.png"), Path.Combine(StreamingRoot, "card_back.png"), true);
            CopyResource("card_back.png");
            foreach (var name in new[] { "how_to_play_v1.0.txt", "detailed_rule_v1.0.txt" })
            {
                var source = Path.GetFullPath("../" + name);
                if (File.Exists(source)) File.Copy(source, Path.Combine(StreamingRoot, name), true);
                CopyResource(name);
            }
            Directory.CreateDirectory(Path.Combine(ResourceRoot, "how_to_play_slides"));
            for (var slide = 1; slide <= 9; slide++)
                CopyResource("how_to_play_slides/slide" + slide.ToString("00") + ".jpg");
            CopyResource("quota_goods_v1.0.json");
            CopyResource("cpu_ranking_v1.2.json");
            var sourceGoods = Path.Combine(StreamingRoot, "goods");
            var targetGoods = Path.Combine(ResourceRoot, "goods");
            Directory.CreateDirectory(targetGoods);
            foreach (var source in Directory.GetFiles(sourceGoods, "*.png"))
                File.Copy(source, Path.Combine(targetGoods, Path.GetFileName(source)), true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            // Default NPOT import rounds each axis separately, distorting photos and logos.
            foreach (var file in Directory.GetFiles(ResourceRoot, "*", SearchOption.AllDirectories))
            {
                var importer = AssetImporter.GetAtPath(file) as TextureImporter;
                if (importer == null) continue;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 4096;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }
        }

        static void CopyResource(string name)
        {
            File.Copy(Path.Combine(StreamingRoot, name), Path.Combine(ResourceRoot, name), true);
        }

        static void HardenWebGlPlayer()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = false;
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            PlayerSettings.WebGL.powerPreference = WebGLPowerPreference.Default;
            PlayerSettings.WebGL.initialMemorySize = 128;
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, Il2CppCodeGeneration.OptimizeSize);
            Debug.Log("WebGL player hardened for iPhone Safari.");
        }
    }
}
