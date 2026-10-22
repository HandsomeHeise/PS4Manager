using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Games.Bo3
{
    public sealed partial class Bo3Trainer : IGameTrainer
    {
        private readonly ITrainerTransport transport;
        private TrainerIdentity cachedIdentity;
        private int cachedCount;
        private Dictionary<ulong, Bo3Weapon> cachedWeapons;
        private TrainerIdentity ownedIdentity;
        private ulong ownedClient;
        private string ownedName;
        public Bo3Trainer(ITrainerTransport transport) { this.transport = transport ?? throw new ArgumentNullException(nameof(transport)); }

        public async Task<TrainerStatus> ProbeAsync(TrainerTarget target, CancellationToken ct)
        {
            try {
                var p = await ReadProbeAsync(target, ct).ConfigureAwait(false);
                bool owned = p.Identity.Equals(ownedIdentity) && p.Client == ownedClient && p.PlayerName == ownedName;
                if ((p.Flags & 1) == 0 || !owned) ownedIdentity = null;
                return new TrainerStatus(p.Identity, true, "Confirm offline Zombies and pause before each action.",
                    (p.Flags & 1) == 0 ? GodmodeState.Off : owned ? GodmodeState.OnOwned : GodmodeState.OnExternal, p.PlayerName,
                    Bo3Profile.DisplayWeapon(p.Weapons[p.CurrentWeapon & 0x1ff].Name), ContextKey(p));
            } catch (OperationCanceledException) { ownedIdentity = null; throw; }
            catch (Exception ex) {
                cachedWeapons = null; ownedIdentity = null;
                return new TrainerStatus(null, false, ex.Message, GodmodeState.Unknown, null, null);
            }
        }

        private static void Guard(TrainerTarget target, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); target.SessionToken.ThrowIfCancellationRequested(); if (!target.IsCurrent()) throw new OperationCanceledException("The console session changed."); }
        private static string ContextKey(Bo3Probe p)
        {
            using (var hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(p.Client.ToString("X") + ":" + p.CurrentWeapon.ToString("X") + ":" + p.PlayerName + ":" + Convert.ToBase64String(p.InventoryBytes))));
        }
        private static ulong Pointer(ulong value)
        {
            if (value < 0x1000 || value > 0x7FFFFFFFFFFF || (value & 7) != 0) throw new InvalidDataException("Invalid player/weapon pointer. Refresh after entering a local round.");
            return value;
        }
        private async Task<byte[]> Read(TrainerTarget target, int pid, ulong address, uint length, CancellationToken ct)
        {
            Guard(target, ct);
            if (length == 0 || length > 4096 || address > ulong.MaxValue - length) throw new InvalidDataException("Invalid trainer read range.");
            var bytes = await transport.ReadAsync(target, pid, address, length, ct).ConfigureAwait(false);
            Guard(target, ct);
            if (bytes == null || bytes.Length != length) throw new InvalidDataException("Incomplete trainer memory read.");
            return bytes;
        }
        private async Task<ulong> U64(TrainerTarget t, int pid, ulong address, CancellationToken ct) => BitConverter.ToUInt64(await Read(t, pid, address, 8, ct).ConfigureAwait(false), 0);
        private async Task<uint> U32(TrainerTarget t, int pid, ulong address, CancellationToken ct) => BitConverter.ToUInt32(await Read(t, pid, address, 4, ct).ConfigureAwait(false), 0);
        private async Task<string> StringAt(TrainerTarget t, int pid, ulong address, CancellationToken ct)
        {
            var bytes = await Read(t, pid, address, 128, ct).ConfigureAwait(false);
            var end = Array.IndexOf(bytes, (byte)0);
            if (end < 1) throw new InvalidDataException("Missing or unterminated player/weapon name.");
            var value = new UTF8Encoding(false, true).GetString(bytes, 0, end);
            if (value.Any(char.IsControl)) throw new InvalidDataException("Invalid player/weapon name.");
            return value;
        }

        private async Task<TrainerIdentity> IdentityAsync(TrainerTarget target, CancellationToken ct)
        {
            try { return await ReadIdentityAsync(target, ct).ConfigureAwait(false); }
            catch { transport.Invalidate(); cachedIdentity = null; cachedWeapons = null; ownedIdentity = null; throw; }
        }
        private async Task<TrainerIdentity> ReadIdentityAsync(TrainerTarget target, CancellationToken ct)
        {
            Guard(target, ct);
            var processes = (await transport.GetProcessesAsync(target, ct).ConfigureAwait(false)).Where(p => p.Name == "eboot.bin").ToArray();
            if (processes.Length != 1) throw new InvalidOperationException("Start BO3 and enter a local Zombies round.");
            var info = await transport.GetProcessInfoAsync(target, processes[0].Pid, ct).ConfigureAwait(false);
            if (info.Pid != processes[0].Pid || info.TitleId != Bo3Profile.TitleId) throw new InvalidOperationException("Start BO3 (CUSA02624). The running title is not supported.");
            var modules = (await transport.GetModulesAsync(target, info.Pid, ct).ConfigureAwait(false)).Where(m => m.Name == "eboot.bin").ToArray();
            if (modules.Length != 1) throw new InvalidDataException("The eboot module could not be identified.");
            var module = modules[0]; Pointer(module.TextAddress);
            foreach (var signature in Bo3Profile.Signatures) {
                if (signature.Rva + (uint)signature.Pattern.Length > module.TextSize) throw new InvalidDataException("Unsupported build: code range is missing.");
                var bytes = await Read(target, info.Pid, checked(module.TextAddress + signature.Rva), (uint)signature.Pattern.Length, ct).ConfigureAwait(false);
                for (int i = 0; i < bytes.Length; i++)
                    if (signature.Pattern.Mask[i] != 0 && bytes[i] != signature.Pattern.Bytes[i]) throw new InvalidDataException("Unsupported build: " + signature.Name + " signature differs.");
            }
            return new TrainerIdentity(target.Generation, target.Endpoint.Host, info.Pid, info.TitleId, module.TextAddress, Bo3Profile.Id);
        }

        private async Task<Bo3Weapon> WeaponAsync(TrainerTarget target, TrainerIdentity identity, ulong id, CancellationToken ct)
        {
            var header = Pointer(await U64(target, identity.Pid, checked(identity.ModuleBase + Bo3Profile.WeaponTable + id * 8), ct).ConfigureAwait(false));
            var fields = await Read(target, identity.Pid, header, 32, ct).ConfigureAwait(false);
            var nameAddress = BitConverter.ToUInt64(fields, 0);
            if (nameAddress < 0x1000 || nameAddress > 0x7FFFFFFFFFFF) throw new InvalidDataException("Invalid weapon name pointer.");
            return new Bo3Weapon { Id = id, Header = header, Definition = Pointer(BitConverter.ToUInt64(fields, 24)), NameAddress = nameAddress,
                Name = await StringAt(target, identity.Pid, nameAddress, ct).ConfigureAwait(false) };
        }

        internal async Task<Bo3Probe> ReadProbeAsync(TrainerTarget target, CancellationToken ct)
        {
            var identity = await IdentityAsync(target, ct).ConfigureAwait(false); int pid = identity.Pid;
            var entity = checked(identity.ModuleBase + Bo3Profile.Entities);
            var client = Pointer(await U64(target, pid, entity + 0x250, ct).ConfigureAwait(false));
            int health = unchecked((int)await U32(target, pid, entity + 0x2C8, ct).ConfigureAwait(false));
            int mirror = unchecked((int)await U32(target, pid, client + 0x368, ct).ConfigureAwait(false));
            if (health <= 0 || health > 10000 || health != mirror) throw new InvalidOperationException("No active player, or player state is changing. Pause and refresh.");
            var current = await U64(target, pid, client + 0x2C0, ct).ConfigureAwait(false);
            var inventoryBytes = await Read(target, pid, client + 0x378, 720, ct).ConfigureAwait(false);
            var inventory = Enumerable.Range(0, 15).Select(i => BitConverter.ToUInt64(inventoryBytes, i * 48)).ToArray();
            int slot = Array.IndexOf(inventory, current);
            if (current == 0 || slot < 0) throw new InvalidOperationException("No held inventory weapon. Leave menus and pause the round.");
            var maxIndex = await U32(target, pid, identity.ModuleBase + Bo3Profile.WeaponCount, ct).ConfigureAwait(false);
            if (maxIndex < 1 || maxIndex >= 512) throw new InvalidDataException("Invalid loaded weapon count.");
            int count = (int)maxIndex + 1;
            var weapons = new Dictionary<ulong, Bo3Weapon>();
            if (cachedWeapons == null || !identity.Equals(cachedIdentity) || cachedCount != count) {
                var table = await Read(target, pid, identity.ModuleBase + Bo3Profile.WeaponTable, (uint)(count * 8), ct).ConfigureAwait(false);
                for (int i = 1; i < count; i++) {
                    if (BitConverter.ToUInt64(table, i * 8) == 0) continue;
                    weapons.Add((ulong)i, await WeaponAsync(target, identity, (ulong)i, ct).ConfigureAwait(false));
                }
                if (weapons.Values.GroupBy(w => w.Name).Any(g => (g.Key == "pistol_burst_zm" || g.Key == "pistol_revolver38_zm") && g.Count() != 1))
                    throw new InvalidDataException("Ambiguous loaded pistol definition.");
                cachedWeapons = weapons; cachedIdentity = identity; cachedCount = count;
            } else {
                foreach (var w in cachedWeapons) weapons.Add(w.Key, w.Value);
            }
            // Refresh only relevant entries on polling; never trust cached heap pointers for writes.
            var relevant = inventory.Where(w => w != 0).Select(w => w & 0x1ff).Concat(weapons.Values.Where(w => w.Name == "pistol_burst_zm" || w.Name == "pistol_revolver38_zm").Select(w => w.Id)).Distinct().ToArray();
            foreach (var id in relevant) {
                if (id == 0 || id >= (ulong)count || !weapons.ContainsKey(id)) throw new InvalidDataException("Invalid inventory weapon.");
                var fresh = await WeaponAsync(target, identity, id, ct).ConfigureAwait(false);
                var old = weapons[id];
                if (old.Header != fresh.Header || old.Definition != fresh.Definition || old.Name != fresh.Name) { cachedWeapons = null; throw new InvalidOperationException("Weapon definitions changed. Refresh again."); }
                weapons[id] = fresh;
            }
            var ammo = await Read(target, pid, client + 0x648, 120, ct).ConfigureAwait(false);
            int reserve = BitConverter.ToInt32(ammo, slot * 4), clip = BitConverter.ToInt32(ammo, 60 + slot * 4);
            if (reserve < 0 || reserve > 100000 || clip < 0 || clip > 10000) throw new InvalidDataException("Invalid player ammunition values.");
            var flags = await U32(target, pid, client + 0x20, ct).ConfigureAwait(false);
            var name = await StringAt(target, pid, client + 0x16C44, ct).ConfigureAwait(false);
            if (client != await U64(target, pid, entity + 0x250, ct).ConfigureAwait(false) ||
                current != await U64(target, pid, client + 0x2C0, ct).ConfigureAwait(false) ||
                !inventoryBytes.SequenceEqual(await Read(target, pid, client + 0x378, 720, ct).ConfigureAwait(false)))
                throw new InvalidOperationException("Player state changed during the probe. Pause and refresh.");
            return new Bo3Probe { Identity = identity, Entity = entity, Client = client, Health = health, CurrentWeapon = current,
                Flags = flags, PlayerName = name, Inventory = inventory, InventoryBytes = inventoryBytes, AmmoBytes = ammo, Clip = clip, Reserve = reserve, Weapons = weapons };
        }

        internal async Task<Bo3Probe> RevalidateAsync(TrainerTarget target, Bo3Probe expected, CancellationToken ct)
        {
            var fresh = await ReadProbeAsync(target, ct).ConfigureAwait(false);
            if (!expected.Identity.Equals(fresh.Identity) || expected.Client != fresh.Client || expected.Entity != fresh.Entity ||
                expected.CurrentWeapon != fresh.CurrentWeapon || expected.PlayerName != fresh.PlayerName || !expected.InventoryBytes.SequenceEqual(fresh.InventoryBytes))
                throw new InvalidOperationException("Player or inventory changed. Refresh and confirm the action again.");
            return fresh;
        }
    }
}
