import numpy as np
np.set_printoptions(suppress=True)
np.random.seed(1)
import altair as alt
import random
random.seed(1)
import pandas as pd
import polars as pl
pl.Config.set_tbl_cols(-1)
pl.Config.set_tbl_rows(-1)
pl.Config.set_fmt_str_lengths(1000)
import seaborn as sns
sns.set_style("whitegrid")
import matplotlib.pyplot as plt
plt.style.use("ggplot")
plt.rcParams.update({
    "figure.figsize": (8, 5),
    "figure.dpi": 100,
    "savefig.dpi": 300,
    "figure.constrained_layout.use": True,
    "axes.titlesize": 12,
    "axes.labelsize": 10,
    "xtick.labelsize": 9,
    "ytick.labelsize": 9,
    "legend.fontsize": 9,
    "legend.title_fontsize": 10,
    "grid.alpha": 1.0,
})
import matplotlib as mpl
from cycler import cycler
mpl.rcParams['axes.prop_cycle'] = cycler(color=["#000000", "#000000"])
import datetime as dt
import plotly.express as exp
from pathlib import Path
import sys
target_directory = str(Path("C:/Users/HAKIM/Desktop/Retail_Project/QRetail/stat_learning").resolve())
sys.path.append(target_directory)
import stat_learning as stat
import scipy.stats as sc_stats
from scipy.optimize import minimize
from statsforecast import StatsForecast
from statsforecast.models import AutoETS, AutoARIMA, ARIMA
from statsforecast.arima import ARIMASummary
from statsmodels.graphics.tsaplots import plot_acf, plot_pacf
from statsmodels.stats.diagnostic import acorr_ljungbox
from statsmodels.tsa.stattools import kpss

mpl.colormaps.register(
    mpl.colors.ListedColormap(
        ["#000000", "#2f2fff"], name="black_and_blue"),
    force=True,
)
mpl.colormaps.register(
    mpl.colors.ListedColormap(
        ["#000000", "#D55E00"], name="black_and_orange"),
    force=True,
)
mpl.colormaps.register(
    mpl.colors.ListedColormap(
        ["#000000", "#000000"], name="black"),
    force=True,
)
mpl.colormaps.register(
    mpl.colors.ListedColormap(
        ["#000000", "#0072B2", "#D55E00"],
        name='black_and_2color',
    ),
    force=True
)
mpl.colormaps.register(
    mpl.colors.ListedColormap(
        ["#000000", "#D55E00", "#0072B2", "#009E73"],
        name='black_and_3color',
    ),
    force=True
)
mpl.colormaps.register(
    mpl.colors.ListedColormap(
        ["#000000", "#D55E00", "#0072B2", "#009E73", "#CC79A7"],
        name='black_and_4color',
    ),
    force=True
)
mpl.colormaps.register(
    mpl.colors.ListedColormap(
        ["#D55E00", "#0072B2", "#009E73", "#CC79A7"],
        name='r_colors',
    ),
    force=True
)
mpl.rcParams['axes.prop_cycle'] = cycler(color=["#000000", "#2f2fff"])
from statsmodels.tsa.seasonal import STL
from utilsforecast.plotting import plot_series


def mean_forecasting(df: pl.DataFrame, ds_col: str, y_col: str, h: str = "1mo", interval: str = "1d", validation: bool = False):
    """
    Generates a mean baseline forecast with full accuracy metric computation (scale-dependent & scaled).
    
    Returns:
        dict: {
            "forecast": pl.DataFrame,
            "chart": alt.LayerChart,
            "metrics": dict (or None if validation=False)
        }
    """
    # 0. Automatically deduce the seasonal period for scaled metrics
    period = 4 if interval in ["1q", "3mo"] else 12
    
    if validation:
        # 1. Validation Mode Split
        total_end_date = df.select(pl.col(ds_col).max()).item()
        clean_h = h.lstrip("-")
        train_end_date = pl.select(pl.lit(total_end_date).dt.offset_by(f"-{clean_h}")).item()
        
        train_df = df.filter(pl.col(ds_col) <= train_end_date)
        test_df = df.filter(pl.col(ds_col) > train_end_date)
        date_range = test_df.select(pl.col(ds_col)).to_series()
    else:
        # 2. Standard Production Mode
        train_df = df
        last_historical_date = train_df.select(pl.col(ds_col).max()).item()
        start_forecast = pl.select(pl.lit(last_historical_date).dt.offset_by(interval)).item()
        end_forecast = pl.select(pl.lit(last_historical_date).dt.offset_by(h)).item()
        date_range = pl.date_range(start_forecast, end_forecast, interval=interval, eager=True)

    # 3. Core Forecasting Math (Strictly using train_df)
    mean_val = train_df.select(pl.col(y_col).mean()).item()
    sum_et2 = train_df.select((pl.col(y_col) - mean_val).pow(2)).sum().item()
    sigma1_hat = np.sqrt(sum_et2 / (train_df.height - 1))
    sigmah_hat = sigma1_hat * np.sqrt(1 + (1 / train_df.height))

    # 4. Construct Forecast Frame
    df_res = pl.DataFrame({ds_col: date_range}).with_columns(
        pl.lit(mean_val).alias(y_col),
        (pl.lit(mean_val) - 1.28 * sigmah_hat).alias("ci_lower_80"),
        (pl.lit(mean_val) + 1.28 * sigmah_hat).alias("ci_upper_80"),
        (pl.lit(mean_val) - 1.96 * sigmah_hat).alias("ci_lower_95"),
        (pl.lit(mean_val) + 1.96 * sigmah_hat).alias("ci_upper_95")
    )

    # 5. Evaluate Metrics if in Validation Mode
    metrics = None
    if validation:
        df_res = df_res.join(
            test_df.select([ds_col, y_col]).rename({y_col: "actual"}),
            on=ds_col,
            how="left"
        )
        
        # A. Scale-Dependent Errors (MAE, RMSE)
        mae = df_res.select((pl.col("actual") - pl.col(y_col)).abs().mean()).item()
        rmse = df_res.select((pl.col("actual") - pl.col(y_col)).pow(2).mean().sqrt()).item()
        
        # B. Scaled Errors (MASE, RMSSE) using Training Seasonal-Naive as the baseline
        # Polars .mean() automatically ignores the nulls introduced by the .shift()
        hist_mase_denom = train_df.select((pl.col(y_col) - pl.col(y_col).shift(period)).abs().mean()).item()
        hist_rmsse_denom = train_df.select((pl.col(y_col) - pl.col(y_col).shift(period)).pow(2).mean()).item()
        
        mase = mae / hist_mase_denom if hist_mase_denom != 0 else np.nan
        rmsse = rmse / np.sqrt(hist_rmsse_denom) if hist_rmsse_denom != 0 else np.nan
        
        metrics = {
            "scale_dependent": {
                "MAE": round(mae, 4),
                "RMSE": round(rmse, 4)
            },
            "scaled": {
                "MASE": round(mase, 4),
                "RMSSE": round(rmsse, 4)
            }
        }

    # 6. Build Visualization
    base_res = alt.Chart(df_res).encode(x=alt.X(f"{ds_col}:T"))
    
    # Multi-shaded Confidence Interval Bands
    ci_95 = base_res.mark_area(color="blue", opacity=0.15).encode(y="ci_lower_95:Q", y2="ci_upper_95:Q")
    ci_80 = base_res.mark_area(color="blue", opacity=0.30).encode(y="ci_lower_80:Q", y2="ci_upper_80:Q")
    
    # Lines
    fc_chart = base_res.mark_line(color="blue", strokeWidth=2).encode(y=alt.Y(f"{y_col}:Q"))
    old_chart = alt.Chart(train_df).mark_line(color="black").encode(x=alt.X(f"{ds_col}:T"), y=alt.Y(f"{y_col}:Q"))

    if validation:
        test_chart = alt.Chart(test_df).mark_line(color="gray", strokeDash=[2, 2]).encode(x=alt.X(f"{ds_col}:T"), y=alt.Y(f"{y_col}:Q"))
        chart = (ci_95 + ci_80 + old_chart + test_chart + fc_chart).interactive()
    else:
        chart = (ci_95 + ci_80 + old_chart + fc_chart).interactive()

    # Return everything cleanly as a structured dictionary map
    return {
        "forecast": df_res,
        "chart": chart,
        "metrics": metrics
    }

