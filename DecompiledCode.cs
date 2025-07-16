using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using REPOLib.Modules;
using UnityEngine;
using Newtonsoft.Json;

namespace ItemSpawner
{
    [BepInPlugin("com.spoopylocal.itemspawner", "ItemSpawner", "2.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Logger { get; private set; }

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            Logger.LogInfo("ItemSpawner Enhanced has awoken!");

            var harmony = new Harmony("com.spoopylocal.itemspawner");
            harmony.PatchAll();
            Logger.LogInfo("ItemSpawner Enhanced loaded and patches applied.");

            StartCoroutine(WaitForStatsManager());
        }

        private IEnumerator WaitForStatsManager()
        {
            while (StatsManager.instance == null)
            {
                Debug.LogWarning("[ItemSpawner] Waiting for StatsManager to initialize...");
                yield return new WaitForSeconds(0.5f);
            }
            Logger.LogInfo("[ItemSpawner] StatsManager is now available!");
        }
    }

    [System.Serializable]
    public class SpawnableItem
    {
        public string name;
        public string displayName;
        public string category;
        public string path;
        public bool isItem;
        public bool isFavorite;

        public SpawnableItem(string name, string displayName, string category, string path, bool isItem)
        {
            this.name = name;
            this.displayName = displayName;
            this.category = category;
            this.path = path;
            this.isItem = isItem;
            this.isFavorite = false;
        }
    }

    public class EnhancedSpawnGUI : MonoBehaviour
    {
        private bool showSpawnGUI = false;
        private string searchText = "";
        private Vector2 scrollPosition = Vector2.zero;
        private Vector2 favoritesScrollPosition = Vector2.zero;

        private List<SpawnableItem> allItems = new List<SpawnableItem>();
        private List<SpawnableItem> filteredItems = new List<SpawnableItem>();
        private List<SpawnableItem> favoriteItems = new List<SpawnableItem>();

        private bool showFavorites = true;
        private bool showSearch = true;
        private string favoritesFilePath;

        // Keyboard navigation
        private int selectedIndex = -1;
        private bool isInFavorites = false; // True if selection is in favorites, false if in search results
        private List<SpawnableItem> currentDisplayList = new List<SpawnableItem>(); // The list currently being navigated

        // Input handling
        private bool f1KeyPressed = false;
        private bool f1KeyWasPressed = false;
        private bool upKeyPressed = false;
        private bool upKeyWasPressed = false;
        private bool downKeyPressed = false;
        private bool downKeyWasPressed = false;
        private bool enterKeyPressed = false;
        private bool enterKeyWasPressed = false;
        private bool isSearchFieldFocused = false;

        private void Start()
        {
            favoritesFilePath = Path.Combine(Application.persistentDataPath, "ItemSpawner_Favorites.json");
            StartCoroutine(InitializeItemsWhenReady());
        }

        private IEnumerator InitializeItemsWhenReady()
        {
            while (StatsManager.instance == null)
            {
                yield return new WaitForSeconds(0.1f);
            }

            yield return new WaitForSeconds(1f);
            InitializeItems();
            LoadFavorites();
        }

