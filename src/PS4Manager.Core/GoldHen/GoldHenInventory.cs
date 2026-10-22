using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Paths;
using FileManager.Core.Transfers;
using Ps4.Core.Titles;

namespace PS4Manager.Core.GoldHen
{
    /// <summary>
    /// Gruppiert Cheat- und Patch-Dateien nach der Titel-ID in ihrem Namen
    /// („CUSA00411.json" → CUSA00411) und holt den Spielnamen über den
    /// vorhandenen Titel-Katalog. Ohne Namen bleibt die ID stehen; ein
    /// fehlgeschlagener Nachschlag verliert keine Datei.
    /// </summary>
    public static class GoldHenInventory
    {
        public const string OtherLabel = "Andere";

        public static async Task<IReadOnlyList<GoldHenTitleGroup>> GroupAsync(IReadOnlyList<RemoteItem> files,
            Func<string, CancellationToken, Task<string>> resolveGameName, CancellationToken ct)
        {
            var byTitle = new Dictionary<string, List<RemoteItem>>(StringComparer.Ordinal);
            var others = new List<RemoteItem>();

            foreach (var file in files ?? (IReadOnlyList<RemoteItem>)new RemoteItem[0])
            {
                var titleId = TitleIdOf(file.Path);
                if (titleId == null)
                {
                    others.Add(file);
                    continue;
                }

                List<RemoteItem> list;
                if (!byTitle.TryGetValue(titleId, out list)) byTitle[titleId] = list = new List<RemoteItem>();
                list.Add(file);
            }

            var groups = new List<GoldHenTitleGroup>();
            foreach (var pair in byTitle.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                groups.Add(new GoldHenTitleGroup(pair.Key, await LabelAsync(pair.Key, resolveGameName, ct).ConfigureAwait(false),
                    pair.Value));
            }
            if (others.Count > 0) groups.Add(new GoldHenTitleGroup(null, OtherLabel, others));
            return groups;
        }

        /// <summary>Vier Großbuchstaben und fünf Ziffern, ohne Dateiendung.</summary>
        public static string TitleIdOf(string path)
        {
            var name = RemotePath.Name(path ?? "");
            var dot = name.LastIndexOf('.');
            var stem = dot > 0 ? name.Substring(0, dot) : name;
            return TitleId.IsMatch(stem) ? stem : null;
        }

        private static async Task<string> LabelAsync(string titleId,
            Func<string, CancellationToken, Task<string>> resolveGameName, CancellationToken ct)
        {
            if (resolveGameName == null) return titleId;
            try
            {
                var name = await resolveGameName(titleId, ct).ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(name) ? titleId : name;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return titleId; }
        }
    }
}
