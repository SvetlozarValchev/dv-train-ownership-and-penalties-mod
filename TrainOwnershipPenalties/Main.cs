using System.Collections.Generic;
using System.Reflection;
using UnityModManagerNet;
using Harmony12;
using UnityEngine;
using System.Linq;
using System;
using Newtonsoft.Json;

namespace TrainOwnershipPenalties
{
    public class Main
    {
        public static List<LocoShopSpec> locoShop = new List<LocoShopSpec>();

        public static bool boughtLoco = false;
        public static int boughtLocoIdx;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            var harmony = HarmonyInstance.Create(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            locoShop.Add(new LocoShopSpec("Shunter Locomotive", "Spawns a Shunter Locomotive", 25000f, 2));
            locoShop.Add(new LocoShopSpec("Steam Locomotive", "Spawns a Steam Locomotive", 40000f, 1));

            return true;
        }
    }

    public class LocoShopSpec
    {
        public string name;
        public string description;
        public float price;
        public int amount;

        public LocoShopSpec(string name, string description, float price, int amount)
        {
            this.name = name;
            this.description = description;
            this.price = price;
            this.amount = amount;
        }
    }

    [HarmonyPatch(typeof(TrainCar), "Derail")]
    class TrainCar_Derail_Patch
    {
        static void Prefix(TrainCar __instance)
        {
            var inventory = SingletonBehaviour<Inventory>.Instance;

            if (!Application.isPlaying || __instance.derailed || inventory.PlayerMoney <= 0.0f)
                return;

            float penaltyAmount = 0f;

            switch (__instance.carType)
            {
                case TrainCarType.LocoShunter:
                    penaltyAmount = 2500f;
                    break;
                case TrainCarType.LocoSteamHeavy:
                case TrainCarType.LocoSteamHeavyBlue:
                    penaltyAmount = 5000f;
                    break;
                default:
                    penaltyAmount = 1000f;
                    break;
            }

            inventory.RemoveMoney(penaltyAmount);
        }
    }

    [HarmonyPatch(typeof(GlobalShopController), "Awake")]
    class GlobalShopController_Awake_Patch
    {
        static void Prefix(GlobalShopController __instance)
        {
            for (var i = 0; i < Main.locoShop.Count; i++)
            {
                var loco = Main.locoShop[i];
                var shopItem = new ShopItemSpecs();
                shopItem.amount = loco.amount;
                shopItem.basePrice = loco.price;
                shopItem.isGlobal = true;
                shopItem.item = CreateItem(loco.name, loco.description);
                shopItem.soldAt = new List<Shop>();

                __instance.shopItems.Insert(0, shopItem);
            }
        }

        static InventoryItemSpec CreateItem(string name, string description)
        {
            var KeyPrefab = Resources.Load("Key") as GameObject;
            var KeyPrefabInventoryItemSpec = KeyPrefab.GetComponent<InventoryItemSpec>();
            var newKeyPrefab = new GameObject();
            var inventoryItemSpec = newKeyPrefab.AddComponent<InventoryItemSpec>();
            var LODGroup = newKeyPrefab.AddComponent<LODGroup>();

            inventoryItemSpec.itemName = name;
            inventoryItemSpec.itemDescription = description;
            inventoryItemSpec.isEssential = false;
            inventoryItemSpec.itemPrefabName = "Key";
            inventoryItemSpec.previewPrefab = KeyPrefabInventoryItemSpec.previewPrefab;
            inventoryItemSpec.previewBounds = KeyPrefabInventoryItemSpec.previewBounds;
            inventoryItemSpec.applyRotationOnItemTakenOutFromInventory = false;
            inventoryItemSpec.previewRotation = KeyPrefabInventoryItemSpec.previewRotation;

            LODGroup.localReferencePoint = Vector3.zero;
            LODGroup.size = 0.09045122f;
            LODGroup.fadeMode = LODFadeMode.None;
            LODGroup.animateCrossFading = false;

            return inventoryItemSpec;
        }
    }

