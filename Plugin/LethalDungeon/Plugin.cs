﻿using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DunGen;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using static LethalLib.Modules.Levels;

namespace LethalDungeon
{
    [BepInPlugin(modGUID, modName, modVersion)]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    public class LethalDungeon : BaseUnityPlugin
    {
        private const string modGUID = "LethalDungeon";
        private const string modName = "LethalDungeon";
        private const string modVersion = "1.0.0";

        private readonly Harmony harmony = new Harmony(modGUID);

        private static LethalDungeon Instance;

        internal ManualLogSource mls;

        public static AssetBundle DungeonAssets;

        // Configs
        private ConfigEntry<int> configRarity;
        private ConfigEntry<string> configMoons;
        private ConfigEntry<bool> configGuaranteed;

        private string[] MoonConfigs = 
        {
            "all",
            "paid",
            "easy",
            "titan",
            "rend",
            "dine",
            "experimentation",
            "assurance",
            "vow",
            "offense",
            "march",
        };

        private void Awake()
        {
            if (Instance == null) 
            {
                Instance = this;
            }

            mls = BepInEx.Logging.Logger.CreateLogSource(modGUID);

            string sAssemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            DungeonAssets = AssetBundle.LoadFromFile(Path.Combine(sAssemblyLocation, "exampledungeon"));
            if (DungeonAssets == null) 
            {
                mls.LogError("Failed to load Dungeon assets.");
                return;
            }

            harmony.PatchAll(typeof(LethalDungeon));
            harmony.PatchAll(typeof(RoundManagerPatch));

            // Config setup
            configRarity = Config.Bind("General", 
                "Rarity", 
                100, 
                new ConfigDescription("How rare it is for the dungeon to be chosen. Higher values increases the chance of spawning the dungeon.", 
                new AcceptableValueRange<int>(0, 300)));
            configMoons = Config.Bind("General", 
                "Moons", 
                "all", 
                new ConfigDescription("The moon(s) that the dungeon can spawn on, from the given presets.", 
                new AcceptableValueList<string>(MoonConfigs)));
            configGuaranteed = Config.Bind("General", 
                "Guaranteed", 
                false, 
                new ConfigDescription("If enabled, the dungeon will be effectively guaranteed to spawn. Only recommended for debugging/sightseeing purposes."));

            DunGen.Graph.DungeonFlow DungeonFlow = DungeonAssets.LoadAsset<DunGen.Graph.DungeonFlow>("assets/Example/Flow/ExampleFlow.asset");
            if (DungeonFlow == null) 
            {
                mls.LogError("Failed to load Dungeon Flow.");
                return;
            }

            string sMoonType = configMoons.Value.ToLower(); // Convert to lower just in case the user put in caps characters by accident, for leniency
            LevelTypes LevelType = GetLevelTypeFromMoonConfig(sMoonType);
            if (LevelType == LevelTypes.None) 
            {
                mls.LogError("Config file invalid, moon config does not match one of the preset values.");
                return;
            }
            mls.LogInfo($"Moon type string \"{sMoonType}\" got type(s) {LevelType}");

            LethalLib.Extras.DungeonDef DungeonDef = ScriptableObject.CreateInstance<LethalLib.Extras.DungeonDef>();
            DungeonDef.dungeonFlow = DungeonFlow;
            DungeonDef.rarity = configGuaranteed.Value ? 99999 : configRarity.Value; // Set to a value so high it is pretty hard for it not to be chosen.
            //DungeonDef.firstTimeDungeonAudio = DungeonAssets.LoadAsset<AudioClip>("TODO?");

            LethalLib.Modules.Dungeon.AddDungeon(DungeonDef, LevelType);

            mls.LogInfo($"Lethal Dungeon for Lethal Company [Version {modVersion}] successfully loaded.");
        }

        private LevelTypes GetLevelTypeFromMoonConfig(string sConfigName)
        {
            switch (sConfigName)
            {
                // Special names to use several at once
                case "all": return (LevelTypes.ExperimentationLevel | LevelTypes.AssuranceLevel | LevelTypes.VowLevel | LevelTypes.OffenseLevel | LevelTypes.MarchLevel |
                                    LevelTypes.RendLevel | LevelTypes.DineLevel | LevelTypes.TitanLevel);
                case "paid": return (LevelTypes.TitanLevel | LevelTypes.DineLevel | LevelTypes.RendLevel);
                case "easy": return (LevelTypes.ExperimentationLevel | LevelTypes.AssuranceLevel | LevelTypes.VowLevel | LevelTypes.OffenseLevel | LevelTypes.MarchLevel);

                // Single moons
                case "titan": 
                    return LevelTypes.TitanLevel;
                case "rend": 
                    return LevelTypes.RendLevel;
                case "dine": 
                    return LevelTypes.DineLevel;
                case "experimentation": 
                    return LevelTypes.ExperimentationLevel;
                case "assurance": 
                    return LevelTypes.AssuranceLevel;
                case "vow": 
                    return LevelTypes.VowLevel;
                case "offense": 
                    return LevelTypes.OffenseLevel;
                case "march": 
                    return LevelTypes.MarchLevel;
                default: 
                    return LevelTypes.None;
            }
        }

        // Patch to update our dummy objects (entrances, vents, turrets, mines, scrap, storage shelving) with the real prefab references
        [HarmonyPatch(typeof(RoundManager))]
        internal class RoundManagerPatch
        {
            // After generating the dungeon fix up the sync'd objects which contain our dummies with the real prefabs
            [HarmonyPatch("GenerateNewFloor")]
            [HarmonyPostfix]
            static void GenerateNewFloor(ref RuntimeDungeon ___dungeonGenerator)
            {
                if (___dungeonGenerator.Generator.DungeonFlow.name != "ExampleFlow")
                {
                    return;
                }
                Instance.mls.LogInfo("Attempting to fix entrance teleporters.");
                SpawnSyncedObject[] SyncedObjects = FindObjectsOfType<SpawnSyncedObject>();
                NetworkManager networkManager = FindObjectOfType<NetworkManager>();
                NetworkPrefab realVentPrefab = networkManager.NetworkConfig.Prefabs.Prefabs.First(x => x.Prefab.name == "VentEntrance");
                if (realVentPrefab == null) 
                {
                    Instance.mls.LogError("Failed to find VentEntrance prefab.");
                    return;
                }

                NetworkPrefab realEntranceAPrefab = networkManager.NetworkConfig.Prefabs.Prefabs.First(x => x.Prefab.name == "EntranceTeleportA");
                if (realEntranceAPrefab == null)
                {
                    Instance.mls.LogError("Failed to find EntranceTeleportA prefab.");
                    return;
                }

                NetworkPrefab realEntranceBPrefab = networkManager.NetworkConfig.Prefabs.Prefabs.First(x => x.Prefab.name == "EntranceTeleportB");
                if (realEntranceBPrefab == null)
                {
                    Instance.mls.LogError("Failed to find EntranceTeleportB prefab.");
                    return;
                }

                NetworkPrefab realStorageShelfPrefab = networkManager.NetworkConfig.Prefabs.Prefabs.First(x => x.Prefab.name == "StorageShelfContainer");
                if (realStorageShelfPrefab == null)
                {
                    Instance.mls.LogError("Failed to find StorageShelfContainer prefab.");
                    return;
                }

                bool bFoundEntranceA = false;
                bool bFoundEntranceB = false;
                int iVentsFound = 0;
                foreach (SpawnSyncedObject syncedObject in SyncedObjects) 
                {
                    if (syncedObject.spawnPrefab.name == "ExampleDungeon_EntranceTeleportA_DUMMY") 
                    {
                        Instance.mls.LogInfo("Found and replaced EntranceTeleportA prefab.");
                        bFoundEntranceA = true;
                        syncedObject.spawnPrefab = realEntranceAPrefab.Prefab;
                    }
                    else if (syncedObject.spawnPrefab.name == "ExampleDungeon_EntranceTeleportB_DUMMY") 
                    {
                        Instance.mls.LogInfo("Found and replaced EntranceTeleportB prefab.");
                        bFoundEntranceB = true;
                        syncedObject.spawnPrefab = realEntranceBPrefab.Prefab;
                    }
                    else if (syncedObject.spawnPrefab.name == "ExampleDungeon_Vent_DUMMY") 
                    {
                        Instance.mls.LogInfo("Found and replaced VentEntrance prefab.");
                        iVentsFound++;
                        syncedObject.spawnPrefab = realVentPrefab.Prefab;
                    }
                    else if (syncedObject.spawnPrefab.name == "ExampleDungeon_StorageShelf_DUMMY")
                    {
                        Instance.mls.LogInfo("Found and replaced StorageShelfContainer prefab.");
                        iVentsFound++;
                        syncedObject.spawnPrefab = realStorageShelfPrefab.Prefab;
                    }
                }
                if (!bFoundEntranceA && !bFoundEntranceB) 
                {
                    Instance.mls.LogError("Failed to find entrance teleporters to replace. Map will not be playable!");
                    return;
                }
                if (iVentsFound == 0)
                {
                    Instance.mls.LogWarning("No vents found to replace.");
                }
                else
                {
                    Instance.mls.LogInfo($"{iVentsFound} vents found and replaced with network prefab.");
                }
            }

            // Fix up turret and landmine prefab references before trying to spawn map objects
            [HarmonyPatch("SpawnMapObjects")]
            [HarmonyPrefix]
            static void SpawnMapObjects(ref SelectableLevel ___currentLevel, ref RuntimeDungeon ___dungeonGenerator)
            {
                if (___dungeonGenerator.Generator.DungeonFlow.name != "ExampleFlow")
                {
                    return;
                }

                // Lethal Lib does this for us, and if we don't let it it causes an exception @ Dungeon.cs line 196 (Sequence contains no matching element)
                /*RandomMapObject[] RandomObjects = FindObjectsOfType<RandomMapObject>();
                NetworkManager networkManager = FindObjectOfType<NetworkManager>();
                NetworkPrefab realLandminePrefab = networkManager.NetworkConfig.Prefabs.Prefabs.First(x => x.Prefab.name == "Landmine");
                if (realLandminePrefab == null)
                {
                    Instance.mls.LogError("Failed to find Landmine prefab.");
                    return;
                }

                NetworkPrefab realTurretContainerPrefab = networkManager.NetworkConfig.Prefabs.Prefabs.First(x => x.Prefab.name == "TurretContainer");
                if (realTurretContainerPrefab == null)
                {
                    Instance.mls.LogError("Failed to find TurretContainer prefab.");
                    return;
                }

                foreach (RandomMapObject randomObject in RandomObjects)
                {
                    List<GameObject> props = randomObject.spawnablePrefabs;
                    List<GameObject> newProps = new List<GameObject>();

                    foreach (GameObject prop in props)
                    {
                        if (prop.name == "ExampleDungeon_Turret_DUMMY")
                        {
                            newProps.Add(realTurretContainerPrefab.Prefab);
                        }
                        else if (prop.name == "ExampleDungeon_Landmine_DUMMY")
                        {
                            newProps.Add(realLandminePrefab.Prefab);
                        }
                    }

                    if(newProps.Count() > 0)
                    {
                        randomObject.spawnablePrefabs = newProps;
                    }
                }*/
            }

            // Just before spawning the scrap (the level is ready at this point) fix up our referenes to the item groups
            [HarmonyPatch("SpawnScrapInLevel")]
            [HarmonyPrefix]
            static void SpawnScrapInLevel(ref SelectableLevel ___currentLevel, ref RuntimeDungeon ___dungeonGenerator)
            {
                if (___dungeonGenerator.Generator.DungeonFlow.name != "ExampleFlow")
                {
                    return;
                }
                // Look for items with stored classes.
                SpawnableItemWithRarity itemWithClasses = ___currentLevel.spawnableScrap.Find(x => x.spawnableItem.itemName == "Bottles");
                if (itemWithClasses == null)
                {
                    itemWithClasses = ___currentLevel.spawnableScrap.Find(x => x.spawnableItem.itemName == "Cash register");
                    if (itemWithClasses == null)
                    {
                        itemWithClasses = ___currentLevel.spawnableScrap.Find(x => x.spawnableItem.itemName == "Chemical jug");
                        if (itemWithClasses == null)
                        {
                            itemWithClasses = ___currentLevel.spawnableScrap.Find(x => x.spawnableItem.itemName == "Gift");
                            if (itemWithClasses == null)
                            {
                                itemWithClasses = ___currentLevel.spawnableScrap.Find(x => x.spawnableItem.itemName == "Tea kettle");
                                if (itemWithClasses == null)
                                {
                                    Instance.mls.LogError("Unable to find an item with spawn positions to pull from. No junk will spawn in this dungeon!");
                                    return;
                                }
                            }
                        }
                    }
                }

                // Grab the item groups
                ItemGroup itemGroupGeneral = itemWithClasses.spawnableItem.spawnPositionTypes.Find(x => x.name == "GeneralItemClass");
                ItemGroup itemGroupTabletop = itemWithClasses.spawnableItem.spawnPositionTypes.Find(x => x.name == "TabletopItems");
                if(!itemGroupGeneral || !itemGroupTabletop)
                {
                    Instance.mls.LogError($"Found an item '{itemWithClasses.spawnableItem.name}' that is suppose to have both general and table top items but no longer does...");
                    return;
                }

                // Grab the small item group from the fancy glass. It is the only item that uses it and if it isn't used will default to table top items which is similar.
                SpawnableItemWithRarity itemWithSmallItems = ___currentLevel.spawnableScrap.Find(x => x.spawnableItem.itemName == "Golden cup");
                ItemGroup itemGroupSmall = (itemWithSmallItems == null) ? itemGroupTabletop : itemWithSmallItems.spawnableItem.spawnPositionTypes.Find(x => x.name == "SmallItems");

                // Fix all scrap spawners
                RandomScrapSpawn[] scrapSpawns = FindObjectsOfType<RandomScrapSpawn>();
                foreach (RandomScrapSpawn scrapSpawn in scrapSpawns)
                {
                    switch (scrapSpawn.spawnableItems.name)
                    {
                        case "ExampleDungeon_GeneralItemClass_DUMMY": scrapSpawn.spawnableItems = itemGroupGeneral; break;
                        case "ExampleDungeon_TabletopItems_DUMMY": scrapSpawn.spawnableItems = itemGroupTabletop; break;
                        case "ExampleDungeon_SmallItems_DUMMY": scrapSpawn.spawnableItems = itemGroupSmall; break;
                    }
                }
            }
        }
    }
}
