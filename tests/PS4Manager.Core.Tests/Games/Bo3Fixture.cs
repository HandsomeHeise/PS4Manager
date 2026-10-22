using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using PS4Manager.Core.Frame4;
using PS4Manager.Core.Games;
using PS4Manager.Core.Games.Bo3;

namespace PS4Manager.Core.Tests.Games
{
    internal sealed class Bo3Fixture
    {
        internal readonly FakeTrainerTransport Transport = new FakeTrainerTransport();
        internal ulong Base, Entity;
        internal ulong Client = 0x2000100000;
        internal bool Current = true;
        internal TrainerTarget Target;
        internal Bo3Trainer Trainer;
        internal string FailSymbol;
        internal bool LoseAfterEffect;
        internal int ClipCapacity = 10, ReserveCapacity = 60;
        internal Action<FakeTrainerTransport.RpcCall> BeforeCall;
        internal void EnableRpc()
        {
            Transport.OnCall = call => {
                BeforeCall?.Invoke(call);
                if (call.Symbol == FailSymbol && !LoseAfterEffect) throw new TrainerDispatchException("test lost response", null);
                ulong weapon = call.Symbol == "BO3_GivePlayerWeaponInternal" ? call.Arguments[2] :
                    call.Symbol == "BO3_GetWeaponClipCapacity" || call.Symbol == "BO3_GetWeaponAlternate" || call.Symbol == "BO3_GetWeaponDualWieldPartner" || call.Symbol == "BO3_IsWeaponDualWield" ? call.Arguments[0] : call.Arguments[1];
                int slot = Enumerable.Range(0, 15).FirstOrDefault(i => Transport.ReadUInt32(Client + 0x378 + (uint)i * 48) == weapon);
                ulong clipAddress = Client + 0x684 + (uint)slot * 4, reserveAddress = Client + 0x648 + (uint)slot * 4;
                int clip = (int)Transport.ReadUInt32(clipAddress), reserve = (int)Transport.ReadUInt32(reserveAddress);
                ulong value;
                switch (call.Symbol) {
                    case "BO3_GetWeaponClipCapacity": value = (ulong)ClipCapacity; break;
                    case "BO3_GetWeaponReserveCapacity": value = (ulong)ReserveCapacity; break;
                    case "BO3_GetWeaponAmmoHeadroom": value = (ulong)(ReserveCapacity - reserve); break;
                    case "Trainer_GetWeaponClipAmmo": value = (ulong)clip; break;
                    case "Trainer_GetWeaponReserveAmmo": value = (ulong)reserve; break;
                    case "BO3_StartingAmmoDeficit": value = 50; break;
                    case "BO3_GetWeaponAlternate": case "BO3_GetWeaponDualWieldPartner": value = 0; break;
                    case "BO3_IsWeaponDualWield": value = 0xF00D000000000000; break;
                    case "BO3_GivePlayerWeaponInternal":
                        slot = Enumerable.Range(0, 15).First(i => Transport.ReadUInt32(Client + 0x378 + (uint)i * 48) == 0);
                        Transport.Put64(Client + 0x378 + (uint)slot * 48, weapon); value = 0xF00D000000000001; break;
                    case "Trainer_TransferReserveToClip":
                        int moved = Math.Min(ClipCapacity - clip, reserve);
                        Transport.Put32(clipAddress, (uint)(clip + moved)); Transport.Put32(reserveAddress, (uint)(reserve - moved)); value = clipAddress; break;
                    case "BO3_AddWeaponAmmo":
                        int total = clip + reserve; reserve += (int)call.Arguments[2]; int transfer = Math.Min(ClipCapacity - clip, reserve);
                        clip += transfer; reserve = Math.Min(ReserveCapacity, reserve - transfer);
                        Transport.Put32(clipAddress, (uint)clip); Transport.Put32(reserveAddress, (uint)reserve); value = (ulong)(clip + reserve - total); break;
                    default: throw new InvalidOperationException("Unhandled fixture RPC " + call.Symbol);
                }
                if (call.Symbol == FailSymbol) throw new TrainerDispatchException("test response lost after effect", null);
                return value;
            };
        }
        internal void SetAmmo(int clip, int reserve)
        { Transport.Put32(Client + 0x684, (uint)clip); Transport.Put32(Client + 0x648, (uint)reserve); }
        internal readonly Dictionary<string, ulong> Weapons = new Dictionary<string, ulong> { { "pistol_revolver38_zm", 7 }, { "pistol_burst_zm", 9 }, { "rifle_zm", 11 } };
        internal static Bo3Fixture Create(ulong moduleBase = 0x400000)
        {
            var f = new Bo3Fixture { Base = moduleBase, Entity = moduleBase + 0x66EAFE0 };
            f.Target = new TrainerTarget(1, new Frame4Endpoint("console"), () => f.Current, CancellationToken.None);
            f.Transport.Modules = new[] { new Frame4PrxModule(0, "eboot.bin", moduleBase, 0x1520000, moduleBase + 0x1520000, 0xE000000) };
            f.Transport.Put64(f.Entity + 0x250, f.Client); f.PutHealth(100); f.PutFlags(4);
            f.Transport.Put(f.Client + 0x16C44, new byte[128]); f.Transport.Put(f.Client + 0x16C44, Encoding.UTF8.GetBytes("Local Player\0"));
            f.Transport.Put32(moduleBase + 0xE978834, 11);
            f.Transport.Put(moduleBase + 0xE978840, new byte[12 * 8]);
            foreach (var w in f.Weapons) {
                ulong header = f.HeaderAddress(w.Value), def = f.DefAddress(w.Value);
                f.Transport.Put64(moduleBase + 0xE978840 + w.Value * 8, header);
                f.Transport.Put(header, new byte[512]); f.Transport.Put64(header, f.NameAddress(w.Value)); f.Transport.Put64(header + 0x18, def);
                f.Transport.Put(f.NameAddress(w.Value), new byte[128]); f.Transport.Put(f.NameAddress(w.Value), Encoding.ASCII.GetBytes(w.Key + "\0"));
                f.Transport.Put(def, new byte[4500]); f.Transport.Put64(def + 8, 0x2000800000);
                f.Transport.Put32(def + 112, 4); f.Transport.Put32(def + 2320, uint.MaxValue);
                f.Transport.Put32(header + 404, 10);
            }
            foreach (var s in Bo3Profile.Signatures) {
                f.Transport.Put(moduleBase + s.Rva, s.Pattern.Bytes);
                f.Transport.Symbols[moduleBase + s.Rva] = s.Name;
            }
            f.PutInventory("pistol_revolver38_zm"); f.PutHeldWeapon("pistol_revolver38_zm");
            f.Trainer = new Bo3Trainer(f.Transport); return f;
        }
        internal ulong HeaderAddress(ulong id) => 0x2000300000 + id * 0x1000;
        internal ulong DefAddress(ulong id) => 0x2000500000 + id * 0x2000;
        internal ulong NameAddress(ulong id) => 0x2000700000 + id * 0x1000;
        internal void PutFlags(uint flags) => Transport.Put32(Client + 0x20, flags);
        internal void PutHealth(int health) { Transport.Put32(Entity + 0x2C8, (uint)health); Transport.Put32(Client + 0x368, (uint)health); }
        internal void PutHeldWeapon(string name) => Transport.Put64(Client + 0x2C0, Weapons[name]);
        internal void PutInventory(params string[] names)
        {
            Transport.Put(Client + 0x378, new byte[720]); Transport.Put(Client + 0x648, new byte[120]);
            for (int i = 0; i < names.Length; i++) { Transport.Put64(Client + 0x378 + (uint)i * 48, Weapons[names[i]]); Transport.Put32(Client + 0x648 + (uint)i * 4, 32); Transport.Put32(Client + 0x684 + (uint)i * 4, 8); }
        }
        internal void SetTitleId(string title) => Transport.Info = new Frame4ProcessInfo(42, "eboot.bin", "", title, "");
        internal void CorruptSignature(string symbol) { var s = Bo3Profile.Signatures.Single(x => x.Name == symbol); Transport.Put(Base + s.Rva, new byte[] { 0 }); }
    }
}