def naive_forecasting(df: pl.DataFrame, ds_col: str, y_col: str, h: str = "5y", interval: str = "1q", validation: bool = False):
    """
    Generates a naive-based baseline forecast for a specified horizon.
    
    Parameters:
    - df: The historical Polars DataFrame.
    - ds_col: Name of the date column.
    - y_col: Name of the target value column.
    - h: Forecast horizon offset string (e.g., "5y", "12mo").
    - interval: The data frequency (e.g., "1mo", "1q", "1d").
    """

    # 0. Automatically deduce the seasonal period for scaled metrics
    period = 4 if interval in ["1q", "3mo"] else 12
    
    if validation:
        # 1. Validation Mode Split
        total_end_date = df.select(pl.col(ds_col).max()).item()
        clean_h = h.lstrip("-")
        train_end_date = pl.select(pl.lit(total_end_date).dt.offset_by(f"-{clean_h}")).item()
        
        train_df = df.filter(pl.col(ds_col) <= train_end_date)
        test_df = df.filter(pl.col(ds_col) > train_end_date)
        date_range = test_df.select(pl.col(ds_col)).to_series()
    else:
        # 2. Standard Production Mode
        train_df = df
        last_historical_date = train_df.select(pl.col(ds_col).max()).item()
        start_forecast = pl.select(pl.lit(last_historical_date).dt.offset_by(interval)).item()
        end_forecast = pl.select(pl.lit(last_historical_date).dt.offset_by(h)).item()
        date_range = pl.date_range(start_forecast, end_forecast, interval=interval, eager=True)
    
    yt = train_df.select(y_col).tail(1).item()

    df_res = pl.DataFrame({
        ds_col: date_range
    }).with_columns(
        pl.lit(yt).alias(y_col)
    )
    # Computing CI for Naive Method
    # 1) sum(et^2) -> Residual is: y_t - y_{t-1}
    sum_et2 = train_df.select((pl.col(y_col) - pl.col(y_col).shift(1)).pow(2)).sum().item()

    # 2) sigma1_hat
    sigma1_hat = np.sqrt(sum_et2 / (train_df.height - 1))

    # 3) sigmah_hat -> Increases over time: sigma * sqrt(h)
    df_res = df_res.with_row_index("h", 
    offset=1).with_columns(
        (pl.col("h").sqrt() * sigma1_hat).alias("sigmah_hat")
    ).with_columns(
        (pl.col(y_col) - 1.28 * pl.col("sigmah_hat")).alias("ci_lower_80"),
        (pl.col(y_col) + 1.28 * pl.col("sigmah_hat")).alias("ci_upper_80"),
        (pl.col(y_col) - 1.96 * pl.col("sigmah_hat")).alias("ci_lower_95"),
        (pl.col(y_col) + 1.96 * pl.col("sigmah_hat")).alias("ci_upper_95")
    ).drop("sigmah_hat")

    # 5. Evaluate Metrics if in Validation Mode
    metrics = None
    if validation:
        df_res = df_res.join(
            test_df.select([ds_col, y_col]).rename({y_col: "actual"}),
            on=ds_col,
            how="left"
        )
        
        # A. Scale-Dependent Errors (MAE, RMSE)
        mae = df_res.select((pl.col("actual") - pl.col(y_col)).abs().mean()).item()
        rmse = df_res.select((pl.col("actual") - pl.col(y_col)).pow(2).mean().sqrt()).item()
        
        # B. Scaled Errors (MASE, RMSSE) using Training Seasonal-Naive as the baseline
        # Polars .mean() automatically ignores the nulls introduced by the .shift()
        hist_mase_denom = train_df.select((pl.col(y_col) - pl.col(y_col).shift(period)).abs().mean()).item()
        hist_rmsse_denom = train_df.select((pl.col(y_col) - pl.col(y_col).shift(period)).pow(2).mean()).item()
        
        mase = mae / hist_mase_denom if hist_mase_denom != 0 else np.nan
        rmsse = rmse / np.sqrt(hist_rmsse_denom) if hist_rmsse_denom != 0 else np.nan
        
        metrics = {
            "scale_dependent": {
                "MAE": round(mae, 4),
                "RMSE": round(rmse, 4)
            },
            "scaled": {
                "MASE": round(mase, 4),
                "RMSSE": round(rmsse, 4)
            }
        }
    # 6. Build Visualization
    base_res = alt.Chart(df_res).encode(x=alt.X(f"{ds_col}:T"))
    
    # Multi-shaded Confidence Interval Bands
    ci_95 = base_res.mark_area(color="blue", opacity=0.15).encode(y="ci_lower_95:Q", y2="ci_upper_95:Q")
    ci_80 = base_res.mark_area(color="blue", opacity=0.30).encode(y="ci_lower_80:Q", y2="ci_upper_80:Q")
    
    # Lines
    fc_chart = base_res.mark_line(color="blue", strokeWidth=2).encode(y=alt.Y(f"{y_col}:Q"))
    old_chart = alt.Chart(train_df).mark_line(color="black").encode(x=alt.X(f"{ds_col}:T"), y=alt.Y(f"{y_col}:Q"))

    if validation:
        test_chart = alt.Chart(test_df).mark_line(color="gray", strokeDash=[2, 2]).encode(x=alt.X(f"{ds_col}:T"), y=alt.Y(f"{y_col}:Q"))
        chart = (ci_95 + ci_80 + old_chart + test_chart + fc_chart).interactive()
    else:
        chart = (ci_95 + ci_80 + old_chart + fc_chart).interactive()

    # Return everything cleanly as a structured dictionary map
    return {
        "forecast": df_res,
        "chart": chart,
        "metrics": metrics
    }

