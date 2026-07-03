import polars as pl
import numpy as np
import scipy.stats as stats
import math
import statsmodels.formula.api as sm
from scipy.interpolate import make_smoothing_spline
from sklearn.preprocessing import StandardScaler
from itertools import combinations


pl.Config.set_tbl_cols(-1)
pl.Config.set_tbl_rows(-1)
np.set_printoptions(legacy="1.25")


# ── helpers ───────────────────────────────────────────────────────────────────

def _split(predictors, quantitative, qualitative):
    """Split a flat predictor list back into (quantitative, qualitative) sublists."""
    return (
        [p for p in predictors if p in quantitative],
        [p for p in predictors if p in qualitative],
    )


def _fit_curve(df, predictor, y_col, beta_hat, basis_fn, n_pts=300):
    """
    Build an hvplot scatter+curve overlay for any single-predictor model.
    basis_fn(x_range) must return the design matrix (n_pts, p) excluding intercept.
    """
    x_min = df.select(predictor).min().item()
    x_max = df.select(predictor).max().item()
    x_range = np.linspace(x_min, x_max, n_pts)
    X_curve = np.c_[np.ones(n_pts), basis_fn(x_range)]
    y_curve = (X_curve @ beta_hat).flatten()
    curve_df = pl.DataFrame({predictor: x_range, y_col: y_curve})
    return df.plot.point(x=predictor, y=y_col) + curve_df.plot.line(x=predictor, y=y_col).mark_line(color="red")


# ── simple linear regression ──────────────────────────────────────────────────

def regression(df, predictor, y_col, qualitative=False):
    """
    Simple linear regression of y_col on a single predictor.

    For qualitative predictors:
        - Binary  → 0/1 encoded in-place.
        - Multi-category → one-hot encoded; regression uses the second category
        dummy (first = reference). For full multi-category modelling use
        multiple_regression instead.

    Returns
    -------
    dict:
        "summary" : pl.DataFrame  — b0, b1, SE, 95% CI, H0 decision, RSS, σ²
        "b0"      : float
        "b1"      : float
        "chart"   : hvplot overlay — scatter + fitted line
    """
    if qualitative:
        categories = df.select(predictor).unique().to_series().to_list()
        if len(categories) == 2:
            ref = categories[0]
            df = df.with_columns((pl.col(predictor) != ref).cast(pl.Int8).alias(predictor))
        else:
            dummies = df.select(pl.col(predictor)).to_dummies()
            df = df.hstack(dummies).drop(f"{predictor}_{categories[0]}")
            predictor = f"{predictor}_{categories[1]}"

    x_mean = df.select(predictor).mean().item()
    y_mean = df.select(y_col).mean().item()
    n      = df.height

    b1 = (
        df.with_columns(((pl.col(predictor) - x_mean) * (pl.col(y_col) - y_mean)).alias("_cov"))
        .select("_cov").sum().item()
        / (n * df.select(predictor).var(0).item())
    )
    b0 = y_mean - b1 * x_mean

    rss        = df.with_columns((pl.col(y_col) - (b0 + b1 * pl.col(predictor))).alias("_e")).select((pl.col("_e") ** 2).sum()).item()
    sigma2_hat = rss / (n - 2)
    se_b1      = (sigma2_hat / (n * df.select(predictor).var(0).item())) ** 0.5
    t_value    = stats.t.ppf(0.975, n - 2)
    decision   = abs(b1 / se_b1) < t_value  # True → fail to reject H0: b1 = 0

    summary = pl.DataFrame({
        "b0": b0, "b1": b1, "se_b1": se_b1,
        "ci": f"[{b1 - t_value * se_b1:.4f}, {b1 + t_value * se_b1:.4f}]",
        "decision": decision, "RSS": rss, "sigma2_hat": sigma2_hat,
    })

    x_max   = math.ceil(df.select(predictor).max().item())
    line_df = pl.DataFrame({predictor: range(0, x_max)}).with_columns((b0 + b1 * pl.col(predictor)).alias(y_col))
    chart   = df.plot.point(x=predictor, y=y_col) + line_df.plot.line(x=predictor, y=y_col).mark_line(color="red")

    return {"summary": summary, "b0": b0, "b1": b1, "chart": chart}


# ── multiple / polynomial regression (OLS) ───────────────────────────────────

