﻿using System;
using System.Reflection.Emit;
using Microsoft.Xna.Framework;
using Netcode;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace StardewHack.HarvestWithScythe
{
    public enum HarvestMode {
        HANDS,
        SCYTHE,
        BOTH, // I.e. determined by whether the scythe is equipped.
    }

    public class ModConfig {
        /** How should flowers be harvested? 
         * Any Crop that is `programColored` is considered a flower. */
        public HarvestMode HarvestFlowers = HarvestMode.BOTH;
        
        /** How should forage be harvested? 
         * Any Object that `isForage()` is considered forage. */
        public HarvestMode HarvestForage = HarvestMode.BOTH;
        
        /** How should spring onions be harvested?
         * Any Crop that is `forageCrop` is considered a spring onion. */
        public HarvestMode HarvestSpringOnion = HarvestMode.BOTH;
        
        /** How should pluckable crops be harvested? 
         * Any Crop that has `harvestMethod == 0` is considered a pluckable crop. */
        public HarvestMode HarvestPluckableCrops = HarvestMode.BOTH;
        
        /** How should scythable crops be harvested?
         * Any Crop that has `harvestMethod == 1` is considered a scythable crop. */
        public HarvestMode HarvestScythableCrops = HarvestMode.SCYTHE;
    }
    
    /**
     * This is the core of the Harvest With Scythe mod.
     *
     * Crops are either harvested by hand, which is initiatied by HoeDirt.PerformUseAction(), 
     * or harvested by scythe, which is initiated by HoeDirt.PerformToolAction().
     * These methods check whether the crop is allowed to be harvested by this method and 
     * then passes control to Crop.harvest() to perform the actual harvesting. 
     *
     * Crop.Harvest() can do further checks whether harvesting is possible. If not, it returns
     * false to indicate that harvesting failed.
     * 
     * The harvesting behavior, i.e. whether the item drops on the ground (scything) or 
     * is held above the head (plucking) is determined by the value of `harvestMethod`.
     * Hence HoeDirt.Perform*Action must set this field to the appropriate value and restore 
     * it afterwards.
     *
     * Flowers can have different colors, which is not supported by the original scythe harvesting 
     * code. To support it, this mod provides a `CreateObject()` method as a proxy for spawning the
     * dropped crops/flowers.
     *
     * Forage are plain Objects with `isForage()` returning true. Those are handled by
     * Object.performUseAction() and Object.performToolAction(). As the game does not provide
     * logic for scythe harvesting of forage, this is provided by this mod, see ScytheForage().
     *
     */
    public class ModEntry : HackWithConfig<ModEntry, ModConfig>
    {
        // Changes the vector to be pre-multiplied by 64, so it's easier to use for spawning debris.
        // Vector is stored in loc_3.
        private void Crop_harvest_fix_vector() {
            Harmony.CodeInstruction vector2_constructor = null;

            // Remove line (2x)
            // Vector2 vector = new Vector2 ((float)xTile, (float)yTile);
            for (int i = 0; i < 2; i++) {
                var vec = FindCode (
                    OpCodes.Ldloca_S,
                    OpCodes.Ldarg_1,
                    OpCodes.Conv_R4,
                    OpCodes.Ldarg_2,
                    OpCodes.Conv_R4,
                    OpCodes.Call
                );
                vector2_constructor = vec[5];
                vec.Remove();
            }
            
            // Add to begin of function
            // Vector2 vector = new Vector2 ((float)xTile*64., (float)yTile*64.);
            BeginCode().Append(
                Instructions.Ldloca_S(3),
                Instructions.Ldarg_1(),
                Instructions.Conv_R4(),
                Instructions.Ldc_R4(64),
                Instructions.Mul(),
                Instructions.Ldarg_2(),
                Instructions.Conv_R4(),
                Instructions.Ldc_R4(64),
                Instructions.Mul(),
                vector2_constructor
            );
            
            // Replace (4x):
            //   from: new Vector2 (vector.X * 64f, vector.Y * 64f)
            //   to:   vector
            for (int i = 0; i < 4; i++) {
                FindCode(
                    null,
                    OpCodes.Ldfld,
                    Instructions.Ldc_R4(64),
                    OpCodes.Mul,
                    null,
                    OpCodes.Ldfld,
                    Instructions.Ldc_R4(64),
                    OpCodes.Mul,
                    OpCodes.Newobj
                ).Replace(
                    Instructions.Ldloc_3() // vector
                );
            }
        }

        // Support harvesting of spring onions with scythe
        private void Crop_harvest_support_spring_onion() {
            // Note: the branch
            //   if (this.forageCrop)
            // refers mainly to the crop spring union.
            
            // Find the lines:
            var AddItem = FindCode(
                // if (Game1.player.addItemToInventoryBool (@object, false)) {
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                OpCodes.Ldloc_0,
                OpCodes.Ldc_I4_0,
                Instructions.Callvirt(typeof(Farmer), nameof(Farmer.addItemToInventoryBool), typeof(Item), typeof(bool)),
                OpCodes.Brfalse
            );

            // Swap the lines (add '*64' to vector) &
            // Insert check for harvesting with scythe and act accordingly.
            AddItem.Prepend(
                // if (this.harvestMethod != 0) {
                Instructions.Ldarg_0(),
                Instructions.Ldfld(typeof(Crop), nameof(Crop.harvestMethod)),
                Instructions.Call_get(typeof(NetInt), nameof(NetInt.Value)),
                Instructions.Brfalse(AttachLabel(AddItem[0])),
                // Game1.createItemDebris (@object, vector, -1, null, -1)
                Instructions.Ldloc_0(), // @object
                Instructions.Ldloc_3(), // vector
                Instructions.Ldc_I4_M1(), // -1
                Instructions.Ldnull(), // null
                Instructions.Ldc_I4_M1(), // -1
                Instructions.Call(typeof(Game1), nameof(Game1.createItemDebris), typeof(Item), typeof(Vector2), typeof(int), typeof(GameLocation), typeof(int)),
                // Game1.player.gainExperience (2, howMuch);
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                Instructions.Ldc_I4_2(),
                Instructions.Ldloc_1(),
                Instructions.Callvirt(typeof(Farmer), nameof(Farmer.gainExperience), typeof(int), typeof(int)),
                // return true
                Instructions.Ldc_I4_1(),
                Instructions.Ret()
                // }
            );
        }

        // For colored flowers we need to call createItemDebris instead of createObjectDebris
        // Returns the local variable used for storing the quality of the crop.
        private LocalBuilder Crop_harvest_colored_fowers() {
            var code = FindCode(
                // Game1.createObjectDebris (indexOfHarvest, xTile, yTile, -1, num3, 1f, null);
                OpCodes.Ldarg_0,
                OpCodes.Ldfld,
                OpCodes.Call,
                OpCodes.Ldarg_1,
                OpCodes.Ldarg_2,
                OpCodes.Ldc_I4_M1,
                OpCodes.Ldloc_S, // [6] num3: quality
                OpCodes.Ldc_R4,
                OpCodes.Ldnull,
                OpCodes.Call
            );
            var var_quality = code[6].operand as LocalBuilder; // num3
            code.Replace(
                // var tmp = CreateObject(this, num3);
                Instructions.Ldarg_0(), // this
                Instructions.Ldloc_S(var_quality), // num3
                Instructions.Call(typeof(ModEntry), nameof(CreateObject), typeof(Crop), typeof(int)),
                // Game1.createItemDebris(tmp, vector, -1, null, -1);
                Instructions.Ldloc_3(), // vector
                Instructions.Ldc_I4_M1(), // -1
                Instructions.Ldnull(), // null
                Instructions.Ldc_I4_M1(), // -1
                Instructions.Call(typeof(Game1), nameof(Game1.createItemDebris), typeof(Item), typeof(Vector2), typeof(int), typeof(GameLocation), typeof(int))
            );
            return var_quality;
        }

       /**
        * Patch code to drop sunflower seeds when harvesting with scythe.
        * Patch code to let harvesting with scythe drop only 1 item.
        * The other item drops are handled by the plucking code. 
        */
       void Crop_harvest_sunflower_drops(LocalBuilder var_quality) {
            // Remove start of loop
            var start_loop = FindCode(
                // for (int i = 0
                OpCodes.Ldc_I4_0,
                OpCodes.Stloc_S,
                OpCodes.Br,
                // junimoHarvester != null
                Instructions.Ldarg_S(4),
                OpCodes.Brfalse
            );
            
            // Get a reference to the 'i' variable.
            var var_i = start_loop[1].operand as LocalBuilder;
            // Remove the head of the loop.
            start_loop.length = 3;
            start_loop.Remove();

            // Find the start of the 'drop sunflower seeds' part.
            var DropSunflowerSeeds = FindCode(
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(Crop), nameof(Crop.indexOfHarvest)),
                OpCodes.Call, // Netcode
                Instructions.Ldc_I4(421), // 421 = Item ID of Sunflower.
                OpCodes.Bne_Un
            );
            // Set quality for seeds to 0.
            DropSunflowerSeeds.Append(
                Instructions.Ldc_I4_0(),
                Instructions.Stloc_S(var_quality)
            );

            // Remove end of loop and everything after that until the end of the harvest==1 branch.
            var ScytheBranchTail = FindCode(
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(Crop), nameof(Crop.harvestMethod)),
                OpCodes.Call, // Netcode
                OpCodes.Ldc_I4_1,
                OpCodes.Bne_Un
            ).Follow(4);
            ScytheBranchTail.ExtendBackwards(
                Instructions.Ldloc_S(var_i),
                OpCodes.Ldc_I4_1,
                OpCodes.Add,
                Instructions.Stloc_S(var_i),
                Instructions.Ldloc_S(var_i),
                OpCodes.Ldloc_S, // num2
                OpCodes.Blt
            );
            
            // Change jump to end of loop into jump to drop sunflower seeds.
            ScytheBranchTail.ReplaceJump(0, DropSunflowerSeeds[0]);

            // Rewrite the tail of the Scythe harvest branch. 
            ScytheBranchTail.Replace(
                // Jump to the 'drop subflower seeds' part.
                Instructions.Br(AttachLabel(DropSunflowerSeeds[0]))
            );
        }

        [BytecodePatch("StardewValley.Crop::harvest")]
        void Crop_harvest() {
            Crop_harvest_fix_vector();
            Crop_harvest_support_spring_onion();
            var var_quality = Crop_harvest_colored_fowers();
            Crop_harvest_sunflower_drops(var_quality);
            
            if (config.HarvestFlowers == HarvestMode.HANDS) {
                // If the crop is a flower and being harvested with scythe, 
                // return with false indicating harvesting has failed.
                // TODO: this should be moved to HoeDirt.performToolAction().
                var lbl = AttachLabel(instructions[0]);
                BeginCode().Append(
                    // if (harvestMethod==1 && programColored) {
                    Instructions.Ldarg_0(),
                    Instructions.Ldfld(typeof(Crop), nameof(Crop.harvestMethod)),
                    Instructions.Call_get(typeof(NetInt), nameof(NetInt.Value)),
                    Instructions.Brfalse(lbl),
                    Instructions.Ldarg_0(),
                    Instructions.Ldfld(typeof(Crop), nameof(Crop.programColored)),
                    Instructions.Call_get(typeof(NetBool), nameof(NetBool.Value)),
                    Instructions.Brfalse(lbl),
                    // return false
                    Instructions.Ldc_I4_0(),
                    Instructions.Ret()
                    // }
                );
            }
        }

        // Proxy method for creating an object suitable for spawning as debris.
        public static StardewValley.Object CreateObject(Crop crop, int quality) {
            if (crop.programColored.Value) {
                return new StardewValley.Objects.ColoredObject(crop.indexOfHarvest.Value, 1, crop.tintColor.Value) {
                    Quality = quality
                };
            } else {
                return new StardewValley.Object(crop.indexOfHarvest.Value, 1, false, -1, quality);
            }
        }

        [BytecodePatch("StardewValley.TerrainFeatures.HoeDirt::performToolAction")]
        void HoeDirt_performToolAction() {
            // Find the first harvestMethod==1 check.
            var HarvestMethodCheck = FindCode(
                OpCodes.Ldarg_0,
                Instructions.Call_get(typeof(HoeDirt), nameof(HoeDirt.crop)),
                Instructions.Ldfld(typeof(Crop), nameof(Crop.harvestMethod)),
                OpCodes.Call, // Netcode
                OpCodes.Ldc_I4_1,
                OpCodes.Bne_Un
            );

            // Change the harvestMethod==1 check to damage=harvestMethod; harvestMethod=1
            HarvestMethodCheck.Replace(
                // damage = crop.harvestMethod.
                HarvestMethodCheck[0],
                HarvestMethodCheck[1],
                HarvestMethodCheck[2],
                HarvestMethodCheck[3],
                Instructions.Starg_S(2), // damage

                // crop.harvestMethod = 1
                HarvestMethodCheck[0],
                HarvestMethodCheck[1],
                HarvestMethodCheck[2],
                Instructions.Ldc_I4_1(),
                Instructions.Call_set(typeof(NetInt), nameof(NetInt.Value))
            );

            // Set harvestMethod=damage after the following crop!=null check.
            HarvestMethodCheck.FindNext(
                OpCodes.Ldarg_0,
                Instructions.Call_get(typeof(HoeDirt), nameof(HoeDirt.crop)),
                Instructions.Ldfld(typeof(Crop), nameof(Crop.dead)),
                OpCodes.Call, // Netcode
                OpCodes.Brfalse
            ).Prepend(
                HarvestMethodCheck[0],
                HarvestMethodCheck[1],
                HarvestMethodCheck[2],
                Instructions.Ldarg_2(), // damage
                Instructions.Call_set(typeof(NetInt), nameof(NetInt.Value))
            );
        }

        public bool DisableHandHarvesting() {
            return config.HarvestPluckableCrops == HarvestMode.SCYTHE;
        }
        
        [BytecodePatch("StardewValley.TerrainFeatures.HoeDirt::performUseAction", "DisableHandHarvesting")]
        void HoeDirt_performUseAction() {
            var harvest_hand = FindCode(
                OpCodes.Ldarg_0,
                OpCodes.Call,
                OpCodes.Ldfld,
                OpCodes.Call,
                OpCodes.Brtrue
            );
            // Logic here depends on whether flowers can be harvested by scythe.
            if (config.HarvestFlowers == HarvestMode.SCYTHE) {
                // Entirely remove logic related to harvesting by hand.
                harvest_hand.Extend(
                    OpCodes.Ldarg_0,
                    OpCodes.Call,
                    OpCodes.Ldfld,
                    OpCodes.Call,
                    OpCodes.Ldc_I4_1,
                    OpCodes.Bne_Un
                );
                harvest_hand.Remove();
            } else {
                // Only allow harvesting by hand for flowers. Otherwise those would not be harvestable.
                harvest_hand.Replace(
                    harvest_hand[0],
                    harvest_hand[1],
                    Instructions.Ldfld(typeof(Crop), nameof(Crop.programColored)),
                    Instructions.Call_get(typeof(NetBool), nameof(NetBool.Value)),
                    Instructions.Brfalse((Label)harvest_hand[4].operand)
                );
                var harvest_scythe = FindCode(
                    OpCodes.Ldarg_0,
                    OpCodes.Call,
                    OpCodes.Ldfld,
                    OpCodes.Call,
                    OpCodes.Ldc_I4_1,
                    OpCodes.Bne_Un
                );
                harvest_scythe.Replace(
                    harvest_scythe[0],
                    harvest_scythe[1],
                    Instructions.Ldfld(typeof(Crop), nameof(Crop.programColored)),
                    Instructions.Call_get(typeof(NetBool), nameof(NetBool.Value)),
                    Instructions.Brtrue((Label)harvest_scythe[5].operand)
                );
            }
        }

        public bool HarvestForageEnabled() {
            return config.HarvestForage != HarvestMode.HANDS;
        }
        
        [BytecodePatch("StardewValley.Object::performToolAction", "HarvestForageEnabled")]
        void Object_performToolAction() {
            var code = BeginCode();
            Label begin = AttachLabel(code[0]);
            code.Prepend(
                // Check if Tool is scythe.
                Instructions.Ldarg_1(),
                Instructions.Isinst(typeof(StardewValley.Tools.MeleeWeapon)),
                Instructions.Brfalse(begin),
                Instructions.Ldarg_1(),
                Instructions.Isinst(typeof(StardewValley.Tools.MeleeWeapon)),
                Instructions.Callvirt_get(typeof(Tool), nameof(Tool.BaseName)),
                Instructions.Ldstr("Scythe"),
                Instructions.Callvirt(typeof(string), nameof(string.Equals), typeof(string)),
                Instructions.Brfalse(begin),
                // Hook
                Instructions.Ldarg_0(),
                Instructions.Ldarg_1(),
                Instructions.Ldarg_2(),
                Instructions.Call(typeof(ModEntry), nameof(ScytheForage), typeof(StardewValley.Object), typeof(Tool), typeof(GameLocation)),
                Instructions.Brfalse(begin),
                Instructions.Ldc_I4_1(),
                Instructions.Ret()
            );
        }

        public static bool ScytheForage(StardewValley.Object o, Tool t, GameLocation loc) {
            if (o.IsSpawnedObject && !o.questItem.Value && o.isForage(loc)) {
                var who = t.getLastFarmerToUse();
                var vector = o.TileLocation;
                // For objects stored in GameLocation.Objects, the TileLocation is not always set.
                // So determine its location by looping trough all such objects.
#pragma warning disable RECS0018 // Comparison of floating point numbers with equality operator
                if (vector.X==0 && vector.Y==0) {
#pragma warning restore RECS0018 // Comparison of floating point numbers with equality operator
                    foreach (System.Collections.Generic.KeyValuePair<Vector2, StardewValley.Object> pair in loc.Objects.Pairs) {
                        if (pair.Value.Equals(o)) {
                            vector = pair.Key;
                            break;
                        }
                    }
                }
                int quality = o.Quality;
                Random random = new Random((int)Game1.uniqueIDForThisGame / 2 + (int)Game1.stats.DaysPlayed + (int)vector.X + (int)vector.Y * 777);
                if (who.professions.Contains(16)) {
                    quality = 4;
                } else if (random.NextDouble() < (double)((float)who.ForagingLevel / 30)) {
                    quality = 2;
                } else if (random.NextDouble() < (double)((float)who.ForagingLevel / 15)) {
                    quality = 1;
                }
                who.gainExperience(2, 7);
                Game1.createObjectDebris(o.ParentSheetIndex, (int)vector.X, (int)vector.Y, -1, quality, 1, loc);
                Game1.stats.ItemsForaged += 1;
                if (who.professions.Contains(13) && random.NextDouble() < 0.2) {
                    Game1.createObjectDebris(o.ParentSheetIndex, (int)vector.X, (int)vector.Y, -1, quality, 1, loc);
                    who.gainExperience(2, 7);
                }
                return true;
            } else {
                return false;
            }
        }
    }
}

