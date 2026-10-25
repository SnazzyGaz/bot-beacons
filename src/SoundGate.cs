using System;
using System.Collections.Generic;

namespace BotBeacons
{
    // 1.1.5: decides whether an alert sound may play. Pure logic (no Windows, no settings, no clock of its own),
    // so it can be checked off-PC with Mono.
    //   message: at most one per beacon per 4 s (any sound from that beacon counts)
    //   attention: at most one per beacon per 10 s
    //   global: a burst across beacons plays at most one sound per 1.5 s
    //   quiet hours: 23:00-07:00 local time, when switched on
    public sealed class SoundGate
    {
        public double MessageGap = 4.0, AttentionGap = 10.0, GlobalGap = 1.5;
        public int QuietFrom = 23, QuietTo = 7;
        readonly Dictionary<string, double> lastAnyByBeacon = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, double> lastAttByBeacon = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double lastGlobal = double.NegativeInfinity;
        readonly object gate = new object();

        public static bool InQuietHours(DateTime local, int from, int to)
        {
            int h = local.Hour;
            if (from == to) return false;
            return from > to ? (h >= from || h < to) : (h >= from && h < to);
        }

        // now = monotonic seconds. Returns null when the sound should play (and records it), else the reason it is skipped.
        public string Check(string beacon, bool attention, double now, DateTime local, bool soundOn, bool quietHours, bool muted)
        {
            if (!soundOn) return "sound off";
            if (muted) return "beacon muted";
            if (quietHours && InQuietHours(local, QuietFrom, QuietTo)) return "quiet hours";
            beacon = beacon ?? "";
            lock (gate)
            {
                double t;
                if (attention) { if (lastAttByBeacon.TryGetValue(beacon, out t) && now - t < AttentionGap) return "throttled (attention, this beacon)"; }
                else if (lastAnyByBeacon.TryGetValue(beacon, out t) && now - t < MessageGap) return "throttled (message, this beacon)";
                if (now - lastGlobal < GlobalGap) return "throttled (global)";
                lastGlobal = now; lastAnyByBeacon[beacon] = now;
                if (attention) lastAttByBeacon[beacon] = now;
                return null;
            }
        }
    }
}