def multiple_regression(df, quantitative, qualitative, y_col, interaction=False, polynomial_degree=1):
    """
    OLS multiple regression with optional interactions and polynomial terms.

    Feature engineering order: dummies → interaction product → polynomial powers.
    All engineered columns live on a local copy of df; the caller's df is unchanged.

    Parameters
    ----------
    quantitative    : list[str]  — numeric predictor columns
    qualitative     : list[str]  — categorical columns (one-hot, first cat = reference)
    interaction     : bool       — appends predictor[0] × predictor[1] as "Product"
    polynomial_degree : int      — adds x², …, x^d for every quantitative predictor

    Returns
    -------
    dict:
        "summary"     : pl.DataFrame — coefficients, SE, t-stat, CI, H0 decision
        "RSS"         : float
        "sigma2_hat"  : float
        "beta_hat"    : np.ndarray  shape (p+1, 1)
        "y_hat"       : np.ndarray  shape (n, 1)
        "predictors"  : list[str]   — final predictor names after feature engineering
        "quantitative": list[str]
        "qualitative" : list[str]
        "chart"       : hvplot overlay or None — only for single-quant polynomial fits
    """
    predictors = quantitative.copy()
    p0         = len(predictors)

    for col in qualitative:
        dummies    = df.select(pl.col(col)).to_dummies()
        dummy_cols = dummies.columns[1:]   # first category dropped as reference
        df         = df.hstack(dummies)
        predictors.extend(dummy_cols)

    if interaction:
        df         = df.with_columns((pl.col(predictors[0]) * pl.col(predictors[1])).alias("Product"))
        predictors = predictors + ["Product"]

    if polynomial_degree > 1:
        for i in range(p0):
            base = predictors[i]
            for d in range(2, polynomial_degree + 1):
                new_col = f"{base}^{d}"
                df      = df.with_columns((pl.col(base) ** d).alias(new_col))
                predictors.append(new_col)

    n       = df.height
    p       = len(predictors)
    Y       = df.select(y_col).to_numpy()
    t_value = stats.t.ppf(0.975, n - p - 1)

    X        = np.c_[np.ones(n), df.select(predictors).to_numpy()] if p > 0 else np.ones((n, 1))
    beta_hat = np.linalg.solve(X.T @ X, X.T @ Y)
    y_hat    = X @ beta_hat
    residuals = Y - y_hat
    rss       = (residuals.T @ residuals).item()
    sigma2_hat = rss / (n - p - 1)
    se_beta   = np.sqrt(np.diag(sigma2_hat * np.linalg.inv(X.T @ X)))

    summary = (
        pl.DataFrame({
            "component":   ["Intercept"] + predictors,
            "Coefficient": beta_hat.flatten(),
            "Std.Error":   se_beta,
        })
        .with_columns((pl.col("Coefficient") / pl.col("Std.Error")).alias("t-stat"))
        .with_columns(
            pl.when(pl.col("t-stat").abs() < t_value)
            .then(pl.lit("Accept H0")).otherwise(pl.lit("Reject H0"))
            .alias("Decision")
        )
        .with_columns(
            (pl.col("Coefficient") - t_value * pl.col("Std.Error")).alias("CI lower"),
            (pl.col("Coefficient") + t_value * pl.col("Std.Error")).alias("CI upper"),
        )
    )

    # Chart only makes sense for a single quantitative predictor with polynomial terms
    chart = None
    if polynomial_degree > 1 and len(quantitative) == 1 and not qualitative and not interaction:
        base  = quantitative[0]
        chart = _fit_curve(df, base, y_col, beta_hat,
                           lambda x: np.column_stack([x ** d for d in range(1, polynomial_degree + 1)]))

    return {
        "summary":      summary,
        "RSS":          rss,
        "sigma2_hat":   sigma2_hat,
        "beta_hat":     beta_hat,
        "y_hat":        y_hat,
        "predictors":   predictors,
        "quantitative": quantitative,
        "qualitative":  qualitative,
        "chart":        chart,
    }


# ── ridge regression ──────────────────────────────────────────────────────────

