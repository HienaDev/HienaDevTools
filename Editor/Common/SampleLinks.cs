using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Samples that need another sample bring it with them: importing the Building Builder imports Texture Studio
    /// too (the builder uses its materials, pictures and text).</summary>
    [InitializeOnLoad]
    static class SampleLinks
    {
        const string Package = "com.hienadev.devtools";
        static readonly (string sample, string needs)[] Links = { ("Building Builder", "Texture Studio") };

        static SampleLinks() { EditorApplication.delayCall += Check; EditorApplication.projectChanged += Check; }

        static bool s_Busy;
        static void Check()
        {
            if (s_Busy || EditorApplication.isPlayingOrWillChangePlaymode) return; s_Busy = true;
            try
            {
                var samples = Sample.FindByPackage(Package, null).ToList(); if (samples.Count == 0) return;
                foreach (var (sample, needs) in Links)
                {
                    var s = samples.FirstOrDefault(q => q.displayName == sample); var n = samples.FirstOrDefault(q => q.displayName == needs);
                    if (string.IsNullOrEmpty(s.displayName) || string.IsNullOrEmpty(n.displayName) || !s.isImported || n.isImported) continue;
                    // (an older copy imported for another version counts too)
                    if (AssetDatabase.FindAssets("TextureStudioWindow t:MonoScript").Length > 0) continue;
                    Debug.Log("HienaDev Dev Tools: the " + sample + " sample uses " + needs + ", so it's being imported too.");
                    n.Import(Sample.ImportOptions.HideImportWindow | Sample.ImportOptions.OverridePreviousImports);
                }
            }
            catch (System.Exception e) { Debug.LogWarning("HienaDev Dev Tools: couldn't check the samples: " + e.Message); }
            finally { s_Busy = false; }
        }
    }
}
