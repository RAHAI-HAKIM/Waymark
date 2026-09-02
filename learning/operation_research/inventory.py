import polars as pl
import altair as alt
import math

df = pl.DataFrame({"month": ["June", "July", "August", "September", "October", "November", "December","January", "February", "March", "April", "May", "June"],
                    "demand": [25, 31, 18, 22, 40, 19, 38, 21, 25, 36, 34, 28, 27]})

#print(df.select("demand").std(1).item())

