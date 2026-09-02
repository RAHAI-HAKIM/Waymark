# learning/

Stage 1 of the project: eight weeks, 13 June – 7 August 2026. Kept in the
repository because these modules are the reference implementations the Almanac
engine is checked against — when `waymark-engine` produces a forecast interval,
this is where the method it implements was first worked through and understood.

| Directory | Phase | Contents |
| :---- | :---- | :---- |
| `phase1/` | Foundations | Polars, data handling, 3NF tables, DuckDB querying |
| `stat_learning/` | Statistical core | ISL ch. 2–4, 6–7. Regression, inference, model selection, non-linearity |
| `forecasting/` | Time series | FPP3. Decomposition, benchmark methods, exponential smoothing, ARIMA pipeline over StatsForecast |
| `operation_research/` | OR | Hillier & Lieberman. LP, IP, decision analysis, inventory theory, Monte Carlo simulation |
| `M5/` | Practice | The M5 competition dataset, used throughout |

Datasets (`*.csv`) and databases (`*.db`) are gitignored — they are large and
externally sourced. The scripts that read them record where they came from.

The running log for this stage, including what was learned when and what went
wrong, is in `docs/Project_Organization.md`.
