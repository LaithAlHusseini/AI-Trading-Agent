internal sealed class DryRunExecutionPlanner
{
    private readonly FrozenForwardPolicy
        _policy;

    private readonly MarketRegimeSnapshot?
        _currentRegime;

    // Kept temporarily so Program.cs continues to compile
    // until V9C is wired into it in the next step.
    public DryRunExecutionPlanner()
        : this(
            FrozenForwardPolicy.V9C,
            null
        )
    {
    }

    public DryRunExecutionPlanner(
        FrozenForwardPolicy policy,
        MarketRegimeSnapshot? currentRegime)
    {
        _policy =
            policy;

        _currentRegime =
            currentRegime;
    }

    public DryRunExecutionPlan BuildAndPrint(
        PaperTradingPreflightResult preflight,
        IEnumerable<TradeCandidate> candidates)
    {
        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " PAPER TRADING ENGINE V9C"
        );

        Console.WriteLine(
            " FROZEN-POLICY DRY-RUN PLANNER"
        );

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            $"Policy Version: {_policy.PolicyVersion}"
        );

        Console.WriteLine(
            $"Strategy: {_policy.StrategyName}"
        );

        Console.WriteLine(
            $"Regime Gate: {_policy.RegimeGate}"
        );

        Console.WriteLine(
            $"Strategy Capital: " +
            $"${_policy.StrategyCapital:F2}"
        );

        Console.WriteLine(
            $"Risk Per Trade: " +
            $"{_policy.RiskPerTradePercent:F2}%"
        );

        Console.WriteLine(
            $"Max Position: " +
            $"{_policy.MaxPositionPercent:F2}%"
        );

        Console.WriteLine(
            $"Max Planned Positions: " +
            $"{_policy.MaxOpenPositions}"
        );

        Console.WriteLine(
            $"Minimum Risk/Reward: " +
            $"1:{_policy.MinimumRiskRewardRatio:F2}"
        );

        Console.WriteLine();

        if (_currentRegime is null)
        {
            Console.WriteLine(
                "Current Regime: UNAVAILABLE"
            );
        }
        else
        {
            Console.WriteLine(
                $"Regime Source Date: " +
                $"{_currentRegime.Date:yyyy-MM-dd}"
            );

            Console.WriteLine(
                $"Trend Regime: " +
                $"{_currentRegime.TrendRegime}"
            );

            Console.WriteLine(
                $"Volatility Regime: " +
                $"{_currentRegime.VolatilityRegime}"
            );

            Console.WriteLine(
                $"SPY Close: " +
                $"${_currentRegime.Close:F2}"
            );
        }

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
            $"Paper Order Submission Enabled: " +
            $"{_policy.PaperOrderSubmissionEnabled}"
        );

        Console.WriteLine(
            $"Live Trading Enabled: " +
            $"{_policy.LiveTradingEnabled}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "IMPORTANT: V9C IS STILL DRY-RUN ONLY."
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

            var stopDistanceAtr =
                CalculateStopDistanceAtr(
                    candidate
                );

            Console.WriteLine();

            Console.WriteLine(
                "----------------------------------------------"
            );

            Console.WriteLine(
                $"SYMBOL: {candidate.Symbol}"
            );

            Console.WriteLine(
                $"POLICY: {_policy.PolicyVersion}"
            );

            Console.WriteLine(
                $"STRATEGY: {_policy.StrategyName}"
            );

            Console.WriteLine(
                $"REGIME GATE: {_policy.RegimeGate}"
            );

            if (_currentRegime is not null)
            {
                Console.WriteLine(
                    $"CURRENT REGIME: " +
                    $"{_currentRegime.TrendRegime} | " +
                    $"{_currentRegime.VolatilityRegime}"
                );
            }
            else
            {
                Console.WriteLine(
                    "CURRENT REGIME: UNAVAILABLE"
                );
            }

            Console.WriteLine();

            Console.WriteLine(
                $"Entry:        " +
                $"${candidate.EntryPrice:F2}"
            );

            Console.WriteLine(
                $"ATR(14):      " +
                $"${candidate.Atr:F2}"
            );

            Console.WriteLine(
                $"Stop Loss:    " +
                $"${candidate.StopLoss:F2}"
            );

            Console.WriteLine(
                $"Stop Distance:" +
                $" {FormatStopDistance(stopDistanceAtr)} ATR"
            );

            Console.WriteLine(
                $"Take Profit:  " +
                $"${candidate.TakeProfit:F2}"
            );

            Console.WriteLine();

            Console.WriteLine(
                $"Quantity:     " +
                $"{candidate.Quantity:F4} shares"
            );

            Console.WriteLine(
                $"Position $:   " +
                $"${candidate.PositionValue:F2}"
            );

            Console.WriteLine(
                $"Max Risk:     " +
                $"${candidate.ExpectedMaximumLoss:F2}"
            );

            Console.WriteLine(
                $"Risk/Reward:  " +
                $"1:{candidate.RiskRewardRatio:F2}"
            );

            Console.WriteLine();

            Console.WriteLine(
                $"DRY-RUN DECISION: " +
                $"{decision.Decision}"
            );

            Console.WriteLine(
                $"Reason: {decision.Reason}"
            );

            if (decision.WouldSubmit)
            {
                plannedOrders.Add(
                    new DryRunPlannedOrder(
                        candidate.Symbol,
                        _policy.PolicyVersion,
                        _policy.StrategyName,
                        _policy.RegimeGate.ToString(),
                        _currentRegime?.TrendRegime ??
                        "UNAVAILABLE",
                        _currentRegime?.VolatilityRegime ??
                        "UNAVAILABLE",
                        candidate.EntryPrice,
                        candidate.Atr,
                        candidate.StopLoss,
                        stopDistanceAtr,
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
            " V9C FROZEN-POLICY DRY-RUN SUMMARY"
        );

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            $"Candidates Evaluated: " +
            $"{orderedCandidates.Count}"
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

        var capitalUsagePercent =
            _policy.StrategyCapital > 0
                ? totalPlannedPositionValue /
                  _policy.StrategyCapital *
                  100.0
                : 0;

        Console.WriteLine(
            $"Planned Capital Usage: " +
            $"{capitalUsagePercent:F2}%"
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
            "REAL PAPER ORDER SUBMISSION REMAINS DISABLED."
        );

        Console.WriteLine(
            "LIVE TRADING REMAINS DISABLED."
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
            _policy.PaperOrderSubmissionEnabled,
            _policy.PolicyVersion,
            _currentRegime?.TrendRegime,
            _currentRegime?.VolatilityRegime
        );
    }

    private DryRunDecision EvaluateCandidate(
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

        if (_currentRegime is null)
        {
            return Reject(
                "Current completed-bar market regime is unavailable."
            );
        }

        if (!_policy.AllowsRegime(
                _currentRegime.TrendRegime,
                _currentRegime.VolatilityRegime
            ))
        {
            return Reject(
                $"Frozen regime gate {_policy.RegimeGate} " +
                $"does not allow current regime " +
                $"{_currentRegime.TrendRegime} | " +
                $"{_currentRegime.VolatilityRegime}."
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

        if (candidate.Atr <= 0)
        {
            return Reject(
                "ATR is unavailable or invalid."
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

        if (!_policy.AllowsStopDistance(
                candidate.Atr,
                candidate.EntryPrice,
                candidate.StopLoss
            ))
        {
            var stopDistanceAtr =
                CalculateStopDistanceAtr(
                    candidate
                );

            return Reject(
                $"Stop distance " +
                $"{FormatStopDistance(stopDistanceAtr)} ATR " +
                $"is outside frozen strategy range " +
                $"{_policy.MinimumStopDistanceAtr:F2}-" +
                $"{_policy.MaximumStopDistanceAtr:F2} ATR."
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
            _policy.MinimumRiskRewardRatio)
        {
            return Reject(
                $"Risk/reward ratio " +
                $"{candidate.RiskRewardRatio:F2} " +
                $"is below " +
                $"{_policy.MinimumRiskRewardRatio:F2}."
            );
        }

        var maximumRiskDollars =
            _policy.StrategyCapital *
            (
                _policy.RiskPerTradePercent /
                100.0
            );

        if (candidate.ExpectedMaximumLoss >
            maximumRiskDollars +
            0.000001)
        {
            return Reject(
                $"Expected max loss " +
                $"${candidate.ExpectedMaximumLoss:F4} " +
                $"exceeds the frozen per-trade cap " +
                $"${maximumRiskDollars:F4}."
            );
        }

        var maximumPositionDollars =
            _policy.StrategyCapital *
            (
                _policy.MaxPositionPercent /
                100.0
            );

        if (candidate.PositionValue >
            maximumPositionDollars +
            0.000001)
        {
            return Reject(
                $"Position value " +
                $"${candidate.PositionValue:F2} " +
                $"exceeds the frozen per-position cap " +
                $"${maximumPositionDollars:F2}."
            );
        }

        if (alreadyPlannedPositions >=
            _policy.MaxOpenPositions)
        {
            return Reject(
                $"Frozen maximum planned positions " +
                $"({_policy.MaxOpenPositions}) already reached."
            );
        }

        if (alreadyPlannedPositionValue +
            candidate.PositionValue >
            _policy.StrategyCapital +
            0.000001)
        {
            return Reject(
                "Combined planned position value would exceed " +
                "the frozen strategy capital cap."
            );
        }

        return new DryRunDecision(
            true,
            "WOULD SUBMIT",
            preflight.MarketOpen
                ? "Passed frozen-policy V9C dry-run checks."
                : "Passed frozen-policy V9C checks; market is closed, " +
                  "so this is a future-paper-execution plan only."
        );
    }

    private static double CalculateStopDistanceAtr(
        TradeCandidate candidate)
    {
        if (candidate.Atr <= 0 ||
            candidate.EntryPrice <= 0 ||
            candidate.StopLoss <= 0)
        {
            return double.NaN;
        }

        return
            (
                candidate.EntryPrice -
                candidate.StopLoss
            ) /
            candidate.Atr;
    }

    private static string FormatStopDistance(
        double value)
    {
        return
            double.IsNaN(value) ||
            double.IsInfinity(value)
                ? "N/A"
                : value.ToString(
                    "F2"
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
    string PolicyVersion,
    string StrategyName,
    string RegimeGate,
    string TrendRegime,
    string VolatilityRegime,
    double EntryPrice,
    double Atr,
    double StopLoss,
    double StopDistanceAtr,
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
    bool PaperOrderSubmissionEnabled,
    string PolicyVersion,
    string? TrendRegime,
    string? VolatilityRegime
);
