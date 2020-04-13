﻿using AmeisenBotX.Core.Data.Enums;
using AmeisenBotX.Core.Dungeon.Enums;
using AmeisenBotX.Core.Dungeon.Objects;
using AmeisenBotX.Pathfinding.Objects;
using System.Collections.Generic;

namespace AmeisenBotX.Core.Jobs.Profiles
{
    public interface IDungeonProfile
    {
        string Author { get; }

        string Description { get; }

        DungeonFactionType FactionType { get; }

        int GroupSize { get; }

        MapId MapId { get; }

        MapId WorldEntryMapId { get; }

        int MaxLevel { get; }

        string Name { get; }

        List<DungeonNode> Path { get; }

        int RequiredItemLevel { get; }

        int RequiredLevel { get; }

        Vector3 WorldEntry { get; }
    }
}