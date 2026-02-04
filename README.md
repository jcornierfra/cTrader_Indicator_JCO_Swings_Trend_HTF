# JCO Swings Trend HTF Indicator

A cTrader indicator for detecting swing highs and lows on a higher timeframe (HTF) with advanced trend analysis, CHoCH detection, and liquidity sweep detection.

## Features

- **Trend Detection**: Identifies BULLISH (Higher Lows) / BEARISH (Lower Highs) / UNCLEAR market structure
- **Trend Status**: Momentum (perfect trend) vs Compression (slowdown/consolidation)
- **CHoCH Detection**: Change of Character - first sign of potential reversal
- **Liquidity Sweep Detection**: Identifies institutional stop hunting patterns
- **Swing Alternation**: Enforces High-Low-High-Low sequence
- **Confirmation Logic**: Waits for closed candles before confirming swings

## Installation

1. Copy the `JCO Swings Trend HTF.cs` file to your cTrader Indicators folder
2. Build the indicator in cTrader
3. Add to your chart

## Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| Swing Period | 5 | Number of candles for fractal detection (left + right) |
| Swing Lookback Period | 200 | How many swings to analyze |
| Swing Time Frame | H1 | Higher timeframe for swing detection |
| Draw Icons | true | Show arrows at swing points |
| Draw Dots | true | Show dots at swing points |
| Icon Gap (%) | 2.0 | Gap between icon and candle (% of chart height) |
| Display Dashboard | true | Show trend info on chart |

## Dashboard Display

The indicator displays the following information:

```
Time frame of the swings: H1
Trend: BULLISH Momentum
Continuation
No liquidity sweep
Swing Expansion in pips: 45.2
```

## Trend Logic

### BULLISH Trend
Based on **Higher Lows** (LL1 > LL2 > LL3):
- **Momentum**: LL1 > LL2 AND HH1 > HH2 (perfect uptrend)
- **Compression**: LL1 > LL2 BUT HH1 < HH2 (highs are blocked)

### BEARISH Trend
Based on **Lower Highs** (HH1 < HH2 < HH3):
- **Momentum**: HH1 < HH2 AND LL1 < LL2 (perfect downtrend)
- **Compression**: HH1 < HH2 BUT LL1 > LL2 (lows are blocked)

## CHoCH (Change of Character)

CHoCH is an early warning signal of potential trend reversal:

### CHoCH Bullish
- Previous bearish structure: HH2 < HH3 (lower highs)
- Break: HH1 > HH2
- Candle must close above HH2

### CHoCH Bearish
- Previous bullish structure: LL2 > LL3 (higher lows)
- Break: LL1 < LL2
- Candle must close below LL2

> **Note**: CHoCH is an alert only. It can be a liquidity grab (fake out) or a real reversal. Wait for BOS (Break of Structure) to confirm.

## Liquidity Sweep Detection

Detects institutional stop hunting when an intermediate swing (LL2, LL3, or LL4) sweeps below at least 2 other swing levels in a bullish trend (or above in a bearish trend).

## Version History

- **v1.4** (2026-02-04): Added Momentum/Compression status, simplified CHoCH, fixed swing confirmation
- **v1.3** (2026-02-02): Added CHoCH detection
- **v1.2** (2026-02-02): Added swing alternation, dynamic icon gap
- **v1.1**: Changed to 3-swing trend detection, 5-swing liquidity detection
- **v1.0**: Initial release

## License

MIT License - Feel free to use and modify.

## Author

JCO