def seasonal_naive_forecasting(df: pl.DataFrame, ds_col: str, y_col: str, h: str = "5y", interval: str = "1mo", period: int = None, validation: bool = False):
    """
    Generates a seasonal naive baseline forecast by repeating the last historical seasonal cycle.
    
    Parameters:
        period (int): The exact number of rows in one complete cycle. 
                        (e.g., 7 for daily data with weekly patterns, 365 for daily data with yearly patterns).
                        If None, defaults to 4 (quarterly) or 12 (monthly) based on interval.
    """
    # 0. Deduce the seasonal period if not explicitly passed
    if period is None:
        period = 4 if interval in ["1q", "3mo"] else 12
    
    if validation:
        # 1. Validation Mode Split
        total_end_date = df.select(pl.col(ds_col).max()).item()
        clean_h = h.lstrip("-")
        train_end_date = pl.select(pl.lit(total_end_date).dt.offset_by(f"-{clean_h}")).item()
        
        train_df = df.filter(pl.col(ds_col) <= train_end_date)
        test_df = df.filter(pl.col(ds_col) > train_end_date)
        date_range = test_df.select(pl.col(ds_col)).to_series()
    else:
        # 2. Standard Production Mode
        train_df = df
        last_historical_date = train_df.select(pl.col(ds_col).max()).item()
        start_forecast = pl.select(pl.lit(last_historical_date).dt.offset_by(interval)).item()
        end_forecast = pl.select(pl.lit(last_historical_date).dt.offset_by(h)).item()
        date_range = pl.date_range(start_forecast, end_forecast, interval=interval, eager=True)
    
    # 3. ENHANCEMENT: Extract the last 'period' rows of history (the baseline seasonal cycle)
    hist_cycle = train_df.tail(period).with_row_index(name="cycle_idx")
    
    # 4. ENHANCEMENT: Map future dates by cleanly looping this cycle sequentially
    df_res = (
        pl.DataFrame({ds_col: date_range})
        .with_row_index(name="h", offset=1)
        .with_columns(
            ((pl.col("h") - 1) % period).alias("cycle_idx")
        )
        .join(hist_cycle.select(["cycle_idx", y_col]), on="cycle_idx", how="left")
        .drop("cycle_idx")
    )

    # 5. Computing Confidence Intervals
    sum_et2 = train_df.select((pl.col(y_col) - pl.col(y_col).shift(period)).pow(2)).sum().item()
    sigma1_hat = np.sqrt(sum_et2 / (train_df.height - period))

    df_res = df_res.with_columns(
        ((((pl.col("h") - 1) / period).floor() + 1).sqrt() * sigma1_hat).alias("sigmah_hat")
    ).with_columns(
        (pl.col(y_col) - 1.28 * pl.col("sigmah_hat")).alias("ci_lower_80"),
        (pl.col(y_col) + 1.28 * pl.col("sigmah_hat")).alias("ci_upper_80"),
        (pl.col(y_col) - 1.96 * pl.col("sigmah_hat")).alias("ci_lower_95"),
        (pl.col(y_col) + 1.96 * pl.col("sigmah_hat")).alias("ci_upper_95")
    ).drop(["h", "sigmah_hat"])

    # 6. Evaluate Metrics if in Validation Mode
    metrics = None
    if validation:
        df_res = df_res.join(
            test_df.select([ds_col, y_col]).rename({y_col: "actual"}),
            on=ds_col,
            how="left"
        )
        
        mae = df_res.select((pl.col("actual") - pl.col(y_col)).abs().mean()).item()
        rmse = df_res.select((pl.col("actual") - pl.col(y_col)).pow(2).mean().sqrt()).item()
        
        hist_mase_denom = train_df.select((pl.col(y_col) - pl.col(y_col).shift(period)).abs().mean()).item()
        hist_rmsse_denom = train_df.select((pl.col(y_col) - pl.col(y_col).shift(period)).pow(2).mean()).item()
        
        mase = mae / hist_mase_denom if hist_mase_denom != 0 else np.nan
        rmsse = rmse / np.sqrt(hist_rmsse_denom) if hist_rmsse_denom != 0 else np.nan
        
        metrics = {
            "scale_dependent": {
                "MAE": round(mae, 4),
                "RMSE": round(rmse, 4)
            },
            "scaled": {
                "MASE": round(mase, 4),
                "RMSSE": round(rmsse, 4)
            }
        }

    # 7. Build Visualization
    base_res = alt.Chart(df_res).encode(x=alt.X(f"{ds_col}:T"))
    
    # Multi-shaded Confidence Interval Bands
    ci_95 = base_res.mark_area(color="blue", opacity=0.15).encode(y="ci_lower_95:Q", y2="ci_upper_95:Q")
    ci_80 = base_res.mark_area(color="blue", opacity=0.30).encode(y="ci_lower_80:Q", y2="ci_upper_80:Q")
    
    # Lines
    fc_chart = base_res.mark_line(color="blue", strokeWidth=2).encode(y=alt.Y(f"{y_col}:Q"))
    old_chart = alt.Chart(train_df).mark_line(color="black").encode(x=alt.X(f"{ds_col}:T"), y=alt.Y(f"{y_col}:Q"))

    if validation:
        test_chart = alt.Chart(test_df).mark_line(color="gray", strokeDash=[2, 2]).encode(x=alt.X(f"{ds_col}:T"), y=alt.Y(f"{y_col}:Q"))
        chart = (ci_95 + ci_80 + old_chart + test_chart + fc_chart).interactive()
    else:
        chart = (ci_95 + ci_80 + old_chart + fc_chart).interactive()

    return {
        "forecast": df_res,
        "chart": chart,
        "metrics": metrics
    }

