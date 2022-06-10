﻿using AmeisenBotX.BehaviorTree;
using AmeisenBotX.BehaviorTree.Enums;
using AmeisenBotX.BehaviorTree.Objects;
using AmeisenBotX.Common.Math;
using AmeisenBotX.Common.Utils;
using AmeisenBotX.Core.Engines.Movement;
using AmeisenBotX.Core.Engines.Movement.Enums;
using AmeisenBotX.Core.Engines.Movement.Providers.Basic;
using AmeisenBotX.Core.Engines.Movement.Providers.Special;
using AmeisenBotX.Core.Logic.Enums;
using AmeisenBotX.Core.Logic.Routines;
using AmeisenBotX.Core.Logic.StaticDeathRoutes;
using AmeisenBotX.Core.Managers.Character.Inventory.Objects;
using AmeisenBotX.Core.Objects;
using AmeisenBotX.Core.Objects.Enums;
using AmeisenBotX.Logging;
using AmeisenBotX.Logging.Enums;
using AmeisenBotX.Memory.Win32;
using AmeisenBotX.Wow.Objects;
using AmeisenBotX.Wow.Objects.Enums;
using AmeisenBotX.Wow.Shared.Lua;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace AmeisenBotX.Core.Logic
{
    public class AmeisenBotLogic : IAmeisenBotLogic
    {
        private readonly List<IStaticDeathRoute> StaticDeathRoutes = new()
        {
            new ForgeOfSoulsDeathRoute(),
            new PitOfSaronDeathRoute()
        };

        public AmeisenBotLogic(AmeisenBotConfig config, AmeisenBotInterfaces bot)
        {
            Config = config;
            Bot = bot;

            FirstStart = true;
            FirstLogin = true;
            Random = new();

            Mode = BotMode.None;

            AntiAfkEvent = new(TimeSpan.FromMilliseconds(1200));
            CharacterUpdateEvent = new(TimeSpan.FromMilliseconds(5000));
            EatBlockEvent = new(TimeSpan.FromMilliseconds(30000));
            EatEvent = new(TimeSpan.FromMilliseconds(250));
            IdleActionEvent = new(TimeSpan.FromMilliseconds(1000));
            LoginAttemptEvent = new(TimeSpan.FromMilliseconds(500));
            LootTryEvent = new(TimeSpan.FromMilliseconds(750));
            NpcInteractionEvent = new(TimeSpan.FromMilliseconds(1000));
            PartymembersFightEvent = new(TimeSpan.FromMilliseconds(1000));
            RenderSwitchEvent = new(TimeSpan.FromMilliseconds(1000));
            UpdateFood = new(TimeSpan.FromMilliseconds(1000));
            UnitsLootedCleanupEvent = new(TimeSpan.FromMilliseconds(1000));

            UnitsLooted = new();
            UnitsToLoot = new();

            MovementManager = new
            (
                new List<IMovementProvider>()
                {
                    new DungeonMovementProvider(bot),
                    new SimpleCombatMovementProvider(bot),
                    new FollowMovementProvider(bot, config),
                }
            );

            // OPEN WORLD -----------------------------

            INode openworldGhostNode = new Selector
            (
                () => CanUseStaticPaths(),
                // prefer static paths
                new Leaf(() => { Bot.Movement.DirectMove(StaticRoute.GetNextPoint(Bot.Player.Position)); return BtStatus.Success; }),
                // run to corpse by position
                new Leaf(RunToCorpseAndRetrieveIt)
            );

            INode combatNode = new Selector
            (
                () => Bot.CombatClass == null,
                // start autoattacking if we have no combat class loaded
                new Selector
                (
                    () => Bot.Target == null,
                    new Leaf(() => { Bot.Wow.StartAutoAttack(); return BtStatus.Success; }),
                    new Selector
                    (
                        () => !Bot.Player.IsInMeleeRange(Bot.Target),
                        new Leaf(() => MoveToPosition(Bot.Target.Position)),
                        new Selector
                        (
                            () => !BotMath.IsFacing(Bot.Player.Position, Bot.Player.Rotation, Bot.Target.Position),
                            new Leaf(() => { Bot.Wow.FacePosition(Bot.Player.BaseAddress, Bot.Player.Position, Bot.Target.Position); return BtStatus.Success; }),
                            new Selector
                            (
                                () => !Bot.Player.IsAutoAttacking,
                                new Leaf(() => { Bot.Wow.StartAutoAttack(); /*Bot.Wow.StopClickToMove();*/ return BtStatus.Success; }),
                                new Leaf(() => { return BtStatus.Success; })
                            )
                        )
                    )
                ),
                // TODO: handle tactics here run combat class logic
                new Leaf(() => { Bot.CombatClass.Execute(); return BtStatus.Success; })
            );

            INode jobsNode = new Waterfall
            (
                new Leaf(() => { Bot.Jobs.Execute(); return BtStatus.Success; }),
                (() => Bot.Player.IsDead, new Leaf(Dead)),
                (() => Bot.Player.IsGhost, openworldGhostNode),
                (() => !Bot.Player.IsMounted && NeedToFight(), combatNode),
                (NeedToRepairOrSell, new Leaf(SpeakWithMerchant)),
                // (NeedToLoot, new Leaf(LootNearUnits)),
                (NeedToEat, new Leaf(Eat))
            );

            INode grindingNode = new Waterfall
            (
                new Leaf(() => { Bot.Grinding.Execute(); return BtStatus.Success; }),
                (() => Bot.Player.IsDead, new Leaf(Dead)),
                (() => Bot.Player.IsGhost, openworldGhostNode),
                (NeedToFight, combatNode),
                (NeedToRepairOrSell, new Leaf(SpeakWithMerchant)),
                (NeedToTrainSpells, new Leaf(SpeakWithClassTrainer)),
                (NeedToTrainSecondarySkills, new Leaf(SpeakWithProfessionTrainer)),
                (NeedToLoot, new Leaf(LootNearUnits)),
                (NeedToEat, new Leaf(Eat))
            );

            INode questingNode = new Waterfall
            (
                new Leaf(() => { Bot.Quest.Execute(); return BtStatus.Success; }),
                (() => Bot.Player.IsDead, new Leaf(Dead)),
                (() => Bot.Player.IsGhost, openworldGhostNode),
                (NeedToFight, combatNode),
                (NeedToRepairOrSell, new Leaf(SpeakWithMerchant)),
                (NeedToLoot, new Leaf(LootNearUnits)),
                (NeedToEat, new Leaf(Eat))
            );

            INode pvpNode = new Waterfall
            (
                new Leaf(() => { Bot.Pvp.Execute(); return BtStatus.Success; }),
                (() => Bot.Player.IsDead, new Leaf(Dead)),
                (() => Bot.Player.IsGhost, openworldGhostNode),
                (NeedToFight, combatNode),
                (NeedToRepairOrSell, new Leaf(SpeakWithMerchant)),
                (NeedToLoot, new Leaf(LootNearUnits)),
                (NeedToEat, new Leaf(Eat))
            );

            INode testingNode = new Waterfall
            (
                new Leaf(() => { Bot.Test.Execute(); return BtStatus.Success; }),
                (() => Bot.Player.IsDead, new Leaf(Dead)),
                (() => Bot.Player.IsGhost, openworldGhostNode)
            );

            INode openworldNode = new Waterfall
            (
                // do idle stuff as fallback
                new Leaf(Idle),
                // handle main open world states
                (() => Bot.Player.IsDead, new Leaf(Dead)),
                (() => Bot.Player.IsGhost, openworldGhostNode),
                (NeedToFight, combatNode),
                (NeedToRepairOrSell, new Leaf(SpeakWithMerchant)),
                (NeedToLoot, new Leaf(LootNearUnits)),
                (NeedToEat, new Leaf(Eat)),
                (() => Config.IdleActions && IdleActionEvent.Run(), new Leaf(() => { Bot.IdleActions.Tick(Config.Autopilot); return BtStatus.Success; }))
            );

            // SPECIAL ENVIRONMENTS -----------------------------

            INode battlegroundNode = new Waterfall
            (
                new Leaf(() => { Bot.Battleground.Execute(); return BtStatus.Success; }),
                // leave battleground once it is finished
                (IsBattlegroundFinished, new Leaf(() => { Bot.Wow.LeaveBattleground(); Bot.Battleground.Reset(); return BtStatus.Success; })),
                // only handle dead state here, ghost should only be a problem on AV as the
                // graveyard might get lost while we are a ghost
                (() => Bot.Player.IsDead, new Leaf(Dead)),
                (NeedToFight, combatNode),
                (NeedToEat, new Leaf(Eat))
            );

            INode dungeonNode = new Waterfall
            (
                new Selector
                (
                    () => Config.DungeonUsePartyMode,
                    // just follow when we use party mode in dungeon
                    openworldNode,
                    new Leaf(() => { Bot.Dungeon.Execute(); return BtStatus.Success; })
                ),
                (() => Bot.Player.IsDead, new Leaf(DeadDungeon)),
                (
                    NeedToFight,
                    new Selector
                    (
                        NeedToFollowTactic,
                        new Leaf(() => { return BtStatus.Success; }),
                        combatNode
                    )
                ),
                (NeedToLoot, new Leaf(LootNearUnits)),
                (NeedToEat, new Leaf(Eat))
            );

            INode raidNode = new Waterfall
            (
                new Selector
                (
                    () => Config.DungeonUsePartyMode,
                    // just follow when we use party mode in raid
                    new Leaf(Move),
                    new Leaf(() => { Bot.Dungeon.Execute(); return BtStatus.Success; })
                ),
                (
                    NeedToFight,
                    new Selector
                    (
                        NeedToFollowTactic,
                        new Leaf(() => { return BtStatus.Success; }),
                        combatNode
                    )
                ),
                (NeedToLoot, new Leaf(LootNearUnits)),
                (NeedToEat, new Leaf(Eat))
            );

            // GENERIC -----------------------------

            INode mainLogicNode = new Annotator
            (
                // run the update stuff before we execute the main logic objects will be updated
                // here for example
                new Leaf(UpdateWowInterface),
                new Selector
                (
                    () => Bot.Objects.IsWorldLoaded && Bot.Player != null && Bot.Objects != null,
                    new Annotator
                    (
                        // update stuff that needs us to be ingame
                        new Leaf(UpdateIngame),
                        new Waterfall
                        (
                            // open world auto behavior as fallback
                            openworldNode,
                            // handle movement
                            (MovementManager.NeedToMove, new Leaf(Move)),
                            // handle special environments
                            (() => Bot.Objects.MapId.IsBattlegroundMap(), battlegroundNode),
                            (() => Bot.Objects.MapId.IsDungeonMap(), dungeonNode),
                            (() => Bot.Objects.MapId.IsRaidMap(), raidNode),
                            // handle open world modes
                            (() => Mode == BotMode.Grinding, grindingNode),
                            (() => Mode == BotMode.Jobs, jobsNode),
                            (() => Mode == BotMode.Questing, questingNode),
                            (() => Mode == BotMode.PvP, pvpNode),
                            (() => Mode == BotMode.Testing, testingNode)
                        )
                    ),
                    // we are most likely in the loading screen or player/objects are null
                    new Leaf(() =>
                    {
                        // make sure we dont run after we leave the loadingscreen
                        Bot.Movement.StopMovement();
                        return BtStatus.Success;
                    })
                )
            );

            Tree = new
            (
                new Waterfall
                (
                    // run the anti afk and main logic if wow is running and we are logged in
                    new Annotator
                    (
                        new Leaf(AntiAfk),
                        mainLogicNode
                    ),
                    // accept tos and eula, start wow
                    (
                        () => Bot.Memory.Process == null || Bot.Memory.Process.HasExited,
                        new Sequence
                        (
                            new Leaf(CheckTosAndEula),
                            new Leaf(ChangeRealmlist),
                            new Leaf(StartWow)
                        )
                    ),
                    // setup interface and login
                    (() => !Bot.Wow.IsReady, new Leaf(SetupWowInterface)),
                    (NeedToLogin, new Leaf(Login))
                )
            );
        }

        public event Action OnWoWStarted;

        public BotMode Mode { get; private set; }

        private TimegatedEvent AntiAfkEvent { get; }

        private bool ArePartymembersInFight { get; set; }

        private AmeisenBotInterfaces Bot { get; }

        private TimegatedEvent CharacterUpdateEvent { get; }

        private IWowUnit ClassTrainer { get; set; }

        private AmeisenBotConfig Config { get; }

        private DateTime DungeonDiedTimestamp { get; set; }

        private TimegatedEvent EatBlockEvent { get; }

        private TimegatedEvent EatEvent { get; }

        private bool FirstLogin { get; set; }

        private bool FirstStart { get; set; }

        private Vector3 FollowOffset { get; set; }

        private IEnumerable<IWowInventoryItem> Food { get; set; }

        private TimegatedEvent IdleActionEvent { get; }

        private DateTime IngameSince { get; set; }

        private TimegatedEvent LoginAttemptEvent { get; }

        private int LootTry { get; set; }

        private TimegatedEvent LootTryEvent { get; }

        private IWowUnit Merchant { get; set; }

        private MovementManager MovementManager { get; }

        private TimegatedEvent NpcInteractionEvent { get; }

        private TimegatedEvent PartymembersFightEvent { get; }

        private IWowUnit PlayerToFollow { get; set; }

        private IWowUnit ProfessionTrainer { get; set; }

        private Random Random { get; }

        private TimegatedEvent RenderSwitchEvent { get; }

        private bool SearchedStaticRoutes { get; set; }

        private IStaticDeathRoute StaticRoute { get; set; }

        private Tree Tree { get; }

        private List<ulong> UnitsLooted { get; }

        private TimegatedEvent UnitsLootedCleanupEvent { get; }

        private Queue<ulong> UnitsToLoot { get; }

        private TimegatedEvent UpdateFood { get; }

        public static NpcSubType DecideClassTrainer(WowClass wowClass)
        {
            return wowClass switch
            {
                WowClass.Warrior => NpcSubType.WarriorTrainer,
                WowClass.Paladin => NpcSubType.PaladinTrainer,
                WowClass.Hunter => NpcSubType.HunterTrainer,
                WowClass.Rogue => NpcSubType.RougeTrainer,
                WowClass.Priest => NpcSubType.PriestTrainer,
                WowClass.Deathknight => NpcSubType.DeathKnightTrainer,
                WowClass.Shaman => NpcSubType.ShamanTrainer,
                WowClass.Mage => NpcSubType.MageTrainer,
                WowClass.Warlock => NpcSubType.WarlockTrainer,
                WowClass.Druid => NpcSubType.DruidTrainer,
            };
        }

        public void ChangeMode(BotMode mode)
        {
            Mode = mode;

            switch (Mode)
            {
                case BotMode.Questing:
                    Bot.Quest.Enter();
                    break;

                default:
                    break;
            }
        }

        public void Tick()
        {
            Tree.Tick();
        }

        private BtStatus AntiAfk()
        {
            if (AntiAfkEvent.Run())
            {
                Bot.Memory.Write(Bot.Memory.Offsets.TickCount, Environment.TickCount);
                AntiAfkEvent.Timegate = TimeSpan.FromMilliseconds(Random.Next(300, 2300));
            }

            return BtStatus.Success;
        }

        /// <summary>
        /// This method searches for static death routes, this is needed when pathfinding cannot
        /// find a good route from the graveyard to th dungeon entry. For example the ICC dungeons
        /// are only reachable by flying, its easier to use static routes.
        /// </summary>
        /// <returns>True when a static path can be used, false if not</returns>
        private bool CanUseStaticPaths()
        {
            if (!SearchedStaticRoutes)
            {
                if (Bot.Memory.Read(Bot.Memory.Offsets.CorpsePosition, out Vector3 corpsePosition))
                {
                    SearchedStaticRoutes = true;

                    Vector3 endPosition = Bot.Dungeon.Profile != null ? Bot.Dungeon.Profile.WorldEntry : corpsePosition;
                    IStaticDeathRoute staticRoute = StaticDeathRoutes.FirstOrDefault(e => e.IsUseable(Bot.Objects.MapId, Bot.Player.Position, endPosition));

                    if (staticRoute != null)
                    {
                        StaticRoute = staticRoute;
                        StaticRoute.Init(Bot.Player.Position);
                    }
                    else
                    {
                        staticRoute = StaticDeathRoutes.FirstOrDefault(e => e.IsUseable(Bot.Objects.MapId, Bot.Player.Position, corpsePosition));

                        if (staticRoute != null)
                        {
                            StaticRoute = staticRoute;
                            StaticRoute.Init(Bot.Player.Position);
                        }
                    }
                }
            }

            return StaticRoute != null;
        }

        private BtStatus ChangeRealmlist()
        {
            if (!Config.AutoChangeRealmlist)
            {
                return BtStatus.Success;
            }

            try
            {
                AmeisenLogger.I.Log("StartWow", "Changing Realmlist");
                string configWtfPath = Path.Combine(Directory.GetParent(Config.PathToWowExe).FullName, "wtf", "config.wtf");

                if (File.Exists(configWtfPath))
                {
                    bool editedFile = false;
                    List<string> content = File.ReadAllLines(configWtfPath).ToList();

                    if (!content.Any(e => e.Contains($"SET REALMLIST {Config.Realmlist}", StringComparison.OrdinalIgnoreCase)))
                    {
                        bool found = false;

                        for (int i = 0; i < content.Count; ++i)
                        {
                            if (content[i].Contains("SET REALMLIST", StringComparison.OrdinalIgnoreCase))
                            {
                                editedFile = true;
                                content[i] = $"SET REALMLIST {Config.Realmlist}";
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            editedFile = true;
                            content.Add($"SET REALMLIST {Config.Realmlist}");
                        }
                    }

                    if (editedFile)
                    {
                        File.SetAttributes(configWtfPath, FileAttributes.Normal);
                        File.WriteAllLines(configWtfPath, content);
                        File.SetAttributes(configWtfPath, FileAttributes.ReadOnly);
                    }
                }

                return BtStatus.Success;
            }
            catch
            {
                AmeisenLogger.I.Log("StartWow", "Cannot write realmlist to config.wtf");
            }

            return BtStatus.Failed;
        }

        private BtStatus CheckTosAndEula()
        {
            try
            {
                string configWtfPath = Path.Combine(Directory.GetParent(Config.PathToWowExe).FullName, "wtf", "config.wtf");

                if (File.Exists(configWtfPath))
                {
                    bool editedFile = false;
                    string content = File.ReadAllText(configWtfPath);

                    if (!content.Contains("SET READEULA \"0\"", StringComparison.OrdinalIgnoreCase))
                    {
                        editedFile = true;

                        if (content.Contains("SET READEULA", StringComparison.OrdinalIgnoreCase))
                        {
                            content = content.Replace("SET READEULA \"0\"", "SET READEULA \"1\"", StringComparison.OrdinalIgnoreCase);
                        }
                        else
                        {
                            content += "\nSET READEULA \"1\"";
                        }
                    }

                    if (!content.Contains("SET READTOS \"0\"", StringComparison.OrdinalIgnoreCase))
                    {
                        editedFile = true;

                        if (content.Contains("SET READTOS", StringComparison.OrdinalIgnoreCase))
                        {
                            content = content.Replace("SET READTOS \"0\"", "SET READTOS \"1\"", StringComparison.OrdinalIgnoreCase);
                        }
                        else
                        {
                            content += "\nSET READTOS \"1\"";
                        }
                    }

                    if (!content.Contains("SET MOVIE \"0\"", StringComparison.OrdinalIgnoreCase))
                    {
                        editedFile = true;

                        if (content.Contains("SET MOVIE", StringComparison.OrdinalIgnoreCase))
                        {
                            content = content.Replace("SET MOVIE \"0\"", "SET MOVIE \"1\"", StringComparison.OrdinalIgnoreCase);
                        }
                        else
                        {
                            content += "\nSET MOVIE \"1\"";
                        }
                    }

                    if (editedFile)
                    {
                        File.SetAttributes(configWtfPath, FileAttributes.Normal);
                        File.WriteAllText(configWtfPath, content);
                        File.SetAttributes(configWtfPath, FileAttributes.ReadOnly);
                    }
                }

                return BtStatus.Success;
            }
            catch
            {
                AmeisenLogger.I.Log("StartWow", "Cannot write to config.wtf");
            }

            return BtStatus.Failed;
        }

        private BtStatus Dead()
        {
            SearchedStaticRoutes = false;

            if (Config.ReleaseSpirit || Bot.Objects.MapId.IsBattlegroundMap())
            {
                Bot.Wow.RepopMe();
                return BtStatus.Success;
            }

            return BtStatus.Ongoing;
        }

        private BtStatus DeadDungeon()
        {
            if (!ArePartymembersInFight)
            {
                if (DungeonDiedTimestamp == default)
                {
                    DungeonDiedTimestamp = DateTime.UtcNow;
                }
                else if (DateTime.UtcNow - DungeonDiedTimestamp > TimeSpan.FromSeconds(30))
                {
                    Bot.Wow.RepopMe();
                    SearchedStaticRoutes = false;
                    return BtStatus.Success;
                }
            }

            if ((!ArePartymembersInFight && DateTime.UtcNow - DungeonDiedTimestamp > TimeSpan.FromSeconds(30))
                || Bot.Objects.Partymembers.Any(e => !e.IsDead
                    && (e.Class == WowClass.Paladin || e.Class == WowClass.Druid || e.Class == WowClass.Priest || e.Class == WowClass.Shaman)))
            {
                // if we died 30s ago or no one that can ress us is alive
                Bot.Wow.RepopMe();
                SearchedStaticRoutes = false;
                return BtStatus.Success;
            }

            return BtStatus.Ongoing;
        }

        private BtStatus Eat()
        {
            if (EatEvent.Run())
            {
                bool needToEat = Bot.Player.HealthPercentage < Config.EatUntilPercent;
                bool needToDrink = Bot.Player.ManaPercentage < Config.DrinkUntilPercent;

                bool isEating = Bot.Player.Auras.Any(e => Bot.Db.GetSpellName(e.SpellId) == "Food");
                bool isDrinking = Bot.Player.Auras.Any(e => Bot.Db.GetSpellName(e.SpellId) == "Drink");

                if (isEating && isDrinking)
                {
                    return BtStatus.Ongoing;
                }

                IWowInventoryItem refreshment = Food.FirstOrDefault(e => Enum.IsDefined(typeof(WowRefreshment), e.Id));

                if (needToEat && needToDrink && refreshment != null)
                {
                    if (refreshment != null)
                    {
                        Bot.Wow.UseItemByName(refreshment.Name);
                        return BtStatus.Ongoing;
                    }
                }

                IWowInventoryItem food = Food.FirstOrDefault(e => Enum.IsDefined(typeof(WowFood), e.Id));

                if (!isEating && needToEat && (food != null || refreshment != null))
                {
                    // only use food if its not very lowlevel, otherwise try to use a refreshment
                    if (food != null && (refreshment == null || food.RequiredLevel >= Bot.Player.Level - 5))
                    {
                        Bot.Wow.UseItemByName(food.Name);
                        return BtStatus.Ongoing;
                    }

                    if (refreshment != null)
                    {
                        Bot.Wow.UseItemByName(refreshment.Name);
                        return BtStatus.Ongoing;
                    }
                }

                IWowInventoryItem water = Food.FirstOrDefault(e => Enum.IsDefined(typeof(WowWater), e.Id));

                if (!isDrinking && needToDrink && (water != null || refreshment != null))
                {
                    // only use water if its not very lowlevel, otherwise try to use a refreshment
                    if (water != null && (refreshment == null || water.RequiredLevel >= Bot.Player.Level - 5))
                    {
                        Bot.Wow.UseItemByName(water.Name);
                        return BtStatus.Ongoing;
                    }

                    if (refreshment != null)
                    {
                        Bot.Wow.UseItemByName(refreshment.Name);
                        return BtStatus.Ongoing;
                    }
                }
            }

            return BtStatus.Success;
        }

        private IEnumerable<IWowUnit> GetLootableUnits()
        {
            return Bot.Objects.All.OfType<IWowUnit>()
                .Where(e => e.IsLootable
                    && !UnitsLooted.Contains(e.Guid)
                    && e.Position.GetDistance(Bot.Player.Position) < Config.LootUnitsRadius);
        }

        private BtStatus Idle()
        {
            Bot.CombatClass?.OutOfCombatExecute();
            return BtStatus.Success;
        }

        private bool IsBattlegroundFinished()
        {
            return Bot.Memory.Read(Bot.Memory.Offsets.BattlegroundFinished, out int bgFinished)
                && bgFinished == 1;
        }

        private bool IsRepairNpcNear(out IWowUnit unit)
        {
            unit = Bot.Objects.All.OfType<IWowUnit>()
                    .FirstOrDefault(e => e.GetType() != typeof(IWowPlayer)
                                         && !e.IsDead
                                         && e.IsRepairer
                && Bot.Db.GetReaction(Bot.Player, e) != WowUnitReaction.Hostile
                && Bot.Player.DistanceTo(e) <= Config.RepairNpcSearchRadius);

            return unit != null;
        }

        private bool IsVendorNpcNear(out IWowUnit unit)
        {
            unit = Bot.Objects.All.OfType<IWowUnit>()
                .FirstOrDefault(e => e.GetType() != typeof(IWowPlayer)
                    && !e.IsDead
                    && e.IsVendor
                    && Bot.Db.GetReaction(Bot.Player, e) != WowUnitReaction.Hostile
                    && e.Position.GetDistance(Bot.Player.Position) < Config.RepairNpcSearchRadius);

            return unit != null;
        }

        private void LoadWowWindowPosition()
        {
            if (Config.SaveWowWindowPosition && !Config.AutoPositionWow)
            {
                if (Bot.Memory.Process.MainWindowHandle != IntPtr.Zero && Config.WowWindowRect != new Rect() { Left = -1, Top = -1, Right = -1, Bottom = -1 })
                {
                    Bot.Memory.SetWindowPosition(Bot.Memory.Process.MainWindowHandle, Config.WowWindowRect);
                    AmeisenLogger.I.Log("AmeisenBot", $"Loaded window position: {Config.WowWindowRect}", LogLevel.Verbose);
                }
                else
                {
                    AmeisenLogger.I.Log("AmeisenBot", $"Unable to load window position of {Bot.Memory.Process.MainWindowHandle} to {Config.WowWindowRect}", LogLevel.Warning);
                }
            }
        }

        private BtStatus Login()
        {
            Bot.Wow.SetWorldLoadedCheck(true);

            if (FirstLogin)
            {
                FirstLogin = true;
                SetCVars();
            }

            // needed to prevent direct logout due to inactivity
            AntiAfk();

            if (LoginAttemptEvent.Run())
            {
                Bot.Wow.LuaDoString(LuaLogin.Get(Config.Username, Config.Password, Config.Realm, Config.CharacterSlot));
            }

            Bot.Wow.SetWorldLoadedCheck(false);
            return BtStatus.Success;
        }

        private BtStatus LootNearUnits()
        {
            IWowUnit unit = Bot.GetWowObjectByGuid<IWowUnit>(UnitsToLoot.Peek());

            if (unit == null || !unit.IsLootable || LootTry > 2)
            {
                UnitsLooted.Add(UnitsToLoot.Dequeue());
                LootTry = 0;
                return BtStatus.Failed;
            }

            if (unit.Position != Vector3.Zero && Bot.Player.DistanceTo(unit) > 3.0f)
            {
                Bot.Movement.SetMovementAction(MovementAction.Move, unit.Position);
                return BtStatus.Ongoing;
            }
            else if (LootTryEvent.Run())
            {
                if (Bot.Memory.Read(Bot.Memory.Offsets.LootWindowOpen, out byte lootOpen)
                    && lootOpen > 0)
                {
                    Bot.Wow.LootEverything();

                    UnitsLooted.Add(UnitsToLoot.Dequeue());
                    LootTry = 0;

                    Bot.Wow.ClickUiElement("LootCloseButton");
                    return BtStatus.Success;
                }
                else
                {
                    Bot.Wow.StopClickToMove();
                    Bot.Wow.InteractWithUnit(unit);
                    ++LootTry;
                }
            }

            return BtStatus.Ongoing;
        }

        private BtStatus Move()
        {
            return MoveToPosition(MovementManager.Target);
        }

        private BtStatus MoveToPosition(Vector3 position, MovementAction movementAction = MovementAction.Move)
        {
            if (position != Vector3.Zero && Bot.Player.DistanceTo(position) > 3.0f)
            {
                Bot.Movement.SetMovementAction(movementAction, position);
                return BtStatus.Ongoing;
            }

            return BtStatus.Success;
        }

        private bool NeedToEat()
        {
            // is eating blocked, used to prevent shredding of food
            if (!EatBlockEvent.Ready)
            {
                return false;
            }

            // when we are in a group an they move too far away, abort eating and dont start eating
            // for 30s
            if (Config.EatDrinkAbortFollowParty && Bot.Objects.PartymemberGuids.Any() && Bot.Player.DistanceTo(Bot.Objects.CenterPartyPosition) > Config.EatDrinkAbortFollowPartyDistance)
            {
                EatBlockEvent.Run();
                return false;
            }

            bool isEating = Bot.Player.Auras.Any(e => Bot.Db.GetSpellName(e.SpellId) == "Food");
            bool isDrinking = Bot.Player.Auras.Any(e => Bot.Db.GetSpellName(e.SpellId) == "Drink");

            // still eating/drinking, wait until threshold is reached
            if ((isEating && Bot.Player.HealthPercentage < Config.EatUntilPercent)
                || (isDrinking && Bot.Player.MaxMana > 0 && Bot.Player.ManaPercentage < Config.DrinkUntilPercent))
            {
                return true;
            }

            if (UpdateFood.Run())
            {
                Food = Bot.Character.Inventory.Items
                    .Where(e => e.RequiredLevel <= Bot.Player.Level)
                    .OrderByDescending(e => e.ItemLevel);
            }

            return Bot.Player.HealthPercentage < Config.EatUntilPercent
                   && (Food.Any(e => Enum.IsDefined(typeof(WowFood), e.Id))
                       || Food.Any(e => Enum.IsDefined(typeof(WowRefreshment), e.Id)))
                || Bot.Player.MaxMana > 0 && Bot.Player.ManaPercentage < Config.DrinkUntilPercent
                   && (Food.Any(e => Enum.IsDefined(typeof(WowWater), e.Id))
                       || Food.Any(e => Enum.IsDefined(typeof(WowRefreshment), e.Id)));
        }

        private bool NeedToFight()
        {
            if (PartymembersFightEvent.Run())
            {
                ArePartymembersInFight = Bot.Objects.Partymembers.Any(e => e.IsInCombat && e.DistanceTo(Bot.Player) < Config.SupportRange)
                    || Bot.Objects.All.OfType<IWowUnit>().Any(e => e.IsInCombat
                        && (e.IsTaggedByMe || !e.IsTaggedByOther)
                        && (e.TargetGuid == Bot.Player.Guid || Bot.Objects.Partymembers.Any(x => x.Guid == e.TargetGuid))
                        && Bot.Wow.GetReaction(Bot.Player.BaseAddress, e.BaseAddress) == WowUnitReaction.Hostile);
            }

            return Bot.Player.IsInCombat
                || ArePartymembersInFight;
        }

        private bool NeedToFollowTactic()
        {
            return Bot.Tactic.Execute() && !Bot.Tactic.AllowAttacking;
        }

        private bool NeedToLogin()
        {
            return Bot.Memory.Read(Bot.Memory.Offsets.IsIngame, out int isIngame) && isIngame == 0;
        }

        private bool NeedToLoot()
        {
            if (UnitsLootedCleanupEvent.Run())
            {
                UnitsLooted.RemoveAll((guid) =>
                {
                    // remove unit from looted list when its gone or seen alive
                    IWowUnit unit = Bot.GetWowObjectByGuid<IWowUnit>(guid);
                    return unit != null && !unit.IsDead;
                });
            }

            foreach (IWowUnit unit in GetLootableUnits())
            {
                if (!UnitsLooted.Contains(unit.Guid) && !UnitsToLoot.Contains(unit.Guid))
                {
                    UnitsToLoot.Enqueue(unit.Guid);
                }
            }

            return UnitsToLoot.Count > 0;
        }

        private bool NeedToRepairOrSell()
        {
            bool needToRepair = Bot.Character.Equipment.Items.Any(e => e.Value.MaxDurability > 0 && e.Value.Durability / (double)e.Value.MaxDurability * 100.0 <= Config.ItemRepairThreshold);

            bool needToSell = Bot.Character.Inventory.FreeBagSlots < Config.BagSlotsToGoSell
                              && Bot.Character.Inventory.Items
                              .Any(e => e.Price > 0 && !Config.ItemSellBlacklist.Contains(e.Name)
                                      && ((Config.SellGrayItems && e.ItemQuality == (int)WowItemQuality.Poor)
                                      || (Config.SellWhiteItems && e.ItemQuality == (int)WowItemQuality.Common)
                                      || (Config.SellGreenItems && e.ItemQuality == (int)WowItemQuality.Uncommon)
                                      || (Config.SellBlueItems && e.ItemQuality == (int)WowItemQuality.Rare)
                                      || (Config.SellPurpleItems && e.ItemQuality == (int)WowItemQuality.Epic)));

            IWowUnit vendorRepair = null;
            IWowUnit vendorSell = null;

            if (Mode != BotMode.None && Bot.Grinding.Profile?.NpcsOfInterest == null)
            {
                return false;
            }

            switch (Mode)
            {
                case BotMode.Grinding:
                    {
                        Npc repairNpcEntry = Bot.Grinding.Profile.NpcsOfInterest.FirstOrDefault(e => e.Type == NpcType.VendorRepair);

                        if (repairNpcEntry != null)
                        {
                            vendorRepair = Bot.GetClosestVendorByEntryId(repairNpcEntry.EntryId);
                        }

                        Npc sellNpcEntry = Bot.Grinding.Profile.NpcsOfInterest.FirstOrDefault(e => e.Type is NpcType.VendorRepair or NpcType.VendorSellBuy);

                        if (sellNpcEntry != null)
                        {
                            vendorSell = Bot.GetClosestVendorByEntryId(sellNpcEntry.EntryId);
                        }

                        break;
                    }
                case BotMode.None:
                    IsRepairNpcNear(out IWowUnit repairNpc);
                    vendorRepair = repairNpc;

                    IsVendorNpcNear(out IWowUnit sellNpc);
                    vendorSell = sellNpc;
                    break;

                case BotMode.Questing:
                    break;

                case BotMode.PvP:
                    break;

                case BotMode.Testing:
                    break;

                case BotMode.Jobs:
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (needToRepair && vendorRepair != null)
            {
                Merchant = vendorRepair;
                return true;
            }
            if (needToSell && vendorSell != null)
            {
                Merchant = vendorSell;
                return true;
            }

            return false;
        }

        private bool NeedToTrainSecondarySkills()
        {
            IWowUnit professionTrainer = null;
            Npc profileTrainer = null;

            if (Bot.Grinding.Profile != null)
            {
                profileTrainer = Bot.Grinding.Profile.NpcsOfInterest?.FirstOrDefault(e =>
                    e.Type == NpcType.ProfessionTrainer);
            }

            if (profileTrainer != null)
            {
                professionTrainer = profileTrainer.SubType switch
                {
                    NpcSubType.FishingTrainer when !Bot.Character.Skills.ContainsKey("Fishing") => Bot
                        .GetClosestTrainerByEntryId(profileTrainer.EntryId),
                    NpcSubType.FirstAidTrainer when !Bot.Character.Skills.ContainsKey("First Aid") => Bot
                        .GetClosestTrainerByEntryId(profileTrainer.EntryId),
                    NpcSubType.CookingTrainer when !Bot.Character.Skills.ContainsKey("Cooking") => Bot
                        .GetClosestTrainerByEntryId(profileTrainer.EntryId),
                    _ => null
                };
            }

            if (professionTrainer == null)
            {
                return false;
            }

            ProfessionTrainer = professionTrainer;
            return ProfessionTrainer != null; // todo: Config.LearnSecondarySkills
        }

        private bool NeedToTrainSpells()
        {
            IWowUnit classTrainer = null;
            Npc profileTrainer = null;

            if (Bot.Grinding.Profile != null)
            {
                profileTrainer = Bot.Grinding.Profile.NpcsOfInterest?.FirstOrDefault(e =>
                    e.Type == NpcType.ClassTrainer && e.SubType == DecideClassTrainer(Bot.Player.Class));
            }

            if (profileTrainer != null)
            {
                classTrainer = Bot.GetClosestTrainerByEntryId(profileTrainer.EntryId);
            }

            if (classTrainer == null)
            {
                return false;
            }

            ClassTrainer = classTrainer;
            return Bot.Character.LastLevelTrained != 0 && Bot.Character.LastLevelTrained < Bot.Player.Level;
        }

        private BtStatus RunToCorpseAndRetrieveIt()
        {
            if (!Bot.Memory.Read(Bot.Memory.Offsets.CorpsePosition, out Vector3 corpsePosition))
            {
                return BtStatus.Failed;
            }

            if (Bot.Player.Position.GetDistance(corpsePosition) > Config.GhostResurrectThreshold)
            {
                Bot.Movement.SetMovementAction(MovementAction.Move, corpsePosition);
                return BtStatus.Ongoing;
            }

            Bot.Wow.RetrieveCorpse();
            return BtStatus.Success;
        }

        private void SetCVars()
        {
            List<(string, string)> cvars = new()
            {
                ("maxfps", $"{Config.MaxFps}"),
                ("maxfpsbk", $"{Config.MaxFps}"),
                ("AutoInteract", "1"),
                ("AutoLootDefault", "0"),
            };

            if (Config.AutoSetUlowGfxSettings)
            {
                cvars.AddRange(new (string, string)[]
                {
                    ("alphalevel", "1"),
                    ("anisotropic", "0"),
                    ("basemip", "1"),
                    ("bitdepth", "16"),
                    ("characterAmbient", "1"),
                    ("detaildensity", "1"),
                    ("detailDoodadAlpha", "0"),
                    ("doodadanim", "0"),
                    ("environmentDetail", "0.5"),
                    ("extshadowquality", "0"),
                    ("farclip", "177"),
                    ("ffx", "0"),
                    ("fog", "0"),
                    ("fullalpha", "0"),
                    ("groundeffectdensity", "16"),
                    ("groundeffectdist", "1"),
                    ("gxcolorbits", "16"),
                    ("gxdepthbits", "16"),
                    ("horizonfarclip", "1305"),
                    ("hwPCF", "1"),
                    ("light", "0"),
                    ("lod", "0"),
                    ("loddist", "50"),
                    ("m2Faster", "1"),
                    ("mapshadows", "0"),
                    ("maxlights", "0"),
                    ("maxlod", "0"),
                    ("overridefarclip ", "0"),
                    ("particledensity", "0.3"),
                    ("pixelshader", "0"),
                    ("shadowlevel", "1"),
                    ("shadowlod", "0"),
                    ("showfootprintparticles", "0"),
                    ("showfootprints", "0"),
                    ("showshadow", "0"),
                    ("showwater", "0"),
                    ("skyclouddensity", "0"),
                    ("skycloudlod", "0"),
                    ("skyshow", "0"),
                    ("skysunglare", "0"),
                    ("smallcull", "1"),
                    ("specular", "0"),
                    ("textureloddist", "80"),
                    ("timingmethod", "1"),
                    ("unitdrawdist", "20"),
                    ("waterlod", "0"),
                    ("watermaxlod", "0"),
                    ("waterparticulates", "0"),
                    ("waterripples", "0"),
                    ("waterspecular", "0"),
                    ("waterwaves", "0"),
                });
            }

            StringBuilder sb = new();

            foreach ((string cvar, string value) in cvars)
            {
                sb.Append($"pcall(SetCVar,\"{cvar}\",\"{value}\");");
            }

            Bot.Wow.LuaDoString(sb.ToString());
        }

        private BtStatus SetupWowInterface()
        {
            return Bot.Wow.Setup() ? BtStatus.Success : BtStatus.Failed;
        }

        private BtStatus SpeakWithClassTrainer()
        {
            if (ClassTrainer == null)
            {
                return BtStatus.Failed;
            }

            if (Bot.Player.Position.GetDistance(ClassTrainer.Position) > 3.0f)
            {
                Bot.Movement.SetMovementAction(MovementAction.Move, ClassTrainer.Position);
                return BtStatus.Ongoing;
            }

            Bot.Movement.StopMovement();

            if (!NpcInteractionEvent.Run())
            {
                return BtStatus.Failed;
            }

            SpeakToClassTrainerRoutine.Run(Bot, ClassTrainer);
            return BtStatus.Success;
        }

        private BtStatus SpeakWithMerchant()
        {
            if (Merchant == null)
            {
                return BtStatus.Failed;
            }

            if (Bot.Player.Position.GetDistance(Merchant.Position) > 3.0f)
            {
                Bot.Movement.SetMovementAction(MovementAction.Move, Merchant.Position);
                return BtStatus.Ongoing;
            }

            Bot.Movement.StopMovement();

            if (!NpcInteractionEvent.Run())
            {
                return BtStatus.Failed;
            }

            SpeakToMerchantRoutine.Run(Bot, Merchant);
            return BtStatus.Success;
        }

        private BtStatus SpeakWithProfessionTrainer()
        {
            if (ProfessionTrainer == null)
            {
                return BtStatus.Failed;
            }

            if (Bot.Player.Position.GetDistance(ProfessionTrainer.Position) > 3.0f)
            {
                Bot.Movement.SetMovementAction(MovementAction.Move, ProfessionTrainer.Position);
                return BtStatus.Ongoing;
            }

            Bot.Movement.StopMovement();

            if (!NpcInteractionEvent.Run())
            {
                return BtStatus.Failed;
            }

            SpeakToClassTrainerRoutine.Run(Bot, ProfessionTrainer);
            return BtStatus.Success;
        }

        private BtStatus StartWow()
        {
            if (File.Exists(Config.PathToWowExe))
            {
                AmeisenLogger.I.Log("StartWow", "Starting WoW Process");
                Process p = Bot.Memory.StartProcessNoActivate($"\"{Config.PathToWowExe}\" -windowed -d3d9", out IntPtr processHandle, out IntPtr mainThreadHandle);
                p.WaitForInputIdle();

                AmeisenLogger.I.Log("StartWow", $"Attaching XMemory to {p.ProcessName} ({p.Id})");

                if (Bot.Memory.Init(p, processHandle, mainThreadHandle))
                {
                    Bot.Memory.Offsets.Init(Bot.Memory.Process.MainModule.BaseAddress);

                    OnWoWStarted?.Invoke();

                    if (Config.SaveWowWindowPosition)
                    {
                        LoadWowWindowPosition();
                    }

                    return BtStatus.Success;
                }
                else
                {
                    AmeisenLogger.I.Log("StartWow", $"Attaching XMemory failed...");
                    p.Kill();
                    return BtStatus.Failed;
                }
            }

            return BtStatus.Failed;
        }

        private BtStatus UpdateIngame()
        {
            if (FirstStart)
            {
                FirstStart = false;
                IngameSince = DateTime.UtcNow;
            }

            if (Bot.Wow.Events != null)
            {
                if (!Bot.Wow.Events.IsActive && DateTime.UtcNow - IngameSince > TimeSpan.FromSeconds(2))
                {
                    // need to wait for the Frame setup
                    Bot.Wow.Events.Start();
                }

                Bot.Wow.Events.Tick();
            }

            Bot.Movement.Execute();

            if (CharacterUpdateEvent.Run())
            {
                Bot.Character.UpdateAll();
            }

            if (!Bot.Player.IsDead)
            {
                DungeonDiedTimestamp = default;
            }

            // auto disable rendering when not in focus
            if (Config.AutoDisableRender && RenderSwitchEvent.Run())
            {
                IntPtr foregroundWindow = Bot.Memory.GetForegroundWindow();
                Bot.Wow.SetRenderState(foregroundWindow == Bot.Memory.Process.MainWindowHandle);
            }

            return BtStatus.Success;
        }

        private BtStatus UpdateWowInterface()
        {
            Bot.Wow.Tick();
            return BtStatus.Success;
        }
    }
}