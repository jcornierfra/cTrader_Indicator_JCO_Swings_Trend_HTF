// =====================================================
// JCO Swings Trend HTF Indicator
// =====================================================
// Version: 1.5
// Date: 2026-02-05
// Author: Jerome Cornier
// GitHub: https://github.com/jcornierfra/cTrader_Indicator_JCO_Swings_Trend_HTF
//
// Description:
// Detects swing highs and lows on a higher timeframe (HTF)
// and determines the trend direction based on swing structure.
// Also detects liquidity sweeps and CHoCH for enhanced market analysis.
//
// Features:
// - Trend detection: BULLISH (HL) / BEARISH (LH) / UNCLEAR
// - Trend status: Momentum (perfect trend) / Compression (slowdown)
// - CHoCH detection: First sign of potential reversal (alert only)
// - Liquidity sweep detection using 5 swings
// - Swing confirmation waits for closed candles on both sides
//
// Changelog:
// v1.5 (2026-02-05)
//   - New liquidity sweep detection logic (aligned with TradingView version)
//     * Bullish: Case1 (LL2 swept LL3 + reversed) or Case2 (new low rejected)
//     * Bearish: Case1 (HH2 swept HH3 + reversed) or Case2 (new high rejected)
//   - Now requires only 3 swings instead of 5 for liquidity detection
//
// v1.4 (2026-02-04)
//   - Added trend status: Momentum vs Compression
//     * Momentum: LL1 > LL2 AND HH1 > HH2 (bullish) or inverse (bearish)
//     * Compression: trend continues but opposite swings are blocked
//   - Simplified CHoCH to Bullish/Bearish (removed Possible/Confirmed)
//   - CHoCH requires previous opposite structure (HH2 < HH3 for bullish)
//   - Fixed swing detection to wait for confirmation candles (middleIndex)
//
// v1.3 (2026-02-02)
//   - Added CHoCH (Change of Character) detection
//   - CHoCH detection based on PREVIOUS structure (HH3-HH2, LL3-LL2)
//   - Dashboard displays CHoCH status
//
// v1.2 (2026-02-02)
//   - Added swing alternation logic to ensure High-Low-High-Low sequence
//   - Automatically inserts missing swings between consecutive same-type swings
//   - Dynamic icon gap based on visible chart height (percentage)
//
// v1.1
//   - Changed trend detection logic to use 3 swings
//   - Changed liquidity sweep detection to use 5 swings
//
// v1.0 (Initial)
//   - Swing High/Low detection based on fractal logic
//   - HTF trend analysis with configurable timeframe
//   - Liquidity sweep detection
//   - Dashboard display with trend and expansion info
// =====================================================

