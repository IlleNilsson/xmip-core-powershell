using System.Globalization;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// What the prompt segment says and in which colors, from one publication's
/// index and figures and, where there was one, the figures before it. Pure:
/// no surface, no clock, no state — <see cref="PromptMonitor"/> observes and
/// remembers, this renders. Apart since 2026-09-18, when the two together
/// reached the length a file may be.
/// </summary>
public static class SegmentRender
{
    /// <summary>
    /// The segment the way posh-git says a repository, and no wider: five
    /// figures with their letters, R, P and S for what the three stages count
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
    /// </summary>
    public static XmipPromptSegment Render(
        ScopeIndex index, Figures figures, Figures? before = null)
    {
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
            parts.Add(new XmipPromptPart(name + " ", worst));
        }

        if (square)
        {
            parts.Add(new XmipPromptPart("≡ ", ConsoleColor.Cyan));
        }

        parts.AddRange(Figure("R", figures.Streams, before?.Streams, Traffic(stages[0])));
        parts.AddRange(Figure(" P", figures.Journeys, before?.Journeys, Traffic(stages[1])));
        parts.AddRange(Figure(" S", figures.Messages, before?.Messages, Traffic(stages[2])));

        if (figures.Retrying > 0)
        {
            parts.AddRange(Figure(" T", figures.Retrying, before?.Retrying, ConsoleColor.Yellow));
        }

        if (figures.Failed > 0)
        {
            parts.AddRange(Figure(" F", figures.Failed, before?.Failed, ConsoleColor.Red));
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
        // and the prompt is at C1 then, not at pingpong.
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
    /// Which way a short count is going, as a color for its number: warmer
    /// where it rose since the last publication and hot where it rose by a
    /// tenth or more, cooler where it fell and icy where it fell by as much;
    /// null where it stands still, was not published before, or is below a
    /// thousand. A count written in K, M or G hides its own movement — 5.3K
    /// is 5.3K for a long while — so the color says what the digits cannot
    /// (the owner, 2026-09-18: paint it hotter or icier). The letter keeps
    /// the mood's color; only the number takes this one. Hot is magenta and
    /// not a red: on a console dark red and red read alike, and red is the
    /// mood's word for a stage that is done.
    /// </summary>
    public static ConsoleColor? Trend(ulong now, ulong? before)
    {
        if (now < 1000 || before is not { } then || then == now)
        {
            return null;
        }

        // A tenth, without multiplying: a count near the top of its type must not wrap.
        ulong moved = now > then ? now - then : then - now;
        bool fast = moved >= Math.Max(then / 10, 1);

        return now > then
            ? (fast ? ConsoleColor.Magenta : ConsoleColor.DarkYellow)
            : (fast ? ConsoleColor.Blue : ConsoleColor.DarkCyan);
    }

    /// <summary>One figure: its letter and its count, the count in its own
    /// part where it has a trend to show; a gray dash for none.</summary>
    private static XmipPromptPart[] Figure(
        string letter, ulong? value, ulong? before, ConsoleColor color)
    {
        // A colon between the letter and its number, where there is a number
        // to present (the owner, 2026-09-18): R:5,317 reads as a figure, and
        // R5,317 read as a name. A figure nobody published keeps its dash.
        if (value is not { } count)
        {
            return [new XmipPromptPart(letter + "–", ConsoleColor.DarkGray)];
        }

        return Trend(count, before) is { } going
            ? [new XmipPromptPart(letter + ":", color), new XmipPromptPart(Short(count), going)]
            : [new XmipPromptPart(letter + ":" + Short(count), color)];
    }
}
