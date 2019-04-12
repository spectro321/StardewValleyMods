﻿//Copyright (c) 2019 Jahangmar

//This program is free software: you can redistribute it and/or modify
//it under the terms of the GNU Lesser General Public License as published by
//the Free Software Foundation, either version 3 of the License, or
//(at your option) any later version.

//This program is distributed in the hope that it will be useful,
//but WITHOUT ANY WARRANTY; without even the implied warranty of
//MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//GNU Lesser General Public License for more details.

//You should have received a copy of the GNU Lesser General Public License
//along with this program. If not, see <https://www.gnu.org/licenses/>.

using System.Collections.Generic;

using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Locations;
using StardewValley.Objects;

using Microsoft.Xna.Framework;
using StardewValley.Tools;

namespace InteractionTweaks
{
    public class EatingBlockingFeature : ModFeature
    {
        private static bool isEating = false;

        public static new void Enable()
        {
            Helper.Events.Input.ButtonPressed += Input_ButtonPressed;
        }

        public static new void Disable()
        {
            Helper.Events.Input.ButtonPressed -= Input_ButtonPressed;
        }

        static void Input_ButtonPressed(object sender, StardewModdingAPI.Events.ButtonPressedEventArgs e)
        {
            GameLocation location = Game1.currentLocation;
            Farmer player = Game1.player;

            if (location != null && e.Button.IsActionButton())
            {
                Vector2 grabTileVec = e.Cursor.GrabTile;
                Vector2 cursorScreenPos = e.Cursor.ScreenPixels;
                Vector2 cursorMapPos = e.Cursor.AbsolutePixels;
                Object objAtGrabTile = location.getObjectAtTile((int)grabTileVec.X, (int)grabTileVec.Y);

                //player.isEating = true;

                if (Game1.activeClickableMenu != null
                    || objAtGrabTile?.heldObject?.Value != null && objAtGrabTile.heldObject.Value.readyForHarvest //interactable containers (e.g. preserver jars) has finished product
                    || location.isActionableTile((int)grabTileVec.X, (int)grabTileVec.Y, player) //isActionableTile checks stuff like doors, chests, ...
                    || objAtGrabTile != null && (objAtGrabTile.isForage(location) || objAtGrabTile.isAnimalProduct()) //forage and animal products
                    || (location is Farm && grabTileVec.X >= 71 && grabTileVec.X <= 72 && grabTileVec.Y >= 13 && grabTileVec.Y <= 14) //shippingBin on Farm map
                    || Game1.getFarm().getAllFarmAnimals().Exists((animal) => animal.currentLocation == location && AnimalCollision(animal, cursorMapPos)) //animals
                    || location.doesPositionCollideWithCharacter((int)cursorMapPos.X, (int)cursorMapPos.Y) != null || location.doesPositionCollideWithCharacter((int)cursorMapPos.X, (int)cursorMapPos.Y + Game1.tileSize) != null //character, important for gifting and speaking

                /*|| player.isRidingHorse()*/ || !player.canMove)
                {
                    return;
                }

                if (player.ActiveObject != null && objAtGrabTile != null && objAtGrabTile.performObjectDropInAction(player.ActiveObject, true, player)) //container (e.g. preserver jar) accepts input
                {
                    Monitor.Log($"true == player.ActiveObject != null && obj != null && obj.performObjectDropInAction(player.ActiveObject, true, player)");
                    objAtGrabTile.heldObject.Value = null; //performObjectDropInAction sets heldObject so we reset it
                    return;
                }

                if (false/*Config.WeaponBlockingFeature*/ && player.CurrentTool is MeleeWeapon && NotInFightingLocation())
                {
                    Helper.Input.Suppress(e.Button);
                }
                else if (Config.EatingFeature && player.CurrentItem is Object food && food.Edibility > -300 &&
                    !isEating && player.ActiveObject != null && !Game1.dialogueUp && !Game1.eventUp && !player.canOnlyWalk && !player.FarmerSprite.PauseForSingleAnimation && !Game1.fadeToBlack)
                {
                    Monitor.Log("Eating " + food.Name + "; Edibility is " + food.Edibility, LogLevel.Trace);

                    player.faceDirection(2);
                    isEating = true;
                    player.itemToEat = player.ActiveObject;
                    player.FarmerSprite.setCurrentSingleAnimation(304);

                    int untilFull = UntilFull(player, food);

                    Monitor.Log($"Until full is {untilFull}, staminaInc: {StaminaInc(food)}*{untilFull}, new: {player.Stamina + StaminaInc(food) * untilFull}/{player.MaxStamina}, healthInc: {HealthInc(food)}*{untilFull}, new: {player.health + HealthInc(food) * untilFull}/{player.maxHealth}", LogLevel.Trace);

                    Response[] responses = {
                            new Response ("One", GetTrans("dia.eatanswerone")),
                            new Response ("Multi", GetTrans("dia.eatanswermulti", new { amount = untilFull})),
                            new Response ("No", GetTrans("dia.eatanswerno"))
                        };
                    Response[] noMultiResponses =
                    {
                        new Response ("One", GetTrans("dia.eatanswerone")),
                        new Response ("No", GetTrans("dia.eatanswerno"))
                    };
                    location.createQuestionDialogue(GetTrans("dia.eatquestion", new { item = food.DisplayName }), (food.Edibility > 0 && untilFull > 1) ? responses : noMultiResponses, delegate (Farmer _, string answer)
                    {
                        switch (answer)
                        {
                            case "One":
                                player.eatHeldObject();
                                break;
                            case "Multi":
                                Monitor.Log("Eating stack", LogLevel.Trace);
                                float oldStamina = player.Stamina;
                                int oldHealth = player.health;
                                EatFood(player, food, untilFull - 1);
                                Monitor.Log("Eating last object", LogLevel.Trace);
                                float midStamina = player.Stamina;
                                int midHealth = player.health;
                                player.eatHeldObject();
                                HUDMessages(oldStamina, oldHealth, midStamina, midHealth);
                                break;
                        }
                        isEating = false;
                    });
                    Helper.Input.Suppress(e.Button);
                }
            }
        }

