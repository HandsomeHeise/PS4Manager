using System.Collections.Generic;

namespace PS4Manager.Core.Games.Bo3
{
    internal sealed class Bo3Probe
    {
        internal TrainerIdentity Identity;
        internal ulong Entity, Client, CurrentWeapon;
        internal uint Flags;
        internal int Health, Clip, Reserve;
        internal string PlayerName;
        internal ulong[] Inventory;
        internal byte[] InventoryBytes, AmmoBytes;
        internal Dictionary<ulong, Bo3Weapon> Weapons;
    }
    internal sealed class Bo3Weapon
    {
        internal ulong Id, Header, Definition, NameAddress;
        internal string Name;
    }
}