def ridge_regression_computation_base(df, quantitative, qualitative, y_col, lmbda=1.0, interaction=False, polynomial_degree=1):
    """
    Ridge regression for a single λ.

    Standardizes features before solving (so the penalty is scale-invariant),
    then maps coefficients back to the original scale. The intercept is never
    penalized (standard convention).

    Returns
    -------
    dict: "beta_hat" (original scale, shape (p+1,1)), "predictors", "RSS"
    """
    predictors = quantitative.copy()
    p0         = len(predictors)

    for col in qualitative:
        dummies    = df.select(pl.col(col)).to_dummies()
        dummy_cols = dummies.columns[1:]
        df         = df.hstack(dummies)
        predictors.extend(dummy_cols)

    if interaction:
        df         = df.with_columns((pl.col(predictors[0]) * pl.col(predictors[1])).alias("Product"))
        predictors = predictors + ["Product"]

    if polynomial_degree > 1:
        for i in range(p0):
            base = quantitative[i]
            for d in range(2, polynomial_degree + 1):
                new_col = f"{base}^{d}"
                df      = df.with_columns((pl.col(base) ** d).alias(new_col))
                predictors.append(new_col)

    n = df.height
    X = df.select(predictors).to_numpy()
    Y = df.select(y_col).to_numpy()

    X_mean            = X.mean(axis=0)
    X_std             = X.std(axis=0)
    X_std[X_std == 0] = 1.0          # guard against constant columns
    X_scaled          = np.c_[np.ones(n), (X - X_mean) / X_std]

    # Penalty matrix with intercept row/col zeroed (intercept not penalized)
    penalty       = np.eye(X_scaled.shape[1])
    penalty[0, 0] = 0.0
    beta_scaled   = np.linalg.solve(X_scaled.T @ X_scaled + lmbda * penalty, X_scaled.T @ Y)

    # Convert back to original scale
    b_slopes  = beta_scaled[1:].flatten()
    beta_hat  = np.insert(b_slopes / X_std, 0, beta_scaled[0, 0] - np.sum(b_slopes * X_mean / X_std)).reshape(-1, 1)

    X_orig = np.c_[np.ones(n), X]
    rss    = float(((Y - X_orig @ beta_hat) ** 2).sum())

    return {"beta_hat": beta_hat, "predictors": predictors, "RSS": rss}


def ridge_regression(df, quantitative, qualitative, y_col, interaction=False, polynomial_degree=1):
    """
    Selects the optimal λ for Ridge via 10-fold CV over a log-spaced grid [1e-4, 1e5],
    then re-fits on the full dataset at the best λ.

    Returns the same dict as ridge_regression_computation_base.
    """
    lambda_grid = np.logspace(-4, 5, num=100)
    cv_errors   = [k_fold_cv(df, quantitative, qualitative, y_col, k=10, lmbda=lmbda,
                                interaction=interaction, polynomial_degree=polynomial_degree)
                    for lmbda in lambda_grid]
    return ridge_regression_computation_base(df, quantitative, qualitative, y_col,
                                                lmbda=lambda_grid[np.argmin(cv_errors)],
                                                interaction=interaction, polynomial_degree=polynomial_degree)


# ── lasso regression ──────────────────────────────────────────────────────────

def lasso_regression_computation_base(df, quantitative, qualitative, y_col, lmbda=1.0, interaction=False, polynomial_degree=1):
    """
    Lasso regression for a single λ via statsmodels formula API + coordinate descent.

    Design choices worth knowing:
    - Formula API: patsy handles C() dummies, ':' interactions, and np.power()
        polynomials without manual column construction.
    - StandardScaler on quantitative columns: coordinate descent penalizes all
        coefficients equally, so unscaled features with different magnitudes get
        penalized unequally. Qualitative (0/1) columns are left as-is.
    - Lambda convention: statsmodels minimises 0.5·RSS/n + α·|β|₁ while ISL uses
        RSS + λ·|β|₁, so α = λ_ISL / n. lasso_regression() handles this; if calling
        this function directly you must divide λ by n first.

    Returns
    -------
    dict:
        "summary"    : pl.DataFrame — component names + coefficients
        "RSS"        : float
        "predictors" : list[str]   — patsy-style names (e.g. "C(Own)[T.Yes]")
        "beta_hat"   : np.ndarray
        "results"    : statsmodels result — used by k_fold_cv to predict on held-out folds
        "scaler"     : fitted StandardScaler — apply with .transform() (not .fit_transform())
    """
    predictors = quantitative.copy()
    p0         = len(predictors)

    for col in qualitative:
        predictors.append(f"C({col})")

    # ':' = product term only; '*' would re-add the main effects already in predictors
    if interaction and len(predictors) >= 2:
        predictors.append(f"{predictors[0]}:{predictors[1]}")

    # np.power() required: bare '**' in a patsy formula means "up-to-N-way interactions"
    if polynomial_degree > 1:
        for i in range(p0):
            base = quantitative[i]
            for d in range(2, polynomial_degree + 1):
                predictors.append(f"np.power({base}, {d})")

    formula   = f"{y_col} ~ {' + '.join(predictors)}" if predictors else f"{y_col} ~ 1"
    df_pandas = df.to_pandas()

    scaler = StandardScaler()
    if quantitative:
        df_pandas[quantitative] = scaler.fit_transform(df_pandas[quantitative])

    results      = sm.ols(formula, data=df_pandas).fit_regularized(alpha=lmbda, L1_wt=1.0)
    param_names  = results.params.index.tolist()
    coefficients = results.params.values
    y_hat        = results.predict(df_pandas)
    rss          = float(np.sum((df_pandas[y_col].values - y_hat) ** 2))

    return {
        "summary":    pl.DataFrame({"component": param_names, "Coefficient": coefficients}),
        "RSS":        rss,
        "predictors": param_names[1:],
        "beta_hat":   coefficients,
        "results":    results,
        "scaler":     scaler,
    }


