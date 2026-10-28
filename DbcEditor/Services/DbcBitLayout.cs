using DbcEditor.Models;

namespace DbcEditor.Services;

/// <summary>Vector/BUSMASTER bit occupancy: Intel +=1; Motorola within-byte reverse then +15.</summary>
public static class DbcBitLayout
{
    public static IEnumerable<int> CollectBits(DbcSignal sg)
    {
        int bit = sg.StartBit;
        int len = Math.Max(0, sg.Length);
        for (int i = 0; i < len; i++)
        {
            yield return bit;
            if (i + 1 >= len) break;
            if (sg.IsLittleEndian) bit += 1;
            else if (bit % 8 == 0) bit += 15;
            else bit -= 1;
        }
    }

    /// <summary>
    /// Number CANdb++ shows in Startbit. Intel is the DBC SG_ value (LSB).
    /// Motorola SG_ stores the MSB; the dialog shows the LSB, the last bit of <see cref="CollectBits"/>.
    /// <c>97|6@0+</c> occupies 97,96,111,110,109,108, so the box shows 108.
    /// </summary>
    public static int VectorStartBit(DbcSignal sg)
    {
        if (sg.IsLittleEndian || sg.Length <= 1) return sg.StartBit;
        int last = sg.StartBit;
        foreach (var b in CollectBits(sg))
            last = b;
        return last;
    }

    /// <summary>DBC SG_ start from the CANdb++ Startbit box. Motorola walks back to the MSB.</summary>
    public static int DbcStartFromVector(bool isLittleEndian, int vectorStart, int length)
    {
        if (isLittleEndian || length <= 1) return Math.Max(0, vectorStart);
        int bit = vectorStart;
        for (int i = 1; i < length; i++)
            bit = bit % 8 == 7 ? bit - 15 : bit + 1;
        return bit < 0 ? 0 : bit;
    }

    public static int MinOccupiedBit(DbcSignal sg)
    {
        int min = int.MaxValue;
        foreach (var b in CollectBits(sg))
            if (b < min) min = b;
        return min == int.MaxValue ? sg.StartBit : min;
    }

    public static int MaxOccupiedBit(DbcSignal sg)
    {
        int max = int.MinValue;
        foreach (var b in CollectBits(sg))
            if (b > max) max = b;
        return max == int.MinValue ? sg.StartBit : max;
    }

    /// <summary>
    /// True when occupancy is compact (no Motorola byte-wrap hole like start=51 → 48..51 + 60..63).
    /// </summary>
    public static bool IsCompactPlacement(DbcSignal sg)
    {
        int len = Math.Max(1, sg.Length);
        int min = MinOccupiedBit(sg);
        int max = MaxOccupiedBit(sg);
        return max - min + 1 <= len + 7;
    }

    /// <summary>
    /// Lowest StartBit whose occupied bits are all &gt;= <paramref name="gapStart"/>,
    /// do not overlap <paramref name="blocked"/>, and stay compact.
    /// </summary>
    public static int? FindCompactStartInGap(DbcSignal sg, int gapStart, IEnumerable<DbcSignal> blocked)
    {
        gapStart = Math.Max(0, gapStart);
        int len = Math.Max(1, sg.Length);
        var occupied = new HashSet<int>();
        foreach (var o in blocked)
        {
            if (ReferenceEquals(o, sg) || o.Length <= 0) continue;
            foreach (var b in CollectBits(o))
                occupied.Add(b);
        }

        for (int cand = gapStart; cand <= gapStart + 256; cand++)
        {
            var probe = new DbcSignal
            {
                StartBit = cand,
                Length = len,
                IsLittleEndian = sg.IsLittleEndian
            };
            if (!IsCompactPlacement(probe)) continue;
            if (MinOccupiedBit(probe) < gapStart) continue;
            bool hit = false;
            foreach (var b in CollectBits(probe))
            {
                if (occupied.Contains(b)) { hit = true; break; }
            }
            if (hit) continue;
            return cand;
        }
        return null;
    }

