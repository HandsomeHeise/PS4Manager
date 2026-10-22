using System;
using System.Threading.Tasks;
using FileManager.Core.Transfers;

namespace PS4Manager.Core.Mods
{
    public static class ModActivationPrompt
    {
        public static async Task<string> OfferAsync(TransferJob install, string action, Func<bool> isCurrent,
            Func<ModReceipt> loadReceipt, Func<ModReceipt, Task<bool>> ask, Func<Guid, Task> activate)
        {
            return (await OfferResultAsync(install, action, isCurrent, loadReceipt, ask, activate))?.Message;
        }
        public static async Task<ModActivationOutcome> OfferResultAsync(TransferJob install, string action, Func<bool> isCurrent,
            Func<ModReceipt> loadReceipt, Func<ModReceipt, Task<bool>> ask, Func<Guid, Task> activate)
        {
            if (action != "install" && action != "resume") return null;
            await install.Completion;
            if (!isCurrent() || install.State != JobState.Completed) return null;
            try
            {
                var receipt = loadReceipt();
                if (ModPluginActivation.PluginPaths(receipt).Count == 0) return null;
                var accepted = await ask(receipt);
                if (!isCurrent()) return null;
                if (!accepted) return new ModActivationOutcome(ModActivationOutcomeKind.Deferred, "Installed. You can enable the plugin later in GoldHEN Manager.");
                await activate(receipt.TransactionId);
                return isCurrent() ? new ModActivationOutcome(ModActivationOutcomeKind.Enabled, "Enabled in GoldHEN. Start (or restart) the game to load the plugin.") : null;
            }
            catch (Exception ex)
            {
                return isCurrent() ? new ModActivationOutcome(ModActivationOutcomeKind.ActivationFailed, "Installation succeeded, but GoldHEN activation failed: " + ex.Message + " Use GoldHEN Manager to inspect or retry activation.") : null;
            }
        }
    }
}