def lasso_regression(df, quantitative, qualitative, y_col, interaction=False, polynomial_degree=1):
    """
    Selects the optimal λ for Lasso via 10-fold CV over a log-spaced grid,
    then re-fits on the full dataset at the best λ.

    The grid is divided by n to convert ISL-scale λ into statsmodels α
    (statsmodels minimises 0.5·RSS/n + α·|β|₁; ISL minimises RSS + λ·|β|₁).

    Returns the same dict as lasso_regression_computation_base.
    """
    n           = df.height
    lambda_grid = np.logspace(-4, 5, num=100) / n  # ISL λ → statsmodels α
    cv_errors   = [k_fold_cv(df, quantitative, qualitative, y_col, k=10, lmbda=lmbda,
                                method="lasso", interaction=interaction, polynomial_degree=polynomial_degree)
                    for lmbda in lambda_grid]
    return lasso_regression_computation_base(df, quantitative, qualitative, y_col,
                                                lmbda=lambda_grid[np.argmin(cv_errors)],
                                                interaction=interaction, polynomial_degree=polynomial_degree)


# ── model evaluation ──────────────────────────────────────────────────────────

def validation_set(df, quantitative, qualitative, y_col, interaction=False, polynomial_degree=1):
    """
    Validation-set approach: trains OLS on the first half of df, evaluates on the second.
    Applies identical dummy / interaction / polynomial engineering to the test half.

    Returns test-set MSE (float).
    """
    mid      = df.height // 2
    train_df = df.slice(0, mid)
    test_df  = df.slice(mid, df.height - mid)

    model      = multiple_regression(train_df, quantitative, qualitative, y_col, interaction, polynomial_degree)
    predictors = model["predictors"]

    for col in qualitative:
        dummies = test_df.select(pl.col(col)).to_dummies()
        for dummy_col in predictors:
            if dummy_col.startswith(col + "_") and dummy_col not in dummies.columns:
                dummies = dummies.with_columns(pl.lit(0).alias(dummy_col))
        test_df = test_df.hstack(dummies)

    if interaction and len(predictors) >= 2:
        test_df = test_df.with_columns((pl.col(predictors[0]) * pl.col(predictors[1])).alias("Product"))

    if polynomial_degree > 1:
        for base in quantitative:
            for d in range(2, polynomial_degree + 1):
                test_df = test_df.with_columns((pl.col(base) ** d).alias(f"{base}^{d}"))

    X         = np.c_[np.ones(test_df.height), test_df.select(predictors).to_numpy()] if predictors else np.ones((test_df.height, 1))
    Y         = test_df.select(y_col).to_numpy()
    residuals = Y - X @ model["beta_hat"]
    return ((residuals.T @ residuals) / test_df.height).item()


