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
    /// <c>[orders+1 ≡ R:5.3K P:60 S:60]</c>, the number in gray, the color this
    /// segment already gives what it is not showing (ADR-0052, amendment
    /// 2026-09-20). None beside is nothing on the line, as everything else here.
    /// </para>
    /// <para>
    /// <b>Every number on this line is per second</b>, and none of them says
    /// so: the owner, 2026-09-20, *the Xmip Prompt does not need /s spelled
    /// out*. Five letters each followed by <c>/s</c> is the unit written five
    /// times on a line whose whole purpose is to be short, and an operator
    /// learns a prompt's units once. <c>R:1.2K</c> is what the stage is moving
    /// now, <see cref="FigureFlow"/> between the last two publications, and
    /// not a total since the roll began (*the number does not mean anything
    /// over time*). A rate is bounded by throughput rather than by uptime, and
    /// <c>R:0</c> says stalled, which a rising total never can. One
    /// publication is no interval and therefore no rate, and that is the dash
    /// this segment already uses for a figure nobody published: *not known
    /// yet* must not read as *stalled*.
    /// </para>
    /// <para>
    /// <b>T and F are rates too</b>, on the same ruling — the owner named all
    /// five letters when he capped the numbers, and they were left as totals
    /// on the assistant's judgement rather than his. They are gated on the
    /// <em>total</em> and drawn as the <em>rate</em>: a run that has retried
    /// once carries T for as long as it rolls, and the number says whether the
    /// trouble is still arriving. Gating on the rate would put T on the line
    /// for one prompt and take it off again, which is how the owner came to
    /// say he had never seen either letter in a test that had both.
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
        // then the counts — [main ≡ +0 ~1 -0] there, [orders ≡ R:12 P:11 S:10]
        // here (the owner, 2026-09-18). The name wears the worst stage's color.
        // R, P and S are the stage letters and never a name's first letter:
        // what a cluster or a node is called says nothing about what it does.
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

        // Gated on the total, drawn as the rate. A run that has retried once is
        // a run with trouble in it and says so for as long as it rolls; the
        // number beside the letter says whether the trouble is still arriving.
        // Gating on the rate instead would flash T for one prompt and drop it,
        // which is how the owner came to say he had never seen either letter.
        if (figures.Retrying > 0)
        {
            parts.AddRange(Moving(" T", flow.Retrying, before?.Retrying, ConsoleColor.Yellow));
        }

        if (figures.Failed > 0)
        {
            parts.AddRange(Moving(" F", flow.Failed, before?.Failed, ConsoleColor.Red));
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
        // and the prompt is at the cluster then, not at round-trip.
        string[] common = [.. scopes[0].Take(shared)];
        int node = Array.IndexOf(common, "node");

        return node >= 0 && node + 1 < common.Length
            ? common[node + 1]
            : common.FirstOrDefault(part => part != "node") ?? string.Empty;
    }

    /// <summary>
    /// A rate short enough for a prompt: the same K, M and G ladder as a count
    /// and the same one decimal below ten of a unit, carried down to the unit
    /// itself — 0.3, 9.9, 240, 1.2K. A trickle is written 0.3 and never 0,
    /// because on this line <c>0</c> means stalled and nothing else may.
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

    /// <summary>A leaf's mood in the prompt's colors: the color the shared
    /// English names for it (<see cref="English.Color"/>, the one mood-to-color
    /// map, ADR-0041), painted by <see cref="Paint"/>. Gray when no leaf is
    /// there.</summary>
    public static ConsoleColor Traffic(HealthState? state)
    {
        return state is { } mood ? Paint(English.Color(mood)) : ConsoleColor.DarkGray;
    }

    /// <summary>
    /// The console color the prompt paints the estate's name for a mood's
    /// color in. The prompt sits beside posh-git and takes its palette
    /// (ADR-0052, amendments 2026-09-15): the cyan posh-git gives a branch in
    /// step for green (Fine); yellow for slate, blue and yellow (Paused,
    /// Working, Stressed); red for burnt, red and orange (Exhausted, Done,
    /// Holding); gray for muted, a mood this build does not know. The name
    /// decides, never the mood itself, so no mood is painted from a table of
    /// the prompt's own.
    /// </summary>
    public static ConsoleColor Paint(string color)
    {
        return color switch
        {
            "green" => ConsoleColor.Cyan,
            "slate" or "blue" or "yellow" => ConsoleColor.Yellow,
            "burnt" or "red" or "orange" => ConsoleColor.Red,
            _ => ConsoleColor.DarkGray,
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
    /// winding down — and no digit on this line says that, because 1.2K is
    /// 1.2K across a wide band of throughput. The rule that a number below a
    /// thousand is not painted holds for the same reason it always did: 238
    /// to 241 is visible in the digits themselves.
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
        // to present (the owner, 2026-09-18): R:1.2K reads as a figure, and
        // R1.2K read as a name. A rate nobody can compute keeps its dash.
        if (value is not { } rate)
        {
            return [new XmipPromptPart(letter + "–", ConsoleColor.DarkGray)];
        }

        return Trend(rate, before) is { } going
            ?
            [
                new XmipPromptPart(letter + ":", color),
                new XmipPromptPart(Rate(rate), going),
            ]
            : [new XmipPromptPart(letter + ":" + Rate(rate), color)];
    }
}
