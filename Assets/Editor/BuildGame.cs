using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PetTD.EditorBuild
{
    public static class BuildGame
    {
        [MenuItem("Forestkeepers/Verify and Build Windows")]
        public static void Build()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            try
            {
                ImportArt("Assets/Resources/Art/units-atlas.png");
                ImportArt("Assets/Resources/Art/forest-ground.png");
                ImportArt("Assets/Resources/Art/forest-battlefield-v04.png");
                ImportArt("Assets/Resources/Art/enemies-v05.png");
                ValidateEnemyArt();
                ImportArt("Assets/Resources/Art/pet-stages-v2.png");
                ValidatePetArt();
                GameConfig config = JsonUtility.FromJson<GameConfig>(Resources.Load<TextAsset>("balance").text);
                string report = CoreChecks.Run(config);
                Directory.CreateDirectory(Path.Combine(root, "docs"));
                File.WriteAllText(Path.Combine(root, "docs", "core-test-results.md"), "# Core verification\n\nUnity " + Application.unityVersion + " / " + DateTime.UtcNow.ToString("u") + "\n\n```text\n" + report + "\n```\n");
                Debug.Log("TD_CORE_CHECKS_OK\n" + report);
                string drawing = CombatDrawingChecks.Run(config, JsonUtility.FromJson<PetGame.PetAtlasData>(Resources.Load<TextAsset>("Art/pet-stage-regions").text));
                File.WriteAllText(Path.Combine(root, "docs", "drawing-test-results.md"), "# Drawing geometry verification\n\n" + drawing);
                Debug.Log("TD_DRAWING_CHECKS_OK\n" + drawing);

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.06f, 0.15f, 0.12f);
                camera.gameObject.AddComponent<AudioListener>();
                new GameObject("Forestkeepers").AddComponent<PetGame>();
                Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scenes"));
                EditorSceneManager.SaveScene(scene, "Assets/Scenes/Forestkeepers.unity");
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Forestkeepers.unity", true) };
                PlayerSettings.companyName = "ForestkeepersStudio";
                PlayerSettings.productName = "Forestkeepers";
                PlayerSettings.bundleVersion = "0.5.0";
                PlayerSettings.defaultScreenWidth = 1600;
                PlayerSettings.defaultScreenHeight = 900;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.resizableWindow = true;
                PlayerSettings.runInBackground = true;
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
                QualitySettings.vSyncCount = 1;
                AssetDatabase.SaveAssets();
                string destination = Path.Combine(root, "Builds", "Windows", "Forestkeepers.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { "Assets/Scenes/Forestkeepers.unity" },
                    locationPathName = destination,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                });
                if (result.summary.result != BuildResult.Succeeded) throw new Exception("Build failed: " + result.summary.result);
                Debug.Log("TD_BUILD_OK " + destination + " bytes=" + result.summary.totalSize);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        static void ValidateEnemyArt()
        {
            byte[] png=File.ReadAllBytes("Assets/Resources/Art/enemies-v05.png");
            if(png.Length<26 || png[25]!=6) throw new InvalidDataException("Enemy art needs actual RGBA, not a checkerboard.");
            var texture=Resources.Load<Texture2D>("Art/enemies-v05");
            var data=JsonUtility.FromJson<PetGame.PetAtlasData>(Resources.Load<TextAsset>("Art/enemy-regions-v05").text);
            if(data.regions.Length!=12||texture.width!=data.width||texture.height!=data.height)
                throw new InvalidDataException("Enemy atlas dimensions/count mismatch.");
            for(int i=0;i<data.regions.Length;i++)
            {
                var a=data.regions[i];
                if(a.x<0||a.y<0||a.width<1||a.height<1||a.x+a.width>data.width||a.y+a.height>data.height)
                    throw new InvalidDataException("Enemy region outside atlas.");
                for(int j=0;j<i;j++)
                { var b=data.regions[j];if(new Rect(a.x,a.y,a.width,a.height).Overlaps(new Rect(b.x,b.y,b.width,b.height)))throw new InvalidDataException("Enemy regions overlap."); }
            }
            Debug.Log("TD_ENEMY_ART_CHECKS_OK 12 RGBA regions");
        }

        static void ValidatePetArt()
        {
            string path = "Assets/Resources/Art/pet-stages-v2.png";
            byte[] header = File.ReadAllBytes(path);
            if (header.Length < 26 || header[0] != 137 || header[1] != 80 || header[25] != 6)
                throw new InvalidDataException("Pet art must be a genuine RGBA PNG.");
            Texture2D texture = Resources.Load<Texture2D>("Art/pet-stages-v2");
            TextAsset mapping = Resources.Load<TextAsset>("Art/pet-stage-regions");
            if (mapping == null) throw new InvalidDataException("Missing pet sprite regions.");
            var data = JsonUtility.FromJson<PetGame.PetAtlasData>(mapping.text);
            if (data == null || data.regions == null || data.regions.Length != 35
                || data.width != texture.width || data.height != texture.height)
                throw new InvalidDataException("Pet sprite region count or atlas size mismatch.");
            for (int i = 0; i < data.regions.Length; i++)
            {
                var r = data.regions[i];
                if (r == null || r.x < 0 || r.y < 0 || r.width <= 0 || r.height <= 0
                    || r.x + r.width > data.width || r.y + r.height > data.height)
                    throw new InvalidDataException("Invalid pet sprite region " + i);
                var bounds = new Rect(r.x, r.y, r.width, r.height);
                for (int j = 0; j < i; j++)
                {
                    var other = data.regions[j];
                    if (bounds.Overlaps(new Rect(other.x, other.y, other.width, other.height)))
                        throw new InvalidDataException("Pet sprite regions overlap: " + i + "/" + j);
                }
            }
            Debug.Log("TD_PET_ART_CHECKS_OK 35 RGBA regions");
        }

        static void ImportArt(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new FileNotFoundException(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 4096;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