    /// <summary>
    /// Compact StartBit with MinOccupied &gt;= gapStart (Vector To Front: may overlap others).
    /// Prefers exact MinOccupied == gapStart (e.g. after Failure@43 → start 51 → bits 44..51).
    /// </summary>
    public static int? FindCompactStartAt(DbcSignal sg, int gapStart)
    {
        gapStart = Math.Max(0, gapStart);
        int len = Math.Max(1, sg.Length);

        for (int cand = gapStart; cand <= gapStart + 64; cand++)
        {
            var probe = new DbcSignal
            {
                StartBit = cand,
                Length = len,
                IsLittleEndian = sg.IsLittleEndian
            };
            if (!IsCompactPlacement(probe)) continue;
            if (MinOccupiedBit(probe) == gapStart)
                return cand;
        }

        for (int cand = gapStart; cand <= gapStart + 256; cand++)
        {
            var probe = new DbcSignal
            {
                StartBit = cand,
                Length = len,
                IsLittleEndian = sg.IsLittleEndian
            };
            if (!IsCompactPlacement(probe)) continue;
            if (MinOccupiedBit(probe) >= gapStart)
                return cand;
        }
        return null;
    }

    /// <summary>StartBit so the signal's lowest occupied bit equals <paramref name="minBit"/> (compact only).</summary>
    public static int? TryStartBitForMinOccupied(DbcSignal sg, int minBit)
    {
        minBit = Math.Max(0, minBit);
        if (sg.IsLittleEndian)
            return minBit;
        int len = Math.Max(1, sg.Length);
        for (int cand = minBit; cand <= minBit + 64; cand++)
        {
            var probe = new DbcSignal
            {
                StartBit = cand,
                Length = len,
                IsLittleEndian = false
            };
            if (MinOccupiedBit(probe) == minBit && IsCompactPlacement(probe))
                return cand;
        }
        return null;
    }

    /// <summary>StartBit so the signal's highest occupied bit equals <paramref name="maxBit"/> (compact only).</summary>
    public static int? TryStartBitForMaxOccupied(DbcSignal sg, int maxBit)
    {
        maxBit = Math.Max(0, maxBit);
        int len = Math.Max(1, sg.Length);
        if (sg.IsLittleEndian)
            return Math.Max(0, maxBit - len + 1);
        for (int cand = Math.Max(0, maxBit - 64); cand <= maxBit + 8; cand++)
        {
            var probe = new DbcSignal
            {
                StartBit = cand,
                Length = len,
                IsLittleEndian = false
            };
            if (MaxOccupiedBit(probe) == maxBit && IsCompactPlacement(probe))
                return cand;
        }
        return null;
    }

    /// <summary>
    /// Vector CANdb++: yellow ! when two signals that can be on the wire together share a bit.
    /// Multiplexed signals with different switch values (ADM_SYNC m32 vs m40) are not an overlap.
    /// </summary>
    public static HashSet<DbcSignal> FindOverlappingSignals(IEnumerable<DbcSignal> signals)
    {
        var list = signals.Where(s => s.Length > 0).ToList();
        var atBit = new Dictionary<int, List<DbcSignal>>();
        foreach (var s in list)
        {
            foreach (var b in CollectBits(s))
            {
                if (!atBit.TryGetValue(b, out var owners))
                    atBit[b] = owners = new List<DbcSignal>();
                if (!owners.Contains(s))
                    owners.Add(s);
            }
        }

        var hit = new HashSet<DbcSignal>();
        foreach (var owners in atBit.Values)
        {
            for (int i = 0; i < owners.Count; i++)
            {
                for (int j = i + 1; j < owners.Count; j++)
                {
                    if (MutuallyExclusiveMux(owners[i], owners[j])) continue;
                    hit.Add(owners[i]);
                    hit.Add(owners[j]);
                }
            }
        }
        return hit;
    }

