using System;
using System.Collections.Generic;
using System.Linq;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;

namespace PS4Manager.Core.GoldHen
{
    public static class GoldHenConfigurationRows
    {
        public static IReadOnlyList<GoldHenConfigurationRow> Build(IReadOnlyList<RemoteItem> files,
            string configurationPath, IReadOnlyList<PluginEntry> plugins)
        {
            var rows = new List<GoldHenConfigurationRow>();
            foreach (var file in files ?? new RemoteItem[0])
            {
                rows.Add(new GoldHenConfigurationRow(new[]
                {
                    FileManager.Core.Paths.RemotePath.Name(file.Path),
                    SizeFormat.Bytes(file.Size),
                    FileTime(file.Modified)
                }, file, false));

                if (!string.Equals(file.Path, configurationPath, StringComparison.Ordinal)) continue;
                foreach (var plugin in (plugins ?? new PluginEntry[0]).Where(p => p.LineIndex >= 0).OrderBy(p => p.LineIndex))
                {
                    rows.Add(new GoldHenConfigurationRow(new[]
                    {
                        "  " + plugin.Name,
                        Activation(plugin.Activation),
                        "Zeile " + (plugin.LineIndex + 1)
                    }, file, true));
                }
            }
            return rows;
        }

        private static string Activation(GoldHenActivation activation)
        {
            switch (activation)
            {
                case GoldHenActivation.Enabled: return "aktiv";
                case GoldHenActivation.Disabled: return "inaktiv";
                default: return "unbekannt";
            }
        }

        private static string FileTime(DateTime modified)
        {
            return modified == DateTime.MinValue ? "" : modified.ToString("g");
        }
    }
}