        private void InitializeItems()
        {
            allItems.Clear();

            try
            {
                var registeredItems = Items.RegisteredItems;
                if (registeredItems != null)
                {
                    foreach (var item in registeredItems)
                    {
                        if (item != null && !string.IsNullOrEmpty(item.name))
                        {
                            string displayName = item.name.Replace("(Clone)", "").Trim();
                            allItems.Add(new SpawnableItem(item.name, displayName, "Items", "Items/" + item.name, true));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error loading registered items: {e.Message}");
            }

            try
            {
                var registeredValuables = Valuables.RegisteredValuables;
                if (registeredValuables != null)
                {
                    foreach (var valuable in registeredValuables)
                    {
                        if (valuable != null && !string.IsNullOrEmpty(valuable.name))
                        {
                            string displayName = valuable.name.Replace("(Clone)", "").Trim();
                            string path = "";
                            try
                            {
                                path = ResourcesHelper.GetValuablePrefabPath(valuable);
                            }
                            catch
                            {
                                path = "Valuables/" + valuable.name;
                            }
                            allItems.Add(new SpawnableItem(valuable.name, displayName, "Valuables", path, false));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error loading registered valuables: {e.Message}");
            }

            ScanResourcesFolder();
            filteredItems = new List<SpawnableItem>(allItems);
            Debug.Log($"ItemSpawner: Loaded {allItems.Count} items total");
        }

        private void ScanResourcesFolder()
        {
            string[] valuableCategories = { "01 Tiny", "02 Small", "03 Medium", "04 Big", "05 Wide", "06 Tall", "07 Very Tall" };

            foreach (string category in valuableCategories)
            {
                try
                {
                    var valuables = Resources.LoadAll<GameObject>("Valuables/" + category);
                    foreach (var valuable in valuables)
                    {
                        if (valuable != null && !allItems.Any(item => item.name == valuable.name))
                        {
                            string displayName = valuable.name.Replace("(Clone)", "").Trim();
                            allItems.Add(new SpawnableItem(valuable.name, displayName, category, "Valuables/" + category + "/" + valuable.name, false));
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Could not load valuables from category {category}: {e.Message}");
                }
            }

            try
            {
                var items = Resources.LoadAll<GameObject>("Items");
                foreach (var item in items)
                {
                    if (item != null && !allItems.Any(existingItem => existingItem.name == item.name))
                    {
                        string displayName = item.name.Replace("(Clone)", "").Trim();
                        allItems.Add(new SpawnableItem(item.name, displayName, "Items", "Items/" + item.name, true));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not load items: {e.Message}");
            }

            try
            {
                var removedItems = Resources.LoadAll<GameObject>("Items/Removed Items");
                foreach (var item in removedItems)
                {
                    if (item != null && !allItems.Any(existingItem => existingItem.name == item.name))
                    {
                        string displayName = item.name.Replace("(Clone)", "").Trim();
                        allItems.Add(new SpawnableItem(item.name, displayName, "Removed Items", "Items/Removed Items/" + item.name, true));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not load removed items: {e.Message}");
            }
        }

        private void Update()
        {
            // F1 key handling
            f1KeyWasPressed = f1KeyPressed;
            f1KeyPressed = Input.GetKey(KeyCode.F1);

            if (f1KeyPressed && !f1KeyWasPressed)
            {
                showSpawnGUI = !showSpawnGUI;
                if (showSpawnGUI)
                {
                    selectedIndex = -1; // Reset selection when opening
                    isInFavorites = false;
                    GUI.FocusControl(null); // Ensure no control is focused when GUI opens
                }
                Debug.Log("Enhanced Spawn GUI toggled: " + (showSpawnGUI ? "Shown" : "Hidden"));
            }

            if (!showSpawnGUI) return;

            // Capture arrow and enter key states
            upKeyWasPressed = upKeyPressed;
            upKeyPressed = Input.GetKey(KeyCode.UpArrow);
            downKeyWasPressed = downKeyPressed;
            downKeyPressed = Input.GetKey(KeyCode.DownArrow);
            enterKeyWasPressed = enterKeyPressed;
            enterKeyPressed = Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter);

            // Handle navigation/spawn
            if (upKeyPressed && !upKeyWasPressed)
            {
                if (isSearchFieldFocused)
                {
                    GUI.FocusControl(null); // Unfocus search bar
                    isSearchFieldFocused = false; // Update internal state
                }
                NavigateUp();
            }
            else if (downKeyPressed && !downKeyWasPressed)
            {
                if (isSearchFieldFocused)
                {
                    GUI.FocusControl(null); // Unfocus search bar
                    isSearchFieldFocused = false; // Update internal state
                }
                NavigateDown();
            }
            else if (enterKeyPressed && !enterKeyWasPressed)
            {
                if (isSearchFieldFocused)
                {
                    // If Enter is pressed while search field is focused, unfocus it
                    // and then try to spawn if an item is selected.
                    GUI.FocusControl(null);
                    isSearchFieldFocused = false;
                }
                SpawnSelectedItem();
            }
        }

        private void NavigateUp()
        {
            UpdateCurrentDisplayList();

            if (currentDisplayList.Count == 0) return;

            if (selectedIndex <= 0)
            {
                // If at top of current section or no selection, try to go to bottom of other section
                if (isInFavorites && showSearch && filteredItems.Count > 0)
                {
                    isInFavorites = false;
                    selectedIndex = filteredItems.Count - 1;
                    scrollPosition.y = float.MaxValue; // Scroll to bottom of search results
                }
                else if (!isInFavorites && showFavorites && favoriteItems.Count > 0)
                {
                    isInFavorites = true;
                    selectedIndex = favoriteItems.Count - 1;
                    favoritesScrollPosition.y = float.MaxValue; // Scroll to bottom of favorites
                }
                else
                {
                    selectedIndex = currentDisplayList.Count - 1; // Wrap around within current list
                }
            }
            else
            {
                selectedIndex--;
            }
            ScrollToSelectedItem();
        }

        private void NavigateDown()
        {
            UpdateCurrentDisplayList();

            if (currentDisplayList.Count == 0) return;

            if (selectedIndex >= currentDisplayList.Count - 1)
            {
                // If at bottom of current section, try to go to top of other section
                if (!isInFavorites && showFavorites && favoriteItems.Count > 0)
                {
                    isInFavorites = true;
                    selectedIndex = 0;
                    favoritesScrollPosition.y = 0; // Scroll to top of favorites
                }
                else if (isInFavorites && showSearch && filteredItems.Count > 0)
                {
                    isInFavorites = false;
                    selectedIndex = 0;
                    scrollPosition.y = 0; // Scroll to top of search results
                }
                else
                {
                    selectedIndex = 0; // Wrap around within current list
                }
            }
            else
            {
                selectedIndex++;
            }
            ScrollToSelectedItem();
        }

        private void UpdateCurrentDisplayList()
        {
            // Prioritize search results if search is active and has results
            if (showSearch && filteredItems.Count > 0 && !isInFavorites)
            {
                currentDisplayList = filteredItems;
            }
            // Otherwise, if favorites are shown and have results, use them
            else if (showFavorites && favoriteItems.Count > 0 && isInFavorites)
            {
                currentDisplayList = favoriteItems;
            }
            // Fallback if current section is empty or hidden
            else if (showSearch && filteredItems.Count > 0)
            {
                isInFavorites = false;
                currentDisplayList = filteredItems;
            }
            else if (showFavorites && favoriteItems.Count > 0)
            {
                isInFavorites = true;
                currentDisplayList = favoriteItems;
            }
            else
            {
                currentDisplayList = new List<SpawnableItem>(); // No items to display
                selectedIndex = -1;
            }

            // Ensure selectedIndex is valid for the current list
            if (selectedIndex >= currentDisplayList.Count)
            {
                selectedIndex = currentDisplayList.Count > 0 ? currentDisplayList.Count - 1 : -1;
            }
            if (selectedIndex == -1 && currentDisplayList.Count > 0)
            {
                selectedIndex = 0; // Select first item if nothing is selected and list has items
            }
        }

        private void ScrollToSelectedItem()
        {
            if (selectedIndex == -1 || currentDisplayList.Count == 0) return;

            float itemHeight = 25f; // Approximate height of each item button
            float viewHeight = 150f; // Height of the scroll view

            if (!isInFavorites) // Search results
            {
                float targetScrollY = selectedIndex * itemHeight;
                if (targetScrollY < scrollPosition.y)
                {
                    scrollPosition.y = targetScrollY;
                }
                else if (targetScrollY + itemHeight > scrollPosition.y + viewHeight)
                {
                    scrollPosition.y = targetScrollY - viewHeight + itemHeight;
                }
            }
            else // Favorites
            {
                float targetScrollY = selectedIndex * itemHeight;
                if (targetScrollY < favoritesScrollPosition.y)
                {
                    favoritesScrollPosition.y = targetScrollY;
                }
                else if (targetScrollY + itemHeight > favoritesScrollPosition.y + viewHeight)
                {
                    favoritesScrollPosition.y = targetScrollY - viewHeight + itemHeight;
                }
            }
        }

        private void SpawnSelectedItem()
        {
            UpdateCurrentDisplayList(); // Ensure currentDisplayList is up-to-date

            if (selectedIndex >= 0 && selectedIndex < currentDisplayList.Count)
            {
                SpawnItem(currentDisplayList[selectedIndex]);
            }
        }

        private void OnGUI()
        {
            if (!showSpawnGUI) return;

            GUILayout.BeginArea(new Rect(10, 10, 600, 500), "Enhanced Item Spawner", GUI.skin.window);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(showSearch ? "Hide Search" : "Show Search", GUILayout.Width(100)))
            {
                showSearch = !showSearch;
                selectedIndex = -1; // Reset selection
                isInFavorites = false;
            }
            if (GUILayout.Button(showFavorites ? "Hide Favorites" : "Show Favorites", GUILayout.Width(120)))
            {
                showFavorites = !showFavorites;
                selectedIndex = -1; // Reset selection
                isInFavorites = false;
            }
            GUILayout.EndHorizontal();

            if (showSearch)
            {
                GUILayout.Space(10);
                GUILayout.Label("Search Items:");

                GUILayout.BeginHorizontal();
                GUI.SetNextControlName("SearchField");
                string newSearchText = GUILayout.TextField(searchText, GUILayout.ExpandWidth(true));
                if (newSearchText != searchText)
                {
                    searchText = newSearchText;
                    FilterItems();
                    selectedIndex = -1; // Reset selection when search changes
                    isInFavorites = false; // Ensure selection is in search results
                }

                if (GUILayout.Button("Clear", GUILayout.Width(50)))
                {
                    searchText = "";
                    FilterItems();
                    selectedIndex = -1;
                    isInFavorites = false;
                    GUI.FocusControl(null); // Remove focus from search field
                }
                GUILayout.EndHorizontal();

                // Check if search field is focused
                isSearchFieldFocused = GUI.GetNameOfFocusedControl() == "SearchField";

                GUILayout.Label($"Search Results ({filteredItems.Count} items):");
                scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(150));

                for (int i = 0; i < filteredItems.Count; i++)
                {
                    var item = filteredItems[i];
                    bool isSelected = !isInFavorites && selectedIndex == i;

                    // Highlight selected item with orange-like color
                    if (isSelected)
                    {
                        GUI.backgroundColor = new Color(1f, 0.65f, 0f, 1f); // Orange color
                    }

                    GUILayout.BeginHorizontal();

                    string favText = item.isFavorite ? "★" : "☆";
                    if (GUILayout.Button(favText, GUILayout.Width(25)))
                    {
                        ToggleFavorite(item);
                    }

                    if (GUILayout.Button($"{item.displayName} ({item.category})"))
                    {
                        SpawnItem(item);
                    }

                    GUILayout.EndHorizontal();

                    if (isSelected)
                    {
                        GUI.backgroundColor = Color.white; // Reset color
                    }
                }

                GUILayout.EndScrollView();
            }

            if (showFavorites)
            {
                GUILayout.Space(10);
                GUILayout.Label($"Favorite Items ({favoriteItems.Count} items):");

                favoritesScrollPosition = GUILayout.BeginScrollView(favoritesScrollPosition, GUILayout.Height(150));

                for (int i = 0; i < favoriteItems.Count; i++)
                {
                    var item = favoriteItems[i];
                    bool isSelected = isInFavorites && selectedIndex == i;

                    // Highlight selected item with orange-like color
                    if (isSelected)
                    {
                        GUI.backgroundColor = new Color(1f, 0.65f, 0f, 1f); // Orange color
                    }

                    GUILayout.BeginHorizontal();

                    if (GUILayout.Button("✖", GUILayout.Width(25)))
                    {
                        ToggleFavorite(item);
                    }

                    if (GUILayout.Button($"{item.displayName} ({item.category})"))
                    {
                        SpawnItem(item);
                    }

                    GUILayout.EndHorizontal();

                    if (isSelected)
                    {
                        GUI.backgroundColor = Color.white; // Reset color
                    }
                }

                GUILayout.EndScrollView();
            }

            GUILayout.Space(10);
            GUILayout.Label("Controls: F1=Toggle | ↑↓=Navigate | Enter=Spawn | ★/☆=Favorite | ✖=Remove");

            GUILayout.EndArea();
        }

        private void FilterItems()
        {
            if (string.IsNullOrEmpty(searchText))
            {
                filteredItems = new List<SpawnableItem>(allItems);
            }
            else
            {
                filteredItems = allItems.Where(item =>
                    item.displayName.ToLower().Contains(searchText.ToLower()) ||
                    item.category.ToLower().Contains(searchText.ToLower())
                ).ToList();
            }
        }

        private void ToggleFavorite(SpawnableItem item)
        {
            item.isFavorite = !item.isFavorite;

            if (item.isFavorite)
            {
                if (!favoriteItems.Contains(item))
                {
                    favoriteItems.Add(item);
                }
            }
            else
            {
                favoriteItems.Remove(item);
            }

            SaveFavorites();
        }

        private void SaveFavorites()
        {
            try
            {
                var favoriteNames = favoriteItems.Select(item => item.name).ToList();
                string json = JsonConvert.SerializeObject(favoriteNames, Formatting.Indented);
                File.WriteAllText(favoritesFilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save favorites: {e.Message}");
            }
        }

        private void LoadFavorites()
        {
            try
            {
                if (File.Exists(favoritesFilePath))
                {
                    string json = File.ReadAllText(favoritesFilePath);
                    var favoriteNames = JsonConvert.DeserializeObject<List<string>>(json);

                    if (favoriteNames != null)
                    {
                        foreach (string favoriteName in favoriteNames)
                        {
                            var item = allItems.FirstOrDefault(i => i.name == favoriteName);
                            if (item != null)
                            {
                                item.isFavorite = true;
                                favoriteItems.Add(item);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load favorites: {e.Message}");
            }
        }

        private void SpawnItem(SpawnableItem spawnableItem)
        {
            if (Camera.main == null)
            {
                Debug.LogError("No main camera found!");
                return;
            }

            var cameraTransform = Camera.main.transform;
            var spawnPosition = cameraTransform.position + cameraTransform.forward * 2f;
            var spawnRotation = Quaternion.identity;

            try
            {
                if (spawnableItem.isItem)
                {
                    var registeredItems = Items.RegisteredItems;
                    var item = registeredItems?.FirstOrDefault(i => i.name == spawnableItem.name);

                    if (item != null && item.prefab != null)
                    {
                        if (SemiFunc.IsMultiplayer())
                        {
                            PhotonNetwork.InstantiateRoomObject(spawnableItem.path, spawnPosition, spawnRotation, 0, null);
                        }
                        else
                        {
                            Instantiate(item.prefab, spawnPosition, spawnRotation);
                        }
                        Debug.Log($"Spawned item: {spawnableItem.displayName}");
                        return;
                    }
                }
                else
                {
                    var registeredValuables = Valuables.RegisteredValuables;
                    var valuable = registeredValuables?.FirstOrDefault(v => v.name == spawnableItem.name);

                    if (valuable != null)
                    {
                        if (SemiFunc.IsMultiplayer())
                        {
                            PhotonNetwork.InstantiateRoomObject(spawnableItem.path, spawnPosition, spawnRotation, 0, null);
                        }
                        else
                        {
                            Instantiate(valuable, spawnPosition, spawnRotation);
                        }
                        Debug.Log($"Spawned valuable: {spawnableItem.displayName}");
                        return;
                    }
                }

                // Fallback: try to load from Resources
                var prefab = Resources.Load<GameObject>(spawnableItem.path);
                if (prefab != null)
                {
                    if (SemiFunc.IsMultiplayer())
                    {
                        PhotonNetwork.InstantiateRoomObject(spawnableItem.path, spawnPosition, spawnRotation, 0, null);
                    }
                    else
                    {
                        Instantiate(prefab, spawnPosition, spawnRotation);
                    }
                    Debug.Log($"Spawned from Resources: {spawnableItem.displayName}");
                }
                else
                {
                    Debug.LogWarning($"Failed to spawn item: {spawnableItem.displayName} - prefab not found at path: {spawnableItem.path}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error spawning {spawnableItem.displayName}: {e.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ShopManager), "Awake")]
    public static class ShopManager_Awake_Patch
    {
        private static void Postfix(ShopManager __instance)
        {
            if (__instance.gameObject.GetComponent<EnhancedSpawnGUI>() == null)
            {
                __instance.gameObject.AddComponent<EnhancedSpawnGUI>();
                Debug.Log("EnhancedSpawnGUI added via Harmony patch.");
            }
        }
    }
}
