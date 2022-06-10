﻿using AmeisenBotX.Common.Math;
using AmeisenBotX.Wow.Objects;
using AmeisenBotX.Wow.Objects.Enums;
using AmeisenBotX.Wow335a.Objects.Descriptors;
using System;
using System.Collections.Specialized;
using System.Globalization;

namespace AmeisenBotX.Wow335a.Objects
{
    [Serializable]
    public class WowGameobject335a : WowObject335a, IWowGameobject
    {
        public byte Bytes0 { get; set; }

        public ulong CreatedBy { get; set; }

        public int DisplayId { get; set; }

        public int Faction { get; set; }

        public BitVector32 Flags { get; set; }

        public WowGameObjectType GameObjectType { get; set; }

        public int Level { get; set; }

        public override string ToString()
        {
            return $"GameObject: [{EntryId}] ({(Enum.IsDefined(typeof(WowGameObjectDisplayId), DisplayId) ? ((WowGameObjectDisplayId)DisplayId).ToString() : DisplayId.ToString(CultureInfo.InvariantCulture))}:{DisplayId})";
        }

        public override void Update()
        {
            base.Update();

            if (Memory.Read(DescriptorAddress + WowObjectDescriptor335a.EndOffset, out WowGameobjectDescriptor335a objPtr)
                && Memory.Read(IntPtr.Add(BaseAddress, (int)Memory.Offsets.WowGameobjectPosition), out Vector3 position))
            {
                GameObjectType = (WowGameObjectType)objPtr.GameobjectBytes1;
                CreatedBy = objPtr.CreatedBy;
                Bytes0 = objPtr.GameobjectBytes0;
                DisplayId = objPtr.DisplayId;
                Faction = objPtr.Faction;
                Flags = new(objPtr.Flags);
                Level = objPtr.Level;
                Position = position;
            }
        }
    }
}