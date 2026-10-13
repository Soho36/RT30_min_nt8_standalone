# GG Synchronization

The GG SafeExits strategy was synchronized on 2026-10-14 with the latest
standalone RR implementation in
`RR_m_w_r_stoplimits_and_tplimit_InstanceID_WindowRR_Offsets_SafeExits_MaxRedRun.cs`.
The plain `RR_..._SafeExits.cs` file is an older version and was left unchanged.

GG keeps its existing filename, class and strategy Name. It still buys above
the signal candle's high, places its initial sell stop at that candle's low,
and uses the same quantities, windows, R:R calculations and bar-close targets
as the current RR version. It now includes the session-opening window/gap
handling, one-minute protective stop-limit market fallback, and 25-minute
triggered entry cancellation. See `STOP_TIMEOUT.md` for timeout behavior and
limitations.

GG's signal is strictly green (`Close > Open`), whereas RR's is strictly red.
The corresponding optional cap is `MaxGreenRun`, disabled by default (`0`).
Red/doji candles break a green run. Rejected green signals cancel the pending
entry when the cap is enabled. The green cap counts loaded history, including
across session/window boundaries, just as the RR red cap does.

`verify_stop_timeout.ps1` now checks all three current sources: standalone RR,
routed RR and GG. Each passes the same 37 timeout scenarios. The runner also
checks exact RR/GG parity after identity and candle-color substitutions,
tests nine green-run cases using GG's actual filter method, and compiles the
three complete strategies against installed NT8 assemblies. This does not
exercise NinjaTrader's managed-order engine or a broker. Compile/restart the
updated GG in NinjaTrader and verify in Simulation/Playback before live use.

The pre-update GG source is preserved in
`backups/gg_sync_20261014/GG_SafeExits.cs.bak`.