def k_fold_cv(df, quantitative, qualitative, y_col, k=5, interaction=False, polynomial_degree=1, lmbda=-1, method="ridge", seed=42):
    """
    k-Fold Cross-Validation. Supports OLS, Ridge, Lasso, and Smoothing Splines.

    Parameters
    ----------
    lmbda  : float — regularisation strength; ≤ 0 runs plain OLS
    method : str   — "ridge" | "lasso" | "smoothing_spline" (only relevant when lmbda > 0)
    seed   : int   — shuffle seed for reproducibility

    Returns mean CV-MSE across all k folds (float).
    """
    shuffled  = df.sample(fraction=1.0, shuffle=True, seed=seed)
    n         = shuffled.height
    fold_size = n // k
    mse_list  = []

    for i in range(k):
        start    = i * fold_size
        end      = (i + 1) * fold_size if i < k - 1 else n
        test_df  = shuffled.slice(start, end - start)
        train_df = pl.concat([shuffled.slice(0, start), shuffled.slice(end, n - end)])

        if lmbda > 0:
            if method == "ridge":
                model = ridge_regression_computation_base(train_df, quantitative, qualitative, y_col, lmbda, interaction, polynomial_degree)
            elif method == "smoothing_spline":
                model = smoothing_spline_computation_base(train_df, quantitative[0], y_col, lmbda)
            else:  # lasso
                model = lasso_regression_computation_base(train_df, quantitative, qualitative, y_col, lmbda, interaction, polynomial_degree)
                # Lasso uses patsy-style predictor names (e.g. "C(Own)[T.Yes]", "np.power(x,2)")
                # which aren't real Polars columns, so the generic select→matmul path below
                # won't work. Predict via statsmodels/patsy using the scaler from training.
                test_pd = test_df.to_pandas()
                if quantitative:
                    test_pd[quantitative] = model["scaler"].transform(test_pd[quantitative])
                y_hat = model["results"].predict(test_pd).to_numpy().reshape(-1, 1)
                Y     = test_df.select(y_col).to_numpy()
                mse_list.append(((Y - y_hat).T @ (Y - y_hat) / test_df.height).item())
                continue
        else:
            model = multiple_regression(train_df, quantitative, qualitative, y_col, interaction, polynomial_degree)

        predictors = model["predictors"]

        for col in qualitative:
            dummies = test_df.select(pl.col(col)).to_dummies()
            for dummy_col in predictors:
                if dummy_col.startswith(col + "_") and dummy_col not in dummies.columns:
                    dummies = dummies.with_columns(pl.lit(0).alias(dummy_col))
            test_df = test_df.hstack(dummies)

        if interaction and len(predictors) >= 2:
            test_df = test_df.with_columns((pl.col(predictors[0]) * pl.col(predictors[1])).alias("Product"))

        if polynomial_degree > 1:
            for base in quantitative:
                for d in range(2, polynomial_degree + 1):
                    test_df = test_df.with_columns((pl.col(base) ** d).alias(f"{base}^{d}"))

        X = np.c_[np.ones(test_df.height), test_df.select(predictors).to_numpy()] if predictors else np.ones((test_df.height, 1))
        Y = test_df.select(y_col).to_numpy()

        if method == "smoothing_spline":
            y_hat = model["spline_model"](test_df.select(quantitative[0]).to_numpy().flatten()).reshape(-1, 1)
        else:
            y_hat = X @ model["beta_hat"]

        residuals = Y - y_hat
        mse_list.append(((residuals.T @ residuals) / test_df.height).item())

    return float(np.mean(mse_list))


# ── subset and stepwise selection ────────────────────────────────────────────

def BIC(n, models):
    """Mallows' BIC: selects the model minimising (RSS + log(n)·(p+1)·σ²) / n."""
    return min(models, key=lambda m: (m["RSS"] + np.log(n) * (len(m["predictors"]) + 1) * m["sigma2_hat"]) / n)


def Cp(n, models):
    """Mallows' Cp: selects the model minimising (RSS + 2·(p+1)·σ²) / n."""
    return min(models, key=lambda m: (m["RSS"] + 2 * (len(m["predictors"]) + 1) * m["sigma2_hat"]) / n)


def _select_final(models, df, quantitative, qualitative, y_col, selection_method):
    """Apply the final selection criterion to a ranked list of candidate models."""
    if selection_method == "VS":
        return min(models, key=lambda bm: validation_set(df, *_split(bm["predictors"], quantitative, qualitative), y_col))
    if selection_method == "CV":
        return min(models, key=lambda bm: k_fold_cv(df, *_split(bm["predictors"], quantitative, qualitative), y_col))
    return globals()[selection_method](df.height, models)


