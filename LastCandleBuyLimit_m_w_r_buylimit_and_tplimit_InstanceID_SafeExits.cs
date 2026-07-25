#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
#endregion

// Port of LastCandleBuyLimitEA (MT5) into the SafeExits NT8 structure.
//
// On every closed bar (red or green):
//   - place a BUY LIMIT at the bar's LOW; if it doesn't trigger, the order is
//     moved to the next bar's low (Cancel On New Bar), so at most one pending
//     order lives at a time
// When the entry fills, BOTH exits are submitted immediately:
//   - SL stop-limit at entry - SL offset (ticks)
//   - TP sell limit at entry + signal candle range x multiplier
//     (multiplier 1 = the signal candle's high)
// The position closes when either working order is reached — not on bar close.
// Entries are allowed only inside the enabled time windows, and everything
// is flattened at the configurable end-of-session time.

namespace NinjaTrader.NinjaScript.Strategies
{
    public class LastCandleBuyLimitTimeWinTPlimitSafeExits : Strategy
    {
        private Order longOrder;
        private double pendingStopPrice;
        private double entryPrice;
        private double riskPerTrade;
        private double pendingCandleRange;
        private double positionCandleRange;
        private readonly List<Order> stopOrders = new List<Order>();
        private readonly List<Order> takeProfitOrders = new List<Order>();
        private DateTime lastFlattenDate = Core.Globals.MinDate;
        private bool lastWindowState = false;

