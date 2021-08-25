﻿using AmeisenBotX.Common.Utils;
using AmeisenBotX.Core.Engines.Character.Comparators;
using AmeisenBotX.Core.Engines.Character.Spells.Objects;
using AmeisenBotX.Core.Engines.Character.Talents.Objects;
using AmeisenBotX.Core.Engines.Combat.Helpers.Healing;
using AmeisenBotX.Core.Engines.Movement.Enums;
using AmeisenBotX.Core.Logic.Utils.Auras.Objects;
using AmeisenBotX.Wow.Objects.Enums;
using AmeisenBotX.Wow335a.Constants;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AmeisenBotX.Core.Engines.Combat.Classes.Jannis
{
    public class PaladinHoly : BasicCombatClass
    {
        public PaladinHoly(AmeisenBotInterfaces bot) : base(bot)
        {
            Configurables.TryAdd("AttackInGroups", true);
            Configurables.TryAdd("AttackInGroupsUntilManaPercent", 85.0);
            Configurables.TryAdd("AttackInGroupsCloseCombat", false);

            MyAuraManager.Jobs.Add(new KeepActiveAuraJob(bot.Db, Paladin335a.BlessingOfWisdom, () => TryCastSpell(Paladin335a.BlessingOfWisdom, Bot.Wow.PlayerGuid, true)));
            MyAuraManager.Jobs.Add(new KeepActiveAuraJob(bot.Db, Paladin335a.DevotionAura, () => TryCastSpell(Paladin335a.DevotionAura, Bot.Wow.PlayerGuid, true)));
            MyAuraManager.Jobs.Add(new KeepActiveAuraJob(bot.Db, Paladin335a.SealOfWisdom, () => Bot.Character.SpellBook.IsSpellKnown(Paladin335a.SealOfWisdom) && TryCastSpell(Paladin335a.SealOfWisdom, Bot.Wow.PlayerGuid, true)));
            MyAuraManager.Jobs.Add(new KeepActiveAuraJob(bot.Db, Paladin335a.SealOfVengeance, () => !Bot.Character.SpellBook.IsSpellKnown(Paladin335a.SealOfWisdom) && TryCastSpell(Paladin335a.SealOfVengeance, Bot.Wow.PlayerGuid, true)));

            GroupAuraManager.SpellsToKeepActiveOnParty.Add((Paladin335a.BlessingOfWisdom, (spellName, guid) => TryCastSpell(spellName, guid, true)));

            HealingManager = new(bot, (string spellName, ulong guid) => { return TryCastSpell(spellName, guid); });

            // make sure all new spells get added to the healing manager
            Bot.Character.SpellBook.OnSpellBookUpdate += () =>
            {
                if (Bot.Character.SpellBook.TryGetSpellByName(Paladin335a.FlashOfLight, out Spell spellFlashOfLight))
                {
                    HealingManager.AddSpell(spellFlashOfLight);
                }

                if (Bot.Character.SpellBook.TryGetSpellByName(Paladin335a.HolyLight, out Spell spellHolyLight))
                {
                    HealingManager.AddSpell(spellHolyLight);
                }

                if (Bot.Character.SpellBook.TryGetSpellByName(Paladin335a.HolyShock, out Spell spellHolyShock))
                {
                    HealingManager.AddSpell(spellHolyShock);
                }

                if (Bot.Character.SpellBook.TryGetSpellByName(Paladin335a.LayOnHands, out Spell spellLayOnHands))
                {
                    HealingManager.AddSpell(spellLayOnHands);
                }
            };

            SpellAbortFunctions.Add(HealingManager.ShouldAbortCasting);
        }

        public override string Description => "Half-Smart CombatClass for the Holy Paladin spec.";

        public override string DisplayName => "Paladin Holy";

        public override bool HandlesMovement => false;

        public override bool IsMelee => false;

        public override IItemComparator ItemComparator { get; set; } = new BasicComparator
        (
            null,
            new() { WowWeaponType.TWOHANDED_AXES, WowWeaponType.TWOHANDED_MACES, WowWeaponType.TWOHANDED_SWORDS },
            new Dictionary<string, double>()
            {
                { "ITEM_MOD_CRIT_RATING_SHORT", 0.88 },
                { "ITEM_MOD_INTELLECT_SHORT", 0.2 },
                { "ITEM_MOD_SPELL_POWER_SHORT", 0.68 },
                { "ITEM_MOD_HASTE_RATING_SHORT", 0.71},
            }
        );

        public override WowRole Role => WowRole.Heal;

        public override TalentTree Talents { get; } = new()
        {
            Tree1 = new()
            {
                { 1, new(1, 1, 5) },
                { 3, new(1, 3, 3) },
                { 4, new(1, 4, 5) },
                { 6, new(1, 6, 1) },
                { 7, new(1, 7, 5) },
                { 8, new(1, 8, 1) },
                { 10, new(1, 10, 2) },
                { 13, new(1, 13, 1) },
                { 14, new(1, 14, 3) },
                { 16, new(1, 16, 5) },
                { 17, new(1, 17, 3) },
                { 18, new(1, 18, 1) },
                { 21, new(1, 21, 5) },
                { 22, new(1, 22, 1) },
                { 23, new(1, 23, 5) },
                { 24, new(1, 24, 2) },
                { 25, new(1, 25, 2) },
                { 26, new(1, 26, 1) },
            },
            Tree2 = new()
            {
                { 1, new(2, 1, 5) },
            },
            Tree3 = new()
            {
                { 2, new(3, 2, 5) },
                { 4, new(3, 4, 3) },
                { 5, new(3, 5, 2) },
                { 7, new(3, 7, 5) },
            },
        };

        public override bool UseAutoAttacks => false;

        public override string Version => "1.1";

        public override bool WalkBehindEnemy => false;

        public override WowClass WowClass => WowClass.Paladin;

        private HealingManager HealingManager { get; }

        public override void Execute()
        {
            base.Execute();

            if (Bot.Player.ManaPercentage < 50
               && Bot.Player.ManaPercentage > 20
               && TryCastSpell(Paladin335a.DivineIllumination, 0, true))
            {
                return;
            }

            if (Bot.Player.ManaPercentage < 60
                && TryCastSpell(Paladin335a.DivinePlea, 0, true))
            {
                return;
            }

            if (NeedToHealSomeone())
            {
                return;
            }
            else
            {
                bool isAlone = !Bot.Objects.Partymembers.Any(e => e.Guid != Bot.Player.Guid);

                if ((isAlone || (Configurables["AttackInGroups"] && Configurables["AttackInGroupsUntilManaPercent"] < Bot.Player.ManaPercentage))
                    && SelectTarget(TargetProviderDps))
                {
                    if ((Bot.Player.Auras.Any(e => Bot.Db.GetSpellName(e.SpellId) == Paladin335a.SealOfVengeance) || Bot.Player.Auras.Any(e => Bot.Db.GetSpellName(e.SpellId) == Paladin335a.SealOfWisdom))
                        && TryCastSpell(Paladin335a.JudgementOfLight, Bot.Wow.TargetGuid, true))
                    {
                        return;
                    }

                    if (TryCastSpell(Paladin335a.Exorcism, Bot.Wow.TargetGuid, true))
                    {
                        return;
                    }

                    // either we are alone or allowed to go close combat in groups
                    if (isAlone || Configurables["AttackInGroupsCloseCombat"])
                    {
                        if (!Bot.Player.IsAutoAttacking
                            && Bot.Target.Position.GetDistance(Bot.Player.Position) < 3.5f
                            && EventAutoAttack.Run())
                        {
                            Bot.Wow.StartAutoAttack();
                            return;
                        }
                        else
                        {
                            Bot.Movement.SetMovementAction(MovementAction.Move, Bot.Target.Position);
                            return;
                        }
                    }
                }
            }
        }

        public override void Load(Dictionary<string, JsonElement> objects)
        {
            base.Load(objects);

            if (objects.ContainsKey("HealingManager"))
            {
                Dictionary<string, JsonElement> s = objects["HealingManager"].To<Dictionary<string, JsonElement>>();

                if (s.TryGetValue("SpellHealing", out JsonElement j)) { HealingManager.SpellHealing = j.To<Dictionary<string, int>>(); }
                if (s.TryGetValue("DamageMonitorSeconds", out j)) { HealingManager.DamageMonitorSeconds = j.To<int>(); }
                if (s.TryGetValue("HealthWeight", out j)) { HealingManager.HealthWeightMod = j.To<float>(); }
                if (s.TryGetValue("DamageWeight", out j)) { HealingManager.IncomingDamageMod = j.To<float>(); }
                if (s.TryGetValue("OverhealingStopThreshold", out j)) { HealingManager.OverhealingStopThreshold = j.To<float>(); }
                if (s.TryGetValue("TargetDyingSeconds", out j)) { HealingManager.TargetDyingSeconds = j.To<int>(); }
            }
        }

        public override void OutOfCombatExecute()
        {
            base.OutOfCombatExecute();

            if (NeedToHealSomeone())
            {
                return;
            }
        }

        public override Dictionary<string, object> Save()
        {
            Dictionary<string, object> s = base.Save();

            s.Add("HealingManager", new Dictionary<string, object>()
            {
                { "SpellHealing", HealingManager.SpellHealing },
                { "DamageMonitorSeconds", HealingManager.DamageMonitorSeconds },
                { "HealthWeight", HealingManager.HealthWeightMod },
                { "DamageWeight", HealingManager.IncomingDamageMod },
                { "OverhealingStopThreshold", HealingManager.OverhealingStopThreshold },
                { "TargetDyingSeconds", HealingManager.TargetDyingSeconds },
            });

            return s;
        }

        private bool NeedToHealSomeone()
        {
            // TODO: bugged need to figure out why cooldown is always wrong
            // if (targetUnit.HealthPercentage < 50
            //     && CastSpellIfPossible(divineFavor, targetUnit.Guid, true))
            // {
            //     LastHealAction = DateTime.Now;
            //     return true;
            // }

            if (Bot.Player.Auras.Any(e => Bot.Db.GetSpellName(e.SpellId) == Paladin335a.BeaconOfLight)
                && TryCastSpell(Paladin335a.BeaconOfLight, Bot.Player.Guid, true))
            {
                return true;
            }

            if (HealingManager.Tick())
            {
                return true;
            }

            return false;
        }
    }
}