def subset_selection(df, quantitative, qualitative, y_col, selection_method="BIC"):
    """
    Best-subset selection: fits all 2^p predictor combinations, keeps the best
    RSS model at each size, then applies the final criterion.

    selection_method : "BIC" | "Cp" | "VS" | "CV"

    ⚠ Exponential in p — only practical for small predictor sets.
    """
    all_preds = quantitative + qualitative
    y         = df.select(y_col).to_numpy().flatten()
    null_rss  = float(((y - y.mean()) ** 2).sum())
    models    = [{"predictors": (), "RSS": null_rss, "sigma2_hat": null_rss / (len(y) - 1)}]

    for size in range(1, len(all_preds) + 1):
        best_combo = min(
            combinations(all_preds, size),
            key=lambda combo: multiple_regression(df, *_split(combo, quantitative, qualitative), y_col)["RSS"]
        )
        rc = multiple_regression(df, *_split(best_combo, quantitative, qualitative), y_col)
        models.append({"predictors": best_combo, "RSS": rc["RSS"], "sigma2_hat": rc["sigma2_hat"]})

    return _select_final(models, df, quantitative, qualitative, y_col, selection_method)


def forward_stepwise(df, quantitative, qualitative, y_col, selection_method="BIC"):
    """
    Forward stepwise: starts from the null model and greedily adds the predictor
    that most reduces RSS at each step.

    selection_method : "BIC" | "Cp" | "VS" | "CV"
    """
    all_preds = quantitative + qualitative
    y         = df.select(y_col).to_numpy().flatten()
    null_rss  = float(((y - y.mean()) ** 2).sum())
    selected  = []
    remaining = all_preds.copy()
    models    = [{"predictors": (), "RSS": null_rss, "sigma2_hat": null_rss / (len(y) - 1)}]

    while remaining:
        best = min(remaining, key=lambda c: multiple_regression(df, *_split(selected + [c], quantitative, qualitative), y_col)["RSS"])
        selected.append(best)
        remaining.remove(best)
        rc = multiple_regression(df, *_split(selected, quantitative, qualitative), y_col)
        models.append({"predictors": tuple(selected), "RSS": rc["RSS"], "sigma2_hat": rc["sigma2_hat"]})

    return _select_final(models, df, quantitative, qualitative, y_col, selection_method)


def backward_stepwise(df, quantitative, qualitative, y_col, selection_method="BIC"):
    """
    Backward stepwise: starts from the full model and greedily removes the
    predictor whose removal most reduces RSS at each step.

    selection_method : "BIC" | "Cp" | "VS" | "CV"
    """
    selected = quantitative + qualitative
    rc       = multiple_regression(df, quantitative, qualitative, y_col)
    models   = [{"predictors": tuple(selected), "RSS": rc["RSS"], "sigma2_hat": rc["sigma2_hat"]}]

    while len(selected) > 1:
        worst = min(
            selected,
            key=lambda c: multiple_regression(df, *_split([p for p in selected if p != c], quantitative, qualitative), y_col)["RSS"]
        )
        selected.remove(worst)
        rc = multiple_regression(df, *_split(selected, quantitative, qualitative), y_col)
        models.append({"predictors": tuple(selected), "RSS": rc["RSS"], "sigma2_hat": rc["sigma2_hat"]})

    return _select_final(models, df, quantitative, qualitative, y_col, selection_method)


# ── splines and step functions ────────────────────────────────────────────────

def step_function_regression(df, predictor, y_col, breaks):
    """
    Piecewise-constant (step function) regression.

    Cuts `predictor` into len(breaks)+1 ordered bins and fits OLS with those
    bins as a qualitative predictor (first bin = reference category).

    Returns the same dict as multiple_regression (with "chart" added).
    """
    labels    = [f"C{i}" for i in range(len(breaks) + 1)]
    dummy_col = f"{predictor}_Group"
    df2       = df.with_columns(pl.col(predictor).cut(breaks=breaks, labels=labels).alias(dummy_col))
    res       = multiple_regression(df2, [], [dummy_col], y_col)

    # Build step curve: map each x to its bin intercept
    x_vals  = df.select(predictor).to_numpy().flatten()
    x_range = np.linspace(x_vals.min(), x_vals.max(), 500)
    b       = res["beta_hat"].flatten()
    # b[0] = intercept (bin C0), b[j] = offset for bin Cj
    y_curve = np.array([
        b[0] + sum(b[j] * (1 if np.searchsorted(breaks, x, side="right") == j else 0)
                    for j in range(1, len(b)))
        for x in x_range
    ])
    curve_df   = pl.DataFrame({predictor: x_range, y_col: y_curve})
    res["chart"] = df.plot.point(x=predictor, y=y_col) + curve_df.plot.line(x=predictor, y=y_col).mark_line(color="red")
    res["y_hat"] = res["y_hat"].flatten()
    return res