def drift_forecasting(df: pl.DataFrame, ds_col: str, y_col: str, h: str = "5y", interval: str = "1mo", validation: bool = False):
    """
    Generates a drift baseline forecast accompanied by 95% Confidence Intervals.
    """
    # 0. Automatically deduce the seasonal period for scaled metrics
    period = 4 if interval in ["1q", "3mo"] else 12
    
    if validation:
        # 1. Validation Mode Split
        total_end_date = df.select(pl.col(ds_col).max()).item()
        clean_h = h.lstrip("-")
        train_end_date = pl.select(pl.lit(total_end_date).dt.offset_by(f"-{clean_h}")).item()
        
        train_df = df.filter(pl.col(ds_col) <= train_end_date)
        test_df = df.filter(pl.col(ds_col) > train_end_date)
        date_range = test_df.select(pl.col(ds_col)).to_series()
    else:
        # 2. Standard Production Mode
        train_df = df
        last_historical_date = train_df.select(pl.col(ds_col).max()).item()
        start_forecast = pl.select(pl.lit(last_historical_date).dt.offset_by(interval)).item()
        end_forecast = pl.select(pl.lit(last_historical_date).dt.offset_by(h)).item()
        date_range = pl.date_range(start_forecast, end_forecast, interval=interval, eager=True)
    
    # 2. Extract Key Values for Points & Dimensions
    y_t = train_df.select(pl.col(y_col).tail(1)).item()
    y_1 = train_df.select(pl.col(y_col).head(1)).item()
    T = train_df.height
    slope = (y_t - y_1) / (T - 1)
    
    # 3. Compute Residual Variance (sigma1_hat)
    # Formula: e_t = y_t - (y_{t-1} + slope)
    df_residuals = train_df.select(
        (pl.col(y_col) - (pl.col(y_col).shift(1) + pl.lit(slope))).pow(2).alias("e2")
    )
    sum_et2 = df_residuals.select(pl.col("e2").sum()).item()
    sigma1_hat = np.sqrt(sum_et2 / (T - 2)) # Degrees of freedom drops by 2 (intercept & slope parameters)
    
    # 4. Generate Point Forecast along with Compounding Drift Error
    df_res = (
        pl.DataFrame({ds_col: date_range})
        .with_row_index("h", offset=1)
        .with_columns(
            # Standard Point Forecast
            (pl.lit(y_t) + pl.col("h") * pl.lit(slope)).alias(y_col),
            # Drift Error: sigma1 * sqrt( h * (1 + h/T) )
            (sigma1_hat * (pl.col("h") * (1 + pl.col("h") / T)).sqrt()).alias("sigmah_hat")
        )
        .with_columns(
            (pl.col(y_col) - 1.28 * pl.col("sigmah_hat")).alias("ci_lower_80"),
            (pl.col(y_col) + 1.28 * pl.col("sigmah_hat")).alias("ci_upper_80"),
            (pl.col(y_col) - 1.96 * pl.col("sigmah_hat")).alias("ci_lower_95"),
            (pl.col(y_col) + 1.96 * pl.col("sigmah_hat")).alias("ci_upper_95")
        )
        .drop(["h", "sigmah_hat"])
    )
    
    # 5. Evaluate Metrics if in Validation Mode
    metrics = None
    if validation:
        df_res = df_res.join(
            test_df.select([ds_col, y_col]).rename({y_col: "actual"}),
            on=ds_col,
            how="left"
        )
        
        # A. Scale-Dependent Errors (MAE, RMSE)
        mae = df_res.select((pl.col("actual") - pl.col(y_col)).abs().mean()).item()
        rmse = df_res.select((pl.col("actual") - pl.col(y_col)).pow(2).mean().sqrt()).item()
        
        # B. Scaled Errors (MASE, RMSSE) using Training Seasonal-Naive as the baseline
        # Polars .mean() automatically ignores the nulls introduced by the .shift()
        hist_mase_denom = train_df.select((pl.col(y_col) - pl.col(y_col).shift(period)).abs().mean()).item()
        hist_rmsse_denom = train_df.select((pl.col(y_col) - pl.col(y_col).shift(period)).pow(2).mean()).item()
        
        mase = mae / hist_mase_denom if hist_mase_denom != 0 else np.nan
        rmsse = rmse / np.sqrt(hist_rmsse_denom) if hist_rmsse_denom != 0 else np.nan
        
        metrics = {
            "scale_dependent": {
                "MAE": round(mae, 4),
                "RMSE": round(rmse, 4)
            },
            "scaled": {
                "MASE": round(mase, 4),
                "RMSSE": round(rmsse, 4)
            }
        }
    
    base_res = alt.Chart(df_res).encode(x=alt.X(f"{ds_col}:T"))
    
    # Multi-shaded Confidence Interval Bands
    ci_95 = base_res.mark_area(color="blue", opacity=0.15).encode(y="ci_lower_95:Q", y2="ci_upper_95:Q")
    ci_80 = base_res.mark_area(color="blue", opacity=0.30).encode(y="ci_lower_80:Q", y2="ci_upper_80:Q")
    
    # Lines
    fc_chart = base_res.mark_line(color="blue", strokeWidth=2).encode(y=alt.Y(f"{y_col}:Q"))
    old_chart = alt.Chart(train_df).mark_line(color="black").encode(x=alt.X(f"{ds_col}:T"), y=alt.Y(f"{y_col}:Q"))

    if validation:
        test_chart = alt.Chart(test_df).mark_line(color="gray", strokeDash=[2, 2]).encode(x=alt.X(f"{ds_col}:T"), y=alt.Y(f"{y_col}:Q"))
        chart = (ci_95 + ci_80 + old_chart + test_chart + fc_chart).interactive()
    else:
        chart = (ci_95 + ci_80 + old_chart + fc_chart).interactive()

    # Return everything cleanly as a structured dictionary map
    return {
        "forecast": df_res,
        "chart": chart,
        "metrics": metrics
    }

def classical_decomposition(df: pl.DataFrame, ds_col: str, y_col: str, period: int = 12, form: str = "additive"):
    """
    Performs classical additive or multiplicative decomposition on a Polars DataFrame.
    """
    trend_col_name = f"2x{period}-MA"
    
    # Define expressions based on form
    if form == "multiplicative":
        detrended_expr = pl.col(y_col) / pl.col(trend_col_name)
        reminder_expr = pl.col(y_col) / (pl.col(trend_col_name) * pl.col("s"))
    elif form == "additive":
        detrended_expr = pl.col(y_col) - pl.col(trend_col_name)
        reminder_expr = pl.col(y_col) - pl.col(trend_col_name) - pl.col("s")
    else:
        raise ValueError("Form must be either 'additive' or 'multiplicative'")

    # 1. Trend Estimation: 2xM Moving Average
    df_trend = df.with_columns(
        pl.col(y_col).rolling_mean(window_size=2, center=True).alias("2-MA")
    ).with_columns(
        pl.col("2-MA").rolling_mean(window_size=period, center=True).alias(trend_col_name)
    )
    
    # 2. Detrending & Extracting Seasonal Period (Handles Month vs Quarter dynamically)
    period_expr = pl.col(ds_col).dt.quarter() if period == 4 else pl.col(ds_col).dt.month()
    
    df_detrended = df_trend.with_columns(
        detrended_expr.alias("detrended"),
        period_expr.alias("season_idx")
    )
    
    # 3. Seasonal Component Estimation
    df_seasonal = (
        df_detrended.group_by("season_idx")
        .agg(pl.col("detrended").mean().alias("s_raw"))
    )
    
    s_offset = df_seasonal.select(pl.col("s_raw").mean()).item()
    if form == "multiplicative":
        # Multiplicative indices must average out to 1.0
        df_seasonal = df_seasonal.with_columns(
            (pl.col("s_raw") / s_offset).alias("s")
        )
    else:
        # Additive indices must average out to 0.0
        df_seasonal = df_seasonal.with_columns(
            (pl.col("s_raw") - s_offset).alias("s")
        )
    df_seasonal = df_seasonal.drop("s_raw")
    
    # 4. Recompose Data & Isolate Remainder (r)
    df_final = (
        df_detrended.join(df_seasonal, on="season_idx", how="left")
        .with_columns(
            reminder_expr.alias("r")
        )
    )
    
    # 5. Generate Visualizations
    whole = df_final.plot.line(ds_col, y_col)
    trend_plot = df_final.plot.line(ds_col, trend_col_name)
    seasonal_plot = df_final.plot.line(ds_col, "s")
    remainder_plot = df_final.plot.line(ds_col, "r")
    
    chart = (whole | trend_plot) & (seasonal_plot | remainder_plot)
    
    return chart, df_final

def stl_decomposition(df: pl.DataFrame, ds_col: str, y_col: str, period: int = 12, seasonal: int = 13, trend: int = 21, robust=True):
    stl = STL(
        df.select(y_col).to_numpy(),
        period=period,
        seasonal=seasonal,
        trend=trend,
        robust=robust
    )
    res_stl = stl.fit()

    dcmp = df.with_columns(
        pl.lit(res_stl.trend).alias("trend"),
        pl.lit(res_stl.seasonal).alias("seasonal"),
        pl.lit(res_stl.resid).alias("remainder")
    )


    whole = dcmp.plot.line(ds_col, y_col)
    trend_plot = dcmp.plot.line(ds_col, "trend")
    seasonal_plot = dcmp.plot.line(ds_col, "seasonal")
    remainder_plot = dcmp.plot.line(ds_col, "remainder")


    chart = whole & trend_plot & seasonal_plot & remainder_plot

    return df, chart

