﻿using AmeisenBotX.Core.Character.Comparators;
using AmeisenBotX.Core.Character.Inventory.Enums;
using AmeisenBotX.Core.Character.Talents.Objects;
using AmeisenBotX.Core.Data.Enums;
using AmeisenBotX.Core.Fsm;
using AmeisenBotX.Core.Fsm.Utils.Auras.Objects;

namespace AmeisenBotX.Core.Combat.Classes.Jannis
{
    public class DeathknightFrost : BasicCombatClass
    {
        public DeathknightFrost(WowInterface wowInterface, AmeisenBotFsm stateMachine) : base(wowInterface, stateMachine)
        {
            MyAuraManager.Jobs.Add(new KeepActiveAuraJob(frostPresenceSpell, () => TryCastSpellDk(frostPresenceSpell, 0)));
            MyAuraManager.Jobs.Add(new KeepActiveAuraJob(hornOfWinterSpell, () => TryCastSpellDk(hornOfWinterSpell, 0, true)));

            TargetAuraManager.Jobs.Add(new KeepActiveAuraJob(frostFeverSpell, () => TryCastSpellDk(icyTouchSpell, WowInterface.TargetGuid, false, false, false, true)));
            TargetAuraManager.Jobs.Add(new KeepActiveAuraJob(bloodPlagueSpell, () => TryCastSpellDk(plagueStrikeSpell, WowInterface.TargetGuid, false, false, false, true)));

            InterruptManager.InterruptSpells = new()
            {
                { 0, (x) => TryCastSpellDk(mindFreezeSpell, x.Guid, true) },
                { 1, (x) => TryCastSpellDk(strangulateSpell, x.Guid, false, true) }
            };
        }

        public override string Description => "FCFS based CombatClass for the Frost Deathknight spec.";

        public override string Displayname => "Deathknight Frost";

        public override bool HandlesMovement => false;

        public override bool IsMelee => true;

        public override IItemComparator ItemComparator { get; set; } = new BasicStrengthComparator(new() { WowArmorType.SHIELDS });

        public override WowRole Role => WowRole.Dps;

        public override TalentTree Talents { get; } = new()
        {
            Tree1 = new()
            {
                { 2, new(1, 2, 3) },
            },
            Tree2 = new()
            {
                { 1, new(2, 1, 3) },
                { 2, new(2, 2, 2) },
                { 5, new(2, 5, 2) },
                { 6, new(2, 6, 3) },
                { 7, new(2, 7, 5) },
                { 9, new(2, 9, 3) },
                { 10, new(2, 10, 5) },
                { 11, new(2, 11, 2) },
                { 12, new(2, 12, 2) },
                { 14, new(2, 14, 3) },
                { 16, new(2, 16, 1) },
                { 17, new(2, 17, 2) },
                { 18, new(2, 18, 3) },
                { 22, new(2, 22, 3) },
                { 23, new(2, 23, 3) },
                { 24, new(2, 24, 1) },
                { 26, new(2, 26, 1) },
                { 27, new(2, 27, 3) },
                { 28, new(2, 28, 5) },
                { 29, new(2, 29, 1) },
            },
            Tree3 = new()
            {
                { 1, new(3, 1, 2) },
                { 2, new(3, 2, 3) },
                { 4, new(3, 4, 2) },
                { 7, new(3, 7, 3) },
                { 9, new(3, 9, 5) },
            },
        };

        public override bool UseAutoAttacks => true;

        public override string Version => "1.0";

        public override bool WalkBehindEnemy => false;

        public override WowClass WowClass => WowClass.Deathknight;

        public override void Execute()
        {
            base.Execute();

            if (SelectTarget(TargetProviderDps))
            {
                if (WowInterface.Target.TargetGuid != WowInterface.PlayerGuid
                   && TryCastSpellDk(darkCommandSpell, WowInterface.TargetGuid))
                {
                    return;
                }

                if (!WowInterface.Target.HasBuffByName(chainsOfIceSpell)
                    && WowInterface.Target.Position.GetDistance(WowInterface.Player.Position) > 2.0
                    && TryCastSpellDk(chainsOfIceSpell, WowInterface.TargetGuid, false, false, true))
                {
                    return;
                }

                if (WowInterface.Target.HasBuffByName(chainsOfIceSpell)
                    && TryCastSpellDk(chainsOfIceSpell, WowInterface.TargetGuid, false, false, true))
                {
                    return;
                }

                if (TryCastSpellDk(empowerRuneWeapon, 0))
                {
                    return;
                }

                if ((WowInterface.Player.HealthPercentage < 60
                        && TryCastSpellDk(iceboundFortitudeSpell, 0, true))
                    || TryCastSpellDk(unbreakableArmorSpell, 0, false, false, true)
                    || TryCastSpellDk(obliterateSpell, WowInterface.TargetGuid, false, false, true, true)
                    || TryCastSpellDk(bloodStrikeSpell, WowInterface.TargetGuid, false, true)
                    || TryCastSpellDk(deathCoilSpell, WowInterface.TargetGuid, true)
                    || (WowInterface.Player.Runeenergy > 60
                        && TryCastSpellDk(runeStrikeSpell, WowInterface.TargetGuid)))
                {
                    return;
                }
            }
        }

        public override void OutOfCombatExecute()
        {
            base.OutOfCombatExecute();
        }
    }
}