def linear_splines(df, predictor, y_col, knots):
    """
    Piecewise-linear (linear spline) regression with truncated basis functions.

    For each knot kⱼ, adds basis function max(x - kⱼ, 0), giving the fit one
    extra slope change at each knot while remaining continuous there.

    Returns the same dict as multiple_regression (with "chart" added).
    """
    spline_cols = []
    for idx, knot in enumerate(knots):
        col_name = f"lin_spline_k{idx}"
        df = df.with_columns(
            pl.when(pl.col(predictor) > knot).then(pl.col(predictor) - knot).otherwise(0.0).alias(col_name)
        )
        spline_cols.append(col_name)

    res = multiple_regression(df, [predictor] + spline_cols, [], y_col)
    res["chart"] = _fit_curve(df, predictor, y_col, res["beta_hat"],
                                lambda x: np.column_stack([x] + [np.maximum(x - k, 0) for k in knots]))
    res["y_hat"] = res["y_hat"].flatten()
    return res


def cubic_splines(df, predictor, y_col, knots):
    """
    Cubic spline regression with truncated power basis.

    Adds x², x³ as global terms, then for each knot kⱼ adds max(x − kⱼ, 0)³,
    giving the fit C² continuity (continuous up to the 2nd derivative) at each knot.

    Returns the same dict as multiple_regression (with "chart" added).
    """
    df = df.with_columns(
        (pl.col(predictor) ** 2).alias(f"{predictor}^2"),
        (pl.col(predictor) ** 3).alias(f"{predictor}^3"),
    )
    base_cols   = [predictor, f"{predictor}^2", f"{predictor}^3"]
    spline_cols = []

    for idx, knot in enumerate(knots):
        col_name = f"cub_spline_k{idx}"
        df = df.with_columns(
            pl.when(pl.col(predictor) > knot).then((pl.col(predictor) - knot) ** 3).otherwise(0.0).alias(col_name)
        )
        spline_cols.append(col_name)

    res = multiple_regression(df, base_cols + spline_cols, [], y_col)
    res["chart"] = _fit_curve(df, predictor, y_col, res["beta_hat"],
                                lambda x: np.column_stack([x, x**2, x**3] + [np.maximum(x - k, 0)**3 for k in knots]))
    res["y_hat"] = res["y_hat"].flatten()
    return res


# ── smoothing splines ─────────────────────────────────────────────────────────

def smoothing_spline_computation_base(df, predictor, y_col, lmbda=1.0):
    """
    Smoothing spline for a single λ via SciPy's make_smoothing_spline.

    Ties (duplicate x values) are resolved by averaging y values before fitting,
    since the solver requires strictly increasing x. RSS is then evaluated on
    the original (non-aggregated) data.

    Returns
    -------
    dict:
        "spline_model" : callable   — call spline_model(x) to predict at new x values
        "y_hat"        : np.ndarray — fitted values on the original (non-aggregated) data
        "RSS"          : float
        "summary"      : pl.DataFrame — RSS and λ
        "predictors"   : list[str]
        "chart"        : hvplot overlay — scatter + fitted curve
    """
    df_agg = df.group_by(predictor).agg(pl.col(y_col).mean()).sort(predictor)
    x_fit  = df_agg.select(predictor).to_numpy().flatten()
    y_fit  = df_agg.select(y_col).to_numpy().flatten()
    spline = make_smoothing_spline(x_fit, y_fit, lam=lmbda)

    x_orig    = df.select(predictor).to_numpy().flatten()
    y_orig    = df.select(y_col).to_numpy().flatten()
    y_hat     = spline(x_orig)
    rss       = float(np.sum((y_orig - y_hat) ** 2))

    sort_idx = np.argsort(x_orig)
    curve_df = pl.DataFrame({predictor: x_orig[sort_idx], y_col: y_hat[sort_idx]})
    chart    = df.plot.point(x=predictor, y=y_col) + curve_df.plot.line(x=predictor, y=y_col).mark_line(color="red")

    return {
        "spline_model": spline,
        "y_hat":        y_hat,
        "RSS":          rss,
        "summary":      pl.DataFrame({"RSS": [rss], "lambda": [lmbda]}),
        "predictors":   [predictor],
        "chart":        chart,
    }


