import polars as pl
import polars.selectors as cs
import matplotlib.pyplot as plt
import duckdb

from pathlib import Path
import sys

# 1. Get the absolute path to the directory containing your target file
# Example target folder: "/Users/username/projects/my_tools"
target_directory = str(Path("C:/Users/HAKIM/Desktop/Retail_Project/QRetail/stat_learning").resolve())

# 2. Inject that folder path into Python's search system
sys.path.append(target_directory)

# 3. Import your target module directly by its file name (without .py)
import stat_learning as stat
#import stat_learning.stat_learning as stat 




pl.Config.set_tbl_cols(-1)
pl.Config.set_tbl_rows(-1)



conn = duckdb.connect("M5\M5.db")


df = conn.execute("SELECT c.event_name_1, c.event_name_2, AVG(s.units_sold) AS units_sold " \
"FROM SALES s JOIN CALENDAR c ON c.day = s.day " \
"GROUP BY CUBE(c.event_name_1, c.event_name_2)").pl()

df = df.fill_null("no_event")


#print(df)

res = stat.regression(df, "event_name_2", "units_sold", qualitative=True)

print(res["summary"])