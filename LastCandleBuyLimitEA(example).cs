//+------------------------------------------------------------------+
//|                                          LastCandleBuyLimitEA.mq5 |
//|   Deliberately minimal starting point, derived from               |
//|   LevelRejectionEA.mq5 (order plumbing) and from                  |
//|   RR_r_MFE_buy-stop-entry (time windows + session flatten).       |
//|                                                                   |
//|   On every new bar:                                               |
//|     - take the candle that just closed (shift 1), red or green    |
//|     - place a BUY LIMIT at its LOW                                |
//|     - SL = entry - offset (input, points)                         |
//|     - TP, either mode (input):                                    |
//|         risk:reward  -> entry + offset * R:R                      |
//|         candle range -> entry + (high - low) * multiplier, so     |
//|                         multiplier 1 = the signal candle's high   |
//|   Any still-pending order from the previous bar is cancelled      |
//|   first, so at most one pending order lives at a time.            |
//|                                                                   |
//|   Entries are allowed only inside the enabled time windows, and   |
//|   everything is flattened at the session-end time.                |
//+------------------------------------------------------------------+
#property copyright "Level_rejection port"
#property version   "1.00"
#property strict

#include <Trade/Trade.mqh>

//====================================================================
//  INPUTS
//====================================================================

enum ENUM_TP_MODE
  {
   TP_RISK_REWARD,      // Risk:Reward multiple of the SL offset
   TP_CANDLE_RANGE      // Multiple of the signal candle range
  };

input group "=== Entry ==="
input double   InpSLoffsetPoints   = 100;      // SL offset below entry (points)
input bool     InpCancelOnNewBar   = true;     // Cancel the old pending order each new bar

input group "=== Take profit ==="
input ENUM_TP_MODE InpTPMode       = TP_RISK_REWARD; // How the TP is derived
input double   InpRiskReward       = 2.0;      // [Risk:Reward mode] TP = this x SL offset
input double   InpTPrangeMult      = 1.0;      // [Candle range mode] TP = low + range x this

input group "=== Orders ==="
input double   InpFixedLot         = 1;     // Lot size
input int      InpMaxPositions     = 1;        // Max simultaneous positions for this EA
input long     InpMagic            = 20240418; // Magic number

// flattening end session
input group "=== Flatten end of session ==="
input bool     UseFlattenEnd       = true;     // USE FLATTENING END SESSION
input int      FlattenHourEnd      = 23;
input int      FlattenMinuteEnd    = 30;

// ======== TIME WINDOW FILTERING ========
// Enabled window = trading ALLOWED in that slot. With UseTradeWindow = true
// and every slot false, no entries are taken at all.
input group "=== Time windows ==="
input bool     UseTradeWindow      = true;     // USE TIME TRADE WINDOW

// ========== SESSION 1: MARKET CLOSED (00:00-01:00) ==========
input bool W0000W0100 = false;  // 00:00–01:00 (Market Closed)

// ========== SESSION 2: MORNING SESSION (01:00-10:00) ==========
// 01:00-02:00 split into 30-min intervals
input bool W0100W0130 = false;  // 01:00–01:30 (Morning Session)
input bool W0130W0200 = false;  // 01:30–02:00 (Morning Session)

// 1-hour intervals for 02:00-10:00
input bool W0200W0300 = false;  // 02:00–03:00 (Morning Session)
input bool W0300W0400 = false;  // 03:00–04:00 (Morning Session)
input bool W0400W0500 = false;  // 04:00–05:00 (Morning Session)
input bool W0500W0600 = false;  // 05:00–06:00 (Morning Session)
input bool W0600W0700 = false;  // 06:00–07:00 (Morning Session)
input bool W0700W0800 = false;  // 07:00–08:00 (Morning Session)
input bool W0800W0900 = false;  // 08:00–09:00 (Morning Session)
input bool W0900W1000 = false;  // 09:00–10:00 (Morning Session)

// ========== SESSION 3: MAIN SESSION (10:00-23:00) ==========
input bool W1000W1100 = false;  // 10:00–11:00 (Main Session)
input bool W1100W1200 = false;  // 11:00–12:00 (Main Session)
input bool W1200W1300 = false;  // 12:00–13:00 (Main Session)
input bool W1300W1400 = false;  // 13:00–14:00 (Main Session)
input bool W1400W1500 = false;  // 14:00–15:00 (Main Session)
input bool W1500W1600 = false;  // 15:00–16:00 (Main Session)
input bool W1600W1700 = false;  // 16:00–17:00 (Main Session)
input bool W1700W1800 = false;  // 17:00–18:00 (Main Session)
input bool W1800W1900 = false;  // 18:00–19:00 (Main Session)
input bool W1900W2000 = false;  // 19:00–20:00 (Main Session)
input bool W2000W2100 = false;  // 20:00–21:00 (Main Session)
input bool W2100W2200 = false;  // 21:00–22:00 (Main Session)
input bool W2200W2300 = false;  // 22:00–23:00 (Main Session)

