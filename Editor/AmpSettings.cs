// AMP Unity SDK — Project Settings page (concise).

#if UNITY_EDITOR
using Amp.Unity;
using UnityEditor;
using UnityEngine;

namespace Amp.Unity.Editor
{
    [InitializeOnLoad]
    public static class AmpSettings
    {
        private const string ServerKey = "Amp.ServerUrl";
        private const string DefaultServer = "https://amp.playwithamp.xyz";

        public static string ServerUrl
        {
            get => EditorPrefs.GetString(ServerKey, DefaultServer);
            set => EditorPrefs.SetString(ServerKey, value);
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider() => new SettingsProvider("Project/AMP", SettingsScope.Project)
        {
            label = "AMP",
            guiHandler = _ =>
            {
                EditorGUILayout.HelpBox(
                    "Default matchmaker for new AmpManager components.", MessageType.Info);
                ServerUrl = EditorGUILayout.TextField("Server URL", ServerUrl);
                EditorGUILayout.LabelField("Contract", AmpCrypto.DefaultContract);
                EditorGUILayout.LabelField("Chain", $"Fuji ({AmpCrypto.DefaultChainId})");
            },
        };
    }
}
#endif