def ANN_exp_smoothing(df: pl.DataFrame, ds_col: str, y_col: str, Alpha: float = None, l0: float = None, h: str = "5y", interval: str = "1q", validation: bool = False):
    """
    Generates a Simple Exponential Smoothing (SES) forecast with 80% and 95% confidence intervals.
    
    Returns:
        dict: {"forecast": pl.DataFrame, "chart": alt.LayerChart, "metrics": dict/None}
    """
    # 0. Automatically deduce the seasonal period for scaled metrics
    period = 4 if interval in ["1q", "3mo"] else 12
    
    if validation:
        # 1. Validation Mode Split
        total_end_date = df.select(pl.col(ds_col).max()).item()
        
        # SAFEGUARD: Extract the raw string if 'h' was passed as a list or series
        if isinstance(h, (list, tuple)):
            h = h[0]
        elif hasattr(h, "to_list"): # handles Polars Series/numpy arrays
            h = h.to_list()[0]
            
        # Clean up the string syntax completely
        clean_h = str(h).lstrip("-")
        
        # Now this will cleanly evaluate to a raw string like "-5y"
        train_end_date = pl.select(pl.lit(total_end_date).dt.offset_by(f"-{clean_h}")).item()
        
        train_df = df.filter(pl.col(ds_col) <= train_end_date)
        test_df = df.filter(pl.col(ds_col) > train_end_date)
        date_range = test_df.select(pl.col(ds_col)).to_series()
    else:
        # 2. Standard Production Mode
        train_df = df
        last_historical_date = train_df.select(pl.col(ds_col).max()).item()
        start_forecast = pl.select(pl.lit(last_historical_date).dt.offset_by(interval)).item()
        end_forecast = pl.select(pl.lit(last_historical_date).dt.offset_by(h)).item()
        date_range = pl.date_range(start_forecast, end_forecast, interval=interval, eager=True)

    # Extract historical training values as a raw array for the optimizer
    train_y = train_df[y_col].to_numpy()

    # 2. NUMERICAL OPTIMIZATION (Runs only if parameters aren't provided)
    if Alpha is None or l0 is None:
        # Define the RSS objective function
        def rss_objective(params):
            a, l = params
            # Mirror the exact Polars EWM logic inside the optimization loop
            y_extended = pl.Series([l] + list(train_y))
            fitted = y_extended.ewm_mean(alpha=a, adjust=False).slice(0, len(train_y)).to_numpy()
            return np.sum((train_y - fitted) ** 2)

        # Smart initial guesses: alpha = 0.2, l0 = first historical value
        initial_guess = [0.2 if Alpha is None else Alpha, train_y[0] if l0 is None else l0]
        
        # Enforce bounds: Alpha must be between 0 and 1. l0 can be anything.
        bounds = [(1e-4, 1.0) if Alpha is None else (Alpha, Alpha), 
                    (None, None) if l0 is None else (l0, l0)]

        # Run the optimizer
        res = minimize(rss_objective, x0=initial_guess, bounds=bounds, method="L-BFGS-B")
        
        # Extract the optimal values
        Alpha, l0 = res.x[0], res.x[1]
    # 3. Core SES Math via Polars Series
    # Prepend l0 to history to seed the recursive level tracking cleanly
    y_extended = pl.Series("y", [l0] + train_df[y_col].to_list())
    ewm_series = y_extended.ewm_mean(alpha=Alpha, adjust=False)
    
    # Slice ewm values to isolate historical fitted states vs the final future forecast level
    fitted_vals = ewm_series.slice(0, train_df.height)
    final_level = ewm_series[train_df.height]
    
    # Add fitted column to history to track historical residuals
    train_df_fit = train_df.with_columns(pl.lit(fitted_vals).alias("fitted"))
    sum_et2 = train_df_fit.select((pl.col(y_col) - pl.col("fitted")).pow(2)).sum().item()
    sigma1_hat = np.sqrt(sum_et2 / (train_df.height - 1))

    # 4. Construct Future Forecast Frame with Compounding SES Uncertainty
    # Formula: sigma_h = sigma_1 * sqrt(1 + alpha^2 * (h - 1))
    df_res = (
        pl.DataFrame({ds_col: date_range})
        .with_row_index("h_step", offset=1)
        .with_columns(
            pl.lit(final_level).alias(y_col),
            (pl.lit(sigma1_hat) * (1 + (Alpha ** 2) * (pl.col("h_step") - 1)).sqrt()).alias("sigmah")
        )
        .with_columns(
            (pl.col(y_col) - 1.28 * pl.col("sigmah")).alias("ci_lower_80"),
            (pl.col(y_col) + 1.28 * pl.col("sigmah")).alias("ci_upper_80"),
            (pl.col(y_col) - 1.96 * pl.col("sigmah")).alias("ci_lower_95"),
            (pl.col(y_col) + 1.96 * pl.col("sigmah")).alias("ci_upper_95")
        )
        .drop(["h_step", "sigmah"])
    )

    # 5. Evaluate Metrics if in Validation Mode
    metrics = None
    if validation:
        df_res = df_res.join(
            test_df.select([ds_col, y_col]).rename({y_col: "actual"}),
            on=ds_col,
            how="left"
        )
        
        mae = df_res.select((pl.col("actual") - pl.col(y_col)).abs().mean()).item()
        rmse = df_res.select((pl.col("actual") - pl.col(y_col)).pow(2).mean().sqrt()).item()
        
        hist_mase_denom = train_df.select((pl.col(y_col) - pl.col(y_col).shift(period)).abs().mean()).item()
        hist_rmsse_denom = train_df.select((pl.col(y_col) - pl.col(y_col).shift(period)).pow(2).mean()).item()
        
        mase = mae / hist_mase_denom if hist_mase_denom != 0 else np.nan
        rmsse = rmse / np.sqrt(hist_rmsse_denom) if hist_rmsse_denom != 0 else np.nan
        
        metrics = {
            "scale_dependent": {"MAE": round(mae, 4), "RMSE": round(rmse, 4)},
            "scaled": {"MASE": round(mase, 4), "RMSSE": round(rmsse, 4)}
        }

    # 6. Build Graph Layers 
    base_res = alt.Chart(df_res).encode(x=alt.X(f"{ds_col}:T"))
    
    # Multi-shaded Confidence Interval Bands
    ci_95 = base_res.mark_area(color="blue", opacity=0.15).encode(y="ci_lower_95:Q", y2="ci_upper_95:Q")
    ci_80 = base_res.mark_area(color="blue", opacity=0.30).encode(y="ci_lower_80:Q", y2="ci_upper_80:Q")
    
    # Lines
    fc_chart = base_res.mark_line(color="blue", strokeWidth=2).encode(y=alt.Y(f"{y_col}:Q"))
    old_chart = alt.Chart(train_df_fit).mark_line(color="black").encode(x=alt.X(f"{ds_col}:T"), y=alt.Y(f"{y_col}:Q"))
    fitted_chart = alt.Chart(train_df_fit).mark_line(color="orange").encode(x=alt.X(f"{ds_col}:T"), y=alt.Y("fitted:Q"))

    if validation:
        test_chart = alt.Chart(test_df).mark_line(color="gray", strokeDash=[2, 2]).encode(x=alt.X(f"{ds_col}:T"), y=alt.Y(f"{y_col}:Q"))
        chart = (ci_95 + ci_80 + old_chart + fitted_chart + test_chart + fc_chart).interactive()
    else:
        chart = (ci_95 + ci_80 + old_chart + fitted_chart + fc_chart).interactive()

    return {
        "forecast": df_res,
        "chart": chart, 
        "metrics": metrics,
        "Alpha": Alpha,
        "l0": l0
        }