    [HarmonyPatch(typeof(ShopInstantiator), "GenerateShop")]
    class ShopInstantiator_GenerateShop_Patch
    {
        static void Prefix(ShopInstantiator __instance)
        {
            __instance.shopSlots += 2;
            __instance.shopLayout.x += 1f;
        }
    }

    [HarmonyPatch(typeof(GlobalShopController), "InstantiatePurchasedItem")]
    class GlobalShopController_InstantiatePurchasedItem_Patch
    {
        static IEnumerator<object> coroutine;

        static void Prefix(GlobalShopController __instance, List<ShoppingCartEntry> ___shoppingCart)
        {
            var index = ___shoppingCart.Count - 1;
            var item = ___shoppingCart[index].specs.item;

            for (var i = 0; i < Main.locoShop.Count; i++)
            {
                if (Main.locoShop[i].name == item.itemName)
                {
                    Main.boughtLoco = true;
                    Main.boughtLocoIdx = i;
                }
            }

            if (coroutine != null)
            {
                __instance.StopCoroutine(coroutine);
            }

            coroutine = RestockShop();

            __instance.StartCoroutine(coroutine);
        }

        static IEnumerator<object> RestockShop()
        {
            yield return new WaitForSeconds(120);

            SingletonBehaviour<GlobalShopController>.Instance.RestockShops();
        }
    }

