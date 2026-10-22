using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Games.Bo3
{
    public sealed partial class Bo3Trainer
    {
        public async Task<TrainerResult> ExecuteAsync(TrainerTarget target, TrainerIdentity expected, TrainerAction action, CancellationToken ct, string expectedContext = null)
        {
            var operation = new Operation(expected, action);
            try {
                var probe = await ReadProbeAsync(target, ct).ConfigureAwait(false);
                if (!probe.Identity.Equals(expected)) return operation.Result(ActionOutcome.Blocked, "The confirmed game session changed.");
                if (expectedContext != null && expectedContext != ContextKey(probe)) return operation.Result(ActionOutcome.Blocked, "The confirmed player or weapon changed. Refresh and confirm again.");
                if (action == TrainerAction.EnableGodmode || action == TrainerAction.DisableGodmode)
                    return await GodmodeAsync(target, probe, operation, ct).ConfigureAwait(false);
                if (action == TrainerAction.RefillAmmo) return await RefillAsync(target, probe, operation, ct).ConfigureAwait(false);
                if (action == TrainerAction.GiveRk5) return await GiveAsync(target, probe, operation, ct).ConfigureAwait(false);
                return operation.Result(ActionOutcome.Blocked, "This action is not available.");
            } catch (Exception ex) {
                ownedIdentity = null;
                if (operation.GrantVerified)
                    return operation.Result(ActionOutcome.Partial, "RK5 was added, but ammunition initialization could not be verified. Do not grant it again. " + ex.Message);
                if (operation.Dispatched || ex is TrainerDispatchException)
                    return operation.Result(ActionOutcome.Unknown, "Outcome unknown; the change may have been applied. Refresh before continuing. " + ex.Message);
                return operation.Result(ex is OperationCanceledException ? ActionOutcome.Cancelled : ActionOutcome.Blocked, ex.Message);
            }
        }

        private async Task<TrainerResult> GodmodeAsync(TrainerTarget target, Bo3Probe probe, Operation operation, CancellationToken ct)
        {
            var fresh = await RevalidateAsync(target, probe, ct).ConfigureAwait(false);
            var flags = await U32(target, fresh.Identity.Pid, fresh.Client + 0x20, ct).ConfigureAwait(false);
            bool enable = operation.Action == TrainerAction.EnableGodmode;
            uint next = enable ? flags | 1U : flags & ~1U;
            if (next == flags) return operation.Result(ActionOutcome.NoChange, enable ? "Godmode is already enabled." : "Godmode is already off.");
            Guard(target, ct); operation.Dispatched = true;
            await transport.WriteAsync(target, fresh.Identity, fresh.Client + 0x20, BitConverter.GetBytes(next), ct).ConfigureAwait(false);
            var after = await RevalidateAsync(target, fresh, ct).ConfigureAwait(false);
            if (after.Flags != next) throw new InvalidDataException("Godmode readback did not match the requested state.");
            ownedIdentity = enable ? after.Identity : null; ownedClient = after.Client; ownedName = after.PlayerName;
            return operation.Result(ActionOutcome.Success, enable ? "Godmode enabled. Closing Games will leave it enabled." : "Godmode disabled.");
        }

        private sealed class Operation
        {
            internal Operation(TrainerIdentity identity, TrainerAction action) { Identity = identity; Action = action; }
            internal TrainerIdentity Identity { get; }
            internal TrainerAction Action { get; }
            internal bool Dispatched;
            internal bool GrantVerified;
            internal TrainerResult Result(ActionOutcome outcome, string message) => new TrainerResult(Identity, Action, outcome, message);
        }

        private async Task PlainPistolAsync(TrainerTarget target, Bo3Probe p, ulong weapon, CancellationToken ct)
        {
            Bo3Weapon w;
            if (weapon == 0 || weapon > 511 || !p.Weapons.TryGetValue(weapon, out w) ||
                (w.Name != "pistol_revolver38_zm" && w.Name != "pistol_burst_zm"))
                throw new InvalidOperationException("Only the plain Bloodhound and RK5 are supported. Upgraded/modified weapons are not supported.");
            // Path guards from EFDEE0/DE9460 and the verified plain RK5 probe. No offhand,
            // attachment, alternate, dual-wield, shared-cap or client-command branches.
            var h = await Read(target, p.Identity.Pid, w.Header, 408, ct).ConfigureAwait(false);
            var d = await Read(target, p.Identity.Pid, w.Definition, 4096, ct).ConfigureAwait(false);
            var tail = await Read(target, p.Identity.Pid, w.Definition + 4096, 378, ct).ConfigureAwait(false);
            if (BitConverter.ToUInt64(h, 24) != w.Definition || BitConverter.ToUInt64(d, 8) == 0 ||
                BitConverter.ToInt32(d, 108) != 0 || BitConverter.ToInt32(d, 112) != 4 ||
                BitConverter.ToInt32(d, 124) != 0 || BitConverter.ToInt32(d, 144) != 0 ||
                BitConverter.ToInt32(d, 148) != 0 || BitConverter.ToInt32(d, 724) != 0 ||
                BitConverter.ToInt32(d, 728) != 0 || BitConverter.ToInt32(d, 2148) != 0 ||
                BitConverter.ToInt32(d, 2320) != -1 || d[3712] != 0 || d[3724] != 0 || d[3739] != 0 ||
                BitConverter.ToInt32(d, 3936) != 0 || tail[4473 - 4096] != 0 ||
                BitConverter.ToInt32(h, 384) != 0 || h[400] != 0 || BitConverter.ToInt32(h, 404) <= 0)
                throw new InvalidOperationException("This weapon definition does not match the tested plain-pistol path.");
        }

        private async Task<ulong> Call(TrainerTarget target, Bo3Probe p, Operation operation, string symbol, ulong[] args, CancellationToken ct)
        {
            await RevalidateAsync(target, p, ct).ConfigureAwait(false);
            Guard(target, ct);
            var signature = Bo3Profile.Signatures.Single(s => s.Name == symbol);
            operation.Dispatched = true;
            return await transport.CallAsync(target, p.Identity, checked(p.Identity.ModuleBase + signature.Rva), args, ct).ConfigureAwait(false);
        }
        private static int Amount(ulong value)
        {
            int amount = RpcReturn.AsInt32(value);
            if (amount < 0 || amount > 100000) throw new InvalidDataException("Invalid engine ammunition amount.");
            return amount;
        }
        private async Task CheckPistolHelpers(TrainerTarget target, Bo3Probe p, Operation op, ulong weapon, CancellationToken ct)
        {
            await PlainPistolAsync(target, p, weapon, ct).ConfigureAwait(false);
            if (await Call(target, p, op, "BO3_GetWeaponAlternate", new[] { weapon }, ct).ConfigureAwait(false) != 0 ||
                await Call(target, p, op, "BO3_GetWeaponDualWieldPartner", new[] { weapon }, ct).ConfigureAwait(false) != 0 ||
                RpcReturn.AsBool(await Call(target, p, op, "BO3_IsWeaponDualWield", new[] { weapon }, ct).ConfigureAwait(false)))
                throw new InvalidOperationException("Alternate and dual-wield weapon paths are not supported.");
        }
        private async Task<TrainerResult> GiveAsync(TrainerTarget target, Bo3Probe p, Operation op, CancellationToken ct)
        {
            var candidates = p.Weapons.Values.Where(w => w.Name == "pistol_burst_zm").ToArray();
            if (candidates.Length != 1) return op.Result(ActionOutcome.Blocked, "The RK5 definition is not loaded.");
            ulong weapon = candidates[0].Id;
            if (p.Inventory.Any(w => (w & 0x1ff) == weapon)) return op.Result(ActionOutcome.NoChange, "Already owned: RK5.");
            if (!p.Inventory.Contains(0UL)) return op.Result(ActionOutcome.Blocked, "Inventory is full.");
            int firearms = 0;
            foreach (var item in p.Inventory.Where(w => w != 0)) {
                string name = p.Weapons[item & 0x1ff].Name;
                if (name == "pistol_revolver38_zm" || name == "pistol_burst_zm") {
                    await PlainPistolAsync(target, p, item, ct).ConfigureAwait(false); firearms++;
                } else if (name != "knife_zm" && name != "frag_grenade_zm")
                    return op.Result(ActionOutcome.Blocked, "Inventory is outside the tested loadout. Keep only the starting pistol and standard equipment.");
            }
            if (firearms != 1) return op.Result(ActionOutcome.Blocked, "RK5 requires one existing ordinary firearm and a free slot.");
            await PlainPistolAsync(target, p, p.CurrentWeapon, ct).ConfigureAwait(false);
            await CheckPistolHelpers(target, p, op, weapon, ct).ConfigureAwait(false);
            await PlainPistolAsync(target, p, weapon, ct).ConfigureAwait(false);
            int returned = RpcReturn.AsInt32(await Call(target, p, op, "BO3_GivePlayerWeaponInternal", new[] { p.Entity, p.Client, weapon, 0UL, 0UL, 0UL }, ct).ConfigureAwait(false));
            var after = await ReadProbeAsync(target, ct).ConfigureAwait(false);
            EnsureSamePlayer(p, after);
            int newSlot = -1;
            for (int i = 0; i < 15; i++) {
                if (p.Inventory[i] == 0 && after.Inventory[i] == weapon) { if (newSlot >= 0) throw new InvalidDataException("Multiple new weapon slots appeared."); newSlot = i; }
                else if (!p.InventoryBytes.Skip(i * 48).Take(48).SequenceEqual(after.InventoryBytes.Skip(i * 48).Take(48))) throw new InvalidDataException("Another inventory slot changed during grant.");
            }
            if (newSlot < 0 || returned != 1) throw new InvalidDataException("RK5 grant readback was not confirmed.");
            op.GrantVerified = true;
            for (int i = 0; i < 15; i++) if (i != newSlot &&
                (BitConverter.ToInt32(p.AmmoBytes, i * 4) != BitConverter.ToInt32(after.AmmoBytes, i * 4) ||
                 BitConverter.ToInt32(p.AmmoBytes, 60 + i * 4) != BitConverter.ToInt32(after.AmmoBytes, 60 + i * 4)))
                throw new InvalidDataException("Another weapon's ammunition changed during grant.");
            int deficit = Amount(await Call(target, after, op, "BO3_StartingAmmoDeficit", new[] { after.Client, weapon }, ct).ConfigureAwait(false));
            int beforeTotal = checked(BitConverter.ToInt32(after.AmmoBytes, newSlot * 4) + BitConverter.ToInt32(after.AmmoBytes, 60 + newSlot * 4));
            if (deficit > 0) {
                await PlainPistolAsync(target, after, weapon, ct).ConfigureAwait(false);
                int added = Amount(await Call(target, after, op, "BO3_AddWeaponAmmo", new[] { after.Entity, weapon, (ulong)deficit, 1UL }, ct).ConfigureAwait(false));
                if (added != deficit) throw new InvalidDataException("RK5 starting ammunition was not fully added.");
            }
            var final = await RevalidateAsync(target, after, ct).ConfigureAwait(false);
            EnsureOtherAmmo(after, final, newSlot);
            if (checked(BitConverter.ToInt32(final.AmmoBytes, newSlot * 4) + BitConverter.ToInt32(final.AmmoBytes, 60 + newSlot * 4)) != beforeTotal + deficit)
                throw new InvalidDataException("RK5 ammunition readback differs from the engine result.");
            return op.Result(ActionOutcome.Success, "RK5 added with starting ammunition. Resume, select it and test firing.");
        }
        private static void EnsureSamePlayer(Bo3Probe before, Bo3Probe after)
        {
            if (!before.Identity.Equals(after.Identity) || before.Client != after.Client || before.Entity != after.Entity || before.CurrentWeapon != after.CurrentWeapon || before.PlayerName != after.PlayerName)
                throw new InvalidOperationException("Player session changed during the action.");
        }
        private static void EnsureOtherAmmo(Bo3Probe before, Bo3Probe after, int slot)
        {
            for (int i = 0; i < 15; i++) if (i != slot && (BitConverter.ToInt32(before.AmmoBytes, i * 4) != BitConverter.ToInt32(after.AmmoBytes, i * 4) ||
                BitConverter.ToInt32(before.AmmoBytes, 60 + i * 4) != BitConverter.ToInt32(after.AmmoBytes, 60 + i * 4)))
                throw new InvalidDataException("Another weapon's ammunition changed.");
        }
        private async Task<TrainerResult> RefillAsync(TrainerTarget target, Bo3Probe p, Operation op, CancellationToken ct)
        {
            ulong weapon = p.CurrentWeapon;
            await PlainPistolAsync(target, p, weapon, ct).ConfigureAwait(false);
            await CheckPistolHelpers(target, p, op, weapon, ct).ConfigureAwait(false);
            int cap = Amount(await Call(target, p, op, "BO3_GetWeaponClipCapacity", new[] { weapon }, ct).ConfigureAwait(false));
            int reserveCap = Amount(await Call(target, p, op, "BO3_GetWeaponReserveCapacity", new[] { p.Client, weapon, 0UL }, ct).ConfigureAwait(false));
            int clip = Amount(await Call(target, p, op, "Trainer_GetWeaponClipAmmo", new[] { p.Client, weapon }, ct).ConfigureAwait(false));
            int reserve = Amount(await Call(target, p, op, "Trainer_GetWeaponReserveAmmo", new[] { p.Client, weapon }, ct).ConfigureAwait(false));
            if (cap <= 0 || clip > cap || reserve > reserveCap || clip != p.Clip || reserve != p.Reserve)
                throw new InvalidDataException("Engine and read-only ammunition values disagree.");
            if (clip == cap && reserve == reserveCap) return op.Result(ActionOutcome.NoChange, "Ammunition is already full.");
            if (clip < cap && reserve > 0) {
                await PlainPistolAsync(target, p, weapon, ct).ConfigureAwait(false);
                // IDA DE93C0: (playerState*, packedWeapon); result is incidental pointer, not a success bool.
                await Call(target, p, op, "Trainer_TransferReserveToClip", new[] { p.Client, weapon }, ct).ConfigureAwait(false);
                var transferred = await RevalidateAsync(target, p, ct).ConfigureAwait(false);
                int moved = Math.Min(cap - clip, reserve);
                if (transferred.Clip != clip + moved || transferred.Reserve != reserve - moved) throw new InvalidDataException("Magazine transfer readback mismatch.");
                EnsureOtherAmmo(p, transferred, Array.IndexOf(p.Inventory, weapon)); p = transferred;
            }
            int headroom = Amount(await Call(target, p, op, "BO3_GetWeaponAmmoHeadroom", new[] { p.Client, weapon }, ct).ConfigureAwait(false));
            // With shared-cap index -1 and special-ammo off, headroom is reserve-only (IDA 15924E0).
            int deficit = checked(cap - p.Clip + reserveCap - p.Reserve);
            if (headroom != reserveCap - p.Reserve || deficit < 0 || deficit > 100000) throw new InvalidDataException("Inconsistent ammunition capacity.");
            if (deficit > 0) {
                await PlainPistolAsync(target, p, weapon, ct).ConfigureAwait(false);
                int added = Amount(await Call(target, p, op, "BO3_AddWeaponAmmo", new[] { p.Entity, weapon, (ulong)deficit, 1UL }, ct).ConfigureAwait(false));
                if (added != deficit) throw new InvalidDataException("Engine did not add the requested ammunition.");
            }
            var final = await RevalidateAsync(target, p, ct).ConfigureAwait(false);
            EnsureOtherAmmo(p, final, Array.IndexOf(p.Inventory, weapon));
            if (final.Clip != cap || final.Reserve != reserveCap) throw new InvalidDataException("Ammunition refill readback mismatch.");
            return op.Result(ActionOutcome.Success, "Ammunition refilled: " + final.Clip + "/" + final.Reserve + ".");
        }
    }
}