        // Derived signal names — all unique per instance to prevent cross-instance interference
        private string EntrySignalName    => $"LastCandleBL_{InstanceId}";
        private string StopLossSignalName => $"StopLimit_{InstanceId}";
        private string TakeProfitName     => $"TP_Limit_{InstanceId}";
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

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SL Offset (ticks)", Order = 4, GroupName = "Risk Management",
                 Description = "Stop-loss distance below the entry price, in ticks")]
        public int SLOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cancel On New Bar", Order = 5, GroupName = "Risk Management",
                 Description = "Re-submit the pending buy limit at the new bar's low each bar; off = leave the resting order untouched")]
        public bool CancelOnNewBar { get; set; }

        // ===== TAKE PROFIT =====
        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "TP Range Multiplier", Order = 0, GroupName = "Take Profit",
                 Description = "TP = entry + signal candle range x this. Multiplier 1 = signal candle's high")]
        public double TPRangeMult { get; set; }

        // ===== TIME WINDOW INPUTS =====
        // Enabled window = trading ALLOWED in that slot. With Use Trade Window on
        // and every slot off, no entries are taken at all.
		[NinjaScriptProperty]
		[Display(Name = "Use Trade Window", Order = 0, GroupName = "Trade Windows")]
		public bool UseTradeWindow { get; set; }

        [NinjaScriptProperty]
        [Display(Name="00:00–01:00", Order=1, GroupName="Trade Windows")]
        public bool W00 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="01:00–02:00", Order=2, GroupName="Trade Windows")]
		public bool W01 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="02:00–03:00", Order=3, GroupName="Trade Windows")]
		public bool W02 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="03:00–04:00", Order=4, GroupName="Trade Windows")]
		public bool W03 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="04:00–05:00", Order=5, GroupName="Trade Windows")]
		public bool W04 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="05:00–06:00", Order=6, GroupName="Trade Windows")]
		public bool W05 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="06:00–07:00", Order=7, GroupName="Trade Windows")]
		public bool W06 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="07:00–08:00", Order=8, GroupName="Trade Windows")]
		public bool W07 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="08:00–09:00", Order=9, GroupName="Trade Windows")]
		public bool W08 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="09:00–10:00", Order=10, GroupName="Trade Windows")]
		public bool W09 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="10:00–11:00", Order=11, GroupName="Trade Windows")]
		public bool W10 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="11:00–12:00", Order=12, GroupName="Trade Windows")]
		public bool W11 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="12:00–13:00", Order=13, GroupName="Trade Windows")]
		public bool W12 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="13:00–14:00", Order=14, GroupName="Trade Windows")]
		public bool W13 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="14:00–15:00", Order=15, GroupName="Trade Windows")]
		public bool W14 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="15:00–16:00", Order=16, GroupName="Trade Windows")]
		public bool W15 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="16:00–17:00", Order=17, GroupName="Trade Windows")]
		public bool W16 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="17:00–18:00", Order=18, GroupName="Trade Windows")]
		public bool W17 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="18:00–19:00", Order=19, GroupName="Trade Windows")]
		public bool W18 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="19:00–20:00", Order=20, GroupName="Trade Windows")]
		public bool W19 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="20:00–21:00", Order=21, GroupName="Trade Windows")]
		public bool W20 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="21:00–22:00", Order=22, GroupName="Trade Windows")]
		public bool W21 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="22:00–23:00", Order=23, GroupName="Trade Windows")]
		public bool W22 { get; set; }

		[NinjaScriptProperty]
		[Display(Name="23:00–00:00", Order=24, GroupName="Trade Windows")]
		public bool W23 { get; set; }

        // ===== FLATTEN END OF SESSION =====
        [NinjaScriptProperty]
        [Display(Name = "Use Flatten End", Order = 0, GroupName = "Flatten End Of Session")]
        public bool UseFlattenEnd { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Flatten Hour", Order = 1, GroupName = "Flatten End Of Session")]
        public int FlattenHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Flatten Minute", Order = 2, GroupName = "Flatten End Of Session")]
        public int FlattenMinute { get; set; }

        private bool[] tradeWindows;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "LastCandleBuyLimitTimeWinTPlimitSafeExits";
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
                SLOffsetTicks = 100;
                CancelOnNewBar = true;
                TPRangeMult = 1.0;
                UseTradeWindow = true;
                UseFlattenEnd = true;
                FlattenHour = 23;
                FlattenMinute = 57;
                W00 = W01 = W02 = W03 = W04 = W05 = false;
                W06 = W07 = W08 = W09 = W10 = W11 = false;
                W12 = W13 = W14 = W15 = W16 = W17 = false;
                W18 = W19 = W20 = W21 = W22 = W23 = false;
            }
            else if (State == State.DataLoaded)
            {
                tradeWindows = new bool[]
                {
                    W00, W01, W02, W03, W04, W05,
                    W06, W07, W08, W09, W10, W11,
                    W12, W13, W14, W15, W16, W17,
                    W18, W19, W20, W21, W22, W23
                };
            }
            else if (State == State.Realtime)
            {
                longOrder = null;
                pendingStopPrice = 0;
                entryPrice = 0;
                riskPerTrade = 0;
                pendingCandleRange = 0;
                positionCandleRange = 0;
                stopOrders.Clear();
                takeProfitOrders.Clear();
                Print($"=== Strategy entering REALTIME mode (Instance {InstanceId}, signal={EntrySignalName}) ===");
                Print($"=== SL offset={SLOffsetTicks} ticks, TP=candle range x {TPRangeMult}, cancel on new bar={CancelOnNewBar} ===");
            }
        }

        private bool IsTradeWindow(DateTime time)
        {
            if (!UseTradeWindow)
                return true;

            return tradeWindows[time.Hour];
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

        private void CancelActiveOrders(List<Order> orders)
        {
            foreach (Order order in orders)
            {
                if (IsActiveOrder(order))
                    CancelOrder(order);
            }
        }

        private bool IsFlattenTime(DateTime time)
        {
            int flattenStart = FlattenHour * 10000 + FlattenMinute * 100;
            int t = ToTime(time);
            return t >= flattenStart && t < flattenStart + 100;
        }

        protected override void OnBarUpdate()
        {
			if (State != State.Realtime)
			    return;

            // === End of session FLATTEN LOGIC ===
            if (UseFlattenEnd && IsFlattenTime(Time[0]))
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

            // SL and TP are working orders submitted on fill — nothing to do while long
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            // 🔹 TRADE WINDOW (ENTRY ONLY)
            bool inWindow = IsTradeWindow(Time[0]);

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
                    Print($"[{Time[0]}] [{EntrySignalName}] ⏱ Outside window → cancelling pending order @ {longOrder.LimitPrice}");
                    CancelOrder(longOrder);
                }
                return;
            }

			/// ENTRY BLOCK
            // Any candle, red or green: buy limit at the low of the bar that just closed
            if (High[0] <= Low[0])
                return;

            if (!CancelOnNewBar && IsActiveOrder(longOrder))
                return;     // keep the resting order untouched

			entryPrice = Low[0];
			pendingStopPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice - SLOffsetTicks * TickSize);
			riskPerTrade = entryPrice - pendingStopPrice;
			pendingCandleRange = High[0] - Low[0];

			Print($"[{Time[0]}] [{EntrySignalName}] 🕯 Last candle closed -> evaluating buy limit");
			Print($"[{Time[0]}] [{EntrySignalName}] Entry={entryPrice} SL={pendingStopPrice} Risk={riskPerTrade} Range={pendingCandleRange}");

			double ask = GetCurrentAsk();

			if (ask <= entryPrice)
			{
				Print($"[{Time[0]}] [{EntrySignalName}] ⚠️ Price already at/below entry → skipping limit placement");
				return;
			}

			// Same signal name + liveUntilCancelled: NT8 amends the working order, so
			// CancelOnNewBar just moves the limit to the new bar's low each bar
			longOrder = EnterLongLimit(0, true, EntryQuantity, entryPrice, EntrySignalName);
			Print($"[{Time[0]}] [{EntrySignalName}] Submitted BUY LIMIT @ {entryPrice}");
        }

		/// EXIT ORDERS BLOCK — SL and TP submitted together as soon as the entry fills
        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order != null && execution.Order.Name == EntrySignalName)
            {
                if (quantity > 0 && pendingCandleRange > 0)
                    positionCandleRange = pendingCandleRange;

                if (quantity > 0 && pendingStopPrice > 0)
                {
                    entryPrice = execution.Order.AverageFillPrice;
                    riskPerTrade = entryPrice - pendingStopPrice;
                    double takeProfitPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice + positionCandleRange * TPRangeMult);

                    Print($"[{time}] [{EntrySignalName}] 🚀 Entry FILLED at {entryPrice} - Submitting STOP-LIMIT + TP LIMIT immediately");
                    Print($"[{time}] [{EntrySignalName}]    Stop={pendingStopPrice}, Risk={riskPerTrade}, TP={takeProfitPrice} (range {positionCandleRange} x {TPRangeMult})");

					ExitLongStopLimit(0, true, execution.Order.Filled, pendingStopPrice, pendingStopPrice, StopLossSignalName, EntrySignalName);
					ExitLongLimit(0, true, execution.Order.Filled, takeProfitPrice, TakeProfitName, EntrySignalName);
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
            // Only track orders that belong to this instance
            if (order.Name == EntrySignalName)
            {
                longOrder = order;
            }
            else if (order.Name == StopLossSignalName)
            {
                TrackOrder(stopOrders, order);

                if (orderState == OrderState.Filled)
                    CancelActiveOrders(takeProfitOrders);
            }
            else if (order.Name == TakeProfitName)
            {
                TrackOrder(takeProfitOrders, order);

                if (orderState == OrderState.Filled)
                    CancelActiveOrders(stopOrders);
            }
        }
    }
}