        private static bool NotInFightingLocation()
        {
            GameLocation loc = Game1.currentLocation;
            bool nomonster = true;
            foreach (NPC npc in loc.getCharacters())
            {
                if (npc.IsMonster)
                {
                    nomonster = false;
                    break;
                }
            }
            return nomonster;
            /*
            loc is Farm && Game1.whichFarm == 4 //wilderness farm
                || loc is MineShaft
                || loc is StardewValley.Locations.Woods
                */               
        }

        /// <summary>
        /// Checks if cursor position collides with animal bounding box
        /// </summary>
        /// <param name="animal">Animal.</param>
        /// <param name="mapVec">The position of the cursor relative to the top-left corner of the map.</param>
        private static bool AnimalCollision(FarmAnimal animal, Vector2 mapVec)
        {
            return animal.GetBoundingBox().Intersects(new Rectangle((int)mapVec.X, (int)mapVec.Y, 1, 1));
        }

        /// <summary>
        /// Increase of stamina as calculated by the game.
        /// </summary>
        /// <param name="food">Food.</param>
        private static int StaminaInc(Object food)
        {
            return (int)System.Math.Ceiling((double)@food.Edibility * 2.5) + (int)@food.quality * @food.Edibility;
        }

        /// <summary>
        /// Increase of health as calculated by the game.
        /// </summary>
        /// <param name="food">Food.</param>
        private static int HealthInc(Object food)
        {
            return ((@food.Edibility >= 0) ? ((int)((float)StaminaInc(food) * 0.45f)) : 0);
        }

        /// <summary>
        /// Returns the amount of items of the given item stack that have to be eaten until the players health or stamina is full
        /// </summary>
        /// <param name="player">Player.</param>
        /// <param name="food">Food.</param>
        private static int UntilFull(Farmer player, Object food)
        {
            int sinc = StaminaInc(food);
            int hinc = HealthInc(food);
            return System.Math.Min(System.Math.Max((int)System.Math.Ceiling((player.MaxStamina - player.Stamina) / sinc), (int)System.Math.Ceiling((player.maxHealth - (float)player.health) / hinc)), food.Stack);
        }

        private static void EatFood(Farmer player, Object food, int redAmount)
        {
            Monitor.Log($"Setting Stamina to System.Math.Min({(float)player.MaxStamina}, {player.Stamina + (float)StaminaInc(food) * redAmount})", LogLevel.Trace);

            player.Stamina = System.Math.Min((float)player.MaxStamina, player.Stamina + (float)StaminaInc(food) * redAmount);

            Monitor.Log($"Setting health to System.Math.Min({player.maxHealth}, {player.health + HealthInc(food) * redAmount})", LogLevel.Trace);

            player.health = System.Math.Min(player.maxHealth, player.health + HealthInc(food) * redAmount);
            player.removeItemsFromInventory(food.ParentSheetIndex, redAmount);
        }
        /// <summary>
        /// Shows health and stamina messages.
        /// </summary>
        private static void HUDMessages(float oldStamina, int oldHealth, float midStamina, int midHealth)
        {
            if (midStamina > oldStamina)
            {
                string staminaText = Game1.content.LoadString("Strings\\StringsFromCSFiles:Game1.cs.3116", (int)(midStamina - oldStamina));
                Game1.addHUDMessage(new HUDMessage(staminaText, HUDMessage.stamina_type));
            }
            if (midHealth > oldHealth)
            {
                string healthText = Game1.content.LoadString("Strings\\StringsFromCSFiles:Game1.cs.3118", midHealth - oldHealth);
                Game1.addHUDMessage(new HUDMessage(healthText, HUDMessage.health_type));
            }
        }

    }
}
