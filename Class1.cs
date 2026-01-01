using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using HarmonyLib;

namespace TheCodex
{
    public class Class1 : MelonMod
    {
        private bool showDropdown = false;
        private Rect windowRect = new Rect(20, 20, 500, 600);
        private Vector2 scrollPosition = Vector2.zero;
        
        // Tab system
        private AbilityTabType currentTab = AbilityTabType.Primary;
        private readonly string[] tabNames = { "Primary", "Secondary", "Utility", "Movement", "Core", "Augments", "Favorites", "Tools" };

        // Search functionality
        private string searchText = "";
        private readonly Dictionary<AbilityTabType, string> searchTexts = new Dictionary<AbilityTabType, string>();

        // Cache the full ability lists to prevent them from being overwritten
        private static readonly Dictionary<PlayerAbilityType, List<AbilityTree>> cachedFullAbilityLists = new Dictionary<PlayerAbilityType, List<AbilityTree>>();
        private static readonly List<AugmentTree> cachedFullCoreList = new List<AugmentTree>();
        private static bool hasInitializedCache = false;

        // Favorites persistence
        private static readonly HashSet<string> favorites = new HashSet<string>(StringComparer.Ordinal);
        private static bool favoritesLoaded = false;
        private static MelonPreferences_Category ?prefsCategory;
        private static MelonPreferences_Entry<string> ?prefsFavoritesEntry;
        private const string PrefsCategoryName = "TheCodex";
        private const string PrefsFavoritesKey = "Favorites";

        // Tools/Cheats
        private static bool noClipEnabled = false;
        public static bool godModeEnabled = false;
        public static bool blockPlayerAugmentsEnabled = false;
        public static bool infiniteManaEnabled = false;

        // Tools: Rerolls editor
        private string rerollsInput = string.Empty;
        private int lastAutoFilledRerolls = int.MinValue;
        // Tools: Font points editor
        private string fontPointsInput = string.Empty;
        private int lastAutoFilledFontPoints = int.MinValue;

        // Cached textures to avoid creating new ones every frame
        // Key includes width, height, and color to ensure correct texture retrieval
        private static readonly Dictionary<(int width, int height, Color color), Texture2D> textureCache = new Dictionary<(int width, int height, Color color), Texture2D>();

        // Cached GUI styles (initialized lazily in InitializeStyles())
        private GUIStyle cachedWindowStyle = null!;
        private GUIStyle cachedTabStyle = null!;
        private GUIStyle cachedSelectedTabStyle = null!;
        private GUIStyle cachedSearchStyle = null!;
        private GUIStyle cachedClearButtonStyle = null!;
        private GUIStyle cachedItemButtonStyle = null!;
        private GUIStyle cachedStarButtonStyleOn = null!;
        private GUIStyle cachedStarButtonStyleOff = null!;
        private GUIStyle cachedHeaderStyle = null!;
        private GUIStyle cachedFavoritesHeaderStyle = null!;
        private GUIStyle cachedCurrentEquippedStyle = null!;
        private GUIStyle cachedLabelStyle = null!;
        private GUIStyle cachedToggleStyle = null!;
        private GUIStyle cachedTextFieldStyle = null!;
        private GUIStyle cachedApplyButtonStyle = null!;
        private GUIStyle cachedAugmentButtonStyle = null!;
        private GUIStyle cachedSubHeaderStyle = null!;
        private bool stylesInitialized = false;

        public enum AbilityTabType
        {
            Primary,
            Secondary,
            Utility,
            Movement,
            Core,
            AllAugments,
            Favorites,
            Tools
        }

        public override void OnInitializeMelon()
        {
            // Apply Harmony patches
            try
            {
                var harmonyInstance = new HarmonyLib.Harmony("TheCodex.Patches");
                
                // Find the PlayerHealth type and the ApplyDamageImmediate method
                var playerHealthType = typeof(PlayerHealth);
                var applyDamageMethod = playerHealthType.GetMethod("ApplyDamageImmediate", BindingFlags.Public | BindingFlags.Instance);
                
                if (applyDamageMethod != null)
                {
                    var prefixMethod = typeof(GodModePatches).GetMethod("ApplyDamageImmediatePrefix", BindingFlags.Static | BindingFlags.Public);
                    harmonyInstance.Patch(applyDamageMethod, new HarmonyMethod(prefixMethod));
                }
                else
                {
                    MelonLogger.Warning("Could not find PlayerHealth.ApplyDamageImmediate method to patch");
                }

                // Find the EntityHealth type and the Die method
                var entityHealthType = typeof(EntityHealth);
                var dieMethod = entityHealthType.GetMethod("Die", BindingFlags.Public | BindingFlags.Instance);
                
                if (dieMethod != null)
                {
                    var diePrefixMethod = typeof(GodModePatches).GetMethod("DiePrefix", BindingFlags.Static | BindingFlags.Public);
                    harmonyInstance.Patch(dieMethod, new HarmonyMethod(diePrefixMethod));
                }
                else
                {
                    MelonLogger.Warning("Could not find EntityHealth.Die method to patch");
                }

                // Find the PlayerControl type and the AddAugment method with specific parameters
                var playerControlType = typeof(PlayerControl);
                // Look for AddAugment method with AugmentTree and int parameters
                var addAugmentMethod = playerControlType.GetMethod("AddAugment", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(AugmentTree), typeof(int) }, null);
                
                if (addAugmentMethod != null)
                {
                    var addAugmentPrefixMethod = typeof(AugmentBlockPatches).GetMethod("AddAugmentPrefix", BindingFlags.Static | BindingFlags.Public);
                    harmonyInstance.Patch(addAugmentMethod, new HarmonyMethod(addAugmentPrefixMethod));
                }
                else
                {
                    MelonLogger.Warning("Could not find PlayerControl.AddAugment(AugmentTree, int) method to patch");
                    
                    // Try to find any AddAugment method as fallback
                    var allAddAugmentMethods = playerControlType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => m.Name == "AddAugment").ToArray();
                    
                    MelonLogger.Msg($"Found {allAddAugmentMethods.Length} AddAugment methods:");
                    foreach (var method in allAddAugmentMethods)
                    {
                        var parameters = method.GetParameters();
                        var paramTypes = string.Join(", ", parameters.Select(p => p.ParameterType.Name));
                        MelonLogger.Msg($"  AddAugment({paramTypes})");
                    }
                }

