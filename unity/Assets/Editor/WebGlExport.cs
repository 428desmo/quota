using System.IO;
using System.Linq;
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
            var commit = BuildCommit();
            File.WriteAllText(Path.Combine(ResourceRoot, "build_commit.txt"), commit);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
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
                WriteOfflineFiles(dest);
                File.WriteAllText(Path.Combine(dest, "build-commit.txt"), commit + "\n");
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

        static string BuildCommit()
        {
            var commit = System.Environment.GetEnvironmentVariable("QUOTA_GIT_COMMIT");
            if (string.IsNullOrWhiteSpace(commit))
            {
                using (var process = new System.Diagnostics.Process())
                {
                    process.StartInfo = new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD")
                    {
                        WorkingDirectory = Path.GetFullPath(".."),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    };
                    process.Start();
                    commit = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                        throw new System.InvalidOperationException("Could not read Git commit for WebGL build: " + process.StandardError.ReadToEnd());
                }
            }
            commit = commit.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(commit, "^[0-9a-fA-F]{40}$"))
                throw new System.InvalidOperationException("QUOTA_GIT_COMMIT must be a full 40-character Git commit hash.");
            return commit.ToLowerInvariant();
        }

        [System.Serializable]
        sealed class OfflineManifest
        {
            public string[] assets;
        }

        static void WriteOfflineFiles(string dest)
        {
            var assets = Directory.GetFiles(dest, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(dest, file).Replace('\\', '/'))
                .Where(path => path.StartsWith("Build/") || path.StartsWith("StreamingAssets/") || path.StartsWith("TemplateData/"))
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .ToArray();
            File.WriteAllText(Path.Combine(dest, "offline-manifest.json"),
                JsonUtility.ToJson(new OfflineManifest { assets = assets }));
            File.Copy("Assets/WebGLTemplates/Quota/offline-sw.js", Path.Combine(dest, "offline-sw.js"), true);
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
