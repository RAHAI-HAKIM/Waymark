# Project organisation

Hakim's roadmap across the four stages, and the running log. Compacted 13/09/2026:
phase details live in `Waymark_Build_Plan.md` and progress in `status.md`.

## The project

A retail management system for Algerian SMB retailers, with a decision-support engine. The
original distinctive features, all now placed in the phase plan:

1. **Stockout and overstock cost**: money lost both ways, as one figure
2. **Substitution effect**: does B sell more when A is out?
3. **Seasonality decomposition**: trend, seasonal, random
4. **Multi-product ordering under a budget**, respecting expiry
5. **What-if simulation**: price change, elasticity and substitutes together
6. **Demand forecast plus ordering quantities**: EOQ, safety stock, reorder points (the heart)
7. **Expiration-aware ordering**: the locally distinctive part

## Stages

| Stage | Period | State |
| :---- | :---- | :---- |
| 1. Learning (8 weeks) | 13/06 – 07/08/2026 | Done. Code in `learning/` |
| 2. Design and engineering | 08/08 – 31/08/2026 | Done: market research, module design, brand identity, operating rules |
| 3. Implementation (16–22 weeks) | 01/09/2026 – | Phase 0 complete (17/09/2026); **Phase 0.5 next** (`status.md`) |
| 4. Testing and refinement (4 weeks) | — | Not started |

Stage 1 phases: Foundations (Polars, visualisation) · Statistical core (ISL ch. 2–4, 6–7)
· Time series (FPP3, ARIMA pipeline on StatsForecast) · Operations research (Hillier &
Lieberman: LP, IP, decision analysis, inventory theory, simulation; newsvendor in
CP-SAT).

Stage 2 produced the documents now in `docs/`. The brand identity is
`Waymark_Brand_Identity.pptx`, with its locked rules summarised in CLAUDE.md §6.

## Log

| Day | Note |
| :---- | :---- |
| 18/06 | DuckDB adopted: the database hosts the data and Polars analyses exactly what is extracted |
| 25/06 | ISL ch. 2–3 done; own regression module (simple, multiple, polynomial, interactions) |
| 27/06 | Logistic regression via statsmodels; subset selection implemented; CV, lasso and ridge next |
| 02/07 | Statistical core done, beyond plan (non-linearity, model selection): the `stat_learning` module |
| 09/07 | Benchmark forecasters and metrics done (mean, naive, seasonal naive, drift); ETS next |
| 17/07 | Time series done: an ARIMA pipeline on StatsForecast (CV, dynamic regression, seasonality, validation) |
| 24/07 | OR week 1: intro, LP, simplex overview, sensitivity analysis, IP |
| 05/08 | Decision analysis, inventory theory, simulation done: directly useful for the engine |
| 07/08 | Learning stage closed on time, with Monte Carlo cash-risk and stochastic inventory cases |
| 01/09 | Implementation starts. Stack and layout decided (`Waymark_Implementation.md`) |
| 13/09 | Phase 0 review: W1–W9 and W11 done, W10 next; docs compacted |

## Readings

1. [Predictive analytics in retail](https://coaxsoft.com/blog/predictive-analytics-in-retail)
2. [IDC MarketScape: retail assortment planning (Oracle)](https://www.oracle.com/a/ocom/docs/gated/idc-marketscape-retail-assortment-planing-solutions.pdf)
3. [Oracle Retail](https://www.oracle.com/retail/) · [SAP Retail](https://www.sap.com/industries/retail.html)
4. [Solving operational statistics via a Bayesian analysis](https://www.sciencedirect.com/science/article/abs/pii/S0167637707000636)
