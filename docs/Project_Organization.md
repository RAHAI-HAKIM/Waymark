# Project Presentation:

This document presents the necessary information needed for completing my summer project **The intelligent retail store management system.**  
Which consists of a *decision support system* that offers the basic features for this type of software from keeping track of customers, products, financial state .., in addition to some additional features listed below:

1. **Stockout/overstock cost analysis** — quantify money lost both ways (lost sales vs spoilage/holding cost). This turns your analysis into a dollar figure, which is what business people actually care about.  
2. **Substitution effect analysis** — when product A is out of stock, does product B's sales increase? This requires correlation/causality thinking and is rarely done by student projects.  
3. **Seasonality decomposition** — separating trend, seasonal, and random components in sales data. Classic time series, very visual, very explainable.  
4. **Multi-product ordering optimization** — given a budget constraint, what combination of products to order maximizes expected profit while respecting expiration?  
5. **What-if simulation** — "if you raise this product's price by 10%, here's the expected effect on revenue AND on substitute products." This ties price elasticity \+ substitution together into a "decision impact" feature,  
6. **Demand prediction \+ ordering quantities** — this is the heart of the project. Classic inventory theory (EOQ, safety stock, reorder points) combined with demand forecasting. Rich stat content.   
7. **Expiration-aware ordering** — this is the *locally distinctive* part. Perishable inventory models exist in OR literature but are less commonly implemented in student projects. This is your differentiator.   
     
   *Other features to be considered later*

# 

# Real-Time workflow of the project(2b strictly respected):

## Stage 01: Learning (8 weeks)

| Learning roadmap |  |  |  |
| :---: | :---: | :---: | :---: |
| Period | ![No type][image1] Phase | ![Dropdowns][image2] Status | ![No type][image1] Notes |
| 13-19 / 06 | **Foundations**  | Done | Polars (core operations)  Pandas if needed for compatibility Basic data visualization (matplotlib/seaborn)  Practice on public retail/sales datasets (Kaggle).  **Goal**: comfortable manipulating and visualizing tabular data without friction.  |
| 20/06 \-03/07 | **Statistical core**  | Done | ISL Ch 2-3 (regression, inference framing) applied to the practice dataset — estimate price elasticity, build confidence intervals on coefficients, run hypothesis tests on whether certain factors matter.-- **Goal**: be able to answer "is this effect real or noise" rigorously.  |
| 04-17 / 07 | **Time series**  | Done | FPP3 chapters listed, applied to seasonal sales data. Build a basic forecasting pipeline (decomposition \+ ARIMA \+ evaluation).  **Goal**: working demand forecast with uncertainty bounds.  |
| 18/07-07/08 (including 1 week holiday) | **Operations research**  | Done | Newsvendor model, then formulate it in OR-Tools CP-SAT. Start simple (single product, single period) then extend to multi-product with budget constraint.  **Goal**: given a forecast \+ the given costs, output an ordering recommendation.  |

### **Encountered issues/messages**