def exp_smoothing(df: pl.DataFrame, ds_col: str, y_col: str, h: str = "5y", interval: str = "1q", validation: bool = False, model: str = "ANN", damp=None):
    """
    Generalized Exponential Smoothing using Nixtla's statsforecast library.
    Assumes ds_col has already been parsed into Date/Datetime format.
    
    Returns:
        dict: {"forecast": pl.DataFrame, "chart": alt.LayerChart, "metrics": dict/None}
    """
    # 0. Automatically deduce the seasonal period and string frequencies
    period = 4 if interval in ["1q", "3mo"] else (12 if "m" in interval.lower() else 1)
    sf_freq = 'Y' if 'y' in interval.lower() else ('Q' if interval in ["1q", "3mo"] else 'M')

    # 1. Timeline Split Logic (Purely Temporal)
    if validation:
        total_end_date = df.select(pl.col(ds_col).max()).item()
        if isinstance(h, (list, tuple)): h = h[0]
        elif hasattr(h, "to_list"): h = h.to_list()[0]
        
        clean_h = str(h).lstrip("-")
        train_end_date = pl.select(pl.lit(total_end_date).dt.offset_by(f"-{clean_h}")).item()
        
        train_df = df.filter(pl.col(ds_col) <= train_end_date)
        test_df = df.filter(pl.col(ds_col) > train_end_date)
        date_range = test_df.select(pl.col(ds_col)).to_series()
    else:
        train_df = df
        last_historical_date = train_df.select(pl.col(ds_col).max()).item()
        date_range = pl.date_range(
            pl.select(pl.lit(last_historical_date).dt.offset_by(interval)).item(),
            pl.select(pl.lit(last_historical_date).dt.offset_by(h)).item(),
            interval=interval, eager=True
        )

    # 2. Format Training Frame for statsforecast (requires unique_id, ds, y)
    h_steps = len(date_range)
    df_sf = train_df.select([
        pl.lit("ts_1").alias("unique_id"),
        pl.col(ds_col).alias("ds"),
        pl.col(y_col).alias("y")
    ]).to_pandas()

    # 3. Model & Predict in a single step to unlock historical fits
    model_obj = AutoETS(season_length=period, model=model, damped=damp)
    sf = StatsForecast(models=[model_obj], freq=sf_freq, n_jobs=1)
    
    # FIX: Use .forecast directly with fitted=True and levels passed inside
    fc_pd = sf.forecast(df=df_sf, h=h_steps, level=[80, 95], fitted=True)
    fitted_pd = sf.forecast_fitted_values()

    # Safeguard: Reset index so 'unique_id' or 'ds' don't get trapped as Pandas indexes
    fc_pd = fc_pd.reset_index()
    fitted_pd = fitted_pd.reset_index()

    # 4. Convert Results Back to Clean Polars Frames
    fc_pl = pl.from_pandas(fc_pd)
    fitted_pl = pl.from_pandas(fitted_pd)

    df_res = pl.DataFrame({ds_col: date_range}).with_columns([
        fc_pl["AutoETS"].alias(y_col),
        fc_pl["AutoETS-lo-80"].alias("ci_lower_80"),
        fc_pl["AutoETS-hi-80"].alias("ci_upper_80"),
        fc_pl["AutoETS-lo-95"].alias("ci_lower_95"),
        fc_pl["AutoETS-hi-95"].alias("ci_upper_95")
    ])

    train_df_fit = train_df.with_columns(fitted_pl["AutoETS"].alias("fitted"))

    # 5. Evaluate Metrics if in Validation Mode
    metrics = None
    if validation:
        df_res = df_res.join(test_df.select([ds_col, y_col]).rename({y_col: "actual"}), on=ds_col, how="left")
        
        mae = df_res.select((pl.col("actual") - pl.col(y_col)).abs().mean()).item()
        rmse = df_res.select((pl.col("actual") - pl.col(y_col)).pow(2).mean().sqrt()).item()
        
        hist_mase_denom = train_df.select((pl.col(y_col) - pl.col(y_col).shift(period)).abs().mean()).item()
        hist_rmsse_denom = train_df.select((pl.col(y_col) - pl.col(y_col).shift(period)).pow(2).mean()).item()
        
        mase = mae / hist_mase_denom if hist_mase_denom != 0 else np.nan
        rmsse = rmse / np.sqrt(hist_rmsse_denom) if hist_rmsse_denom != 0 else np.nan
        
        metrics = {
            "scale_dependent": {"MAE": round(mae, 4), "RMSE": round(rmse, 4)},
            "scaled": {"MASE": round(mase, 4), "RMSSE": round(rmsse, 4)}
        }

    # 6. Build Graphical Layers (Hardcoded to Temporal ':T')
    base_res = alt.Chart(df_res).encode(x=alt.X(f"{ds_col}:T"))
    
    ci_95 = base_res.mark_area(color="blue", opacity=0.15).encode(y="ci_lower_95:Q", y2="ci_upper_95:Q")
    ci_80 = base_res.mark_area(color="blue", opacity=0.30).encode(y="ci_lower_80:Q", y2="ci_upper_80:Q")
    
    fc_chart = base_res.mark_line(color="blue", strokeWidth=2).encode(y=alt.Y(f"{y_col}:Q"))
    old_chart = alt.Chart(train_df_fit).mark_line(color="black").encode(x=alt.X(f"{ds_col}:T"), y=alt.Y(f"{y_col}:Q"))
    fitted_chart = alt.Chart(train_df_fit).mark_line(color="orange").encode(x=alt.X(f"{ds_col}:T"), y=alt.Y("fitted:Q"))

    if validation:
        test_chart = alt.Chart(test_df).mark_line(color="gray", strokeDash=[2, 2]).encode(x=alt.X(f"{ds_col}:T"), y=alt.Y(f"{y_col}:Q"))
        chart = (ci_95 + ci_80 + old_chart + fitted_chart + test_chart + fc_chart).interactive()
    else:
        chart = (ci_95 + ci_80 + old_chart + fitted_chart + fc_chart).interactive()

    return {"forecast": df_res, "chart": chart, "metrics": metrics}