// ========== SESSION 4: EVENING SESSION (23:00-00:00) ==========
input bool W2300W2330 = false;  // 23:00–23:30 (Evening Session)
input bool W2330W0000 = false;  // 23:30–00:00 (Evening Session)

//====================================================================
//  GLOBALS
//====================================================================

bool      g_windows[26];          // Total slots: 1 + 2 + 8 + 13 + 2 = 26
CTrade    g_trade;
datetime  g_lastBarTime = 0;

//====================================================================
//  LIFECYCLE
//====================================================================

int OnInit()
  {
   g_trade.SetExpertMagicNumber(InpMagic);
   g_trade.SetTypeFillingBySymbol(_Symbol);
   g_trade.SetDeviationInPoints(20);

   if(InpSLoffsetPoints <= 0)
     {
      Print("InpSLoffsetPoints must be > 0");
      return(INIT_PARAMETERS_INCORRECT);
     }
   if(InpRiskReward <= 0)
     {
      Print("InpRiskReward must be > 0");
      return(INIT_PARAMETERS_INCORRECT);
     }
   if(FlattenHourEnd < 0 || FlattenHourEnd > 23 ||
      FlattenMinuteEnd < 0 || FlattenMinuteEnd > 59)
     {
      Print("Session close time must be 00:00 - 23:59");
      return(INIT_PARAMETERS_INCORRECT);
     }

   LoadWindows();
   DisplaySettings();

   PrintFormat("LastCandleBuyLimitEA initialised on %s %s", _Symbol, EnumToString(_Period));
   return(INIT_SUCCEEDED);
  }

void OnDeinit(const int reason)
  {
  }

//+------------------------------------------------------------------+
//| Copy the window inputs into the slot array. Done here rather than |
//| in a global initialiser so the input values are guaranteed set.  |
//+------------------------------------------------------------------+
void LoadWindows()
  {
   // ===== SESSION 1: MARKET CLOSED (00:00-01:00) =====
   g_windows[0]  = W0000W0100;

   // ===== SESSION 2: MORNING SESSION (01:00-10:00) =====
   g_windows[1]  = W0100W0130;  g_windows[2]  = W0130W0200;
   g_windows[3]  = W0200W0300;  g_windows[4]  = W0300W0400;
   g_windows[5]  = W0400W0500;  g_windows[6]  = W0500W0600;
   g_windows[7]  = W0600W0700;  g_windows[8]  = W0700W0800;
   g_windows[9]  = W0800W0900;  g_windows[10] = W0900W1000;

   // ===== SESSION 3: MAIN SESSION (10:00-23:00) =====
   g_windows[11] = W1000W1100;  g_windows[12] = W1100W1200;
   g_windows[13] = W1200W1300;  g_windows[14] = W1300W1400;
   g_windows[15] = W1400W1500;  g_windows[16] = W1500W1600;
   g_windows[17] = W1600W1700;  g_windows[18] = W1700W1800;
   g_windows[19] = W1800W1900;  g_windows[20] = W1900W2000;
   g_windows[21] = W2000W2100;  g_windows[22] = W2100W2200;
   g_windows[23] = W2200W2300;

   // ===== SESSION 4: EVENING SESSION (23:00-00:00) =====
   g_windows[24] = W2300W2330;  g_windows[25] = W2330W0000;
  }

//+------------------------------------------------------------------+
//| Main: act once per closed bar                                    |
//+------------------------------------------------------------------+
void OnTick()
  {
   datetime barOpen = iTime(_Symbol, _Period, 0);
   if(barOpen == g_lastBarTime)
      return;                       // still inside the same bar
   g_lastBarTime = barOpen;         // a new bar has opened => bar 1 just closed

   // Session borders in the log
   DisplayTradeWindowStatus(barOpen);

   // flatten end of session
   if(UseFlattenEnd && IsFlattenTimeEnd(barOpen))
     {
      Print("Flatten cutoff reached -> closing everything");
      CloseMyPositions();
      DeleteMyPendingOrders();
      return;
     }

   // time window check (ENTRY ONLY)
   if(!IsTradeWindow(barOpen))
     {
      DeleteMyPendingOrders();      // don't leave a limit resting outside the window
      return;
     }

   if(InpCancelOnNewBar)
      DeleteMyPendingOrders();

   PlaceBuyLimitOnLastCandle();
  }

//====================================================================
//  TIME WINDOWS / SESSION CLOSE
//====================================================================