    /// <summary>
    /// True when the two signals belong to the same multiplexor but can never be active together.
    /// The multiplexor itself and signals with no switch value are always present.
    /// </summary>
    public static bool MutuallyExclusiveMux(DbcSignal a, DbcSignal b)
    {
        var ma = MuxModeOf(a);
        var mb = MuxModeOf(b);
        if (ma.Always || mb.Always) return false;
        if (!string.Equals(ma.Parent, mb.Parent, StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (var ra in ma.Ranges)
        {
            foreach (var rb in mb.Ranges)
            {
                if (ra.Lo <= rb.Hi && rb.Lo <= ra.Hi)
                    return false;
            }
        }
        return ma.Ranges.Count > 0 && mb.Ranges.Count > 0;
    }

    private readonly struct MuxMode
    {
        public bool Always { get; init; }
        public string Parent { get; init; }
        public List<(int Lo, int Hi)> Ranges { get; init; }
    }

    private static MuxMode MuxModeOf(DbcSignal sg)
    {
        var t = (sg.Multiplexing ?? "").Trim();
        if (t.Length == 0 || t == "-" ||
            t.Equals("Multiplexor", StringComparison.OrdinalIgnoreCase))
            return new MuxMode { Always = true, Parent = "", Ranges = new List<(int, int)>() };

        var basic = System.Text.RegularExpressions.Regex.Match(t,
            @"^Multiplexed signal\s+(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (basic.Success && int.TryParse(basic.Groups[1].Value, out var n))
            return new MuxMode { Parent = "", Ranges = new List<(int, int)> { (n, n) } };

        var ext = System.Text.RegularExpressions.Regex.Match(t,
            @"^Mux\s+(\S+)\s+\[([^\]]+)\]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (ext.Success)
        {
            var ranges = new List<(int Lo, int Hi)>();
            foreach (var part in ext.Groups[2].Value.Split(','))
            {
                var seg = part.Split('-');
                if (seg.Length == 2 &&
                    int.TryParse(seg[0].Trim(), out var lo) &&
                    int.TryParse(seg[1].Trim(), out var hi))
                    ranges.Add((Math.Min(lo, hi), Math.Max(lo, hi)));
            }
            if (ranges.Count > 0)
                return new MuxMode { Parent = ext.Groups[1].Value, Ranges = ranges };
        }

        return new MuxMode { Always = true, Parent = "", Ranges = new List<(int, int)>() };
    }

    /// <summary>
    /// Layout visibility. No value: only signals that are always on the wire (the multiplexor
    /// and unswitched signals). A value: those, plus signals switched to that raw value.
    /// </summary>
    public static bool IsLayoutVisible(DbcSignal sg, string? multiplexorName, int? muxValue)
    {
        var mode = MuxModeOf(sg);
        if (mode.Always) return true;
        if (muxValue == null) return false;
        if (!string.IsNullOrEmpty(mode.Parent) &&
            !string.IsNullOrEmpty(multiplexorName) &&
            !mode.Parent.Equals(multiplexorName, StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (var r in mode.Ranges)
            if (muxValue.Value >= r.Lo && muxValue.Value <= r.Hi)
                return true;
        return false;
    }

    /// <summary>
    /// Vector Layout drag: drop cell becomes the new StartBit (bit-precise, same row OK).
    /// Intel: StartBit = LSB; Motorola: StartBit = MSB — both equal the clicked abs bit.
    /// </summary>
    public static int StartBitForDrop(DbcSignal sg, int absBit) =>
        Math.Max(0, absBit);

    /// <summary>Layout Add on a row: default 8-bit signal aligned to that byte.</summary>
    public static int StartBitForByteInsert(DbcSignal sg, int absBit)
    {
        int byteIdx = Math.Max(0, absBit / 8);
        if (sg.IsLittleEndian)
            return byteIdx * 8;
        return byteIdx * 8 + 7;
    }

    /// <summary>
    /// Vector Layout [Arrange] — pack in current Signals list order:
    /// 8-bit ≤ 1 byte, 16-bit ≤ 2 bytes, 8–16 bit on byte limits;
    /// &lt; 8 bit must not cross a byte boundary; pack from the start of the message.
    /// Caller must ensure Σ lengths ≤ 64 for classic CAN.
    /// </summary>
    public static void ArrangeSignals(IList<DbcSignal> signals)
    {
        int nextBit = 0; /* next free bit (low → high), message start first */
        foreach (var sg in signals)
        {
            int len = Math.Max(1, sg.Length);

            if (len >= 8)
            {
                /* Byte-limit alignment for 8–16 bit signals */
                if (nextBit % 8 != 0)
                    nextBit = (nextBit / 8 + 1) * 8;

                if (sg.IsLittleEndian)
                {
                    sg.StartBit = nextBit;
                }
                else
                {
                    /* Motorola: StartBit = MSB of first byte of the packed field */
                    sg.StartBit = nextBit + 7;
                }

                /* Advance by whole bytes used (8 → 1 byte, 9–16 → 2 bytes) */
                int bytesUsed = len <= 8 ? 1 : 2;
                nextBit += bytesUsed * 8;
            }
            else
            {
                /* < 8 bits: stay inside one byte; prefer message beginning */
                int bitInByte = nextBit % 8;
                int remain = 8 - bitInByte;
                if (len > remain)
                    nextBit = (nextBit / 8 + 1) * 8;

                if (sg.IsLittleEndian)
                {
                    sg.StartBit = nextBit;
                }
                else
                {
                    /* Motorola MSB of the small field */
                    sg.StartBit = nextBit + len - 1;
                }

                nextBit += len;
            }
        }
    }

    public static string OverlapTooltip(DbcSignal sg) =>
        $"Signal '{sg.Name}' overlaps another signal in the same message.";

    /// <summary>
    /// After a StartBit swap, if <paramref name="covered"/> lies entirely inside
    /// <paramref name="cover"/>, move it onto bits <paramref name="cover"/> just released.
    /// numTxObjs 42↔Failure 43 → numTxObjs 43..55, Failure parks on freed bit 51.
    /// </summary>
    public static void ParkIfCovered(DbcSignal covered, DbcSignal cover, IReadOnlyCollection<int> released)
    {
        if (covered.Length <= 0 || released.Count == 0) return;
        var coverBits = new HashSet<int>(CollectBits(cover));
        bool inside = true;
        foreach (var b in CollectBits(covered))
        {
            if (!coverBits.Contains(b)) { inside = false; break; }
        }
        if (!inside) return;

        var free = new HashSet<int>(released);
        if (covered.Length == 1)
        {
            int lsb = CollectBits(cover).Last();
            int next = lsb % 8 == 0 ? lsb + 15 : lsb - 1;
            covered.StartBit = free.Contains(next) ? next : released.First();
            return;
        }

        foreach (var cand in released)
        {
            var probe = new DbcSignal
            {
                StartBit = cand,
                Length = covered.Length,
                IsLittleEndian = covered.IsLittleEndian
            };
            bool ok = true;
            foreach (var b in CollectBits(probe))
            {
                if (!free.Contains(b)) { ok = false; break; }
            }
            if (ok)
            {
                covered.StartBit = cand;
                return;
            }
        }
    }

    /// <summary>True when two signals share any occupied bit.</summary>
    public static bool Overlaps(DbcSignal a, DbcSignal b)
    {
        if (a.Length <= 0 || b.Length <= 0) return false;
        if (MutuallyExclusiveMux(a, b)) return false;
        var bits = new HashSet<int>(CollectBits(a));
        foreach (var bit in CollectBits(b))
            if (bits.Contains(bit)) return true;
        return false;
    }

    /// <summary>One step along the signal order. +1 is toward the back (CollectBits), −1 toward the front.</summary>
    public static int StepBit(bool isLittleEndian, int bit, int delta)
    {
        if (delta == 0) return bit;
        if (isLittleEndian) return bit + delta;
        if (delta > 0)
            return bit % 8 == 0 ? bit + 15 : bit - 1;
        return bit % 8 == 7 ? bit - 15 : bit + 1;
    }

    /// <summary>Walk <paramref name="steps"/> along the signal order. Negative walks toward the front.</summary>
    public static int WalkBits(bool isLittleEndian, int bit, int steps)
    {
        int dir = steps < 0 ? -1 : 1;
        for (int i = 0, n = Math.Abs(steps); i < n; i++)
        {
            int next = StepBit(isLittleEndian, bit, dir);
            if (next < 0) return 0;
            bit = next;
        }
        return bit;
    }

    /// <summary>
    /// Earliest bit in layout order. Motorola: low byte first, bit7 before bit0.
    /// Intel: low bit number first.
    /// </summary>
    public static int EarliestBit(IEnumerable<int> bits, bool isLittleEndian)
    {
        int best = -1;
        int bestKey = int.MaxValue;
        foreach (var b in bits)
        {
            int key = isLittleEndian ? b : (b / 8) * 8 + (7 - (b % 8));
            if (key < bestKey)
            {
                bestKey = key;
                best = b;
            }
        }
        return best < 0 ? 0 : best;
    }

    /// <summary>
    /// Place <paramref name="follower"/> on the next bit after <paramref name="ahead"/>'s LSB.
    /// Motorola crosses the byte as bit0 → next byte bit7, not as numeric max+1.
    /// </summary>
    public static int StartBitRightAfter(DbcSignal ahead, DbcSignal follower)
    {
        var aheadBits = CollectBits(ahead).ToList();
        if (aheadBits.Count == 0)
            return Math.Max(0, ahead.StartBit);

        if (follower.IsLittleEndian)
            return MaxOccupiedBit(ahead) + 1;

        return Math.Max(0, StepBit(false, aheadBits[^1], +1));
    }

    /// <summary>Bit just outside the signal: Front = before MSB, Back = after LSB. Motorola walk.</summary>
    public static int StepOutside(DbcSignal sg, int delta)
    {
        var bits = CollectBits(sg).ToList();
        int edge;
        if (bits.Count == 0)
            edge = sg.StartBit;
        else if (sg.IsLittleEndian)
            edge = delta < 0 ? bits.Min() : bits.Max();
        else
            edge = delta < 0 ? bits[0] : bits[^1];

        return StepBit(sg.IsLittleEndian, edge, delta);
    }

    /// <summary>
    /// Nearest other signal toward the front, walking across empty bits.
    /// The bit immediately before the MSB may be a hole (XVel bit 72, numTxObjs bit 64).
    /// </summary>
    public static DbcSignal? NearestSignalTowardFront(IEnumerable<DbcSignal> signals, DbcSignal sg)
    {
        int bit = StepOutside(sg, -1);
        var seen = new HashSet<int>();
        for (int i = 0; i < 4096 && bit >= 0; i++)
        {
            if (!seen.Add(bit)) return null;
            var hit = SignalAtBit(signals, bit, sg);
            if (hit != null) return hit;
            int prev = StepBit(sg.IsLittleEndian, bit, -1);
            if (prev < 0 || prev == bit) return null;
            bit = prev;
        }
        return null;
    }

    /// <summary>
    /// Nearest other signal toward the back, walking across empty bits.
    /// MisAlign's next bit is 55, so the signal across that hole is numTxObjs, not Failure.
    /// </summary>
    public static DbcSignal? NearestSignalTowardBack(IEnumerable<DbcSignal> signals, DbcSignal sg)
    {
        int bit = StepOutside(sg, +1);
        var seen = new HashSet<int>();
        for (int i = 0; i < 4096 && bit >= 0 && bit < 65536; i++)
        {
            if (!seen.Add(bit)) return null;
            var hit = SignalAtBit(signals, bit, sg);
            if (hit != null) return hit;
            int next = StepBit(sg.IsLittleEndian, bit, +1);
            if (next < 0 || next == bit) return null;
            bit = next;
        }
        return null;
    }

    /// <summary>
    /// Hole To Back slide (numTxObjs at 55, Timestamp 87→79, numTxObjs takes 111–104).
    /// The bits just in front of <paramref name="next"/> must be free, and the tail must
    /// be at least <paramref name="sg"/> long. A shorter neighbor (MeasFlag behind XPos)
    /// does not fit this slide.
    /// </summary>
    public static bool TryHoleSlide(DbcSignal sg, DbcSignal next, IEnumerable<DbcSignal> signals,
        out int newStart, out int newNextStart)
    {
        int oldNext = next.StartBit;
        int oldLen = next.Length;
        newNextStart = WalkBits(next.IsLittleEndian, oldNext, -sg.Length);
        newStart = WalkBits(sg.IsLittleEndian, oldNext, oldLen - sg.Length);
        if (sg.Length <= 0 || oldLen < sg.Length)
            return false;

        var probeSg = new DbcSignal
        {
            StartBit = newStart,
            Length = sg.Length,
            IsLittleEndian = sg.IsLittleEndian
        };
        var probeNext = new DbcSignal
        {
            StartBit = newNextStart,
            Length = next.Length,
            IsLittleEndian = next.IsLittleEndian
        };
        if (Overlaps(probeSg, probeNext))
            return false;
        foreach (var other in signals)
        {
            if (other.Length <= 0 || ReferenceEquals(other, sg) || ReferenceEquals(other, next))
                continue;
            if (Overlaps(probeSg, other) || Overlaps(probeNext, other))
                return false;
        }
        return true;
    }

    /// <summary>Signal other than <paramref name="sg"/> that occupies <paramref name="absBit"/>.</summary>
    public static DbcSignal? SignalAtBit(IEnumerable<DbcSignal> signals, int absBit, DbcSignal sg)
    {
        if (absBit < 0) return null;
        foreach (var other in signals)
        {
            if (other.Length <= 0 || ReferenceEquals(other, sg)) continue;
            foreach (var b in CollectBits(other))
            {
                if (b == absBit) return other;
            }
        }
        return null;
    }

    /// <summary>
    /// Adjacent interchange. Raw StartBit exchange when the two do not overlap afterwards
    /// (numTxObjs ↔ MisAlign). If a raw exchange overlaps (Timestamp len 32 ↔ numTxObjs len 8),
    /// re-pack so <paramref name="first"/> is in front and <paramref name="second"/> follows.
    /// </summary>
    public static void InterchangeAdjacent(DbcSignal first, DbcSignal second)
    {
        var probeA = new DbcSignal
        {
            StartBit = second.StartBit,
            Length = first.Length,
            IsLittleEndian = first.IsLittleEndian
        };
        var probeB = new DbcSignal
        {
            StartBit = first.StartBit,
            Length = second.Length,
            IsLittleEndian = second.IsLittleEndian
        };
        if (!Overlaps(probeA, probeB))
        {
            int tmp = first.StartBit;
            first.StartBit = second.StartBit;
            second.StartBit = tmp;
            return;
        }
        RepackAdjacentPair(first, second);
    }

    /// <summary>
    /// Whole layout interchange (Vector To Front/Back when the next bit is occupied).
    /// <paramref name="first"/> starts at the earliest occupied bit of the pair in layout
    /// order and keeps its length. <paramref name="second"/> starts on the following bit.
    /// Numeric min/max is not the Motorola order: bit 24 is immediately before bit 39.
    /// </summary>
    public static void RepackAdjacentPair(DbcSignal first, DbcSignal second)
    {
        var bits = new List<int>();
        bits.AddRange(CollectBits(first));
        bits.AddRange(CollectBits(second));
        if (bits.Count == 0)
        {
            int tmp = first.StartBit;
            first.StartBit = second.StartBit;
            second.StartBit = tmp;
            return;
        }
        first.StartBit = EarliestBit(bits, first.IsLittleEndian);
        second.StartBit = StartBitRightAfter(first, second);
    }

    /// <summary>First other signal that shares any bit with <paramref name="sg"/>.</summary>
    public static DbcSignal? FindOverlapPeer(DbcSignal sg, IEnumerable<DbcSignal> signals,
        DbcSignal? skip = null)
    {
        foreach (var other in signals)
        {
            if (other.Length <= 0) continue;
            if (ReferenceEquals(other, sg) || ReferenceEquals(other, skip)) continue;
            if (Overlaps(sg, other)) return other;
        }
        return null;
    }

    /// <summary>Vector CANdb++ Invalid Offset body text.</summary>
    public static string FormatOverlapWarning(DbcMessage msg, DbcSignal peer) =>
        $"Overlapping with the message signal '{msg.Name} ({msg.IdHex})::{peer.Name}'!";
}
