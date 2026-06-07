using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/* Sugestões
 * 
 */

/* 1.0.0
 * Atualizado para o forced wipe de Fevereiro.
 */

namespace Oxide.Plugins
{
	[Info("Tugboats", "MrMadara", "1.0.0")]
	[Description("Permite a compra de rebocadores na vila de pescadores.")]
	class Tugboats : RustPlugin
	{
        #region Config

        private Configuration config;
        public class Configuration
        {
            [JsonProperty("How long should the tugboat be unmountable by other players for? [This will also repossess the tugboat if the player has not claimed it]")]
            public float safe_time = 300f;

            [JsonProperty("Should the boat be removed if the player does not claim it within the safe time?")]
            public bool remove_after_safe_time = false;

            [JsonProperty("How much fuel should the tugboar spawn with?")]
            public int starting_fuel = 100;

            [JsonProperty("Draw on the players hud after puchasing the boat, to show its spawn location?")]
            public bool paint_boat_location = true;

            [JsonProperty("Limit the amount of boats a player can purchase during a wipe? [0 = no limit]")]
            public int boat_limit = 0;

            [JsonProperty("Prevent the purchase of tugboats if the spawned amount exceeds the following [0 = no limit]")]
            public int max_tugboats = 0;

            [JsonProperty("Items required to purchase the tugboat")]
            public List<ItemInfo> items = new List<ItemInfo>();

            [JsonProperty("Local spawn positions")]
            public Dictionary<Monument, List<Vector3>> LocalSpawns = new Dictionary<Monument, List<Vector3>>();

            [JsonProperty("Chat option ui position")]
            public AnchorInfo menuAnchor = new AnchorInfo("0.5 0.5", "0.5 0.5", "150.8 35.5", "411 55.5");

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }        

        public class AnchorInfo
        {
            public string anchorMin;
            public string anchorMax;
            public string offsetMin;
            public string offsetMax;
            public AnchorInfo(string anchorMin, string anchorMax, string offsetMin, string offsetMax)
            {
                this.anchorMin = anchorMin;
                this.anchorMax = anchorMax;
                this.offsetMin = offsetMin;
                this.offsetMax = offsetMax;
            }
        }

        public class ItemInfo
        {
            public string shortname;
            public ulong skin;
            public int amount;
            public ItemInfo(string shortname, ulong skin, int amount)
            {
                this.shortname = shortname;
                this.skin = skin;
                this.amount = amount;
            }
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
            config.items = DefaultItems;
            config.LocalSpawns = DefaultLocalSpawnPoints;
        }

        Dictionary<Monument, List<Vector3>> DefaultLocalSpawnPoints
        {
            get
            {
                return new Dictionary<Monument, List<Vector3>>()
                {
                    [Monument.LargeFishingVillage] = new List<Vector3>() { new Vector3(17.8f, 1.9f, 33.4f), new Vector3(-6.1f, 1.9f, 38.8f), new Vector3(-44.6f, 1.9f, 20.5f), new Vector3(50.7f, 1.9f, 11.1f), new Vector3(52.3f, 1.9f, -6.2f), new Vector3(34.4f, 1.9f, 31.5f), new Vector3(-28.8f, 1.9f, 40f), new Vector3(-50.8f, 1.9f, 3.2f),},
                    [Monument.SmallFishingVillage] = new List<Vector3>() { new Vector3(6.9f, 1.9f, 51.2f), new Vector3(26.8f, 1.9f, 25.3f), new Vector3(-37.8f, 1.9f, -6.6f), new Vector3(-39.1f, 1.9f, 25.4f), new Vector3(35f, 1.9f, -1.3f), }
                };
            }
        }

        List<ItemInfo> DefaultItems
        {
            get
            {
                return new List<ItemInfo>()
                {
                    new ItemInfo("scrap", 0, 500),
                    new ItemInfo("wood", 0, 2000),
                    new ItemInfo("metal.fragments", 0, 200),
                    new ItemInfo("metal.refined", 0, 20),
                };
            }
        }

