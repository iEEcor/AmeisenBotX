﻿using AmeisenBotX.Common.Math;
using AmeisenBotX.Wow;
using AmeisenBotX.Wow.Objects;
using AmeisenBotX.Wow335a.Objects.Raw;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AmeisenBotX.Wow335a.Objects
{
    public class ObjectManager335a : ObjectManager<WowObject335a, WowUnit335a, WowPlayer335a, WowGameobject335a, WowDynobject335a, WowItem335a, WowCorpse335a, WowContainer335a>
    {
        public ObjectManager335a(WowMemoryApi memory)
            : base(memory)
        {
        }

        protected override void ReadParty()
        {
            PartyleaderGuid = ReadLeaderGuid();

            if (PartyleaderGuid > 0)
            {
                PartymemberGuids = ReadPartymemberGuids();
                Partymembers = wowObjects.OfType<IWowUnit>().Where(e => PartymemberGuids.Contains(e.Guid));

                Vector3 pos = new();

                foreach (Vector3 vec in Partymembers.Select(e => e.Position))
                {
                    pos += vec;
                }

                CenterPartyPosition = pos / Partymembers.Count();

                PartyPetGuids = PartyPets.Select(e => e.Guid);
                PartyPets = wowObjects.OfType<IWowUnit>().Where(e => PartymemberGuids.Contains(e.SummonedByGuid));
            }
        }

        private ulong ReadLeaderGuid()
        {
            if (Memory.Read(Memory.Offsets.RaidLeader, out ulong partyleaderGuid))
            {
                if (partyleaderGuid == 0
                    && Memory.Read(Memory.Offsets.PartyLeader, out partyleaderGuid))
                {
                    return partyleaderGuid;
                }

                return partyleaderGuid;
            }

            return 0;
        }

        private IEnumerable<ulong> ReadPartymemberGuids()
        {
            List<ulong> partymemberGuids = new();

            if (Memory.Read(Memory.Offsets.PartyLeader, out ulong partyLeader)
                && partyLeader != 0
                && Memory.Read(Memory.Offsets.PartyPlayerGuids, out RawPartyGuids partyMembers))
            {
                partymemberGuids.AddRange(partyMembers.AsArray());
            }

            if (Memory.Read(Memory.Offsets.RaidLeader, out ulong raidLeader)
                && raidLeader != 0
                && Memory.Read(Memory.Offsets.RaidGroupStart, out RawRaidStruct raidStruct))
            {
                foreach (IntPtr raidPointer in raidStruct.GetPointers())
                {
                    if (Memory.Read(raidPointer, out ulong guid))
                    {
                        partymemberGuids.Add(guid);
                    }
                }
            }

            return partymemberGuids.Where(e => e != 0 && e != PlayerGuid).Distinct();
        }
    }
}