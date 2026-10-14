public sealed record FrozenForwardPolicy(
    string PolicyVersion,
    DateTime FrozenAtUtc,
    string StrategyName,
    RegimeGateMode RegimeGate,
    double MinimumStopDistanceAtr,
    double MaximumStopDistanceAtr,
    double StrategyCapital,
    double RiskPerTradePercent,
    double MaxPositionPercent,
    int MaxOpenPositions,
    double MinimumRiskRewardRatio,
    bool PaperOrderSubmissionEnabled,
    bool LiveTradingEnabled)
{
    public static FrozenForwardPolicy V9C { get; } =
        new FrozenForwardPolicy(
            PolicyVersion:
                "V9C-2026-10-14",

            FrozenAtUtc:
                new DateTime(
                    2026,
                    10,
                    14,
                    0,
                    0,
                    0,
                    DateTimeKind.Utc
                ),

            StrategyName:
                "STOP_1_5_TO_2_ATR",

            RegimeGate:
                RegimeGateMode.BullOnly,

            MinimumStopDistanceAtr:
                1.50,

            MaximumStopDistanceAtr:
                2.00,

            StrategyCapital:
                20.00,

            RiskPerTradePercent:
                0.50,

            MaxPositionPercent:
                25.00,

            MaxOpenPositions:
                3,

            MinimumRiskRewardRatio:
                1.80,

            // V9C freezes the policy only.
            // Paper order submission stays disabled until
            // the dry-run planner is fully policy-aware and verified.
            PaperOrderSubmissionEnabled:
                false,

            // Live trading is explicitly outside the current phase.
            LiveTradingEnabled:
                false
        );

    public bool AllowsRegime(
        string trendRegime,
        string volatilityRegime)
    {
        return RegimeGate switch
        {
            RegimeGateMode.None =>
                true,

            RegimeGateMode.BullOnly =>
                string.Equals(
                    trendRegime,
                    "BULL_TREND",
                    StringComparison.OrdinalIgnoreCase
                ),

            RegimeGateMode.LowVolOnly =>
                string.Equals(
                    volatilityRegime,
                    "LOW_VOL",
                    StringComparison.OrdinalIgnoreCase
                ),

            RegimeGateMode.BullAndLowVol =>
                string.Equals(
                    trendRegime,
                    "BULL_TREND",
                    StringComparison.OrdinalIgnoreCase
                ) &&
                string.Equals(
                    volatilityRegime,
                    "LOW_VOL",
                    StringComparison.OrdinalIgnoreCase
                ),

            _ =>
                false
        };
    }

    public bool AllowsStopDistance(
        double atr,
        double entryPrice,
        double stopLoss)
    {
        if (atr <= 0 ||
            entryPrice <= 0 ||
            stopLoss <= 0 ||
            stopLoss >= entryPrice)
        {
            return false;
        }

        var stopDistanceAtr =
            (
                entryPrice -
                stopLoss
            ) /
            atr;

        return
            stopDistanceAtr >=
            MinimumStopDistanceAtr &&
            stopDistanceAtr <=
            MaximumStopDistanceAtr;
    }

    public void Print()
    {
        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " FROZEN FORWARD POLICY V9C"
        );

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            $"Policy Version: {PolicyVersion}"
        );

        Console.WriteLine(
            $"Frozen At UTC: " +
            $"{FrozenAtUtc:yyyy-MM-dd HH:mm:ss}"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Strategy: {StrategyName}"
        );

        Console.WriteLine(
            $"Regime Gate: {RegimeGate}"
        );

        Console.WriteLine(
            $"Stop Distance: " +
            $"{MinimumStopDistanceAtr:F2} - " +
            $"{MaximumStopDistanceAtr:F2} ATR"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Strategy Capital: ${StrategyCapital:F2}"
        );

        Console.WriteLine(
            $"Risk Per Trade: {RiskPerTradePercent:F2}%"
        );

        Console.WriteLine(
            $"Max Position: {MaxPositionPercent:F2}%"
        );

        Console.WriteLine(
            $"Max Open Positions: {MaxOpenPositions}"
        );

        Console.WriteLine(
            $"Minimum Risk/Reward: " +
            $"1:{MinimumRiskRewardRatio:F2}"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Paper Order Submission Enabled: " +
            $"{PaperOrderSubmissionEnabled}"
        );

        Console.WriteLine(
            $"Live Trading Enabled: " +
            $"{LiveTradingEnabled}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "POLICY STATUS: FROZEN FOR FORWARD VALIDATION"
        );

        Console.WriteLine(
            "Historical research must not modify this policy " +
            "during the validation period."
        );
    }
}