bool IsFlattenTimeEnd(datetime barOpen)
  {
   MqlDateTime dt;
   TimeToStruct(barOpen, dt);
   return(dt.hour == FlattenHourEnd && dt.min == FlattenMinuteEnd);
  }

//+------------------------------------------------------------------+
//| Map the bar's open time to one of the 26 slots and return        |
//| whether trading is enabled there.                                |
//+------------------------------------------------------------------+
bool IsTradeWindow(datetime barOpen)
  {
   if(!UseTradeWindow)
      return(true);

   MqlDateTime dt;
   TimeToStruct(barOpen, dt);

   int totalMinutes = dt.hour * 60 + dt.min;
   int slot;

   // Session 1: 00:00-01:00 - 1-hour interval
   if(totalMinutes < 60)
     {
      slot = 0;
     }
   // Session 2: 01:00-10:00 - mixed intervals
   else if(totalMinutes >= 60 && totalMinutes < 600)
     {
      if(totalMinutes < 120)                     // 01:00-02:00 (30-min slots)
         slot = 1 + ((totalMinutes - 60) / 30);
      else                                       // 02:00-10:00 (hourly slots)
         slot = 3 + (dt.hour - 2);               // slots 3-10
     }
   // Session 3: 10:00-23:00 - hourly intervals
   else if(totalMinutes >= 600 && totalMinutes < 1380)
     {
      slot = 11 + (dt.hour - 10);                // slots 11-23
     }
   // Session 4: 23:00-00:00 - 30-min intervals
   else
     {
      slot = 24 + ((totalMinutes - 1380) / 30);  // slots 24-25
     }

   return(g_windows[slot]);
  }

//+------------------------------------------------------------------+
//| Print a banner when a new session starts.                        |
//+------------------------------------------------------------------+
void DisplayTradeWindowStatus(datetime barOpen)
  {
   if(!UseTradeWindow)
      return;

   MqlDateTime dt;
   TimeToStruct(barOpen, dt);

   int totalMinutes = dt.hour * 60 + dt.min;

   if(totalMinutes == 0)
      Print("=============== MARKET CLOSED ===============");
   else if(totalMinutes == 60)
      Print("=============== MORNING SESSION START ===============");
   else if(totalMinutes == 600)
      Print("================ MAIN SESSION START ================");
   else if(totalMinutes == 1380)
      Print("=============== EVENING SESSION START ===============");
  }

//====================================================================
//  ENTRY
//====================================================================

//+------------------------------------------------------------------+
//| Buy limit at the low of the bar that just closed (shift 1).      |
//+------------------------------------------------------------------+
void PlaceBuyLimitOnLastCandle()
  {
   if(CountMyPositions() >= InpMaxPositions)
      return;
   if(CountMyPendingOrders() > 0)   // one pending order at a time
      return;

   double low1  = iLow(_Symbol, _Period, 1);
   double high1 = iHigh(_Symbol, _Period, 1);
   if(low1 <= 0 || high1 <= low1)
      return;

   double entry  = low1;
   double offset = InpSLoffsetPoints * _Point;
   double sl     = entry - offset;

   // TP_RISK_REWARD : TP = entry + SL offset x R:R
   // TP_CANDLE_RANGE: TP = entry + (high - low) x multiplier, so mult = 1
   //                  puts the TP exactly on the signal candle's high.
   double range = high1 - low1;
   double tp    = (InpTPMode == TP_CANDLE_RANGE)
                  ? entry + range * InpTPrangeMult
                  : entry + offset * InpRiskReward;

   entry = NormalizePrice(entry);
   sl    = NormalizePrice(sl);
   tp    = NormalizePrice(tp);

   // A buy limit must sit below the current Ask by at least the broker's
   // stops level; if price already ran away below the low, skip this bar.
   double ask       = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double stopsDist = (double)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL) * _Point;
   if(entry >= ask - stopsDist)
     {
      PrintFormat("Skip: buy limit %.*f too close to / above Ask %.*f (stops level %.*f)",
                  _Digits, entry, _Digits, ask, _Digits, stopsDist);
      return;
     }

   // SL and TP must also clear the stops level measured from the order price.
   if(tp - entry <= stopsDist || entry - sl <= stopsDist)
     {
      PrintFormat("Skip: SL/TP too tight - entry %.*f SL %.*f TP %.*f (stops level %.*f)",
                  _Digits, entry, _Digits, sl, _Digits, tp, _Digits, stopsDist);
      return;
     }

   double lot = CalcLot();
   if(lot <= 0)
      return;

   if(g_trade.BuyLimit(lot, entry, _Symbol, sl, tp, ORDER_TIME_GTC, 0, "LastCandleBL"))
      PrintFormat("BUY LIMIT %.2f lots @ %.*f  SL %.*f  TP %.*f  (%s, candle range %.*f)",
                  lot, _Digits, entry, _Digits, sl, _Digits, tp,
                  (InpTPMode == TP_CANDLE_RANGE ? "range mode" : "R:R mode"),
                  _Digits, range);
   else
      PrintFormat("Order failed: retcode=%d %s", g_trade.ResultRetcode(),
                  g_trade.ResultRetcodeDescription());
  }

