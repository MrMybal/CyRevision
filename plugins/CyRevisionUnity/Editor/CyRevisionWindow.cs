using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace CyRevision.Unity
{
    internal sealed class CyRevisionWindow : EditorWindow
    {
        private string _status = "Connection not tested.";

        [MenuItem("Tools/CyRevision/Connection")]
        private static void OpenWindow() => GetWindow<CyRevisionWindow>("CyRevision");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("CyRevision", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This companion remains useful as a project status panel without CyRevision. " +
                "When linked, it can notify the matching desktop project over loopback.",
                MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Test connection")) _status = CyRevisionBridge.TestConnection();
                if (GUILayout.Button("Notify project change")) _status = CyRevisionBridge.Notify("unity-project-change");
                if (GUILayout.Button("Open CyRevision")) _status = CyRevisionBridge.OpenApplication();
            }
            EditorGUILayout.Space();
            EditorGUILayout.SelectableLabel(_status, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(60));
        }
    }

    internal sealed class CyRevisionAssetSaveHook : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            Task.Run(() => CyRevisionBridge.Notify("assets-saved"));
            return paths;
        }
    }

    [Serializable]
    internal sealed class BridgeSettings
    {
        public int schemaVersion;
        public string engine;
        public string url;
        public string token;
        public string executablePath;
    }

    internal static class CyRevisionBridge
    {
        private static string SettingsPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "CyRevision", "bridge.json");

        public static string TestConnection() => Send("status", "GET", null);

        public static string Notify(string action) => Send("notify", "POST", "{\"action\":\"" + Escape(action) + "\"}");

        public static string OpenApplication()
        {
            BridgeSettings settings = Load();
            if (settings == null || string.IsNullOrWhiteSpace(settings.executablePath)) return "CyRevision executable is not configured.";
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = settings.executablePath,
                    Arguments = "--project \"" + Directory.GetParent(Application.dataPath).FullName + "\"",
                    UseShellExecute = true
                });
                return "CyRevision opened for this Unity project.";
            }
            catch (Exception exception) { return "Could not open CyRevision: " + exception.Message; }
        }

        private static string Send(string route, string method, string body)
        {
            BridgeSettings settings = Load();
            if (settings == null) return "Link not configured. Install or configure the Unity companion from CyRevision.";
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(settings.url.TrimEnd('/') + "/" + route);
                request.Method = method;
                request.Timeout = 2000;
                request.Headers[HttpRequestHeader.Authorization] = "Bearer " + settings.token;
                if (body != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    request.ContentType = "application/json";
                    request.ContentLength = bytes.Length;
                    using (Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
                }
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    return "Connected to CyRevision (HTTP " + (int)response.StatusCode + ").";
            }
            catch (Exception exception) { return "CyRevision connection failed: " + exception.Message; }
        }

        private static BridgeSettings Load()
        {
            try { return File.Exists(SettingsPath) ? JsonUtility.FromJson<BridgeSettings>(File.ReadAllText(SettingsPath)) : null; }
            catch { return null; }
        }

        private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
