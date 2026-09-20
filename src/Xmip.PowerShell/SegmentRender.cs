using System.Globalization;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// What the prompt segment says and in which colors, from one publication's
/// index, its figures and the rate it is moving at. Pure: no surface, no
/// clock, no state — <see cref="PromptMonitor"/> observes and remembers, this
/// renders. Apart since 2026-09-18, when the two together reached the length a
/// file may be.
/// </summary>
public static class SegmentRender
{
    /// <summary>
    /// The segment the way posh-git says a repository, and no wider: five
    /// figures with their letters, R, P and S for what the three stages move
    /// (<see cref="ScopeTree.CountedAt"/>: Streams, Journeys, Messages), T for
    /// Retrying, F for Failed. No mood is spelled out; the color carries it —
    /// a stage letter is green, yellow or red by the worst leaf on that stage,
    /// T is yellow and F red. An unpublished stage figure is a dash, never a
    /// zero (ADR-0052, amendment 2026-09-15, the owner's second word). T and F
    /// are there only when there is something retrying or failed: the owner,
    /// 2026-09-18, on <c>[R5,317 P60 S60 T– F–]</c> — they do not need to be
    /// there if there are none, as posh-git shows no count it has nothing for.
    /// And it wears posh-git's clothes (the owner, the same evening: *I like
    /// the posh-git style better*): yellow brackets, the cyan posh-git gives
    /// a branch that is in step with its remote for a stage that is fine, and
    /// posh-git's own ≡ at the end when every stage is fine and nothing is
    /// retrying or failed — the cluster is square, as the branch is.
    /// <para>
    /// <paramref name="beside"/> is how many clusters are rolling that this
    /// prompt is not following. The prompt reads one publication — a mood or a
    /// sum over two clusters would be at a scope in neither tree — so where
    /// there are more it says so rather than reading as the whole estate:
    /// <c>[C2+1 ≡ R:5.3K P:60 S:60]</c>, the count in gray, the color this
    /// segment already gives what it is not showing (ADR-0052, amendment
    /// 2026-09-20). None beside is nothing on the line, as everything else here.
    /// </para>
    /// <para>
    /// R, P and S are a rate — <c>R:1.2K/s</c>, what the stage is moving now,
    /// <see cref="FigureFlow"/> between the last two publications — and not a
    /// total since the roll began (the owner, 2026-09-20: *the number does not
    /// mean anything over time*). A rate is bounded by throughput rather than
    /// by uptime, and <c>R:0/s</c> says stalled, which a rising total never
    /// can. One publication is no interval and therefore no rate, and that is
    /// the dash this segment already uses for a figure nobody published:
    /// *not known yet* must not read as *stalled*. T and F stay counts —
    /// a retry total and a failure total mean something and should be small —
    /// and keep the rule they had, on the line only when above zero.
    /// </para>
    /// </summary>
    public static XmipPromptSegment Render(
        ScopeIndex index,
        Figures figures,
        FigureFlow flow,
        FigureFlow? before = null,
        int beside = 0)
    {
        ArgumentNullException.ThrowIfNull(figures);
        ArgumentNullException.ThrowIfNull(flow);

        HealthState?[] stages =
        [
            index.WorstAtStage("receive")?.State,
            index.WorstAtStage("process")?.State,
            index.WorstAtStage("send")?.State,
        ];
        bool square = stages.Any(stage => stage is not null)
            && stages.All(stage => stage is null or HealthState.Fine)
            && figures.Retrying is null or 0
            && figures.Failed is null or 0;

        // posh-git's order: what the prompt is at, its sign when square,
        // then the counts — [main ≡ +0 ~1 -0] there, [R1 ≡ R:12 P:11 S:10]
        // here (the owner, 2026-09-18). The name wears the worst stage's color.
        List<XmipPromptPart> parts = [new XmipPromptPart("[", ConsoleColor.Yellow)];
        string name = At(index);

        if (name.Length > 0)
        {
            ConsoleColor worst = stages.Select(Traffic).MaxBy(Rank);
            parts.Add(new XmipPromptPart(beside > 0 ? name : name + " ", worst));
        }

        // What this segment is not showing, in the gray it gives everything
        // it has no figure for: the name is the cluster in the prompt, and
        // +1 says there is another the operator is not looking at.
        if (beside > 0)
        {
            parts.Add(new XmipPromptPart(
                "+" + beside.ToString(CultureInfo.InvariantCulture) + " ", ConsoleColor.DarkGray));
        }

        if (square)
        {
            parts.Add(new XmipPromptPart("≡ ", ConsoleColor.Cyan));
        }

        parts.AddRange(Moving("R", flow.Streams, before?.Streams, Traffic(stages[0])));
        parts.AddRange(Moving(" P", flow.Journeys, before?.Journeys, Traffic(stages[1])));
        parts.AddRange(Moving(" S", flow.Messages, before?.Messages, Traffic(stages[2])));

        if (figures.Retrying > 0)
        {
            parts.AddRange(Figure(" T", figures.Retrying, ConsoleColor.Yellow));
        }

        if (figures.Failed > 0)
        {
            parts.AddRange(Figure(" F", figures.Failed, ConsoleColor.Red));
        }

        parts.Add(new XmipPromptPart("]", ConsoleColor.Yellow));

        return new XmipPromptSegment([.. parts]);
    }

