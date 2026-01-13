#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using System.ComponentModel.DataAnnotations;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class RTLongOnlyTimeWindows : Strategy
    {
        private Order longOrder;
        private double pendingStopPrice;
        private double entryPrice;
        private double riskPerTrade;
        private DateTime lastFlattenDate = Core.Globals.MinDate;

        // ===== TIME WINDOW INPUTS =====
        [NinjaScriptProperty]
        [Display(Name = "Use Trade Window", Order = 0, GroupName = "Trade Windows")]
        public bool UseTradeWindow { get; set; }

        [NinjaScriptProperty] public bool W00 { get; set; }// 00:00–00:30
		[NinjaScriptProperty] public bool W01 { get; set; }// 00:30–01:00
		[NinjaScriptProperty] public bool W02 { get; set; }// 01:00–01:30
		[NinjaScriptProperty] public bool W03 { get; set; }// 01:30–02:00
		[NinjaScriptProperty] public bool W04 { get; set; }// 02:00–02:30
		[NinjaScriptProperty] public bool W05 { get; set; }// 02:30–03:00
		[NinjaScriptProperty] public bool W06 { get; set; }// 03:00–03:30
		[NinjaScriptProperty] public bool W07 { get; set; }// 03:30–04:00
		[NinjaScriptProperty] public bool W08 { get; set; }// 04:00–04:30
		[NinjaScriptProperty] public bool W09 { get; set; }// 04:30–05:00
		[NinjaScriptProperty] public bool W10 { get; set; }// 05:00–05:30
		[NinjaScriptProperty] public bool W11 { get; set; }// 05:30–06:00
		[NinjaScriptProperty] public bool W12 { get; set; }// 06:00–06:30
		[NinjaScriptProperty] public bool W13 { get; set; }// 06:30–07:00
		[NinjaScriptProperty] public bool W14 { get; set; }// 07:00–07:30
		[NinjaScriptProperty] public bool W15 { get; set; }// 07:30–08:00
		[NinjaScriptProperty] public bool W16 { get; set; }// 08:00–08:30
		[NinjaScriptProperty] public bool W17 { get; set; }// 08:30–09:00
		[NinjaScriptProperty] public bool W18 { get; set; }// 09:00–09:30
		[NinjaScriptProperty] public bool W19 { get; set; }// 09:30–10:00
		[NinjaScriptProperty] public bool W20 { get; set; }// 10:00–10:30
		[NinjaScriptProperty] public bool W21 { get; set; }// 10:30–11:00
		[NinjaScriptProperty] public bool W22 { get; set; }// 11:00–11:30
		[NinjaScriptProperty] public bool W23 { get; set; }// 11:30–12:00
		[NinjaScriptProperty] public bool W24 { get; set; }// 12:00–12:30
		[NinjaScriptProperty] public bool W25 { get; set; }// 12:30–13:00
		[NinjaScriptProperty] public bool W26 { get; set; }// 13:00–13:30
		[NinjaScriptProperty] public bool W27 { get; set; }// 13:30–14:00
		[NinjaScriptProperty] public bool W28 { get; set; }// 14:00–14:30
		[NinjaScriptProperty] public bool W29 { get; set; }// 14:30–15:00
		[NinjaScriptProperty] public bool W30 { get; set; }// 15:00–15:30
		[NinjaScriptProperty] public bool W31 { get; set; }// 15:30–16:00
		[NinjaScriptProperty] public bool W32 { get; set; }// 16:00–16:30
		[NinjaScriptProperty] public bool W33 { get; set; }// 16:30–17:00
		[NinjaScriptProperty] public bool W34 { get; set; }// 17:00–17:30
		[NinjaScriptProperty] public bool W35 { get; set; }// 17:30–18:00
		[NinjaScriptProperty] public bool W36 { get; set; }// 18:00–18:30
		[NinjaScriptProperty] public bool W37 { get; set; }// 18:30–19:00
		[NinjaScriptProperty] public bool W38 { get; set; }// 19:00–19:30
		[NinjaScriptProperty] public bool W39 { get; set; }// 19:30–20:00
		[NinjaScriptProperty] public bool W40 { get; set; }// 20:00–20:30
		[NinjaScriptProperty] public bool W41 { get; set; }// 20:30–21:00
		[NinjaScriptProperty] public bool W42 { get; set; }// 21:00–21:30
		[NinjaScriptProperty] public bool W43 { get; set; }// 21:30–22:00
		[NinjaScriptProperty] public bool W44 { get; set; }// 22:00–22:30
		[NinjaScriptProperty] public bool W45 { get; set; }// 22:30–23:00
		[NinjaScriptProperty] public bool W46 { get; set; }// 23:00–23:30
		[NinjaScriptProperty] public bool W47 { get; set; }// 23:30–00:00

        private bool[] tradeWindows;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "RTLongOnlyTimeWindows";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                BarsRequiredToTrade = 5;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                StartBehavior = StartBehavior.ImmediatelySubmit;
                IsUnmanaged = false;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;

                UseTradeWindow = true;
            }
            else if (State == State.DataLoaded)
            {
                tradeWindows = new bool[]
                {
                    W00,W01,W02,W03,W04,W05,W06,W07,W08,W09,W10,W11,
                    W12,W13,W14,W15,W16,W17,W18,W19,W20,W21,W22,W23,
                    W24,W25,W26,W27,W28,W29,W30,W31,W32,W33,W34,W35,
                    W36,W37,W38,W39,W40,W41,W42,W43,W44,W45,W46,W47
                };
            }
            else if (State == State.Realtime)
            {
                longOrder = null;
                pendingStopPrice = 0;
                entryPrice = 0;
                riskPerTrade = 0;
                Print("=== Strategy entering REALTIME mode ===");
            }
        }

        private bool IsTradeWindow(DateTime time)
        {
            if (!UseTradeWindow)
                return true;

            int minutes = time.Hour * 60 + time.Minute;
            int slot = minutes / 30;

            if (slot < 0 || slot > 47)
                return false;

            return tradeWindows[slot];
        }

        protected override void OnBarUpdate()
        {
            // === DAILY FLATTEN LOGIC ===
            if (ToTime(Time[0]) >= 170000 && ToTime(Time[0]) < 170100)
            {
                if (lastFlattenDate.Date != Time[0].Date)
                {
                    lastFlattenDate = Time[0];

                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong("DailyFlatten", "Long1");

                    if (longOrder != null &&
                        (longOrder.OrderState == OrderState.Working || longOrder.OrderState == OrderState.Accepted))
                        CancelOrder(longOrder);
                }
                return;
            }

            if (CurrentBar < BarsRequiredToTrade) return;
            if (State != State.Realtime) return;

            // 🔹 R:R flatten
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (Close[0] - entryPrice >= riskPerTrade)
                    ExitLong("RR_Flatten", "Long1");

                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            // 🔹 TRADE WINDOW (ENTRY ONLY)
            if (!IsTradeWindow(Time[0]))
            {
                Print($"[{Time[0]}] ⏱ Outside trading window → no entry");

                if (longOrder != null &&
                    (longOrder.OrderState == OrderState.Working || longOrder.OrderState == OrderState.Accepted))
                    CancelOrder(longOrder);

                return;
            }

            // 🔹 Red candle logic
            if (Close[0] < Open[0])
            {
                entryPrice = High[0] + TickSize;
                pendingStopPrice = Low[0] - TickSize;
                riskPerTrade = entryPrice - pendingStopPrice;

                SetStopLoss("Long1", CalculationMode.Price, pendingStopPrice, false);

                if (Close[0] > entryPrice)
                    longOrder = EnterLongLimit(0, true, 1, entryPrice, "Long1");
                else
                    longOrder = EnterLongStopLimit(0, true, 1, entryPrice, entryPrice, "Long1");
            }
        }
    }
}