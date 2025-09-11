#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class NT8LongOnly : Strategy
    {
        private Order longOrder;
        private Order slOrder;
        private double pendingStopPrice;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NT8LongOnly";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                BarsRequiredToTrade = 5;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                StartBehavior = StartBehavior.ImmediatelySubmit;
                IsUnmanaged = false;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
            }
            else if (State == State.Realtime)
            {
                longOrder = null;
                slOrder = null;
                pendingStopPrice = 0;
                Print("=== Strategy entering REALTIME mode ===");
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade) return;
			
			if (State != State.Realtime)
				return;

            // Debug prints about bar context
            Print($"[{Time[0]}] OnBarUpdate | O={Open[0]} H={High[0]} L={Low[0]} C={Close[0]} Ask={GetCurrentAsk()} Bid={GetCurrentBid()} Pos={Position.MarketPosition}");

            // --- Decision branches ---
			
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                Print($"[{Time[0]}] Skipping — already in position ({Position.MarketPosition})");
                return;
            }
			
            if (Close[0] >= Open[0])
            {
                Print($"[{Time[0]}] Skipping — candle not red (O={Open[0]}, C={Close[0]})");
                return;
            }

            // If we get here → flat AND red candle
            double entryPrice = High[0];  // (no TickSize for debugging exact match)
            pendingStopPrice = Low[0];

            // Cancel previous unfilled order
            if (longOrder != null)
            {
                Print($"[{Time[0]}] Existing order {longOrder.Name} state={longOrder.OrderState}");
                if (longOrder.OrderState == OrderState.Working)
                {
                    CancelOrder(longOrder);
                    Print($"[{Time[0]}] Cancelled old LONG stop");
                }
            }

            // Place new buy stop
            longOrder = EnterLongStopMarket(1, entryPrice, "Long1");
            Print($"[{Time[0]}] >>> Submitted new LONG stop @ {entryPrice}, SL candidate: {pendingStopPrice}");
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice,
            OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            Print($"[OnOrderUpdate] {time} | {order.Name} state={orderState} filled={filled} stopPrice={order.StopPrice} error={error} nativeError={nativeError}");
        }

        protected override void OnExecutionUpdate(Cbi.Execution execution, string executionId, double price, int quantity,
            Cbi.MarketPosition marketPosition, string orderId, DateTime time)
        {
            Print($"[OnExecutionUpdate] {time} | Order={execution.Order?.Name} state={execution.Order?.OrderState} price={price} marketPos={marketPosition}");

            // Only attach SL after the LONG entry is actually filled
            if (execution.Order != null
                && execution.Order.Name == "Long1"
                && execution.Order.OrderState == OrderState.Filled)
            {
                if (slOrder != null && slOrder.OrderState == OrderState.Working)
                    CancelOrder(slOrder);

                slOrder = ExitLongStopMarket(execution.Order.Filled, pendingStopPrice, "SL_LastRed", "Long1");
                Print($"[{time}] [SL SET] Stop-loss placed @ {pendingStopPrice}");
            }
        }
    }
}
