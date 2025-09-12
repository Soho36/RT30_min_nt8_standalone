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
			if (State != State.Realtime) return;

			// Debug info for every bar
			Print($"[{Time[0]}] OnBarUpdate | O={Open[0]} H={High[0]} L={Low[0]} C={Close[0]} Ask={GetCurrentAsk()} Bid={GetCurrentBid()} Pos={Position.MarketPosition}");

			// Skip if we are already in a trade
			if (Position.MarketPosition != MarketPosition.Flat)
			{
				Print($"[{Time[0]}] Skipping — already in position ({Position.MarketPosition})");
				return;
			}

			// If candle is red, we may want to set/replace the buy-stop
			if (Close[0] < Open[0])
			{
				double entryPrice = High[0] + TickSize;     // safe: above the bar high
				double slCandidate = Low[0] - TickSize;
				pendingStopPrice = slCandidate;

				// If there is a working order, check if its stopPrice equals desired entryPrice
				if (longOrder != null && longOrder.OrderState == OrderState.Working)
				{
					Print($"[{Time[0]}] Existing working order id={longOrder.OrderId} stopPrice={longOrder.StopPrice}");

					// Only replace if the stop price actually changed
					if (Math.Abs(longOrder.StopPrice - entryPrice) > 0.0000001) // tolerance for floating rounding
					{
						CancelOrder(longOrder);
						Print($"[{Time[0]}] Cancelled old LONG stop (id={longOrder.OrderId}) to replace with new price {entryPrice}");
					}
					else
					{
						Print($"[{Time[0]}] Existing LONG stop already at desired price {entryPrice} -> keep it");
						return; // nothing to do
					}
				}

				// Place new buy stop using explicit overload (avoid ambiguous mapping)
				longOrder = EnterLongStopMarket(0, true, 1, entryPrice, "Long1");
				Print($"[{Time[0]}] >>> Submitted new LONG stop @ {entryPrice}, SL candidate: {pendingStopPrice} (new order id will appear in OnOrderUpdate)");
			}
			else
			{
				// Green candle → do nothing, keep existing order alive if any
				Print($"[{Time[0]}] Green candle → keep working LONG stop alive (if any)");
			}
		}



        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
			int quantity, int filled, double averageFillPrice,
			OrderState orderState, DateTime time, ErrorCode error, string nativeError)
		{
			string id = order?.OrderId ?? "null";
			string name = order?.Name ?? "null";
			double sp = order?.StopPrice ?? double.NaN;
			Print($"[OnOrderUpdate] {time:dd.MM.yyyy HH:mm:ss} | id={id} name={name} state={orderState} filled={filled} stopPrice={sp} error={error} nativeError={nativeError}");
		}


       protected override void OnExecutionUpdate(Cbi.Execution execution, string executionId, double price, int quantity,
        Cbi.MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (execution.Order == null)
				return;

			// --- Long entry just filled ---
			if (execution.Order.Name == "Long1"
				&& execution.Order.OrderState == OrderState.Filled
				&& marketPosition == MarketPosition.Long)
			{
				if (slOrder == null || slOrder.OrderState == OrderState.Cancelled || slOrder.OrderState == OrderState.Filled)
				{
					slOrder = ExitLongStopMarket(
						execution.Order.Filled,    // same size as entry
						pendingStopPrice,          // SL under red candle low
						"SL_LastRed",              // signal name
						"Long1"                    // entry name it is tied to
					);

					Print($"[{time}] [SL SET] Stop-loss placed @ {pendingStopPrice}");
				}
			}

			// --- Only clean up if we are actually flat ---
			if (Position.MarketPosition == MarketPosition.Flat)
			{
				if (slOrder != null && slOrder.OrderState == OrderState.Working)
				{
					CancelOrder(slOrder);
					Print($"[{time}] Truly flat → canceled SL");
				}
				slOrder = null;
			}
		}

    }
}