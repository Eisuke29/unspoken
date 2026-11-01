using System.IO;
using Before.App;
using Before.Config;
using Before.Editor.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Before.Editor.Setup
{
    /// <summary>
    /// Deterministic builder for the BEFORE_Main scene: creates the config assets, regenerates
    /// the art, and saves a scene containing only BEFORE_Root + BeforeApp with every reference wired.
    /// Never touches the old click-prediction prototype (Assets/Scenes/BeforeMain.unity).
    /// </summary>
    public static class BeforeSceneBuilder
    {
        private const string ConfigRoot = "Assets/BEFORE/Generated/Config";
        private const string ScenePath = "Assets/BEFORE/Scenes/BEFORE_Main.unity";
        private const string ModelRoot = "Assets/BEFORE/ThirdParty/UnityBlazeHand";
        private const string FontPath = "Assets/BEFORE/ThirdParty/Fonts/DotGothic16/DotGothic16-Regular.ttf";

        [MenuItem("BEFORE/セットアップ/BEFORE_Main を構築")]
        public static void BuildAll()
        {
            BeforeArtGenerator.RegenerateAll();

            // the scene must be created BEFORE loading any asset references:
            // NewScene(Single) unloads previously loaded ScriptableObject assets
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var runtime = GetOrCreateConfig<BeforeRuntimeConfig>("BeforeRuntimeConfig.asset");
            var recognition = GetOrCreateConfig<BeforeRecognitionConfig>("BeforeRecognitionConfig.asset");
            var course = GetOrCreateConfig<BeforeCourseConfig>("BeforeCourseConfig.asset");
            var presentation = GetOrCreateConfig<BeforePresentationConfig>("BeforePresentationConfig.asset");

            var root = new GameObject("BEFORE_Root");
            var app = root.AddComponent<BeforeApp>();

            var so = new SerializedObject(app);
            Bind(so, "runtimeConfig", runtime);
            Bind(so, "recognitionConfig", recognition);
            Bind(so, "courseConfig", course);
            Bind(so, "presentationConfig", presentation);
            Bind(so, "handDetector", Load<Unity.InferenceEngine.ModelAsset>($"{ModelRoot}/Models/hand_detector.onnx"));
            Bind(so, "handLandmarker", Load<Unity.InferenceEngine.ModelAsset>($"{ModelRoot}/Models/hand_landmarks_detector.onnx"));
            Bind(so, "anchorsCsv", Load<TextAsset>($"{ModelRoot}/Data/anchors.csv"));
            Bind(so, "nulSideSheet", Load<Texture2D>($"{BeforeArtGenerator.OutputRoot}/NUL_Side_48.png"));
            Bind(so, "worldTiles", Load<Texture2D>($"{BeforeArtGenerator.OutputRoot}/WorldTiles_16.png"));
            Bind(so, "hazardsSheet", Load<Texture2D>($"{BeforeArtGenerator.OutputRoot}/Hazards_32.png"));
            Bind(so, "bgFar", Load<Texture2D>($"{BeforeArtGenerator.OutputRoot}/BG_Far_640x180.png"));
            Bind(so, "bgMid", Load<Texture2D>($"{BeforeArtGenerator.OutputRoot}/BG_Mid_640x180.png"));
            Bind(so, "bgNear", Load<Texture2D>($"{BeforeArtGenerator.OutputRoot}/BG_Near_640x180.png"));
            Bind(so, "dotGothic", Load<Font>(FontPath));
            so.FindProperty("useFakeHandStream").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log(saved
                ? $"[BEFORE] BEFORE_Main built and saved at {ScenePath}."
                : $"[BEFORE] FAILED to save {ScenePath}.");
        }

        private static T GetOrCreateConfig<T>(string fileName) where T : ScriptableObject
        {
            Directory.CreateDirectory(ConfigRoot);
            string path = $"{ConfigRoot}/{fileName}";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            // return the persisted asset (a deferred import would otherwise serialize as null in the scene)
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) Debug.LogError($"[BEFORE] Required asset missing: {path}");
            return asset;
        }

        private static void Bind(SerializedObject so, string property, Object value)
        {
            var prop = so.FindProperty(property);
            if (prop == null)
            {
                Debug.LogError($"[BEFORE] BeforeApp property not found: {property}");
                return;
            }
            prop.objectReferenceValue = value;
        }
    }
}