def Arima_forecast_helper(
    df: pl.DataFrame, 
    ds_col: str, 
    y_col: str, 
    sf: StatsForecast, 
    h: str = "5y", 
    interval: str = "1y", 
    validation: bool = False,
    future_x_df: pl.DataFrame = None
):
    """
    Generates a forecast from a Nixtla StatsForecast engine.
    Extracts out-of-sample data points alongside 80% and 95% intervals.
    """
    # Automatically deduce the seasonal period for metrics
    period = 4 if interval in ["1q", "3mo"] else (12 if "m" in interval.lower() else 1)

    # Ensure unique_id exists (StatsForecast requirement)
    has_orig_id = "unique_id" in df.columns
    if not has_orig_id:
        df = df.with_columns(pl.lit("1").alias("unique_id"))

    # Clear Session Pollution by re-instantiating
    clean_sf = StatsForecast(models=sf.models, freq=sf.freq, n_jobs=getattr(sf, 'n_jobs', -1))

    # Determine Horizon Steps
    if validation:
        total_end_date = df.select(pl.col(ds_col).max()).item()
        clean_h = str(h).lstrip("-")
        train_end_date = pl.select(pl.lit(total_end_date).dt.offset_by(f"-{clean_h}")).item()
        
        train_df = df.filter(pl.col(ds_col) <= train_end_date)
        test_df = df.filter(pl.col(ds_col) > train_end_date)
        h_steps = test_df.select(pl.col(ds_col).n_unique()).item()
    else:
        train_df = df
        last_historical_date = train_df.select(pl.col(ds_col).max()).item()
        date_range = pl.date_range(
            pl.select(pl.lit(last_historical_date).dt.offset_by(interval)).item(),
            pl.select(pl.lit(last_historical_date).dt.offset_by(h)).item(),
            interval=interval, eager=True
        )
        h_steps = len(date_range)

    # Execute Forecast & Cache In-Sample Fitted Values natively
    if future_x_df: X_df = future_x_df.to_pandas()
    else: X_df = None
    fc_pd = clean_sf.forecast(
        df=train_df.rename({ds_col: "ds"}).to_pandas(),
        h=h_steps,
        level=[80, 95],
        fitted=True,
        target_col=y_col,
        X_df=X_df
    ).reset_index()

    fitted_pd = clean_sf.forecast_fitted_values().reset_index()

    fc_pl = pl.from_pandas(fc_pd)
    fitted_pl = pl.from_pandas(fitted_pd)

    if ds_col != "ds":
        fc_pl = fc_pl.rename({"ds": ds_col})
        fitted_pl = fitted_pl.rename({"ds": ds_col})

    model_name = sf.models[0].alias

    # Build Future Results Table
    df_res = fc_pl.select([
        pl.col(ds_col),
        pl.col(model_name).alias(y_col),
        pl.col(f"{model_name}-lo-80").alias("ci_lower_80"),
        pl.col(f"{model_name}-hi-80").alias("ci_upper_80"),
        pl.col(f"{model_name}-lo-95").alias("ci_lower_95"),
        pl.col(f"{model_name}-hi-95").alias("ci_upper_95")
    ])

    y_type = df_res.select(y_col).dtypes[0]
    
    # Apply Visual Anchor
    last_hist_row = train_df.tail(1)
    last_date = last_hist_row.select(ds_col).item()
    last_val = last_hist_row.select(y_col).item()

    anchor_df = pl.DataFrame({
        ds_col: [last_date],
        y_col: [last_val],
        "ci_lower_80": [last_val],
        "ci_upper_80": [last_val],
        "ci_lower_95": [last_val],
        "ci_upper_95": [last_val]
    }).with_columns(
        pl.col(y_col).cast(y_type),
        pl.col("ci_lower_80").cast(y_type), 
        pl.col("ci_lower_95").cast(y_type), 
        pl.col("ci_upper_80").cast(y_type),
        pl.col("ci_upper_95").cast(y_type)
    )

    df_res = df_res.with_columns(pl.col(ds_col).cast(pl.Date))
    fitted_pl = fitted_pl.with_columns(pl.col(ds_col).cast(pl.Date))

    df_res = pl.concat([anchor_df, df_res])

    # Compile Historical Fitted Values
    train_df_fit = train_df.join(
        fitted_pl.select(["unique_id", ds_col, model_name]).rename({model_name: "fitted"}),
        on=["unique_id", ds_col],
        how="left"
    )

    # Evaluate Metrics if in Validation Mode
    metrics = None
    if validation:
        df_res_eval = df_res.join(
            test_df.select([ds_col, y_col]).rename({y_col: "actual"}),
            on=ds_col,
            how="left"
        )

        mae = df_res_eval.select((pl.col("actual") - pl.col(y_col)).abs().mean()).item()
        rmse = df_res_eval.select((pl.col("actual") - pl.col(y_col)).pow(2).mean().sqrt()).item()

        hist_mase_denom = train_df.select((pl.col(y_col) - pl.col(y_col).shift(period)).abs().mean()).item()
        hist_rmsse_denom = train_df.select((pl.col(y_col) - pl.col(y_col).shift(period)).pow(2).mean()).item()

        mase = mae / hist_mase_denom if (hist_mase_denom and hist_mase_denom != 0) else np.nan
        rmsse = rmse / np.sqrt(hist_rmsse_denom) if (hist_rmsse_denom and hist_rmsse_denom != 0) else np.nan

        metrics = {
            "scale_dependent": {"MAE": round(mae, 4) if mae else None, "RMSE": round(rmse, 4) if rmse else None},
            "scaled": {"MASE": round(mase, 4) if not np.isnan(mase) else None, "RMSSE": round(rmsse, 4) if not np.isnan(rmsse) else None}
        }

    # Clean up artificial unique_id if we generated it
    if not has_orig_id:
        df_res = df_res.drop("unique_id", strict=False)
        train_df_fit = train_df_fit.drop("unique_id", strict=False)

    # Graphical Layers
    base_res = alt.Chart(df_res).encode(x=alt.X(f"{ds_col}:T"))
    ci_95_band = base_res.mark_area(color="blue", opacity=0.15).encode(y="ci_lower_95:Q", y2="ci_upper_95:Q")
    ci_80_band = base_res.mark_area(color="blue", opacity=0.30).encode(y="ci_lower_80:Q", y2="ci_upper_80:Q")
    fc_chart = base_res.mark_line(color="blue", strokeWidth=2).encode(y=alt.Y(f"{y_col}:Q"))
    old_chart = alt.Chart(train_df_fit).mark_line(color="black").encode(x=alt.X(f"{ds_col}:T"), y=alt.Y(f"{y_col}:Q"))

    if validation:
        test_chart = alt.Chart(test_df).mark_line(color="gray", strokeDash=[2, 2]).encode(x=alt.X(f"{ds_col}:T"), y=alt.Y(f"{y_col}:Q"))
        chart = (ci_95_band + ci_80_band + old_chart + test_chart + fc_chart).interactive()
    else:
        chart = (ci_95_band + ci_80_band + old_chart + fc_chart).interactive()

    return {"forecast": df_res, "chart": chart, "metrics": metrics}

