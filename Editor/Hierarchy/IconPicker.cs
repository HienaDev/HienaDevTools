using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Pick an icon: for a script (every object using it shows it: saved in the script's .meta, so git keeps it) or for
    /// objects in the scene (saved in the scene). Right-click a component's title > Choose Script Icon..., or right-click
    /// objects in the Hierarchy > Choose Icon....
    /// </summary>
    public class IconPicker : EditorWindow
    {
        static readonly string[] Names =
        {
            "sv_label_0", "sv_label_1", "sv_label_2", "sv_label_3", "sv_label_4", "sv_label_5", "sv_label_6", "sv_label_7",
            "sv_icon_dot0_pix16_gizmo", "sv_icon_dot1_pix16_gizmo", "sv_icon_dot2_pix16_gizmo", "sv_icon_dot3_pix16_gizmo", "sv_icon_dot4_pix16_gizmo", "sv_icon_dot5_pix16_gizmo", "sv_icon_dot6_pix16_gizmo", "sv_icon_dot7_pix16_gizmo",
            "d_Favorite Icon", "d_GameManager Icon", "d_cs Script Icon", "d_ScriptableObject Icon", "d_Prefab Icon", "d_SceneAsset Icon", "d_Settings Icon", "d_EventSystem Icon",
            "d_Camera Icon", "d_Light Icon", "d_AudioSource Icon", "d_AudioListener Icon", "d_Animator Icon", "d_Animation Icon", "d_PlayableDirector Icon", "d_ParticleSystem Icon",
            "d_Rigidbody Icon", "d_BoxCollider Icon", "d_SphereCollider Icon", "d_CharacterController Icon", "d_NavMeshAgent Icon", "d_WindZone Icon", "d_Terrain Icon", "d_Tree Icon",
            "d_Canvas Icon", "d_Image Icon", "d_Text Icon", "d_Button Icon", "d_Slider Icon", "d_Toggle Icon", "d_SpriteRenderer Icon", "d_Grid Icon",
            "d_Material Icon", "d_Mesh Icon", "d_Texture Icon", "d_AudioClip Icon", "d_Font Icon", "d_VideoPlayer Icon", "d_ReflectionProbe Icon", "d_LightProbeGroup Icon",
            "d_console.infoicon", "d_console.warnicon", "d_console.erroricon", "d_PlayButton", "d_PauseButton", "d_Refresh", "d_SaveAs", "d_Search Icon",
        };

        List<Object> m_Targets; bool m_ForScript; string m_What; Vector2 m_Scroll;
        static List<Texture2D> s_Icons;

        [MenuItem("CONTEXT/MonoBehaviour/Choose Script Icon...", false, 2000)]
        static void ForScript(MenuCommand c)
        {
            var script = MonoScript.FromMonoBehaviour((MonoBehaviour)c.context);
            if (!script || !AssetDatabase.GetAssetPath(script).StartsWith("Assets")) { EditorUtility.DisplayDialog("Script icon", "Only scripts in your Assets folder can get an icon (this one is in a package).", "OK"); return; }
            Open(new List<Object> { script }, true, script.name + " (every object using this script)");
        }

        [MenuItem("GameObject/Choose Icon...", false, 0)]
        static void ForObjects()
        {
            var gos = Selection.gameObjects.Cast<Object>().ToList();
            if (gos.Count == 0) return;
            Open(gos, false, gos.Count == 1 ? gos[0].name : gos.Count + " objects");
        }
        [MenuItem("GameObject/Choose Icon...", true)] static bool ForObjectsOk() => Selection.gameObjects.Length > 0;

        static void Open(List<Object> targets, bool forScript, string what)
        {
            var w = CreateInstance<IconPicker>();
            w.m_Targets = targets; w.m_ForScript = forScript; w.m_What = what;
            w.titleContent = new GUIContent("Choose an icon");
            w.minSize = new Vector2(300, 330); w.maxSize = new Vector2(300, 600);
            w.ShowUtility();
        }

        void OnGUI()
        {
            if (m_Targets == null || m_Targets.All(t => !t)) { Close(); return; }
            EditorGUILayout.LabelField("Icon for " + m_What, EditorStyles.boldLabel);
            if (s_Icons == null) s_Icons = Names.Select(n => EditorGUIUtility.FindTexture(n)).Where(t => t).Distinct().ToList();
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            int cols = 8, i = 0;
            foreach (var icon in s_Icons)
            {
                if (i % cols == 0) EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent(icon, icon.name), GUILayout.Width(32), GUILayout.Height(32))) { Apply(icon); return; }
                if (++i % cols == 0) EditorGUILayout.EndHorizontal();
            }
            if (i % cols != 0) { GUILayout.FlexibleSpace(); EditorGUILayout.EndHorizontal(); }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("A picture from the project...")) EditorGUIUtility.ShowObjectPicker<Texture2D>(null, false, "", 4321);
            if (GUILayout.Button("No icon", GUILayout.Width(70))) { Apply(null); return; }
            EditorGUILayout.EndHorizontal();
            if (Event.current.commandName == "ObjectSelectorClosed" && EditorGUIUtility.GetObjectPickerControlID() == 4321)
            {
                var t = EditorGUIUtility.GetObjectPickerObject() as Texture2D;
                if (t) { Apply(t); return; }
            }
        }

        void Apply(Texture2D icon)
        {
            foreach (var t in m_Targets)
            {
                if (!t) continue;
                if (!m_ForScript) Undo.RecordObject(t, "Choose icon");
                EditorGUIUtility.SetIconForObject(t, icon);
                if (m_ForScript) { EditorUtility.SetDirty(t); AssetDatabase.SaveAssetIfDirty(t); }
                else EditorUtility.SetDirty(t);
            }
            EditorApplication.RepaintHierarchyWindow();
            Close();
        }
    }
}
