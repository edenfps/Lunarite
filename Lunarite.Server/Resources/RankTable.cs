using System;

namespace Lunarite.Resources;

// Military rank (ENUM_RANK 0..51, HuxleyPlayerWrapper.uc) from cumulative city medal donations.
// Thresholds: UIText.int [UIMMOSystem_CityMedalDonationScene] SoldierRankList/SergentRankList/
// OfficerRankList, shown by UIActiveHelp.RankList as "A / B" (A = Battle Medal 609001,
// B = Medal of Valor 609101). Each named rank lists its first grade; grades step evenly.
// Promotion requires max level (Dialogue.int City_Medal; "Check level 30" judicator kismet).
// See analysis/audit/rank-rules.md.
public sealed class RankTable
{
    public const int MaxRank = 51;
    public const int RequiredLevel = 30;

    public readonly record struct Requirement(int BattleMedals, int MedalsOfValor);

    // (first ordinal, grade count, first A, A step, first B, B step) per named rank.
    private static readonly (int First, int Grades, int A, int AStep, int B, int BStep)[] Groups =
    [
        (0, 1, 0, 0, 0, 0),          // Recruit
        (1, 3, 10, 10, 0, 0),        // Private 3rd..1st
        (4, 3, 45, 15, 0, 0),        // Private First Class
        (7, 3, 95, 20, 0, 0),        // Corporal
        (10, 3, 160, 25, 0, 0),      // Sergeant
        (13, 5, 245, 35, 0, 0),      // Staff Sergeant
        (18, 5, 430, 45, 0, 0),      // Sergeant First Class
        (23, 5, 665, 55, 0, 0),      // Master Sergeant
        (28, 8, 955, 70, 2, 2),      // Second Lieutenant
        (36, 8, 1540, 95, 21, 5),    // First Lieutenant
        (44, 8, 2310, 105, 64, 8),   // Captain
    ];

    private readonly Requirement[] Requirements;

    public RankTable()
    {
        Requirements = Build();
    }

    private Requirement[] Build()
    {
        var table = new Requirement[MaxRank + 1];
        foreach (var g in Groups)
            for (int i = 0; i < g.Grades; i++)
                table[g.First + i] = new Requirement(g.A + i * g.AStep, g.B + i * g.BStep);
        return table;
    }

    public Requirement For(int rank) => Requirements[Math.Clamp(rank, 0, MaxRank)];

    // Highest rank whose A and B totals are both met.
    public byte RankFor(int battleMedalsDonated, int medalsOfValorDonated)
    {
        byte rank = 0;
        for (int candidate = 1; candidate <= MaxRank; candidate++)
            if (battleMedalsDonated >= Requirements[candidate].BattleMedals
                && medalsOfValorDonated >= Requirements[candidate].MedalsOfValor)
                rank = (byte)candidate;
        return rank;
    }

    // First ordinal of each named rank: crossing into one is EVENT_GROUP_UP ("rank group up").
    public static bool IsGroupStart(int rank)
    {
        foreach (var g in Groups) if (g.First == rank && rank > 0) return true;
        return false;
    }

    public static int GroupStartsCrossed(int fromRank, int toRank)
    {
        int count = 0;
        for (int rank = fromRank + 1; rank <= toRank; rank++) if (IsGroupStart(rank)) count++;
        return count;
    }
}
