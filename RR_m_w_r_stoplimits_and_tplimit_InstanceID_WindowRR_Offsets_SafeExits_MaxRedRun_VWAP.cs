// Custom VWAP variant; original strategy remains a separate type.
// Requires Indicators/RTLSessionVWAP.cs (install once for both variants).
#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class RRLongTimeWinStopLimitTPlimitGAPWindowRROffsetsSafeExitsMaxRedRunVWAP : Strategy
    {
        // Bump on every behaviour change; printed at startup and shown as the
        // strategy description, so the Output window shows which build is running.
        private const string ScriptVersion = "2026.10.29-vwap.1";

        private RTLSessionVWAP sessionVwap;

        private Order longOrder;
        private Order entryTimeoutOrder;
        private DateTime entryTriggeredAt = Core.Globals.MinDate;
        private bool entryTimeoutCancelRequested;
        private DateTime lastEntryTimeoutCancelAttempt = Core.Globals.MinDate;
        private int entryTimeoutExecuted;
        // Stop-limit escape runs independently of Calculate.OnBarClose.
        private System.Timers.Timer stopFallbackTimer;
        private int stopFallbackTimerQueued;
        private DateTime stopTriggeredAt = Core.Globals.MinDate;
        private bool stopFallbackRequested;
        private bool stopFallbackSubmitting;
        private DateTime lastStopFallbackAttempt = Core.Globals.MinDate;
        private Order stopFallbackOrder;
        private Order stopFallbackEntryOrder;
        private readonly Dictionary<Order, int> stopFallbackExecutions = new Dictionary<Order, int>();
        private string StopFallbackName => $"StopTimeoutExit_{InstanceId}";
        private double pendingStopPrice;
        private double entryPrice;
        private double riskPerTrade;
        private double pendingRiskReward;
        private double positionRiskReward;
        private bool takeProfitSubmitted;
        private readonly object exitOrdersSync = new object();
        private readonly List<Order> stopOrders = new List<Order>();
        private readonly List<Order> takeProfitOrders = new List<Order>();
        private DateTime lastFlattenDate = Core.Globals.MinDate;
        private bool lastWindowState = false;
        private SessionIterator sessionIterator;
        // Bar whose signal priced the working entry order; drives EntryExpiryBars.
        private int entryOrderBar = -1;
        private DateTime entryOrderBarTime = Core.Globals.MinDate;

        // Derived signal names — all unique per instance to prevent cross-instance interference
        private string EntrySignalName    => $"Long1_{InstanceId}";
        private string StopLossSignalName => $"StopLimit_{InstanceId}";
        private string TakeProfitName     => $"RR_Limit_{InstanceId}";
        private string FlattenName        => $"DailyFlatten_{InstanceId}";
        private int EntryQuantity         => UseCustomQuantity ? CustomQuantity : DefaultQuantity;

		// ===== INSTANCE ID =====
		[NinjaScriptProperty]
		[Display(Name = "Instance ID", Order = 0, GroupName = "Risk Management",
		         Description = "Unique ID per chart instance — prevents cross-instance order interference when running multiple copies")]
		public int InstanceId { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Custom Quantity", Order = 2, GroupName = "Risk Management")]
        public bool UseCustomQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Custom Quantity", Order = 3, GroupName = "Risk Management")]
        public int CustomQuantity { get; set; }
		
		// ===== OFFSETS =====
        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Exit Offset (ticks)", Order = 5, GroupName = "Risk Management")]
        public int ExitOffset { get; set; }

        // ===== MT5 SESSION VWAP EXPERIMENT =====
        [NinjaScriptProperty]
        [Display(Name = "Use VWAP gate", Order = 0, GroupName = "VWAP")]
        public bool UseVwapGate { get; set; }

        [NinjaScriptProperty]
        [Range(-100.0, 100.0)]
        [Display(Name = "Far-below Z", Order = 1, GroupName = "VWAP",
            Description = "Reject a red M30 signal only when Close < minute-volume VWAP + Z * A20. MT5 research value: -1. A20 is a simple mean of 20 true ranges, not Wilder ATR.")]
        public double VwapFarBelowZ { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1439)]
        [Display(Name = "Anchor minute", Order = 2, GroupName = "VWAP",
            Description = "Minutes after midnight on the shifted session clock. 0 matches the futures study; 570 is 09:30 for stocks with New York time and zero shift.")]
        public int VwapAnchorMinute { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session time zone ID", Order = 3, GroupName = "VWAP",
            Description = "Windows time zone ID. Central Standard Time handles Chicago DST automatically. NT8's configured chart time zone is converted to this zone before the shift.")]
        public string VwapSessionTimeZoneId { get; set; }

        [NinjaScriptProperty]
        [Range(-1440, 1440)]
        [Display(Name = "Session clock shift (minutes)", Order = 4, GroupName = "VWAP",
            Description = "480 with Chicago time matches MT5's Chicago + 8 hour date (rollover 16:00 Chicago, regular CME open 17:00). This is not a fixed UTC offset.")]
        public int VwapSessionShiftMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show VWAP indicator", Order = 5, GroupName = "VWAP")]
        public bool ShowVwapIndicator { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Log VWAP decisions", Order = 6, GroupName = "VWAP",
            Description = "Print candidate close, VWAP, A20, threshold and decision to NinjaScript Output. Unknown values allow entries, matching MT5.")]
        public bool LogVwapDecisions { get; set; }

        // ===== RED-RUN ENTRY FILTER =====
        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MaxRedRun (0 = off)", Order = 0, GroupName = "Entry Filters",
                 Description = "Maximum consecutive red candles ending at the signal, including the signal. 0 disables the cap. Green/doji bars break the run. Rejected red signals cancel the pending entry.")]
        public int MaxRedRun { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Entry expiry (bars, 0 = off)", Order = 1, GroupName = "Entry Filters",
                 Description = "Cancel a pending entry whose buy stop was not reached within this many bars after its signal. A new signal that re-prices the entry restarts the count. A reached but unfilled entry keeps the 25-minute timeout.")]
        public int EntryExpiryBars { get; set; }

        // ===== RISK/REWARD BY TIME WINDOW =====
        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="00:00-01:00", Order=0, GroupName="Window Risk/Reward")]
        public double RR00 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="01:00-02:00", Order=1, GroupName="Window Risk/Reward")]
        public double RR01 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="02:00-03:00", Order=2, GroupName="Window Risk/Reward")]
        public double RR02 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="03:00-04:00", Order=3, GroupName="Window Risk/Reward")]
        public double RR03 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="04:00-05:00", Order=4, GroupName="Window Risk/Reward")]
        public double RR04 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="05:00-06:00", Order=5, GroupName="Window Risk/Reward")]
        public double RR05 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="06:00-07:00", Order=6, GroupName="Window Risk/Reward")]
        public double RR06 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="07:00-08:00", Order=7, GroupName="Window Risk/Reward")]
        public double RR07 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="08:00-09:00", Order=8, GroupName="Window Risk/Reward")]
        public double RR08 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="09:00-10:00", Order=9, GroupName="Window Risk/Reward")]
        public double RR09 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="10:00-11:00", Order=10, GroupName="Window Risk/Reward")]
        public double RR10 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="11:00-12:00", Order=11, GroupName="Window Risk/Reward")]
        public double RR11 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="12:00-13:00", Order=12, GroupName="Window Risk/Reward")]
        public double RR12 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="13:00-14:00", Order=13, GroupName="Window Risk/Reward")]
        public double RR13 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="14:00-15:00", Order=14, GroupName="Window Risk/Reward")]
        public double RR14 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="15:00-16:00", Order=15, GroupName="Window Risk/Reward")]
        public double RR15 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="16:00-17:00", Order=16, GroupName="Window Risk/Reward")]
        public double RR16 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="17:00-18:00", Order=17, GroupName="Window Risk/Reward")]
        public double RR17 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="18:00-19:00", Order=18, GroupName="Window Risk/Reward")]
        public double RR18 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="19:00-20:00", Order=19, GroupName="Window Risk/Reward")]
        public double RR19 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="20:00-21:00", Order=20, GroupName="Window Risk/Reward")]
        public double RR20 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="21:00-22:00", Order=21, GroupName="Window Risk/Reward")]
        public double RR21 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="22:00-23:00", Order=22, GroupName="Window Risk/Reward")]
        public double RR22 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name="23:00-00:00", Order=23, GroupName="Window Risk/Reward")]
        public double RR23 { get; set; }

        // ===== TIME WINDOW INPUTS =====
		[NinjaScriptProperty]
		[Display(Name = "Use Trade Window", Order = 0, GroupName = "Trade Windows")]
		[Browsable(false)]
		public bool UseTradeWindow { get; set; }

        [NinjaScriptProperty]
        [Display(Name="00:00–01:00", Order=1, GroupName="Trade Windows")]
        [Browsable(false)]
        public bool W00 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="01:00–02:00", Order=2, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W01 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="02:00–03:00", Order=3, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W02 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="03:00–04:00", Order=4, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W03 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="04:00–05:00", Order=5, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W04 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="05:00–06:00", Order=6, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W05 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="06:00–07:00", Order=7, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W06 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="07:00–08:00", Order=8, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W07 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="08:00–09:00", Order=9, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W08 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="09:00–10:00", Order=10, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W09 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="10:00–11:00", Order=11, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W10 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="11:00–12:00", Order=12, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W11 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="12:00–13:00", Order=13, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W12 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="13:00–14:00", Order=14, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W13 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="14:00–15:00", Order=15, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W14 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="15:00–16:00", Order=16, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W15 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="16:00–17:00", Order=17, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W16 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="17:00–18:00", Order=18, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W17 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="18:00–19:00", Order=19, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W18 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="19:00–20:00", Order=20, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W19 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="20:00–21:00", Order=21, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W20 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="21:00–22:00", Order=22, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W21 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="22:00–23:00", Order=23, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W22 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="23:00–00:00", Order=24, GroupName="Trade Windows")]
		[Browsable(false)]
		public bool W23 { get; set; }

        private bool[] tradeWindows;
        private double[] windowRiskRewards;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "RRLongTimeWinStopLimitTPlimitGAPWindowRROffsetsSafeExitsMaxRedRunVWAP";
                Description = "MT5 minute-volume VWAP experiment. Version " + ScriptVersion;
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                BarsRequiredToTrade = 5;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                StartBehavior = StartBehavior.ImmediatelySubmit;
                IsUnmanaged = false;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                InstanceId = 1;
                UseCustomQuantity = false;
                CustomQuantity = 1;
                ExitOffset = 0;
                UseVwapGate = true;
                VwapFarBelowZ = -1.0;
                VwapAnchorMinute = 0;
                VwapSessionTimeZoneId = "Central Standard Time";
                VwapSessionShiftMinutes = 480;
                ShowVwapIndicator = true;
                LogVwapDecisions = false;
                MaxRedRun = 0; // Disabled by default, matching the MT5 variant.
                EntryExpiryBars = 0; // Off by default: untriggered entries stay working.
                UseTradeWindow = true;
                RR00 = RR01 = RR02 = RR03 = RR04 = RR05 = 0.0;
                RR06 = RR07 = RR08 = RR09 = RR10 = RR11 = 0.0;
                RR12 = RR13 = RR14 = RR15 = RR16 = RR17 = 0.0;
                RR18 = RR19 = RR20 = RR21 = RR22 = RR23 = 0.0;
            }
            else if (State == State.Configure)
            {
                // Must mirror RTLSessionVWAP's hardcoded secondary series.
                AddDataSeries(BarsPeriodType.Minute, 1);
            }
            else if (State == State.DataLoaded)
            {
                sessionIterator = new SessionIterator(Bars);
                if (UseVwapGate || ShowVwapIndicator || LogVwapDecisions)
                {
                    if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value != 30
                        || Calculate != Calculate.OnBarClose)
                        throw new InvalidOperationException("The MT5 VWAP experiment requires 30-minute bars and Calculate.OnBarClose.");
                    sessionVwap = RTLSessionVWAP(Closes[0], VwapFarBelowZ, VwapAnchorMinute,
                        VwapSessionTimeZoneId, VwapSessionShiftMinutes);
                    if (ShowVwapIndicator)
                        AddChartIndicator(sessionVwap);
                }


                W00 = RR00 > 0; W01 = RR01 > 0; W02 = RR02 > 0; W03 = RR03 > 0;
                W04 = RR04 > 0; W05 = RR05 > 0; W06 = RR06 > 0; W07 = RR07 > 0;
                W08 = RR08 > 0; W09 = RR09 > 0; W10 = RR10 > 0; W11 = RR11 > 0;
                W12 = RR12 > 0; W13 = RR13 > 0; W14 = RR14 > 0; W15 = RR15 > 0;
                W16 = RR16 > 0; W17 = RR17 > 0; W18 = RR18 > 0; W19 = RR19 > 0;
                W20 = RR20 > 0; W21 = RR21 > 0; W22 = RR22 > 0; W23 = RR23 > 0;
                UseTradeWindow = true;

                tradeWindows = new bool[]
                {
                    W00, W01, W02, W03, W04, W05,
                    W06, W07, W08, W09, W10, W11,
                    W12, W13, W14, W15, W16, W17,
                    W18, W19, W20, W21, W22, W23
                };
                windowRiskRewards = new double[]
                {
                    RR00, RR01, RR02, RR03, RR04, RR05, RR06, RR07,
                    RR08, RR09, RR10, RR11, RR12, RR13, RR14, RR15,
                    RR16, RR17, RR18, RR19, RR20, RR21, RR22, RR23
                };
            }
            else if (State == State.Realtime)
            {
                ResetEntryTimeoutTracking();
                longOrder = null;
                pendingStopPrice = 0;
                entryPrice = 0;
                riskPerTrade = 0;
                pendingRiskReward = 0;
                positionRiskReward = 0;
                ResetExitTracking();
                Print($"=== Strategy entering REALTIME mode (Instance {InstanceId}, signal={EntrySignalName}, version {ScriptVersion}) ===");
                StartStopFallbackTimer();
            }
            else if (State == State.Terminated)
            {
                StopStopFallbackTimer();
            }
        }

        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            if (marketDataUpdate.MarketDataType == MarketDataType.Last)
            {
                ObserveEntryTrigger(marketDataUpdate.Price);
                ObserveStopTrigger(marketDataUpdate.Price);
            }
        }



        private void ResetEntryTimeoutTracking()
        {
            lock (exitOrdersSync)
            {
                entryTimeoutOrder = null;
                entryTriggeredAt = Core.Globals.MinDate;
                entryTimeoutCancelRequested = false;
                lastEntryTimeoutCancelAttempt = Core.Globals.MinDate;
                entryTimeoutExecuted = 0;
            }
        }

        private void TrackEntryTimeoutOrder(Order order, OrderState state, int filled, int quantity)
        {
            lock (exitOrdersSync)
            {
                if (entryTimeoutOrder != order)
                {
                    // Ignore late terminal callbacks for an older entry. A new
                    // live Order object starts a new watch; repricing the same
                    // object preserves its original trigger and deadline.
                    if (Order.IsTerminalState(state))
                        return;
                    ResetEntryTimeoutTracking();
                    entryTimeoutOrder = order;
                }
                if (filled > 0 && filled < quantity && entryTriggeredAt == Core.Globals.MinDate)
                    entryTriggeredAt = Core.Globals.Now;
            }
        }

        private void RecordEntryTimeoutExecution(Execution execution, int quantity)
        {
            if (execution == null || execution.Order == null || quantity <= 0)
                return;
            lock (exitOrdersSync)
                if (execution.Order == entryTimeoutOrder)
                    entryTimeoutExecuted += quantity;
        }

        private void ObserveEntryTrigger(double lastPrice)
        {
            if (State != State.Realtime || lastPrice <= 0)
                return;
            lock (exitOrdersSync)
            {
                Order order = entryTimeoutOrder;
                if (order == null || Order.IsTerminalState(order.OrderState)
                    || order.OrderType != OrderType.StopLimit || order.OrderAction != OrderAction.Buy
                    || order.StopPrice <= 0 || lastPrice < order.StopPrice
                    || entryTriggeredAt != Core.Globals.MinDate)
                    return;
                entryTriggeredAt = Core.Globals.Now;
                Print($"[{entryTriggeredAt}] [{EntrySignalName}] Buy stop price reached at {lastPrice}; "
                    + "unfilled entry quantity will be cancelled after 25 minutes");
            }
        }

        private bool IsEntryTimeoutSettled()
        {
            return entryTimeoutOrder == null || (Order.IsTerminalState(entryTimeoutOrder.OrderState)
                && entryTimeoutOrder.Filled <= entryTimeoutExecuted);
        }

        private bool IsEntryTimeoutCancellationPending()
        {
            lock (exitOrdersSync)
                return entryTimeoutCancelRequested && !IsEntryTimeoutSettled();
        }

        private void CheckEntryTimeout()
        {
            if (State != State.Realtime)
                return;
            Order order;
            DateTime now = Core.Globals.Now;
            lock (exitOrdersSync)
            {
                order = entryTimeoutOrder;
                if (order == null)
                    return;
                if (Order.IsTerminalState(order.OrderState))
                {
                    // Let executions that raced the cancel reach position/stop
                    // management before allowing another entry.
                    if (IsEntryTimeoutSettled())
                        ResetEntryTimeoutTracking();
                    return;
                }
                if (entryTriggeredAt == Core.Globals.MinDate || (now - entryTriggeredAt).TotalMinutes < 25)
                    return;
                if (!entryTimeoutCancelRequested)
                {
                    entryTimeoutCancelRequested = true;
                    Print($"[{now}] [{EntrySignalName}] Entry timeout after 25 minutes: cancelling "
                        + $"unfilled buy quantity={Math.Max(0, order.Quantity - order.Filled)}");
                }
                if (order.OrderState == OrderState.CancelPending || order.OrderState == OrderState.CancelSubmitted
                    || (now - lastEntryTimeoutCancelAttempt).TotalSeconds < 1)
                    return;
                lastEntryTimeoutCancelAttempt = now;
            }
            try { CancelOrder(order); }
            catch (Exception ex)
            {
                Print($"[{now}] [{EntrySignalName}] Entry timeout cancellation failed: {ex.Message}; will retry");
            }
        }

        private void StartStopFallbackTimer()
        {
            stopFallbackTimer = new System.Timers.Timer(500);
            stopFallbackTimer.Elapsed += OnStopFallbackTimer;
            stopFallbackTimer.Start();
        }

        private void StopStopFallbackTimer()
        {
            if (stopFallbackTimer == null)
                return;
            stopFallbackTimer.Stop();
            stopFallbackTimer.Elapsed -= OnStopFallbackTimer;
            stopFallbackTimer.Dispose();
            stopFallbackTimer = null;
        }

        private void OnStopFallbackTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (State != State.Realtime
                || System.Threading.Interlocked.Exchange(ref stopFallbackTimerQueued, 1) != 0)
                return;
            try
            {
                TriggerCustomEvent(o =>
                {
                    try
                    {
                        CheckStopFallback();
                        CheckEntryTimeout();
                    }
                    finally { System.Threading.Interlocked.Exchange(ref stopFallbackTimerQueued, 0); }
                }, null);
            }
            catch (Exception ex)
            {
                System.Threading.Interlocked.Exchange(ref stopFallbackTimerQueued, 0);
                if (State == State.Realtime)
                    Print($"[{EntrySignalName}] Stop timeout timer error: {ex.Message}");
            }
        }

        private void ObserveStopTrigger(double lastPrice)
        {
            if (State != State.Realtime || lastPrice <= 0)
                return;
            lock (exitOrdersSync)
            {
                if (stopTriggeredAt != Core.Globals.MinDate || stopFallbackRequested)
                    return;
                foreach (Order order in stopOrders)
                {
                    if (order != null && !Order.IsTerminalState(order.OrderState)
                        && order.OrderType == OrderType.StopLimit && order.OrderAction == OrderAction.Sell
                        && order.StopPrice > 0 && lastPrice <= order.StopPrice)
                    {
                        stopTriggeredAt = Core.Globals.Now;
                        Print($"[{stopTriggeredAt}] [{EntrySignalName}] Stop price reached at {lastPrice}; "
                            + "unfilled stop quantity will exit at market after 60 seconds");
                        return;
                    }
                }
            }
        }

        private bool IsStopFallbackActive()
        {
            lock (exitOrdersSync)
                // A stopped-out partial entry can be flat while its unfilled buy
                // remainder is still live. Keep bar logic from resetting its clock.
                return stopFallbackRequested || (stopTriggeredAt != Core.Globals.MinDate
                    && Position.MarketPosition == MarketPosition.Flat
                    && !IsStopFallbackOrderSettled(stopFallbackEntryOrder));
        }

        private void ResetStopFallback()
        {
            lock (exitOrdersSync)
            {
                stopTriggeredAt = Core.Globals.MinDate;
                stopFallbackRequested = false;
                stopFallbackSubmitting = false;
                lastStopFallbackAttempt = Core.Globals.MinDate;
                stopFallbackOrder = null;
                stopFallbackEntryOrder = null;
                stopFallbackExecutions.Clear();
            }
        }

        private void RecordStopFallbackExecution(Execution execution, int quantity)
        {
            if (execution == null || execution.Order == null || quantity <= 0)
                return;
            Order order = execution.Order;
            if (order.Name != EntrySignalName && order.Name != StopLossSignalName
                && order.Name != TakeProfitName && order.Name != StopFallbackName)
                return;
            lock (exitOrdersSync)
            {
                int executed;
                stopFallbackExecutions.TryGetValue(order, out executed);
                stopFallbackExecutions[order] = executed + quantity;
                if (order.Name == StopFallbackName && order.OrderState == OrderState.Filled
                    && executed + quantity >= order.Filled)
                    stopFallbackOrder = null;
            }
        }

        private bool IsStopFallbackOrderSettled(Order order)
        {
            if (order == null)
                return true;
            int executed;
            stopFallbackExecutions.TryGetValue(order, out executed);
            // A terminal order update can precede its execution callback. Wait for
            // that callback so Position.Quantity includes fills during cancellation.
            return Order.IsTerminalState(order.OrderState) && order.Filled <= executed;
        }

        private void CancelForStopFallback(Order order)
        {
            if (order == null || Order.IsTerminalState(order.OrderState)
                || order.OrderState == OrderState.CancelPending || order.OrderState == OrderState.CancelSubmitted)
                return;
            try { CancelOrder(order); }
            catch (Exception ex)
            {
                Print($"[{Core.Globals.Now}] [{EntrySignalName}] Stop timeout cancellation failed "
                    + $"for {order.Name}: {ex.Message}; will retry");
            }
        }

        private void CheckStopFallback()
        {
            if (State != State.Realtime)
                return;

            Order[] stops;
            Order[] targets;
            Order entry;
            DateTime now = Core.Globals.Now; // Follows Playback time as well as live time.
            lock (exitOrdersSync)
            {
                if (stopTriggeredAt == Core.Globals.MinDate && !stopFallbackRequested)
                    return;
                if (!stopFallbackRequested)
                {
                    if (Position.MarketPosition == MarketPosition.Flat
                        && IsStopFallbackOrderSettled(stopFallbackEntryOrder))
                    {
                        stopTriggeredAt = Core.Globals.MinDate;
                        return;
                    }
                    if ((now - stopTriggeredAt).TotalSeconds < 60)
                        return;
                    stopFallbackRequested = true;
                    Print($"[{now}] [{EntrySignalName}] Stop timeout: cancelling entry/stop/target orders "
                        + "before selling the remaining position at market");
                }
                stops = stopOrders.ToArray();
                targets = takeProfitOrders.ToArray();
                entry = stopFallbackEntryOrder;
            }

            // Never hold our tracking lock over a managed-order API call; it can
            // synchronously re-enter OnOrderUpdate/OnExecutionUpdate.
            CancelForStopFallback(entry);
            foreach (Order order in stops)
                CancelForStopFallback(order);
            foreach (Order order in targets)
                CancelForStopFallback(order);

            int remaining;
            lock (exitOrdersSync)
            {
                if (!IsStopFallbackOrderSettled(stopFallbackEntryOrder))
                    return;
                foreach (Order order in stopOrders)
                    if (!IsStopFallbackOrderSettled(order))
                        return;
                foreach (Order order in takeProfitOrders)
                    if (!IsStopFallbackOrderSettled(order))
                        return;
                if (stopFallbackSubmitting || (stopFallbackOrder != null
                    && (!IsStopFallbackOrderSettled(stopFallbackOrder)
                        || stopFallbackOrder.OrderState == OrderState.Filled)))
                    return;
                if (Position.MarketPosition == MarketPosition.Flat)
                {
                    ResetStopFallback();
                    return;
                }
                if (Position.MarketPosition != MarketPosition.Long || Position.Quantity <= 0
                    || (now - lastStopFallbackAttempt).TotalSeconds < 1)
                    return;
                remaining = Position.Quantity;
                stopFallbackSubmitting = true;
                lastStopFallbackAttempt = now;
            }

            try
            {
                Order submitted = ExitLong(0, remaining, StopFallbackName, EntrySignalName);
                lock (exitOrdersSync)
                {
                    if (submitted != null && !Order.IsTerminalState(submitted.OrderState))
                        stopFallbackOrder = submitted;
                }
                Print($"[{now}] [{EntrySignalName}] Stop timeout market exit quantity={remaining}: "
                    + (submitted == null ? "API returned no order; will retry" : submitted.OrderState.ToString()));
            }
            catch (Exception ex)
            {
                Print($"[{now}] [{EntrySignalName}] Stop timeout market exit failed: {ex.Message}; will retry");
            }
            finally
            {
                lock (exitOrdersSync)
                    stopFallbackSubmitting = false;
            }
        }

        // Called only for a red entry candidate, after open-position exits.
        // A previous-session signal at the next session open is unknown, as in MT5.
        private bool IsVwapEntryRejected(DateTime windowTime)
        {
            if (!UseVwapGate && !LogVwapDecisions)
                return false;

            bool sameSession = sessionVwap != null
                && sessionVwap.SessionDate(Time[0].AddMinutes(-30))
                    == sessionVwap.SessionDate(windowTime);
            bool known = sameSession && sessionVwap.IsKnown;
            double vwap = known ? sessionVwap.SessionVWAP[0] : double.NaN;
            double a20 = known ? sessionVwap.A20[0] : double.NaN;
            double gate = known ? sessionVwap.GateLine[0] : double.NaN;
            bool rejected = UseVwapGate && known && Close[0] < gate;

            if (LogVwapDecisions || rejected)
                Print(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[{0:yyyy-MM-dd HH:mm:ss}] [{1}] VWAP gate: close={2:R}, vwap={3:R}, "
                    + "A20={4:R}, line={5:R}, known={6}, enabled={7}, decision={8}",
                    Time[0], EntrySignalName, Close[0], vwap, a20, gate, known, UseVwapGate,
                    rejected ? "SKIP" : (known ? "ALLOW" : "ALLOW (unknown)")));
            return rejected;
        }

        private bool ExceedsMaxRedRun()
        {
            if (MaxRedRun <= 0)
                return false;

            // Calculate.OnBarClose: [0] is the just-closed signal candle.
            // Scan loaded bars directly so earlier reds still count after a
            // window/session boundary or the transition into realtime.
            int redRun = 0;
            for (int barsAgo = 0; barsAgo <= CurrentBar; barsAgo++)
            {
                if (Close[barsAgo] >= Open[barsAgo])
                    break;

                redRun++;
                if (redRun > MaxRedRun)
                    return true; // No need to scan beyond cap + 1 reds.
            }
            return false;
        }

        private bool IsTradeWindow(DateTime time)
        {
            return GetWindowRiskReward(time) > 0;
        }

        private double GetWindowRiskReward(DateTime time)
        {
            return windowRiskRewards[time.Hour];
        }

        // MT5 checks the window when the bar after the signal candle opens. Inside a
        // session that is the signal bar's close, Time[0]. The session's last bar is
        // stamped with the session end (e.g. 00:00), but NinjaTrader only processes it on
        // the next session's first tick, and MT5 only sees it once that session opens - so
        // it belongs to the next session's opening-hour window, as in the MT5 backtests.
        private DateTime GetSignalWindowTime()
        {
            if (sessionIterator == null)
                return Time[0];

            sessionIterator.GetNextSession(Time[0], true);
            if (Time[0] < sessionIterator.ActualSessionEnd)
                return Time[0];

            // Excluding the end stamp moves past the session this bar closed.
            sessionIterator.GetNextSession(Time[0], false);
            DateTime nextSessionBegin = sessionIterator.ActualSessionBegin;
            return nextSessionBegin > Time[0] ? nextSessionBegin : Time[0];
        }

        // With Calculate.OnBarClose the signal bar is closed by the first tick of the next
        // bar, which realtime Bars already holds. NaN when that bar is not available.
        private double GetNextBarHigh()
        {
            int next = CurrentBar + 1;
            return Bars != null && Bars.Count > next ? Bars.GetHigh(next) : double.NaN;
        }

        // Cancels the working entry when its buy stop was not reached within
        // EntryExpiryBars bars after the signal that priced it. A reached stop is
        // left to the 25-minute entry timeout.
        private void CancelExpiredEntry()
        {
            if (EntryExpiryBars <= 0 || entryOrderBar < 0 || !IsActiveOrder(longOrder)
                || CurrentBar - entryOrderBar < EntryExpiryBars || longOrder.Filled > 0
                || longOrder.OrderState == OrderState.CancelPending
                || longOrder.OrderState == OrderState.CancelSubmitted)
                return;
            lock (exitOrdersSync)
                if (entryTriggeredAt != Core.Globals.MinDate)
                    return;

            Print($"[{Time[0]}] [{EntrySignalName}] ⏱ Entry from {entryOrderBarTime} not triggered within "
                + $"{EntryExpiryBars} bars → cancelling pending order @ {longOrder.StopPrice}");
            CancelOrder(longOrder);
        }

        private bool IsActiveOrder(Order order)
        {
            return order != null &&
                (order.OrderState == OrderState.Submitted ||
                 order.OrderState == OrderState.Accepted ||
                 order.OrderState == OrderState.Working ||
                 order.OrderState == OrderState.PartFilled ||
                 order.OrderState == OrderState.ChangeSubmitted ||
                 order.OrderState == OrderState.CancelSubmitted);
        }

        private void TrackOrder(List<Order> orders, Order order)
        {
            if (order == null)
                return;

            lock (exitOrdersSync)
            {
                for (int i = 0; i < orders.Count; i++)
                {
                    if (orders[i] == order || orders[i].OrderId == order.OrderId)
                    {
                        orders[i] = order;
                        return;
                    }
                }
                orders.Add(order);
            }
        }

        private void CancelActiveOrders(List<Order> orders)
        {
            Order[] snapshot;
            lock (exitOrdersSync)
                snapshot = orders.ToArray();
            foreach (Order order in snapshot)
            {
                if (IsActiveOrder(order))
                    CancelOrder(order);
            }
        }

        private void ResetExitTracking()
        {
            lock (exitOrdersSync)
            {
                ResetStopFallback();
                takeProfitSubmitted = false;
                stopOrders.Clear();
                takeProfitOrders.Clear();
            }
        }

        protected override void OnBarUpdate()
        {
            // Minute bars feed the indicator only. All orders retain primary-bar timing.
            if (BarsInProgress != 0)
                return;
            // Warm history and keep the plot current even outside windows/in a position.
            if (sessionVwap != null)
                sessionVwap.Update();

            if (IsStopFallbackActive())
                return;
			if (State != State.Realtime)
			    return;

            CheckEntryTimeout();

            // === End of session FLATTEN LOGIC ===
            if (ToTime(Time[0]) >= 235700 && ToTime(Time[0]) < 235800)
			{
				if (lastFlattenDate.Date != Time[0].Date)
				{
					lastFlattenDate = Time[0];
					Print($"[{Time[0]}] [{EntrySignalName}] ❌ End of session FLATTEN → all positions & orders cleared");

					if (Position.MarketPosition == MarketPosition.Long)
						ExitLong(FlattenName, EntrySignalName);

					if (longOrder != null &&
						(longOrder.OrderState == OrderState.Working ||
						 longOrder.OrderState == OrderState.Accepted))
						CancelOrder(longOrder);
				}
				return;
			}

            if (CurrentBar < BarsRequiredToTrade) return;
            if (State != State.Realtime) return;

			/// EXIT BLOCK
            // R:R target uses the value captured from the entry signal's time window
			if (Position.MarketPosition == MarketPosition.Long)
            {
                double targetPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice + (riskPerTrade * positionRiskReward));

				if (!takeProfitSubmitted && positionRiskReward > 0 && Close[0] >= targetPrice)
				{
					double exitLimitPrice = Instrument.MasterInstrument.RoundToTickSize(Close[0] + (ExitOffset * TickSize));

					Print($"[{Time[0]}] [{EntrySignalName}] {positionRiskReward}R reached: Bar Close={Close[0]}, Target={targetPrice}");
					ExitLongLimit(0, true, Position.Quantity, exitLimitPrice, TakeProfitName, EntrySignalName);
                    takeProfitSubmitted = true;
					Print($"[{Time[0]}] [{EntrySignalName}] Limit order submitted @ {exitLimitPrice} (target={targetPrice}, offset={ExitOffset} ticks, signal={TakeProfitName})");
				}
				return;
			}

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            if (IsEntryTimeoutCancellationPending())
                return;

            // 🔹 TRADE WINDOW (ENTRY ONLY)
            if (takeProfitSubmitted || stopOrders.Count > 0 || takeProfitOrders.Count > 0)
                ResetExitTracking();

            DateTime windowTime = GetSignalWindowTime();
            bool inWindow = IsTradeWindow(windowTime);

            if (inWindow != lastWindowState)
            {
                Print($"[{Time[0]}] [{EntrySignalName}] Trade window state changed -> {(inWindow ? "INSIDE" : "OUTSIDE")}");
                lastWindowState = inWindow;
            }

            if (!inWindow)
            {
                if (longOrder != null &&
                    (longOrder.OrderState == OrderState.Working || longOrder.OrderState == OrderState.Accepted))
                {
                    Print($"[{Time[0]}] [{EntrySignalName}] ⏱ Outside window → cancelling pending order @ {longOrder.StopPrice}");
                    CancelOrder(longOrder);
                }
                return;
            }

            // 🔹 Red candle logic
			/// ENTRY BLOCK
            if (Close[0] < Open[0])
			{
                // Filter only new entries; open-position exits are handled above.
                // Match MT5: a rejected red cancels the old pending entry;
                // a green/doji bar does not cancel it merely because of this filter.
                if (ExceedsMaxRedRun())
                {
                    Print($"[{Time[0]}] [{EntrySignalName}] Red run exceeds MaxRedRun={MaxRedRun} - skipping entry");
                    if (IsActiveOrder(longOrder) &&
                        longOrder.OrderState != OrderState.CancelPending &&
                        longOrder.OrderState != OrderState.CancelSubmitted)
                        CancelOrder(longOrder);
                    return;
                }

                if (IsVwapEntryRejected(windowTime))
                {
                    if (IsActiveOrder(longOrder) &&
                        longOrder.OrderState != OrderState.CancelPending &&
                        longOrder.OrderState != OrderState.CancelSubmitted)
                        CancelOrder(longOrder);
                    return;
                }

				entryPrice = High[0];
				pendingStopPrice = Low[0];
				riskPerTrade = entryPrice - pendingStopPrice;
                if (windowTime != Time[0])
                    Print($"[{Time[0]}] [{EntrySignalName}] Session-open signal: previous session's last candle "
                        + $"evaluated for the {windowTime:HH:mm} window");

				pendingRiskReward = GetWindowRiskReward(windowTime);
				Print($"[{Time[0]}] [{EntrySignalName}] Window R:R={pendingRiskReward}");

                if (pendingRiskReward <= 0)
                {
                    Print($"[{Time[0]}] [{EntrySignalName}] Window R:R is 0 - skipping entry");
                    return;
                }

				Print($"[{Time[0]}] [{EntrySignalName}] 🔴 Red candle detected -> evaluating entry");
				Print($"[{Time[0]}] [{EntrySignalName}] Entry={entryPrice} SL={pendingStopPrice} Risk={riskPerTrade}");

				double ask = GetCurrentAsk();
				// The bar after the signal has already started; its high is every trade
				// printed since. At a session open the ask can still be the previous
				// session's stale quote, but this bar's high is the new session's price.
				double nextBarHigh = GetNextBarHigh();

				if (ask >= entryPrice
					|| (!double.IsNaN(nextBarHigh) && nextBarHigh >= entryPrice))
				{
					Print($"[{Time[0]}] [{EntrySignalName}] ⚠️ Gap above entry → skipping stop placement "
						+ $"(entry={entryPrice}, ask={ask}, next bar high={nextBarHigh})");
					CancelExpiredEntry();
					return;
				}

				ResetExitTracking();
				longOrder = EnterLongStopLimit(0, true, EntryQuantity, entryPrice, entryPrice, EntrySignalName);
				Print($"[{Time[0]}] [{EntrySignalName}] Submitted BUY STOP-LIMIT @ {entryPrice}");
				if (longOrder != null)
				{
					entryOrderBar = CurrentBar;
					entryOrderBarTime = Time[0];
				}
			}

            CancelExpiredEntry();
        }

		/// STOP-LOSS ORDERS BLOCK
        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            RecordEntryTimeoutExecution(execution, quantity);
            RecordStopFallbackExecution(execution, quantity);
            if (IsStopFallbackActive())
                return;
            if (execution != null && execution.Order != null && execution.Order.Name == EntrySignalName)
            {
                takeProfitSubmitted = false;

                if (quantity > 0 && pendingRiskReward > 0)
                    positionRiskReward = pendingRiskReward;

                if (quantity > 0 && pendingStopPrice > 0)
                {
                    entryPrice = execution.Order.AverageFillPrice;

                    if (pendingStopPrice > 0)
                    {
                        riskPerTrade = entryPrice - pendingStopPrice;
                        double limitPrice = pendingStopPrice - TickSize;	// Can be used in ExitLongStopLimit (currently 2*pendingStopPrice instead)

                        Print($"[{time}] [{EntrySignalName}] 🚀 Entry FILLED at {entryPrice} - Submitting STOP-LIMIT immediately");
                        Print($"[{time}] [{EntrySignalName}]    Stop={pendingStopPrice}, Limit={limitPrice}, Risk={riskPerTrade}");

						ExitLongStopLimit(0, true, execution.Order.Filled, pendingStopPrice, pendingStopPrice, StopLossSignalName, EntrySignalName);
                    }
                }

                // Clear local reference once our own entry order is filled
                if (execution.Order.OrderState == OrderState.Filled)
                {
                    longOrder = null;
                }
            }
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice, OrderState orderState,
            DateTime time, ErrorCode error, string nativeError)
        {
            if (order == null)
                return;
            // Only track orders that belong to this instance
            if (order.Name == EntrySignalName)
            {
                TrackEntryTimeoutOrder(order, orderState, filled, quantity);
                lock (exitOrdersSync)
                    stopFallbackEntryOrder = order;
                longOrder = order;
            }
            else if (order.Name == StopLossSignalName)
            {
                TrackOrder(stopOrders, order);

                // A partial fill also confirms triggering if the price tick was missed.
                if (filled > 0 && filled < quantity)
                {
                    lock (exitOrdersSync)
                        if (stopTriggeredAt == Core.Globals.MinDate)
                            stopTriggeredAt = Core.Globals.Now;
                }

                if (orderState == OrderState.Filled)
                    CancelActiveOrders(takeProfitOrders);
            }
            else if (order.Name == TakeProfitName)
            {
                TrackOrder(takeProfitOrders, order);

                if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
                    takeProfitSubmitted = false;

                if (orderState == OrderState.Filled)
                    CancelActiveOrders(stopOrders);
            }
            else if (order.Name == StopFallbackName)
            {
                lock (exitOrdersSync)
                    stopFallbackOrder = order;
                if (error != ErrorCode.NoError || orderState == OrderState.Rejected)
                    Print($"[{time}] [{EntrySignalName}] Stop timeout market order error: {error} {nativeError}");
            }
        }
    }
}
