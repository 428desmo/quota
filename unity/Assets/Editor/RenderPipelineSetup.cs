using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Quota.EditorTools
{
    static class RenderPipelineSetup
    {
        const string Dir = "Assets/Settings";
        const string RendererPath = Dir + "/UniversalRenderer.asset";
        const string PipelinePath = Dir + "/UniversalRenderPipeline.asset";

        [InitializeOnLoadMethod]
        static void Schedule()
        {
            EditorApplication.delayCall += Ensure;
        }

        public static void Create()
        {
            Ensure();
            EditorApplication.Exit(0);
        }

        static void Ensure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset assigned &&
                    AssetDatabase.Contains(assigned))
                {
                    Finish();
                    return;
                }

                if (!AssetDatabase.IsValidFolder(Dir))
                    AssetDatabase.CreateFolder("Assets", "Settings");

                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
                if (pipeline == null)
                {
                    var create = typeof(UniversalRenderPipelineAsset).GetMethod(
                        "CreateRendererAsset",
                        BindingFlags.Static | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(string), typeof(RendererType), typeof(bool), typeof(string) },
                        null);
                    var renderer = (ScriptableRendererData)create.Invoke(
                        null,
                        new object[] { RendererPath, RendererType.UniversalRenderer, false, "Renderer" });
                    pipeline = UniversalRenderPipelineAsset.Create(renderer);
                    AssetDatabase.CreateAsset(pipeline, PipelinePath);
                }

                GraphicsSettings.defaultRenderPipeline = pipeline;
                var level = QualitySettings.GetQualityLevel();
                for (var i = 0; i < QualitySettings.names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    QualitySettings.renderPipeline = pipeline;
                }
                QualitySettings.SetQualityLevel(level, false);

                AssetDatabase.SaveAssets();
                Finish();
                Debug.Log("Quota: assigned the Universal Render Pipeline.");
            }
            catch (System.Exception error)
            {
                Debug.LogError("Quota: failed to assign the Universal Render Pipeline. " + error);
            }
        }

        static void Finish()
        {
            var settingsType = typeof(UniversalRenderPipelineAsset).Assembly.GetType(
                "UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings");
            var ensure = settingsType.GetMethod(
                "Ensure",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(bool) },
                null);
            ensure.Invoke(null, new object[] { true });
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
                PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.SaveAssets();
        }
    }
}
