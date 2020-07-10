﻿using AmeisenBotX.Core.Character.Inventory.Enums;
using AmeisenBotX.Core.Common;
using AmeisenBotX.Core.Data.Objects.WowObject;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace AmeisenBotX.Core.Statemachine.States
{
    public class StateIdle : BasicState
    {
        public StateIdle(AmeisenBotStateMachine stateMachine, AmeisenBotConfig config, WowInterface wowInterface) : base(stateMachine, config, wowInterface)
        {
            FirstStart = true;

            LastBagSlotCheck = new TimegatedEvent(TimeSpan.FromMilliseconds(5000));
            LastEatCheck = new TimegatedEvent(TimeSpan.FromMilliseconds(2000));
            LastLoot = new TimegatedEvent(TimeSpan.FromMilliseconds(2000));
            LastRepairCheck = new TimegatedEvent(TimeSpan.FromMilliseconds(5000));
        }

        public bool FirstStart { get; set; }

        private TimegatedEvent LastBagSlotCheck { get; set; }

        private TimegatedEvent LastEatCheck { get; set; }

        private TimegatedEvent LastLoot { get; set; }

        private TimegatedEvent LastRepairCheck { get; set; }

        public override void Enter()
        {
            if (FirstStart)
            {
                while (!WowInterface.ObjectManager.IsWorldLoaded)
                {
                    WowInterface.ObjectManager.RefreshIsWorldLoaded();
                    Thread.Sleep(100);
                }

                FirstStart = false;
                WowInterface.XMemory.ReadString(WowInterface.OffsetList.PlayerName, Encoding.ASCII, out string playerName);
                StateMachine.PlayerName = playerName;

                if (!WowInterface.EventHookManager.IsActive)
                {
                    WowInterface.EventHookManager.Start();
                }
            }

            WowInterface.CharacterManager.UpdateAll();
            WowInterface.HookManager.SetMaxFps((byte)Config.MaxFps);
            WowInterface.HookManager.EnableClickToMove();
        }

        public override void Execute()
        {
            // do we need to loot stuff
            if (LastLoot.Run()
                && StateMachine.GetNearLootableUnits().Count() > 0)
            {
                StateMachine.SetState((int)BotState.Looting);
                return;
            }

            // do we need to eat something
            if (LastEatCheck.Run()
                // Refreshment
                && ((WowInterface.ObjectManager.Player.HealthPercentage < Config.EatHealthPercent
                         && WowInterface.ObjectManager.Player.ManaPercentage < Config.DrinkHealthPercent
                         && WowInterface.CharacterManager.HasRefreshmentInBag())
                     // Food
                     || (WowInterface.ObjectManager.Player.HealthPercentage < Config.EatHealthPercent
                         && WowInterface.CharacterManager.HasFoodInBag())
                     // Water
                     || (WowInterface.ObjectManager.Player.MaxMana > 0
                         && WowInterface.ObjectManager.Player.ManaPercentage < Config.DrinkHealthPercent
                         && WowInterface.CharacterManager.HasWaterInBag())))
            {
                StateMachine.SetState((int)BotState.Eating);
                return;
            }

            // we are on a battleground
            if (WowInterface.XMemory.Read(WowInterface.OffsetList.BattlegroundStatus, out int bgStatus)
                && bgStatus == 3
                && !Config.BattlegroundUsePartyMode)
            {
                StateMachine.SetState((int)BotState.Battleground);
                return;
            }

            // we are in a dungeon
            if (StateMachine.IsDungeonMap(WowInterface.ObjectManager.MapId)
                && !Config.DungeonUsePartyMode)
            {
                StateMachine.SetState((int)BotState.Dungeon);
                return;
            }

            // do i need to follow someone
            if (!Config.Autopilot && IsUnitToFollowThere())
            {
                StateMachine.SetState((int)BotState.Following);
                return;
            }

            // do we need to repair our equipment
            if (LastRepairCheck.Run()
                && IsRepairNpcNear())
            {
                WowInterface.CharacterManager.Equipment.Update();
                if (WowInterface.CharacterManager.Equipment.Items.Any(e => e.Value.MaxDurability > 0 && e.Value.Durability <= Config.ItemRepairThreshold))
                {
                    StateMachine.SetState((int)BotState.Repairing);
                    return;
                }
            }

            // do we need to sell stuff
            if (LastBagSlotCheck.Run()
                && IsVendorNpcNear()
                && WowInterface.CharacterManager.Inventory.FreeBagSlots < Config.BagSlotsToGoSell
                && WowInterface.CharacterManager.Inventory.Items.Where(e => !Config.ItemSellBlacklist.Contains(e.Name)
                       && ((Config.SellGrayItems && e.ItemQuality == ItemQuality.Poor)
                           || (Config.SellWhiteItems && e.ItemQuality == ItemQuality.Common)
                           || (Config.SellGreenItems && e.ItemQuality == ItemQuality.Uncommon)
                           || (Config.SellBlueItems && e.ItemQuality == ItemQuality.Rare)
                           || (Config.SellPurpleItems && e.ItemQuality == ItemQuality.Epic)))
                   .Any(e => e.Price > 0))
            {
                StateMachine.SetState((int)BotState.Selling);
                return;
            }

            // do buffing etc...
            WowInterface.CombatClass?.OutOfCombatExecute();
        }

        public override void Exit()
        {
        }

        public bool IsUnitToFollowThere()
        {
            WowPlayer playerToFollow = null;

            // TODO: make this crap less redundant
            // check the specific character
            List<WowPlayer> wowPlayers = WowInterface.ObjectManager.WowObjects.OfType<WowPlayer>().ToList();
            if (wowPlayers.Count > 0)
            {
                if (Config.FollowSpecificCharacter)
                {
                    playerToFollow = wowPlayers.FirstOrDefault(p => p.Name.Equals(Config.SpecificCharacterToFollow, StringComparison.OrdinalIgnoreCase));
                    playerToFollow = SkipIfOutOfRange(playerToFollow);
                }

                // check the group/raid leader
                if (playerToFollow == null && Config.FollowGroupLeader)
                {
                    playerToFollow = wowPlayers.FirstOrDefault(p => p.Guid == WowInterface.ObjectManager.PartyleaderGuid);
                    playerToFollow = SkipIfOutOfRange(playerToFollow);
                }

                // check the group members
                if (playerToFollow == null && Config.FollowGroupMembers)
                {
                    playerToFollow = wowPlayers.FirstOrDefault(p => WowInterface.ObjectManager.PartymemberGuids.Contains(p.Guid));
                    playerToFollow = SkipIfOutOfRange(playerToFollow);
                }
            }

            return playerToFollow != null;
        }

        internal bool IsRepairNpcNear()
        {
            return WowInterface.ObjectManager.WowObjects.OfType<WowUnit>()
                       .Any(e => e.GetType() != typeof(WowPlayer)
                       && e.IsRepairVendor
                       && e.Position.GetDistance(WowInterface.ObjectManager.Player.Position) < Config.RepairNpcSearchRadius);
        }

        internal bool IsVendorNpcNear()
        {
            return WowInterface.ObjectManager.WowObjects.OfType<WowUnit>()
                       .Any(e => e.GetType() != typeof(WowPlayer)
                       && e.IsVendor
                       && e.Position.GetDistance(WowInterface.ObjectManager.Player.Position) < Config.MerchantNpcSearchRadius);
        }

        private void CheckForBattlegroundInvites()
        {
            if (WowInterface.XMemory.Read(WowInterface.OffsetList.BattlegroundStatus, out int bgStatus)
                && bgStatus == 2)
            {
                WowInterface.HookManager.AcceptBattlegroundInvite();
            }
        }

        private WowPlayer SkipIfOutOfRange(WowPlayer playerToFollow)
        {
            if (playerToFollow != null)
            {
                double distance = playerToFollow.Position.GetDistance(WowInterface.ObjectManager.Player.Position);
                if (UnitIsOutOfRange(distance))
                {
                    playerToFollow = null;
                }
            }

            return playerToFollow;
        }

        private bool UnitIsOutOfRange(double distance)
        {
            return (distance < Config.MinFollowDistance || distance > Config.MaxFollowDistance);
        }
    }
}