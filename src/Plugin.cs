using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Features.Detection;
using Features.Inventory;
using Features.MainMenu;
using Features.PlayerEmotes;
using Features.Rank;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PE_XpHelper
{
    [BepInPlugin("com.dsh.pe.xphelper", "PE Xp Helper", "1.5.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ConfigEntry<float> XpMultiplier;
        internal static ConfigEntry<int> GrantStep;
        internal static ConfigEntry<bool> MemoryAllUnlock;
        internal static ConfigEntry<KeyboardShortcut> PanelKey;
        internal static ManualLogSource Log;

        internal static bool SuppressMultiplier;
        internal static bool PanelVisible;
        internal static bool BypassMemoryUnlockCheck;

        internal static readonly string Title =
            "PE Xp Helper  v" + (((BepInPlugin)Attribute.GetCustomAttribute(typeof(Plugin), typeof(BepInPlugin)))?.Version.ToString() ?? "?");

        private static GameObject _host;

        private void Awake()
        {
            Log = Logger;

            XpMultiplier = Config.Bind("General", "XpMultiplier", 3f,
                new ConfigDescription("经验和分数获取倍率（1x - 10x）", new AcceptableValueRange<float>(1f, 10f)));
            GrantStep = Config.Bind("General", "GrantStep", 100, "“增加经验”按钮每次增加的数值");
            MemoryAllUnlock = Config.Bind("General", "UnlockEverything", false,
                "临时全解锁（仅内存生效，不写存档；关闭即恢复）");
            PanelKey = Config.Bind("General", "PanelKey", new KeyboardShortcut(KeyCode.Insert),
                "打开/关闭面板的快捷键（默认 Insert）。F10 已被 Dark Suite 的 SlowDetection 与部分修改器占用（v1.3.0 起默认改为 Insert），如需换键改此配置即可");

            Log.LogInfo("PE Xp Helper 已加载，初始倍率 x" + XpMultiplier.Value.ToString("0.0"));

            try
            {
                new Harmony("com.dsh.pe.xphelper").PatchAll(typeof(Plugin).Assembly);
                Log.LogInfo("Harmony 补丁已应用（GrantXp / CalculateRunXp / 临时解锁检查）");
            }
            catch (Exception ex)
            {
                Log.LogError("Harmony 补丁失败: " + ex);
            }

            try
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
            }
            catch (Exception ex)
            {
                Log.LogError("订阅 sceneLoaded 失败: " + ex);
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureHost();
        }

        internal static void EnsureHost()
        {
            if (_host != null) return;

            _host = new GameObject("PE_XpHelper_Host");
            _host.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.AddComponent<XpHost>();
            Log.LogInfo("面板宿主已创建（场景: " + _host.scene.name + "）");
        }
    }

    [HarmonyPatch(typeof(SensoryManager), "Update")]
    internal static class PatchGameTick
    {
        private static void Prefix()
        {
            Plugin.EnsureHost();
        }
    }

    public class XpHost : MonoBehaviour
    {
        private enum MainTab { General, Single, Travel, Stats, Achievements, Backup }
        private enum UnlockCategory { Items, Pois, RankUnlocks }

        private static Font _font;
        private static bool _fontTried;
        private static int _lastToggleFrame = -1;

        private Rect _windowRect = new Rect(60f, 60f, 660f, 580f);
        private bool _started;
        private bool _prevCursorVisible;
        private CursorLockMode _prevCursorLockState;

        private MainTab _tab = MainTab.General;
        private UnlockCategory _cat = UnlockCategory.Items;
        private string _search = "";
        private bool _onlyLocked;
        private Vector2 _scroll;

        private List<ItemEntry> _items;
        private List<PoiEntry> _pois;
        private RankUnlock[] _rankUnlocks;

        // ---- 兴趣点传送 ----
        private List<TravelPoi> _travelPois;
        private string _travelSearch = "";
        private Vector2 _travelScroll;
        private string _travelStatus = "";

        // ---- 金钱 ----
        private string _moneyInput = "999999";
        private int _moneyCurrent = -1;
        private string _moneyStatus = "";

        // ---- 统计 ----
        private sealed class StatField
        {
            public string Field;
            public string Label;
            public bool IsFloat;
            public string Value;
        }
        private List<StatField> _statFields;
        private Vector2 _statScroll;
        private string _statStatus = "";

        // ---- 成就 ----
        private sealed class AchievementEntry
        {
            public string Id;
            public string Label;
            public bool Unlocked;
        }
        private List<AchievementEntry> _achievements;
        private string _achSearch = "";
        private Vector2 _achScroll;
        private string _achStatus = "";

        // ---- 备份与恢复 ----
        private List<string> _backups;
        private Vector2 _backupScroll;
        private string _backupStatus = "";

        private static Dictionary<string, string> _nameCn;
        private static bool _nameCnLoaded;
        private static bool _namesDumped;

        private sealed class ItemEntry
        {
            public InventoryItemSO Asset;
            public string Label;
            public string SearchKey;
            public bool Unlocked;
        }

        private sealed class PoiEntry
        {
            public PoiEmoteProfileSO Asset;
            public string Label;
            public string SearchKey;
            public bool Unlocked;
        }

        /// <summary>兴趣点快速传送的目的地（移植自 PE_Trainer 的 POI 传送）。</summary>
        private sealed class TravelPoi
        {
            public string Scene;
            public string Label;      // 显示名（已按翻译表转换）
            public string RawLabel;   // 原始英文名，供搜索时中英双向命中
            public string District;
            public bool Unlocked;
            public int Visits;
        }

        private void Start()
        {
            Plugin.Log.LogInfo("宿主已启动，按 " + Plugin.PanelKey.Value + " 打开或关闭面板");
        }

        private void Update()
        {
            if (!_started)
            {
                _started = true;
                Plugin.Log.LogInfo("宿主 Update 运行中");
            }

            if (Plugin.PanelKey.Value.IsDown()) Toggle(Plugin.PanelKey.Value.ToString());

            if (Plugin.PanelVisible)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private void Toggle(string source)
        {
            if (Time.frameCount == _lastToggleFrame) return;
            _lastToggleFrame = Time.frameCount;

            if (!Plugin.PanelVisible)
            {
                _prevCursorVisible = Cursor.visible;
                _prevCursorLockState = Cursor.lockState;
                Plugin.PanelVisible = true;
                _nameCnLoaded = false;
                Plugin.Log.LogInfo("面板已打开（" + source + "）");
            }
            else
            {
                Plugin.PanelVisible = false;
                Cursor.visible = _prevCursorVisible;
                Cursor.lockState = _prevCursorLockState;
                Plugin.Log.LogInfo("面板已关闭（" + source + "）");
            }
        }

        private void OnGUI()
        {
            if (!Plugin.PanelVisible) return;

            if (!_fontTried)
            {
                _fontTried = true;
                try
                {
                    _font = Font.CreateDynamicFontFromOSFont(
                        new string[] { "Microsoft YaHei", "SimHei", "SimSun", "Arial Unicode MS" }, 14);
                }
                catch
                {
                    _font = null;
                }

                if (_font != null) Plugin.Log.LogInfo("中文字体已加载: " + _font.name);
                else Plugin.Log.LogWarning("中文字体加载失败，面板可能显示方块");
            }

            Font prevFont = GUI.skin.font;
            try
            {
                if (_font != null) GUI.skin.font = _font;
                _windowRect = GUILayout.Window(0x5845, _windowRect, DrawWindow, Plugin.Title);
            }
            finally
            {
                GUI.skin.font = prevFont;
            }
        }

        private void DrawWindow(int id)
        {
            try
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Toggle(_tab == MainTab.General, "经验与全局", "Button")) _tab = MainTab.General;
                if (GUILayout.Toggle(_tab == MainTab.Single, "单项解锁", "Button"))
                {
                    if (_tab != MainTab.Single)
                    {
                        _tab = MainTab.Single;
                        if (_items == null) RefreshUnlockCache();
                    }
                }
                if (GUILayout.Toggle(_tab == MainTab.Travel, "兴趣点传送", "Button"))
                {
                    if (_tab != MainTab.Travel)
                    {
                        _tab = MainTab.Travel;
                        if (_travelPois == null) RefreshTravelList();
                    }
                }
                if (GUILayout.Toggle(_tab == MainTab.Stats, "统计", "Button"))
                {
                    if (_tab != MainTab.Stats)
                    {
                        _tab = MainTab.Stats;
                        LoadStatsFields();
                    }
                }
                if (GUILayout.Toggle(_tab == MainTab.Achievements, "成就", "Button"))
                {
                    if (_tab != MainTab.Achievements)
                    {
                        _tab = MainTab.Achievements;
                        if (_achievements == null) RefreshAchievementList();
                    }
                }
                if (GUILayout.Toggle(_tab == MainTab.Backup, "备份与恢复", "Button"))
                {
                    if (_tab != MainTab.Backup)
                    {
                        _tab = MainTab.Backup;
                        RefreshBackupList();
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(6f);

                if (_tab == MainTab.General) DrawGeneralTab();
                else if (_tab == MainTab.Single) DrawSingleTab();
                else if (_tab == MainTab.Travel) DrawTravelTab();
                else if (_tab == MainTab.Stats) DrawStatsTab();
                else if (_tab == MainTab.Achievements) DrawAchievementsTab();
                else DrawBackupTab();
            }
            catch (Exception ex)
            {
                GUILayout.Label("面板异常：" + ex.Message);
            }

            GUI.DragWindow();
        }

        // ---------------- 经验与全局 ----------------

        private void DrawGeneralTab()
        {
            GUILayout.Label("等级：" + RankProgressionService.Level + "    经验：" +
                            RankProgressionService.Xp.ToString("0") + " / " + NextCostText());
            GUILayout.Label("段位：" + RankText() + "    累计经验：" + RankProgressionService.TotalXpEarned.ToString("0"));

            GUILayout.Space(8f);
            GUILayout.Label("经验倍率：x" + Plugin.XpMultiplier.Value.ToString("0.0"));
            float raw = GUILayout.HorizontalSlider(Plugin.XpMultiplier.Value, 1f, 10f);
            float snapped = Mathf.Round(raw * 2f) / 2f;
            if (Mathf.Abs(snapped - Plugin.XpMultiplier.Value) > 0.001f) Plugin.XpMultiplier.Value = snapped;

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+" + Plugin.GrantStep.Value + " 经验", GUILayout.Height(28f))) Grant(Plugin.GrantStep.Value);
            if (GUILayout.Button("升一级", GUILayout.Height(28f))) LevelUp();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+1000 经验")) Grant(1000);
            if (GUILayout.Button("+10000 经验")) Grant(10000);
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);
            if (GUILayout.Button(
                Plugin.MemoryAllUnlock.Value ? "关闭临时全解锁（仅内存）" : "开启临时全解锁（仅内存，不写存档）",
                GUILayout.Height(30f)))
            {
                Plugin.MemoryAllUnlock.Value = !Plugin.MemoryAllUnlock.Value;
            }
            GUILayout.Label(Plugin.MemoryAllUnlock.Value
                ? "临时全解锁：开启（可能跳过新物品弹窗/成就）"
                : "临时全解锁：关闭");

            GUILayout.Space(4f);
            if (GUILayout.Button("永久解锁当前已加载物品与场景（写入存档）", GUILayout.Height(28f)))
            {
                UnlockEverythingPermanent();
            }

            GUILayout.Space(8f);
            GUILayout.Label(Plugin.PanelKey.Value + " 开关面板（可在 BepInEx\\config\\com.dsh.pe.xphelper.cfg 修改）；加经验与永久解锁会立即写入存档。");
            GUILayout.Label("提示：用调试方式加分可能使本局排行榜成绩失效。");

            DrawMoneySection();
        }

        // ---------------- 金钱 ----------------

        private void DrawMoneySection()
        {
            GUILayout.Space(10f);
            GUILayout.Label("—— 金钱 ——");

            if (_moneyCurrent < 0) _moneyCurrent = ReadBankBalance();
            GUILayout.Label(_moneyCurrent >= 0 ? "银行余额：" + _moneyCurrent : "银行余额：未找到 MoneyManager（需在游戏场景内）");

            GUILayout.BeginHorizontal();
            GUILayout.Label("设为：", GUILayout.Width(44f));
            _moneyInput = GUILayout.TextField(_moneyInput ?? "", GUILayout.Width(120f));
            if (GUILayout.Button("应用", GUILayout.Width(56f)))
            {
                int v;
                if (!int.TryParse((_moneyInput ?? "").Trim(), out v))
                {
                    _moneyStatus = "请输入整数。";
                }
                else if (SetBankBalance(v))
                {
                    _moneyCurrent = v;
                    _moneyStatus = "已设为 " + v + "（已保存）";
                }
                else
                {
                    _moneyStatus = "设置失败：MoneyManager 未就绪。";
                }
            }
            if (GUILayout.Button("+10 万", GUILayout.Width(62f)))
            {
                if (SetBankBalance(Math.Max(0, ReadBankBalance()) + 100000)) { _moneyCurrent = ReadBankBalance(); _moneyStatus = "已加 10 万"; }
            }
            if (GUILayout.Button("刷新", GUILayout.Width(52f))) { _moneyCurrent = ReadBankBalance(); _moneyStatus = ""; }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_moneyStatus)) GUILayout.Label(_moneyStatus);
        }

        private static Type MoneyType()
        {
            return Type.GetType("ProjektSumperk.MoneyManager, Assembly-CSharp");
        }

        private static object FindMoneyManager()
        {
            Type t = MoneyType();
            return t == null ? null : UnityEngine.Object.FindObjectOfType(t);
        }

        private int ReadBankBalance()
        {
            try
            {
                object mgr = FindMoneyManager();
                if (mgr == null) return -1;
                FieldInfo f = mgr.GetType().GetField("BankBalance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f == null) return -1;
                object v = f.GetValue(mgr);
                return v is int ? (int)v : -1;
            }
            catch { return -1; }
        }

        private bool SetBankBalance(int value)
        {
            try
            {
                object mgr = FindMoneyManager();
                if (mgr == null) return false;
                Type t = mgr.GetType();
                FieldInfo f = t.GetField("BankBalance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f == null) return false;
                f.SetValue(mgr, value);

                // 走游戏自己的保存流程，避免下次读档被覆盖
                MethodInfo save = t.GetMethod("SaveTransactionData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (save != null) save.Invoke(mgr, null);

                Plugin.Log.LogInfo("银行余额已设为 " + value);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("设置金钱失败: " + ex.Message);
                return false;
            }
        }

        // ---------------- 单项解锁 ----------------

        private void DrawSingleTab()
        {
            if (_items == null) RefreshUnlockCache();

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_cat == UnlockCategory.Items, "物品 (" + _items.Count + ")", "Button")) _cat = UnlockCategory.Items;
            if (GUILayout.Toggle(_cat == UnlockCategory.Pois, "场景点 (" + _pois.Count + ")", "Button")) _cat = UnlockCategory.Pois;
            if (GUILayout.Toggle(_cat == UnlockCategory.RankUnlocks, "段位特权 (" + _rankUnlocks.Length + ")", "Button")) _cat = UnlockCategory.RankUnlocks;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("搜索：", GUILayout.Width(46f));
            _search = GUILayout.TextField(_search == null ? "" : _search, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("清空", GUILayout.Width(46f))) _search = "";
            if (GUILayout.Button("刷新列表", GUILayout.Width(74f))) RefreshUnlockCache();
            GUILayout.EndHorizontal();

            _onlyLocked = GUILayout.Toggle(_onlyLocked, "仅显示未解锁项");

            string filter = (_search == null ? "" : _search.Trim().ToLowerInvariant());
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));

            if (_cat == UnlockCategory.Items) DrawItemList(filter);
            else if (_cat == UnlockCategory.Pois) DrawPoiList(filter);
            else DrawRankList(filter);

            GUILayout.EndScrollView();
        }

        private void DrawItemList(string filter)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                ItemEntry e = _items[i];
                if (e.Asset == null) continue;
                if (filter.Length > 0 && !e.SearchKey.Contains(filter)) continue;
                if (_onlyLocked && e.Unlocked) continue;

                GUILayout.BeginHorizontal();
                GUILayout.Label((e.Unlocked ? "[已解锁] " : "[未解锁] ") + e.Label, GUILayout.ExpandWidth(true));
                GUI.enabled = !e.Unlocked;
                if (GUILayout.Button(e.Unlocked ? "已拥有" : "解锁", GUILayout.Width(64f)))
                {
                    UnlockItemEntry(e);
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        private void DrawPoiList(string filter)
        {
            for (int i = 0; i < _pois.Count; i++)
            {
                PoiEntry e = _pois[i];
                if (e.Asset == null) continue;
                if (filter.Length > 0 && !e.SearchKey.Contains(filter)) continue;
                if (_onlyLocked && e.Unlocked) continue;

                GUILayout.BeginHorizontal();
                GUILayout.Label((e.Unlocked ? "[已解锁] " : "[未解锁] ") + e.Label, GUILayout.ExpandWidth(true));
                GUI.enabled = !e.Unlocked;
                if (GUILayout.Button(e.Unlocked ? "已拥有" : "解锁", GUILayout.Width(64f)))
                {
                    UnlockPoiEntry(e);
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        private void DrawRankList(string filter)
        {
            for (int i = 0; i < _rankUnlocks.Length; i++)
            {
                RankUnlock ru = _rankUnlocks[i];
                string name = RankUnlockName(ru);
                if (filter.Length > 0 && !name.ToLowerInvariant().Contains(filter)) continue;

                bool unlocked;
                try { unlocked = RankProgressionService.IsUnlocked(ru); }
                catch { unlocked = false; }

                if (_onlyLocked && unlocked) continue;

                GUILayout.BeginHorizontal();
                GUILayout.Label((unlocked ? "[已解锁] " : "[未解锁] ") + name, GUILayout.ExpandWidth(true));
                if (GUILayout.Button(unlocked ? "锁定" : "解锁", GUILayout.Width(64f)))
                {
                    try
                    {
                        RankProgressionService.SetUnlocked(ru, !unlocked);
                        PlayerPrefs.Save();
                        Plugin.Log.LogInfo((unlocked ? "已锁定段位特权: " : "已解锁段位特权: ") + name);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError("切换段位特权失败: " + ex);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        // ---------------- 缓存 / 解锁 ----------------

        private void RefreshUnlockCache()
        {
            _items = new List<ItemEntry>();
            _pois = new List<PoiEntry>();

            HashSet<InventoryItemSO> seenItems = new HashSet<InventoryItemSO>();
            InventoryItemSO[] items = Resources.FindObjectsOfTypeAll<InventoryItemSO>();
            for (int i = 0; i < items.Length; i++)
            {
                InventoryItemSO so = items[i];
                if (so == null || !seenItems.Add(so)) continue;

                string label = ResolveItemLabel(so);
                bool unlocked = QueryItemUnlocked(so);
                _items.Add(new ItemEntry
                {
                    Asset = so,
                    Label = label,
                    SearchKey = (label + " " + so.name + " " + (so.itemId == null ? "" : so.itemId)).ToLowerInvariant(),
                    Unlocked = unlocked
                });
            }
            _items.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));

            HashSet<PoiEmoteProfileSO> seenPois = new HashSet<PoiEmoteProfileSO>();
            PoiEmoteProfileSO[] pois = Resources.FindObjectsOfTypeAll<PoiEmoteProfileSO>();
            for (int i = 0; i < pois.Length; i++)
            {
                PoiEmoteProfileSO so = pois[i];
                if (so == null || !seenPois.Add(so)) continue;

                string label = ResolvePoiLabel(so);
                bool unlocked = QueryPoiUnlocked(so);
                _pois.Add(new PoiEntry
                {
                    Asset = so,
                    Label = label,
                    SearchKey = (label + " " + so.name + " " + (so.PoiId == null ? "" : so.PoiId)).ToLowerInvariant(),
                    Unlocked = unlocked
                });
            }
            _pois.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));

            _rankUnlocks = (RankUnlock[])Enum.GetValues(typeof(RankUnlock));

            DumpNamesIfMissing();

            Plugin.Log.LogInfo("单项解锁列表已刷新：物品 " + _items.Count + "，场景点 " + _pois.Count + "，段位特权 " + _rankUnlocks.Length);
        }

        private static string ResolveItemLabel(InventoryItemSO so)
        {
            string raw = string.IsNullOrEmpty(so.itemName) ? so.name : so.itemName;
            string cn = Cn(raw);
            if (cn != raw) return cn;
            if (!string.IsNullOrEmpty(so.name) && raw != so.name) return raw + "（" + so.name + "）";
            return raw;
        }

        private static string ResolvePoiLabel(PoiEmoteProfileSO so)
        {
            string raw = string.IsNullOrEmpty(so.DisplayName) ? so.name : so.DisplayName;
            string cn = Cn(raw);
            if (cn != raw) return cn;
            if (!string.IsNullOrEmpty(so.name) && raw != so.name) return raw + "（" + so.name + "）";
            return raw;
        }

        private static string Cn(string en)
        {
            if (string.IsNullOrEmpty(en)) return en;
            EnsureNameTable();
            string v;
            return _nameCn.TryGetValue(en.Trim(), out v) && !string.IsNullOrEmpty(v) ? v : en;
        }

        private static void EnsureNameTable()
        {
            if (_nameCnLoaded) return;
            _nameCnLoaded = true;
            _nameCn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string path = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "pe_xphelper_names_cn.txt");
            try
            {
                if (!System.IO.File.Exists(path)) return;
                foreach (string line in System.IO.File.ReadAllLines(path, System.Text.Encoding.UTF8))
                {
                    string t = line.Trim();
                    if (t.Length == 0 || t.StartsWith("#")) continue;
                    int eq = t.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = t.Substring(0, eq).Trim();
                    string v = t.Substring(eq + 1).Trim();
                    if (k.Length > 0 && v.Length > 0) _nameCn[k] = v;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("读取名称翻译表失败: " + ex.Message);
            }
        }

        private static void DumpNamesIfMissing()
        {
            if (_namesDumped) return;
            _namesDumped = true;

            string path = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "pe_xphelper_names_cn.txt");
            try
            {
                if (System.IO.File.Exists(path)) return;

                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("# PE Xp Helper 名称翻译表（可直接编辑，保存后重新打开面板即可生效）");
                sb.AppendLine("# 格式：英文名=中文名；等号右边的中文名留空则显示英文");
                sb.AppendLine("# 物品与场景点各列出一次；可以在下方追加新条目");

                List<string> names = new List<string>();
                InventoryItemSO[] items = Resources.FindObjectsOfTypeAll<InventoryItemSO>();
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i] == null) continue;
                    string n = string.IsNullOrEmpty(items[i].itemName) ? items[i].name : items[i].itemName;
                    if (seen.Add(n)) names.Add(n);
                }

                PoiEmoteProfileSO[] pois = Resources.FindObjectsOfTypeAll<PoiEmoteProfileSO>();
                for (int i = 0; i < pois.Length; i++)
                {
                    if (pois[i] == null) continue;
                    string n = string.IsNullOrEmpty(pois[i].DisplayName) ? pois[i].name : pois[i].DisplayName;
                    if (seen.Add(n)) names.Add(n);
                }

                names.Sort(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < names.Count; i++) sb.AppendLine(names[i] + "=");

                System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
                Plugin.Log.LogInfo("名称翻译表模板已导出: " + path);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("导出名称翻译表失败: " + ex.Message);
            }
        }

        private static string RankUnlockName(RankUnlock ru)
        {
            switch (ru)
            {
                case RankUnlock.MoodSetting: return "心情设置";
                case RankUnlock.RankMode: return "段位模式";
                case RankUnlock.Sandbox: return "沙盒模式";
                case RankUnlock.Godmode: return "无敌模式";
                case RankUnlock.WindowFlashing: return "窗前闪光";
                default: return ru.ToString();
            }
        }

        private static bool QueryItemUnlocked(InventoryItemSO item)
        {
            Plugin.BypassMemoryUnlockCheck = true;
            try { return CollectionUnlockRegistry.IsItemUnlocked(item); }
            catch { return false; }
            finally { Plugin.BypassMemoryUnlockCheck = false; }
        }

        private static bool QueryPoiUnlocked(PoiEmoteProfileSO poi)
        {
            Plugin.BypassMemoryUnlockCheck = true;
            try { return CollectionUnlockRegistry.IsPoiUnlocked(poi); }
            catch { return false; }
            finally { Plugin.BypassMemoryUnlockCheck = false; }
        }

        private void UnlockItemEntry(ItemEntry e)
        {
            try
            {
                CollectionUnlockRegistry.UnlockItem(e.Asset);

                int variants = 0;
                if (e.Asset.rarityVariants != null)
                {
                    for (int i = 0; i < e.Asset.rarityVariants.Count; i++)
                    {
                        RarityVariantEntry v = e.Asset.rarityVariants[i];
                        if (v == null) continue;
                        try
                        {
                            CollectionUnlockRegistry.RegisterItemFound(e.Asset, e.Asset.rarity, v.surfaceIndex);
                            variants++;
                        }
                        catch
                        {
                            // 单个变体失败不影响主解锁
                        }
                    }
                }

                PlayerPrefs.Save();
                e.Unlocked = true;
                Plugin.Log.LogInfo("已单项解锁物品: " + e.Label + "（变体 " + variants + " 个）");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("单项解锁物品失败: " + ex);
            }
        }

        private void UnlockPoiEntry(PoiEntry e)
        {
            try
            {
                CollectionUnlockRegistry.UnlockPoi(e.Asset);
                PlayerPrefs.Save();
                e.Unlocked = true;
                Plugin.Log.LogInfo("已单项解锁场景点: " + e.Label);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("单项解锁场景点失败: " + ex);
            }
        }

        // ---------------- 经验与永久解锁 ----------------

        private static string NextCostText()
        {
            try
            {
                var cfg = RankProgressionService.Config;
                if (cfg == null) return "?";
                return cfg.GetLevelCost(RankProgressionService.Level).ToString("0");
            }
            catch
            {
                return "?";
            }
        }

        private static string RankText()
        {
            try
            {
                var rank = RankProgressionService.CurrentRank;
                if (rank == null) return "未知";
                return rank.displayName;
            }
            catch
            {
                return "未知";
            }
        }

        private void Grant(float amount)
        {
            Plugin.SuppressMultiplier = true;
            try
            {
                RankProgressionService.GrantXp(amount);
                Plugin.Log.LogInfo("已增加 " + amount.ToString("0") + " 经验");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("加经验失败: " + ex);
            }
            finally
            {
                Plugin.SuppressMultiplier = false;
            }
        }

        private void LevelUp()
        {
            Plugin.SuppressMultiplier = true;
            try
            {
                var cfg = RankProgressionService.Config;
                if (cfg == null)
                {
                    RankProgressionService.GrantXp(1000f);
                    Plugin.Log.LogInfo("配置未加载，改为增加 1000 经验");
                    return;
                }

                float cost = cfg.GetLevelCost(RankProgressionService.Level);
                float need = cost - RankProgressionService.Xp;
                if (need < 1f) need = 1f;
                RankProgressionService.GrantXp(need + 1f);
                Plugin.Log.LogInfo("升一级：需要 " + need.ToString("0") + " 经验");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("升级失败: " + ex);
            }
            finally
            {
                Plugin.SuppressMultiplier = false;
            }
        }

        private void UnlockEverythingPermanent()
        {
            try
            {
                foreach (object value in Enum.GetValues(typeof(RankUnlock)))
                {
                    RankProgressionService.SetUnlocked((RankUnlock)value, true);
                }

                int itemCount = 0;
                InventoryItemSO[] items = Resources.FindObjectsOfTypeAll<InventoryItemSO>();
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i] == null) continue;
                    CollectionUnlockRegistry.UnlockItem(items[i]);
                    itemCount++;
                }

                int poiCount = 0;
                PoiEmoteProfileSO[] pois = Resources.FindObjectsOfTypeAll<PoiEmoteProfileSO>();
                for (int i = 0; i < pois.Length; i++)
                {
                    if (pois[i] == null) continue;
                    CollectionUnlockRegistry.UnlockPoi(pois[i]);
                    poiCount++;
                }

                PlayerPrefs.Save();
                if (_items != null) RefreshUnlockCache();
                Plugin.Log.LogInfo("永久解锁完成：物品 " + itemCount + " 个，场景点 " + poiCount + " 个");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("永久解锁失败: " + ex);
            }
        }

        // ---------------- 兴趣点快速传送（移植自 PE_Trainer v1.4 的 F11 面板） ----------------

        private void DrawTravelTab()
        {
            if (_travelPois == null) RefreshTravelList();

            GUILayout.BeginHorizontal();
            GUILayout.Label("搜索：", GUILayout.Width(46f));
            _travelSearch = GUILayout.TextField(_travelSearch ?? "", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("清空", GUILayout.Width(46f))) _travelSearch = "";
            if (GUILayout.Button("刷新", GUILayout.Width(52f))) RefreshTravelList();
            GUILayout.EndHorizontal();

            bool ready = GetTransitionManager() != null;
            GUILayout.Label(ready
                ? "场景管理器：就绪（共 " + _travelPois.Count + " 个目的地）"
                : "场景管理器：未就绪——请先进游戏场景（主菜单里没有）");

            if (!string.IsNullOrEmpty(_travelStatus)) GUILayout.Label(_travelStatus);

            string filter = (_travelSearch ?? "").Trim().ToLowerInvariant();
            _travelScroll = GUILayout.BeginScrollView(_travelScroll, GUILayout.ExpandHeight(true));

            int shown = 0;
            for (int i = 0; i < _travelPois.Count; i++)
            {
                TravelPoi p = _travelPois[i];
                if (filter.Length > 0)
                {
                    string hay = ((p.Label ?? "") + " " + (p.RawLabel ?? "") + " " + (p.Scene ?? "") + " " + (p.District ?? "")).ToLowerInvariant();
                    if (hay.IndexOf(filter, StringComparison.Ordinal) < 0) continue;
                }

                shown++;
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    (p.Unlocked ? "[OK] " : "[--] ") + p.Label +
                    (string.IsNullOrEmpty(p.District) ? "" : "　(" + p.District + ")") +
                    (p.Visits > 0 ? "　x" + p.Visits : ""),
                    GUILayout.ExpandWidth(true));
                if (GUILayout.Button("传送", GUILayout.Width(56f))) TravelTo(p.Scene, true);
                GUILayout.EndHorizontal();
            }

            if (shown == 0) GUILayout.Label("没有匹配的兴趣点。");
            GUILayout.EndScrollView();

            GUILayout.Label("[OK] 已解锁 / [--] 未发现（未解锁也可能传送成功）");
        }

        private void RefreshTravelList()
        {
            _travelPois = new List<TravelPoi>();
            HashSet<string> seen = new HashSet<string>();

            try
            {
                // ① 游戏内地图 App 的目的地表
                Type mapsApp = Type.GetType("Features.CielOS.Apps.Maps.CielOSMapsApp, Assembly-CSharp");
                if (mapsApp != null)
                {
                    FieldInfo f = mapsApp.GetField("Destinations", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    Array arr = f != null ? f.GetValue(null) as Array : null;
                    if (arr != null)
                    {
                        foreach (object o in arr)
                        {
                            if (o == null) continue;
                            AddTravelPoi(seen,
                                GetMember(o, "SceneName") as string,
                                GetMember(o, "DisplayName") as string,
                                GetMember(o, "District") as string);
                        }
                    }
                }

                // ② 进度服务记录的已知场景
                if (_travelPois.Count == 0)
                {
                    Type svc = Type.GetType("Features.CielOS.Apps.Maps.PoiFastTravelProgressService, Assembly-CSharp");
                    FieldInfo f2 = svc != null
                        ? svc.GetField("KnownScenes", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        : null;
                    string[] known = f2 != null ? f2.GetValue(null) as string[] : null;
                    if (known != null)
                    {
                        for (int i = 0; i < known.Length; i++) AddTravelPoi(seen, known[i], null, null);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("兴趣点列表读取失败，改用内置清单: " + ex.Message);
            }

            // ③ 兜底：固定清单
            string[] fallback =
            {
                "POI - Art Gallery", "POI - Kombini", "POI - Mall",
                "POI - Pool", "POI - Stadium", "POI - University"
            };
            for (int i = 0; i < fallback.Length; i++) AddTravelPoi(seen, fallback[i], null, null);

            _travelPois.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
            Plugin.Log.LogInfo("兴趣点传送列表已刷新：" + _travelPois.Count + " 个目的地");
        }

        private void AddTravelPoi(HashSet<string> seen, string scene, string label, string district)
        {
            if (string.IsNullOrEmpty(scene) || seen.Contains(scene)) return;
            seen.Add(scene);

            TravelPoi p = new TravelPoi();
            p.Scene = scene;

            // 名称统一走中文映射表：DisplayName → 去前缀的场景名 → 翻译
            string raw = PrettifyScene(string.IsNullOrEmpty(label) ? scene : label);
            p.RawLabel = raw;
            p.Label = Cn(raw);
            p.District = string.IsNullOrEmpty(district) ? null : Cn(district);

            try
            {
                Type svc = Type.GetType("Features.CielOS.Apps.Maps.PoiFastTravelProgressService, Assembly-CSharp");
                if (svc != null)
                {
                    MethodInfo m1 = svc.GetMethod("IsDestinationUnlocked", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m1 != null)
                    {
                        object r = m1.Invoke(null, new object[] { scene });
                        if (r is bool) p.Unlocked = (bool)r;
                    }

                    MethodInfo m2 = svc.GetMethod("GetVisitCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m2 != null)
                    {
                        object r2 = m2.Invoke(null, new object[] { scene });
                        if (r2 is int) p.Visits = (int)r2;
                    }
                }
            }
            catch
            {
                // 解锁状态读不到不影响传送
            }

            _travelPois.Add(p);
        }

        private static string PrettifyScene(string scene)
        {
            string t = scene ?? "";
            if (t.StartsWith("POI - ")) t = t.Substring(6);
            return t.Replace('_', ' ');
        }

        private static object GetMember(object obj, string name)
        {
            if (obj == null) return null;
            Type t = obj.GetType();
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f.GetValue(obj);
            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return p != null ? p.GetValue(obj, null) : null;
        }

        private static object GetTransitionManager()
        {
            Type t = Type.GetType("ProjectExhibitionist.Core.SceneManagement.SceneTransitionManager, Assembly-CSharp");
            if (t == null) return null;

            PropertyInfo prop = t.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
            object o = prop != null ? prop.GetValue(null, null) : null;
            if (o == null) o = UnityEngine.Object.FindObjectOfType(t);
            return o;
        }

        private static Transform GetPlayerTransform()
        {
            Type t = Type.GetType("Invector.vCharacterController.vThirdPersonController, Assembly-CSharp");
            if (t == null) return null;
            object o = UnityEngine.Object.FindObjectOfType(t);
            Component c = o as Component;
            return c != null ? c.transform : null;
        }

        private void TravelTo(string sceneName, bool recordVisit)
        {
            Type mgrType = Type.GetType("ProjectExhibitionist.Core.SceneManagement.SceneTransitionManager, Assembly-CSharp");
            object mgr = GetTransitionManager();
            MethodInfo load = mgrType != null
                ? mgrType.GetMethod("LoadGameplayScene",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new Type[] { typeof(string) }, null)
                : null;

            if (mgr == null || load == null)
            {
                _travelStatus = "传送失败：场景管理器未就绪，请先进游戏场景。";
                Plugin.Log.LogWarning("兴趣点传送：管理器或方法缺失");
                return;
            }

            if (recordVisit) RecordPoiVisit(sceneName);

            try
            {
                load.Invoke(mgr, new object[] { sceneName });
                _travelStatus = "正在传送 → " + PrettifyScene(sceneName);
                Plugin.Log.LogInfo("兴趣点传送 → " + sceneName);

                // 传送后收起面板并恢复光标
                Plugin.PanelVisible = false;
                Cursor.visible = _prevCursorVisible;
                Cursor.lockState = _prevCursorLockState;
            }
            catch (Exception ex)
            {
                _travelStatus = "传送失败：" + ex.Message;
                Plugin.Log.LogError("兴趣点传送失败: " + ex);
            }
        }

        private void RecordPoiVisit(string destScene)
        {
            try
            {
                Type stateT = Type.GetType("ProjectExhibitionist.PoiTravel.PoiTravelState, Assembly-CSharp");
                Type originT = Type.GetType("ProjectExhibitionist.PoiTravel.PoiTravelOrigin, Assembly-CSharp");
                if (stateT == null || originT == null) return;

                MethodInfo begin = stateT.GetMethod("BeginVisit", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (begin == null) return;

                Transform pt = GetPlayerTransform();
                Vector3 pos = pt != null ? pt.position : Vector3.zero;
                Quaternion rot = pt != null ? pt.rotation : Quaternion.identity;
                string fromScene = pt != null ? pt.gameObject.scene.name : SceneManager.GetActiveScene().name;

                object origin = Enum.Parse(originT, "HomeFastTravel");
                begin.Invoke(null, new object[] { fromScene, pos, rot, origin, destScene, "" });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("记录 POI 来路失败（不影响传送）: " + ex.Message);
            }
        }

        // ---------------- 统计 ----------------

        private static readonly string[] StatIntFields =
        {
            "runsPlayed", "totalTimesSeen", "totalEmotes", "totalLocationsVisited",
            "bestLocationsVisitedInRun", "totalCarSurfs", "totalSteps"
        };

        private static readonly string[] StatFloatFields =
        {
            "totalDistanceMeters", "bestDistanceMeters", "totalVehicleDistanceMeters",
            "totalWalkingMeters", "totalRunningMeters", "totalPlayTimeSeconds", "totalActiveGameplaySeconds"
        };

        private static Type StatsServiceType()
        {
            return Type.GetType("Features.MatchRecap.PlayerStatsService, Assembly-CSharp");
        }

        private static object GetStatsData()
        {
            try
            {
                Type t = StatsServiceType();
                if (t == null) return null;

                PropertyInfo p = t.GetProperty("Data", BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null)
                {
                    try { return p.GetValue(null, null); } catch { }
                    try
                    {
                        object inst = UnityEngine.Object.FindObjectOfType(t);
                        if (inst != null) return p.GetValue(inst, null);
                    }
                    catch { }
                }

                MethodInfo snap = t.GetMethod("GetSnapshot", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (snap != null)
                {
                    try { return snap.Invoke(null, null); } catch { }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("读取统计数据失败: " + ex.Message);
            }
            return null;
        }

        private static bool SaveStats()
        {
            try
            {
                Type t = StatsServiceType();
                MethodInfo m = t != null
                    ? t.GetMethod("Save", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    : null;
                if (m == null) return false;
                m.Invoke(null, null);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("保存统计数据失败: " + ex.Message);
                return false;
            }
        }

        private void LoadStatsFields()
        {
            _statFields = new List<StatField>();
            object data = GetStatsData();
            if (data == null)
            {
                _statStatus = "读不到统计数据（需在游戏场景内，且 PlayerStatsService 已加载）。";
                return;
            }
            _statStatus = "";

            for (int i = 0; i < StatIntFields.Length; i++)
            {
                StatField f = new StatField();
                f.Field = StatIntFields[i];
                f.Label = f.Field;
                f.IsFloat = false;
                object v = GetMember(data, f.Field);
                f.Value = v == null ? "0" : Convert.ToInt64(v).ToString();
                _statFields.Add(f);
            }

            for (int i = 0; i < StatFloatFields.Length; i++)
            {
                StatField f = new StatField();
                f.Field = StatFloatFields[i];
                f.Label = f.Field;
                f.IsFloat = true;
                object v = GetMember(data, f.Field);
                f.Value = v == null ? "0" : Convert.ToSingle(v).ToString("0.##");
                _statFields.Add(f);
            }
        }

        private void DrawStatsTab()
        {
            if (_statFields == null) LoadStatsFields();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("刷新", GUILayout.Width(56f))) LoadStatsFields();
            if (GUILayout.Button("填入高数值（不写入，需点应用）", GUILayout.Width(240f)))
            {
                if (_statFields != null)
                {
                    for (int i = 0; i < _statFields.Count; i++)
                    {
                        _statFields[i].Value = _statFields[i].IsFloat ? "999999" : "99999";
                    }
                }
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_statStatus)) GUILayout.Label(_statStatus);

            if (_statFields == null || _statFields.Count == 0)
            {
                GUILayout.Label("没有可用的统计字段。");
                return;
            }

            _statScroll = GUILayout.BeginScrollView(_statScroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _statFields.Count; i++)
            {
                StatField f = _statFields[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label(f.Label, GUILayout.Width(210f));
                f.Value = GUILayout.TextField(f.Value ?? "", GUILayout.Width(110f));
                if (GUILayout.Button("应用", GUILayout.Width(52f)))
                {
                    object data = GetStatsData();
                    if (data == null) { _statStatus = "统计数据未就绪。"; }
                    else
                    {
                        try
                        {
                            FieldInfo fi = data.GetType().GetField(f.Field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (fi == null) { _statStatus = "找不到字段 " + f.Field; }
                            else
                            {
                                if (f.IsFloat) fi.SetValue(data, float.Parse(f.Value));
                                else fi.SetValue(data, int.Parse(f.Value));

                                if (SaveStats()) _statStatus = "已写入 " + f.Field + "（已保存）";
                                else _statStatus = "已写入 " + f.Field + "，但保存失败（可能下次读档被覆盖）";
                            }
                        }
                        catch (Exception ex) { _statStatus = "写入失败：" + ex.Message; }
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Label("提示：统计写的是 PlayerStatsService.Data，点「应用」后调 Save() 落盘。");
        }

        // ---------------- 成就 ----------------

        private void RefreshAchievementList()
        {
            _achievements = new List<AchievementEntry>();
            try
            {
                Type idsT = Type.GetType("Features.Achievements.AchievementIds, Assembly-CSharp");
                Type svcT = Type.GetType("Features.Achievements.AchievementService, Assembly-CSharp");

                object all = null;
                if (idsT != null)
                {
                    PropertyInfo p = idsT.GetProperty("All", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null) all = p.GetValue(null, null);
                }

                if (all != null)
                {
                    foreach (object id in (System.Collections.IEnumerable)all)
                    {
                        if (id == null) continue;
                        AchievementEntry e = new AchievementEntry();
                        e.Id = id.ToString();
                        e.Label = Cn(e.Id);
                        e.Unlocked = QueryAchievementUnlocked(svcT, e.Id);
                        _achievements.Add(e);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("读取成就列表失败: " + ex.Message);
            }

            _achievements.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
            Plugin.Log.LogInfo("成就列表已刷新：" + _achievements.Count + " 项");
        }

        private static bool QueryAchievementUnlocked(Type svcT, string id)
        {
            try
            {
                MethodInfo m = svcT != null
                    ? svcT.GetMethod("IsLocallyUnlocked", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    : null;
                if (m == null) return false;
                object r = m.Invoke(null, new object[] { id });
                return r is bool && (bool)r;
            }
            catch { return false; }
        }

        private static bool UnlockAchievement(string id)
        {
            try
            {
                Type svcT = Type.GetType("Features.Achievements.AchievementService, Assembly-CSharp");
                MethodInfo m = svcT != null
                    ? svcT.GetMethod("Unlock", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    : null;
                if (m == null) return false;
                object r = m.Invoke(null, new object[] { id });
                return !(r is bool) || (bool)r;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("解锁成就失败 " + id + ": " + ex.Message);
                return false;
            }
        }

        private void DrawAchievementsTab()
        {
            if (_achievements == null) RefreshAchievementList();

            GUILayout.BeginHorizontal();
            GUILayout.Label("搜索：", GUILayout.Width(46f));
            _achSearch = GUILayout.TextField(_achSearch ?? "", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("清空", GUILayout.Width(46f))) _achSearch = "";
            if (GUILayout.Button("刷新", GUILayout.Width(52f))) RefreshAchievementList();
            if (GUILayout.Button("全部解锁", GUILayout.Width(80f)))
            {
                int n = 0;
                for (int i = 0; i < _achievements.Count; i++)
                {
                    if (_achievements[i].Unlocked) continue;
                    if (UnlockAchievement(_achievements[i].Id)) { _achievements[i].Unlocked = true; n++; }
                }
                _achStatus = "已解锁 " + n + " 项成就";
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_achStatus)) GUILayout.Label(_achStatus);

            if (_achievements.Count == 0)
            {
                GUILayout.Label("没有读到成就 ID（AchievementIds.All 为空）。");
                return;
            }

            int unlocked = 0;
            for (int i = 0; i < _achievements.Count; i++) if (_achievements[i].Unlocked) unlocked++;
            GUILayout.Label("已解锁 " + unlocked + " / " + _achievements.Count);

            string filter = (_achSearch ?? "").Trim().ToLowerInvariant();
            _achScroll = GUILayout.BeginScrollView(_achScroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _achievements.Count; i++)
            {
                AchievementEntry e = _achievements[i];
                if (filter.Length > 0 && (e.Id ?? "").ToLowerInvariant().IndexOf(filter, StringComparison.Ordinal) < 0) continue;

                GUILayout.BeginHorizontal();
                GUILayout.Label((e.Unlocked ? "[OK] " : "[--] ") + e.Id, GUILayout.ExpandWidth(true));
                if (!e.Unlocked)
                {
                    if (GUILayout.Button("解锁", GUILayout.Width(56f)))
                    {
                        if (UnlockAchievement(e.Id)) { e.Unlocked = true; _achStatus = "已解锁 " + e.Id; }
                        else _achStatus = "解锁失败 " + e.Id;
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        // ---------------- 备份与恢复 ----------------

        private static readonly string[] SaveFiles =
        {
            "progression.json", "stats.json", "missions.json", "poi_fast_travel.json"
        };

        private static string SaveDir()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string localLow = System.IO.Path.GetFullPath(System.IO.Path.Combine(localApp, "..", "LocalLow"));
            return System.IO.Path.Combine(localLow, "JoystickyLabs\\Project Exhibitionist\\ProjectExhibitionist\\GameData\\Runtime");
        }

        private static string BackupRoot()
        {
            // 放插件配置目录下，不污染存档目录
            return System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "PE_XpHelper_Backups");
        }

        private void RefreshBackupList()
        {
            _backups = new List<string>();
            try
            {
                string root = BackupRoot();
                if (System.IO.Directory.Exists(root))
                {
                    string[] dirs = System.IO.Directory.GetDirectories(root);
                    Array.Sort(dirs);
                    Array.Reverse(dirs);
                    for (int i = 0; i < dirs.Length; i++) _backups.Add(System.IO.Path.GetFileName(dirs[i]));
                }
                _backupStatus = _backups.Count + " 份备份";
            }
            catch (Exception ex)
            {
                _backupStatus = "读取备份列表失败：" + ex.Message;
            }
        }

        private void BackupNow()
        {
            try
            {
                string src = SaveDir();
                if (!System.IO.Directory.Exists(src))
                {
                    _backupStatus = "存档目录不存在：" + src;
                    return;
                }

                string dir = System.IO.Path.Combine(BackupRoot(), DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                System.IO.Directory.CreateDirectory(dir);

                int n = 0;
                for (int i = 0; i < SaveFiles.Length; i++)
                {
                    string f = System.IO.Path.Combine(src, SaveFiles[i]);
                    if (!System.IO.File.Exists(f)) continue;
                    System.IO.File.Copy(f, System.IO.Path.Combine(dir, SaveFiles[i]), true);
                    n++;
                }

                Plugin.Log.LogInfo("已备份 " + n + " 个存档文件到 " + dir);
                RefreshBackupList();
                _backupStatus = "已备份 " + n + " 个文件 → " + System.IO.Path.GetFileName(dir);
            }
            catch (Exception ex)
            {
                _backupStatus = "备份失败：" + ex.Message;
                Plugin.Log.LogError("备份失败: " + ex);
            }
        }

        private void RestoreBackup(string stamp)
        {
            try
            {
                string dir = System.IO.Path.Combine(BackupRoot(), stamp);
                if (!System.IO.Directory.Exists(dir))
                {
                    _backupStatus = "备份不存在：" + stamp;
                    return;
                }

                string dst = SaveDir();
                if (!System.IO.Directory.Exists(dst))
                {
                    _backupStatus = "存档目录不存在：" + dst;
                    return;
                }

                // 覆盖前先把当前状态留一份
                BackupNow();

                int n = 0;
                for (int i = 0; i < SaveFiles.Length; i++)
                {
                    string f = System.IO.Path.Combine(dir, SaveFiles[i]);
                    if (!System.IO.File.Exists(f)) continue;
                    System.IO.File.Copy(f, System.IO.Path.Combine(dst, SaveFiles[i]), true);
                    n++;
                }

                Plugin.Log.LogInfo("已从 " + stamp + " 恢复 " + n + " 个文件");
                _backupStatus = "已恢复 " + n + " 个文件 · 请重启游戏让存档生效";
            }
            catch (Exception ex)
            {
                _backupStatus = "恢复失败：" + ex.Message;
                Plugin.Log.LogError("恢复失败: " + ex);
            }
        }

        private void DrawBackupTab()
        {
            if (_backups == null) RefreshBackupList();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("立即备份", GUILayout.Width(90f))) BackupNow();
            if (GUILayout.Button("刷新", GUILayout.Width(56f))) RefreshBackupList();
            if (GUILayout.Button("打开备份文件夹", GUILayout.Width(130f)))
            {
                try
                {
                    string root = BackupRoot();
                    System.IO.Directory.CreateDirectory(root);
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + root + "\"");
                }
                catch (Exception ex)
                {
                    _backupStatus = "打开失败：" + ex.Message;
                }
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_backupStatus)) GUILayout.Label(_backupStatus);

            GUILayout.Label("存档目录：" + SaveDir());

            if (_backups == null || _backups.Count == 0)
            {
                GUILayout.Label("还没有备份，点「立即备份」创建第一份。");
                return;
            }

            _backupScroll = GUILayout.BeginScrollView(_backupScroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _backups.Count; i++)
            {
                string stamp = _backups[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label(stamp, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("恢复", GUILayout.Width(56f))) RestoreBackup(stamp);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Label("恢复前会自动备份当前状态；恢复后需重启游戏才生效。");
        }
    }

    // 已核对 Assembly-CSharp：AddRunResults 不调用 GrantXp，两条路径不会叠加成平方倍率。
    [HarmonyPatch(typeof(RankProgressionService), "GrantXp")]
    internal static class PatchGrantXp
    {
        private static void Prefix(ref float amount)
        {
            if (Plugin.SuppressMultiplier) return;
            if (Plugin.XpMultiplier == null) return;
            float m = Mathf.Clamp(Plugin.XpMultiplier.Value, 1f, 10f);
            if (m > 1f && amount > 0f)
            {
                float before = amount;
                amount *= m;
                Plugin.Log.LogInfo("GrantXp 倍率生效：" + before.ToString("0.#") + " -> " + amount.ToString("0.#") + " (x" + m.ToString("0.0") + ")");
            }
        }
    }

    [HarmonyPatch(typeof(RankProgressionService), "CalculateRunXp")]
    internal static class PatchCalculateRunXp
    {
        private static void Postfix(ref RunXpBreakdown __result)
        {
            if (Plugin.XpMultiplier == null) return;
            float m = Mathf.Clamp(Plugin.XpMultiplier.Value, 1f, 10f);
            if (m <= 1f) return;

            float beforeTotal = __result.totalXp;
            __result.baseXp *= m;
            __result.scoreXp *= m;
            __result.totalXp *= m;
            Plugin.Log.LogInfo("CalculateRunXp 倍率生效：" + beforeTotal.ToString("0.#") + " -> " + __result.totalXp.ToString("0.#") + " (x" + m.ToString("0.0") + ")");
        }
    }

    [HarmonyPatch(typeof(CollectionUnlockRegistry), "IsItemUnlocked")]
    internal static class PatchIsItemUnlocked
    {
        private static void Postfix(ref bool __result)
        {
            if (Plugin.BypassMemoryUnlockCheck) return;
            if (Plugin.MemoryAllUnlock != null && Plugin.MemoryAllUnlock.Value) __result = true;
        }
    }

    [HarmonyPatch(typeof(CollectionUnlockRegistry), "IsVariantUnlocked")]
    internal static class PatchIsVariantUnlocked
    {
        private static void Postfix(ref bool __result)
        {
            if (Plugin.BypassMemoryUnlockCheck) return;
            if (Plugin.MemoryAllUnlock != null && Plugin.MemoryAllUnlock.Value) __result = true;
        }
    }

    [HarmonyPatch(typeof(CollectionUnlockRegistry), "IsPoiUnlocked")]
    internal static class PatchIsPoiUnlocked
    {
        private static void Postfix(ref bool __result)
        {
            if (Plugin.BypassMemoryUnlockCheck) return;
            if (Plugin.MemoryAllUnlock != null && Plugin.MemoryAllUnlock.Value) __result = true;
        }
    }
}
