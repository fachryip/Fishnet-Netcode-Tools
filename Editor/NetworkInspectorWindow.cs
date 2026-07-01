using UnityEditor;
using UnityEngine;

namespace FishNetNetworkTools.Editor
{
    /// <summary>
    /// Editor window host for the FishNet network inspector. This is the Phase 0 shell:
    /// it owns the menu entry and an empty body. The live inspector UI (spawned object
    /// tree, ownership/observers, and the SyncVar readout via the SyncTypeAdapter)
    /// is added in a later build step.
    /// </summary>
    public class NetworkInspectorWindow : EditorWindow
    {
        private const string MenuPath = "Tools/FishNet Network Tools/Network Inspector";
        private const string WindowTitle = "FishNet Network Inspector";

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var window = GetWindow<NetworkInspectorWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(360f, 240f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                WindowTitle + "\n\n" +
                "Enter Play Mode as a host (or a separate server and client) to inspect live " +
                "network state: spawned NetworkObjects, ownership, observers, and SyncVar values.\n\n" +
                "The live inspector view is coming in a later build step.",
                MessageType.Info);
        }
    }
}
