df = df.drop_nulls()
stat, pvalue, lags, critical_values = kpss(
    df["diff2"].to_numpy(),
    regression="c",      # constant only
    nlags="auto"
)

print(f"KPSS Statistic: {stat:.4f}")
print(f"p-value: {pvalue:.4f}")
print(f"Lags Used: {lags}")
print("Critical Values:")
for key, value in critical_values.items():
    print(f"  {key}: {value}")
# 3) ========== Examine ACF an