def smoothing_spline(df, predictor, y_col, k=10):
    """
    Selects the optimal λ for a smoothing spline via k-fold CV over a log-spaced
    grid [1e-4, 1e5], then re-fits on the full dataset at the best λ.

    Returns the same dict as smoothing_spline_computation_base.
    """
    lambda_grid = np.logspace(-4, 5, num=100)
    cv_errors   = [k_fold_cv(df, [predictor], [], y_col, k=k, lmbda=lmbda, method="smoothing_spline")
                    for lmbda in lambda_grid]
    return smoothing_spline_computation_base(df, predictor, y_col, lmbda=lambda_grid[np.argmin(cv_errors)])


# ── generalised additive model ────────────────────────────────────────────────

def gam(df, model_specs, y_col):
    """
    Generalised Additive Model: y = b0 + f1(x1) + f2(x2) + … + ε

    Each component fⱼ is fitted independently and stored as column f(predictor).
    b0 is estimated as  ȳ − Σ mean(fⱼ)  — the GAM identifiability constraint
    that centres each component so predictions average to ȳ.

    Parameters
    ----------
    model_specs : list of (predictor, method, params) tuples
        predictor : str  — column name
        method    : str  — "regression" | "step_function_regression" |
                            "linear_splines" | "cubic_splines" |
                            "smoothing_spline_computation_base"
                            (any function in this module returning a dict with "y_hat")
        params    : dict — keys used per method:
        "qualitative"       bool  — for method="regression"
        "polynomial_degree" int   — for method="regression"
        "knots"             list  — for spline / step-function methods

    Returns
    -------
    dict:
        "df"      : pl.DataFrame   — original df + f(predictor) columns + "y_hat"
        "RSS"     : float
        "summary" : list[pl.DataFrame]  — one summary table per component
    """
    summaries = []

    for predictor, method, params in model_specs:
        f_col = f"f({predictor})"

        if method == "regression":
            degree  = params.get("polynomial_degree", 1)
            is_qual = params.get("qualitative", False)

            if degree == 1 and not is_qual:
                # Quantitative linear: use the closed-form Polars expression
                res = regression(df, predictor, y_col, qualitative=False)
                df  = df.with_columns((res["b0"] + res["b1"] * pl.col(predictor)).alias(f_col))
            else:
                # Qualitative or polynomial: feature engineering happens inside
                # multiple_regression; pull y_hat directly from the result dict
                quan = [] if is_qual else [predictor]
                qual = [predictor] if is_qual else []
                res  = multiple_regression(df, quan, qual, y_col, polynomial_degree=degree)
                df   = df.with_columns(pl.Series(f_col, np.asarray(res["y_hat"]).flatten()))
        else:
            fn  = globals()[method]
            res = fn(df, predictor, y_col, params.get("knots"))
            df  = df.with_columns(pl.Series(f_col, np.asarray(res["y_hat"]).flatten()))

        summaries.append(res["summary"])

    f_cols = [col for col in df.columns if col.startswith("f(")]
    b0     = float(df[y_col].mean()) - sum(float(df[c].mean()) for c in f_cols)
    df     = df.with_columns((b0 + pl.sum_horizontal(f_cols)).alias("y_hat"))

    rss = float(((df[y_col] - df["y_hat"]) ** 2).sum())
    return {"df": df, "RSS": rss, "summary": summaries}


# ── logistic regression ────────────────────────────────────────────────

def logistic_regression(df, x_cols, y_col, y_value="Yes"):
    """
    Does logistic regression
    Used when the response is qualitative
    Uses statsmodels' Logit method
    """
    X = df.select(x_cols).to_numpy()
    Y = df.with_columns(
        (pl.when(pl.col(y_col) == y_value).then(1).otherwise(0).alias(y_col))
    ).select(y_col).to_numpy().flatten()

    X = sm.add_constant(X)

    model = sm.Logit(Y, X).fit()

    return model.summary()
# ── entry point ───────────────────────────────────────────────────────────────

if __name__ == "__main__":
    df1 = pl.read_csv(r"stat_learning\datasets\Advertising.csv")
    df2 = pl.read_csv(r"stat_learning\datasets\Wage.csv")

    model_specs = [
        ("year",   "step_function_regression", {"knots": [2002, 2004, 2006, 2008]}),
        ("age",    "step_function_regression", {"knots": [30, 40, 50, 60]}),
        ("maritl", "regression",               {"qualitative": True}),
        ("race",   "regression",               {"qualitative": True}),
    ]
    res = gam(df2, model_specs, "wage")
    print(res["summary"])