    [HarmonyPatch(typeof(StationLocoSpawner), "Update")]
    class StationLocoSpawner_Update_Patch
    {
        static void Prefix(StationLocoSpawner __instance, ref bool ___playerEnteredLocoSpawnRange, GameObject ___spawnTrackMiddleAnchor, List<TrainCar> ___spawnedLocos)
        {
            Transform playerTransform = PlayerManager.PlayerTransform;
            if (playerTransform == null || !SaveLoadController.carsAndJobsLoadingFinished)
                return;

            bool inRange = (double)(playerTransform.position - ___spawnTrackMiddleAnchor.transform.position).sqrMagnitude < (double)__instance.spawnLocoPlayerSqrDistanceFromTrack;

            if (inRange)
            {
                ___playerEnteredLocoSpawnRange = true;

                if (Main.boughtLoco)
                {
                    var trainCarTypes = new List<TrainCarType>();

                    if (Main.boughtLocoIdx == 0)
                    {
                        trainCarTypes.Add(TrainCarType.LocoShunter);
                    }
                    else if (Main.boughtLocoIdx == 1)
                    {
                        trainCarTypes.Add(TrainCarType.LocoSteamHeavy);
                        trainCarTypes.Add(TrainCarType.Tender);
                    }

                    List<TrainCar> trainCarList = CarSpawner.SpawnCarTypesOnTrack(trainCarTypes, __instance.locoSpawnTrack, 0.0, __instance.spawnRotationFlipped);

                    Main.boughtLoco = false;

                    if (trainCarList == null)
                        return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(SaveGameManager), "Save")]
    class SaveGameManager_Save_Patch
    {
        static void Prefix(ShopInstantiator __instance)
        {
            CarsTOPSaveData[] carsSaveData = GetCarsSaveData();

            SaveGameManager.data.SetObject("Mod_TOP", carsSaveData, (JsonSerializerSettings)null);
        }

        static CarsTOPSaveData[] GetCarsSaveData()
        {
            TrainCar[] objectsOfType = UnityEngine.Object.FindObjectsOfType<TrainCar>();
            CarsTOPSaveData[] array = ((IEnumerable<TrainCar>)objectsOfType).Where<TrainCar>((Func<TrainCar, bool>)(car =>
            {
                if (!car.derailed)
                    return car.logicCar != null;
                return false;
            })).Select<TrainCar, CarsTOPSaveData>((Func<TrainCar, CarsTOPSaveData>)(car => GetSaveData(car))).ToArray<CarsTOPSaveData>();

            return array;
        }

        static CarsTOPSaveData GetSaveData(TrainCar car)
        {
            var topSaveData = new CarsTOPSaveData();
            
            if (car.carType == TrainCarType.LocoShunter)
            {
                var simulationComponent = car.GetComponent<ShunterLocoSimulation>();

                topSaveData.fuel = simulationComponent.fuel.value;
                topSaveData.oil = simulationComponent.oil.value;
                topSaveData.sand = simulationComponent.sand.value;
                topSaveData.carGuid = car.logicCar.carGuid;
            } else if (car.carType == TrainCarType.LocoSteamHeavy)
            {
                var simulationComponent = car.GetComponent<SteamLocoSimulation>();

                topSaveData.boilerWater = simulationComponent.boilerWater.value;
                topSaveData.sand = simulationComponent.sand.value;
                topSaveData.carGuid = car.logicCar.carGuid;
            } else if (car.carType == TrainCarType.Tender)
            {
                var simulationComponent = car.GetComponent<TenderSimulation>();

                topSaveData.tenderWater = simulationComponent.tenderWater.value;
                topSaveData.tenderCoal = simulationComponent.tenderCoal.value;
                topSaveData.carGuid = car.logicCar.carGuid;
            }

            return topSaveData;
        }
    }

    [HarmonyPatch(typeof(CarsSaveManager), "Load")]
    class CarsSaveManager_Load_Patch
    {
        static void Postfix(ShopInstantiator __instance, bool __result)
        {
            CarsTOPSaveData[] carsSaveData = SaveGameManager.data.GetObject<CarsTOPSaveData[]>("Mod_TOP");

            if (carsSaveData != null && __result)
            {
                TrainCar[] trainCars = UnityEngine.Object.FindObjectsOfType<TrainCar>();

                for (var i = 0; i < trainCars.Length; i++)
                {
                    for (var j = 0; j < carsSaveData.Length; j++)
                    {
                        if (trainCars[i].logicCar.carGuid == carsSaveData[j].carGuid)
                        {
                            LoadData(trainCars[i], carsSaveData[j]);
                        }
                    }
                }
            }
        }

        static void LoadData(TrainCar car, CarsTOPSaveData data)
        {
            if (car.carType == TrainCarType.LocoShunter)
            {
                var simulationComponent = car.GetComponent<ShunterLocoSimulation>();
                
                simulationComponent.fuel.SetValue(data.fuel);
                simulationComponent.oil.SetValue(data.oil);
                simulationComponent.sand.SetValue(data.sand);
            }
            else if (car.carType == TrainCarType.LocoSteamHeavy)
            {
                var simulationComponent = car.GetComponent<SteamLocoSimulation>();

                simulationComponent.boilerWater.SetValue(data.boilerWater);
                simulationComponent.sand.SetValue(data.sand);
            }
            else if (car.carType == TrainCarType.Tender)
            {
                var simulationComponent = car.GetComponent<TenderSimulation>();

                simulationComponent.tenderWater.SetValue(data.tenderWater);
                simulationComponent.tenderCoal.SetValue(data.tenderCoal);
            }
        }
    }

    [HarmonyPatch(typeof(CarSpawner), "DeleteTrainCars")]
    class CarSpawner_DeleteTrainCars_Patch
    {
        static List<TrainCarType> excludedTrainCarTypes = new List<TrainCarType>
        {
            TrainCarType.LocoShunter,
            TrainCarType.LocoSteamHeavy,
            TrainCarType.Tender
        };

        static void Prefix(CarSpawner __instance, ref List<TrainCar> trainCarsToDelete)
        {
            trainCarsToDelete = trainCarsToDelete.Where(trainCar => !excludedTrainCarTypes.Contains(trainCar.carType)).ToList();
        }
    }

    class CarsTOPSaveData
    {
        public string carGuid;
        public float fuel;
        public float oil;
        public float sand;
        public float boilerWater;
        public float tenderWater;
        public float tenderCoal;
    }
}