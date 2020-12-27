﻿using Netcode;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Monsters;
using StardewValley.Objects;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace StardewHack.WearMoreRings
{
    public class ModEntry : Hack<ModEntry>, IWearMoreRingsAPI
    {
        private const String DATA_KEY = "bcmpinc.WearMoreRings/chest-id";
        private const int EXTRA_RINGS = 6;

        static readonly ConditionalWeakTable<Farmer, Chest> ring_inventory = new ConditionalWeakTable<Farmer, Chest>();
        
        public static readonly Random random = new Random();
        
        public override void HackEntry(IModHelper helper) {
            helper.Events.GameLoop.SaveLoaded += GameLoop_SaveLoaded;
            
            Patch((Farmer f)=>f.isWearingRing(0), Farmer_isWearingRing);
            Patch(typeof(Farmer), "updateCommon", Farmer_updateCommon);
            Patch((GameLocation gl)=>gl.cleanupBeforePlayerExit(), GameLocation_cleanupBeforePlayerExit); 
            Patch(typeof(GameLocation), "resetLocalState", GameLocation_resetLocalState); 
            Patch((GameLocation gl)=>gl.damageMonster(new Rectangle(),0,0,false,0.0f,0,0.0f,0.0f,false,null), GameLocation_damageMonster);
            Patch(()=>new InventoryPage(0,0,0,0), InventoryPage_ctor);
            Patch((InventoryPage ip)=>ip.draw(null), InventoryPage_draw);
            Patch((InventoryPage ip)=>ip.performHoverAction(0,0), InventoryPage_performHoverAction);
            Patch((InventoryPage ip)=>ip.receiveLeftClick(0,0,false), InventoryPage_receiveLeftClick);
            Patch(()=>new Ring(0), Ring_ctor);
        }

        #region API
        public override object GetApi() {
            return this;
        }
        
        delegate void RingVisitor(Ring ring);
        static void ForEachRing(Farmer f, RingVisitor visitor) {
            if (f == null) throw new ArgumentNullException(nameof(f));
            if (f.leftRing.Value != null) visitor(f.leftRing.Value);
            if (f.rightRing.Value != null) visitor(f.rightRing.Value);
            foreach(Item item in ModEntry.GetRingInventory(f).items) {
                var ring = item as Ring;
                if (ring != null) visitor(ring);
            }
        }

        public int CountEquippedRings(Farmer f, int which) {
            int res = 0;
            ForEachRing(f, r => {if (r.indexInTileSheet.Value == which) res++; });
            return res;
        }

        public IEnumerable<Ring> GetAllRings(Farmer f) {
            var res = new List<Ring>();
            ForEachRing(f, r => res.Add(r));
            return res;
        }
        #endregion API
        
        #region JewelryChest
        static Vector2 getPositionFromId(int id) {
            return new Vector2(id, -50);
        }
        static Chest GetRingInventory_internal(Farmer farmer) {
            Farm farm = Game1.getFarm();
            int id = 0;
            
            // Check whether the farmer has a chest assigned.
            if (farmer.modData.ContainsKey(DATA_KEY)) {
                id = int.Parse(farmer.modData[DATA_KEY]);
                getInstance().Monitor.Log($"Farmer {farmer.Name} has existing chest {id}.");
                if (farm.objects.ContainsKey(getPositionFromId(id))) {
                    var existing_chest = farm.objects[getPositionFromId(id)];
                    if (existing_chest is Chest) {
                        // Yes, return it.
                        return (Chest)existing_chest;
                    }
                    getInstance().Monitor.Log("Object is not a chest!", LogLevel.Error);
                } else {
                    getInstance().Monitor.Log("Chest went missing!", LogLevel.Error);
                }
            }
            
            // No, create a new chest.
            while (farm.objects.ContainsKey(getPositionFromId(id))) id++;
            Chest new_chest = new Chest(true);
            farm.objects[getPositionFromId(id)] = new_chest;
            farmer.modData[DATA_KEY] = id.ToString();
            getInstance().Monitor.Log($"Farmer {farmer.Name} has new chest {id}.");
            return new_chest;
        }
        
        public static Chest GetRingInventory(Farmer f) {
            var res = ring_inventory.GetValue(f, GetRingInventory_internal);
            while (res.items.Count < EXTRA_RINGS) res.items.Add(null);
            return res;
        }
        #endregion JewelryChest
        
        #region Events
        /// <summary>
        /// Reads the saved extra rings and creates them.
        /// </summary>
        void GameLoop_SaveLoaded(object sender, StardewModdingAPI.Events.SaveLoadedEventArgs e) {
            Migration.Import(Monitor, helper);
        }
        #endregion Events

        
        void Farmer_isWearingRing() {
            AllCode().Replace(
                Instructions.Call(typeof(ModEntry), nameof(ModEntry.getInstance)),
                Instructions.Ldarg_0(),
                Instructions.Ldarg_1(),
                Instructions.Call(typeof(ModEntry), nameof(ModEntry.CountEquippedRings), typeof(Farmer), typeof(int)),
                Instructions.Ret()
            );
        }

        public static void UpdateRings(GameTime time, GameLocation location, Farmer f) {
            void Update(Ring r) { 
                if (r != null) r.update(time, location, f); 
            };
            Update(f.leftRing.Value);
            Update(f.rightRing.Value);
            foreach(Item item in ModEntry.GetRingInventory(f).items) {
                Update(item as Ring);
            }
        }

        #region Patch Farmer
        void Farmer_updateCommon() {
            FindCode(
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.rightRing)),
                OpCodes.Callvirt,
                OpCodes.Brfalse,
                
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.rightRing)),
                OpCodes.Callvirt,
                OpCodes.Ldarg_1,
                OpCodes.Ldarg_2,
                OpCodes.Ldarg_0,
                OpCodes.Callvirt,
                
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.leftRing)),
                OpCodes.Callvirt,
                OpCodes.Brfalse,
                
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.leftRing)),
                OpCodes.Callvirt,
                OpCodes.Ldarg_1,
                OpCodes.Ldarg_2,
                OpCodes.Ldarg_0,
                OpCodes.Callvirt
            ).Replace(
                Instructions.Ldarg_1(),
                Instructions.Ldarg_2(),
                Instructions.Ldarg_0(),
                Instructions.Call(typeof(ModEntry), nameof(UpdateRings), typeof (GameTime), typeof(GameLocation), typeof(Farmer))
            );
        }
        #endregion Patch Farmer

        #region Patch GameLocation
        public static void ring_onLeaveLocation(GameLocation location) {
            ForEachRing(Game1.player, r => r.onLeaveLocation(Game1.player, location));
        }
        
        void GameLocation_cleanupBeforePlayerExit() { 
            var code = FindCode(
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.rightRing)),
                OpCodes.Callvirt,
                OpCodes.Brfalse
            );
            code.Extend(
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.leftRing)),
                OpCodes.Callvirt,
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                OpCodes.Ldarg_0,
                Instructions.Callvirt(typeof(Ring), nameof(Ring.onLeaveLocation), typeof(Farmer), typeof(GameLocation))
            );
            code.Replace(
                Instructions.Ldarg_0(),
                Instructions.Call(typeof(ModEntry), nameof(ModEntry.ring_onLeaveLocation), typeof(GameLocation))
            );
        }
        
        public static void ring_onNewLocation(GameLocation location) {
            ForEachRing(Game1.player, r => r.onNewLocation(Game1.player, location));
        }
        
        void GameLocation_resetLocalState() { 
            var code = FindCode(
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.rightRing)),
                OpCodes.Callvirt,
                OpCodes.Brfalse
            );
            code.Extend(
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.leftRing)),
                OpCodes.Callvirt,
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                OpCodes.Ldarg_0,
                Instructions.Callvirt(typeof(Ring), nameof(Ring.onNewLocation), typeof(Farmer), typeof(GameLocation))
            );
            code.Replace(
                Instructions.Ldarg_0(),
                Instructions.Call(typeof(ModEntry), nameof(ring_onNewLocation), typeof(GameLocation))
            );
        }
         
        public static void ring_onMonsterSlay(Monster target, GameLocation location, Farmer who) {
            if (who == null) return;
            ForEachRing(who, r => r.onMonsterSlay(target, location, who));
        }
        
        void GameLocation_damageMonster() {
            byte arg_who = (byte)(Array.Find(original.GetParameters(), info => info.Name == "who").Position+1);

            var code = FindCode(
                // if (who != null && who.leftRing.Value != null) {
                Instructions.Ldarg_S(arg_who), // who
                OpCodes.Brfalse,
                Instructions.Ldarg_S(arg_who), // who
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.leftRing)),
                OpCodes.Callvirt,
                OpCodes.Brfalse
            );
            code.Extend(
                // who.rightRing.Value
                Instructions.Ldarg_S(arg_who), // who
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.rightRing)),
                OpCodes.Callvirt,
                // .onMonsterSlay (monster, this, who);
                OpCodes.Ldloc_2, // monster
                OpCodes.Ldarg_0, // this
                Instructions.Ldarg_S(arg_who), // who
                Instructions.Callvirt(typeof(Ring), nameof(Ring.onMonsterSlay), typeof(Monster), typeof(GameLocation), typeof(Farmer))
            );
            
            var monster = code.SubRange(code.length - 4, 3);
            code.Replace(
                // ModEntry.ring_onMonsterSlay(monster, this, who);
                monster[0],
                monster[1],
                monster[2],
                Instructions.Call(typeof(ModEntry), nameof(ring_onMonsterSlay), typeof(Monster), typeof(GameLocation), typeof(Farmer))
            );
        }
        #endregion Patch GameLocation
        
        #region Patch InventoryPage
        static public void AddIcon(InventoryPage page, string name, int x, int y, int id, int up, int down, int left, int right, Item item) {
            var rect = new Rectangle (
                page.xPositionOnScreen + 48 + x*4, 
                page.yPositionOnScreen + IClickableMenu.borderWidth + IClickableMenu.spaceToClearTopBorder + 256 - 12 + y*4, 
                64, 64
            );
            var component = new ClickableComponent(rect, name) {
                myID = id,
                downNeighborID = down,
                upNeighborID = up,
                rightNeighborID = right,
                leftNeighborID = left,
                item = item,
                fullyImmutable = true
            };
            page.equipmentIcons.Add(component);
        }

        static public void AddEquipmentIcons(InventoryPage page) {
            Chest chest = GetRingInventory(Game1.player);
            
            int inv = Game1.player.MaxItems - 12;
            //             name            x   y   id   up   dn   lt   rt, item
            AddIcon(page, "Hat",           0,  0, 102, inv, 103,  -1, 110, Game1.player.hat.Value);
            AddIcon(page, "Shirt",         0, 16, 103, 102, 104,  -1, 111, Game1.player.shirtItem.Value);
            AddIcon(page, "Pants",         0, 32, 104, 103, 108,  -1, 112, Game1.player.pantsItem.Value);
            AddIcon(page, "Boots",         0, 48, 108, 104,  -1,  -1, 112, Game1.player.boots.Value);
            AddIcon(page, "Left Ring",    52,  0, 110, inv, 111, 102, 101, Game1.player.leftRing.Value);
            AddIcon(page, "Right Ring",   68,  0, 101, inv, 121, 110, 105, Game1.player.rightRing.Value);
            AddIcon(page, "Extra Ring 1", 52, 16, 111, 110, 112, 103, 121, chest.items[0] as Ring);
            AddIcon(page, "Extra Ring 2", 68, 16, 121, 101, 122, 111, 105, chest.items[1] as Ring);
            AddIcon(page, "Extra Ring 3", 52, 32, 112, 111, 113, 104, 122, chest.items[2] as Ring);
            AddIcon(page, "Extra Ring 4", 68, 32, 122, 121, 123, 112, 105, chest.items[3] as Ring);
            AddIcon(page, "Extra Ring 5", 52, 48, 113, 122,  -1, 108, 123, chest.items[4] as Ring);
            AddIcon(page, "Extra Ring 6", 68, 48, 123, 122,  -1, 113, 105, chest.items[5] as Ring);
        }
        
        void InventoryPage_ctor() {
            // Replace code for equipment icon creation with method calls to our AddEquipmentIcon method.
            // Replace rings & boots
            var items = FindCode(
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(InventoryPage), nameof(InventoryPage.equipmentIcons)),
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(IClickableMenu), nameof(IClickableMenu.xPositionOnScreen)),
                Instructions.Ldc_I4_S(48)
            );
            items.Extend(
                OpCodes.Dup,
                Instructions.Ldc_I4_S(109),
                Instructions.Stfld(typeof(ClickableComponent), nameof(ClickableComponent.rightNeighborID)),
                OpCodes.Dup,
                OpCodes.Ldc_I4_1,
                Instructions.Stfld(typeof(ClickableComponent), nameof(ClickableComponent.fullyImmutable)),
                OpCodes.Callvirt
            );
            // Add our own equipment icons
            items.Replace(
                Instructions.Ldarg_0(), // page
                Instructions.Call(typeof(ModEntry), nameof(AddEquipmentIcons), typeof(InventoryPage))
            );
            // Remove hat, shirt & pants
            items = items.FindNext(
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(InventoryPage), nameof(InventoryPage.equipmentIcons)),
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(IClickableMenu), nameof(IClickableMenu.xPositionOnScreen)),
                Instructions.Ldc_I4_S(48)
            );
            items.Extend(
                OpCodes.Dup,
                Instructions.Ldc_I4_S(104),
                Instructions.Stfld(typeof(ClickableComponent), nameof(ClickableComponent.leftNeighborID)),
                OpCodes.Dup,
                OpCodes.Ldc_I4_1,
                Instructions.Stfld(typeof(ClickableComponent), nameof(ClickableComponent.fullyImmutable)),
                OpCodes.Callvirt
            );
            items.Remove();
        }
        
        static public void DrawEquipment(ClickableComponent icon, Microsoft.Xna.Framework.Graphics.SpriteBatch b) {
            if (icon.item != null) {
                b.Draw(Game1.menuTexture, icon.bounds, Game1.getSourceRectForStandardTileSheet (Game1.menuTexture, 10, -1, -1), Color.White);
                icon.item.drawInMenu(b, new Vector2(icon.bounds.X, icon.bounds.Y), icon.scale, 1f, 0.866f, StackDrawType.Hide);
            } else {
                int tile = 41;
                if (icon.name == "Hat") tile = 42;
                if (icon.name == "Shirt") tile = 69;
                if (icon.name == "Pants") tile = 68;
                if (icon.name == "Boots") tile = 40;
                b.Draw (Game1.menuTexture, icon.bounds, Game1.getSourceRectForStandardTileSheet (Game1.menuTexture, tile, -1, -1), Color.White);
            }
        }
        
        void InventoryPage_draw() {
            // Change the equipment slot drawing code to draw the 4 additional slots.
            InstructionRange range;
            try {
                // Linux & MacOS
                range  = FindCode(
                    // switch (equipmentIcon.name) {
                    OpCodes.Ldloca_S,
                    OpCodes.Call,
                    OpCodes.Stloc_S, // 4
                    OpCodes.Ldloc_S, // 4
                    Instructions.Ldfld(typeof(ClickableComponent), nameof(ClickableComponent.name)),
                    OpCodes.Stloc_S, // 5
                    OpCodes.Ldloc_S, // 5
                    OpCodes.Brfalse,
                    // case "Hat":
                    OpCodes.Ldloc_S, // 5
                    Instructions.Ldstr("Hat")
                );
            } catch (Exception err) {
                LogException(err, LogLevel.Trace);
                // Windows
                range  = FindCode(
                    // switch (equipmentIcon.name) {
                    OpCodes.Ldloca_S,
                    OpCodes.Call,
                    OpCodes.Stloc_S, // 4
                    OpCodes.Ldloc_S, // 4
                    Instructions.Ldfld(typeof(ClickableComponent), nameof(ClickableComponent.name)),
                    OpCodes.Stloc_S, // 5
                    // case "Hat":
                    OpCodes.Ldloc_S, // 5
                    Instructions.Ldstr("Hat")
                );
            }
            
            // Select entire loop contents (i.e. switch block)
            range.Extend(range.Follow(-1));
            range.Replace(
                range[0],
                range[1],
                Instructions.Ldarg_1(),
                Instructions.Call(typeof(ModEntry), nameof(DrawEquipment), typeof(ClickableComponent), typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch))
            );
            
            // Move other stuff 32/64px to the right to eliminate overlap.
            range = range.FindNext(
                Instructions.Ldc_R4(32),
                OpCodes.Stloc_0
            );
            range[0] = Instructions.Ldc_R4(64);
        }
        
        void InventoryPage_performHoverAction() {
            // Change code responsible for obtaining the tooltip information.
            var var_item = generator.DeclareLocal(typeof(Item));
            InstructionRange code;
            try {
                // Linux & MacOS
                code = FindCode(
                    // switch (equipmentIcon.name) {
                    OpCodes.Ldloc_1,
                    Instructions.Ldfld(typeof(ClickableComponent), nameof(ClickableComponent.name)),
                    OpCodes.Stloc_2,
                    // case null: break;
                    OpCodes.Ldloc_2,
                    OpCodes.Brfalse,
                    // case "Hat":
                    OpCodes.Ldloc_2,
                    Instructions.Ldstr("Hat")
                );
                code.Extend(code.Follow(4));
            } catch (Exception err) {
                LogException(err, LogLevel.Trace);
                // Windows
                code = FindCode(
                    // switch (equipmentIcon.name) {
                    OpCodes.Ldloc_1,
                    Instructions.Ldfld(typeof(ClickableComponent), nameof(ClickableComponent.name)),
                    OpCodes.Stloc_2,
                    // case "Hat":
                    OpCodes.Ldloc_2,
                    Instructions.Ldstr("Hat")
                );
                code.Extend(
                    OpCodes.Ldloc_2,
                    Instructions.Ldstr("Pants"),
                    OpCodes.Call,
                    OpCodes.Brtrue,
                    OpCodes.Br
                );
                code.Extend(code.End.Follow(-1));
            }
            code.Replace(
                // var item = EquipmentIcon.item
                code[0],
                Instructions.Ldfld(typeof(ClickableComponent), nameof(ClickableComponent.item)),
                Instructions.Stloc_S(var_item),
                // if (item != null)
                Instructions.Ldloc_S(var_item),
                Instructions.Brfalse(AttachLabel(code.End[0])),
                // hoveredItem = item;
                Instructions.Ldarg_0(),
                Instructions.Ldloc_S(var_item),
                Instructions.Stfld(typeof(InventoryPage), "hoveredItem"),
                // hoverText = item.getDescription();
                Instructions.Ldarg_0(),
                Instructions.Ldloc_S(var_item),
                Instructions.Callvirt(typeof(Item), nameof(Item.getDescription)),
                Instructions.Stfld(typeof(InventoryPage), "hoverText"),
                // hoverTitle = item.DisplayName;
                Instructions.Ldarg_0(),
                Instructions.Ldloc_S(var_item),
                Instructions.Callvirt_get(typeof(Item), nameof(Item.DisplayName)),
                Instructions.Stfld(typeof(InventoryPage), "hoverTitle")
            );
        }
        
        static public bool EquipmentClick(ClickableComponent icon) {
            // Check that item type is compatible.
            // And play corresponding sound.
            var helditem = Game1.player.CursorSlotItem;
            // Convert special items (such as copper pan & Lewis pants)
            if (helditem is StardewValley.Tools.Pan) helditem = new Hat (71);
            if (helditem is StardewValley.Object && helditem.ParentSheetIndex == 71) helditem = new Clothing(15);
            if (helditem == null) {
                if (icon.item == null) return false;
                Game1.playSound("dwop");
            } else {
                switch (icon.name) {
                    case "Hat":
                        if (!(helditem is Hat)) return false;
                        Game1.playSound ("grassyStep");
                        break;
                    case "Shirt":
                        if (!(helditem is Clothing)) return false;
                        if ((helditem as Clothing).clothesType.Value != (int)Clothing.ClothesType.SHIRT) return false;
                        Game1.playSound ("sandyStep");
                        break;
                    case "Pants":
                        if (!(helditem is Clothing)) return false;
                        if ((helditem as Clothing).clothesType.Value != (int)Clothing.ClothesType.PANTS) return false;
                        Game1.playSound ("sandyStep");
                        break;
                    case "Boots":
                        if (!(helditem is Boots)) return false;
                        Game1.playSound ("sandyStep");
                        DelayedAction.playSoundAfterDelay ("sandyStep", 150, null);
                        break;
                    default:
                        if (!(helditem is Ring)) return false;
                        Game1.playSound ("crit");
                        break;
                }
            }
            
            // I have no idea why StardewValley does this in InventoryPage::setHeldItem, but I guess it might be important.
            if (helditem != null) {
                helditem.NetFields.Parent = null;
            }
            
            // Update inventory
            Chest chest = GetRingInventory(Game1.player);
            switch (icon.name) {
            case "Hat":          Game1.player.hat.Set (helditem as Hat);            break;
            case "Shirt":        Game1.player.shirtItem.Set (helditem as Clothing); break;
            case "Pants":        Game1.player.pantsItem.Set (helditem as Clothing); break;
            case "Boots":        Game1.player.boots.Set (helditem as Boots);        break;
            case "Left Ring":    Game1.player.leftRing.Set (helditem as Ring);      break;
            case "Right Ring":   Game1.player.rightRing.Set (helditem as Ring);     break;
            case "Extra Ring 1": chest.items[0] = helditem;                         break;
            case "Extra Ring 2": chest.items[1] = helditem;                         break;
            case "Extra Ring 3": chest.items[2] = helditem;                         break;
            case "Extra Ring 4": chest.items[3] = helditem;                         break;
            case "Extra Ring 5": chest.items[4] = helditem;                         break;
            case "Extra Ring 6": chest.items[5] = helditem;                         break;
            default:
                getInstance().Monitor.Log ($"ERROR: Trying to fit equipment item into invalid slot '{icon.name}'", LogLevel.Error);
                return false;
            }

            // Equip/unequip
            (icon.item as Ring )?.onUnequip(Game1.player, Game1.currentLocation);
            (icon.item as Boots)?.onUnequip();
            (helditem as Ring )?.onEquip(Game1.player, Game1.currentLocation);
            (helditem as Boots)?.onEquip();
            
            // Swap items
            Game1.player.CursorSlotItem = Utility.PerformSpecialItemGrabReplacement(icon.item);
            icon.item = helditem;
            return true;
        }
        
        static public bool AutoEquipment(InventoryPage page) {
            var helditem = Game1.player.CursorSlotItem;
            foreach (ClickableComponent icon in page.equipmentIcons) {
                if (icon.item == null && EquipmentClick(icon)) {
                    return true;
                }
            }
            return false;
        }
        
        void InventoryPage_receiveLeftClick() {
            // Handle a ring-inventory slot being clicked.
            InstructionRange code;
            try {
                // Linux & MacOS
                code = FindCode(
                    // switch (equipmentIcon.name) {
                    OpCodes.Ldloc_1,
                    Instructions.Ldfld(typeof(ClickableComponent), nameof(ClickableComponent.name)),
                    OpCodes.Stloc_3,
                    // case null: break;
                    OpCodes.Ldloc_3,
                    OpCodes.Brfalse,
                    // case "Hat":
                    OpCodes.Ldloc_3,
                    Instructions.Ldstr("Hat")
                );
                code.Extend(code.Follow(4));
            } catch (Exception err) {
                LogException(err, LogLevel.Trace);
                // Windows
                code = FindCode(
                    // switch (equipmentIcon.name) {
                    OpCodes.Ldloc_1,
                    Instructions.Ldfld(typeof(ClickableComponent), nameof(ClickableComponent.name)),
                    OpCodes.Stloc_3,
                    // case "Hat":
                    OpCodes.Ldloc_3,
                    Instructions.Ldstr("Hat")
                );
                code.Extend(
                    OpCodes.Ldloc_3,
                    Instructions.Ldstr("Pants"),
                    OpCodes.Call,
                    OpCodes.Brtrue,
                    OpCodes.Br
                );
                code.Extend(code.End.Follow(-1));
            }
            code.Replace(
                code[0],
                Instructions.Call(typeof(ModEntry), nameof(EquipmentClick), typeof(ClickableComponent)),
                Instructions.Pop()
            );
            
            // Select code for equipping items through shift+click.
            code = code.FindNext(
                // if (checkHeldItem ((Item i) => i is Ring))
                OpCodes.Ldarg_0,
                OpCodes.Ldsfld,
                OpCodes.Dup,
                OpCodes.Brtrue,
                //
                OpCodes.Pop,
                OpCodes.Ldsfld,
                OpCodes.Ldftn,
                OpCodes.Newobj,
                OpCodes.Dup,
                OpCodes.Stsfld,
                //
                Instructions.Callvirt(typeof(InventoryPage), "checkHeldItem", typeof(Func<Item, bool>)),
                OpCodes.Brfalse,
                // if (Game1.player.leftRing.Value == null)
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.leftRing)),
                OpCodes.Callvirt,
                OpCodes.Brtrue
            );
            code.Extend(
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.pantsItem)),
                OpCodes.Callvirt,
                OpCodes.Brtrue
            );
            Label equip_failed = (Label)code.End[-1].operand;
            code.Extend(code.End.Follow(-1));
            
            code.Replace(
                Instructions.Ldarg_0(),
                Instructions.Call(typeof(ModEntry), nameof(AutoEquipment), typeof(InventoryPage)),
                // if (true) return;
                Instructions.Brfalse(equip_failed),
                Instructions.Ret()
            );
        }
        #endregion Patch InventoryPage
        
        #region Patch Ring
        void Ring_ctor() {
            var code = FindCode(
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(Ring), nameof(Ring.uniqueID)),
                Instructions.Ldsfld(typeof(Game1), nameof(Game1.year)),
                Instructions.Ldsfld(typeof(Game1), nameof(Game1.dayOfMonth)),
                OpCodes.Add
            );
            code.Extend(
                Instructions.Call_get(typeof(Game1), nameof(Game1.stats)),
                Instructions.Ldfld(typeof(Stats), nameof(Stats.itemsCrafted)),
                OpCodes.Add,
                OpCodes.Callvirt
            );
            code = code.SubRange(2,code.length-3);
            code.Replace(
                // ModEntry.random.Next()
                Instructions.Ldsfld(typeof(ModEntry), nameof(random)),
                Instructions.Call(typeof(Random), nameof(Random.Next))
            );
        }
        #endregion Patch Ring
    }
}