    /// <summary>
    /// What the prompt is at, from what every published scope shares: the
    /// node where they share one, else the cluster, the first name. Nothing
    /// shared is no name. A segment that is only a kind, such as the
    /// Playground's <c>node</c>, names nothing; and what lies below the node
    /// or the cluster, a scenario or a stage, is never where the prompt is.
    /// </summary>
    public static string At(ScopeIndex index)
    {
        string[][] scopes =
        [
            .. index.Health(ScopeTree.Root).Select(record => ScopeTree.Parts(record.Scope)),
        ];

        if (scopes.Length == 0)
        {
            return string.Empty;
        }

        int shared = 0;

        // A leaf's own name is not where the prompt is, so stop one short.
        while (scopes.All(parts => parts.Length > shared + 1 && parts[shared] == scopes[0][shared]))
        {
            shared++;
        }

        // A node where one node is shared, else the cluster: the first name.
        // Never what lies deeper. A roll of one test shares its scenario too,
        // and the prompt is at C1 then, not at round-trip.
        string[] common = [.. scopes[0].Take(shared)];
        int node = Array.IndexOf(common, "node");

        return node >= 0 && node + 1 < common.Length
            ? common[node + 1]
            : common.FirstOrDefault(part => part != "node") ?? string.Empty;
    }

    /// <summary>
    /// A count short enough for a prompt: as it is below a thousand, then in
    /// K, M and G with one decimal below ten of a unit — 5.3K, 53K, 532K,
    /// 1.2M. Xmip counts past what an integer holds, and a line that grows
    /// with its numbers goes wild (the owner, 2026-09-18).
    /// </summary>
    public static string Short(ulong count)
    {
        if (count < 1000)
        {
            return count.ToString(CultureInfo.InvariantCulture);
        }

        string[] units = ["K", "M", "G"];
        double value = count;
        int unit = -1;

        // 999,950 is 1M and not 1000K: what would round up to a thousand moves on.
        while (unit < units.Length - 1 && value >= 999.5)
        {
            value /= 1000;
            unit++;
        }

        string format = value < 9.95 ? "0.#" : "0";

        return value.ToString(format, CultureInfo.InvariantCulture) + units[unit];
    }

    /// <summary>
    /// A rate short enough for a prompt: the same K, M and G ladder as a count
    /// and the same one decimal below ten of a unit, carried down to the unit
    /// itself — 0.3, 9.9, 240, 1.2K. A trickle is written 0.3 and never 0,
    /// because on this line <c>0/s</c> means stalled and nothing else may.
    /// </summary>
    public static string Rate(double perSecond)
    {
        string[] units = ["K", "M", "G"];
        double value = perSecond;
        int unit = -1;

        while (unit < units.Length - 1 && value >= 999.5)
        {
            value /= 1000;
            unit++;
        }

        string format = value < 9.95 ? "0.#" : "0";

        return value.ToString(format, CultureInfo.InvariantCulture)
            + (unit < 0 ? string.Empty : units[unit]);
    }