        public enum Monument
        {
            LargeFishingVillage,
            SmallFishingVillage
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    throw new JsonException();
                }

                SaveConfig();
            }
            catch
            {
                PrintToConsole($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            PrintToConsole($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }

        #endregion

        #region Data

        PlayerEntity pcdData;
        private DynamicConfigFile PCDDATA;

        const string perm_admin = "pixelcraft.tug.admin";
        const string perm_free = "pixelcraft.tug.vip";
        const string perm_use = "pixelcraft.tug.use";

        bool CanAccess(BasePlayer player) => permission.UserHasPermission(player.UserIDString, perm_use) || permission.UserHasPermission(player.UserIDString, perm_admin);

        void Init()
        {
            PCDDATA = Interface.Oxide.DataFileSystem.GetFile(this.Name);
            LoadData();

            permission.RegisterPermission(perm_admin, this);
            permission.RegisterPermission(perm_free, this);
            permission.RegisterPermission(perm_use, this);

            Unsubscribe(nameof(OnEntitySpawned));
            Unsubscribe(nameof(OnEntityDeath));
        }

        void Unload()
        {
            SaveData();

            foreach (var timerEntry in TugboatTimers.Values)
    {
        if (timerEntry != null && !timerEntry.Destroyed)
            timerEntry.Destroy();
    }

    TugboatTimers.Clear();

    BoughtBoat.Clear();

    Talkers.Clear();

    foreach (var player in BasePlayer.activePlayerList)
        {
        DestroyUI(player);
        }
}

        void SaveData()
        {
            PCDDATA.WriteObject(pcdData);
        }

        void LoadData()
        {
            try
            {
                pcdData = Interface.Oxide.DataFileSystem.ReadObject<PlayerEntity>(this.Name);
            }
            catch
            {
                Puts("Couldn't load data, creating new file");
                pcdData = new PlayerEntity();
            }
        }

        class PlayerEntity
        {
            public Dictionary<ulong, int> purchases = new Dictionary<ulong, int>();

        }

        class PCDInfo
        {
            
        }

        #endregion;

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["MissingNPC"] = "Could not find NPC shop keeper nearby.",
                ["AddPointSuccess"] = "Saved new location: {0} to Fishing Village type: {1}",
                ["NoSpawnRoom"] = "There is not enough room to spawn your boat.",
                ["MissingItems"] = "You do not have enough items to build this vessle.",
                ["RepossessNotification"] = "You have {0} seconds to claim your tugboat before it is repossessed. Enter the drivers seat to claim it.",
                ["ReposessedNotification"] = "Your tugboat has been repossessed as you have not taken delivery.",
                ["UITugboatOption"] = "How about a Tugboat?",
                ["UIOptionNumber_Revised"] = "0",
                ["UIBoatVendorTitle"] = "Boat Vendor",
                ["UIVendorText"] = "You will need some items so we can build this for you...",
                ["BoatSpawnedNotification"] = "Your boat has been built and is waiting for you nearby.",
                ["hudLocationText"] = "<size=20>Tugboat</size>",
                ["DisableNoclip"] = "Disable noclip to use this command.",
                ["PurchaseLimit"] = "You have already purchased the maximum amount of boats this wipe - {0}.",
                ["TugboatLimit"] = "You cannot purchase a tugboat right now as the server has reached capactiy.",
            }, this);
        }

        #endregion

        #region Hooks

        int TugboatCount = 0;
        void OnServerInitialized(bool initial)
        {
            foreach (var item in ItemManager.GetItemDefinitions())
                ItemIDs.Add(item.shortname, item.itemid);

            if (config.max_tugboats > 0)
            {
                foreach (var tugboat in BaseNetworkable.serverEntities.OfType<Tugboat>())
                    TugboatCount++;

                Subscribe(nameof(OnEntitySpawned));
                Subscribe(nameof(OnEntityDeath));
            }
            
        }

        void OnEntitySpawned(Tugboat tugboat)
        {
            TugboatCount++;
        }

        void OnEntityDeath(Tugboat tugboat, HitInfo info)
        {
            TugboatCount--;
        }

        void OnNewSave(string filename)
        {
            pcdData.purchases.Clear();
        }

        Dictionary<string, int> ItemIDs = new Dictionary<string, int>();

        #endregion

        #region CUI

        List<Item> AllItems(BasePlayer player)
        {
            List<Item> result = Pool.Get<List<Item>>();

            if (player.inventory.containerMain?.itemList != null)
                result.AddRange(player.inventory.containerMain.itemList);

            if (player.inventory.containerBelt?.itemList != null)
                result.AddRange(player.inventory.containerBelt.itemList);

            if (player.inventory.containerWear?.itemList != null)
                result.AddRange(player.inventory.containerWear.itemList);

            return result;
        }

        void DestroyUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "Option5");
            CuiHelper.DestroyUi(player, "TugboatPurchasePanel");
        }

        private void Option5(BasePlayer player)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0.1647058823 0.1647058823 0.133333333 1" },
                RectTransform = { AnchorMin = config.menuAnchor.anchorMin, AnchorMax = config.menuAnchor.anchorMax, OffsetMin = config.menuAnchor.offsetMin, OffsetMax = config.menuAnchor.offsetMax }
            }, "Overlay", "Option5");

            container.Add(new CuiElement
            {
                Name = "ResponseText",
                Parent = "Option5",
                Components = {
                    new CuiTextComponent { Text = lang.GetMessage("UITugboatOption", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "0.6980392 0.6745098 0.6352941 1" },
                    new CuiRectTransformComponent { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "24.966 -10", OffsetMax = "280.501 10" }
                }
            });

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0.3529412 0.4431373 0.2235294 0.8" },
                RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "5 -8", OffsetMax = "20 7" }
            }, "Option5", "NumberPanel");

            container.Add(new CuiElement
            {
                Name = "text",
                Parent = "NumberPanel",
                Components = {
                    new CuiTextComponent { Text = lang.GetMessage("UIOptionNumber_Revised", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.4" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" }
                }
            });

            container.Add(new CuiButton
            {
                Button = { Color = "1 1 1 0", Command = "sendtugboatbuyui" },
                Text = { Text = " ", Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0.002 0" }
            }, "Option5", "Button");

            CuiHelper.DestroyUi(player, "Option5");
            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("sendtugboatbuyui")]
        void SendTugboatBuyMenu(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            NPCTalking npc;
            if (!Talkers.TryGetValue(player, out npc)) return;

            Unsubscribe(nameof(OnNpcConversationEnded));
            npc.ForceEndConversation(player);
            Subscribe(nameof(OnNpcConversationEnded));

            CuiHelper.DestroyUi(player, "Option5");
            TugboatPurchasePanel(player);
        }

        Dictionary<BasePlayer, NPCTalking> Talkers = new Dictionary<BasePlayer, NPCTalking>();

        private void TugboatPurchasePanel(BasePlayer player)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0.1226415 0.122063 0.122063 0.8431373" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "139.523 -107.097", OffsetMax = "445.477 89.497" }
            }, "Overlay", "TugboatPurchasePanel");

            container.Add(new CuiPanel
            {
                Image = { Color = "0.2901961 0.2705882 0.2588235 0.8" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-143.4 72.38", OffsetMax = "-41.455 89.9" }
            }, "TugboatPurchasePanel", "TitlePanel");

            container.Add(new CuiElement
            {
                Name = "Title",
                Parent = "TitlePanel",
                Components = {
                    new CuiTextComponent { Text = lang.GetMessage("UIBoatVendorTitle", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.7529413 0.7411765 0.7333333 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-50.969 -8.76", OffsetMax = "50.971 8.76" }
                }
            });

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image = { Color = "0.2901961 0.2705882 0.2588235 0.8" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-143.4 26.423", OffsetMax = "141.984 67.1" }
            }, "TugboatPurchasePanel", "ConvoPanel");

            container.Add(new CuiElement
            {
                Name = "text",
                Parent = "ConvoPanel",
                Components = {
                    new CuiTextComponent { Text = lang.GetMessage("UIVendorText", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.UpperLeft, Color = "0.7529412 0.7411765 0.7333333 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-134.999 -20.339", OffsetMax = "135.001 20.338" }
                }
            });

            var count = 0;
            var row = 0;
            foreach (var item in config.items)
            {
                int itemID;
                container.Add(new CuiElement
                {
                    Name = $"Item_{count}_{row}",
                    Parent = "TugboatPurchasePanel",
                    Components = {
                    new CuiImageComponent { Color = "1 1 1 1", ItemId = ItemIDs.TryGetValue(item.shortname, out itemID) ? itemID : 1751045826, SkinId = item.skin },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-143.4 + (count * 110)} {-10.6 - (row * 38)}", OffsetMax = $"{-111.4 + (count * 110)} {21.4- (row * 38)}" }
                }
                });

                container.Add(new CuiElement
                {
                    Name = "text",
                    Parent = $"Item_{count}_{row}",
                    Components = {
                    new CuiTextComponent { Text = $"x{item.amount}", Font = "robotocondensed-regular.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "19 -16", OffsetMax = "67 16" }
                }
                });

                count++;
                if (count > 2)
                {
                    count = 0;
                    row++;
                }
            }

            container.Add(new CuiButton
            {
                Button = { Color = "0.3764706 0.4470589 0.2117647 1", Command = "trybuildtugboat" },
                Text = { Text = "BUILD", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.8196079 0.8313726 0.764706 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-32 -88.4", OffsetMax = "32 -64.4" }
            }, "TugboatPurchasePanel", "button");

            container.Add(new CuiButton
            {
                Button = { Color = "0.6784314 0.2156863 0 1", Command = "closetugboatbuildmenu" },
                Text = { Text = "X", Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.9686275 0.9137256 0.8705883 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "132.98 78.297", OffsetMax = "148.98 94.297" }
            }, "TugboatPurchasePanel", "close");

            CuiHelper.DestroyUi(player, "TugboatPurchasePanel");
            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("closetugboatbuildmenu")]
        void CloseMenu(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            DestroyUI(player);
        }

        [ConsoleCommand("trybuildtugboat")]
        void TryBuildTugboat(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (config.boat_limit > 0 && !CanBuyMoreTugboats(player))
            {
                PrintToChat(player, string.Format(lang.GetMessage("PurchaseLimit", this, player.UserIDString), config.boat_limit));
                return;
            } 

            if (config.max_tugboats > 0 && !PassTugboatPopulationCheck)
            {
                PrintToChat(player, lang.GetMessage("TugboatLimit", this, player.UserIDString));
                return;
            }

            DestroyUI(player);

            var entities = FindEntitiesOfType<NPCTalking>(player.transform.position, 5f);

            bool foundNPC = false;
            foreach (var npc in entities)
            {
                if (npc.ShortPrefabName == "boat_shopkeeper")
                {
                    foundNPC = true;
                    break;
                }
            }
            Pool.FreeUnmanaged(ref entities);

            if (!foundNPC)
            {
                PrintToChat(player, lang.GetMessage("MissingNPC", this, player.UserIDString));
                return;
            }

            var monument = GetClosestVillage(player.transform.position);

            SpawnBoat(player, monument, monument.displayPhrase.english.Equals("large fishing village", StringComparison.OrdinalIgnoreCase) ? Monument.LargeFishingVillage : Monument.SmallFishingVillage);
        }

        bool CanBuyMoreTugboats(BasePlayer player)
        {
            int bought;
            if (pcdData.purchases.TryGetValue(player.userID, out bought) && bought >= config.boat_limit) return false;
            return true;
        }

        bool PassTugboatPopulationCheck
        {
            get
            {
                return TugboatCount < config.max_tugboats;
            }
        }

        public class TakeItemsInfo
        {
            public int foundTotal;
            public List<Item> items;        
        }

        bool TakeItems(BasePlayer player)
        {
            if (permission.UserHasPermission(player.UserIDString, perm_free)) return true;

            Dictionary<string, TakeItemsInfo> ItemWatch = new Dictionary<string, TakeItemsInfo>();
            foreach (var entry in config.items)
            {
                TakeItemsInfo data;
                ItemWatch.Add(entry.shortname, data = new TakeItemsInfo());
                data.items = Pool.Get<List<Item>>();
                var allItems = AllItems(player);
                foreach (var item in allItems)
                {
                    if (item.info.shortname == entry.shortname && item.skin == entry.skin)
                    {
                        data.items.Add(item);
                        data.foundTotal += item.amount;
                    }
                    if (data.foundTotal >= entry.amount) break;
                }
                Pool.FreeUnmanaged(ref allItems);
                if (data.foundTotal < entry.amount)
                {
                    ClearTaken(ItemWatch);
                    return false;
                }
            }

            foreach (var entry in config.items)
            {
                TakeItemsInfo data;
                if (!ItemWatch.TryGetValue(entry.shortname, out data))
                {
                    ClearTaken(ItemWatch);
                    return false;
                }
                    
                data.foundTotal = 0;
                foreach (var item in data.items)
                {
                    if (item.amount >= entry.amount - data.foundTotal)
                    {
                        item.UseItem(entry.amount - data.foundTotal);
                        data.foundTotal = entry.amount;
                    }
                    else
                    {
                        data.foundTotal += item.amount;
                        item.Remove();
                    }
                    if (data.foundTotal >= entry.amount) break;
                }
            }

            ClearTaken(ItemWatch);
            return true;
        }

        void ClearTaken(Dictionary<string, TakeItemsInfo> dict)
        {
            foreach (var kvp in dict)
            {
                if (kvp.Value.items != null) Pool.FreeUnmanaged(ref kvp.Value.items);
            }

            dict.Clear();
        }

       

        void FreeUnmanageds(Dictionary<ItemInfo, TakeItemsInfo> info)
        {
            foreach (var entry in info)
                FreeUnmanaged(entry.Value);
        }
        void FreeUnmanaged(TakeItemsInfo list) => Pool.FreeUnmanaged(ref list.items);

        void OnNpcConversationStart(NPCTalking npcTalking, BasePlayer player, ConversationData conversationData)
        {
            if (!CanAccess(player)) return;
            if (!Talkers.ContainsKey(player)) Talkers.Add(player, npcTalking);
            else Talkers[player] = npcTalking;

            return;
        }

        void OnNpcConversationEnded(NPCTalking npcTalking, BasePlayer player)
        {
            DestroyUI(player);
            Talkers.Remove(player);
        }

        void OnNpcConversationRespond(NPCTalking npcTalking, BasePlayer player, ConversationData conversationData, ConversationData.ResponseNode responseNode)
        {
            if (!CanAccess(player)) return;
            if (responseNode.responseTextLocalized.english.Contains("buy a boat")) Option5(player);
            else DestroyUI(player);

            return;
        }

        #endregion

        #region Boat spawn

        MonumentInfo GetClosestVillage(Vector3 pos)
        {
            var closest = -1f;
            MonumentInfo result = null;
            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                //Puts($"Monument: {monument.displayPhrase.english}");
                var dist = Vector3.Distance(pos, monument.transform.position);
                if (!monument.IsSafeZone) continue;
                if (closest < 0 || dist < closest)
                {
                    closest = dist;
                    result = monument;
                }
            }

            return result;
        }

        [ChatCommand("btshowspawnpoints")]
        void ShowSpawnPoints(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            if (!player.IsAdmin && !player.IsDeveloper && player.IsFlying)
            {
                PrintToChat(player, lang.GetMessage("DisableNoclip", this, player.UserIDString));
                return; // BasePlayer => FinalizeTick => NoteAdminHack => Ban => Cheat Detected!
            }

            MonumentInfo monument = GetClosestVillage(player.transform.position);
            if (monument == null || !monument.displayPhrase.english.Contains("Fishing Village"))
            {
                player.ChatMessage("Could not find monument Fishing Village.");
                return;
            }

            var type = monument.displayPhrase.english.Equals("Large Fishing Village", StringComparison.OrdinalIgnoreCase) ? Monument.LargeFishingVillage : Monument.SmallFishingVillage;

            List<Vector3> locs;
            if (!config.LocalSpawns.TryGetValue(type, out locs)) return;

            var wasAdmin = player.IsAdmin;
            if (!wasAdmin)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                player.SendNetworkUpdateImmediate();
            }

            foreach (var loc in locs)
            {
                player.SendConsoleCommand("ddraw.text", 10f, Color.yellow, ConvertLocalsToWorld(monument, loc), "<size=20>X</size>");
            }

            if (!wasAdmin)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                player.SendNetworkUpdateImmediate();
            }
        }

        [ChatCommand("btaddspawnpoint")]
        void SetBoatSpawnPoint(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, perm_admin)) return;

            if (!player.IsAdmin && !player.IsDeveloper && player.IsFlying)
            {
                PrintToChat(player, lang.GetMessage("DisableNoclip", this, player.UserIDString));
                return; // BasePlayer => FinalizeTick => NoteAdminHack => Ban => Cheat Detected!
            }

            MonumentInfo monument = GetClosestVillage(player.transform.position);
            if (monument == null || !monument.displayPhrase.english.Contains("Fishing Village"))
            { 
                player.ChatMessage("Could not find monument Fishing Village."); 
                return; 
            }

            var type = monument.displayPhrase.english.Equals("Large Fishing Village", StringComparison.OrdinalIgnoreCase) ? Monument.LargeFishingVillage : Monument.SmallFishingVillage;

            Vector3 localPosition = monument.transform.InverseTransformPoint(player.transform.position);

            List<Vector3> locs;
            if (!config.LocalSpawns.TryGetValue(type, out locs)) return;
            locs.Add(localPosition);

            PrintToChat(player, string.Format(lang.GetMessage("AddPointSuccess", this, player.UserIDString), localPosition, type));

            var wasAdmin = player.IsAdmin;
            if (!wasAdmin)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                player.SendNetworkUpdateImmediate();
            }

            foreach (var loc in locs)
            {
                player.SendConsoleCommand("ddraw.text", 10f, Color.yellow, ConvertLocalsToWorld(monument, loc), "<size=20>X</size>");
            }

            if (!wasAdmin)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                player.SendNetworkUpdateImmediate();
            }

            SaveConfig();
        }

        Vector3 ConvertLocalsToWorld(MonumentInfo monument, Vector3 loc)
        {
            return monument.transform.localToWorldMatrix.MultiplyPoint3x4(loc);
        }

        void SpawnBoat(BasePlayer player, MonumentInfo monument, Monument type)
        {
            Vector3 pos = Vector3.zero;
            foreach (var _pos in config.LocalSpawns[type])
            {
                var worldPos = ConvertLocalsToWorld(monument, _pos);
                var entities = FindEntitiesOfType<BaseBoat>(worldPos, 5f);
                if (entities.Count > 0)
                {
                    Pool.FreeUnmanaged(ref entities);
                    continue;
                }
                Pool.FreeUnmanaged(ref entities);
                pos = worldPos;
                break;
            }
            if (pos == Vector3.zero)
            {
                PrintToChat(player, lang.GetMessage("NoSpawnRoom", this, player.UserIDString));
                return;
            }

            if (!TakeItems(player))
            {
                PrintToChat(player, lang.GetMessage("MissingItems", this, player.UserIDString));
                return;
            }

            var rot = Quaternion.LookRotation((pos - monument.transform.position).normalized);
            var tugboat = GameManager.server.CreateEntity("assets/content/vehicles/boats/tugboat/tugboat.prefab", pos, rot) as Tugboat;
            tugboat.Spawn();
            
            AddFuel(tugboat);
            AddSafety(player, tugboat);

            if (!pcdData.purchases.ContainsKey(player.userID)) pcdData.purchases.Add(player.userID, 1);
            else pcdData.purchases[player.userID]++;

            if (!config.paint_boat_location) return;

            if (!player.IsAdmin && !player.IsDeveloper && player.IsFlying)
            {
                return; // BasePlayer => FinalizeTick => NoteAdminHack => Ban => Cheat Detected!
            }

            var wasAdmin = player.IsAdmin;
            if (!wasAdmin)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                player.SendNetworkUpdateImmediate();
            }

            player.SendConsoleCommand("ddraw.text", 10f, Color.yellow, pos, lang.GetMessage("hudLocationText", this, player.UserIDString));

            if (!wasAdmin)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                player.SendNetworkUpdateImmediate();
            }
        }

        void AddFuel(Tugboat tugboat)
        {
            if (config.starting_fuel < 1) return;
            ItemManager.CreateByName("lowgradefuel", config.starting_fuel).MoveToContainer(tugboat.fuelSystem.GetFuelContainer().inventory);
        }
        
        private static List<T> FindEntitiesOfType<T>(Vector3 a, float n, int m = -1) where T : BaseEntity
        {
            int hits = Physics.OverlapSphereNonAlloc(a, n, Vis.colBuffer, m, QueryTriggerInteraction.Collide);
            List<T> entities = Pool.Get<List<T>>();
            for (int i = 0; i < hits; i++)
            {
                var entity = Vis.colBuffer[i]?.ToBaseEntity() as T;
                if (entity != null && !entities.Contains(entity)) entities.Add(entity);
                Vis.colBuffer[i] = null;
            }
            return entities;
        }

        #endregion

        #region Monitor bought boat

        Dictionary<Tugboat, BasePlayer> BoughtBoat = new Dictionary<Tugboat, BasePlayer>();

        object CanMountEntity(BasePlayer player, BaseMountable entity)
        {
            var tugboat = entity.GetParentEntity() as Tugboat;
            if (tugboat == null) return null;

            BasePlayer owner;
            if (BoughtBoat.TryGetValue(tugboat, out owner))
            {
                if (owner != player) return false;
                RemoveSafety(tugboat);
            }
                
            return null;
        }

        Dictionary<Tugboat, Timer> TugboatTimers = new Dictionary<Tugboat, Timer>();

        void OnEntityKill(Tugboat tugboat)
        {
    RemoveSafety(tugboat);
            }

        void OnPlayerDisconnected(BasePlayer player, string reason)
{
    Talkers.Remove(player);

    DestroyUI(player);

        var ownedBoats = BoughtBoat
        .Where(x => x.Value == player)
        .Select(x => x.Key)
        .ToList();

        foreach (var boat in ownedBoats)
    {
        RemoveSafety(boat);
    }
}

        void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
    DestroyUI(player);
            }

        void AddSafety(BasePlayer player, Tugboat tugboat)
        {
            BoughtBoat.Add(tugboat, player);
            AddTugboatTimer(tugboat);
            if (config.remove_after_safe_time) PrintToChat(player, string.Format(lang.GetMessage("RepossessNotification", this, player.UserIDString), config.safe_time));
            else PrintToChat(player, lang.GetMessage("BoatSpawnedNotification", this, player.UserIDString));
        }

        void RemoveSafety(Tugboat tugboat)
        {
    if (tugboat == null)
        return;

    BoughtBoat.Remove(tugboat);

    RemoveTugboatTimer(tugboat);
            }
			
        void AddTugboatTimer(Tugboat tugboat)
        {           
            TugboatTimers.Add(tugboat, timer.Once(config.safe_time, () =>
            {
                if (config.remove_after_safe_time)
                {
                    tugboat.Invoke(tugboat.KillMessage, 0.01f);
                    BasePlayer player;
                    if (BoughtBoat.TryGetValue(tugboat, out player)) PrintToChat(player, lang.GetMessage("ReposessedNotification", this, player.UserIDString));
                }
                RemoveSafety(tugboat);                          
            }));
        }

        void RemoveTugboatTimer(Tugboat tugboat)
        {
                if (tugboat == null)
        return;

                if (!TugboatTimers.TryGetValue(tugboat, out Timer timerInstance))
        return;

                if (timerInstance != null && !timerInstance.Destroyed)
                    timerInstance.Destroy();

                    TugboatTimers.Remove(tugboat);
        }

        #endregion
    }
}
 