                // Patch PlayerMana.ConsumeMana(float,bool) and PlayerMana.Drain() for infinite mana toggle
                try
                {
                    var playerManaType = typeof(PlayerMana);

                    var consumeManaMethod = playerManaType.GetMethod(
                        "ConsumeMana",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new Type[] { typeof(float), typeof(bool) },
                        null);

                    if (consumeManaMethod != null)
                    {
                        var consumePrefix = typeof(ManaPatches).GetMethod("ConsumeManaPrefix", BindingFlags.Static | BindingFlags.Public);
                        harmonyInstance.Patch(consumeManaMethod, new HarmonyMethod(consumePrefix));
                    }
                    else
                    {
                        MelonLogger.Warning("Could not find PlayerMana.ConsumeMana(float, bool) method to patch");
                    }

                    var drainMethod = playerManaType.GetMethod("Drain", BindingFlags.Public | BindingFlags.Instance);
                    if (drainMethod != null)
                    {
                        var drainPrefix = typeof(ManaPatches).GetMethod("DrainPrefix", BindingFlags.Static | BindingFlags.Public);
                        harmonyInstance.Patch(drainMethod, new HarmonyMethod(drainPrefix));
                    }
                    else
                    {
                        MelonLogger.Warning("Could not find PlayerMana.Drain method to patch");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Failed to apply PlayerMana patches: {ex}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to apply Harmony patches: {ex}");
            }
        }

        public override void OnUpdate()
        {
            // Toggle menu with '=' key (both main and keypad)
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                showDropdown = !showDropdown;                
                // Initialize cache when first opening the menu
                if (showDropdown && !hasInitializedCache)
                {
                    InitializeAbilityCache();
                    InitializeSearchTexts();
                }
                if (showDropdown && !favoritesLoaded)
                {
                    LoadFavorites();
                }
            }

            // Force cursor state every frame while menu is open
            if (showDropdown)
            {
                SetCursorState(true);
            }

            // Apply Tools effects
            ApplyNoClip();
        }

        private void InitializeSearchTexts()
        {
            // Initialize search text dictionary for each tab
            foreach (AbilityTabType tabType in Enum.GetValues(typeof(AbilityTabType)))
            {
                if (!searchTexts.ContainsKey(tabType))
                {
                    searchTexts[tabType] = "";
                }
            }
        }

        private void InitializeAbilityCache()
        {
            try
            {
                MelonLogger.Msg("Initializing ability cache...");
                
                // Cache all ability types
                var abilityTypes = new PlayerAbilityType[] 
                { 
                    PlayerAbilityType.Primary, 
                    PlayerAbilityType.Secondary, 
                    PlayerAbilityType.Utility, 
                    PlayerAbilityType.Movement 
                };

                foreach (var abilityType in abilityTypes)
                {
                    var abilities = GetAbilitiesForTypeInternal(abilityType);
                    cachedFullAbilityLists[abilityType] = new List<AbilityTree>(abilities);
                    MelonLogger.Msg($"Cached {abilities.Count} abilities for type {abilityType}");
                }

                // Cache cores
                var cores = GetAvailableCoresInternal();
                cachedFullCoreList.Clear();
                cachedFullCoreList.AddRange(cores);
                MelonLogger.Msg($"Cached {cores.Count} cores");

                hasInitializedCache = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error initializing ability cache: {ex}");
            }
        }

