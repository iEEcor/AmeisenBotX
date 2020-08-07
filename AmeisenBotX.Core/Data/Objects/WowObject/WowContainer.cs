﻿using AmeisenBotX.Core.Data.Objects.WowObject.Structs;
using System;

namespace AmeisenBotX.Core.Data.Objects.WowObject
{
    [Serializable]
    public class WowContainer : WowObject
    {
        public WowContainer(IntPtr baseAddress, WowObjectType type, IntPtr descriptorAddress) : base(baseAddress, type, descriptorAddress)
        {
        }

        public int SlotCount { get; set; }

        public override string ToString()
        {
            return $"Container: [{Guid}] SlotCount: {SlotCount}";
        }

        public unsafe override void Update()
        {
            base.Update();

            fixed (RawWowContainer* objPtr = stackalloc RawWowContainer[1])
            {
                if (WowInterface.I.XMemory.ReadStruct(DescriptorAddress + RawWowObject.EndOffset, objPtr))
                {
                    SlotCount = objPtr[0].SlotCount;
                }
            }
        }
    }
}