def Arima_forecasting(
    df: pl.DataFrame, 
    ds_col: str, 
    y_col: str,
    h: str = "5y", 
    interval: str = "1q", 
    validation: bool = False, 
    seas_len: int = 0,
    manual_candidates: list[dict] = None, # Programmatic list of candidate orders, e.g. [{"order": (1,1,1), "seasonal_order": (0,1,1)}]
    selection_metric: str = "aicc",      # "aic", "aicc", "bic"
    show_plots: bool = True,
    use_cv: bool = True,
    cv_windows: int = 3,
    future_x_df: pl.DataFrame = None
):
    """
    Automated ARIMA selection, diagnostic, and forecasting pipeline.
    """
    # Standardize/ensure unique_id is present
    has_orig_id = "unique_id" in df.columns
    if not has_orig_id:
        df = df.with_columns(pl.lit("1").alias("unique_id"))

    # 1. Stations/Differencing analysis
    df = df.with_columns(
        pl.col(y_col).diff().alias("diff1"),
        pl.col(y_col).diff().diff().alias("diff2")
    )
    
    # Run KPSS test on level, 1st, and 2nd differences[cite: 1]
    kpss_results = {}
    for col_name, d_val in [(y_col, 0), ("diff1", 1), ("diff2", 2)]:
        series_clean = df.select(col_name).drop_nulls().to_numpy().flatten()
        try:
            stat, p_value, _, _ = kpss(series_clean, regression="c", nlags="auto")
            kpss_results[d_val] = p_value
        except Exception:
            kpss_results[d_val] = 0.0  # Fallback to force differencing if test errors

    # Auto-select d: Choose the lowest d where we fail to reject stationarity (p >= 0.05)[cite: 1]
    auto_d = 0
    if kpss_results.get(0, 0) < 0.05:
        if kpss_results.get(1, 0) >= 0.05:
            auto_d = 1
        else:
            auto_d = 2

    print(f"-> Automated KPSS checks selected differencing degree: d={auto_d} (Level p={kpss_results.get(0,0):.4f}, Diff1 p={kpss_results.get(1,0):.4f})")

    # 2. ACF / PACF generation for diagnostics[cite: 1]
    chosen_diff_col = y_col if auto_d == 0 else (f"diff{auto_d}")
    diff_data = df.select(chosen_diff_col).drop_nulls().to_numpy().flatten()

    if show_plots:
        fig, axes = plt.subplots(1, 2, figsize=(15, 4))
        plot_acf(diff_data, ax=axes[0], lags=20, title=f"ACF (d={auto_d})")
        plot_pacf(diff_data, ax=axes[1], lags=20, method="ywm", title=f"PACF (d={auto_d})")
        plt.tight_layout()
        plt.show()

    # 3. Model construction
    models = []
    
    # Translate candidate input lists into ARIMA models
    if manual_candidates:
        for i, cand in enumerate(manual_candidates):
            order = cand.get("order", (1, auto_d, 1))
            s_order = cand.get("seasonal_order", None)
            alias_name = f"arima_{order[0]}_{order[1]}_{order[2]}"
            models.append(
                ARIMA(order=order, seasonal_order=s_order, season_length=seas_len, alias=alias_name)
            )
    else:
        # Defaults if none provided
        models.append(ARIMA(order=(1, auto_d, 1), alias=f"arima_1_{auto_d}_1"))
        models.append(ARIMA(order=(0, auto_d, 2), alias=f"arima_0_{auto_d}_2"))

    # Always include AutoARIMA for benchmarking
    models.append(AutoARIMA(stepwise=False, approximation=False, alias="auto_arima", season_length=seas_len))

    # Fit models & rank using the information criteria[cite: 1]
    df_fit = df.select(["unique_id", ds_col, y_col]).rename({y_col: "y", ds_col: "ds"})
    inferred_frequency = pd.infer_freq(df_fit.to_pandas()["ds"])
    if inferred_frequency is None:
        freq_map = {"1d": "D", "1w": "W", "1mo": "MS", "1q": "QS", "1y": "YS"}
        inferred_frequency = freq_map.get(interval)
    sf = StatsForecast(models=models, freq=inferred_frequency, n_jobs=-1)
    sf.fit(df_fit.to_pandas())

    # 4. Fit the engine on historical data FIRST so we can extract residuals
    # If using CV, run it and map the MSE errors to the model aliases
    cv_errors = {}
    if use_cv:
        print(f"-> Running {cv_windows}-fold Time Series Cross-Validation...")
        cv_df = sf.cross_validation(df=df_fit.to_pandas(), h=cv_windows, n_windows=cv_windows, step_size=1)
        
        for m in models:
            # Mean Squared Error: mean( (actual - predicted)^2 )
            mse = ((cv_df["y"] - cv_df[m.alias]) ** 2).mean()
            cv_errors[m.alias] = mse

    # 5. Evaluate Information Criteria AND Residual Health for ALL candidates
    candidate_pool = []
    
    for model_obj in sf.fitted_[0]:
        residuals = model_obj.model_.get("residuals")
        res_df = pd.DataFrame(residuals, columns=["Residuals"])
        
        # Calculate Ljung-Box test for this specific model
        p, d_val, q = getattr(model_obj, "order", (0, 0, 0))
        df_model = p + q 
        test_lags = [max(df_model + 1, min(24, len(res_df) // 5))]
        
        lb_test = acorr_ljungbox(res_df["Residuals"], return_df=True, lags=test_lags, model_df=df_model)
        lb_p = lb_test["lb_pvalue"].iloc[0]
        jb_stat, jb_p = sc_stats.jarque_bera(res_df["Residuals"])
        
        is_white_noise = lb_p > 0.05

        candidate_pool.append({
            "model_obj": model_obj,
            "alias": model_obj.alias,
            "aic": model_obj.model_.get("aic", float('inf')),
            "aicc": model_obj.model_.get("aicc", float('inf')),
            "bic": model_obj.model_.get("bic", float('inf')),
            "cv_mse": cv_errors.get(model_obj.alias, float('inf')), # Safely defaults to infinity if not using CV
            "is_white_noise": is_white_noise,
            "lb_p": lb_p,
            "jb_p": jb_p,
            "residuals": res_df
        })

    # --- Unified Backtracking / Selection Strategy ---
    # Determine which metric we are sorting by
    sort_key = "cv_mse" if use_cv else selection_metric.lower()
    
    # Filter to models that passed the white noise check
    healthy_models = [m for m in candidate_pool if m["is_white_noise"]]
    
    if healthy_models:
        # Best model among those that are actually white noise
        healthy_models_sorted = sorted(healthy_models, key=lambda x: x[sort_key])
        best_summary = healthy_models_sorted[0]
        backtrack_warning = False
        print(f"-> Selected best valid model (passed white noise) ranked by {sort_key.upper()}: {best_summary['alias']}")
    else:
        # Fallback: All models failed white noise. Pick the best metric but flag a warning
        all_models_sorted = sorted(candidate_pool, key=lambda x: x[sort_key])
        best_summary = all_models_sorted[0]
        backtrack_warning = True
        print(f"-> WARNING: All models failed White Noise check. Backing up to best available {sort_key.upper()}: {best_summary['alias']}")

    best_model = best_summary["model_obj"]
    res_df = best_summary["residuals"]

    # 5. Plot Winner's Residual Diagnostics
    if show_plots:
        fig, axs = plt.subplots(nrows=2, ncols=2, figsize=(14, 10))
        res_df.plot(ax=axs[0,0], legend=False)
        axs[0,0].set_title(f"Residuals ({best_summary['alias']})")
        axs[0,0].grid(True, alpha=0.3)
        sns.histplot(data=res_df, x="Residuals", kde=True, ax=axs[0,1])
        axs[0,1].set_title("Density & Histogram")
        sc_stats.probplot(res_df["Residuals"], dist="norm", plot=axs[1,0])
        axs[1,0].set_title('Normal Q-Q Plot')
        plot_acf(res_df["Residuals"], lags=min(35, len(res_df) - 1), ax=axs[1,1], color="fuchsia")
        axs[1,1].set_title("Autocorrelation")
        plt.tight_layout()
        plt.show()

    residual_report = {
        "is_white_noise": best_summary["is_white_noise"],
        "ljung_box_p": best_summary["lb_p"],
        "is_normal": best_summary["jb_p"] > 0.05,
        "jarque_bera_p": best_summary["jb_p"],
        "backtrack_warning_unreliable_intervals": backtrack_warning
    }

    # 6. Final Forecast
    forecast_sf = StatsForecast(models=[best_model], freq=inferred_frequency, n_jobs=-1)
    df_clean = df.select(["unique_id", ds_col, y_col])
    if not has_orig_id:
        df_clean = df_clean.drop("unique_id", strict=False)

    helper_output = Arima_forecast_helper(
        df=df_clean, ds_col=ds_col, y_col=y_col, sf=forecast_sf, h=h, interval=interval, validation=validation, future_x_df=future_x_df
    )

    helper_output["residual_diagnostics"] = residual_report
    return helper_output