        private void InitializeStyles()
        {
            if (stylesInitialized) return;

            // Window style
            cachedWindowStyle = new GUIStyle(GUI.skin.window);
            cachedWindowStyle.normal.background = MakeTexture(2, 2, new Color(0.12f, 0.12f, 0.12f, 1f));
            cachedWindowStyle.hover.background = cachedWindowStyle.normal.background;
            cachedWindowStyle.active.background = cachedWindowStyle.normal.background;
            cachedWindowStyle.focused.background = cachedWindowStyle.normal.background;
            cachedWindowStyle.onNormal.background = cachedWindowStyle.normal.background;
            cachedWindowStyle.onHover.background = cachedWindowStyle.normal.background;
            cachedWindowStyle.onActive.background = cachedWindowStyle.normal.background;
            cachedWindowStyle.onFocused.background = cachedWindowStyle.normal.background;
            cachedWindowStyle.normal.textColor = Color.white;
            cachedWindowStyle.hover.textColor = Color.white;
            cachedWindowStyle.active.textColor = Color.white;
            cachedWindowStyle.focused.textColor = Color.white;
            cachedWindowStyle.onNormal.textColor = Color.white;
            cachedWindowStyle.onHover.textColor = Color.white;
            cachedWindowStyle.onActive.textColor = Color.white;
            cachedWindowStyle.onFocused.textColor = Color.white;

            // Tab style
            cachedTabStyle = new GUIStyle(GUI.skin.button);
            cachedTabStyle.normal.background = MakeTexture(2, 2, new Color(0.2f, 0.2f, 0.2f, 1f));
            cachedTabStyle.normal.textColor = Color.white;
            cachedTabStyle.fontSize = 14;
            cachedTabStyle.fixedHeight = 30;
            cachedTabStyle.hover.background = MakeTexture(2, 2, new Color(0.3f, 0.3f, 0.3f, 1f));
            cachedTabStyle.active.background = cachedTabStyle.normal.background;

            // Selected tab style
            cachedSelectedTabStyle = new GUIStyle(cachedTabStyle);
            cachedSelectedTabStyle.normal.background = MakeTexture(2, 2, new Color(0.4f, 0.4f, 0.4f, 1f));
            cachedSelectedTabStyle.normal.textColor = Color.yellow;
            cachedSelectedTabStyle.hover.background = cachedSelectedTabStyle.normal.background;
            cachedSelectedTabStyle.active.background = cachedSelectedTabStyle.normal.background;

            // Search bar style
            cachedSearchStyle = new GUIStyle(GUI.skin.textField);
            cachedSearchStyle.normal.background = MakeTexture(2, 2, new Color(0.15f, 0.15f, 0.15f, 1f));
            cachedSearchStyle.normal.textColor = Color.white;
            cachedSearchStyle.fontSize = 14;
            cachedSearchStyle.fixedHeight = 25;

            // Label style
            cachedLabelStyle = new GUIStyle(GUI.skin.label);
            cachedLabelStyle.normal.textColor = Color.white;
            cachedLabelStyle.fontSize = 12;

            // Clear button style
            cachedClearButtonStyle = new GUIStyle(GUI.skin.button);
            cachedClearButtonStyle.normal.background = MakeTexture(2, 2, new Color(0.3f, 0.1f, 0.1f, 1f));
            cachedClearButtonStyle.normal.textColor = Color.white;
            cachedClearButtonStyle.fontSize = 12;
            cachedClearButtonStyle.fixedHeight = 25;
            cachedClearButtonStyle.hover.background = MakeTexture(2, 2, new Color(0.4f, 0.2f, 0.2f, 1f));

            // Item button style
            cachedItemButtonStyle = new GUIStyle(GUI.skin.button);
            cachedItemButtonStyle.normal.background = MakeTexture(2, 2, new Color(0.25f, 0.25f, 0.25f, 1f));
            cachedItemButtonStyle.normal.textColor = Color.white;
            cachedItemButtonStyle.fontSize = 16;
            cachedItemButtonStyle.fixedHeight = 40;
            cachedItemButtonStyle.hover.background = MakeTexture(2, 2, new Color(0.35f, 0.35f, 0.35f, 1f));
            cachedItemButtonStyle.active.background = cachedItemButtonStyle.normal.background;

            // Star button styles
            var starBg = MakeTexture(2, 2, new Color(0.18f, 0.18f, 0.18f, 1f));
            
            cachedStarButtonStyleOn = new GUIStyle(GUI.skin.button);
            cachedStarButtonStyleOn.fontSize = 18;
            cachedStarButtonStyleOn.fixedWidth = 36f;
            cachedStarButtonStyleOn.fixedHeight = 40f;
            cachedStarButtonStyleOn.alignment = TextAnchor.MiddleCenter;
            cachedStarButtonStyleOn.normal.textColor = Color.yellow;
            cachedStarButtonStyleOn.hover.textColor = Color.yellow;
            cachedStarButtonStyleOn.normal.background = starBg;
            cachedStarButtonStyleOn.hover.background = starBg;
            cachedStarButtonStyleOn.active.background = starBg;

            cachedStarButtonStyleOff = new GUIStyle(GUI.skin.button);
            cachedStarButtonStyleOff.fontSize = 18;
            cachedStarButtonStyleOff.fixedWidth = 36f;
            cachedStarButtonStyleOff.fixedHeight = 40f;
            cachedStarButtonStyleOff.alignment = TextAnchor.MiddleCenter;
            cachedStarButtonStyleOff.normal.textColor = Color.white;
            cachedStarButtonStyleOff.hover.textColor = Color.yellow;
            cachedStarButtonStyleOff.normal.background = starBg;
            cachedStarButtonStyleOff.hover.background = starBg;
            cachedStarButtonStyleOff.active.background = starBg;

            // Header style
            cachedHeaderStyle = new GUIStyle(GUI.skin.label);
            cachedHeaderStyle.fontSize = 18;
            cachedHeaderStyle.normal.textColor = Color.white;
            cachedHeaderStyle.fontStyle = FontStyle.Bold;

            // Current equipped style
            cachedCurrentEquippedStyle = new GUIStyle(GUI.skin.box);
            cachedCurrentEquippedStyle.normal.background = MakeTexture(2, 2, new Color(0.1f, 0.3f, 0.1f, 1f));
            cachedCurrentEquippedStyle.normal.textColor = Color.green;
            cachedCurrentEquippedStyle.fontSize = 14;

            // Toggle style
            cachedToggleStyle = new GUIStyle(GUI.skin.toggle);
            cachedToggleStyle.normal.textColor = Color.white;
            cachedToggleStyle.fontSize = 14;

            // Text field style for tools
            cachedTextFieldStyle = new GUIStyle(GUI.skin.textField);
            cachedTextFieldStyle.normal.background = MakeTexture(2, 2, new Color(0.15f, 0.15f, 0.15f, 1f));
            cachedTextFieldStyle.normal.textColor = Color.white;
            cachedTextFieldStyle.fontSize = 14;
            cachedTextFieldStyle.fixedHeight = 25;

            // Apply button style
            cachedApplyButtonStyle = new GUIStyle(GUI.skin.button);
            cachedApplyButtonStyle.normal.background = MakeTexture(2, 2, new Color(0.2f, 0.3f, 0.2f, 1f));
            cachedApplyButtonStyle.normal.textColor = Color.white;
            cachedApplyButtonStyle.fontSize = 14;
            cachedApplyButtonStyle.fixedHeight = 25;
            cachedApplyButtonStyle.hover.background = MakeTexture(2, 2, new Color(0.25f, 0.4f, 0.25f, 1f));

            // Augment button style
            cachedAugmentButtonStyle = new GUIStyle(GUI.skin.button);
            cachedAugmentButtonStyle.normal.background = MakeTexture(2, 2, new Color(0.25f, 0.25f, 0.25f, 1f));
            cachedAugmentButtonStyle.normal.textColor = Color.white;
            cachedAugmentButtonStyle.fontSize = 16;
            cachedAugmentButtonStyle.fixedHeight = 35;
            cachedAugmentButtonStyle.hover.background = MakeTexture(2, 2, new Color(0.35f, 0.35f, 0.35f, 1f));
            cachedAugmentButtonStyle.active.background = cachedAugmentButtonStyle.normal.background;

            // Sub header style
            cachedSubHeaderStyle = new GUIStyle(GUI.skin.label);
            cachedSubHeaderStyle.fontSize = 16;
            cachedSubHeaderStyle.fontStyle = FontStyle.Bold;
            cachedSubHeaderStyle.normal.textColor = Color.white;

            // Favorites header style (yellow text)
            cachedFavoritesHeaderStyle = new GUIStyle(GUI.skin.label);
            cachedFavoritesHeaderStyle.fontSize = 18;
            cachedFavoritesHeaderStyle.fontStyle = FontStyle.Bold;
            cachedFavoritesHeaderStyle.normal.textColor = Color.yellow;

            stylesInitialized = true;
        }

        public override void OnGUI()
        {
            if (showDropdown)
            {
                // Initialize styles once
                InitializeStyles();

                // Prevent any mouse events from affecting the game behind the GUI
                GUI.FocusWindow(0);

                GUI.depth = 0;
                windowRect = GUILayout.Window(0, windowRect, DrawWindow, "The Codex", cachedWindowStyle);
            }
        }

