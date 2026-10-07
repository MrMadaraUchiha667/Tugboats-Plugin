// Tugboats 1.3.2 - snapshot atualizado
// Compatibilidade: remove a chamada direta a IFuelSystem.GetFuelContainer().
// O método AddFuel usa reflexão para localizar o container de combustível sem depender
// da API antiga.

using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Tugboats", "MrMadara", "1.3.2")]
    [Description("Permite a compra de rebocadores na vila de pescadores.")]
    class Tugboats : RustPlugin
    {
        #region Config
        private Configuration config;
        public class Configuration
        {
            [JsonProperty("How long should the tugboat be unmountable by other players for?")] public float safe_time = 300f;
            [JsonProperty("Should the boat be removed if the player does not claim it within the safe time?")] public bool remove_after_safe_time = false;
            [JsonProperty("How much fuel should the tugboat spawn with?")] public int starting_fuel = 100;
            [JsonProperty("Draw on the players hud after purchasing the boat, to show its spawn location?")] public bool paint_boat_location = true;
            [JsonProperty("Limit the amount of boats a player can purchase during a wipe? [0 = no limit]")] public int boat_limit = 0;
            [JsonProperty("Prevent purchase if the server has this many tugboats? [0 = no limit]")] public int max_tugboats = 0;
            [JsonProperty("Itens necessários para a compra do Tugboat")] public List<ItemInfo> items = new List<ItemInfo>();
            [JsonProperty("Local spawn positions")] public Dictionary<Monument, List<Vector3>> LocalSpawns = new Dictionary<Monument, List<Vector3>>();
            [JsonProperty("Chat option ui position")] public AnchorInfo menuAnchor = new AnchorInfo("0.5 0.5", "0.5 0.5", "150.8 35.5", "411 55.5");
        }
        public class AnchorInfo { public string anchorMin, anchorMax, offsetMin, offsetMax; public AnchorInfo(string a,string b,string c,string d){anchorMin=a;anchorMax=b;offsetMin=c;offsetMax=d;} }
        public class ItemInfo { public string shortname; public ulong skin; public int amount; public ItemInfo(string s,ulong k,int a){shortname=s;skin=k;amount=a;} }
        public enum Monument { LargeFishingVillage, SmallFishingVillage }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration { items = DefaultItems, LocalSpawns = DefaultLocalSpawnPoints };
        }
        Dictionary<Monument,List<Vector3>> DefaultLocalSpawnPoints => new Dictionary<Monument,List<Vector3>> {
            [Monument.LargeFishingVillage] = new List<Vector3>{new Vector3(17.8f,1.9f,33.4f),new Vector3(-6.1f,1.9f,38.8f),new Vector3(-44.6f,1.9f,20.5f),new Vector3(50.7f,1.9f,11.1f),new Vector3(52.3f,1.9f,-6.2f),new Vector3(34.4f,1.9f,31.5f),new Vector3(-28.8f,1.9f,40f),new Vector3(-50.8f,1.9f,3.2f)},
            [Monument.SmallFishingVillage] = new List<Vector3>{new Vector3(6.9f,1.9f,51.2f),new Vector3(26.8f,1.9f,25.3f),new Vector3(-37.8f,1.9f,-6.6f),new Vector3(-39.1f,1.9f,25.4f),new Vector3(35f,1.9f,-1.3f)}
        };
        List<ItemInfo> DefaultItems => new List<ItemInfo>{new ItemInfo("scrap",0,3000)};
        protected override void LoadConfig(){base.LoadConfig();try{config=Config.ReadObject<Configuration>();if(config==null)throw new JsonException();SaveConfig();}catch{PrintWarning($"Configuration file {Name}.json is invalid; using defaults");LoadDefaultConfig();SaveConfig();}}
        protected override void SaveConfig(){Config.WriteObject(config,true);}
        #endregion

        #region Data
        private PlayerEntity pcdData; private DynamicConfigFile PCDDATA;
        private const string perm_admin="Tugboats.tug.admin",perm_free="Tugboats.tug.vip",perm_use="Tugboats.tug.use";
        private class PlayerEntity { public Dictionary<ulong,int> purchases=new Dictionary<ulong,int>(); }
        private void Init(){PCDDATA=Interface.Oxide.DataFileSystem.GetFile(Name);LoadData();permission.RegisterPermission(perm_admin,this);permission.RegisterPermission(perm_free,this);permission.RegisterPermission(perm_use,this);}
        private void LoadData(){try{pcdData=Interface.Oxide.DataFileSystem.ReadObject<PlayerEntity>(Name);}catch{pcdData=new PlayerEntity();}}
        private void SaveData(){PCDDATA.WriteObject(pcdData);}
        private void Unload(){SaveData();foreach(var p in BasePlayer.activePlayerList)DestroyUI(p);foreach(var t in TugboatTimers.Values.ToList())if(t!=null&&!t.Destroyed)t.Destroy();BoughtBoat.Clear();TugboatTimers.Clear();}
        private bool CanAccess(BasePlayer p)=>permission.UserHasPermission(p.UserIDString,perm_use)||permission.UserHasPermission(p.UserIDString,perm_admin);
        #endregion

        #region Localization
        protected override void LoadDefaultMessages(){lang.RegisterMessages(new Dictionary<string,string>{
            ["MissingNPC"]="Não foi possível encontrar um NPC comerciante nas proximidades.",["AddPointSuccess"]="Novo local salvo: {0} para o tipo Vila de Pescadores: {1}",["NoSpawnRoom"]="Não há espaço suficiente para posicionar seu barco.",["MissingItems"]="Você não tem Sucatas suficientes para construir esta embarcação.",["RepossessNotification"]="Você tem {0} segundos para reivindicar seu Tugboat antes que ele seja retomado. Entre no banco do motorista para reivindicá-lo.",["ReposessedNotification"]="Seu Tugboat foi retomado, uma vez que você não efetuou a retirada.",["UITugboatOption"]="Que tal um Tugboat?",["UIOptionNumber_Revised"]="0",["UIBoatVendorTitle"]="Vendedor de barcos",["UIVendorText"]="Você precisará de alguns itens para que possamos construir isso para você...",["BoatSpawnedNotification"]="Seu barco foi construído e está esperando por você nas proximidades.",["hudLocationText"]="<size=20>Tugboat</size>",["DisableNoclip"]="Desative o noclip para usar este comando.",["PurchaseLimit"]="Você já comprou a quantidade máxima de barcos nesta atualização. - {0}.",["TugboatLimit"]="Você não pode comprar um Tugboat no momento, pois o servidor atingiu sua capacidade máxima."},this);}
        #endregion

        #region Hooks
        private int TugboatCount;
        private void OnServerInitialized(bool initial){TugboatCount=BaseNetworkable.serverEntities.OfType<Tugboat>().Count();if(config.max_tugboats>0){Subscribe(nameof(OnEntitySpawned));Subscribe(nameof(OnEntityDeath));}}
        private void OnEntitySpawned(Tugboat t){if(t!=null)TugboatCount++;}
        private void OnEntityDeath(Tugboat t,HitInfo info){TugboatCount=Mathf.Max(0,TugboatCount-1);}
        private void OnNewSave(string filename){pcdData.purchases.Clear();SaveData();}
        #endregion

        #region CUI
        private readonly Dictionary<BasePlayer,NPCTalking> Talkers=new Dictionary<BasePlayer,NPCTalking>();
        private void DestroyUI(BasePlayer p){CuiHelper.DestroyUi(p,"Option5");CuiHelper.DestroyUi(p,"TugboatPurchasePanel");}
        private void Option5(BasePlayer p){var c=new CuiElementContainer();c.Add(new CuiPanel{CursorEnabled=false,Image={Color="0.1647 0.1647 0.1333 1"},RectTransform={AnchorMin=config.menuAnchor.anchorMin,AnchorMax=config.menuAnchor.anchorMax,OffsetMin=config.menuAnchor.offsetMin,OffsetMax=config.menuAnchor.offsetMax}},"Overlay","Option5");c.Add(new CuiElement{Name="ResponseText",Parent="Option5",Components={new CuiTextComponent{Text=lang.GetMessage("UITugboatOption",this,p.UserIDString),Font="robotocondensed-regular.ttf",FontSize=11,Align=TextAnchor.MiddleLeft,Color="0.698 0.6745 0.6353 1"},new CuiRectTransformComponent{AnchorMin="0 0.5",AnchorMax="0 0.5",OffsetMin="24.966 -10",OffsetMax="280.501 10"}}});c.Add(new CuiPanel{Image={Color="0.3529 0.4431 0.2235 0.8"},RectTransform={AnchorMin="0 0.5",AnchorMax="0 0.5",OffsetMin="5 -8",OffsetMax="20 7"}},"Option5","NumberPanel");c.Add(new CuiElement{Name="text",Parent="NumberPanel",Components={new CuiTextComponent{Text=lang.GetMessage("UIOptionNumber_Revised",this,p.UserIDString),Font="robotocondensed-bold.ttf",FontSize=11,Align=TextAnchor.MiddleCenter,Color="1 1 1 0.4"},new CuiRectTransformComponent{AnchorMin="0 0",AnchorMax="1 1"}}});c.Add(new CuiButton{Button={Color="1 1 1 0",Command="sendtugboatbuyui"},Text={Text=" "},RectTransform={AnchorMin="0 0",AnchorMax="1 1"}},"Option5","Button");CuiHelper.DestroyUi(p,"Option5");CuiHelper.AddUi(p,c);}
        [ConsoleCommand("sendtugboatbuyui")] private void SendTugboatBuyMenu(ConsoleSystem.Arg arg){var p=arg.Player();if(p==null)return;NPCTalking n;if(!Talkers.TryGetValue(p,out n))return;Unsubscribe(nameof(OnNpcConversationEnded));n.ForceEndConversation(p);Subscribe(nameof(OnNpcConversationEnded));CuiHelper.DestroyUi(p,"Option5");TugboatPurchasePanel(p);}
        private void TugboatPurchasePanel(BasePlayer p){var c=new CuiElementContainer();c.Add(new CuiPanel{CursorEnabled=true,Image={Color="0.1226 0.1221 0.1221 0.8431"},RectTransform={AnchorMin="0.5 0.5",AnchorMax="0.5 0.5",OffsetMin="139.523 -107.097",OffsetMax="445.477 89.497"}},"Overlay","TugboatPurchasePanel");c.Add(new CuiPanel{Image={Color="0.2902 0.2706 0.2588 0.8"},RectTransform={AnchorMin="0.5 0.5",AnchorMax="0.5 0.5",OffsetMin="-143.4 72.38",OffsetMax="-41.455 89.9"}},"TugboatPurchasePanel","TitlePanel");c.Add(new CuiElement{Name="Title",Parent="TitlePanel",Components={new CuiTextComponent{Text=lang.GetMessage("UIBoatVendorTitle",this,p.UserIDString),Font="robotocondensed-bold.ttf",FontSize=10,Align=TextAnchor.MiddleCenter,Color="0.7529 0.7412 0.7333 1"},new CuiRectTransformComponent{AnchorMin="0.5 0.5",AnchorMax="0.5 0.5",OffsetMin="-50.969 -8.76",OffsetMax="50.971 8.76"}}});c.Add(new CuiPanel{Image={Color="0.2902 0.2706 0.2588 0.8"},RectTransform={AnchorMin="0.5 0.5",AnchorMax="0.5 0.5",OffsetMin="-143.4 26.423",OffsetMax="141.984 67.1"}},"TugboatPurchasePanel","ConvoPanel");c.Add(new CuiElement{Name="text",Parent="ConvoPanel",Components={new CuiTextComponent{Text=lang.GetMessage("UIVendorText",this,p.UserIDString),Font="robotocondensed-regular.ttf",FontSize=10,Align=TextAnchor.UpperLeft,Color="0.7529 0.7412 0.7333 1"},new CuiRectTransformComponent{AnchorMin="0.5 0.5",AnchorMax="0.5 0.5",OffsetMin="-134.999 -20.339",OffsetMax="135.001 20.338"}}});int count=0,row=0;foreach(var item in config.items){var def=ItemManager.FindItemDefinition(item.shortname);int id=def!=null?def.itemid:1751045826;c.Add(new CuiElement{Name=$"Item_{count}_{row}",Parent="TugboatPurchasePanel",Components={new CuiImageComponent{Color="1 1 1 1",ItemId=id,SkinId=item.skin},new CuiRectTransformComponent{AnchorMin="0.5 0.5",AnchorMax="0.5 0.5",OffsetMin=$"{-143.4+count*110} {-10.6-row*38}",OffsetMax=$"{-111.4+count*110} {21.4-row*38}"}}});c.Add(new CuiElement{Name="text",Parent=$"Item_{count}_{row}",Components={new CuiTextComponent{Text=$"x{item.amount}",Font="robotocondensed-regular.ttf",FontSize=12,Align=TextAnchor.MiddleLeft,Color="1 1 1 1"},new CuiRectTransformComponent{AnchorMin="0.5 0.5",AnchorMax="0.5 0.5",OffsetMin="19 -16",OffsetMax="67 16"}}});if(++count>2){count=0;row++;}}c.Add(new CuiButton{Button={Color="0.3765 0.4471 0.2118 1",Command="trybuildtugboat"},Text={Text="BUILD",Font="robotocondensed-bold.ttf",FontSize=14,Align=TextAnchor.MiddleCenter,Color="0.8196 0.8314 0.7647 1"},RectTransform={AnchorMin="0.5 0.5",AnchorMax="0.5 0.5",OffsetMin="-32 -88.4",OffsetMax="32 -64.4"}},"TugboatPurchasePanel","button");c.Add(new CuiButton{Button={Color="0.6784 0.2157 0 1",Command="closetugboatbuildmenu"},Text={Text="X",Font="robotocondensed-bold.ttf",FontSize=10,Align=TextAnchor.MiddleCenter,Color="0.9686 0.9137 0.8706 1"},RectTransform={AnchorMin="0.5 0.5",AnchorMax="0.5 0.5",OffsetMin="132.98 78.297",OffsetMax="148.98 94.297"}},"TugboatPurchasePanel","close");CuiHelper.DestroyUi(p,"TugboatPurchasePanel");CuiHelper.AddUi(p,c);}
        [ConsoleCommand("closetugboatbuildmenu")] private void CloseMenu(ConsoleSystem.Arg arg){var p=arg.Player();if(p!=null)DestroyUI(p);}
        #endregion

        #region Purchase
        [ConsoleCommand("trybuildtugboat")]
        private void TryBuildTugboat(ConsoleSystem.Arg arg){var p=arg.Player();if(p==null)return;if(config.boat_limit>0&&!CanBuyMoreTugboats(p)){PrintToChat(p,string.Format(lang.GetMessage("PurchaseLimit",this,p.UserIDString),config.boat_limit));return;}if(config.max_tugboats>0&&TugboatCount>=config.max_tugboats){PrintToChat(p,lang.GetMessage("TugboatLimit",this,p.UserIDString));return;}DestroyUI(p);var es=FindEntitiesOfType<NPCTalking>(p.transform.position,5f);bool found=es.Any(x=>x!=null&&x.ShortPrefabName=="boat_shopkeeper");Pool.FreeUnmanaged(ref es);if(!found){PrintToChat(p,lang.GetMessage("MissingNPC",this,p.UserIDString));return;}var m=GetClosestVillage(p.transform.position);if(m==null){PrintToChat(p,lang.GetMessage("NoSpawnRoom",this,p.UserIDString));return;}SpawnBoat(p,m,m.displayPhrase.english.Equals("large fishing village",StringComparison.OrdinalIgnoreCase)?Monument.LargeFishingVillage:Monument.SmallFishingVillage);}
        private bool CanBuyMoreTugboats(BasePlayer p){int n;return !pcdData.purchases.TryGetValue(p.userID,out n)||n<config.boat_limit;}
        private List<Item> AllItems(BasePlayer p){var r=Pool.Get<List<Item>>();if(p.inventory.containerMain!=null)r.AddRange(p.inventory.containerMain.itemList);if(p.inventory.containerBelt!=null)r.AddRange(p.inventory.containerBelt.itemList);if(p.inventory.containerWear!=null)r.AddRange(p.inventory.containerWear.itemList);return r;}
        private bool TakeItems(BasePlayer p){if(permission.UserHasPermission(p.UserIDString,perm_free))return true;var watch=new Dictionary<string,List<Item>>();foreach(var e in config.items){var list=Pool.Get<List<Item>>();watch[e.shortname]=list;var all=AllItems(p);int total=0;foreach(var i in all){if(i.info.shortname==e.shortname&&i.skin==e.skin){list.Add(i);total+=i.amount;}if(total>=e.amount)break;}Pool.FreeUnmanaged(ref all);if(total<e.amount){ClearTaken(watch);return false;}}foreach(var e in config.items){List<Item> list;if(!watch.TryGetValue(e.shortname,out list)){ClearTaken(watch);return false;}int remain=e.amount;foreach(var i in list){if(i==null)continue;int take=Math.Min(i.amount,remain);if(take>=i.amount)i.Remove();else i.UseItem(take);remain-=take;if(remain<=0)break;}}ClearTaken(watch);return true;}
        private void ClearTaken(Dictionary<string,List<Item>> d){foreach(var x in d.Values){if(x!=null){var list=x;Pool.FreeUnmanaged(ref list);}}d.Clear();}
        #endregion

        #region NPC
        private void OnNpcConversationStart(NPCTalking n,BasePlayer p,ConversationData d){if(CanAccess(p))Talkers[p]=n;}
        private void OnNpcConversationEnded(NPCTalking n,BasePlayer p){DestroyUI(p);Talkers.Remove(p);}
        private void OnNpcConversationRespond(NPCTalking n,BasePlayer p,ConversationData d,ConversationData.ResponseNode r){if(!CanAccess(p))return;if(r!=null&&r.responseTextLocalized!=null&&r.responseTextLocalized.english.Contains("buy a boat"))Option5(p);else DestroyUI(p);}
        #endregion

        #region Spawn
        private MonumentInfo GetClosestVillage(Vector3 pos){float closest=-1;MonumentInfo result=null;foreach(var m in TerrainMeta.Path.Monuments){if(!m.IsSafeZone)continue;var text=m.displayPhrase?.english;if(string.IsNullOrEmpty(text)||!text.Contains("Fishing Village"))continue;float dist=Vector3.Distance(pos,m.transform.position);if(closest<0||dist<closest){closest=dist;result=m;}}return result;}
        [ChatCommand("btshowspawnpoints")] private void ShowSpawnPoints(BasePlayer p){if(!permission.UserHasPermission(p.UserIDString,perm_admin))return;if(!p.IsAdmin&&!p.IsDeveloper&&p.IsFlying){PrintToChat(p,lang.GetMessage("DisableNoclip",this,p.UserIDString));return;}var m=GetClosestVillage(p.transform.position);if(m==null){p.ChatMessage("Could not find monument Fishing Village.");return;}var type=m.displayPhrase.english.Equals("Large Fishing Village",StringComparison.OrdinalIgnoreCase)?Monument.LargeFishingVillage:Monument.SmallFishingVillage;List<Vector3> locs;if(!config.LocalSpawns.TryGetValue(type,out locs))return;bool admin=p.IsAdmin;if(!admin){p.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin,true);p.SendNetworkUpdateImmediate();}foreach(var loc in locs)p.SendConsoleCommand("ddraw.text",10f,Color.yellow,ConvertLocalsToWorld(m,loc),"<size=20>X</size>");if(!admin){p.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin,false);p.SendNetworkUpdateImmediate();}}
        [ChatCommand("btaddspawnpoint")] private void SetBoatSpawnPoint(BasePlayer p){if(!permission.UserHasPermission(p.UserIDString,perm_admin))return;if(!p.IsAdmin&&!p.IsDeveloper&&p.IsFlying){PrintToChat(p,lang.GetMessage("DisableNoclip",this,p.UserIDString));return;}var m=GetClosestVillage(p.transform.position);if(m==null){p.ChatMessage("Could not find monument Fishing Village.");return;}var type=m.displayPhrase.english.Equals("Large Fishing Village",StringComparison.OrdinalIgnoreCase)?Monument.LargeFishingVillage:Monument.SmallFishingVillage;Vector3 local=m.transform.InverseTransformPoint(p.transform.position);List<Vector3> locs;if(!config.LocalSpawns.TryGetValue(type,out locs))return;locs.Add(local);PrintToChat(p,string.Format(lang.GetMessage("AddPointSuccess",this,p.UserIDString),local,type));bool admin=p.IsAdmin;if(!admin){p.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin,true);p.SendNetworkUpdateImmediate();}foreach(var loc in locs)p.SendConsoleCommand("ddraw.text",10f,Color.yellow,ConvertLocalsToWorld(m,loc),"<size=20>X</size>");if(!admin){p.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin,false);p.SendNetworkUpdateImmediate();}SaveConfig();}
        private Vector3 ConvertLocalsToWorld(MonumentInfo m,Vector3 loc)=>m.transform.localToWorldMatrix.MultiplyPoint3x4(loc);
        private void SpawnBoat(BasePlayer p,MonumentInfo m,Monument type){Vector3 pos=Vector3.zero;List<Vector3> points;if(!config.LocalSpawns.TryGetValue(type,out points))return;foreach(var lp in points){var wp=ConvertLocalsToWorld(m,lp);var es=FindEntitiesOfType<BaseBoat>(wp,5f);bool occupied=es.Count>0;Pool.FreeUnmanaged(ref es);if(!occupied){pos=wp;break;}}if(pos==Vector3.zero){PrintToChat(p,lang.GetMessage("NoSpawnRoom",this,p.UserIDString));return;}if(!TakeItems(p)){PrintToChat(p,lang.GetMessage("MissingItems",this,p.UserIDString));return;}var rot=Quaternion.LookRotation((pos-m.transform.position).normalized);var tug=GameManager.server.CreateEntity("assets/content/vehicles/boats/tugboat/tugboat.prefab",pos,rot) as Tugboat;if(tug==null){PrintToChat(p,"Não foi possível criar o Tugboat.");return;}tug.Spawn();NextTick(()=>{if(tug==null||tug.IsDestroyed)return;AddFuel(tug);AddSafety(p,tug);});pcdData.purchases[p.userID]=pcdData.purchases.ContainsKey(p.userID)?pcdData.purchases[p.userID]+1:1;SaveData();if(!config.paint_boat_location)return;if(!p.IsAdmin&&!p.IsDeveloper&&p.IsFlying)return;bool admin=p.IsAdmin;if(!admin){p.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin,true);p.SendNetworkUpdateImmediate();}p.SendConsoleCommand("ddraw.text",10f,Color.yellow,pos,lang.GetMessage("hudLocationText",this,p.UserIDString));if(!admin){p.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin,false);p.SendNetworkUpdateImmediate();}}
        private void AddFuel(Tugboat tugboat)
        {
            if(tugboat==null||tugboat.IsDestroyed||config.starting_fuel<1)return;
            try
            {
                object fuelSystem=GetMemberValue(tugboat,"fuelSystem");
                if(fuelSystem==null){PrintWarning("Tugboats: fuelSystem não encontrado no Tugboat.");return;}
                object container=FindFuelContainer(fuelSystem);
                if(container==null){PrintWarning("Tugboats: não foi possível localizar o container de combustível na API atual.");return;}
                var inventory=GetMemberValue(container,"inventory") as ItemContainer;
                if(inventory==null){PrintWarning("Tugboats: container de combustível encontrado, mas inventory não está disponível.");return;}
                var fuel=ItemManager.CreateByName("lowgradefuel",config.starting_fuel);
                if(fuel==null){PrintWarning("Tugboats: não foi possível criar lowgradefuel.");return;}
                if(!fuel.MoveToContainer(inventory))fuel.Remove();
            }
            catch(Exception ex){PrintError($"Tugboats: erro ao adicionar combustível: {ex.Message}");}
        }
        private object FindFuelContainer(object fuelSystem)
        {
            foreach(string name in new[]{"GetFuelContainer","GetFuelContainerEntity","fuelContainer","FuelContainer","container","Container"})
            {
                var v=GetMemberValue(fuelSystem,name,true);
                if(v!=null)return v;
            }
            var type=fuelSystem.GetType();
            foreach(var f in type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                object v;try{v=f.GetValue(fuelSystem);}catch{continue;}
                if(v==null)continue;
                if(v is ItemContainer||GetMemberValue(v,"inventory",true) is ItemContainer)return v;
            }
            return null;
        }
        private object GetMemberValue(object obj,string name,bool invokeMethod=false)
        {
            if(obj==null)return null;var t=obj.GetType();
            var p=t.GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);if(p!=null){try{return p.GetValue(obj,null);}catch{}}
            var f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);if(f!=null){try{return f.GetValue(obj);}catch{}}
            if(invokeMethod){var m=t.GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic, null, Type.EmptyTypes,null);if(m!=null){try{return m.Invoke(obj,null);}catch{}}}
            return null;
        }
        private static List<T> FindEntitiesOfType<T>(Vector3 pos,float radius,int layerMask=-1) where T:BaseEntity{int hits=Physics.OverlapSphereNonAlloc(pos,radius,Vis.colBuffer,layerMask,QueryTriggerInteraction.Collide);var list=Pool.Get<List<T>>();for(int i=0;i<hits;i++){var e=Vis.colBuffer[i]?.ToBaseEntity() as T;if(e!=null&&!list.Contains(e))list.Add(e);Vis.colBuffer[i]=null;}return list;}
        #endregion

        #region Protection
        private readonly Dictionary<Tugboat,BasePlayer> BoughtBoat=new Dictionary<Tugboat,BasePlayer>();
        private readonly Dictionary<Tugboat,Timer> TugboatTimers=new Dictionary<Tugboat,Timer>();
        private object CanMountEntity(BasePlayer p,BaseMountable entity){var tug=entity.GetParentEntity() as Tugboat;if(tug==null)return null;BasePlayer owner;if(BoughtBoat.TryGetValue(tug,out owner)){if(owner!=p)return false;RemoveSafety(tug);}return null;}
        private void AddSafety(BasePlayer p,Tugboat tug){BoughtBoat[tug]=p;AddTugboatTimer(tug);if(config.remove_after_safe_time)PrintToChat(p,string.Format(lang.GetMessage("RepossessNotification",this,p.UserIDString),config.safe_time));else PrintToChat(p,lang.GetMessage("BoatSpawnedNotification",this,p.UserIDString));}
        private void RemoveSafety(Tugboat tug){if(tug==null)return;BoughtBoat.Remove(tug);RemoveTugboatTimer(tug);}
        private void AddTugboatTimer(Tugboat tug){RemoveTugboatTimer(tug);TugboatTimers[tug]=timer.Once(config.safe_time,()=>{if(tug==null||tug.IsDestroyed){RemoveSafety(tug);return;}BasePlayer owner;BoughtBoat.TryGetValue(tug,out owner);if(config.remove_after_safe_time){if(owner!=null)PrintToChat(owner,lang.GetMessage("ReposessedNotification",this,owner.UserIDString));tug.Invoke(tug.KillMessage,0.01f);}RemoveSafety(tug);});}
        private void RemoveTugboatTimer(Tugboat tug){if(tug==null)return;Timer t;if(!TugboatTimers.TryGetValue(tug,out t))return;if(t!=null&&!t.Destroyed)t.Destroy();TugboatTimers.Remove(tug);}
        #endregion
    }
}