//====================================================================
//  HELPERS
//====================================================================

//+------------------------------------------------------------------+
//| Fixed lot, clamped to the broker's volume constraints.           |
//+------------------------------------------------------------------+
double CalcLot()
  {
   double lot = InpFixedLot;

   double minLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double maxLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);

   if(lotStep > 0)
      lot = MathFloor(lot / lotStep) * lotStep;
   lot = MathMax(minLot, MathMin(maxLot, lot));
   return(lot);
  }

//+------------------------------------------------------------------+
//| Snap a price to the symbol's tick size / digits.                 |
//+------------------------------------------------------------------+
double NormalizePrice(double price)
  {
   double tickSize = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   if(tickSize > 0)
      price = MathRound(price / tickSize) * tickSize;
   return(NormalizeDouble(price, _Digits));
  }

int CountMyPositions()
  {
   int count = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetString(POSITION_SYMBOL) == _Symbol &&
         PositionGetInteger(POSITION_MAGIC) == InpMagic)
         count++;
     }
   return(count);
  }

int CountMyPendingOrders()
  {
   int count = 0;
   for(int i = OrdersTotal() - 1; i >= 0; i--)
     {
      ulong ticket = OrderGetTicket(i);
      if(ticket == 0)
         continue;
      if(OrderGetString(ORDER_SYMBOL) == _Symbol &&
         OrderGetInteger(ORDER_MAGIC) == InpMagic)
         count++;
     }
   return(count);
  }

//+------------------------------------------------------------------+
//| Delete this EA's pending orders; returns how many were deleted.  |
//+------------------------------------------------------------------+
int DeleteMyPendingOrders()
  {
   int deleted = 0;
   for(int i = OrdersTotal() - 1; i >= 0; i--)
     {
      ulong ticket = OrderGetTicket(i);
      if(ticket == 0)
         continue;
      if(OrderGetString(ORDER_SYMBOL) == _Symbol &&
         OrderGetInteger(ORDER_MAGIC) == InpMagic)
         if(g_trade.OrderDelete(ticket))
            deleted++;
     }
   return(deleted);
  }

//+------------------------------------------------------------------+
//| Close this EA's open positions; returns how many were closed.    |
//+------------------------------------------------------------------+
int CloseMyPositions()
  {
   int closed = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetString(POSITION_SYMBOL) == _Symbol &&
         PositionGetInteger(POSITION_MAGIC) == InpMagic)
         if(g_trade.PositionClose(ticket))
            closed++;
     }
   return(closed);
  }

//+------------------------------------------------------------------+
//| Settings banner on init.                                         |
//+------------------------------------------------------------------+
void DisplaySettings()
  {
   Print("============ LastCandleBuyLimitEA SETTINGS ============");
   PrintFormat("Lot: %.2f   SL offset: %.0f points", InpFixedLot, InpSLoffsetPoints);
   if(InpTPMode == TP_CANDLE_RANGE)
      PrintFormat("TP mode: candle range x %.2f", InpTPrangeMult);
   else
      PrintFormat("TP mode: risk:reward x %.2f", InpRiskReward);
   PrintFormat("Time window filter: %s", UseTradeWindow ? "ENABLED" : "DISABLED");

   if(UseTradeWindow)
     {
      string enabled = "";
      string names[26] =
        {
         "00:00-01:00",
         "01:00-01:30", "01:30-02:00",
         "02:00-03:00", "03:00-04:00", "04:00-05:00", "05:00-06:00",
         "06:00-07:00", "07:00-08:00", "08:00-09:00", "09:00-10:00",
         "10:00-11:00", "11:00-12:00", "12:00-13:00", "13:00-14:00",
         "14:00-15:00", "15:00-16:00", "16:00-17:00", "17:00-18:00",
         "18:00-19:00", "19:00-20:00", "20:00-21:00", "21:00-22:00",
         "22:00-23:00",
         "23:00-23:30", "23:30-00:00"
        };
      for(int i = 0; i < 26; i++)
         if(g_windows[i])
            enabled += (enabled == "" ? "" : ", ") + names[i];

      Print("Enabled windows: ", (enabled == "" ? "NONE - no entries will be taken" : enabled));
     }

   PrintFormat("Flatten end of session: %s",
               UseFlattenEnd
               ? StringFormat("Yes (%02d:%02d)", FlattenHourEnd, FlattenMinuteEnd)
               : "No");
   Print("======================================================");
  }
//+------------------------------------------------------------------+