        private void DrawWindow(int windowID)
        {
            // Draw tab buttons
            GUILayout.BeginHorizontal();
            for (int i = 0; i < tabNames.Length; i++)
            {
                AbilityTabType tabType = (AbilityTabType)i;
                GUIStyle styleToUse = (currentTab == tabType) ? cachedSelectedTabStyle : cachedTabStyle;
                
                if (GUILayout.Button(tabNames[i], styleToUse, GUILayout.ExpandWidth(true)))
                {
                    currentTab = tabType;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Draw search bar (not for Tools tab)
            if (currentTab != AbilityTabType.Tools)
            {
                DrawSearchBar();
                GUILayout.Space(5);
            }

            // Draw content based on current tab
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            switch (currentTab)
            {
                case AbilityTabType.Primary:
                    DrawAbilityTab(PlayerAbilityType.Primary, "Primary Abilities");
                    break;
                case AbilityTabType.Secondary:
                    DrawAbilityTab(PlayerAbilityType.Secondary, "Secondary Abilities");
                    break;
                case AbilityTabType.Utility:
                    DrawAbilityTab(PlayerAbilityType.Utility, "Utility Abilities");
                    break;
                case AbilityTabType.Movement:
                    DrawAbilityTab(PlayerAbilityType.Movement, "Movement Abilities");
                    break;
                case AbilityTabType.Core:
                    DrawCoreTab();
                    break;
                case AbilityTabType.AllAugments:
                    DrawAllAugmentsTab();
                    break;
                case AbilityTabType.Favorites:
                    DrawFavoritesTab();
                    break;
                case AbilityTabType.Tools:
                    DrawToolsTab();
                    break;
            }

            GUILayout.EndScrollView();

            // Make window draggable
            GUI.DragWindow();
        }

        private void DrawSearchBar()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", cachedLabelStyle, GUILayout.Width(50));
            
            // Get current search text for this tab
            string currentSearchText = searchTexts.ContainsKey(currentTab) ? searchTexts[currentTab] : "";
            string newSearchText = GUILayout.TextField(currentSearchText, cachedSearchStyle, GUILayout.ExpandWidth(true));
            
            // Update search text if changed
            if (newSearchText != currentSearchText)
            {
                searchTexts[currentTab] = newSearchText;
            }

            if (GUILayout.Button("Clear", cachedClearButtonStyle, GUILayout.Width(50)))
            {
                searchTexts[currentTab] = "";
            }

            GUILayout.EndHorizontal();
        }

        private GUIStyle BuildItemButtonStyle()
        {
            return cachedItemButtonStyle;
        }

        private GUIStyle BuildStarButtonStyle(bool isOn)
        {
            return isOn ? cachedStarButtonStyleOn : cachedStarButtonStyleOff;
        }

        private void DrawAbilityTab(PlayerAbilityType abilityType, string tabTitle)
        {
            GUILayout.Label(tabTitle, cachedHeaderStyle);
            GUILayout.Space(10);

            // Show current equipped ability
            var currentAbility = GetCurrentAbility(abilityType);
            if (currentAbility != null)
            {
                GUILayout.BeginVertical(cachedCurrentEquippedStyle);
                GUILayout.Label($"Currently Equipped: {currentAbility.Root.Name}", cachedCurrentEquippedStyle);
                GUILayout.EndVertical();
                GUILayout.Space(10);
            }

            // Get abilities for this type - use cached version and apply search filter
            var abilities = GetFilteredAbilities(abilityType);
            
            if (abilities.Count == 0)
            {
                string searchText = searchTexts.ContainsKey(currentTab) ? searchTexts[currentTab] : "";
                if (!string.IsNullOrEmpty(searchText))
                {
                    GUILayout.Label($"No abilities found matching '{searchText}'.", GUI.skin.label);
                }
                else
                {
                    GUILayout.Label("No abilities available for this type.", GUI.skin.label);
                }
                return;
            }

            // Button styles
            GUIStyle buttonStyle = BuildItemButtonStyle();

            foreach (var ability in abilities)
            {
                string buttonText = ability.Root.Name;
                bool isEquipped = currentAbility != null && currentAbility.ID == ability.ID;
                bool isFav = IsFavoriteAbility(abilityType, ability);

                GUILayout.BeginHorizontal();
                // Favorite toggle
                if (GUILayout.Button(isFav ? "★" : "☆", BuildStarButtonStyle(isFav)))
                {
                    ToggleFavoriteAbility(abilityType, ability);
                }

                // Main select button
                if (isEquipped)
                {
                    buttonStyle.normal.textColor = Color.green;
                }
                else
                {
                    buttonStyle.normal.textColor = Color.white;
                }

                if (isEquipped) buttonText += " (Equipped)";

                if (GUILayout.Button(buttonText, buttonStyle, GUILayout.ExpandWidth(true)))
                {
                    if (!isEquipped)
                    {
                        EquipAbility(abilityType, ability);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawCoreTab()
        {
            GUILayout.Label("Core Selection", cachedHeaderStyle);
            GUILayout.Space(10);

            // Show current equipped core
            var currentCore = GetCurrentCore();
            if (currentCore != null)
            {
                GUILayout.BeginVertical(cachedCurrentEquippedStyle);
                GUILayout.Label($"Currently Equipped: {currentCore.Root.Name}", cachedCurrentEquippedStyle);
                GUILayout.EndVertical();
                GUILayout.Space(10);
            }

            // Get available cores - use cached version and apply search filter
            var cores = GetFilteredCores();
            
            if (cores.Count == 0)
            {
                string searchText = searchTexts.ContainsKey(currentTab) ? searchTexts[currentTab] : "";
                if (!string.IsNullOrEmpty(searchText))
                {
                    GUILayout.Label($"No cores found matching '{searchText}'.", GUI.skin.label);
                }
                else
                {
                    GUILayout.Label("No cores available.", GUI.skin.label);
                }
                return;
            }

            // Button style for cores
            GUIStyle buttonStyle = BuildItemButtonStyle();

            foreach (var core in cores)
            {
                string buttonText = core.Root.Name;
                bool isEquipped = currentCore != null && currentCore.ID == core.ID;
                bool isFav = IsFavoriteCore(core);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(isFav ? "★" : "☆", BuildStarButtonStyle(isFav)))
                {
                    ToggleFavoriteCore(core);
                }

                if (isEquipped)
                {
                    buttonStyle.normal.textColor = Color.green;
                }
                else
                {
                    buttonStyle.normal.textColor = Color.white;
                }

                if (isEquipped) buttonText += " (Equipped)";

                if (GUILayout.Button(buttonText, buttonStyle, GUILayout.ExpandWidth(true)))
                {
                    if (!isEquipped)
                    {
                        EquipCore(core);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawAllAugmentsTab()
        {
            GUILayout.Label("All Augments", cachedHeaderStyle);
            GUILayout.Space(10);

            // Get filtered augments
            var augments = GetFilteredAugments();
            
            if (augments.Count == 0)
            {
                string searchText = searchTexts.ContainsKey(currentTab) ? searchTexts[currentTab] : "";
                if (!string.IsNullOrEmpty(searchText))
                {
                    GUILayout.Label($"No augments found matching '{searchText}'.", GUI.skin.label);
                }
                else
                {
                    GUILayout.Label("No augments available.", GUI.skin.label);
                }
                return;
            }

            // Use the filtered augment trees
            foreach (var augment in augments)
            {
                string displayName = augment.Root?.Name ?? "Unnamed Augment";
                bool isFav = IsFavoriteAugment(augment);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(isFav ? "★" : "☆", BuildStarButtonStyle(isFav)))
                {
                    ToggleFavoriteAugment(augment);
                }

                if (GUILayout.Button(displayName, cachedAugmentButtonStyle, GUILayout.ExpandWidth(true)))
                {
                    MelonLogger.Msg($"Adding augment: {displayName} (ID: {augment.ID})");
                    AddAugment(augment);
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawFavoritesTab()
        {
            GUILayout.Label("Favorites", cachedFavoritesHeaderStyle);
            GUILayout.Space(10);

            string filter = searchTexts.ContainsKey(AbilityTabType.Favorites) ? searchTexts[AbilityTabType.Favorites] : "";
            bool HasFilter(string name) => string.IsNullOrEmpty(filter) || (name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;

            // Abilities by type
            void DrawFavAbilitySection(PlayerAbilityType type, string title)
            {
                var all = GetAbilitiesForType(type);
                var favs = all.Where(a => IsFavoriteAbility(type, a) && HasFilter(a.Root.Name)).ToList();
                if (favs.Count == 0) return;

                GUILayout.Label(title, cachedSubHeaderStyle);

                var current = GetCurrentAbility(type);
                GUIStyle buttonStyle = BuildItemButtonStyle();

                foreach (var ability in favs)
                {
                    string text = ability.Root.Name;
                    bool isEquipped = current != null && current.ID == ability.ID;

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("★", BuildStarButtonStyle(true)))
                    {
                        ToggleFavoriteAbility(type, ability); // will un-favorite
                    }

                    buttonStyle.normal.textColor = isEquipped ? Color.green : Color.white;
                    if (isEquipped) text += " (Equipped)";
                    if (GUILayout.Button(text, buttonStyle, GUILayout.ExpandWidth(true)))
                    {
                        if (!isEquipped) EquipAbility(type, ability);
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(6);
            }

            DrawFavAbilitySection(PlayerAbilityType.Primary, "Primary Abilities");
            DrawFavAbilitySection(PlayerAbilityType.Secondary, "Secondary Abilities");
            DrawFavAbilitySection(PlayerAbilityType.Utility, "Utility Abilities");
            DrawFavAbilitySection(PlayerAbilityType.Movement, "Movement Abilities");

            // Cores
            var coreList = GetAvailableCores().Where(c => IsFavoriteCore(c) && HasFilter(c.Root.Name)).ToList();
            if (coreList.Count > 0)
            {
                GUILayout.Label("Cores", cachedSubHeaderStyle);

                var currentCore = GetCurrentCore();
                GUIStyle buttonStyle = BuildItemButtonStyle();

                foreach (var core in coreList)
                {
                    string text = core.Root.Name;
                    bool isEquipped = currentCore != null && currentCore.ID == core.ID;

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("★", BuildStarButtonStyle(true)))
                    {
                        ToggleFavoriteCore(core); // un-favorite
                    }

                    buttonStyle.normal.textColor = isEquipped ? Color.green : Color.white;
                    if (isEquipped) text += " (Equipped)";
                    if (GUILayout.Button(text, buttonStyle, GUILayout.ExpandWidth(true)))
                    {
                        if (!isEquipped) EquipCore(core);
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(6);
            }

            // Augments
            var augmentList = GetAddableAugmentTrees().Where(a => IsFavoriteAugment(a) && HasFilter(a.Root.Name)).ToList();
            if (augmentList.Count > 0)
            {
                GUILayout.Label("Augments", cachedSubHeaderStyle);

                foreach (var aug in augmentList)
                {
                    string name = aug.Root?.Name ?? "Unnamed Augment";

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("★", BuildStarButtonStyle(true)))
                    {
                        ToggleFavoriteAugment(aug); // un-favorite
                    }

                    if (GUILayout.Button(name, cachedAugmentButtonStyle, GUILayout.ExpandWidth(true)))
                    {
                        MelonLogger.Msg($"Adding augment: {name} (ID: {aug.ID})");
                        AddAugment(aug);
                    }
                    GUILayout.EndHorizontal();
                }
            }
        }

        private void DrawToolsTab()
        {
            GUILayout.Label("Tools", cachedHeaderStyle);
            GUILayout.Space(10);

            noClipEnabled = GUILayout.Toggle(noClipEnabled, "No Clip", cachedToggleStyle);
            godModeEnabled = GUILayout.Toggle(godModeEnabled, "God Mode", cachedToggleStyle);
            infiniteManaEnabled = GUILayout.Toggle(infiniteManaEnabled, "Infinite Mana", cachedToggleStyle);
            blockPlayerAugmentsEnabled = GUILayout.Toggle(blockPlayerAugmentsEnabled, "Block Player Pages", cachedToggleStyle);

            GUILayout.Space(10);

            // Rerolls editor UI
            try
            {
                int baseRerolls = 0;
                int currentRemaining = 0;
                if (PlayerControl.myInstance != null)
                {
                    baseRerolls = (int)PlayerControl.myInstance.GetPassiveMod(Passive.EntityValue.P_PageRerolls, 0f);
                    currentRemaining = baseRerolls - PlayerChoicePanel.RerollsUsed;
                }

                // Auto-fill input when value changes and field is not focused
                string controlName = "RerollsInputField";
                string focused = GUI.GetNameOfFocusedControl();
                bool isFocused = focused == controlName;
                if (!isFocused && currentRemaining != lastAutoFilledRerolls)
                {
                    rerollsInput = currentRemaining.ToString();
                    lastAutoFilledRerolls = currentRemaining;
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("Rerolls:", cachedLabelStyle, GUILayout.Width(70));
                GUI.SetNextControlName(controlName);
                rerollsInput = GUILayout.TextField(rerollsInput ?? string.Empty, cachedTextFieldStyle, GUILayout.ExpandWidth(true));

                if (GUILayout.Button("Apply", cachedApplyButtonStyle, GUILayout.Width(70)))
                {
                    if (int.TryParse(rerollsInput, out int desiredRemaining))
                    {
                        int newUsed = baseRerolls - desiredRemaining;
                        PlayerChoicePanel.RerollsUsed = newUsed;
                        lastAutoFilledRerolls = desiredRemaining; // reflect desired in UI
                    }
                    else
                    {
                        MelonLogger.Warning($"Invalid rerolls input: '{rerollsInput}'");
                    }
                }
                GUILayout.EndHorizontal();

                // Font points editor UI (Fountain points)
                try
                {
                    int currentPoints = 0;
                    if (InkManager.instance != null)
                    {
                        currentPoints = InkManager.MyShards;
                    }

                    string fpControlName = "FontPointsInputField";
                    string focused2 = GUI.GetNameOfFocusedControl();
                    bool isFPfocused = focused2 == fpControlName;
                    if (!isFPfocused && currentPoints != lastAutoFilledFontPoints)
                    {
                        fontPointsInput = currentPoints.ToString();
                        lastAutoFilledFontPoints = currentPoints;
                    }

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Font Points:", cachedLabelStyle, GUILayout.Width(90));
                    GUI.SetNextControlName(fpControlName);
                    fontPointsInput = GUILayout.TextField(fontPointsInput ?? string.Empty, cachedTextFieldStyle, GUILayout.ExpandWidth(true));

                    if (GUILayout.Button("Apply", cachedApplyButtonStyle, GUILayout.Width(70)))
                    {
                        if (int.TryParse(fontPointsInput, out int desiredPoints))
                        {
                            if (desiredPoints < 0) desiredPoints = 0;
                            int delta = desiredPoints - currentPoints;
                            if (delta != 0)
                            {
                                if (InkManager.instance != null)
                                {
                                    try
                                    {
                                        InkManager.instance.AddInk(delta);
                                        lastAutoFilledFontPoints = desiredPoints; // update UI hint
                                    }
                                    catch (Exception ex)
                                    {
                                        MelonLogger.Warning($"Failed to apply font points change: {ex.Message}");
                                    }
                                }
                                else
                                {
                                    MelonLogger.Warning("InkManager.instance is null - cannot change font points");
                                }
                            }
                        }
                        else
                        {
                            MelonLogger.Warning($"Invalid font points input: '{fontPointsInput}'");
                        }
                    }
                    GUILayout.EndHorizontal();
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Font points editor error: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Rerolls editor error: {ex.Message}");
            }
        }

        private void ApplyNoClip()
        {
            if (PlayerControl.myInstance == null) return;

            try
            {
                var rigidbody = PlayerControl.myInstance.GetComponent<Rigidbody>();
                var colliders = PlayerControl.myInstance.GetComponentsInChildren<Collider>();

                if (noClipEnabled)
                {
                    if (rigidbody != null)
                    {
                        rigidbody.isKinematic = true;
                    }

                    foreach (var collider in colliders)
                    {
                        if (collider != null)
                        {
                            collider.isTrigger = true;
                        }
                    }
                }
                else
                {
                    if (rigidbody != null)
                    {
                        rigidbody.isKinematic = false;
                    }

                    foreach (var collider in colliders)
                    {
                        if (collider != null)
                        {
                            collider.isTrigger = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error applying no clip: {ex}");
            }
        }

        private List<AbilityTree> GetFilteredAbilities(PlayerAbilityType abilityType)
        {
            var allAbilities = GetAbilitiesForType(abilityType);
            string searchText = searchTexts.ContainsKey(currentTab) ? searchTexts[currentTab] : "";
            
            if (string.IsNullOrEmpty(searchText))
            {
                return allAbilities;
            }

            return allAbilities.Where(ability => 
                ability.Root?.Name != null && 
                ability.Root.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
            ).ToList();
        }

        private List<AugmentTree> GetFilteredCores()
        {
            var allCores = GetAvailableCores();
            string searchText = searchTexts.ContainsKey(currentTab) ? searchTexts[currentTab] : "";
            
            if (string.IsNullOrEmpty(searchText))
            {
                return allCores;
            }

            return allCores.Where(core => 
                core.Root?.Name != null && 
                core.Root.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
            ).ToList();
        }

        private List<AugmentTree> GetFilteredAugments()
        {
            var allAugments = GetAddableAugmentTrees();
            string searchText = searchTexts.ContainsKey(currentTab) ? searchTexts[currentTab] : "";
            
            if (string.IsNullOrEmpty(searchText))
            {
                return allAugments;
            }

            return allAugments.Where(augment => 
                augment.Root?.Name != null && 
                augment.Root.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
            ).ToList();
        }

        private void AddAugment(AugmentTree augmentTree)
        {
            try
            {
                if (PlayerControl.myInstance != null)
                {
                    MelonLogger.Msg($"Adding augment: {augmentTree.Root?.Name ?? "Unnamed"} (ID: {augmentTree.ID})");
                    PlayerControl.myInstance.AddAugment(augmentTree, 1);
                    MelonLogger.Msg($"Successfully added augment: {augmentTree.Root?.Name ?? "Unnamed"}");
                }
                else
                {
                    MelonLogger.Error("PlayerControl.myInstance is null - cannot add augment");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error adding augment {augmentTree.Root?.Name ?? "Unnamed"}: {ex}");
            }
        }

        // Get all addable augments as AugmentTree objects instead of name strings
        private List<AugmentTree> GetAddableAugmentTrees()
        {
            var augments = new List<AugmentTree>();
            try
            {
                var list = GraphDB.GetAllAugments(ModType.Player);
                if (list != null)
                {
                    foreach (var augment in list)
                    {
                        if (augment != null)
                        {
                            augments.Add(augment);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error getting augment trees: {ex}");
            }
            
            // Sort by name (case-insensitive), handling null names
            return augments.OrderBy(a => a.Root?.Name ?? "zzz_Unnamed", StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Public method that returns cached data if available, fresh data otherwise
        private List<AbilityTree> GetAbilitiesForType(PlayerAbilityType abilityType)
        {
            if (hasInitializedCache && cachedFullAbilityLists.ContainsKey(abilityType))
            {
                return cachedFullAbilityLists[abilityType];
            }
            return GetAbilitiesForTypeInternal(abilityType);
        }

        // Internal method that always fetches fresh data
        private List<AbilityTree> GetAbilitiesForTypeInternal(PlayerAbilityType abilityType)
        {
            var abilities = new List<AbilityTree>();
            
            try
            {
                // Use the correct method to get player abilities for a type
                var allAbilities = GraphDB.GetPlayerAbilities(abilityType);
                if (allAbilities != null)
                {
                    foreach (var ability in allAbilities)
                    {
                        if (ability?.Root?.PlrAbilityType == abilityType && ability.Root.Name != null)
                        {
                            abilities.Add(ability);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error getting abilities for type {abilityType}: {ex}");
            }

            // Sort by name
            return abilities.OrderBy(a => a.Root.Name).ToList();
        }

        // Public method that returns cached cores if available
        private List<AugmentTree> GetAvailableCores()
        {
            if (hasInitializedCache && cachedFullCoreList.Count > 0)
            {
                return cachedFullCoreList;
            }
            return GetAvailableCoresInternal();
        }

        // Internal method that always fetches fresh cores
        private List<AugmentTree> GetAvailableCoresInternal()
        {
            var cores = new List<AugmentTree>();
            
            try
            {
                // Use reflection to access the private PlayerDB.instance
                var playerDBType = typeof(PlayerDB);
                var instanceField = playerDBType.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
                
                if (instanceField != null)
                {
                    var playerDBInstance = instanceField.GetValue(null);
                    if (playerDBInstance != null)
                    {
                        // Get the Cores field from the instance
                        var coresField = playerDBType.GetField("Cores", BindingFlags.Public | BindingFlags.Instance);
                        if (coresField != null)
                        {
                            var coresCollection = coresField.GetValue(playerDBInstance);
                            if (coresCollection is IEnumerable<object> coresList)
                            {
                                foreach (var coreDisplayObj in coresList)
                                {
                                    // Get the core field from CoreDisplay
                                    var coreField = coreDisplayObj.GetType().GetField("core", BindingFlags.Public | BindingFlags.Instance);
                                    var colorField = coreDisplayObj.GetType().GetField("color", BindingFlags.Public | BindingFlags.Instance);
                                    
                                    if (coreField != null && colorField != null)
                                    {
                                        var core = coreField.GetValue(coreDisplayObj) as AugmentTree;
                                        var color = colorField.GetValue(coreDisplayObj);
                                        
                                        if (core != null)
                                        {
                                            cores.Add(core);
                                            MelonLogger.Msg($"Found core: {core.Root.Name} (Color: {color})");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                
                // Alternative approach: Use the static GetCore method with known MagicColor values
                if (cores.Count == 0)
                {
                    MelonLogger.Msg("Reflection approach failed, trying static method approach...");
                    
                    var magicColors = new MagicColor[] 
                    { 
                        MagicColor.Red, MagicColor.Yellow, MagicColor.Green, MagicColor.Blue,
                        MagicColor.Pink, MagicColor.Orange, MagicColor.Teal, MagicColor.Neutral
                    };
                    
                    foreach (var color in magicColors)
                    {
                        try
                        {
                            var coreDisplay = PlayerDB.GetCore(color);
                            if (coreDisplay?.core != null)
                            {
                                cores.Add(coreDisplay.core);
                                MelonLogger.Msg($"Found core via static method: {coreDisplay.core.Root.Name} (Color: {color})");
                            }
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Warning($"Failed to get core for color {color}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error getting available cores: {ex}");
            }

            // Sort by name and remove duplicates
            return cores.GroupBy(c => c.ID).Select(g => g.First()).OrderBy(c => c.Root.Name).ToList();
        }

        private AbilityTree? GetCurrentAbility(PlayerAbilityType abilityType)
        {
            try
            {
                if (PlayerControl.myInstance?.actions != null)
                {
                var ability = PlayerControl.myInstance.actions.GetAbility(abilityType);
                    return ability?.AbilityTree;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error getting current ability for type {abilityType}: {ex}");
            }
            return null;
        }

        private AugmentTree? GetCurrentCore()
        {
            try
            {
                if (PlayerControl.myInstance?.actions != null)
                {
                    // Access the core field from PlayerActions
                    var actionsType = PlayerControl.myInstance.actions.GetType();
                    var coreField = actionsType.GetField("core", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (coreField != null)
                    {
                        return coreField.GetValue(PlayerControl.myInstance.actions) as AugmentTree;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error getting current core: {ex}");
            }
            return null;
        }

        private void EquipAbility(PlayerAbilityType abilityType, AbilityTree ability)
        {
            try
            {
                if (PlayerControl.myInstance?.actions != null)
                {
                    MelonLogger.Msg($"Equipping {ability.Root.Name} to {abilityType}");
                    PlayerControl.myInstance.actions.LoadAbility(abilityType, ability.Root.guid, false);
                    
                    // Save the loadout
                    Settings.SaveLoadout();
                    
                    MelonLogger.Msg($"Successfully equipped {ability.Root.Name}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error equipping ability {ability.Root.Name}: {ex}");
            }
        }

        private void EquipCore(AugmentTree core)
        {
            try
            {
                if (PlayerControl.myInstance?.actions != null)
                {
                    MelonLogger.Msg($"Equipping core: {core.Root.Name}");
                    PlayerControl.myInstance.actions.SetCore(core);
                    
                    // Save the loadout
                    Settings.SaveLoadout();
                    
                    MelonLogger.Msg($"Successfully equipped core {core.Root.Name}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error equipping core {core.Root.Name}: {ex}");
            }
        }

        private void SetCursorState(bool unlocked)
        {
            if (unlocked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private Texture2D MakeTexture(int width, int height, Color color)
        {
            // Use cached texture if available (key includes width, height, and color)
            var cacheKey = (width, height, color);
            if (textureCache.TryGetValue(cacheKey, out var cachedTexture))
            {
                return cachedTexture;
            }

            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            
            // Cache the texture
            textureCache[cacheKey] = texture;
            return texture;
        }

        // ===================== Favorites Helpers =====================
        private static string MakeAbilityFavKey(PlayerAbilityType type, AbilityTree ability) => $"A:{(int)type}:{ability.ID}";
        private static string MakeCoreFavKey(AugmentTree core) => $"C:{core.ID}";
        private static string MakeAugmentFavKey(AugmentTree aug) => $"G:{aug.ID}";

        private static void EnsurePrefs()
        {
            if (prefsCategory == null)
            {
                prefsCategory = MelonPreferences.CreateCategory(PrefsCategoryName);
            }
            if (prefsFavoritesEntry == null)
            {
                prefsFavoritesEntry = prefsCategory.CreateEntry(PrefsFavoritesKey, "");
            }
        }

        private static void LoadFavorites()
        {
            try
            {
                EnsurePrefs();
                favorites.Clear();
                var raw = prefsFavoritesEntry?.Value ?? string.Empty;
                foreach (var token in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    favorites.Add(token);
                }
                favoritesLoaded = true;
                MelonLogger.Msg($"Loaded {favorites.Count} favorites");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Failed to load favorites: {ex.Message}");
            }
        }

        private static void SaveFavorites()
        {
            try
            {
                EnsurePrefs();
                prefsFavoritesEntry.Value = string.Join(",", favorites);
                MelonPreferences.Save();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Failed to save favorites: {ex.Message}");
            }
        }

        private static bool IsFavoriteAbility(PlayerAbilityType type, AbilityTree ability)
        {
            return favorites.Contains(MakeAbilityFavKey(type, ability));
        }

        private static bool IsFavoriteCore(AugmentTree core)
        {
            return favorites.Contains(MakeCoreFavKey(core));
        }

        private static bool IsFavoriteAugment(AugmentTree aug)
        {
            return favorites.Contains(MakeAugmentFavKey(aug));
        }

        private static void ToggleFavoriteAbility(PlayerAbilityType type, AbilityTree ability)
        {
            var key = MakeAbilityFavKey(type, ability);
            if (!favorites.Remove(key)) favorites.Add(key);
            SaveFavorites();
        }

        private static void ToggleFavoriteCore(AugmentTree core)
        {
            var key = MakeCoreFavKey(core);
            if (!favorites.Remove(key)) favorites.Add(key);
            SaveFavorites();
        }

        private static void ToggleFavoriteAugment(AugmentTree aug)
        {
            var key = MakeAugmentFavKey(aug);
            if (!favorites.Remove(key)) favorites.Add(key);
            SaveFavorites();
        }
    }

    // Harmony patch class for god mode
    public static class GodModePatches
    {
        public static bool ApplyDamageImmediatePrefix()
        {
            // If god mode is enabled, prevent damage by returning false (skip original method)
            if (Class1.godModeEnabled)
            {
                return false;
            }
            // Allow normal damage processing
            return true;
        }

        public static bool DiePrefix(EntityHealth __instance)
        {
            // If god mode is enabled, check if this is the local player's health component
            if (Class1.godModeEnabled)
            {
                try
                {
                    // Use reflection to access the internal control field
                    var controlField = typeof(EntityHealth).GetField("control", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (controlField != null)
                    {
                        var control = controlField.GetValue(__instance);
                        // Only block death if this is specifically the local player's control
                        if (control != null && control == PlayerControl.myInstance)
                        {
                            return false; // Skip the original Die method for local player only
                        }
                    }
                }
                catch (Exception ex)
                {
                    // If reflection fails, we can't determine if it's the local player
                    // So we allow the death to proceed to be safe
                    MelonLoader.MelonLogger.Warning($"Failed to check player control in DiePrefix: {ex.Message}");
                }
            }
            // Allow normal death processing for all other entities or when god mode is disabled
            return true;
        }
    }

    // Harmony patch class for augment blocking
    public static class AugmentBlockPatches
    {
        public static bool AddAugmentPrefix(PlayerControl __instance)
        {
            // If augment blocking is enabled and this is the local player, prevent augments
            if (Class1.blockPlayerAugmentsEnabled && __instance == PlayerControl.myInstance)
            {
                MelonLoader.MelonLogger.Msg("Blocked augment pickup - Block Augments is enabled");
                return false; // Skip the original AddAugment method
            }
            // Allow normal augment processing
            return true;
        }
    }

    // Harmony patch class for mana consumption/drain
    public static class ManaPatches
    {
        public static bool ConsumeManaPrefix(PlayerMana __instance, float amount, bool local, ref Dictionary<MagicColor, int> __result)
        {
            // Only affect local player when toggle is enabled
            if (!(Class1.infiniteManaEnabled && __instance != null && __instance.Control == PlayerControl.myInstance))
            {
                return true; // run original
            }

            // Default result
            __result = new Dictionary<MagicColor, int>();

            try
            {
                // Respect base method behavior for edge cases
                if (__instance.Control == null || __instance.Control.IsDead || amount == 0f)
                {
                    return false; // skip original
                }

                if (amount < 0f)
                {
                    // Forward recharge behavior even when infinite mana is enabled
                    __instance.Recharge(-amount);
                    return false; // skip original
                }

                int count = (int)amount;
                if (count <= 0)
                {
                    return false; // skip original
                }

                // Determine which mana colors would be used without actually consuming them
                var colors = __instance.GetNextMana(count);

                // Build the same dictionary the original method would return
                foreach (var color in colors)
                {
                    if (!__result.ContainsKey(color))
                    {
                        __result[color] = 0;
                    }
                    __result[color]++;

                    // Trigger colored mana effects when requested locally (mirror OnManaUsed behavior)
                    if (local)
                    {
                        try
                        {
                            var props = new EffectProperties();
                            props.StartLoc = (props.OutLoc = __instance.Control.Display.CenterLocation);
                            props.SourceControl = __instance.Control;
                            props.Affected = __instance.Control.gameObject;
                            props.AddMana(color, 1);
                            __instance.Control.TriggerSnippets(EventTrigger.ManaUsed, props, 1f);

                            if (__instance.Control == PlayerControl.myInstance && color != MagicColor.Neutral && color != MagicColor.Any)
                            {
                                GameStats.IncrementStat(color, GameStats.SignatureStat.ManaSpent, 1U, false);
                            }
                        }
                        catch (Exception ex)
                        {
                            MelonLoader.MelonLogger.Warning($"Failed to trigger mana used effects: {ex.Message}");
                        }
                    }
                }

                // Skip original so no real consumption happens
                return false;
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"Infinite mana prefix failed, falling back to original: {ex.Message}");
                return true; // run original if something went wrong
            }
        }

        public static bool DrainPrefix(PlayerMana __instance)
        {
            // Prevent draining all mana if enabled for local player
            if (Class1.infiniteManaEnabled && __instance != null && __instance.Control == PlayerControl.myInstance)
            {
                return false; // skip original Drain
            }
            return true;
        }
    }
}
