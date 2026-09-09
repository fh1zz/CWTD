using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace PetTD
{
    /// <summary>Unity presentation and local profile. Game rules are owned by GameModel.</summary>
    public sealed class PetGame : MonoBehaviour
    {
        enum Page { Home, Levels, Team, Presets, Gallery, Battle }
        [Serializable] public class Preset { public string[] team = new string[0]; }
        [Serializable] public class PetAtlasRegion { public int x, y, width, height; }
        [Serializable] public class PetAtlasData { public int width, height; public PetAtlasRegion[] regions; }
        [Serializable] public class Profile
        {
            public int version = 1;
            public string[] lastTeam = { "fox", "otter", "falcon", "sprout", "deer" };
            public Preset[] presets = { new Preset(), new Preset(), new Preset() };
            public int activePreset = -1;
            public int[] stars = new int[3];
            public int[] badges = new int[3];
            public bool muted;
        }
        GameConfig config;
        GameModel game;
        Profile profile;
        Page page;
        List<string> draft = new List<string>();
        List<string> presetEntryDraft;
        Page presetOrigin = Page.Home;
        int presetEntryActive = -1;
        string[] battleTeam;
        int levelIndex, selectedSpecies = 3, selectedPet = -1, selectedPreset, editingPreset = -1, elementTab = -1;
        bool confirmStart, showSkill, skillTarget, resultSaved, help, poolOpen = true, gachaOpen = true;
        float speed = 1, poolReveal = 1, gachaReveal = 1, noticeUntil, accumulator;
        string notice = "", capturePath, capturePage;
        bool captureStarted, qaSession;
        bool galleryActive, bestiary;
        int selectedEnemyId = -1;
        int bestiaryPage;
        bool backupUnreadableProfile, newerProfile;
        Texture2D ground, battlefield, enemyAtlas, atlas, stageAtlas, white, circle;
        PetAtlasData enemyAtlasData;
        PetAtlasData petAtlasData;
        Font font;
        GUIStyle textStyle, transparentButton;
        AudioSource audioSource;
        AudioClip clickTone;
        readonly Color ink = Hex("172C29"), panel = Hex("193D35"), deep = Hex("10251F"), gold = Hex("E9C477"), cream = Hex("F3E9CE"), muted = Hex("A8B9A0");
        // Fit the combat metric into the unobstructed map area; never stretch X.
        readonly Rect field = CombatDrawing.Battlefield;
        string SavePath { get { return Path.Combine(Application.persistentDataPath, "forestkeepers-profile.json"); } }
        int Capacity { get { return editingPreset >= 0 ? 5 : config.levels[levelIndex].slots; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindObjectOfType<PetGame>() == null) new GameObject("Forestkeepers").AddComponent<PetGame>();
        }

        void Awake()
        {
            Application.targetFrameRate = 60;
            config = JsonUtility.FromJson<GameConfig>(Resources.Load<TextAsset>("balance").text);
            if (config == null || config.pets == null || config.pets.Length != 7 || config.levels == null || config.levels.Length != 3)
                throw new InvalidDataException("Invalid balance.json");
            ground = Resources.Load<Texture2D>("Art/forest-ground");
            battlefield = Resources.Load<Texture2D>("Art/forest-battlefield-v04");
            enemyAtlas = Resources.Load<Texture2D>("Art/enemies-v05");
            TextAsset enemyRegions = Resources.Load<TextAsset>("Art/enemy-regions-v05");
            if (enemyRegions != null) enemyAtlasData = JsonUtility.FromJson<PetAtlasData>(enemyRegions.text);
            atlas = Resources.Load<Texture2D>("Art/units-atlas");
            if(enemyAtlas==null)
            {
                enemyAtlas=atlas;
                TextAsset fallback=Resources.Load<TextAsset>("Art/enemy-regions-legacy-v04");
                if(fallback!=null) enemyAtlasData=JsonUtility.FromJson<PetAtlasData>(fallback.text);
            }
            stageAtlas = Resources.Load<Texture2D>("Art/pet-stages-v2");
            TextAsset regionAsset = Resources.Load<TextAsset>("Art/pet-stage-regions");
            if (regionAsset != null) petAtlasData = JsonUtility.FromJson<PetAtlasData>(regionAsset.text);
            white = Texture2D.whiteTexture;
            circle = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[4096];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                pixels[y * 64 + x] = new Color(1, 1, 1, Mathf.Clamp01((31.5f - Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f))) * 1.4f));
            circle.SetPixels(pixels); circle.Apply();
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" }, 24);
            audioSource = gameObject.AddComponent<AudioSource>();
            clickTone = AudioClip.Create("UI tap", 2205, 1, 22050, false);
            float[] tone = new float[2205];
            for (int i = 0; i < tone.Length; i++) tone[i] = Mathf.Sin(i * 0.22f) * Mathf.Exp(-i / 360f) * 0.12f;
            clickTone.SetData(tone, 0);
            // Only an isolated QA bundle carries this flag; it is never published.
            qaSession=File.Exists(Path.Combine(Application.dataPath,"td-qa.flag"));
            profile = qaSession ? new Profile() : ReadProfile();
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-tdCapture") capturePath = args[i + 1];
                if (args[i] == "-tdPage") capturePage = args[i + 1];
            }
            if(qaSession)
            {
                capturePath="interactive-qa";
                levelIndex=1; draft=new List<string>{"fox","otter","falcon","sprout","deer"}; BeginBattle();
            }
            else if (!string.IsNullOrEmpty(capturePath)) StartCoroutine(CaptureRun());
        }

        void Update()
        {
            if(qaSession && Input.GetKeyDown(KeyCode.F9))
                ScreenCapture.CaptureScreenshot(Path.Combine(Application.dataPath,"../qa-battle-v04.png"));
            poolReveal = Mathf.MoveTowards(poolReveal, poolOpen ? 1 : 0, Time.unscaledDeltaTime * 6);
            gachaReveal = Mathf.MoveTowards(gachaReveal, gachaOpen ? 1 : 0, Time.unscaledDeltaTime * 6);
            if (page == Page.Battle && game != null && !captureStarted)
            {
                accumulator += Time.unscaledDeltaTime * speed;
                int steps = 0;
                while (accumulator >= 1f / 60 && steps++ < 12) { game.Tick(1f / 60); accumulator -= 1f / 60; }
                accumulator = Mathf.Min(accumulator, 0.2f);
                if (game.Stage != RunStage.Running || game.Paused) skillTarget = false;
                if ((game.Stage == RunStage.Won || game.Stage == RunStage.Lost) && !resultSaved)
                {
                    resultSaved = true;
                    if (game.Stage == RunStage.Won)
                    {
                        profile.badges[levelIndex] |= CurrentBadges();
                        profile.stars[levelIndex] = BadgeCount(profile.badges[levelIndex]);
                        SaveProfile();
                    }
                }
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (confirmStart) confirmStart = false;
                else if (showSkill) showSkill = false;
                else if (bestiary) { bestiary = false; if (game != null) game.Paused = false; }
                else if (help) { help = false; if (game != null) game.Paused = false; }
                else if (page == Page.Battle && game != null && game.Stage != RunStage.Won && game.Stage != RunStage.Lost)
                {
                    if (skillTarget) skillTarget = false;
                    else { game.Paused = !game.Paused; selectedPet = -1; }
                }
                else if (page == Page.Team) BackFromTeam();
                else if (page == Page.Gallery) page = Page.Team;
                else if (page == Page.Presets) BackFromPresets();
                else if (page != Page.Home && page != Page.Battle) page = Page.Home;
            }
            if (page == Page.Battle && game != null && !game.Paused && !help)
            {
                if (Input.GetKeyDown(KeyCode.Space) && game.Stage == RunStage.Preparing) Act(game.StartWave());
                if (Input.GetKeyDown(KeyCode.G)) Act(game.DrawPet());
                if (Input.GetKeyDown(KeyCode.E)) Act(game.EvolvePool() > 0);
            }
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && string.IsNullOrEmpty(capturePath) && page == Page.Battle && game != null && game.Stage != RunStage.Won && game.Stage != RunStage.Lost)
            { game.Paused = true; skillTarget = false; selectedPet = -1; }
        }

        void OnGUI()
        {
            if (config == null) return;
            if (textStyle == null)
            {
                textStyle = new GUIStyle(GUI.skin.label) { font = font, wordWrap = true, richText = false, padding = new RectOffset() };
                transparentButton = new GUIStyle(GUIStyle.none);
            }
            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            Fill(new Rect(0,0,Screen.width,Screen.height), deep);
            GUI.matrix = CombatDrawing.ScreenMatrix(Screen.width, Screen.height);
            GUI.color = Color.white;
            Fill(new Rect(0, 0, 1920, 1080), deep);
            if (page != Page.Battle && ground != null) GUI.DrawTexture(new Rect(0, 0, 1920, 1080), ground, ScaleMode.ScaleAndCrop);
            bool modal = confirmStart || showSkill || help || bestiary;
            GUI.enabled = !modal;
            if (page == Page.Home) Home();
            else if (page == Page.Levels) Levels();
            else if (page == Page.Team) Team();
            else if (page == Page.Presets) Presets();
            else if (page == Page.Gallery) Gallery();
            else Battle();
            GUI.enabled = true;
            if (confirmStart) ConfirmStart();
            if (showSkill) SkillModal();
            if (help) HelpModal();
            if (bestiary) Bestiary();
            if (Time.unscaledTime < noticeUntil && (page != Page.Battle || game == null || !game.Paused))
            {
                Box(new Rect(610, 34, 700, 66), ink, gold);
                Label(new Rect(635, 48, 650, 44), notice, 23, cream, TextAnchor.MiddleCenter);
            }
            GUI.color = Color.white; GUI.matrix = oldMatrix;
        }

        void Home()
        {
            Fill(new Rect(0, 0, 930, 1080), new Color(0.035f, 0.12f, 0.1f, 0.86f));
            Fill(new Rect(930, 0, 990, 1080), new Color(0.07f, 0.16f, 0.1f, 0.16f));
            Label(new Rect(115, 85, 650, 40), "FORESTKEEPERS  /  PET DEFENSE", 22, gold);
            Label(new Rect(110, 228, 750, 130), "森灵守望", 100, cream, TextAnchor.MiddleLeft, true);
            Label(new Rect(120, 374, 600, 48), "每一个小小伙伴，都有守护森林的力量。", 26, muted);
            Label(new Rect(120, 448, 620, 90), "挑选你的队伍，扭蛋即刻获得伙伴。\n五阶成长，七种天赋。让三份微光汇成更强的守护。", 23, cream);
            if (Button(new Rect(120, 594, 340, 82), "开始守护   →", true)) page = Page.Levels;
            if (Button(new Rect(480, 594, 236, 82), "伙伴图鉴")) { OpenTeam(); page = Page.Team; }
            if (Button(new Rect(120, 700, 285, 64), "预设编队")) OpenPresets();
            if (Button(new Rect(425, 700, 291, 64), "玩法指南")) help = true;
            Line(new Vector2(120, 840), new Vector2(716, 840), 1, new Color(0.8f, 0.7f, 0.4f, 0.4f));
            string[] nums = { "07", "05", "03" }, caps = { "元素伙伴", "进化阶段", "森林挑战" };
            for (int i = 0; i < 3; i++) { Label(new Rect(125 + i * 213, 875, 150, 52), nums[i], 42, gold); Label(new Rect(125 + i * 213, 940, 180, 32), caps[i], 20, muted); }
            PetSprite(3, 3, new Rect(1065, 384, 465, 465));
            PetSprite(5, 4, new Rect(1490, 460, 310, 310));
            PetSprite(1, 2, new Rect(1210, 737, 230, 230));
            Label(new Rect(1150, 240, 640, 60), "欢迎来到，初芽林地", 32, cream, TextAnchor.MiddleCenter, true);
            Label(new Rect(1230, 303, 480, 35), "从初生伙伴，到五阶守护者", 22, cream, TextAnchor.MiddleCenter);
            if (Button(new Rect(1710, 36, 162, 48), profile.muted ? "音效：关" : "音效：开")) { profile.muted = !profile.muted; SaveProfile(); }
            Label(new Rect(120, 1020, 750, 30), "方向键以外的故事，由你的每一次选择写下。", 17, muted);
        }

        void Header(string subtitle, string title, Action back)
        {
            Fill(new Rect(0, 0, 1920, 1080), new Color(0.025f, 0.08f, 0.06f, 0.8f));
            Label(new Rect(85, 52, 1200, 32), "FORESTKEEPERS  /  " + subtitle, 19, gold);
            Label(new Rect(80, 100, 1300, 76), title, 52, cream, TextAnchor.MiddleLeft, true);
            if (Button(new Rect(1695, 64, 145, 55), "←  返回")) back();
        }

        void Levels()
        {
            Header("THE WOODLAND TRAIL", "选择你的守护之地", () => page = Page.Home);
            Label(new Rect(87, 185, 1300, 40), "从第一道防线开始。每次出战，都能重新挑选伙伴。", 23, muted);
            for (int i = 0; i < config.levels.Length; i++)
            {
                LevelSpec level = config.levels[i]; float x = 86 + i * 593;
                Box(new Rect(x, 290, 556, 620), panel, i == levelIndex ? gold : Hex("567A59"));
                GUI.DrawTexture(new Rect(x + 12, 302, 532, 246), ground, ScaleMode.ScaleAndCrop);
                Fill(new Rect(x + 12, 302, 532, 246), new Color(0.07f, 0.12f, 0.09f, 0.16f + i * 0.08f));
                Label(new Rect(x + 32, 320, 110, 58), "0" + (i + 1), 42, cream, TextAnchor.MiddleLeft, true);
                Sprite(i == 0 ? 1 : i == 1 ? 2 : 9, new Rect(x + 337, 337, 158, 158));
                Label(new Rect(x + 36, 578, 480, 55), level.name, 36, cream, TextAnchor.MiddleLeft, true);
                Label(new Rect(x + 36, 650, 480, 62), level.subtitle, 23, muted);
                Label(new Rect(x + 36, 727, 480, 38), level.waves + " 波敌人    ·    " + level.slots + " 个编队位", 22, gold);
                Label(new Rect(x + 36, 780, 210, 35), profile.stars[i] > 0 ? new string('★', profile.stars[i]) : "尚未通关", 22, gold);
                if (Button(new Rect(x + 275, 795, 238, 68), "准备队伍  →", true)) { levelIndex = i; OpenTeam(); }
            }
            Label(new Rect(88, 964, 1650, 40), "提示：初芽林地开放 3 个编队位，后续关卡开放全部 5 个。三只同种同级伙伴可以进化。", 22, muted);
        }

        void OpenTeam()
        {
            editingPreset = -1;
            string[] initial = profile.activePreset >= 0 ? profile.presets[profile.activePreset].team : profile.lastTeam;
            draft = CleanTeam(initial, config.levels[levelIndex].slots).ToList();
            selectedSpecies = draft.Count > 0 ? Array.FindIndex(config.pets, p => p.id == draft[0]) : 0;
            elementTab = -1; page = Page.Team;
        }

        void Team()
        {
            Header("YOUR COMPANIONS", editingPreset >= 0 ? "编辑预设队伍 0" + (editingPreset + 1) : "为「" + config.levels[levelIndex].name + "」挑选伙伴", BackFromTeam);
            Label(new Rect(85, 189, 1300, 40), "出战的物种决定扭蛋范围。相同伙伴会在战场上重逢，并一起进化。", 22, muted);
            string[] tabs = { "全部", "金", "木", "水", "火", "土", "光", "暗" };
            for (int i = 0; i < 8; i++) if (Button(new Rect(85 + i * 115, 257, 101, 48), tabs[i], elementTab == i - 1)) { elementTab = i - 1; if (elementTab >= 0) selectedSpecies = elementTab; }
            IEnumerable<int> order = Enumerable.Range(0, 7).Where(i => elementTab < 0 || elementTab == i).OrderByDescending(i => draft.Contains(config.pets[i].id));
            int j = 0;
            foreach (int i in order)
            {
                PetSpec pet = config.pets[i]; float x = 85 + (j % 4) * 233, y = 330 + (j / 4) * 231;
                bool picked = selectedSpecies == i;
                Box(new Rect(x, y, 214, 210), picked ? Hex("335B42") : panel, picked ? gold : Hex("476C52"));
                Sprite(i, new Rect(x + 45, y + 12, 124, 124));
                Label(new Rect(x + 12, y + 143, 190, 38), pet.name, 25, cream, TextAnchor.MiddleCenter, true);
                Label(new Rect(x + 8, y + 182, 198, 24), draft.Contains(pet.id) ? "● 编队中" : pet.role, 17, draft.Contains(pet.id) ? gold : muted, TextAnchor.MiddleCenter);
                if (Hit(new Rect(x, y, 214, 210))) { selectedSpecies = i; Tap(); }
                j++;
            }
            PetSpec focus = config.pets[Mathf.Clamp(selectedSpecies, 0, 6)];
            Box(new Rect(1068, 257, 760, 539), panel, Hex("557458"));
            Sprite(selectedSpecies, new Rect(1110, 290, 215, 215));
            Label(new Rect(1360, 289, 380, 58), focus.name, 40, cream, TextAnchor.MiddleLeft, true);
            Label(new Rect(1363, 360, 385, 38), focus.element + "元素  /  " + focus.role, 24, Hex(focus.colorHex));
            Label(new Rect(1363, 428, 390, 56), "攻击 " + focus.damage + "    间隔 " + focus.interval.ToString("0.00") + "s", 23, cream);
            Label(new Rect(1110, 540, 662, 82), focus.description, 24, muted);
            if (Button(new Rect(1110, 665, 220, 64), "查看技能")) showSkill = true;
            if (Button(new Rect(1110, 744, 665, 40), "五阶进化图鉴  →")) page = Page.Gallery;
            if (Button(new Rect(1350, 665, 425, 64), draft.Contains(focus.id) ? "从队伍下阵" : "加入队伍  ＋", !draft.Contains(focus.id)))
                ToggleRoster(focus.id);
            Label(new Rect(90, 830, 470, 40), "当前队伍   " + draft.Count + " / " + Capacity, 25, cream, TextAnchor.MiddleLeft, true);
            for (int i = 0; i < 5; i++)
            {
                Rect r = new Rect(85 + i * 171, 886, 152, 130);
                Box(r, i >= Capacity ? deep : panel, i < draft.Count ? gold : Hex("4F6752"));
                if (i >= Capacity) Label(r, "锁定", 23, muted, TextAnchor.MiddleCenter);
                else if (i < draft.Count)
                {
                    int index = Array.FindIndex(config.pets, p => p.id == draft[i]);
                    Sprite(index, new Rect(r.x + 28, r.y + 4, 97, 97));
                    Label(new Rect(r.x, r.y + 101, 152, 24), "点击下阵", 16, muted, TextAnchor.MiddleCenter);
                    if (Hit(r)) { draft.RemoveAt(i); Tap(); break; }
                }
                else Label(r, "＋", 40, muted, TextAnchor.MiddleCenter);
            }
            if (editingPreset >= 0)
            {
                if (Button(new Rect(1388, 918, 390, 76), "保存预设", true)) CommitPreset();
            }
            else
            {
                if (Button(new Rect(1045, 918, 298, 76), "预设编队")) OpenPresets();
                if (Button(new Rect(1375, 918, 410, 76), "出击  →", true, draft.Count > 0)) RequestBattle();
            }
        }

        void Gallery()
        {
            PetSpec focus = config.pets[selectedSpecies];
            Header("FIVE FORMS, ONE COMPANION", focus.name + " · 五阶进化图鉴", () => page = Page.Team);
            for (int i = 0; i < 7; i++)
                if (Button(new Rect(85 + i * 247, 217, 230, 50), config.pets[i].element + " · " + config.pets[i].name, selectedSpecies == i)) selectedSpecies = i;
            focus = config.pets[selectedSpecies];
            if (Button(new Rect(1195, 158, 292, 42), "普攻 · 被动", !galleryActive)) galleryActive = false;
            if (Button(new Rect(1500, 158, 310, 42), "主动技 · 自动释放", galleryActive)) galleryActive = true;
            string[] phaseLabels = { "初生", "成长", "觉醒", "超越", "终极" };
            for (int i = 0; i < 5; i++)
            {
                StageSpec phase = focus.stages[i];
                float x = 85 + i * 351;
                Box(new Rect(x, 306, 330, 627), panel, i == 4 ? gold : Hex("557458"));
                Label(new Rect(x + 19, 324, 292, 34), (i + 1) + "/5  ·  " + phaseLabels[i], 23, gold, TextAnchor.MiddleCenter, true);
                PetSprite(selectedSpecies, i + 1, new Rect(x + 51, 376, 228, 228));
                Label(new Rect(x + 17, 619, 296, 46), phase.name, 27, cream, TextAnchor.MiddleCenter, true);
                Label(new Rect(x + 24, 683, 284, 56), "攻击 " + PhaseDamage(focus, i).ToString("0.#") + "   间隔 " + PhaseInterval(focus, i).ToString("0.00") + "s\n射程 " + (focus.range * phase.rangeMultiplier).ToString("0.000"), 19, muted);
                AutoSkillSpec automatic = phase.active;
                string skillTitle = galleryActive ? automatic.name : phase.ability;
                string skillBody = galleryActive ? automatic.description : phase.description;
                Label(new Rect(x + 24, 752, 284, 30), skillTitle, 23, Hex(focus.colorHex), TextAnchor.MiddleLeft, true);
                Label(new Rect(x + 24, 790, 284, 94), skillBody, 18, cream);
                Label(new Rect(x + 18, 894, 295, 25), galleryActive ? "首施3s · 冷却" + automatic.cooldown.ToString("0.#") + "s · 自动" : (i == 4 ? "终极阶段 · 81只1阶当量" : "下一阶：3只同种同阶合成"), 17, gold, TextAnchor.MiddleCenter);
            }
            Label(new Rect(91, 974, 1737, 57), "宠物主动技自动释放，无需点击；冷却就绪后等待合适目标。三只同种同阶可进化，最多五阶。", 24, cream, TextAnchor.MiddleCenter);
        }
        static float PhaseDamage(PetSpec pet, int index) { return pet.damage * pet.stages[index].damageMultiplier; }
        static float PhaseInterval(PetSpec pet, int index) { return pet.interval * pet.stages[index].intervalMultiplier; }

        void BackFromTeam() { if (editingPreset >= 0) { editingPreset = -1; page = Page.Presets; } else page = Page.Levels; }

        void ToggleRoster(string id)
        {
            if (draft.Contains(id)) draft.Remove(id);
            else if (draft.Count < Capacity) draft.Add(id);
            else Notify("队伍已满，请先下阵一个伙伴");
        }
        void RequestBattle() { if (draft.Count == 0) return; if (draft.Count < Capacity) confirmStart = true; else BeginBattle(); }
        void CommitPreset()
        {
            profile.presets[editingPreset].team = draft.ToArray();
            if (draft.Count == 0 && profile.activePreset == editingPreset) profile.activePreset = -1;
            bool saved = SaveProfile(); selectedPreset = editingPreset; editingPreset = -1; page = Page.Presets;
            if (saved) Notify("预设队伍已保存");
        }
        void TogglePreset()
        {
            if (profile.presets[selectedPreset].team.Length == 0) Notify("请先编辑并保存至少一个伙伴");
            else { profile.activePreset = profile.activePreset == selectedPreset ? -1 : selectedPreset; if (SaveProfile()) Notify("编队偏好已保存"); }
        }

        void OpenPresets()
        {
            presetOrigin = page;
            presetEntryDraft = page == Page.Team ? new List<string>(draft) : null;
            presetEntryActive = profile.activePreset;
            selectedPreset = Math.Max(0, profile.activePreset); page = Page.Presets;
        }

        void BackFromPresets()
        {
            if (presetOrigin == Page.Team)
            {
                if (profile.activePreset != presetEntryActive && profile.activePreset >= 0) OpenTeam();
                else { draft = presetEntryDraft ?? new List<string>(); editingPreset = -1; page = Page.Team; }
            }
            else page = Page.Home;
        }

        void Presets()
        {
            Header("SAVED FORMATIONS", "熟悉的伙伴，随时集结", BackFromPresets);
            Label(new Rect(86, 187, 1550, 42), "启用的预设会用于下次编队；当前关卡只取前 N 个可用伙伴。", 23, muted);
            for (int row = 0; row < 3; row++)
            {
                float y = 285 + row * 205;
                Box(new Rect(85, y, 1744, 174), selectedPreset == row ? Hex("31563F") : panel, selectedPreset == row ? gold : Hex("476851"));
                if (Hit(new Rect(85, y, 1744, 174))) { selectedPreset = row; Tap(); }
                Label(new Rect(123, y + 32, 280, 45), "预设队伍 0" + (row + 1), 28, cream, TextAnchor.MiddleLeft, true);
                Label(new Rect(125, y + 97, 260, 35), row == profile.activePreset ? "● 已启用" : "点击选择", 20, row == profile.activePreset ? gold : muted);
                for (int k = 0; k < 5; k++)
                {
                    Rect r = new Rect(493 + k * 222, y + 21, 160, 130);
                    Box(r, deep, Hex("45634F"));
                    if (k < profile.presets[row].team.Length)
                    {
                        int idx = Array.FindIndex(config.pets, p => p.id == profile.presets[row].team[k]);
                        Sprite(idx, new Rect(r.x + 25, r.y + 3, 110, 110));
                    }
                    else Label(r, "—", 34, muted, TextAnchor.MiddleCenter);
                }
            }
            if (Button(new Rect(1112, 948, 326, 68), profile.activePreset == selectedPreset ? "取消启用" : "启用这支队伍", true))
                TogglePreset();
            if (Button(new Rect(1460, 948, 327, 68), "编辑预设")) { editingPreset = selectedPreset; draft = profile.presets[selectedPreset].team.ToList(); selectedSpecies = 0; elementTab = -1; page = Page.Team; }
        }

        void BeginBattle()
        {
            if (draft.Count == 0) return;
            confirmStart = false; battleTeam = draft.ToArray(); profile.lastTeam = battleTeam; SaveProfile();
            game = new GameModel(config, levelIndex, battleTeam, 1701 + levelIndex * 101);
            selectedPet = -1; selectedEnemyId = -1; bestiary = false; skillTarget = false; resultSaved = false; speed = 1; accumulator = 0;
            poolOpen = gachaOpen = true; page = Page.Battle;
            Notify("先选待部署栏中的伙伴，再点击林地上的部署点");
        }

        void Battle()
        {
            if (game == null) { page = Page.Levels; return; }
            bool ended = game.Stage == RunStage.Won || game.Stage == RunStage.Lost;
            bool underlyingEnabled = GUI.enabled;
            GUI.enabled = underlyingEnabled && !ended;
            World();
            GUI.enabled = underlyingEnabled;
            if (game.Paused) { if (!help && !bestiary) PauseMenu(); return; }
            GUI.enabled = GUI.enabled && !ended;
            HudSurface(CombatDrawing.HealthHud);
            Label(new Rect(55, 38, 150, 50), "♥  " + game.Lives, 35, Hex("F5997F"), TextAnchor.MiddleLeft, true);
            Label(new Rect(218, 38, 168, 50), "●  " + game.Coins, 35, gold, TextAnchor.MiddleLeft, true);
            HudSurface(CombatDrawing.WaveHud);
            Label(new Rect(447, 31, 246, 30), config.levels[levelIndex].name, 20, muted, TextAnchor.MiddleCenter);
            Label(new Rect(447, 63, 246, 32), game.Wave + " / " + game.Level.waves + (game.Stage == RunStage.Preparing ? " 波 · 准备中" : " 波 · 防守中"), 23, cream, TextAnchor.MiddleCenter, true);
            if (Button(new Rect(1230, 38, 160, 63), "敌人图鉴")) { game.Paused = true; bestiary = true; }
            if (Button(new Rect(1410, 38, 102, 63), speed == 1 ? "1×" : "2×")) speed = speed == 1 ? 2 : 1;
            if (Button(new Rect(1532, 38, 122, 63), "指南")) { game.Paused = true; help = true; }
            if (Button(new Rect(1675, 38, 177, 63), "Ⅱ  暂停")) { game.Paused = true; selectedPet = -1; }
            WavePreview();
            Sidebar();
            Pool();
            if (Button(new Rect(46, 925, 262, 88), game.SkillRemaining > 0 ? "星雨  " + Mathf.CeilToInt(game.SkillRemaining) + "s" : skillTarget ? "选择落点…" : "✦  星雨", true, game.Stage == RunStage.Running && game.SkillRemaining <= 0))
            { skillTarget = !skillTarget; selectedPet = -1; if (skillTarget) Notify("点击战场投下星雨；Esc 取消"); }
            Label(new Rect(48, 1021, 270, 26), "全局指令 · 手动选点", 18, cream, TextAnchor.MiddleCenter);
            if (Button(new Rect(1540, 929, 310, 78), game.Stage == RunStage.Preparing ? (game.Wave == 0 ? "开始第一波  →" : "开始下一波  →") : "敌人正在来袭", true, game.Stage == RunStage.Preparing)) { Act(game.StartWave()); selectedPet = -1; }
            Label(new Rect(1540, 1020, 320, 28), game.Stage == RunStage.Preparing ? "Space 开波  ·  G 扭蛋" : "存活 " + game.Enemies.Count + "   待出 " + game.RemainingToSpawn, 18, cream, TextAnchor.MiddleCenter);
            GUI.enabled = true;
            if (ended) Result();
        }

        static Rect FitField(Rect available)
        {
            float height = Mathf.Min(available.height, available.width / TrailGeometry.Aspect);
            float width = height * TrailGeometry.Aspect;
            return new Rect(available.center.x - width / 2, available.center.y - height / 2, width, height);
        }
        Vector2 Point(float x, float y) { return new Vector2(field.x + field.width * x, field.y + field.height * y); }
        static Rect EnemyBounds(Vector2 feet, float size) { return new Rect(feet.x - size / 2, feet.y - size, size, size); }

        void TrailLayer(float width, Color color, Vector2 offset)
        {
            for (int i = 0; i < game.Path.Count; i++)
            {
                Vector2 b = Point(game.Path[i].X, game.Path[i].Y) + offset;
                if (i > 0) Line(Point(game.Path[i - 1].X, game.Path[i - 1].Y) + offset, b, width, color);
                Disc(b, width, color);
            }
        }

        void RouteHints()
        {
            if (game.Stage == RunStage.Preparing && !game.Paused)
            {
                float length = TrailGeometry.Length(game.Path);
                for (float distance = .12f; distance < length - .1f; distance += .19f)
                {
                    V2 point, direction;
                    TrailGeometry.Sample(game.Path, distance, out point, out direction);
                    Vector2 p = Point(point.X, point.Y);
                    Vector2 forward = new Vector2(direction.X * field.width, direction.Y * field.height).normalized;
                    Vector2 side = new Vector2(-forward.y, forward.x);
                    Line(p - forward * 5 + side * 6, p + forward * 5, 3, Hex("776B42"));
                    Line(p - forward * 5 - side * 6, p + forward * 5, 3, Hex("776B42"));
                }
            }
            Vector2 entry = Point(game.Path[0].X, game.Path[0].Y) + new Vector2(24,0);
            Disc(entry, 45, Hex("713D32")); Disc(entry, 32, Hex("AE7451"));
            Label(new Rect(entry.x - 21, entry.y - 23, 42, 43), "→", 28, cream, TextAnchor.MiddleCenter, true);
            Label(new Rect(field.x+4,entry.y+30,125,30),"敌人入口",18,cream,TextAnchor.MiddleLeft,true);
        }

        void World()
        {
            GUI.BeginGroup(new Rect(0,0,1920,1080));
            if (battlefield != null) GUI.DrawTexture(field, battlefield, ScaleMode.StretchToFill);
            else { TrailLayer(67, Hex("425035"), new Vector2(0,5)); TrailLayer(52, Hex("B5AA70"), Vector2.zero); }
            RouteHints();
            Pet selected = game.FindPet(selectedPet);
            if (selected != null && selected.Pad >= 0 && !game.Paused)
            {
                Vector2 p=Point(game.Pads[selected.Pad].X,game.Pads[selected.Pad].Y);
                float radius=game.Range(selected)*field.height;
                GUI.color=new Color(.85f,.93f,.58f,.13f);
                GUI.DrawTexture(new Rect(p.x-radius,p.y-radius,radius*2,radius*2),circle);
                GUI.color=Color.white;
                Ring(p,radius,new Color(.88f,.90f,.65f,.45f));
            }
            // All ground decals first; one shared foot-depth order for BOTH factions.
            for (int i=0;i<game.Pads.Count;i++)
            {
                Vector2 pos=Point(game.Pads[i].X,game.Pads[i].Y);
                bool occupied=game.Towers.Any(t=>t.Pad==i);
                bool placing=selected!=null && selected.Pad<0;
                GroundEllipse(pos+new Vector2(0,3),48,19,new Color(.14f,.19f,.06f,.38f));
                GroundEllipse(pos,occupied?35:44,occupied?14:21,Hex("A69256"));
                GroundEllipse(pos-new Vector2(0,1),occupied?30:39,occupied?11:17,Hex("C1AC73"));
                if (!occupied)
                {
                    if(placing) GroundEllipse(pos,52,26,new Color(.93f,.81f,.44f,.5f));
                    Label(new Rect(pos.x-22,pos.y-24,44,38),"+",25,cream,TextAnchor.MiddleCenter,true);
                    if(!game.Paused&&!skillTarget&&BattleInput(Event.current.mousePosition)&&Hit(new Rect(pos.x-30,pos.y-30,60,60)))
                    {
                        if(placing) { Act(game.Deploy(selected.Uid,i));selectedPet=-1; }
                        else Notify("先选择待部署栏中的伙伴");
                    }
                }
            }
            var drawOrder=new List<WorldUnit>();
            foreach(Pet tower in game.Towers) drawOrder.Add(new WorldUnit { Pet=tower,Y=game.Pads[tower.Pad].Y,Id=tower.Uid });
            foreach(Enemy enemy in game.Enemies) drawOrder.Add(new WorldUnit { Enemy=enemy,Y=enemy.Y,Id=enemy.Id });
            foreach(WorldUnit unit in drawOrder.OrderBy(u=>u.Y).ThenBy(u=>u.Pet==null?1:0).ThenBy(u=>u.Id))
            {
                if(unit.Pet!=null) DrawTower(unit.Pet);
                else DrawEnemy(unit.Enemy);
            }
            foreach (CombatFx fx in game.Effects) DrawCombatFx(fx);
            V2 last = game.Path[game.Path.Count - 1]; Vector2 home = Point(last.X, last.Y);
            // A small destination banner sits beside the exit, not over the lane.
            Vector2 flag=home+new Vector2(-27,-49);
            Line(flag+new Vector2(0,-24),flag+new Vector2(0,23),5,Hex("68442C"));
            Fill(new Rect(flag.x-31,flag.y-23,30,25),Hex("426D5A"));
            Label(new Rect(home.x-150,home.y+20,145,28),"守护之树",18,cream,TextAnchor.MiddleRight,true);
            if (skillTarget && !game.Paused)
            {
                if (Event.current.type == EventType.MouseDown && Event.current.button == 1) { skillTarget = false; Event.current.Use(); }
                Vector2 mouse = Event.current.mousePosition;
                if (BattleInput(mouse))
                {
                    GUI.color = new Color(1, .9f, .5f, .23f);
                    GUI.DrawTexture(new Rect(mouse.x - config.skillRadius / TrailGeometry.Aspect * field.width, mouse.y - config.skillRadius * field.height, 2 * config.skillRadius / TrailGeometry.Aspect * field.width, 2 * config.skillRadius * field.height), circle); GUI.color = Color.white;
                }
            }
            if (!game.Paused && GUI.enabled && Event.current.type == EventType.MouseDown && Event.current.button == 0 && BattleInput(Event.current.mousePosition))
            {
                if (skillTarget) { Vector2 m = Event.current.mousePosition; Act(game.CastSkill((m.x - field.x) / field.width, (m.y - field.y) / field.height)); skillTarget = false; }
                else { selectedPet = -1; selectedEnemyId = -1; }
                Event.current.Use();
            }
            GUI.EndGroup();
        }

        bool BattleInput(Vector2 point)
        {
            bool inspecting = game.FindPet(selectedPet) != null || game.Enemies.Any(e=>e.Id==selectedEnemyId);
            return CombatDrawing.InteractionField.Contains(point)
                && !new Rect(1540,858,310,59).Contains(point)
                && !(inspecting && CombatDrawing.Inspector.Contains(point));
        }

        sealed class WorldUnit { public Pet Pet; public Enemy Enemy; public float Y; public int Id; }
        void GroundEllipse(Vector2 p,float width,float height,Color color)
        {
            Color before=GUI.color; GUI.color=color;
            GUI.DrawTexture(new Rect(p.x-width/2,p.y-height/2,width,height),circle);
            GUI.color=before;
        }
        void DrawTower(Pet tower)
        {
            Vector2 pos=Point(game.Pads[tower.Pad].X,game.Pads[tower.Pad].Y);
            if(tower.Uid==selectedPet) GroundEllipse(pos,59,26,WithAlpha(gold,.8f));
            Rect frame=CombatDrawing.PetFrame(pos,tower.Level);
            GroundEllipse(pos+new Vector2(0,2),frame.width*.57f,frame.width*.16f,new Color(.09f,.16f,.06f,.42f));
            PetSprite(game.Spec(tower.Species).atlasIndex,tower.Level,frame);
            Label(new Rect(pos.x-44,pos.y+5,88,24),new string('★',tower.Level),15,gold,TextAnchor.MiddleCenter,true);
            if(tower.Uid==selectedPet&&game.AutoSkill(tower)!=null)
            {
                Fill(new Rect(pos.x-24,pos.y+28,48,4),ink);
                Fill(new Rect(pos.x-24,pos.y+28,48*(1-Mathf.Clamp01(tower.ActiveRemaining/game.AutoSkill(tower).cooldown)),4),gold);
            }
            if(game.CanEvolve(tower.Uid)&&!game.Paused)
                Label(new Rect(pos.x+19,frame.y+8,30,32),"↑",24,gold,TextAnchor.MiddleCenter,true);
            if(!game.Paused&&!skillTarget&&BattleInput(Event.current.mousePosition)&&Hit(new Rect(pos.x-33,frame.y,66,frame.height+29))) {selectedPet=tower.Uid;selectedEnemyId=-1;Tap();}
        }
        void DrawEnemy(Enemy enemy)
        {
            Vector2 feet=Point(enemy.X,enemy.Y);
            float size=CombatDrawing.EnemySize(enemy.Kind);
            if(game.EnemyInfo(enemy)!=null && game.EnemyInfo(enemy).visualSize>0) size=game.EnemyInfo(enemy).visualSize;
            GroundEllipse(feet+new Vector2(0,2),size*.70f,size*.19f,new Color(.10f,.14f,.04f,.43f));
            // Feet never bob: subtle squash changes the silhouette above the same contact point.
            float pulse=enemy.RootRemaining>0?0:Mathf.Sin(enemy.Progress*190+enemy.Id)*.025f;
            Rect frame=new Rect(feet.x-size*(1-pulse)/2,feet.y-size*(1+pulse),size*(1-pulse),size*(1+pulse));
            if(enemyAtlas!=null&&enemyAtlasData!=null&&enemyAtlasData.regions.Length==12)
            {
                EnemySpec enemySpec=game.EnemyInfo(enemy);
                var r=enemyAtlasData.regions[enemySpec==null?enemy.Kind:enemySpec.atlasIndex];
                float fit=Mathf.Min(frame.width/r.width,frame.height/r.height);
                Rect body=new Rect(feet.x-r.width*fit/2,feet.y-r.height*fit,r.width*fit,r.height*fit);
                // Regions trim transparent padding at import metadata, not by resampling artwork.
                GUI.DrawTextureWithTexCoords(body,enemyAtlas,new Rect(r.x/(float)enemyAtlasData.width,
                    1f-(r.y+r.height)/(float)enemyAtlasData.height,r.width/(float)enemyAtlasData.width,r.height/(float)enemyAtlasData.height));
            }
            else Sprite(enemy.Kind==0?7:enemy.Kind==1?8:9,EnemyBounds(feet,size));
            EnemySpec info = game.EnemyInfo(enemy);
            if(enemy.Kind==3) WorldLabel(new Rect(feet.x-80,Mathf.Max(120,feet.y-size-35),160,25),info==null?"精英守门者":info.name,18,gold);
            if (enemy.Shield > 0)
            {
                GroundEllipse(feet-new Vector2(0,size*.48f),size*1.08f,size*1.15f,new Color(.35f,.8f,1,.14f));
                Fill(new Rect(feet.x-22,feet.y-size-14,44,4),ink);
                Fill(new Rect(feet.x-22,feet.y-size-14,44*Mathf.Clamp01(enemy.Shield/enemy.MaxShield),4),Hex("90DDEC"));
            }
            if(enemy.PulseRemaining>0)
                GroundEllipse(feet, size*1.2f, size*.36f, WithAlpha(enemy.Enraged?Hex("F39B72"):Hex("BBE47E"),enemy.PulseRemaining*.5f));
            if(enemy.Enraged) WorldLabel(new Rect(feet.x-28,feet.y+7,56,23),"狂暴",16,Hex("FFC084"));
            else if(info!=null && info.behavior!="none") WorldLabel(new Rect(feet.x-30,feet.y+7,60,23),info.role,15,cream);
            if(!game.Paused&&!skillTarget&&BattleInput(Event.current.mousePosition)&&Hit(new Rect(feet.x-size/2,feet.y-size,size,size)))
            { selectedEnemyId=enemy.Id;selectedPet=-1;Tap(); }
            if(enemy.Hp<enemy.MaxHp||enemy.Kind==3)
            {
                Fill(new Rect(feet.x-19,feet.y-size-8,38,6),ink);
                Fill(new Rect(feet.x-18,feet.y-size-7,36*Mathf.Clamp01(enemy.Hp/enemy.MaxHp),4),enemy.SlowRemaining>0?Hex("7CD7DD"):Hex("E19468"));
            }
            string status=(enemy.RootRemaining>0?"缚 ":"")+(enemy.BurnRemaining>0?"灼 ":"")+(enemy.VulnerableRemaining>0?"弱":"");
            if(status.Length>0)WorldLabel(new Rect(feet.x-40,feet.y+29,80,22),status,14,gold);
        }

        string AutoStatus(Pet pet)
        {
            if (pet == null || game.AutoSkill(pet) == null) return "无自动技能";
            if (pet.Pad < 0) return "待部署 · 冷却冻结";
            if (game.Paused || game.Stage != RunStage.Running) return "战斗未运行 · 冷却冻结";
            return pet.ActiveRemaining > 0 ? "自动释放 · " + pet.ActiveRemaining.ToString("0.0") + "s后就绪" : "就绪 · 自动等待合适目标";
        }

        Vector2 ShotSource(CombatFx fx, Vector2 target)
        {
            Vector2 feet = Point(fx.X, fx.Y);
            int level = Mathf.Clamp(fx.SourceLevel, 1, 5);
            Rect body = CombatDrawing.PetFrame(feet, level);
            int regionIndex = fx.PetIndex * 5 + level - 1;
            if (petAtlasData != null && petAtlasData.regions != null
                && regionIndex >= 0 && regionIndex < petAtlasData.regions.Length)
            {
                var region = petAtlasData.regions[regionIndex];
                body = CombatDrawing.PetBounds(body, region.width, region.height, level);
            }
            return CombatDrawing.Muzzle(body, target);
        }

        void DrawCombatFx(CombatFx fx)
        {
            float alpha = CombatDrawing.FeedbackAlpha(fx.Life, fx.Duration);
            if (alpha <= 0) return;
            float t = Mathf.Clamp01(1 - fx.Life / fx.Duration);
            Vector2 groundTarget = Point(fx.ToX, fx.ToY);
            Color color = fx.PetIndex >= 0 && fx.PetIndex < config.pets.Length ? Hex(config.pets[fx.PetIndex].colorHex) : gold;
            // Ground attacks/support pulses are not travelling projectiles. Their
            // centre and radius describe the instantaneous gameplay effect.
            if (fx.Radius > 0)
            {
                float radius = fx.Radius * field.height;
                Color glow = color; glow.a = .13f * alpha;
                Disc(groundTarget, radius * 2, glow);
                Ring(groundTarget, radius * Mathf.Lerp(.7f, 1, t), WithAlpha(color, .7f * alpha));
                if (fx.PetIndex < 0)
                {
                    Vector2 top = groundTarget + new Vector2(18, -85);
                    Line(top, groundTarget, 5, WithAlpha(color, alpha));
                    Impact(groundTarget, color, alpha, t);
                }
            }
            else
            {
                Vector2 target = CombatDrawing.EnemyAim(groundTarget, fx.TargetKind);
                if(fx.TargetSize>0) target=groundTarget-new Vector2(0,fx.TargetSize*.4f);
                Vector2 source = ShotSource(fx, target);
                // Combat is hitscan. Show a brief hit-aligned streak and flash now,
                // not a projectile that is still travelling after damage/death.
                float streakAlpha = fx.IsAuto ? Mathf.Max(0, 1 - t * 4) : alpha;
                if (streakAlpha > 0)
                {
                    Line(source, target, fx.IsAuto ? 9 : 7, WithAlpha(color, streakAlpha * .22f));
                    Line(source, target, fx.IsAuto ? 3.5f : 2.5f, WithAlpha(color, streakAlpha));
                    Disc(source, 7, WithAlpha(cream, streakAlpha));
                }
                Impact(target, color, alpha, t);
            }
            if (fx.IsAuto && fx.PetIndex >= 0 && fx.PetIndex < config.pets.Length)
            {
                StageSpec[] stages = config.pets[fx.PetIndex].stages;
                int stage = Mathf.Clamp(fx.SourceLevel - 1, 0, stages == null ? 0 : stages.Length - 1);
                if (stages != null && stages.Length > 0 && stages[stage].active != null)
                    Label(new Rect(groundTarget.x - 110, groundTarget.y - 78 - t * 12, 220, 28),
                        stages[stage].active.name, 18, WithAlpha(color, alpha), TextAnchor.MiddleCenter, true);
            }
        }

        static Color WithAlpha(Color color, float alpha) { color.a = alpha; return color; }
        void Impact(Vector2 point, Color color, float alpha, float t)
        {
            float radius = Mathf.Lerp(5, 13, t);
            Disc(point, 12, WithAlpha(color, alpha * .3f));
            Line(point + new Vector2(-radius, -radius), point + new Vector2(radius, radius), 2, WithAlpha(cream, alpha));
            Line(point + new Vector2(radius, -radius), point + new Vector2(-radius, radius), 2, WithAlpha(cream, alpha));
        }
        void Ring(Vector2 center, float radius, Color color)
        {
            const int sides = 40;
            Vector2 previous = center + Vector2.right * radius;
            for (int i = 1; i <= sides; i++)
            {
                float angle = i * Mathf.PI * 2 / sides;
                Vector2 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Line(previous, next, 2, color);
                previous = next;
            }
        }

        void HudSurface(Rect r)
        {
            Fill(new Rect(r.x+3,r.y+4,r.width,r.height),new Color(.04f,.09f,.06f,.3f));
            Fill(r,WithAlpha(deep,.91f));
            Fill(new Rect(r.x,r.yMax-2,r.width,2),WithAlpha(gold,.55f));
        }
        void WorldLabel(Rect r,string text,int size,Color color)
        {
            Label(new Rect(r.x+1,r.y+2,r.width,r.height),text,size,deep,TextAnchor.MiddleCenter,true);
            Label(r,text,size,color,TextAnchor.MiddleCenter,true);
        }
        void WavePreview()
        {
            if(game.Stage!=RunStage.Preparing) return;
            string[] ids=game.WaveRoster(game.Wave+1);
            string summary=String.Join("   ",ids.Distinct().Select(id=>game.EnemyInfo(id).name+" ×"+ids.Count(x=>x==id)).ToArray());
            WorldLabel(new Rect(450,120,1300,28),"下一波  "+summary,18,cream);
        }
        void Sidebar()
        {
            Pet pet=game.FindPet(selectedPet);
            Enemy enemy=game.Enemies.Find(e=>e.Id==selectedEnemyId);
            if(pet!=null && !pet.IsEgg)
            {
                Rect r=CombatDrawing.Inspector; HudSurface(r);
                PetSpec spec=game.Spec(pet.Species); StageSpec phase=game.StageInfo(pet);
                PetSprite(spec.atlasIndex,pet.Level,new Rect(54,127,80,80));
                Label(new Rect(147,129,268,36),phase.name+" · "+pet.Level+"阶",24,cream,TextAnchor.MiddleLeft,true);
                Label(new Rect(147,173,270,28),"攻击 "+game.CombatDamage(pet).ToString("0.#")+"   间隔 "+game.AttackInterval(pet).ToString("0.00")+"秒",18,muted);
                AutoSkillSpec automatic=game.AutoSkill(pet);
                Label(new Rect(56,206,370,28),automatic==null?spec.role:"自动技 · "+automatic.name+"  "+Mathf.CeilToInt(pet.ActiveRemaining)+"s",20,gold);
                Label(new Rect(56,238,370,49),automatic==null?spec.description:automatic.description,17,cream);
                if(Button(new Rect(56,291,155,48),"出售 +"+game.SellValue(pet))) {Act(game.Sell(pet.Uid));selectedPet=-1;}
                if(pet.Pad>=0)
                {
                    bool evolve=game.CanEvolve(pet.Uid);
                    if(Button(new Rect(225,291,202,48),evolve?"进化 ↑":"回收",evolve)) {Act(evolve?game.EvolveTower(pet.Uid):game.Recall(pet.Uid));selectedPet=-1;}
                }
                else if(Button(new Rect(225,291,202,48),"去部署",true)) Notify("点击地图带 ＋ 的空位");
            }
            else if(enemy!=null)
            {
                EnemySpec info=game.EnemyInfo(enemy);
                HudSurface(CombatDrawing.Inspector);
                Label(new Rect(56,124,370,40),info==null?"敌人":info.name+" · "+info.role,25,cream,TextAnchor.MiddleLeft,true);
                Label(new Rect(56,171,370,32),"生命 "+Mathf.CeilToInt(enemy.Hp)+"/"+Mathf.CeilToInt(enemy.MaxHp)+"  护盾 "+Mathf.CeilToInt(enemy.Shield),18,gold);
                Label(new Rect(56,213,370,74),info==null?"":info.description,20,cream);
                Label(new Rect(56,289,370,48),info==null?"":"应对 · "+info.counter,18,muted);
            }
            // Compact summon control below the lane, with no permanent right column.
            bool valid=game.Coins>=config.drawCost&&game.Pool.Count<config.poolCapacity;
            if(Button(new Rect(1540,858,310,59),"扭蛋得宠  ● "+config.drawCost+"   [G]",valid,true,!valid)) Act(game.DrawPet());
        }

        void Bestiary()
        {
            Fill(new Rect(0,0,1920,1080),new Color(.03f,.08f,.05f,.88f));
            Label(new Rect(100,62,1000,70),"林地来客 · 敌人图鉴",44,cream,TextAnchor.MiddleLeft,true);
            Label(new Rect(104,137,1500,40),"认清轮廓与职责，选择合适的伙伴应对。打开图鉴时战斗暂停。",22,muted);
            if(Button(new Rect(1650,70,166,61),"返回")) {bestiary=false;if(game!=null)game.Paused=false;}
            for(int k=0;k<6;k++)
            {
                int index=bestiaryPage*6+k;if(config.enemyTypes==null||index>=config.enemyTypes.Length)break;
                EnemySpec spec=config.enemyTypes[index];float x=100+(k%3)*580,y=218+(k/3)*330;
                HudSurface(new Rect(x,y,545,304));
                if(enemyAtlasData!=null&&enemyAtlasData.regions.Length==12)
                {
                    var a=enemyAtlasData.regions[spec.atlasIndex];float fit=Mathf.Min(130f/a.width,140f/a.height);
                    GUI.DrawTextureWithTexCoords(new Rect(x+83-a.width*fit/2,y+166-a.height*fit,a.width*fit,a.height*fit),enemyAtlas,
                        new Rect(a.x/(float)enemyAtlasData.width,1f-(a.y+a.height)/(float)enemyAtlasData.height,a.width/(float)enemyAtlasData.width,a.height/(float)enemyAtlasData.height));
                }
                Label(new Rect(x+177,y+26,348,38),spec.name+" · "+spec.role,25,cream,TextAnchor.MiddleLeft,true);
                Label(new Rect(x+177,y+78,338,103),spec.description,20,cream);
                Label(new Rect(x+26,y+197,492,68),"应对 · "+spec.counter,20,gold);
                Label(new Rect(x+26,y+270,492,25),"掉落 "+spec.reward+" 金币  /  漏怪扣 "+spec.leak+" 点生命",17,muted);
            }
            if(Button(new Rect(640,945,260,62),"上一页",false,bestiaryPage>0))bestiaryPage--;
            if(Button(new Rect(1020,945,260,62),"下一页",false,bestiaryPage<1))bestiaryPage++;
            Label(new Rect(910,950,100,50),(bestiaryPage+1)+" / 2",23,gold,TextAnchor.MiddleCenter);
        }

        void Pool()
        {
            float y = 916 + (1 - poolReveal) * 180;
            if (Button(new Rect(815, Mathf.Min(y - 49, 1030), 219, 40), poolOpen ? "待部署栏  ▾" : "待部署栏  ▴"))
            {
                poolOpen = !poolOpen;
                Pet selected = game.FindPet(selectedPet);
                if (!poolOpen && selected != null && selected.Pad < 0) selectedPet = -1;
            }
            Box(new Rect(346, y, 1165, 135), deep, Hex("75835A"));
            Label(new Rect(366, y + 6, 220, 24), "待部署栏 " + game.Pool.Count + "/" + config.poolCapacity, 16, muted);
            for (int i = 0; i < config.poolCapacity; i++)
            {
                Rect r = new Rect(366 + i * 97, y + 34, 87, 88);
                Pet pet = i < game.Pool.Count ? game.Pool[i] : null;
                Box(r, pet != null && pet.Uid == selectedPet ? Hex("516445") : panel, pet != null && pet.Uid == selectedPet ? gold : Hex("456047"));
                if (pet == null) Label(r, "·", 36, Hex("58725A"), TextAnchor.MiddleCenter);
                else
                {
                    PetSprite(game.Spec(pet.Species).atlasIndex, pet.Level, new Rect(r.x + 8, r.y + 1, 72, 67));
                    Label(new Rect(r.x, r.y + 67, r.width, 21), new string('★', pet.Level), 14, gold, TextAnchor.MiddleCenter);
                    if (Hit(r)) { selectedPet = pet.Uid; selectedEnemyId=-1; skillTarget = false; Tap(); }
                }
            }
            if (game.CanEvolvePool() && Button(new Rect(1355, y + 38, 135, 76), "进化\n三合一", true)) { Act(game.EvolvePool() > 0); selectedPet = -1; }
        }

        void PauseMenu()
        {
            Fill(new Rect(0, 0, 1920, 1080), new Color(.18f, .2f, .18f, .73f));
            Box(new Rect(617, 277, 686, 530), deep, gold);
            Label(new Rect(687, 317, 547, 74), "林间小憩", 49, cream, TextAnchor.MiddleCenter, true);
            Label(new Rect(687, 401, 547, 38), "战斗已暂停，伙伴们正在等你。", 22, muted, TextAnchor.MiddleCenter);
            if (Button(new Rect(697, 473, 526, 73), "继续守护", true)) game.Paused = false;
            if (Button(new Rect(697, 566, 526, 67), "重新开始")) { draft = battleTeam.ToList(); BeginBattle(); }
            if (Button(new Rect(697, 653, 526, 67), "返回选关")) LeaveBattle();
            if (Button(new Rect(1225, 296, 54, 50), "×")) game.Paused = false;
        }

        void LeaveBattle() { game.Paused = false; game = null; selectedPet = -1; skillTarget = false; speed = 1; accumulator = 0; page = Page.Levels; }

        void Result()
        {
            bool win = game.Stage == RunStage.Won;
            Fill(new Rect(0, 0, 1920, 1080), new Color(.03f, .08f, .06f, .79f));
            Box(new Rect(552, 239, 816, 596), deep, gold);
            Label(new Rect(610, 288, 700, 88), win ? "森林记住了你的守护" : "休整之后，再次出发", 42, cream, TextAnchor.MiddleCenter, true);
            int mask = CurrentBadges();
            int currentStars = BadgeCount(mask);
            Label(new Rect(610, 402, 700, 60), win ? new string('★', currentStars) : "这一次，我们学会了更多。", win ? 48 : 24, gold, TextAnchor.MiddleCenter);
            if (win) Label(new Rect(610, 468, 700, 32), "通关 " + ((mask & 1) != 0 ? "✓" : "—") + "     无损 " + ((mask & 2) != 0 ? "✓" : "—") + "     " + MissionElement() + "系编队 " + ((mask & 4) != 0 ? "✓" : "—"), 21, cream, TextAnchor.MiddleCenter);
            Label(new Rect(651, 509, 621, 89), "击退 " + game.Kills + " 个敌人    ·    守护 " + game.Wave + " 波\n基地生命 " + game.Lives + "    ·    剩余金币 " + game.Coins, 24, muted, TextAnchor.MiddleCenter);
            if (Button(new Rect(622, 666, 320, 77), "再试一次", true)) { draft = battleTeam.ToList(); BeginBattle(); }
            if (Button(new Rect(977, 666, 320, 77), "返回选关")) LeaveBattle();
        }

        void ConfirmStart()
        {
            Overlay("队伍还没有满员", "当前 " + draft.Count + " / " + Capacity + " 位伙伴。\n本局只会抽取已上阵的物种，确定出击吗？");
            if (Button(new Rect(653, 645, 285, 73), "继续编队")) confirmStart = false;
            if (Button(new Rect(968, 645, 300, 73), "就这样出击", true)) BeginBattle();
        }

        string MissionElement() { return levelIndex == 0 ? "金" : levelIndex == 1 ? "水" : "光"; }
        int CurrentBadges()
        {
            if (game == null || game.Stage != RunStage.Won) return 0;
            int mask = 1;
            if (game.Lives == config.initialLives) mask |= 2;
            if (battleTeam.Any(id => game.Spec(id).element == MissionElement())) mask |= 4;
            return mask;
        }
        static int BadgeCount(int mask) { return ((mask & 1) != 0 ? 1 : 0) + ((mask & 2) != 0 ? 1 : 0) + ((mask & 4) != 0 ? 1 : 0); }

        void SkillModal()
        {
            PetSpec pet = config.pets[selectedSpecies];
            StageSpec phase = pet.stages[0];
            string body = "普攻：" + pet.basicName + "\n被动：" + pet.passiveName + " · " + phase.description
                + "\n\n自动主动技：" + phase.active.name + "\n" + phase.active.description
                + "\n首施3秒，冷却" + phase.active.cooldown.ToString("0.#") + "秒；就绪且有目标自动释放。\n五阶图鉴可查看各阶段变化；无需手动施法。";
            Overlay(pet.name + " · 一阶技能", "");
            Label(new Rect(639, 389, 646, 304), body, 21, cream);
            if (Button(new Rect(725, 709, 470, 60), "明白了", true)) showSkill = false;
            if (Event.current.type == EventType.MouseDown && !new Rect(584, 259, 752, 541).Contains(Event.current.mousePosition)) { showSkill = false; Event.current.Use(); }
        }

        void HelpModal()
        {
            Overlay("即刻相遇，一起成长", "编队决定能抽到的宠物；开场赠送前三种伙伴。\n" + config.drawCost + "金币直接获得1阶宠物，选中后点地图＋部署。\n三只同种同阶合一，最多5阶，不连续跳阶。\n宠物主动技：冷却就绪且有目标时自动释放。\n全局指令星雨：手动点击后选择落点。\nSpace开波 · G扭蛋得宠 · E合成 · Esc暂停");
            if (Button(new Rect(725, 701, 470, 65), "开始守护", true)) { help = false; if (game != null) game.Paused = false; }
        }

        void Overlay(string title, string body)
        {
            Fill(new Rect(0, 0, 1920, 1080), new Color(.025f, .07f, .05f, .82f));
            Box(new Rect(584, 259, 752, 541), deep, gold);
            Label(new Rect(631, 299, 660, 68), title, 35, cream, TextAnchor.MiddleCenter, true);
            Label(new Rect(639, 414, 646, 255), body, 24, muted, TextAnchor.UpperLeft);
        }

        IEnumerator CaptureRun()
        {
            // Allow the player graphics and Unity startup splash to finish before QA capture.
            yield return new WaitForSecondsRealtime(4f);
            if (capturePage == "verify")
            {
                bool failed = false;
                try { VerifyPresentationState(); }
                catch (Exception ex) { Debug.LogException(ex); Application.Quit(1); failed = true; }
                if (failed) yield break;
            }
            if (capturePage == "team") { levelIndex = 1; OpenTeam(); }
            else if (capturePage == "gallery") { levelIndex = 1; OpenTeam(); page = Page.Gallery; }
            else if (capturePage == "levels") page = Page.Levels;
            else if (capturePage != "home")
            {
                levelIndex = 1; draft = new List<string> { "fox", "otter", "falcon", "sprout", "deer" }; BeginBattle();
                int[] positions = { 2, 5, 8 };
                for (int i = 0; i < 3; i++) game.Deploy(game.Pool.First(p => !p.IsEgg).Uid, positions[i]);
                for (int i = 0; i < 5; i++) game.DrawPet();
                game.StartWave(); for (int i = 0; i < 650; i++) game.Tick(1f / 60);
                captureStarted = true;
                if (capturePage == "pause") game.Paused = true;
            }
            noticeUntil = 0;
            yield return null; yield return new WaitForEndOfFrame();
            Directory.CreateDirectory(Path.GetDirectoryName(capturePath));
            ScreenCapture.CaptureScreenshot(capturePath);
            yield return new WaitForSecondsRealtime(1.5f);
            Debug.Log("TD_CAPTURE_OK " + capturePath);
            Application.Quit(0);
        }

        void VerifyPresentationState()
        {
            // Calls the same state handlers used by menus; this is not a physical mouse test.
            var lines = new List<string>();
            Action<bool, string> check = (ok, name) => { if (!ok) throw new Exception("UI_STATE_FAILED " + name); lines.Add("PASS " + name); };
            profile = new Profile(); game = null; levelIndex = 0; OpenTeam();
            check(!string.IsNullOrEmpty(CombatDrawingChecks.Run(config, petAtlasData)), "attack lines, source anchors and impact geometry regression");
            check(Math.Abs(field.width / field.height - TrailGeometry.Aspect) < .00001f, "map uses the same aspect as combat distance");
            check(CombatDrawing.InteractionField.yMax==867 && field.xMin==0 && field.yMin==0 && field.xMax==1920 && field.yMax>=1080,
                "single full-bleed artwork covers canvas without top or right seams");
            check(battlefield!=null&&enemyAtlas!=null&&enemyAtlasData!=null&&enemyAtlasData.regions.Length==12,
                "hand-painted battlefield and twelve enemy regions load");
            foreach(V2 p in TrailGeometry.ForestTrail()) check(Point(p.X,p.Y).y<820,"whole road visible above reserve UI");
            foreach(V2 p in TrailGeometry.ForestPads()) check(CombatDrawing.InteractionField.Contains(Point(p.X,p.Y)),"all pads remain interactive");
            foreach(var spec in config.enemyTypes)
            {
                check(spec.atlasIndex>=0&&spec.atlasIndex<enemyAtlasData.regions.Length,"archetype resolves its own sprite");
                check(spec.visualSize>=28&&spec.visualSize<=96,"readable child / normal / boss silhouette scale");
            }
            foreach(V2 p in TrailGeometry.ForestTrail())
            {
                Rect boss=EnemyBounds(Point(p.X,p.Y),96);
                foreach(Rect hud in new[]{CombatDrawing.HealthHud,CombatDrawing.WaveHud,CombatDrawing.UtilityHud})
                    check(!boss.Overlaps(hud),"largest enemy body never hidden behind fixed top HUD at "+p.X+","+p.Y);
            }
            foreach(V2 p in TrailGeometry.ForestPads())
                check(!CombatDrawing.PetFrame(Point(p.X,p.Y),5).Overlaps(CombatDrawing.Inspector),"inspector leaves every deployed pet visible");
            foreach (Vector2 size in new[] { new Vector2(1280, 720), new Vector2(1600, 900), new Vector2(1920, 1080),
                new Vector2(2560, 1440), new Vector2(3440, 1440), new Vector2(3840, 2160) })
            {
                float screenScale = Mathf.Min(size.x / 1920, size.y / 1080);
                float horizontal = Vector2.Distance(Point(0, 0), Point(.1f / TrailGeometry.Aspect, 0)) * screenScale;
                float vertical = Vector2.Distance(Point(0, 0), Point(0, .1f)) * screenScale;
                check(Mathf.Abs(horizontal - vertical) < .001f, "isotropic projected speed and range at " + size);
            }
            foreach (float size in new[] { 54f, 61f, 79f, 108f })
            {
                Vector2 feet = Point(.48f, .5f);
                Rect body = EnemyBounds(feet, size);
                check(Mathf.Abs(body.yMax - feet.y) < .001f && Mathf.Abs(body.center.x - feet.x) < .001f,
                    "enemy ground anchor lies on route for sprite size " + size);
            }
            check(stageAtlas != null, "five-stage sprite atlas loads from project resources");
            check(petAtlasData != null && petAtlasData.regions != null && petAtlasData.regions.Length == 35
                && petAtlasData.width == stageAtlas.width && petAtlasData.height == stageAtlas.height,
                "35 explicit sprite regions match the imported texture dimensions");
            foreach (PetAtlasRegion region in petAtlasData.regions)
                check(region.x >= 0 && region.y >= 0 && region.width > 0 && region.height > 0
                    && region.x + region.width <= petAtlasData.width && region.y + region.height <= petAtlasData.height,
                    "sprite region remains inside atlas bounds");
            GameModel library = new GameModel(config, 1, config.pets.Take(5).Select(p => p.id).ToArray(), 83);
            foreach (PetSpec species in config.pets)
            {
                check(species.stages != null && species.stages.Length == 5, species.id + " has exactly five gallery stages");
                for (int stage = 0; stage < 5; stage++)
                {
                    Pet preview = new Pet { Species = species.id, Level = stage + 1, ReadyWave = 0, Pad = -1 };
                    check(library.StageInfo(preview).name == species.stages[stage].name
                        && Math.Abs(library.Damage(preview) - PhaseDamage(species, stage)) < .001f
                        && Math.Abs(library.Interval(preview) - PhaseInterval(species, stage)) < .0001f,
                        species.id + " stage " + (stage + 1) + " gallery stats match combat");
                    check(library.AutoSkill(preview) == species.stages[stage].active
                        && library.AutoSkill(preview).initialDelay == 3
                        && !string.IsNullOrEmpty(library.AutoSkill(preview).description),
                        species.id + " stage " + (stage + 1) + " automatic skill gallery matches combat");
                }
            }
            check(draft.Count == 3 && Capacity == 3, "tutorial uses first 3 unlocked formation slots");
            string[] saved = profile.lastTeam.ToArray();
            ToggleRoster(draft[0]);
            check(draft.Count == 2 && profile.lastTeam.SequenceEqual(saved), "roster edits do not commit last battle team");
            BackFromTeam(); OpenTeam();
            check(draft.Count == 3, "back discards roster draft");
            levelIndex = 1; OpenTeam(); ToggleRoster(draft[0]);
            string[] before = draft.ToArray(); OpenPresets(); BackFromPresets();
            check(page == Page.Team && draft.SequenceEqual(before), "view presets then return preserves roster draft");
            OpenPresets(); editingPreset = 0; draft = new List<string> { "fox", "otter" }; CommitPreset();
            check(profile.presets[0].team.Length == 2 && profile.activePreset == -1 && profile.lastTeam.SequenceEqual(saved), "saving a preset affects only that preset");
            TogglePreset(); BackFromPresets();
            check(profile.activePreset == 0 && draft.SequenceEqual(new[] { "fox", "otter" }), "explicitly enabled preset loads into roster");
            RequestBattle();
            check(confirmStart && game == null, "non-full formation requires confirmation");
            confirmStart = false; draft.Clear(); RequestBattle();
            check(game == null && !confirmStart, "empty formation cannot enter battle");
            draft = new List<string> { "fox", "otter" }; BeginBattle();
            check(game.Pool.Count == 2 && profile.lastTeam.SequenceEqual(draft), "battle entry commits team and grants actual starter count");
            draft.Clear();
            check(battleTeam.Length == 2 && profile.lastTeam.Length == 2, "battle team is a snapshot, not draft alias");
            var beforeDraw = new HashSet<int>(game.Pool.Select(p => p.Uid));
            check(game.DrawPet(), "preparing draw succeeds immediately");
            Pet drawn = game.Pool.Single(p => !beforeDraw.Contains(p.Uid));
            check(drawn.Level == 1 && !drawn.IsEgg && drawn.ReadyWave == 0 && game.Deploy(drawn.Uid, 0), "newly drawn pet can deploy in the same phase");
            game.Stage = RunStage.Won; game.Lives = config.initialLives - 1; profile.badges[levelIndex] = 7;
            check(CurrentBadges() == 5 && BadgeCount(CurrentBadges()) == 2 && profile.badges[levelIndex] == 7, "current clear and element badges do not borrow historical perfect badge");
            game.Stage = RunStage.Lost;
            check(CurrentBadges() == 0 && profile.badges[levelIndex] == 7, "failure earns no badges and preserves historical record");
            game.Stage = RunStage.Preparing;
            game.Paused = true; int coins = game.Coins;
            check(!game.DrawPet() && game.Coins == coins, "paused menu does not allow economy mutations");
            LeaveBattle();
            check(page == Page.Levels && game == null && speed == 1, "return to levels clears battle state and speed");
            editingPreset = 0; draft.Clear(); CommitPreset();
            check(profile.activePreset == -1, "saving empty active preset clears enabled state");
            TogglePreset();
            check(profile.activePreset == -1, "empty preset cannot be enabled");
            Directory.CreateDirectory(Path.GetDirectoryName(capturePath));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(capturePath), "ui-state-check-results.md"), "# Runtime UI state checks\n\n" + string.Join("\n\n", lines) + "\n\nScope: real menu state handlers in Windows player; not a physical pointer/end-to-end input test. Capture mode does not write the user's profile.\n");
            Debug.Log("TD_UI_STATE_CHECKS_OK " + lines.Count);
            profile = new Profile();
        }

        Profile ReadProfile()
        {
            Profile p = new Profile();
            try { if (File.Exists(SavePath)) p = JsonUtility.FromJson<Profile>(File.ReadAllText(SavePath)) ?? p; }
            catch (Exception e) { backupUnreadableProfile = true; Notify("存档无法读取，暂用默认设置；原文件保留"); Debug.LogWarning("Profile reset: " + e.Message); }
            if (p.version > 1) { newerProfile = true; Notify("存档版本较新，本次只读游玩，不覆盖资料"); p = new Profile(); }
            else if (p.version != 1) { backupUnreadableProfile = true; p = new Profile(); }
            p.lastTeam = CleanTeam(p.lastTeam, 5);
            if (p.lastTeam.Length == 0) p.lastTeam = new[] { "fox", "otter", "falcon" };
            if (p.presets == null || p.presets.Length != 3) p.presets = new[] { new Preset(), new Preset(), new Preset() };
            for (int i = 0; i < 3; i++) { if (p.presets[i] == null) p.presets[i] = new Preset(); p.presets[i].team = CleanTeam(p.presets[i].team, 5); }
            if (p.stars == null || p.stars.Length != 3) p.stars = new int[3];
            if (p.badges == null || p.badges.Length != 3) p.badges = new int[3];
            for (int i = 0; i < 3; i++) p.badges[i] &= 7;
            for (int i = 0; i < 3; i++) p.stars[i] = Mathf.Clamp(p.stars[i], 0, 3);
            if (p.activePreset < -1 || p.activePreset > 2 || (p.activePreset >= 0 && p.presets[p.activePreset].team.Length == 0)) p.activePreset = -1;
            return p;
        }

        string[] CleanTeam(string[] team, int count) { return (team ?? new string[0]).Where(id => config.pets.Any(p => p.id == id)).Distinct().Take(count).ToArray(); }
        bool SaveProfile()
        {
            if (!string.IsNullOrEmpty(capturePath)) return true;
            if (newerProfile) { Notify("新版存档只读保护，本次进度不会覆盖原文件"); return false; }
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                if (backupUnreadableProfile && File.Exists(SavePath))
                {
                    File.Copy(SavePath, SavePath + ".backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"), false);
                    backupUnreadableProfile = false;
                }
                string temp = SavePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(profile, true));
                if (File.Exists(SavePath)) File.Replace(temp, SavePath, SavePath + ".previous"); else File.Move(temp, SavePath);
                return true;
            }
            catch (Exception e) { Notify("保存失败，本次进度仅保留到退出"); Debug.LogWarning(e.Message); return false; }
        }

        void Act(bool ok) { if (!string.IsNullOrEmpty(game.LastMessage)) Notify(game.LastMessage); if (ok) Tap(); }
        void Notify(string message) { if (notice == message && Time.unscaledTime < noticeUntil) return; notice = message; noticeUntil = Time.unscaledTime + 2; }
        void Tap() { if (profile != null && !profile.muted && audioSource != null) audioSource.PlayOneShot(clickTone); }
        bool Hit(Rect r) { return GUI.Button(r, GUIContent.none, transparentButton); }
        bool Button(Rect r, string label, bool primary = false, bool enabled = true, bool gray = false)
        {
            bool hover = r.Contains(Event.current.mousePosition) && GUI.enabled && enabled;
            Color bg = primary ? gold : panel, fg = primary ? ink : cream;
            if (!enabled || gray) { bg = Hex("31453A"); fg = Hex("83907C"); }
            else if (hover) bg = Color.Lerp(bg, Color.white, .12f);
            if (hover && Input.GetMouseButton(0)) bg = Color.Lerp(bg, ink, .2f);
            Fill(new Rect(r.x + 1, r.y + 5, r.width, r.height), new Color(0, .03f, .01f, .5f));
            Box(r, bg, primary ? Hex("F8DE9D") : Hex("69855E"));
            Label(new Rect(r.x + 8, r.y + 2, r.width - 16, r.height - 4), label, r.height >= 62 ? 24 : 21, fg, TextAnchor.MiddleCenter, primary);
            bool prior = GUI.enabled; GUI.enabled = prior && enabled;
            bool clicked = Hit(r); GUI.enabled = prior;
            if (clicked) Tap(); return clicked;
        }
        void Label(Rect r, string s, int size, Color color, TextAnchor align = TextAnchor.UpperLeft, bool bold = false)
        {
            textStyle.fontSize = size; textStyle.normal.textColor = color; textStyle.alignment = align; textStyle.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            GUI.Label(r, s, textStyle);
        }
        void Fill(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, white); GUI.color = Color.white; }
        void Box(Rect r, Color bg, Color border) { Fill(r, border); Fill(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), bg); Fill(new Rect(r.x + 4, r.y + 4, r.width - 8, 2), new Color(1, 1, 1, .06f)); }
        void Disc(Vector2 p, float diameter, Color c) { GUI.color = c; GUI.DrawTexture(new Rect(p.x - diameter / 2, p.y - diameter / 2, diameter, diameter), circle); GUI.color = Color.white; }
        void Sprite(int index, Rect r)
        {
            if (index >= 0 && index < 7 && stageAtlas != null) { PetSprite(index, 1, r); return; }
            if (atlas == null || index < 0 || index > 11) return;
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(r, atlas, new Rect((index % 4) * .25f, 1 - (index / 4 + 1) / 3f, .25f, 1 / 3f), true);
        }

        void PetSprite(int index, int level, Rect r)
        {
            if (stageAtlas == null) { Sprite(index, r); return; }
            if (index < 0 || index >= 7 || level < 1 || level > 5) return;
            if (petAtlasData == null || petAtlasData.regions == null || petAtlasData.regions.Length != 35) return;
            PetAtlasRegion region = petAtlasData.regions[index * 5 + level - 1];
            // Metadata uses top-left pixel coordinates. Fit without stretching; later
            // stages grow in silhouette while sharing the same bottom-center anchor.
            Rect fitted = CombatDrawing.PetBounds(r, region.width, region.height, level);
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(fitted, stageAtlas, new Rect(region.x / (float)petAtlasData.width,
                1 - (region.y + region.height) / (float)petAtlasData.height,
                region.width / (float)petAtlasData.width, region.height / (float)petAtlasData.height), true);
        }
        void Line(Vector2 a, Vector2 b, float width, Color color)
        {
            Matrix4x4 old = GUI.matrix, transform;
            Color previousColor = GUI.color;
            float length;
            if (!CombatDrawing.TryLine(old, a, b, width, out transform, out length)) return;
            try
            {
                GUI.matrix = transform;
                Fill(new Rect(0, -width / 2, length, width), color);
            }
            finally { GUI.matrix = old; GUI.color = previousColor; }
        }
        static Color Hex(string value) { Color c; return ColorUtility.TryParseHtmlString("#" + value, out c) ? c : Color.white; }
    }
}