| Day | Notes |
| :---: | :---: |
| **18/06** | Discovered about duck\_db, from now on the database will host the data, all query operations will be held on it. This will result in fast/efficient extract of exactly what’s needed for polars to do it’s analysis work . |
| **25/06** | We have 1 day before the end of week1 of phase2, I studied chapters 2 and 3, practiced on the given datasets and created my own regression computation modules that help analyzing: simple/multiple/polynomial regression and Interactions.In the next week I’ll try my best to understand chapters 4,6,7 which will help me do interesting stuff with my dataset later.  |
| **27/06** | First day of week2 phase1, I studied logistic regression but only theoretical stuff and applying using the **statsmodels** library (since implementation was a bit complicated and not that necessary)I also studied most of what I need from chapter6:**model\_selection,** did implement methods for subset selection but other approaches like validation and cross-validation are yet to be implemented (also lasso and ridge regression) hopefully by tomorrow inshallah. |
| **02/07** | We have one last day before phase3, phase2 was really successful I tried to get as much details as I could and managed to do chapters that weren’t planned like **non-linearity** and **model-selection,** Now the file pushed to github as **stat\_learning** will act as a module I built for doing all the stuff learned in this phase, I’ll practice a bit in the last day on our M5\_dataset before starting phase3\! |
| **09/07** | Today I finished the remaining part from implementing the benchmark forecasting methods with their metrics (mean, naive, seasonal naive and drift) So chap.5 is done and I’ll start **Exponential smoothing** today. |
| **17/07** | The end of phase 3, most challenging till now with loads of information, I was able to properly make use of **Statsforecast** library to create a pipeline method for **ARIMA** that is able to handle many options (cv, dynamic regression, seasonality, validation set, …) I believe we can refer to this pipeline as great revision and tool for doing the real work |
| **24/07** | Today is the end of phase 4’s first week. At the start I figured out that there are many chapters I need to study from the **HILLIER & LIBERMAN** textbook. Till now I’m done with the introduction (important general points about OR), LP and overview of Simplex method (Didn’t take time to learn it’s algebra), sensitivity analysis and IP. I practiced on some cases which provide a good level of understanding. At least 3 more chapters to go. |
| **05/08** | We are 2 days before the end. Actually I made good progress, I studied Decision Analysis, Inventory theory and Simulation which turned out to be extremely efficient for this project, we finished this phase with a fairly good level of understanding.I took a week of holiday (The one we saved from week 1\) so we are aligned with 8 weeks requirement, In the next 2 days I’ll practice more and prepare for the stage 2 roadmap. |
| **07/08** | The end of phase 4 and the learning stage, we were exactly on time, the last 2 days I practiced on a Monte Caro simulation case with cash modeling and risk analysis, and a stochastic inventory management case. So excited for next stage\! |

## Stage 02: Design and engineering (6-8 weeks)

| D\&E roadmap |  |  |  |
| :---: | :---: | :---: | :---: |
| Period | ![No type][image1] Phase | ![Dropdowns][image2] Status | ![No type][image1] Notes |
| 08-18 / 08 | **Research & current market analysis** | Done | Make a deep research on how basic retail management systems work, and on how competitors (like oracle) implemented theirs, also doing a survey in my city for data needed in phase 2\. |
| 19-26 /08 | **Modules selection & Product design** | Done | Brain storming and searching for all possible services, selecting most relevant ones and designing the whole pipeline in an organized way. |
| 27-31 / 08 (This phase is still considered after the due date) | **Brand identity design** | In progress | Decide on all relevant project details: Project name, Logo, color palette, guidelines, policies …. |

### **Brand identity:**

