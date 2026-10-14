using Alpaca.Markets;

public sealed class ForwardExecutionEngine
{
    private const double SlippageBps =
        5.0;

    private const double TransactionCostBps =
        1.0;

    private const int MaximumOpenBarDelayMinutes =
        5;

    private readonly IAlpacaDataClient
        _dataClient;

    private readonly ForwardSignalStore
        _signalStore;

    public ForwardExecutionEngine(
        IAlpacaDataClient dataClient,
        ForwardSignalStore signalStore)
    {
        _dataClient =
            dataClient;

        _signalStore =
            signalStore;
    }

    public async Task<ForwardExecutionPlan>
        RunAsync(
            PaperTradingPreflightResult preflight,
            FrozenForwardPolicy policy,
            CancellationToken cancellationToken = default)
    {
        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " FORWARD EXECUTION ENGINE V9F"
        );

        Console.WriteLine(
            " NEXT-SESSION OPEN DRY-RUN"
        );

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            $"Policy Version: {policy.PolicyVersion}"
        );

        Console.WriteLine(
            $"Strategy: {policy.StrategyName}"
        );

        Console.WriteLine(
            $"Regime Gate: {policy.RegimeGate}"
        );

        Console.WriteLine(
            $"Broker Time UTC: " +
            $"{preflight.BrokerTimestampUtc:yyyy-MM-dd HH:mm:ss}"
        );

        Console.WriteLine(
            $"Market Open: {preflight.MarketOpen}"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Slippage Assumption: {SlippageBps:F1} bps"
        );

        Console.WriteLine(
            $"Transaction Cost Assumption: " +
            $"{TransactionCostBps:F1} bps"
        );

        Console.WriteLine();

        Console.WriteLine(
            "IMPORTANT: V9F IS DRY-RUN ONLY."
        );

        Console.WriteLine(
            "NO ORDER API IS CALLED BY THIS ENGINE."
        );

        if (!preflight.SafetyChecksPassed)
        {
            return PrintBlocked(
                policy,
                "Paper account preflight safety checks did not pass."
            );
        }

        if (!preflight.MarketOpen)
        {
            return PrintBlocked(
                policy,
                "Market is closed. V9F evaluates frozen signals only " +
                "during their planned execution session."
            );
        }

        var snapshot =
            await _signalStore.LoadAsync(
                cancellationToken
            );

        if (snapshot is null)
        {
            return PrintBlocked(
                policy,
                "No frozen forward-signal store exists."
            );
        }

        if (!string.Equals(
                snapshot.PolicyVersion,
                policy.PolicyVersion,
                StringComparison.Ordinal
            ))
        {
            return PrintBlocked(
                policy,
                $"Stored policy {snapshot.PolicyVersion} does not match " +
                $"active policy {policy.PolicyVersion}."
            );
        }

        if (!snapshot.PlannedExecutionOpenUtc.HasValue)
        {
            return PrintBlocked(
                policy,
                "Stored signal set has no planned execution session."
            );
        }

        var plannedOpenUtc =
            EnsureUtc(
                snapshot.PlannedExecutionOpenUtc.Value
            );

        var brokerTimestampUtc =
            EnsureUtc(
                preflight.BrokerTimestampUtc
            );

        if (brokerTimestampUtc <
            plannedOpenUtc)
        {
            return PrintBlocked(
                policy,
                $"Execution session has not started yet. Planned open: " +
                $"{plannedOpenUtc:yyyy-MM-dd HH:mm:ss} UTC."
            );
        }

        if (brokerTimestampUtc.Date !=
            plannedOpenUtc.Date)
        {
            return PrintBlocked(
                policy,
                $"Stored signals belong to execution date " +
                $"{plannedOpenUtc:yyyy-MM-dd}, but broker date is " +
                $"{brokerTimestampUtc:yyyy-MM-dd}."
            );
        }

        if (snapshot.PendingSignals.Count == 0)
        {
            return PrintBlocked(
                policy,
                "Stored signal set contains no pending signals."
            );
        }

        if (brokerTimestampUtc <
            plannedOpenUtc.AddMinutes(1))
        {
            return PrintBlocked(
                policy,
                "Waiting for the first completed one-minute bar so the " +
                "session opening price can be observed without using " +
                "future information."
            );
        }

        Console.WriteLine();

        Console.WriteLine(
            "FROZEN SIGNAL SET"
        );

        Console.WriteLine(
            $"Signal Date: " +
            $"{FormatDate(snapshot.SignalDate)}"
        );

        Console.WriteLine(
            $"Planned Execution Open UTC: " +
            $"{plannedOpenUtc:yyyy-MM-dd HH:mm:ss}"
        );

        Console.WriteLine(
            $"Pending Signals Loaded: " +
            $"{snapshot.PendingSignals.Count}"
        );

        if (snapshot.Regime is not null)
        {
            Console.WriteLine(
                $"Frozen Regime: " +
                $"{snapshot.Regime.TrendRegime} | " +
                $"{snapshot.Regime.VolatilityRegime}"
            );

            if (!policy.AllowsRegime(
                    snapshot.Regime.TrendRegime,
                    snapshot.Regime.VolatilityRegime
                ))
            {
                return PrintBlocked(
                    policy,
                    "Frozen signal-set regime no longer satisfies the " +
                    "active frozen policy."
                );
            }
        }
        else
        {
            return PrintBlocked(
                policy,
                "Frozen signal set has no regime snapshot."
            );
        }

        var decisions =
            new List<ForwardExecutionDecision>();

        var orderedSignals =
            snapshot.PendingSignals
                .OrderByDescending(
                    signal =>
                        signal.NetScore
                )
                .ThenBy(
                    signal =>
                        signal.Symbol
                )
                .ToArray();

        var remainingCash =
            policy.StrategyCapital;

        var plannedPositions =
            0;

        foreach (var signal in orderedSignals)
        {
            var decision =
                await EvaluateSignalAsync(
                    signal,
                    plannedOpenUtc,
                    brokerTimestampUtc,
                    policy,
                    remainingCash,
                    plannedPositions,
                    cancellationToken
                );

            decisions.Add(
                decision
            );

            PrintDecision(
                decision
            );

            if (decision.WouldSubmit)
            {
                remainingCash -=
                    decision.TotalEntryCost;

                plannedPositions++;
            }
        }

        var accepted =
            decisions
                .Where(
                    decision =>
                        decision.WouldSubmit
                )
                .ToArray();

        var rejected =
            decisions.Count -
            accepted.Length;

        var totalPositionValue =
            accepted.Sum(
                decision =>
                    decision.PositionValue
            );

        var totalEntryFees =
            accepted.Sum(
                decision =>
                    decision.EntryFee
            );

        var totalExpectedMaximumLoss =
            accepted.Sum(
                decision =>
                    decision.ExpectedMaximumLoss
            );

        Console.WriteLine();

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            " V9F FORWARD EXECUTION SUMMARY"
        );

        Console.WriteLine(
            "=============================================="
        );

        Console.WriteLine(
            $"Frozen Signals Evaluated: " +
            $"{decisions.Count}"
        );

        Console.WriteLine(
            $"Would Submit: {accepted.Length}"
        );

        Console.WriteLine(
            $"Rejected: {rejected}"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Planned Position Value: " +
            $"${totalPositionValue:F2}"
        );

        Console.WriteLine(
            $"Estimated Entry Fees: " +
            $"${totalEntryFees:F4}"
        );

        Console.WriteLine(
            $"Combined Expected Max Loss: " +
            $"${totalExpectedMaximumLoss:F4}"
        );

        Console.WriteLine(
            $"Remaining Strategy Cash: " +
            $"${Math.Max(0, remainingCash):F2}"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Paper Order Submission Enabled: " +
            $"{policy.PaperOrderSubmissionEnabled}"
        );

        Console.WriteLine(
            $"Live Trading Enabled: " +
            $"{policy.LiveTradingEnabled}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "NO ORDERS WERE SUBMITTED."
        );

        return new ForwardExecutionPlan(
            PolicyVersion:
                policy.PolicyVersion,

            ExecutionSessionOpenUtc:
                plannedOpenUtc,

            FrozenSignalsEvaluated:
                decisions.Count,

            Decisions:
                decisions,

            WouldSubmitCount:
                accepted.Length,

            RejectedCount:
                rejected,

            TotalPlannedPositionValue:
                totalPositionValue,

            TotalEstimatedEntryFees:
                totalEntryFees,

            TotalExpectedMaximumLoss:
                totalExpectedMaximumLoss,

            RemainingStrategyCash:
                Math.Max(
                    0,
                    remainingCash
                ),

            MarketOpen:
                preflight.MarketOpen,

            SafetyChecksPassed:
                preflight.SafetyChecksPassed,

            RealOrderSubmissionEnabled:
                policy.PaperOrderSubmissionEnabled
        );
    }

    private async Task<ForwardExecutionDecision>
        EvaluateSignalAsync(
            ForwardPendingSignal signal,
            DateTime plannedOpenUtc,
            DateTime brokerTimestampUtc,
            FrozenForwardPolicy policy,
            double remainingCash,
            int alreadyPlannedPositions,
            CancellationToken cancellationToken)
    {
        if (!string.Equals(
                signal.PolicyVersion,
                policy.PolicyVersion,
                StringComparison.Ordinal
            ))
        {
            return Reject(
                signal,
                "Signal policy version does not match the active policy."
            );
        }

        if (!string.Equals(
                signal.Status,
                "PENDING_NEXT_SESSION_EXECUTION",
                StringComparison.Ordinal
            ))
        {
            return Reject(
                signal,
                $"Unexpected signal status: {signal.Status}."
            );
        }

        if (EnsureUtc(
                signal.PlannedExecutionOpenUtc
            ) !=
            plannedOpenUtc)
        {
            return Reject(
                signal,
                "Signal execution session does not match the frozen set."
            );
        }

        if (alreadyPlannedPositions >=
            policy.MaxOpenPositions)
        {
            return Reject(
                signal,
                $"Maximum planned positions ({policy.MaxOpenPositions}) " +
                "already reached."
            );
        }

        var openingBar =
            await LoadSessionOpeningBarAsync(
                signal.Symbol,
                plannedOpenUtc,
                brokerTimestampUtc,
                cancellationToken
            );

        if (openingBar is null)
        {
            return Reject(
                signal,
                "No eligible session-opening minute bar is available yet."
            );
        }

        var openingDelayMinutes =
            (
                openingBar.TimeUtc -
                plannedOpenUtc
            ).TotalMinutes;

        if (openingDelayMinutes < 0 ||
            openingDelayMinutes >
            MaximumOpenBarDelayMinutes)
        {
            return Reject(
                signal,
                $"First observed minute bar is " +
                $"{openingDelayMinutes:F1} minutes from the planned open; " +
                "execution-open fidelity check failed."
            );
        }

        var rawOpen =
            (double)openingBar.Open;

        if (rawOpen <= 0)
        {
            return Reject(
                signal,
                "Observed raw opening price is invalid."
            );
        }

        var executionPrice =
            ApplyBuySlippage(
                rawOpen
            );

        var candidate =
            BuildCandidate(
                signal,
                executionPrice,
                policy
            );

        if (!candidate.IsValid)
        {
            return Reject(
                signal,
                candidate.RejectionReason,
                rawOpen,
                executionPrice,
                openingBar.TimeUtc
            );
        }

        var stopDistanceAtr =
            signal.Atr > 0
                ? (
                    executionPrice -
                    candidate.StopLoss
                  ) /
                  signal.Atr
                : 0;

        if (!policy.AllowsStopDistance(
                signal.Atr,
                executionPrice,
                candidate.StopLoss
            ))
        {
            return Reject(
                signal,
                $"Stop distance {stopDistanceAtr:F2} ATR is outside " +
                $"{policy.MinimumStopDistanceAtr:F2}-" +
                $"{policy.MaximumStopDistanceAtr:F2} ATR.",
                rawOpen,
                executionPrice,
                openingBar.TimeUtc,
                candidate
            );
        }

        if (candidate.RiskRewardRatio <
            policy.MinimumRiskRewardRatio)
        {
            return Reject(
                signal,
                $"Risk/reward ratio {candidate.RiskRewardRatio:F2} " +
                $"is below {policy.MinimumRiskRewardRatio:F2}.",
                rawOpen,
                executionPrice,
                openingBar.TimeUtc,
                candidate,
                stopDistanceAtr
            );
        }

        var maximumRiskDollars =
            policy.StrategyCapital *
            (
                policy.RiskPerTradePercent /
                100.0
            );

        var maximumPositionDollars =
            policy.StrategyCapital *
            (
                policy.MaxPositionPercent /
                100.0
            );

        var quantityByRisk =
            maximumRiskDollars /
            candidate.RiskPerShare;

        var quantityByPosition =
            maximumPositionDollars /
            executionPrice;

        var feeRate =
            TransactionCostBps /
            10000.0;

        var quantityByCash =
            remainingCash /
            (
                executionPrice *
                (
                    1.0 +
                    feeRate
                )
            );

        var quantity =
            Math.Min(
                quantityByRisk,
                Math.Min(
                    quantityByPosition,
                    quantityByCash
                )
            );

        quantity =
            Math.Floor(
                quantity *
                10000.0
            ) /
            10000.0;

        if (quantity <
            0.001)
        {
            return Reject(
                signal,
                "Position size is below the minimum 0.001 shares.",
                rawOpen,
                executionPrice,
                openingBar.TimeUtc,
                candidate,
                stopDistanceAtr
            );
        }

        var positionValue =
            executionPrice *
            quantity;

        var entryFee =
            positionValue *
            feeRate;

        var totalEntryCost =
            positionValue +
            entryFee;

        if (totalEntryCost >
            remainingCash +
            0.000001)
        {
            return Reject(
                signal,
                "Planned entry cost exceeds remaining strategy cash.",
                rawOpen,
                executionPrice,
                openingBar.TimeUtc,
                candidate,
                stopDistanceAtr
            );
        }

        var expectedMaximumLoss =
            candidate.RiskPerShare *
            quantity;

        if (expectedMaximumLoss >
            maximumRiskDollars +
            0.000001)
        {
            return Reject(
                signal,
                "Expected maximum loss exceeds the frozen per-trade risk cap.",
                rawOpen,
                executionPrice,
                openingBar.TimeUtc,
                candidate,
                stopDistanceAtr
            );
        }

        var entryGapPercent =
            signal.SignalClose > 0
                ? (
                    rawOpen -
                    signal.SignalClose
                  ) /
                  signal.SignalClose *
                  100.0
                : 0;

        var slippageCost =
            (
                executionPrice -
                rawOpen
            ) *
            quantity;

        return new ForwardExecutionDecision(
            Symbol:
                signal.Symbol,

            WouldSubmit:
                true,

            Decision:
                "WOULD SUBMIT",

            Reason:
                "Frozen signal and next-session execution checks passed.",

            SignalDate:
                signal.SignalDate,

            ExecutionBarTimeUtc:
                openingBar.TimeUtc,

            RawSessionOpen:
                rawOpen,

            ExecutionPrice:
                executionPrice,

            EntryGapPercent:
                entryGapPercent,

            Atr:
                signal.Atr,

            StopLoss:
                candidate.StopLoss,

            StopDistanceAtr:
                stopDistanceAtr,

            TakeProfit:
                candidate.TakeProfit,

            RiskPerShare:
                candidate.RiskPerShare,

            RewardPerShare:
                candidate.RewardPerShare,

            RiskRewardRatio:
                candidate.RiskRewardRatio,

            Quantity:
                quantity,

            PositionValue:
                positionValue,

            EntryFee:
                entryFee,

            SlippageCost:
                slippageCost,

            TotalEntryCost:
                totalEntryCost,

            ExpectedMaximumLoss:
                expectedMaximumLoss,

            NetScore:
                signal.NetScore
        );
    }

    private async Task<IBar?>
        LoadSessionOpeningBarAsync(
            string symbol,
            DateTime plannedOpenUtc,
            DateTime brokerTimestampUtc,
            CancellationToken cancellationToken)
    {
        var requestEnd =
            brokerTimestampUtc;

        var maximumUsefulEnd =
            plannedOpenUtc
                .AddMinutes(
                    MaximumOpenBarDelayMinutes +
                    2
                );

        if (requestEnd >
            maximumUsefulEnd)
        {
            requestEnd =
                maximumUsefulEnd;
        }

        if (requestEnd <=
            plannedOpenUtc)
        {
            return null;
        }

        var request =
            new HistoricalBarsRequest(
                symbol,
                plannedOpenUtc,
                requestEnd,
                BarTimeFrame.Minute
            )
            {
                Feed =
                    MarketDataFeed.Iex
            };

        var page =
            await _dataClient
                .ListHistoricalBarsAsync(
                    request,
                    cancellationToken
                );

        return page.Items
            .Where(
                bar =>
                    bar.TimeUtc >=
                    plannedOpenUtc &&
                    bar.TimeUtc <
                    plannedOpenUtc.AddMinutes(
                        MaximumOpenBarDelayMinutes +
                        1
                    )
            )
            .OrderBy(
                bar =>
                    bar.TimeUtc
            )
            .FirstOrDefault();
    }

    private static ForwardExecutionCandidate
        BuildCandidate(
            ForwardPendingSignal signal,
            double entryPrice,
            FrozenForwardPolicy policy)
    {
        if (signal.Atr <= 0)
        {
            return
                ForwardExecutionCandidate.Invalid(
                    "ATR is invalid."
                );
        }

        var atrStop =
            entryPrice -
            (
                signal.Atr *
                2.0
            );

        var supportStop =
            signal.Support10 -
            (
                signal.Atr *
                0.25
            );

        var stopLoss =
            Math.Max(
                atrStop,
                supportStop
            );

        if (stopLoss <= 0 ||
            stopLoss >= entryPrice)
        {
            return
                ForwardExecutionCandidate.Invalid(
                    "No valid stop-loss level could be calculated."
                );
        }

        var takeProfit =
            signal.Resistance20 +
            (
                signal.Atr *
                1.50
            );

        if (takeProfit <=
            entryPrice)
        {
            return
                ForwardExecutionCandidate.Invalid(
                    "Calculated target is not above execution price."
                );
        }

        var riskPerShare =
            entryPrice -
            stopLoss;

        var rewardPerShare =
            takeProfit -
            entryPrice;

        if (riskPerShare <= 0 ||
            rewardPerShare <= 0)
        {
            return
                ForwardExecutionCandidate.Invalid(
                    "Risk/reward geometry is invalid."
                );
        }

        var riskRewardRatio =
            rewardPerShare /
            riskPerShare;

        if (signal.Trend !=
            "Bullish")
        {
            return
                ForwardExecutionCandidate.Invalid(
                    "Frozen signal trend is not bullish."
                );
        }

        if (signal.Momentum <= 0)
        {
            return
                ForwardExecutionCandidate.Invalid(
                    "Frozen signal momentum is not positive."
                );
        }

        if (signal.RiskScore > 35)
        {
            return
                ForwardExecutionCandidate.Invalid(
                    "Frozen signal risk score exceeds 35."
                );
        }

        if (riskRewardRatio <
            policy.MinimumRiskRewardRatio)
        {
            return
                ForwardExecutionCandidate.Invalid(
                    $"Risk/reward ratio {riskRewardRatio:F2} is below " +
                    $"{policy.MinimumRiskRewardRatio:F2}."
                );
        }

        return new ForwardExecutionCandidate(
            true,
            stopLoss,
            takeProfit,
            riskPerShare,
            rewardPerShare,
            riskRewardRatio,
            string.Empty
        );
    }

    private static double ApplyBuySlippage(
        double price)
    {
        return
            price *
            (
                1.0 +
                SlippageBps /
                10000.0
            );
    }

    private static void PrintDecision(
        ForwardExecutionDecision decision)
    {
        Console.WriteLine();

        Console.WriteLine(
            "----------------------------------------------"
        );

        Console.WriteLine(
            $"SYMBOL: {decision.Symbol}"
        );

        Console.WriteLine(
            $"Signal Date: " +
            $"{decision.SignalDate:yyyy-MM-dd}"
        );

        if (decision.ExecutionBarTimeUtc.HasValue)
        {
            Console.WriteLine(
                $"Observed Opening Bar UTC: " +
                $"{decision.ExecutionBarTimeUtc.Value:yyyy-MM-dd HH:mm:ss}"
            );
        }

        if (decision.RawSessionOpen > 0)
        {
            Console.WriteLine(
                $"Raw Session Open: " +
                $"${decision.RawSessionOpen:F2}"
            );

            Console.WriteLine(
                $"Execution Price (+{SlippageBps:F1} bps): " +
                $"${decision.ExecutionPrice:F2}"
            );

            Console.WriteLine(
                $"Entry Gap vs Signal Close: " +
                $"{decision.EntryGapPercent:F2}%"
            );

            Console.WriteLine();

            Console.WriteLine(
                $"ATR(14): {decision.Atr:F2}"
            );

            Console.WriteLine(
                $"Stop Loss: ${decision.StopLoss:F2}"
            );

            Console.WriteLine(
                $"Stop Distance: " +
                $"{decision.StopDistanceAtr:F2} ATR"
            );

            Console.WriteLine(
                $"Take Profit: ${decision.TakeProfit:F2}"
            );

            Console.WriteLine(
                $"Risk/Reward: " +
                $"1:{decision.RiskRewardRatio:F2}"
            );
        }

        if (decision.Quantity > 0)
        {
            Console.WriteLine();

            Console.WriteLine(
                $"Quantity: {decision.Quantity:F4} shares"
            );

            Console.WriteLine(
                $"Position Value: " +
                $"${decision.PositionValue:F2}"
            );

            Console.WriteLine(
                $"Estimated Entry Fee: " +
                $"${decision.EntryFee:F4}"
            );

            Console.WriteLine(
                $"Estimated Slippage Cost: " +
                $"${decision.SlippageCost:F4}"
            );

            Console.WriteLine(
                $"Expected Max Loss: " +
                $"${decision.ExpectedMaximumLoss:F4}"
            );
        }

        Console.WriteLine();

        Console.WriteLine(
            $"DRY-RUN DECISION: " +
            $"{decision.Decision}"
        );

        Console.WriteLine(
            $"Reason: {decision.Reason}"
        );
    }

    private static ForwardExecutionDecision Reject(
        ForwardPendingSignal signal,
        string reason,
        double rawSessionOpen = 0,
        double executionPrice = 0,
        DateTime? executionBarTimeUtc = null,
        ForwardExecutionCandidate? candidate = null,
        double stopDistanceAtr = 0)
    {
        return new ForwardExecutionDecision(
            Symbol:
                signal.Symbol,

            WouldSubmit:
                false,

            Decision:
                "REJECTED",

            Reason:
                reason,

            SignalDate:
                signal.SignalDate,

            ExecutionBarTimeUtc:
                executionBarTimeUtc,

            RawSessionOpen:
                rawSessionOpen,

            ExecutionPrice:
                executionPrice,

            EntryGapPercent:
                signal.SignalClose > 0 &&
                rawSessionOpen > 0
                    ? (
                        rawSessionOpen -
                        signal.SignalClose
                      ) /
                      signal.SignalClose *
                      100.0
                    : 0,

            Atr:
                signal.Atr,

            StopLoss:
                candidate?.StopLoss ??
                0,

            StopDistanceAtr:
                stopDistanceAtr,

            TakeProfit:
                candidate?.TakeProfit ??
                0,

            RiskPerShare:
                candidate?.RiskPerShare ??
                0,

            RewardPerShare:
                candidate?.RewardPerShare ??
                0,

            RiskRewardRatio:
                candidate?.RiskRewardRatio ??
                0,

            Quantity:
                0,

            PositionValue:
                0,

            EntryFee:
                0,

            SlippageCost:
                0,

            TotalEntryCost:
                0,

            ExpectedMaximumLoss:
                0,

            NetScore:
                signal.NetScore
        );
    }

    private static ForwardExecutionPlan PrintBlocked(
        FrozenForwardPolicy policy,
        string reason)
    {
        Console.WriteLine();

        Console.WriteLine(
            "V9F STATUS: BLOCKED"
        );

        Console.WriteLine(
            $"Reason: {reason}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "NO ORDERS WERE SUBMITTED."
        );

        return new ForwardExecutionPlan(
            PolicyVersion:
                policy.PolicyVersion,

            ExecutionSessionOpenUtc:
                null,

            FrozenSignalsEvaluated:
                0,

            Decisions:
                Array.Empty<
                    ForwardExecutionDecision
                >(),

            WouldSubmitCount:
                0,

            RejectedCount:
                0,

            TotalPlannedPositionValue:
                0,

            TotalEstimatedEntryFees:
                0,

            TotalExpectedMaximumLoss:
                0,

            RemainingStrategyCash:
                policy.StrategyCapital,

            MarketOpen:
                false,

            SafetyChecksPassed:
                false,

            RealOrderSubmissionEnabled:
                false
        );
    }

    private static DateTime EnsureUtc(
        DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc =>
                value,

            DateTimeKind.Local =>
                value.ToUniversalTime(),

            _ =>
                DateTime.SpecifyKind(
                    value,
                    DateTimeKind.Utc
                )
        };
    }

    private static string FormatDate(
        DateTime? value)
    {
        return value.HasValue
            ? value.Value
                .ToString(
                    "yyyy-MM-dd"
                )
            : "N/A";
    }
}

