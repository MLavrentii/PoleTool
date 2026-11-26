using System.Collections.Generic;
using System.Linq;

namespace PoleToolApp.Core
{
    /// <summary>
    /// Switching equipment on / off together: items mounted on an item (insulators on an arm, heads on their arm …)
    /// and items that only exist for it (the VCT arm, meter cable and meter box for the VCT) follow it.
    /// Items switched off this way remember who did it and come back when that item is switched on again.
    /// </summary>
    public static class Links
    {
        /// <summary>Extra followers that are not "mounted on" the item.</summary>
        static readonly Dictionary<string, string[]> Followers = new Dictionary<string, string[]>
        {
            ["vct"] = new[] { "arm_vct", "meter_cable", "meter_box", "conduit_meter", "pulse_box" },
            ["meter_box"] = new[] { "pulse_box" },
            ["pas"] = new[] { "ctrl_cable", "sog_box", "pas_rbox", "conduit_ctrl" },
            ["guy"] = new[] { "guy_ins", "guy_guard", "guy_anchor", "guy_grip" },
            ["lrod"] = new[] { "earth_lp" },
        };

        /// <summary>Items that move / switch with 'host' (mounted on it).</summary>
        public static IEnumerable<EquipItem> MountedOn(PoleJob job, EquipItem host)
        {
            foreach (var it in job.Items)
            {
                if (it == host) continue;
                if (!string.IsNullOrEmpty(it.At)) { if (it.At == host.Id) yield return it; continue; }
                var hd = Catalog.Find(it.Def)?.AtDef;
                if (hd != host.Def) continue;
                // automatic host = the first enabled item of that definition (or this one if none is enabled)
                var first = job.Items.FirstOrDefault(i => i.On && i.Def == hd && i != it) ?? host;
                if (first == host) yield return it;
            }
        }

        /// <summary>Switches an item on or off together with its followers. Returns the other items that changed.</summary>
        public static List<EquipItem> SetOn(PoleJob job, EquipItem item, bool on)
        {
            var changed = new List<EquipItem>();
            if (on)
            {
                item.On = true; item.OffBy = null;
                foreach (var f in job.Items.Where(i => !i.On && i.OffBy == item.Id).ToList())
                { changed.Add(f); changed.AddRange(SetOn(job, f, true)); }
            }
            else
            {
                var follow = new List<EquipItem>(MountedOn(job, item));
                if (Followers.TryGetValue(item.Def, out var defs))
                    follow.AddRange(job.Items.Where(i => defs.Contains(i.Def) && !follow.Contains(i)));
                item.On = false;
                foreach (var f in follow)
                {
                    if (!f.On) continue;
                    // keep it if another enabled item of the same kind can still host / need it
                    if (f.At == null && job.Items.Any(o => o != item && o.On && o.Def == item.Def)) continue;
                    var sub = SetOn(job, f, false);
                    f.OffBy = item.Id; changed.Add(f); changed.AddRange(sub);
                }
            }
            return changed;
        }
    }
}