**Commercial, legal and operational rules** are maintained in [Waymark\_Operating\_Rules](https://docs.google.com/document/d/1BXrD18DRwEBrVGl5ea83uhHn7ApngKiJ78uOGk0vNEg/edit?usp=sharing), which is the authoritative source. It covers the commercial model and tiers, offline behaviour and hosting, customer lifecycle and the founding programme, legal vehicle and payment collection, privacy rules, liability and data ownership, and positioning and messaging. Anything remaining or deferred is listed in its final section.

**Design identity** is in [Waymark Brand Identity Design](https://docs.google.com/presentation/d/10DsUZFRSS561ahfdEb4iwLczVrVUDg1iCjY_6Xt8BwY/edit?usp=sharing),. **Privacy** is assessed in [Waymark\_DPIA\_v1.md](https://docs.google.com/document/u/0/d/1F9SZiZPnJag6QJnrxy8CB2Mui131te8w_13wadrKnUY/edit). **Architecture** is in [System Architecture](https://docs.google.com/document/u/0/d/1SdpDhN4uBULYBaM-sC5gy4ntEVxWp3S8ay53USoRwAc/edit).

## Stage 03: Implementation (16-22 weeks)

| Implementation roadmap |  |  |  |
| :---: | :---: | :---: | :---: |
| Period | ![No type][image1] Phase | ![Dropdowns][image2] Status | ![No type][image1] Notes |
| 01/09 \- ? **Estimate: 50–70 h.** | **Foundation**  | In progress | Repo, solution layout, full schema, migrations, decimal discipline, store scoping, domain model, synthetic store generator, `CLAUDE.md`, decision log. Everything after this assumes it exists.  |
| ? \- ? | **The till runs a shop.**  | Not started | POS categories A (checkout), B (shift and end-of-day), D (inventory and stock), plus C in manual/CSV form. Local Admin for catalogue, stock and store setup. Auth, roles, customers and suppliers, Customers, consent capture, rights tooling, `consent_events`, `data_subject_requests`. PIN gating. Offline Level 1 complete. Backup and restore. *Demo:* a real shop could open on this. Nothing intelligent yet.  |
| ?-? | **The engine speaks.**  | Not started | Statistics tier 1→2 boundary, the \~8 stats the first departments need. Integration Layer both halves, recommendation envelope, pending queue, POS category K. Inventory Group 1 complete, Group 3 expiry detection with the 3-stage markdown path. Because blocks, intervals, Almanac cards. *Demo:* this is the pitch. Expiry works from day one with zero history — the strongest single claim, end to end.  |
| ?-? | **It orders for you**  | Not started | Sales & Demand forecast with intervals. Inventory Group 2: reorder point, safety stock, suggested quantity, expected stockout date. Accept creates a Purchase Order. Categories F and G complete. *Demo:* the full Basic tier engine depth as written in the Operating Rules.  |
| ?-? | **Reachable and remembering**  | Not started | Cloud Admin as PWA. Sync in both directions, decisions from the cloud applied at the store. Categories E, H, I, L. *Demo:* check your shop from your phone; customer details never leave it. Also where the DPIA becomes true rather than designed.  |
| ?-? | **Multi-terminal**  | Not started | Offline Level 2 full reconciliation, hot replica, flagged-transaction review queue. Supply department: lead-time tracking, supplier comparison. *Demo:* Pro archetype is now real.  |
| ?-? | **Planning**  | Not started | Budget-constrained multi-product ordering, CP-SAT. The remaining statistics.  |
| ?-? | **Depth**  | Not started | Groups 3 overstock/dead stock/declining, product performance conclusions, substitution, elasticity, revealed-preference tuning, remaining Planning services.  |

### **Encountered issues/messages**

| Day | Notes |
| ----- | :---: |
|  |  |
|  |  |
|  |  |
|  |  |
|  |  |
|  |  |
|  |  |
|  |  |
|  |  |
|  |  |

## Stage 04: Testing and Refinement (4 weeks)

| T\&R roadmap |  |  |  |
| :---: | :---: | :---: | :---: |
| Period | ![No type][image1] Phase | ![Dropdowns][image2] Status | ![No type][image1] Notes |
|  |  | Not started |  |
|  |  | Not started |  |
|  |  | Not started |  |
|  |  | Not started |  |

### **Encountered issues/messages**

| Day | Notes |
| ----- | :---: |
|  |  |
|  |  |
|  |  |
|  |  |
|  |  |
|  |  |
|  |  |
|  |  |
|  |  |
|  |  |

# Project-Related Readings

1. [Predictive analytics in retail: Behavior analysis tools & practices](https://coaxsoft.com/blog/predictive-analytics-in-retail)  
2. [Oracle Strategy](https://www.oracle.com/a/ocom/docs/gated/idc-marketscape-retail-assortment-planing-solutions.pdf)  
3. [Retail | Oracle](https://www.oracle.com/retail/)  
4. [Retail Industry Software | SAP](https://www.sap.com/industries/retail.html)  
5. [Solving operational statistics via a Bayesian analysis \- ScienceDirect](https://www.sciencedirect.com/science/article/abs/pii/S0167637707000636)

[image1]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABQAAAAQAQMAAAAs1s1YAAAABlBMVEUAAABER0byc6G0AAAAAXRSTlMAQObYZgAAAB9JREFUeF5jYEAD9h8YmEA0MwOYZmSWWQjhs4H56BgAT4ECDeGaeV4AAAAASUVORK5CYII=>

[image2]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABQAAAAQCAYAAAAWGF8bAAAAx0lEQVR4Xu2TYRHCMAyFKwEJSEBCjyVpXIAEHIATJCBhEpCAhEkA0tEtTVcod/zku8ufvDR7fduc+/NTmHkNge6t1QU82x0TQHRMg8i4t7rGI266QJc0b3Xt7Gq1d3jvV69zQyZUn9QAMHg5K8vnpuTBtFNzXzEawj5rKL0AmE7pFku3KXrR8jPHeaRELy00m2NhuYIstT1hPB8OqoF9dKmDbQQD3ZZcTznIJ2S1GmlZ5k4DhENa3FrbDz+Bk5eTIqhVdFbJ8wG0lJX5M/zhmwAAAABJRU5ErkJggg==>