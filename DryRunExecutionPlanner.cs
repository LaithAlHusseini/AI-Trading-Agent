internal sealed class DryRunExecutionPlanner
{
    private const double StrategyCapital =
        20.00;

    private const double RiskPerTradePercent =
        0.50;

    private const double MaxPositionPercent =
        25.00;

    private const double MinimumRiskRewardRatio =
        1.80;

    private const int MaximumPlannedPositions =
        3;

    public DryRunExecutionPlan BuildAndPrint(
        PaperTradingPreflightResult preflight,
        IEnumerable<TradeCandidate> candidates)
    {
        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " PAPER TRADING ENGINE V9B"
        );

        Console.WriteLine(
            " DRY-RUN EXECUTION PLANNER"
        );

        Console.WriteLine(
            "=============================================="
        );

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
            $"Max Planned Positions: {MaximumPlannedPositions}"
        );

        Console.WriteLine(
            $"Minimum Risk/Reward: 1:{MinimumRiskRewardRatio:F2}"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Preflight Safety Passed: " +
            $"{preflight.SafetyChecksPassed}"
        );

        Console.WriteLine(
            $"Market Open Right Now: " +
            $"{preflight.MarketOpen}"
        );

        Console.WriteLine(
            $"Real Order Submission Enabled: " +
            $"{preflight.OrderSubmissionEnabled}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "IMPORTANT: V9B IS PLANNING ONLY."
        );

        Console.WriteLine(
            "NO ORDER API IS CALLED BY THIS ENGINE."
        );

        var orderedCandidates =
            candidates
                .OrderByDescending(
                    candidate =>
                        candidate.FinalDecision ==
                        "PAPER TRADE CANDIDATE"
                )
                .ThenByDescending(
                    candidate =>
                        candidate.RiskRewardRatio
                )
                .ThenBy(
                    candidate =>
                        candidate.Symbol
                )
                .ToList();

        var plannedOrders =
            new List<DryRunPlannedOrder>();

        var totalPlannedPositionValue =
            0.0;

        var totalExpectedMaximumLoss =
            0.0;

        foreach (var candidate in orderedCandidates)
        {
            var decision =
                EvaluateCandidate(
                    preflight,
                    candidate,
                    plannedOrders.Count,
                    totalPlannedPositionValue
                );

            Console.WriteLine();

            Console.WriteLine(
                "----------------------------------------------"
            );

            Console.WriteLine(
                $"SYMBOL: {candidate.Symbol}"
            );

            Console.WriteLine(
                "STRATEGY: CURRENT_ANALYSIS_RULESET_V1"
            );

            Console.WriteLine(
                "REGIME: OBSERVE_ONLY_IN_V9B"
            );

            Console.WriteLine();

            Console.WriteLine(
                $"Entry:        ${candidate.EntryPrice:F2}"
            );

            Console.WriteLine(
                $"Stop Loss:    ${candidate.StopLoss:F2}"
            );

            Console.WriteLine(
                $"Take Profit:  ${candidate.TakeProfit:F2}"
            );

            Console.WriteLine();

            Console.WriteLine(
                $"Quantity:     {candidate.Quantity:F4} shares"
            );

            Console.WriteLine(
                $"Position $:   ${candidate.PositionValue:F2}"
            );

            Console.WriteLine(
                $"Max Risk:     ${candidate.ExpectedMaximumLoss:F2}"
            );

            Console.WriteLine(
                $"Risk/Reward:  1:{candidate.RiskRewardRatio:F2}"
            );

            Console.WriteLine();

            Console.WriteLine(
                $"DRY-RUN DECISION: {decision.Decision}"
            );

            Console.WriteLine(
                $"Reason: {decision.Reason}"
            );

            if (decision.WouldSubmit)
            {
                plannedOrders.Add(
                    new DryRunPlannedOrder(
                        candidate.Symbol,
                        candidate.EntryPrice,
                        candidate.StopLoss,
                        candidate.TakeProfit,
                        candidate.Quantity,
                        candidate.PositionValue,
                        candidate.ExpectedMaximumLoss,
                        candidate.RiskRewardRatio,
                        decision.Decision,
                        decision.Reason
                    )
                );

                totalPlannedPositionValue +=
                    candidate.PositionValue;

                totalExpectedMaximumLoss +=
                    candidate.ExpectedMaximumLoss;
            }
        }

        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " V9B DRY-RUN SUMMARY"
        );

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            $"Candidates Evaluated: {orderedCandidates.Count}"
        );

        Console.WriteLine(
            $"Would Submit: {plannedOrders.Count}"
        );

        Console.WriteLine(
            $"Rejected: " +
            $"{orderedCandidates.Count - plannedOrders.Count}"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Planned Position Value: " +
            $"${totalPlannedPositionValue:F2}"
        );

        Console.WriteLine(
            $"Planned Capital Usage: " +
            $"{(
                totalPlannedPositionValue /
                StrategyCapital *
                100.0
            ):F2}%"
        );

        Console.WriteLine(
            $"Combined Expected Max Loss: " +
            $"${totalExpectedMaximumLoss:F2}"
        );

        Console.WriteLine();

        if (!preflight.MarketOpen)
        {
            Console.WriteLine(
                "Execution timing note: MARKET IS CLOSED."
            );

            Console.WriteLine(
                "WOULD SUBMIT means structurally eligible for a future"
            );

            Console.WriteLine(
                "paper execution cycle when the market is open."
            );
        }

        Console.WriteLine();

        Console.WriteLine(
            "REAL ORDER SUBMISSION REMAINS DISABLED."
        );

        Console.WriteLine(
            "NO ORDERS WERE SUBMITTED."
        );

        return new DryRunExecutionPlan(
            orderedCandidates.Count,
            plannedOrders,
            totalPlannedPositionValue,
            totalExpectedMaximumLoss,
            preflight.MarketOpen,
            preflight.SafetyChecksPassed,
            preflight.OrderSubmissionEnabled
        );
    }

    private static DryRunDecision EvaluateCandidate(
        PaperTradingPreflightResult preflight,
        TradeCandidate candidate,
        int alreadyPlannedPositions,
        double alreadyPlannedPositionValue)
    {
        if (!preflight.SafetyChecksPassed)
        {
            return Reject(
                "Paper account preflight safety checks did not pass."
            );
        }

        if (candidate.FinalDecision !=
            "PAPER TRADE CANDIDATE")
        {
            return Reject(
                candidate.Reason
            );
        }

        if (candidate.EntryPrice <= 0)
        {
            return Reject(
                "Entry price is invalid."
            );
        }

        if (candidate.StopLoss <= 0 ||
            candidate.StopLoss >=
            candidate.EntryPrice)
        {
            return Reject(
                "Stop-loss geometry is invalid."
            );
        }

        if (candidate.TakeProfit <=
            candidate.EntryPrice)
        {
            return Reject(
                "Take-profit geometry is invalid."
            );
        }

        if (candidate.Quantity <= 0)
        {
            return Reject(
                "Calculated quantity is not positive."
            );
        }

        if (candidate.RiskPerShare <= 0)
        {
            return Reject(
                "Risk per share is invalid."
            );
        }

        if (candidate.RiskRewardRatio <
            MinimumRiskRewardRatio)
        {
            return Reject(
                $"Risk/reward ratio " +
                $"{candidate.RiskRewardRatio:F2} " +
                $"is below {MinimumRiskRewardRatio:F2}."
            );
        }

        var maximumRiskDollars =
            StrategyCapital *
            (
                RiskPerTradePercent /
                100.0
            );

        if (candidate.ExpectedMaximumLoss >
            maximumRiskDollars +
            0.000001)
        {
            return Reject(
                $"Expected max loss " +
                $"${candidate.ExpectedMaximumLoss:F4} " +
                $"exceeds the per-trade cap " +
                $"${maximumRiskDollars:F4}."
            );
        }

        var maximumPositionDollars =
            StrategyCapital *
            (
                MaxPositionPercent /
                100.0
            );

        if (candidate.PositionValue >
            maximumPositionDollars +
            0.000001)
        {
            return Reject(
                $"Position value " +
                $"${candidate.PositionValue:F2} " +
                $"exceeds the per-position cap " +
                $"${maximumPositionDollars:F2}."
            );
        }

        if (alreadyPlannedPositions >=
            MaximumPlannedPositions)
        {
            return Reject(
                $"Maximum planned positions " +
                $"({MaximumPlannedPositions}) already reached."
            );
        }

        if (alreadyPlannedPositionValue +
            candidate.PositionValue >
            StrategyCapital +
            0.000001)
        {
            return Reject(
                "Combined planned position value would exceed " +
                "the strategy capital cap."
            );
        }

        return new DryRunDecision(
            true,
            "WOULD SUBMIT",
            preflight.MarketOpen
                ? "Passed V9B dry-run execution checks."
                : "Passed V9B checks; market is closed, so this is " +
                  "a future-paper-execution plan only."
        );
    }

    private static DryRunDecision Reject(
        string reason)
    {
        return new DryRunDecision(
            false,
            "REJECTED",
            reason
        );
    }
}

internal sealed record DryRunDecision(
    bool WouldSubmit,
    string Decision,
    string Reason
);

internal sealed record DryRunPlannedOrder(
    string Symbol,
    double EntryPrice,
    double StopLoss,
    double TakeProfit,
    double Quantity,
    double PositionValue,
    double ExpectedMaximumLoss,
    double RiskRewardRatio,
    string Decision,
    string Reason
);

internal sealed record DryRunExecutionPlan(
    int CandidatesEvaluated,
    IReadOnlyList<DryRunPlannedOrder> PlannedOrders,
    double TotalPlannedPositionValue,
    double TotalExpectedMaximumLoss,
    bool MarketOpen,
    bool SafetyChecksPassed,
    bool RealOrderSubmissionEnabled
);