public sealed record ForwardExecutionCandidate(
    bool IsValid,
    double StopLoss,
    double TakeProfit,
    double RiskPerShare,
    double RewardPerShare,
    double RiskRewardRatio,
    string RejectionReason)
{
    public static ForwardExecutionCandidate Invalid(
        string reason)
    {
        return new ForwardExecutionCandidate(
            false,
            0,
            0,
            0,
            0,
            0,
            reason
        );
    }
}

public sealed record ForwardExecutionDecision(
    string Symbol,
    bool WouldSubmit,
    string Decision,
    string Reason,
    DateTime SignalDate,
    DateTime? ExecutionBarTimeUtc,
    double RawSessionOpen,
    double ExecutionPrice,
    double EntryGapPercent,
    double Atr,
    double StopLoss,
    double StopDistanceAtr,
    double TakeProfit,
    double RiskPerShare,
    double RewardPerShare,
    double RiskRewardRatio,
    double Quantity,
    double PositionValue,
    double EntryFee,
    double SlippageCost,
    double TotalEntryCost,
    double ExpectedMaximumLoss,
    int NetScore
);

public sealed record ForwardExecutionPlan(
    string PolicyVersion,
    DateTime? ExecutionSessionOpenUtc,
    int FrozenSignalsEvaluated,
    IReadOnlyList<ForwardExecutionDecision> Decisions,
    int WouldSubmitCount,
    int RejectedCount,
    double TotalPlannedPositionValue,
    double TotalEstimatedEntryFees,
    double TotalExpectedMaximumLoss,
    double RemainingStrategyCash,
    bool MarketOpen,
    bool SafetyChecksPassed,
    bool RealOrderSubmissionEnabled
);
