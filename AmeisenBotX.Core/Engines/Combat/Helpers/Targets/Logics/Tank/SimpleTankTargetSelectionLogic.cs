﻿using AmeisenBotX.Core.Engines.Combat.Helpers.Targets.Priority.Special;
using AmeisenBotX.Core.Engines.Combat.Helpers.Targets.Validation.Basic;
using AmeisenBotX.Core.Engines.Combat.Helpers.Targets.Validation.Special;
using AmeisenBotX.Core.Engines.Combat.Helpers.Targets.Validation.Util;
using AmeisenBotX.Wow.Objects;
using AmeisenBotX.Wow.Objects.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AmeisenBotX.Core.Engines.Combat.Helpers.Targets.Logics.Tank
{
    public class SimpleTankTargetSelectionLogic : BasicTargetSelectionLogic
    {
        public SimpleTankTargetSelectionLogic(AmeisenBotInterfaces bot) : base(bot)
        {
            TargetValidator.Validators.Add(new IsAttackableTargetValidator(bot));
            TargetValidator.Validators.Add(new IsThreatTargetValidator(bot));
            TargetValidator.Validators.Add(new DungeonTargetValidator(bot));
            TargetValidator.Validators.Add
            (
                new CachedTargetValidator(new IsReachableTargetValidator(bot), TimeSpan.FromSeconds(4))
            );

            // ListTargetPrioritizer not enabled as the tank needs to keep its focus on the boss,
            // need to test this TargetPrioritizer.Prioritizers.Add(new ListTargetPrioritizer());
            TargetPrioritizer.Prioritizers.Add(new DungeonTargetPrioritizer(bot));
        }

        public override bool SelectTarget(out IEnumerable<IWowUnit> possibleTargets)
        {
            possibleTargets = null;

            IEnumerable<IWowUnit> unitsAroundMe = Bot.Objects.All
                .OfType<IWowUnit>()
                .Where(e => TargetValidator.IsValid(e) && e.IsInCombat)
                .OrderByDescending(e => e.Type)
                .ThenByDescending(e => e.MaxHealth);

            IEnumerable<IWowUnit> targetsINeedToTank = unitsAroundMe
                .Where(e => e.Type != WowObjectType.Player
                         && e.TargetGuid != Bot.Wow.PlayerGuid
                         && Bot.Objects.PartymemberGuids.Contains(e.TargetGuid));

            if (targetsINeedToTank.Any())
            {
                possibleTargets = targetsINeedToTank;
                return true;
            }
            else
            {
                if (Bot.Objects.Partymembers.Any())
                {
                    Dictionary<IWowUnit, int> targets = new();

                    foreach (IWowUnit unit in Bot.Objects.Partymembers)
                    {
                        if (unit.TargetGuid > 0)
                        {
                            IWowUnit targetUnit = Bot.GetWowObjectByGuid<IWowUnit>(unit.TargetGuid);

                            if (targetUnit != null && Bot.Db.GetReaction(targetUnit, Bot.Player) != WowUnitReaction.Friendly)
                            {
                                if (!targets.ContainsKey(targetUnit))
                                {
                                    targets.Add(targetUnit, 1);
                                }
                                else
                                {
                                    ++targets[targetUnit];
                                }
                            }
                        }
                    }

                    possibleTargets = targets.OrderBy(e => e.Value).Select(e => e.Key);
                    return true;
                }

                possibleTargets = unitsAroundMe;
                return true;
            }
        }
    }
}