import polars as pl
import random
import numpy as np
from dataclasses import dataclass, asdict

pl.Config.set_tbl_cols(-1)
pl.Config.set_tbl_rows(-1)
pl.Config.set_fmt_str_lengths(1000)

"""seasonality_factors = pl.DataFrame({"month": 
                                    ["January", "February", "March", "April", "May", "June", 
                                    "July", "August", "September", "October", "Novermber", "December"],
                                    "factor":
                                    [0.79, 0.88, 0.95, 1.05, 1.09, 0.84, 0.74, 0.98, 1.06, 1.1, 1.16, 1.18]})

print(seasonality_factors)"""

SEASONALITY = {
    1: 0.72,
    2: 0.82,
    3: 0.93,
    4: 1.08,
    5: 1.14,
    6: 0.96,
    7: 0.87,
    8: 0.82,
    9: 0.94,
    10: 1.06,
    11: 1.38,
    12: 1.18
}

UNIT_PRICE = 10
FIXED_COST = 15000
MIN_CASH = 20000

@dataclass
class MonthState:

    month: int

    base_sales: float
    actual_sales: float

    cash_ratio: float

    income: float
    cost: float
    repair_cost: float

    cash_balance: float

    loan: float

    prime: float
    loan_interest: float
    saving_interest: float

def sample_prime_change():

    return random.choices(
        population=[0,0.25,-0.25,0.5,-0.5],
        weights=[70,10,10,5,5],
        k=1
    )[0]

def simulate_month(previous: MonthState) -> MonthState:

    month = previous.month % 12 + 1

    # ------------------------
    # Random variables
    # ------------------------

    base_sales = np.random.normal(previous.base_sales,500)

    cash_ratio = np.random.triangular(
        left=0.28,
        mode=0.40,
        right=0.48
    )

    unit_cost = np.random.uniform(6,8)

    repairs = 5000*np.random.binomial(8,0.1)

    prime = previous.prime + sample_prime_change()

    # ------------------------
    # Deterministic calculations
    # ------------------------

    actual_sales = base_sales * SEASONALITY[month]

    income = (
        actual_sales*cash_ratio*UNIT_PRICE
        +
        previous.actual_sales*(1-previous.cash_ratio)*UNIT_PRICE
    )

    cost = actual_sales*unit_cost + FIXED_COST

    loan_interest = min(prime+2,9)

    saving_interest = max(prime-2,2)

    cash_balance = (
        income
        + previous.cash_balance*(1+previous.saving_interest/100/12)
        - cost
        - repairs
        - previous.loan*(1+previous.loan_interest/100/12)
    )

    loan = max(0.0,MIN_CASH-cash_balance)

    cash_balance += loan

    return MonthState(

        month=month,

        base_sales=base_sales,
        actual_sales=actual_sales,

        cash_ratio=cash_ratio,

        income=income,
        cost=cost,
        repair_cost=repairs,

        cash_balance=cash_balance,

        loan=loan,

        prime=prime,
        loan_interest=loan_interest,
        saving_interest=saving_interest
    )

initial_state = MonthState(

    month=12,

    base_sales=6000,
    actual_sales=7080,

    cash_ratio=0.42,

    income=0,
    cost=0,
    repair_cost=0,

    cash_balance=25000,

    loan=0.0,

    prime=5.0,
    loan_interest=7,
    saving_interest=3
)

def run_simulation(n_months=12):

    history=[]
    state=initial_state

    for _ in range(n_months):
        state=simulate_month(state)
        history.append(asdict(state))

    return pl.DataFrame(history)

def monte_carlo(n_scenarios=1000,n_months=12):

    scenarios=[]

    for s in range(n_scenarios):

        df=run_simulation(n_months)
        df=df.with_columns(
            pl.lit(s+1).alias("scenario")
        )
        scenarios.append(df)

    return pl.concat(scenarios)

def net_worth(n_scenarios=1000, n_months=12) -> pl.DataFrame:
    scenarios=[]

    for s in range(n_scenarios):

        df=run_simulation(n_months)
        df=df.with_columns(
            pl.lit(s+1).alias("scenario")
        )
        scenarios.append(df.filter(pl.col("month") == 12)
                        .with_columns(
                            (
                                pl.col("cash_balance") * (1 + pl.col("saving_interest")/100/12)
                                + pl.col("actual_sales") * ((1 - pl.col("cash_ratio"))* UNIT_PRICE)
                                - pl.col("loan") * (1 + pl.col("loan_interest")/100/12)
                            ).alias("net_worth")
                        )
                        )

    return pl.concat(scenarios)

def credit_limit(n_scenarios=1000, n_months=12) -> pl.DataFrame:
    scenarios=[]

    for s in range(n_scenarios):

        df=run_simulation(n_months)
        df=df.with_columns(
            pl.lit(s+1).alias("scenario")
        )
        scenarios.append(df
                        .with_columns(pl.col("loan").max().alias("limit"))
                        .select("scenario", "limit").unique()
                        )

    return pl.concat(scenarios)

result = credit_limit(1000)
#graphic = result.plot.point("scenario", "net_worth")
#graphic.save("graphic.html")
print(result.select("limit").describe(percentiles=[0.5, 0.75, 0.9, 0.95, 0.99]))


