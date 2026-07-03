import polars as pl
import polars.selectors as cs

import matplotlib.pyplot as plt
import duckdb




pl.Config.set_tbl_cols(-1)
pl.Config.set_tbl_rows(-1)

# eager api
#df = pl.read_csv("sales_data.csv", try_parse_dates=True)

#df_small = df.filter(pl.col("Price") > 100)

#df_agg = df_small.group_by("Category").agg(pl.col("Price").mean())


#print(df_agg)


# lazy api
"""

In eager API, Every step is executed immediately returning the intermediate results. 

This can be very wasteful as we might do work or load extra data that is not being used. 

If we instead used the lazy API and waited on execution until all the steps are defined then the query planner could perform various optimizations. 


In this case:

Predicate pushdown: Apply filters as early as possible while reading the dataset, thus only reading rows with sepal length greater than 5.

Projection pushdown: Select only the columns that are needed while reading the dataset, thus removing the need to load additional columns (e.g., petal length and petal width).

q = (

    pl.scan_csv("sales_data.csv", try_parse_dates=True)

    .filter(pl.col("Price") > 100)

    .group_by(pl.col("Category"))

    .agg(pl.col("Price").var())
)


df = q.collect()
print(df)

"""

"""

Most important methods:

df = pl.read_csv("sales_data.csv", try_parse_dates=True)

print(df.approx_n_unique())

print(df.bottom_k(7), by)    X

print(df.head(7))
df.clone()
df.corr()    X

df.describe()    X
df.drop("col name")
df.filter("condition")  X
df.gather("row_num")    X

df.gather_every("row_num")    X
df.get_column("col name")     X
df.get_columns()   X

df.group_by("operation, ie col name")

print(df.item(6,7))
df.iter_columns()
df.iter_rows()

df.join("other df")

df.join_where("other df")
df.map_columns()

df.max()
df.mean()
df.median()
df.min()

df.partition_by("col name")
df.pipe()
df.product()

df.quantile()

df.remove("condition")

df.replace_column("index", "series")

df.row("index")

df.rows_by_key()

df.sample("n", with_replacement=True)
df.select("col")

df.slice("start", "length")
df.sort("col")

df.sql("query")
df.sum()

df.std(ddof=0)
df.to_dict()
df.to_dicts()
df.to_pandas()

df.to_series("index")

df.top_k("k", "col")

df.unique()

df.var()

df.with_columns(pl.col("Price" ** 2).alias("Price^2"))

df.with_row_index()

df.write_csv()

df.write_database()

df.write_excel()

df.write_json("file")  X
"""

"""

Most important attributes:

df.columns                    X
df.flags

df.height                     X

df.shape                      X

df.style

df.width                      X
"""

"""
long_df = result.unpivot(
    index="Store ID",
    on=["Price", "Competitor Pricing"],
    variable_name="Pricing",
    value_name="mean"
)
chart = long_df.plot.point(x="Store ID", y="mean", color="Pricing").properties(width=400, height=400)
chart.save("line.html")
"""
#corr_matrix = df.select(["Units Sold", "Units Ordered"]).corr(label="Units correlation Sold VS Ordered")

#print(df.group_by_dynamic("Date", every="1w", include_boundaries=True).agg(pl.col("Demand").sum().alias("Week's Demand")))

#expr = (pl.col("Inventory Level") - pl.col("Demand")).alias("Expctd inventory lvl")
#s = pl.Series("index", range(df.height))
#df.insert_column(0, s)


#chart.save("scatter.html")

#df2 = df.group_by_dynamic("Date", every="1y").agg(pl.col("Income").sum())



#result = df.insert_column(18, (pl.col("Price") - pl.col("Competitor Pricing")).alias("Margin")).group_by("Product ID").agg(pl.sum("Demand"), pl.sum("Margin"))


#chart = result.plot.line(x="Margin", y="Demand")

conn = duckdb.connect("M5.db")

print(conn.execute("PRAGMA database_size;").fetchall())


