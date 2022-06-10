﻿using AmeisenBotX.Wow.Objects;
using AmeisenBotX.Wow.Objects.Flags;
using System.Runtime.InteropServices;

namespace AmeisenBotX.Wow335a.Objects.Raw
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct WowAura548 : IWowAura
    {
        public fixed int Pad0[7];

        public byte Flags { get; set; }

        public byte StackCount { get; set; }

        public byte Unknown { get; set; }

        public byte Level { get; set; }

        public ulong Creator { get; set; }

        public int SpellId { get; set; }

        public fixed int Pad1[5];

        public bool IsActive => ((WowAuraFlag)Flags).HasFlag(WowAuraFlag.Active);

        public bool IsHarmful => ((WowAuraFlag)Flags).HasFlag(WowAuraFlag.Harmful);

        public bool IsPassive => ((WowAuraFlag)Flags).HasFlag(WowAuraFlag.Passive);

        public override string ToString()
        {
            return $"{SpellId} (lvl. {Level}) x{StackCount} [CG: {Creator}], Harmful: {IsHarmful}, Passive: {IsPassive}";
        }
    }
}