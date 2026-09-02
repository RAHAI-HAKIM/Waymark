from ortools.linear_solver import pywraplp

# ==========================================================
# Create Solver
# ==========================================================

solver = pywraplp.Solver.CreateSolver("SCIP")

if solver is None:
    raise RuntimeError("GLOP solver is unavailable.")

print(f"Solver: {solver.SolverVersion()}")

# ==========================================================
# Decision Variables
# ==========================================================
infinity = solver.infinity()

# 2. Define Set Variables (Keys 1 to 20 to match features dict)
set_vars = {i: solver.BoolVar(f"set{i}") for i in range(1, 21)}

# 3. Define Feature Variables
tiles = ["T1", "T2", "T3", "T4"]
tile_vars = {t: solver.BoolVar(t) for t in tiles}

lights = ["L1", "L2", "L3", "L4"]
light_vars = {l: solver.BoolVar(l) for l in lights}

sinks = ["S1", "S2", "S3", "S4"]
sink_vars = {s: solver.BoolVar(s) for s in sinks}

walls = ["W1", "W2", "W3", "W4"]
wall_vars = {w: solver.BoolVar(w) for w in walls}

cabinets = ["C1", "C2", "C3", "C4"]
cabinet_vars = {c: solver.BoolVar(c) for c in cabinets}

counters = ["O1", "O2", "O3", "O4"]
counter_vars = {o: solver.BoolVar(o) for o in counters}

dishes = ["D1", "D2"]
dish_vars = {d: solver.BoolVar(d) for d in dishes}

ranges = ["R1", "R2", "R3", "R4"]
range_vars = {r: solver.BoolVar(r) for r in ranges}

# Map string feature names to actual BoolVar objects
var_map = {
    **tile_vars,
    **light_vars,
    **sink_vars,
    **wall_vars,
    **cabinet_vars,
    **counter_vars,
    **dish_vars,
    **range_vars,
}

# 4. Define Feature Sets
features = {
    1: ["T2", "W2", "L4", "C2", "O4", "S2", "D2", "R2"],
    2: ["T2", "W1", "L1", "C4", "O4", "S4", "D2", "R2"],
    3: ["T1", "W3", "L2", "C1", "O1", "S3", "D1", "R3"],
    4: ["T3", "W3", "L3", "C3", "O3", "S1", "D1", "R1"],
    5: ["T4", "W4", "L1", "C1", "O2", "S2", "D1", "R1"],
    6: ["T2", "W2", "L2", "C4", "O4", "S3", "D2", "R4"],
    7: ["T1", "W3", "L4", "C3", "O2", "S1", "D1", "R1"],
    8: ["T2", "W1", "L3", "C1", "O1", "S3", "D2", "R4"],
    9: ["T2", "W1", "L2", "C3", "O2", "S2", "D2", "R2"],
    10: ["T1", "W1", "L1", "C1", "O3", "S4", "D1", "R3"],
    11: ["T3", "W1", "L3", "C3", "O1", "S1", "D1", "R3"],
    12: ["T2", "W2", "L1", "C2", "O2", "S4", "D2", "R2"],
    13: ["T4", "W4", "L3", "C3", "O1", "S2", "D1", "R3"],
    14: ["T4", "W4", "L4", "C1", "O3", "S1", "R1"],
    15: ["T3", "W3", "L1", "C1", "O1", "S3", "R3"],
    16: ["T3", "W3", "L4", "C1", "O3", "S2", "R1"],
    17: ["T1", "W4", "L2", "C3", "O3", "S4", "R3"],
    18: ["T2", "W3", "L3", "C2", "O4", "S1", "R2"],
    19: ["T2", "W4", "L4", "C4", "O4", "S2", "R4"],
    20: ["T2", "W3", "L1", "C1", "O2", "S3", "R2"],
    }

# ==========================================================
# Category Constraints
# ==========================================================

c1 = solver.Constraint(-infinity, 2, "c1")
for tile in tiles:
    c1.SetCoefficient(tile_vars[tile], 1)

c2 = solver.Constraint(-infinity, 2, "c2")
for light in lights:
    c2.SetCoefficient(light_vars[light], 1)

c3 = solver.Constraint(-infinity, 2, "c3")
for wall in walls:
    c3.SetCoefficient(wall_vars[wall], 1)

c4 = solver.Constraint(-infinity, 2, "c4")
for cabinet in cabinets:
    c4.SetCoefficient(cabinet_vars[cabinet], 1)

c5 = solver.Constraint(-infinity, 3, "c5")
for counter in counters:
    c5.SetCoefficient(counter_vars[counter], 1)

c6 = solver.Constraint(-infinity, 2, "c6")
for sink in sinks:
    c6.SetCoefficient(sink_vars[sink], 1)

c7 = solver.Constraint(-infinity, 2, "c7")
for r in ranges:
    c7.SetCoefficient(range_vars[r], 1)
for d in dishes:
    c7.SetCoefficient(dish_vars[d], 1)

# ==========================================================
# Set Logic Constraints
# ==========================================================

for i in range(1, 21):
    feat_list = features[i]

    # Upper bound logic: set_vars[i] can only be 1 if ALL features in set i are selected
    solver.Add(
        sum(var_map[f] for f in feat_list) - set_vars[i] <= len(feat_list) - 1
    )

    # Lower bound logic: set_vars[i] = 1 implies every feature in set i is 1
    for f in feat_list:
        solver.Add(var_map[f] - set_vars[i] >= 0)

# ==========================================================
# Objective Function & Solve
# ==========================================================

solver.Maximize(sum(set_vars.values()))

status = solver.Solve()

# ==========================================================
# Solution
# ==========================================================

if status == pywraplp.Solver.OPTIMAL:

    print("\n========== Optimal Solution ==========")

    print(f"Optimal objective value = {solver.Objective().Value():.2f}\n")

    print("Decision Variables")
    for var in solver.variables():
        print(
            f"{var.name():3} = {var.solution_value():6.2f}"
            #f"    Reduced Cost = {var.ReducedCost():7.2f}"
        )

    print("\nConstraints")
    for c in solver.constraints():

        activity = sum(
            c.GetCoefficient(v) * v.solution_value()
            for v in solver.variables()
        )

        print(
            f"{c.name():10}"
            f" Activity = {activity:6.2f}"
            f" RHS = {c.ub():6.2f}"
            #f" Shadow Price = {c.DualValue():7.2f}"
        )

    print("\nStatistics")
    print(f"Variables   : {solver.NumVariables()}")
    print(f"Constraints : {solver.NumConstraints()}")
    print(f"Iterations  : {solver.iterations()}")
    print(f"Wall Time   : {solver.wall_time()} ms")

else:
    print("No optimal solution found.")