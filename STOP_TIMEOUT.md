# Stop-limit Timeouts

Applied to the standalone and routed `SafeExits_MaxRedRun` strategies on
2026-10-12. The class names and settings are unchanged. Both timeouts are always
enabled: 60 seconds for protective stops and 25 minutes for entry orders.

## Protective Stops

The first realtime last-trade tick at or below an outstanding sell stop-limit's
stop price starts the clock. A reported partial stop fill also arms it if the
price tick was missed. NinjaTrader's documented Order interface does not expose
a universal broker stop-trigger timestamp, so this uses the locally observed
trigger. Recovery above the stop does not restart or disarm the clock.

A 500 ms timer queues a NinjaScript custom event, independent of chart bar
closes and subsequent market ticks. At 60 seconds, it cancels this strategy's
remaining entry, stop and target orders. After terminal confirmations and their
reported fills' execution callbacks, it sends an `ExitLong` market order for the
remaining strategy position, tied to its entry signal. Its order name is
`StopTimeoutExit_<InstanceId>`. Normal strategy entry/target/stop submissions are
blocked while the fallback is completing. The routed seat stays busy.

Partial fills reduce the market-exit quantity. An accepted or partially filled
market exit blocks duplicate submissions. Failed submissions or cancelled/
rejected market exits can retry, at most once per second, after accounting for
reported executions. Events and failures are printed to NinjaScript Output.

The timer is client-side. NinjaTrader must remain enabled, running and connected.
Broker cancellation confirmations can delay submission beyond 60 seconds;
the script waits rather than leave the old exit able to fill alongside a new
sell order. Market execution can slip and is not a price or loss guarantee.

## Entry Orders

The first realtime last-trade tick at or above an outstanding buy stop-limit's
stop price starts a separate 25-minute clock. A partial entry fill also starts
it if the price tick was missed. When the clock expires, the timer cancels the
unfilled entry remainder; it never converts the entry to a market buy or closes
the filled portion. Filled quantity keeps normal stop-loss/target management.

The 25 minutes are measured from the first observed trigger, not the bar opening.
For example, a 07:03 trigger expires at 07:28, and a 07:20 trigger expires at 07:45.
Price recovery and repricing the same Order object do not restart the clock.
A genuinely new Order object gets a fresh watch. Exit-tracking resets do not
erase the entry clock. Untriggered entries keep their existing behavior; window,
session and rejected-signal rules can still cancel orders earlier.

Cancellation retries after a failure are limited to once per second. A pending
cancel blocks replacement entries until cancellation and any reported late fills
are processed, while existing positions still receive normal exits. Routed seats
remain reserved during this interval. This uses the same client-side timer as
stop protection, with the same running/connection requirements.

## Verification

Run in PowerShell 7:

```powershell
.\verify_stop_timeout.ps1
```

This runs 37 scenarios against the actual timeout methods extracted from each
source and compiles both complete scripts plus `PropRouter.cs` against the locally
installed NT8 assemblies. It does not run NinjaTrader's managed-order engine or a
broker. Before live use, compile the strategies in NinjaTrader and verify a
missed stop-limit fill in Simulation/Playback, including cancellation and fills.
Existing enabled instances must be restarted to run the updated code.

Original source backups are in `backups/stop_timeout_20261012/standalone.cs.bak`
and `backups/stop_timeout_20261012/routed.cs.bak`. Test sources and backup files
are not NinjaScripts to import.