using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Indicators
{
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class SwingsHighLowHTFIndicator : Indicator
    {
        // ----- Swings Highs and Lows -----
        [Parameter("Swing Period", DefaultValue = 5, Group = "Swings H & L")]
        public int SwingPeriod { get; set; }

        [Parameter("Swing Lookback Period", DefaultValue = 200, Group = "Swings H & L")]
        public int SwingLookbackPeriod { get; set; }

        [Parameter("Swing Time Frame", DefaultValue = "H1", Group = "Swings H & L")]
        public TimeFrame SwingTimeFrame { get; set; }

        [Parameter("Draw icons", DefaultValue = true, Group = "Swings Chart")]
        public bool DrawSwingIcons { get; set; }

        [Parameter("Draw dots", DefaultValue = true, Group = "Swings Chart")]
        public bool DrawSwingDots { get; set; }

        [Parameter("Icon Gap (%)", DefaultValue = 2.0, MinValue = 0.1, MaxValue = 10, Group = "Swings Chart")]
        public double SwingIconGapPercent { get; set; }

        [Parameter("Display Dashboard for swings and trend", DefaultValue = true, Group = "Swings Chart")]
        public bool DisplayDashboardSwings { get; set; }

        [Parameter("Enable Print for Swings", DefaultValue = false, Group = "Swings Log")]
        public bool EnablePrintSwings { get; set; }

        private int candleRangeInHTF;
        private Bars swingBarsHTF;
        
        public class Swing
        {
            public DateTime SwingHTFOpenTime { get; set; }
            public DateTime SwingChartOpenTime { get; set; }
            public double   SwingPrice { get; set; }        // HTF price for calculations
            public double   DisplayPrice { get; set; }      // Chart price for visual display
            public int      SwingBarsIndex { get; set; }
        }

        // Structure unifiée pour le tri et l'alternance
        private enum SwingType { High, Low }

        private class SwingPoint
        {
            public Swing Swing { get; set; }
            public SwingType Type { get; set; }
        }
        
        private Swing[] swingHighPrices;
        private Swing[] swingLowPrices;
        
        private int swingHighCount;
        private int swingLowCount;
        
        private int _swingsDirection;
        private const int NODIRECTION = 0;
        private const int BULLISH = 1;
        private const int BEARISH = -1;
        private bool _liquiditySweep;

        // Trend status (momentum vs compression)
        private int _trendStatus;
        private const int MOMENTUM = 1;
        private const int COMPRESSION = -1;

        // CHoCH (Change of Character) status
        private int _chochStatus;
        private const int CONTINUATION = 0;
        private const int CHOCH_BULLISH = 1;
        private const int CHOCH_BEARISH = -1;
 
        protected override void Initialize()
        {
        
            // Initialize the Bars array of the given timeframe for Swings
            swingBarsHTF = MarketData.GetBars(SwingTimeFrame);
            
            // Initialize the Swing arrays
            swingHighPrices = new Swing[SwingLookbackPeriod];
            swingLowPrices = new Swing[SwingLookbackPeriod];
            swingHighCount = 0;
            swingLowCount = 0;
            _swingsDirection = NODIRECTION;
            _liquiditySweep = false;
            
            if (EnablePrintSwings)
                Print($"Indicator initialized with Swing Time Frame: {SwingTimeFrame}");
            
            candleRangeInHTF = GetTimeFrameInSeconds(SwingTimeFrame) / GetTimeFrameInSeconds(Bars.TimeFrame);
            if (EnablePrintSwings)
                Print($"Count of LTF candle in the HTF: {candleRangeInHTF}");
        }

        public override void Calculate(int index)
        {
            // Only calculate if the swing time frame is greater than or equal to the Chart time frame
            if (Bars.TimeFrame > SwingTimeFrame)
                return;

            // Start when enough HTF Bars are available
            // Use minimum of 500 bars or the calculated requirement to avoid excessive wait on low timeframes
            int minRequiredBars = Math.Min(500, SwingLookbackPeriod * candleRangeInHTF);
            if (index < minRequiredBars)
                return;
            
            // Reset Swing coutners of arrays
            swingHighCount = 0;
            swingLowCount = 0;
            
            // Calculate current for the Swing time frame
            var time = Bars.OpenTimes[index];
            int swingIndex = swingBarsHTF.OpenTimes.GetIndexByTime(time);

            // Find swings highs & lows based on fractal and loopBack periods
            bool swingDetected = DetectSwings(swingIndex, swingBarsHTF);

            if (swingDetected)
            {
                // Force l'alternance High-Low-High-Low
                ForceSwingAlternation(swingBarsHTF);
            }

            if (swingDetected)
            {
                // Calculate the trend based on the last 3 highs and 3 lows
                _swingsDirection = CalculateSwingsTrend();

                // Search for liquidity sweep based on the direction
                _liquiditySweep = CalculateLiquiditySweep();

                // Check for CHoCH (Change of Character)
                _chochStatus = CalculateCHoCH(); 
                
                // Draw the swings
                if(DrawSwingIcons || DrawSwingDots)
                    DrawSwingIconsDots();
                
                // Display the swings prices & trend
                if (DisplayDashboardSwings)
                    DisplaySwingsTrend();
            }
        }

        private bool DetectSwings(int swingIndex, Bars swingBars)
        {
            bool swingFound = false;
            int middleIndex = SwingPeriod / 2;

            // Check there is enough candles
            if (swingIndex < SwingLookbackPeriod)
                return swingFound;

            // Start at swingIndex - middleIndex to ensure we have 'middleIndex' closed candles
            // to the RIGHT of each potential swing (confirmation candles)
            int searchStartIndex = swingIndex - middleIndex;

            for (int i = searchStartIndex; i > swingIndex - SwingLookbackPeriod + middleIndex; i--)
            {
                if (IsSwingHigh(swingBars, i, SwingPeriod))
                {
                    int startIndex = Bars.OpenTimes.GetIndexByTime(swingBars.OpenTimes[i]);
                    int chartIndex = startIndex;
                    
                    if (startIndex < 0)
                        continue;
                    
                    // Look for the exact candle on the Chart that have the wick
                    int endIndex = Math.Min(startIndex + candleRangeInHTF, Bars.Count - 1);
                    for (int x = startIndex; x < endIndex; x++)
                    {
                        if (Bars.HighPrices[x] > Bars.HighPrices[chartIndex])
                        {
                            chartIndex = x;
                        }
                    }
                    
                    if (EnablePrintSwings)
                        Print("start index: {0} | Chart index: {1}", startIndex, chartIndex);
                        
                    if (chartIndex >= 0)
                    {
                        if (EnablePrintSwings)
                            Print($"Swing High detected at Swing Time Frame Index: {i}, Chart Index: {chartIndex}");

                        // Store the swing high price - USE HTF PRICE for consistency
                        if (swingHighCount < SwingLookbackPeriod)
                        {
                            swingHighPrices[swingHighCount++] = new Swing
                            {
                                SwingHTFOpenTime = swingBars.OpenTimes[i],
                                SwingChartOpenTime = Bars.OpenTimes[chartIndex],
                                SwingPrice = swingBars.HighPrices[i],      // HTF price for calculations
                                DisplayPrice = Bars.HighPrices[chartIndex], // Chart price for display
                                SwingBarsIndex = startIndex
                            };
                            
                            swingFound = true;
                        }
                    }
                }

                if (IsSwingLow(swingBars, i, SwingPeriod))
                {
                    int startIndex = Bars.OpenTimes.GetIndexByTime(swingBars.OpenTimes[i]);
                    int chartIndex = startIndex;
                    
                    if (startIndex < 0)
                        continue;

                    // Look for the exact candle on the Chart that have the wick
                    int endIndex = Math.Min(startIndex + candleRangeInHTF, Bars.Count - 1);
                    for (int x = startIndex; x <= endIndex; x++)
                    {
                        if (Bars.LowPrices[x] < Bars.LowPrices[chartIndex])
                        {
                            chartIndex = x;
                        }
                    }
                    
                    if (EnablePrintSwings)
                        Print("start index: {0} | Chart index: {1}", startIndex, chartIndex);
                    
                    if (chartIndex >= 0)
                    {
                        if (EnablePrintSwings)
                            Print($"Swing Low detected at Swing Time Frame Index: {i}, Chart Index: {chartIndex}");

                        // Store the swing low price - USE HTF PRICE for consistency
                        if (swingLowCount < SwingLookbackPeriod)
                        {
                            swingLowPrices[swingLowCount++] = new Swing
                            {
                                SwingHTFOpenTime = swingBars.OpenTimes[i],
                                SwingChartOpenTime = Bars.OpenTimes[chartIndex],
                                SwingPrice = swingBars.LowPrices[i],      // HTF price for calculations
                                DisplayPrice = Bars.LowPrices[chartIndex], // Chart price for display
                                SwingBarsIndex = startIndex
                            };
                            
                            swingFound = true;
                        }
                        
                    }
                }
            }
            
            return swingFound;
        }

        private void ForceSwingAlternation(Bars swingBars)
        {
            // Étape 1: Fusionner tous les swings dans une liste unique triée par temps
            var allSwings = new System.Collections.Generic.List<SwingPoint>();

            for (int i = 0; i < swingHighCount; i++)
            {
                allSwings.Add(new SwingPoint { Swing = swingHighPrices[i], Type = SwingType.High });
            }
            for (int i = 0; i < swingLowCount; i++)
            {
                allSwings.Add(new SwingPoint { Swing = swingLowPrices[i], Type = SwingType.Low });
            }

            // Trier par temps HTF (du plus ancien au plus récent)
            allSwings = allSwings.OrderBy(s => s.Swing.SwingHTFOpenTime).ToList();

            if (allSwings.Count < 2)
                return;

            if (EnablePrintSwings)
            {
                Print("=== AVANT ALTERNATION ===");
                foreach (var s in allSwings)
                    Print($"{s.Type} @ {s.Swing.SwingHTFOpenTime:yyyy-MM-dd HH:mm} - Price: {s.Swing.SwingPrice:F5}");
            }

            // Étape 2: Parcourir et corriger les doublons
            var correctedSwings = new System.Collections.Generic.List<SwingPoint>();
            correctedSwings.Add(allSwings[0]);

            for (int i = 1; i < allSwings.Count; i++)
            {
                var current = allSwings[i];
                var previous = correctedSwings[correctedSwings.Count - 1];

                if (current.Type == previous.Type)
                {
                    // Deux swings du même type consécutifs - chercher le swing opposé entre les deux
                    var missingSwing = FindMissingSwing(swingBars, previous, current);

                    if (missingSwing != null)
                    {
                        // Insérer le swing manquant
                        correctedSwings.Add(missingSwing);

                        if (EnablePrintSwings)
                            Print($"Swing {missingSwing.Type} INSÉRÉ @ {missingSwing.Swing.SwingHTFOpenTime:yyyy-MM-dd HH:mm} - Price: {missingSwing.Swing.SwingPrice:F5}");
                    }
                }

                correctedSwings.Add(current);
            }

            if (EnablePrintSwings)
            {
                Print("=== APRÈS ALTERNATION ===");
                foreach (var s in correctedSwings)
                    Print($"{s.Type} @ {s.Swing.SwingHTFOpenTime:yyyy-MM-dd HH:mm} - Price: {s.Swing.SwingPrice:F5}");
            }

            // Étape 3: Reconstruire les tableaux séparés High/Low
            swingHighCount = 0;
            swingLowCount = 0;

            // Parcourir du plus récent au plus ancien pour garder l'ordre original
            for (int i = correctedSwings.Count - 1; i >= 0; i--)
            {
                var sp = correctedSwings[i];
                if (sp.Type == SwingType.High && swingHighCount < SwingLookbackPeriod)
                {
                    swingHighPrices[swingHighCount++] = sp.Swing;
                }
                else if (sp.Type == SwingType.Low && swingLowCount < SwingLookbackPeriod)
                {
                    swingLowPrices[swingLowCount++] = sp.Swing;
                }
            }
        }

        private SwingPoint FindMissingSwing(Bars swingBars, SwingPoint first, SwingPoint second)
        {
            // Trouver les indices HTF entre les deux swings
            int startIdx = swingBars.OpenTimes.GetIndexByTime(first.Swing.SwingHTFOpenTime);
            int endIdx = swingBars.OpenTimes.GetIndexByTime(second.Swing.SwingHTFOpenTime);

            if (startIdx >= endIdx || startIdx < 0 || endIdx < 0)
                return null;

            // Chercher le swing opposé entre les deux
            SwingType missingType = (first.Type == SwingType.High) ? SwingType.Low : SwingType.High;

            int bestIdx = startIdx + 1;
            double bestPrice = (missingType == SwingType.High) ? swingBars.HighPrices[bestIdx] : swingBars.LowPrices[bestIdx];

            for (int i = startIdx + 1; i < endIdx; i++)
            {
                if (missingType == SwingType.High)
                {
                    if (swingBars.HighPrices[i] > bestPrice)
                    {
                        bestPrice = swingBars.HighPrices[i];
                        bestIdx = i;
                    }
                }
                else
                {
                    if (swingBars.LowPrices[i] < bestPrice)
                    {
                        bestPrice = swingBars.LowPrices[i];
                        bestIdx = i;
                    }
                }
            }

            // Créer le swing manquant
            int chartStartIndex = Bars.OpenTimes.GetIndexByTime(swingBars.OpenTimes[bestIdx]);
            int chartIndex = chartStartIndex;

            if (chartStartIndex < 0)
                return null;

            // Trouver la bougie exacte sur le chart
            int chartEndIndex = Math.Min(chartStartIndex + candleRangeInHTF, Bars.Count - 1);
            for (int x = chartStartIndex; x < chartEndIndex; x++)
            {
                if (missingType == SwingType.High)
                {
                    if (Bars.HighPrices[x] > Bars.HighPrices[chartIndex])
                        chartIndex = x;
                }
                else
                {
                    if (Bars.LowPrices[x] < Bars.LowPrices[chartIndex])
                        chartIndex = x;
                }
            }

            var newSwing = new Swing
            {
                SwingHTFOpenTime = swingBars.OpenTimes[bestIdx],
                SwingChartOpenTime = Bars.OpenTimes[chartIndex],
                SwingPrice = (missingType == SwingType.High) ? swingBars.HighPrices[bestIdx] : swingBars.LowPrices[bestIdx],
                DisplayPrice = (missingType == SwingType.High) ? Bars.HighPrices[chartIndex] : Bars.LowPrices[chartIndex],
                SwingBarsIndex = chartStartIndex
            };

            return new SwingPoint { Swing = newSwing, Type = missingType };
        }

        private void DrawSwingIconsDots()
        {
            // Remove existing FVG rectangles with names starting with "FVG"
            foreach (var obj in Chart.Objects.Where(o => o.Name.StartsWith("SwingIcon")).ToList())
            {
                Chart.RemoveObject(obj.Name);
            }
            foreach (var obj in Chart.Objects.Where(o => o.Name.StartsWith("SwingDot")).ToList())
            {
                Chart.RemoveObject(obj.Name);
            }

            // Calculate dynamic icon gap based on visible chart height
            double chartHeight = Chart.TopY - Chart.BottomY;
            double iconGap = chartHeight * (SwingIconGapPercent / 100.0);

            if (DrawSwingIcons)
            {
                // Draw arrow up for bullish swings
                for (int bullCount = 0; bullCount < swingHighCount; bullCount++)
                {
                    Swing swingBull = swingHighPrices[bullCount];

                    // Calculate candle time on the Chart
                    DateTime positionIcon = swingBull.SwingChartOpenTime;

                    // Draw icon using DisplayPrice for accurate visual placement
                    Chart.DrawIcon("SwingIcon_" + swingBull.SwingBarsIndex, ChartIconType.DownArrow, swingBull.SwingChartOpenTime, swingBull.DisplayPrice + iconGap, Color.Red);
                }
                
                
                // Draw arrow down for bearish swings
                for (int bearCount = 0; bearCount < swingLowCount; bearCount++)
                {
                    Swing swingBear = swingLowPrices[bearCount];

                    // Calculate candle time on the Chart
                    DateTime positionIcon = swingBear.SwingChartOpenTime;

                    // Draw icon using DisplayPrice for accurate visual placement
                    Chart.DrawIcon("SwingIcon_" + swingBear.SwingBarsIndex, ChartIconType.UpArrow, swingBear.SwingChartOpenTime, swingBear.DisplayPrice - iconGap, Color.Green);
                }
            }
            
            if (DrawSwingDots)
            {
                // Draw arrow up for bullish swings
                for (int bullCount = 0; bullCount < swingHighCount; bullCount++)
                {
                    Swing swingBull = swingHighPrices[bullCount];
                    
                    // Calculate candle time on the Chart
                    DateTime positionIcon = swingBull.SwingChartOpenTime;
                    
                    // Plot dot at the swing using DisplayPrice for accurate visual placement
                    Chart.DrawTrendLine("SwingDot_" + swingBull.SwingBarsIndex, swingBull.SwingChartOpenTime, swingBull.DisplayPrice,
                        swingBull.SwingChartOpenTime, swingBull.DisplayPrice + Symbol.PipSize, Color.Yellow, 3, LineStyle.Solid);
                }
                
                
                // Draw arrow down for bearish swings
                for (int bearCount = 0; bearCount < swingLowCount; bearCount++)
                {
                    Swing swingBear = swingLowPrices[bearCount];
                    
                    // Calculate candle time on the Chart
                    DateTime positionIcon = swingBear.SwingChartOpenTime;
                    
                    // Plot dot at the swing using DisplayPrice for accurate visual placement
                    Chart.DrawTrendLine("SwingDot_" + swingBear.SwingBarsIndex, swingBear.SwingChartOpenTime,
                        swingBear.SwingPrice, swingBear.SwingChartOpenTime, swingBear.DisplayPrice - Symbol.PipSize, Color.Yellow, 3, LineStyle.Solid);
                }
            }
        }
        
        private int CalculateSwingsTrend()
        {
            // Need at least 3 of each for trend calculation
            if (swingHighCount < 3 || swingLowCount < 3)
                return NODIRECTION;
            
            // ==============================================
            // TWO-LEVEL LOGIC: Primary + Secondary Confirmation
            // ==============================================
            
            // PRIMARY ANALYSIS - BULLISH structure based on LOWS
            // Perfect case: LL3 < LL2 < LL1
            bool perfectBullish = (swingLowPrices[2].SwingPrice < swingLowPrices[1].SwingPrice) 
                                  && (swingLowPrices[1].SwingPrice < swingLowPrices[0].SwingPrice);
            
            // Sweep case: LL3 > LL2 (sweep) but LL1 > LL3 (recovery above)
            bool sweepBullish = (swingLowPrices[2].SwingPrice > swingLowPrices[1].SwingPrice) // LL2 swept below LL3
                                && (swingLowPrices[0].SwingPrice > swingLowPrices[2].SwingPrice); // But LL1 recovered above LL3
            
            // Additional check: LL1 should be higher than LL2 for any bullish scenario
            bool ll1HigherThanLL2 = swingLowPrices[0].SwingPrice > swingLowPrices[1].SwingPrice;
            
            bool primaryBullish = (perfectBullish || sweepBullish) && ll1HigherThanLL2;
            
            // Ambiguous case for bullish: LL3 > LL2 < LL1 (V-shape correction)
            bool ambiguousBullish = (swingLowPrices[2].SwingPrice > swingLowPrices[1].SwingPrice)
                                    && (swingLowPrices[1].SwingPrice < swingLowPrices[0].SwingPrice);
            
            // PRIMARY ANALYSIS - BEARISH structure based on HIGHS
            // Perfect case: HH3 > HH2 > HH1
            bool perfectBearish = (swingHighPrices[2].SwingPrice > swingHighPrices[1].SwingPrice) 
                                  && (swingHighPrices[1].SwingPrice > swingHighPrices[0].SwingPrice);
            
            // Sweep case: HH3 < HH2 (sweep) but HH1 < HH3 (breakdown below)
            bool sweepBearish = (swingHighPrices[2].SwingPrice < swingHighPrices[1].SwingPrice) // HH2 swept above HH3
                                && (swingHighPrices[0].SwingPrice < swingHighPrices[2].SwingPrice); // But HH1 broke below HH3
            
            // Additional check: HH1 should be lower than HH2 for any bearish scenario
            bool hh1LowerThanHH2 = swingHighPrices[0].SwingPrice < swingHighPrices[1].SwingPrice;
            
            bool primaryBearish = (perfectBearish || sweepBearish) && hh1LowerThanHH2;
            
            // Ambiguous case for bearish: HH3 < HH2 > HH1 (^-shape correction)
            bool ambiguousBearish = (swingHighPrices[2].SwingPrice < swingHighPrices[1].SwingPrice)
                                    && (swingHighPrices[1].SwingPrice > swingHighPrices[0].SwingPrice);
            
            // SECONDARY CONFIRMATION using opposite swings
            // For ambiguous bullish structure, check if highs confirm buyers' pressure
            bool highsConfirmBullish = swingHighPrices[0].SwingPrice > swingHighPrices[1].SwingPrice; // HH1 > HH2
            
            // For ambiguous bearish structure, check if lows confirm sellers' pressure
            bool lowsConfirmBearish = swingLowPrices[0].SwingPrice < swingLowPrices[1].SwingPrice; // LL1 < LL2
            
            if (EnablePrintSwings)
            {
                Print($"=== TREND ANALYSIS (3 Swings with Secondary Confirmation) ===");
                Print($"Highs: HH3={swingHighPrices[2].SwingPrice:F5} -> HH2={swingHighPrices[1].SwingPrice:F5} -> HH1={swingHighPrices[0].SwingPrice:F5}");
                Print($"Lows:  LL3={swingLowPrices[2].SwingPrice:F5} -> LL2={swingLowPrices[1].SwingPrice:F5} -> LL1={swingLowPrices[0].SwingPrice:F5}");
                Print($"PRIMARY - Perfect Bullish: {perfectBullish}, Sweep Bullish: {sweepBullish}, LL1>LL2: {ll1HigherThanLL2}");
                Print($"PRIMARY - Perfect Bearish: {perfectBearish}, Sweep Bearish: {sweepBearish}, HH1<HH2: {hh1LowerThanHH2}");
                Print($"AMBIGUOUS - Bullish V-shape: {ambiguousBullish}, Bearish ^-shape: {ambiguousBearish}");
                Print($"SECONDARY - Highs confirm bullish: {highsConfirmBullish}, Lows confirm bearish: {lowsConfirmBearish}");
            }
            
            // DECISION LOGIC

            // Clear BULLISH (primary structure confirmed)
            if (primaryBullish)
            {
                // Bullish Momentum: LL1 > LL2 AND HH1 > HH2 (perfect trend)
                // Bullish Compression: LL1 > LL2 BUT HH1 < HH2 (highs are blocked)
                _trendStatus = highsConfirmBullish ? MOMENTUM : COMPRESSION;

                if (EnablePrintSwings)
                    Print($"=> Trend: BULLISH {(_trendStatus == MOMENTUM ? "MOMENTUM" : "COMPRESSION")} (Primary HL structure confirmed)");
                return BULLISH;
            }

            // Clear BEARISH (primary structure confirmed)
            if (primaryBearish)
            {
                // Bearish Momentum: HH1 < HH2 AND LL1 < LL2 (perfect trend)
                // Bearish Compression: HH1 < HH2 BUT LL1 > LL2 (lows are blocked)
                _trendStatus = lowsConfirmBearish ? MOMENTUM : COMPRESSION;

                if (EnablePrintSwings)
                    Print($"=> Trend: BEARISH {(_trendStatus == MOMENTUM ? "MOMENTUM" : "COMPRESSION")} (Primary LH structure confirmed)");
                return BEARISH;
            }

            // Ambiguous BULLISH but highs confirm
            if (ambiguousBullish && highsConfirmBullish)
            {
                _trendStatus = MOMENTUM; // Highs confirm, so momentum

                if (EnablePrintSwings)
                    Print("=> Trend: BULLISH MOMENTUM (Ambiguous lows but highs confirm buyer pressure)");
                return BULLISH;
            }

            // Ambiguous BEARISH but lows confirm
            if (ambiguousBearish && lowsConfirmBearish)
            {
                _trendStatus = MOMENTUM; // Lows confirm, so momentum

                if (EnablePrintSwings)
                    Print("=> Trend: BEARISH MOMENTUM (Ambiguous highs but lows confirm seller pressure)");
                return BEARISH;
            }

            // Otherwise: Structure is unclear
            _trendStatus = 0; // No status for unclear trend

            if (EnablePrintSwings)
                Print("=> Trend: UNCLEAR (No clear structure or confirmation)");
            return NODIRECTION;
        }

        private bool CalculateLiquiditySweep()
        {
            // Need at least 3 of each for liquidity sweep calculation
            if (swingHighCount < 3 || swingLowCount < 3)
                return false;

            // ==============================================
            // LIQUIDITY SWEEP DETECTION
            // ==============================================
            // Detects when price sweeps a swing level and reverses
            // Notation: Low0/High0 = most recent (LL1/HH1), Low1/High1 = previous (LL2/HH2), etc.

            // Get the close price of the HTF candle that formed LL1 and HH1
            int ll1HTFIndex = swingBarsHTF.OpenTimes.GetIndexByTime(swingLowPrices[0].SwingHTFOpenTime);
            int hh1HTFIndex = swingBarsHTF.OpenTimes.GetIndexByTime(swingHighPrices[0].SwingHTFOpenTime);
            double ll1CandleClose = (ll1HTFIndex >= 0) ? swingBarsHTF.ClosePrices[ll1HTFIndex] : 0;
            double hh1CandleClose = (hh1HTFIndex >= 0) ? swingBarsHTF.ClosePrices[hh1HTFIndex] : 0;

            // Swing prices (using index notation: 0 = most recent)
            double low0 = swingLowPrices[0].SwingPrice;   // LL1
            double low1 = swingLowPrices[1].SwingPrice;   // LL2
            double low2 = swingLowPrices[2].SwingPrice;   // LL3
            double high0 = swingHighPrices[0].SwingPrice; // HH1
            double high1 = swingHighPrices[1].SwingPrice; // HH2
            double high2 = swingHighPrices[2].SwingPrice; // HH3

            // BULLISH TREND with liquidity sweep (analyze lows):
            if (_swingsDirection == BULLISH)
            {
                // Case 1: Low1 < Low2 AND Low0 > Low1 AND High0 > High1
                // → LL2 swept below LL3 and price reversed (LL1 > LL2, HH1 > HH2)
                bool case1 = (low1 < low2) && (low0 > low1) && (high0 > high1);

                // Case 2: Low0 < Low1 AND (Close0 > Low1 OR High0 > High1)
                // → New low made (LL1 < LL2) but rejected (closed above or highs confirm)
                bool case2 = (low0 < low1) && (ll1CandleClose > low1 || high0 > high1);

                if (EnablePrintSwings)
                {
                    Print($"=== LIQUIDITY SWEEP ANALYSIS (BULLISH) ===");
                    Print($"LL3={low2:F5}, LL2={low1:F5}, LL1={low0:F5}, LL1 Close={ll1CandleClose:F5}");
                    Print($"HH2={high1:F5}, HH1={high0:F5}");
                    Print($"Case1 (LL2 swept LL3 + reversed): {case1}");
                    Print($"Case2 (New low rejected): {case2}");
                }

                if (case1 || case2)
                {
                    if (EnablePrintSwings)
                        Print($"=> Liquidity Sweep DETECTED (Bullish): {(case1 ? "Case1 - LL2 swept and reversed" : "Case2 - New low rejected")}");
                    return true;
                }
            }

            // BEARISH TREND with liquidity sweep (analyze highs):
            if (_swingsDirection == BEARISH)
            {
                // Case 1: High1 > High2 AND High0 < High1 AND Low0 < Low1
                // → HH2 swept above HH3 and price reversed (HH1 < HH2, LL1 < LL2)
                bool case1 = (high1 > high2) && (high0 < high1) && (low0 < low1);

                // Case 2: High0 > High1 AND (Close0 < High1 OR Low0 < Low1)
                // → New high made (HH1 > HH2) but rejected (closed below or lows confirm)
                bool case2 = (high0 > high1) && (hh1CandleClose < high1 || low0 < low1);

                if (EnablePrintSwings)
                {
                    Print($"=== LIQUIDITY SWEEP ANALYSIS (BEARISH) ===");
                    Print($"HH3={high2:F5}, HH2={high1:F5}, HH1={high0:F5}, HH1 Close={hh1CandleClose:F5}");
                    Print($"LL2={low1:F5}, LL1={low0:F5}");
                    Print($"Case1 (HH2 swept HH3 + reversed): {case1}");
                    Print($"Case2 (New high rejected): {case2}");
                }

                if (case1 || case2)
                {
                    if (EnablePrintSwings)
                        Print($"=> Liquidity Sweep DETECTED (Bearish): {(case1 ? "Case1 - HH2 swept and reversed" : "Case2 - New high rejected")}");
                    return true;
                }
            }

            // If no liquidity sweep pattern found, return false
            if (EnablePrintSwings)
                Print("=> No liquidity sweep detected");
            return false;
        }

        private int CalculateCHoCH()
        {
            // Need at least 4 highs and 4 lows for CHoCH detection
            if (swingHighCount < 4 || swingLowCount < 4)
                return CONTINUATION;

            // Reference levels (older swings)
            double maxOlderHighs = Math.Max(swingHighPrices[2].SwingPrice, swingHighPrices[3].SwingPrice);
            double minOlderLows = Math.Min(swingLowPrices[2].SwingPrice, swingLowPrices[3].SwingPrice);

            // Get the close price of the HTF candle that formed HH1 and LL1
            int hh1HTFIndex = swingBarsHTF.OpenTimes.GetIndexByTime(swingHighPrices[0].SwingHTFOpenTime);
            int ll1HTFIndex = swingBarsHTF.OpenTimes.GetIndexByTime(swingLowPrices[0].SwingHTFOpenTime);

            double hh1CandleClose = (hh1HTFIndex >= 0) ? swingBarsHTF.ClosePrices[hh1HTFIndex] : 0;
            double ll1CandleClose = (ll1HTFIndex >= 0) ? swingBarsHTF.ClosePrices[ll1HTFIndex] : 0;

            if (EnablePrintSwings)
            {
                Print($"=== CHoCH ANALYSIS ===");
                Print($"HH4={swingHighPrices[3].SwingPrice:F5}, HH3={swingHighPrices[2].SwingPrice:F5}, HH2={swingHighPrices[1].SwingPrice:F5}, HH1={swingHighPrices[0].SwingPrice:F5}");
                Print($"LL4={swingLowPrices[3].SwingPrice:F5}, LL3={swingLowPrices[2].SwingPrice:F5}, LL2={swingLowPrices[1].SwingPrice:F5}, LL1={swingLowPrices[0].SwingPrice:F5}");
                Print($"Max older highs (HH3,HH4)={maxOlderHighs:F5}, Min older lows (LL3,LL4)={minOlderLows:F5}");
                Print($"HH1 candle close={hh1CandleClose:F5}, LL1 candle close={ll1CandleClose:F5}");
            }

            // ============================================
            // CHoCH BULLISH: Requires previous bearish structure (LH) then a break
            // - Previous structure must be bearish: HH2 < HH3 (lower highs)
            // - Then HH1 > HH2 (breaks the lower high pattern)
            // - The candle that formed HH1 must have closed above HH2
            // ============================================
            bool previousBearishStructure = swingHighPrices[1].SwingPrice < swingHighPrices[2].SwingPrice; // HH2 < HH3
            bool hh1AboveHH2 = swingHighPrices[0].SwingPrice > swingHighPrices[1].SwingPrice;
            bool hh1CandleClosedAboveHH2 = hh1CandleClose > swingHighPrices[1].SwingPrice;

            if (previousBearishStructure && hh1AboveHH2 && hh1CandleClosedAboveHH2)
            {
                if (EnablePrintSwings)
                    Print($"=> CHoCH BULLISH: HH2 < HH3 (bearish structure), then HH1 > HH2, candle closed above HH2");
                return CHOCH_BULLISH;
            }

            // ============================================
            // CHoCH BEARISH: Requires previous bullish structure (HL) then a break
            // - Previous structure must be bullish: LL2 > LL3 (higher lows)
            // - Then LL1 < LL2 (breaks the higher low pattern)
            // - The candle that formed LL1 must have closed below LL2
            // ============================================
            bool previousBullishStructure = swingLowPrices[1].SwingPrice > swingLowPrices[2].SwingPrice; // LL2 > LL3
            bool ll1BelowLL2 = swingLowPrices[0].SwingPrice < swingLowPrices[1].SwingPrice;
            bool ll1CandleClosedBelowLL2 = ll1CandleClose < swingLowPrices[1].SwingPrice;

            if (previousBullishStructure && ll1BelowLL2 && ll1CandleClosedBelowLL2)
            {
                if (EnablePrintSwings)
                    Print($"=> CHoCH BEARISH: LL2 > LL3 (bullish structure), then LL1 < LL2, candle closed below LL2");
                return CHOCH_BEARISH;
            }

            // No CHoCH detected - market in continuation
            if (EnablePrintSwings)
                Print("=> CONTINUATION (No CHoCH pattern detected)");
            return CONTINUATION;
        }

        private void DisplaySwingsTrend()
        {
            // Remove existing text objects with names starting with "SwingsText"
            foreach (var obj in Chart.Objects.Where(o => o.Name.StartsWith("SwingsText")).ToList())
            {
                Chart.RemoveObject(obj.Name);
            }
            
            string dashBoard = "";
            
            // Display the time frame of the swings
            dashBoard = "Time frame of the swings: " + SwingTimeFrame;
            Chart.DrawStaticText("SwingsTextTF", dashBoard, VerticalAlignment.Top, HorizontalAlignment.Right, Color.SlateGray);
            
            // Display the Trend at the top right of the chart
            dashBoard = "\n";

            if (_swingsDirection == BULLISH)
            {
                string statusText = _trendStatus == MOMENTUM ? "Momentum" : "Compression";
                dashBoard += $"Trend: BULLISH {statusText}";
                Chart.DrawStaticText("SwingsTextDirextion", dashBoard, VerticalAlignment.Top, HorizontalAlignment.Right, Color.LimeGreen);
            }
            else if (_swingsDirection == BEARISH)
            {
                string statusText = _trendStatus == MOMENTUM ? "Momentum" : "Compression";
                dashBoard += $"Trend: BEARISH {statusText}";
                Chart.DrawStaticText("SwingsTextDirextion", dashBoard, VerticalAlignment.Top, HorizontalAlignment.Right, Color.Red);
            }
            else
            {
                dashBoard += "Trend: UNCLEAR";
                Chart.DrawStaticText("SwingsTextDirextion", dashBoard, VerticalAlignment.Top, HorizontalAlignment.Right, Color.Orange);
            }
            
            // Display the CHoCH status
            dashBoard = "\n\n";

            if (_chochStatus == CHOCH_BULLISH)
            {
                dashBoard += "CHoCH Bullish";
                Chart.DrawStaticText("SwingsTextCHoCH", dashBoard, VerticalAlignment.Top, HorizontalAlignment.Right, Color.LimeGreen);
            }
            else if (_chochStatus == CHOCH_BEARISH)
            {
                dashBoard += "CHoCH Bearish";
                Chart.DrawStaticText("SwingsTextCHoCH", dashBoard, VerticalAlignment.Top, HorizontalAlignment.Right, Color.Red);
            }
            else
            {
                dashBoard += "Continuation";
                Chart.DrawStaticText("SwingsTextCHoCH", dashBoard, VerticalAlignment.Top, HorizontalAlignment.Right, Color.WhiteSmoke);
            }

            // Display the liquidity sweep
            dashBoard = "\n\n\n";

            if (_liquiditySweep)
            {
                dashBoard += "Liquidity sweep detected";
                Chart.DrawStaticText("SwingsTextLiquidity", dashBoard, VerticalAlignment.Top, HorizontalAlignment.Right, Color.LightYellow);
            }
            else
            {
                dashBoard += "No liquidity sweep";
                Chart.DrawStaticText("SwingsTextLiquidity", dashBoard, VerticalAlignment.Top, HorizontalAlignment.Right, Color.WhiteSmoke);
            }

            // Display the expansion size in pips only if we have valid swings
            if (swingHighCount > 0 && swingLowCount > 0)
            {
                double _swingsExpansion = swingHighPrices[0].SwingPrice - swingLowPrices[0].SwingPrice;
                _swingsExpansion /= Symbol.PipSize;
                _swingsExpansion = Math.Round(_swingsExpansion, 2);

                dashBoard = "\n\n\n\nSwing Expansion in pips:\t" + _swingsExpansion;
                Chart.DrawStaticText("SwingsTextExpansion", dashBoard, VerticalAlignment.Top, HorizontalAlignment.Right, Color.LightBlue);
            }
            
            // Build the second string with last 3 swings highs & lows
            /*
            if (swingHighCount >= 3 && swingLowCount >= 3)
            {
                string highPriceText = string.Format("Swing High -2: \t {0:F" + Symbol.Digits + "}", swingHighPrices[2].SwingPrice);
                highPriceText += string.Format(" \n Swing High -1: \t {0:F" + Symbol.Digits + "}", swingHighPrices[1].SwingPrice);
                highPriceText += string.Format("\n Last Swing High: {0:F" + Symbol.Digits + "}", swingHighPrices[0].SwingPrice);
                
                string lowPriceText = string.Format("Swing Low -2: \t {0:F" + Symbol.Digits + "}", swingLowPrices[2].SwingPrice);
                lowPriceText += string.Format("\n Swing Low -1: \t {0:F" + Symbol.Digits + "}", swingLowPrices[1].SwingPrice);
                lowPriceText += string.Format("\n Last Swing Low: {0:F" + Symbol.Digits + "}", swingLowPrices[0].SwingPrice);
                
                dashBoard = $"\n\n\n\n\n{highPriceText}\n\n{lowPriceText}";
                
                // Display Swings at the top right of the chart, below the trend
                Chart.DrawStaticText("SwingsTextLastSwings", dashBoard, VerticalAlignment.Top, HorizontalAlignment.Right, Color.SlateGray);
            }
            */
        }

        private bool IsSwingHigh(Bars candleArray, int index, int period)
        {
            int middleIndex = period / 2;
            double middleValue = candleArray.HighPrices[index];
            
            if (index - middleIndex < 0)
                return false;
            
            for (int i = 0; i < period; i++)
            {
                if (i != middleIndex && candleArray.HighPrices[index - middleIndex + i] >= middleValue)
                {
                    return false;
                }
            }
            return true;
        }

        private bool IsSwingLow(Bars candleArray, int index, int period)
        {
            int middleIndex = period / 2;
            double middleValue = candleArray.LowPrices[index];

            if (index - middleIndex < 0)
                return false;

            for (int i = 0; i < period; i++)
            {
                if (i != middleIndex && candleArray.LowPrices[index - middleIndex + i] <= middleValue)
                {
                    return false;
                }
            }
            return true;
        }
        
        private int GetTimeFrameInSeconds(TimeFrame timeFrame)
        {
            switch (timeFrame.ToString())
            {
                case "Minute":
                    return 60;
                case "Minute2":
                    return 2 * 60;
                case "Minute3":
                    return 3 * 60;
                case "Minute4":
                    return 4 * 60;
                case "Minute5":
                    return 5 * 60;
                case "Minute10":
                    return 10 * 60;
                case "Minute15":
                    return 15 * 60;
                case "Minute20":
                    return 20 * 60;
                case "Minute30":
                    return 30 * 60;
                case "Minute45":
                    return 45 * 60;
                case "Hour":
                    return 60 * 60;
                case "Hour2":
                    return 2 * 60 * 60;
                case "Hour3":
                    return 3 * 60 * 60;
                case "Hour4":
                    return 4 * 60 * 60;
                case "Hour6":
                    return 6 * 60 * 60;
                case "Hour8":
                    return 8 * 60 * 60;
                case "Hour12":
                    return 12 * 60 * 60;
                case "Daily":
                    return 24 * 60 * 60;
                case "Weekly":
                    return 7 * 24 * 60 * 60;
                case "Monthly":
                    return 30 * 24 * 60 * 60; // Approximation
                default:
                    throw new ArgumentException("Unsupported timeframe: " + timeFrame.ToString());
            }
        }
    }
}
