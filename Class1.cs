using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace TheCodex
{
    public class Class1 : MelonMod
    {
        private bool showDropdown = false;
        private Rect windowRect = new Rect(20, 20, 500, 600);
        private Vector2 scrollPosition = Vector2.zero;
        
        // Tab system
        private AbilityTabType currentTab = AbilityTabType.Primary;
        private readonly string[] tabNames = { "Primary", "Secondary", "Utility", "Movement", "Core", "Augments" };

        // Cache the full ability lists to prevent them from being overwritten
        private static readonly Dictionary<PlayerAbilityType, List<AbilityTree>> cachedFullAbilityLists = new Dictionary<PlayerAbilityType, List<AbilityTree>>();
        private static readonly List<AugmentTree> cachedFullCoreList = new List<AugmentTree>();
        private static bool hasInitializedCache = false;

        public enum AbilityTabType
        {
            Primary,
            Secondary,
            Utility,
            Movement,
            Core,
            AllAugments
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
                }
            }

            // Force cursor state every frame while menu is open
            if (showDropdown)
            {
                SetCursorState(true);
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

        public override void OnGUI()
        {
            if (showDropdown)
            {
                // Prevent any mouse events from affecting the game behind the GUI
                GUI.FocusWindow(0);
                
                // Solid color style for the window
                GUIStyle windowStyle = new GUIStyle(GUI.skin.window);
                windowStyle.normal.background = MakeTexture(2, 2, new Color(0.12f, 0.12f, 0.12f, 1f));
                // Fix border issues by setting all states to the same background
                windowStyle.hover.background = windowStyle.normal.background;
                windowStyle.active.background = windowStyle.normal.background;
                windowStyle.focused.background = windowStyle.normal.background;
                windowStyle.onNormal.background = windowStyle.normal.background;
                windowStyle.onHover.background = windowStyle.normal.background;
                windowStyle.onActive.background = windowStyle.normal.background;
                windowStyle.onFocused.background = windowStyle.normal.background;
                // Fix title text color issues
                windowStyle.normal.textColor = Color.white;
                windowStyle.hover.textColor = Color.white;
                windowStyle.active.textColor = Color.white;
                windowStyle.focused.textColor = Color.white;
                windowStyle.onNormal.textColor = Color.white;
                windowStyle.onHover.textColor = Color.white;
                windowStyle.onActive.textColor = Color.white;
                windowStyle.onFocused.textColor = Color.white;

                GUI.depth = 0;
                windowRect = GUILayout.Window(0, windowRect, DrawWindow, "The Codex", windowStyle);
            }
        }

        private void DrawWindow(int windowID)
        {
            // Tab button style
            GUIStyle tabStyle = new GUIStyle(GUI.skin.button);
            tabStyle.normal.background = MakeTexture(2, 2, new Color(0.2f, 0.2f, 0.2f, 1f));
            tabStyle.normal.textColor = Color.white;
            tabStyle.fontSize = 14;
            tabStyle.fixedHeight = 30;
            // Fix button transparency issues
            tabStyle.hover.background = MakeTexture(2, 2, new Color(0.3f, 0.3f, 0.3f, 1f));
            tabStyle.active.background = tabStyle.normal.background;

            // Selected tab style
            GUIStyle selectedTabStyle = new GUIStyle(tabStyle);
            selectedTabStyle.normal.background = MakeTexture(2, 2, new Color(0.4f, 0.4f, 0.4f, 1f));
            selectedTabStyle.normal.textColor = Color.yellow;
            selectedTabStyle.hover.background = selectedTabStyle.normal.background;
            selectedTabStyle.active.background = selectedTabStyle.normal.background;

            // Draw tab buttons
            GUILayout.BeginHorizontal();
            for (int i = 0; i < tabNames.Length; i++)
            {
                AbilityTabType tabType = (AbilityTabType)i;
                GUIStyle styleToUse = (currentTab == tabType) ? selectedTabStyle : tabStyle;
                
                if (GUILayout.Button(tabNames[i], styleToUse, GUILayout.ExpandWidth(true)))
                {
                    currentTab = tabType;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

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
            }

            GUILayout.EndScrollView();

            // Make window draggable
            GUI.DragWindow();
        }

        private void DrawAbilityTab(PlayerAbilityType abilityType, string tabTitle)
        {
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontSize = 18;
            headerStyle.normal.textColor = Color.white;
            headerStyle.fontStyle = FontStyle.Bold;

            GUILayout.Label(tabTitle, headerStyle);
            GUILayout.Space(10);

            // Show current equipped ability
            var currentAbility = GetCurrentAbility(abilityType);
            if (currentAbility != null)
            {
                GUIStyle currentStyle = new GUIStyle(GUI.skin.box);
                currentStyle.normal.background = MakeTexture(2, 2, new Color(0.1f, 0.3f, 0.1f, 1f));
                currentStyle.normal.textColor = Color.green;
                currentStyle.fontSize = 14;
                
                GUILayout.BeginVertical(currentStyle);
                GUILayout.Label($"Currently Equipped: {currentAbility.Root.Name}", currentStyle);
                GUILayout.EndVertical();
                GUILayout.Space(10);
            }

            // Get abilities for this type - use cached version
            var abilities = GetAbilitiesForType(abilityType);
            
            if (abilities.Count == 0)
            {
                GUILayout.Label("No abilities available for this type.", GUI.skin.label);
                return;
            }

            // Button style for abilities
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.normal.background = MakeTexture(2, 2, new Color(0.25f, 0.25f, 0.25f, 1f));
            buttonStyle.normal.textColor = Color.white;
            buttonStyle.fontSize = 16;
            buttonStyle.fixedHeight = 40;
            // Fix button transparency issues
            buttonStyle.hover.background = MakeTexture(2, 2, new Color(0.35f, 0.35f, 0.35f, 1f));
            buttonStyle.active.background = buttonStyle.normal.background;

            foreach (var ability in abilities)
            {
                string buttonText = ability.Root.Name;
                bool isEquipped = currentAbility != null && currentAbility.ID == ability.ID;
                
                if (isEquipped)
                {
                    buttonStyle.normal.textColor = Color.green;
                    buttonText += " (Equipped)";
                }
                else
                {
                    buttonStyle.normal.textColor = Color.white;
                }

                if (GUILayout.Button(buttonText, buttonStyle, GUILayout.ExpandWidth(true)))
                {
                    if (!isEquipped)
                    {
                        EquipAbility(abilityType, ability);
                    }
                }
            }
        }

        private void DrawCoreTab()
        {
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontSize = 18;
            headerStyle.normal.textColor = Color.white;
            headerStyle.fontStyle = FontStyle.Bold;

            GUILayout.Label("Core Selection", headerStyle);
            GUILayout.Space(10);

            // Show current equipped core
            var currentCore = GetCurrentCore();
            if (currentCore != null)
            {
                GUIStyle currentStyle = new GUIStyle(GUI.skin.box);
                currentStyle.normal.background = MakeTexture(2, 2, new Color(0.1f, 0.3f, 0.1f, 1f));
                currentStyle.normal.textColor = Color.green;
                currentStyle.fontSize = 14;
                
                GUILayout.BeginVertical(currentStyle);
                GUILayout.Label($"Currently Equipped: {currentCore.Root.Name}", currentStyle);
                GUILayout.EndVertical();
                GUILayout.Space(10);
            }

            // Get available cores - use cached version
            var cores = GetAvailableCores();
            
            if (cores.Count == 0)
            {
                GUILayout.Label("No cores available.", GUI.skin.label);
                return;
            }

            // Button style for cores
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.normal.background = MakeTexture(2, 2, new Color(0.25f, 0.25f, 0.25f, 1f));
            buttonStyle.normal.textColor = Color.white;
            buttonStyle.fontSize = 16;
            buttonStyle.fixedHeight = 40;
            // Fix button transparency issues
            buttonStyle.hover.background = MakeTexture(2, 2, new Color(0.35f, 0.35f, 0.35f, 1f));
            buttonStyle.active.background = buttonStyle.normal.background;

            foreach (var core in cores)
            {
                string buttonText = core.Root.Name;
                bool isEquipped = currentCore != null && currentCore.ID == core.ID;
                
                if (isEquipped)
                {
                    buttonStyle.normal.textColor = Color.green;
                    buttonText += " (Equipped)";
                }
                else
                {
                    buttonStyle.normal.textColor = Color.white;
                }

                if (GUILayout.Button(buttonText, buttonStyle, GUILayout.ExpandWidth(true)))
                {
                    if (!isEquipped)
                    {
                        EquipCore(core);
                    }
                }
            }
        }

        private void DrawAllAugmentsTab()
        {
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontSize = 18;
            headerStyle.normal.textColor = Color.white;
            headerStyle.fontStyle = FontStyle.Bold;

            GUILayout.Label("All Augments", headerStyle);
            GUILayout.Space(10);

            // Button style for augments
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.normal.background = MakeTexture(2, 2, new Color(0.25f, 0.25f, 0.25f, 1f));
            buttonStyle.normal.textColor = Color.white;
            buttonStyle.fontSize = 16;
            buttonStyle.fixedHeight = 35;
            // Fix button transparency issues
            buttonStyle.hover.background = MakeTexture(2, 2, new Color(0.35f, 0.35f, 0.35f, 1f));
            buttonStyle.active.background = buttonStyle.normal.background;

            // Use the augment trees directly instead of names
            foreach (var augment in GetAddableAugmentTrees())
            {
                string displayName = augment.Root?.Name ?? "Unnamed Augment";
                if (GUILayout.Button(displayName, buttonStyle, GUILayout.ExpandWidth(true)))
                {
                    MelonLogger.Msg($"Adding augment: {displayName} (ID: {augment.ID})");
                    AddAugment(augment);
                }
            }
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
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