    // Trouble outranks calm when the name takes the worst stage's color.
    private static int Rank(ConsoleColor color)
    {
        return color switch
        {
            ConsoleColor.Red => 3,
            ConsoleColor.Yellow => 2,
            ConsoleColor.Cyan => 1,
            _ => 0,
        };
    }

    /// <summary>Cyan, yellow or red for a leaf's mood (ADR-0041): Fine is
    /// green; Paused, Working and Stressed are yellow; Exhausted and Done are
    /// red. Gray when no leaf is there.</summary>
    public static ConsoleColor Traffic(HealthState? state)
    {
        return state switch
        {
            null => ConsoleColor.DarkGray,
            HealthState.Fine => ConsoleColor.Cyan,
            HealthState.Paused or HealthState.Working or HealthState.Stressed
                => ConsoleColor.Yellow,
            _ => ConsoleColor.Red,
        };
    }

    /// <summary>
    /// Which way a number is going, as a color for it: warmer where it rose
    /// since the last publication and hot where it rose by a tenth or more,
    /// cooler where it fell and icy where it fell by as much; null where it
    /// stands still, was not there before, or is below a thousand. A number
    /// written in K, M or G hides its own movement — 5.3K is 5.3K for a long
    /// while — so the color says what the digits cannot (the owner,
    /// 2026-09-18: paint it hotter or icier). The letter keeps the mood's
    /// color; only the number takes this one. Hot is magenta and not a red: on
    /// a console dark red and red read alike, and red is the mood's word for a
    /// stage that is done.
    /// <para>
    /// <b>It still earns its place now that the number is a rate</b>, and the
    /// judgement is stated rather than assumed (the owner, 2026-09-20). It
    /// answers a different question than it did: over a total it said *is
    /// anything moving*, which the rate now says outright; over a rate it says
    /// *is the rate climbing or falling* — whether a cluster is speeding up or
    /// winding down — and no digit on this line says that, because 1.2K/s is
    /// 1.2K/s across a wide band of throughput. The rule that a number below a
    /// thousand is not painted holds for the same reason it always did: 238/s
    /// to 241/s is visible in the digits themselves.
    /// </para>
    /// </summary>
    public static ConsoleColor? Trend(double now, double? before)
    {
        if (now < 1000 || before is not { } then || then == now)
        {
            return null;
        }

        double moved = Math.Abs(now - then);
        bool fast = moved >= Math.Max(then / 10, 1);

        return now > then
            ? (fast ? ConsoleColor.Magenta : ConsoleColor.DarkYellow)
            : (fast ? ConsoleColor.Blue : ConsoleColor.DarkCyan);
    }

    /// <summary>One rate: its letter and what the stage is moving per second,
    /// the number in its own part where it has a trend to show; a gray dash
    /// where there is no interval to divide by, which is not a stall.</summary>
    private static XmipPromptPart[] Moving(
        string letter, double? value, double? before, ConsoleColor color)
    {
        // A colon between the letter and its number, where there is a number
        // to present (the owner, 2026-09-18): R:1.2K/s reads as a figure, and
        // R1.2K/s read as a name. A rate nobody can compute keeps its dash.
        if (value is not { } rate)
        {
            return [new XmipPromptPart(letter + "–", ConsoleColor.DarkGray)];
        }

        return Trend(rate, before) is { } going
            ?
            [
                new XmipPromptPart(letter + ":", color),
                new XmipPromptPart(Rate(rate), going),
                new XmipPromptPart("/s", color),
            ]
            : [new XmipPromptPart(letter + ":" + Rate(rate) + "/s", color)];
    }

    /// <summary>One count: its letter and its value. T and F are totals that
    /// mean something and should be small, and <see cref="Trend"/> paints
    /// nothing below a thousand, so they carry no trend — a promise never kept
    /// is not kept here either.</summary>
    private static XmipPromptPart[] Figure(string letter, ulong? value, ConsoleColor color)
    {
        return value is { } count
            ? [new XmipPromptPart(letter + ":" + Short(count), color)]
            : [new XmipPromptPart(letter + "–", ConsoleColor.DarkGray)];
    }
}
