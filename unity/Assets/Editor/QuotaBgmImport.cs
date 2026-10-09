using UnityEditor;
using UnityEngine;

namespace Quota.EditorTools
{
    public sealed class QuotaBgmImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (assetPath != "Assets/Resources/QuotaBgm/loop.ogg") return;
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = settings;
        }
    }
}
